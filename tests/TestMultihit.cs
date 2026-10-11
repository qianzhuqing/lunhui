using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 多段攻击（`skill_active.hit_count`）与目标类型（`target_type=all_enemy`）——对位
/// `tests/test_multihit.gd`（2026-10-11 P4 第二批搬 C#）。
///
/// 设计 04：招式记录里有「攻击段数」；横扫是 3 段打全体、夜雨是 2 段单体、定军是 2 段全体。
/// 每段各掷一次命中／暴击／格挡，伤害与破架势逐段结算（打到死就不再补刀）。
/// </summary>
public partial class TestMultihit : TestCase
{
    private const int Seed = 20261003;

    /// <summary>确定性修饰：必命中、不格挡、不浮动，方便按倍率比数。</summary>
    private static readonly Godot.Collections.Dictionary Fixed = new()
    {
        { "force_hit", true },
        { "no_variance", true },
        { "no_block", true },
    };

    public override string SuiteName() => "多段攻击与目标类型";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_hit_count(db);
        _check_all_enemy(db);
        _check_poise_per_hit(db);
        _check_events(db);
        _check_single_hit_regression(db);
        _check_auto_pick_counts_area(db);
    }

    /// <summary>夜雨 2 段 × 倍率 2.5 = 起手的 5 倍（同一个人、同一个靶子）。</summary>
    private void _check_hit_count(TableDb db)
    {
        SkillActiveRow night_rain = _active(db, "sk_heifeng_01");
        CheckEq(night_rain.hit_count, 2, "黑风刀法·夜雨是 2 段");
        int baseline = _damage_of(db, "sk_xuanwei_01");
        int doubled = _damage_of(db, "sk_heifeng_01");
        CheckEq(
            doubled,
            (int)(baseline * 2.5 * 2),
            $"2 段 × 2.5 倍率 = 单段 1.0 的 5 倍（{doubled} vs {baseline}）");
    }

    /// <summary>横扫 3 段打全体：手选一个目标也要波及全场。</summary>
    private void _check_all_enemy(TableDb db)
    {
        SkillActiveRow sweep = _active(db, "sk_boss_hengsao");
        CheckEq(sweep.hit_count, 3, "黑风刀法·横扫是 3 段");
        CheckEq(sweep.target_type, "all_enemy", "横扫打全体");

        BattleActor attacker = MeleeActor(
            "hero", BattleActor.side_ally(), new Godot.Collections.Dictionary { { "atk_phys", 50.0 } });
        var enemies = new Godot.Collections.Array();
        for (int index = 0; index < 3; index++)
        {
            enemies.Add(MeleeActor(
                $"foe{index}",
                BattleActor.side_enemy(),
                new Godot.Collections.Dictionary { { "hp_max", 400.0 } }));
        }

        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            enemies,
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        sim.act(attacker, sweep, enemies[0].AsGodotObject() as BattleActor);
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as BattleActor;
            CheckLt(enemy.hp, 400.0, $"{enemy.actor_id} 也被横扫打到（气血 {enemy.hp}）");
        }
    }

    /// <summary>破架势逐段结算：3 段招式扣三倍的架势。</summary>
    private void _check_poise_per_hit(TableDb db)
    {
        SkillActiveRow sweep = _active(db, "sk_boss_hengsao");
        SkillActiveRow single = _active(db, "sk_xuanwei_01");
        BattleActor attacker = MeleeActor(
            "hero", BattleActor.side_ally(), new Godot.Collections.Dictionary { { "poise_break", 6.0 } });
        BattleActor target = MeleeActor(
            "dummy", BattleActor.side_enemy(), new Godot.Collections.Dictionary { { "hp_max", 5000.0 } });
        target.poise_max = 999;
        target.poise = 999;
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { target },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        int before_single = target.poise;
        sim.act(attacker, single, target);
        int single_loss = before_single - target.poise;
        int before_sweep = target.poise;
        sim.act(attacker, sweep, target);
        int sweep_loss = before_sweep - target.poise;
        // 每段的破架势 = 招式破架势 + 攻击者破架势（横扫自己的 poise_damage 与起手不同，按表算）
        int per_hit = sweep.poise_damage + (int)attacker.stat("poise_break");
        CheckEq(sweep_loss, per_hit * 3, $"3 段招式按段结算破架势（{sweep_loss} = 3 × {per_hit}）");
        CheckEq(
            single_loss,
            single.poise_damage + (int)attacker.stat("poise_break"),
            "单段招式一段结算");
    }

    /// <summary>事件带段序号：界面靠它错开跳字。</summary>
    private void _check_events(TableDb db)
    {
        BattleActor attacker = MeleeActor(
            "hero", BattleActor.side_ally(), new Godot.Collections.Dictionary { { "atk_phys", 40.0 } });
        BattleActor target = MeleeActor(
            "dummy", BattleActor.side_enemy(), new Godot.Collections.Dictionary { { "hp_max", 900.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { target },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        sim.act(attacker, _active(db, "sk_heifeng_01"), target);

        var hits = new Godot.Collections.Dictionary();
        foreach (Variant event_variant in sim.take_events())
        {
            Godot.Collections.Dictionary hit_event = event_variant.AsGodotDictionary();
            if (_s(hit_event, "kind") == "hit")
            {
                hits[_i(hit_event, "hit_index", -1)] = _i(hit_event, "hit_count");
            }
        }

        CheckEq(hits.Count, 2, $"两段各产生一个事件：{hits}");
        CheckTrue(hits.ContainsKey(0) && hits.ContainsKey(1), "段序号是 0 与 1");
        CheckEq(hits.ContainsKey(0) ? hits[0].AsInt32() : 0, 2, "事件里记着总段数");
    }

    /// <summary>回归：单段招式仍然只打一段、只出一个事件。</summary>
    private void _check_single_hit_regression(TableDb db)
    {
        BattleActor attacker = MeleeActor(
            "hero", BattleActor.side_ally(), new Godot.Collections.Dictionary { { "atk_phys", 40.0 } });
        BattleActor target = MeleeActor(
            "dummy", BattleActor.side_enemy(), new Godot.Collections.Dictionary { { "hp_max", 900.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { target },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        Godot.Collections.Array lines = sim.act(attacker, _active(db, "sk_xuanwei_01"), target);
        int hit_events = 0;
        foreach (Variant event_variant in sim.take_events())
        {
            Godot.Collections.Dictionary hit_event = event_variant.AsGodotDictionary();
            if (_s(hit_event, "kind") == "hit")
            {
                hit_events += 1;
            }
        }

        CheckEq(hit_events, 1, "单段招式只出一个命中事件");
        CheckEq(lines.Count, 1, "单段招式只有一行日志");
        CheckFalse(lines[0].AsString().Contains("段"), $"单段日志不写段数：{lines[0].AsString()}");
    }

    /// <summary>
    /// 自动策略按「倍率 × 段数 × 人数」排序：
    /// 横扫 1.3×3 段 = 3.9（单体也一样比斩风的 2.1 高，因为它是 3 段），三个人时更是 11.7。
    /// </summary>
    private void _check_auto_pick_counts_area(TableDb db)
    {
        BattleActor boss = MeleeActor(
            "boss", BattleActor.side_enemy(), new Godot.Collections.Dictionary { { "atk_phys", 40.0 } });
        boss.skills = new[] { "sk_boss_zhangfeng", "sk_boss_hengsao" };
        BattleActor single_foe = MeleeActor("solo", BattleActor.side_ally());
        var sim_one = BattleSimulator.create(db, RngService.create(Seed));
        sim_one.setup(
            new Godot.Collections.Array { single_foe },
            new Godot.Collections.Array { boss },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        SkillActiveRow sweep_row = _active(db, "sk_boss_hengsao");
        SkillActiveRow single_row = _active(db, "sk_boss_zhangfeng");
        CheckFloat(sim_one._skill_score(boss, sweep_row), 3.9, "横扫 1 人时权重 1.3×3 段", 0.001);
        CheckFloat(sim_one._skill_score(boss, single_row), 2.1, "斩风权重就是 2.1", 0.001);
        CheckEq(
            (sim_one.pick_skill(boss) as SkillActiveRow)?.skill_id,
            "sk_boss_hengsao",
            "段数也算进来：3 段横扫（3.9）压过单段斩风（2.1）");

        var crowd = new Godot.Collections.Array();
        for (int index = 0; index < 3; index++)
        {
            crowd.Add(MeleeActor($"ally{index}", BattleActor.side_ally()));
        }

        var sim_many = BattleSimulator.create(db, RngService.create(Seed));
        sim_many.setup(
            crowd,
            new Godot.Collections.Array { boss },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        CheckFloat(sim_many._skill_score(boss, sweep_row), 11.7, "横扫打 3 人权重 1.3×3 段×3 人", 0.001);
        CheckEq(
            (sim_many.pick_skill(boss) as SkillActiveRow)?.skill_id,
            "sk_boss_hengsao",
            "三个人时还是横扫（11.7 ≫ 2.1）");
    }

    /// <summary>打一发并返回造成的伤害（同一个攻击者／靶子，保证可比）。</summary>
    private int _damage_of(TableDb db, string skill_id)
    {
        BattleActor attacker = MeleeActor(
            "hero", BattleActor.side_ally(), new Godot.Collections.Dictionary { { "atk_phys", 50.0 } });
        BattleActor target = MeleeActor(
            "dummy", BattleActor.side_enemy(), new Godot.Collections.Dictionary { { "hp_max", 100000.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { target },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        sim.act(attacker, _active(db, skill_id), target);
        return 100000 - target.hp;
    }

    private static SkillActiveRow _active(TableDb db, string skill_id) =>
        db.get_row("skill_active", skill_id) as SkillActiveRow;

    // `Godot.Collections.Dictionary` 没有 `Get(key, 默认值)`（坑④），照仓库里那套 `_s`／`_i` 自己包一层。
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;
}
