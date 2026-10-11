using Godot;

namespace Lunhui.Tests;

/// <summary>
/// 测试用的「写不进去」的存档设施：只实现 `SaveService` 会用到的那几个方法。
/// 用途：验「自动存档失败**不静默**」——玩家得在战报里看到一句，而不是以为进度存住了
/// （本工程的沙箱真出过 `user://` 写失败的事故）。
///
/// 旧链 `test_battle_ui.gd` 把它写成文件内的内部类；C# 侧 GodotObject 子类要是顶层类才能被
/// `Call("save_slot", …)` 叫到（`SaveService` 对非 `SaveStore` 的注入走的就是这条路），所以单开一份。
/// </summary>
public partial class FailingSaveStore : RefCounted
{
    public void ensure_dir()
    {
    }

    public Godot.Collections.Dictionary save_slot(int slot, Variant state) =>
        new() { { "ok", false }, { "error", "测试注入：盘写不进去" } };

    public string slot_path(int slot) => "";
}
