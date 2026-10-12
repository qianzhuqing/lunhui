using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的界面绑定与刷新（partial，守 800 行硬上限）。
///
/// 骨架在 `scenes/battle_screen.tscn` 里（真实节点树，编辑器／MCP 可直接调版式与皮肤），
/// 这里只做查找、接线与刷新。单位与头顶信息是运行时生成的动态内容，见 `BattleScreenActors.cs`。
///
/// 这一版是**整屏 · 无壳**（22 §1.1）：没有顶栏、没有两侧卡片列表、没有增减益面板、
/// 没有预兆横带、没有日志区、没有工具行——只有战场 ＋ 贴底那一条指令行。
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
        _stage_root = _require_node("StageRoot") as Node2D;
        _boss_bar = _require_node("BossBar") as Control;
        _boss_name = _require_node("BossBar/BossName") as Label;
        _boss_phase = _require_node("BossBar/BossPhase") as Label;
        _boss_poise_label = _require_node("BossBar/BossPoiseLabel") as Label;
        _boss_aff = _require_node("BossBar/BossAff") as HBoxContainer;
        _boss_seg = _require_node("BossBar/BossSeg") as HBoxContainer;
        _boss_poise = _require_node("BossBar/BossPoise") as HBoxContainer;
        _turn_bar = _require_node("TurnBar") as VBoxContainer;
        _log_button = _require_node("LogButton") as Button;
        _log_panel = _require_node("LogPanel") as PanelContainer;
        _log = _require_node("LogPanel/Log") as RichTextLabel;
        _round_label = _require_node("RoundLabel") as Label;
        _status = _require_node("MsgLine") as Label;
        _layer = _require_node("Layer") as PanelContainer;
        _skill_row = _require_node("Layer/LayerScroll/SkillRow") as GridContainer;
        _command_row = _require_node("CommandRow") as HBoxContainer;
        _pause_bar = _require_node("PauseBar") as HBoxContainer;
        _basic_button = _require_node("CommandRow/BasicAttackButton") as Button;
        _skill_button = _require_node("CommandRow/SkillButton") as Button;
        _inner_button = _require_node("CommandRow/InnerButton") as Button;
        _item_button = _require_node("CommandRow/ItemButton") as Button;
        _defend_button = _require_node("CommandRow/DefendButton") as Button;
        _flee_button = _require_node("CommandRow/FleeButton") as Button;
        _result_confirm = _require_node("ResultConfirm") as Button;
        // 结算面板（22 §1.6 第八版）：面板那棵树在场景里，六块内容结算时逐块建（见 `BattleScreenSettlePanel.cs`）
        _bind_settle_panel();
        _auto_button = _require_node("PauseBar/AutoButton") as Button;
        _speed_button = _require_node("PauseBar/SpeedButton") as Button;
        _strategy_button = _require_node("PauseBar/StrategyButton") as Button;
        _report_button = _require_node("PauseBar/ReportButton") as Button;
        // `_enemies_box` 是给用例按名字找战场单位用的（整屏之后"敌方列表"就是 `StageRoot` 本身）
        _enemies_box = _stage_root;
        // 结算卡的「确定」＝离场（用户口径：点确定就等于返回）；`press_return()` 是 void，直连即可。
        // 其余回调返回非 void 的那些要包一层 lambda（CS0407）。
        _result_confirm.Pressed += press_return;
        // 「用完就收」（22 §1.6）：暂停条里按了任何一下，它自己收起来，不占着地方
        _auto_button.Pressed += () => { press_auto(); close_pause(); };
        _speed_button.Pressed += () => { toggle_speed(); close_pause(); };
        _strategy_button.Pressed += () => { press_strategy(); close_pause(); };
        _flee_button.Pressed += () => press_flee();
        _basic_button.Pressed += () => press_basic_attack();
        _skill_button.Pressed += () => press_skill_list();
        _inner_button.Pressed += () => press_inner_list();
        _item_button.Pressed += () => press_item_list();
        _defend_button.Pressed += () => press_defend();
        _log_button.Pressed += toggle_log_panel;
        _report_button.Pressed += () => { toggle_log_panel(); close_pause(); };
        // 六指令的说明写在 meta 上（`_refresh_buttons()` 每次会按当前状态覆盖 tooltip）
        _basic_button.SetMeta("hint", "六指令之一：不耗内力、不看武器，打当前目标");
        _skill_button.SetMeta("hint", "六指令之一：展开已装配的招式，选一招打出去");
        _inner_button.SetMeta("hint", "六指令之一：查看已装内功，催动一次换运功 buff");
        _item_button.SetMeta("hint", "六指令之一：战斗中可用的道具");
        _defend_button.SetMeta("hint", "六指令之一：本回合减伤、架势回复、下回合先手");
        _flee_button.Disabled = true;
        _result_confirm.TooltipText = "离开结算，回到刚才那张地图";
        _log.Clear();
        _status.Text = Enc != null ? Enc.headline() : "";
        _settle_focus_taken = false;
        _settle_hud_applied = false;
        // 战报面板默认**开着**：它就是示意源里右上那块「近 5 条」，勾掉的只是常驻的一整条日志区
        _log_panel.Visible = true;
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
        _build_backdrop();
        _refresh_units();
        _round_label.Text = $"第 {System.Math.Max(sim != null ? sim.Call("rounds_played").AsInt32() : 1, 1)} 回合"
            + $"　{(_sim_finished() ? "已结束" : "进行中")}";
        _refresh_buttons();
        _refresh_skills();
        _refresh_boss_bar();
        _refresh_turn_bar();
        _apply_settle_hud();
    }

    /// <summary>
    /// 结算屏上**只剩结算面板 ＋ 它那枚「确定」**（22 §1.6）：回合标签、那一行状态播报、Boss 长血条
    /// 这一组整组收起——它们是从上半场留下来的，压在 66% 的压暗底下面会从面板旁边透出来
    /// （第八版真窗口实测：「第 3 回合　已结束」与贴顶血条在结算面板边上若隐若现）。没打完照常显示。
    ///
    /// **战报**（面板右缘外那一条）：结算那一帧把它**收起来**，但留着右边那枚「战报」钮——面板只摆
    /// 得下 8 格掉落，全量明细在战报里，收掉入口等于把口径②那句「放不下的进战报」变成"看不见"。
    /// 那枚钮按下去会走 `toggle_log_panel()`，它自带的 `MoveToFront()` 把战报盖到面板**之上**，
    /// 想要全量明细的玩家能整块读（所以钮也要提到压暗底之上，别让它在底下若隐若现看不出能按）。
    /// </summary>
    private void _apply_settle_hud()
    {
        bool done = sim != null && _sim_finished();
        if (_round_label != null)
        {
            _round_label.Visible = !done;
        }

        if (_status != null)
        {
            _status.Visible = !done;
        }

        // Boss 长血条由 `_refresh_boss_bar()` 每次都刷成「有 Boss 就显示」，这里只在结算时压掉它
        if (done && _boss_bar != null)
        {
            _boss_bar.Visible = false;
        }

        // 这两下只做一次：玩家结算后自己把战报按出来，不该被下一次 `_refresh()` 又收回去
        if (done && !_settle_hud_applied)
        {
            _settle_hud_applied = true;
            if (_log_panel != null)
            {
                _log_panel.Visible = false;
            }

            _log_button?.MoveToFront();
        }
    }

    /// <summary>把战场上的单位按版式重摆一遍（节点是每次刷新重建的，与以前的卡片同一套口径）。</summary>
    private void _refresh_units()
    {
        if (_stage_root == null)
        {
            return;
        }

        foreach (Node child in _stage_root.GetChildren())
        {
            if (child is Control unit && unit.Name.ToString().StartsWith("Actor_", System.StringComparison.Ordinal))
            {
                _stage_root.RemoveChild(unit);
                unit.QueueFree();
            }
        }

        _units_by_id.Clear();
        foreach ((GodotObject actor, Vector2 position, int size) in _placements())
        {
            Control unit = _make_unit(actor, size);
            unit.Position = position;
            _stage_root.AddChild(unit);
            _units_by_id[actor.Get("actor_id").AsString()] = unit;
        }

        _stage_reset(false);
    }

    /// <summary>
    /// 招式／内功／道具的**展开浮层**（22 §1.6：贴底浮层 120 高、两列格、可滚动，
    /// 与指令行**互斥**）。`_panel_mode` 为空就是收起——收起时指令行常驻。
    /// </summary>
    private void _refresh_skills()
    {
        if (_skill_row == null || _layer == null || _command_row == null || _pause_bar == null
            || _result_confirm == null)
        {
            return;
        }

        // 对位 `_refresh_buttons()` 的同名量：**没开局（sim == null）不算打完**——
        // 不然 `_bind_ui()` 那一下会先把指令行收掉、把「确定」露出来（结算屏在开局前不该在）。
        bool done = sim != null && _sim_finished();
        if (done)
        {
            _pause_open = false; // 打完了就把暂停条收掉（结算屏上它没意义）
        }

        bool open = _panel_mode.Length > 0 && !_pause_open && sim != null && !done;
        _layer.Visible = open;
        // 打完之后指令行整条退场，画面下缘换成结算卡那一枚「确定」（用户口径：点确定＝返回）
        _command_row.Visible = !open && !_pause_open && !done;
        _pause_bar.Visible = _pause_open;
        // 打完之后整屏换成结算面板（§1.6）：指令行／暂停条／展开浮层都不在场，只剩面板 ＋ 那枚「确定」
        if (_settle_root != null)
        {
            _settle_root.Visible = done;
        }

        _result_confirm.Visible = done;
        if (done && !_settle_focus_taken && _result_confirm != null)
        {
            // 「确定」是**默认焦点**（§1.6：确认键就等效于按它）——只抢一次，别每帧把焦点拽回来
            _settle_focus_taken = true;
            _result_confirm.GrabFocus();
        }
        _clear(_skill_row);
        if (!open)
        {
            return;
        }

        GodotObject actor = sim.Call("in_round").AsBool()
            ? sim.Call("current_actor").AsGodotObject()
            : null;
        if (actor == null)
        {
            _panel_mode = "";
            _layer.Visible = false;
            _command_row.Visible = !_pause_open;
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
        if (!ally_turn)
        {
            _skill_row.AddChild(new Label
            {
                Name = "SkillHint",
                Text = "敌方行动中（点 ▶ 推进）",
            });
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
            // 道具的效果数值列还没配（设计 08 Q89 的读取端等 Q33），所以按钮一律置灰、
            // 理由写在按钮上——但**列表照样列出来**，玩家看得见自己有什么。
            _skill_row.AddChild(_make_button(
                $"ItemButton_{item_id}", text, () => press_item_list(), true));
        }
    }

    /// <summary>战报面板：右侧居中的那枚钮开合它（默认收起——整屏战场不摆常驻日志）。</summary>
    public void toggle_log_panel()
    {
        if (_log_panel == null)
        {
            return;
        }

        _log_panel.Visible = !_log_panel.Visible;
        if (_log_panel.Visible)
        {
            _log_panel.MoveToFront();
        }
    }
}
