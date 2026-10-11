using Godot;

namespace Lunhui.Core;

/// <summary>
/// 可复现的随机源（C# 版，对位原 `src/core/rng_service.gd`）。
///
/// 战斗、掉落的所有随机都必须走这里，测试靠固定种子拿到确定结果。
///
/// **两个构造入口**（GDScript 的 `new(seed)`／`new()` 都过不了 C# 的解析期实参检查，
/// C# 的默认参数对 GDScript 也不生效，所以拆成两个名字）：
///   · `create(seed)`        —— 固定种子（用例与可复现路径**只许用这个**）
///   · `create_random()`     —— 显式随机（等价旧的 `new()`；用例里禁止出现，
///     门限见 `tests/test_handshake_code.gd::_check_unsafe_rng`）
/// </summary>
public partial class RngService : RefCounted
{
    private readonly RandomNumberGenerator _rng = new();

    public RngService(int seed_value)
    {
        set_seed(seed_value);
    }

    /// <summary>GDScript 侧固定种子那一版（`seed_value` 传 -1 也当随机，与旧口径一致）。</summary>
    public static RngService create(int seed_value) => new(seed_value);

    /// <summary>GDScript 侧「不要种子」那一版（旧 `new()`）。名字刻意写全，好在调用点一眼认出来。</summary>
    public static RngService create_random() => new(-1);

    public void set_seed(int seed_value)
    {
        if (seed_value < 0)
        {
            _rng.Randomize();
        }
        else
        {
            _rng.Seed = (ulong)seed_value;
        }
    }

    public long get_seed() => (long)_rng.Seed;

    public double randf() => _rng.Randf();

    /// <summary>
    /// **端点按引擎的 `real_t`（float）收窄**再进引擎——这不是丢精度，而是与 GDScript 完全同一条路：
    /// `RandomNumberGenerator` 在单精度构建里就是 `float`，GDScript 的 `randf_range(double, double)`
    /// 经 Variant 调用时同样收窄到 `real_t`。所以这里显式转 float 才**不改**既有手感与用例期望值。
    /// </summary>
    public double randf_range(double from, double to) =>
        _rng.RandfRange((float)from, (float)to);

    public int randi_range(int from, int to) => _rng.RandiRange(from, to);

    /// <summary>以概率 p 判定：p &lt;= 0 恒 false，p &gt;= 1 恒 true。</summary>
    public bool chance(double p)
    {
        if (p <= 0.0)
        {
            return false;
        }

        if (p >= 1.0)
        {
            return true;
        }

        return _rng.Randf() < p;
    }

    /// <summary>
    /// 档位抽取：按权重从 entries（`[{"key": Variant, "weight": float}, ...]`）里取一个。
    /// 权重全为 0 或列表为空时返回 null。
    /// </summary>
    public Variant pick_weighted(Godot.Collections.Array entries)
    {
        double total = 0.0;
        foreach (Variant entry_variant in entries)
        {
            total += System.Math.Max(0.0, number_of(entry_variant.AsGodotDictionary(), "weight"));
        }

        if (total <= 0.0 || entries.Count == 0)
        {
            return default;
        }

        double roll = _rng.Randf() * total;
        double cursor = 0.0;
        foreach (Variant entry_variant in entries)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            cursor += System.Math.Max(0.0, number_of(entry, "weight"));
            if (roll < cursor)
            {
                return entry["key"];
            }
        }

        return entries[entries.Count - 1].AsGodotDictionary()["key"];
    }

    private static double number_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;
}
