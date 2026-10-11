using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/event_skill_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class EventSkillDefRow : TableRow
{
    // event_skill_def.csv 行：非战斗技能定义（事件判定用）。
    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 关联属性，引用 attribute_def.attr_id
    [Export]
    public string related_attr { get; set; } = "";

    [Export]
    public int max_level { get; set; } = 1;

    [Export]
    public string desc { get; set; } = "";
}
