using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的**行动顺序条**（partial，从 `BattleScreenStage.cs` 拆出来守 800 行硬上限）：
/// 只挪位置、不改行为——版式常量与站位仍在 `BattleScreenStage.cs`。
/// </summary>
public partial class BattleScreen
{

    // ------------------------------------------------------------------ 行动顺序条（22 §1.1 第五版）

    /// <summary>
    /// 贴左缘竖排的顺序条：**整条 ≤40px**、一行＝头像 ＋ 一条行动条、上端＝当前出手（红框＋▶）、
    /// 下半段细线隔开＝下回合预览。**挂在屏上、不进 `StageRoot`**——挂进去的话出手推镜会把它推出画。
    /// </summary>
    private void _refresh_turn_bar()
    {
        if (_turn_bar == null)
        {
            return;
        }

        _clear(_turn_bar);
        if (sim == null || _sim_finished())
        {
            _turn_bar.Visible = false;
            return;
        }

        Godot.Collections.Array order = sim.Call("turn_order", allies, enemies).AsGodotArray();
        if (order.Count == 0)
        {
            _turn_bar.Visible = false;
            return;
        }

        _turn_bar.Visible = true;
        GodotObject current = sim.Call("in_round").AsBool()
            ? sim.Call("current_actor").AsGodotObject()
            : null;
        var speeds = new System.Collections.Generic.List<double>();
        foreach (Variant actor_variant in order)
        {
            GodotObject actor = actor_variant.AsGodotObject();
            var one = new Godot.Collections.Array { actor };
            foreach (Variant single in sim.Call("turn_order", one, new Godot.Collections.Array()).AsGodotArray())
            {
                speeds.Add(_actor_speed(single.AsGodotObject()));
            }
        }

        int shown = 0;
        foreach (Variant actor_variant in order)
        {
            if (shown >= 8)
            {
                break;
            }

            _turn_bar.AddChild(_turn_row(actor_variant.AsGodotObject(), speeds, false, actor_variant.AsGodotObject() == current));
            shown += 1;
        }

        if (order.Count > shown)
        {
            _turn_bar.AddChild(_turn_label($"＋{order.Count - shown}"));
        }

        _turn_bar.AddChild(_turn_separator());
        int preview = 0;
        foreach (Variant actor_variant in order)
        {
            if (preview >= 4)
            {
                break;
            }

            _turn_bar.AddChild(_turn_row(actor_variant.AsGodotObject(), speeds, true, false));
            preview += 1;
        }

        if (order.Count > preview)
        {
            _turn_bar.AddChild(_turn_label("…"));
        }

        // 竖排、竖直居中：整条压到左缘 2px（22 §1.1 那条注——右缘必须让开我方第一个人的头顶条）
        _turn_bar.ResetSize();
        Vector2 world = _stage_size();
        _turn_bar.Position = new Vector2(2.0f, (world.Y - _turn_bar.Size.Y) * 0.5f);
        _turn_bar.CustomMinimumSize = new Vector2(TURN_BAR_WIDTH, 0);
    }

    private static Label _turn_label(string text)
    {
        var label = new Label
        {
            Name = $"TurnText_{text}",
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color("#A89A7E"));
        return label;
    }

    private static Control _turn_separator()
    {
        // 「下回合预览」用一条细线隔开（22 §1.1 第五版：整条 ≤40px，
        // 「下回合」三个字在 12px 档上放不下——所以不写字，只画线）
        return new ColorRect
        {
            Name = "TurnSeparator",
            Color = new Color("#A89A7E"),
            CustomMinimumSize = new Vector2(24, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>
    /// 顺序条的一行：头像 ＋ 行动条（24×4）。**不写名字、不挂状态**——状态与气血归身上，
    /// 顺序条只报顺序（22 §1.1 第五版）。
    /// </summary>
    private Control _turn_row(
        GodotObject actor, System.Collections.Generic.List<double> speeds, bool next, bool current)
    {
        bool foe = actor.Get("side").AsInt32() == _side_enemy();
        var row = new Control
        {
            Name = $"Turn_{(next ? "nx_" : "now_")}{actor.Get("actor_id").AsString()}",
            CustomMinimumSize = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(24, 24),
        };
        if (current)
        {
            // 红框（22 §1.1 第五版：当前出手那一行＝红框 ＋ ▶）——只画一圈边，不遮头像
            var frame = new Panel
            {
                Name = "NowFrame",
                CustomMinimumSize = new Vector2(24, 24),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            frame.AddThemeStyleboxOverride("panel", _frame_box(UiKit.color("cinnabar"), 2));
            row.AddChild(frame);

            var mark = new Label
            {
                Name = "NowMark",
                Text = "▶",
                Position = new Vector2(0, 2),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            mark.AddThemeFontSizeOverride("font_size", 12);
            mark.AddThemeColorOverride("font_color", UiKit.color("gold"));
            row.AddChild(mark);
        }

        var portrait = new TextureRect
        {
            Name = "Portrait",
            Texture = BattleAvatarArt.portrait(actor.Get("actor_id").AsString(), foe),
            Position = new Vector2(2, 0),
            Size = new Vector2(20, 20),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(portrait);
        row.AddChild(_turn_progress(actor, speeds));
        if (next)
        {
            row.Modulate = new Color(1, 1, 1, 0.72f);
        }

        return row;
    }

    /// <summary>
    /// 一条行动条（24×4）：把这一场的身法拉成 **0.25～1** 的刻度——最快的满格、最慢的留 1/4。
    /// 「下回合」那半段另加「疾／缓」修正，所以同一个人的两段可以不一样长（22 §1.1 第五版）。
    /// </summary>
    private Control _turn_progress(GodotObject actor, System.Collections.Generic.List<double> speeds)
    {
        var bar = new Control
        {
            Name = "Action",
            Position = new Vector2(0, 19),
            Size = new Vector2(24, 4),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var track = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        track.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        track.AddThemeStyleboxOverride("panel", _stage_box(UiKit.color("track"), 1, 1));
        bar.AddChild(track);
        var fill = new ColorRect
        {
            Name = "Fill",
            Color = actor.Get("side").AsInt32() == _side_enemy()
                ? UiKit.color("rim_foe")
                : UiKit.color("gold"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fill.AnchorRight = (float)_turn_progress_ratio(_actor_speed(actor), speeds);
        fill.OffsetRight = -1.0f;
        fill.OffsetLeft = 1.0f;
        fill.OffsetTop = 1.0f;
        fill.OffsetBottom = -1.0f;
        bar.AddChild(fill);
        return bar;
    }

    private static double _turn_progress_ratio(
        double speed, System.Collections.Generic.List<double> speeds)
    {
        if (speeds.Count == 0)
        {
            return 1.0;
        }

        double lo = speeds[0];
        double hi = speeds[0];
        foreach (double value in speeds)
        {
            lo = System.Math.Min(lo, value);
            hi = System.Math.Max(hi, value);
        }

        if (hi - lo < 0.0001)
        {
            return 1.0;
        }

        return 0.25 + (0.75 * System.Math.Clamp((speed - lo) / (hi - lo), 0.0, 1.0));
    }

    /// <summary>身法（出手顺序与行动条进度都读它，与 `BattleSimulator.turn_order` 同一处口径）。</summary>
    private static double _actor_speed(GodotObject actor) =>
        actor.Get("stats").AsGodotDictionary().ContainsKey("speed")
            ? actor.Get("stats").AsGodotDictionary()["speed"].AsDouble()
            : 0.0;
}
