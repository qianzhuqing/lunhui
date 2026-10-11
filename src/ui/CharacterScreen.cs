using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 角色与行囊：角色面板／装备／背包三个页签。C# 版，对位原 `src/ui/character_screen.gd`（1363 行）。
///
/// 界面用代码搭（占位美术阶段的既有约定），数据全部来自 `CharacterSheet` 与 `Inventory`，
/// 面板不自己算数值。写操作（加点、穿戴、卸下、丢弃）后立刻重建页签。
///
/// 自检：`-- --character-selftest` 会走一遍「切页签 → 加点 → 穿脱装备 → 丢钥匙道具」并返回退出码。
///
/// 为了守 800 行硬上限，这个类拆成四份 partial：本文件＝骨架／对外接口／界面搭建／环境，
/// `CharacterScreenActions.cs`＝写操作与几个查询小口，`CharacterScreenPages.cs`＝三个页签的重建，
/// `CharacterScreenSelfTest.cs`＝真实场景自检（棘轮表盯的是**自检那一份**）。
///
/// **跨语言面**：`CharacterSheet`／`SkillGrant`／`LevelService`／`TalentService`／`StoryService`／
/// `RecruitService`／`Inventory`／`TableDb`／`GameState`／`GameSession`／`GameData` 都已是 C#，直调。
/// 两个回调（`back_handler`／`npc_open_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// 用例（`tests/TestCharacterUi.cs`）直接读写 `state_override`／`back_handler`／`_db`，
/// 所以这三处必须是 `[Export]`。
/// </summary>
public partial class CharacterScreen : Control
{
    private const string PLACEHOLDER_SCENE = "res://scenes/placeholder_game.tscn";

    /// <summary>
    /// 立绘目录与命名：**按 char_id**（`assets/sprites/portraits/portrait_&lt;char_id&gt;.png`）。
    /// 美术出图后同名替换；缺图时**退回纯文字占位**，不报错（占位美术阶段的既有口径）。
    /// </summary>
    private const string PORTRAIT_DIR = "res://assets/sprites/portraits/";

    /// <summary>
    /// 背包分类筛选（key ↔ 显示名）。`tools/validate_tables.ps1` 的 5.24 那条警告按这里前四个 key
    /// 的口径提「哪些 `item_type` 没落格」——**两边是一套口径，改这里要连着看那条**。
    /// </summary>
    private static readonly string[][] FILTERS =
    {
        new[] { "all", "全部" },
        new[] { "material", "材料" },
        new[] { "consumable", "消耗品" },
        new[] { "key", "钥匙" },
        new[] { "skillbook", "残页" },
        new[] { "equip", "装备" },
    };

    /// <summary>自检注入点。用例与控制器直接赋值 → `[Export]`。</summary>
    [Export] public Variant state_override = default;

    /// <summary>返回回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable back_handler;

    /// <summary>
    /// Q67 拍板 ④ 的接线点：同伴不站位，交往（赠送／切磋／兑换／委托）从**角色面板点人**开。
    /// 面板只负责「把谁交给场景控制器」，真正的 `open_npc()`（浮层栈、刷新）在控制器那一侧——
    /// 与 `back_handler` 同一个往来口径。**战斗里的角色面板不接这个**（战斗中不交往，按钮自然不摆）。
    /// </summary>
    [Export] public Callable npc_open_handler;

    /// <summary>
    /// 表库。用例直接读 `screen._db`（`TestCharacterUi.cs` 拿它造装备实例），所以对用例要可见。
    /// 类型写成 `GodotObject`（不用 `TableDb`）是为了导出的类型名一定认得出——强类型视图走 `Db`。
    /// </summary>
    [Export] public GodotObject _db;

    private string _selected_char = "";
    private string _selected_slot = "";
    private string _filter = "all";

    /// <summary>
    /// 页签索引自己记一份：`TabContainer.current_tab` 在 `--script` 自检环境里不会真正生效
    /// （节点还没进活动场景树），所以这里作为唯一真相；用户点击页签时由信号同步回来。
    /// </summary>
    private int _tab_index;

    private Label _header;
    private TabContainer _tabs;
    private VBoxContainer _char_box;
    private VBoxContainer _equip_box;
    private VBoxContainer _bag_box;
    private Label _status;

    /// <summary>左侧立绘（设计 14：角色面板左侧 320×480；占位是 80×120 的剪影，4 倍放大显示）</summary>
    private TextureRect _portrait;

    /// <summary>`_db` 的强类型视图（表库本来就是 C#，这里只是把 `GodotObject` 收窄一次）。</summary>
    private TableDb Db => _db as TableDb;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--character-selftest"))
        {
            Callable.From(_run_character_selftest).CallDeferred();
        }
    }

    /// <summary>幂等：用例可以直接调，不依赖引擎是否已经发过 `_ready`。</summary>
    public void setup()
    {
        if (_tabs != null)
        {
            return;
        }

        _db = _resolve_db();
        GodotObject st = current_state();
        string[] ids = st != null ? st.Get("char_ids").AsStringArray() : System.Array.Empty<string>();
        if (ids.Length > 0)
        {
            _selected_char = ids[0];
        }

        _build_ui();
        refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            press_back();
        }
    }

    // ------------------------------------------------------------------ 供用例与入口调用

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        return _session_node()?.Get("state").AsGodotObject();
    }

    public CharacterSheet sheet()
    {
        GodotObject st = current_state();
        if (st == null || _selected_char.Length == 0)
        {
            return null;
        }

        return CharacterSheet.create(Db, st, _selected_char);
    }

    public string status_text() => _status != null ? _status.Text : "";

    public int current_tab() => _tabs != null ? _tab_index : -1;

    public void select_tab(int index)
    {
        if (_tabs == null)
        {
            return;
        }

        _tab_index = System.Math.Clamp(index, 0, System.Math.Max(0, _tabs.GetTabCount() - 1));
        _tabs.CurrentTab = _tab_index;
    }

    private void _on_tab_changed(long index)
    {
        _tab_index = (int)index;
    }

    public void press_back()
    {
        if (_callable_valid(back_handler))
        {
            _invoke_callback(back_handler);
            return;
        }

        _tree()?.ChangeSceneToFile(PLACEHOLDER_SCENE);
    }

    public void select_char(string char_id)
    {
        _selected_char = char_id;
        _selected_slot = "";
        refresh();
    }

    /// <summary>刷新左侧立绘（选中谁显示谁）。缺图时给一句文字占位，不留空白框（0.19.1 的准话）。</summary>
    private void _refresh_portrait()
    {
        if (_portrait == null)
        {
            return;
        }

        string path = portrait_path();
        if (_selected_char.Length == 0 || !ResourceLoader.Exists(path))
        {
            _portrait.Texture = null;
            return;
        }

        _portrait.Texture = GD.Load<Texture2D>(path);
    }

    public string portrait_path() => $"{PORTRAIT_DIR}portrait_{_selected_char}.png";

    /// <summary>
    /// 交往目标（Q67 拍板 ④）：**选中的是同伴**（在队伍里、且不是主角）**并且有人接手这一按**。
    ///
    /// 「主角就是开局第一个人」这条口径与 `GuideService`／`NpcService`／`StoryService` 同一处
    /// （`char_ids[0]`，`CreationService` 把玩家选的出身放在第一位）——主角不摆这一按：
    /// 设计 20 §十 那套「提升途径」是给**同伴**的（招募 +20／支线 +30／赠送／切磋）。
    /// </summary>
    private string _social_target()
    {
        if (_selected_char.Length == 0 || !_callable_valid(npc_open_handler))
        {
            return "";
        }

        GodotObject st = current_state();
        if (st == null)
        {
            return "";
        }

        string[] ids = st.Get("char_ids").AsStringArray();
        if (ids.Length == 0 || System.Array.IndexOf(ids, _selected_char) < 0)
        {
            return "";
        }

        return _selected_char == ids[0] ? "" : _selected_char;
    }

    /// <summary>
    /// Q67 ④ 的落点：把「谁」交给场景控制器，由它走现成的 `open_npc()`——
    /// 浮层栈会压成「角色面板 → 交往屏」，关掉交往屏就回到角色面板。
    /// </summary>
    public Godot.Collections.Dictionary open_social()
    {
        string who = _social_target();
        if (who.Length == 0)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "char_id", "" }, { "error", "这个人不能交往" } };
        }

        _invoke_callback(npc_open_handler, who);
        return new Godot.Collections.Dictionary { { "ok", true }, { "char_id", who }, { "error", "" } };
    }

    public void refresh()
    {
        if (_tabs == null)
        {
            return;
        }

        _refresh_portrait();
        _rebuild_header();
        _rebuild_character();
        _rebuild_equipment();
        _rebuild_bag();
    }

    // ------------------------------------------------------------------ 界面构建

    private void _build_ui()
    {
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // 背板：这界面是盖在游戏画面上的（Tab 打开），没有背板就会让地图与 NPC 透上来把字糊掉
        var backdrop = new ColorRect
        {
            Name = "Backdrop",
            // 背板色值只在主题里一处（`UiKit/colors/backdrop`）
            Color = UiKit.color("backdrop"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        var margin = new MarginContainer { Name = "Margin" };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var top = new HBoxContainer { Name = "Top" };
        top.AddThemeConstantOverride("separation", 12);
        column.AddChild(top);
        _header = new Label
        {
            Name = "Header",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        top.AddChild(_header);
        var back = new Button { Name = "BackButton", Text = "返回游戏" };
        back.Pressed += press_back;
        top.AddChild(back);

        // 左侧立绘（设计 14 §「角色面板」：左侧立绘 320×480 ｜ 右侧标签页）。
        // 占位是**原生 80×120 的人形剪影**，这里 4 倍放大显示（15 §4.6：颗粒要和地图同一量级），
        // 美术出图后**同名替换**，代码一行不动。
        var body = new HBoxContainer { Name = "Body" };
        body.AddThemeConstantOverride("separation", 12);
        column.AddChild(body);
        _portrait = new TextureRect
        {
            Name = "Portrait",
            CustomMinimumSize = new Vector2(320, 480),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        body.AddChild(_portrait);
        _tabs = new TabContainer
        {
            Name = "Tabs",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(760, 520),
        };
        _tabs.TabChanged += _on_tab_changed;
        body.AddChild(_tabs);
        _char_box = _make_tab("角色");
        _equip_box = _make_tab("装备");
        _bag_box = _make_tab("背包");

        _status = new Label
        {
            Name = "Status",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 32),
        };
        column.AddChild(_status);
    }

    private VBoxContainer _make_tab(string tab_name)
    {
        var scroll = new ScrollContainer
        {
            Name = $"Scroll{tab_name}",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var box = new VBoxContainer
        {
            Name = $"Box{tab_name}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        box.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(box);
        _tabs.AddChild(scroll);
        _tabs.SetTabTitle(_tabs.GetTabCount() - 1, tab_name);
        return box;
    }

    private Button _make_button(string node_name, string text, System.Action pressed, bool disabled = false)
    {
        var button = new Button
        {
            Name = node_name,
            Text = text,
            Disabled = disabled,
            Alignment = HorizontalAlignment.Left,
        };
        button.Pressed += pressed;
        return button;
    }

    private Label _make_label(string node_name, string text, int font_size = 0, string color = "")
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

        if (color.Length > 0)
        {
            label.AddThemeColorOverride("font_color", new Color(color));
        }

        return label;
    }

    private static void _clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    // ------------------------------------------------------------------ 环境

    private TableDb _resolve_db()
    {
        GodotObject game_data = _session_node("GameData");
        GodotObject game_db = game_data?.Get("db").AsGodotObject();
        if (game_db != null && game_db.Get("tables").AsGodotDictionary().Count > 0)
        {
            return game_db as TableDb;
        }

        var db = new TableDb();
        db.load_all();
        return db;
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

    /// <summary>`GameState.MAX_PARTY`——C# 常量，直读。</summary>
    private static int _max_party() => GameState.max_party();

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

    /// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static double _f(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    private static string _bool(bool value) => value ? "true" : "false";

    /// <summary>对位 GDScript 的 `%.2f`：**显式走不变文化**，别让小数点跟着系统区域跑。</summary>
    private static string _f2(double value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
