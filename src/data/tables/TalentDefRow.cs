using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/talent_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class TalentDefRow : TableRow
{
    // talent_def.csv 行：创建角色的天赋池（24 行，五个点内自选）。
    //
    // 0.16.0 新增（设计 12）：出身定底子、天赋定偏好。数值／规则效果在 `talent_effect`
    // （`attr:`／`stat:`／`rule:` 三种前缀），这里只有展示与花费。
    [Export]
    public string talent_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 六个方向：combat（武学）／body（筋骨）／agile（身法）／mind（悟道）／social（江湖）／fortune（财货）
    [Export]
    public string category { get; set; } = "";

    // 花几个天赋点（1~3）；总点数在 growth_const.talent_points
    [Export]
    public int cost { get; set; } = 1;

    // 玩家在创建界面看到的就是这句话——**带代价的天赋必须把代价写在这里**（设计 12 第三条纪律）
    [Export]
    public string desc { get; set; } = "";
}
