using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/npc_offer.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class NpcOfferRow : TableRow
{
    // npc_offer.csv 行：NPC 的可兑换与可偷道具（设计 19 §2.3／§2.4）。
    //
    // `kind=offer`（好感度兑换，需要 `favor_required`＋`price`，price 可空＝只靠好感）
    // `kind=steal`（可偷，难度在 `npc_favor.steal_difficulty`）
    //
    // 这是「特有道具」的出口：NPC 独有装备只走这条路（`drop_only=1` 保证它们不从别处冒出来）。
    [Export]
    public string offer_id { get; set; } = "";

    [Export]
    public string npc_id { get; set; } = "";

    [Export]
    public string kind { get; set; } = "offer";

    // 引用 equip_base.equip_id 或 item_base.item_id
    [Export]
    public string item_id { get; set; } = "";

    [Export]
    public int favor_required { get; set; } = 0;

    // 价格（空 = 只靠好感换）
    [Export]
    public int price { get; set; } = 0;

    [Export]
    public string note { get; set; } = "";
}
