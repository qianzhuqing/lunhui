using System;
using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// hidden_trigger.csv 行：隐藏内容触发条件与奖励。
public partial class HiddenTriggerRow : TableRow
{
    [Export]
    public string trigger_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 引用 map_local.scene_id
    [Export]
    public string scene_id { get; set; } = "";

    // 引用 dungeon_room.room_id
    [Export]
    public string room_id { get; set; } = "";

    // item / space / sequence / behavior / kill_style / completion / carry
    [Export]
    public string trigger_type { get; set; } = "";

    // 需要的道具，引用 item_base.item_id
    [Export]
    public string required_item { get; set; } = "";

    // 条件表达式，由具体 trigger_type 解释（公式不进表，解释在代码里）
    [Export]
    public string required_condition { get; set; } = "";

    // boss / equip / skillbook / room / event
    [Export]
    public string reward_type { get; set; } = "";

    // 奖励 id，随 reward_type 指向敌表／装备表／物品表／房间表；event 为自由文本
    [Export]
    public string reward_id { get; set; } = "";

    [Export]
    public bool once_only { get; set; }

    // 线索来源，多个用 ";" 分隔
    [Export]
    public string clue_source { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    /// <summary>线索来源列表（Godot 会把 string[] 桥成 PackedStringArray）。</summary>
    public string[] clues()
    {
        var parts = clue_source.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }
}
