using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 异常状态行上的两个数值列（C# 版，对位 `src/core/status_rules.gd` 的读法）。
///
/// `battle_actor.gd` 算防御乘数与内力回复时都走这一处——这条用例钉的是「读法只此一份」：
/// 表里有就按表、缺表／缺列／手搓状态一律退回兜底值。
/// </summary>
public partial class TestStatusRules : TestCase
{
    private const int Seed = 20261003;

    public override string SuiteName() => "异常状态与持续伤害";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckStatusRateTable(db);
        _CheckChance(db);
        _CheckStacking(db);
        _CheckSnapshot(db);
        _CheckDefenseRules(db);
        _CheckBurnAndInternal(db);
        _CheckUnhandledRules(db);
        _CheckDispellable(db);
        _CheckBattleFlow(db);
        _CheckEnemyResistances(db);
        _CheckPlayerResistances(db);
        _CheckRegenCaps(db);
    }

    /// <summary>
    /// 异常状态行上的两个数值列：表里有就按表、缺表／缺列／手搓状态一律退回兜底值。
    /// `battle_actor` 算防御乘数与内力回复时都走这一处，这条钉的是「读法只此一份」。
    /// </summary>
    private void _CheckStatusRateTable(TableDb db)
    {
        CheckEq(
            StatusRules.rate(db, _entry("burn"), "burn_def_cut", 0.05),
            0.05, "灼伤每层削 5% 外防（读 status_effect.burn_def_cut）");
        CheckEq(
            StatusRules.rate(db, _entry("internal"), "qi_regen_mul", 1.0),
            0.5, "内伤把内力回复压到五成（读 status_effect.qi_regen_mul）");
        CheckEq(
            StatusRules.rate(db, _entry("burn"), "qi_regen_mul", 0.25),
            1.0, "灼伤那列是 1（它不压内力回复）");
        CheckEq(
            StatusRules.rate(db, _entry("poison"), "burn_def_cut", 0.05),
            0.0, "中毒那列是 0（只灼伤削防御）");

        CheckEq(
            StatusRules.rate(db, _entry("no_such_status"), "burn_def_cut", 0.07),
            0.07, "表里没有这条状态 → 兜底值");
        CheckEq(
            StatusRules.rate(db, new Godot.Collections.Dictionary(), "burn_def_cut", 0.07),
            0.07, "手搓状态没写 status_id → 兜底值");
        CheckEq(
            StatusRules.rate(null, _entry("burn"), "burn_def_cut", 0.07),
            0.07, "没有表库 → 兜底值");
    }

    /// <summary>触发几率 = base_chance × status_chance_mul + 异常触发率（运换算），再乘 (1 − 抗性)。</summary>
    private void _CheckChance(TableDb db)
    {
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor attacker = _Actor("atk", 10.0, BattleActor.side_ally());
        attacker.stats["debuff_chance"] = 0.04;
        BattleActor target = _Actor("def", 10.0, BattleActor.side_enemy());
        Resource poison_skill = db.get_row("skill_active", "sk_wudu_01");
        // 中毒 base_chance 0.35 × 招式 1.5 + 运给的 4% = 56.5%
        CheckFloat(sim.status_chance(attacker, target, poison_skill), 0.565, "伤害型招式上异常的几率", 0.0001);
        // 目标有 50% 抗性 → 减半
        target.resistances["poison"] = 0.5;
        CheckFloat(sim.status_chance(attacker, target, poison_skill), 0.2825, "抗性把几率减半", 0.0001);
        target.resistances.Remove("poison");
        // 招式没有 status_id 时不上状态
        Resource plain = db.get_row("skill_active", "sk_xuanwei_01");
        CheckFloat(sim.status_chance(attacker, target, plain), 0.0, "不带异常的招式几率为 0");
    }

    /// <summary>同源同类叠层、刷新时长；到上限只刷新不加层。</summary>
    private void _CheckStacking(TableDb db)
    {
        BattleActor actor = _Actor("victim", 10.0, BattleActor.side_enemy());
        for (int index = 0; index < 6; index++)
        {
            actor.add_status("poison", "atk", 10.0, 5, 3, "", true);
        }
        CheckEq(actor.status_stacks("poison"), 5, "中毒最多 5 层");
        Godot.Collections.Dictionary entry = actor.statuses[0].AsGodotDictionary();
        CheckEq(entry["remaining"].AsInt32(), 3, "刷新时长（不会叠到 6 回合）");
        // 不同来源独立计算
        actor.add_status("poison", "other", 8.0, 5, 3, "", true);
        CheckEq(actor.statuses.Count, 2, "不同来源的中毒是两条");
        CheckEq(actor.status_stacks("poison"), 6, "总层数按来源相加");
    }

    /// <summary>快照制：每层按施加那一刻的伤害记，之后重新施放不会改老层的值。</summary>
    private void _CheckSnapshot(TableDb db)
    {
        BattleActor actor = _Actor("victim", 10.0, BattleActor.side_enemy());
        actor.add_status("poison", "atk", 10.0, 5, 3, "", true);
        actor.add_status("poison", "atk", 25.0, 5, 3, "", true);
        Godot.Collections.Array ticks = actor.tick_statuses();
        CheckEq(ticks.Count, 1, "一条状态跳一次");
        if (ticks.Count > 0)
        {
            CheckEq(
                ticks[0].AsGodotDictionary()["damage"].AsInt32(),
                35, "10 + 25：每层按各自锁定的值跳");
        }
    }

    /// <summary>
    /// 防御口径（按当前数据）：中毒/灼伤无视内外防御，流血吃外功防御，**内伤吃内功防御**。
    /// 注：04_战斗与伤害.md 的伤害类型表把内伤写成「吃内防 否」，三处口径不一致已记缺口 #21 请设计确认。
    /// </summary>
    private void _CheckDefenseRules(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor attacker = _Actor("atk", 10.0, BattleActor.side_ally());
        attacker.stats["atk_phys"] = 60.0;
        attacker.stats["atk_qi"] = 60.0;
        BattleActor tanky = _Actor("def", 10.0, BattleActor.side_enemy());
        tanky.stats["def_phys"] = 120.0;
        tanky.stats["def_qi"] = 120.0;

        Resource poison = db.get_row("skill_active", "sk_wudu_02");
        Resource bleed = db.get_row("skill_active", "sk_xuanwei_07");
        Resource internal_skill = db.get_row("skill_active", "sk_drunk_01");
        // **必须把状态自己的伤害类型传进去**：`resolve_dot` 的 `dot_type_id` 缺省时退回招式自己的
        // `damage_type`——`sk_drunk_01`（醉里乾坤·颠倒）是 `dmg_normal`，于是「内伤吃内功防御」那条
        // 以前其实在验**外功防御**（def_phys 120 → 1200 当然更低），**内伤这个格子从来没被验过**。
        // 是「把 dot_internal.use_def_qi 改成 0」的反向探针没红，才把这条弱断言暴露出来（决策 91）。
        int poison_damage = _Dot(resolver, attacker, tanky, poison, "dot_poison")["damage"].AsInt32();
        int bleed_damage = _Dot(resolver, attacker, tanky, bleed, "dot_bleed")["damage"].AsInt32();
        int internal_damage = _Dot(resolver, attacker, tanky, internal_skill, "dot_internal")["damage"].AsInt32();

        // 把防御堆到极高：吃防御的那两条要明显变低，无视防御的不受影响
        BattleActor wall = _Actor("def2", 10.0, BattleActor.side_enemy());
        wall.stats["def_phys"] = 1200.0;
        wall.stats["def_qi"] = 1200.0;
        CheckEq(
            _Dot(resolver, attacker, wall, poison, "dot_poison")["damage"].AsInt32(),
            poison_damage,
            "中毒无视内外防御");
        CheckLt(
            _Dot(resolver, attacker, wall, bleed, "dot_bleed")["damage"].AsDouble(),
            bleed_damage,
            "流血吃外功防御");
        CheckLt(
            _Dot(resolver, attacker, wall, internal_skill, "dot_internal")["damage"].AsDouble(),
            internal_damage,
            "内伤吃内功防御（按当前数据；04 的伤害类型表写的是「不吃」，见缺口 #21）");
    }

    /// <summary>灼伤每层削 5% 外功防御；内伤让内力回复减半。</summary>
    private void _CheckBurnAndInternal(TableDb db)
    {
        BattleActor victim = _Actor("victim", 10.0, BattleActor.side_enemy());
        victim.stats["def_phys"] = 100.0;
        CheckFloat(victim.defense_multiplier("def_phys"), 1.0, "没状态时防御不变");
        victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true);
        victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true);
        CheckFloat(victim.defense_multiplier("def_phys"), 0.9, "两层灼伤削 10% 外功防御", 0.0001);
        CheckFloat(victim.defense_multiplier("def_qi"), 1.0, "灼伤只削外功防御");

        // 削防真的让直伤变高（同一发攻击，带火伤的目标更疼）
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor attacker = _Actor("atk", 10.0, BattleActor.side_ally());
        Resource skill = db.get_row("skill_active", "sk_xuanwei_01");
        BattleActor plain = _Actor("plain", 10.0, BattleActor.side_enemy());
        plain.stats["def_phys"] = 100.0;
        int before = resolver.resolve(attacker, plain, skill, _NoVariance())["damage"].AsInt32();
        int after = resolver.resolve(attacker, victim, skill, _NoVariance())["damage"].AsInt32();
        CheckGt(after, before, $"灼伤削防后同一招打得更疼（{before} → {after}）");

        // 内伤：内力回复减半
        BattleActor hurt = _Actor("hurt", 10.0, BattleActor.side_ally());
        hurt.stats["qi_regen"] = 10.0;
        hurt.qi = 0;
        hurt.end_of_round_regen();
        CheckEq(hurt.qi, 10, "正常每回合回 10 内力");
        hurt.qi = 0;
        hurt.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", false);
        CheckFloat(hurt.qi_regen_factor(), 0.5, "内伤内力回复减半");
        hurt.end_of_round_regen();
        CheckEq(hurt.qi, 5, "内伤下半量回复");

        // 这两个数**取自表**（设计 08 参数口径 Q86）：改哪一列，效果就跟着变。
        // 以前它们只活在代码常量里（`desc` 写 5%、代码写 0.05），改一个忘另一个没人管。
        CheckFloat(
            db.get_row("status_effect", "burn").Get("burn_def_cut").AsDouble(),
            0.05, "灼伤削防比例落在表里");
        CheckFloat(
            db.get_row("status_effect", "internal").Get("qi_regen_mul").AsDouble(),
            0.5, "内伤内力回复乘数落在表里");
        TableDb tuned_db = _StatusEffectTableWith(db, "burn", "burn_def_cut", 0.2, "internal", "qi_regen_mul", 0.25);
        BattleActor tuned_actor = _Actor("tuned", 10.0, BattleActor.side_enemy());
        tuned_actor._db = tuned_db;
        tuned_actor.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true);
        CheckFloat(
            tuned_actor.defense_multiplier("def_phys"),
            0.8, "削防比例读表（改成 0.2 → 一层只剩 80%）", 0.0001);
        tuned_actor.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", false);
        CheckFloat(tuned_actor.qi_regen_factor(), 0.25, "内力回复乘数读表（改成 0.25）", 0.0001);
    }

    /// <summary>
    /// 「表里配了、代码还不认」的状态规则必须报出来，不许静默。
    ///
    /// 表里 `bleed.extra_rule = rule_bleed_move`（流血换位），而它依赖站位、设计还没定规则——
    /// 以前代码把它当空气：既不做效果，也不出声，`actor.statuses` 里那个 `rule` 字段就这么躺着。
    /// 现在会记进 `unhandled_rules` 并由战斗结算播成「暂缓规则」（和奇袭的「无防备」同一套口径）。
    /// </summary>
    private void _CheckUnhandledRules(TableDb db)
    {
        BattleActor victim = _Actor("victim", 10.0, BattleActor.side_enemy());
        // 表里流血那条规则 id 就是它（不是代码里编的）
        CheckEq(db.get_row("status_effect", "bleed").Get("extra_rule").AsString(), "rule_bleed_move", "流血规则取自表");
        victim.add_status("bleed", "atk", 5.0, 3, 4, "rule_bleed_move", true);
        CheckTrue(
            System.Array.IndexOf(victim.unhandled_rules, "rule_bleed_move") >= 0,
            "不认识的规则被记下来");
        // 已经实现的规则不该被误报
        victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true);
        victim.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", true);
        CheckEq(
            victim.unhandled_rules.Length,
            1, $"只记不认识的（灼伤／内伤都实现了）：{string.Join(",", victim.unhandled_rules)}");
        // 无规则（空串）也不该被当成「不认识」
        victim.add_status("poison", "atk", 5.0, 5, 3, "", true);
        CheckEq(victim.unhandled_rules.Length, 1, "空规则不算未实现");
        // 结算要把它播出来：界面读 result()["pending_rules"] 写「暂缓规则：…」
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor ally = _Actor("ally", 10.0, BattleActor.side_ally());
        sim.setup(
            new Godot.Collections.Array { ally },
            new Godot.Collections.Array { victim },
            new Godot.Collections.Dictionary());
        Godot.Collections.Dictionary result = sim.result();
        Godot.Collections.Array pending = result.ContainsKey("pending_rules")
            ? result["pending_rules"].AsGodotArray()
            : new Godot.Collections.Array();
        bool mentioned = false;
        bool leaks_id = false;
        foreach (Variant line_variant in pending)
        {
            string line = line_variant.AsString();
            if (line.Contains("流血"))
            {
                mentioned = true;
            }
            if (line.Contains("rule_"))
            {
                leaks_id = true;
            }
        }
        CheckTrue(mentioned, $"结算的暂缓规则里点名了它（说中文状态名）：{_Join(pending)}");
        CheckFalse(leaks_id, $"暂缓规则文案不漏表内 id：{_Join(pending)}");
    }

    /// <summary>可驱散：毒/灼伤/流血能被清，内伤清不掉。</summary>
    private void _CheckDispellable(TableDb db)
    {
        BattleActor actor = _Actor("victim", 10.0, BattleActor.side_enemy());
        actor.add_status("poison", "atk", 5.0, 5, 3, "", true);
        actor.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true);
        actor.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", false);
        string[] cleared = actor.clear_dispellable();
        CheckEq(cleared.Length, 2, "清掉两条可驱散的");
        CheckTrue(actor.has_status("internal"), "内伤不可驱散");
        CheckFalse(actor.has_status("poison"), "中毒被清掉");
    }

    /// <summary>整场战斗：命中挂状态 → 回合末统一跳字 → 血量过低时斩杀结算。</summary>
    private void _CheckBattleFlow(TableDb db)
    {
        BattleActor attacker = _Actor("atk", 20.0, BattleActor.side_ally());
        attacker.skills = new[] { "sk_wudu_02" };
        attacker.stats["debuff_chance"] = 0.4; // 抬几率，保证上得去
        BattleActor victim = _Actor("victim", 5.0, BattleActor.side_enemy());
        victim.hp = victim.max_hp();
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { victim },
            new Godot.Collections.Dictionary());
        sim.begin_round();
        Resource skill = db.get_row("skill_active", "sk_wudu_02");
        sim.act(attacker, skill, victim);
        CheckTrue(victim.has_status("poison"), "命中后上了中毒");
        int stacks = victim.status_stacks("poison");
        CheckEq(stacks, 1, "第一次施放 1 层");
        CheckEq(sim.kill_style_of(victim.actor_id), "", "还没死人，没有死因");

        // 敌方随后行动 → 本回合结束 → 回合末统一结算跳一次毒（层数留着继续跳）
        sim.auto_act();
        int dot_events = 0;
        foreach (Variant event_variant in sim.take_events())
        {
            Godot.Collections.Dictionary ev = event_variant.AsGodotDictionary();
            if (ev.ContainsKey("kind") && ev["kind"].AsString() == "dot")
            {
                dot_events++;
            }
        }
        CheckGt(dot_events, 0.0, "回合末跳出持续伤害事件");

        // 斩杀：血量压到阈值以下，剩余层数立即结算并清空
        BattleActor execute_target = _Actor("exec", 5.0, BattleActor.side_enemy());
        execute_target.hp = System.Math.Max(1, (int)(execute_target.max_hp() * 0.1));
        execute_target.add_status("poison", "atk", 9.0, 5, 3, "", true);
        execute_target.add_status("poison", "atk", 9.0, 5, 3, "", true);
        BattleSimulator sim2 = BattleSimulator.create(db, RngService.create(Seed));
        sim2.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { execute_target },
            new Godot.Collections.Dictionary());
        sim2.begin_round();
        sim2.act(attacker, skill, execute_target);
        bool executed = false;
        foreach (Variant event_variant in sim2.take_events())
        {
            Godot.Collections.Dictionary ev = event_variant.AsGodotDictionary();
            if (ev.ContainsKey("kind") && ev["kind"].AsString() == "dot"
                && ev.ContainsKey("execute") && ev["execute"].AsBool())
            {
                executed = true;
            }
        }
        CheckTrue(executed || !execute_target.is_alive(), "血量过低时剩余持续伤害立即结算");
        CheckTrue(execute_target.statuses.Count == 0 || !execute_target.is_alive(), "斩杀后清空层数");
        if (!execute_target.is_alive())
        {
            CheckEq(sim2.kill_style_of(execute_target.actor_id), "poison", "死于毒伤 → 死因记成中毒（毒杀判定用）");
        }
    }

    /// <summary>
    /// 0.12.0：抗性是**角色与敌人共用的派生数值**——玩家练五毒心法也能拿到毒抗。
    ///
    /// 由来（2026-10-03）：`skill_passive_stat` 早就配了 `stat:res_poison`（五毒心法系 0.10~0.25），
    /// 而状态生效那条路只读 `BattleActor.resistances` 快照——那是敌人行专用的一份，
    /// 玩家这份抗性算进了派生数值却**没人读**，等于「练了以毒攻毒、照样中毒」。
    /// </summary>
    private void _CheckPlayerResistances(TableDb db)
    {
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        Godot.Collections.Array before = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        CheckEq(before.Count, 1, "单人队伍");
        BattleActor bare = before[0].As<BattleActor>();
        CheckFloat(bare.resistance_of("poison"), 0.0, "没练内功时没有毒抗");

        // 学会并装上五毒心法·引气（skill_passive_stat 给 stat:res_poison 0.10）
        state.Call("learn_skill", char_id, "pf_wudu_01");
        state.Call("set_loadout", char_id, System.Array.Empty<string>(), new[] { "pf_wudu_01" });
        Godot.Collections.Array armed = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        BattleActor actor = armed[0].As<BattleActor>();
        CheckFloat(actor.stat("res_poison"), 0.10, "内功的毒抗进了派生数值");
        CheckFloat(actor.resistance_of("poison"), 0.10, "抗性读取走同一条派生数值（角色与敌人共用）");

        // 真实效果：同一个人打同一招，目标有这条抗性时上状态的几率按 (1−抗性) 打折
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor attacker = _Actor("atk_resist", 10.0, BattleActor.side_ally());
        attacker.stats["debuff_chance"] = 0.0;
        Resource poison_skill = db.get_row("skill_active", "sk_wudu_01");
        double chance_bare = sim.status_chance(attacker, bare, poison_skill);
        double chance_armed = sim.status_chance(attacker, actor, poison_skill);
        CheckFloat(chance_armed, chance_bare * 0.9, "0.10 毒抗把上状态几率乘 0.9", 0.0001);

        // 敌人那一侧没变：毒手 50%（现在也走同一条 `resistance_of`）
        EnemyFactory factory = EnemyFactory.create(db);
        BattleActor poison_hand = (BattleActor)factory.create_enemy("en_bd_poison_hand", "normal");
        CheckFloat(poison_hand.resistance_of("poison"), 0.15, "毒手那 15% 毒抗（内功给的）走同一条读取口");
    }

    /// <summary>敌人的异常抗性（enemy_base 的 res_* 列）：几率要减半，内伤抗性还减伤害。</summary>
    private void _CheckEnemyResistances(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        BattleActor wolf = (BattleActor)factory.create_enemy("en_wolf", "normal");
        // 0.14.0（设计 10 §二）：毒/火/流血三条**特权列取消**——敌人的抗性改为「装内功才有」，
        // 值来自 `enemy_passive` → `skill_passive_stat` 的 `stat:res_*`；读的入口也统一成
        // `resistance_of()`（角色与敌人共用派生数值）。**数值比旧列低**，见下面的断言。
        CheckFloat(wolf.resistance_of("poison"), 0.0, "野狼没装五毒内功 → 没有毒抗");
        BattleActor poison_hand = (BattleActor)factory.create_enemy("en_bd_poison_hand", "normal");
        CheckFloat(poison_hand.resistance_of("poison"), 0.15, "毒手装了五毒心法（pf_wudu_03 给 0.15 毒抗）");
        BattleActor boss = (BattleActor)factory.create_enemy("en_bd_boss", "normal");
        CheckFloat(boss.resistance_of("poison"), 0.10, "大寨主装了五毒心法·腐骨（pf_wudu_02 给 0.10 毒抗）");
        CheckFloat(boss.resistance_of("burn"), 0.0, "大寨主没有火抗（旧特权列 0.2 已随设计取消）");
        CheckFloat(boss.resistance_of("bleed"), 0.0, "大寨主没有流血抗（旧特权列 0.2 已随设计取消）");
        BattleActor drunk = (BattleActor)factory.create_enemy("en_hidden_drunk", "normal");
        CheckFloat(drunk.resistance_of("bleed"), 0.15, "醉刀客装了罗汉功（pf_luohan_01 给 0.15 流血抗）");

        // 几率：同一个人打野狼 vs 打毒手，抗性把几率砍半
        BattleActor attacker = _Actor("clever", 10.0, BattleActor.side_ally());
        attacker.stats["debuff_chance"] = 0.0;
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { attacker },
            new Godot.Collections.Array { wolf },
            new Godot.Collections.Dictionary());
        Resource skill = db.get_row("skill_active", "sk_wudu_03"); // 中毒 0.9 × 2.2
        double base_chance = sim.status_chance(attacker, wolf, skill);
        double resisted = sim.status_chance(attacker, poison_hand, skill);
        CheckFloat(
            resisted,
            base_chance * (1.0 - 0.15),
            $"毒抗按内功给的 0.15 压低上毒几率（{base_chance:F3} → {resisted:F3}）",
            0.0001);

        // res_internal 额外减免内伤类持续伤害
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor tall = _Actor("tall", 10.0, BattleActor.side_enemy());
        tall.stats["res_internal"] = 0.0;
        BattleActor tough = _Actor("tough", 10.0, BattleActor.side_enemy());
        tough.stats["res_internal"] = 0.5;
        Resource internal_skill = db.get_row("skill_active", "sk_drunk_01");
        int plain_damage = _Dot(resolver, attacker, tall, internal_skill, "dot_internal")["damage"].AsInt32();
        int resisted_damage = _Dot(resolver, attacker, tough, internal_skill, "dot_internal")["damage"].AsInt32();
        CheckLt(resisted_damage, plain_damage, $"内伤抗性减伤（{plain_damage} → {resisted_damage}）");
        // 上限**来自表**（stat_def.res_internal.max_value），不是代码里写死的常数：
        // 把表里的上限改成 0.5，同一个超上限的抗性（0.9）就只按 0.5 减伤 → 伤害比默认 0.75 时更高。
        // 这条是 2026-10-03 把 `clampf(…, 0.75)` 改成 `_stat_cap("res_internal", …)` 的回归锚
        // （见框架说明决策 180）。
        TableDb lowered_db = _StatDefTableWith(db, "res_internal", "max_value", 0.5);
        BattleActor capped = _Actor("capped", 10.0, BattleActor.side_enemy());
        capped.stats["res_internal"] = 0.9;
        int default_cap = _Dot(resolver, attacker, capped, internal_skill, "dot_internal")["damage"].AsInt32();
        DamageResolver lowered_resolver = DamageResolver.create(lowered_db, RngService.create(Seed));
        int lowered_cap = _Dot(lowered_resolver, attacker, capped, internal_skill, "dot_internal")["damage"].AsInt32();
        CheckGt(
            lowered_cap,
            default_cap,
            $"内伤抗性上限读表：表里改成 0.5 后同一击伤害更高（{lowered_cap} > {default_cap}）");
        // 别的 DoT 不受内伤抗性影响
        Resource poison_skill = db.get_row("skill_active", "sk_wudu_02");
        CheckEq(
            _Dot(resolver, attacker, tough, poison_skill, "dot_poison")["damage"].AsInt32(),
            _Dot(resolver, attacker, tall, poison_skill, "dot_poison")["damage"].AsInt32(),
            "中毒不吃内伤抗性");
        // 直的伤害型招式挂的中毒层，也要按「中毒」的规则算（无视外功防御）
        Resource fist = db.get_row("skill_active", "sk_wudu_01"); // 直伤 + 中毒
        BattleActor wall = _Actor("wall", 10.0, BattleActor.side_enemy());
        wall.stats["def_phys"] = 900.0;
        CheckEq(
            _Dot(resolver, attacker, wall, fist, "dot_poison")["damage"].AsInt32(),
            _Dot(resolver, attacker, tall, fist, "dot_poison")["damage"].AsInt32(),
            "毒层无视外功防御（哪怕挂毒的那招是直伤）");
    }

    /// <summary>
    /// 回合末回复的边界：回满就停（不溢出）、内伤减半后再封顶、倒下的单位不回复（气血与内力都不动）。
    /// 「内伤内力回复减半」本身在 `_CheckBurnAndInternal` 里验过，这条补的是**封顶**与**死人**两种边界——
    /// 溢出（内力超过上限）不会报错，只会在界面上显示成一条满不起来的条，很难被发现。
    /// </summary>
    private void _CheckRegenCaps(TableDb db)
    {
        BattleActor actor = _Actor("regen", 10.0, BattleActor.side_ally());
        actor.stats["hp_regen"] = 10.0;
        actor.stats["qi_regen"] = 10.0;
        // ① 满血满内力：回复不动
        actor.hp = actor.max_hp();
        actor.qi = actor.max_qi();
        actor.end_of_round_regen();
        CheckEq(actor.hp, actor.max_hp(), "满血时回合末回复不溢出");
        CheckEq(actor.qi, actor.max_qi(), "满内力时不溢出");
        // ② 差 2 点：回 10 也只补到上限
        actor.hp = actor.max_hp() - 2;
        actor.qi = actor.max_qi() - 2;
        actor.end_of_round_regen();
        CheckEq(actor.hp, actor.max_hp(), "气血补到上限就停");
        CheckEq(actor.qi, actor.max_qi(), "内力补到上限就停");
        // ③ 内伤减半之后再封顶：剩 2 点、回复 10 → 减半 5，仍然只补到上限
        actor.add_status("internal", "foe", 4.0, 3, 3, "rule_internal_qi", true);
        CheckFloat(actor.qi_regen_factor(), 0.5, "内伤下内力回复减半");
        actor.hp = actor.max_hp() - 2;
        actor.qi = actor.max_qi() - 2;
        actor.end_of_round_regen();
        CheckEq(actor.hp, actor.max_hp(), "内伤不影响气血回复的口径（这里仍是满）");
        CheckEq(actor.qi, actor.max_qi(), "减半之后也只补到上限");
        CheckTrue(actor.qi <= actor.max_qi(), $"内力不会超过上限（{actor.qi}/{actor.max_qi()}）");
        // ④ 倒下的单位：气血与内力都不回复（回合末结算会跳过死人）
        actor.hp = 0;
        actor.qi = 5;
        actor.end_of_round_regen();
        CheckEq(actor.hp, 0, "倒下不回复气血");
        CheckEq(actor.qi, 5, "倒下也不回复内力");
    }

    /// <summary>造一个固定数值的战斗单位（对位旧链 `_actor` 夹具）。</summary>
    private static BattleActor _Actor(string id, double speed, int side)
    {
        var actor = new BattleActor();
        actor.actor_id = id;
        actor.display_name = id;
        actor.side = side;
        actor.level = 10;
        actor.base_accuracy = 1.0;
        actor.stats = new Godot.Collections.Dictionary
        {
            { "hp_max", 300.0 }, { "atk_phys", 40.0 }, { "atk_qi", 40.0 },
            { "def_phys", 0.0 }, { "def_qi", 0.0 }, { "speed", speed },
            { "hit_rate", 0.5 }, { "dodge_rate", 0.0 }, { "crit_rate", 0.0 }, { "crit_dmg", 0.0 },
            { "qi_max", 100.0 }, { "qi_regen", 1.0 }, { "hp_regen", 0.0 },
            { "pen_rate", 0.0 }, { "block_rate", 0.0 }, { "block_reduction", 0.0 },
            { "dmg_reduction", 0.0 }, { "debuff_chance", 0.0 }, { "debuff_power", 0.0 },
        };
        actor.poise_max = 100;
        actor.refill();
        return actor;
    }

    /// <summary>持续伤害按状态自己的伤害类型算（模拟器也是这么调的）。</summary>
    private static Godot.Collections.Dictionary _Dot(
        DamageResolver resolver, BattleActor attacker, BattleActor defender, Resource skill, string dot_type_id) =>
        resolver.resolve_dot(attacker, defender, skill, _NoVariance(), dot_type_id);

    private static Godot.Collections.Dictionary _NoVariance() =>
        new() { { "no_variance", true } };

    private static string _Join(Godot.Collections.Array values)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant value in values)
        {
            parts.Add(value.AsString());
        }
        return string.Join(" / ", parts);
    }

    /// <summary>复制一份表库、把 `status_effect` 里两列改掉（只改内存副本，不碰策划的 CSV）。</summary>
    private static TableDb _StatusEffectTableWith(
        TableDb db,
        string status_a, string column_a, double value_a,
        string status_b, string column_b, double value_b)
    {
        TableDb custom = new TableDb();
        custom.tables = db.tables.Duplicate();
        var table = (TableResource)db.tables["status_effect"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (StatusEffectRow)entry.As<TableRow>().Duplicate(true);
            if (row.status_id == status_a)
            {
                row.Set(column_a, value_a);
            }
            if (row.status_id == status_b)
            {
                row.Set(column_b, value_b);
            }
            rows.Add(row);
        }
        table.rows = rows;
        custom.tables["status_effect"] = table;
        return custom;
    }

    /// <summary>复制一份表库、把 `stat_def` 某一行的某一列改掉（同上，只改内存副本）。</summary>
    private static TableDb _StatDefTableWith(TableDb db, string stat_id, string column, double value)
    {
        TableDb custom = new TableDb();
        custom.tables = db.tables.Duplicate();
        var table = (TableResource)db.tables["stat_def"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (StatDefRow)entry.As<TableRow>().Duplicate(true);
            if (row.stat_id == stat_id)
            {
                row.Set(column, value);
            }
            rows.Add(row);
        }
        table.rows = rows;
        custom.tables["stat_def"] = table;
        return custom;
    }

    private static Godot.Collections.Dictionary _entry(string status_id) =>
        new() { { "status_id", status_id } };
}
