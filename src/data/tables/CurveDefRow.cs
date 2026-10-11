using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/curve_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class CurveDefRow : TableRow
{
    // curve_def.csv 行：成长曲线枚举与公式说明（公式本身在 CurveEvaluator 里实现）。
    // linear / diminishing / piecewise
    [Export]
    public string curve_id { get; set; } = "";

    [Export]
    public string formula { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
