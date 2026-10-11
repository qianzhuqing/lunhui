using System;
using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// event_check.csv 行：事件判定（属性／非战斗技能两套来源）。
//
// check_source 带前缀：`attr:` 用裸属性（不含装备加成），`skill:` 用非战斗技能等级。
public partial class EventCheckRow : TableRow
{
    [Export]
    public string check_id { get; set; } = "";

    // 大地图节点，引用 map_region.node_id，留空表示在小地图里
    [Export]
    public string region_id { get; set; } = "";

    // 小地图场景，引用 map_local.scene_id
    [Export]
    public string scene_id { get; set; } = "";

    // 房间，引用 dungeon_room.room_id
    [Export]
    public string room_id { get; set; } = "";

    [Export]
    public string check_source { get; set; } = "";

    // 判定难度值（裸属性或技能等级要对到这个数）
    [Export]
    public int difficulty { get; set; }

    // hard 失败有代价 / soft 失败只是少拿好处
    [Export]
    public string check_type { get; set; } = "soft";

    // none / item / room / event
    [Export]
    public string reward_type { get; set; } = "none";

    [Export]
    public string reward_id { get; set; } = "";

    [Export]
    public bool once_only { get; set; }

    [Export]
    public string fail_note { get; set; } = "";

    [Export]
    public string clue_source { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    /// <summary>解析 check_source，返回 {"kind": "attr"|"skill"|"", "target_id": String}。</summary>
    public Dictionary parsed_source()
    {
        var separator = check_source.IndexOf(':');
        if (separator < 0)
        {
            return new Dictionary { { "kind", "" }, { "target_id", check_source } };
        }

        return new Dictionary
        {
            { "kind", check_source.Substring(0, separator) },
            { "target_id", check_source.Substring(separator + 1) },
        };
    }

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
