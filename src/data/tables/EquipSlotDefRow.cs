using System;
using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// equip_slot_def.csv 行：装备槽位定义。
//
// 槽位清单是这张表说了算：equip_base.slot 与 affix_pool.allow_slots 都引用它，
// 代码里不再写死槽位枚举（增删槽位不必改校验器）。
public partial class EquipSlotDefRow : TableRow
{
    [Export]
    public string slot_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 该槽位能同时装备几件（戒指为 2）
    [Export]
    public int max_equip { get; set; } = 1;

    [Export]
    public int sort_order { get; set; }

    // 允许的武器类型，"|" 分隔，引用 weapon_type_def；非武器槽留空
    [Export]
    public string allow_weapon_types { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>允许的武器类型列表（Godot 会把 string[] 桥成 PackedStringArray）。</summary>
    public string[] allowed_weapon_types()
    {
        var parts = allow_weapon_types.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }

    /// <summary>是否武器槽（以「有没有允许的武器类型」判定，避免再写死 slot_id）。</summary>
    public bool is_weapon_slot() => allow_weapon_types.Trim().Length > 0;
}
