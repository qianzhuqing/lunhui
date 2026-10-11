using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 创建角色（设计 13，0.17.0）。C# 版，对位原 `src/ui/creation_screen.gd`（682 行）。
///
/// 两条路——「使用模板」与「不使用模板」：
///   使用模板：选出身（5 张卡）→ 选天赋 → 起名 → 确认
///   不使用模板：分七维（总和 49、单项 3~15）→ 选武器 → 选本系 ★1 武学 → 选天赋 → 起名 → 确认
///
/// 界面用代码搭（占位美术阶段的既有约定）。所有规则与数据都在 `CreationService`／
/// `TalentService` 里，这里只做「显示 + 收集选择 + 把 spec 交给 MenuController」。
///
/// 两条纪律（设计 13）：**创建没走完不写存档**（确认那一刻才落盘）；
/// 选了谁当主角，谁就不作为同伴出现（`RecruitService` 只收不在队里的人，天然满足）。
///
/// 自检：`-- --creation-selftest` 走「模板路 → 自建路 → 非法方案被拒」并返回退出码。
///
/// **拆成三个 partial 文件**（单文件 800 行硬上限）：本文件＝骨架／刷新／选择／提交／共用助手，
/// `CreationScreenSections.cs`＝各区段渲染与汇总，`CreationScreenSelfTest.cs`＝自检。
/// **跨语言面**：`CreationService`／`TalentService`／`MenuController`／`SaveStore`／`TableDb`／
/// `CopyGuard`／`LayoutBudget`／`GameData`／`GameSession` 都已是 C#，直调。
/// 回调（`scene_switch_handler`）收 `Callable`（P5 起 GDScript 已清零）。
/// `store_override` 被用例与 `check_loop` 直接赋值，必须是 `[Export]`。
/// </summary>
public partial class CreationScreen : Control
{
    /// <summary>确认后落盘进游戏；返回时回主菜单。</summary>
    private const string MENU_SCENE = "res://scenes/main_menu.tscn";
    private const string HUB_SCENE = "res://scenes/placeholder_game.tscn";

    /// <summary>七维的展示顺序（与面板一致：五维在前，资质在后）</summary>
    private static readonly string[] ATTR_ORDER = { "str", "con", "agi", "int", "luk", "wu", "gen" };

    private const string ROUTE_ORIGIN = "origin";
    private const string ROUTE_CUSTOM = "custom";

    /// <summary>立绘目录：**按 char_id** 命名（`assets/sprites/portraits/portrait_&lt;char_id&gt;.png`）。</summary>
    private const string PORTRAIT_DIR = "res://assets/sprites/portraits/";

    /// <summary>缺图时的浅色底提示（不留空白框，玩家不会以为是 bug）</summary>
    private const string PORTRAIT_PLACEHOLDER = "（立绘待出）";

    /// <summary>自检注入点。`check_loop`／`test_menu` 直接赋值，必须是 `[Export]`。</summary>
    [Export] public Variant store_override = default;

    /// <summary>切场景回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable scene_switch_handler;

    private GodotObject db;

    private string route = ROUTE_ORIGIN;
    private Godot.Collections.Dictionary spec = new();

    private VBoxContainer _content;
    private Label _status;
    private Button _confirm_button;
    private HBoxContainer _route_box;
    private VBoxContainer _body;

    /// <summary>右侧立绘（设计 0.19.1：创建界面放右侧 320×480；占位是 80×120 剪影 4 倍显示）</summary>
    private TextureRect _portrait = null;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--creation-selftest"))
        {
            Callable.From(_run_creation_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (db != null)
        {
            return;
        }

        db = _resolve_db();
        spec = new Godot.Collections.Dictionary
        {
            { "route", ROUTE_ORIGIN },
            { "talents", new Godot.Collections.Array() },
            { "name_cn", "" },
        };
        _build_ui();
        _refresh();
    }

    private GodotObject _resolve_db()
    {
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

    private Node _session_node(string node_name) => _tree()?.Root.GetNodeOrNull(node_name);

    private SceneTree _tree()
    {
        if (IsInsideTree())
        {
            return GetTree();
        }

        return Engine.GetMainLoop() as SceneTree;
    }

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    // ------------------------------------------------------------------ 界面

    private void _build_ui()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // 背板色值只在主题里一处（`UiKit.color("backdrop")`）
        var bg = new ColorRect { Name = "Backdrop", Color = UiKit.color("backdrop") };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var margin = new MarginContainer { Name = "Margin" };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        // 立绘位（320×480）进来之后纵向只剩 6px 余量：上下边距压到 8——设计 0.19.1 提醒过
        // 「这两处版面会很紧，加任何一行都要先想清楚从哪儿腾」。
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var column = new VBoxContainer { Name = "Column" };
        margin.AddChild(column);

        var title = new Label { Name = "Title", Text = "创建角色" };
        // 界面标题档（设计 15 §4.4：标题 24／正文 12，**不再有 16 档**——16 ＝ 12×1.33，像素字发虚）
        title.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(title);

        _route_box = new HBoxContainer { Name = "Routes" };
        string[][] route_entries =
        {
            new[] { ROUTE_ORIGIN, "使用模板" },
            new[] { ROUTE_CUSTOM, "不使用模板" },
        };
        foreach (string[] entry in route_entries)
        {
            var button = new Button
            {
                Name = "Route" + _capitalize(entry[0]),
                Text = entry[1],
                CustomMinimumSize = new Vector2(160, 40),
            };
            string captured = entry[0];
            button.Pressed += () => select_route(captured);
            _route_box.AddChild(button);
        }

        column.AddChild(_route_box);

        var scroll = new ScrollContainer { Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill };
        // 左内容 / 右立绘（设计 0.19.1 的版式：创建界面立绘放右侧）
        var body = new HBoxContainer { Name = "Body", SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        column.AddChild(body);
        body.AddChild(scroll);
        _portrait = new TextureRect
        {
            Name = "Portrait",
            CustomMinimumSize = new Vector2(320, 480),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        body.AddChild(_portrait);
        _content = new VBoxContainer { Name = "Content", CustomMinimumSize = new Vector2(760, 0) };
        scroll.AddChild(_content);
        _body = _content;

        _status = new Label
        {
            Name = "Status",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(760, 0),
        };
        column.AddChild(_status);

        var buttons = new HBoxContainer { Name = "Buttons" };
        _confirm_button = new Button
        {
            Name = "ConfirmButton",
            Text = "确认创建",
            CustomMinimumSize = new Vector2(200, 40),
        };
        _confirm_button.Pressed += () => confirm();
        buttons.AddChild(_confirm_button);
        var back = new Button
        {
            Name = "BackButton",
            Text = "返回（Esc）",
            CustomMinimumSize = new Vector2(160, 40),
        };
        back.Pressed += go_back;
        buttons.AddChild(back);
        column.AddChild(buttons);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            go_back();
        }
    }

    public void select_route(string next)
    {
        route = next;
        spec["route"] = next;
        _refresh();
    }

    private void _rebuild_body()
    {
        foreach (Node child in _body.GetChildren())
        {
            // **先从树上摘掉再 queue_free**：只 queue_free 的话旧节点要到帧末才真走，
            // 而下面马上又用**同一个名字**建新节点——Godot 见名字撞了就把新的自动改名
            // （`@Label@718` 这种），于是"按名字找节点"的人拿到的可能是上一帧那个（2026-10-04 踩到）。
            _body.RemoveChild(child);
            child.QueueFree();
        }

        _body = _content;
    }

    private void _refresh()
    {
        _rebuild_body();
        _refresh_portrait();
        if (route == ROUTE_ORIGIN)
        {
            _build_origin_section();
        }
        else
        {
            _build_custom_section();
        }

        _build_talent_section();
        _build_name_section();
        _build_summary_section();
        string[] errors = CreationService.validate((TableDb)db, _current_spec());
        _confirm_button.Disabled = errors.Length > 0;
        _status.Text = errors.Length == 0 ? "可以创建了：按「确认创建」" : string.Join("；", errors);
    }

    // ------------------------------------------------------------------ 选择

    public void pick_origin(string origin_id)
    {
        spec["origin_id"] = origin_id;
        // 预填**真名**而不是卡的标题（设计 21 §七 第 5 条）：书生的卡叫「家道失落的书生」，
        // 但主角默认名该是「陆文昭」。空值退回卡标题，见 `CreationService.default_name_of`。
        if (_s(spec, "name_cn").Trim().Length == 0)
        {
            spec["name_cn"] = CreationService.default_name_of((TableDb)db, origin_id);
        }

        _refresh();
    }

    public void nudge_attr(string attr_id, int delta)
    {
        Godot.Collections.Dictionary attrs = spec.ContainsKey("attrs")
            ? spec["attrs"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        attrs[attr_id] = (attrs.ContainsKey(attr_id) ? attrs[attr_id].AsInt32() : 0) + delta;
        spec["attrs"] = attrs;
        _refresh();
    }

    public void pick_weapon(string weapon_type)
    {
        spec["weapon_type"] = weapon_type;
        // 换武器就把起始武学清掉：上一把的武学多半不在新武器的选项里
        spec.Remove("start_skill_id");
        _refresh();
    }

    public void pick_start_skill(string skill_id)
    {
        spec["start_skill_id"] = skill_id;
        _refresh();
    }

    public void toggle_talent(string talent_id)
    {
        Godot.Collections.Dictionary result = TalentService.toggle((TableDb)db, _picks(), talent_id);
        if (_b(result, "ok"))
        {
            // `Array(result["picks"])`：C# 的 `string[]` 进 Variant 是 PackedStringArray，
            // 而面板自己后面按 Godot Array 读（`AsGodotArray()`），所以这里显式转成 Array 存。
            var next = new Godot.Collections.Array();
            foreach (string pick in result["picks"].AsStringArray())
            {
                next.Add(pick);
            }

            spec["talents"] = next;
        }
        else
        {
            _status.Text = _s(result, "error");
        }

        _refresh();
    }

    // ------------------------------------------------------------------ 提交

    /// <summary>确认创建：走 `MenuController.new_game(spec)`（落盘也由它做），成功就进枢纽页。</summary>
    public Godot.Collections.Dictionary confirm()
    {
        string[] errors = CreationService.validate((TableDb)db, _current_spec());
        if (errors.Length > 0)
        {
            _status.Text = string.Join("；", errors);
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", string.Join("; ", errors) },
            };
        }

        MenuController controller = MenuController.create(
            (TableDb)db, _make_store(), System.Array.Empty<string>(), "normal");
        Godot.Collections.Dictionary result = controller.new_game(_current_spec());
        if (!_b(result, "ok"))
        {
            string message = _s(result, "message");
            _status.Text = message;
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", message } };
        }

        // 会话状态要写进 GameSession（与菜单 `_handle()` 同一处口径）：枢纽页与大地图都读它
        Node session = _session_node("GameSession");
        GodotObject state = result["state"].AsGodotObject();
        if (session != null && state != null)
        {
            session.Call("set_state", state);
        }

        _status.Text = "创建完成，进游戏";
        _switch_to(HUB_SCENE);
        return new Godot.Collections.Dictionary { { "ok", true }, { "slot", _i(result, "slot") } };
    }

    private GodotObject _make_store()
    {
        if (store_override.VariantType != Variant.Type.Nil)
        {
            return store_override.AsGodotObject();
        }

        return SaveStore.create(SaveStore.default_dir(), SaveStore.default_slot_count());
    }

    public void go_back() => _switch_to(MENU_SCENE);

    private void _switch_to(string path)
    {
        if (_callable_valid(scene_switch_handler))
        {
            _invoke_callback(scene_switch_handler, path);
            return;
        }

        SceneTree tree = _tree();
        if (tree != null)
        {
            tree.ChangeSceneToFile(path);
        }
    }

    // ------------------------------------------------------------------ 共用助手

    private Label _section_label(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    private string _attr_name(string attr_id)
    {
        Resource row = ((TableDb)db).get_row("attribute_def", attr_id);
        return row != null ? row.Get("name_cn").AsString() : attr_id;
    }

    private string[] _picks()
    {
        var out_list = new System.Collections.Generic.List<string>();
        Godot.Collections.Array talents = spec.ContainsKey("talents")
            ? spec["talents"].AsGodotArray()
            : new Godot.Collections.Array();
        foreach (Variant item in talents)
        {
            out_list.Add(item.AsString());
        }

        return out_list.ToArray();
    }

    private static string _capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

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

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
