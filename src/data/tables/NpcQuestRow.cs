using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/npc_quest.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class NpcQuestRow : TableRow
{
    // npc_quest.csv 行：NPC 专属任务线（设计 19 §2.5）。
    //
    // 同一个 NPC 的多条任务按 `sort_order` 串成一条线。
    // `requirement` **复用既有条件语言**，不新造一套：
    // `item:<item_id>:<数量>`／`flag_<旗标>`／`event:<check_id>`／`kill_style:<要素>`
    [Export]
    public string quest_id { get; set; } = "";

    [Export]
    public string npc_id { get; set; } = "";

    [Export]
    public int sort_order { get; set; } = 0;

    [Export]
    public string requirement { get; set; } = "";

    // 奖励道具（分号分隔，引用 item_base／equip_base）
    [Export]
    public string reward_item_ids { get; set; } = "";

    [Export]
    public int reward_favor { get; set; } = 0;

    [Export]
    public string text_cn { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
