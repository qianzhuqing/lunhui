using Godot;

namespace Lunhui.Core;

/// <summary>
/// `BattleSimulator` 的「战斗过程记录与共用小工具」这一块（partial 之一）：
/// 招式冷却、击杀方式、招式使用次数（供隐藏内容与熟练度结算读），
/// 以及取值／切片／拼数组那一组跨件共用的小助手。
/// </summary>
public partial class BattleSimulator
{
    // ------------------------------------------------------------------ 冷却与击杀统计

    /// <summary>这个招式还要等几回合才能再用（0 = 现在就能用）。</summary>
    public int cooldown_left(string actor_id, string skill_id)
    {
        Godot.Collections.Dictionary entries = _cooldowns.ContainsKey(actor_id)
            ? _cooldowns[actor_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        int ready_round = _i(entries, skill_id, 0);
        return System.Math.Max(0, ready_round - _rounds);
    }

    /// <summary>从第几回合起又能用了（0 = 现在就能用）。</summary>
    public int cooldown_ready_round(string actor_id, string skill_id)
    {
        Godot.Collections.Dictionary entries = _cooldowns.ContainsKey(actor_id)
            ? _cooldowns[actor_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return _i(entries, skill_id, 0);
    }

    private void _start_cooldown(BattleActor actor, Resource skill)
    {
        int rounds = skill.Get("cooldown").AsInt32();
        if (rounds <= 0 || actor == null)
        {
            return;
        }
        Godot.Collections.Dictionary entries = _cooldowns.ContainsKey(actor.actor_id)
            ? _cooldowns[actor.actor_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        // 冷却 N 回合 = 接下来 N 个回合不能再放（第 N+1 个回合起可用）
        entries[skill.Get("skill_id").AsString()] = _rounds + rounds + 1;
        _cooldowns[actor.actor_id] = entries;
    }

    public string kill_style_of(string actor_id) =>
        _kill_styles.ContainsKey(actor_id) ? _kill_styles[actor_id].AsString() : "";

    public Godot.Collections.Dictionary kill_styles() => _kill_styles.Duplicate();

    /// <summary>某个单位这一场施放过的招式次数（没有就空字典）。</summary>
    public Godot.Collections.Dictionary skill_uses(string actor_id) =>
        _skill_uses.ContainsKey(actor_id)
            ? _skill_uses[actor_id].AsGodotDictionary().Duplicate()
            : new Godot.Collections.Dictionary();

    private void _record_use(BattleActor actor, Resource skill)
    {
        if (actor == null || skill == null)
        {
            return;
        }
        // 现造的「普通攻击」不是武学：不进招式使用记录（隐藏内容的 skill_use 判定不该被它污染）
        if (skill.Get("skill_id").AsString() == BASIC_ATTACK_ID)
        {
            return;
        }
        string actor_id = actor.actor_id;
        Godot.Collections.Dictionary uses = _skill_uses.ContainsKey(actor_id)
            ? _skill_uses[actor_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        string skill_id = skill.Get("skill_id").AsString();
        uses[skill_id] = _i(uses, skill_id, 0) + 1;
        _skill_uses[actor_id] = uses;
    }

    // ------------------------------------------------------------------ 共用小工具

    /// <summary>`_allies + _enemies`：两段拼成一个新数组（顺次遍历两边，不额外建对象）。</summary>
    private Godot.Collections.Array _both_sides() => _both(_allies, _enemies);

    private static Godot.Collections.Array _both(Godot.Collections.Array first, Godot.Collections.Array second)
    {
        var out_arr = new Godot.Collections.Array();
        foreach (Variant value in first)
        {
            out_arr.Add(value);
        }
        foreach (Variant value in second)
        {
            out_arr.Add(value);
        }
        return out_arr;
    }

    /// <summary>`array.slice(from)`：日志行按索引切片（C# 的 `Array.Slice` 签名易踩坑，这里自己遍历）。</summary>
    private static Godot.Collections.Array _slice(Godot.Collections.Array source, int from)
    {
        var out_arr = new Godot.Collections.Array();
        for (int i = from; i < source.Count; i++)
        {
            out_arr.Add(source[i]);
        }
        return out_arr;
    }

    /// <summary>按元数各留一条：`add_buff` 有 3／4 参两条重载，这里统一走 4 参。</summary>
    private static Godot.Collections.Dictionary _add_buff(
        BattleActor actor, string buff_id, string source_type, string source_id, int stacks) =>
        actor.add_buff(buff_id, source_type, source_id, System.Math.Max(1, stacks));

    /// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;

    /// <summary>`dict.get(key, {})`：键不在或不是字典时给一个空字典。</summary>
    private static Godot.Collections.Dictionary _d(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotDictionary() : new Godot.Collections.Dictionary();

    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static double _f(Godot.Collections.Dictionary dict, string key, double fallback = 0.0) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();
}
