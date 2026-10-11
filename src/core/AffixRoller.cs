using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 装备词条生成（C# 版，对位原 `src/core/affix_roller.gd`；规则出处 05_装备与掉落.md
/// 的「稀有度 → 词条数量」与「词条按槽位限定」）。
///
/// 规则与数值全在表里：
///   `rarity_def.affix_min/affix_max` 决定这件装备能带几条词条；
///   `affix_pool.allow_slots` 决定词条能出现在哪些槽位（武器不该刷出「内力回复」）；
///   `affix_pool.min_rarity` 决定词条最低出在哪一档稀有度上；
///   `affix_pool.weight` 是抽取权重，`value_min/value_max` 是数值区间。
///
/// 存档里存 `{affix_id, target, value_kind, value}`：滚好的数值与目标都冻结在存档里，
/// 设计后来改了词条池也不会把已刷出来的装备改坏（只剩 affix_id 的老档仍按表反查）。
///
/// 贡献 kind 字符串的唯一定义处仍是 `AttributeCalculator`，这里只借常量用（旧 `.gd` 里那两个
/// `attr_kind()`／`stat_kind()` 静态取值口随之删掉——它们只有本文件在用）。
/// </summary>
public partial class AffixRoller : RefCounted
{
    private readonly TableDb _db;
    private readonly RngService _rng;

    public AffixRoller(TableDb db, RngService rng)
    {
        _db = db;
        _rng = rng;
    }

    /// <summary>GDScript 侧的构造入口（旧 `new(db, rng)`）。</summary>
    public static AffixRoller create(TableDb db, RngService rng) => new(db, rng);

    /// <summary>
    /// 稀有度的高低顺序**唯一出处是表**（`rarity_def` 从低到高的行顺序）——设计侧校验器
    /// （`validate_tables.ps1` 的 `$rarityRank`）就是这么算的。以前这里抄了一份常量名单，
    /// 于是设计**加一档稀有度**时：PS1 跟着表走、代码却对新档 `return 0`（＝凡品），
    /// `min_rarity` 的门槛静默失效。见框架说明决策 230。
    /// </summary>
    public int rarity_rank(string rarity_id)
    {
        Godot.Collections.Array rows = _db.rows("rarity_def");
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].AsGodotObject().Get("rarity_id").AsString() == rarity_id)
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>这件装备能带几条词条（取 rarity_def.affix_min~affix_max 之间的随机数）。</summary>
    public int affix_count(string rarity_id)
    {
        var row = _db.get_row("rarity_def", rarity_id) as RarityDefRow;
        if (row == null)
        {
            GD.PushError($"[AffixRoller] rarity_def 缺少 {rarity_id}");
            return 0;
        }

        int low = System.Math.Max(0, row.affix_min);
        int high = System.Math.Max(low, row.affix_max);
        return _rng == null ? low : _rng.randi_range(low, high);
    }

    /// <summary>候选词条：槽位命中且稀有度够高。</summary>
    public Godot.Collections.Array candidates_for(string slot_id, string rarity_id)
    {
        var out_rows = new Godot.Collections.Array();
        int rank = rarity_rank(rarity_id);
        foreach (Variant entry in _db.rows("affix_pool"))
        {
            var row = entry.AsGodotObject() as AffixPoolRow;
            if (row == null)
            {
                continue;
            }

            if (System.Array.IndexOf(row.slots(), slot_id) < 0)
            {
                continue;
            }

            if (rarity_rank(row.min_rarity) > rank)
            {
                continue;
            }

            if (row.weight <= 0)
            {
                continue;
            }

            out_rows.Add(row);
        }

        return out_rows;
    }

    /// <summary>
    /// 掷一件装备的词条。返回 `[{affix_id, target, value_kind, value}]`，按抽取顺序排列。
    /// 候选不够（比如低稀有度 + 冷门槽位）时能出几条出几条，不报错也不补假词条。
    /// </summary>
    public Godot.Collections.Array roll_for(string equip_id)
    {
        var base_row = _db.get_row("equip_base", equip_id) as EquipBaseRow;
        if (base_row == null)
        {
            GD.PushError($"[AffixRoller] equip_base 缺少 {equip_id}");
            return new Godot.Collections.Array();
        }

        Godot.Collections.Array pool = candidates_for(base_row.slot, base_row.rarity);
        int count = affix_count(base_row.rarity);
        var out_rows = new Godot.Collections.Array();
        for (var index = 0; index < System.Math.Min(count, pool.Count); index++)
        {
            Resource picked = _take_weighted(pool);
            if (picked == null)
            {
                break;
            }

            var row = picked as AffixPoolRow;
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "affix_id", row.affix_id },
                { "target", row.target },
                { "value_kind", row.value_kind },
                { "value", _roll_value(row) },
            });
        }

        return out_rows;
    }

    /// <summary>按权重抽一条并从池子里拿掉（同一件装备不重复出同一条词条）。</summary>
    private Resource _take_weighted(Godot.Collections.Array pool)
    {
        var entries = new Godot.Collections.Array();
        foreach (Variant item in pool)
        {
            entries.Add(new Godot.Collections.Dictionary
            {
                { "key", item },
                { "weight", item.AsGodotObject().Get("weight").AsDouble() },
            });
        }

        Variant picked = _rng != null
            ? _rng.pick_weighted(entries)
            : (entries.Count > 0 ? entries[0].AsGodotDictionary()["key"] : default);
        if (picked.VariantType == Variant.Type.Nil)
        {
            return null;
        }

        // 按**对象身份**找下标再移除：别用 Variant 判等（内容相同会判不等，见施工单第 ⑤ 条）。
        GodotObject target = picked.AsGodotObject();
        for (var index = 0; index < pool.Count; index++)
        {
            if (ReferenceEquals(pool[index].AsGodotObject(), target))
            {
                pool.RemoveAt(index);
                break;
            }
        }

        return target as Resource;
    }

    private double _roll_value(AffixPoolRow row)
    {
        double low = row.value_min;
        double high = System.Math.Max(low, row.value_max);
        double value = _rng != null ? _rng.randf_range(low, high) : low;
        if (row.value_kind == AttributeCalculator.CONTRIB_ATTR_POINT)
        {
            return (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
        }

        // 派生数值保留 3 位：暴击率这类本身就是小数（0.02 = 2%）
        return System.Math.Round(value * 1000.0, System.MidpointRounding.AwayFromZero) / 1000.0;
    }

    // ---------------------------------------------------------------- 读档后的查询（静态：Inventory 直接用）

    /// <summary>记录里的目标串（`attr:str` / `stat:atk_phys`），没有就回表里查。</summary>
    public static string target_of(TableDb db, Godot.Collections.Dictionary record)
    {
        string target = record.ContainsKey("target") ? record["target"].AsString() : "";
        if (target.Length != 0)
        {
            return target;
        }

        string affix_id = record.ContainsKey("affix_id") ? record["affix_id"].AsString() : "";
        var row = db.get_row("affix_pool", affix_id) as AffixPoolRow;
        if (row != null)
        {
            return row.target;
        }

        GD.PushError($"[AffixRoller] affix_pool 里没有词条 {affix_id}");
        return "";
    }

    /// <summary>存档里的词条记录 → AttributeCalculator 的贡献列表。</summary>
    public static Godot.Collections.Array contributions_of(TableDb db, Godot.Collections.Array records)
    {
        var out_contribs = new Godot.Collections.Array();
        foreach (Variant record_variant in records)
        {
            Godot.Collections.Dictionary record = record_variant.AsGodotDictionary();
            string target = target_of(db, record);
            if (target.Length == 0)
            {
                continue;
            }

            int separator = target.IndexOf(':');
            string prefix = separator >= 0 ? target.Substring(0, separator) : "";
            string target_id = separator >= 0 ? target.Substring(separator + 1) : target;
            double value = record.ContainsKey("value") ? record["value"].AsDouble() : 0.0;
            if (Mathf.IsEqualApprox(value, 0.0))
            {
                continue;
            }

            string kind = prefix == "attr"
                ? AttributeCalculator.CONTRIB_ATTR_POINT
                : AttributeCalculator.CONTRIB_STAT_FLAT;
            out_contribs.Add(new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "target", target_id },
                {
                    "value",
                    kind == AttributeCalculator.CONTRIB_ATTR_POINT ? (int)value : value
                },
                { "source", record.ContainsKey("affix_id") ? record["affix_id"].AsString() : "" },
            });
        }

        return out_contribs;
    }

    /// <summary>单条词条的显示文案：「外功攻击 +7」「暴击率 +2.0%」。</summary>
    public static string describe(TableDb db, Godot.Collections.Dictionary record)
    {
        string target = target_of(db, record);
        if (target.Length == 0)
        {
            return "未知词条";
        }

        int separator = target.IndexOf(':');
        string prefix = separator >= 0 ? target.Substring(0, separator) : "";
        string target_id = separator >= 0 ? target.Substring(separator + 1) : target;
        double value = record.ContainsKey("value") ? record["value"].AsDouble() : 0.0;
        if (prefix == "attr")
        {
            var attr_row = db.get_row("attribute_def", target_id) as AttributeDefRow;
            string attr_name = attr_row != null ? attr_row.name_cn : target_id;
            return $"{attr_name} +{(int)value}";
        }

        var stat_row = db.get_row("stat_def", target_id) as StatDefRow;
        string stat_name = stat_row != null ? stat_row.name_cn : target_id;
        if (stat_row != null && stat_row.is_percent)
        {
            return $"{stat_name} +{value * 100.0:0.0}%";
        }

        if (stat_row != null && stat_row.show_decimals > 0)
        {
            return $"{stat_name} +{value:0.0}";
        }

        return $"{stat_name} +{(int)System.Math.Round(value, System.MidpointRounding.AwayFromZero)}";
    }

    /// <summary>一件装备的整段词条文案。</summary>
    public static string summarize(TableDb db, Godot.Collections.Array records)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant record_variant in records)
        {
            parts.Add(describe(db, record_variant.AsGodotDictionary()));
        }

        return string.Join("、", parts);
    }
}
