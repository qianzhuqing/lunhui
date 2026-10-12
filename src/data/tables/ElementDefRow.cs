using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// element_def.csv 行：**系别维表**（外功／内功／奇诡）。
// `skill_active.element`／`equip_base.element`／`weapon_type_def.default_element` 三个枚举列
// 与 `element_counter` 那张相克矩阵指的都是 `element_id`，名字一律读这里的 `name_cn`
// ——**界面不许出现 `external`／`internal`／`odd` 这三个 id**（文案审计盯着）。
public partial class ElementDefRow : TableRow
{
    [Export]
    public string element_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";
}
