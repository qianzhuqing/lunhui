using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/map_region.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class MapRegionRow : TableRow
{
    // map_region.csv 行：大地图节点（地标／兴趣点／驿站）。
    [Export]
    public string node_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // town / fast_travel / wild / dungeon / poi
    [Export]
    public string node_type { get; set; } = "";

    [Export]
    public string parent_region { get; set; } = "";

    [Export]
    public double pos_x { get; set; } = 0.0;

    [Export]
    public double pos_y { get; set; } = 0.0;

    [Export]
    public string icon { get; set; } = "";

    [Export]
    public string unlock_condition { get; set; } = "";

    [Export]
    public bool reveal_on_map { get; set; } = false;

    // 进入的小地图，引用 map_local.scene_id
    [Export]
    public string enter_scene { get; set; } = "";

    [Export]
    public string respawn_group { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    // **大区中文名**（本章＝「江南道·东部」）：大地图 HUD 左上角**常驻**显示的那一级名字。
    // 以前它只作为代码里的兜底字符串存在（`overworld_controller._region_name()` 的 `else`），
    // 违反「文案在表里」——设计 11 §三／14 §六 定的（0.31.1）。
    [Export]
    public string region_name_cn { get; set; } = "";

    // **指路牌文案**（设计 0.31.2）：空 = 不摆；非空时地编要在那张图上摆 `Markers/Sign_<node_id>` 位点，
    // 代码把这句话摆在牌子旁边（**文案只在表里**——不许在代码里拼「→ 多少里」这类句子）。
    [Export]
    public string signpost_cn { get; set; } = "";
}
