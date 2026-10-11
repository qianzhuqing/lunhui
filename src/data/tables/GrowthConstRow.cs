using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/growth_const.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class GrowthConstRow : TableRow
{
    // growth_const.csv 行：成长系统常数（槽位上限、熟练度、图鉴奖励）。
    //
    // 算法在代码里（src/core/GrowthCalculator.cs），常数在这里——与伤害管线同一套处理方式。
    [Export]
    public string const_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public double value { get; set; } = 0.0;

    [Export]
    public string desc { get; set; } = "";
}
