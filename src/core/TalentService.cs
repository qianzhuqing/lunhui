using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 天赋（C# 版，对位原 `src/core/talent_service.gd`；设计 12，0.16.0）：
/// 出身定底子、天赋定偏好，5 个点内自选。
///
/// 数据全在两张表里：`talent_def`（展示与花费）＋ `talent_effect`（效果）。
/// 效果目标三种前缀：`attr:`（属性点）／`stat:`（派生数值）／`rule:`（规则性效果）。
///
/// 三条设计纪律在代码里的落点：
///   ① 「动七维的天赋必须总和为零」——**构建期**由 `TableValidatorWorld.CheckCreationTables`
///      报警告（0.16.0 的发行数据本身是净 +2／+3，已记 Q58 等设计裁决）；
///   ② 「纯增益只加资源与规则、不加数值」——`rule:` 那几种（起始铜钱、图鉴翻倍…）
///      由**各自的系统**读 `rule_value()`，不混进属性贡献；
///   ③ 「高代价天赋的代价必须写进 desc」——那是文案纪律，代码只保证 `desc` 有值（构建期查）。
///
/// **过渡期的 `state` 收成 `GodotObject`**：`GameState` 还是 GDScript，等它搬过来再收紧类型。
/// 本类全是静态方法，`state` 只用来读 `talent_picks`／`char_ids` 两个字段。
///
/// `picks` 的入参统一收 `string[]`（Godot 会与 GDScript 的 `PackedStringArray` 互桥；旧 `.gd`
/// 里那几处 `Array(raw)` 的兼容写法在 C# 侧由 `_to_ids()` 接住）。
/// </summary>
public partial class TalentService : RefCounted
{
    /// <summary>已选天赋（`state.talent_picks[char_id]`）。</summary>
    public static string[] picked_of(GodotObject state, string char_id)
    {
        if (state == null)
        {
            return System.Array.Empty<string>();
        }

        Variant raw_picks = state.Get("talent_picks");
        if (raw_picks.VariantType != Variant.Type.Dictionary)
        {
            return System.Array.Empty<string>();
        }

        Godot.Collections.Dictionary picks = raw_picks.AsGodotDictionary();
        return picks.ContainsKey(char_id) ? _to_ids(picks[char_id]) : System.Array.Empty<string>();
    }

    public static int cost_of(TableDb db, string[] picks)
    {
        var total = 0;
        if (db == null)
        {
            return total;
        }

        foreach (string talent_id in picks)
        {
            var row = db.get_row("talent_def", talent_id) as TalentDefRow;
            if (row != null)
            {
                total += row.cost;
            }
        }

        return total;
    }

    public static int budget(TableDb db)
    {
        if (db == null)
        {
            return 0;
        }

        foreach (Variant entry in db.rows("growth_const"))
        {
            var row = entry.AsGodotObject() as GrowthConstRow;
            if (row != null && row.const_id == "talent_points")
            {
                return (int)row.value;
            }
        }

        return 0;
    }

    public static int remaining_points(TableDb db, string[] picks) => budget(db) - cost_of(db, picks);

    /// <summary>能不能再选一个（已选过、点数不够、天赋不存在都被拒，并给原因）。</summary>
    public static Godot.Collections.Dictionary can_pick(TableDb db, string[] picks, string talent_id)
    {
        var row = db.get_row("talent_def", talent_id) as TalentDefRow;
        if (row == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", $"没有这个天赋：{talent_id}" } };
        }

        if (System.Array.IndexOf(picks, talent_id) >= 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", $"已经选了「{row.name_cn}」" },
            };
        }

        int remaining = remaining_points(db, picks);
        if (row.cost > remaining)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", $"点数不够：「{row.name_cn}」要 {row.cost} 点，只剩 {remaining} 点" },
            };
        }

        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    /// <summary>选／取消一个天赋（创建界面按一下就走这里）。返回 {ok, picks, error}</summary>
    public static Godot.Collections.Dictionary toggle(TableDb db, string[] picks, string talent_id)
    {
        if (System.Array.IndexOf(picks, talent_id) >= 0)
        {
            var next_off = new System.Collections.Generic.List<string>();
            foreach (string item in picks)
            {
                if (item != talent_id)
                {
                    next_off.Add(item);
                }
            }

            return new Godot.Collections.Dictionary
            {
                { "ok", true }, { "picks", next_off.ToArray() }, { "error", "" },
            };
        }

        Godot.Collections.Dictionary check = can_pick(db, picks, talent_id);
        if (!check["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "picks", picks }, { "error", check["error"] },
            };
        }

        var next = new System.Collections.Generic.List<string>(picks) { talent_id };
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "picks", next.ToArray() }, { "error", "" },
        };
    }

    /// <summary>
    /// 天赋的 `attr:`／`stat:` 效果 → `AttributeCalculator` 的贡献列表。
    ///
    /// 与图鉴奖励、装备、内功走同一条通道（`CharacterSheet.contributions()` 汇总），
    /// 所以「选了什么天赋」会真的进面板与战斗，而不是只写在存档里。
    /// </summary>
    public static Godot.Collections.Array contributions(TableDb db, GodotObject state, string char_id)
    {
        var out_contribs = new Godot.Collections.Array();
        if (db == null || state == null)
        {
            return out_contribs;
        }

        foreach (string talent_id in picked_of(state, char_id))
        {
            foreach (Variant entry in db.rows_where("talent_effect", "talent_id", talent_id))
            {
                var row = entry.AsGodotObject() as TalentEffectRow;
                if (row == null)
                {
                    continue;
                }

                // 对位 GDScript 的 `target.split(":", true, 1)`：保留空段、最多劈两段。
                string[] parts = row.target.Split(':', 2, System.StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    continue;
                }

                string kind = parts[0] switch
                {
                    "attr" => AttributeCalculator.CONTRIB_ATTR_POINT,
                    "stat" => AttributeCalculator.CONTRIB_STAT_FLAT,
                    _ => "",
                };
                if (kind.Length == 0)
                {
                    continue;
                }

                out_contribs.Add(new Godot.Collections.Dictionary
                {
                    { "kind", kind },
                    { "target", parts[1] },
                    { "value", row.value },
                    { "source", $"talent:{talent_id}" },
                });
            }
        }

        return out_contribs;
    }

    /// <summary>
    /// 规则性效果（`rule:`）的取值表：`{rule_id: 合计值}`。
    ///
    /// 谁消费谁读——例如起始铜钱由创建流程读 `start_money`，图鉴翻倍由 `codex_bonus()` 读
    /// `codex_bonus_multiplier`。**不在这里替它们决定语义**，这一层只把表里的数字合并起来。
    /// </summary>
    public static Godot.Collections.Dictionary rules(TableDb db, GodotObject state, string char_id)
    {
        var out_rules = new Godot.Collections.Dictionary();
        if (db == null || state == null)
        {
            return out_rules;
        }

        foreach (string talent_id in picked_of(state, char_id))
        {
            foreach (Variant entry in db.rows_where("talent_effect", "talent_id", talent_id))
            {
                var row = entry.AsGodotObject() as TalentEffectRow;
                if (row == null || !row.target.StartsWith("rule:"))
                {
                    continue;
                }

                string rule_id = row.target.Substring("rule:".Length);
                double before = out_rules.ContainsKey(rule_id) ? out_rules[rule_id].AsDouble() : 0.0;
                out_rules[rule_id] = before + row.value;
            }
        }

        return out_rules;
    }

    public static double rule_value(
        TableDb db, GodotObject state, string char_id, string rule_id, double fallback)
    {
        Godot.Collections.Dictionary values = rules(db, state, char_id);
        return values.ContainsKey(rule_id) ? values[rule_id].AsDouble() : fallback;
    }

    /// <summary>
    /// 队里这条规则的**最大值**：判定门槛、买价这类效果是「队伍级」的——谁带着它就算数
    /// （设计 12 §六：江湖百晓生「判定门槛 −2」，让不想打的人也能推内容）。
    /// 也用来问「队里有没有这条规则」（返回值 &gt; 0）。
    /// </summary>
    public static double party_rule_value(TableDb db, GodotObject state, string rule_id, double fallback)
    {
        double out_value = fallback;
        if (db == null || state == null)
        {
            return out_value;
        }

        foreach (Variant char_id in state.Get("char_ids").AsGodotArray())
        {
            out_value = System.Math.Max(
                out_value, rule_value(db, state, char_id.AsString(), rule_id, fallback));
        }

        return out_value;
    }

    /// <summary>
    /// 判定值上的天赋加成（设计 12 §六 的三条）：
    ///   `rule:event_check_bonus`        = **所有**判定 +N（见多识广 +1）
    ///   `rule:event_check_bonus_&lt;技能&gt;` = 只加这一门（三寸不烂之舌：文学 +2／悬壶：医术 +2）
    /// 与 `CharacterSheet.event_check_value()` 的公式**同一条路**：先加进判定值，再由
    /// `EventCheckService` 与门槛比——不减门槛（那是另一条规则 `event_check_difficulty`）。
    /// </summary>
    public static int check_bonus(TableDb db, GodotObject state, string char_id, string skill_id)
    {
        var out_bonus = (int)rule_value(db, state, char_id, "event_check_bonus", 0.0);
        if (skill_id.Length != 0)
        {
            out_bonus += (int)rule_value(db, state, char_id, $"event_check_bonus_{skill_id}", 0.0);
        }

        return out_bonus;
    }

    /// <summary>展示用的天赋卡（创建界面直接渲染这个）：按 category 分组、按花费排序。</summary>
    public static Godot.Collections.Array cards(TableDb db)
    {
        var out_cards = new Godot.Collections.Array();
        if (db == null)
        {
            return out_cards;
        }

        var entries = new System.Collections.Generic.List<Godot.Collections.Dictionary>();
        foreach (Variant entry in db.rows("talent_def"))
        {
            var row = entry.AsGodotObject() as TalentDefRow;
            if (row == null)
            {
                continue;
            }

            entries.Add(new Godot.Collections.Dictionary
            {
                { "talent_id", row.talent_id },
                { "name_cn", row.name_cn },
                { "category", row.category },
                { "cost", row.cost },
                { "desc", row.desc },
            });
        }

        // 对位 GDScript 的 sort_custom：category → cost → talent_id（排序键齐全，不怕不稳定排序）。
        entries.Sort((left, right) =>
        {
            string left_category = left["category"].AsString();
            string right_category = right["category"].AsString();
            if (left_category != right_category)
            {
                return string.CompareOrdinal(left_category, right_category);
            }

            int left_cost = left["cost"].AsInt32();
            int right_cost = right["cost"].AsInt32();
            if (left_cost != right_cost)
            {
                return left_cost.CompareTo(right_cost);
            }

            return string.CompareOrdinal(left["talent_id"].AsString(), right["talent_id"].AsString());
        });

        foreach (Godot.Collections.Dictionary card in entries)
        {
            out_cards.Add(card);
        }

        return out_cards;
    }

    /// <summary>`picks` 的兼容转换：PackedStringArray 与 Array 都收（旧 `.gd` 里是 `Array(raw)`）。</summary>
    private static string[] _to_ids(Variant raw)
    {
        if (raw.VariantType == Variant.Type.PackedStringArray)
        {
            return raw.AsStringArray();
        }

        if (raw.VariantType == Variant.Type.String)
        {
            return new[] { raw.AsString() };
        }

        if (raw.VariantType != Variant.Type.Array)
        {
            return System.Array.Empty<string>();
        }

        Godot.Collections.Array items = raw.AsGodotArray();
        var out_ids = new string[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            out_ids[index] = items[index].AsString();
        }

        return out_ids;
    }
}
