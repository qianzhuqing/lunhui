using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的六指令与回合推进——从 `BattleScreen.cs` 拆出来的 partial
/// （那份要守 800 行硬上限）。
///
/// 「下一回合」只推进到**轮到我方**（敌方自动跑），这样才能手选招式；自动战斗才是一次打到底。
/// 手选指令的公共开场（必要时开新回合、推进到轮到我方）集中在 `_begin_round_for_command()`。
/// </summary>
public partial class BattleScreen
{
    /// <summary>
    /// 「玩家点任意指令立刻接管」（设计 11 §四的准话）。
    ///
    /// 自动战斗是省事不是夺权：只要玩家动一下任何指令，本场就退出自动，
    /// 让他把接下来的回合握在自己手里。所有手选指令与目标切换都要先过这里。
    /// </summary>
    public bool take_over()
    {
        if (!auto_enabled)
        {
            return false;
        }

        auto_enabled = false;
        _refresh_buttons();
        return true;
    }

    public Godot.Collections.Array press_next_round()
    {
        if (sim == null || _sim_finished())
        {
            return new Godot.Collections.Array();
        }

        // 语义：推进到「轮到我方行动」为止（敌方自动跑），这样才能手选招式；
        // 「自动战斗」才是一次打到底。
        var lines = new Godot.Collections.Array();
        if (!sim.Call("in_round").AsBool())
        {
            sim.Call("begin_round");
            lines.Add($"—— 第 {sim.Call("rounds_played").AsInt32()} 回合 ——");
            lines.AddRange(_intent_lines());
        }

        lines.AddRange(_advance_until_ally());
        // 自动战斗：轮到我方时**也要自己出招**。
        // 以前这里只推回合，而 `_advance_until_ally()` 碰到我方就停——自动战斗会在第一个
        // 玩家回合永远卡住（`--battle-selftest` 的注释「曾经把自检挂死」说的就是这件事，
        // 但游戏里的「自动战斗」按钮一直踩这个坑：只有敌方回合会自动跑）。
        // 我方出招走 `sim.auto_act()`，和敌方同一条路：策略（保守／均衡／全力）在 `pick_skill` 里生效。
        if (auto_enabled && sim.Call("in_round").AsBool() && !_sim_finished())
        {
            GodotObject actor = sim.Call("current_actor").AsGodotObject();
            if (actor != null && actor.Get("side").AsInt32() == _side_ally())
            {
                lines.AddRange(sim.Call("auto_act").AsGodotArray());
                lines.AddRange(_advance_until_ally());
            }
        }

        Godot.Collections.Array events = sim.Call("take_events").AsGodotArray();
        _append_log(lines);
        _refresh();
        // 顺序很重要：先重建卡片，再挂跳字/反馈——反过来会被 _refresh() 清掉
        _spawn_floats(events);
        _lunge_attackers(events);
        if (_sim_finished())
        {
            _settle();
        }

        return lines;
    }

    /// <summary>手选招式：轮到我方时点招式按钮，打血量最低的敌人</summary>
    public Godot.Collections.Dictionary press_skill(string skill_id)
    {
        if (sim == null || _sim_finished())
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "战斗已结束" } };
        }

        take_over();
        var lines = new Godot.Collections.Array();
        if (!sim.Call("in_round").AsBool())
        {
            sim.Call("begin_round");
            lines.Add($"—— 第 {sim.Call("rounds_played").AsInt32()} 回合 ——");
            lines.AddRange(_intent_lines());
        }

        lines.AddRange(_advance_until_ally());
        GodotObject actor = sim.Call("current_actor").AsGodotObject();
        if (actor == null || actor.Get("side").AsInt32() != _side_ally())
        {
            _append_log(lines);
            _refresh();
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        Variant chosen = default;
        foreach (Variant active in sim.Call("available_skills", actor).AsGodotArray())
        {
            if (_prop_str(active, "skill_id") == skill_id)
            {
                chosen = active;
                break;
            }
        }

        if (chosen.VariantType == Variant.Type.Nil)
        {
            _set_status("这一招现在用不了（内力不足或武器不符）");
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "招式不可用" } };
        }

        GodotObject target = _current_target();
        if (target == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可打的目标" } };
        }

        lines.AddRange(sim.Call("act", actor, chosen, target).AsGodotArray());
        Godot.Collections.Array events = sim.Call("take_events").AsGodotArray();
        _append_log(lines);
        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        _spawn_floats(events);
        _lunge_attackers(events);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "skill_id", skill_id }, { "target", target.Get("actor_id") },
        };
    }

    /// <summary>
    /// 拆招（设计 04 核心循环）：花掉这次行动读破某个敌人的预兆招式 —— 那一招伤害减半，
    /// 自己反涨架势。和出招互斥（都要花掉这一次行动）。
    /// </summary>
    public Godot.Collections.Dictionary press_parry(string enemy_id)
    {
        if (sim == null || _sim_finished())
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "战斗已结束" } };
        }

        take_over();
        var lines = new Godot.Collections.Array();
        if (!sim.Call("in_round").AsBool())
        {
            sim.Call("begin_round");
            lines.Add($"—— 第 {sim.Call("rounds_played").AsInt32()} 回合 ——");
            lines.AddRange(_intent_lines());
        }

        lines.AddRange(_advance_until_ally());
        GodotObject actor = sim.Call("current_actor").AsGodotObject();
        if (actor == null || actor.Get("side").AsInt32() != _side_ally())
        {
            _append_log(lines);
            _refresh();
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        GodotObject target = null;
        foreach (Variant enemy_variant in enemies)
        {
            if (_prop_str(enemy_variant, "actor_id") == enemy_id)
            {
                target = enemy_variant.AsGodotObject();
                break;
            }
        }

        Godot.Collections.Dictionary result = sim.Call("parry", actor, target).AsGodotDictionary();
        lines.AddRange(_v(result, "lines").AsGodotArray());
        _append_log(lines);
        if (_b(result, "ok"))
        {
            _set_status($"拆招：{actor.Get("display_name").AsString()} 读破 "
                + $"{target.Get("display_name").AsString()} 的招（这一招伤害减半），"
                + $"架势 +{_i(result, "poise_gain")}");
        }
        else
        {
            _set_status($"拆不了招：{_s(result, "error")}");
        }

        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        return result;
    }

    /// <summary>新回合的预兆播报（敌方出招前有一回合预兆，玩家据此决定出招还是拆招）</summary>
    private Godot.Collections.Array _intent_lines()
    {
        var out_lines = new Godot.Collections.Array();
        foreach (string line in sim.Call("intent_lines").AsStringArray())
        {
            out_lines.Add($"预兆：{line}");
        }

        return out_lines;
    }

    /// <summary>
    /// 敌方预兆**横带**（设计 14 §四 要点一：「预兆必须显眼——藏在角落等于这套机制没做，
    /// 它要占一整条横带」）。
    ///
    /// 这一条以前只贴在敌人卡片里、再往日志里写一行，玩家得在两处找；现在 `Action` 这个
    /// 整宽标签就是预兆带：一条一句，把这一回合**所有活着的敌人**要出的招并排写出来。
    /// 日志仍照常记（回看用），但**不再用它顶这一行**——两处显示同一件事没有必要。
    /// </summary>
    private void _refresh_intent_band()
    {
        if (_action_label == null)
        {
            return;
        }

        if (sim == null || _sim_finished())
        {
            _action_label.Text = "";
            return;
        }

        string[] lines = sim.Call("intent_lines").AsStringArray();
        _action_label.Text = lines.Length == 0
            ? "敌方预兆：这一回合没有敌人要出招"
            : $"敌方预兆：{string.Join("　｜　", lines)}";
    }

    /// <summary>
    /// 六指令里那几条「花一次行动」的按钮共用这个开场：必要时开新回合、推进到轮到我方。
    /// 不是我方行动就把日志补上并返回 null（调用方只管报错，不再各写一遍开场）。
    /// </summary>
    private GodotObject _begin_round_for_command()
    {
        _command_lines = new Godot.Collections.Array();
        if (sim == null || _sim_finished())
        {
            _set_status("战斗已结束");
            return null;
        }

        // 推进到轮到我方：可能这一回合正好在敌方行动后跑完——那就把回合末结算跑掉、开新回合继续
        // （最多 4 轮兜底，正常情况下一次就够）
        int guard = 0;
        while (guard < 4)
        {
            guard += 1;
            if (!sim.Call("in_round").AsBool())
            {
                sim.Call("begin_round");
                if (_sim_finished())
                {
                    break;
                }

                _command_lines.Add($"—— 第 {sim.Call("rounds_played").AsInt32()} 回合 ——");
                _command_lines.AddRange(_intent_lines());
            }

            _command_lines.AddRange(_advance_until_ally());
            GodotObject actor = sim.Call("current_actor").AsGodotObject();
            if (actor != null && actor.Get("side").AsInt32() == _side_ally())
            {
                return actor;
            }

            if (_sim_finished() || actor != null)
            {
                break;
            }
        }

        _append_log(_command_lines);
        _refresh();
        _set_status(_sim_finished() ? "战斗已结束" : "现在不是我方行动");
        return null;
    }

    /// <summary>普通攻击（六指令之一）：不耗内力、不看武器与招式装配，打当前目标</summary>
    public Godot.Collections.Dictionary press_basic_attack()
    {
        take_over();
        GodotObject actor = _begin_round_for_command();
        if (actor == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        GodotObject target = _current_target();
        if (target == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可打的目标" } };
        }

        Godot.Collections.Array lines = _command_lines.Duplicate();
        lines.AddRange(sim.Call("basic_attack", actor, target).AsGodotArray());
        Godot.Collections.Array events = sim.Call("take_events").AsGodotArray();
        _append_log(lines);
        _set_status($"{actor.Get("display_name").AsString()} 普通攻击 {target.Get("display_name").AsString()}");
        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        _spawn_floats(events);
        _lunge_attackers(events);
        return new Godot.Collections.Dictionary { { "ok", true }, { "target", target.Get("actor_id") } };
    }

    /// <summary>主动防御（六指令之一）：花一次行动换一个防御姿态 buff（减伤、架势、下回合先手）</summary>
    public Godot.Collections.Dictionary press_defend()
    {
        take_over();
        GodotObject actor = _begin_round_for_command();
        if (actor == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        Godot.Collections.Dictionary result = sim.Call("defend", actor).AsGodotDictionary();
        Godot.Collections.Array lines = _command_lines.Duplicate();
        lines.AddRange(_v(result, "lines").AsGodotArray());
        _append_log(lines);
        _set_status(_b(result, "ok")
            ? $"{actor.Get("display_name").AsString()} 进入防御姿态（本回合减伤、下回合先手，"
                + $"架势 +{_i(result, "poise_gain")}）"
            : $"防不了：{_s(result, "error")}");
        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        return result;
    }

    /// <summary>内功指令：把招式行切成「已装内功」列表（再点一次切回招式）</summary>
    public Godot.Collections.Dictionary press_inner_list()
    {
        take_over();
        _panel_mode = _panel_mode == "inner" ? "skills" : "inner";
        _refresh_skills();
        return new Godot.Collections.Dictionary { { "ok", true }, { "mode", _panel_mode } };
    }

    /// <summary>道具指令：把招式行切成「战斗中可用的道具」列表（效果列未配，条目会写明原因）</summary>
    public Godot.Collections.Dictionary press_item_list()
    {
        take_over();
        _panel_mode = _panel_mode == "items" ? "skills" : "items";
        _refresh_skills();
        return new Godot.Collections.Dictionary { { "ok", true }, { "mode", _panel_mode } };
    }

    /// <summary>催动某部内功：花一次行动换它的运功 buff（buff_grant trigger=on_cast）</summary>
    public Godot.Collections.Dictionary press_cast_passive(string skill_id)
    {
        take_over();
        GodotObject actor = _begin_round_for_command();
        if (actor == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "现在不是我方行动" } };
        }

        Godot.Collections.Dictionary result = sim.Call("cast_passive", actor, skill_id).AsGodotDictionary();
        Godot.Collections.Array lines = _command_lines.Duplicate();
        lines.AddRange(_v(result, "lines").AsGodotArray());
        _append_log(lines);
        _set_status(_b(result, "ok")
            ? $"{actor.Get("display_name").AsString()} 催动内功（运功 buff 已生效）"
            : $"催不动：{_s(result, "error")}");
        _panel_mode = "skills";
        if (_sim_finished())
        {
            _settle();
        }

        _refresh();
        return result;
    }

    /// <summary>当前轮到我方行动的人（没有就返回 null）</summary>
    private GodotObject _current_ally_actor()
    {
        if (sim == null || _sim_finished() || !sim.Call("in_round").AsBool())
        {
            return null;
        }

        GodotObject actor = sim.Call("current_actor").AsGodotObject();
        return actor != null && actor.Get("side").AsInt32() == _side_ally() ? actor : null;
    }

    /// <summary>敌方自动跑，直到轮到我方（或本回合结束）</summary>
    private Godot.Collections.Array _advance_until_ally()
    {
        var lines = new Godot.Collections.Array();
        int guard = 0;
        while (sim.Call("in_round").AsBool() && guard < 20)
        {
            GodotObject actor = sim.Call("current_actor").AsGodotObject();
            if (actor == null || actor.Get("side").AsInt32() == _side_ally())
            {
                break;
            }

            lines.AddRange(sim.Call("auto_act").AsGodotArray());
            guard += 1;
        }

        return lines;
    }

    /// <summary>选中的敌人还活着就用它，否则退回「血量最低」</summary>
    private GodotObject _current_target()
    {
        if (selected_target_id.Length > 0)
        {
            foreach (Variant enemy_variant in enemies)
            {
                GodotObject enemy = enemy_variant.AsGodotObject();
                if (enemy.Get("actor_id").AsString() == selected_target_id
                    && enemy.Call("is_alive").AsBool())
                {
                    return enemy;
                }
            }
        }

        return _lowest_hp_enemy();
    }

    private GodotObject _lowest_hp_enemy()
    {
        GodotObject best = null;
        foreach (Variant enemy_variant in enemies)
        {
            GodotObject enemy = enemy_variant.AsGodotObject();
            if (!enemy.Call("is_alive").AsBool())
            {
                continue;
            }

            if (best == null || enemy.Get("hp").AsDouble() < best.Get("hp").AsDouble())
            {
                best = enemy;
            }
        }

        return best;
    }

    /// <summary>点敌人卡片上的按钮选目标</summary>
    public void select_target(string actor_id)
    {
        take_over();
        selected_target_id = selected_target_id != actor_id ? actor_id : "";
        GodotObject target = _current_target();
        _set_status($"目标：{(target != null ? target.Get("display_name").AsString() : "无")}");
        _refresh();
    }
}
