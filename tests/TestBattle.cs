using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 战斗序列：出手顺序、胜负、奖励、跑飞保护。对位 `tests/test_battle.gd`
/// （2026-10-11 P4 第四十批整体搬 C#）。
///
/// 这一份钉的是**整条战斗管线的手感口径**：同速的稳定次序、难度只改数值不改清单、
/// 同种子日志逐字一致、默认回合上限打满 50 回合算平局、预兆与拆招（含拆招花掉这次行动）、
/// `stat:extra_hit` 段数 +1（普攻不吃）、内功催动按表扣气、逃跑的四段公式与「被追上第一回合撤不了」、
/// 三档自动战斗策略各挑各的、以及 `target_type=self` 的招式**打自己**（手选与自动两条路）。
/// </summary>
public partial class TestBattle : TestCase
{
    private const int Seed = 424242;

    public override string SuiteName() => "战斗序列与胜负";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckAbsoluteConstants();
        _CheckTurnOrder(db);
        _CheckPartyVsTeam(db);
        _CheckRewards(db);
        _CheckDifficultyScaling(db);
        _CheckDeterminism(db);
        _CheckRoundCap(db);
        _CheckTelegraphAndParry(db);
        _CheckExtraHit(db);
        _CheckPassiveQiCost(db);
        _CheckFlee(db);
        _CheckExactKill(db);
        _CheckAutoStrategies(db);
        _CheckSelfTargetSkill(db);
    }

    /// <summary>固定命中／无浮动／无格挡：拆招「伤害减半」的对照要精确到数值。</summary>
    private static Godot.Collections.Dictionary _FixedMods() => new()
    {
        { "force_hit", true },
        { "no_variance", true },
        { "no_block", true },
    };

    /// <summary>带上那条固定修饰的选项（`setup` 的第三个实参）。</summary>
    private static Godot.Collections.Dictionary _Modifiers() =>
        new() { { "modifiers", _FixedMods() } };

    /// <summary>
    /// 手感常量的**绝对值**断言（框架说明决策 153：期望值不许由被测常量自己算出来——
    /// 「按倍数关系」的断言挡不住整条公式被同倍缩放）。这一组是 2026-10-03 变异探针第三块
    /// 点名「没人钉」的那几条。
    /// </summary>
    private void _CheckAbsoluteConstants()
    {
        CheckEq(BattleActor.side_ally(), 0, "我方阵营常量 = 0（Encounter 与模拟器共用这一处）");
        CheckEq(BattleActor.side_enemy(), 1, "敌方阵营常量 = 1");
        CheckFloat(BattleSimulator.basic_attack_power(), 1.0, "普通攻击倍率 1.0");
        CheckEq(BattleSimulator.basic_attack_poise(), 8, "普通攻击破架势 8");
        CheckFloat(BattleSimulator.defend_poise_gain(), 0.15, "主动防御回架势上限的 15%");

        // 2026-10-10（C# 迁移第十六批顺手补的洞）：下面六条**以前没人钉**——0.32.0（Q85）把它们
        // 搬进 `combat_const` 之后，代码里这几份只剩「表里没这行时的兜底」，而用例全在有表的进程里跑，
        // 于是"改坏兜底"不产生任何行为差异（`run_mutation_sweep.bat code` 六条全报 `[missed]`）。
        // 兜底值照样是开发侧定死的口径，所以按老规矩补**绝对值断言**；表里的正式值另有验收网盯着。
        CheckFloat(BattleSimulator.parry_damage_mult_default(), 0.5, "拆招把被读破那招压到五成（兜底值）");
        CheckFloat(BattleSimulator.parry_poise_gain(), 0.15, "拆招者回架势上限的 15%（兜底值）");
        CheckFloat(BattleSimulator.poise_break_damage_bonus(), 0.5, "破绽窗口增伤 50%（兜底值）");
        CheckFloat(BattleSimulator.flee_base_chance(), 0.5, "逃跑基础成功率 50%（兜底值）");
        CheckFloat(BattleSimulator.flee_min_chance(), 0.25, "逃跑成功率下限 25%（兜底值）");
        CheckFloat(BattleSimulator.flee_max_chance(), 0.95, "逃跑成功率上限 95%（兜底值）");
    }

    private void _CheckTurnOrder(TableDb db)
    {
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor slow = _Actor("slow", 10.0, BattleActor.side_ally());
        BattleActor fast = _Actor("fast", 30.0, BattleActor.side_ally());
        BattleActor middle = _Actor("middle", 20.0, BattleActor.side_ally());
        Godot.Collections.Array order = simulator.turn_order(
            new Godot.Collections.Array { slow, fast, middle }, new Godot.Collections.Array());
        CheckEq(_Names(order), "fast,middle,slow", "身法高的先手");

        BattleActor tie_a = _Actor("tie_a", 15.0, BattleActor.side_ally());
        BattleActor tie_b = _Actor("tie_b", 15.0, BattleActor.side_enemy());
        Godot.Collections.Array tie_order = simulator.turn_order(
            new Godot.Collections.Array { tie_a }, new Godot.Collections.Array { tie_b });
        CheckEq(_Names(tie_order), "tie_a,tie_b", "同速按稳定次序排列");
    }

    private void _CheckPartyVsTeam(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        Godot.Collections.Array allies = _Party(db, 5);
        Godot.Collections.Array enemies = factory.create_team("team_wolf_pack", "normal");
        CheckEq(enemies.Count, 3, "野狼群是 3 只");
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        Godot.Collections.Dictionary result = simulator.simulate(allies, enemies);
        CheckEq(result["winner"].AsString(), BattleSimulator.winner_ally(), "5 级两人组应打赢 2 级野狼群");
        CheckInRange(result["rounds"].AsDouble(), 1.0, 20.0, "普通战回合数落在合理区间");
        CheckGt(result["log"].AsGodotArray().Count, 0.0, "战斗日志不为空");
        foreach (Variant entry in enemies)
        {
            CheckFalse(entry.As<BattleActor>().is_alive(), "我方获胜后敌人应全部倒下");
        }
    }

    private void _CheckRewards(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        Godot.Collections.Dictionary normal = BattleSimulator.create(db, RngService.create(Seed)).simulate(
            _Party(db, 5), factory.create_team("team_wolf_pack", "normal"));
        CheckEq(normal["rewards"].AsGodotDictionary()["exp"].AsInt32(), 12 * 3, "普通难度经验 = 野狼 12 × 3");
        CheckEq(normal["rewards"].AsGodotDictionary()["money"].AsInt32(), 5 * 3, "普通难度铜钱 = 野狼 5 × 3");

        // 绝境：exp ×1.7 → round(20.4)=20，money ×1.5 → round(7.5)=8
        Godot.Collections.Dictionary nightmare =
            BattleSimulator.create(db, RngService.create(Seed)).simulate(
                _Party(db, 8), factory.create_team("team_wolf_pack", "nightmare"));
        CheckEq(nightmare["winner"].AsString(), BattleSimulator.winner_ally(), "8 级队伍能扛住绝境野狼群");
        CheckEq(
            nightmare["rewards"].AsGodotDictionary()["exp"].AsInt32(), 20 * 3, "绝境经验倍率 ×1.7");
        CheckEq(
            nightmare["rewards"].AsGodotDictionary()["money"].AsInt32(), 8 * 3, "绝境铜钱倍率 ×1.5");
    }

    /// <summary>
    /// 难度倍率（设计 05：难度只改数值，不改清单）——`enemy_base × difficulty_config` 的
    /// **战斗数值**三档。exp／money 的倍率由 `_CheckRewards` 钉住；这里补 hp／atk／def：
    /// 以前它们**没有任何断言**，漏乘或写错就会出现「困难与普通一模一样」而用例全绿。
    /// 期望值由**同一份** `AttributeCalculator` 现算，不写死数字；另外补一条**单调性**断言
    /// （越难越厚越疼，表值被改成反向也要红）。
    /// </summary>
    private void _CheckDifficultyScaling(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        var base_row = (EnemyBaseRow)db.get_row("enemy_base", "en_bd_thug");
        // 0.28.0（设计 10 §七）：敌人走**共用管线但不吃 `level_growth` 基础列**——
        // 派生数值 = 七维 → `attr_to_stat` ＋ 装备／内功固定值（山寨喽啰挂着柴刀与布衣，
        // 布衣给 20 点气血），难度倍率照旧**在最后乘**。
        AttributeCalculator calculator = AttributeCalculator.create(db);
        CheckFalse(base_row.attr_map().Count == 0, "夹具前提：山寨喽啰的七维配着（不再是空的）");
        Godot.Collections.Dictionary expected_base = calculator.compute(
            base_row.level,
            base_row.attr_map(),
            new Godot.Collections.Dictionary(),
            factory.enemy_contributions("en_bd_thug"),
            false);
        CheckGt(expected_base["hp_max"].AsDouble(), 0.0, "夹具前提：七维算得出气血");

        foreach (string difficulty_id in new[] { "normal", "hard", "nightmare" })
        {
            var row = (DifficultyConfigRow)db.get_row("difficulty_config", difficulty_id);
            BattleActor actor = factory.create_enemy("en_bd_thug", difficulty_id) as BattleActor;
            CheckEq(
                actor.max_hp(),
                Mathf.RoundToInt((float)(expected_base["hp_max"].AsDouble() * row.enemy_hp_mul)),
                $"{difficulty_id}：血量 = 七维算出的基础 × enemy_hp_mul");
            CheckFloat(
                actor.stat("atk_phys"), expected_base["atk_phys"].AsDouble() * row.enemy_atk_mul,
                $"{difficulty_id}：外功 = 七维算出的基础 × enemy_atk_mul");
            CheckFloat(
                actor.stat("def_phys"), expected_base["def_phys"].AsDouble() * row.enemy_def_mul,
                $"{difficulty_id}：外防 = 七维算出的基础 × enemy_def_mul");
        }

        BattleActor normal_enemy = factory.create_enemy("en_bd_thug", "normal") as BattleActor;
        BattleActor hard_enemy = factory.create_enemy("en_bd_thug", "hard") as BattleActor;
        BattleActor nightmare_enemy = factory.create_enemy("en_bd_thug", "nightmare") as BattleActor;
        CheckGt(hard_enemy.max_hp(), normal_enemy.max_hp(), "困难比普通血厚");
        CheckGt(nightmare_enemy.max_hp(), hard_enemy.max_hp(), "绝境比困难血厚");
        CheckGt(
            nightmare_enemy.stat("atk_phys"), hard_enemy.stat("atk_phys"), "绝境比困难更疼");
        CheckGt(
            nightmare_enemy.stat("def_phys"), normal_enemy.stat("def_phys"), "绝境比普通更硬");
    }

    private void _CheckDeterminism(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        Godot.Collections.Dictionary first = BattleSimulator.create(db, RngService.create(Seed)).simulate(
            _Party(db, 6), factory.create_team("team_bandit_patrol", "normal"));
        Godot.Collections.Dictionary second = BattleSimulator.create(db, RngService.create(Seed)).simulate(
            _Party(db, 6), factory.create_team("team_bandit_patrol", "normal"));
        CheckEq(first["rounds"].AsInt32(), second["rounds"].AsInt32(), "同种子回合数一致");
        CheckEq(_LogText(first["log"]), _LogText(second["log"]), "同种子战斗日志完全一致");
    }

    private void _CheckRoundCap(TableDb db)
    {
        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor tank = _Actor("tank", 10.0, BattleActor.side_ally(), 100000);
        BattleActor wall = _Actor("wall", 9.0, BattleActor.side_enemy(), 100000);
        Godot.Collections.Dictionary result = simulator.simulate(
            new Godot.Collections.Array { tank },
            new Godot.Collections.Array { wall },
            new Godot.Collections.Dictionary { { "max_rounds", 3 } });
        CheckEq(result["rounds"].AsInt32(), 3, "达到回合上限就收手");
        CheckEq(result["winner"].AsString(), BattleSimulator.winner_draw(), "打不完判平局");

        // 上面那条**把上限压到 3**，所以默认值从来没被走过：变异探针实测，把常数改成 10
        // 一整套自检照样绿——而它决定「打到多少回合算平局」，真打起来会直接把普通仗判平（决策 154）。
        // 这里不传 max_rounds，让默认值自己跑满。
        BattleActor default_tank = _Actor("tank2", 10.0, BattleActor.side_ally(), 100000);
        BattleActor default_wall = _Actor("wall2", 9.0, BattleActor.side_enemy(), 100000);
        Godot.Collections.Dictionary default_result =
            BattleSimulator.create(db, RngService.create(Seed)).simulate(
                new Godot.Collections.Array { default_tank },
                new Godot.Collections.Array { default_wall });
        CheckEq(
            default_result["rounds"].AsInt32(), 50,
            $"不指定上限时打满默认 50 回合（实得 {default_result["rounds"].AsInt32()}）");
        CheckEq(
            default_result["winner"].AsString(), BattleSimulator.winner_draw(),
            "默认上限下打不完也是平局");
    }

    /// <summary>
    /// 预兆与拆招（设计 04 核心循环）：敌方出招前有预兆；我方花一次行动拆招，
    /// 读到的那一招伤害减半，拆招者反涨自身架势。
    /// </summary>
    private void _CheckTelegraphAndParry(TableDb db)
    {
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(_Party(db, 5), enemies, _Modifiers());
        sim.begin_round();
        CheckEq(sim.intent_lines().Length, 3, "每只野狼都有预兆");
        foreach (Variant entry in enemies)
        {
            BattleActor enemy = entry.As<BattleActor>();
            CheckNe(sim.intent_of(enemy.actor_id), "", $"{enemy.display_name} 预兆了招式");
        }

        CheckTrue(
            sim.intent_lines()[0].Contains("将用"),
            $"预兆文案写清出什么招：{sim.intent_lines()[0]}");

        // 伤害减半：同种子同对手，一条路拆招、一条路不拆
        int plain = _EnemyRoundDamage(db, false);
        int parried = _EnemyRoundDamage(db, true);
        CheckGt(plain, 0.0, $"不拆招时敌人打出伤害：{plain}");
        CheckEq(parried, (int)Mathf.Floor(plain * 0.5), $"拆招后那一招伤害减半（{plain} → {parried}）");

        // 反涨自身架势 + 花掉这次行动 + 同一个敌人一回合只能拆一次
        BattleActor hero = _Actor("hero", 10.0, BattleActor.side_ally(), 400);
        BattleActor foe = _Actor("foe", 9.0, BattleActor.side_enemy(), 400);
        BattleSimulator duel = BattleSimulator.create(db, RngService.create(Seed));
        duel.setup(new Godot.Collections.Array { hero }, new Godot.Collections.Array { foe }, _Modifiers());
        duel.begin_round();
        CheckEq((duel.current_actor() as BattleActor).actor_id, "hero", "身法高的我方先手");
        hero.poise = 0;
        hero.poise_max = 40;
        Godot.Collections.Dictionary result = duel.parry(hero, foe);
        CheckTrue(result["ok"].AsBool(), $"拆招成功：{result["error"].AsString()}");
        // 反涨 = 自身架势上限的 15%（PARRY_POISE_GAIN 是开发侧定的，交接表待设计补列）。
        // 这里写死 40 × 0.15 = 6：只写 `> 0` 时把 0.15 改成 0.25 也不会红（变异探针实测）。
        CheckEq(result["poise_gain"].AsInt32(), 6, $"拆招反涨架势 = 上限 15%（40 × 0.15 = 6，实得 {result["poise_gain"].AsInt32()}）");
        CheckEq(hero.poise, result["poise_gain"].AsInt32(), "架势真的加上去了");
        CheckTrue(duel.is_parried("foe"), "目标被标记为已拆招");
        CheckEq((duel.current_actor() as BattleActor).actor_id, "foe", "拆招花掉了这次行动（轮到敌人）");
        CheckEq(
            duel.parry_block_reason(duel.current_actor() as BattleActor, foe), "只能拆敌人的招",
            "不是我方行动时拆不了");
        CheckFalse(duel.parry(hero, foe)["ok"].AsBool(), "同一个敌人这一回合不能重复拆");
    }

    /// <summary>
    /// 「招式段数 +1」（设计 08 参数口径 Q84）：`stat_def.extra_hit` 加在招式段数上，**普攻不吃**。
    /// 这条以前是「表里没有、通道也没有」——套装档位配了也不会生效，所以要连日志一起钉住。
    /// </summary>
    private void _CheckExtraHit(TableDb db)
    {
        var skill_row = (SkillActiveRow)db.get_row("skill_active", "sk_xuanwei_01");
        int table_hits = skill_row.hit_count;
        CheckEq(
            ((StatDefRow)db.get_row("stat_def", "extra_hit")).max_value, 2.0,
            "段数派生值的上限是 2（设计定）");

        BattleActor hero = _Actor("hero", 20.0, BattleActor.side_ally());
        hero._db = db;
        BattleActor foe = _Actor("foe", 1.0, BattleActor.side_enemy());
        CheckEq(
            _HitsInOptions(db, hero, "sk_xuanwei_01"), table_hits,
            $"没加成时就是表里的 {table_hits} 段");
        hero.stats["extra_hit"] = 1.0;
        CheckEq(
            _HitsInOptions(db, hero, "sk_xuanwei_01"), table_hits + 1,
            "extra_hit 让招式多打一段（招式列表里也如实写）");

        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(new Godot.Collections.Array { hero }, new Godot.Collections.Array { foe }, _Modifiers());
        sim.begin_round();
        string skill_text = _LogText(sim.act(hero, skill_row, foe));
        CheckTrue(
            skill_text.Contains($"（{table_hits + 1} 段）"),
            $"打出来确实是 {table_hits + 1} 段：{skill_text}");
        string basic_text = _LogText(sim.basic_attack(hero, foe));
        CheckFalse(basic_text.Contains("段）"), $"普通攻击不吃 extra_hit：{basic_text}");

        // 第一章唯一一处数：玄微剑三招档的 buff 里挂着这条（与「敏 +3」并存）
        bool found = false;
        foreach (Variant entry in db.rows("buff_stat"))
        {
            var row = (BuffStatRow)entry.AsGodotObject();
            if (row.buff_id == "buff_set_xuanwei_sword_3" && row.target == "stat:extra_hit")
            {
                found = true;
            }
        }

        CheckTrue(found, "玄微剑三招档的 buff 里有一条 stat:extra_hit");
    }

    /// <summary>招式列表里报的段数（界面与自动战斗都读这一列，不该各算一套）。</summary>
    private int _HitsInOptions(TableDb db, BattleActor actor, string skill_id)
    {
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { actor },
            new Godot.Collections.Array { _Actor("dummy", 1.0, BattleActor.side_enemy()) },
            new Godot.Collections.Dictionary());
        foreach (Variant entry in sim.skill_options(actor))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["skill_id"].AsString() == skill_id)
            {
                return option["hit_count"].AsInt32();
            }
        }

        return -1;
    }

    /// <summary>
    /// 内功催动耗气（设计 08 参数口径 Q87）：`skill_passive.qi_cost` 按占格 ×6，催动真的扣。
    /// 以前按 0 收、按钮上写「内力消耗列未配」——本命运功白送。
    /// </summary>
    private void _CheckPassiveQiCost(TableDb db)
    {
        CheckEq(((SkillPassiveRow)db.get_row("skill_passive", "pf_xuanwei_01")).qi_cost, 6, "1 格内功催动收 6 气");
        CheckEq(((SkillPassiveRow)db.get_row("skill_passive", "pf_xuanwei_05")).qi_cost, 18, "3 格内功收 18 气");
        BattleActor hero = _Actor("hero", 20.0, BattleActor.side_ally());
        hero._db = db;
        hero.passives = new[] { "pf_xuanwei_01" };
        hero.qi = 20;
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { _Actor("foe", 1.0, BattleActor.side_enemy()) },
            new Godot.Collections.Dictionary());
        sim.begin_round();
        Godot.Collections.Dictionary result = sim.cast_passive(hero, "pf_xuanwei_01");
        CheckTrue(result["ok"].AsBool(), $"催动成功：{result["error"].AsString()}");
        CheckEq(hero.qi, 14, $"催动按表扣 6 点内力（20 → {hero.qi}）");
        hero.qi = 5;
        CheckEq(
            sim.passive_cast_block_reason(hero, "pf_xuanwei_01"), "内力不足（要 6）",
            "气不够时挡下，并把要多少写给玩家");
    }

    /// <summary>
    /// 自动战斗三档策略（设计 04：保守／均衡／全力，语义是开发侧定的）：
    /// 同一局面三种策略要挑出**不同**的招式，否则这个功能就是摆设。
    /// </summary>
    private void _CheckAutoStrategies(TableDb db)
    {
        // 一个能同时装下单发高倍率与群攻的对手组：两只活着的敌人，群攻才有价值
        BattleActor hero = _Actor("hero", 20.0, BattleActor.side_ally(), 400);
        hero.stats["qi_max"] = 60.0;
        hero.refill();
        // 起手（0 内力 1.0 倍）、斩风（16 内力、2.1 倍单体）、横扫（14 内力、1.3 倍 ×3 段全体）
        hero.skills = new[] { "sk_xuanwei_01", "sk_boss_zhangfeng", "sk_boss_hengsao" };
        BattleActor foe_a = _Actor("foe_a", 10.0, BattleActor.side_enemy(), 400);
        BattleActor foe_b = _Actor("foe_b", 10.0, BattleActor.side_enemy(), 400);

        var picks = new Godot.Collections.Dictionary();
        foreach (string strategy_id in BattleSimulator.strategy_order())
        {
            BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
            sim.setup(
                new Godot.Collections.Array { hero },
                new Godot.Collections.Array { foe_a, foe_b },
                new Godot.Collections.Dictionary { { "strategy", strategy_id } });
            var picked = sim.pick_skill(hero) as SkillActiveRow;
            picks[strategy_id] = picked != null ? picked.skill_id : "";
        }

        CheckEq(
            picks[BattleSimulator.strategy_conservative()].AsString(), "sk_xuanwei_01",
            "保守挑最省内力的起手式");
        CheckEq(
            picks[BattleSimulator.strategy_balanced()].AsString(), "sk_boss_hengsao",
            "均衡挑期望总伤害最高的群攻");
        CheckEq(
            picks[BattleSimulator.strategy_all_out()].AsString(), "sk_boss_zhangfeng",
            "全力挑单发倍率最高的斩风");

        // 默认是均衡；不认识的 id 拒绝且不改现状
        BattleSimulator default_sim = BattleSimulator.create(db, RngService.create(Seed));
        default_sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe_a, foe_b },
            new Godot.Collections.Dictionary());
        CheckEq(default_sim.strategy(), BattleSimulator.strategy_balanced(), "默认策略是均衡");
        CheckFalse(default_sim.set_strategy("nonsense"), "不认识的策略被拒绝");
        CheckEq(default_sim.strategy(), BattleSimulator.strategy_balanced(), "拒绝后策略不变");
        CheckEq(default_sim.cycle_strategy(), BattleSimulator.strategy_all_out(), "轮换顺序：均衡 → 全力");
        CheckEq(
            default_sim.cycle_strategy(), BattleSimulator.strategy_conservative(), "轮换顺序：全力 → 保守");
        CheckEq(default_sim.cycle_strategy(), BattleSimulator.strategy_balanced(), "轮换回到均衡");

        // 策略只改我方：同一只敌人换三档策略都应该挑同一招
        var enemy_picks = new Godot.Collections.Dictionary();
        foreach (string strategy_id in BattleSimulator.strategy_order())
        {
            BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
            sim.setup(
                new Godot.Collections.Array { hero },
                new Godot.Collections.Array { foe_a, foe_b },
                new Godot.Collections.Dictionary { { "strategy", strategy_id } });
            var picked = sim.pick_skill(foe_a) as SkillActiveRow;
            enemy_picks[strategy_id] = picked != null ? picked.skill_id : "";
        }

        CheckEq(
            enemy_picks[BattleSimulator.strategy_balanced()].AsString(),
            enemy_picks[BattleSimulator.strategy_all_out()].AsString(),
            "敌人不受玩家策略影响");
        CheckEq(
            enemy_picks[BattleSimulator.strategy_balanced()].AsString(),
            enemy_picks[BattleSimulator.strategy_conservative()].AsString(),
            "敌人不受玩家策略影响（保守）");
    }

    /// <summary>一个回合里敌人打出的总伤害（拆招减半的对照：两条路只有「拆不拆」不同）。</summary>
    private int _EnemyRoundDamage(TableDb db, bool with_parry)
    {
        BattleActor hero = _Actor("hero", 10.0, BattleActor.side_ally(), 400);
        BattleActor foe = _Actor("foe", 9.0, BattleActor.side_enemy(), 400);
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(new Godot.Collections.Array { hero }, new Godot.Collections.Array { foe }, _Modifiers());
        sim.begin_round();
        BattleActor actor = sim.current_actor() as BattleActor;
        if (with_parry)
        {
            sim.parry(actor, foe);
        }
        else
        {
            sim.act(actor, sim.available_skills(actor)[0].As<Resource>(), foe);
        }

        sim.auto_act();
        int total = 0;
        foreach (Variant entry in sim.take_events())
        {
            Godot.Collections.Dictionary ev = entry.AsGodotDictionary();
            if (ev["kind"].AsString() == "hit" && ev["source"].AsString() == "foe")
            {
                total += ev["damage"].AsInt32();
            }
        }

        return total;
    }

    /// <summary>
    /// 逃跑（设计 02：被追上第一回合不能逃）。成功率公式是开发侧定的，这里钉住口径与两个分支。
    /// </summary>
    private void _CheckFlee(TableDb db)
    {
        double even = _FleeChanceWith(db, 10.0, 10.0);
        CheckFloat(even, 0.5, "同身法时成功率 50%");
        CheckGt(_FleeChanceWith(db, 30.0, 10.0), even, "身法高的一边更容易逃掉");
        CheckLt(_FleeChanceWith(db, 10.0, 30.0), even, "身法低的一边更难得手");
        // 上下限要**顶到**才算钉住：写成 `<= 0.95` 时，把上限调到 0.85 也照样绿（变异探针实测）。
        CheckFloat(_FleeChanceWith(db, 1000.0, 1.0), 0.95, "身法碾压时成功率顶到上限 95%");
        CheckFloat(_FleeChanceWith(db, 1.0, 1000.0), 0.25, "身法被碾压时成功率顶到下限 25%");

        // 成功：以「撤退」收场，没有奖励
        Godot.Collections.Dictionary win = _FleeFight(
            db, new Godot.Collections.Dictionary { { "force_flee", true } });
        CheckTrue(win["escaped"].AsBool(), "force_flee 下必定撤成功");
        CheckEq(win["winner"].AsString(), BattleSimulator.winner_flee(), "撤退是 flee 结局，不是胜负");
        CheckEq(win["rewards"].AsGodotDictionary()["exp"].AsInt32(), 0, "撤退不结算经验");
        CheckEq(win["rewards"].AsGodotDictionary()["money"].AsInt32(), 0, "撤退不结算铜钱");

        // 失败：白费一次行动，战斗继续
        Godot.Collections.Dictionary fail = _FleeFight(
            db, new Godot.Collections.Dictionary { { "no_flee", true } });
        CheckFalse(fail["escaped"].AsBool(), "no_flee 下撤不掉");
        CheckNe(fail["winner"].AsString(), BattleSimulator.winner_flee(), "撤不掉就不是撤退结局");

        // 被追上：第一回合撤不了，过了第一回合就能撤
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor hero = _Actor("hero", 30.0, BattleActor.side_ally(), 400);
        BattleActor foe = _Actor("foe", 5.0, BattleActor.side_enemy(), 400);
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary { { "encounter", _CaughtEncounter(db) }, { "modifiers", _FixedMods() } });
        sim.begin_round();
        string blocked = sim.flee_block_reason(hero);
        CheckTrue(blocked.Contains("第一回合"), $"被追上的第一回合撤不掉：{blocked}");
        sim.step_round();
        sim.begin_round();
        CheckEq(sim.flee_block_reason(hero), "", "过了第一回合就能撤");
    }

    /// <summary>
    /// 气血「正好被打死」：`take_damage(hp)` 必须让它倒下（归 0、is_alive 假），差 1 点不能；
    /// 伤害只减不加——0／负数不许当治疗用，倒下的单位也不掉负血。
    /// </summary>
    private void _CheckExactKill(TableDb db)
    {
        BattleActor hero = _Actor("hero", 10.0, BattleActor.side_ally(), 40);
        CheckEq(hero.take_damage(39), 39, "差 1 点：伤害全吃下");
        CheckTrue(hero.is_alive(), "还剩 1 血，站着");
        CheckEq(hero.hp, 1, "气血正好剩 1");
        CheckEq(hero.take_damage(5), 1, "超出剩余气血的伤害只按剩下的扣");
        CheckEq(hero.hp, 0, "正好打空：气血归 0");
        CheckFalse(hero.is_alive(), "气血 0 就是倒下");
        CheckEq(hero.take_damage(10), 0, "倒下的单位再挨打不掉负血");
        CheckEq(hero.hp, 0, "气血不会变负");
        CheckEq(hero.take_damage(-5), 0, "负伤害不是治疗");
        CheckEq(hero.hp, 0, "负伤害不改气血");
    }

    private double _FleeChanceWith(TableDb db, double my_speed, double their_speed)
    {
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor hero = _Actor("hero", my_speed, BattleActor.side_ally(), 400);
        BattleActor foe = _Actor("foe", their_speed, BattleActor.side_enemy(), 400);
        sim.setup(new Godot.Collections.Array { hero }, new Godot.Collections.Array { foe }, new Godot.Collections.Dictionary());
        return sim.flee_chance(hero);
    }

    private Godot.Collections.Dictionary _FleeFight(TableDb db, Godot.Collections.Dictionary modifiers)
    {
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        BattleActor hero = _Actor("hero", 10.0, BattleActor.side_ally(), 400);
        BattleActor foe = _Actor("foe", 10.0, BattleActor.side_enemy(), 400);
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary { { "modifiers", modifiers } });
        sim.begin_round();
        Godot.Collections.Dictionary result = sim.flee(hero);
        CheckTrue(result["ok"].AsBool(), $"逃跑判定能跑：{result["error"].AsString()}");
        Godot.Collections.Dictionary out_result = sim.result();
        out_result["escaped"] = result["escaped"].AsBool();
        return out_result;
    }

    private static Encounter _CaughtEncounter(TableDb db)
    {
        var spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        var team = db.get_row("enemy_team", spawn.Get("team_id").AsString());
        return Encounter.build(db, spawn, team, Encounter.contact_caught(), "normal");
    }

    /// <summary>0.4.0 起 `character_base` 只有一个角色模板，队伍由同一个模板复制出多人。</summary>
    private static Godot.Collections.Array _Party(TableDb db, int level)
    {
        var out_rows = new Godot.Collections.Array();
        Resource row = db.get_row("character_base", "scholar_fallen");
        for (int index = 0; index < 2; index++)
        {
            BattleActor actor = BattleActor.from_character(
                db, row, level, new Godot.Collections.Dictionary(), new Godot.Collections.Array());
            actor.actor_id = $"scholar_{index + 1}";
            actor.display_name = $"书生 {index + 1}";
            out_rows.Add(actor);
        }

        return out_rows;
    }

    /// <summary>
    /// `target_type=self` 的招式必须打**自己**（反噬／自伤那一类）。
    ///
    /// 06 允许 `single / self / all_enemy`，代码却只在手选路径认了 `all_enemy`、在自动路径也认了它——
    /// `self` 两处都掉进「打对面的目标」分支：**自伤招式会打到敌人身上**（2026-10-03 修，见决策 224）。
    /// 发行数据里还没有这类招式，所以用夹具（复制表 + 加一条 self 招式）把两条行动路径都走一遍。
    /// </summary>
    private void _CheckSelfTargetSkill(TableDb db)
    {
        var stub = new TableDb();
        stub.load_all();
        var rows = (TableResource)stub.tables["skill_active"].As<TableResource>().Duplicate(true);
        var self_skill = new SkillActiveRow
        {
            id = "sk_test_self",
            skill_id = "sk_test_self",
            element = "external",
            damage_type = "dmg_normal",
            power_ratio = 1.0,
            hit_count = 1,
            target_type = "self",
        };
        // 手工建的行要**自己写主键与索引**：`get_row` 查的是 `index` 字典，加载器平时会写这两样，
        // 夹具不补就会出现「表里有这行、get_row 却找不到」（这个坑踩过一次）。
        var new_rows = new Godot.Collections.Array();
        foreach (Variant entry in rows.rows)
        {
            new_rows.Add(entry);
        }

        new_rows.Add(self_skill);
        rows.rows = new_rows;
        rows.index["sk_test_self"] = new_rows.Count - 1;
        stub.tables["skill_active"] = rows;

        // ① 手选路径：点着敌人放，也得打自己。（第三个实参照旧链原样传修饰词典本身——
        // 它落在 options 位上、不是 `{"modifiers": …}`，所以这一节本来就不吃那三条修饰，别顺手"修好"。）
        BattleSimulator sim = BattleSimulator.create(stub, RngService.create(Seed));
        BattleActor hero = _Actor("hero", 10.0, BattleActor.side_ally());
        BattleActor foe = _Actor("foe", 10.0, BattleActor.side_enemy());
        hero.skills = new[] { "sk_test_self" };
        sim.setup(new Godot.Collections.Array { hero }, new Godot.Collections.Array { foe }, _FixedMods());
        int hero_hp = hero.hp;
        int foe_hp = foe.hp;
        sim.act(hero, self_skill, foe);
        CheckLt(hero.hp, hero_hp, "self 招式打自己（手选：点着敌人也一样）");
        CheckEq(foe.hp, foe_hp, "敌人一点血都没掉（以前这里会误伤敌人）");

        // ② 自动战斗路径：同一个口径
        BattleSimulator sim2 = BattleSimulator.create(stub, RngService.create(Seed));
        BattleActor hero2 = _Actor("hero", 10.0, BattleActor.side_ally());
        BattleActor foe2 = _Actor("foe", 10.0, BattleActor.side_enemy());
        hero2.skills = new[] { "sk_test_self" };
        sim2.setup(new Godot.Collections.Array { hero2 }, new Godot.Collections.Array { foe2 }, _FixedMods());
        sim2.begin_round();
        int hero2_hp = hero2.hp;
        int foe2_hp = foe2.hp;
        Godot.Collections.Array lines = sim2.auto_act();
        CheckLt(hero2.hp, hero2_hp, "self 招式打自己（自动战斗同样遵守 target_type）");
        CheckEq(foe2.hp, foe2_hp, "自动战斗下敌人也不掉血");
        CheckTrue(_LogText(lines).Contains("hero"), $"战报里点名的是自己：{_LogText(lines)}");
    }

    private static BattleActor _Actor(string actor_id, double speed, int side, int hp = 400)
    {
        var actor = new BattleActor
        {
            actor_id = actor_id,
            display_name = actor_id,
            side = side,
            level = 5,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", (double)hp },
                { "qi_max", 20.0 },
                { "atk_phys", 10.0 },
                { "atk_qi", 0.0 },
                { "def_phys", 0.0 },
                { "def_qi", 0.0 },
                { "speed", speed },
                { "hit_rate", 1.0 },
                { "dodge_rate", 0.0 },
                { "crit_rate", 0.0 },
                { "crit_dmg", 0.0 },
            },
            skills = new[] { "sk_xuanwei_01" },
        };
        actor.refill();
        return actor;
    }

    private static string _Names(Godot.Collections.Array actors)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry in actors)
        {
            parts.Add(entry.As<BattleActor>().actor_id);
        }

        return string.Join(",", parts);
    }

    /// <summary>把一次行动的日志行拼成一串（断言「打了几段」这类文字要看全文，别只看第一行）。</summary>
    private static string _LogText(Godot.Collections.Array lines)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant line in lines)
        {
            parts.Add(line.AsString());
        }

        return string.Join("｜", parts);
    }

    private static string _LogText(Variant lines) => _LogText(lines.AsGodotArray());
}
