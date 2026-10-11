using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/roaming_spawn.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class RoamingSpawnRow : TableRow
{
    // roaming_spawn.csv 行：明雷刷新点。
    [Export]
    public string spawn_id { get; set; } = "";

    // 引用 map_region.node_id
    [Export]
    public string region_id { get; set; } = "";

    // 引用 enemy_team.team_id
    [Export]
    public string team_id { get; set; } = "";

    // idle / patrol / wander / chase / sleep
    [Export]
    public string behavior { get; set; } = "idle";

    [Export]
    public string patrol_path_id { get; set; } = "";

    [Export]
    public double alert_radius { get; set; } = 0.0;

    [Export]
    public double chase_speed { get; set; } = 0.0;

    [Export]
    public int respawn_sec { get; set; } = 0;

    [Export]
    public bool is_elite { get; set; } = false;

    [Export]
    public string elite_marker { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
