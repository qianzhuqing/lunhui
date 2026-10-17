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

    // 效果数值（Q33 的三列，2026-10-13 小策划答）：回复气血／回复内力／解除状态。
    // **以前效果只写在 `desc` 那一句中文里**（「非战斗恢复气血120」），代码不解析中文、
    // 界面也不编数（道具使用弹窗与战斗的道具指令都因此置灰过）。现在数值有了列。
    /// <summary>吃下去回多少气血（0＝不回；战斗内／外由 `use_context` 分）。</summary>
    [Export]
    public int heal_hp { get; set; }

    /// <summary>吃下去回多少内力（0＝不回）。</summary>
    [Export]
    public int heal_qi { get; set; }

    /// <summary>解除哪一个异常状态（填 `status_effect.status_id`，**空＝不解**；设计明写不做"全解"）。</summary>
    [Export]
    public string clear_status { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>是不是非战斗回血道具（游戏路径暂未接，见 test_handshake 的白名单理由）。</summary>
    public bool is_field_use() => use_context == "field";

    /// <summary>是不是战斗中回血道具。</summary>
    public bool is_battle_use() => use_context == "battle";
}
