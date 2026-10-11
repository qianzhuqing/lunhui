using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/status_effect.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class StatusEffectRow : TableRow
{
    // status_effect.csv 行：异常状态（层数、持续、特殊规则）。
    //
    // 触发率统一归运，强度统一归智（trigger_stat / power_stat），见 docs/design/04_战斗与伤害.md。
    [Export]
    public string status_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 关联伤害类型，引用 damage_type.type_id
    [Export]
    public string damage_type { get; set; } = "";

    [Export]
    public string trigger_stat { get; set; } = "";

    [Export]
    public string power_stat { get; set; } = "";

    [Export]
    public double base_chance { get; set; } = 0.0;

    [Export]
    public int max_stack { get; set; } = 1;

    [Export]
    public int duration { get; set; } = 0;

    [Export]
    public bool dispellable { get; set; } = false;

    [Export]
    public string extra_rule { get; set; } = "";

    // 每层削减外功防御的比例（灼伤用；0 = 不削防）
    [Export]
    public double burn_def_cut { get; set; } = 0.0;

    // 内力回复乘数（内伤用；1 = 不打折）
    [Export]
    public double qi_regen_mul { get; set; } = 1.0;

    [Export]
    public string icon { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
