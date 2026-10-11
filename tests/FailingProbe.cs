using Godot;

namespace Lunhui.Tests;

/// <summary>
/// **故意失败**的探针：4 条断言，4 条都该失败。
///
/// 为什么要有它：`tests/HarnessSelfTest.cs` 要证明"失败路径真的被记下来、且计入断言数"——
/// 只在成功的分支上打转的断言库，可能整个 `_failures.Add` 都不通（对照 GDScript 版
/// `test_handshake` 那套"反向验证"的习惯）。
///
/// **不许进运行器的 `Suites` 清单**：进了就会天天把自检判红。它只被
/// `HarnessSelfTest` 手动 `new` 出来跑一次，跑完断言它的 `Checks`／`Failures.Count` 都是 4。
/// 文件名故意不叫 `Test*.cs`：将来 C# 运行器按 `Test*.cs` glob 登记用例时，它不会被扫进去。
/// </summary>
public partial class FailingProbe : TestCase
{
    public override string SuiteName() => "故意失败的探针";

    public override void Run()
    {
        CheckTrue(false, "探针：CheckTrue 失败分支应被记账");
        CheckEq(1, 2, "探针：CheckEq 失败分支应被记账");
        CheckNotNull(null, "探针：CheckNotNull 失败分支应被记账");
        CheckInRange(5.0, 1.0, 2.0, "探针：CheckInRange 失败分支应被记账");
    }
}
