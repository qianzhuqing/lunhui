using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/character_base_skill.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class CharacterBaseSkillRow : TableRow
{
    // character_base_skill.csv 行：角色的初始非战斗技能等级。
    //
    // 非战斗技能定义在 event_skill_def.csv，事件判定（event_check）用它们做 check_source。
    [Export]
    public string char_id { get; set; } = "";

    // 引用 event_skill_def.skill_id
    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public int level { get; set; } = 0;
}
