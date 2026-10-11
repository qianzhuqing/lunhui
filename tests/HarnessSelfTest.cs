using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 验收骨架自检（C# 侧的第一条用例）：把 `tests/TestCase.cs` 与 `tools/RunTestsCs.cs`
/// 自己先验一遍，否则后面每搬一个模块都在一个没验过的网上跑。
///
/// 14 处 `Check*` 调用点全部无条件执行，所以"静态断言点 == 实际执行数"——
/// 它同时是运行器那条假绿门限（静态 &gt; 实际就红）的第一个观察对象。
///
/// **探针不进运行器清单**：`FailingProbe` 由本用例手动跑，验"失败真的被记、且计入断言数"。
/// </summary>
public partial class HarnessSelfTest : TestCase
{
    /// <summary>
    /// 「没给种子的随机源」的模式表（对位 `tests/test_handshake.gd::UNSAFE_RNG_PATTERNS`）。
    /// 只列**无参构造**：`new RngService(seed)`／`RngService.create(seed)` 是给了种子的，不算。
    /// </summary>
    private static readonly string[] UnsafeRngPatterns =
    {
        "RngService.create_random(",
        "new RngService()",
        "new RandomNumberGenerator()",
    };

    public override string SuiteName() => "验收骨架自检";

    public override void Run()
    {
        // 1) 通过路径：10 种断言各走一次成功分支。
        CheckTrue(true, "CheckTrue 通过分支");
        CheckFalse(false, "CheckFalse 通过分支");
        CheckEq(2, 2, "CheckEq 通过分支");
        CheckNe(1, 2, "CheckNe 通过分支");
        CheckFloat(1.0, 1.0, "CheckFloat 通过分支");
        CheckInRange(5.0, 1.0, 10.0, "CheckInRange 通过分支");
        CheckGt(5.0, 1.0, "CheckGt 通过分支");
        CheckLt(1.0, 5.0, "CheckLt 通过分支");
        CheckNotNull(this, "CheckNotNull 通过分支");
        CheckNull(null, "CheckNull 通过分支");

        // 2) 数值按值比：GDScript 的 check_eq 里 1 == 1.0 判等，C# 装箱之后不能变成不等。
        CheckEq(1, 1.0, "整数 1 与浮点 1.0 应判等（GDScript 口径）");

        // 3) 失败路径：拿故意失败的探针验"记失败"与"计数"两件事都真的发生。
        var probe = new FailingProbe();
        probe.Run();
        CheckEq(probe.Checks, 4, "探针的 4 条断言都应计数");
        CheckEq(probe.Failures.Count, 4, "探针的 4 条失败都应被记下");

        // 4) 搬过来的 C# 用例也得用固定种子。
        //    当初是因为 GDScript 那条门限只扫 `.gd`，用例一搬 C# 就落在它视野外；后来那条门限
        //    也搬成了 C#（`TestHandshakeCode.CheckTestsUseSeededRng`，两种语言一起扫），这一半留在
        //    骨架里当"就算那条网退场也还有人管"的兜底（2026-10-11 P4 首批搬 `test_cooldown.gd` 时补）。
        _check_tests_use_seeded_rng();
    }

    /// <summary>
    /// 扫 `res://tests/*.cs` 里有没有没给种子的随机源——发现就记一条失败（不计数）。
    /// **先抹掉字符串字面量再搜**：否则上面那张模式表会把门限自己判红（GDScript 那版第一次跑就是这么红的）。
    /// </summary>
    private void _check_tests_use_seeded_rng()
    {
        var dir = DirAccess.Open("res://tests");
        if (dir == null)
        {
            Fail("打不开 tests/ 目录，无法检查随机源");
            return;
        }

        var scanned = 0;
        foreach (var file_name in dir.GetFiles())
        {
            if (!file_name.EndsWith(".cs"))
            {
                continue;
            }

            var source = FileAccess.GetFileAsString($"res://tests/{file_name}");
            scanned += 1;
            foreach (var raw in source.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("*"))
                {
                    continue;
                }

                var code = _strip_string_literals(raw);
                foreach (var pattern in UnsafeRngPatterns)
                {
                    if (code.Contains(pattern))
                    {
                        Fail(
                            $"{file_name} 里有没给种子的随机源 `{pattern}`：断言会时红时绿。"
                                + "改成 `RngService.create(seed)`");
                    }
                }
            }
        }

        CheckGt(scanned, 20.0, $"扫到足够多的 C# 用例文件（{scanned} 个）");
    }

    /// <summary>去掉一行里的字符串字面量（含 `\"` 转义），只留代码部分。</summary>
    private static string _strip_string_literals(string line)
    {
        var builder = new System.Text.StringBuilder();
        var in_string = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (!in_string)
            {
                if (ch == '"')
                {
                    in_string = true;
                }
                else if (ch == '\'' && index + 2 < line.Length && line[index + 2] == '\'')
                {
                    index += 2; // 字符字面量整段跳过
                }
                else
                {
                    builder.Append(ch);
                }

                continue;
            }

            if (ch == '\\')
            {
                index += 1;
            }
            else if (ch == '"')
            {
                in_string = false;
            }
        }

        return builder.ToString();
    }
}
