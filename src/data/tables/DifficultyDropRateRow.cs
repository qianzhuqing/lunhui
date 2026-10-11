using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/difficulty_drop_rate.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class DifficultyDropRateRow : TableRow
{
    // difficulty_drop_rate.csv 行：「难度不改变掉落清单，只改变掉落几率」的唯一实现处。
    [Export]
    public string difficulty_id { get; set; } = "";

    [Export]
    public string rarity_id { get; set; } = "";

    [Export]
    public double rate_multiplier { get; set; } = 1.0;

    [Export]
    public string note { get; set; } = "";
}
