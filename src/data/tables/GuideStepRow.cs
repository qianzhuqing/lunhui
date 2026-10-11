using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/guide_step.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class GuideStepRow : TableRow
{
    // guide_step.csv 行：开局引导的目标链（HUD 上常驻「当前目标」）。
    //
    // 0.10.0 新增（设计 09 §3.1）4 步；**0.22.0 扩到 6 步**（设计 18 §3.2）：
    // 告示板 → 招募同伴 → 木桩练到 5 级并备货 → 上山 → 进寨 → 救出沈小姐回城复命。
    // `condition` 是「这一步成为当前目标」的判据（剧情旗标；`start` 表示开局即满足）；
    // `condition_spec` 是**给程序看的判定口径**——原来只有一个裸旗标名，
    // 程序得猜「什么时候算备齐了」，现在是表里的契约（设计 18 的断点 7）。
    [Export]
    public string step_id { get; set; } = "";

    [Export]
    public string chapter_id { get; set; } = "";

    [Export]
    public int sort_order { get; set; } = 0;

    [Export]
    public string condition { get; set; } = "";

    [Export]
    public string text_cn { get; set; } = "";

    [Export]
    public string condition_spec { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
