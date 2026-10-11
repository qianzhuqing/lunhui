using Godot;
using Lunhui.Data;

namespace Lunhui.Core;

/// <summary>
/// 运行期表库（C# 版，对位原 `src/core/TableDb.cs`）：只读 `data/generated/*.tres`，绝不读 CSV。
///
/// CSV 是设计师的唯一数值源，构建期由 `tools/build_tables.tscn`（代码在 `tools/BuildTables.cs`）转成资源，
/// 运行期所有系统都通过 TableDb 取数，方便校验与替换。
///
/// **两个成员名必须是 snake_case**（`tables`／`errors`），因为**还没搬走的 GDScript**
/// 里到处是 `db.tables.is_empty()`／`db.errors.size()`：Godot 的 C# 脚本按成员名暴露属性，
/// 改名要同时扫全仓调用点。
/// </summary>
public partial class TableDb : RefCounted
{
    /// <summary>生成资源的默认目录。</summary>
    public const string DEFAULT_DIRECTORY = "res://data/generated";

    private readonly System.Collections.Generic.List<string> _errors = new();

    /// <summary>表名 → TableResource。</summary>
    [Export]
    public Godot.Collections.Dictionary tables { get; set; } = new();

    /// <summary>加载错误，空数组表示全部加载成功。</summary>
    [Export]
    public string[] errors
    {
        get => _errors.ToArray();
        set
        {
            _errors.Clear();
            if (value != null)
            {
                _errors.AddRange(value);
            }
        }
    }

    /// <summary>加载全部生成资源（默认目录），返回是否全部成功。</summary>
    public bool load_all() => LoadAll(DEFAULT_DIRECTORY);

    /// <summary>
    /// 显式指定生成目录的那一版。
    /// **为什么另起一个名字**：C# 的默认参数值对 GDScript 不生效（少传实参报
    /// `Invalid call. Nonexistent function`），而这里又不适合重载（Godot 的 C# 脚本注册按名字索引）。
    /// </summary>
    public bool load_all_from(string dir_path) => LoadAll(dir_path);

    private bool LoadAll(string dir_path)
    {
        tables.Clear();
        _errors.Clear();
        foreach (string table_name in TableRegistry.TABLES)
        {
            string path = $"{dir_path}/{table_name}.tres";
            if (!ResourceLoader.Exists(path))
            {
                _errors.Add($"缺少生成资源 {path}，请先运行 tools/build_tables.tscn 生成配置表");
                continue;
            }

            TableResource resource = GD.Load<TableResource>(path);
            if (resource == null || resource.rows == null)
            {
                _errors.Add($"生成资源结构不对：{path}");
                continue;
            }

            tables[table_name] = resource;
        }

        _warn_if_generated_is_stale(dir_path);
        return _errors.Count == 0;
    }

    /// <summary>
    /// 数据新鲜度：CSV 比生成的 `.tres` 新，就说明「改完表没重建，现在读的是旧数据」。
    ///
    /// 由来（2026-10-03）：观测工具的脚本**不重建配置表**，直接读 `data/generated`——
    /// 改完 CSV 跑它，量到的是上一版数据，而它照样打印一张像模像样的表（见框架说明决策 167）。
    /// 那次的错误结论被写进了两份文档和给策划的问题里，所以这里加一道**加载时的提醒**：
    /// 只 `push_warning` 不报错（导出包里没有 CSV，正常跑也不该被打断；时间戳读不到就不猜）。
    /// </summary>
    private void _warn_if_generated_is_stale(string dir_path)
    {
        string csv_dir = TableRegistry.TABLES_DIR;
        if (!DirAccess.DirExistsAbsolute(csv_dir))
        {
            return; // 导出包：CSV 不在包里，跳过
        }

        ulong newest_csv = 0;
        foreach (string file_name in DirAccess.GetFilesAt(csv_dir))
        {
            if (!file_name.EndsWith(".csv"))
            {
                continue;
            }

            newest_csv = System.Math.Max(newest_csv, FileAccess.GetModifiedTime(csv_dir.PathJoin(file_name)));
        }

        if (newest_csv == 0)
        {
            return;
        }

        ulong oldest_generated = 0;
        foreach (string table_name in TableRegistry.TABLES)
        {
            string path = $"{dir_path}/{table_name}.tres";
            if (!ResourceLoader.Exists(path))
            {
                continue;
            }

            ulong stamp = FileAccess.GetModifiedTime(path);
            if (stamp == 0)
            {
                return; // 时间戳不可靠（某些平台／打包），不猜
            }

            oldest_generated = oldest_generated == 0 ? stamp : System.Math.Min(oldest_generated, stamp);
        }

        if (oldest_generated > 0 && newest_csv > oldest_generated)
        {
            GD.PushWarning(
                "[TableDb] data/generated 里有比 CSV 更旧的文件：你改完表没重建，现在读的是**旧数据**——"
                + "先跑 tools/build_tables.tscn（或 run_tests.bat／run_all_checks.bat）"
            );
        }
    }

    public bool has_table(string table_name) => tables.ContainsKey(table_name);

    public Godot.Collections.Array rows(string table_name)
    {
        TableResource resource = TableOf(table_name);
        return resource?.rows ?? new Godot.Collections.Array();
    }

    /// <summary>按主键取行，取不到返回 null。</summary>
    public Resource get_row(string table_name, Variant row_id)
    {
        return TableOf(table_name)?.get_row(row_id);
    }

    /// <summary>按主键取行，取不到时报错并返回 null（用在「配置缺失就该立刻炸」的地方）。</summary>
    public Resource require_row(string table_name, Variant row_id)
    {
        Resource row = get_row(table_name, row_id);
        if (row == null)
        {
            GD.PushError($"[TableDb] 找不到配置：{table_name}[{row_id}]");
        }

        return row;
    }

    /// <summary>按某列的值筛行（用于 drop_group 这类非主键分组）。</summary>
    public Godot.Collections.Array rows_where(string table_name, string column, Variant value)
    {
        TableResource resource = TableOf(table_name);
        return resource?.rows_where(column, value) ?? new Godot.Collections.Array();
    }

    /// <summary>
    /// 往表里注入一行运行期的行（见 `TableResource.inject_row` 的说明）。
    /// 只影响内存：CSV 与 `data/generated` 一个字都不动。
    ///
    /// **注入前先把这张表复制一份**：`load()` 拿到的是 Godot 资源缓存里的**同一份对象**，
    /// 直接改它就是改**所有** `TableDb` 实例看到的东西。2026-10-10 实测的后果：
    /// 创建角色（`CreationService.build` 的「改过名字」那条路）注入一行自建模板后，
    /// 同一进程里后跑的角色面板用例读到的模板已经是玩家的真名（「家道失落的书生」→「陆文昭」）、
    /// 行数也多出一行——**跨用例污染**，症状看着像属性算错，其实与属性管线无关。
    /// 复制一份之后只影响本实例；游戏里只有一个 `TableDb`（`GameData.db`），行为不变。
    /// </summary>
    public void inject_row(string table_name, Resource row)
    {
        TableResource resource = TableOf(table_name);
        if (resource == null)
        {
            GD.PushError($"[TableDb] 没有这张表，注入失败：{table_name}");
            return;
        }

        var own_copy = (TableResource)resource.Duplicate(true);
        own_copy.inject_row(row);
        tables[table_name] = own_copy;
    }

    /// <summary>某列出现过的全部取值（用于引用校验）。</summary>
    public Godot.Collections.Dictionary column_values(string table_name, string column)
    {
        var out_values = new Godot.Collections.Dictionary();
        foreach (Variant entry in rows(table_name))
        {
            out_values[entry.As<Resource>().Get(column).AsString()] = true;
        }

        return out_values;
    }

    public bool has_value(string table_name, string column, Variant value)
    {
        bool key_is_nil = value.VariantType == Variant.Type.Nil;
        var probe = new Godot.Collections.Dictionary();
        if (!key_is_nil)
        {
            probe[value] = true;
        }

        foreach (Variant entry in rows(table_name))
        {
            Variant cell = entry.As<Resource>().Get(column);
            if (key_is_nil ? cell.VariantType == Variant.Type.Nil : probe.ContainsKey(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>表名 → 行数，用于引导场景打印摘要。</summary>
    public Godot.Collections.Dictionary summary()
    {
        var out_summary = new Godot.Collections.Dictionary();
        foreach (string table_name in TableRegistry.TABLES)
        {
            out_summary[table_name] = rows(table_name).Count;
        }

        return out_summary;
    }

    /// <summary>
    /// 显示名兜底：给玩家看的文案一律走这里，**不要把表里的英文 id 甩到界面上**。
    ///
    /// 起因（2026-10-03 换对象审计）：文案里查出成片的 id 泄露——「打开宝箱：item_health_pill ×2、
    /// eq_sword_01#1」「需要 item_pickaxe」「要用 poison 击杀目标」「挖通！直接通到「hf3_dungeon」」。
    /// 根源不是某处写错，而是每个界面各自拿 id 当名字用；所以把「id → 中文名」收在一处。
    ///
    /// `row_id` 可以是装备实例 id（`基础id#序号`），会自动剥掉 `#序号`。
    /// 查不到就原样返回 id——查不到说明数据或调用写错了，**不要在这里编个假名字**。
    /// </summary>
    private static readonly string[][] DISPLAY_NAME_SOURCES =
    {
        new[] { "item_base", "name_cn" },
        new[] { "equip_base", "name_cn" },
        new[] { "skill_base", "name_cn" },
        new[] { "enemy_base", "name_cn" },
        new[] { "character_base", "name_cn" },
        new[] { "building_def", "name_cn" },
        new[] { "hidden_trigger", "name_cn" },
        new[] { "map_region", "name_cn" },
        new[] { "map_local", "name_cn" },
        new[] { "dungeon_room", "room_name" },
        new[] { "status_effect", "name_cn" },
        new[] { "damage_type", "name_cn" },
        new[] { "rarity_def", "name_cn" },
        new[] { "difficulty_config", "name_cn" },
        new[] { "attribute_def", "name_cn" },
        new[] { "weapon_type_def", "name_cn" },
        new[] { "equip_slot_def", "name_cn" },
    };

    public string display_name(string row_id)
    {
        if (string.IsNullOrEmpty(row_id))
        {
            return "";
        }

        // 对位 GDScript 的 `row_id.split("#", false)[0]`：去掉空段，`eq_sword_01#1` → `eq_sword_01`。
        string[] parts = row_id.Split('#', System.StringSplitOptions.RemoveEmptyEntries);
        string bare = parts.Length > 0 ? parts[0] : row_id;
        foreach (string[] source in DISPLAY_NAME_SOURCES)
        {
            Resource row = get_row(source[0], bare);
            if (row != null)
            {
                return row.Get(source[1]).AsString();
            }
        }

        return row_id;
    }

    private TableResource TableOf(string table_name)
    {
        if (!tables.ContainsKey(table_name))
        {
            return null;
        }

        return tables[table_name].As<TableResource>();
    }
}
