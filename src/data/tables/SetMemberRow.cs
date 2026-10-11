using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/set_member.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class SetMemberRow : TableRow
{
    // set_member.csv 行：套装包含哪些成员（复合主键 set_id + member_id）。
    //
    // member_id 归哪张表由 set_def.set_kind 决定，**只有已装备／已装配的成员才算数**。
    [Export]
    public string set_id { get; set; } = "";

    [Export]
    public string member_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
