using Godot;

namespace Lunhui.Core;

/// <summary>
/// 退出前的一次强制回收（C# 迁移带来的一处**必须**做的收尾）。
///
/// **它解决的问题**（2026-10-10 实测，别删）：C# 侧 `new` 出来的 `RefCounted`／`Resource`
/// （`TableDb`、`AttributeCalculator`、`TableResource.duplicate(true)` 的副本…）在托管堆上挂着
/// 一个 "unsafe reference" 登记。进程退出时 Godot 先做完 `CSharpLanguage::finalize()`（把还挂着的
/// 登记逐条打成 `ERROR: Leaked unsafe reference to object`）**再**卸载 native 侧，
/// 剩下的托管终结器这时才跑，`GodotObject.Finalize()` 调 native 直接抛
/// `Fatal error. System.Runtime.InteropServices.SEHException`。
///
/// 现场：`tools\check_loop.bat`（跨场景闭环，几十次切场景）
/// 打 32 条 leaked + 那条 Fatal，**退出码非零**——可 `LOOPCHECK: OK` 已经打出来了，
/// 于是 `run_all_checks` 报 `[FAIL] cross-scene loop`，是"验收假红"里最难查的一种
/// （场景自检全 OK、单跑也对，只有攒够多对象的那条链会撞上）。
///
/// **为什么放在这里**：`GameData` 是 autoload，它的 `_exit_tree()` 在 native 侧还活着的时候跑，
/// 是"进程退出前最后一段能执行托管代码的地方"。跑一次 `Collect + WaitForPendingFinalizers`
/// 就能把上面的登记全部清干净（实测 32 → 0，退出码 0）。游戏正常退出、`--script` 工具链、
/// 场景自检三条路都经过这里，所以只在这一处调。
///
/// **P5 可以回头考虑把根因也消掉**：真正的源头是"每次调用都 `new` 一个 C# `RefCounted`"
/// （`AttributeCalculator.create(db)` 最典型）。把它做成静态方法（`compute(db, …)`）之后
/// 每次调用不再产生托管对象，这条泄漏自然少一大截——那时这里可以留作兜底。
/// </summary>
public partial class GcShutdown : RefCounted
{
    /// <summary>退出前跑一次完整回收（含终结器）。重复调用无副作用。</summary>
    public static void run()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }
}
