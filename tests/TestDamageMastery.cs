using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 伤害结算管线那一族里的**内功满级签名**（设计 08 §内功满级签名，2026-10-13）：
/// 甲＋乙 24 条接完之后的成对断言（装着与没装差在哪）——从 `TestDamage.cs` 拆出来的 partial
/// （那一份顶到了 800 行硬上限，这一簇按职责整块搬出来，**只挪位置不改行为**）。
/// </summary>
public partial class TestDamage
{
    /// 内功满级签名 · **伤害结算那一簇**（设计 08 §内功满级签名，2026-10-13 B2）：
    /// 受击（铁骨／厚土）／命中（定军·命中 ＋ 拆招架势）／暴击（忘忧）／穿透（开碑）／
    /// 连招（知白）／闪避（醉意／影）共 9 条。
    ///
    /// 每条都**成对**验：装着满级签名与没装差在哪——只验一边的话，代码写成恒真也照样过。
    /// 签名只认两件事：`passives`（装上才生效）与熟练度（满级才给），所以夹具就是这两样。
    /// </summary>
    private void _CheckMasterySignatures(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true), ("no_variance", true));

        // 穿透（开碑 `pf_chensha_03`，+15%）：只对**满血**目标算
        CheckFloat(
            _SigActor(db, true, "pf_chensha_03").mastery_marker("pen_vs_full"), 0.15,
            "签名读得到（`passives` ＋ 熟练满级两样都在）");
        int plain_dmg = resolver.resolve(
            _SigActor(db, true), _FullHealth(db), skill, mods)["damage"].AsInt32();
        int pen_dmg = resolver.resolve(
            _SigActor(db, true, "pf_chensha_03"), _FullHealth(db), skill, mods)["damage"].AsInt32();
        CheckGt(pen_dmg, plain_dmg, $"「开碑」对满血目标穿透 +15%：{plain_dmg} → {pen_dmg}");

        // 受击减伤（铁骨 `pf_heifeng_02`，气血 <30% 时 +20%）
        BattleActor low_plain = _SigActor(db, false);
        low_plain.hp = (int)(low_plain.max_hp() * 0.2);
        BattleActor low_guard = _SigActor(db, false, "pf_heifeng_02");
        low_guard.hp = (int)(low_guard.max_hp() * 0.2);
        int low_plain_dmg = resolver.resolve(_SigActor(db, true), low_plain, skill, mods)["damage"].AsInt32();
        int low_guard_dmg = resolver.resolve(_SigActor(db, true), low_guard, skill, mods)["damage"].AsInt32();
        CheckLt(low_guard_dmg, low_plain_dmg, $"「铁骨」低血减伤 20%：{low_plain_dmg} → {low_guard_dmg}");
        BattleActor healthy_plain = _FullHealth(db);
        BattleActor healthy_guard = _FullHealth(db);
        _WithSignature(healthy_guard, "pf_heifeng_02");
        CheckEq(
            resolver.resolve(_SigActor(db, true), healthy_guard, skill, mods)["damage"].AsInt32(),
            resolver.resolve(_SigActor(db, true), healthy_plain, skill, mods)["damage"].AsInt32(),
            "「铁骨」只在血线以下生效（满血时与没装一样）");

        // 破绽窗口（厚土 `pf_chensha_01`）：被打进破绽时**不吃**那 50% 增伤。
        // 这一条走的是结算那一层（`_act_once` 加 `dmg_up`），所以用 `act()` 真打一次。
        int broken_plain = _BrokenDamage(db, false);
        int broken_guard = _BrokenDamage(db, true);
        CheckLt(broken_guard, broken_plain, $"「厚土」不吃破绽那 50%：{broken_plain} → {broken_guard}");

        // 命中（定军 `pf_tieqiang_02`，+20%）与闪避（醉意 `pf_drunk_01`，有减益时 +15%）
        // 先把命中率压到不贴上限（0.99）——贴了上限就量不出 +20% 那一段
        BattleActor dodgy = _SigActor(db, false);
        dodgy.stats["dodge_rate"] = 0.45;
        double base_chance = resolver.hit_chance(_SigActor(db, true), dodgy);
        CheckGt(
            resolver.hit_chance(_SigActor(db, true, "pf_tieqiang_02"), dodgy),
            base_chance, "「定军」给攻方命中 +20%");
        BattleActor debuffed = _SigActor(db, false);
        debuffed.add_status("poison", "test", 1.0, 3, 3);
        CheckTrue(debuffed.has_debuff(db), "夹具身上真有减益（中毒是异常状态，也算减益）");
        CheckTrue(debuffed.has_affliction(), "夹具身上真有异常状态（忘忧读的是这一条）");
        CheckGt(
            resolver.hit_chance(_SigActor(db, true), _WithSignature(debuffed, "pf_drunk_01")), base_chance,
            "「醉意」在身上有减益时给闪避 +15%");
        CheckFloat(
            resolver.hit_chance(_SigActor(db, true), _SigActor(db, false, "pf_drunk_01")),
            resolver.hit_chance(_SigActor(db, true), _SigActor(db, false)),
            "没有减益就不给（这条是有条件的）");

        // 暴击（忘忧 `pf_drunk_02`，打带异常的目标 +25%）：命中率那类比值不好直接读，
        // 用同一颗种子连打 20 次——暴击率 0 的人一次都不暴击，装了签名至少暴击一次。
        // ⚠️ **现数据里没有任何招式是 `can_crit` 的**（`dmg_normal` 全是 0、也没人用 `dmg_crit`），
        // 所以这里现造一条 `dmg_crit` 的招式行来验这条签名——真数据里它现在打不出来，已报策划。
        var crit_skill = new SkillActiveRow
        {
            skill_id = "sk_xuanwei_03",
            damage_type = "dmg_crit",
            power_ratio = 1.0,
            hit_count = 1,
            target_type = "single",
        };
        int crit_hits = 0;
        int crit_hits_marked = 0;
        var crit_mods = _Mods(("force_hit", true), ("no_variance", true));
        for (int round = 0; round < 20; round++)
        {
            BattleActor afflicted = _SigActor(db, false);
            afflicted.add_status("poison", "test", 1.0, 3, 3);
            BattleActor plain_critter = _SigActor(db, true);
            plain_critter.stats["crit_rate"] = 0.0;
            if (resolver.resolve(plain_critter, afflicted, crit_skill, crit_mods)["is_crit"].AsBool())
            {
                crit_hits += 1;
            }

            BattleActor marked = _SigActor(db, true, "pf_drunk_02");
            marked.stats["crit_rate"] = 0.0;
            BattleActor afflicted_marked = _SigActor(db, false);
            afflicted_marked.add_status("poison", "test", 1.0, 3, 3);
            if (resolver.resolve(marked, afflicted_marked, crit_skill, crit_mods)["is_crit"].AsBool())
            {
                crit_hits_marked += 1;
            }
        }

        CheckEq(crit_hits, 0, $"暴击率 0 的人一次都暴不了（{crit_hits}/20）");
        CheckEq(crit_hits_marked > 0, true, $"「忘忧」：打中毒目标暴击率 +25%（{crit_hits_marked}/20）");

        // 减益伤害（不还 `pf_chensha_buhuan`，每个减益 +8%）
        BattleActor debuff_plain = _SigActor(db, true);
        BattleActor debuff_marked = _SigActor(db, true, "pf_chensha_buhuan");
        foreach (BattleActor actor in new[] { debuff_plain, debuff_marked })
        {
            actor.add_status("poison", "test", 1.0, 3, 3);
        }

        CheckGt(
            resolver.resolve(debuff_marked, _FullHealth(db), skill, mods)["damage"].AsInt32(),
            resolver.resolve(debuff_plain, _FullHealth(db), skill, mods)["damage"].AsInt32(),
            "「不还」：身上一个减益 → 伤害 +8%");

        // 连招（知白 `pf_xuanwei_zhbai`）：这一招该吃的那一档由 `_record_use()` 推进，
        // 这里直接量"加成接不接进伤害"这一步（推进那一步在 `_CheckChainAdvance` 里量）。
        BattleActor chain_marked = _SigActor(db, true, "pf_xuanwei_zhbai");
        chain_marked.chain_damage_bonus = 0.15;
        CheckGt(
            resolver.resolve(chain_marked, _FullHealth(db), skill, mods)["damage"].AsInt32(), plain_dmg,
            "「知白」连招那一档加成进得了伤害");

        // 影（`pf_qingluo_ying`）：每场第一次被攻击必定闪避，且只免一次
        BattleActor shadow = _SigActor(db, false, "pf_qingluo_ying");
        Godot.Collections.Dictionary first = resolver.resolve(_SigActor(db, true), shadow, skill, _Mods(("no_variance", true)));
        CheckFalse(first["is_hit"].AsBool(), "「影」：第一次被攻击必定闪避");
        CheckEq(shadow.first_dodge_used, true, "这一次闪避会被记下来（每场只免一次）");
    }

    /// <summary>
    /// 内功满级签名 · **资源与回合 ＋ 异常**那一簇（设计 08，2026-10-13 B2b）：
    /// 引气（每场第一招不耗内力）／凝神（内力低时回气翻倍）／周天（没出招则回合末回气翻倍）／
    /// 回春（血低时气血与内力都翻倍）／遗篇（命中后退三成气）／百草（回合开始清 1 层减益）／
    /// 引毒（普攻附带中毒）／腐肌（中毒目标外防 −10%）／蚀脉（异常无视抗性）／万蛊（挂层 +2）。
    ///
    /// 同样成对验（装着与没装差在哪）。「守一」（受击掉架势减半）在 `_CheckPoiseGuard` 里。
    /// </summary>
    private void _CheckMasterySignaturesRound(TableDb db)
    {
        // 凝神 / 周天 / 回春：都在 `end_of_round_regen()` 这一处收口，直接调它比开一场省事
        BattleActor plain_regen = _SigActor(db, true);
        plain_regen.stats["qi_regen"] = 4.0;
        plain_regen.stats["hp_regen"] = 10.0;
        plain_regen.qi = 1;
        plain_regen.hp = 10;
        plain_regen.acted_this_round = true;
        plain_regen.end_of_round_regen();
        int plain_qi_gain = plain_regen.qi - 1;
        int plain_hp_gain = plain_regen.hp - 10;
        CheckEq(plain_qi_gain, 4, $"没签名的回气就是 `qi_regen`（{plain_qi_gain}）");

        BattleActor hold = _WithSignature(_SigActor(db, true), "pf_xuanwei_04");
        hold.stats["qi_regen"] = 4.0;
        hold.qi = 1;
        hold.acted_this_round = false;
        hold.end_of_round_regen();
        CheckEq(hold.qi - 1, 8, "「周天」：本回合没出招 → 回合末回气翻倍");

        BattleActor focused = _WithSignature(_SigActor(db, true), "pf_xuanwei_02");
        focused.stats["qi_regen"] = 4.0;
        focused.qi = System.Math.Max(1, (int)(focused.max_qi() * 0.1));
        focused.acted_this_round = true;
        int focused_before = focused.qi;
        focused.end_of_round_regen();
        CheckEq(focused.qi - focused_before, 8, "「凝神」：内力低于两成 → 回气翻倍");

        BattleActor spring = _WithSignature(_SigActor(db, true), "pf_tiaoxi_huichun");
        spring.stats["qi_regen"] = 4.0;
        spring.stats["hp_regen"] = 10.0;
        spring.qi = 1;
        spring.hp = System.Math.Max(1, (int)(spring.max_hp() * 0.3));
        spring.acted_this_round = true;
        int spring_hp = spring.hp;
        spring.end_of_round_regen();
        CheckEq(spring.qi - 1, 8, "「回春」：气血低于一半 → 回气也翻倍");
        CheckEq(spring.hp - spring_hp, 20, "「回春」：气血回复同样翻倍");

        // 百草：回合开始清掉自身 1 层减益（清的是层，不是整条）
        BattleActor herbalist = _WithSignature(_SigActor(db, true), "pf_yaowang_03");
        herbalist.add_status("poison", "test", 1.0, 5, 3);
        herbalist.add_status("poison", "test", 1.0, 5, 3);
        int stacks_before = herbalist.status_stacks("poison");
        CheckEq(herbalist.cleanse_one_debuff(), 1, "「百草」：清掉 1 层减益");
        CheckEq(herbalist.status_stacks("poison"), stacks_before - 1, "清的是**层**（3 层 → 2 层）");
        CheckEq(_SigActor(db, true).cleanse_one_debuff(), 0, "没这条签名就清不掉（成对验）");

        // 引气 / 遗篇 / 引毒：都要真出一招（走 `act()` 那条路）
        var skill_row = db.get_row("skill_active", "sk_xuanwei_02");
        BattleSimulator plain_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor plain_hero = _SigActor(db, true);
        BattleActor plain_foe = _FullHealth(db);
        int plain_qi = plain_hero.qi;
        plain_sim.act(plain_hero, skill_row, plain_foe);
        CheckEq(
            plain_qi - plain_hero.qi,
            SkillCost.effective_qi_cost(db, skill_row.Get("qi_cost").AsInt32(), true),
            "没签名：出招扣的就是实际气耗");

        BattleSimulator free_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor free_hero = _WithSignature(_SigActor(db, true), "pf_xuanwei_01");
        BattleActor free_foe = _FullHealth(db);
        int free_qi = free_hero.qi;
        free_sim.act(free_hero, skill_row, free_foe);
        CheckEq(free_hero.qi, free_qi, "「引气」：每场第一招不耗内力");
        CheckEq(free_hero.first_move_done, true, "这一场「第一次出招」已经用掉了（第二招就要扣气）");

        BattleSimulator refund_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor refund_hero = _WithSignature(_SigActor(db, true), "pf_yipian_01");
        BattleActor refund_foe = _FullHealth(db);
        // 用**中毒那一招**（`can_dodge = 0`）：退气的前提是"命中"，而毒招必中，这条断言才与 RNG 无关
        // 气血池先留出空位：满气时"退三成"一点也退不进去（`gain_qi` 照上限夹），
        // 那是**对的**行为——不满气才看得出这条签名在不在
        refund_hero.qi = refund_hero.max_qi() - 20;
        int refund_before = refund_hero.qi;
        CheckFloat(
            refund_hero.mastery_marker("qi_refund"), 0.30,
            "退气那条签名读得到（否则是没装上、不是没接）");
        refund_sim.act(refund_hero, _PoisonSkill(db), refund_foe);
        int cost = SkillCost.effective_qi_cost(
            db, _PoisonSkill(db).Get("qi_cost").AsInt32(), true);
        CheckEq(
            refund_hero.qi,
            refund_before - cost + (int)System.Math.Floor(cost * 0.30),
            $"「遗篇·归元」：命中后退该招气耗的三成（{refund_before} − {cost} ＋ 退三成）");

        BattleSimulator poison_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor venom_hero = _WithSignature(_SigActor(db, true), "pf_wudu_01");
        BattleActor venom_foe = _FullHealth(db);
        venom_foe.hp = venom_foe.max_hp();
        venom_foe.stats["dodge_rate"] = -9.0;
        venom_hero.stats["hit_rate"] = 9.0;
        poison_sim.basic_attack(venom_hero, venom_foe);
        CheckGt(venom_foe.status_stacks("poison"), 0.0, "「引毒」：普攻也附带中毒");

        // 腐肌（中毒目标外防 −10%）与蚀脉（异常无视抗性）、万蛊（挂层 +2）
        BattleActor armored = _FullHealth(db);
        armored.add_status("poison", "test", 1.0, 5, 3);
        int poisoned_plain = DamageResolver.create(db, RngService.create(Seed))
            .resolve(_SigActor(db, true), armored, skill_row, _Mods(("force_hit", true), ("no_variance", true)))["damage"].AsInt32();
        int poisoned_cut = DamageResolver.create(db, RngService.create(Seed))
            .resolve(_WithSignature(_SigActor(db, true), "pf_wudu_02"), armored, skill_row,
                _Mods(("force_hit", true), ("no_variance", true)))["damage"].AsInt32();
        CheckGt(poisoned_cut, poisoned_plain, $"「腐肌」：中毒目标外防 −10% → 伤害更高：{poisoned_plain} → {poisoned_cut}");

        var poison_skill = _PoisonSkill(db);
        BattleSimulator chance_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor resistant = _SigActor(db, false);
        resistant.stats["res_poison"] = 0.5;
        double plain_chance = chance_sim.status_chance(_SigActor(db, true), resistant, poison_skill);
        BattleSimulator pierce_sim = BattleSimulator.create(db, RngService.create(Seed));
        double pierced_chance = pierce_sim.status_chance(
            _WithSignature(_SigActor(db, true), "pf_wudu_03"), resistant, poison_skill);
        CheckGt(pierced_chance, plain_chance, $"「蚀脉」：异常无视抗性：{plain_chance:F3} → {pierced_chance:F3}");

        BattleSimulator burst_sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor burst_hero = _WithSignature(_SigActor(db, true), "pf_wudu_04");
        // 异常能不能上是一条几率（毒 35%）——把「异常触发率」拉满，这一条就与 RNG 无关了
        burst_hero.stats["debuff_chance"] = 5.0;
        BattleActor burst_foe = _FullHealth(db);
        burst_sim.act(burst_hero, poison_skill, burst_foe);
        CheckGt(burst_foe.status_stacks("poison"), 1.0, "「万蛊」：附加异常时直接 +2 层");
    }

    /// <summary>带 `status_id` 的一招（用五毒的招式验挂层那几条）。</summary>
    private static Resource _PoisonSkill(TableDb db)
    {
        foreach (Variant row_variant in db.rows("skill_active"))
        {
            var row = row_variant.As<SkillActiveRow>();
            if (row.status_id == "poison")
            {
                return row;
            }
        }

        return db.get_row("skill_active", "sk_xuanwei_02");
    }

    /// <summary>「守一」：受击时架势掉落减半。</summary>
    private void _CheckPoiseGuard(TableDb db)
    {
        BattleActor plain = _SigActor(db, false);
        plain.poise = plain.max_poise();
        int plain_hit = plain.take_poise_damage(40);
        BattleActor guarded = _WithSignature(_SigActor(db, false), "pf_xuanwei_03");
        guarded.poise = guarded.max_poise();
        int guarded_hit = guarded.take_poise_damage(40);
        CheckEq(plain_hit, 40, "没签名：40 点架势伤害全额吃下");
        CheckEq(guarded_hit, 20, "「守一」：受击时架势掉落减半（40 → 20）");
    }

    /// <summary>打进破绽之后挨一击掉多少血（`with_guard` ＝ 守方装着「厚土」）。</summary>
    private int _BrokenDamage(TableDb db, bool with_guard)
    {
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor hero = _SigActor(db, true);
        BattleActor foe = with_guard ? _SigActor(db, false, "pf_chensha_01") : _SigActor(db, false);
        foe.take_poise_damage(9999);
        CheckEq(foe.is_broken(), true, "夹具真的被打进破绽了");
        int before = foe.hp;
        sim.act(hero, db.get_row("skill_active", "sk_xuanwei_03"), foe);
        return before - foe.hp;
    }

    /// <summary>
    /// 造一个**带 db** 的战斗单位（签名要查 `skill_mastery_bonus`，`_db` 空就读不到），
    /// 认得出「装着这部满级内功」——签名只认 `passives`（装上才生效）与熟练度（满级才给）这两样。
    /// 攻守两侧都走这一个口，成对比较才不会把"两边基础数值不同"当成签名生效。
    /// </summary>
    private static BattleActor _SigActor(TableDb db, bool attacker, string signature_skill_id = "")
    {
        Resource row = db.get_row("character_base", "scholar_fallen");
        BattleActor actor = BattleActor.from_character(
            db, row, 5, new Godot.Collections.Dictionary(), new Godot.Collections.Array());
        actor.actor_id = attacker ? "sig_attacker" : "sig_defender";
        actor.side = attacker ? BattleActor.side_ally() : BattleActor.side_enemy();
        actor.skill_mastery["sk_xuanwei_03"] = 0;
        if (signature_skill_id.Length > 0)
        {
            _WithSignature(actor, signature_skill_id);
        }

        return actor;
    }

    /// <summary>
    /// 一个**满血**的守方。`from_character()` 只填数值、不填当前气血（`hp` 默认 0），
    /// 而「开碑」那条只对满血目标生效——不补这一下，测出来会是"签名没效果"，其实是血量不对。
    /// </summary>
    private static BattleActor _FullHealth(TableDb db)
    {
        BattleActor actor = _SigActor(db, false);
        actor.hp = actor.max_hp();
        // 顺带把外防抬到一个**量得出**的量级：角色裸防只有十几点，15% 穿透算出来的差值
        // 不到 1 点血，取整之后两次一模一样——那种"看起来没效果"其实是量不出来。
        actor.stats["def_phys"] = 300.0;
        return actor;
    }

    /// <summary>把一部内功装到这个单位身上并练满（签名生效的两个前提）。</summary>
    private static BattleActor _WithSignature(BattleActor actor, string skill_id)
    {
        actor.passives = new[] { skill_id };
        actor.skill_mastery[skill_id] = 10;
        return actor;
    }

}

