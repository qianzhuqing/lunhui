using System.Globalization;
using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 副本完成度界面（03_副本_黑风寨.md「完成度」+「已通关层可扫荡」）。
/// C# 版，对位原 `src/ui/dungeon_screen.gd`（443 行）。
///
/// 骨架在 `scenes/dungeon_screen.tscn`，数据来自 `src/core/DungeonService.cs`：
///   四项完成度（宝箱／隐藏房间／隐藏 Boss／事件）—— 设计明说「不影响奖励，只作追求目标与炫耀项」
///   楼层列表：每层几个房间、清了几个、能不能扫荡；扫荡按钮直接把结果交给调用方
///
/// 入口：副本里按 M（`LocalMapController.open_dungeon_panel()`）。自检：`-- --dungeon-selftest`。
///
/// **跨语言面**：`GameState` 已是 C#（2026-10-11）——建实例走静态口 `GameState.new_game(…)`，
/// `record_dungeon`／`dungeon_record` 这些实例口仍走 `Call`（`state` 是跨语言句柄）。
/// 两个回调（`return_handler`／`sweep_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）；
/// `tests/TestDungeonUi.cs` 还会把 `sweep_handler` 显式置成空（`default`）来验「没有落点」那条分支。
/// </summary>
public partial class DungeonScreen : CanvasLayer
{
    [Export] public Variant state_override = default;

    /// <summary>用例注入的表库（要在真场景里跑「几十层」这种发行数据用不到的配置）</summary>
    [Export] public Variant db_override = default;

    [Export] public Callable return_handler;

    /// <summary>扫荡落点：由调用方（小地图控制器）接过去，界面不直接碰场景。类型是 `Callable`。</summary>
    [Export] public Callable sweep_handler;

    [Export] public string scene_id = "";

    [Export] public GodotObject db;
    [Export] public GodotObject state;
    [Export] public GodotObject dungeon;

    private Label _title;
    private Label _summary;
    private Label _detail;
    private VBoxContainer _list;
    private Label _status;
    private Button _return_button;
    private bool _bound;

    /// <summary>
    /// 扫荡动画：掉落**在按下时就结算**（逻辑不能等动画），动画只负责「看得见」——
    /// 进度条推进 + 逐间打勾，走完再把结果与完成度摆到界面上。
    /// 用「自己数时间」而不是 Tween：用例可以 `step_sweep_animation()` 精确推帧，不靠真实帧率。
    /// </summary>
    public const double SWEEP_ANIM_SECONDS = 0.6;

    private bool _sweeping;
    private int _sweep_floor = -1;
    private double _sweep_elapsed;
    private Godot.Collections.Dictionary _sweep_result = new();
    private string[] _sweep_rooms = System.Array.Empty<string>();
    private string _sweep_row_base_text = "";
    private ProgressBar _sweep_bar;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--dungeon-selftest"))
        {
            Callable.From(_run_dungeon_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (dungeon != null)
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

        dungeon = DungeonService.create((TableDb)db, state, RngService.create_random());
        if (scene_id.Length == 0)
        {
            scene_id = "scene_heifengzhai";
        }

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

    public string summary_text() => _summary != null ? _summary.Text : "";

    public string detail_text() => _detail != null ? _detail.Text : "";

    /// <summary>
    /// 楼层行列表（对位 GDScript 的 `_list.get_children()`）。
    /// `GetChildren()` 给的是**带类型的** `Array&lt;Node&gt;`，脚本 API 那边不收——这里转成无类型 Array。
    /// </summary>
    public Godot.Collections.Array floor_rows()
    {
        var out_rows = new Godot.Collections.Array();
        if (_list == null)
        {
            return out_rows;
        }

        foreach (Node child in _list.GetChildren())
        {
            out_rows.Add(child);
        }

        return out_rows;
    }

    private void _bind_ui()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        _title = _require_node("Panel/Margin/Column/Title") as Label;
        _summary = _require_node("Panel/Margin/Column/Summary") as Label;
        _detail = _require_node("Panel/Margin/Column/Detail") as Label;
        _list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer;
        _status = _require_node("Panel/Margin/Column/Status") as Label;
        _return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button;
        if (_return_button != null)
        {
            _return_button.Pressed += press_return;
        }

        if (_status != null)
        {
            _status.Text = "";
        }
    }

    private Node _require_node(string path)
    {
        Node node = GetNodeOrNull(path);
        if (node == null)
        {
            // 场景骨架是「节点名即接口」，缺一个就整页画不出来——报出来，别静默画半张
            GD.PushError($"dungeon_screen.tscn 缺少节点：{path}");
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
        if (dungeon == null || _list == null)
        {
            return;
        }

        TableDb table_db = (TableDb)db;
        Resource room = table_db.get_row("map_local", scene_id);
        if (room == null)
        {
            // 数据错：scene id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见决策 329）
            GD.PushError($"[dungeon] map_local 里没有这张图：{scene_id}");
        }

        _title.Text = $"{(room != null ? room.Get("name_cn").AsString() : "数据错，已记进日志")}　完成度";
        Godot.Collections.Dictionary snapshot = dungeon.Call("progress", scene_id).AsGodotDictionary();
        int percent = _i(snapshot, "percent");
        _summary.Text = $"总计 {percent}%　（{_i(snapshot, "done")} / {_i(snapshot, "total")} 项）"
            + $"{(percent >= 100 ? "　—— 全部完成！" : "")}";
        Godot.Collections.Dictionary chests = _d(snapshot, "chests");
        Godot.Collections.Dictionary hidden_rooms = _d(snapshot, "hidden_rooms");
        Godot.Collections.Dictionary hidden_boss = _d(snapshot, "hidden_boss");
        Godot.Collections.Dictionary events = _d(snapshot, "events");
        _detail.Text = $"宝箱 {_i(chests, "done")}/{_i(chests, "total")}"
            + $"　隐藏房间 {_i(hidden_rooms, "done")}/{_i(hidden_rooms, "total")}"
            + $"　隐藏 Boss {_i(hidden_boss, "done")}/{_i(hidden_boss, "total")}"
            + $"　事件 {_i(events, "done")}/{_i(events, "total")}";
        _clear(_list);
        foreach (Variant entry_variant in dungeon.Call("floors", scene_id).AsGodotArray())
        {
            _list.AddChild(_make_floor_row(entry_variant.AsGodotDictionary()));
        }
    }

    private Control _make_floor_row(Godot.Collections.Dictionary entry)
    {
        int floor_number = _i(entry, "floor");
        var line = new HBoxContainer { Name = $"FloorRow{floor_number}" };
        line.AddThemeConstantOverride("separation", 8);
        Godot.Collections.Array rooms = _v(entry, "rooms").AsGodotArray();
        Godot.Collections.Array enemy_rooms = _v(entry, "enemy_rooms").AsGodotArray();
        Godot.Collections.Array cleared = _v(entry, "cleared").AsGodotArray();
        bool sweepable = _b(entry, "sweepable");
        string text = $"第 {floor_number} 层　房间 {rooms.Count}（有敌人 {enemy_rooms.Count}，已清 {cleared.Count}）";
        if (enemy_rooms.Count == 0)
        {
            text += "　— 这层没有战斗房间";
        }
        else if (sweepable)
        {
            text += "　▶ 已通关，可扫荡";
        }
        else
        {
            var left = new System.Collections.Generic.List<string>();
            foreach (Variant room_variant in enemy_rooms)
            {
                if (!cleared.Contains(room_variant))
                {
                    left.Add(_room_name(room_variant.AsString()));
                }
            }

            text += $"　— 还差：{string.Join("、", left)}";
        }

        Label label = _make_label($"FloorLabel{floor_number}", text);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(label);
        int captured = floor_number;
        line.AddChild(_make_button(
            $"SweepButton{floor_number}", "扫荡（仅掉落）", () => press_sweep(captured), !sweepable));
        return line;
    }

    /// <summary>房间 id → 中文名（给玩家看的不是 hf2_hall 这种 id）</summary>
    private string _room_name(string room_id)
    {
        Resource row = ((TableDb)db).get_row("dungeon_room", room_id);
        return row != null ? row.Get("room_name").AsString() : room_id;
    }

    private static void _clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            // 先摘再 queue_free（同 creation_screen._rebuild_body 的注释）：名字要立刻可复用
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

    private static Button _make_button(
        string node_name, string text, System.Action pressed, bool disabled = false)
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

    /// <summary>扫荡：交给调用方（小地图控制器）执行，界面只负责显示结果</summary>
    public Godot.Collections.Dictionary press_sweep(int floor_number)
    {
        if (_sweeping)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "扫荡动画还没播完" } };
        }

        if (!_callable_valid(sweep_handler))
        {
            show_message("这里不能扫荡（要在大地图的副本里）");
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "no_handler" } };
        }

        Godot.Collections.Dictionary result = _invoke_callback(sweep_handler, floor_number).AsGodotDictionary();
        if (!_b(result, "ok"))
        {
            show_message($"扫荡第 {floor_number} 层：{_s(result, "error")}");
            return result;
        }

        _begin_sweep_animation(floor_number, result);
        return result;
    }

    // ------------------------------------------------------------------ 扫荡动画

    public bool is_sweeping() => _sweeping;

    /// <summary>0.0 ~ 1.0</summary>
    public double sweep_progress() => Mathf.Clamp(_sweep_elapsed / SWEEP_ANIM_SECONDS, 0.0, 1.0);

    /// <summary>
    /// 已经打勾的房间名（动画里逐间冒出来）。
    ///
    /// 四舍五入而不是向下取整：只有一间敌人房的层，打勾要出现在动画**中间**，
    /// 不然它会跟「动画结束→刷新列表」撞在同一帧，玩家根本看不见那一勾。
    /// </summary>
    public string[] sweep_ticked_rooms()
    {
        var out_rooms = new System.Collections.Generic.List<string>();
        if (_sweep_rooms.Length == 0)
        {
            return out_rooms.ToArray();
        }

        int count = (int)System.Math.Floor(sweep_progress() * _sweep_rooms.Length + 0.5);
        for (int index = 0; index < System.Math.Min(count, _sweep_rooms.Length); index++)
        {
            out_rooms.Add(_room_name(_sweep_rooms[index]));
        }

        return out_rooms.ToArray();
    }

    /// <summary>推进动画（真实运行由 `_Process` 调；用例直接调这个，不依赖帧率）</summary>
    public void step_sweep_animation(double delta)
    {
        if (!_sweeping)
        {
            return;
        }

        _sweep_elapsed += delta;
        _refresh_sweep_visuals();
        if (_sweep_elapsed >= SWEEP_ANIM_SECONDS)
        {
            _finish_sweep_animation();
        }
    }

    private void _begin_sweep_animation(int floor_number, Godot.Collections.Dictionary result)
    {
        _sweeping = true;
        _sweep_floor = floor_number;
        _sweep_elapsed = 0.0;
        _sweep_result = result;
        _sweep_rooms = _floor_enemy_rooms(floor_number);
        // 行文本先存下来：打勾是在它后面追加，刷新视觉时重新拼，免得越拼越长
        Label label = FindChild($"FloorLabel{floor_number}", true, false) as Label;
        _sweep_row_base_text = label != null ? label.Text : "";
        _build_sweep_bar();
        _set_sweep_buttons_disabled(true);
        show_message($"扫荡第 {floor_number} 层…（逐间清过去，只结算掉落）");
        _refresh_sweep_visuals();
    }

    private void _refresh_sweep_visuals()
    {
        if (_sweep_bar != null && IsInstanceValid(_sweep_bar))
        {
            _sweep_bar.Value = sweep_progress() * 100.0;
        }

        Label label = FindChild($"FloorLabel{_sweep_floor}", true, false) as Label;
        if (label == null)
        {
            return;
        }

        string[] ticked = sweep_ticked_rooms();
        label.Text = _sweep_row_base_text;
        if (ticked.Length > 0)
        {
            label.Text = $"{_sweep_row_base_text}　✓ {string.Join("、", ticked)}";
        }
    }

    private void _finish_sweep_animation()
    {
        _sweeping = false;
        _sweep_elapsed = SWEEP_ANIM_SECONDS;
        _sweep_bar = null;
        string summary = _sweep_result.ContainsKey("summary")
            ? _s(_sweep_result, "summary")
            : _s(_sweep_result, "error");
        show_message($"扫荡第 {_sweep_floor} 层：{summary}");
        // refresh() 会重建列表，进度条跟着被清掉（它是列表的第一个孩子）
        // 按钮的可用状态也由 refresh() 按各层自己的进度重算——别在这里一律打开，
        // 否则没通关的层会跟着变成可扫（踩过）。
        refresh();
    }

    /// <summary>第 N 层有敌人的房间（动画里一间一间打勾用的）</summary>
    private string[] _floor_enemy_rooms(int floor_number)
    {
        var out_rooms = new System.Collections.Generic.List<string>();
        if (dungeon == null)
        {
            return out_rooms.ToArray();
        }

        foreach (Variant entry_variant in dungeon.Call("floors", scene_id).AsGodotArray())
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_i(entry, "floor") != floor_number)
            {
                continue;
            }

            foreach (Variant room_variant in _v(entry, "enemy_rooms").AsGodotArray())
            {
                out_rooms.Add(room_variant.AsString());
            }
        }

        return out_rooms.ToArray();
    }

    private void _build_sweep_bar()
    {
        if (_sweep_bar != null && IsInstanceValid(_sweep_bar))
        {
            return;
        }

        if (_list == null)
        {
            return;
        }

        _sweep_bar = new ProgressBar
        {
            Name = "SweepProgress",
            MaxValue = 100.0,
            Value = 0.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 12),
        };
        // 放进滚动列表里而不是整个面板的底部：面板竖向已经排满，塞在 Column 末尾会被挤出屏幕
        _list.AddChild(_sweep_bar);
        _list.MoveChild(_sweep_bar, 0);
    }

    private void _set_sweep_buttons_disabled(bool disabled)
    {
        if (_list == null)
        {
            return;
        }

        foreach (Node row in _list.GetChildren())
        {
            foreach (Node child in row.GetChildren())
            {
                if (child is Button button && button.Name.ToString().StartsWith("SweepButton"))
                {
                    button.Disabled = disabled;
                }
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_sweeping)
        {
            step_sweep_animation(delta);
        }
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

    // ------------------------------------------------------------------ 自检

    private async void _run_dungeon_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        scene_id = "scene_heifengzhai";
        refresh();
        lines.Add($"摘要：{summary_text()}");
        lines.Add($"明细：{detail_text()}");
        Godot.Collections.Dictionary snapshot = dungeon.Call("progress", scene_id).AsGodotDictionary();
        ok = ok && _i(snapshot, "total") == 12 && _i(snapshot, "percent") == 0;
        ok = ok && floor_rows().Count == 3;
        lines.Add(
            $"楼层行数={floor_rows().Count}"
            + $"（可扫荡 {dungeon.Call("sweepable_floors", scene_id).AsGodotArray().Count} 层）");
        // 打通第一层之后：那一层变成可扫荡，按钮可点
        state.Call("record_dungeon", scene_id, "rooms", "hf1_yard");
        refresh();
        Button sweep_button = FindChild("SweepButton1", true, false) as Button;
        ok = ok && sweep_button != null && !sweep_button.Disabled;
        lines.Add($"第 1 层清完 → 扫荡按钮可点={_bool(sweep_button != null && !sweep_button.Disabled)}");
        // 扫荡走调用方：这里挂一个假 handler，确认界面把结果播出来
        var calls = new Godot.Collections.Array();
        sweep_handler = Callable.From<int, Godot.Collections.Dictionary>(floor_number =>
        {
            calls.Add(floor_number);
            return new Godot.Collections.Dictionary
            {
                { "ok", true },
                { "summary", "铜钱 +12　物品 铁矿石×1" },
                { "floor", floor_number },
            };
        });
        press_sweep(1);
        // 动画：按下就结算，进度与打勾走完才落结果
        ok = ok && calls.Count == 1 && is_sweeping();
        double mid_progress = sweep_progress();
        step_sweep_animation(SWEEP_ANIM_SECONDS * 0.5);
        ok = ok && sweep_progress() > mid_progress && sweep_ticked_rooms().Length > 0;
        lines.Add($"扫荡动画：进度 {_f2(sweep_progress())} → 打勾 {string.Join("、", sweep_ticked_rooms())}");
        step_sweep_animation(SWEEP_ANIM_SECONDS);
        ok = ok && !is_sweeping() && status_text().Contains("铜钱 +12");
        lines.Add($"扫荡回调={calls.Count} 次　状态栏={status_text()}");
        // 没通关的层按钮灰着并写清还差哪些房间
        Label floor2_label = FindChild("FloorLabel2", true, false) as Label;
        ok = ok && floor2_label != null && floor2_label.Text.Contains("还差");
        lines.Add($"第 2 层提示={(floor2_label != null ? floor2_label.Text : "（没有）")}");
        // 版式预算：整页最小高度要塞得进设计分辨率
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        ok = ok && LayoutBudget.has_opaque_backdrop(this);
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        // 滚动区内容宽度：横滚是关着的，一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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

        GD.Print($"DUNGEON SELF-TEST: {(ok ? "OK" : "FAILED")}");
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

    /// <summary>对位 GDScript 的 `%.2f`：**显式走不变文化**，别让小数点跟着系统区域跑。</summary>
    private static string _f2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;

    private static Godot.Collections.Dictionary _d(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotDictionary() : new Godot.Collections.Dictionary();

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
