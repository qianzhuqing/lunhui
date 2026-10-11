using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/difficulty_config.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class DifficultyConfigRow : TableRow
{
    // difficulty_config.csv 行：难度与敌方数值倍率。
    [Export]
    public string difficulty_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public double enemy_hp_mul { get; set; } = 1.0;

    [Export]
    public double enemy_atk_mul { get; set; } = 1.0;

    [Export]
    public double enemy_def_mul { get; set; } = 1.0;

    [Export]
    public double exp_mul { get; set; } = 1.0;

    [Export]
    public double money_mul { get; set; } = 1.0;

    [Export]
    public string unlock_condition { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
