using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/npc_favor.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class NpcFavorRow : TableRow
{
    // npc_favor.csv 行：好感度规则（设计 19 §2.2）。
    //
    // 喜好分两级：`like_item_ids`（具体物品＝最爱，送一件加很多）与
    // `like_categories`（类别，如 material，送同类只加一点）——
    // **送对具体物品是惊喜，送对类别只是礼貌**。
    [Export]
    public string npc_id { get; set; } = "";

    [Export]
    public int initial_favor { get; set; } = 0;

    [Export]
    public int favor_max { get; set; } = 100;

    // 具体物品（最爱）；多个用 ";" 分隔，引用 item_base.item_id
    [Export]
    public string like_item_ids { get; set; } = "";

    // 类别（如 material／consumable）；多个用 ";" 分隔，引用 item_base.item_type
    [Export]
    public string like_categories { get; set; } = "";

    [Export]
    public string dislike_item_ids { get; set; } = "";

    // 送一件礼物加多少好感（送对喜好再加 `like_bonus` 的份）
    [Export]
    public int gift_favor { get; set; } = 5;

    // 切磋赢了加多少好感（0 = 不能切磋）
    [Export]
    public int spar_favor { get; set; } = 0;

    // 偷窃难度（事件判定的门槛）；**0 = 不可偷**（纯善意的 NPC 留的口子）
    [Export]
    public int steal_difficulty { get; set; } = 0;
}
