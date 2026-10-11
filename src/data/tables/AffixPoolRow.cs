using System;
using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// affix_pool.csv 行：装备随机词条池。
//
// target 带前缀："attr:" 加属性点，"stat:" 加派生数值；value_kind：attr_point / flat / rate。
public partial class AffixPoolRow : TableRow
{
    [Export]
    public string affix_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public string target { get; set; } = "";

    [Export]
    public string value_kind { get; set; } = "flat";

    [Export]
    public double value_min { get; set; }

    [Export]
    public double value_max { get; set; }

    // 允许的槽位，用 "|" 分隔
    [Export]
    public string allow_slots { get; set; } = "";

    // 引用 rarity_def.rarity_id
    [Export]
    public string min_rarity { get; set; } = "common";

    [Export]
    public int weight { get; set; }

    [Export]
    public string desc { get; set; } = "";

    /// <summary>解析 target 前缀，返回 {"kind": "attr"|"stat", "target_id": "..."}。</summary>
    public Dictionary parsed_target()
    {
        var separator = target.IndexOf(':');
        if (separator < 0)
        {
            return new Dictionary { { "kind", "" }, { "target_id", target } };
        }

        return new Dictionary
        {
            { "kind", target.Substring(0, separator) },
            { "target_id", target.Substring(separator + 1) },
        };
    }

    /// <summary>允许槽位列表（Godot 会把 string[] 桥成 PackedStringArray）。</summary>
    public string[] slots()
    {
        var parts = allow_slots.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }
}
