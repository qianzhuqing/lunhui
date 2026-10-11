using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的单位卡片、血条／内力条／架势条与日志——从 `BattleScreen.cs` 拆出来的
/// partial（那份要守 800 行硬上限）。
///
/// 卡片是运行时生成的动态内容（骨架在 `.tscn` 里）；节点名 `Actor_<id>`／`Name_<id>`／
/// `HpBar_<id>` 就是用例与自检的接口，别改。
/// </summary>
public partial class BattleScreen
{
    public Control _make_actor_row(Variant actor_variant)
    {
        GodotObject actor = actor_variant.AsGodotObject();
        string actor_id = actor.Get("actor_id").AsString();
        bool is_ally = actor.Get("side").AsInt32() == _side_ally();
        var row = new VBoxContainer { Name = $"Actor_{actor_id}" };
        row.AddThemeConstantOverride("separation", 2);
        // 名字与「选为目标」同一行：卡片矮一点，多几个敌人也不用滚
        var head = new HBoxContainer { Name = $"Head_{actor_id}" };
        head.AddThemeConstantOverride("separation", 6);
        row.AddChild(head);
        // 敌人剪影（15 六：`assets/sprites/<faction>/<enemy_id>.png`）——**有图才摆**。
        // 美术先出了两条做对照（山寨喽啰／别派弟子），其余还没出；我方那一侧不放
        // （角色的形象在角色面板的立绘里，战斗卡片只列数值，免得卡片变高）。
        if (!is_ally)
        {
            Godot.Collections.Dictionary tags = actor.Get("tags").AsGodotDictionary();
            string faction = tags.ContainsKey("faction") ? tags["faction"].AsString() : "";
            string sprite_path = IconPaths.actor(faction, actor.Get("source_id").AsString());
            TextureRect sprite = IconPaths.make_icon(sprite_path, $"Sprite_{actor_id}");
            if (sprite != null)
            {
                head.AddChild(sprite);
            }
        }

        var label = new Label { Name = $"Name_{actor_id}" };
        bool is_target = !is_ally && actor_id == selected_target_id;
        label.Text = $"{(is_target ? "▶ " : "")}{actor.Get("display_name").AsString()}　"
            + $"Lv{actor.Get("level").AsInt32()}　气血 {actor.Get("hp").AsInt32()}/"
            + $"{actor.Call("max_hp").AsInt32()}　内力 {actor.Get("qi").AsInt32()}";
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 阵亡：卡片压暗并标「（倒）」，还带一点点倒下的位移（跳字与血条都跟着暗下去）
        bool dead = !actor.Call("is_alive").AsBool();
        if (dead)
        {
            label.Text = $"{label.Text}　（倒）";
            row.Modulate = new Color(1, 1, 1, 0.45f);
            row.Position = new Vector2(-6.0f, row.Position.Y);
            Tween fall = CreateTween();
            fall.TweenProperty(row, "position:x", 0.0, 0.25);
        }

        if (is_target)
        {
            label.AddThemeColorOverride("font_color", new Color("ffd24a"));
        }

        if (dead)
        {
            label.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        }
        else if (is_ally
            && actor.Get("hp").AsInt32() * 4 <= System.Math.Max(1, actor.Call("max_hp").AsInt32()))
        {
            // 濒死提示：血量 ≤25% 时名字转红，玩家一眼看到要救
            label.AddThemeColorOverride("font_color", new Color("eb5757"));
            label.Text += "　（危急）";
        }

        head.AddChild(label);
        if (!is_ally && actor.Call("is_alive").AsBool())
        {
            var pick = new Button
            {
                Name = $"TargetButton_{actor_id}",
                Text = is_target ? "▶ 已选中" : "选为目标",
                CustomMinimumSize = new Vector2(88, 24),
            };
            string wanted = actor_id;
            pick.Pressed += () => select_target(wanted);
            head.AddChild(pick);
            // 拆招按钮：预兆已给，玩家在本回合自己的行动里可以「花一次行动读破它」
            var parry = new Button
            {
                Name = $"ParryButton_{actor_id}",
                Text = sim.Call("is_parried", actor_id).AsBool() ? "已拆招" : "拆招",
                CustomMinimumSize = new Vector2(72, 24),
            };
            GodotObject ally = _current_ally_actor();
            string reason = sim.Call("parry_block_reason", ally, actor).AsString();
            parry.Disabled = reason.Length > 0;
            if (parry.Disabled)
            {
                parry.TooltipText = reason;
            }
            else if (!sim.Call("is_parried", actor_id).AsBool())
            {
                // 能拆的时候把**效果**写清楚——三条都是已实现的行为（那一招伤害按 `parry_damage_mult`
                // 打折、架势反涨、花掉这次行动），跟逃跑那个成功率提示同一类：信息就在代码里，
                // 别让玩家靠试。数字**现算**（`combat_const.parry_damage_mult`），设计改表这里自动跟着变。
                parry.TooltipText = "读破这一招：它这次伤害按 "
                    + $"{sim.Call("parry_damage_mult").AsDouble() * 100.0:F0}% 结算，"
                    + "自己反涨架势（要花掉这次行动）";
            }

            string parry_target = actor_id;
            parry.Pressed += () => press_parry(parry_target);
            head.AddChild(parry);
        }

        // 预兆行：敌人这一回合要出什么招，界面上要先看得见（设计 04 的核心循环）
        if (!is_ally && actor.Call("is_alive").AsBool())
        {
            string intent_id = sim.Call("intent_of", actor_id).AsString();
            if (intent_id.Length > 0)
            {
                var intent = new Label { Name = $"Intent_{actor_id}" };
                if (sim.Call("is_parried", actor_id).AsBool())
                {
                    intent.Text = $"预兆：{sim.Call("skill_display_name", intent_id).AsString()}（已被拆招，伤害减半）";
                    intent.AddThemeColorOverride("font_color", new Color("9a9a9a"));
                }
                else
                {
                    intent.Text = $"预兆：{sim.Call("skill_display_name", intent_id).AsString()}";
                    intent.AddThemeColorOverride("font_color", new Color("c9a0ff"));
                }

                intent.AddThemeFontSizeOverride("font_size", 12);
                row.AddChild(intent);
            }
        }

        var bar = new ProgressBar
        {
            Name = $"HpBar_{actor_id}",
            MaxValue = System.Math.Max(1.0, actor.Call("max_hp").AsDouble()),
            Value = actor.Get("hp").AsDouble(),
            CustomMinimumSize = new Vector2(320, 10),
            ShowPercentage = false,
        };
        row.AddChild(bar);
        // 内力条与架势条：手选招式要看内力，破绽机制要看架势
        var bars = new HBoxContainer { Name = $"Bars_{actor_id}" };
        bars.AddThemeConstantOverride("separation", 6);
        row.AddChild(bars);
        bars.AddChild(_make_bar(
            $"QiBar_{actor_id}",
            actor.Get("qi").AsDouble(),
            System.Math.Max(1, actor.Call("max_qi").AsInt32()),
            new Color("5aa9e6"),
            150));
        ProgressBar poise_bar = _make_bar(
            $"PoiseBar_{actor_id}",
            actor.Get("poise").AsDouble(),
            System.Math.Max(1, actor.Call("max_poise").AsInt32()),
            actor.Call("is_broken").AsBool() ? new Color("eb5757") : new Color("e0a13c"),
            160);
        bars.AddChild(poise_bar);
        _decorate_poise_bar(poise_bar, actor);
        if (actor.Call("is_broken").AsBool())
        {
            var mark = new Label
            {
                Name = $"BrokenMark_{actor_id}",
                Text = "破绽！",
            };
            mark.AddThemeColorOverride("font_color", new Color("eb5757"));
            bars.AddChild(mark);
        }

        // 我方单位详情（设计 14 §四：左侧那一栏 = 气血／内力／架势 ＋ **武器与已装内功**）。
        // 前三条已经在上面（名字里的气血／内力 ＋ 两根条 ＋ 架势条），缺的是这一行。
        if (is_ally)
        {
            var gear = new Label
            {
                Name = $"Gear_{actor_id}",
                Text = _gear_text(actor),
            };
            gear.AddThemeFontSizeOverride("font_size", 12);
            gear.AddThemeColorOverride("font_color", new Color(0.78f, 0.83f, 0.9f));
            row.AddChild(gear);
        }

        // 异常状态：中毒/灼伤/流血/内伤，颜色取 damage_type.display_color
        string status_text = _status_text(actor);
        if (status_text.Length > 0)
        {
            var status_label = new Label
            {
                Name = $"Status_{actor_id}",
                Text = status_text,
            };
            status_label.AddThemeColorOverride("font_color", _status_color(actor));
            bars.AddChild(status_label);
        }

        return row;
    }

    /// <summary>
    /// 「武器与已装内功」那一行。两样都从**存档 + 表**里查（战斗单位身上只带武器类型与系别，
    /// 不带物品 id）；查不到就如实写「空手」／「未装内功」，不留空白。
    /// </summary>
    private string _gear_text(GodotObject actor)
    {
        string char_id = actor.Get("actor_id").AsString();
        Resource weapon = PartyBuilder.equipped_weapon(Db, state, char_id);
        string weapon_name = weapon != null ? weapon.Get("name_cn").AsString() : "空手";
        var names = new System.Collections.Generic.List<string>();
        foreach (string skill_id in actor.Get("passives").AsStringArray())
        {
            Resource row = Db.get_row("skill_base", skill_id);
            names.Add(row != null ? row.Get("name_cn").AsString() : skill_id);
        }

        string inner = names.Count == 0 ? "未装内功" : string.Join("、", names);
        return $"武器：{weapon_name}　内功：{inner}";
    }

    /// <summary>一行状态摘要：「中毒×3（2回合）、灼伤×1」；没有状态返回空串</summary>
    private string _status_text(GodotObject actor)
    {
        if (!actor.HasMethod("status_rows"))
        {
            return "";
        }

        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in actor.Call("status_rows", db).AsGodotArray())
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            parts.Add($"{_s(row, "name")}×{_i(row, "stacks")}（{_i(row, "remaining")}回合）");
        }

        return string.Join("、", parts);
    }

    /// <summary>状态行用第一条状态的伤害类型颜色（DoT 的颜色在 damage_type.display_color 里）</summary>
    private Color _status_color(GodotObject actor)
    {
        if (!actor.HasMethod("status_rows"))
        {
            return new Color(1, 1, 1);
        }

        Godot.Collections.Array rows = actor.Call("status_rows", db).AsGodotArray();
        return rows.Count == 0
            ? new Color(1, 1, 1)
            : new Color(rows[0].AsGodotDictionary()["color"].AsString());
    }

    private static ProgressBar _make_bar(
        string node_name, double value, double maximum, Color color, int width)
    {
        var bar = new ProgressBar
        {
            Name = node_name,
            MaxValue = System.Math.Max(1.0, maximum),
            Value = value,
            CustomMinimumSize = new Vector2(width, 8),
            ShowPercentage = false,
        };
        var style = new StyleBoxFlat { BgColor = color };
        bar.AddThemeStyleboxOverride("fill", style);
        return bar;
    }

    /// <summary>
    /// 架势条的「即将破防」（设计 15 §4.5：这是核心循环的爽点，不能只是一条会变短的色块）。
    ///
    /// 分工（美术在 `tools/artgen/ui.js` 里写死的那句话）：**条的颜色仍由这里按色值表刷**，
    /// 两张贴图只管「加什么」——架势低于临界线叠 `poise_critical.png`（裂纹），打空那一刻
    /// 换成 `poise_break.png`（横条压亮 ＋ 四道炸开的短光）。
    ///
    /// 贴图原生 32×12（15 §4.4 的状态条高度），比这根 8px 的条高 2px，所以上下各溢 2px；
    /// **不影响版式**：子 Control 不计入父级最小尺寸，`满招式`那档 636/648 的预算照旧。
    /// 临界线是开发侧定的**表现**阈值（设计只写了"要有临界表现"、没给数值），
    /// 与判定无关，因此不进变异探针的手感常量清单。
    /// </summary>
    private const string POISE_CRITICAL_TEXTURE = "res://assets/ui/battle/poise_critical.png";
    private const string POISE_BREAK_TEXTURE = "res://assets/ui/battle/poise_break.png";
    private const double POISE_CRITICAL_RATIO = 0.3;

    private void _decorate_poise_bar(ProgressBar bar, GodotObject actor)
    {
        string file = "";
        if (actor.Call("is_broken").AsBool())
        {
            file = POISE_BREAK_TEXTURE;
        }
        else if (actor.Get("poise").AsDouble() / System.Math.Max(1.0, actor.Call("max_poise").AsDouble())
                 <= POISE_CRITICAL_RATIO)
        {
            file = POISE_CRITICAL_TEXTURE;
        }

        if (file.Length == 0 || !ResourceLoader.Exists(file))
        {
            return; // 美术还没交图时不摆这一层（有图才加，与敌人剪影同一口径）
        }

        var overlay = new TextureRect
        {
            Name = $"PoiseFx_{actor.Get("actor_id").AsString()}",
            Texture = GD.Load<Texture2D>(file),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.OffsetTop = -2.0f;
        overlay.OffsetBottom = 2.0f;
        bar.AddChild(overlay);
    }

    private static void _clear(Node box)
    {
        if (box == null)
        {
            return;
        }

        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void _append_log(Godot.Collections.Array lines)
    {
        foreach (Variant line in lines)
        {
            string text = line.AsString();
            _log.AppendText($"{_colored_line(text)}\n");
            // 预兆带由 `_refresh_intent_band()` 单独维护——日志不再往那儿写字
        }
    }

    /// <summary>日志配色：回合头蓝色、暴击金色、未命中灰色、其余常规</summary>
    private static string _colored_line(string text)
    {
        if (text.StartsWith("——", System.StringComparison.Ordinal))
        {
            return $"[color=#8ab4f8]{text}[/color]";
        }

        if (text.Contains("暴击"))
        {
            return $"[color=#ffd24a]{text}[/color]";
        }

        if (text.Contains("破绽") || text.Contains("架势被打"))
        {
            return $"[color=#eb5757]{text}[/color]";
        }

        if (text.Contains("未命中") || text.Contains("跳过") || text.Contains("失败"))
        {
            return $"[color=#9aa0a6]{text}[/color]";
        }

        int head_end = text.IndexOf(" ", System.StringComparison.Ordinal);
        if (head_end > 0 && text.StartsWith("R", System.StringComparison.Ordinal))
        {
            return $"[color=#9aa0a6]{text.Substring(0, head_end)}[/color]{text.Substring(head_end)}";
        }

        return text;
    }

    private void _refresh_buttons()
    {
        // 对位 GDScript 的 `sim != null and sim.finished()`：没开局时按钮是**可按**的（点了会自己开场）
        bool done = sim != null && _sim_finished();
        _next_button.Disabled = done;
        _auto_button.Disabled = done;
        _auto_button.Text = auto_enabled && !done ? "自动战斗中…" : "自动战斗";
        _speed_button.Text = $"速度 {(int)speed()}x";
        // 策略按钮：把当前策略写在按钮上，玩家不用记
        _strategy_button.Text =
            $"策略：{(sim != null ? sim.Call("strategy_label").AsString() : "均衡")}";
        _strategy_button.TooltipText =
            "自动战斗策略（保守省内力／均衡期望伤害／全力单发最高倍率），点一下轮换";
        _strategy_button.Disabled = done;
        // 六指令里的四条（招式那一行与逃跑各自管自己）：战斗没结束就能按——
        // 没在回合里点一下会自己开回合（与「直接点招式先手出招」同一套行为）
        GodotObject actor = _current_ally_actor();
        bool command_disabled = done || sim == null;
        foreach (Button button in new[] { _basic_button, _inner_button, _item_button, _defend_button })
        {
            if (button == null)
            {
                continue;
            }

            button.Disabled = command_disabled;
            button.TooltipText = button.GetMeta("hint").AsString();
        }

        if (_defend_button != null && !command_disabled)
        {
            string defend_reason = actor != null
                ? sim.Call("defend_block_reason", actor).AsString()
                : "";
            if (defend_reason.Length > 0)
            {
                _defend_button.Disabled = true;
                _defend_button.TooltipText = defend_reason;
            }
        }

        // 逃跑：轮到我方且不在「被追上第一回合」才可按；不能按时把原因写给玩家
        _flee_button.Disabled = true;
        if (done || sim == null)
        {
            _flee_button.TooltipText = "";
            return;
        }

        string reason = sim.Call("flee_block_reason", _current_ally_actor()).AsString();
        _flee_button.Disabled = reason.Length != 0;
        // 能撤的时候把**成功率**写进 tooltip：soft 判定面板早就如实显示成功率，逃跑是同一类掷点
        // （公式在 `flee_chance`，系数落在 `combat_const`），没道理让玩家摸黑——
        // 而且它是**现算的**，设计改了公式或系数这里自动跟着变。
        _flee_button.TooltipText = reason.Length > 0
            ? reason
            : $"掷点判定：成功率 {System.Math.Round(
                sim.Call("flee_chance", _current_ally_actor()).AsDouble() * 100.0,
                System.MidpointRounding.AwayFromZero)}%（身法差越大越高）";
    }

    private void _set_status(string text)
    {
        if (_status != null)
        {
            _status.Text = text;
        }
    }
}
