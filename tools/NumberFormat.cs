using System;
using System.Globalization;

namespace Lunhui.Tools;

/// <summary>
/// Godot `%` 定点格式化的 C# 对位实现——两个分析工具打印的表格要跟旧链**逐字一样**，
/// 而 `docs/dev/平衡观测.md`／`性能观测.md` 里存档的正是旧链那些数字。
///
/// **为什么要自己写**（2026-10-11 与旧链对拍时三种口径各撞上一批）：
///   · `value.ToString("F1")` 是「二进制真值 ＋ 平局取偶」：`10.25` → `10.2`，Godot 给 `10.3`；
///   · `Math.Round(value, 1, MidpointRounding.AwayFromZero)` 是「十进制最短表示 ＋ 平局远离零」：
///     `2.65` → `2.7`，Godot 给 `2.6`（`2.65` 的二进制真值比 `2.65` 小）；
///   · 「先 `×10` 缩放再 `floor(x+0.5)`」会**二次舍入**：`5.55 * 10` 在 double 里正好等于 `55.5`，
///     于是平局规则被误触发成 `5.6`，Godot 给 `5.5`。
/// Godot 的口径只有一条：**对二进制真值做正确的十进制舍入，平局远离零**
/// （`tools/__fmt_probe.gd` 实测九组边界值：10.25／1.15／0.35／2.65／5.55／30.5／0.05／1.05／2.05）。
/// 所以这里先把 double 落成 17 位有效数字（double 是可往返的，17 位足以唯一区分相邻浮点，
/// 也就能判对「在平局点的哪一侧」），再交给 `decimal` 做精确舍入。
/// </summary>
public static class NumberFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>按 Godot `%` 的口径把数写成定点字符串。</summary>
    public static string Fmt(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(Inv);
        }

        string padded = "F" + decimals.ToString(Inv);
        try
        {
            // G17：对这个 double 唯一的那串 17 位有效数字，正好落在平局点的正确一侧。
            decimal exact = decimal.Parse(value.ToString("G17", Inv), NumberStyles.Float, Inv);
            decimal rounded = Math.Round(exact, decimals, MidpointRounding.AwayFromZero);
            return rounded.ToString(padded, Inv);
        }
        catch (OverflowException)
        {
            // 超出 decimal 表示范围（工具里不该出现）时退回 .NET 自带的定点输出。
            return value.ToString(padded, Inv);
        }
    }
}