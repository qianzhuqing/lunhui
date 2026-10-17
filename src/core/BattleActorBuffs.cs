using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `BattleActor` 的 buff／属性重算一档（对位原 `battle_actor.gd` 的「buff（08 增益与套装）」一节）。
///
/// 结算分工（单一实现，别在别处再写一套）：
///   · **常驻** buff（装备 `on_equip`、套装档位，`duration=0`）在构造角色时就进了贡献列表
///     （`BuffService.contributions_of` ← `Inventory` / `CharacterSheet`），这里不需要再叠一次。
///   · **临时** buff（正回合数 / 整场 / 触发类）走这个列表，属性在变化时整份重算。
/// </summary>
public partial class BattleActor
{
    /// <summary>
    /// 构造时交给它「重算属性所需的输入」：等级、基础五维、加点、贡献列表。
    /// 没有调过这个函数的单位（敌人）走快照叠加那条兜底路径。
    /// </summary>
    public void setup_recompute(
        GodotObject db,
        GodotObject calculator,
        int level_value,
        Godot.Collections.Dictionary base_attrs,
        Godot.Collections.Dictionary allocations,
        Godot.Collections.Array contributions)
    {
        _db = db;
        _calculator = calculator as AttributeCalculator;
        level = level_value;
        _base_attrs = base_attrs.Duplicate(true);
        _allocations = allocations.Duplicate(true);
        _base_contributions = contributions.Duplicate(true);
    }

    public BuffService buff_service()
    {
        TableDb table_db = _table_db;
        if (table_db == null)
        {
            return null;
        }
        return _buff_service_cache ??= BuffService.create(table_db);
    }

    public bool has_buff(string buff_id)
    {
        foreach (Variant entry_variant in buffs)
        {
            if (entry_variant.AsGodotDictionary()["buff_id"].AsString() == buff_id)
            {
                return true;
            }
        }
        return false;
    }

    public int buff_stacks(string buff_id)
    {
        int total = 0;
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["buff_id"].AsString() == buff_id)
            {
                total += entry["stacks"].AsInt32();
            }
        }
        return total;
    }

    /// <summary>剩余回合：>0 回合数 / 0 常驻 / -1 整场。</summary>
    public int buff_remaining(string buff_id)
    {
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["buff_id"].AsString() == buff_id)
            {
                return entry["remaining"].AsInt32();
            }
        }
        return 0;
    }

    /// <summary>
    /// 施加一个 buff。叠加规则按 `buff_def.stack_rule`：
    ///   unique 全局唯一（重复无效）／refresh 只刷新剩余回合／stack 叠层到 max_stack。
    /// 返回 {applied, reason, buff_id, name, stacks, refreshed, remaining}
    ///
    /// GDScript 侧按 3／4 个实参调用，故按元数各留一条重载（**别改成默认参数**，
    /// 默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
    /// </summary>
    public Godot.Collections.Dictionary add_buff(string buff_id, string source_type, string source_id) =>
        add_buff(buff_id, source_type, source_id, 1);

    /// <inheritdoc cref="add_buff(string, string, string)"/>
    public Godot.Collections.Dictionary add_buff(
        string buff_id, string source_type, string source_id, int grant_stacks)
    {
        BuffService service = buff_service();
        if (service == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "applied", false },
                { "reason", "这个单位没有接配置表（buff 需要 db）" },
                { "buff_id", buff_id },
            };
        }
        if (!service.exists(buff_id))
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[BattleActor] buff_def 里没有 {buff_id}");
            return new Godot.Collections.Dictionary
            {
                { "applied", false },
                { "reason", "这条增益的配置对不上（数据错，已记进日志）" },
                { "buff_id", buff_id },
            };
        }
        string rule = service.stack_rule_of(buff_id);
        int cap = System.Math.Max(1, service.max_stack_of(buff_id));
        int add = System.Math.Max(1, grant_stacks);
        if (rule == BuffService.stack_unique())
        {
            foreach (Variant existing_variant in buffs)
            {
                Godot.Collections.Dictionary existing = existing_variant.AsGodotDictionary();
                if (existing["buff_id"].AsString() == buff_id)
                {
                    return new Godot.Collections.Dictionary
                    {
                        { "applied", false },
                        { "reason", "unique" },
                        { "buff_id", buff_id },
                        { "name", _buff_name(buff_id) },
                        { "stacks", existing["stacks"].AsInt32() },
                    };
                }
            }
        }
        Godot.Collections.Dictionary entry = _buff_entry(buff_id, source_type, source_id);
        bool is_new = entry.Count == 0;
        if (is_new)
        {
            entry = new Godot.Collections.Dictionary
            {
                { "buff_id", buff_id },
                { "source_type", source_type },
                { "source_id", source_id },
                // 从 0 起：下面按叠加规则统一加（**不能写 1**——叠层类会变成「第一次就给 2 层」，
                // 这个 bug 是 2026-10-03 写「招式附带增益」用例时抓到的，见框架说明决策 220）
                { "remaining", service.duration_of(buff_id) },
                { "stacks", 0 },
            };
            buffs.Add(entry);
        }
        int before = entry["stacks"].AsInt32();
        if (rule == BuffService.stack_stack())
        {
            entry["stacks"] = System.Math.Min(cap, before + add);
        }
        else
        {
            entry["stacks"] = System.Math.Min(cap, System.Math.Max(1, is_new ? add : before));
        }
        int duration = service.duration_of(buff_id);
        if (duration > 0)
        {
            entry["remaining"] = duration;     // refresh：刷新剩余回合
        }
        else if (is_new)
        {
            entry["remaining"] = duration;     // 0 常驻 / -1 整场
        }
        if (service.effect_kind_of(buff_id) == BuffService.effect_kind_special())
        {
            _note_unhandled_buff(buff_id, "特判效果（effect_kind=special）还没有代码实现，只记层数");
        }
        _rebuild_stats();
        return new Godot.Collections.Dictionary
        {
            { "applied", true },
            { "reason", "" },
            { "buff_id", buff_id },
            { "name", _buff_name(buff_id) },
            { "stacks", entry["stacks"].AsInt32() },
            { "refreshed", !is_new },
            { "remaining", entry["remaining"].AsInt32() },
        };
    }

    /// <summary>
    /// 回合末：`battle` 作用域的限时 buff 各 −1，归零即失效；常驻与整场不动。
    /// 返回失效的条目（给战报播报）。
    /// </summary>
    public Godot.Collections.Array tick_buffs()
    {
        var expired = new Godot.Collections.Array();
        var alive = new Godot.Collections.Array();
        bool changed = false;
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            int remaining = entry["remaining"].AsInt32();
            if (remaining > 0)
            {
                entry["remaining"] = remaining - 1;
                changed = true;
                if (entry["remaining"].AsInt32() <= 0)
                {
                    expired.Add(entry.Duplicate(true));
                    continue;
                }
            }
            alive.Add(entry);
        }
        buffs = alive;
        if (changed)
        {
            _rebuild_stats();
        }
        return expired;
    }

    /// <summary>
    /// 战斗结束：清掉所有战斗内 buff（`field` 作用域的**留在存档里**，由会话层管）。
    /// 返回被清掉的 buff_id 列表。
    /// </summary>
    public string[] clear_battle_buffs()
    {
        var cleared = new System.Collections.Generic.List<string>();
        var alive = new Godot.Collections.Array();
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["source_type"].AsString() == "field")
            {
                alive.Add(entry);
                continue;
            }
            cleared.Add(entry["buff_id"].AsString());
        }
        buffs = alive;
        if (cleared.Count > 0)
        {
            _rebuild_stats();
        }
        return cleared.ToArray();
    }

    /// <summary>
    /// 满级签名「百草」：清掉自身 **1 层**减益，返回清了几层（0 或 1）。
    /// 先清异常状态（`status_effect`）再找减益 buff——异常是"更该被解掉"的那一类；
    /// 清的是**层**不是整条（设计写的"清掉 1 层"），所以中毒 3 层会变成 2 层。
    /// </summary>
    public int cleanse_one_debuff()
    {
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            Godot.Collections.Array stacks = entry["stacks"].AsGodotArray();
            if (stacks.Count > 0)
            {
                stacks.RemoveAt(stacks.Count - 1);
                entry["stacks"] = stacks;
                return 1;
            }
        }

        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            var row = _table_db?.get_row("buff_def", entry["buff_id"].AsString()) as BuffDefRow;
            if (row != null && row.is_debuff && entry["stacks"].AsInt32() > 0)
            {
                entry["stacks"] = entry["stacks"].AsInt32() - 1;
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// 身上有没有减益（两个来源都算：`status_effect` 的异常 ＋ 减益的 `buff_def`）。
    /// 内功满级签名「醉意」（有减益时闪避 +15%）与「不还」（每个减益 +8% 伤害）读它。
    /// </summary>
    public bool has_debuff(GodotObject db)
    {
        foreach (Variant entry_variant in statuses)
        {
            if (entry_variant.AsGodotDictionary()["stacks"].AsGodotArray().Count > 0)
            {
                return true;
            }
        }

        return debuff_count(db) > 0;
    }

    /// <summary>
    /// 身上有几条减益（只数**条目**、不数层数——"每个减益 +8%"说的是几条，不是几层）。
    /// `status_effect` 全是减益（08 的口径），`buff_def` 按 `is_debuff` 判。
    /// </summary>
    public int debuff_count(GodotObject db)
    {
        var table_db = db as TableDb;
        int count = 0;
        foreach (Variant entry_variant in statuses)
        {
            if (entry_variant.AsGodotDictionary()["stacks"].AsGodotArray().Count > 0)
            {
                count += 1;
            }
        }

        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["stacks"].AsInt32() <= 0)
            {
                continue;
            }

            string buff_id = entry["buff_id"].AsString();
            var row = table_db?.get_row("buff_def", buff_id) as BuffDefRow;
            if (row != null && row.is_debuff)
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>
    /// 面板用：[{buff_id, entry_id, name, stacks, remaining, permanent, is_debuff, desc, icon, kind}]，
    /// 与 `status_rows()` 同形状（`kind` 区分两套表）——**合并展示**由 `effect_rows()` 负责。
    /// </summary>
    public Godot.Collections.Array buff_rows(GodotObject db)
    {
        BuffService service = buff_service();
        TableDb table_db = db as TableDb;
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string buff_id = entry["buff_id"].AsString();
            BuffDefRow row = table_db?.get_row("buff_def", buff_id) as BuffDefRow;
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "buff_id", buff_id },
                { "entry_id", buff_id },
                { "name", row != null ? row.name_cn : buff_id },
                { "stacks", entry["stacks"].AsInt32() },
                { "remaining", entry["remaining"].AsInt32() },
                { "permanent", entry["remaining"].AsInt32() == 0 },
                { "is_debuff", row != null && row.is_debuff },
                { "desc", row != null ? row.desc : "" },
                { "icon", row != null ? row.icon : "" },
                { "kind", "buff" },
            });
        }
        // 悬浮说明里带上数值修正的中文名，玩家才知道这条 buff 到底给了什么
        foreach (Variant item_variant in out_rows)
        {
            Godot.Collections.Dictionary item = item_variant.AsGodotDictionary();
            Godot.Collections.Array mods = service != null
                ? service.stat_mods_of(item["buff_id"].AsString())
                : new Godot.Collections.Array();
            if (mods.Count > 0)
            {
                item["desc"] = item["desc"].AsString() + "（" + _describe_stat_mods(db, mods) + "）";
            }
        }
        return sort_effect_rows(out_rows);
    }

    /// <summary>
    /// 08 的 UI 要求：`status_effect` 与 `buff_def` **合并成一条列表**，玩家看不出这是两套表。
    /// 排序固定：减益在前，其余按剩余回合升序（快过期的在前），常驻最后。
    /// </summary>
    public Godot.Collections.Array effect_rows(GodotObject db)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in status_rows(db))
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            row["kind"] = "status";
            row["entry_id"] = row["status_id"].AsString();   // 与 buff 行统一一个键，界面不用分两套取
            row["is_debuff"] = true;                          // 异常状态全是减益
            row["permanent"] = false;
            if (!row.ContainsKey("desc"))
            {
                row["desc"] = "";
            }
            out_rows.Add(row);
        }
        foreach (Variant row in buff_rows(db))
        {
            out_rows.Add(row);
        }
        return sort_effect_rows(out_rows);
    }

    /// <summary>
    /// 固定排序（08）：**减益在前**，同极性按剩余回合升序（快过期的在前），常驻／整场排末尾。
    /// 公开给界面用：战斗面板要把**所有单位**的条目合成一条列表再排一次，不能各排各的
    /// （各排各的 = 敌方的减益会被我方增益压在下面）。
    /// </summary>
    public static Godot.Collections.Array sort_effect_rows(Godot.Collections.Array rows)
    {
        var indexed = new System.Collections.Generic.List<(int Index, Godot.Collections.Dictionary Row)>();
        for (int index = 0; index < rows.Count; index++)
        {
            indexed.Add((index, rows[index].AsGodotDictionary()));
        }
        indexed.Sort((a, b) =>
        {
            int da = _row_is_debuff(a.Row) ? 0 : 1;
            int db_ = _row_is_debuff(b.Row) ? 0 : 1;
            if (da != db_)
            {
                return da.CompareTo(db_);
            }
            int ka = _remaining_sort_key(a.Row);
            int kb = _remaining_sort_key(b.Row);
            if (ka != kb)
            {
                return ka.CompareTo(kb);
            }
            return a.Index.CompareTo(b.Index);
        });
        var out_rows = new Godot.Collections.Array();
        foreach ((int _, Godot.Collections.Dictionary row) in indexed)
        {
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>属性重算：把常驻贡献 + 当前 buff 的临时贡献合起来重算一份快照。</summary>
    private void _rebuild_stats()
    {
        int hp_before = hp;
        int qi_before = qi;
        if (_calculator != null)
        {
            stats = _calculator.compute(level, _base_attrs, _allocations, _runtime_contributions(), true);
        }
        else if (_stats_baseline.Count > 0)
        {
            // 敌人：没有基础五维，`attr:` 类修正没法走属性链——直接在快照上叠 `stat:`，
            // 遇到属性点类修正如实记进 `unhandled_buffs`（不静默丢）
            stats = _stats_baseline.Duplicate(true);
            BuffService service = buff_service();
            if (service != null)
            {
                foreach (Variant entry_variant in buffs)
                {
                    Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                    string buff_id = entry["buff_id"].AsString();
                    int stacks = entry["stacks"].AsInt32();
                    foreach (Variant mod_variant in service.stat_mods_of(buff_id))
                    {
                        Godot.Collections.Dictionary mod = mod_variant.AsGodotDictionary();
                        if (mod["kind"].AsString() == "attr")
                        {
                            _note_unhandled_buff(buff_id, "属性点类修正需要属性链，敌人没有基础五维，暂不结算");
                            continue;
                        }
                        string stat_id = mod["target_id"].AsString();
                        double current = stats.ContainsKey(stat_id) ? stats[stat_id].AsDouble() : 0.0;
                        stats[stat_id] = current + mod["value"].AsDouble() * stacks;
                    }
                }
            }
        }
        foreach (string stat_id in EQUIPMENT_ONLY_STATS)
        {
            if (!stats.ContainsKey(stat_id))
            {
                stats[stat_id] = 0.0;
            }
        }
        // 上限变了不补血／不补内力（临时上限不该当治疗），只把超出的部分夹掉
        hp = System.Math.Clamp(hp_before, 0, max_hp());
        qi = System.Math.Clamp(qi_before, 0, max_qi());
    }

    /// <summary>常驻贡献 + buffer 修正（computed 类＝把来源那份贡献再来一遍）。</summary>
    private Godot.Collections.Array _runtime_contributions()
    {
        Godot.Collections.Array out_rows = _base_contributions.Duplicate(true);
        BuffService service = buff_service();
        if (service == null)
        {
            return out_rows;
        }
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string buff_id = entry["buff_id"].AsString();
            int stacks = System.Math.Max(1, entry["stacks"].AsInt32());
            if (service.effect_kind_of(buff_id) == BuffService.effect_kind_computed())
            {
                // 通用运功（06 原文）：该内功的常驻加成临时翻倍——值不写在表里，算出来
                foreach (Variant contribution_variant in _base_contributions)
                {
                    Godot.Collections.Dictionary contribution = contribution_variant.AsGodotDictionary();
                    string source = contribution.ContainsKey("source") ? contribution["source"].AsString() : "";
                    if (source == entry["source_id"].AsString())
                    {
                        out_rows.Add(contribution.Duplicate(true));
                    }
                }
                continue;
            }
            foreach (Variant row in service.contributions_of(buff_id, $"buff:{buff_id}", stacks))
            {
                out_rows.Add(row);
            }
        }
        return out_rows;
    }

    private Godot.Collections.Dictionary _buff_entry(string buff_id, string source_type, string source_id)
    {
        foreach (Variant entry_variant in buffs)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["buff_id"].AsString() == buff_id
                && entry["source_type"].AsString() == source_type
                && entry["source_id"].AsString() == source_id)
            {
                return entry;
            }
        }
        return new Godot.Collections.Dictionary();
    }

    private void _note_unhandled_buff(string buff_id, string reason)
    {
        foreach (Variant entry_variant in unhandled_buffs)
        {
            if (entry_variant.AsGodotDictionary()["buff_id"].AsString() == buff_id)
            {
                return;
            }
        }
        TableDb table_db = _table_db;
        BuffDefRow row = table_db != null ? table_db.get_row("buff_def", buff_id) as BuffDefRow : null;
        unhandled_buffs.Add(new Godot.Collections.Dictionary
        {
            { "buff_id", buff_id },
            { "name", row != null ? row.name_cn : buff_id },
            { "reason", reason },
        });
    }

    private string _buff_name(string buff_id)
    {
        TableDb table_db = _table_db;
        BuffDefRow row = table_db != null ? table_db.get_row("buff_def", buff_id) as BuffDefRow : null;
        return row != null ? row.name_cn : buff_id;
    }

    private static int _remaining_sort_key(Godot.Collections.Dictionary row)
    {
        int remaining = row.ContainsKey("remaining") ? row["remaining"].AsInt32() : 0;
        return remaining > 0 ? remaining : 9999;    // 常驻／整场排在同极性末尾
    }

    private static bool _row_is_debuff(Godot.Collections.Dictionary row) =>
        row.ContainsKey("is_debuff") && row["is_debuff"].AsBool();

    private static string _describe_stat_mods(GodotObject db, Godot.Collections.Array mods)
    {
        TableDb table_db = db as TableDb;
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant mod_variant in mods)
        {
            Godot.Collections.Dictionary mod = mod_variant.AsGodotDictionary();
            string target_id = mod["target_id"].AsString();
            string name = target_id;
            Resource row = mod["kind"].AsString() == "attr"
                ? table_db?.get_row("attribute_def", target_id)
                : table_db?.get_row("stat_def", target_id);
            if (row != null)
            {
                name = row.Get("name_cn").AsString();
            }
            double value = mod["value"].AsDouble();
            string sign_text = value >= 0.0 ? "+" : "";
            string value_text = Mathf.IsEqualApprox(value, System.Math.Round(value))
                ? ((int)value).ToString()
                : value.ToString();
            parts.Add($"{name} {sign_text}{value_text}");
        }
        return string.Join("，", parts);
    }
}
