using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 线索本（03_副本_黑风寨.md：「线索必须能被找到」）。C# 版，对位原 `src/ui/clue_screen.gd`（449 行）。
///
/// 骨架在 `scenes/clue_screen.tscn`，内容来自 `src/core/ClueService.cs`（**已迁 C#**，
/// 走 `ClueService.create((TableDb)db, state)` 建实例、直调 `section`／`clues_for_scene` 等读内容；
/// 服务那两处 `SECTION_ORDER`／`SECTION_TITLES` 直接读静态字段，**别在这里抄第二份页签顺序**）。
/// 入口：小地图里按 K（看本图隐藏内容与事件判定）；大地图按 K（按地标看野外事件）。自检：`-- --clue-selftest`。
///
/// **跨语言面**：`GameState`／`TableDb`／`GameSession`／`GameData` 都已是 C#，直调。
/// `return_handler` 收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// 用例（`tests/TestClue.cs`）读 `_tabs`／`_section_index`／`_subtitle` 三个字段，
/// 所以它们必须是 `[Export]`。
/// </summary>
public partial class ClueScreen : CanvasLayer
{
    [Export] public Variant state_override = default;

    /// <summary>用例注入的表库（要在真场景里跑「几十条线索」这种发行数据用不到的配置）</summary>
    [Export] public Variant db_override = default;

    [Export] public Callable return_handler;

    /// <summary>"scene"（小地图）或 "region"（大地图按地标）</summary>
    [Export] public string scope = "scene";

    [Export] public string scene_id = "";

    public GodotObject db;
    public GodotObject state;
    public ClueService clues;

    private Label _title;

    /// <summary>用例直接读 `panel._subtitle.text`，所以对 GDScript 要可见 → `[Export]`。</summary>
    [Export] public Label _subtitle;

    /// <summary>用例直接读 `panel._tabs.get_tab_count()`，所以对 GDScript 要可见 → `[Export]`。</summary>
    [Export] public TabContainer _tabs;

    /// <summary>页签 id → 那一页装条目的 VBoxContainer</summary>
    private readonly System.Collections.Generic.Dictionary<string, VBoxContainer> _lists = new();

    /// <summary>页签顺序（取自服务那一处常量，别在这里抄第二份）</summary>
    private Godot.Collections.Array _sections = new();

    /// <summary>
    /// 页签索引自己记一份：`TabContainer.current_tab` 在 `--script` 自检环境里不会真正生效
    /// （与 `character_screen` 同一个坑，那边也是这么记的）。用例读 `panel._section_index` → `[Export]`。
    /// </summary>
    [Export] public int _section_index = 0;

    private Label _status;
    private Button _return_button;
    private bool _bound;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--clue-selftest"))
        {
            Callable.From(_run_clue_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (clues != null)
        {
            return;
        }

        db = _resolve_db();
        state = current_state();
        Node session_node = _session_node();
        if (state == null)
        {
            state = GameState.new_game((TableDb)db, "normal", System.Array.Empty<string>(), 0);
            if (session_node != null)
            {
                session_node.Call("set_state", state);
            }
        }

        clues = ClueService.create((TableDb)db, state);
        _bind_ui();
        refresh();
    }

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        return _session_node()?.Get("state").AsGodotObject();
    }

    public string status_text() => _status != null ? _status.Text : "";

    public int entry_count() => section_row_count(section_id());

    /// <summary>当前页签的 id（"main"／"side"／"rumor"／"cast"／"hidden"）</summary>
    public string section_id()
    {
        if (_sections.Count == 0)
        {
            return "hidden";
        }

        return _sections[Mathf.Clamp(_section_index, 0, _sections.Count - 1)].AsString();
    }

    /// <summary>
    /// 某一页建了几行**条目**——用例按页数行，免得全局找 `Clue_*` 把别的页也算进来；
    /// 只数 `Clue_*`（空页摆的那个提示行不算一行线索）。
    /// 条目现在挂在卡片的内容列里（0.32.0 照示意图补的卡片结构），所以**按后代递归数**。
    /// </summary>
    public int section_row_count(string id)
    {
        if (!_lists.TryGetValue(id, out VBoxContainer box) || box == null)
        {
            return 0;
        }

        return _count_clue_rows(box);
    }

    private static int _count_clue_rows(Node node)
    {
        int rows = 0;
        foreach (Node child in node.GetChildren())
        {
            if (child.Name.ToString().StartsWith("Clue_", System.StringComparison.Ordinal))
            {
                rows += 1;
            }
            else
            {
                rows += _count_clue_rows(child);
            }
        }

        return rows;
    }

    /// <summary>切页签：内容与标题都跟着换（当前页签的高亮由 `TabContainer` 自己画）</summary>
    public void select_section(string id)
    {
        int index = _sections.IndexOf(id);
        if (index < 0)
        {
            return;
        }

        _section_index = index;
        if (_tabs != null && _tabs.CurrentTab != index)
        {
            _tabs.CurrentTab = index;
        }

        refresh();
    }

    private void _on_tab_changed(long index)
    {
        if (index < 0 || index >= _sections.Count || index == _section_index)
        {
            return;
        }

        _section_index = (int)index;
        refresh();
    }

    private void _bind_ui()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        _title = _require_node("Panel/Margin/Column/Title") as Label;
        _subtitle = _require_node("Panel/Margin/Column/Subtitle") as Label;
        _tabs = _require_node("Panel/Margin/Column/Tabs") as TabContainer;
        _status = _require_node("Panel/Margin/Column/Status") as Label;
        _return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button;
        if (_return_button != null)
        {
            _return_button.Pressed += press_return;
        }

        // 五类分页（设计 0.32.0）：主线／支线／传闻／角色／隐藏
        _sections = _section_order();
        Godot.Collections.Dictionary titles = _section_titles();
        foreach (Variant id_variant in _sections)
        {
            string id = id_variant.AsString();
            _lists[id] = _make_tab(titles.ContainsKey(id) ? titles[id].AsString() : id);
        }

        _tabs.TabChanged += _on_tab_changed;
        _status.Text = "";
    }

    /// <summary>一页＝滚动区里一个 VBox：横向滚动关掉（宽了要被 `CONTENT` 那道门限量出来）</summary>
    private VBoxContainer _make_tab(string title)
    {
        var scroll = new ScrollContainer { Name = $"Scroll{title}" };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var box = new VBoxContainer { Name = $"List{title}" };
        box.AddThemeConstantOverride("separation", 6);
        box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(box);
        _tabs.AddChild(scroll);
        _tabs.SetTabTitle(_tabs.GetTabCount() - 1, title);
        return box;
    }

    private Node _require_node(string path)
    {
        Node node = GetNodeOrNull(path);
        if (node == null)
        {
            // 场景骨架是「节点名即接口」，缺一个就整页画不出来——报出来，别静默画半张
            GD.PushError($"clue_screen.tscn 缺少节点：{path}");
        }

        return node;
    }

    public void press_return()
    {
        if (_callable_valid(return_handler))
        {
            _invoke_callback(return_handler);
            return;
        }

        QueueFree();
    }

    public void show_message(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    public void refresh()
    {
        if (clues == null || _tabs == null)
        {
            return;
        }

        if (scope != "scene" && scope != "region")
        {
            scope = "scene";
        }

        // `scene_id` 还没被调用方赋上时（自检环境里 `_ready` 先跑一遍）不算数据错，别刷假 ERROR
        if (scope == "scene" && scene_id.Length > 0 && db.Call("get_row", "map_local", scene_id).AsGodotObject() == null)
        {
            // 数据错：scene id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见决策 329）
            GD.PushError($"[clue] map_local 里没有这张图：{scene_id}");
        }

        Godot.Collections.Dictionary titles = _section_titles();
        // 五页一次性都建出来：没显示的那几页也得有内容，`CONTENT` 那道门限才量得到宽度
        foreach (Variant id_variant in _sections)
        {
            string id = id_variant.AsString();
            VBoxContainer box = _lists[id];
            _clear(box);
            Godot.Collections.Dictionary page = clues.section(id, scope, scene_id);
            Godot.Collections.Array entries = page["entries"].AsGodotArray();
            // 一页＝一块卡片（标题条写这一类叫什么，右边那截写几条——照示意图的信息层级）
            VBoxContainer body = UiKit.add_card(
                box,
                titles.ContainsKey(id) ? titles[id].AsString() : id,
                $"{entries.Count} 条",
                $"Card{id}");
            if (entries.Count == 0)
            {
                body.AddChild(_make_label("Empty", _empty_text(id), 0, "#8A8578"));
                continue;
            }

            foreach (Variant entry_variant in entries)
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                bool done = entry.ContainsKey("done") && entry["done"].AsBool();
                body.AddChild(_make_label(
                    $"Clue_{entry["id"].AsString()}",
                    entry["text"].AsString(),
                    0,
                    done ? "#6fcf97" : ""));
            }
        }

        _apply_page_header();
    }

    /// <summary>标题与小标题永远取自**当前那页**（文案在服务里拼，面板不自己编）</summary>
    private void _apply_page_header()
    {
        Godot.Collections.Dictionary page = clues.section(section_id(), scope, scene_id);
        _title.Text = page["title"].AsString();
        _subtitle.Text = page["subtitle"].AsString();
    }

    private static string _empty_text(string id) => id switch
    {
        "main" => "还没有可跟的线索",
        "side" => "还没有人托你办事",
        "rumor" => "还没听到什么风声——多跟人聊聊，多看看墙角",
        "cast" => "还没遇上同行的人",
        _ => "这里暂时没有可查的线索",
    };

    private static void _clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            // 先摘再 queue_free（同 creation_screen._rebuild_body 的注释）：名字要立刻可复用
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Label _make_label(string node_name, string text, int font_size = 0, string color = "")
    {
        var label = new Label
        {
            Name = node_name,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        if (font_size > 0)
        {
            label.AddThemeFontSizeOverride("font_size", font_size);
        }

        if (!string.IsNullOrEmpty(color))
        {
            label.AddThemeColorOverride("font_color", Color.FromHtml(color));
        }

        return label;
    }

    // ------------------------------------------------------------------ 环境

    private GodotObject _resolve_db()
    {
        if (db_override.VariantType != Variant.Type.Nil)
        {
            return db_override.AsGodotObject();
        }

        GodotObject game_data = _session_node("GameData");
        GodotObject game_db = game_data?.Get("db").AsGodotObject();
        if (game_db != null && game_db.Get("tables").AsGodotDictionary().Count > 0)
        {
            return game_db;
        }

        var table_db = new TableDb();
        table_db.load_all();
        return table_db;
    }

    private Node _session_node(string node_name = "GameSession") => _tree()?.Root.GetNodeOrNull(node_name);

    /// <summary>不在场景树里时 `get_tree()` 会打一条 ERROR（`--script` 模式用例直接 new 节点就会踩到）。</summary>
    private SceneTree _tree()
    {
        if (IsInsideTree())
        {
            SceneTree tree = GetTree();
            if (tree != null)
            {
                return tree;
            }
        }

        return Engine.GetMainLoop() as SceneTree;
    }

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    /// <summary>服务那一处页签顺序／标题（只此一份，别在这里抄第二份）。</summary>
    private static Godot.Collections.Array _section_order() => ClueService.SECTION_ORDER;

    private static Godot.Collections.Dictionary _section_titles() => ClueService.SECTION_TITLES;

    // ------------------------------------------------------------------ 自检

    /// <summary>自检取某页某一条的文字（找不到返回空串——`.contains()` 于是当场假）</summary>
    private static string _section_text(ClueService service, string section_id, string entry_id)
    {
        foreach (Variant entry_variant in service.section(section_id)["entries"].AsGodotArray())
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["id"].AsString() == entry_id)
            {
                return entry["text"].AsString();
            }
        }

        return "";
    }

    private async void _run_clue_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        // 1. 小地图线索：黑风寨的隐藏内容与事件判定
        scope = "scene";
        scene_id = "scene_heifengzhai";
        refresh();
        Godot.Collections.Array scene_entries = clues.clues_for_scene(scene_id);
        int hidden = 0;
        int events = 0;
        int with_clues = 0;
        int unsupported = 0;
        foreach (Variant entry_variant in scene_entries)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["kind"].AsString() == "hidden")
            {
                hidden += 1;
            }
            else
            {
                events += 1;
            }

            if (entry["clues"].AsGodotArray().Count > 0)
            {
                with_clues += 1;
            }

            if (entry.ContainsKey("unsupported") && entry["unsupported"].AsBool())
            {
                unsupported += 1;
            }
        }

        // 黑风寨本图 6 条隐藏（后山密道那条的 scene_id 是 scene_cave）＋ 5 条事件判定
        // （0.29.0 加了 `ev_shen_rescue`：地牢里救沈雁回）
        ok = ok && hidden == 6 && events == 5 && with_clues == scene_entries.Count;
        lines.Add($"黑风寨：隐藏 {hidden} 条／事件 {events} 条／都有线索={_bool(with_clues == scene_entries.Count)}"
            + $"（缺位点未做 {unsupported} 条）");
        select_section("hidden");
        lines.Add($"隐藏页行数={entry_count()}");
        ok = ok && entry_count() == scene_entries.Count;
        // 2. 完成状态跟存档走
        state.Call("record_dungeon", scene_id, "triggers", "trig_wine");
        refresh();
        bool wine_done = false;
        foreach (Variant entry_variant in clues.clues_for_scene(scene_id))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["id"].AsString() == "trig_wine")
            {
                wine_done = entry["done"].AsBool();
            }
        }

        ok = ok && wine_done;
        lines.Add($"完成状态跟存档：酒葫芦已触发={_bool(wine_done)}");
        // 3. 大地图线索（按地标）
        scope = "region";
        refresh();
        Godot.Collections.Array region_entries = clues.region_clues();
        // 野外按地标：落雁坡 3、荒村 1、塌陷山洞 1、黑风寨 2、清风驿 1、官道 1 = 9
        // （官道那条是 0.28.0 的 `ev_patrol_check`，此前这条期望值一直少算了驿站这一格）
        ok = ok && region_entries.Count == 9;
        lines.Add($"大地图线索={region_entries.Count} 条（落雁坡 "
            + $"{clues.clues_for_region("n_luoyanpo").Count}／荒村 "
            + $"{clues.clues_for_region("n_huangcun").Count}）");
        // 隐藏那一页跟着视线走：野外视角摆 9 条、小标题写「野外」，并且明写「未揭」几处
        Godot.Collections.Dictionary hidden_region = clues.section("hidden", "region", "");
        string hidden_region_subtitle = hidden_region["subtitle"].AsString();
        bool hidden_scope_ok = section_row_count("hidden") == region_entries.Count
            && hidden_region_subtitle.Contains("野外")
            && hidden_region_subtitle.Contains("未揭");
        ok = ok && hidden_scope_ok;
        lines.Add($"隐藏页跟视角走（野外）={_bool(hidden_scope_ok)}（{hidden_region_subtitle}）");

        // —— 3.5 五类分页（设计 0.32.0／UI 落地清单 §二-10）——
        bool tabs_ok = _sections.Count == 5 && _tabs.GetTabCount() == 5;
        ok = ok && tabs_ok;
        lines.Add($"五类页签={_bool(tabs_ok)}（{string.Join("／", _section_id_list())}）");
        // 当前页高亮跟着走：切到「传闻」那页，索引、TabContainer、标题三处都要换
        select_section("rumor");
        bool switched = _section_index == 2 && _tabs.CurrentTab == 2 && _title.Text.Contains("传闻");
        ok = ok && switched;
        lines.Add($"切页签＝标题与高亮都跟着换={_bool(switched)}（{_title.Text}）");

        // 主线：开局 4 步、第一步「进行中」、后面的「未接」、小标题写百分比
        Godot.Collections.Dictionary main_page = clues.section("main");
        string main_text = "";
        foreach (Variant entry_variant in main_page["entries"].AsGodotArray())
        {
            main_text += entry_variant.AsGodotDictionary()["text"].AsString();
        }

        bool main_ok = main_page["entries"].AsGodotArray().Count == db.Call("rows", "guide_step").AsGodotArray().Count
            && main_page["subtitle"].AsString().Contains("进度")
            && main_text.Contains("进行中") && main_text.Contains("未接");
        ok = ok && main_ok;
        lines.Add($"主线页={_bool(main_ok)}（{main_page["subtitle"].AsString()}）");

        // 支线：条件没到＝待接；凑齐条件＝待交；交过＝已了（三步各改一处存档，状态词跟着动）
        string side_text_before = _section_text(clues, "side", "nq_wang_01");
        _inventory()?.Call("add_item", db, "item_iron", 10);
        string side_text_ready = _section_text(clues, "side", "nq_wang_01");
        state.Call("set_flag", NpcService.quest_done_flag("nq_wang_01"), true);
        string side_text_done = _section_text(clues, "side", "nq_wang_01");
        bool side_ok = side_text_before.Contains("［待接］")
            && side_text_ready.Contains("［待交］") && side_text_done.Contains("［已了］")
            && side_text_before.Contains("好感 +15");
        ok = ok && side_ok;
        lines.Add($"支线页：待接→待交→已了={_bool(side_ok)}（委托人与奖励也写了）");

        // 传闻：两处来源各接一遍，且「验」跟着指向的东西走（用一份新档做，免得被上面的旗标污染）
        GodotObject fresh = GameState.new_game((TableDb)db, "normal", System.Array.Empty<string>(), 0);
        ClueService fresh_clues = ClueService.create((TableDb)db, fresh);
        bool rumor_empty = fresh_clues.section("rumor")["entries"].AsGodotArray().Count == 0;
        fresh.Call("set_flag", "flag_obs_ob_dukou_pile_04", true);
        string rumor_text = _section_text(fresh_clues, "rumor", "ob_dukou_pile_04");
        bool rumor_ok = rumor_empty && rumor_text.Contains("未验") && rumor_text.Contains("醉刀客");
        ok = ok && rumor_ok;
        lines.Add($"传闻页：读过的碎句认得出「指向谁」＝{_bool(rumor_ok)}"
            + $"（{(rumor_text.Contains("醉刀客") ? "指向醉刀客" : "没认出指向")}）");
        fresh.Call("set_flag", WorldEventService.hint_flag("trig_wine"), true);
        int rumor_heard = fresh_clues.section("rumor")["entries"].AsGodotArray().Count;
        fresh.Call("record_dungeon", "scene_heifengzhai", "triggers", "trig_wine");
        bool rumor_verified = rumor_heard == 2
            && _section_text(fresh_clues, "rumor", "ob_dukou_pile_04").Contains("已验")
            && _section_text(fresh_clues, "rumor", "we_rumor").Contains("已验");
        ok = ok && rumor_verified;
        lines.Add($"传闻页：触发了它指向的东西才翻「已验」＝{_bool(rumor_verified)}（听来 {rumor_heard} 条）");

        // 角色：主角自己不算「同伴」；同伴状态跟着队伍与加入条件走
        Godot.Collections.Dictionary cast_page = fresh_clues.section("cast");
        string[] char_ids = fresh.Get("char_ids").AsStringArray();
        string protagonist_id = char_ids.Length > 0 ? char_ids[0] : "";
        bool protagonist_listed = false;
        foreach (Variant entry_variant in cast_page["entries"].AsGodotArray())
        {
            if (entry_variant.AsGodotDictionary()["id"].AsString() == protagonist_id)
            {
                protagonist_listed = true;
            }
        }

        string ci_before = _section_text(fresh_clues, "cast", "ch_ci");
        fresh.Call("set_flag", "flag_board_read", true);
        string ci_ready = _section_text(fresh_clues, "cast", "ch_ci");
        // `char_ids` 是 PackedStringArray（值类型）：C# 侧要「取出来、加一个、写回去」，
        // 别照 GDScript 的 `fresh.char_ids.append(...)` 就地改——那样改的是一份拷贝。
        var joined_ids = new string[char_ids.Length + 1];
        char_ids.CopyTo(joined_ids, 0);
        joined_ids[char_ids.Length] = "ch_ci";
        fresh.Set("char_ids", joined_ids);
        string ci_joined = _section_text(fresh_clues, "cast", "ch_ci");
        bool cast_ok = !protagonist_listed
            && ci_before.Contains("［未遇］") && ci_ready.Contains("［待入队］")
            && ci_joined.Contains("［已入队］");
        ok = ok && cast_ok;
        lines.Add($"角色页：主角不摆＋同伴未遇→待入队→已入队={_bool(cast_ok)}"
            + $"（共 {cast_page["entries"].AsGodotArray().Count} 行）");
        // 版式预算：整页最小高度要塞得进设计分辨率
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        ok = ok && LayoutBudget.has_opaque_backdrop(this);
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        // 滚动区内容宽度：横滚是关着的，线索一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
        ok = ok && LayoutBudget.content_fits(this);
        lines.Add(LayoutBudget.ascii_content_line(this));
        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        string[] copy_hits = CopyGuard.id_tokens(this);
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        if (copy_hits.Length > 0)
        {
            lines.Add($"COPY 命中：{string.Join("；", copy_hits)}");
        }

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"CLUE SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

    private string[] _section_id_list()
    {
        var ids = new System.Collections.Generic.List<string>();
        foreach (Variant id_variant in _sections)
        {
            ids.Add(id_variant.AsString());
        }

        return ids.ToArray();
    }

    private GodotObject _inventory() => state?.Get("inventory").AsGodotObject();

    // ------------------------------------------------------------------ 小工具

    /// <summary>这个回调接上没有（空 Callable 与「根本没接」都算没接）。</summary>
    private static bool _callable_valid(Callable callable) =>
        callable.Delegate != null || (callable.Target != null && !callable.Method.IsEmpty);

    /// <summary>调这个回调（没接就不动）；返回值原样给出来，`sweep_handler` 那类要读它。</summary>
    private static Variant _invoke_callback(Callable callable, params Variant[] args)
    {
        if (!_callable_valid(callable))
        {
            return default;
        }

        return callable.Call(args);
    }

    private static string _bool(bool value) => value ? "true" : "false";
}
