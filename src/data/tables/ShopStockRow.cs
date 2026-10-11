using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// shop_stock.csv 行：货架（某店卖什么、什么价、限不限量）。
//
// 价格规则：卖出价一律低于买入价，差价即交易税（校验器会查，别让玩家刷钱）。
public partial class ShopStockRow : TableRow
{
    [Export]
    public string shop_id { get; set; } = "";

    [Export]
    public int sort_order { get; set; }

    // 物品或装备 id，按是否存在于 equip_base 分流
    [Export]
    public string item_id { get; set; } = "";

    [Export]
    public int buy_price { get; set; }

    [Export]
    public int sell_price { get; set; }

    // 0 表示无限量（第一章所有货架都是无限量）
    [Export]
    public int stock_limit { get; set; }

    [Export]
    public string note { get; set; } = "";

    public bool is_unlimited() => stock_limit <= 0;

    public int margin() => buy_price - sell_price;
}
