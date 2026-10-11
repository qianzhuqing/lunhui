using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/npc_guard.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class NpcGuardRow : TableRow
{
    // npc_guard.csv 行：宝箱守护者——同一个箱子给「文取」与「武取」两条路。
    //
    // 0.10.0 新增（设计 09 §一）：宝箱不再由打怪解锁，而是绑给一个具体的人。
    // 文取 = 满足 `peace_condition`（`item:<道具>` 或 `skill:<非战斗技能>`，用 `|` 连多条件）；
    // 武取 = 打赢 `fight_team`。两条路拿**同一个箱子**（`dungeon_room.chest_id` 仍描述箱内东西）。
    [Export]
    public string guard_id { get; set; } = "";

    // 守护者守的房间（引用 dungeon_room.room_id）
    [Export]
    public string room_id { get; set; } = "";

    [Export]
    public string npc_name_cn { get; set; } = "";

    // 文取的三列（设计 0.14.0 把原来的单列 `peace_condition` 拆开，门槛值终于有了出处——Q53）：
    // `peace_item` 要交的道具（引用 item_base）、`peace_check_source` 走哪条判定
    // （`skill:<非战斗技能>` 或 `attr:<属性>`）、`peace_check_value` 判定门槛。
    // **三字段可叠加，满足任意一条即算过**（06 的口径）；两条都留空时这位守卫就没有文取这条路。
    [Export]
    public string peace_item { get; set; } = "";

    [Export]
    public string peace_check_source { get; set; } = "";

    [Export]
    public int peace_check_value { get; set; } = 0;

    [Export]
    public string peace_note { get; set; } = "";

    // 武取队伍（引用 enemy_team.team_id）
    [Export]
    public string fight_team { get; set; } = "";

    [Export]
    public string fight_note { get; set; } = "";

    // 奖励掉落组（引用 drop_table.drop_group）
    [Export]
    public string reward_group { get; set; } = "";
}
