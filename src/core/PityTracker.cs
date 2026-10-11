using Godot;

namespace Lunhui.Core;

/// <summary>
/// 掉落保底计数器（C# 版，对位原 `src/core/pity_tracker.gd`）。
///
/// 计数单位是「掉落组 + 掉落槽」（pity_count 配在槽上，所以按槽计数最不容易含糊）。
/// `to_dict` / `from_dict` 是跨难度继承的落点：切换难度时不要清空，直接带着走。
/// 跨周目继承随多周目一起暂缓（`docs/design/CHANGELOG.md` 0.4.1）：接口留着，暂不启用。
///
/// `VERSION` 是存档里的标记，测试读它（`test_drop.gd` 原来读 `PityTrackerScript.VERSION`）——
/// GDScript 读不到 C# 的 `const`，所以留一个 `version()` 过渡取值口。
/// </summary>
public partial class PityTracker : RefCounted
{
    public const int VERSION = 1;

    /// <summary>过渡取值口（见类注释）。</summary>
    public static int version() => VERSION;

    private readonly Godot.Collections.Dictionary _counters = new();

    public PityTracker()
    {
    }

    /// <summary>GDScript 侧唯一的构造入口（`new()` 过不了解析期检查）。</summary>
    public static PityTracker create() => new();

    /// <summary>记一次未出货，返回累计次数。</summary>
    public int register_attempt(string key)
    {
        int count = (_counters.ContainsKey(key) ? _counters[key].AsInt32() : 0) + 1;
        _counters[key] = count;
        return count;
    }

    public int attempts(string key) => _counters.ContainsKey(key) ? _counters[key].AsInt32() : 0;

    /// <summary>是否已达保底（达到即本次强制掉落）。</summary>
    public bool should_force(string key, int threshold) => threshold > 0 && attempts(key) >= threshold;

    /// <summary>出货后清零。</summary>
    public void reset(string key) => _counters.Remove(key);

    public void clear() => _counters.Clear();

    public int size() => _counters.Count;

    public Godot.Collections.Dictionary to_dict() => new()
    {
        { "version", VERSION },
        { "counters", _counters.Duplicate(true) },
    };

    public void from_dict(Godot.Collections.Dictionary data)
    {
        _counters.Clear();
        Godot.Collections.Dictionary raw = data.ContainsKey("counters")
            ? data["counters"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        foreach (Variant key in raw.Keys)
        {
            _counters[key.AsString()] = raw[key].AsInt32();
        }
    }
}
