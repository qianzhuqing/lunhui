using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 成长系统的计算：槽位上限、熟练度、图鉴奖励（C# 版，对位原 `src/core/growth_calculator.gd`）。
///
/// 与伤害管线同一套处理方式：**算法在代码、常数在表**（`growth_const.csv`），
/// 星级相关的系数在 `skill_star_def.csv`。
///
/// 口径说明：槽位上限里的悟性／根骨取**含装备的属性合计**（设计只说「悟性／根骨」，
/// 没有像事件判定那样注明「裸属性」）；若设计要改成裸值，只改 `active_slots`／
/// `passive_capacity` 的入参即可。
///
/// **跨语言接口（照 `AttributeCalculator` 那三条实测结论，别再猜）**：
///   1. GDScript 走 `preload("…GrowthCalculator.cs").create(db)`——`new(db)` 过不了
///      GDScript 的解析期实参检查（它看不见 C# 的构造签名）；
///   2. GDScript **读不到** C# 的 `const`／静态字段，所以 `DEFAULTS` 只在 C# 侧直接用，
///      构建期校验器直接调 `Defaults()`；
///   3. C# 的默认参数值对 GDScript **不生效**，所以这里**一个可选参数都不留**——
///      GDScript 调用方一律写明全部实参（`codex_bonus(n, 1.0)`／`passive_contributions(ids, {})`）。
/// </summary>
public partial class GrowthCalculator : RefCounted
{
    /// <summary>
    /// `growth_const` 缺行时的兜底值（正常运行时这些值都在表里）。
    ///
    /// **用完即建，不存静态字段**：一对位 `TableValidator` 那几口词表——Godot 集合挂在
    /// C# 静态字段上会活到进程收尾，多一份无谓的生命周期。
    /// </summary>
    public static Godot.Collections.Dictionary Defaults() => new()
    {
        { "active_slot_base", 2.0 },
        { "active_slot_lv_div", 5.0 },
        { "active_slot_attr_div", 8.0 },
        { "active_slot_cap", 9.0 },
        { "passive_cap_base", 2.0 },
        { "passive_cap_lv_div", 6.0 },
        { "passive_cap_attr_div", 10.0 },
        { "passive_cap_max", 7.0 },
        { "mastery_max", 10.0 },
        { "mastery_combat_gain", 1.0 },
        { "cultivate_cost_growth", 1.6 },
        { "codex_step", 5.0 },
        { "codex_bonus", 1.0 },
        { "codex_cap", 10.0 },
    };

    /// <summary>图鉴奖励覆盖七项属性。</summary>
    private static readonly string[] ALL_ATTRS = { "str", "con", "agi", "int", "luk", "wu", "gen" };

    private readonly TableDb _db;
    private readonly System.Collections.Generic.Dictionary<string, double> _consts = new();

    public GrowthCalculator(TableDb db)
    {
        _db = db;
        _consts.Clear();
        foreach (Variant entry in _db.rows("growth_const"))
        {
            var row = entry.AsGodotObject() as GrowthConstRow;
            if (row == null)
            {
                GD.PushError("[GrowthCalculator] growth_const 的行不是 C# 行类（生成的 .tres 过期了？先重建配置表）");
                continue;
            }

            _consts[row.const_id] = row.value;
        }
    }

    /// <summary>GDScript 侧唯一的构造入口（见类注释第 1 条）。</summary>
    public static GrowthCalculator create(TableDb db) => new(db);

    /// <summary>取成长常数；表里缺行就报错并退回默认值。</summary>
    public double constant(string const_id)
    {
        if (_consts.TryGetValue(const_id, out double found))
        {
            return found;
        }

        GD.PushError($"[GrowthCalculator] growth_const 缺少 {const_id}");
        Godot.Collections.Dictionary defaults = Defaults();
        return defaults.ContainsKey(const_id) ? defaults[const_id].AsDouble() : 0.0;
    }

    /// <summary>
    /// 取整版常数。**必须 `AwayFromZero`**：GDScript 的 `round()` 是四舍五入远离零，
    /// C# 的 `Math.Round` 默认是银行家舍入（0.5 往偶数靠），差一个数就会让打坐费用错一档。
    /// </summary>
    public int constant_int(string const_id) =>
        (int)System.Math.Round(constant(const_id), System.MidpointRounding.AwayFromZero);

    /// <summary>招式槽上限 = base + floor(等级 / lv_div) + floor(悟性 / attr_div)，硬上限 cap。</summary>
    public int active_slots(int level, Godot.Collections.Dictionary attrs)
    {
        double base_value = constant("active_slot_base");
        int by_level = Mathf.FloorToInt(
            System.Math.Max(level, 1) / System.Math.Max(constant("active_slot_lv_div"), 1.0));
        int by_attr = Mathf.FloorToInt(
            number_of(attrs, "wu") / System.Math.Max(constant("active_slot_attr_div"), 1.0));
        // 对位 GDScript 的 `int(...)`：正数上是截断，写成 (int) 而不是 FloorToInt 也同值。
        return Mathf.Clamp((int)(base_value + by_level + by_attr), 0, constant_int("active_slot_cap"));
    }

    /// <summary>内功容量 = base + floor(等级 / lv_div) + floor(根骨 / attr_div)，硬上限 cap（容量点）。</summary>
    public int passive_capacity(int level, Godot.Collections.Dictionary attrs)
    {
        double base_value = constant("passive_cap_base");
        int by_level = Mathf.FloorToInt(
            System.Math.Max(level, 1) / System.Math.Max(constant("passive_cap_lv_div"), 1.0));
        int by_attr = Mathf.FloorToInt(
            number_of(attrs, "gen") / System.Math.Max(constant("passive_cap_attr_div"), 1.0));
        return Mathf.Clamp((int)(base_value + by_level + by_attr), 0, constant_int("passive_cap_max"));
    }

    /// <summary>两个公式型派生数值（不来自 attr_to_stat，单独算好合并进面板）。</summary>
    public Godot.Collections.Dictionary derived_stats(int level, Godot.Collections.Dictionary attrs) => new()
    {
        { "slot_active", slot_active_stat(level, attrs) },
        { "passive_capacity", passive_capacity(level, attrs) },
    };

    /// <summary>招式槽的数值形态（int 派生数值用同一套显示逻辑）。</summary>
    public int slot_active_stat(int level, Godot.Collections.Dictionary attrs) => active_slots(level, attrs);

    public Resource star_row(int star)
    {
        Resource row = _db.get_row("skill_star_def", star);
        if (row == null)
        {
            GD.PushError($"[GrowthCalculator] skill_star_def 缺少 {star} 星");
        }

        return row;
    }

    public int mastery_max() => constant_int("mastery_max");

    /// <summary>招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)。</summary>
    public double mastery_multiplier(int star, int mastery_level)
    {
        var row = star_row(star) as SkillStarDefRow;
        if (row == null)
        {
            return 1.0;
        }

        int level = Mathf.Clamp(mastery_level, 0, mastery_max());
        return 1.0 + row.mastery_gain * level;
    }

    /// <summary>打坐费用 = cultivate_cost_base × cultivate_cost_growth^(熟练度 - 1)。</summary>
    public int cultivate_cost(int star, int mastery_level)
    {
        var row = star_row(star) as SkillStarDefRow;
        if (row == null)
        {
            return 0;
        }

        int level = Mathf.Clamp(mastery_level, 1, System.Math.Max(1, mastery_max()));
        double cost = row.cultivate_cost_base
            * System.Math.Pow(constant("cultivate_cost_growth"), level - 1);
        return (int)System.Math.Round(cost, System.MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 图鉴奖励：每收集 step 部武学，七项属性各 +bonus，最多 cap 次。
    /// 返回 {times, bonus, attrs: {attr_id: 加成}, step, cap, per_tier}
    ///
    /// `bonus_multiplier`：天赋「藏书癖」（`rule:codex_bonus_multiplier 2`）把每一档的奖励翻倍。
    /// 倍数**只放大每档给多少**，不动 `step`／`cap`（收集的节奏与上限是设计定的）。
    /// </summary>
    public Godot.Collections.Dictionary codex_bonus(int collected, double bonus_multiplier)
    {
        int step = System.Math.Max(1, constant_int("codex_step"));
        int cap = System.Math.Max(0, constant_int("codex_cap"));
        int bonus = constant_int("codex_bonus");
        int times = Mathf.Clamp(
            Mathf.FloorToInt(System.Math.Max(collected, 0) / (double)step), 0, cap);
        int per_tier = (int)System.Math.Round(
            bonus * System.Math.Max(1.0, bonus_multiplier), System.MidpointRounding.AwayFromZero);
        var attrs = new Godot.Collections.Dictionary();
        foreach (string attr_id in ALL_ATTRS)
        {
            attrs[attr_id] = times * per_tier;
        }

        // step／cap 一起返回：界面要拿它显示「已收集 X／下一步还差几部」
        return new Godot.Collections.Dictionary
        {
            { "times", times },
            { "bonus", times * per_tier },
            { "attrs", attrs },
            { "step", step },
            { "cap", cap },
            { "per_tier", per_tier },
        };
    }

    /// <summary>
    /// 图鉴奖励转成属性点层贡献（`AttributeCalculator` 的贡献格式）。
    ///
    /// **换算只写这一处**：面板（`CharacterSheet`）与门槛判定（`SkillLoadout.attrs()` 的兜底）
    /// 都调它，免得两边各算一套、迟早对不上——这一条踩过：门槛走的是裸 SkillLoadout，
    /// 图鉴奖励只加进了面板那份属性，于是「面板显示够门槛了、实际却学不会」。
    /// </summary>
    public Godot.Collections.Array codex_contributions(int collected, double bonus_multiplier)
    {
        var out_contribs = new Godot.Collections.Array();
        var attrs = codex_bonus(collected, bonus_multiplier)["attrs"].AsGodotDictionary();
        foreach (Variant attr_id in attrs.Keys)
        {
            int value = attrs[attr_id].AsInt32();
            if (value != 0)
            {
                out_contribs.Add(new Godot.Collections.Dictionary
                {
                    { "kind", AttributeCalculator.CONTRIB_ATTR_POINT },
                    { "target", attr_id.AsString() },
                    { "value", value },
                    { "source", "codex" },
                });
            }
        }

        return out_contribs;
    }

    /// <summary>
    /// 内功加成转成 AttributeCalculator 的贡献列表（装配系统接入后直接可用）。
    ///
    /// **熟练度放大 `stat:` 类、不放大 `attr:` 类**（设计 0.15.0 口径确认第 3 条）：
    /// 系数就是招式那套 `mastery_multiplier()`（`1 + 熟练度 × skill_star_def.mastery_gain`）。
    /// 为什么不放大属性点：属性点是整数、还会再经 `attr_to_stat` 换算一次——
    /// 放大它等于二次放大，而且面板会出现「+4.5 智」这种玩家看不懂的数。
    ///
    /// `mastery_levels` 是 `{skill_id: 熟练度}`；传空（敌人、构建期校验）＝ 全 0，即原值。
    /// </summary>
    public Godot.Collections.Array passive_contributions(
        string[] passive_ids, Godot.Collections.Dictionary mastery_levels)
    {
        var out_contribs = new Godot.Collections.Array();
        foreach (string skill_id in passive_ids)
        {
            var base_row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
            int star = base_row?.star ?? 0;
            // star 取不到（数据缺行）时不放大：宁可给原值，也不要按 0 星算出个假系数
            // 对位 GDScript 的 `int(mastery_levels.get(skill_id, 0))`：非整数熟练度截断成整数。
            double scale = star > 0
                ? mastery_multiplier(star, (int)number_of(mastery_levels, skill_id))
                : 1.0;
            foreach (Variant entry in _db.rows_where("skill_passive_stat", "skill_id", skill_id))
            {
                var row = entry.AsGodotObject() as SkillPassiveStatRow;
                if (row == null)
                {
                    continue;
                }

                Godot.Collections.Dictionary parsed = row.parsed_target();
                string kind = parsed["kind"].AsString();
                if (kind == "attr")
                {
                    out_contribs.Add(new Godot.Collections.Dictionary
                    {
                        { "kind", AttributeCalculator.CONTRIB_ATTR_POINT },
                        { "target", parsed["target_id"] },
                        { "value", row.value },
                        { "source", skill_id },
                    });
                }
                else if (kind == "stat")
                {
                    out_contribs.Add(new Godot.Collections.Dictionary
                    {
                        { "kind", AttributeCalculator.CONTRIB_STAT_FLAT },
                        { "target", parsed["target_id"] },
                        { "value", row.value * scale },
                        { "source", skill_id },
                    });
                }
            }
        }

        return out_contribs;
    }

    /// <summary>装得下吗（内功占格制）：返回 {used, capacity, fits}。</summary>
    public Godot.Collections.Dictionary passive_fit(
        string[] passive_ids, int level, Godot.Collections.Dictionary attrs)
    {
        int used = 0;
        foreach (string skill_id in passive_ids)
        {
            var row = _db.get_row("skill_passive", skill_id) as SkillPassiveRow;
            if (row != null)
            {
                used += row.slot_cost;
            }
        }

        int capacity = passive_capacity(level, attrs);
        return new Godot.Collections.Dictionary
        {
            { "used", used },
            { "capacity", capacity },
            { "fits", used <= capacity },
        };
    }

    private static double number_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;
}
