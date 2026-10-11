using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// item_base.csv 行：货币／材料／消耗品／钥匙道具／残页。
public partial class ItemBaseRow : TableRow
{
    [Export]
    public string item_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // currency / material / consumable / key / tool / skillbook
    [Export]
    public string item_type { get; set; } = "";

    // 引用 rarity_def.rarity_id
    [Export]
    public string rarity { get; set; } = "common";

    // 使用场景：field 非战斗回血 / battle 战斗中回血 / 空表示不是回血道具
    [Export]
    public string use_context { get; set; } = "";

    [Export]
    public int stack_max { get; set; } = 1;

    [Export]
    public int sell_price { get; set; }

    [Export]
    public bool is_key_item { get; set; }

    [Export]
    public string icon { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>是不是非战斗回血道具（游戏路径暂未接，见 test_handshake 的白名单理由）。</summary>
    public bool is_field_use() => use_context == "field";

    /// <summary>是不是战斗中回血道具。</summary>
    public bool is_battle_use() => use_context == "battle";
}
