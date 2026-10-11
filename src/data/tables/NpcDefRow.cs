using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/npc_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class NpcDefRow : TableRow
{
    // npc_def.csv 行：可以交往的 NPC（设计 19 §2.1，0.25 交付）。
    //
    // 本章的 NPC 从「按 E 触发固定结果」升级成**可以交往的人**：
    // 身份／位置／称号／基本信息／切磋队伍都在这一行里。
    // 好感度规则在 `npc_favor`、兑换与偷窃在 `npc_offer`、专属任务在 `npc_quest`。
    [Export]
    public string npc_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 称号（「清风驿铁匠」这种），信息面板第一行显示
    [Export]
    public string title_cn { get; set; } = "";

    // 所在位置：`map_local.scene_id`（城镇／副本）或 `map_region.node_id`（大地图区域，如 n_luoyanpo）
    // ——列名是 `place_id`（设计 19 的写法，与 `story_node.place_id` 同一口径：地点可以是两者之一）
    [Export]
    public string place_id { get; set; } = "";

    // NPC 等级（切磋与偷窃难度都按它算）
    [Export]
    public int level { get; set; } = 1;

    [Export]
    public string faction { get; set; } = "civilian";

    // 切磋队伍（引用 enemy_team.team_id）；留空 = 不能切磋
    [Export]
    public string spar_team_id { get; set; } = "";

    [Export]
    public string greet_text_cn { get; set; } = "";

    // 基本信息（信息面板正文）：**要有人味**——刀疤、脾气、旧识，不然 NPC 就是自动售货机
    [Export]
    public string info_text_cn { get; set; } = "";
}
