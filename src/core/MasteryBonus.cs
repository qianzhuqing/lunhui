using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 内功满级签名（设计 `08_增益与套装.md` §内功满级签名，2026-10-13 拍板）的**查询口 ＋ 接线名单**。
///
/// 一条签名 = 「这部内功练到满级（熟练 10）时给什么」，装在身上才生效、卸下立刻失效。
/// 两类落法：
///   • `stat:<stat_id>` → 走贡献通道（`GrowthCalculator.passive_contributions()` 里并进内功自己的加成）
///   • `effect:<标记>`  → 机制类，战斗各结算点按标记判断
///
/// **接线名单为什么要有**：这张表 30 行，而实现的批次是设计定的（甲＋乙 24 条先落、丙那 6 条
/// 等第一章试玩后再排）。名单是**唯一**说"这条现在到底生不生效"的地方——面板照它写「还没接」，
/// 用例照它查"表里的标记有没有漏进任何一条名单"（否则一条签名会**静静地不生效**，
/// 而玩家只会觉得"练满了怎么没变化"）。
/// </summary>
public static class MasteryBonus
{
    /// <summary>已经接线的 `effect:` 标记（战斗里真读它）。**是数据不是接口**，用例直接读它核名单。</summary>
    public static readonly string[] WIRED_MARKERS =
    {
        // 甲 · 「规则表里已有」那一档：内力回复减半那条规则里加一个 bypass
        "ignore_internal_qi",
        // 乙 · 伤害结算那一簇（受击／命中／暴击／穿透／破绽／连招／闪避，`DamageResolver` 与
        // `BattleSimulatorSettle`）
        "dmg_reduction_low_hp", "no_break_window", "pen_vs_full", "crit_vs_afflicted",
        "dodge_when_debuffed", "dmg_per_debuff", "same_element_chain", "first_dodge",
        "parry_double",
        // 乙 · 资源与回合那一簇（回气／退气／清减益）＋ 异常那一簇（挂层／抗性／层数上限）
        "first_move_free", "low_qi_regen_double", "hold_qi_double", "low_hp_regen_double",
        "qi_refund", "cleanse_one", "basic_poison", "poison_def_down", "pierce_res",
        "venom_burst",
        // 甲 · 规矩表里已有的那一条：受击时架势掉落减半（`take_poise_damage()`）
        "poise_guard",
    };

    /// <summary>本批（甲＋乙）还要接的标记——**B2 段接完这里就空了**，空即全部接完。</summary>
    public static readonly string[] QUEUED_MARKERS =
    {
        // 甲＋乙 24 条**已全部接线**（2026-10-13 B1／B2a／B2b 三段落）。
        // 这里空着是正常的：剩下没接的只有下面「等第一章试玩后再排」那 6 条丙。
    };

    /// <summary>设计明写「等第一章试玩后再排」的 6 条（要新结算点或状态机）。</summary>
    public static readonly string[] AFTER_PLAYTEST_MARKERS =
    {
        "block_counter", "initiative_push", "item_free_action", "once_survive",
        "thorns", "poison_retaliate",
    };

    /// <summary>这个标记接线了没有（战斗真读它）。</summary>
    public static bool wired(string marker) =>
        System.Array.IndexOf(WIRED_MARKERS, marker) >= 0;

    /// <summary>这个标记在不在任何一条名单里（不在＝表里配了个谁都不认的标记）。</summary>
    public static bool known(string marker) =>
        System.Array.IndexOf(WIRED_MARKERS, marker) >= 0
        || System.Array.IndexOf(QUEUED_MARKERS, marker) >= 0
        || System.Array.IndexOf(AFTER_PLAYTEST_MARKERS, marker) >= 0;

    /// <summary>满级熟练度（`growth_const.mastery_max`，缺行退回 10）——校验器与面板同一处口径。</summary>
    public static int mastery_max(TableDb db)
    {
        Resource row = db != null ? db.get_row("growth_const", "mastery_max") : null;
        return row != null ? System.Math.Max(1, (int)row.Get("value").AsDouble()) : 10;
    }

    /// <summary>这一部内功在**给定熟练度**下够到的签名（按表顺序；够不到的不返回）。</summary>
    public static Godot.Collections.Array rows_for(TableDb db, string skill_id, int mastery)
    {
        var out_rows = new Godot.Collections.Array();
        if (db == null || skill_id.Length == 0)
        {
            return out_rows;
        }

        foreach (Variant row_variant in db.rows_where("skill_mastery_bonus", "skill_id", skill_id))
        {
            var row = row_variant.AsGodotObject() as SkillMasteryBonusRow;
            if (row != null && mastery >= row.mastery)
            {
                out_rows.Add(row);
            }
        }

        return out_rows;
    }

    /// <summary>
    /// `stat:` 类签名当前生效的加成（`{stat_id: value}`）——**同一条数值多来源取最高、不叠加**
    /// （设计 08 §内功满级签名规则 3）。规则只在签名之间成立：装备／套装那些 `stat_flat`
    /// 该叠还是叠（那条是属性管线的既有口径，别在这儿顺手改）。
    /// </summary>
    public static Godot.Collections.Dictionary stat_bonuses(TableDb db, string skill_id, int mastery)
    {
        var out_map = new Godot.Collections.Dictionary();
        foreach (Variant row_variant in rows_for(db, skill_id, mastery))
        {
            var row = (SkillMasteryBonusRow)row_variant.AsGodotObject();
            Godot.Collections.Dictionary parsed = row.parsed_target();
            if (parsed["kind"].AsString() != "stat")
            {
                continue;
            }

            string stat_id = parsed["target_id"].AsString();
            if (stat_id.Length == 0)
            {
                continue;
            }

            double best = out_map.ContainsKey(stat_id) ? out_map[stat_id].AsDouble() : 0.0;
            if (!out_map.ContainsKey(stat_id) || row.value > best)
            {
                out_map[stat_id] = row.value;
            }
        }

        return out_map;
    }

    /// <summary>
    /// 武学页那一行要的字：`熟练 10/10 · 满级效果：招式段数 +1`；没满级写还差几级；
    /// 满级了但代码还没接的，**如实写「还没接」**（与内功 `passive_effect` 那套同一口径：
    /// 宁可说清楚，也别让玩家以为"练满了没变化"）。
    /// </summary>
    public static Godot.Collections.Dictionary describe(TableDb db, string skill_id, int mastery)
    {
        int max = mastery_max(db);
        Godot.Collections.Array rows = rows_for(db, skill_id, mastery);
        string note = rows.Count > 0 ? rows[0].As<SkillMasteryBonusRow>().note : "";
        bool pending = false;
        if (rows.Count > 0)
        {
            var row = rows[0].As<SkillMasteryBonusRow>();
            Godot.Collections.Dictionary parsed = row.parsed_target();
            if (parsed["kind"].AsString() == "effect")
            {
                pending = !wired(parsed["target_id"].AsString());
            }
        }

        string text;
        if (rows.Count == 0)
        {
            int left = System.Math.Max(1, max - mastery);
            text = $"熟练 {mastery}/{max} · 再练 {left} 级解锁满级签名";
        }
        else
        {
            text = $"熟练 {mastery}/{max} · 满级效果：{note}"
                + (pending ? "（满级签名还没接）" : "");
        }

        return new Godot.Collections.Dictionary
        {
            { "text", text },
            { "pending", pending },
            { "note", note },
        };
    }
}
