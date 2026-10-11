using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `BattleActor` 的异常状态一档（对位原 `battle_actor.gd` 的「异常状态」一节）。
/// 拆文件的理由同主壳：一个文件只担一类职责。
/// </summary>
public partial class BattleActor
{
    /// <summary>
    /// 代码目前认得的 `status_effect.extra_rule`（其余值一律记进 `unhandled_rules` 报出去）。
    /// 静默忽略等于「配了但没生效」：`bleed` 表里写着 `rule_bleed_move`（流血换位），
    /// 而它依赖站位、设计还没定规则——那就必须像奇袭的「无防备」一样明说，不能当没看见。
    /// </summary>
    public static readonly string[] KNOWN_STATUS_RULES =
        { "", "rule_burn_def_down", "rule_internal_qi" };

    /// <summary>
    /// 设计已经声明、但代码还没实现的规则：**允许留在表里**（构建期不报错），
    /// 运行期会走 `unhandled_rules` → 战斗结算的「暂缓规则」播出来。
    /// 加进来必须写清为什么暂时不做；实现之后要从这里删掉，并把它挪进 KNOWN_STATUS_RULES。
    /// </summary>
    public static readonly Godot.Collections.Dictionary DECLARED_STATUS_RULES = new()
    {
        {
            "rule_bleed_move",
            "设计 04 的「流血换位额外触发」依赖前/后排站位，站位规则还没定（见对照表 C 段）"
        },
    };

    /// <summary>
    /// 上一层状态（同源同类叠层，超出上限只刷新时长）。返回 {stacks, refreshed, damage}
    ///
    /// GDScript 侧按 5／6／7 个实参调用，故按元数各留一条重载（**别改成默认参数**，
    /// 默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
    /// </summary>
    public Godot.Collections.Dictionary add_status(
        string status_id, string source_id, double damage_per_stack, int max_stack, int duration) =>
        add_status(status_id, source_id, damage_per_stack, max_stack, duration, "", true);

    /// <inheritdoc cref="add_status(string, string, double, int, int)"/>
    public Godot.Collections.Dictionary add_status(
        string status_id, string source_id, double damage_per_stack, int max_stack, int duration,
        string rule) =>
        add_status(status_id, source_id, damage_per_stack, max_stack, duration, rule, true);

    /// <inheritdoc cref="add_status(string, string, double, int, int)"/>
    public Godot.Collections.Dictionary add_status(
        string status_id,
        string source_id,
        double damage_per_stack,
        int max_stack,
        int duration,
        string rule,
        bool dispellable)
    {
        if (System.Array.IndexOf(KNOWN_STATUS_RULES, rule) < 0
            && System.Array.IndexOf(unhandled_rules, rule) < 0)
        {
            unhandled_rules = _appended(unhandled_rules, rule);
        }
        Godot.Collections.Dictionary entry = _status_entry(status_id, source_id);
        if (entry.Count == 0)
        {
            entry = new Godot.Collections.Dictionary
            {
                { "status_id", status_id },
                { "source_id", source_id },
                { "rule", rule },
                { "dispellable", dispellable },
                { "stacks", new Godot.Collections.Array() },
                { "remaining", 0 },
            };
            statuses.Add(entry);
        }
        Godot.Collections.Array stacks = entry["stacks"].AsGodotArray();
        bool refreshed = stacks.Count >= System.Math.Max(1, max_stack);
        if (!refreshed)
        {
            stacks.Add(damage_per_stack);
        }
        entry["stacks"] = stacks;
        entry["remaining"] = System.Math.Max(entry["remaining"].AsInt32(), System.Math.Max(1, duration));
        return new Godot.Collections.Dictionary
        {
            { "stacks", stacks.Count },
            { "refreshed", refreshed },
            { "damage", damage_per_stack },
        };
    }

    public int status_stacks(string status_id)
    {
        int total = 0;
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["status_id"].AsString() == status_id)
            {
                total += entry["stacks"].AsGodotArray().Count;
            }
        }
        return total;
    }

    public bool has_status(string status_id) => status_stacks(status_id) > 0;

    /// <summary>
    /// 回合末结算：每层按锁定的伤害跳一次，持续回合 -1，到 0 的整条清掉。
    /// 返回 [{status_id, source_id, stacks, damage, expired}]
    /// </summary>
    public Godot.Collections.Array tick_statuses()
    {
        var out_rows = new Godot.Collections.Array();
        var alive = new Godot.Collections.Array();
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            entry["remaining"] = entry["remaining"].AsInt32() - 1;
            Godot.Collections.Array stacks = entry["stacks"].AsGodotArray();
            int damage = 0;
            foreach (Variant value in stacks)
            {
                damage += (int)System.Math.Floor(value.AsDouble());
            }
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "status_id", entry["status_id"].AsString() },
                { "source_id", entry["source_id"].AsString() },
                { "stacks", stacks.Count },
                { "damage", damage },
                { "expired", entry["remaining"].AsInt32() <= 0 },
            });
            if (entry["remaining"].AsInt32() > 0)
            {
                alive.Add(entry);
            }
        }
        statuses = alive;
        return out_rows;
    }

    /// <summary>斩杀：把剩下的层数立刻各结算一次并清空（combat_const.dot_execute_threshold 触发）。</summary>
    public Godot.Collections.Array consume_all_statuses()
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            Godot.Collections.Array stacks = entry["stacks"].AsGodotArray();
            int damage = 0;
            foreach (Variant value in stacks)
            {
                damage += (int)System.Math.Floor(value.AsDouble());
            }
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "status_id", entry["status_id"].AsString() },
                { "source_id", entry["source_id"].AsString() },
                { "stacks", stacks.Count },
                { "damage", damage },
                { "expired", true },
            });
        }
        statuses = new Godot.Collections.Array();
        return out_rows;
    }

    /// <summary>可驱散的清掉（内伤 dispellable=0，驱散不掉）。</summary>
    public string[] clear_dispellable()
    {
        var cleared = new System.Collections.Generic.List<string>();
        var alive = new Godot.Collections.Array();
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            bool dispellable = !entry.ContainsKey("dispellable") || entry["dispellable"].AsBool();
            if (!dispellable)
            {
                alive.Add(entry);
            }
            else
            {
                cleared.Add(entry["status_id"].AsString());
            }
        }
        statuses = alive;
        return cleared.ToArray();
    }

    /// <summary>面板／战斗卡片用：[{status_id, name, stacks, remaining, color, icon}]。</summary>
    public Godot.Collections.Array status_rows(GodotObject db)
    {
        TableDb table_db = db as TableDb;
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string status_id = entry["status_id"].AsString();
            StatusEffectRow row = table_db?.get_row("status_effect", status_id) as StatusEffectRow;
            DamageTypeRow type_row = row != null
                ? table_db.get_row("damage_type", row.damage_type) as DamageTypeRow
                : null;
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "status_id", status_id },
                { "name", row != null ? row.name_cn : status_id },
                { "stacks", entry["stacks"].AsGodotArray().Count },
                { "remaining", entry["remaining"].AsInt32() },
                { "color", type_row != null ? type_row.display_color : "#FFFFFF" },
                // 图标**交给界面按表拼路径**（这里只递数据，不碰 UI 路径——core 不依赖界面）
                { "icon", row != null ? row.icon : "" },
            });
        }
        return out_rows;
    }

    /// <summary>防御乘数：灼伤每层削外功防御（比例取自 `status_effect.burn_def_cut`；只影响 def_phys）。</summary>
    public double defense_multiplier(string stat_id)
    {
        if (stat_id != "def_phys")
        {
            return 1.0;
        }
        double cut = 0.0;
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string rule = entry.ContainsKey("rule") ? entry["rule"].AsString() : "";
            if (rule != "rule_burn_def_down")
            {
                continue;
            }
            double per = StatusRules.rate(_table_db, entry, "burn_def_cut", BURN_DEF_DOWN_PER_STACK);
            cut += per * entry["stacks"].AsGodotArray().Count;
        }
        return cut > 0.0 ? System.Math.Max(0.0, 1.0 - cut) : 1.0;
    }

    /// <summary>内力回复乘数：内伤减半（乘数取自 `status_effect.qi_regen_mul`）。</summary>
    public double qi_regen_factor()
    {
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string rule = entry.ContainsKey("rule") ? entry["rule"].AsString() : "";
            if (rule == "rule_internal_qi")
            {
                return StatusRules.rate(_table_db, entry, "qi_regen_mul", INTERNAL_QI_REGEN_FACTOR);
            }
        }
        return 1.0;
    }

    private Godot.Collections.Dictionary _status_entry(string status_id, string source_id)
    {
        foreach (Variant entry_variant in statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["status_id"].AsString() == status_id && entry["source_id"].AsString() == source_id)
            {
                return entry;
            }
        }
        return new Godot.Collections.Dictionary();
    }

    /// <summary>
    /// `PackedStringArray`（C# 侧 `string[]`）是值语义：追加要整份换掉属性，
    /// 不能指望调用方拿到的那份会跟着变（原 `.gd` 的 `unhandled_rules.append` 对位这里）。
    /// </summary>
    private static string[] _appended(string[] source, string value)
    {
        var list = new System.Collections.Generic.List<string>(source ?? System.Array.Empty<string>())
        {
            value,
        };
        return list.ToArray();
    }
}
