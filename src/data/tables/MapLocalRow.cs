using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/map_local.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class MapLocalRow : TableRow
{
    // map_local.csv 行：小地图场景（城镇／副本）。
    [Export]
    public string scene_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // town / dungeon / poi
    [Export]
    public string scene_type { get; set; } = "";

    // 所属大地图节点，引用 map_region.node_id
    [Export]
    public string parent_node { get; set; } = "";

    [Export]
    public int room_count { get; set; } = 0;

    [Export]
    public string level_range { get; set; } = "";

    [Export]
    public bool has_combat { get; set; } = false;

    [Export]
    public bool has_completion { get; set; } = false;

    [Export]
    public bool save_allowed { get; set; } = true;

    [Export]
    public string note { get; set; } = "";
}
