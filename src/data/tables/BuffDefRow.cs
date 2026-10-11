using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/buff_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class BuffDefRow : TableRow
{
    // buff_def.csv 行：buff 本体（极性／作用域／持续／叠加规则）。
    //
    // 统一模型见 docs/design/08_增益与套装.md：装备特效、内功运功、套装档位、招式增益
    // 全部收敛成这个表里的一个 buff；会逐回合掉血的减益仍走 status_effect。
    [Export]
    public string buff_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 0 增益 / 1 减益（同一套机制，只是极性不同）
    [Export]
    public bool is_debuff { get; set; } = false;

    // battle 进战斗清空／field 跨场景保留、进战斗后按回合递减
    [Export]
    public string scope { get; set; } = "";

    // >0 剩余回合数；0 常驻（随来源存在而存在）；-1 持续到本场结束
    [Export]
    public int duration { get; set; } = 0;

    // **仅 `scope=field` 用**：战斗外的有效分钟数（现实时间）；进战斗后转为**整场有效**（0.8.0 口径）。
    // `battle` 的 buff 看 `duration`（回合），两者别混。
    [Export]
    public int field_minutes { get; set; } = 0;

    // refresh 刷新回合／stack 叠层（上限 max_stack）／unique 全局唯一
    [Export]
    public string stack_rule { get; set; } = "";

    [Export]
    public int max_stack { get; set; } = 1;

    // stat 走 buff_stat 的数值修正／computed 由代码算／special 特殊实现
    [Export]
    public string effect_kind { get; set; } = "";

    [Export]
    public string icon { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
