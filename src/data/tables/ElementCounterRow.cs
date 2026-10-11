using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/element_counter.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class ElementCounterRow : TableRow
{
    // element_counter.csv 行：系别相克倍率矩阵。
    // external / internal / odd
    [Export]
    public string element_atk { get; set; } = "";

    [Export]
    public string element_def { get; set; } = "";

    [Export]
    public double multiplier { get; set; } = 1.0;

    [Export]
    public string note { get; set; } = "";
}
