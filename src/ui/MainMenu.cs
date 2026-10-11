using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 启动菜单：新建游戏 / 读取存档 / 退出游戏。C# 版，对位原 `src/ui/main_menu.gd`（353 行）。
///
/// 界面用代码搭（占位美术阶段的取舍），决策都在 `MenuController` 里，
/// 所以菜单流程既被 `tests/TestMenu.cs` 覆盖，也能用 `--menu-selftest`
/// 在真实场景里整条跑一遍（见 docs/dev/框架说明.md）。
///
/// **跨语言面**：`GameState`／`GameSession`／`GameData` 都已是 C#，直调
/// （`GameState.default_party_ids_from_recruit(…)` 已换成静态口）。
/// 两个回调（`quit_handler`／`scene_switch_handler`）收 `Callable`
/// （P5 起 GDScript 已清零，不再需要跨语言桥）。
///
/// **常量**：原 `.gd` 的 `CREATION_SCENE` 那几个 `const` 从 GDScript 读不到（坑⑩），
/// 于是三个读它的地方（`tests/TestMenu.cs:192/231`、`tools/CheckLoopCs.cs:287`）
/// 改成各自的本地常量；这几个常量本身照样留在 C# 侧给内部用。
/// </summary>
public partial class MainMenu : Control
{
    public const string PLACEHOLDER_SCENE = "res://scenes/placeholder_game.tscn";
    public const string OVERWORLD_SCENE = "res://scenes/world_run.tscn";

    /// <summary>
    /// 创建角色（设计 13）：**新建游戏先过创建界面**——「创建没走完不写存档」，
    /// 所以这一步只切场景，落盘由创建界面确认时调 `MenuController.new_game(spec)` 做。
    /// </summary>
    public const string CREATION_SCENE = "res://scenes/creation_screen.tscn";

    public const string BUTTON_NEW = "新建游戏";
    public const string BUTTON_LOAD = "读取存档";
    public const string BUTTON_QUIT = "退出游戏";

    /// <summary>自检注入点。都不注入时走真实实现：写 user://saves、切场景、退出进程。</summary>
    [Export] public GodotObject store_override = null;

    /// <summary>退出回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable quit_handler;

    /// <summary>场景切换回调（用例靠它拦下切场景）。同上，类型是 `Callable`。</summary>
    [Export] public Callable scene_switch_handler;

    /// <summary>
    /// 进游戏要切到哪个场景（`_enter_game` 写；用例注入场景切换回调时靠它验证落点）。
    /// 设计 09 §二：新建走占位枢纽页，读档一律回大地图。
    /// </summary>
    [Export] public string pending_scene = PLACEHOLDER_SCENE;

    /// <summary>`MenuController`（C# 类，但要能从 GDScript 写 null 重建，所以收成 `GodotObject`）。</summary>
    [Export] public GodotObject controller = null;

    private Label _status;
    private VBoxContainer _slot_box;
    private Button _new_button;
    private Button _load_button;
    private Button _quit_button;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--menu-selftest"))
        {
            Callable.From(_run_menu_selftest).CallDeferred();
        }
    }

    /// <summary>搭界面并接上控制器。幂等，用例可以直接调（不依赖引擎是否已发 `_ready`）。</summary>
    public void setup()
    {
        if (controller != null)
        {
            return;
        }

        TableDb db = _resolve_db();
        string[] party_ids = GameState.default_party_ids_from_recruit(db);
        controller = MenuController.create(db, _make_store(), party_ids, "normal");
        _build_ui();
        _refresh();
    }

    /// <summary>场景与逻辑的连接点，方便用例直接调。</summary>
    public TableDb resolve_db() => _resolve_db();

    /// <summary>新建游戏：造状态 → 落在存档槽里 → 进入游戏。</summary>
    public Godot.Collections.Dictionary press_new_game()
    {
        // 设计 13：新建游戏走创建角色流程（选出身／分七维 → 天赋 → 起名 → 确认）。
        // 这一步**不建号、不落盘**——玩家中途退出就什么都不留。
        if (_callable_valid(scene_switch_handler))
        {
            _invoke_callback(scene_switch_handler, CREATION_SCENE);
        }
        else
        {
            _tree()?.ChangeSceneToFile(CREATION_SCENE);
        }

        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "action", MenuController.action_none() },
            { "slot", 0 },
            { "state", default(Variant) },
            { "message", "去创建角色" },
        };
    }

    /// <summary>读取存档：展开存档列表；没有存档时给出提示。</summary>
    public Godot.Collections.Dictionary press_load_game()
    {
        _slot_box.Visible = !_slot_box.Visible;
        if (!controller.Call("can_load").AsBool())
        {
            _set_status("暂无存档可以读取，先「新建游戏」");
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "action", MenuController.action_none() },
                { "message", status_text() },
            };
        }

        _set_status($"选择要读取的存档（共 {controller.Call("save_count").AsInt32()} 份）");
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "action", "list" },
            { "message", status_text() },
        };
    }

    /// <summary>读取指定槽位。</summary>
    public Godot.Collections.Dictionary press_slot(int slot)
    {
        Godot.Collections.Dictionary result = controller.Call("load_game", slot).AsGodotDictionary();
        _handle(result);
        return result;
    }

    /// <summary>退出游戏：真实实现会结束进程。</summary>
    public void press_quit()
    {
        if (_callable_valid(quit_handler))
        {
            _invoke_callback(quit_handler);
            return;
        }

        SceneTree tree = _tree();
        if (tree == null)
        {
            GD.PushError("[MainMenu] 没有场景树，无法退出");
            return;
        }

        tree.Quit();
    }

    public string status_text() => _status != null ? _status.Text : "";

    public Button slot_button(int slot) => _slot_box?.GetNodeOrNull($"SlotButton{slot}") as Button;

    private void _handle(Godot.Collections.Dictionary result)
    {
        string message = _s(result, "message");
        if (message.Length > 0)
        {
            _set_status(message);
        }

        if (_s(result, "action") != MenuController.action_play())
        {
            return;
        }

        // 设计 09 §二：读档一律回大地图（`load_game` 在结果里写 `start_scene`），
        // 新建游戏照旧进占位枢纽页。
        string start_scene = _s(result, "start_scene") == MenuController.start_overworld()
            ? OVERWORLD_SCENE
            : PLACEHOLDER_SCENE;
        _enter_game(_v(result, "state"), start_scene);
    }

    private void _enter_game(Variant state, string start_scene = PLACEHOLDER_SCENE)
    {
        GodotObject session = _session();
        if (session != null)
        {
            session.Call("set_state", state);
        }

        // 落点交给视图：用例注入 scene_switch_handler 时不切场景，但可以读这个字段验证落点
        pending_scene = start_scene;
        if (_callable_valid(scene_switch_handler))
        {
            _invoke_callback(scene_switch_handler, state);
            return;
        }

        _tree()?.ChangeSceneToFile(start_scene);
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
            CustomMinimumSize = new Vector2(440, 0),
        };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);

        // 标题档 24（设计 15 §4.4：A11 原先的「标题 36px」按 Q82 改成 24——1× 体系里 36 会顶到布局，
        // 也超出一套 12px 字集的可用倍率）。以前这里停在 36，是没跟上那条修订。
        var title = new Label
        {
            Name = "Title",
            Text = "《轮回》",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        column.AddChild(title);

        // 副标题只说「这一版能做什么」。别写成自我否定的一句话——大地图、明雷、战斗、
        // 行囊都已经接进游戏了，玩家的第一句话不能是谎话（tests/TestCopyAudit.cs 会盯着）。
        var subtitle = new Label
        {
            Name = "Subtitle",
            Text = "第一章「黑风寨」　大地图・明雷・战斗・行囊",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        column.AddChild(subtitle);

        _new_button = _make_button("NewGameButton", BUTTON_NEW, _on_new_game_pressed);
        column.AddChild(_new_button);
        _load_button = _make_button("LoadGameButton", BUTTON_LOAD, _on_load_pressed);
        column.AddChild(_load_button);

        _slot_box = new VBoxContainer { Name = "SlotList" };
        _slot_box.AddThemeConstantOverride("separation", 4);
        column.AddChild(_slot_box);

        _quit_button = _make_button("QuitButton", BUTTON_QUIT, _on_quit_pressed);
        column.AddChild(_quit_button);

        _status = new Label
        {
            Name = "Status",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(440, 48),
            VerticalAlignment = VerticalAlignment.Top,
        };
        column.AddChild(_status);
    }

    private static Button _make_button(string node_name, string text, System.Action handler)
    {
        var button = new Button
        {
            Name = node_name,
            Text = text,
            CustomMinimumSize = new Vector2(440, 44),
        };
        button.Pressed += handler;
        return button;
    }

    /// <summary>刷界面。**必须是 public**：`tests/TestMenu.cs` 直接调 `menu._refresh()` 重建槽位列表。</summary>
    public void _refresh()
    {
        // 先让控制器重读存档目录，再刷界面，避免按钮状态停留在上一次
        controller.Call("refresh");
        _refresh_slots();
        bool can_new = controller.Call("can_start_new_game").AsBool();
        bool can_load = controller.Call("can_load").AsBool();
        _new_button.Disabled = !can_new;
        _load_button.Disabled = !can_load;
        if (!can_new)
        {
            _set_status("配置表未就绪：先运行 tools\\run_tests.bat 生成 data/generated");
        }
        else if (!can_load)
        {
            _set_status("暂无存档，先「新建游戏」");
        }
        else
        {
            _set_status($"已有 {controller.Call("save_count").AsInt32()} 份存档，点「读取存档」选择");
        }
    }

    private void _refresh_slots()
    {
        foreach (Node child in _slot_box.GetChildren())
        {
            // 先摘再 queue_free（同 creation_screen._rebuild_body 的注释）：名字要立刻可复用
            _slot_box.RemoveChild(child);
            child.QueueFree();
        }

        string[] labels = controller.Call("slot_labels").AsStringArray();
        Godot.Collections.Array slots = controller.Get("slots").AsGodotArray();
        for (int index = 0; index < slots.Count; index++)
        {
            Godot.Collections.Dictionary entry = slots[index].AsGodotDictionary();
            int slot = _i(entry, "slot");
            var button = new Button
            {
                Name = $"SlotButton{slot}",
                Text = index < labels.Length ? labels[index] : "",
                CustomMinimumSize = new Vector2(440, 32),
                Disabled = !_b(entry, "exists") || _b(entry, "corrupt"),
            };
            int captured = slot;
            button.Pressed += () => _on_slot_pressed(captured);
            _slot_box.AddChild(button);
        }
    }

    private void _on_new_game_pressed() => press_new_game();

    private void _on_load_pressed() => press_load_game();

    private void _on_slot_pressed(int slot) => press_slot(slot);

    private void _on_quit_pressed() => press_quit();

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    private GodotObject _make_store()
    {
        if (store_override != null)
        {
            return store_override;
        }

        return SaveStore.create(SaveStore.default_dir(), SaveStore.default_slot_count());
    }

    private TableDb _resolve_db()
    {
        GodotObject game_data = _session_node("GameData");
        TableDb db = game_data?.Get("db").AsGodotObject() as TableDb;
        if (db != null && db.tables.Count > 0)
        {
            return db;
        }

        var fresh = new TableDb();
        fresh.load_all();
        return fresh;
    }

    /// <summary>
    /// 取 autoload 节点。
    ///
    /// 不能用 `get_node("/root/X")`：`--script` 模式下的节点还没进入活动场景树，
    /// 绝对路径与 get_tree() 都会失效，所以统一从主循环的 root 相对查找。
    /// </summary>
    private Node _session_node(string node_name) => _root_node()?.GetNodeOrNull(node_name);

    private Node _session() => _session_node("GameSession");

    private Node _root_node() => _tree()?.Root;

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

    /// <summary>真实场景自检：不注入任何东西，走一遍新建 → 落盘 → 枚举 → 读取 → 退出动作。</summary>
    private async void _run_menu_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();

        Godot.Collections.Dictionary created =
            controller.Call("new_game", new Godot.Collections.Dictionary()).AsGodotDictionary();
        bool new_ok = _b(created, "ok") && _s(created, "action") == MenuController.action_play();
        ok = ok && new_ok;
        int slot = _i(created, "slot");
        lines.Add($"新建游戏 ok={_bool(new_ok)} 槽={slot} {_s(created, "message")}");
        controller.Call("refresh");

        GodotObject store = controller.Call("store").AsGodotObject();
        bool saved = slot > 0 && store.Call("slot_exists", slot).AsBool();
        ok = ok && saved;
        lines.Add($"落盘 ok={_bool(saved)} 路径={store.Call("slot_path", slot).AsString()}");

        bool listed = false;
        foreach (Variant entry_variant in store.Call("list_slots", _resolve_db()).AsGodotArray())
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_i(entry, "slot") == slot && _b(entry, "exists") && !_b(entry, "corrupt"))
            {
                listed = true;
            }
        }

        ok = ok && listed;
        lines.Add($"枚举 ok={_bool(listed)} 存档数={controller.Call("save_count").AsInt32()}");

        Godot.Collections.Dictionary loaded = controller.Call("load_game", slot).AsGodotDictionary();
        bool load_ok = _b(loaded, "ok") && _s(loaded, "action") == MenuController.action_play();
        bool same = false;
        if (load_ok)
        {
            GodotObject state = _v(loaded, "state").AsGodotObject();
            same = state != null && state.Call("party_size").AsInt32() > 0
                && state.Get("slot").AsInt32() == slot;
        }

        ok = ok && load_ok && same;
        lines.Add($"读取 ok={_bool(load_ok)} 队伍与槽位一致={_bool(same)}");

        Godot.Collections.Dictionary quit_result = controller.Call("quit_game").AsGodotDictionary();
        bool quit_ok = _s(quit_result, "action") == MenuController.action_quit();
        ok = ok && quit_ok;
        lines.Add($"退出动作 ok={_bool(quit_ok)}");

        // 版式预算：整页最小高度要塞得进设计分辨率
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
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

        GD.Print($"MENU SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

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
    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
