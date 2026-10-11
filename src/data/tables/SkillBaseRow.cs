using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// skill_base.csv 行：武学总表（身份与门槛）。
//
// 0.6.0 起这里是**所有武学的索引**：招式数值在 skill_active.csv，内功在 skill_passive.csv，
// 内功加成在 skill_passive_stat.csv。战斗数值不要再往这张表加列。
public partial class SkillBaseRow : TableRow
{
    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // active 招式 / passive 内功
    [Export]
    public string skill_kind { get; set; } = "active";

    // 门派／流派标记
    [Export]
    public string school_id { get; set; } = "";

    // 星级 1~5，引用 skill_star_def
    [Export]
    public int star { get; set; } = 1;

    // 招式绑定的武器类型；通用填 any，内功留空
    [Export]
    public string weapon_type { get; set; } = "";

    // 修习门槛：某属性达到阈值才能学（留空表示无门槛）
    [Export]
    public string learn_req_attr { get; set; } = "";

    [Export]
    public int learn_req_value { get; set; }

    // start / npc / drop / story / hidden / item / shop
    [Export]
    public string source_type { get; set; } = "";

    // 来源 id（门派、敌人、触发点等，自由文本）
    [Export]
    public string source_id { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    public bool is_active() => skill_kind == "active";

    public bool is_passive() => skill_kind == "passive";

    /// <summary>武器要求是否通用（any 或留空都算不限制）。</summary>
    public bool accepts_any_weapon() => weapon_type.Length == 0 || weapon_type == "any";

    public bool has_learn_requirement() => learn_req_attr.Length > 0 && learn_req_value > 0;
}
