using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// skill_active.csv 行：招式明细（战斗数值）。
//
// 每条 skill_kind=active 的武学必须有且只有一行，技能本身在 skill_base。
public partial class SkillActiveRow : TableRow
{
    [Export]
    public string skill_id { get; set; } = "";

    // 系别，引用 element_counter 的 external / internal / odd
    [Export]
    public string element { get; set; } = "";

    // 引用 damage_type.type_id
    [Export]
    public string damage_type { get; set; } = "";

    [Export]
    public double power_ratio { get; set; }

    [Export]
    public int qi_cost { get; set; }

    [Export]
    public int poise_damage { get; set; }

    [Export]
    public int hit_count { get; set; } = 1;

    // single / self / all_enemy
    [Export]
    public string target_type { get; set; } = "single";

    // 附带异常状态，引用 status_effect.status_id
    [Export]
    public string status_id { get; set; } = "";

    [Export]
    public double status_chance_mul { get; set; }

    [Export]
    public int cooldown { get; set; }

    /// <summary>是不是要打伤害的招式（辅助招式倍率为 0）。</summary>
    public bool is_attack() => damage_type.Length > 0 && power_ratio > 0.0;
}
