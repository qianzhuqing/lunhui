using Godot;

namespace Lunhui.Core;

/// <summary>
/// 成长曲线求值（C# 版，对位 `src/core/curve_evaluator.gd`）。
///
/// 公式写在代码里、系数写在表里（docs/design/06_配置表说明.md 的边界约定）：
///   linear       eff = rate * attr
///   diminishing  eff = cap * attr / (attr + param)   趋近 cap 但永不到达
///   piecewise    按传入的分段点线性插值
/// </summary>
public partial class CurveEvaluator : RefCounted
{
    public const string CURVE_LINEAR = "linear";
    public const string CURVE_DIMINISHING = "diminishing";
    public const string CURVE_PIECEWISE = "piecewise";

    /// <summary>求值。cap &lt;= 0 视为无上限（表里留空即无上限）。</summary>
    public static double evaluate(
        string curve,
        double rate,
        double attrValue,
        double cap = 0.0,
        double param = 0.0,
        Vector2[] points = null)
    {
        var x = System.Math.Max(0.0, attrValue);
        double result;
        switch (curve)
        {
            case CURVE_DIMINISHING:
                if (cap <= 0.0 || param <= 0.0)
                {
                    GD.PushError(
                        $"[CurveEvaluator] diminishing 需要 cap > 0 且 param > 0，退回线性：cap={cap} param={param}");
                    result = rate * x;
                }
                else
                {
                    result = cap * x / (x + param);
                }

                break;
            case CURVE_PIECEWISE:
                if (points == null || points.Length < 2)
                {
                    GD.PushError("[CurveEvaluator] piecewise 需要至少两个分段点，退回线性");
                    result = rate * x;
                }
                else
                {
                    result = _interpolate(points, x);
                }

                break;
            default:
                result = rate * x;
                break;
        }

        if (cap > 0.0 && result > cap)
        {
            result = cap;
        }

        return result;
    }

    /// <summary>分段线性插值：按 x 排好序，超出两端取端点值。</summary>
    private static double _interpolate(Vector2[] points, double x)
    {
        var sorted = (Vector2[])points.Clone();
        System.Array.Sort(sorted, (a, b) => a.X.CompareTo(b.X));
        if (x <= sorted[0].X)
        {
            return sorted[0].Y;
        }

        var last = sorted[sorted.Length - 1];
        if (x >= last.X)
        {
            return last.Y;
        }

        for (var i = 0; i < sorted.Length - 1; i++)
        {
            var a = sorted[i];
            var b = sorted[i + 1];
            if (x <= b.X)
            {
                var span = b.X - a.X;
                if (span <= 0.0)
                {
                    return b.Y;
                }

                var t = (x - a.X) / span;
                return a.Y + (b.Y - a.Y) * t;
            }
        }

        return last.Y;
    }
}
