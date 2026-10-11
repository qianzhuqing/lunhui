using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 属性四层计算与成长曲线（C# 版，对位 `tests/test_attribute.gd`）。
///
/// 期望值全部按表现算或写死「公式推出来的那个数」——数据一变它就红，这是故意的。
/// </summary>
public partial class TestAttribute : TestCase
{
    private static readonly Godot.Collections.Dictionary BASE_ATTRS = new()
    {
        { "str", 5 },
        { "con", 5 },
        { "agi", 5 },
        { "int", 5 },
        { "luk", 5 },
    };

    public override string SuiteName() => "属性四层计算与曲线";

    public override void Run()
    {
        var db = GetDb();
        var calculator = new AttributeCalculator(db);
        _check_curves();
        _check_four_layers(calculator);
        _check_caps(calculator);
        _check_level_growth(calculator);
    }

    private void _check_curves()
    {
        CheckFloat(CurveEvaluator.evaluate("linear", 2.0, 5.0), 10.0, "线性：2.0/点 × 5 点");
        CheckFloat(CurveEvaluator.evaluate("linear", 2.0, 100.0, 50.0), 50.0, "线性 + 上限：夹到 cap");
        CheckFloat(
            CurveEvaluator.evaluate("diminishing", 0.01, 80.0, 0.95, 80.0), 0.475, "递减：attr = param 时取到一半 cap");
        var low = CurveEvaluator.evaluate("diminishing", 0.01, 10.0, 0.95, 80.0);
        var high = CurveEvaluator.evaluate("diminishing", 0.01, 400.0, 0.95, 80.0);
        CheckGt(high, low, "递减曲线单调递增");
        CheckLt(high, 0.95, "递减曲线永远到不了 cap");
        CheckLt(
            CurveEvaluator.evaluate("diminishing", 0.01, 1000000.0, 0.95, 80.0),
            0.950001,
            "递减曲线趋近 cap 但不越界");
        var points = new[] { new Vector2(0, 0), new Vector2(10, 5), new Vector2(20, 20) };
        CheckFloat(CurveEvaluator.evaluate("piecewise", 0.0, 5.0, 0.0, 0.0, points), 2.5, "分段插值：区间中点");
        CheckFloat(CurveEvaluator.evaluate("piecewise", 0.0, 100.0, 0.0, 0.0, points), 20.0, "分段插值：超出上界取端点");
    }

    /// <summary>四层公式：final = (基础 + 等级成长 + 加点 + 固定值) × (1 + 百分比)</summary>
    private void _check_four_layers(AttributeCalculator calculator)
    {
        const int level = 1;
        var baseStats = calculator.compute(level, BASE_ATTRS);
        // level_growth[1]：hp 100 / qi 40 / atk 10 / def 5 / speed 10
        CheckEq((int)num(baseStats, "hp_max"), 100 + 12 * 5, "第 1+3 层：气血 = 基础 100 + 体 5 × 12");
        CheckEq((int)num(baseStats, "atk_phys"), 10 + 2 * 5, "第 1+3 层：外功 = 基础 10 + 力 5 × 2");
        CheckEq(
            (int)num(baseStats, "atk_qi"),
            (int)System.Math.Floor(10.0 + 1.8 * 5),
            "第 1+3 层：内功 = 基础 10 + 智 5 × 1.8");
        CheckEq(
            (int)num(baseStats, "def_phys"),
            (int)System.Math.Floor(5.0 + 1.2 * 5),
            "第 1+3 层：外防 = 基础 5 + 体 5 × 1.2");
        CheckEq(
            (int)num(baseStats, "speed"),
            (int)System.Math.Floor(10.0 + 1.5 * 5),
            "第 1+3 层：身法向下取整");
        CheckEq((int)num(baseStats, "qi_max"), 40 + 6 * 5, "第 1+3 层：内力 = 基础 40 + 智 5 × 6");

        // 第 2 层加点
        var allocated = calculator.compute(level, BASE_ATTRS, Str(5));
        CheckEq((int)num(allocated, "atk_phys"), 10 + 2 * 10, "第 2 层：加点走属性→派生换算");

        // 第 2 层装备属性点（会连锁影响多个派生数值）
        var equipAttr = new Godot.Collections.Array
        {
            Contribution("attr_point", "str", 4, "夹具"),
        };
        var withEquip = calculator.compute(level, BASE_ATTRS, Str(5), equipAttr);
        CheckEq((int)num(withEquip, "atk_phys"), 10 + 2 * 14, "第 2 层：装备属性点连锁换算");
        CheckEq((int)num(withEquip, "poise_break"), (int)System.Math.Floor(0.8 * 14), "第 2 层：同一属性点同时驱动破架势");

        // 第 4 层固定值 + 百分比层
        var contributions = new Godot.Collections.Array
        {
            Contribution("attr_point", "str", 4),
            Contribution("stat_flat", "atk_phys", 10),
            Contribution("stat_percent", "atk_phys", 0.5),
        };
        var full = calculator.compute(level, BASE_ATTRS, Str(5), contributions);
        // (10 + 2×14 + 10) × 1.5 = 72；若把百分比提前加，会得到 67，说明顺序是对的
        CheckEq((int)num(full, "atk_phys"), 72, "百分比最后相乘，不参与加法层");
    }

    private void _check_caps(AttributeCalculator calculator)
    {
        var huge = new Godot.Collections.Dictionary
        {
            { "str", 0 }, { "con", 200 }, { "agi", 1000000 }, { "int", 0 }, { "luk", 1000000 },
        };
        var stats = calculator.compute(20, huge);
        CheckInRange(num(stats, "hit_rate"), 0.94, 0.95, "命中率被夹在 cap 0.95 以内");
        CheckInRange(num(stats, "dodge_rate"), 0.0, 0.6, "闪避率不超过 cap 0.6");
        CheckInRange(num(stats, "crit_rate"), 0.0, 0.75, "暴击率不超过 cap 0.75");
        CheckInRange(num(stats, "crit_dmg"), 0.0, 3.0, "暴击伤害不超过 cap 3.0");
        CheckInRange(num(stats, "drop_rate"), 0.0, 0.25, "掉落机缘不超过保守方案上限 0.25");
        CheckInRange(num(stats, "res_internal"), 0.0, 0.75, "内伤抗性不超过上限 0.75");

        var zeroAttr = new Godot.Collections.Dictionary
        {
            { "str", 0 }, { "con", 0 }, { "agi", 0 }, { "int", 0 }, { "luk", 0 },
        };
        var bare = calculator.compute(1, zeroAttr);
        CheckEq((int)num(bare, "atk_phys"), 10, "属性为 0 时只剩等级基础值");
        CheckEq(num(bare, "hit_rate"), 0.0, "属性为 0 时递减类派生数值为 0");
        CheckEq(num(bare, "res_internal"), 0.0, "内伤抗性下限为 0");
    }

    private void _check_level_growth(AttributeCalculator calculator)
    {
        var level20 = calculator.compute(20, BASE_ATTRS);
        CheckEq((int)num(level20, "hp_max"), 252 + 12 * 5, "20 级基础气血 252 + 体 5 × 12");
        var row = GetDb().Call("get_row", "level_growth", 20).AsGodotObject() as LevelGrowthRow;
        CheckNotNull(row, "level_growth 有 20 级行");
        if (row != null)
        {
            CheckEq(row.upgrade_points, 0, "满级不再给属性点");
        }
    }

    private static Godot.Collections.Dictionary Str(int value) =>
        new() { { "str", value } };

    private static Godot.Collections.Dictionary Contribution(string kind, string target, double value, string source = "") =>
        new()
        {
            { "kind", kind },
            { "target", target },
            { "value", value },
            { "source", source },
        };

    private static double num(Godot.Collections.Dictionary stats, string key) => stats[key].AsDouble();
}
