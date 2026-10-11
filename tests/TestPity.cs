using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 掉落保底计数器（C# 版，对位 `tests/test_drop.gd` 的保底那几节）。
///
/// GDScript 那条用例还留着跑整合——保底穿过 `DropResolver`（还在 GDScript 里）真的影响掷点、
/// 以及存档往返后 `GameState.pity_tracker()` 读得回来。
/// </summary>
public partial class TestPity : TestCase
{
    public override string SuiteName() => "掉落保底（C# 侧）";

    public override void Run()
    {
        CheckEq(PityTracker.version(), 1, "保底字典的版本标记 = 1");

        var tracker = PityTracker.create();
        CheckEq(tracker.size(), 0, "新建的计数器是空的");
        CheckEq(tracker.attempts("g|slot"), 0, "没记过的 key 是 0");
        CheckEq(tracker.register_attempt("g|slot"), 1, "第一次记 1");
        CheckEq(tracker.register_attempt("g|slot"), 2, "第二次记 2");
        CheckEq(tracker.attempts("g|slot"), 2, "读得到累计次数");
        CheckEq(tracker.size(), 1, "同一个 key 只占一条");
        CheckEq(tracker.register_attempt("other|slot"), 1, "不同 key 分开计数");
        CheckEq(tracker.size(), 2, "两条 key");

        CheckFalse(tracker.should_force("g|slot", 3), "没到门槛不强制");
        CheckTrue(tracker.should_force("g|slot", 2), "达到门槛就强制");
        CheckFalse(tracker.should_force("g|slot", 0), "门槛 ≤0 = 不设保底");
        CheckFalse(tracker.should_force("never|slot", 1), "没记过的 key 不强制");

        // 存档往返：跨难度继承靠 to_dict / from_dict，切难度时不清空
        Godot.Collections.Dictionary saved = tracker.to_dict();
        CheckEq(saved["version"].AsInt32(), 1, "存档里带版本标记");
        var restored = PityTracker.create();
        restored.from_dict(saved);
        CheckEq(restored.attempts("g|slot"), 2, "往返后计数一致");
        CheckEq(restored.size(), 2, "往返带上全部 key");
        CheckEq(restored.register_attempt("g|slot"), 3, "往返后能接着往上记");

        restored.reset("g|slot");
        CheckEq(restored.attempts("g|slot"), 0, "出货后清零");
        CheckEq(restored.size(), 1, "清零只动那一条");

        restored.from_dict(new Godot.Collections.Dictionary());
        CheckEq(restored.size(), 0, "读一份没有 counters 的字典 = 清空（老档容错）");
        CheckEq(restored.attempts("g|slot"), 0, "读空之后是 0");

        tracker.clear();
        CheckEq(tracker.size(), 0, "clear 清空全部");
    }
}
