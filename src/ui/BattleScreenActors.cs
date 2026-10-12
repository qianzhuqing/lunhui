using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的**单位**与**挂在人身上的信息**（partial，守 800 行硬上限）：
/// 形象、头顶条、架势刻度、相性行、状态图标、预兆签，外加日志与六指令按钮的刷新。
///
/// 节点名 `Actor_&lt;id&gt;`／`Name_&lt;id&gt;`／`HpBar_&lt;id&gt;` 是用例与自检的接口，别改；
/// 版式常量与站位在 `BattleScreenStage.cs`，这里只管「一个单位长什么样」。
///
/// 这一版的核心动作（22 §1.2）：**没有卡片了——战场就是唯一的选人界面**，
/// 信息全挂人身上：头顶条／状态图标／预兆签／相性行。
/// </summary>
public partial class BattleScreen
{
    /// <summary>
    /// 一个单位：形象 ＋ 站位影 ＋ 头顶信息牌。`size` 是形象框的边长（远景敌 96／近景我方 128／Boss 176）。
    /// 挂牌子的位置要等排完版才知道，所以先建出来、`ResetSize()` 之后再挪。
    /// </summary>
    public Control _make_unit(Variant actor_variant, int size)
    {
        GodotObject actor = actor_variant.AsGodotObject();
        string actor_id = actor.Get("actor_id").AsString();
        var unit = new Control
        {
            Name = $"Actor_{actor_id}",
            CustomMinimumSize = new Vector2(size, size),
            Size = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var shadow = _shadow((int)(size * 0.72f));
        shadow.Position = new Vector2(size * 0.14f, size - 7.0f);
        unit.AddChild(shadow);

        bool dead = !actor.Call("is_alive").AsBool();
        var sprite = new TextureRect
        {
            Name = $"Sprite_{actor_id}",
            Texture = _unit_texture(actor),
            Position = Vector2.Zero,
            Size = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        unit.AddChild(sprite);
        if (dead)
        {
            // 阵亡：压暗 ＋ 倒下（转 90 度）——比在名字后面写「（倒）」直观
            sprite.Modulate = new Color(1, 1, 1, 0.45f);
            sprite.PivotOffset = new Vector2(size * 0.5f, size * 0.9f);
            sprite.Rotation = Mathf.DegToRad(78.0f);
        }

        Control plate = _make_plate(actor, size);
        plate.ResetSize();
        plate.Position = new Vector2((size - plate.Size.X) * 0.5f, -plate.Size.Y - PLATE_GAP);
        unit.AddChild(plate);
        return unit;
    }

    /// <summary>
    /// 形象：**有图才摆**——美术的动作表（A6）一交就用它，没交才用程序占位剪影
    /// （`BattleAvatarArt`；这块一交图就自动退休）。
    /// </summary>
    private Texture2D _unit_texture(GodotObject actor)
    {
        bool is_ally = actor.Get("side").AsInt32() == _side_ally();
        string source_id = actor.Get("source_id").AsString();
        string actor_id = actor.Get("actor_id").AsString();
        string faction = is_ally
            ? "characters"
            : (actor.Get("tags").AsGodotDictionary().ContainsKey("faction")
                ? actor.Get("tags").AsGodotDictionary()["faction"].AsString()
                : "");
        Texture2D texture = IconPaths.load_icon(IconPaths.actor(faction, source_id));
        if (texture != null)
        {
            return texture;
        }

        string weapon = _weapon_type(actor);
        return is_ally
            ? BattleAvatarArt.ally_figure(actor_id, weapon, false)
            : BattleAvatarArt.foe_figure(actor_id, weapon, false);
    }

    /// <summary>
    /// 这个单位手上是什么兵器——只用来挑占位剪影那一笔（我方读装配的武器、敌方读它第一招的武器类型）。
    /// 表里没有敌人的武器列（`enemy_base` 只有招式），所以从招式反推，读不到就当空手。
    /// </summary>
    private string _weapon_type(GodotObject actor)
    {
        if (actor.Get("side").AsInt32() == _side_ally())
        {
            Resource weapon = PartyBuilder.equipped_weapon(Db, state, actor.Get("actor_id").AsString());
            return weapon != null ? weapon.Get("weapon_type").AsString() : "";
        }

        foreach (string skill_id in actor.Get("skills").AsStringArray())
        {
            Resource skill = Db?.get_row("skill_base", skill_id);
            if (skill == null)
            {
                continue;
            }

            string weapon = skill.Get("weapon_type").AsString();
            if (weapon.Length > 0 && weapon != "any")
            {
                return weapon;
            }
        }

        return "";
    }

    /// <summary>
    /// 头顶信息牌（22 §1.2.1–§1.2.5）：敌我自上而下——
    /// **预兆签（只敌方）／名字／气血条＋数值／架势刻度／内力条（只我方）／相性行（只敌方）／状态图标**。
    /// 牌子水平居中在单位头顶 8px 处（位置由调用方在 `ResetSize()` 之后摆）。
    /// </summary>
    private Control _make_plate(GodotObject actor, int size)
    {
        string actor_id = actor.Get("actor_id").AsString();
        bool is_ally = actor.Get("side").AsInt32() == _side_ally();
        var plate = new VBoxContainer
        {
            Name = $"Plate_{actor_id}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        plate.AddThemeConstantOverride("separation", 2);
        string rim = is_ally ? "rim_ally" : "rim_foe";
        int bar_width = is_ally ? 72 : (size >= ALLY_W ? 96 : 56);

        if (!is_ally)
        {
            Control sign = _intent_sign(actor);
            if (sign != null)
            {
                plate.AddChild(sign);
            }
        }

        var name = new Label
        {
            Name = $"Name_{actor_id}",
            Text = actor.Get("display_name").AsString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 12);
        name.AddThemeColorOverride("font_color", UiKit.color("stage_text"));
        if (!actor.Call("is_alive").AsBool())
        {
            name.Text = $"{name.Text}（倒）";
        }
        else if (is_ally && actor.Get("hp").AsInt32() * 4 <= System.Math.Max(1, actor.Call("max_hp").AsInt32()))
        {
            // 濒死提示：血量 ≤25% 时名字转红，玩家一眼看到要救
            name.AddThemeColorOverride("font_color", UiKit.color("cinnabar"));
            name.Text = $"{name.Text}（危急）";
        }

        plate.AddChild(name);

        var hp_row = new HBoxContainer
        {
            Name = $"HpRow_{actor_id}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hp_row.AddThemeConstantOverride("separation", 5);
        hp_row.AddChild(_status_bar(
            $"HpBar_{actor_id}", actor.Get("hp").AsDouble(), actor.Call("max_hp").AsDouble(),
            "hp", bar_width, 6, rim));
        var hp_text = new Label
        {
            Name = $"HpText_{actor_id}",
            Text = $"{actor.Get("hp").AsInt32()}/{actor.Call("max_hp").AsInt32()}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hp_text.AddThemeFontSizeOverride("font_size", 12);
        hp_text.AddThemeColorOverride("font_color", UiKit.color("stage_dim"));
        hp_row.AddChild(hp_text);
        plate.AddChild(hp_row);

        plate.AddChild(_poise_ticks(actor, bar_width, rim));

        if (is_ally)
        {
            plate.AddChild(_status_bar(
                $"QiBar_{actor_id}", actor.Get("qi").AsDouble(), actor.Call("max_qi").AsDouble(),
                "mp", bar_width, 3, rim));
        }
        else
        {
            var affinity = new HBoxContainer
            {
                Name = $"Affinity_{actor_id}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            affinity.AddThemeConstantOverride("separation", 4);
            foreach (string mark in _affinity_marks(actor))
            {
                affinity.AddChild(_affinity_chip(mark));
            }

            if (affinity.GetChildCount() > 0)
            {
                plate.AddChild(affinity);
            }
        }

        plate.AddChild(_status_icons(actor));
        if (actor.Call("is_broken").AsBool())
        {
            var mark = new Label
            {
                Name = $"BrokenMark_{actor_id}",
                Text = "破绽",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            mark.AddThemeFontSizeOverride("font_size", 24);
            mark.AddThemeColorOverride("font_color", UiKit.color("cinnabar"));
            plate.AddChild(mark);
        }

        if (!is_ally)
        {
            // 点敌人本体＝选它（22 §1.3：战场就是唯一的选人界面）
            string wanted = actor_id;
            var pick = new Button
            {
                Name = $"TargetButton_{actor_id}",
                Text = actor_id == selected_target_id ? "▶ 已选中" : "选为目标",
                CustomMinimumSize = new Vector2(88, 22),
            };
            pick.AddThemeFontSizeOverride("font_size", 12);
            pick.Pressed += () => select_target(wanted);
            plate.AddChild(pick);
        }

        if (is_ally)
        {
            var gear = new Label
            {
                Name = $"Gear_{actor_id}",
                Text = _gear_text(actor),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            gear.AddThemeFontSizeOverride("font_size", 12);
            gear.AddThemeColorOverride("font_color", UiKit.color("stage_dim"));
            plate.AddChild(gear);
        }

        return plate;
    }

    /// <summary>
    /// 预兆签（22 §1.2.4）：敌人头顶那一枚朱红签，**点签＝拆招**（横带上的按钮随之取消）。
    /// 拆过之后签转灰写「已被拆招 · 伤害减半」——判定与以前一字不差。
    /// </summary>
    private Control _intent_sign(GodotObject actor)
    {
        if (sim == null || _sim_finished() || !actor.Call("is_alive").AsBool())
        {
            return null;
        }

        string actor_id = actor.Get("actor_id").AsString();
        string skill_id = sim.Call("intent_of", actor_id).AsString();
        if (skill_id.Length == 0)
        {
            return null;
        }

        Resource skill = Db?.get_row("skill_base", skill_id);
        string skill_name = skill != null ? skill.Get("name_cn").AsString() : skill_id;
        bool parried = sim.Call("is_parried", actor_id).AsBool();
        // 能拆时把**效果**说清楚（22 §1.2.4：点签＝拆招）——折扣现算自 `combat_const.parry_damage_mult`，
        // 改表这里跟着变，别把 50% 写死在字里。
        Resource parry_const = Db?.get_row("combat_const", "parry_damage_mult");
        string parry_hint = parry_const != null
            ? $"——点这枚签＝拆招（伤害打到 {parry_const.Get("value").AsDouble() * 100:F0}%、我方架势反涨）"
            : "——点这枚签＝拆招";
        var sign = new Button
        {
            Name = $"Intent_{actor_id}",
            Text = parried ? "已被拆招 · 伤害减半" : $"{skill_name} · 将用",
            CustomMinimumSize = new Vector2(120, 22),
            TooltipText = parried
                ? $"{actor.Get("display_name").AsString()} 这一招已被拆招，伤害减半"
                : $"{actor.Get("display_name").AsString()} 将用 {skill_name}{parry_hint}",
        };
        sign.AddThemeFontSizeOverride("font_size", 12);
        sign.AddThemeColorOverride(
            "font_color", parried ? UiKit.color("rim_foe") : UiKit.color("stage_text"));
        sign.AddThemeStyleboxOverride(
            "normal", _stage_box(parried ? new Color(0.25f, 0.25f, 0.27f, 0.9f)
                : new Color(UiKit.color("cinnabar"), 0.88f), 2, 1));
        string wanted = actor_id;
        sign.Pressed += () => press_parry(wanted);
        return sign;
    }

    /// <summary>
    /// 状态图标（22 §1.2.3）：挂在头顶血条**正下方**一排，**减益在前**（红框）、增益在后（蓝框），
    /// 每个 20×20；最多 6 个，超了末位写「+N」。剩余回合写图标右下角小数字，常驻（∞）不写。
    /// </summary>
    private Control _status_icons(GodotObject actor)
    {
        string actor_id = actor.Get("actor_id").AsString();
        var box = new HBoxContainer
        {
            Name = $"StatusIcons_{actor_id}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);
        if (!actor.HasMethod("effect_rows"))
        {
            return box;
        }

        Godot.Collections.Array rows = BattleActor.sort_effect_rows(
            actor.Call("effect_rows", db).AsGodotArray());
        int shown = 0;
        foreach (Variant row_variant in rows)
        {
            // 行里补上归属：图标就挂在**这个人**的头上，用例与自检按 `Buff_<actor>_<id>` 找它
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary().Duplicate();
            row["actor_id"] = actor_id;
            if (rows.Count > 6 && shown == 5)
            {
                var more = new Label
                {
                    Name = $"StatusMore_{actor_id}",
                    Text = $"＋{rows.Count - 5}",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                more.AddThemeFontSizeOverride("font_size", 12);
                more.AddThemeColorOverride("font_color", UiKit.color("stage_text"));
                box.AddChild(more);
                break;
            }

            if (shown >= 6)
            {
                break;
            }

            box.AddChild(_make_effect_chip(row));
            shown += 1;
        }

        return box;
    }

    /// <summary>
    /// 一件事的一枚图标（增益／减益／异常状态）：**有图用图**（`icons/buff` 与 `icons/status`），
    /// 没图退回一个字（色盲可辨：极性还要靠「形状」，光红蓝不够——设计 08）。
    /// 悬浮给全名、剩余回合、层数、效果。
    /// </summary>
    public Button _make_effect_chip(Godot.Collections.Dictionary row)
    {
        string actor_id = _s(row, "actor_id", "?");
        string entry_id = _s(row, "entry_id").Length > 0 ? _s(row, "entry_id") : _s(row, "buff_id", "?");
        var chip = new Button
        {
            Name = $"Buff_{actor_id}_{entry_id}",
            CustomMinimumSize = new Vector2(20, 20),
            FocusMode = Control.FocusModeEnum.None,
        };
        chip.AddThemeFontSizeOverride("font_size", 12);
        bool is_debuff = _b(row, "is_debuff");
        int remaining = _i(row, "remaining");
        string time_text = _b(row, "permanent") || remaining < 0 ? "∞" : remaining.ToString();
        int stacks = _i(row, "stacks", 1);

        // 图标优先表里的 `icon`，空则退回行 id——与 `item_base.icon` 同一套优先级，
        // 美术按哪个名字交都能对上。**有图才摆**：没交付时退回一个字，不占空位。
        string icon_id = _s(row, "icon").Trim();
        if (icon_id.Length == 0)
        {
            icon_id = entry_id;
        }

        string icon_path = _s(row, "kind") == "status" ? IconPaths.status(icon_id) : IconPaths.buff(icon_id);
        chip.SetMeta("icon_path", icon_path);
        Texture2D icon_texture = IconPaths.load_icon(icon_path);
        string name = _s(row, "name");
        if (icon_texture != null)
        {
            chip.Icon = icon_texture;
        }
        else
        {
            chip.Text = name.Length > 0 ? name.Substring(0, 1) : "?";
        }

        // 极性用「形状 + 字」两重编码（08 要求色盲可辨：光靠红蓝不够），
        // 形状走边框色（减益红／增益蓝），字走 tooltip 与溢出时那一位。
        chip.AddThemeStyleboxOverride(
            "normal", _stage_box(new Color(0, 0, 0, 0.62f), 2, 1));
        chip.AddThemeStyleboxOverride(
            "hover", _stage_box(new Color(0, 0, 0, 0.62f), 2, 1));
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var style = (StyleBoxFlat)chip.GetThemeStylebox(state).Duplicate();
            style.BorderColor = is_debuff ? UiKit.color("cinnabar") : UiKit.color("mp");
            chip.AddThemeStyleboxOverride(state, style);
        }

        chip.AddThemeColorOverride(
            "font_color", is_debuff ? new Color("#F0B7A6") : new Color("#A8CFE4"));
        string stack_text = stacks <= 1 ? "" : $" ×{stacks}";
        chip.TooltipText = $"{name}（{(is_debuff ? "减益" : "增益")}，剩余 {time_text}{stack_text}）"
            + _s(row, "desc");
        return chip;
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
        _auto_button.Text = auto_enabled && !done ? "自动战斗中…" : "自动战斗";
        _speed_button.Text = $"速度 {(int)speed()}x";
        // 策略按钮：把当前策略写在按钮上，玩家不用记
        _strategy_button.Text =
            $"策略：{(sim != null ? sim.Call("strategy_label").AsString() : "均衡")}";
        _strategy_button.TooltipText =
            "自动战斗策略（保守省内力／均衡期望伤害／全力单发最高倍率），点一下轮换";
        _strategy_button.Disabled = done;
        _auto_button.Disabled = done;
        _speed_button.Disabled = done;
        // 六指令里的四条（招式那一行与逃跑各自管自己）：战斗没结束就能按——
        // 没在回合里点一下会自己开回合（与「直接点招式先手出招」同一条行为）
        GodotObject actor = _current_ally_actor();
        bool command_disabled = done || sim == null;
        foreach (Button button in new[] { _basic_button, _skill_button, _inner_button, _item_button, _defend_button })
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
        // （公式在 `flee_chance`，系数落在 `combat_const`），没道理让玩家摸黑——而且它是**现算的**。
        _flee_button.TooltipText = reason.Length > 0
            ? reason
            : $"掷点判定：成功率 {System.Math.Round(
                sim.Call("flee_chance", _current_ally_actor()).AsDouble() * 100.0,
                System.MidpointRounding.AwayFromZero)}%（身法差越大越高）";
    }

    private void _set_status(string text)
    {
        if (_status == null)
        {
            return;
        }

        _status.Text = text;
        // 播报只留 3 秒：长内容进战报，屏幕上那一行是给"刚刚发生了什么"用的（22 §1.6）
        _status.Modulate = new Color(1, 1, 1, 1);
        _status_tween?.Kill();
        _status_tween = CreateTween();
        _status_tween.TweenInterval(3.0);
        _status_tween.TweenProperty(_status, "modulate:a", 0.0, 0.6);
    }
}
