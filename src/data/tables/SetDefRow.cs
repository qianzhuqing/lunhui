using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/set_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class SetDefRow : TableRow
{
    // set_def.csv 行：套装本体。见 docs/design/08_增益与套装.md。
    [Export]
    public string set_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // equip（按穿在身上的件数）／skill_active（按装配的招数）／skill_passive（按装配的占格数之和）
    [Export]
    public string set_kind { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
