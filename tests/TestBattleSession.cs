using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tests;

/// <summary>
/// 分步战斗：逐回合推进、与一次性模拟一致、遭遇修正生效——
/// 对位 `tests/test_battle_session.gd`（2026-10-11 P4 第十批搬 C#）。
///
/// 这份盯的是**两条路走同一场架**：`step_round()` 逐回合推（战斗界面按回合播的那条）
/// 与 `simulate()` 一次跑完，同种子必须给出**逐字一样的日志**。分步那条一歪，
/// 界面看到的与结算算出来的就是两场架，而且不会报错。
/// </summary>
public partial class TestBattleSession : TestCase
{
    private const int Seed = 5150;

    public override string SuiteName() => "分步战斗与遭遇修正";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_stepwise_matches_simulate(db);
        _check_first_side(db);
        _check_surprise_bonus(db);
    }

    private Godot.Collections.Dictionary _new_fight(
        TableDb db, Encounter encounter = null, Godot.Collections.Dictionary options = null)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Array allies = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        Godot.Collections.Dictionary opts =
            options == null ? new Godot.Collections.Dictionary() : options.Duplicate();
        if (encounter != null)
        {
            opts["encounter"] = encounter;
        }

        sim.setup(allies, enemies, opts);
        return new Godot.Collections.Dictionary
        {
            { "sim", sim },
            { "allies", allies },
            { "enemies", enemies },
        };
    }

    private static Godot.Collections.Dictionary _fixed_mods() => new()
    {
        {
            "modifiers",
            new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } }
        },
    };

    private void _check_stepwise_matches_simulate(TableDb db)
    {
        Godot.Collections.Dictionary stepwise = _new_fight(db, null, _fixed_mods());
        BattleSimulator sim = stepwise["sim"].As<BattleSimulator>();
        CheckFalse(sim.finished(), "刚摆好没结束");
        CheckEq(sim.rounds_played(), 0, "还没打任何一回合");
        int guard = 0;
        while (!sim.finished() && guard < 60)
        {
            Godot.Collections.Array lines = sim.step_round();
            CheckGt(lines.Count, 0.0, "每回合至少有一行日志（含回合头）");
            guard += 1;
        }

        Godot.Collections.Dictionary stepped = sim.result();

        BattleSimulator once = BattleSimulator.create(db, RngService.create(Seed));
        Godot.Collections.Dictionary direct = once.simulate(
            PartyBuilder.build_actors(db, SoloState(db), new Godot.Collections.Array()),
            EnemyFactory.create(db).create_team("team_wolf_pack", "normal"),
            _fixed_mods());
        CheckEq(stepped["winner"].AsString(), direct["winner"].AsString(), "分步与一次跑完的胜负一致");
        CheckEq(stepped["rounds"].AsInt32(), direct["rounds"].AsInt32(), "回合数一致");
        CheckEq(_log_text(stepped["log"]), _log_text(direct["log"]), "日志完全一致（同种子确定）");
    }

    /// <summary>先手方：被发现的遭遇里，敌方应该抢在第一回合最前面。</summary>
    private void _check_first_side(TableDb db)
    {
        var spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        var team = db.get_row("enemy_team", "team_wolf_pack");
        Encounter spotted = Encounter.build(db, spawn, team, Encounter.contact_spotted(), "normal");
        Godot.Collections.Dictionary fight = _new_fight(db, spotted, _fixed_mods());
        BattleSimulator sim = fight["sim"].As<BattleSimulator>();
        Godot.Collections.Array lines = sim.step_round();
        var first_action = "";
        foreach (Variant line in lines)
        {
            if (line.AsString().StartsWith("R"))
            {
                first_action = line.AsString();
                break;
            }
        }

        CheckTrue(first_action.Contains("野狼"), $"被发现时第一手是敌方：{first_action}");

        Encounter front = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        Godot.Collections.Dictionary calm = _new_fight(db, front, _fixed_mods());
        Godot.Collections.Array calm_lines = calm["sim"].As<BattleSimulator>().step_round();
        var calm_first = "";
        foreach (Variant line in calm_lines)
        {
            if (line.AsString().StartsWith("R"))
            {
                calm_first = line.AsString();
                break;
            }
        }

        CheckTrue(calm_first.Contains("书生"), $"正面遭遇时身法高的我方先手：{calm_first}");
    }

    /// <summary>奇袭：首回合我方总伤害更高，第二回合回到常态。</summary>
    private void _check_surprise_bonus(TableDb db)
    {
        var spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        var team = db.get_row("enemy_team", "team_wolf_pack");
        Godot.Collections.Dictionary mods = _fixed_mods();
        Encounter front = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        Encounter sleep = Encounter.build(db, spawn, team, Encounter.contact_sleep(), "normal");

        Godot.Collections.Dictionary plain = _new_fight(db, front, mods);
        plain["sim"].As<BattleSimulator>().step_round();
        Godot.Collections.Dictionary surprise = _new_fight(db, sleep, mods);
        surprise["sim"].As<BattleSimulator>().step_round();
        double plain_damage = _ally_damage(plain["sim"].As<BattleSimulator>(), plain["allies"].AsGodotArray());
        double surprise_damage =
            _ally_damage(surprise["sim"].As<BattleSimulator>(), surprise["allies"].AsGodotArray());
        CheckGt(
            surprise_damage,
            plain_damage,
            $"奇袭首回合伤害更高（{(int)surprise_damage} > {(int)plain_damage}）");

        // 但奇袭也会让敌方抢先行动完，所以先手顺序不同——这不是矛盾，是奇袭=我方先手+增伤
        Godot.Collections.Array pending =
            surprise["sim"].As<BattleSimulator>().result()["pending_rules"].AsGodotArray();
        CheckTrue(
            _joined(pending, "；").Contains("无防备"),
            "奇袭同样把「敌方首回合无防备」记成暂缓规则（设计还没定它的效果，见 test_encounter）");
    }

    private double _ally_damage(BattleSimulator sim, Godot.Collections.Array allies)
    {
        Godot.Collections.Dictionary dealt = sim.result()["damage_dealt"].AsGodotDictionary();
        double total = 0.0;
        foreach (Variant entry in allies)
        {
            var actor = entry.AsGodotObject() as BattleActor;
            if (actor != null && dealt.ContainsKey(actor.actor_id))
            {
                total += dealt[actor.actor_id].AsDouble();
            }
        }

        return total;
    }

    /// <summary>
    /// 日志行拼成一段文本再比：C# 的数组相等是**引用比**，`CheckEq` 拿两个数组比永远判不等
    /// （对位 GDScript 的 `str(array)`，断言条数不变，见施工单坑⑤）。
    /// </summary>
    private static string _log_text(Variant log) => _joined(log.AsGodotArray(), "\n");

    private static string _joined(Godot.Collections.Array values, string separator)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant value in values)
        {
            parts.Add(value.AsString());
        }

        return string.Join(separator, parts);
    }
}
