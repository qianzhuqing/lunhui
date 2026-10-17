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
        _check_qi_cost_mul(db);
        _check_mastery_bonus(db);
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

        // 选招与体检表：刚好够要算「可用」，差一点要写明「内力不足」。
        // **判定用的是乘过系数的实际气耗**（`SkillCost`，与真扣气同一处算法）——表里写 8，
        // 实际扣 5；这里必须拿实际值当边界，否则测的是"表里那个数"，而战斗根本不用它。
        int effective = SkillCost.effective_qi_cost(db, cost, true);
        CheckLt(effective, cost, "实际气耗比表里的裸值低（乘过系数）");
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        actor.skills = new[] { "sk_xuanwei_02" };
        actor.tags["weapon_type"] = "sword";
        actor.qi = effective;
        CheckTrue(_available(simulator, actor, "sk_xuanwei_02"), "刚好够内力：这一招在可用列表里");
        actor.qi = effective - 1;
        CheckFalse(_available(simulator, actor, "sk_xuanwei_02"), "差一点内力：不在可用列表里");
        Godot.Collections.Dictionary option = _option_of(simulator, actor, "sk_xuanwei_02");
        CheckFalse(!option.ContainsKey("ok") || option["ok"].AsBool(), "体检表也标成不可用");
        CheckTrue(
            _s(option, "reason").Contains("内力不足"),
            $"体检表写明内力不足：{_s(option, "reason")}");
        CheckEq(
            option["qi_cost"].AsInt32(), effective,
            "体检表里写的也是实际气耗（按钮上那个数就是会扣的数）");
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
    /// <summary>
    /// 招式气耗系数（设计 04 §招式循环，2026-10-13 P28）：实际气耗 = `qi_cost` × `combat_const.qi_cost_mul`，
    /// 四舍五入、**非零下限 1**、`0` 仍 `0`——降气耗靠**一处调参**，不去逐行改 35 个数。
    ///
    /// 顺带钉住「每部内功都有一条 `stat:qi_regen`、且不低于占格 ×1.0」这条数据口径：
    /// 界面与战斗都从这一列读续航，漏一行就是"这部内功白装了"，而**漏了不会报错**。
    /// </summary>
    private void _check_qi_cost_mul(TableDb db)
    {
        CheckFloat(SkillCost.qi_cost_mul(db), 0.6, "气耗系数读 `combat_const.qi_cost_mul`");
        CheckEq(SkillCost.effective_qi_cost(db, 0, true), 0, "0 气招仍是 0（不被下限抬成 1）");
        CheckEq(SkillCost.effective_qi_cost(db, 1, true), 1, "1 气招四舍五入仍 1（非零下限）");
        CheckEq(SkillCost.effective_qi_cost(db, 8, true), 5, "8 × 0.6 = 4.8 → 5");
        CheckEq(SkillCost.effective_qi_cost(db, 12, true), 7, "12 × 0.6 = 7.2 → 7");
        CheckEq(SkillCost.effective_qi_cost(db, 35, true), 21, "35 × 0.6 = 21");

        // **只作用我方**（设计 04 §招式循环，2026-10-13 拍板）：敌人那一侧一分不减。
        // 这条不是"顺手补一条"——统一降耗会把敌人的节奏（预兆／拆招那套的输入）抹平，
        // 实测把 `TestEndToEnd` 那一场从 17 回合险胜翻成 6 回合落败（连跑 13 颗种子一致）。
        CheckEq(SkillCost.effective_qi_cost(db, 8, false), 8, "敌人不乘系数（＝表里原值）");
        CheckEq(SkillCost.effective_qi_cost(db, 35, false), 35, "敌人也不吃四舍五入");
        CheckEq(SkillCost.effective_qi_cost(db, 0, false), 0, "敌人的 0 气招仍是 0");

        // 表里每一招都真的按系数降了（别只在个别招上对）；0 气那三招是「替换普攻」那一档（P30）
        var scaled = 0;
        var free = 0;
        foreach (Variant row_variant in db.rows("skill_active"))
        {
            var row = row_variant.As<SkillActiveRow>();
            if (row.qi_cost <= 0)
            {
                free += 1;
                continue;
            }

            int effective = SkillCost.effective_qi_cost(db, row.qi_cost, true);
            CheckTrue(
                effective >= 1 && effective <= row.qi_cost,
                $"{row.skill_id}：实际气耗 {effective} 要落在 1..{row.qi_cost} 之间");
            scaled += 1;
        }

        CheckEq(free, 4, "0 气那四招（玄微起手／通用招式／箭矢／醉步）仍是 0");
        CheckEq(scaled, 31, "35 招里 31 招要内力");

        // 每部内功都要有内力回复，且不低于占格 ×1.0
        var checked_passives = 0;
        foreach (Variant row_variant in db.rows("skill_passive"))
        {
            var passive = row_variant.As<SkillPassiveRow>();
            double line = passive.slot_cost * 1.0;
            double value = 0.0;
            foreach (Variant stat_variant in db.rows("skill_passive_stat"))
            {
                var stat = stat_variant.As<SkillPassiveStatRow>();
                if (stat.skill_id == passive.skill_id && stat.target == "stat:qi_regen")
                {
                    value = stat.value;
                }
            }

            CheckTrue(
                value >= line,
                $"{passive.skill_id}：内力回复 {value} 要不低于占格 ×1.0（{line}）");
            checked_passives += 1;
        }

        CheckEq(checked_passives, 30, "30 部内功逐部过一遍");
    }

    /// <summary>
    /// 内功满级签名（设计 08 §内功满级签名，2026-10-13 拍板）：新表 30 行、`mastery` 一律满级；
    /// **门槛＝满级才生效**（练到 9 级一分不给）；`stat:` 那三条真进贡献通道；
    /// 每条 `effect:` 标记都得在某一条名单里（**漏进名单＝一条签名静静地不生效**，
    /// 而玩家只会觉得"练满了怎么没变化"）。
    /// </summary>
    private void _check_mastery_bonus(TableDb db)
    {
        CheckEq(db.rows("skill_mastery_bonus").Count, 30, "30 部内功各有一条满级签名");
        int mastery_max = MasteryBonus.mastery_max(db);
        CheckEq(mastery_max, 10, "满级熟练度取 `growth_const.mastery_max`");
        foreach (Variant row_variant in db.rows("skill_mastery_bonus"))
        {
            var row = row_variant.As<SkillMasteryBonusRow>();
            CheckEq(row.mastery, mastery_max, $"{row.skill_id} 的签名挂在满级档");
            Godot.Collections.Dictionary parsed = row.parsed_target();
            if (parsed["kind"].AsString() != "effect")
            {
                continue;
            }

            CheckTrue(
                MasteryBonus.known(parsed["target_id"].AsString()),
                $"{row.skill_id} 的标记 {parsed["target_id"].AsString()} 在接线名单里"
                    + "（漏进名单＝这条签名静静地不生效）");
        }

        CheckEq(
            MasteryBonus.WIRED_MARKERS.Length, 21,
            "已接线 21 条（30 条签名 − 3 条 `stat:` − 丙那 6 条等试玩）");
        CheckEq(
            MasteryBonus.WIRED_MARKERS[0], "ignore_internal_qi",
            "接线名单头一条是甲那档的「内伤不再让内力回复减半」");
        CheckEq(
            System.Array.IndexOf(MasteryBonus.WIRED_MARKERS, "no_break_window") >= 0, true,
            "「厚土」也在已接线名单里（伤害结算那一簇）");
        CheckEq(MasteryBonus.QUEUED_MARKERS.Length, 0, "本批待接那一栏已经清空（甲＋乙都接上了）");
        CheckEq(
            MasteryBonus.QUEUED_MARKERS.Length + MasteryBonus.WIRED_MARKERS.Length
                + MasteryBonus.AFTER_PLAYTEST_MARKERS.Length,
            27, "三条名单加起来正好 27 条 ＝ 30 条签名里除 3 条 `stat:` 之外的全部"
                + "（漏一条就是签名静静地不生效）");
        CheckEq(
            MasteryBonus.AFTER_PLAYTEST_MARKERS.Length, 6,
            "设计明写「等第一章试玩后再排」的丙那 6 条");

        // `stat:` 三条：满级才生效，差一级都不给
        GrowthCalculator growth = GrowthCalculator.create(db);
        CheckEq(
            _stat_contrib(
                growth.passive_contributions(
                    new[] { "pf_heifeng_03" },
                    new Godot.Collections.Dictionary { { "pf_heifeng_03", mastery_max } }),
                "poise_break_mul"),
            0.5, "悍勇满级给 `poise_break_mul` +0.5");
        CheckEq(
            _stat_contrib(
                growth.passive_contributions(
                    new[] { "pf_heifeng_03" },
                    new Godot.Collections.Dictionary { { "pf_heifeng_03", mastery_max - 1 } }),
                "poise_break_mul"),
            0.0, "差一级（熟练 9）一分不给");
        CheckEq(
            _stat_contrib(
                growth.passive_contributions(
                    new[] { "pf_heifeng_01" },
                    new Godot.Collections.Dictionary { { "pf_heifeng_01", mastery_max } }),
                "basic_hit"),
            1.0, "蛮力满级给 `basic_hit` +1");
        CheckEq(
            _stat_contrib(
                growth.passive_contributions(
                    new[] { "pf_xuanwei_05" },
                    new Godot.Collections.Dictionary { { "pf_xuanwei_05", mastery_max } }),
                "extra_hit"),
            1.0, "太清满级给 `extra_hit` +1");

        // 普攻与招式的段数是**两条**：普攻吃 `basic_hit`、招式吃 `extra_hit`（Q84）
        CheckEq(
            BattleSimulator.effective_hits_for(
                BattleSimulator.basic_attack_id(), 1, 2.0, 1.0), 2,
            "普攻段数吃 `basic_hit`（蛮力 → 2 段）");
        CheckEq(
            BattleSimulator.effective_hits_for(BattleSimulator.basic_attack_id(), 1, 2.0, 0.0), 1,
            "普攻**不吃** `extra_hit`（Q84：否则套装白送普攻流）");
        CheckEq(
            BattleSimulator.effective_hits_for("sk_xuanwei_02", 1, 1.0, 9.0), 2,
            "招式只吃 `extra_hit`（`basic_hit` 对它无效）");

        // 面板那句话：满级写效果、没满写还差几级、接没接如实说
        string full_text = MasteryBonus.describe(db, "pf_xuanwei_05", mastery_max)["text"].AsString();
        CheckTrue(
            full_text.Contains("满级效果") && full_text.Contains("招式段数 +1"),
            $"满级那条写得出效果：{full_text}");
        CheckFalse(
            full_text.Contains("还没接"),
            $"已经接线的不写「还没接」：{full_text}");
        string almost_text = MasteryBonus.describe(db, "pf_xuanwei_05", mastery_max - 1)["text"].AsString();
        CheckTrue(
            almost_text.Contains("再练 1 级"), $"没满级写还差几级：{almost_text}");
        CheckTrue(
            MasteryBonus.describe(db, "pf_chensha_02", mastery_max)["text"].AsString().Contains("还没接"),
            "满级了但代码还没接的，面板如实写「还没接」（只剩丙那 6 条了）");
    }

    /// <summary>从贡献列表里挑出某条 `stat:` 的合计值（`stat:extra_hit` 这类）。</summary>
    private static double _stat_contrib(Godot.Collections.Array contributions, string stat_id)
    {
        double total = 0.0;
        foreach (Variant item in contributions)
        {
            Godot.Collections.Dictionary contribution = item.AsGodotDictionary();
            string target = contribution.ContainsKey("target")
                ? contribution["target"].AsString()
                : "";
            if (target == stat_id && _s(contribution, "kind") == AttributeCalculator.contrib_stat_flat())
            {
                total += contribution["value"].AsDouble();
            }
        }

        return total;
    }

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
