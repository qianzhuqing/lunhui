using Godot;
using Lunhui.Audio;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 游戏内的枢纽场景（占位美术）。C# 版，对位原 `src/ui/placeholder_game.gd`。
///
/// 它是「新建／读取存档之后落地的第一站」：在这里能查看当前会话状态、手动存档、
/// 进大地图探索（明雷／副本／战斗都从那里开始）、开角色与行囊，也能退回主菜单。
/// 真正的城镇系统（NPC、任务、商店街）还没做，所以这里只是一张精简的中转页。
///
/// **跨语言面**（P3 这批只搬界面，底下的服务还是原语言）：
///   · `Sfx`（`src/audio/Sfx.cs`）已是 C#，直调静态方法（`Sfx.cycle_volume()`）；
///   · `GameSession`／`GameData` 两个 autoload 已是 C#（2026-10-11 P3 第十七批）——读它们的
///     `state`／`db`／`last_battle` 照样要用 `Get(名字)`，**不是 `Call`**（属性不是方法，坑 67／89）。
/// </summary>
public partial class PlaceholderGame : Control
{
    public const string MENU_SCENE = "res://scenes/main_menu.tscn";
    public const string CHARACTER_SCENE = "res://scenes/character_screen.tscn";
    public const string WORLD_SCENE = "res://scenes/world_run.tscn";

    /// <summary>
    /// 返回时的回调（用例直接赋这个名字）。
    ///
    /// **类型是 `Callable`**：这个属性一度只能收 `Variant`——GDScript 的 lambda 是 `CallableCustom`，
    /// 赋进 C# 的 `Callable`（只装得下「GodotObject ＋ 方法名」或「C# 委托」）会**静默变成空 Callable**，
    /// 所以当年得靠 `Variant` ＋ `CallableInvoker` 那座桥绕过去。P5 起 GDScript 已清零、调用方全是 C#，
    /// 桥已退休，直接用 `Callable`。
    /// </summary>
    [Export] public Callable back_handler;

    /// <summary>存档设施（用例可注入临时目录）。类型放宽成 `GodotObject` 才能接 GDScript 桩（坑 86）。</summary>
    [Export] public GodotObject save_store_override = null;

    /// <summary>设置存储（自检同样注入临时目录）——GDScript 用例直接读写这个名字，别改名。</summary>
    [Export] public GodotObject _settings_override = null;

    private VBoxContainer _lines;
    private Label _notice;
    private Button _volume_button;
    /// <summary>自动战斗开关（设计 11 §四）：持久设置，落 settings.cfg，与音量同一处入口。</summary>
    private Button _auto_battle_button;

    public override void _Ready() => setup();

    /// <summary>幂等，用例可以直接调。</summary>
    public void setup()
    {
        if (_lines != null)
        {
            return;
        }

        _build_ui();
        _fill_state();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            press_back();
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Tab)
            {
                press_character();
            }
            else if (key.Keycode == Key.Enter)
            {
                press_world();
            }
            else if (key.Keycode == Key.F5)
            {
                press_save();
            }
        }
    }

    private GodotObject _state() => _session_node("GameSession")?.Get("state").AsGodotObject();

    /// <summary>一句话反馈（存了没存上、存到哪一格）</summary>
    private void _set_headline(string text)
    {
        if (_notice != null)
        {
            _notice.Text = text;
        }
    }

    /// <summary>打开角色与行囊（三个页签）</summary>
    public void press_character() => _tree()?.ChangeSceneToFile(CHARACTER_SCENE);

    /// <summary>进入大地图开始探索（最短闭环的入口）</summary>
    public void press_world() => _tree()?.ChangeSceneToFile(WORLD_SCENE);

    /// <summary>手动存档（城镇/枢纽是存档点）。读档仍在主菜单，游戏内只做保存。</summary>
    public Godot.Collections.Dictionary press_save()
    {
        GodotObject state = _state();
        if (state == null)
        {
            _set_headline("没有会话状态，存不了");
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有会话状态" } };
        }

        GodotObject store = save_store_override;
        if (store == null)
        {
            store = SaveService.make_default();
        }

        var service = SaveService.create(store, state);
        Godot.Collections.Dictionary result = service.save("枢纽存档点", false);
        _set_headline(service.describe());
        return result;
    }

    public void press_back()
    {
        if (_callable_valid(back_handler))
        {
            _invoke_callback(back_handler);
            return;
        }

        _tree()?.ChangeSceneToFile(MENU_SCENE);
    }

    /// <summary>
    /// 音量：按一下轮换一档（静音／25%／50%／75%／100%），**当场生效**并写进设置文件。
    ///
    /// 为什么放在枢纽页：设计还没有设置界面／暂停菜单，而这里是**游戏内唯一随时到得了的菜单**
    /// （大地图按 Esc 就回这儿）。等设计给了正式设置界面，把这一行搬过去即可——`Sfx` 那边的接口不用动。
    /// </summary>
    public double press_volume()
    {
        double value = Sfx.cycle_volume();
        string text = Sfx.volume_text();
        if (_volume_button != null)
        {
            _volume_button.Text = text;
        }

        _set_headline($"音量：{text.Replace("音量：", "")}（已写入设置）");
        return value;
    }

    /// <summary>
    /// 自动战斗开关：按一下开／关，写进设置文件；**下次进战斗就按它自动打**。
    /// 「玩家点任意指令立刻接管」这条在战斗界面里实现（点一下指令就退出自动），
    /// 这里只管持久设置本身——省事，不夺权。
    /// </summary>
    public bool press_auto_battle()
    {
        GodotObject store = settings();
        bool current = _auto_battle_enabled(store);
        bool wanted = !current;
        Godot.Collections.Dictionary result = store.Call("save_auto_battle", wanted).AsGodotDictionary();
        if (_auto_battle_button != null)
        {
            _auto_battle_button.Text = auto_battle_text(wanted);
        }

        string state_cn = wanted ? "开" : "关";
        if (_flag(result, "ok"))
        {
            _set_headline($"自动战斗：{state_cn}（已写入设置）");
        }
        else
        {
            _set_headline($"自动战斗：{state_cn}（设置写不进去：{_error_text(result)}）");
        }

        return wanted;
    }

    public static string auto_battle_text(bool enabled) =>
        $"自动战斗：{(enabled ? "开" : "关")}（点指令即接管）";

    public GodotObject settings()
    {
        if (_settings_override != null)
        {
            return _settings_override;
        }

        _settings_override = new SettingsStore();
        return _settings_override;
    }

    public string[] state_lines()
    {
        Node session = _session_node("GameSession");
        GodotObject state = session?.Get("state").AsGodotObject();
        if (state == null)
        {
            return new[] { "没有会话状态：请回主菜单新建或读取存档" };
        }

        return state.Call("summary_lines", _resolve_db()).AsStringArray();
    }

    private void _build_ui()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer { Name = "Center" };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var column = new VBoxContainer
        {
            Name = "Column",
            CustomMinimumSize = new Vector2(460, 0),
        };
        column.AddThemeConstantOverride("separation", 8);
        center.AddChild(column);

        var title = new Label
        {
            Name = "Title",
            Text = "游戏内（占位场景）",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(title);

        var hint = new Label
        {
            Name = "Hint",
            Text = "这里是游戏内枢纽：进大地图可探索明雷、副本与战斗（Enter）",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        column.AddChild(hint);

        _lines = new VBoxContainer { Name = "StateLines" };
        _lines.AddThemeConstantOverride("separation", 4);
        column.AddChild(_lines);

        column.AddChild(_button("BackButton", "返回主菜单", press_back));
        column.AddChild(_button("CharacterButton", "角色与行囊（Tab）", press_character));
        column.AddChild(_button("WorldButton", "进入大地图（Enter）", press_world));
        // 存档点：设计 02「城镇：存档点，可自由存读」——占位场景就是城镇外的枢纽，给它一个手动存档
        column.AddChild(_button("SaveButton", "保存游戏（F5）", () => press_save()));

        // 音量：游戏有音效之后玩家得关得掉（音效层是复用的 MIT 播放层，音量走它的那条总线）
        _volume_button = _button("VolumeButton", Sfx.volume_text(), () => press_volume());
        column.AddChild(_volume_button);

        // 自动战斗开关（设计 11 §四）：与音量同处，持久设置
        _auto_battle_button = _button(
            "AutoBattleButton",
            auto_battle_text(_auto_battle_enabled(settings())),
            () => press_auto_battle());
        column.AddChild(_auto_battle_button);

        // 上一场战斗的结果（从战斗回来时给一句交代）
        Node session = _session_node("GameSession");
        Godot.Collections.Dictionary last_battle = session == null
            ? new Godot.Collections.Dictionary()
            : session.Get("last_battle").AsGodotDictionary();
        if (last_battle.Count > 0)
        {
            var summary = new Label
            {
                Name = "LastBattle",
                Text = $"上一场：{_text(last_battle, "team")}　{_text(last_battle, "summary")}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(460, 0),
            };
            column.AddChild(summary);
        }

        // 存档反馈（手动存档后写在这里）
        _notice = new Label
        {
            Name = "Notice",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
        };
        column.AddChild(_notice);
    }

    private static Button _button(string node_name, string text, System.Action handler)
    {
        var button = new Button
        {
            Name = node_name,
            Text = text,
            CustomMinimumSize = new Vector2(460, 40),
        };
        button.Pressed += handler;
        return button;
    }

    private void _fill_state()
    {
        foreach (string line in state_lines())
        {
            _lines.AddChild(new Label { Text = "· " + line });
        }
    }

    private GodotObject _resolve_db()
    {
        GodotObject game_data = _session_node("GameData");
        GodotObject db = game_data?.Get("db").AsGodotObject();
        if (db != null && db.Get("tables").AsGodotDictionary().Count > 0)
        {
            return db;
        }

        var fresh = new TableDb();
        fresh.load_all();
        return fresh;
    }

    /// <summary>同 main_menu：`--script` 模式下节点不在活动场景树里，绝对路径不可用。</summary>
    private Node _session_node(string node_name) => _tree()?.Root.GetNodeOrNull(node_name);

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

    private static bool _auto_battle_enabled(GodotObject store)
    {
        Godot.Collections.Dictionary data = store.Call("load_auto_battle").AsGodotDictionary();
        return data.ContainsKey("enabled") && data["enabled"].AsBool();
    }

    /// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
    private static string _text(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static bool _flag(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _error_text(Godot.Collections.Dictionary dict) =>
        dict.ContainsKey("error") ? dict["error"].AsString() : "";

    /// <summary>
    /// 这个回调接上没有。
    ///
    /// 判据：`Callable.From(…)` 造出来的委托型 Callable 看 `Delegate`；
    /// 「GodotObject ＋ 方法名」那种看 `Target`／`Method`（同 `LocalMapController._callable_valid`）。
    /// 委托型 Callable 的 `Target` 是 `null`、`Method` 取值还会抛，所以不能只判后一种。
    /// </summary>
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
}
