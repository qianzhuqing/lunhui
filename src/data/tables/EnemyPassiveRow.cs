using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/enemy_passive.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class EnemyPassiveRow : TableRow
{
    // enemy_passive.csv 行：敌人装配的一部内功（0.12.0 新增，见 06 与设计 10 §二）。
    //
    // 设计口径（0.12.0）：敌人与角色在**五个维度**上完全一致——七维、等级、武器类型、招式、装备、内功。
    // 所以敌人的抗性也不再是 `enemy_base` 的特权列：抗性是 `stat_def` 里的通用派生数值，
    // 谁练了对应内功（如五毒心法给毒抗）谁就有——玩家练同一部拿到的也是同一份。
    [Export]
    public string enemy_id { get; set; } = "";

    // 装配的内功（引用 skill_base 里 skill_kind=passive 的行）
    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
