using Godot;

namespace Lunhui.Data;

/// <summary>
/// 一张配置表的容器资源（C# 版，对位原 `src/data/TableResource.cs`），由 `tools/build_tables.tscn`（代码在 `tools/BuildTables.cs`）
/// 生成到 `data/generated/&lt;table&gt;.tres`。
///
/// 运行期只读它，不读 CSV：CSV 是设计师的唯一数值源，资源是代码的唯一读取入口。
///
/// **成员名必须是 snake_case**：`.tres` 里存的键逐字就是
/// `table_name`／`primary_keys`／`rows`／`index`（Godot 的 C# 序列化按成员名原样写），
/// 改成 PascalCase 会让全部生成物读不出来。与 `src/data/TableRow.cs` 是同一条口径。
/// </summary>
public partial class TableResource : Resource
{
    /// <summary>表名，与 `data/tables/&lt;table&gt;.csv` 同名。</summary>
    [Export]
    public string table_name { get; set; } = "";

    /// <summary>该表的主键列名（复合主键按顺序排列），用于构建 id。</summary>
    [Export]
    public string[] primary_keys { get; set; } = System.Array.Empty<string>();

    /// <summary>行数据。元素是 `TableRegistry.ROW_SCRIPTS` 指定的行类实例。</summary>
    [Export]
    public Godot.Collections.Array rows { get; set; } = new();

    /// <summary>主键 → rows 下标。</summary>
    [Export]
    public Godot.Collections.Dictionary index { get; set; } = new();

    /// <summary>按主键取行，取不到返回 null。</summary>
    public Resource get_row(Variant row_id)
    {
        string key = row_id.AsString();
        int position = index.ContainsKey(key) ? index[key].AsInt32() : -1;
        if (position < 0 || position >= rows.Count)
        {
            return null;
        }

        return rows[position].As<Resource>();
    }

    /// <summary>全部主键，顺序与 rows 一致。</summary>
    public string[] ids()
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row in rows)
        {
            out_ids.Add(row.As<Resource>().Get("id").AsString());
        }

        return out_ids.ToArray();
    }

    /// <summary>
    /// 往表里塞一行**运行期**的行（不进 CSV、不进 data/generated）。
    ///
    /// 目前只有一处用途：创建角色的「不使用模板」路线——玩家自己分的七维不在任何 CSV 里，
    /// 得有一行 `character_base` 供 `get_row()` 查（装备、武学、面板全按 char_id 取模板）。
    /// 这一行随存档走（`GameState.custom_templates`），读档时重新注入。
    /// 主键重复时覆盖旧行（读档重复注入要幂等）。
    /// </summary>
    public void inject_row(Resource row)
    {
        string row_id = row.Get("id").AsString();
        int existing = index.ContainsKey(row_id) ? index[row_id].AsInt32() : -1;
        if (existing >= 0 && existing < rows.Count)
        {
            rows[existing] = row;
            return;
        }

        rows.Add(row);
        index[row_id] = rows.Count - 1;
    }

    /// <summary>按某列的值收集行（用于 drop_group 这类非主键分组）。</summary>
    public Godot.Collections.Array rows_where(string column, Variant value)
    {
        bool key_is_nil = value.VariantType == Variant.Type.Nil;
        // **别用 `Variant.Equals`**：C# 侧它是按内部结构比的——两个内容相同的 String 会判不等
        // （2026-10-10 实测：`rows_where("enemy_passive", "enemy_id", "en_bd_boss")` 返回空集，
        // 而手工 `str(row.get(...)) == id` 数得到 3 行，害得「敌人内功超编」那道校验整段空跑）。
        // 借字典的键比较才是引擎里 `Variant == Variant` 的语义（＝ GDScript 的 `row.get(c) == value`）。
        var probe = new Godot.Collections.Dictionary();
        if (!key_is_nil)
        {
            probe[value] = true;
        }

        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in rows)
        {
            Variant cell = entry.As<Resource>().Get(column);
            if (key_is_nil ? cell.VariantType == Variant.Type.Nil : probe.ContainsKey(cell))
            {
                out_rows.Add(entry);
            }
        }

        return out_rows;
    }
}
