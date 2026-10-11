using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/level_growth.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class LevelGrowthRow : TableRow
{
    // level_growth.csv 行：等级成长（经验、基础派生数值、升级得点）。
    [Export]
    public int level { get; set; } = 1;

    [Export]
    public int upgrade_points { get; set; } = 0;

    [Export]
    public int exp_to_next { get; set; } = 0;

    [Export]
    public int base_hp { get; set; } = 0;

    [Export]
    public int base_qi { get; set; } = 0;

    [Export]
    public double base_atk_phys { get; set; } = 0.0;

    [Export]
    public double base_atk_qi { get; set; } = 0.0;

    [Export]
    public double base_def_phys { get; set; } = 0.0;

    [Export]
    public double base_def_qi { get; set; } = 0.0;

    [Export]
    public double base_speed { get; set; } = 0.0;
}
