using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// building_def.csv 行：建筑（店铺与附加服务）。
public partial class BuildingDefRow : TableRow
{
    [Export]
    public string building_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // shop 等，目前只有店铺
    [Export]
    public string building_type { get; set; } = "shop";

    // 货架 id，引用 shop_stock.shop_id
    [Export]
    public string stock_group { get; set; } = "";

    // 附加服务 id（如 heal 治疗），留空表示只有买卖
    [Export]
    public string service_id { get; set; } = "";

    // 附加服务单价（每点气血的文数）
    [Export]
    public int service_price { get; set; }

    [Export]
    public string unlock_condition { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>有没有附加服务（只有买卖的建筑不算）。</summary>
    public bool has_service() => service_id.Trim().Length > 0;
}
