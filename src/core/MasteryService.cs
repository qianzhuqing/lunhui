using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 熟练度成长（C# 版，对位原 `src/core/mastery_service.gd`；设计 01「熟练度（修炼）」）。
///
/// 三种来源：**战斗中施放**（慢）、**打坐修炼**（客栈／门派，快，花铜钱）、**秘籍灌注**（一次性，稀有材料）。
/// 本文件实现前两种，数值全在表里：
///   `growth_const.mastery_combat_gain` 每次施放 +1（表里写死的注释就是这个口径）
///   `growth_const.mastery_max` 上限 10
///   `skill_star_def.cultivate_cost_base` × `growth_const.cultivate_cost_growth`^(熟练度-1) 是打坐费用
///
/// 口径说明：**一次打坐 = 熟练度 +1**，费用按「当前等级 → 下一级」算
/// （设计写「把一部武学从 1 练到 10 是一笔真实开销」，与逐级递增的费用公式一致；
/// 若设计要一次打坐加更多级，改 `train()` 里的 `amount` 一处即可）。
///
/// 秘籍灌注没做：它要「稀有材料」的消耗规则，设计只写了「一次性」，没给材料与幅度。
///
/// **两条迁移形状**（照 `GrowthCalculator` 那几条实测结论）：
///   ① 构造走 `create(db, state)` 静态工厂；
///   ② GDScript 调得到的 public 方法**不留可选参数**（`cap` 由调用方写明，`apply_combat_usage` 一律传满实参）。
/// **过渡期的 `state` 收成 `GodotObject`**：`GameState` 还是 GDScript，等它搬过来再收紧类型。
/// </summary>
public partial class MasteryService : RefCounted
{
    private readonly TableDb _db;
    private readonly GodotObject _state;
    private readonly GrowthCalculator _growth;

    public MasteryService(TableDb db, GodotObject state)
    {
        _db = db;
        _state = state;
        _growth = db != null ? GrowthCalculator.create(db) : null;
    }

    /// <summary>GDScript 侧唯一的构造入口（见类注释）。</summary>
    public static MasteryService create(TableDb db, GodotObject state) => new(db, state);

    public int mastery_max() => _growth != null ? _growth.mastery_max() : 10;

    public int mastery_of(string char_id, string skill_id) =>
        _state != null ? _state.Call("mastery_of", char_id, skill_id).AsInt32() : 0;

    public int star_of(string skill_id)
    {
        var row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
        return row != null ? row.star : 0;
    }

    public string skill_name(string skill_id)
    {
        var row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
        return row != null ? row.name_cn : skill_id;
    }

    // ------------------------------------------------------------------ 打坐

    /// <summary>打坐一次的费用：按「当前熟练度 → 下一级」查 star 表。</summary>
    public int train_cost(string char_id, string skill_id)
    {
        if (_growth == null)
        {
            return 0;
        }

        return _growth.cultivate_cost(star_of(skill_id), mastery_of(char_id, skill_id) + 1);
    }

    /// <summary>能不能打坐：`{ok, error, cost, mastery, max}`。</summary>
    public Godot.Collections.Dictionary can_train(string char_id, string skill_id)
    {
        int current = mastery_of(char_id, skill_id);
        int top = mastery_max();
        int cost = train_cost(char_id, skill_id);
        var out_check = new Godot.Collections.Dictionary
        {
            { "ok", false }, { "error", "" }, { "cost", cost }, { "mastery", current }, { "max", top },
        };
        if (_state == null || _inventory() == null)
        {
            out_check["error"] = "没有会话状态";
            return out_check;
        }

        if (_db.get_row("skill_base", skill_id) == null)
        {
            out_check["error"] = $"没有这部武学：{skill_id}";
            return out_check;
        }

        if (!_state.Call("is_learned", char_id, skill_id).AsBool())
        {
            out_check["error"] = "还没学会这部武学";
            return out_check;
        }

        if (current >= top)
        {
            out_check["error"] = $"已经练满（{current}/{top}）";
            return out_check;
        }

        int money = _inventory().Get("money").AsInt32();
        if (money < cost)
        {
            out_check["error"] = $"铜钱不够（需要 {cost}，现有 {money}）";
            return out_check;
        }

        out_check["ok"] = true;
        return out_check;
    }

    /// <summary>打坐一次：扣铜钱、熟练度 +1。返回 `{ok, error, cost, mastery, before}`。</summary>
    public Godot.Collections.Dictionary train(string char_id, string skill_id)
    {
        Godot.Collections.Dictionary check = can_train(char_id, skill_id);
        if (!check["ok"].AsBool())
        {
            int current = check["mastery"].AsInt32();
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", check["error"] }, { "cost", 0 },
                { "before", current }, { "mastery", current },
            };
        }

        int before = check["mastery"].AsInt32();
        int cost = check["cost"].AsInt32();
        GodotObject inventory = _inventory();
        inventory.Set("money", inventory.Get("money").AsInt32() - cost);
        int after = _state.Call("set_mastery", char_id, skill_id, before + 1, mastery_max()).AsInt32();
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "cost", cost }, { "before", before }, { "mastery", after },
        };
    }

    // ------------------------------------------------------------------ 战斗施放

    /// <summary>
    /// 战斗后结算：usage 是「实际施放过的招式 → 次数」，每次施放 +mastery_combat_gain。
    /// 返回 `[{skill_id, name, before, after, gained}]`（没有涨的不列出来）。
    ///
    /// `cap` &gt; 0 时把这一次的成长卡在该值（练习战的 `growth_const.dummy_mastery_cap`）：
    /// **只压涨、不压低**——已经练到 5 级的招式打木桩不该掉回 3 级。
    /// 迁移期**不留默认值**：GDScript 调用方一律写明 `cap`（不要上限就传 0）。
    /// </summary>
    public Godot.Collections.Array apply_combat_usage(string char_id, Godot.Collections.Dictionary usage, int cap)
    {
        var out_entries = new Godot.Collections.Array();
        if (_state == null || usage.Count == 0)
        {
            return out_entries;
        }

        int per_cast = _growth.constant_int("mastery_combat_gain");
        if (per_cast <= 0)
        {
            return out_entries;
        }

        foreach (Variant skill_id in usage.Keys)
        {
            int casts = System.Math.Max(0, usage[skill_id].AsInt32());
            Godot.Collections.Dictionary gained = _gain(char_id, skill_id.AsString(), per_cast * casts, cap);
            if (gained["gained"].AsInt32() > 0)
            {
                out_entries.Add(gained);
            }
        }

        return out_entries;
    }

    private Godot.Collections.Dictionary _gain(string char_id, string skill_id, int amount, int cap)
    {
        int before = mastery_of(char_id, skill_id);
        int limit = mastery_max();
        int target = before + amount;
        if (cap > 0)
        {
            // 上限只压「这一次涨到哪」，不压低已有的（打木桩不该让练好的招式退步）
            limit = System.Math.Min(cap, limit);
            target = System.Math.Max(before, System.Math.Min(target, limit));
        }

        int after = _state.Call("set_mastery", char_id, skill_id, target, mastery_max()).AsInt32();
        return new Godot.Collections.Dictionary
        {
            { "skill_id", skill_id },
            { "name", skill_name(skill_id) },
            { "before", before },
            { "after", after },
            { "gained", after - before },
        };
    }

    /// <summary>结算面板上的汇总文案：「玄微剑法·起手 2/10（+2）」。</summary>
    public string describe_usage(Godot.Collections.Array entries)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry in entries)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            parts.Add(
                $"{item["name"].AsString()} {item["after"].AsInt32()}/{mastery_max()}"
                    + $"（+{item["gained"].AsInt32()}）");
        }

        return string.Join("、", parts);
    }

    /// <summary>`state.inventory` 取成 `GodotObject`（`null` 也要原样返回，`can_train` 靠它报「没有会话状态」）。</summary>
    private GodotObject _inventory() => _state?.Get("inventory").AsGodotObject();
}
