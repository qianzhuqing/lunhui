using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/skill_passive.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class SkillPassiveRow : TableRow
{
    // skill_passive.csv 行：内功明细。
    //
    // 内功不互斥，改为**占格制**：每部按星级占 1~3 格，装上的总格数不超过 passive_capacity。
    [Export]
    public string skill_id { get; set; } = "";

    // 占格数，1~3（低级 ★1–2 占 1、高级 ★3–4 占 2、顶级 ★5 占 3）
    [Export]
    public int slot_cost { get; set; } = 1;

    // 催动（运功）一次的内力消耗：按占格 ×6（设计 08 参数口径 Q87）
    [Export]
    public int qi_cost { get; set; } = 0;

    // 特殊被动效果 id，空为无
    [Export]
    public string passive_effect { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
