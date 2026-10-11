using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/feature_toggle.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class FeatureToggleRow : TableRow
{
    // feature_toggle.csv 行：**全局开关**（构建期决定的功能开关）。
    //
    // 只放"要不要开某个功能"这种**一行一个开关**的东西；`value` 的取值语义写在 `desc` 里。
    [Export]
    public string toggle_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public int value { get; set; } = 0;

    [Export]
    public string desc { get; set; } = "";
}
