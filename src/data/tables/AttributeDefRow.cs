using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/attribute_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class AttributeDefRow : TableRow
{
    // attribute_def.csv 行：五维属性定义。
    [Export]
    public string attr_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 1 = 玩家可自由加点（力体敏智运）；0 = 资质，创建时定死（悟性／根骨）。
    // 设计 0.13.0：资质决定天花板、等级决定成长速度，所以资质不许靠刷级买。
    [Export]
    public int allocatable { get; set; } = 1;

    [Export]
    public string desc { get; set; } = "";
}
