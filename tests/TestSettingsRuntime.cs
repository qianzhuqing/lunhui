using Godot;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 设备设置（音量 ＋ 自动战斗开关）与它们在枢纽页上的入口——
/// 对位 `tests/test_settings.gd`（2026-10-11 P4 第十六批搬 C#）。
///
/// 由来：音效接上以后玩家**关不掉**——设计没有设置界面／暂停菜单，而音量是玩家随时会想改的东西。
/// 这条用例钉三件事：① 设置文件读写（读不到／读坏／写不进去都要有说法，**绝不悄悄静音**）；
/// ② 音量真的写到那条总线上（不是只改了个数字）；③ 枢纽页那两个按钮按一下轮换一档、当场生效。
///
/// **类名取 `TestSettingsRuntime`：`TestSettings.cs` 已被占**（`SettingsStore` 搬 C# 时写的那份，
/// suite 名带「（C# 侧）」，专管落盘那四节，外加音量夹到 `[0,1]`）。这一份是旧链原来那份，
/// **多出的是"真生效"那两节**：音量真的写进音效总线（要 `Sfx` ＋ `AudioServer`），
/// 以及枢纽页那两个按钮走真场景（要 `PlaceholderGame`）——那时它们还没搬过来。
/// </summary>
public partial class TestSettingsRuntime : TestCase
{
    private const string HubScene = "res://scenes/placeholder_game.tscn";
    private const string TestDir = "res://.logs/test_settings";

    public override string SuiteName() => "设置：音量与落盘";

    public override void Run()
    {
        _check_store_round_trip();
        _check_store_tolerates_broken();
        _check_auto_battle_round_trip();
        _check_volume_hits_the_bus();
        _check_hub_button();
        _check_hub_auto_battle_button();
    }

    /// <summary>
    /// 自动战斗开关（设计 11 §四）：默认**关**、能存能读，
    /// 而且**两个设置互不覆盖**——这是这条用例真正的重点：
    /// 每个设置各自 `ConfigFile.new()` 后整份保存过一次，那会把另一个键冲掉。
    /// </summary>
    private void _check_auto_battle_round_trip()
    {
        string dir = TestDir + "/auto";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        SettingsStore store = SettingsStore.create(dir);
        DirAccess.RemoveAbsolute(store.file_path());
        Godot.Collections.Dictionary missing = store.load_auto_battle();
        CheckTrue(missing["ok"].AsBool(), "没有设置文件时读自动战斗不报错");
        CheckFalse(missing["enabled"].AsBool(), "默认**关**（自动是省事不是夺权）");

        CheckTrue(store.save_auto_battle(true)["ok"].AsBool(), "能写自动战斗开关");
        CheckTrue(store.load_auto_battle()["enabled"].AsBool(), "写进去 true，读回来还是 true");

        // 关键：再动音量，**不能**把自动战斗冲掉；再动自动战斗，也不能把音量冲掉
        CheckTrue(store.save_volume(0.5)["ok"].AsBool(), "顺手写一次音量");
        CheckTrue(
            store.load_auto_battle()["enabled"].AsBool(),
            "写音量之后自动战斗开关还在（两个键互不覆盖）");
        CheckFloat(store.load_volume()["volume"].AsDouble(), 0.5, "音量也确实写进去了");
        CheckTrue(store.save_auto_battle(false)["ok"].AsBool(), "再关掉自动战斗");
        CheckFloat(store.load_volume()["volume"].AsDouble(), 0.5, "关自动战斗之后音量还在");
        CheckFalse(store.load_auto_battle()["enabled"].AsBool(), "关掉之后读回来是关");
    }

    /// <summary>枢纽页那个自动战斗按钮：按一下开／关、文案跟着走、写进设置文件。</summary>
    private void _check_hub_auto_battle_button()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树");
            return;
        }

        string dir = TestDir + "/hub_auto";
        DirAccess.MakeDirRecursiveAbsolute(dir);
        PlaceholderGame hub = GD.Load<PackedScene>(HubScene).Instantiate<PlaceholderGame>();
        hub._settings_override = SettingsStore.create(dir);
        DirAccess.RemoveAbsolute(((SettingsStore)hub._settings_override).file_path());
        Tree.Root.AddChild(hub);
        hub.setup();
        var button = hub.FindChild("AutoBattleButton", true, false) as Button;
        CheckNotNull(button, "枢纽页有自动战斗开关（与音量同处，设计 11 §四）");
        if (button != null)
        {
            CheckTrue(button.Text.Contains("关"), $"默认文案是「关」：{button.Text}");
            button.EmitSignal("pressed");
            CheckTrue(button.Text.Contains("开"), $"按一下变「开」：{button.Text}");
            CheckTrue(
                ((SettingsStore)hub._settings_override).load_auto_battle()["enabled"].AsBool(),
                "开关写进了设置文件");
            button.EmitSignal("pressed");
            CheckTrue(button.Text.Contains("关"), $"再按一下变回「关」：{button.Text}");
            CheckFalse(
                ((SettingsStore)hub._settings_override).load_auto_battle()["enabled"].AsBool(),
                "关也写进了设置文件");
        }

        Tree.Root.RemoveChild(hub);
        hub.Free();
    }

    private void _check_store_round_trip()
    {
        DirAccess.MakeDirRecursiveAbsolute(TestDir);
        SettingsStore store = SettingsStore.create(TestDir);
        // 上一轮跑留下的文件会让「没有文件时用默认值」这条断言变成时红时绿——先清干净再验
        DirAccess.RemoveAbsolute(store.file_path());
        Godot.Collections.Dictionary missing = store.load_volume();
        CheckTrue(
            missing["ok"].AsBool() && Mathf.IsEqualApprox(missing["volume"].AsDouble(), 1.0),
            $"还没有设置文件时用默认音量 1.0（实际 {missing["volume"].AsDouble()}）");
        Godot.Collections.Dictionary saved = store.save_volume(0.25);
        CheckTrue(saved["ok"].AsBool(), $"写设置：{_s(saved, "error")}");
        Godot.Collections.Dictionary loaded = store.load_volume();
        CheckTrue(loaded["ok"].AsBool(), $"读设置：{_s(loaded, "error")}");
        CheckFloat(loaded["volume"].AsDouble(), 0.25, "写进去 0.25，读回来还是 0.25");
        CheckTrue(
            FileAccess.FileExists(store.file_path()),
            $"设置落在存档目录旁边的 settings.cfg：{store.file_path()}");
    }

    private void _check_store_tolerates_broken()
    {
        // 读坏：文件内容不是合法 cfg → 报错 + 退回默认音量（**不是 0＝静音**）
        string brokenDir = TestDir + "/broken";
        DirAccess.MakeDirRecursiveAbsolute(brokenDir);
        SettingsStore broken = SettingsStore.create(brokenDir);
        using (FileAccess handle = FileAccess.Open(broken.file_path(), FileAccess.ModeFlags.Write))
        {
            CheckNotNull(handle, "能造一个坏设置文件");
            handle?.StoreString("[audio\nvolume = 这不是数字");
        }

        Godot.Collections.Dictionary loaded = broken.load_volume();
        CheckFalse(loaded["ok"].AsBool(), "坏掉的设置文件要报错（不静默吞掉）");
        CheckFloat(loaded["volume"].AsDouble(), 1.0, "坏掉时退回默认音量（不是 0＝静音）");

        // 写不进去：目录被一个同名文件挡着 → 如实返回失败，不抛错、不挡游戏
        string blockerDir = TestDir + "/blocker";
        using (FileAccess blocker = FileAccess.Open(blockerDir, FileAccess.ModeFlags.Write))
        {
            CheckNotNull(blocker, "造一个挡路的同名文件");
            blocker?.StoreString("x");
        }

        SettingsStore blocked = SettingsStore.create(blockerDir + "/sub");
        Godot.Collections.Dictionary failed = blocked.save_volume(0.5);
        CheckFalse(failed["ok"].AsBool(), $"写不进去时如实返回失败：{_s(failed, "error")}");
    }

    private void _check_volume_hits_the_bus()
    {
        int bus = AudioServer.GetBusIndex(Sfx.sound_bus());
        CheckGt(bus, 0.0, $"音效总线在（{Sfx.sound_bus()}）");
        // `persist=false`：用例不写玩家的设置文件
        Sfx.set_volume(0.25, false);
        CheckFloat(Sfx.volume(), 0.25, "音量记在 Sfx 里");
        CheckFloat(
            Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)),
            0.25,
            "0.25 真的写到了总线上（不是只改了个数字）",
            0.01);
        CheckEq(Sfx.volume_text(), "音量：25%", "文案写百分比");
        CheckFloat(Sfx.cycle_volume(false), 0.5, "轮换到下一档");
        Sfx.set_volume(1.0, false);
        CheckFloat(Sfx.cycle_volume(false), 0.0, "到顶回到静音");
        CheckEq(Sfx.volume_text(), "音量：静音", "0 说「静音」不说 0%");
        CheckLt(
            Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)),
            0.001,
            $"静音时总线确实听不见（{Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus))}）");
        Sfx.set_volume(1.0, false);
        CheckFloat(
            Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus)),
            1.0,
            "恢复 100%：总线回到原音量",
            0.01);
    }

    private void _check_hub_button()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树");
            return;
        }

        var sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        sessionNode?.Call("set_state", SoloState(GetDb()));
        PlaceholderGame hub = GD.Load<PackedScene>(HubScene).Instantiate<PlaceholderGame>();
        Tree.Root.AddChild(hub);
        hub.setup();
        var button = hub.FindChild("VolumeButton", true, false) as Button;
        CheckNotNull(button, "枢纽页有音量按钮（设计还没有设置界面，这里是游戏内唯一随时到得了的菜单）");
        if (button != null)
        {
            Sfx.set_volume(1.0, false);
            button.Text = Sfx.volume_text();
            button.EmitSignal("pressed");
            CheckFloat(Sfx.volume(), 0.0, "按一下从 100% 轮到静音");
            CheckTrue(button.Text.Contains("静音"), $"按钮文案跟着更新：{button.Text}");
            button.EmitSignal("pressed");
            CheckFloat(Sfx.volume(), 0.25, "再按一下回到 25%");
            CheckTrue(button.Text.Contains("25%"), $"按钮文案继续跟着走：{button.Text}");
            var notice = hub.FindChild("Notice", true, false) as Label;
            CheckNotNull(notice, "枢纽页有反馈行");
            if (notice != null)
            {
                CheckTrue(notice.Text.Contains("音量"), $"按完给一句反馈：{notice.Text}");
            }

            // 写不进去（沙箱里的 user://）**不许挡住使用**：音量已经在本次运行内生效，
            // 只记一条警告——与「存档写不进去不挡开局」同一条纪律
            CheckFloat(
                Mathf.DbToLinear(
                    AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(Sfx.sound_bus()))),
                0.25,
                "按钮按下后总线就变（落盘失败也不影响）",
                0.01);
        }

        Tree.Root.RemoveChild(hub);
        hub.Free();
        Sfx.set_volume(1.0, false);
    }

    /// <summary>对位 GDScript 的 `dict.get(key, "")`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
