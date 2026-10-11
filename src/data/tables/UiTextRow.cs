using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/ui_text.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class UiTextRow : TableRow
{
    // ui_text.csv 行：界面文案（按 id 取一句话，不写死在代码里）。
    //
    // 0.10.2 新增（设计 09）：第一条是木桩到顶的提示 `dummy_cap_reached`。
    // 为什么要这张表：这类"玩家可能以为卡住了"的提示以后还会加，写在代码里就得改代码。
    [Export]
    public string text_id { get; set; } = "";

    [Export]
    public string text_cn { get; set; } = "";

    // 给设计/开发看的说明：什么时候用它
    [Export]
    public string note { get; set; } = "";
}
