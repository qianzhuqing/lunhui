using Godot;
using Lunhui.Autoload;
using Lunhui.Core;
using Lunhui.Ui;

namespace Lunhui;

/// <summary>
/// 引导场景（C# 版，对位原 `src/bootstrap.gd`，167 行）：加载配置表 → 交叉校验 → 打印摘要。
///
/// 它不做玩法，只是让「F5 就能看到框架是否健康」这件事成立，
/// 后续所有场景都接在它后面（见 docs/dev/框架说明.md 的扩展点）。
///
/// **跨语言面**：2026-10-11 起 `battle_actor` 也搬成 C#，这里**直接静态调用**它的两个工厂
/// （`BattleActor.from_character(...)`，实参写全——默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
/// 表库仍然**不读 `GameData` 的成员**，而是 `Get("db")` 拿——autoload 的成员名就是跨语言接口。
/// </summary>
public partial class Bootstrap : Node
{
    public const string TITLE = "《轮回》基础框架 · 引导场景";

    public override void _Ready()
    {
        GD.Print($"=== {TITLE} ===");
        bool selftest = _has_user_arg("--bootstrap-selftest");
        TableDb db = _resolve_db();
        if (db == null)
        {
            GD.PushError("[Bootstrap] 配置表未生成，先运行 tools/build_tables.tscn");
            if (selftest)
            {
                _finish_selftest(false, new System.Collections.Generic.List<string> { "配置表没加载起来" });
            }

            return;
        }

        _print_tables(db);
        _print_characters(db);
        string[] errors = _check(db);
        if (selftest)
        {
            _run_bootstrap_selftest(db, errors);
        }
        else
        {
            GD.Print("=== 引导结束 ===");
        }
    }

    /// <summary>
    /// 优先复用 `GameData` 单例，避免重复加载；拿不到就自己加载一份。
    ///
    /// **不要写 `get_node("/root/GameData")`**（`AGENTS.md` 的硬规矩）：`--script` 模式下 autoload
    /// 还没进活动场景树，绝对路径取不到。所以按 `main_menu` 的写法取：
    /// `Engine.get_main_loop() as SceneTree` 再相对查找。
    /// </summary>
    private TableDb _resolve_db()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Node game_data = tree?.Root.GetNodeOrNull("GameData");
        if (game_data != null)
        {
            TableDb shared = game_data.Get("db").AsGodotObject() as TableDb;
            if (shared != null && shared.tables.Count > 0)
            {
                return shared;
            }
        }

        var db = new TableDb();
        db.load_all();
        return db;
    }

    private void _print_tables(TableDb db)
    {
        Godot.Collections.Dictionary counts = db.summary();
        GD.Print($"[表] 共 {counts.Count} 张：");
        // `Dictionary.Keys` 是 `ICollection<Variant>`（不是 Godot 的 Array），顺序就是插入顺序
        foreach (Variant key in counts.Keys)
        {
            string table_name = key.AsString();
            GD.Print($"  - {table_name,-22} {counts[table_name].AsInt32(),4} 行");
        }
    }

    private void _print_characters(TableDb db)
    {
        AttributeCalculator calculator = AttributeCalculator.create(db);
        GD.Print("[角色] 可操控角色：");
        Godot.Collections.Array templates = db.rows("character_base");
        for (int i = 0; i < templates.Count; i++)
        {
            Resource row = templates[i].AsGodotObject() as Resource;
            BattleActor actor = BattleActor.from_character(
                db, row, 0, new Godot.Collections.Dictionary(), new Godot.Collections.Array());
            string skills = string.Join(", ", actor.skills);
            GD.Print(
                $"  - {actor.display_name}（{row.Get("role_tag").AsString()}）"
                + $"Lv{actor.level} 气血 {actor.max_hp()}"
                + $" 内力 {actor.max_qi()} 外功 {(int)actor.stat("atk_phys")}"
                + $" 内功 {(int)actor.stat("atk_qi")}"
                + $" 身法 {(int)actor.stat("speed")} 招式 {skills}");
            Godot.Collections.Dictionary attrs = calculator.attr_totals_of(
                row.Call("initial_attrs").AsGodotDictionary(),
                new Godot.Collections.Dictionary(),
                new Godot.Collections.Array());
            GD.Print(
                $"     五维 力{_attr(attrs, "str")} 体{_attr(attrs, "con")} 敏{_attr(attrs, "agi")}"
                + $" 智{_attr(attrs, "int")} 运{_attr(attrs, "luk")}");
        }

        GD.Print($"[敌人] 小队 {db.rows("enemy_team").Count} 支，明雷刷新点 {db.rows("roaming_spawn").Count} 个");
        GD.Print(
            $"[掉落] 掉落组 {db.column_values("drop_table", "drop_group").Count} 个，"
            + $"难度 {string.Join("、", _column_names(db, "difficulty_config", "difficulty_id"))}");
    }

    private string[] _check(TableDb db)
    {
        string[] errors = TableValidator.validate(db);
        if (errors.Length == 0)
        {
            GD.Print("[校验] 通过：主键、枚举、引用、数值区间全部合法");
            return errors;
        }

        GD.PushError($"[校验] 发现 {errors.Length} 条问题：");
        foreach (string message in errors)
        {
            GD.PushError("  - " + message);
        }

        return errors;
    }

    private bool _has_user_arg(string flag)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == flag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 真实场景自检（`-- --bootstrap-selftest`）：把"配置表诊断场景"也变成验收里的一步。
    ///
    /// 为什么值得单独跑：`TableDb.summary()` 与「模板 → 战斗单位 → 五维合计」这条链
    /// 只有这个场景在用（`from_character` 别处也用，但 `summary()` 只此一家）。
    /// 所以按其余场景的同一套口径补上：走一遍真路径 → 打印可核对的证据行 →
    /// 报 `BOOTSTRAP SELF-TEST` → 退出码。
    /// </summary>
    private void _run_bootstrap_selftest(TableDb db, string[] errors)
    {
        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();

        Godot.Collections.Dictionary counts = db.summary();
        int populated = 0;
        foreach (Variant key in counts.Keys)
        {
            if (counts[key.AsString()].AsInt32() > 0)
            {
                populated += 1;
            }
        }

        bool summary_ok = counts.Count > 20 && populated == counts.Count;
        ok = ok && summary_ok;
        lines.Add($"摘要：{counts.Count} 张表、{populated} 张有行={_flag(summary_ok)}");

        Godot.Collections.Array templates = db.rows("character_base");
        bool actors_ok = templates.Count > 0;
        for (int i = 0; i < templates.Count; i++)
        {
            Resource row = templates[i].AsGodotObject() as Resource;
            BattleActor actor = BattleActor.from_character(
                db, row, 0, new Godot.Collections.Dictionary(), new Godot.Collections.Array());
            if (actor == null
                || actor.display_name.Length == 0
                || actor.max_hp() <= 0
                || actor.skills.Length == 0)
            {
                actors_ok = false;
            }
        }

        ok = ok && actors_ok;
        lines.Add($"角色模板 {templates.Count} 条都能建出战斗单位（名字／气血／招式齐全）={_flag(actors_ok)}");

        AttributeCalculator calculator = AttributeCalculator.create(db);
        bool attrs_ok = true;
        string[] attr_keys = { "str", "con", "agi", "int", "luk" };
        for (int i = 0; i < templates.Count; i++)
        {
            Godot.Collections.Dictionary attrs = calculator.attr_totals_of(
                templates[i].AsGodotObject().Call("initial_attrs").AsGodotDictionary(),
                new Godot.Collections.Dictionary(),
                new Godot.Collections.Array());
            foreach (string key in attr_keys)
            {
                if (!attrs.ContainsKey(key))
                {
                    attrs_ok = false;
                }
            }
        }

        ok = ok && attrs_ok;
        lines.Add($"五维合计拿得到（str/con/agi/int/luk）={_flag(attrs_ok)}");

        string[] difficulties = _column_names(db, "difficulty_config", "difficulty_id");
        bool diff_ok = difficulties.Length > 0;
        ok = ok && diff_ok;
        lines.Add($"难度列 {difficulties.Length} 个={_flag(diff_ok)}（{string.Join("／", difficulties)}）");

        bool valid_ok = errors.Length == 0;
        ok = ok && valid_ok;
        lines.Add($"交叉校验零错误={_flag(valid_ok)}（{errors.Length} 条）");

        _finish_selftest(ok, lines);
    }

    private void _finish_selftest(bool ok, System.Collections.Generic.List<string> lines)
    {
        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        string[] copy_hits = CopyGuard.id_tokens(this);
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        if (copy_hits.Length > 0)
        {
            lines.Add("COPY 命中：" + string.Join("；", copy_hits));
        }

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"BOOTSTRAP SELF-TEST: {(ok ? "OK" : "FAILED")}");
        GetTree().Quit(ok ? 0 : 1);
    }

    /// <summary>行类现在都是 C#，`rows()` 给的是 `Resource`；列值一律 `Get(名字)`（属性不是方法）。</summary>
    private static string[] _column_names(TableDb db, string table_name, string column)
    {
        Godot.Collections.Array rows = db.rows(table_name);
        var out_names = new string[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            out_names[i] = rows[i].AsGodotObject().Get(column).AsString();
        }

        return out_names;
    }

    private static int _attr(Godot.Collections.Dictionary attrs, string key)
        => attrs.ContainsKey(key) ? attrs[key].AsInt32() : 0;

    /// <summary>GDScript 的 `%s` 打的是 `true`／`false`（小写），照抄一份，别让证据行变样。</summary>
    private static string _flag(bool value) => value ? "true" : "false";
}
