using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// damage_type.csv 行：伤害类型（结算方式、能否暴击/闪避、吃哪种防御）。
public partial class DamageTypeRow : TableRow
{
    [Export]
    public string type_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // instant 直伤 / dot 持续
    [Export]
    public string category { get; set; } = "instant";

    [Export]
    public bool can_crit { get; set; }

    [Export]
    public bool can_dodge { get; set; }

    [Export]
    public bool use_def_phys { get; set; }

    [Export]
    public bool use_def_qi { get; set; }

    // immediate / turn_end
    [Export]
    public string tick_timing { get; set; } = "immediate";

    [Export]
    public int base_duration { get; set; }

    [Export]
    public int max_stack { get; set; }

    [Export]
    public string display_color { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>是不是持续伤害（dot）。方法名保持 snake_case，见 `TableRow.cs` 的说明。</summary>
    public bool is_dot() => category == "dot";
}
