using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/rarity_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class RarityDefRow : TableRow
{
    // rarity_def.csv 行：稀有度定义。
    [Export]
    public string rarity_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public string color { get; set; } = "";

    [Export]
    public int affix_min { get; set; } = 0;

    [Export]
    public int affix_max { get; set; } = 0;

    // 掉落权重，仅作参考；实际掉率走 drop_table.base_rate
    [Export]
    public double drop_weight { get; set; } = 0.0;

    [Export]
    public string desc { get; set; } = "";
}
