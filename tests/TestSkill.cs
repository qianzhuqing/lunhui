using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 武学：总表与明细对应、武器类型限制、选招与熟练度接线、内力边界、DoT 招式可点——
/// 对位 `tests/test_skill.gd`（2026-10-11 P4 第十八批搬 C#）。
///
/// 五节：① 表规模与「总表 ↔ 明细表」对应（每部武学都要有对位的明细行，双向数得回来）；
/// ② 武器类型闸门（拿刀不能用剑法、通用招式随时能用、熟练度只加伤害不改优先级）；
/// ③ **「内力刚好够一招」的边界**（判定写 `qi_cost > qi` 才拒，所以**刚好相等必须能用**）；
/// ④ 敌人招式映射（毒手用五毒、醉刀客用自己的路数、所有敌人的招式都要有明细）；
/// ⑤ **DoT 招式必须能点出来**（2026-10-03 那处「六招永远没有按钮」的回归钉子）。
/// </summary>
public partial class TestSkill : TestCase
{
    private const int Seed = 31337;

    public override string SuiteName() => "武学：总表／明细／选招";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_tables(db);
        _check_weapon_gate(db);
        _check_qi_boundary(db);
        _check_enemy_skills(db);
        _check_dot_skills_usable(db);
    }

    private void _check_tables(TableDb db)
    {
        CheckEq(db.rows("skill_base").Count, 65, "第一章 65 部武学（0.31.0 的 5 部本命内功）");
        CheckEq(db.rows("skill_active").Count, 35, "35 部招式");
        CheckEq(db.rows("skill_passive").Count, 30, "30 部内功（0.31.0 的 5 部本命内功）");
        CheckEq(db.rows("skill_star_def").Count, 5, "五档星级");
        CheckEq(
            db.rows("growth_const").Count,
            20,
            "20 条成长常数（0.10.0 加木桩两条上限；0.16.0／0.17.0 加天赋 1 条与创建 3 条）");

        var actives = 0;
        var passives = 0;
        foreach (Variant entry in db.rows("skill_base"))
        {
            var row = (SkillBaseRow)entry.AsGodotObject();
            if (row.is_active())
            {
                actives += 1;
                CheckNotNull(db.get_row("skill_active", row.skill_id), $"{row.skill_id} 有招式明细");
            }
            else
            {
                passives += 1;
                CheckNotNull(db.get_row("skill_passive", row.skill_id), $"{row.skill_id} 有内功明细");
            }
        }

        CheckEq(actives, 35, "skill_kind 与明细表一致：35 部招式");
        CheckEq(passives, 30, "skill_kind 与明细表一致：30 部内功");

        // 通用招式（any）与绑定招式都在
        CheckTrue(
            ((SkillBaseRow)db.get_row("skill_base", "sk_common_01")).accepts_any_weapon(),
            "连环腿是通用招式");
        CheckFalse(
            ((SkillBaseRow)db.get_row("skill_base", "sk_xuanwei_01")).accepts_any_weapon(),
            "玄微剑法绑定武器");
        CheckEq(
            ((SkillBaseRow)db.get_row("skill_base", "sk_xuanwei_01")).weapon_type,
            "sword",
            "玄微剑法绑剑");
        // 内功占格 1~3
        CheckEq(
            ((SkillPassiveRow)db.get_row("skill_passive", "pf_xuanwei_05")).slot_cost,
            3,
            "★5 内功占 3 格");
        CheckEq(
            ((SkillPassiveRow)db.get_row("skill_passive", "pf_xuanwei_01")).slot_cost,
            1,
            "★1 内功占 1 格");
    }

    /// <summary>招式绑定武器类型：拿刀就不能用剑法，通用招式随时能用。</summary>
    private void _check_weapon_gate(TableDb db)
    {
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor actor = _actor();
        actor.skills = new[] { "sk_xuanwei_01", "sk_chensha_01", "sk_common_01" };
        actor.tags["weapon_type"] = "sword";
        var with_sword = simulator.pick_skill(actor) as SkillActiveRow;
        CheckEq(with_sword.skill_id, "sk_xuanwei_01", "剑在手时选到剑法");
        CheckNe(with_sword.skill_id, "sk_chensha_01", "拳法被武器类型挡住");

        actor.tags["weapon_type"] = "fist";
        var with_fist = simulator.pick_skill(actor) as SkillActiveRow;
        CheckEq(with_fist.skill_id, "sk_chensha_01", "换拳套后选到拳法");

        actor.tags["weapon_type"] = "";
        var unarmed = simulator.pick_skill(actor) as SkillActiveRow;
        CheckEq(unarmed.skill_id, "sk_common_01", "没武器只能用通用招式");

        // 熟练度更高时，同倍率的招式优先度不变（倍率才是排序依据），但伤害更高
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor target = _actor();
        target.side = BattleActor.side_enemy();
        Godot.Collections.Dictionary plain = resolver.resolve(
            actor, target, with_sword, new Godot.Collections.Dictionary
            {
                { "force_hit", true }, { "no_variance", true },
            });
        actor.skill_mastery[with_sword.skill_id] = 10;
        Godot.Collections.Dictionary trained = resolver.resolve(
            actor, target, with_sword, new Godot.Collections.Dictionary
            {
                { "force_hit", true }, { "no_variance", true },
            });
        CheckGt(trained["damage"].AsDouble(), plain["damage"].AsDouble(), "练过的招式打得更疼");
    }

    /// <summary>
    /// 「内力刚好够一招」：判定写的是 `qi_cost > qi` 才拒，所以**刚好相等必须能用**。
    /// 有人把它写成 `>=` 就会变成「内力明明够却说不够」，而玩家在耗到见底时最容易正好卡在这个数上。
    /// </summary>
    private void _check_qi_boundary(TableDb db)
    {
        BattleActor actor = _actor();
        // 拿一部**要耗内力**的招式当样本（sk_xuanwei_01 是起手式，qi_cost=0 验不出边界）
        var skill = (SkillActiveRow)db.get_row("skill_active", "sk_xuanwei_02");
        CheckNotNull(skill, "玄微剑法·进步有招式明细");
        int cost = skill.qi_cost;
        CheckGt(cost, 0.0, "这一招要耗内力（拿它当边界样本）");

        // 支付本身：刚好够 → true 且归零；差一点 → false 且不扣
        actor.qi = cost;
        CheckTrue(actor.spend_qi(cost), "内力刚好够：支付成功");
        CheckEq(actor.qi, 0, "刚好够时余额归零");
        actor.qi = cost - 1;
        CheckFalse(actor.spend_qi(cost), "差一点：支付失败");
        CheckEq(actor.qi, cost - 1, "支付失败不扣内力");

        // 选招与体检表：刚好够要算「可用」，差一点要写明「内力不足」
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        actor.skills = new[] { "sk_xuanwei_02" };
        actor.tags["weapon_type"] = "sword";
        actor.qi = cost;
        CheckTrue(_available(simulator, actor, "sk_xuanwei_02"), "刚好够内力：这一招在可用列表里");
        actor.qi = cost - 1;
        CheckFalse(_available(simulator, actor, "sk_xuanwei_02"), "差一点内力：不在可用列表里");
        Godot.Collections.Dictionary option = _option_of(simulator, actor, "sk_xuanwei_02");
        CheckFalse(!option.ContainsKey("ok") || option["ok"].AsBool(), "体检表也标成不可用");
        CheckTrue(
            _s(option, "reason").Contains("内力不足"),
            $"体检表写明内力不足：{_s(option, "reason")}");
    }

    private static bool _available(BattleSimulator simulator, BattleActor actor, string skillId)
    {
        foreach (Variant entry in simulator.available_skills(actor))
        {
            if (((SkillActiveRow)entry.AsGodotObject()).skill_id == skillId)
            {
                return true;
            }
        }

        return false;
    }

    private static Godot.Collections.Dictionary _option_of(
        BattleSimulator simulator, BattleActor actor, string skillId)
    {
        foreach (Variant entry in simulator.skill_options(actor))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (_s(option, "skill_id") == skillId)
            {
                return option;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    /// <summary>敌人招式映射：毒手用五毒，醉刀客用自己的路数（不再错用黑风刀法）。</summary>
    private void _check_enemy_skills(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        var poison_hand = factory.create_enemy("en_bd_poison_hand", "normal") as BattleActor;
        CheckNotNull(poison_hand, "毒手能造出来");
        if (poison_hand != null)
        {
            // 0.11.0：招式改由 `enemy_skill.csv` 配（设计 10 §一：毒手＝上毒 + 毒发，主动施加异常）
            CheckTrue(
                _Has(poison_hand.skills, "sk_wudu_02") && _Has(poison_hand.skills, "sk_wudu_03"),
                $"毒手带上毒＋毒发两招（enemy_skill.csv 的 sk_wudu_02／03）：{_Join(poison_hand.skills)}");
            CheckEq(poison_hand.skills.Length, 2, "毒手两招（杂兵 2 招是设计 10 的配表原则）");
        }

        var drunk = factory.create_enemy("en_hidden_drunk", "normal") as BattleActor;
        CheckNotNull(drunk, "醉刀客能造出来");
        if (drunk != null)
        {
            CheckTrue(_Has(drunk.skills, "sk_drunk_jiuzhongdao"), "醉刀客用醉里乾坤");
            CheckFalse(_Has(drunk.skills, "sk_boss_zhangfeng"), "醉刀客不再错用大寨主的招式");
        }

        var boss = factory.create_enemy("en_bd_boss", "normal") as BattleActor;
        CheckNotNull(boss, "大寨主能造出来");
        if (boss != null)
        {
            CheckTrue(_Has(boss.skills, "sk_boss_zhangfeng"), "大寨主用黑风刀法");
        }

        // 所有敌人的招式都必须在 skill_active 里有明细，否则选招会空转
        foreach (string enemy_id in new[]
                 {
                     "en_wolf", "en_boar", "en_bd_thug", "en_bd_scout", "en_bd_archer",
                     "en_bd_lone_wolf", "en_bd_patrol_leader", "en_bd_elite_blade",
                     "en_bd_poison_hand", "en_bd_butcher", "en_bd_boss",
                 })
        {
            var actor = factory.create_enemy(enemy_id, "normal") as BattleActor;
            if (actor == null)
            {
                continue;
            }

            foreach (string skill_id in actor.skills)
            {
                CheckNotNull(
                    db.get_row("skill_active", skill_id),
                    $"{enemy_id} 的招式 {skill_id} 有明细");
            }
        }
    }

    /// <summary>
    /// DoT 类招式（`damage_type = dot_*`）必须是**能点出来**的招式。
    ///
    /// 由来（2026-10-03）：`BattleSimulator._is_supported_damage()` 以前把 `category=dot` 一律当
    /// 「没接结算」挡掉，于是**五毒掌（瘴／蚀骨／化血）与烈火掌（引火／燎原／焚心）这 6 招永远没有按钮**，
    /// 自动战斗也不选——而 DoT 的结算其实早就完整（`resolve()` 的 dot 分支负责命中与挂层，
    /// `resolve_dot()` 快照每层伤害，回合末按 `status_effect` 跳字，`tests/TestStatusRules.cs` 一直在验）。
    /// **为什么以前没人发现**：那些用例是直接 `sim.act()` 放招的，**绕过了 `available_skills` 这道过滤**。
    /// </summary>
    private void _check_dot_skills_usable(TableDb db)
    {
        var dot_skills = new[]
        {
            "sk_wudu_02", "sk_wudu_03", "sk_wudu_04", // 五毒掌：中毒
            "sk_liehuo_01", "sk_liehuo_02", "sk_liehuo_03", // 烈火掌：灼伤
        };
        foreach (string skill_id in dot_skills)
        {
            var row = (SkillActiveRow)db.get_row("skill_active", skill_id);
            CheckNotNull(row, $"表里有 {skill_id}");
            if (row != null)
            {
                CheckTrue(
                    BattleSimulator.create(db, RngService.create(Seed))._is_supported_damage(row.damage_type),
                    $"{skill_id} 的伤害类型（{row.damage_type}）算「支持」");
            }
        }

        BattleActor actor = _actor(); // 不带 weapon_type 标记 → 不受武器类型限制（这几招是拳法）
        actor.skills = dot_skills;
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { actor },
            new Godot.Collections.Array(),
            new Godot.Collections.Dictionary());
        var usable = new System.Collections.Generic.List<string>();
        foreach (Variant entry in sim.skill_options(actor))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["ok"].AsBool())
            {
                usable.Add(_s(option, "skill_id"));
            }
        }

        CheckEq(usable.Count, dot_skills.Length, $"六招 DoT 全部可点（{string.Join(", ", usable)}）");
        foreach (string skill_id in dot_skills)
        {
            CheckTrue(usable.Contains(skill_id), $"{skill_id} 在可点列表里");
        }
    }

    private static BattleActor _actor()
    {
        var actor = new BattleActor
        {
            actor_id = "test_skill_actor",
            display_name = "测试角色",
            side = BattleActor.side_ally(),
            level = 10,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", 500.0 },
                { "qi_max", 100.0 },
                { "atk_phys", 100.0 },
                { "atk_qi", 50.0 },
                { "def_phys", 20.0 },
                { "def_qi", 20.0 },
                { "speed", 20.0 },
                { "hit_rate", 0.9 },
                { "dodge_rate", 0.1 },
                { "crit_rate", 0.0 },
                { "crit_dmg", 0.0 },
                { "pen_rate", 0.0 },
                { "block_rate", 0.0 },
                { "block_reduction", 0.0 },
                { "dmg_reduction", 0.0 },
            },
            base_accuracy = 0.0,
        };
        actor.refill();
        return actor;
    }

    private static bool _Has(string[] values, string wanted)
    {
        foreach (string value in values)
        {
            if (value == wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static string _Join(string[] values) => "[" + string.Join(", ", values) + "]";

    /// <summary>对位 GDScript 的 `dict.get(key, "")`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
