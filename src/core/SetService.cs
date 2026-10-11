using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 套装效果（C# 版，对位原 `src/core/set_service.gd`；设计 08）。
///
/// 三类套装共用一套规则，区别只在**计数口径**：
///   - `equip`         按当前**穿在身上**的件数
///   - `skill_active`  按当前**装配**的招式数
///   - `skill_passive` 按当前**装配**的内功**占格数之和**（内功本来就是占格制）
///
/// 两条关键规则（设计原文）：
///   ① **只算已装备／已装配的成员**——背包里、学会了但没装的不算，否则玩家会为了凑套装全带上；
///   ② **档位向下兼容、各自独立叠加**——达到 4 件时 2 件档的 buff 也生效。
///
/// `KIND_*` 与 `BuffService` 的常量同一个坑：GDScript 读不到 C# 的 `const`，
/// 所以配了 `kind_equip()` 这类过渡取值口（见 `BuffService` 的类注释）。
/// </summary>
public partial class SetService : RefCounted
{
    public const string KIND_EQUIP = "equip";
    public const string KIND_ACTIVE = "skill_active";
    public const string KIND_PASSIVE = "skill_passive";

    public static string kind_equip() => KIND_EQUIP;
    public static string kind_active() => KIND_ACTIVE;
    public static string kind_passive() => KIND_PASSIVE;

    private readonly TableDb _db;

    public SetService(TableDb table_db)
    {
        _db = table_db;
    }

    /// <summary>GDScript 侧唯一的构造入口。</summary>
    public static SetService create(TableDb table_db) => new(table_db);

    public Godot.Collections.Array all_defs() => _db.rows("set_def");

    public SetDefRow def_of(string set_id) => _db.get_row("set_def", set_id) as SetDefRow;

    public bool exists(string set_id) => def_of(set_id) != null;

    public string kind_of(string set_id) => def_of(set_id)?.set_kind ?? "";

    public string[] members_of(string set_id)
    {
        var out_members = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _db.rows("set_member"))
        {
            var row = entry.AsGodotObject() as SetMemberRow;
            if (row != null && row.set_id == set_id)
            {
                out_members.Add(row.member_id);
            }
        }

        return out_members.ToArray();
    }

    /// <summary>档位表，按 required_count 升序（0 件档在前）。</summary>
    public Godot.Collections.Array bonuses_of(string set_id)
    {
        var entries = new System.Collections.Generic.List<SetBonusRow>();
        foreach (Variant entry in _db.rows("set_bonus"))
        {
            var row = entry.AsGodotObject() as SetBonusRow;
            if (row != null && row.set_id == set_id)
            {
                entries.Add(row);
            }
        }

        entries.Sort((left, right) => left.required_count.CompareTo(right.required_count));
        var out_bonuses = new Godot.Collections.Array();
        foreach (SetBonusRow row in entries)
        {
            out_bonuses.Add(row);
        }

        return out_bonuses;
    }

    /// <summary>
    /// 当前达成几件／几招／几格。
    ///
    /// `inventory` 给已穿戴记录（装备套用），`state` 给装配表（招式／内功套用）；
    /// 这两个都还是 GDScript，所以过渡期收 `GodotObject`。
    /// </summary>
    public int count_for(string set_id, string char_id, GodotObject inventory, GodotObject state)
    {
        string[] members = members_of(set_id);
        if (members.Length == 0)
        {
            return 0;
        }

        string kind = kind_of(set_id);
        Godot.Collections.Dictionary loadout = state != null
            ? state.Call("loadout_of", char_id).AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        switch (kind)
        {
            case KIND_EQUIP:
                if (inventory == null)
                {
                    return 0;
                }

                var equipped = new Godot.Collections.Dictionary();
                Godot.Collections.Dictionary slots =
                    inventory.Call("equipment_slots", _db, char_id).AsGodotDictionary();
                Godot.Collections.Dictionary equipment = inventory.Get("equipment").AsGodotDictionary();
                foreach (Variant slot_id in slots.Keys)
                {
                    foreach (Variant instance_id in slots[slot_id].AsGodotArray())
                    {
                        Godot.Collections.Dictionary entry = equipment.ContainsKey(instance_id.AsString())
                            ? equipment[instance_id.AsString()].AsGodotDictionary()
                            : new Godot.Collections.Dictionary();
                        equipped[entry.ContainsKey("base_id") ? entry["base_id"].AsString() : ""] = true;
                    }
                }

                var worn = 0;
                foreach (string member_id in members)
                {
                    if (equipped.ContainsKey(member_id))
                    {
                        worn++;
                    }
                }

                return worn;
            case KIND_ACTIVE:
                string[] actives = _to_ids(loadout.ContainsKey("active") ? loadout["active"] : default);
                var assembled = 0;
                foreach (string member_id in members)
                {
                    if (System.Array.IndexOf(actives, member_id) >= 0)
                    {
                        assembled++;
                    }
                }

                return assembled;
            case KIND_PASSIVE:
                // 内功套按**占格数之和**：每部成员贡献 `skill_passive.slot_cost` 格
                string[] passives = _to_ids(loadout.ContainsKey("passive") ? loadout["passive"] : default);
                var slots_total = 0;
                foreach (string member_id in members)
                {
                    if (System.Array.IndexOf(passives, member_id) < 0)
                    {
                        continue;
                    }

                    var row = _db.get_row("skill_passive", member_id) as SkillPassiveRow;
                    slots_total += row?.slot_cost ?? 0;
                }

                return slots_total;
        }

        return 0;
    }

    /// <summary>当前生效的档位（`required_count &lt;= count`，**向下兼容**）：返回 `set_bonus` 行数组。</summary>
    public Godot.Collections.Array active_bonuses(string set_id, int count)
    {
        var out_bonuses = new Godot.Collections.Array();
        foreach (Variant entry in bonuses_of(set_id))
        {
            var row = entry.AsGodotObject() as SetBonusRow;
            if (row != null && row.required_count <= count)
            {
                out_bonuses.Add(row);
            }
        }

        return out_bonuses;
    }

    /// <summary>当前生效档位对应的 buff_id（按档位升序）。</summary>
    public string[] active_buff_ids(string set_id, int count)
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in active_bonuses(set_id, count))
        {
            var row = entry.AsGodotObject() as SetBonusRow;
            if (row != null)
            {
                out_ids.Add(row.buff_id);
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>
    /// 装配表里那两个 id 列表的兼容转换：GDScript 侧给的是 `PackedStringArray`，
    /// 但夹具／老档也可能是普通 `Array`（对位旧 `.gd` 里的 `PackedStringArray(x)`）。
    /// </summary>
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
