using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/enemy_equip.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class EnemyEquipRow : TableRow
{
    // enemy_equip.csv 行：敌人身上穿的一件装备（0.11.1 新增，见 06 与设计 10 §四）。
    //
    // 设计口径（0.11.1）：**敌人身上穿的，就是能掉的**——掉落不再单配，直接从这张表推
    // （按 `rarity_def.drop_weight` 掷稀有度概率，首杀必掉一件）。
    // 这样「他用着青锋剑却掉柴刀」这类两处定义不同步的问题从结构上消失。
    //
    // 注意：`slot_id` 与 `equip_id` 的合法性与角色那边同一套（`equip_slot_def` / `equip_base`）；
    // 「装备影响敌人属性」那一半要等 0.11.0 第二步（敌人七维模板）落地后一起接（见框架说明）。
    [Export]
    public string enemy_id { get; set; } = "";

    // 装备槽（引用 equip_slot_def.slot_id）
    [Export]
    public string slot_id { get; set; } = "";

    // 穿的那件装备（引用 equip_base.equip_id）
    [Export]
    public string equip_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
