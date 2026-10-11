using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 伤害结算管线：顺序、系数、边界（对位 `tests/test_damage.gd`，2026-10-11 P4 第三十批整体搬过来）。
///
/// 盯的是**九步管线的顺序**与每一步的黄金数值（固定输入 ＋ 关浮动 → 可手算）：
/// 攻击力×倍率×熟练度 → 暴击 → 元素克制 → 防御减免（穿透压在**有效防御**上）→ 格挡 →
/// 增伤／减伤 → 浮动 → 命中／闪避 → 取整。边界都在这里钉：各项 cap（穿透 0.75／格挡 0.8／
/// 减伤 0.6／命中 5%~99%）、闪避与格挡互斥、未命中伤害为 0、持续伤害不结算直伤、反伤类型显式拒绝。
/// </summary>
public partial class TestDamage : TestCase
{
    private const int Seed = 20261002;

    public override string SuiteName() => "伤害结算管线";

    public override void Run()
    {
        TableDb db = GetDb();
        // 绝对值断言：变异探针点名「没人钉」——crit_dmg 的兜底值（combat_const 缺行时才用）
        CheckFloat(DamageResolver.default_crit_dmg(), 0.5, "暴击伤害兜底值 0.5（决策 155 的口径）", 0.0001);
        _CheckGoldenPipeline(db);
        _CheckElementCounter(db);
        _CheckCritBeforeDefense(db);
        _CheckMastery(db);
        _CheckPenetration(db);
        _CheckBlock(db);
        _CheckDamageReduction(db);
        _CheckModifiers(db);
        _CheckVariance(db);
        _CheckHitAndDodge(db);
        _CheckUnsupportedTypes(db);
        _CheckDefenseCurve(db);
    }

    /// <summary>固定输入 ＋ 关闭浮动 → 可手算的黄金数值。</summary>
    private void _CheckGoldenPipeline(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor attacker = _Attacker();
        BattleActor defender = _Defender();
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary result = resolver.resolve(
            attacker, defender, skill, _Mods(("force_hit", true), ("no_variance", true)));
        Godot.Collections.Dictionary detail = _Detail(result);
        CheckTrue(result["ok"].AsBool(), "外功直伤应结算成功");
        CheckTrue(result["is_hit"].AsBool(), "force_hit 时必定命中");
        // 0.6.0 起传 skill_active 行：D = 100 × 1.6 × 1.0（熟练度 0）= 160
        CheckFloat(_d(detail, "step_d"), 160.0, "第 1 步：攻击力 × 倍率 × 熟练度系数");
        CheckFloat(_d(detail, "mastery_multiplier"), 1.0, "没练过时熟练度系数是 1.0");
        CheckFloat(_d(detail, "defense_reduction"), 0.1875, "防御减免公式");
        CheckFloat(_d(detail, "step_r"), 130.0, "第 5 步：防御减免后数值");
        CheckFloat(_d(detail, "pen_rate"), 0.0, "没有穿透时穿透率为 0");
        CheckFloat(_d(detail, "effective_defense"), 60.0, "没有穿透时有效防御等于原始防御");
        CheckFalse(result["is_blocked"].AsBool(), "没有格挡时不触发格挡");
        CheckEq(result["raw_damage"].AsInt32(), 130, "第 9 步：floor 取整");
        CheckEq(result["damage"].AsInt32(), 130, "最终伤害");
    }

    private void _CheckElementCounter(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        BattleActor attacker = _Attacker();
        BattleActor defender = _Defender();
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true), ("no_variance", true));

        var internal_skill = db.get_row("skill_active", "sk_xuanwei_qi_01");
        Godot.Collections.Dictionary internal_result = resolver.resolve(attacker, defender, internal_skill, mods);
        CheckFloat(_d(_Detail(internal_result), "element_multiplier"), 1.3, "内功克外功 +30%");
        CheckEq(internal_result["damage"].AsInt32(), 79, "内功黄金数值");

        var odd_skill = db.get_row("skill_active", "sk_wudu_01");
        Godot.Collections.Dictionary odd_result = resolver.resolve(attacker, defender, odd_skill, mods);
        CheckFloat(_d(_Detail(odd_result), "element_multiplier"), 0.8, "奇诡被外功克 −20%");
        CheckEq(odd_result["damage"].AsInt32(), 39, "奇诡黄金数值（力智均值）");

        defender.tags["element"] = "internal";
        Godot.Collections.Dictionary countered = resolver.resolve(
            attacker, defender, db.get_row("skill_active", "sk_xuanwei_03"), mods);
        CheckFloat(_d(_Detail(countered), "element_multiplier"), 0.8, "外功打内功被克");
        CheckEq(countered["damage"].AsInt32(), 104, "被克时的黄金数值");
    }

    private void _CheckCritBeforeDefense(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var crit_skill = (SkillActiveRow)db.get_row("skill_active", "sk_xuanwei_03").Duplicate(true);
        crit_skill.damage_type = "dmg_crit";
        Godot.Collections.Dictionary result = resolver.resolve(
            _Attacker(), _Defender(), crit_skill,
            _Mods(("force_hit", true), ("force_crit", true), ("no_variance", true)));
        CheckTrue(result["is_crit"].AsBool(), "force_crit 应判定为暴击");
        // 0.4.0 起暴击倍率改为 crit_base_mult(1.5) + 暴击伤害(0.5) = 2.0
        CheckFloat(_d(_Detail(result), "crit_multiplier"), 2.0, "暴击倍率 = crit_base_mult + 暴击伤害");
        // (160 × 2.0) × 0.8125 = 260
        CheckEq(result["damage"].AsInt32(), 260, "九步管线下的暴击黄金数值");

        Godot.Collections.Dictionary non_crit = resolver.resolve(
            _Attacker(), _Defender(), db.get_row("skill_active", "sk_xuanwei_03"),
            _Mods(("force_hit", true), ("no_variance", true)));
        CheckFalse(non_crit["is_crit"].AsBool(), "dmg_normal 不参与暴击判定");
    }

    /// <summary>熟练度：招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)。</summary>
    private void _CheckMastery(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true), ("no_variance", true));

        // 玄微剑法·穿云式是 ★2，mastery_gain 0.045；练满 10 级 → ×1.45
        BattleActor veteran = _Attacker();
        veteran.skill_mastery["sk_xuanwei_03"] = 10;
        Godot.Collections.Dictionary result = resolver.resolve(veteran, _Defender(), skill, mods);
        Godot.Collections.Dictionary detail = _Detail(result);
        CheckEq(_i(detail, "star"), 2, "星级取自 skill_base");
        CheckEq(_i(detail, "mastery"), 10, "熟练度取自战斗单位");
        CheckFloat(_d(detail, "mastery_multiplier"), 1.45, "★2 练满 = 1 + 10 × 0.045");
        CheckFloat(_d(detail, "step_d"), 232.0, "第 1 步乘上熟练度系数（160 × 1.45）");
        CheckEq(result["damage"].AsInt32(), 188, "练满后的黄金数值");

        // ★1 成长更快、★5 更慢：同一熟练度下低星倍率更高
        var rookie_skill = db.get_row("skill_active", "sk_common_01");   // ★1
        var master_skill = db.get_row("skill_active", "sk_drunk_01");    // ★5
        veteran.skill_mastery["sk_common_01"] = 10;
        veteran.skill_mastery["sk_drunk_01"] = 10;
        Godot.Collections.Dictionary rookie = resolver.resolve(veteran, _Defender(), rookie_skill, mods);
        Godot.Collections.Dictionary master = resolver.resolve(veteran, _Defender(), master_skill, mods);
        CheckFloat(_d(_Detail(rookie), "mastery_multiplier"), 1.5, "★1 练满 = 1.50");
        CheckFloat(_d(_Detail(master), "mastery_multiplier"), 1.25, "★5 练满 = 1.25");
    }

    /// <summary>穿透：压的是「有效防御」，不是减免结果——这两个写法算出来的数不一样。</summary>
    private void _CheckPenetration(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true), ("no_variance", true));

        BattleActor piercing = _Attacker();
        piercing.stats["pen_rate"] = 0.5;
        Godot.Collections.Dictionary result = resolver.resolve(piercing, _Defender(), skill, mods);
        Godot.Collections.Dictionary detail = _Detail(result);
        CheckFloat(_d(detail, "pen_rate"), 0.5, "穿透率取自攻击者属性");
        CheckFloat(_d(detail, "effective_defense"), 30.0, "有效防御 = 60 × (1 − 0.5)");
        CheckFloat(_d(detail, "defense_reduction"), 30.0 / 290.0, "减免按有效防御计算");
        CheckEq(result["damage"].AsInt32(), 143, "穿透后的黄金数值");
        // 对照：若把穿透错用在减免结果上，会得到 160 × (1 − 0.1875×0.5) = 145
        double wrong_shape = 160.0 * (1.0 - 0.1875 * 0.5);
        CheckNe((int)System.Math.Floor(wrong_shape), result["damage"].AsInt32(), "穿透必须作用在防御上，而非减免结果上");

        // 穿透上限取 stat_def 的 cap 0.75
        BattleActor over = _Attacker();
        over.stats["pen_rate"] = 0.95;
        Godot.Collections.Dictionary capped = resolver.resolve(over, _Defender(), skill, mods);
        CheckFloat(_d(_Detail(capped), "pen_rate"), 0.75, "穿透率夹在 0.75 以内");
        CheckEq(capped["damage"].AsInt32(), 151, "穿透上限下的黄金数值");

        // 穿透对高防御目标收益更大：同一点穿透，防御越高省得越多
        BattleActor low_def = _Defender();
        low_def.stats["def_phys"] = 20.0;
        BattleActor high_def = _Defender();
        high_def.stats["def_phys"] = 120.0;
        double low_gain = _StepR(resolver, piercing, low_def, skill, mods)
            - _StepR(resolver, _Attacker(), low_def, skill, mods);
        double high_gain = _StepR(resolver, piercing, high_def, skill, mods)
            - _StepR(resolver, _Attacker(), high_def, skill, mods);
        CheckGt(high_gain, low_gain, "同样的穿透，对高防御目标的绝对收益更大");
    }

    /// <summary>格挡：防御之后的独立乘区；闪避与格挡互斥（先闪避，未闪避才判格挡）。</summary>
    private void _CheckBlock(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");

        BattleActor blocker = _Defender();
        blocker.stats["block_rate"] = 0.5;
        blocker.stats["block_reduction"] = 0.3;
        Godot.Collections.Dictionary result = resolver.resolve(
            _Attacker(), blocker, skill,
            _Mods(("force_hit", true), ("force_block", true), ("no_variance", true)));
        Godot.Collections.Dictionary detail = _Detail(result);
        CheckTrue(result["is_blocked"].AsBool(), "force_block 应触发格挡");
        CheckFloat(_d(detail, "step_r"), 130.0, "先算防御减免（格挡在防御之后）");
        CheckFloat(_d(detail, "step_b"), 91.0, "再乘格挡减伤：130 × (1 − 0.3)");
        CheckEq(result["damage"].AsInt32(), 91, "格挡后的黄金数值");

        BattleActor capped_blocker = _Defender();
        capped_blocker.stats["block_rate"] = 0.5;
        capped_blocker.stats["block_reduction"] = 0.95;
        Godot.Collections.Dictionary capped = resolver.resolve(
            _Attacker(), capped_blocker, skill,
            _Mods(("force_hit", true), ("force_block", true), ("no_variance", true)));
        CheckTrue(capped["is_blocked"].AsBool(), "force_block 必定触发格挡");
        Godot.Collections.Dictionary capped_detail = _Detail(capped);
        CheckFloat(_d(capped_detail, "block_reduction"), 0.8, "格挡减伤率夹在 stat_def 的 0.8 以内");
        CheckFloat(_d(capped_detail, "step_b"), 26.0, "格挡上限后的中间值");
        // 130 × (1 − 0.8) 在浮点里是 25.999…，floor 后是 25——顺手钉住这个边界
        CheckEq(capped["damage"].AsInt32(), 25, "格挡上限下的黄金数值（浮点边界取 floor）");

        // 格挡率本身有上限（stat_def 0.75）：写 1.0 也只会按 0.75 掷
        BattleActor over_cap = _Defender();
        over_cap.stats["block_rate"] = 1.0;
        var blocked_count = 0;
        Godot.Collections.Dictionary hit_only = _Mods(("force_hit", true), ("no_variance", true));
        for (var index = 0; index < 400; index++)
        {
            if (resolver.resolve(_Attacker(), over_cap, skill, hit_only)["is_blocked"].AsBool())
            {
                blocked_count += 1;
            }
        }

        CheckInRange(blocked_count, 240.0, 360.0, "格挡率被夹到 0.75 后大致四分之三概率格挡");

        // 闪避与格挡互斥：闪避成功时直接免伤，不再判格挡
        BattleActor blind = _Attacker();
        blind.stats["hit_rate"] = 0.0;
        BattleActor evasive = _Defender();
        evasive.stats["dodge_rate"] = 0.9;
        evasive.stats["block_rate"] = 1.0;
        var dodges = 0;
        Godot.Collections.Dictionary no_variance = _Mods(("no_variance", true));
        for (var index = 0; index < 200; index++)
        {
            Godot.Collections.Dictionary miss_result = resolver.resolve(blind, evasive, skill, no_variance);
            if (!miss_result["is_hit"].AsBool())
            {
                dodges += 1;
                CheckFalse(miss_result["is_blocked"].AsBool(), "被闪避时不判格挡");
                CheckFalse(_b(_Detail(miss_result), "blocked", false), "被闪避时不记录格挡");
            }
        }

        CheckGt(dodges, 0, "低命中下应出现闪避");
    }

    /// <summary>减伤：终局乘区，装备属性 ＋ 临时效果叠加，并夹到上限。</summary>
    private void _CheckDamageReduction(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true), ("no_variance", true));

        BattleActor armored = _Defender();
        armored.stats["dmg_reduction"] = 0.25;
        Godot.Collections.Dictionary result = resolver.resolve(_Attacker(), armored, skill, mods);
        Godot.Collections.Dictionary detail = _Detail(result);
        CheckFloat(_d(detail, "dmg_reduction"), 0.25, "减伤取自防守方属性");
        CheckFloat(_d(detail, "step_m"), 97.5, "130 × (1 − 0.25)");
        CheckEq(result["damage"].AsInt32(), 97, "减伤后的黄金数值");

        Godot.Collections.Dictionary stacked = resolver.resolve(
            _Attacker(), armored, skill,
            _Mods(("force_hit", true), ("no_variance", true), ("dmg_down", 0.1)));
        CheckFloat(_d(_Detail(stacked), "dmg_reduction"), 0.35, "临时减伤叠加在装备减伤之上");
        CheckEq(stacked["damage"].AsInt32(), 84, "叠加后的黄金数值");

        BattleActor capped_defender = _Defender();
        capped_defender.stats["dmg_reduction"] = 0.9;
        Godot.Collections.Dictionary capped = resolver.resolve(_Attacker(), capped_defender, skill, mods);
        CheckFloat(_d(_Detail(capped), "dmg_reduction"), 0.6, "减伤率夹在 stat_def 的 0.6 以内");
        CheckEq(capped["damage"].AsInt32(), 52, "减伤上限下的黄金数值");

        Godot.Collections.Dictionary boosted = resolver.resolve(
            _Attacker(), _Defender(), skill,
            _Mods(("force_hit", true), ("no_variance", true), ("dmg_up", 0.2)));
        CheckEq(boosted["damage"].AsInt32(), 156, "第 7 步的增伤");
    }

    /// <summary>取第 5 步的中间值，用来比较穿透收益（避免取整带来的噪声）。</summary>
    private static double _StepR(
        DamageResolver resolver, GodotObject attacker, GodotObject defender,
        GodotObject skill, Godot.Collections.Dictionary modifiers) =>
        _d(_Detail(resolver.resolve(attacker, defender, skill, modifiers)), "step_r");

    private void _CheckModifiers(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        Godot.Collections.Dictionary boosted = resolver.resolve(
            _Attacker(), _Defender(), skill,
            _Mods(("force_hit", true), ("no_variance", true), ("attack_stat_bonus", 0.1)));
        CheckFloat(_d(_Detail(boosted), "step_a"), 176.0, "第 2 步：攻击属性系数 +10%");
        CheckEq(boosted["damage"].AsInt32(), 143, "攻击属性系数影响黄金数值");

        Godot.Collections.Dictionary mitigated = resolver.resolve(
            _Attacker(), _Defender(), skill,
            _Mods(("force_hit", true), ("no_variance", true), ("dmg_up", 0.2), ("dmg_down", 0.1)));
        CheckEq(mitigated["damage"].AsInt32(), 140, "第 7 步：增伤与临时减伤相乘");
    }

    private void _CheckVariance(TableDb db)
    {
        RngService rng = RngService.create(Seed);
        DamageResolver resolver = DamageResolver.create(db, rng);
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        // 无浮动基准值 130 → 浮动区间 [123.5, 136.5]
        Godot.Collections.Dictionary mods = _Mods(("force_hit", true));
        for (var index = 0; index < 400; index++)
        {
            Godot.Collections.Dictionary result = resolver.resolve(_Attacker(), _Defender(), skill, mods);
            double variance = _d(_Detail(result), "variance");
            CheckTrue(
                variance >= 0.95 && variance <= 1.05,
                $"浮动系数落在 0.95~1.05（第 {index} 次：{variance}）");
            if (index < 40)
            {
                CheckInRange(result["raw_damage"].AsDouble(), 123.0, 137.0, "浮动后的伤害落在理论区间");
            }
        }
    }

    private void _CheckHitAndDodge(TableDb db)
    {
        RngService rng = RngService.create(Seed);
        DamageResolver resolver = DamageResolver.create(db, rng);
        var skill = db.get_row("skill_active", "sk_xuanwei_03");
        CheckFloat(resolver.hit_chance(_Attacker(), _Defender()), 0.7, "命中率 = 命中 − 闪避");

        BattleActor blind = _Attacker();
        blind.stats["hit_rate"] = 0.0;
        BattleActor dodgy = _Defender();
        dodgy.stats["dodge_rate"] = 0.9;
        CheckFloat(resolver.hit_chance(blind, dodgy), 0.05, "命中率下限 5%");

        BattleActor sharp = _Attacker();
        sharp.stats["hit_rate"] = 1.0;
        BattleActor slow = _Defender();
        slow.stats["dodge_rate"] = 0.0;
        CheckFloat(resolver.hit_chance(sharp, slow), 0.99, "命中率上限 99%");

        // 玩家角色走「基准 1.0 + 命中 − 闪避」：敏给的是加成，不是绝对命中
        BattleActor character_like = _Attacker();
        character_like.base_accuracy = 1.0;
        character_like.stats["hit_rate"] = 0.05;
        BattleActor wolf_like = _Defender();
        wolf_like.stats["dodge_rate"] = 0.10;
        CheckFloat(resolver.hit_chance(character_like, wolf_like), 0.95, "玩家命中基准 1.0，敏的命中是加成");

        var hits = 0;
        Godot.Collections.Dictionary no_variance = _Mods(("no_variance", true));
        for (var index = 0; index < 400; index++)
        {
            Godot.Collections.Dictionary result = resolver.resolve(blind, dodgy, skill, no_variance);
            if (result["is_hit"].AsBool())
            {
                hits += 1;
            }
            else
            {
                CheckEq(result["damage"].AsInt32(), 0, "未命中时伤害为 0");
            }
        }

        CheckInRange(hits, 1.0, 60.0, "5% 命中率下 400 次应只有少量命中");
    }

    private void _CheckUnsupportedTypes(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        var dot_skill = db.get_row("skill_active", "sk_wudu_02");
        Godot.Collections.Dictionary dot_result = resolver.resolve(
            _Attacker(), _Defender(), dot_skill, new Godot.Collections.Dictionary());
        // 持续伤害不再被拒：这一下没有直伤，只负责命中判定 + 挂层（每层伤害走 resolve_dot 的快照）
        CheckTrue(dot_result["ok"].AsBool(), "持续伤害类型能走通管线");
        CheckEq(dot_result["damage"].AsInt32(), 0, "持续伤害招式本身不结算直伤");
        CheckTrue(_b(_Detail(dot_result), "dot", false), "detail 标记这是持续伤害");
        Godot.Collections.Dictionary dot_snapshot = resolver.resolve_dot(
            _Attacker(), _Defender(), dot_skill, new Godot.Collections.Dictionary(), "");
        CheckTrue(dot_snapshot["ok"].AsBool(), "每层快照伤害算得出来");
        CheckGt(dot_snapshot["damage"].AsDouble(), 0, $"快照伤害大于 0：{dot_snapshot["damage"].AsInt32()}");

        var reflect_skill = (SkillActiveRow)db.get_row("skill_active", "sk_xuanwei_03").Duplicate(true);
        reflect_skill.damage_type = "dmg_reflect";
        Godot.Collections.Dictionary reflect_result = resolver.resolve(
            _Attacker(), _Defender(), reflect_skill, new Godot.Collections.Dictionary());
        CheckFalse(reflect_result["ok"].AsBool(), "反伤类型应显式拒绝");
    }

    private void _CheckDefenseCurve(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(Seed));
        CheckEq(resolver.defense_reduction(0.0, 10), 0.0, "没有防御就没有减免");
        CheckLt(
            resolver.defense_reduction(10.0, 10), resolver.defense_reduction(100.0, 10),
            "防御越高减免越高");
        CheckLt(resolver.defense_reduction(100000.0, 10), 1.0, "减免恒小于 100%");
        CheckGt(
            resolver.defense_reduction(100.0, 1), resolver.defense_reduction(100.0, 20),
            "攻击者等级越高，分母越大、减免越低");
    }

    // ---------------------------------------------------------------- 夹具与小工具

    /// <summary>手搓攻击者：走「绝对命中率」语义（命中 − 闪避 就是命中率）。</summary>
    private static BattleActor _Attacker()
    {
        var actor = new BattleActor
        {
            actor_id = "test_attacker",
            display_name = "测试攻击者",
            side = BattleActor.side_ally(),
            level = 10,
            base_accuracy = 0.0,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", 500.0 }, { "qi_max", 100.0 },
                { "atk_phys", 100.0 }, { "atk_qi", 50.0 },
                { "def_phys", 20.0 }, { "def_qi", 20.0 },
                { "speed", 20.0 }, { "hit_rate", 0.9 }, { "dodge_rate", 0.1 },
                { "crit_rate", 0.5 }, { "crit_dmg", 0.5 },
                { "pen_rate", 0.0 }, { "block_rate", 0.0 },
                { "block_reduction", 0.0 }, { "dmg_reduction", 0.0 },
            },
        };
        actor.refill();
        return actor;
    }

    private static BattleActor _Defender()
    {
        var actor = new BattleActor
        {
            actor_id = "test_defender",
            display_name = "测试防守者",
            side = BattleActor.side_enemy(),
            level = 5,
            base_accuracy = 0.0,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", 300.0 }, { "qi_max", 0.0 },
                { "atk_phys", 40.0 }, { "atk_qi", 0.0 },
                { "def_phys", 60.0 }, { "def_qi", 30.0 },
                { "speed", 10.0 }, { "hit_rate", 0.8 }, { "dodge_rate", 0.2 },
                { "crit_rate", 0.0 }, { "crit_dmg", 0.0 },
                { "pen_rate", 0.0 }, { "block_rate", 0.0 },
                { "block_reduction", 0.0 }, { "dmg_reduction", 0.0 },
            },
        };
        actor.refill();
        return actor;
    }

    private static Godot.Collections.Dictionary _Mods(params (string Key, Variant Value)[] pairs)
    {
        var mods = new Godot.Collections.Dictionary();
        foreach ((string key, Variant value) in pairs)
        {
            mods[key] = value;
        }

        return mods;
    }

    private static Godot.Collections.Dictionary _Detail(Godot.Collections.Dictionary result) =>
        result["detail"].AsGodotDictionary();

    private static double _d(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static bool _b(Godot.Collections.Dictionary dict, string key, bool fallback) =>
        dict.ContainsKey(key) ? dict[key].AsBool() : fallback;
}
