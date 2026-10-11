using System;
using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// dungeon_room.csv 行：副本房间流程与出口。
public partial class DungeonRoomRow : TableRow
{
    [Export]
    public string room_id { get; set; } = "";

    // 引用 map_local.scene_id
    [Export]
    public string scene_id { get; set; } = "";

    [Export]
    public int floor { get; set; } = 1;

    [Export]
    public string room_name { get; set; } = "";

    // entrance / battle / treasure / trap / secret / elite / story / boss
    [Export]
    public string room_type { get; set; } = "";

    // main / side / hidden
    [Export]
    public string branch_group { get; set; } = "main";

    // 引用 enemy_team.team_id
    [Export]
    public string enemy_team { get; set; } = "";

    // 引用 drop_table.drop_group
    [Export]
    public string chest_id { get; set; } = "";

    // 出口房间，"|" 分隔，引用 dungeon_room.room_id
    [Export]
    public string exit_rooms { get; set; } = "";

    // 引用 hidden_trigger.trigger_id
    [Export]
    public string hidden_trigger { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    /// <summary>出口房间 id 列表（Godot 会把 string[] 桥成 PackedStringArray）。</summary>
    public string[] exits()
    {
        var parts = exit_rooms.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }
}
