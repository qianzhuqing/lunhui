using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/set_bonus.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class SetBonusRow : TableRow
{
    // set_bonus.csv 行：套装的件数档位 → buff（复合主键 set_id + required_count）。
    //
    // 档位**向下兼容、各自独立叠加**：达到 4 件时 2 件档的 buff 也生效。
    [Export]
    public string set_id { get; set; } = "";

    [Export]
    public int required_count { get; set; } = 0;

    [Export]
    public string buff_id { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
