using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的界面绑定与刷新（招式行／内功行／道具行／增益减益 chip）——从
/// `BattleScreen.cs` 拆出来的 partial（那份要守 800 行硬上限）。
///
/// 界面骨架在 `scenes/battle_screen.tscn` 里（真实节点树，编辑器/MCP 可直接调版式与皮肤），
/// 这里只做查找与接线。卡牌行、跳字仍是运行时生成的动态内容。
/// </summary>
public partial class BattleScreen
{
    private void _bind_ui()
    {
        if (_ui_bound)
        {
            return;
        }

        _ui_bound = true;
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _headline = _require_node("Margin/Column/Headline") as Label;
        _allies_box = _require_node("Margin/Column/Front/TeamAllies/AllyScroll/ListAllies") as VBoxContainer;
        _enemies_box = _require_node("Margin/Column/Front/TeamEnemies/EnemyScroll/ListEnemies") as VBoxContainer;
        _round_label = _require_node("Margin/Column/Round") as Label;
        _action_label = _require_node("Margin/Column/Action") as Label;
        _log = _require_node("Margin/Column/Log") as RichTextLabel;
        _skill_row = _require_node("Margin/Column/SkillRow") as HBoxContainer;
        _buff_panel = _require_node("Margin/Column/BuffPanel") as HFlowContainer;
        _basic_button = _require_node("Margin/Column/CommandRow/BasicAttackButton") as Button;
        _inner_button = _require_node("Margin/Column/CommandRow/InnerButton") as Button;
        _item_button = _require_node("Margin/Column/CommandRow/ItemButton") as Button;
        _defend_button = _require_node("Margin/Column/CommandRow/DefendButton") as Button;
        _result_label = _require_node("Margin/Column/Result") as Label;
        _status = _require_node("Margin/Column/Status") as Label;
        _next_button = _require_node("Margin/Column/Buttons/NextRoundButton") as Button;
        _auto_button = _require_node("Margin/Column/Buttons/AutoButton") as Button;
        _speed_button = _require_node("Margin/Column/Buttons/SpeedButton") as Button;
        _strategy_button = _require_node("Margin/Column/Buttons/StrategyButton") as Button;
        _flee_button = _require_node("Margin/Column/Buttons/FleeButton") as Button;
        _return_button = _require_node("Margin/Column/Buttons/ReturnButton") as Button;
        // 回调返回非 void 的那些要包一层 lambda：`Pressed += press_next_round;` 是 CS0407
        _next_button.Pressed += () => press_next_round();
        _auto_button.Pressed += press_auto;
        _speed_button.Pressed += () => toggle_speed();
        _strategy_button.Pressed += () => press_strategy();
        _flee_button.Pressed += () => press_flee();
        _return_button.Pressed += press_return;
        _basic_button.Pressed += () => press_basic_attack();
        _inner_button.Pressed += () => press_inner_list();
        _item_button.Pressed += () => press_item_list();
        _defend_button.Pressed += () => press_defend();
        // 六指令的说明写在 meta 上（`_refresh_buttons()` 每次会按当前状态覆盖 tooltip）
        _basic_button.SetMeta("hint", "六指令之一：不耗内力、不看武器，打当前目标");
        _inner_button.SetMeta("hint", "六指令之一：查看已装内功，催动一次换运功 buff");
        _item_button.SetMeta("hint", "六指令之一：战斗中可用的道具");
        _defend_button.SetMeta("hint", "六指令之一：本回合减伤、架势回复、下回合先手");
        _flee_button.Disabled = true;
        _headline.Text = Enc.headline();
        _log.Clear();
        _action_label.Text = "";
        _result_label.Text = "";
        _status.Text = "";
    }

    /// <summary>场景里少一个节点就应该炸得很响（而不是界面上静悄悄缺一块）</summary>
    private Node _require_node(string path)
    {
        Node node = GetNodeOrNull(path);
        if (node == null)
        {
            GD.PushError($"battle_screen.tscn 缺少节点：{path}");
        }

        return node;
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

    public void _refresh()
    {
        _clear(_allies_box);
        _clear(_enemies_box);
        foreach (Variant actor in allies)
        {
            _allies_box.AddChild(_make_actor_row(actor));
        }

        foreach (Variant actor in enemies)
        {
            _enemies_box.AddChild(_make_actor_row(actor));
        }

        _round_label.Text = $"第 {System.Math.Max(sim.Call("rounds_played").AsInt32(), 1)} 回合　"
            + $"{(_sim_finished() ? "已结束" : "进行中")}";
        _refresh_buttons();
        _refresh_skills();
        _refresh_buffs();
        _refresh_intent_band();
    }

    /// <summary>这一行按当前模式显示：招式（默认）／已装内功／战斗道具</summary>
    private void _refresh_skills()
    {
        if (_skill_row == null)
        {
            return;
        }

        _clear(_skill_row);
        if (sim == null || _sim_finished())
        {
            return;
        }

        if (!sim.Call("in_round").AsBool())
        {
            // 不在这里偷偷开回合（会让「下一回合」的计数对不上）：等玩家点「下一回合」或直接点招式
            _skill_row.AddChild(new Label
            {
                Name = "SkillHint",
                Text = "点「下一回合」开打，或直接点招式先手出招",
            });
            return;
        }

        GodotObject actor = sim.Call("current_actor").AsGodotObject();
        if (actor == null)
        {
            return;
        }

        if (_panel_mode == "inner" && actor.Get("side").AsInt32() == _side_ally())
        {
            _add_inner_buttons(actor);
            return;
        }

        if (_panel_mode == "items")
        {
            _add_item_buttons();
            return;
        }

        bool ally_turn = actor.Get("side").AsInt32() == _side_ally();
        _skill_row.AddChild(new Label
        {
            Name = "SkillHint",
            Text = ally_turn
                ? $"{actor.Get("display_name").AsString()} 行动："
                : "敌方行动中（点「下一回合」自动推进）",
        });
        if (!ally_turn)
        {
            return;
        }

        // 用 skill_options：冷却中的招式也要列出来（灰着写清还差几回合），不然玩家以为招式没了
        foreach (Variant option_variant in sim.Call("skill_options", actor).AsGodotArray())
        {
            Godot.Collections.Dictionary option = option_variant.AsGodotDictionary();
            string skill_id = _s(option, "skill_id");
            string text;
            // 增益招式没有倍率与段数（它施放一次给自己上 buff），按钮上就直说「增益」
            if (_b(option, "support"))
            {
                text = $"{_s(option, "name")}（内力 {_i(option, "qi_cost")}｜增益）";
            }
            else
            {
                string hits = _i(option, "hit_count") <= 1 ? "" : $"｜{_i(option, "hit_count")} 段";
                text = $"{_s(option, "name")}（内力 {_i(option, "qi_cost")}｜"
                    + $"倍率 {_f(option, "power_ratio"):F1}{hits}）";
            }

            if (!_b(option, "ok"))
            {
                text += $"　— {_s(option, "reason")}";
            }

            string wanted = skill_id;
            _skill_row.AddChild(_make_button(
                $"SkillButton_{skill_id}", text, () => press_skill(wanted), !_b(option, "ok")));
        }
    }

    /// <summary>
    /// 内功列表（「内功」指令）：每部已装内功一个按钮，点一次催动。
    /// 内力消耗取自 `skill_passive.qi_cost`（按占格 ×6，设计 08 Q87），按钮上如实写出来。
    /// </summary>
    private void _add_inner_buttons(GodotObject actor)
    {
        Godot.Collections.Array options = sim.Call("passive_cast_options", actor).AsGodotArray();
        if (options.Count == 0)
        {
            _skill_row.AddChild(new Label { Name = "InnerHint", Text = "没装内功（在角色面板装配）" });
            return;
        }

        foreach (Variant option_variant in options)
        {
            Godot.Collections.Dictionary option = option_variant.AsGodotDictionary();
            string skill_id = _s(option, "skill_id");
            string buff_name = _s(option, "buff_name");
            string text = $"{_s(option, "name")}（占 {_i(option, "slot_cost")} 格｜"
                + $"内力 {_i(option, "qi_cost")}";
            if (buff_name.Length > 0)
            {
                text += $"｜催动→{buff_name}";
            }

            text += "）";
            if (!_b(option, "ok"))
            {
                text += $"　— {_s(option, "reason")}";
            }

            string wanted = skill_id;
            _skill_row.AddChild(_make_button(
                $"InnerButton_{skill_id}", text, () => press_cast_passive(wanted), !_b(option, "ok")));
        }
    }

    /// <summary>道具列表（「道具」指令）：`use_context=battle` 的道具，数量与「为什么用不了」都写清楚</summary>
    private void _add_item_buttons()
    {
        GodotObject inventory = current_state()?.Get("inventory").AsGodotObject();
        Godot.Collections.Array options = sim.Call("battle_item_options", inventory).AsGodotArray();
        if (options.Count == 0)
        {
            _skill_row.AddChild(new Label { Name = "ItemHint", Text = "没有战斗道具" });
            return;
        }

        foreach (Variant option_variant in options)
        {
            Godot.Collections.Dictionary option = option_variant.AsGodotDictionary();
            string item_id = _s(option, "item_id");
            string text = $"{_s(option, "name")} ×{_i(option, "qty")}（{_s(option, "desc")}）"
                + $"　— {_s(option, "reason")}";
            _skill_row.AddChild(_make_button(
                $"ItemButton_{item_id}", text, () => press_item_list(), true));
        }
    }

    /// <summary>
    /// 增益减益面板（08 UI 要求）：`status_effect` 与 `buff_def` **合并成一条列表**，
    /// 减益在前、按剩余回合升序；常驻／整场显示 ∞，可叠层的显示 ×N；悬浮给名称与效果。
    /// </summary>
    private void _refresh_buffs()
    {
        if (_buff_panel == null)
        {
            return;
        }

        _clear(_buff_panel);
        // 先把**所有单位**的条目合成一条列表再排一次：08 的排序是全局的
        // （减益在前、同类按剩余回合升序）——各排各的会让敌方的减益被我方增益压到下面。
        var rows = new Godot.Collections.Array();
        foreach (Godot.Collections.Array group in new[] { allies, enemies })
        {
            foreach (Variant actor_variant in group)
            {
                GodotObject actor = actor_variant.AsGodotObject();
                foreach (Variant row_variant in actor.Call("effect_rows", db).AsGodotArray())
                {
                    Godot.Collections.Dictionary entry = row_variant.AsGodotDictionary().Duplicate();
                    entry["actor_id"] = actor.Get("actor_id");
                    entry["actor_name"] = actor.Get("display_name");
                    entry["side_mark"] = actor.Get("side").AsInt32() == _side_ally() ? "我" : "敌";
                    rows.Add(entry);
                }
            }
        }

        foreach (Variant row_variant in BattleActor.sort_effect_rows(rows))
        {
            _buff_panel.AddChild(_make_effect_chip(row_variant.AsGodotDictionary()));
        }
    }

    public Button _make_effect_chip(Godot.Collections.Dictionary row)
    {
        var chip = new Button
        {
            Name = $"Buff_{_s(row, "actor_id", "?")}_{(_s(row, "entry_id").Length > 0 ? _s(row, "entry_id") : _s(row, "buff_id", "?"))}",
            Flat = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        chip.AddThemeFontSizeOverride("font_size", 12);
        bool is_debuff = _b(row, "is_debuff");
        int remaining = _i(row, "remaining");
        string time_text = _b(row, "permanent") || remaining < 0 ? "∞" : remaining.ToString();
        int stacks = _i(row, "stacks", 1);
        // 图标（15 §4.3 的「增益减益 19／异常状态 4」）：**优先表里的 `icon`，空则退回行 id**
        // ——与 `item_base.icon` 同一套优先级，所以美术按哪个名字交都能对上。
        // **有图才摆**：一张都还没交付时 `icon` 留 null，卡片尺寸与以前一模一样（不占空位）。
        string icon_id = _s(row, "icon").Trim();
        if (icon_id.Length == 0)
        {
            icon_id = _s(row, "entry_id").Length > 0 ? _s(row, "entry_id") : _s(row, "buff_id");
        }

        string icon_path = _s(row, "kind") == "status" ? IconPaths.status(icon_id) : IconPaths.buff(icon_id);
        chip.SetMeta("icon_path", icon_path);
        Texture2D icon_texture = IconPaths.load_icon(icon_path);
        if (icon_texture != null)
        {
            chip.Icon = icon_texture;
        }

        // 极性用「形状 + 字」两重编码（08 要求色盲可辨：光靠红蓝不够）
        // 名字要带上是谁的（合并列表里不写归属，玩家不知道这条 buff 挂在谁身上）
        chip.Text = $"{_s(row, "side_mark")} {(is_debuff ? "▼" : "▲")}{(is_debuff ? "减" : "增")}"
            + $"·{_s(row, "name")} {time_text}{(stacks <= 1 ? "" : $" ×{stacks}")}";
        // 极性配色（08：增益蓝、减益红）——另配「增／减」字标，色盲也能分辨
        chip.AddThemeColorOverride(
            "font_color", is_debuff ? new Color("#e57373") : new Color("#64b5f6"));
        chip.TooltipText = $"{_s(row, "actor_name")}·{_s(row, "name")}"
            + $"（{(is_debuff ? "减益" : "增益")}，剩余 {time_text}）{_s(row, "desc")}";
        return chip;
    }
}
