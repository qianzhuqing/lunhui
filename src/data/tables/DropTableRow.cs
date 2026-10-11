using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// drop_table.csv 行：掉落表，每个掉落槽独立判定。
public partial class DropTableRow : TableRow
{
    [Export]
    public string drop_row_id { get; set; } = "";

    // 掉落组，敌人通过 enemy_base.drop_group 引用
    [Export]
    public string drop_group { get; set; } = "";

    [Export]
    public int slot { get; set; }

    // 物品或装备 id，按 item_type 决定查 item_base 还是 equip_base
    [Export]
    public string item_id { get; set; } = "";

    // money / material / consumable / equip / key / tool / skillbook
    [Export]
    public string item_type { get; set; } = "";

    [Export]
    public int qty_min { get; set; } = 1;

    [Export]
    public int qty_max { get; set; } = 1;

    // 基础掉率，1.0 表示必掉
    [Export]
    public double base_rate { get; set; }

    // independent 独立判定 / exclusive 互斥判定
    [Export]
    public string roll_type { get; set; } = "independent";

    // 保底击杀次数，0 表示无保底
    [Export]
    public int pity_count { get; set; }

    [Export]
    public bool difficulty_scaled { get; set; }

    [Export]
    public bool first_kill_only { get; set; }

    [Export]
    public string note { get; set; } = "";

    public bool is_guaranteed() => base_rate >= 1.0;

    public bool has_pity() => pity_count > 0;
}
