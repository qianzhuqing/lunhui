using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 伤害结算管线（C# 版，对位原 `src/core/damage_resolver.gd`，421 行）。
///
/// 严格按 docs/design/04_战斗与伤害.md 的九步走，顺序不可改：
///   0. 闪避判定：未命中 → 伤害为 0，后续全部跳过
///   1. D = 技能基础值 × 技能倍率    「技能基础值」= 攻击力（按系别取力/智/均值）
///   2. A = D × (1 + 攻击属性系数)    系数来自 buff 与被动
///   3. C = 直伤且暴击 ? A × (crit_base_mult + 暴击伤害) : A
///   4. K = C × 系别相克系数
///   5. 有效防御 = DEF × (1 - 穿透率)
///      防御减免 = 有效防御 / (有效防御 + def_const + def_level_coeff × 攻击者等级)
///      R = K × (1 - 防御减免)
///   6. B = 格挡判定成功 ? R × (1 - 格挡减伤率) : R
///   7. M = B × (1 + 增伤总和) × (1 - 减伤率)
///   8. F = M × 随机浮动(variance_min ~ variance_max)
///   9. 最终伤害 = floor(F)
///
/// 位置纪律：暴击与相克在防御之前、穿透在防御之前、格挡在防御之后、减伤在最后。
/// 闪避与格挡互斥：先判闪避（完全免伤），未闪避才判格挡（部分免伤）。
/// 全局常数（crit_base_mult / def_const / def_level_coeff / variance_*）来自 combat_const.csv，
/// 公式仍在代码里——参数在表、算法在代码。
/// 本里程碑只做直伤：dot_* 与 dmg_reflect 会被显式拒绝，不做持续结算（见 docs/dev/框架说明.md）。
///
/// **成员名保持 snake_case**：还没搬走的 GDScript 用例按名字调用（`BattleSimulator` 已是 C#），
/// C# 脚本按成员名暴露。构造走 `create(db, rng)` 静态工厂（C# 的默认参数对 GDScript／`Call` 不生效，
/// 所有对外方法参数写满）。
/// </summary>
public partial class DamageResolver : RefCounted
{
    // combat_const 缺行时的兜底默认值（正常运行时这些值都在表里）。
    public const double RNG_MIN_VARIANCE_DEFAULT = 0.95;
    public const double RNG_MAX_VARIANCE_DEFAULT = 1.05;
    public const double DEF_BASE_DEFAULT = 60.0;
    public const double DEF_LEVEL_FACTOR_DEFAULT = 20.0;
    public const double CRIT_BASE_MULT_DEFAULT = 1.5;
    // 比率上限的兜底；正常运行时取自 stat_def.max_value。
    // 注意：这四个值**必须**在 `stat_def` 里写上上限——表里留空（= 0 = 无上限）时会静默用这里的兜底，
    // 表与实际行为就分家了。`TableValidator.REQUIRED_CAPPED_STATS` 每次都盯着这件事（决策 155）。
    public const double PEN_RATE_CAP_DEFAULT = 0.75;
    public const double BLOCK_RATE_CAP_DEFAULT = 0.75;
    public const double BLOCK_REDUCTION_CAP_DEFAULT = 0.8;
    public const double DMG_REDUCTION_CAP_DEFAULT = 0.6;
    // 内伤抗性的上限兜底（同样是 stat_def.max_value 说了算；2026-10-03 从硬编码 0.75 改成读表，
    // 见框架说明决策 180——它是管线里最后一个"表里有数、代码却写死"的上限）。
    public const double RES_INTERNAL_CAP_DEFAULT = 0.75;
    public const double HIT_CHANCE_MIN = 0.05;
    public const double HIT_CHANCE_MAX = 0.99;
    /// enemy_base 没有暴击伤害列，敌人先统一用这个加成（0.5 = 暴击 +50%）。
    public const double DEFAULT_CRIT_DMG = 0.5;

    public const string DEFAULT_ELEMENT = "external";

    /// <summary>
    /// `DEFAULT_CRIT_DMG` 的取值口——GDScript 读不到 C# 的 `const`，用例读兜底值走这里。
    /// </summary>
    public static double default_crit_dmg() => DEFAULT_CRIT_DMG;

    private TableDb _db;
    private GodotObject _rng;
    /// "攻系|守系" → 倍率
    private readonly Godot.Collections.Dictionary _element_table = new();
    /// combat_const.const_id → value
    private readonly Godot.Collections.Dictionary _consts = new();
    private GrowthCalculator _growth;

    public static DamageResolver create(TableDb db, GodotObject rng) => new(db, rng);

    public DamageResolver(TableDb db, GodotObject rng)
    {
        _db = db;
        _rng = rng;
        _build_element_table();
        _build_constants();
        _growth = GrowthCalculator.create(db);
    }

    private void _build_element_table()
    {
        _element_table.Clear();
        foreach (Variant value in _db.rows("element_counter"))
        {
            var row = (ElementCounterRow)value.AsGodotObject();
            _element_table[$"{row.element_atk}|{row.element_def}"] = row.multiplier;
        }
    }

    private void _build_constants()
    {
        _consts.Clear();
        foreach (Variant value in _db.rows("combat_const"))
        {
            var row = (CombatConstRow)value.AsGodotObject();
            _consts[row.const_id] = row.value;
        }
    }

    /// <summary>取全局常数；表里缺行就报错并退回默认值。</summary>
    public double constant(string const_id, double fallback)
    {
        if (_consts.ContainsKey(const_id))
        {
            return _consts[const_id].AsDouble();
        }
        GD.PushError($"[DamageResolver] combat_const 缺少 {const_id}，退回默认值 {fallback}");
        return fallback;
    }

    public double crit_base_mult() => constant("crit_base_mult", CRIT_BASE_MULT_DEFAULT);

    public double def_const() => constant("def_const", DEF_BASE_DEFAULT);

    public double def_level_coeff() => constant("def_level_coeff", DEF_LEVEL_FACTOR_DEFAULT);

    public double variance_min() => constant("variance_min", RNG_MIN_VARIANCE_DEFAULT);

    public double variance_max() => constant("variance_max", RNG_MAX_VARIANCE_DEFAULT);

    /// <summary>比率上限从 stat_def.max_value 读，表里没配才用兜底值。</summary>
    private double _stat_cap(string stat_id, double fallback)
    {
        var row = _db.get_row("stat_def", stat_id) as StatDefRow;
        if (row != null && row.has_max())
        {
            return row.max_value;
        }
        return fallback;
    }

    public double element_multiplier(string atk_element, string def_element)
    {
        string key = $"{atk_element}|{def_element}";
        return _element_table.ContainsKey(key) ? _element_table[key].AsDouble() : 1.0;
    }

    /// <summary>按系别取攻击力：外功取力、内功取智、奇诡取两者均值。</summary>
    public double attack_value(GodotObject actor, string element)
    {
        return element switch
        {
            "internal" => _stat(actor, "atk_qi", 0.0),
            "odd" => (_stat(actor, "atk_phys", 0.0) + _stat(actor, "atk_qi", 0.0)) * 0.5,
            _ => _stat(actor, "atk_phys", 0.0),
        };
    }

    /// <summary>按伤害类型决定吃哪些防御。</summary>
    public double defense_value(GodotObject actor, GodotObject damage_type_row)
    {
        var row = damage_type_row as DamageTypeRow;
        double total = 0.0;
        if (row.use_def_phys)
        {
            total += _stat(actor, "def_phys", 0.0);
        }
        if (row.use_def_qi)
        {
            total += _stat(actor, "def_qi", 0.0);
        }
        // 灼伤每层削 5% 外功防御（status_effect.extra_rule = rule_burn_def_down）
        if (actor.HasMethod("defense_multiplier"))
        {
            total *= row.use_def_phys ? actor.Call("defense_multiplier", "def_phys").AsDouble() : 1.0;
        }
        return total;
    }

    /// <summary>攻击者的某个派生数值（战斗模拟器算异常触发率时要用同一份快照）</summary>
    public double stat_of(GodotObject actor, string stat_id, double fallback) => _stat(actor, stat_id, fallback);

    /// <summary>防御减免公式：防滚雪球的关键，分母随攻击者等级成长。</summary>
    public double defense_reduction(double defense, int attacker_level)
    {
        if (defense <= 0.0)
        {
            return 0.0;
        }
        return defense / (defense + def_const() + def_level_coeff() * System.Math.Max(attacker_level, 1));
    }

    /// <summary>
    /// 命中率 = 攻击者命中基准 + 命中 - 防守者闪避，夹在 [5%, 99%]。
    ///
    /// **敌我命中基准现在都是 1.0**（设计 10 §七第四条，0.28.0 答 Q57）：`hit_rate` 一律是
    /// 敏经 diminishing 曲线给的**加成**，真正决定打不打得中的是对方的 `dodge_rate`。
    /// 手搓的战斗单位（用例夹具）仍可以传 0.0 走「命中 − 闪避＝绝对命中率」的老语义。
    /// </summary>
    public double hit_chance(GodotObject attacker, GodotObject defender)
    {
        return System.Math.Clamp(
            _accuracy_base(attacker) + _stat(attacker, "hit_rate", 0.0) - _stat(defender, "dodge_rate", 0.0),
            HIT_CHANCE_MIN,
            HIT_CHANCE_MAX);
    }

    private double _accuracy_base(GodotObject actor)
    {
        Variant value = actor.Get("base_accuracy");
        if (value.VariantType != Variant.Type.Nil)
        {
            return value.AsDouble();
        }
        return 1.0;
    }

    /// <summary>
    /// 结算一次伤害。
    ///
    /// 0.6.0 起 `skill_row` 传的是 **skill_active 行**（招式数值），身份与星级去 skill_base 查；
    /// 第 1 步会乘上熟练度系数（`1 + 熟练度 × mastery_gain`）。
    ///
    /// modifiers 支持：
    ///   attack_stat_bonus  float  第 2 步的攻击属性系数
    ///   dmg_up             float  第 7 步的增伤
    ///   dmg_down           float  第 7 步的临时减伤（叠加在防守方 dmg_reduction 之上）
    ///   element_override   String 覆盖招式系别
    ///   force_hit / force_crit / force_block / no_block / no_variance  bool  测试与固定演出用
    /// </summary>
    public Godot.Collections.Dictionary resolve(
        GodotObject attacker,
        GodotObject defender,
        GodotObject skill_row,
        Godot.Collections.Dictionary modifiers)
    {
        string type_id = skill_row.Get("damage_type").AsString();
        var damage_type_row = _db.get_row("damage_type", type_id) as DamageTypeRow;
        if (damage_type_row == null)
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[DamageResolver] 招式 {skill_row.Get("skill_id").AsString()} 的 damage_type '{type_id}' 不存在");
            return _fail("这一招的伤害类型没配（数据错，已记进日志）");
        }
        if (damage_type_row.is_dot())
        {
            // 持续伤害不算直伤：这一下只负责「命中判定 + 把层数挂上去」，
            // 每层伤害由 resolve_dot() 在施加那一刻算好并锁死（快照制）。
            string dot_element = _override_element(modifiers, skill_row);
            bool dot_hit = true;
            double dot_chance = hit_chance(attacker, defender);
            if (damage_type_row.can_dodge && !_mod_bool(modifiers, "force_hit"))
            {
                dot_hit = _rng.Call("chance", dot_chance).AsBool();
            }
            return new Godot.Collections.Dictionary
            {
                { "ok", true },
                { "is_hit", dot_hit },
                { "is_crit", false },
                { "is_blocked", false },
                { "damage", 0 },
                { "raw_damage", 0 },
                {
                    "detail", new Godot.Collections.Dictionary
                    {
                        { "hit_chance", dot_chance },
                        { "element", dot_element },
                        { "blocked", false },
                        { "dot", true },
                    }
                },
            };
        }
        if (type_id == "dmg_reflect")
        {
            return _fail($"反伤未实现（本里程碑只做直伤）：{type_id}");
        }

        string skill_id = skill_row.Get("skill_id").AsString();
        var skill_base = _db.get_row("skill_base", skill_id) as SkillBaseRow;
        int star = skill_base != null ? skill_base.star : 1;
        int mastery = 0;
        if (attacker.HasMethod("mastery_of"))
        {
            mastery = attacker.Call("mastery_of", skill_id).AsInt32();
        }
        double mastery_multiplier = _growth.mastery_multiplier(star, mastery);
        // 天赋「天生武胆」（设计 12 §六：招式伤害 +15%）：**只对真招式**算——
        // 普通攻击没有 skill_base 行，不吃这一条（设计写的是"招式伤害"）。乘区放在第 7 步，
        // 免得插进九步顺序里。
        double talent_skill_factor = 1.0;
        if (skill_base != null)
        {
            Variant raw_rules = attacker.Get("talent_rules");
            var rules = raw_rules.VariantType == Variant.Type.Nil
                ? new Godot.Collections.Dictionary()
                : raw_rules.AsGodotDictionary();
            talent_skill_factor += _mod_double(rules, "skill_damage");
        }

        string element = _override_element(modifiers, skill_row);
        string defender_element = _dict_str(defender.Get("tags"), "element", DEFAULT_ELEMENT);

        // 0. 闪避判定：未命中就完全免伤，后续全部跳过（也就不再判格挡）
        double chance = hit_chance(attacker, defender);
        bool hit = true;
        if (damage_type_row.can_dodge && !_mod_bool(modifiers, "force_hit"))
        {
            hit = _rng.Call("chance", chance).AsBool();
        }
        if (!hit)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", true },
                { "is_hit", false },
                { "is_crit", false },
                { "is_blocked", false },
                { "damage", 0 },
                { "raw_damage", 0 },
                {
                    "detail", new Godot.Collections.Dictionary
                    {
                        { "hit_chance", chance },
                        { "element", element },
                        { "blocked", false },
                    }
                },
            };
        }

        // 1. 技能基础值 × 倍率 × 熟练度系数
        double step_d = attack_value(attacker, element) * skill_row.Get("power_ratio").AsDouble() * mastery_multiplier;
        // 2. 攻击属性系数
        double step_a = step_d * (1.0 + _mod_double(modifiers, "attack_stat_bonus"));
        // 3. 暴击（防御之前）：倍率 = crit_base_mult + 暴击伤害
        bool crit = false;
        if (damage_type_row.can_crit)
        {
            crit = _mod_bool(modifiers, "force_crit")
                || _rng.Call("chance", _stat(attacker, "crit_rate", 0.0)).AsBool();
        }
        double crit_multiplier = crit ? crit_base_mult() + _crit_damage(attacker) : 1.0;
        double step_c = step_a * crit_multiplier;
        // 4. 系别相克（同样在防御之前）
        double counter = element_multiplier(element, defender_element);
        double step_k = step_c * counter;
        // 5. 穿透压低有效防御，再算减免
        double pen_rate = System.Math.Clamp(
            _stat(attacker, "pen_rate", 0.0), 0.0, _stat_cap("pen_rate", PEN_RATE_CAP_DEFAULT));
        double raw_defense = defense_value(defender, damage_type_row);
        double effective_defense = raw_defense * (1.0 - pen_rate);
        double reduction = defense_reduction(effective_defense, attacker.Get("level").AsInt32());
        double step_r = step_k * (1.0 - reduction);
        // 6. 格挡：防御之后的独立乘区（能走到这里说明没被闪避）
        bool blocked = false;
        double block_reduction = 0.0;
        if (!_mod_bool(modifiers, "no_block"))
        {
            double block_rate = System.Math.Clamp(
                _stat(defender, "block_rate", 0.0), 0.0, _stat_cap("block_rate", BLOCK_RATE_CAP_DEFAULT));
            blocked = block_rate > 0.0
                && (_mod_bool(modifiers, "force_block") || _rng.Call("chance", block_rate).AsBool());
            if (blocked)
            {
                block_reduction = System.Math.Clamp(
                    _stat(defender, "block_reduction", 0.0),
                    0.0,
                    _stat_cap("block_reduction", BLOCK_REDUCTION_CAP_DEFAULT));
            }
        }
        double step_b = step_r * (1.0 - block_reduction);
        // 7. 增伤 / 减伤（减伤是终局乘区：装备属性 + 临时效果）
        // 天赋「天生武胆」的招式伤害加成也落在**增伤**这一区（不是另插一步，九步顺序不动）
        double dmg_up = (1.0 + _mod_double(modifiers, "dmg_up")) * talent_skill_factor;
        double total_reduction = System.Math.Clamp(
            _stat(defender, "dmg_reduction", 0.0) + _mod_double(modifiers, "dmg_down"),
            0.0,
            _stat_cap("dmg_reduction", DMG_REDUCTION_CAP_DEFAULT));
        double step_m = step_b * dmg_up * (1.0 - total_reduction);
        // 8. 随机浮动
        double variance = 1.0;
        if (!_mod_bool(modifiers, "no_variance"))
        {
            variance = _rng.Call("randf_range", variance_min(), variance_max()).AsDouble();
        }
        double step_f = step_m * variance;
        // 9. 取整
        int raw = (int)System.Math.Floor(step_f);

        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "is_hit", true },
            { "is_crit", crit },
            { "is_blocked", blocked },
            { "damage", System.Math.Max(raw, 1) },
            { "raw_damage", raw },
            {
                "detail", new Godot.Collections.Dictionary
                {
                    { "damage_type", type_id },
                    { "element", element },
                    { "defender_element", defender_element },
                    { "attack_value", attack_value(attacker, element) },
                    { "skill_id", skill_id },
                    { "star", star },
                    { "mastery", mastery },
                    { "mastery_multiplier", mastery_multiplier },
                    { "talent_skill_factor", talent_skill_factor },
                    { "step_d", step_d },
                    { "step_a", step_a },
                    { "step_c", step_c },
                    { "step_k", step_k },
                    { "step_r", step_r },
                    { "step_b", step_b },
                    { "step_m", step_m },
                    { "step_f", step_f },
                    { "crit_multiplier", crit_multiplier },
                    { "element_multiplier", counter },
                    { "pen_rate", pen_rate },
                    { "defense", raw_defense },
                    { "effective_defense", effective_defense },
                    { "defense_reduction", reduction },
                    { "blocked", blocked },
                    { "block_reduction", block_reduction },
                    { "dmg_reduction", total_reduction },
                    { "variance", variance },
                }
            },
        };
    }

    /// <summary>
    /// 每层持续伤害（快照）：同一套 1~5 步骨架，但**不吃暴击与格挡**；
    /// 闪避按 damage_type.can_dodge（由调用方判定），防御按 `damage_type.use_def_*` 逐类型决定——
    /// 按当前数据：**中毒与灼伤无视内外防御、流血吃外功防御、内伤吃内功防御**。
    /// （注：04_战斗与伤害.md 的伤害类型表把内伤写成「吃内防 否」，与数据/用例不一致，
    ///  已记当前状态缺口 #21 请设计确认；校验器 5.26 每次验收都会把这条点出来。）
    /// 强度乘 (1 + 异常强度)，最后照吃增伤/减伤与浮动——算完就锁进层里，之后不再重算。
    /// `dot_type_id` 传状态自己的伤害类型（`status_effect.damage_type`）：
    /// 中毒/灼伤/内伤该不该吃防御由**状态**决定，不是由挂它的那招决定
    /// （例：五毒散手是直伤招式，但它挂的中毒层照样「无视外功防御」）。
    /// </summary>
    public Godot.Collections.Dictionary resolve_dot(
        GodotObject attacker,
        GodotObject defender,
        GodotObject skill_row,
        Godot.Collections.Dictionary modifiers,
        string dot_type_id)
    {
        string type_id = string.IsNullOrEmpty(dot_type_id) ? skill_row.Get("damage_type").AsString() : dot_type_id;
        var damage_type_row = _db.get_row("damage_type", type_id) as DamageTypeRow;
        if (damage_type_row == null)
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[DamageResolver] damage_type 不存在：{type_id}");
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "damage", 0 },
                { "detail", new Godot.Collections.Dictionary() },
                { "error", "这一招的伤害类型没配（数据错，已记进日志）" },
            };
        }
        string skill_id = skill_row.Get("skill_id").AsString();
        var skill_base = _db.get_row("skill_base", skill_id) as SkillBaseRow;
        int star = skill_base != null ? skill_base.star : 1;
        int mastery = attacker.HasMethod("mastery_of") ? attacker.Call("mastery_of", skill_id).AsInt32() : 0;
        string element = _override_element(modifiers, skill_row);
        // 1~4 步：攻击力 × 倍率 × 熟练度系数 × 攻击属性系数 × 系别相克
        double step = attack_value(attacker, element)
            * skill_row.Get("power_ratio").AsDouble()
            * _growth.mastery_multiplier(star, mastery);
        step *= 1.0 + _mod_double(modifiers, "attack_stat_bonus");
        double counter = element_multiplier(element, _dict_str(defender.Get("tags"), "element", DEFAULT_ELEMENT));
        step *= counter;
        // 5 步：穿透压低有效防御，再按 damage_type 决定吃不吃防御
        double pen_rate = System.Math.Clamp(
            _stat(attacker, "pen_rate", 0.0), 0.0, _stat_cap("pen_rate", PEN_RATE_CAP_DEFAULT));
        double raw_defense = defense_value(defender, damage_type_row);
        double reduction = defense_reduction(raw_defense * (1.0 - pen_rate), attacker.Get("level").AsInt32());
        step *= 1.0 - reduction;
        // 异常强度：智换算出来的 debuff_power
        step *= 1.0 + _stat(attacker, "debuff_power", 0.0);
        // 内伤抗性（stat_def.res_internal，01 文档写「抵抗内伤类效果」）：
        // 只作用在内伤类持续伤害上；04 没写它落在第几步，这里按「伤害减免乘区」处理
        if (type_id == "dot_internal")
        {
            step *= 1.0 - System.Math.Clamp(
                _stat(defender, "res_internal", 0.0), 0.0, _stat_cap("res_internal", RES_INTERNAL_CAP_DEFAULT));
        }
        // 7 步：增伤 / 减伤（DoT 只免暴击与格挡，减伤照吃）
        step *= 1.0 + _mod_double(modifiers, "dmg_up");
        double total_reduction = System.Math.Clamp(
            _stat(defender, "dmg_reduction", 0.0) + _mod_double(modifiers, "dmg_down"),
            0.0,
            _stat_cap("dmg_reduction", DMG_REDUCTION_CAP_DEFAULT));
        step *= 1.0 - total_reduction;
        // 8 步：随机浮动（在施加那一刻掷一次，之后锁死）
        if (!_mod_bool(modifiers, "no_variance"))
        {
            step *= _rng.Call("randf_range", variance_min(), variance_max()).AsDouble();
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "damage", System.Math.Max(1, (int)System.Math.Floor(step)) },
            { "element", element },
            { "counter", counter },
            { "reduction", reduction },
            {
                "detail", new Godot.Collections.Dictionary
                {
                    { "raw_defense", raw_defense },
                    { "effective_defense", raw_defense * (1.0 - pen_rate) },
                }
            },
        };
    }

    private double _crit_damage(GodotObject actor)
    {
        double value = _stat(actor, "crit_dmg", -1.0);
        return value < 0.0 ? DEFAULT_CRIT_DMG : value;
    }

    private double _stat(GodotObject actor, string stat_id, double fallback)
    {
        if (actor.HasMethod("stat"))
        {
            return actor.Call("stat", stat_id, fallback).AsDouble();
        }
        Variant raw_stats = actor.Get("stats");
        if (raw_stats.VariantType == Variant.Type.Dictionary)
        {
            var stats = raw_stats.AsGodotDictionary();
            if (stats.ContainsKey(stat_id))
            {
                return stats[stat_id].AsDouble();
            }
        }
        return fallback;
    }

    private Godot.Collections.Dictionary _fail(string message)
    {
        GD.PushError("[DamageResolver] " + message);
        return new Godot.Collections.Dictionary
        {
            { "ok", false },
            { "is_hit", false },
            { "is_crit", false },
            { "damage", 0 },
            { "raw_damage", 0 },
            { "error", message },
        };
    }

    /// <summary>`element_override` 优先，取不到或空串就回退招式的系别。</summary>
    private static string _override_element(Godot.Collections.Dictionary modifiers, GodotObject skill_row)
    {
        string element = modifiers != null && modifiers.ContainsKey("element_override")
            ? modifiers["element_override"].AsString()
            : skill_row.Get("element").AsString();
        return string.IsNullOrEmpty(element) ? DEFAULT_ELEMENT : element;
    }

    private static bool _mod_bool(Godot.Collections.Dictionary modifiers, string key)
    {
        return modifiers != null && modifiers.ContainsKey(key) && modifiers[key].AsBool();
    }

    private static double _mod_double(Godot.Collections.Dictionary modifiers, string key)
    {
        return modifiers != null && modifiers.ContainsKey(key) ? modifiers[key].AsDouble() : 0.0;
    }

    private static string _dict_str(Variant dict_value, string key, string fallback)
    {
        if (dict_value.VariantType != Variant.Type.Dictionary)
        {
            return fallback;
        }
        var dict = dict_value.AsGodotDictionary();
        return dict != null && dict.ContainsKey(key) ? dict[key].AsString() : fallback;
    }
}
