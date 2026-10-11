using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/recruit_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class RecruitDefRow : TableRow
{
    // recruit_def.csv 行：谁开局就在队里、谁在什么条件下加入。
    //
    // 0.10.0 新增（设计 09 §3.2）：开局只有书生一人，同伴在剧情里陆续加入
    // （燕小七在清风驿客栈 → 林铁山落雁坡 → 苏九娘毒堂 → 白清和荒村）。
    [Export]
    public string char_id { get; set; } = "";

    // 初始成员（0.10.0：只有书生一行是 1，**不再取 character_base 前 4 行**）
    [Export]
    public bool is_initial { get; set; } = false;

    // 加入条件（旗标名，如 flag_board_read）；初始成员留空
    [Export]
    public string join_condition { get; set; } = "";

    // 在哪个场景加入（引用 map_local.scene_id）
    [Export]
    public string join_scene { get; set; } = "";

    [Export]
    public string join_note { get; set; } = "";
}
