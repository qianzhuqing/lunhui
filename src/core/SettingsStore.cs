using Godot;

namespace Lunhui.Core;

/// <summary>
/// 设备设置的落盘（音量 ＋ 自动战斗开关）（C# 版，对位原 `src/core/settings_store.gd`）。
///
/// **为什么不放进存档**：音量是「这台机器／这个玩家」的设置，不该跟着存档槽走
/// （换个存档玩，音量跟着变会很怪）。所以它落在**存档目录旁边**的 `settings.cfg`——
/// 目录解析与存档共用一处（`SaveStore.default_dir()`，支持 `--save-dir=&lt;路径&gt;` 覆盖），
/// 沙箱与自检里也能指到可写目录。
///
/// **写不进去不许挡住游戏**（与存档同一条纪律）：`save_volume()` 把 {ok,error} 交给调用方决定怎么说；
/// 读不到／读坏了退回默认音量 1.0——**绝不悄悄变成 0**（"静音"是最容易看起来正常的坏结果）。
/// </summary>
public partial class SettingsStore : RefCounted
{
    public const string FILE_NAME = "settings.cfg";
    public const string SECTION = "audio";
    public const string KEY_VOLUME = "volume";
    public const double DEFAULT_VOLUME = 1.0;

    /// <summary>自动战斗开关（设计 11 §四）：默认**关**——自动是省事不是夺权。</summary>
    public const string SECTION_BATTLE = "battle";
    public const string KEY_AUTO_BATTLE = "auto_battle";
    public const bool DEFAULT_AUTO_BATTLE = false;

    // ---------------------------------------------------------------- 过渡期取值口

    public static string file_name() => FILE_NAME;
    public static double default_volume() => DEFAULT_VOLUME;
    public static bool default_auto_battle() => DEFAULT_AUTO_BATTLE;

    [Export]
    public string base_dir { get; set; } = "";

    /// <summary>引擎那侧 `SettingsStore.new()`（零参）走这一条：目录用存档目录的默认值。</summary>
    public SettingsStore() : this("")
    {
    }

    public SettingsStore(string dir_path)
    {
        base_dir = dir_path.Length == 0 ? SaveStore.default_dir() : dir_path;
    }

    /// <summary>GDScript 侧的「指定目录」入口（零参那路照旧 `SettingsStoreScript.new()`）。</summary>
    public static SettingsStore create(string dir_path) => new(dir_path);

    public string file_path() => $"{base_dir}/{FILE_NAME}";

    /// <summary>读音量。返回 {ok, volume, error}。</summary>
    public Godot.Collections.Dictionary load_volume()
    {
        if (!FileAccess.FileExists(file_path()))
        {
            return VolumeResult(true, DEFAULT_VOLUME, "");
        }
        var config = new ConfigFile();
        Error error = config.Load(file_path());
        if (error != Error.Ok)
        {
            return VolumeResult(false, DEFAULT_VOLUME, $"设置文件读不了（错误码 {(int)error}）");
        }
        Variant value = config.GetValue(SECTION, KEY_VOLUME, DEFAULT_VOLUME);
        if (value.VariantType != Variant.Type.Float && value.VariantType != Variant.Type.Int)
        {
            return VolumeResult(false, DEFAULT_VOLUME, $"音量不是数字：{value}");
        }
        return VolumeResult(true, Mathf.Clamp(value.AsDouble(), 0.0, 1.0), "");
    }

    /// <summary>写音量。返回 {ok, error}。</summary>
    public Godot.Collections.Dictionary save_volume(double value) =>
        Write(SECTION, KEY_VOLUME, Mathf.Clamp(value, 0.0, 1.0));

    /// <summary>
    /// 读自动战斗开关。返回 {ok, enabled, error}。
    ///
    /// 与音量同一条纪律：**读不到／读坏退回默认（关）**，不因为设置文件坏了就替玩家开自动。
    /// </summary>
    public Godot.Collections.Dictionary load_auto_battle()
    {
        if (!FileAccess.FileExists(file_path()))
        {
            return AutoBattleResult(true, DEFAULT_AUTO_BATTLE, "");
        }
        var config = new ConfigFile();
        Error error = config.Load(file_path());
        if (error != Error.Ok)
        {
            return AutoBattleResult(false, DEFAULT_AUTO_BATTLE, $"设置文件读不了（错误码 {(int)error}）");
        }
        Variant value = config.GetValue(SECTION_BATTLE, KEY_AUTO_BATTLE, DEFAULT_AUTO_BATTLE);
        if (value.VariantType != Variant.Type.Bool && value.VariantType != Variant.Type.Int)
        {
            return AutoBattleResult(false, DEFAULT_AUTO_BATTLE, $"自动战斗开关不是布尔：{value}");
        }
        return AutoBattleResult(true, value.AsBool(), "");
    }

    /// <summary>写自动战斗开关。返回 {ok, error}。</summary>
    public Godot.Collections.Dictionary save_auto_battle(bool enabled) =>
        Write(SECTION_BATTLE, KEY_AUTO_BATTLE, enabled);

    /// <summary>
    /// 落盘一个键——**先读回整份配置再写**。
    ///
    /// 以前每种设置各自 `ConfigFile.new()` 后整份保存：文件里只有那一次写进去的节，
    /// 于是「按一下音量」会把自动战斗开关冲掉（反过来也一样）。设置一多这条就会真丢东西，
    /// 所以落盘只留这一处。
    /// </summary>
    private Godot.Collections.Dictionary Write(string section, string key, Variant value)
    {
        Error error = DirAccess.MakeDirRecursiveAbsolute(base_dir);
        if (error != Error.Ok && error != Error.AlreadyExists)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", $"建不了设置目录：{base_dir}（错误码 {(int)error}）" },
            };
        }
        var config = new ConfigFile();
        if (FileAccess.FileExists(file_path()))
        {
            // 读坏了也不能把别的设置清零：退回空配置，只写这一个键
            config.Load(file_path());
        }
        config.SetValue(section, key, value);
        error = config.Save(file_path());
        if (error != Error.Ok)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", $"设置写不进去：{file_path()}（错误码 {(int)error}）" },
            };
        }
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    private static Godot.Collections.Dictionary VolumeResult(bool ok, double volume, string error) =>
        new() { { "ok", ok }, { "volume", volume }, { "error", error } };

    private static Godot.Collections.Dictionary AutoBattleResult(bool ok, bool enabled, string error) =>
        new() { { "ok", ok }, { "enabled", enabled }, { "error", error } };
}
