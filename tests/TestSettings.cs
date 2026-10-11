using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 设备设置（音量 ＋ 自动战斗开关）的落盘（C# 版，对位 `tests/test_settings.gd` 的前三节）。
///
/// 这条用例钉三件事：① 读写往返；② **两个键互不覆盖**（曾经每种设置各自整份保存，
/// 按一下音量会把自动战斗开关冲掉）；③ 读不到／读坏／写不进去都要有说法，
/// **绝不悄悄静音**、也绝不替玩家开自动。枢纽页那两个按钮那一层由 GDScript 的
/// `test_settings` 走真场景验。
/// </summary>
public partial class TestSettings : TestCase
{
    private const string TestDir = "res://.logs/test_settings_cs";

    public override string SuiteName() => "设置：音量与落盘（C# 侧）";

    public override void Run()
    {
        CheckEq(SettingsStore.file_name(), "settings.cfg", "设置文件名");
        CheckEq(SettingsStore.default_volume(), 1.0, "默认音量 1.0（不是静音）");
        CheckEq(SettingsStore.default_auto_battle(), false, "自动战斗默认关（自动是省事不是夺权）");

        DirAccess.MakeDirRecursiveAbsolute(TestDir);
        SettingsStore store = SettingsStore.create(TestDir);
        CheckEq(store.base_dir, TestDir, "目录照传进来的值");
        CheckEq(store.file_path(), TestDir + "/settings.cfg", "落盘位置在基目录旁边");
        DirAccess.RemoveAbsolute(store.file_path());

        _CheckVolumeRoundTrip(store);
        _CheckTwoKeysDoNotOverwriteEachOther(store);
        _CheckToleratesBroken(store);
        _CheckWriteRefused(store);
        CheckEq(
            new SettingsStore().base_dir, SaveStore.default_dir(),
            "零参构造（GDScript 的 `.new()`）用存档目录的默认值");
    }

    private void _CheckVolumeRoundTrip(SettingsStore store)
    {
        Godot.Collections.Dictionary missing = store.load_volume();
        CheckTrue(missing["ok"].AsBool(), "还没有设置文件时读音量不报错");
        CheckEq(missing["volume"].AsDouble(), 1.0, "还没有设置文件时用默认音量 1.0");

        Godot.Collections.Dictionary saved = store.save_volume(0.25);
        CheckTrue(saved["ok"].AsBool(), $"写设置：{saved["error"].AsString()}");
        Godot.Collections.Dictionary loaded = store.load_volume();
        CheckTrue(loaded["ok"].AsBool(), $"读设置：{loaded["error"].AsString()}");
        CheckEq(loaded["volume"].AsDouble(), 0.25, "写进去 0.25，读回来还是 0.25");
        CheckTrue(FileAccess.FileExists(store.file_path()), $"设置真的落盘了：{store.file_path()}");

        // 夹到 [0,1]：音量不是随便写的数，超界的值要夹回去而不是原样存
        store.save_volume(1.8);
        CheckEq(store.load_volume()["volume"].AsDouble(), 1.0, "超过 1 的值夹到 1");
        store.save_volume(-0.5);
        CheckEq(store.load_volume()["volume"].AsDouble(), 0.0, "负数夹到 0");
    }

    private void _CheckTwoKeysDoNotOverwriteEachOther(SettingsStore store)
    {
        CheckFalse(store.load_auto_battle()["enabled"].AsBool(), "没写过时自动战斗是关的");
        CheckTrue(store.save_auto_battle(true)["ok"].AsBool(), "能写自动战斗开关");
        CheckTrue(store.load_auto_battle()["enabled"].AsBool(), "写进去 true，读回来还是 true");

        // 关键：再动音量，**不能**把自动战斗冲掉；再动自动战斗，也不能把音量冲掉
        CheckTrue(store.save_volume(0.5)["ok"].AsBool(), "顺手写一次音量");
        CheckTrue(
            store.load_auto_battle()["enabled"].AsBool(),
            "写音量之后自动战斗开关还在（两个键互不覆盖）");
        CheckEq(store.load_volume()["volume"].AsDouble(), 0.5, "音量也确实写进去了");
        CheckTrue(store.save_auto_battle(false)["ok"].AsBool(), "再关掉自动战斗");
        CheckEq(store.load_volume()["volume"].AsDouble(), 0.5, "关自动战斗之后音量还在");
        CheckFalse(store.load_auto_battle()["enabled"].AsBool(), "关掉之后读回来是关");
    }

    private void _CheckToleratesBroken(SettingsStore store)
    {
        // 读坏：文件内容不是合法 cfg → 报错 + 退回默认值（**不是 0＝静音**）
        WriteText(store.file_path(), "[audio\nvolume = 这不是数字");
        Godot.Collections.Dictionary broken = store.load_volume();
        CheckFalse(broken["ok"].AsBool(), "坏掉的设置文件要报错（不静默吞掉）");
        CheckEq(broken["volume"].AsDouble(), 1.0, "坏掉时退回默认音量（不是 0＝静音）");
        Godot.Collections.Dictionary broken_auto = store.load_auto_battle();
        CheckFalse(broken_auto["ok"].AsBool(), "坏掉时读自动战斗也报错");
        CheckFalse(broken_auto["enabled"].AsBool(), "坏掉时不替玩家开自动");
    }

    private void _CheckWriteRefused(SettingsStore store)
    {
        // 写不进去：目录被一个同名文件挡着 → 如实返回失败，不抛错、不挡游戏
        string blocker_dir = TestDir + "/blocker";
        WriteText(blocker_dir, "x");
        SettingsStore blocked = SettingsStore.create(blocker_dir + "/sub");
        Godot.Collections.Dictionary failed = blocked.save_volume(0.5);
        CheckFalse(failed["ok"].AsBool(), $"写不进去时如实返回失败：{failed["error"].AsString()}");
        CheckFalse(failed["error"].AsString().Length == 0, "失败要带原因（界面照实说）");
    }

    private static void WriteText(string path, string text)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file?.StoreString(text);
        file?.Close();
    }
}
