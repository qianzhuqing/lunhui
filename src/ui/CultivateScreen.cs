using System.Globalization;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// 打坐修炼界面（客栈／门派）。C# 版，对位原 `src/ui/cultivate_screen.gd`（381 行）。
///
/// 骨架在 `scenes/cultivate_screen.tscn` 里（节点名即接口），规则与记账在 `src/core/MasteryService.cs`：
/// 一次打坐 = 熟练度 +1，费用 = `skill_star_def.cultivate_cost_base × growth_const.cultivate_cost_growth^(熟练度-1)`。
///
/// 入口：清风驿的 `Markers/Facilities/facility_inn` 旁边按 E。
/// 自检：`-- --cultivate-selftest`（打坐一次 → 扣钱 → 熟练度 +1 → 倍率上升，再看满级与缺钱的拒绝）。
///
/// **跨语言面**：`GameState` 已是 C#（2026-10-11）——建实例走静态口 `GameState.new_game(…)`，
/// `learned_of`／`char_name`／`set_mastery`／`inventory.money` 这些实例口仍走 `Call`；
/// 打坐余韵挂在 `GameSession` 上（`add_field_buff`／`active_field_buffs`；2026-10-11 已搬 C#）
/// 走 `Call`，**时钟参数要显式传**（`active_field_buffs(-1)`＝用当前时间）——C# 的默认参数对 `Call` 不生效。
/// `MasteryService`／`GrowthCalculator`／`BuffService`／`TableDb` 已是 C#，直调。
/// 回调（`return_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// </summary>
public partial class CultivateScreen : CanvasLayer
{
    [Export] public Variant state_override = default;

    /// <summary>用例注入的表库（要在真场景里跑「4 人队伍」这种发行数据用不到的配置）</summary>
    [Export] public Variant db_override = default;

    /// <summary>返回回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable return_handler;

    /// <summary>当前在给谁打坐（多人队伍时切换）。用例直接读它比对切人有没有串号。</summary>
    [Export] public string selected_char = "";

    [Export] public GodotObject db;
    [Export] public GodotObject state;

    /// <summary>`MasteryService`。**用例直接读这个名字**（`screen.mastery.mastery_of(…)`）。</summary>
    [Export] public GodotObject mastery;

    private GrowthCalculator _growth;
    private Label _title;
    private Label _money;
    private HBoxContainer _members;
    private VBoxContainer _list;
    private Label _status;
    private Button _return_button;
    private bool _bound;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--cultivate-selftest"))
        {
            Callable.From(_run_cultivate_selftest).CallDeferred();
        }
    }

    public void setup()
    {
        if (mastery != null)
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

        mastery = MasteryService.create((TableDb)db, state);
        _growth = GrowthCalculator.create((TableDb)db);
        Godot.Collections.Array char_ids = state.Get("char_ids").AsGodotArray();
        if (selected_char.Length == 0 && char_ids.Count > 0)
        {
            selected_char = char_ids[0].AsString();
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

    public string money_text() => _money != null ? _money.Text : "";

    public string char_name() =>
        state != null ? state.Call("char_name", db, selected_char).AsString() : "";

    // ------------------------------------------------------------------ 界面

    private void _bind_ui()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        _title = _require_node("Panel/Margin/Column/Title") as Label;
        _money = _require_node("Panel/Margin/Column/Money") as Label;
        _members = _require_node("Panel/Margin/Column/Members") as HBoxContainer;
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
            GD.PushError($"cultivate_screen.tscn 缺少节点：{path}");
        }

        return node;
    }

    public void select_char(string char_id)
    {
        selected_char = char_id;
        refresh();
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

    public void show_message(string text) => _set_status(text);

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }

    public void refresh()
    {
        if (mastery == null || _list == null)
        {
            return;
        }

        TableDb table_db = (TableDb)db;
        _title.Text = $"打坐修炼　（{char_name()}）";
        int money = state.Get("inventory").AsGodotObject().Get("money").AsInt32();
        _money.Text = $"铜钱 {money}　｜　一次打坐 = 熟练度 +1（上限 {mastery.Call("mastery_max").AsInt32()}）";
        _rebuild_members();
        _clear(_list);
        string[] learned = state.Call("learned_of", selected_char).AsStringArray();
        if (learned.Length == 0)
        {
            _list.AddChild(_make_label("Empty", "还没学会任何武学"));
            return;
        }

        int shown = 0;
        foreach (string skill_id in learned)
        {
            Resource row = table_db.get_row("skill_base", skill_id);
            if (row == null)
            {
                continue;
            }

            shown += 1;
            int star_id = row.Get("star").AsInt32();
            Godot.Collections.Dictionary check =
                mastery.Call("can_train", selected_char, skill_id).AsGodotDictionary();
            Resource star = _growth.star_row(star_id);
            var line = new HBoxContainer { Name = "TrainRow" + skill_id };
            line.AddThemeConstantOverride("separation", 8);
            string star_name = star != null ? star.Get("name_cn").AsString() : star_id.ToString();
            string kind = row.Call("is_active").AsBool() ? "招式" : "内功";
            string text = $"{row.Get("name_cn").AsString()}（{star_name}★ {kind}）"
                + $"　熟练 {_i(check, "mastery")}/{_i(check, "max")}"
                + $"　倍率 ×{_f2(_growth.mastery_multiplier(star_id, _i(check, "mastery")))}"
                + $"　打坐 {_i(check, "cost")} 文";
            if (!_b(check, "ok"))
            {
                text += $"　— {_s(check, "error")}";
            }

            Label label = _make_label(
                "TrainLabel" + skill_id, text, 0, star != null ? star.Get("color").AsString() : "");
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);
            string captured = skill_id;
            line.AddChild(_make_button(
                "TrainButton" + skill_id, "打坐", () => press_train(captured), !_b(check, "ok")));
            _list.AddChild(line);
        }

        if (shown == 0)
        {
            _list.AddChild(_make_label("Empty", "已学武学都查不到表数据"));
        }
    }

    private void _rebuild_members()
    {
        _clear(_members);
        foreach (Variant char_variant in state.Get("char_ids").AsGodotArray())
        {
            string char_id = char_variant.AsString();
            string name = state.Call("char_name", db, char_id).AsString();
            string prefix = char_id == selected_char ? "▶ " : "";
            string captured = char_id;
            _members.AddChild(_make_button(
                "CultivateMemberButton" + char_id, prefix + name, () => select_char(captured)));
        }
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

        if (color.Length > 0)
        {
            label.AddThemeColorOverride("font_color", new Color(color));
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

    // ------------------------------------------------------------------ 操作

    public Godot.Collections.Dictionary press_train(string skill_id)
    {
        Godot.Collections.Dictionary result = mastery.Call("train", selected_char, skill_id).AsGodotDictionary();
        if (_b(result, "ok"))
        {
            _set_status(
                $"打坐：{mastery.Call("skill_name", skill_id).AsString()}"
                + $" 熟练度 {_i(result, "before")} → {_i(result, "mastery")}（花 {_i(result, "cost")} 文）"
                + $"　倍率 ×{_f2(_multiplier(skill_id, _i(result, "mastery")))}{_grant_meditation_buff()}");
        }
        else
        {
            _set_status($"打坐失败：{_s(result, "error")}");
        }

        refresh();
        return result;
    }

    /// <summary>
    /// 打坐的余韵（08：`buff_meditated` 是**战斗外增益**，按现实分钟计时；进战斗后维持整场）。
    /// 时长从 `buff_def.field_minutes` 读（不写死在这里），返回给玩家看的一段文案；
    /// 没开会话（用例／独立场景）就跳过——那是环境问题，不该让打坐失败。
    /// </summary>
    private string _grant_meditation_buff()
    {
        Node session_node = _session_node();
        if (session_node == null || !session_node.HasMethod("add_field_buff"))
        {
            return "";
        }

        BuffService service = BuffService.create((TableDb)db);
        int minutes = service.field_minutes_of("buff_meditated");
        if (minutes <= 0)
        {
            return "";
        }

        Godot.Collections.Dictionary applied =
            session_node.Call("add_field_buff", "buff_meditated", minutes, "", 1).AsGodotDictionary();
        if (!_b(applied, "ok"))
        {
            return "";
        }

        BuffDefRow row = service.def_of("buff_meditated");
        string name_cn = row != null ? row.name_cn : "打坐余韵";
        return $"　余韵：{name_cn} {minutes} 分钟（战斗外）";
    }

    private double _multiplier(string skill_id, int level) =>
        _growth.mastery_multiplier(mastery.Call("star_of", skill_id).AsInt32(), level);

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

    private async void _run_cultivate_selftest()
    {
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        _money_of_state().Set("money", 1000);
        refresh();
        string skill_id = "";
        foreach (string candidate in state.Call("learned_of", selected_char).AsStringArray())
        {
            if (((TableDb)db).get_row("skill_base", candidate) != null)
            {
                skill_id = candidate;
                break;
            }
        }

        ok = ok && skill_id.Length > 0;
        if (skill_id.Length == 0)
        {
            lines.Add("没有可打坐的武学");
        }

        int before = mastery.Call("mastery_of", selected_char, skill_id).AsInt32();
        int cost = mastery.Call("train_cost", selected_char, skill_id).AsInt32();
        int money_before = _money_of_state().Get("money").AsInt32();

        // 1. 打坐一次：扣钱、熟练度 +1、倍率上升
        Godot.Collections.Dictionary result = press_train(skill_id);
        int after = mastery.Call("mastery_of", selected_char, skill_id).AsInt32();
        bool trained_ok = _b(result, "ok") && after == before + 1
            && _money_of_state().Get("money").AsInt32() == money_before - cost;
        ok = ok && trained_ok;
        lines.Add(
            $"打坐 ok={_bool(trained_ok)}（{mastery.Call("skill_name", skill_id).AsString()}"
            + $" 熟练 {before} → {after}，花 {cost} 文，倍率 ×{_f2(_multiplier(skill_id, after))}）");

        // 打坐余韵（08）：打坐成功要真的挂上战斗外增益，时长来自 buff_def.field_minutes
        Node session_node = _session_node();
        Godot.Collections.Array field_rows = session_node != null
            ? session_node.Call("active_field_buffs", -1).AsGodotArray()
            : new Godot.Collections.Array();
        BuffDefRow buff_row = BuffService.create((TableDb)db).def_of("buff_meditated");
        int expect_minutes = buff_row != null ? buff_row.field_minutes : 0;
        bool field_ok = field_rows.Count == 1
            && _s(field_rows[0].AsGodotDictionary(), "buff_id") == "buff_meditated"
            && _i(field_rows[0].AsGodotDictionary(), "remaining_sec") > (expect_minutes - 1) * 60;
        ok = ok && field_ok;
        lines.Add(
            $"打坐余韵挂上={_bool(field_ok)}（{(buff_row != null ? buff_row.name_cn : "-")}，"
            + $"剩 {(field_rows.Count > 0 ? _i(field_rows[0].AsGodotDictionary(), "remaining_sec") / 60 : 0)} 分钟）");

        // 2. 缺钱要拒绝
        _money_of_state().Set("money", 0);
        Godot.Collections.Dictionary poor = press_train(skill_id);
        ok = ok && !_b(poor, "ok") && _s(poor, "error").Contains("铜钱");
        lines.Add($"缺钱拒绝 ok={_bool(!_b(poor, "ok"))}（{_s(poor, "error")}）");
        // 按钮态：不能打坐的时候要**置灰**，而不是「能点、点了才报错」
        Button poor_button = FindChild($"TrainButton{skill_id}", true, false) as Button;
        ok = ok && poor_button != null && poor_button.Disabled;
        lines.Add($"缺钱时按钮置灰={_bool(poor_button != null && poor_button.Disabled)}");
        _money_of_state().Set("money", 1000);

        // 3. 练满之后拒绝
        int top = mastery.Call("mastery_max").AsInt32();
        state.Call("set_mastery", selected_char, skill_id, top, top);
        refresh();
        Godot.Collections.Dictionary maxed = press_train(skill_id);
        ok = ok && !_b(maxed, "ok") && _s(maxed, "error").Contains("练满");
        lines.Add($"练满拒绝 ok={_bool(!_b(maxed, "ok"))}（{_s(maxed, "error")}）");
        Button maxed_button = FindChild($"TrainButton{skill_id}", true, false) as Button;
        Label maxed_label = FindChild($"TrainLabel{skill_id}", true, false) as Label;
        bool maxed_ui = maxed_button != null && maxed_button.Disabled
            && maxed_label != null && maxed_label.Text.Contains("练满");
        ok = ok && maxed_ui;
        lines.Add($"练满时按钮置灰且行里写「练满」={_bool(maxed_ui)}");

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

        GD.Print($"CULTIVATE SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

    private GodotObject _money_of_state() => state.Get("inventory").AsGodotObject();

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

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _bool(bool value) => value ? "true" : "false";
}
