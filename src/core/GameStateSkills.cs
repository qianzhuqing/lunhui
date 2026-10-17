using Godot;

namespace Lunhui.Core;

/// <summary>
/// `GameState` 的加点与武学那一半（对位原 `src/core/game_state.gd` 的「加点」「武学熟练度」两节）。
/// 拆出来的理由只有一个：单文件 800 行硬上限（见 `docs/dev/代码拆分清单.md`）。
/// </summary>
public partial class GameState
{
    /// <summary>已加的点数（返回**副本**：调用方改它不会悄悄改到存档，与 GDScript 的 `Dictionary(…)` 同口径）。</summary>
    public Godot.Collections.Dictionary allocations_of(string char_id)
        => char_allocations.ContainsKey(char_id)
            ? char_allocations[char_id].AsGodotDictionary().Duplicate()
            : new Godot.Collections.Dictionary();

    /// <summary>未分配点数 = 1 级到当前等级的 upgrade_points 累计 − 已加的点数。</summary>
    public int available_points(TableDb db, string char_id)
    {
        int total = 0;
        int level_now = Mathf.Max(1, level_of(char_id));
        for (int level = 1; level <= level_now; level++)
        {
            Resource row = db.get_row("level_growth", level);
            if (row != null)
            {
                total += row.Get("upgrade_points").AsInt32();
            }
        }

        int spent = 0;
        Godot.Collections.Dictionary allocations = allocations_of(char_id);
        foreach (Variant attr_id in allocations.Keys)
        {
            spent += allocations[attr_id].AsInt32();
        }

        return Mathf.Max(0, total - spent);
    }

    /// <summary>
    /// 这个属性能不能加点；能加返回空串，不能加返回给玩家看的原因。
    ///
    /// 设计 0.13.0：七维分成**五维（力体敏智运，可加点）与资质（悟性／根骨，创建时定死）**。
    /// 资质只能靠图鉴奖励与特定内功提升——否则「先堆资质再堆输出」永远是唯一解。
    /// 这里是唯一出处：加点入口与角色面板的加号按钮都问它。
    /// </summary>
    public string allocation_block_reason(TableDb db, string attr_id)
    {
        Resource row = db.get_row("attribute_def", attr_id);
        if (row == null)
        {
            return $"没有这个属性：{attr_id}";
        }

        if (row.Get("allocatable").AsInt32() != 1)
        {
            return $"{row.Get("name_cn").AsString()} 是不能加点的资质（只能靠图鉴奖励与内功提升）";
        }

        return "";
    }

    /// <summary>加一点。返回 {ok, error, remaining}。</summary>
    public Godot.Collections.Dictionary spend_point(TableDb db, string char_id, string attr_id)
    {
        string blocked = allocation_block_reason(db, attr_id);
        if (!string.IsNullOrEmpty(blocked))
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", blocked }, { "remaining", available_points(db, char_id) },
            };
        }

        int remaining = available_points(db, char_id);
        if (remaining <= 0)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有可分配的点数" }, { "remaining", 0 } };
        }

        Godot.Collections.Dictionary allocations = allocations_of(char_id);
        allocations[attr_id] = (allocations.ContainsKey(attr_id) ? allocations[attr_id].AsInt32() : 0) + 1;
        char_allocations[char_id] = allocations;
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" }, { "remaining", remaining - 1 } };
    }

    // ------------------------------------------------------------------ 武学熟练度

    /// <summary>熟练度表里出现过的武学也算已学（v3 老档的熟练度是唯一的学习记录）。</summary>
    public void learn_mastered_skills()
    {
        foreach (Variant key in skill_mastery.Keys)
        {
            string char_id = key.AsString();
            foreach (string skill_id in masteries_of(char_id))
            {
                learn_skill(char_id, skill_id);
            }
        }
    }

    public string[] learned_of(string char_id)
    {
        Godot.Collections.Array entries = char_learned.ContainsKey(char_id)
            ? char_learned[char_id].AsGodotArray()
            : new Godot.Collections.Array();
        var out_values = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            out_values[i] = entries[i].AsString();
        }

        System.Array.Sort(out_values);
        return out_values;
    }

    public bool is_learned(string char_id, string skill_id)
        => char_learned.ContainsKey(char_id) && char_learned[char_id].AsGodotArray().Contains(skill_id);

    /// <summary>学会一部武学；返回是否新学（重复学不算失败，掉落会反复遇到同一个来源）。</summary>
    public bool learn_skill(string char_id, string skill_id)
    {
        if (string.IsNullOrEmpty(skill_id) || is_learned(char_id, skill_id))
        {
            return false;
        }

        Godot.Collections.Array learned = char_learned.ContainsKey(char_id)
            ? char_learned[char_id].AsGodotArray()
            : new Godot.Collections.Array();
        learned.Add(skill_id);
        char_learned[char_id] = learned;
        return true;
    }

    /// <summary>图鉴收集数：全队已学武学去重后的部数（图鉴奖励按它算，见 GrowthCalculator.codex_bonus）。</summary>
    public int collected_skill_count()
    {
        var seen = new Godot.Collections.Dictionary();
        foreach (Variant key in char_learned.Keys)
        {
            Godot.Collections.Array entries = char_learned[key].AsGodotArray();
            foreach (Variant skill_id in entries)
            {
                seen[skill_id.AsString()] = true;
            }
        }

        return seen.Count;
    }

    public Godot.Collections.Dictionary loadout_of(string char_id)
    {
        Godot.Collections.Dictionary entry = char_loadouts.ContainsKey(char_id)
            ? char_loadouts[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            { "active", _to_array(entry.ContainsKey("active") ? entry["active"].AsGodotArray() : new Godot.Collections.Array()) },
            { "passive", _to_array(entry.ContainsKey("passive") ? entry["passive"].AsGodotArray() : new Godot.Collections.Array()) },
            // 普攻位（v16 起，设计 04 §武学页的三块玩家配置）：**空串＝照旧用系统普攻**
            // （`BattleSimulator.__basic_attack__` 不落表、也不动）。
            { "basic", entry.ContainsKey("basic") ? entry["basic"].AsString() : "" },
        };
    }

    public void set_loadout(string char_id, string[] active, string[] passive)
    {
        // **普攻位要保住**：装配结构 v16 起多了这一项，旧的"两列表"写法写回时别把它擦掉
        char_loadouts[char_id] = new Godot.Collections.Dictionary
        {
            { "active", _to_array(active) },
            { "passive", _to_array(passive) },
            { "basic", loadout_of(char_id)["basic"].AsString() },
        };
    }

    /// <summary>
    /// 普攻位装哪一招（空串＝照旧用系统普攻）。设计 04 §武学页的三块玩家配置：
    /// 可装的是**已学的免费招**（`skill_active.qi_cost = 0` 且 `cooldown = 0`）；
    /// 装上之后那一招**吃 `extra_hit`、计入招式使用记录、受武器类型限制**，且**与招式位互斥**——
    /// 后面三条的判定在 `SkillLoadout` 那一侧（与招式槽同一处口径）。
    /// </summary>
    public void set_basic_skill(string char_id, string skill_id)
    {
        Godot.Collections.Dictionary entry = char_loadouts.ContainsKey(char_id)
            && char_loadouts[char_id].VariantType == Variant.Type.Dictionary
                ? char_loadouts[char_id].AsGodotDictionary()
                : new Godot.Collections.Dictionary();
        entry["basic"] = skill_id;
        char_loadouts[char_id] = entry;
    }

    public string[] masteries_of(string char_id)
    {
        Godot.Collections.Dictionary entries = skill_mastery.ContainsKey(char_id)
            ? skill_mastery[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        var out_values = new string[entries.Count];
        int index = 0;
        foreach (Variant skill_id in entries.Keys)
        {
            out_values[index] = skill_id.AsString();
            index += 1;
        }

        System.Array.Sort(out_values);
        return out_values;
    }

    public int mastery_of(string char_id, string skill_id)
    {
        Godot.Collections.Dictionary entries = skill_mastery.ContainsKey(char_id)
            ? skill_mastery[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return entries.ContainsKey(skill_id) ? entries[skill_id].AsInt32() : 0;
    }

    /// <summary>
    /// 记下熟练度（成长来源：战斗施放／打坐／秘籍，本轮只提供写入接口）。
    /// **四个参数都要传**（`max_level` 没有默认值，见坑⑲）。
    /// </summary>
    public int set_mastery(string char_id, string skill_id, int level, int max_level)
    {
        Godot.Collections.Dictionary entries = skill_mastery.ContainsKey(char_id)
            ? skill_mastery[char_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        int clamped = Mathf.Clamp(level, 0, Mathf.Max(0, max_level));
        entries[skill_id] = clamped;
        skill_mastery[char_id] = entries;
        return clamped;
    }

    /// <summary>Variant 数组的浅拷贝（GDScript 的 `Array(x)`）。</summary>
    private static Godot.Collections.Array _to_array(Godot.Collections.Array values) => values.Duplicate();

    private static Godot.Collections.Array _to_array(string[] values)
    {
        var out_values = new Godot.Collections.Array();
        foreach (string value in values ?? System.Array.Empty<string>())
        {
            out_values.Add(value);
        }

        return out_values;
    }
}
