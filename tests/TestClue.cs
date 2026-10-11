using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 线索本：隐藏内容与事件判定的线索来源、完成状态（03_副本_黑风寨.md「线索必须能被找到」）——对位
/// `tests/test_clue.gd`（2026-10-11 P4 第四批搬 C#）。
///
/// 这一份**真的把 `clue_screen.tscn` 装起来跑**两处（长列表 ＋ 面板场景），所以它是"第一个需要场景树"
/// 的 C# 用例：`Tree` 由运行器注入，没有就 `Fail`（对位 GDScript 的 `scene_tree == null` 分支）。
/// </summary>
public partial class TestClue : TestCase
{
    private const string ClueScene = "res://scenes/clue_screen.tscn";

    public override string SuiteName() => "线索本";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        ClueService clues = ClueService.create(db, state);
        _check_scene_clues(db, state, clues);
        _check_region_clues(db, clues);
        _check_status_follows_save(state, clues);
        _check_observed_place(db, state);
        _check_clue_source_parsing(db);
        _check_long_list_ui(db, state);
        _check_panel_scene(db, state);
    }

    /// <summary>
    /// 线索来源的解析口径：分号分隔、**空段丢掉**、每段去首尾空白。
    /// 这条是「每条隐藏至少两条线索」那道设计门限的底座——要是空段被当成一条，
    /// 「只有一条;」这种写法就会被机器数成两条而**假绿**。
    /// </summary>
    private void _check_clue_source_parsing(TableDb db)
    {
        var base_row = db.get_row("hidden_trigger", "trig_wine") as HiddenTriggerRow;
        CheckNotNull(base_row, "拿一条真实隐藏内容当模板");
        if (base_row == null)
        {
            return;
        }

        var row = (HiddenTriggerRow)base_row.Duplicate(true);
        row.clue_source = "  甲说的一句话  ;;;乙说的一句话;";
        string[] parsed = row.clues();
        CheckEq(parsed.Length, 2, $"空段丢掉：只剩两条（{_join(parsed)}）");
        if (parsed.Length == 2)
        {
            CheckEq(parsed[0], "甲说的一句话", "第 1 条去掉首尾空白");
            CheckEq(parsed[1], "乙说的一句话", "第 2 条去掉首尾空白");
        }

        row.clue_source = "";
        CheckEq(row.clues().Length, 0, "空字段就是没有线索来源");
        row.clue_source = "只有一条";
        CheckEq(row.clues().Length, 1, "单条写法就是一条（门限会点它名）");

        var event_base = db.get_row("event_check", "ev_gamble") as EventCheckRow;
        CheckNotNull(event_base, "事件判定表也拿一行当模板");
        if (event_base != null)
        {
            var event_row = (EventCheckRow)event_base.Duplicate(true);
            event_row.clue_source = "甲;乙;;";
            CheckEq(event_row.clues().Length, 2, "事件判定的线索来源走同一套解析");
        }
    }

    /// <summary>
    /// 长列表：把黑风寨的隐藏内容复制成 6 倍（36 条隐藏 + 5 条事件 = 41 条），
    /// 面板要**逐条建行**、不许悄悄截断。真实数据只有 8 条左右，这种规模靠当前内容到不了——
    /// 而「列表被截断」在界面上看起来只是「少了几条」，很难被发现。
    /// </summary>
    private void _check_long_list_ui(TableDb db, GodotObject state)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，线索本长列表用例无法进行");
            return;
        }

        TableDb wide = TableWithDuplicatedTriggers("scene_heifengzhai", 6);
        ClueService clues = ClueService.create(wide, state);
        int expected = clues.clues_for_scene("scene_heifengzhai").Count;
        CheckEq(expected, 41, "6 倍复制后是 36 条隐藏 + 5 条事件");
        var screen = GD.Load<PackedScene>(ClueScene).Instantiate<ClueScreen>();
        screen.db_override = wide;
        screen.state_override = state;
        screen.scope = "scene";
        screen.scene_id = "scene_heifengzhai";
        Tree.Root.AddChild(screen);
        screen.setup();
        screen.select_section("hidden");
        CheckEq(
            screen.section_row_count("hidden"),
            expected,
            $"每一行都建出来了（{screen.section_row_count("hidden")} 行）");
        var subtitle = screen.FindChild("Subtitle", true, false) as Label;
        CheckNotNull(subtitle, "有「共 N 条」的小标题");
        if (subtitle != null)
        {
            CheckTrue(subtitle.Text.Contains("共 41 处"), $"小标题写清总数：{subtitle.Text}");
        }

        CheckEq(screen.section_row_count("hidden"), expected, "带名字的行数也对得上");
        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>一张图的线索：隐藏内容 + 事件判定，每条都要有线索来源。</summary>
    private void _check_scene_clues(TableDb db, GodotObject state, ClueService clues)
    {
        Godot.Collections.Array entries = clues.clues_for_scene("scene_heifengzhai");
        CheckEq(entries.Count, 11, "黑风寨 6 条隐藏 + 5 条事件判定");
        var hidden = 0;
        var events = 0;
        foreach (Variant entry_variant in entries)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_s(entry, "kind") == "hidden")
            {
                hidden += 1;
            }
            else
            {
                events += 1;
            }

            CheckFalse(_a(entry, "clues").Count == 0, $"{_s(entry, "name")} 有用得上的线索");
            string text = clues.describe(entry);
            CheckTrue(text.Contains("线索："), $"{_s(entry, "id")} 的文案写清线索：{text}");
            CheckTrue(text.Contains("未完成"), $"新档都还没完成：{_s(entry, "id")}");
        }

        CheckEq(hidden, 6, "黑风寨本图 6 条隐藏（后山密道那条属于塌陷山洞）");
        CheckEq(events, 5, "黑风寨 5 条事件判定（0.29.0 加了 `ev_shen_rescue`：地牢里救沈雁回）");

        // 后山密道的线索在塌陷山洞那张图里
        Godot.Collections.Array cave = clues.clues_for_scene("scene_cave");
        // （石碑那条事件的 scene_id 是空的、按 region_id 归到大地图，所以在 region 视角里）
        CheckEq(cave.Count, 1, "塌陷山洞小地图只有 1 条隐藏（后山密道）");
        var dig_found = false;
        foreach (Variant entry_variant in cave)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_s(entry, "id") == "trig_dig")
            {
                dig_found = true;
                CheckTrue(_s(entry, "note").Contains("后山"), $"线索本用表里的 note 说明这是什么：{_s(entry, "note")}");
                CheckEq(_a(entry, "clues").Count, 2, "后山密道有两条线索");
            }
        }

        CheckTrue(dig_found, "后山密道归在塌陷山洞");

        // 三火盆那条明说「缺位点做不了」，不是静悄悄消失
        var unsupported = 0;
        foreach (Variant entry_variant in entries)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_b(entry, "unsupported"))
            {
                unsupported += 1;
                CheckTrue(clues.describe(entry).Contains("缺地图位点"), $"做不了的条目要说明：{clues.describe(entry)}");
            }
        }

        CheckEq(unsupported, 1, "七类里只剩三火盆（sequence）缺位点");
    }

    /// <summary>大地图线索按地标归组。</summary>
    private void _check_region_clues(TableDb db, ClueService clues)
    {
        Godot.Collections.Array all_entries = clues.region_clues();
        CheckEq(all_entries.Count, 9, "野外共 9 条事件判定（落雁坡 3／荒村 1／山洞 1／黑风寨 2／清风驿 1／驿站 1）");
        CheckEq(clues.clues_for_region("n_luoyanpo").Count, 3, "落雁坡 3 条");
        CheckEq(clues.clues_for_region("n_huangcun").Count, 1, "荒村 1 条");
        CheckEq(clues.clues_for_region("n_qingfengyi").Count, 1, "清风驿 1 条（赌局）");
        foreach (Variant entry_variant in clues.clues_for_region("n_luoyanpo"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            CheckEq(_s(entry, "region_name"), "落雁坡", "带上的地标名对");
            CheckTrue(_s(entry, "source_label").Length > 0, $"判定来源有名字：{_s(entry, "source_label")}");
        }
    }

    /// <summary>
    /// 传闻行「看到于」要写清在哪儿看到的：观察点挂在房间上时得带出 `dungeon_room.room_name`。
    /// **回归**（2026-10-11 搬 C# 时发现）：原 GDScript 取的是 `room.name_cn`，而那张表的列叫
    /// `room_name`——房间名一直取不到（运行期 Invalid get index），这一行会印成空。
    /// 这里把「荒村 · 废屋」钉住。
    /// </summary>
    private void _check_observed_place(TableDb db, GodotObject state)
    {
        var gd_state = (GameState)state;
        CheckEq(
            ClueService.create(db, state).section("rumor", "scene", "")["entries"].AsGodotArray().Count,
            0,
            "还没读过任何碎句时传闻页是空的");
        gd_state.set_flag("flag_obs_ob_huangcun_door_01", true);
        var found = false;
        foreach (Variant entry_variant in ClueService.create(db, state).section("rumor", "scene", "")["entries"]
                     .AsGodotArray())
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_s(entry, "id") == "ob_huangcun_door_01")
            {
                found = true;
                CheckTrue(_s(entry, "text").Contains("看到于：荒村 · 废屋"), $"房间名印得出来：{_s(entry, "text")}");
            }
        }

        CheckTrue(found, "读过的碎句出现在传闻页");
    }

    /// <summary>完成状态跟着存档走：隐藏看 dungeon_records，事件看 event_checks。</summary>
    private void _check_status_follows_save(GodotObject state, ClueService clues)
    {
        var gd_state = (GameState)state;
        foreach (Variant entry_variant in clues.clues_for_scene("scene_heifengzhai"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            CheckFalse(_b(entry, "done"), $"{_s(entry, "id")} 一开始都是未完成");
        }

        gd_state.record_dungeon("scene_heifengzhai", "triggers", "trig_wine");
        gd_state.record_event_check("ev_shed_trap", "done");
        // 用本图（scene 作用域）的事件记录做对照；ev_force_gate 是大地图 region 作用域的
        gd_state.record_event_check("ev_poison_identify", "failed");
        var seen = new Godot.Collections.Dictionary();
        foreach (Variant entry_variant in clues.clues_for_scene("scene_heifengzhai"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            seen[_s(entry, "id")] = entry;
        }

        CheckTrue(_b(seen["trig_wine"].AsGodotDictionary(), "done"), "触发过的隐藏内容标完成");
        CheckTrue(_b(seen["ev_shed_trap"].AsGodotDictionary(), "done"), "判定成功的事件标完成");
        CheckFalse(_b(seen["ev_poison_identify"].AsGodotDictionary(), "done"), "判定失败的不算完成");
        CheckTrue(_b(seen["ev_poison_identify"].AsGodotDictionary(), "failed"), "但记着失败过");
        CheckTrue(
            clues.describe(seen["ev_poison_identify"].AsGodotDictionary()).Contains("失败过"),
            "文案也写「失败过」");
    }

    /// <summary>面板场景：能实例化、按 scope 渲染、能关。</summary>
    private void _check_panel_scene(TableDb db, GodotObject state)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树");
            return;
        }

        var panel = GD.Load<PackedScene>(ClueScene).Instantiate<ClueScreen>();
        panel.state_override = state;
        panel.scope = "scene";
        panel.scene_id = "scene_heifengzhai";
        var closes = new Godot.Collections.Array();
        panel.return_handler = Callable.From(() => closes.Add(true));
        Tree.Root.AddChild(panel);
        panel.setup();
        // 五类分页（设计 0.32.0）：默认落在「主线」，切到「隐藏」才是本图的线索清单
        var titles = new System.Collections.Generic.List<string>();
        for (var index = 0; index < panel._tabs.GetTabCount(); index++)
        {
            titles.Add(panel._tabs.GetTabTitle(index));
        }

        CheckEq(string.Join("|", titles), "主线|支线|传闻|角色|隐藏", "五类页签的名字与顺序");
        panel.select_section("hidden");
        CheckEq(panel.section_id(), "hidden", "切页签后当前页跟着换");
        // 高亮本身看面板自己记的那一份：`--script` 模式下 `TabContainer.current_tab` 不会真正生效
        // （`character_screen` 也踩过这个坑，那边同样是自己记 `_tab_index`）
        CheckEq(panel._section_index, 4, "页签高亮（当前页索引）也跟着换");
        CheckEq(panel.entry_count(), 11, $"面板按条目数渲染：{panel.entry_count()}");
        CheckTrue(panel._subtitle.Text.Contains("共 11 处"), $"小标题写清总数：{panel._subtitle.Text}");
        panel.scope = "region";
        panel.refresh();
        CheckEq(panel.entry_count(), 9, "切成野外视角后按地标列 9 条");
        CheckTrue(panel._subtitle.Text.Contains("野外"), $"小标题跟着作用域变：{panel._subtitle.Text}");
        panel.press_return();
        CheckEq(closes.Count, 1, "离开按钮触发回退");
        Tree.Root.RemoveChild(panel);
        panel.Free();
    }

    private static string _join(string[] parts) => "[" + string.Join(", ", parts) + "]";

    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;

    private static string _s(Godot.Collections.Dictionary dict, string key) => _v(dict, key).AsString();

    private static bool _b(Godot.Collections.Dictionary dict, string key) => _v(dict, key).AsBool();

    private static Godot.Collections.Array _a(Godot.Collections.Dictionary dict, string key) =>
        _v(dict, key).AsGodotArray();
}
