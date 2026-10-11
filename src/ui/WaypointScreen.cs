using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 驿站界面：快速传送 + 切换难度（03_副本_黑风寨.md：难度可在大地图任意驿站切换）。
/// C# 版，对位原 `src/ui/waypoint_screen.gd`。
///
/// 骨架在 `scenes/waypoint_screen.tscn`（节点名即接口），规则在 `src/core/WorldMapService.cs`。
/// 入口：大地图上走到 `Markers/Node_n_post_station`（驿站）旁边按 E。
/// 自检：`-- --waypoint-selftest`（推进到驿站 → 传送目标 → 切难度 → 显示条件未达成的原因）。
///
/// **跨语言面**：`world_map_service` 已是 C#（`src/core/WorldMapService.cs`）——建实例走
/// `WorldMapService.create(db, state)`、方法直调；还剩下的 `db`／`state` 两个字段仍是 `GodotObject`
/// （`GameData`／`GameState` 都在 `src/` 里，类型上够用，`Get`／`Set` 留着）。
/// 两个回调（`return_handler`／`travel_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// </summary>
public partial class WaypointScreen : CanvasLayer
{
    [Export] public Variant state_override = default;

    [Export] public Callable return_handler;

    /// <summary>传送落点：调用方（大地图控制器）接过去切场景；没接就只写会话里的待进入场景</summary>
    [Export] public Callable travel_handler;

    /// <summary>当前所在小地图（用于把「你在这里」那一项灰掉）</summary>
    [Export] public string current_scene_id = "";

    [Export] public GodotObject db;
    [Export] public GodotObject state;
    public WorldMapService world_map;

    private Label _title;
    private Label _progress;
    private VBoxContainer _list;
    private Label _status;
    private Button _return_button;
    private bool _bound;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--waypoint-selftest"))
        {
            Callable.From(_run_waypoint_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (world_map != null)
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

        world_map = WorldMapService.create((TableDb)db, state);
        world_map.apply_initial_reveals();
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

    public string progress_text() => _progress != null ? _progress.Text : "";

    private void _bind_ui()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        _title = _require_node("Panel/Margin/Column/Title") as Label;
        _progress = _require_node("Panel/Margin/Column/Progress") as Label;
        _list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer;
        _status = _require_node("Panel/Margin/Column/Status") as Label;
        _return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button;
        _return_button.Pressed += press_return;
        _status.Text = "";
    }

    private Node _require_node(string path)
    {
        Node node = GetNodeOrNull(path);
        if (node == null)
        {
            // 场景骨架是「节点名即接口」，缺一个就整页画不出来——报出来，别静默画半张
            GD.PushError($"waypoint_screen.tscn 缺少节点：{path}");
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
        if (world_map == null || _list == null)
        {
            return;
        }

        _title.Text = "驿站　（传送与难度切换）";
        _progress.Text = world_map.progress_text();
        _clear(_list);

        // 传送目标
        _list.AddChild(_make_label("TravelHeader", "去哪里", 18));
        Godot.Collections.Array targets = world_map.travel_targets(current_scene_id);
        if (targets.Count == 0)
        {
            _list.AddChild(_make_label("NoTravel", "还没有探索到别的地方（走远一点，地图是自己走出来的）"));
        }

        foreach (Variant entry_variant in targets)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string node_id = _s(entry, "node_id");
            var line = new HBoxContainer { Name = "TravelRow" + node_id };
            line.AddThemeConstantOverride("separation", 8);
            bool here = _b(entry, "current");
            Label label = _make_label(
                "TravelLabel" + node_id,
                $"{_s(entry, "name")}{(here ? "（你在这里）" : "")}");
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            string target_id = node_id;
            line.AddChild(_make_button("TravelButton" + node_id, "传送", () => press_travel(target_id), here));
            _list.AddChild(line);
        }

        // 难度
        _list.AddChild(_make_label("DifficultyHeader", "难度（驿站才能改）", 18));
        foreach (Variant row_variant in world_map.difficulty_rows())
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            string difficulty_id = _s(row, "difficulty_id");
            var line = new HBoxContainer { Name = "DifficultyRow" + difficulty_id };
            line.AddThemeConstantOverride("separation", 8);
            string text = $"{_s(row, "name")}（{_s(row, "condition")}）";
            bool current = _b(row, "current");
            bool ok = _b(row, "ok");
            if (current)
            {
                text += "　▶ 当前";
            }
            else if (!ok)
            {
                text += $"　— {_s(row, "error")}";
            }

            Label label = _make_label("DifficultyLabel" + difficulty_id, text);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            string wanted_id = difficulty_id;
            line.AddChild(_make_button(
                "DifficultyButton" + difficulty_id, "切换",
                () => press_difficulty(wanted_id),
                current || !ok));
            _list.AddChild(line);
        }
    }

    private static void _clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static Label _make_label(string node_name, string text, int font_size = 0)
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

        return label;
    }

    private static Button _make_button(string node_name, string text, System.Action pressed, bool disabled = false)
    {
        var button = new Button
        {
            Name = node_name,
            Text = text,
            Disabled = disabled,
        };
        button.Pressed += pressed;
        return button;
    }

    // ------------------------------------------------------------------ 操作

    /// <summary>传送：界面不自己切场景，交给调用方（大地图控制器）——它还要把玩家位置写回会话</summary>
    public Godot.Collections.Dictionary press_travel(string node_id)
    {
        Godot.Collections.Dictionary result = world_map.travel(node_id, current_scene_id);
        if (!_b(result, "ok"))
        {
            show_message(_s(result, "error"));
            return result;
        }

        Node session_node = _session_node();
        if (_callable_valid(travel_handler))
        {
            _invoke_callback(travel_handler, _v(result, "scene_id"), _v(result, "name"));
        }
        else if (session_node != null)
        {
            session_node.Set("pending_local_scene", _v(result, "scene_id"));
        }

        show_message($"出发去 {_s(result, "name")}");
        return result;
    }

    public Godot.Collections.Dictionary press_difficulty(string difficulty_id)
    {
        Godot.Collections.Dictionary result = world_map.switch_difficulty(difficulty_id);
        show_message(_b(result, "ok")
            ? $"难度已切到 {_s(result, "name", difficulty_id)}"
            : _s(result, "error"));
        refresh();
        return result;
    }

    // ------------------------------------------------------------------ 环境

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

    // ------------------------------------------------------------------ 自检

    private async void _run_waypoint_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        state.Call("reveal_node", "n_qingfengyi");
        state.Call("reveal_node", "n_luoyanpo");
        refresh();
        lines.Add($"地图：{progress_text()}");

        // 1. 传送目标：已揭开且有可进入小地图的才列出来
        // （落雁坡是大地图上的野外区域，enter_scene 是空的，不算「传送目的地」）
        Godot.Collections.Array targets = world_map.travel_targets("");
        var names = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in targets)
        {
            names.Add(_s(entry_variant.AsGodotDictionary(), "name"));
        }

        bool travel_ok = names.Contains("清风驿") && !names.Contains("落雁坡") && !names.Contains("黑风寨");
        ok = ok && travel_ok;
        lines.Add($"传送目标 ok={_bool(travel_ok)}（{string.Join("、", names)}；未探索的与野外区域都不列）");

        // 2. 没揭开的不能传
        Godot.Collections.Dictionary blocked = world_map.travel("n_huangcun", "");
        ok = ok && !_b(blocked, "ok") && _s(blocked, "error").Contains("还没探索");
        lines.Add($"未探索拦截 ok={_bool(!_b(blocked, "ok"))}（{_s(blocked, "error")}）");

        // 3. 难度：普通随便切；困难／绝境要打过 Boss
        Godot.Collections.Dictionary easy = press_difficulty("normal");
        ok = ok && _b(easy, "ok");
        Godot.Collections.Dictionary hard = press_difficulty("hard");
        ok = ok && !_b(hard, "ok") && _s(hard, "error").Contains("大寨主");
        lines.Add($"难度门槛 ok={_bool(!_b(hard, "ok"))}（困难：{_s(hard, "error")}）");
        state.Call("record_dungeon", "scene_heifengzhai", "bosses", "en_bd_boss");
        Godot.Collections.Dictionary hard_now = press_difficulty("hard");
        ok = ok && _b(hard_now, "ok");
        Godot.Collections.Dictionary nightmare = press_difficulty("nightmare");
        ok = ok && !_b(nightmare, "ok") && _s(nightmare, "error").Contains("醉刀客");
        lines.Add(
            $"击败大寨主后 ok={_bool(_b(hard_now, "ok"))}（困难可切={_bool(_b(hard_now, "ok"))}；"
            + $"绝境还差：{_s(nightmare, "error")}）");
        state.Call("record_dungeon", "scene_heifengzhai", "bosses", "en_hidden_drunk");
        Godot.Collections.Dictionary nightmare_now = press_difficulty("nightmare");
        ok = ok && _b(nightmare_now, "ok");
        lines.Add($"击败醉刀客后绝境可切={_bool(_b(nightmare_now, "ok"))}（当前难度 {state.Get("difficulty_id").AsString()}）");

        // 版式预算：整页最小高度要塞得进设计分辨率
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        ok = ok && LayoutBudget.has_opaque_backdrop(this);
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        // 滚动区内容宽度：横滚是关着的，目的地一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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

        GD.Print($"WAYPOINT SELF-TEST: {(ok ? "OK" : "FAILED")}");
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

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
