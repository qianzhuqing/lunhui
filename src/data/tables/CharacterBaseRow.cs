using System;
using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// character_base.csv 行：可操控角色的初始五维与初始武学。
//
// 文档只给了等级成长与属性分层，没写角色初始五维的来源，这张表补上这一层：
// initial_* 是四层公式里的「基础值」，加点与装备在 AttributeCalculator 里叠加。
public partial class CharacterBaseRow : TableRow
{
    [Export]
    public string char_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 刚猛流 / 毒功流 / 刺客流 / 内功续航流，仅作标记与筛选用
    [Export]
    public string role_tag { get; set; } = "";

    // 武器类型，引用 weapon_type_def.weapon_type
    [Export]
    public string weapon_type { get; set; } = "";

    [Export]
    public int initial_str { get; set; }

    [Export]
    public int initial_con { get; set; }

    [Export]
    public int initial_agi { get; set; }

    [Export]
    public int initial_int { get; set; }

    [Export]
    public int initial_luk { get; set; }

    // 0.6.0 新增：悟性（招式槽上限与武学熟练度成长）
    [Export]
    public int initial_wu { get; set; }

    // 0.6.0 新增：根骨（内功容量与内力上限）
    [Export]
    public int initial_gen { get; set; }

    // 初始五维之和，必须等于 initial_* 相加（校验器会查）
    [Export]
    public int attr_total { get; set; }

    [Export]
    public int start_level { get; set; } = 1;

    // 初始武学，多个用 ";" 分隔，引用 skill_base.skill_id
    [Export]
    public string start_skill_ids { get; set; } = "";

    // 初始装备，多个用 ";" 分隔，引用 equip_base.equip_id
    [Export]
    public string start_equip_ids { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>初始五维，键为 attribute_def.attr_id。</summary>
    public Dictionary initial_attrs() => new()
    {
        { "str", initial_str },
        { "con", initial_con },
        { "agi", initial_agi },
        { "int", initial_int },
        { "luk", initial_luk },
        { "wu", initial_wu },
        { "gen", initial_gen },
    };

    /// <summary>初始武学 id 列表（Godot 会把 string[] 桥成 PackedStringArray）。</summary>
    public string[] skill_ids() => SplitSemicolon(start_skill_ids);

    /// <summary>初始五维之和。</summary>
    public int attr_sum() =>
        initial_str + initial_con + initial_agi + initial_int + initial_luk + initial_wu + initial_gen;

    /// <summary>初始装备 id 列表。</summary>
    public string[] equip_ids() => SplitSemicolon(start_equip_ids);

    private static string[] SplitSemicolon(string text)
    {
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }
}
