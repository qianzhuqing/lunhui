using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/skill_star_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class SkillStarDefRow : TableRow
{
    // skill_star_def.csv 行：星级定义（强度与获取难度）。
    //
    // 熟练度成长随星级递减：★1 每级 +5%，★5 只有 +2.5%——刻意如此，
    // 免得玩家每拿到一部高星武学，之前练的全白练。
    [Export]
    public int star { get; set; } = 1;

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public string color { get; set; } = "";

    // 每级熟练度提供的倍率加成
    [Export]
    public double mastery_gain { get; set; } = 0.0;

    // 修习门槛的属性阈值
    [Export]
    public int learn_req_value { get; set; } = 0;

    // 打坐基础费用
    [Export]
    public int cultivate_cost_base { get; set; } = 0;

    [Export]
    public string desc { get; set; } = "";
}
