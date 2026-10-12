using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `CharacterSheet` 的**武学那一段**（已学武学列表／内功加成摘要／附带异常写成一句白话）——
/// 从 `CharacterSheet.cs` 挪出来的 partial（2026-10-12）。
///
/// 挪的原因：加了「附加效果写成一句白话」之后那一份顶破了 800 行硬上限
/// （`docs/dev/代码拆分清单.md` 的门限）。**只挪位置、不改行为**，同一批只挪这一个文件。
/// 武学列表是角色面板与战斗指令行都要读的一份数据，单独占一个文件。
/// </summary>
public partial class CharacterSheet
{
    /// <summary>武学列表：**已学**的武学（模板起始 + 存档记录）。
    /// 每行带上星级、熟练度、熟练度倍率、是否已装配、能不能装（不能装时写清原因）；
    /// 招式附战斗数值，内功附占格与加成摘要。界面的装配按钮就用这里的 can_equip／equip_error。</summary>
    public Godot.Collections.Array skill_rows()
    {
        var out_rows = new Godot.Collections.Array();
        if (template == null)
        {
            return out_rows;
        }
        var seen = new System.Collections.Generic.HashSet<string>();
        var ids = new System.Collections.Generic.List<string>();
        foreach (string skill_id in _loadout.learned_ids())
        {
            if (seen.Add(skill_id))
            {
                ids.Add(skill_id);
            }
        }
        if (_state != null)
        {
            foreach (string skill_id in _state.Call("masteries_of", char_id).AsStringArray())
            {
                if (seen.Add(skill_id))
                {
                    ids.Add(skill_id);
                }
            }
        }
        foreach (string skill_id in ids)
        {
            if (_db.get_row("skill_base", skill_id) is not SkillBaseRow row)
            {
                continue;
            }
            var star_row = _growth.star_row(row.star) as SkillStarDefRow;
            Godot.Collections.Dictionary requirement = _loadout.requirement_of(skill_id);
            bool equipped = _has(_loadout.active_ids(), skill_id) || _has(_loadout.passive_ids(), skill_id);
            string equip_error = "";
            if (!equipped)
            {
                Godot.Collections.Dictionary can = _loadout.can_equip(skill_id);
                equip_error = can.ContainsKey("error") ? can["error"].AsString() : "";
            }
            var entry = new Godot.Collections.Dictionary
            {
                { "skill_id", skill_id },
                { "name", row.name_cn },
                { "kind", row.skill_kind },
                { "star", row.star },
                { "star_name", star_row != null ? star_row.name_cn : "" },
                { "star_color", star_row != null ? star_row.color : "#FFFFFF" },
                { "weapon_type", row.weapon_type },
                { "school_id", row.school_id },
                { "mastery", mastery_of(skill_id) },
                { "mastery_max", _growth.mastery_max() },
                { "mastery_multiplier", _growth.mastery_multiplier(row.star, mastery_of(skill_id)) },
                { "learn_req_attr", row.learn_req_attr },
                { "learn_req_value", row.learn_req_value },
                { "learn_req_reason", requirement.ContainsKey("reason") ? requirement["reason"].AsString() : "" },
                { "equipped", equipped },
                { "equip_error", equip_error },
                { "desc", row.desc },
            };
            if (row.is_active())
            {
                if (_db.get_row("skill_active", skill_id) is SkillActiveRow active)
                {
                    entry["element"] = active.element;
                    entry["damage_type"] = active.damage_type;
                    entry["power_ratio"] = active.power_ratio;
                    entry["qi_cost"] = active.qi_cost;
                    entry["poise_damage"] = active.poise_damage;
                    entry["hit_count"] = active.hit_count;
                    entry["target_type"] = active.target_type;
                    // 清单 §二-6「附加效果写成一句白话」：附带的异常状态在这里译一次（id 不上界面）
                    entry["status_note"] = _status_note(active);
                    // 「这招在战斗里能不能用出来」：伤害型看 `is_attack()`（有伤害类型且倍率 > 0）；
                    // 增益招式（例：醉里乾坤·醉步）：没有伤害、但表里配了 `on_cast` 发放 → 战斗里能用，
                    // 施放一次给自己上 buff。判定与战斗侧同一处（`BuffService.has_cast_grant`），别各写一套。
                    bool support = !active.is_attack() && BuffService.create(_db).has_cast_grant(skill_id);
                    entry["battle_usable"] = active.is_attack() || support;
                    if (active.is_attack())
                    {
                        entry["battle_note"] = "";
                    }
                    else if (support)
                    {
                        entry["battle_note"] = "增益招式：施放时给自己上 buff";
                    }
                    else
                    {
                        entry["battle_note"] = "战斗里用不出来（效果未配）";
                    }
                }
            }
            else if (_db.get_row("skill_passive", skill_id) is SkillPassiveRow passive)
            {
                entry["slot_cost"] = passive.slot_cost;
                entry["passive_effect"] = passive.passive_effect;
                entry["bonus_summary"] = passive_bonus_summary(skill_id);
                // 表里填了「特殊效果 id」但**没有定义表、代码里也没有落点**（4 部高星内功都是这样）：
                // 面板要如实说一句，别让玩家以为「装了这部就该有个特别效果，怎么没反应」。
                // 注意**不要把 id 原样显示**（界面不许出现表内英文 id，有文案审计盯着），只说「暂未生效」。
                entry["special_effect_pending"] = passive.passive_effect.Trim().Length > 0;
            }
            out_rows.Add(entry);
        }
        return out_rows;
    }

    /// <summary>内功加成摘要，如「内力上限+30、智+5」</summary>
    public string passive_bonus_summary(string skill_id)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in _db.rows_where("skill_passive_stat", "skill_id", skill_id))
        {
            var row = row_variant.As<SkillPassiveStatRow>();
            Godot.Collections.Dictionary parsed = row.parsed_target();
            string kind = parsed.ContainsKey("kind") ? parsed["kind"].AsString() : "";
            string target_id = parsed.ContainsKey("target_id") ? parsed["target_id"].AsString() : "";
            if (kind == "attr")
            {
                parts.Add($"{attribute_name(target_id)}+{(int)row.value}");
            }
            else if (kind == "stat")
            {
                parts.Add($"{stat_name(target_id)}+{_format_plain_stat(target_id, row.value)}");
            }
        }
        return parts.Count == 0 ? "无加成" : string.Join("、", parts);
    }

    private string _format_plain_stat(string stat_id, double value)
    {
        var row = _db.get_row("stat_def", stat_id) as StatDefRow;
        if (row != null && row.is_percent)
        {
            return $"{value * 100.0:0.0}%";
        }
        if (row != null && row.show_decimals > 0)
        {
            return $"{value:0.0}";
        }
        return ((int)value).ToString();
    }

    /// <summary>
    /// 招式附带的那条异常状态写成一句白话（清单 §二-6「附加效果写成一句白话」）。
    ///
    /// **不显示状态 id**（界面不许出现表内英文 id，有文案审计盯着）；故意**不写概率数字**——
    /// 实际概率是「基础概率 × 命中 × `status_chance_mul` × 异常触发率」，那是战斗侧那条管线的事，
    /// 面板上抄一个算不准的百分比，玩家会拿它跟战斗里的结果对不上。这里只说「有一招附带的异常」。
    /// </summary>
    private string _status_note(SkillActiveRow active)
    {
        if (active.status_id.Length == 0)
        {
            return "";
        }

        var status = _db.get_row("status_effect", active.status_id) as StatusEffectRow;
        string status_name = status != null ? status.name_cn : "";
        if (status_name.Length == 0)
        {
            // 表里引了一个没定义的状态：不编名字、也不把 id 甩出来（构建期校验会点名）
            return "";
        }

        return $"附带异常：{status_name}";
    }
}
