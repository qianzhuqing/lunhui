using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 属性四层计算（C# 版，对位 `src/core/attribute_calculator.gd`）。
///
/// 文档定死的结构（docs/design/01_角色系统.md），顺序不可改：
///   最终属性 = ( 基础值 + 等级成长 + 加点 + 装备固定值 + 心法固定值 ) × ( 1 + 百分比加成总和 )
///
/// 本实现的分层落点：
///   第 1 层「基础值」      角色初始五维（character_base.initial_*）
///   第 2 层「加点」        玩家分配的属性点 + 装备/心法的属性点加成（都先过 attr_to_stat 换算成派生数值）
///   第 3 层「等级成长」    level_growth 的基础派生数值
///   第 4 层「固定值」      bonus_ 类派生数值加成（装备、心法、临时增益）
///   第 5 步「百分比」      最后统一相乘
/// 最后按 stat_def 的 min/max 夹取，并按 value_kind 决定是否取整。
///
/// **跨语言接口的三条实测结论**（2026-10-10，都是探针跑出来的，别再猜）：
///   1. GDScript 的 `preload("…AttributeCalculator.cs").new(db)` **能**把构造参数传进 C#；
///   2. GDScript 能调 C# 的 `static` 方法（`contrib_attr_point()` 就是这么用的）；
///   3. GDScript **读不到** C# 的 `const`（`Invalid access to property or key 'CONTRIB_ATTR_POINT'`），
///      所以常量只在 C# 侧直接用，过渡期的 GDScript 调用方走下面那两个静态取值口。
///   **另一条**：C# 的默认参数值对 GDScript **不生效**，少传实参会报
///   `Invalid call. Nonexistent function`——所以 GDScript 调用 `compute()`／`attr_totals_of()`
///   必须把参数补全（这也是那两处调用点被改成显式传全部实参的原因）。
/// </summary>
public partial class AttributeCalculator : RefCounted
{
    /// <summary>贡献类型。</summary>
    public const string CONTRIB_ATTR_POINT = "attr_point";
    public const string CONTRIB_STAT_FLAT = "stat_flat";
    public const string CONTRIB_STAT_PERCENT = "stat_percent";

    /// <summary>level_growth 的基础列 → stat_def.stat_id（顺序＝写入顺序，别打乱）。</summary>
    private static readonly string[][] LEVEL_BASE_STATS =
    {
        new[] { "base_hp", "hp_max" },
        new[] { "base_qi", "qi_max" },
        new[] { "base_atk_phys", "atk_phys" },
        new[] { "base_atk_qi", "atk_qi" },
        new[] { "base_def_phys", "def_phys" },
        new[] { "base_def_qi", "def_qi" },
        new[] { "base_speed", "speed" },
    };

    /// <summary>
    /// 运行期表库（2026-10-10 起是 C# 的 `TableDb`，类型已收紧；此前是 GDScript 版，只能按
    /// `GodotObject` 收、走 `Call` 取数）。
    /// </summary>
    private readonly TableDb _db;

    public AttributeCalculator(TableDb db)
    {
        _db = db;
    }

    /// <summary>
    /// **GDScript 侧唯一的构造入口**：`preload(…Cs).new(db)` 过不了 GDScript 的**解析期**
    /// 实参检查——它看不见 C# 的构造签名，一律按 0 参处理，于是报
    /// `Parse Error: Too many arguments for "new()" call. Expected at most 0 but received 1.`
    /// （2026-10-10 实测；`load()` 出来的值是动态的所以那时看着像能用，别被误导）。
    /// 静态方法不受这条限制，所以 GDScript 统一走 `create(db)`；C# 侧仍可直接 `new`。
    /// </summary>
    public static AttributeCalculator create(TableDb db) => new(db);

    /// <summary>
    /// 过渡期给 GDScript 调用方的取值口：GDScript 读不到 C# 的 `const`（见类注释第 3 条）。
    /// 等 P5 把 GDScript 清零之后，这几个方法**连着调用方一起删掉**，只剩上面的 const。
    /// </summary>
    public static string contrib_attr_point() => CONTRIB_ATTR_POINT;

    /// <inheritdoc cref="contrib_attr_point"/>
    public static string contrib_stat_flat() => CONTRIB_STAT_FLAT;

    /// <summary>
    /// 计算最终派生数值，返回 {stat_id: float|int}。
    ///
    /// base_attrs   角色初始五维 {attr_id: int}
    /// allocations  玩家加点 {attr_id: int}
    /// contributions 外部贡献列表，元素形如
    ///   {"kind": "attr_point", "target": "str", "value": 2, "source": "eq_sword_02"}
    ///   {"kind": "stat_flat", "target": "atk_phys", "value": 6, "source": "eq_sword_02"}
    ///   {"kind": "stat_percent", "target": "atk_phys", "value": 0.1, "source": "buff_x"}
    ///
    /// include_level_base  **敌人不吃 `level_growth` 的基础列**（设计 10 §七，0.28.0 答 Q57）：
    /// `base_hp`／`base_atk_phys`… 是**英雄基线**（「一个 N 级角色长什么样」），给敌人共用
    /// 等于让每只 2 级野狼都变成「2 级英雄」，而杂兵本来就该低于同级英雄。所以敌人传 `false`：
    /// 强度**全部由七维 ＋ 装备／内功固定值给**，`level` 对敌人只剩三件事——
    /// 经验与掉落档次、招式槽与内功容量的门槛、难度标签（那三件都不在这条管线里）。
    /// </summary>
    public Godot.Collections.Dictionary compute(
        int level,
        Godot.Collections.Dictionary baseAttrs = null,
        Godot.Collections.Dictionary allocations = null,
        Godot.Collections.Array contributions = null,
        bool includeLevelBase = true)
    {
        baseAttrs ??= new Godot.Collections.Dictionary();
        allocations ??= new Godot.Collections.Dictionary();
        contributions ??= new Godot.Collections.Array();

        var attrTotals = attr_totals_of(baseAttrs, allocations, contributions);
        var subtotal = new System.Collections.Generic.Dictionary<string, double>();
        if (includeLevelBase)
        {
            var levelBase = level_base_stats(level);
            foreach (Variant key in levelBase.Keys)
            {
                subtotal[key.AsString()] = levelBase[key].AsDouble();
            }
        }

        // 属性 → 派生数值
        foreach (Variant rowVariant in rows_of("attr_to_stat"))
        {
            var row = rowVariant.AsGodotObject() as AttrToStatRow;
            if (row == null)
            {
                GD.PushError("[AttributeCalculator] attr_to_stat 的行不是 C# 行类（生成的 .tres 过期了？先重建配置表）");
                continue;
            }

            var value = CurveEvaluator.evaluate(
                row.curve,
                row.rate,
                number_of(attrTotals, row.attr_id),
                row.cap,
                row.param);
            subtotal[row.stat_id] = value_of(subtotal, row.stat_id) + value;
        }

        // 派生数值固定值层
        foreach (Variant contributionVariant in contributions)
        {
            var contribution = contributionVariant.AsGodotDictionary();
            if (string_of(contribution, "kind") != CONTRIB_STAT_FLAT)
            {
                continue;
            }

            var statId = _contribution_target(contribution);
            subtotal[statId] = value_of(subtotal, statId) + number_of(contribution, "value");
        }

        // 百分比层
        var percentTotals = percent_totals_of(contributions);

        var outStats = new Godot.Collections.Dictionary();
        foreach (var pair in subtotal)
        {
            var value = pair.Value * (1.0 + number_of(percentTotals, pair.Key));
            outStats[pair.Key] = apply_stat_def(pair.Key, value);
        }

        return outStats;
    }

    /// <summary>
    /// 只算五维合计（基础 + 加点 + 属性点类贡献）。
    /// GDScript 调用方**必须传满三个实参**（C# 的默认参数值对 GDScript 不生效）。
    /// </summary>
    public Godot.Collections.Dictionary attr_totals_of(
        Godot.Collections.Dictionary baseAttrs,
        Godot.Collections.Dictionary allocations = null,
        Godot.Collections.Array contributions = null)
    {
        allocations ??= new Godot.Collections.Dictionary();
        contributions ??= new Godot.Collections.Array();

        var totals = new Godot.Collections.Dictionary();
        foreach (Variant attrId in baseAttrs.Keys)
        {
            totals[attrId.AsString()] = baseAttrs[attrId].AsDouble();
        }

        foreach (Variant attrId in allocations.Keys)
        {
            var key = attrId.AsString();
            totals[key] = number_of(totals, key) + allocations[attrId].AsDouble();
        }

        foreach (Variant contributionVariant in contributions)
        {
            var contribution = contributionVariant.AsGodotDictionary();
            if (string_of(contribution, "kind") != CONTRIB_ATTR_POINT)
            {
                continue;
            }

            var attrId = _contribution_target(contribution);
            totals[attrId] = number_of(totals, attrId) + number_of(contribution, "value");
        }

        return totals;
    }

    /// <summary>等级成长带的固定值层。</summary>
    public Godot.Collections.Dictionary level_base_stats(int level)
    {
        var row = row_of<LevelGrowthRow>("level_growth", level);
        if (row == null)
        {
            GD.PushError($"[AttributeCalculator] level_growth 缺少等级 {level}");
            return new Godot.Collections.Dictionary();
        }

        var outStats = new Godot.Collections.Dictionary();
        foreach (var pair in LEVEL_BASE_STATS)
        {
            outStats[pair[1]] = row.Get(pair[0]);
        }

        return outStats;
    }

    /// <summary>百分比层的合计（按目标分组）。</summary>
    public Godot.Collections.Dictionary percent_totals_of(Godot.Collections.Array contributions)
    {
        var totals = new Godot.Collections.Dictionary();
        foreach (Variant contributionVariant in contributions)
        {
            var contribution = contributionVariant.AsGodotDictionary();
            if (string_of(contribution, "kind") != CONTRIB_STAT_PERCENT)
            {
                continue;
            }

            var statId = _contribution_target(contribution);
            totals[statId] = number_of(totals, statId) + number_of(contribution, "value");
        }

        return totals;
    }

    /// <summary>按 stat_def 夹取上下限并按 value_kind 取整。</summary>
    public Variant apply_stat_def(string statId, double value)
    {
        var statRow = row_of<StatDefRow>("stat_def", statId);
        if (statRow == null)
        {
            GD.PushError($"[AttributeCalculator] stat_def 缺少派生数值 {statId}");
            return value;
        }

        var result = System.Math.Max(value, statRow.min_value);
        if (statRow.has_max())
        {
            result = System.Math.Min(result, statRow.max_value);
        }

        // 与 GDScript 的 `int(floor(x))` 同口径：都是向下截断，别顺手写成四舍五入。
        return statRow.value_kind == "int" ? (int)System.Math.Floor(result) : result;
    }

    /// <summary>贡献里的目标字段：优先 `target`，兼容 `attr_id`／`stat_id` 两种老写法。</summary>
    private static string _contribution_target(Godot.Collections.Dictionary contribution)
    {
        if (contribution.ContainsKey("target"))
        {
            return contribution["target"].AsString();
        }

        if (contribution.ContainsKey("attr_id"))
        {
            return contribution["attr_id"].AsString();
        }

        if (contribution.ContainsKey("stat_id"))
        {
            return contribution["stat_id"].AsString();
        }

        return "";
    }

    private Godot.Collections.Array rows_of(string table) => _db.rows(table);

    private T row_of<T>(string table, Variant id) where T : GodotObject =>
        _db.get_row(table, id) as T;

    private static double number_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    private static string string_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static double value_of(System.Collections.Generic.Dictionary<string, double> dict, string key) =>
        dict.TryGetValue(key, out var value) ? value : 0.0;
}
