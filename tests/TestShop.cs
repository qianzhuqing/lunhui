using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 商店：货架、买入、卖出、回购、限量、治疗费用与存档交易状态
/// （对位 `tests/test_shop.gd`，2026-10-11 P4 第三十一批整体搬过来）。
///
/// 规则出处 `docs/design/05_装备与掉落.md`「商店与经济」：所有店铺都买也卖；卖出价低于买入价；
/// 卖出的装备只进回购列表；治疗费 = 缺失气血 × service_price。
/// </summary>
public partial class TestShop : TestCase
{
    private const string Smith = "bld_smith";
    private const string Grocery = "bld_grocery";
    private const string Clinic = "bld_clinic";
    private const string ShopScene = "res://scenes/shop_screen.tscn";

    public override string SuiteName() => "商店与交易";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        ShopService shop = ShopService.create(db, state);
        _CheckStock(db, shop);
        _CheckBuy(db, state, shop);
        _CheckSell(db, state, shop);
        _CheckBuyback(db, state, shop);
        _CheckBuybackPartial(db);
        _CheckLimitedStock(db);
        _CheckLimitedStockUi(db);
        _CheckBulkBuy(db);
        _CheckSellAll(db);
        _CheckClinicMultiMember(db);
        _CheckHeal(db, state, shop);
        _CheckSave(db, state);
    }

    /// <summary>
    /// 五家店的货架按表来，且都同时买也卖
    /// （0.28.0 加了第五家：**行商** `bld_caravan`——它不是地图上的建筑，
    /// 是大地图随机事件 `we_caravan` 开出来的货架，所以这里也要能查到它的行）。
    /// </summary>
    private void _CheckStock(TableDb db, ShopService shop)
    {
        // 杂货铺 7 行 = 5 行材料／白板饰品 ＋ 0.23.0 加进来的毒酒（隐藏线「毒杀毒手」的钥匙）
        // ＋ 2026-10-04 的秘籍（Q3：shop 那 5 部武学改走 `item` 来源，四家店各上架一本）
        var counts = new Godot.Collections.Dictionary
        {
            { Grocery, 7 },
            { "bld_tavern", 3 },
            { Clinic, 2 },
            { Smith, 10 },
            { "bld_caravan", 4 },
        };
        foreach (Variant key in counts.Keys)
        {
            string building_id = key.AsString();
            Godot.Collections.Array rows = shop.stock_rows(building_id);
            CheckEq(rows.Count, counts[key].AsInt32(), $"{building_id} 的货架行数");
            int last_order = -1;
            foreach (Variant entry in rows)
            {
                var row = entry.As<ShopStockRow>();
                CheckGt(row.sort_order, last_order, "货架按 sort_order 升序");
                last_order = row.sort_order;
                CheckGt(row.buy_price, 0.0, $"{building_id} 的 {row.item_id} 有买入价");
                CheckGt(row.buy_price, row.sell_price, $"{building_id} 的 {row.item_id} 买入价高于卖出价");
            }
        }
        CheckTrue(shop.has_heal(Clinic), "医馆有治疗服务");
        CheckFalse(shop.has_heal(Smith), "铁匠铺没有治疗服务");
    }

    /// <summary>买入：扣钱、进背包、记累计次数；钱不够、不卖的东西、只掉落的东西都要被拒。</summary>
    private void _CheckBuy(TableDb db, GodotObject state, ShopService shop)
    {
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        Godot.Collections.Dictionary bought = shop.buy(Grocery, "item_iron", 5);
        CheckTrue(bought["ok"].AsBool(), $"买 5 块铁矿石：{bought["error"].AsString()}");
        CheckEq(bought["spent"].AsInt32(), 100, "花掉 5 × 20 = 100 文");
        CheckEq(inventory.money, 900, "铜钱扣掉");
        CheckEq(inventory.count("item_iron"), 5, "材料进背包");
        CheckEq(_Purchased(state, Grocery, "item_iron"), 5, "累计买入次数写进存档");

        Godot.Collections.Dictionary poor = shop.can_buy(Smith, "eq_sword_01", 1);
        CheckTrue(poor["ok"].AsBool(), "900 文买得起 120 文的剑");
        inventory.money = 10;
        Godot.Collections.Dictionary no_money = shop.buy(Smith, "eq_sword_01", 1);
        CheckFalse(no_money["ok"].AsBool(), "铜钱不够买不了");
        CheckTrue(
            no_money["error"].AsString().Contains("铜钱不够"),
            $"说明是铜钱不够：{no_money["error"].AsString()}");
        inventory.money = 1000;

        Godot.Collections.Dictionary not_sold = shop.can_buy(Smith, "item_herb", 1);
        CheckFalse(not_sold["ok"].AsBool(), "铁匠铺不卖草药");
        CheckTrue(
            not_sold["error"].AsString().Contains("不卖"),
            $"说明这家店不卖：{not_sold["error"].AsString()}");

        // 装备买入走实例，且是白板（没有随机词条——词条是刷出来的，不是买来的）
        Godot.Collections.Dictionary weapon = shop.buy(Smith, "eq_sword_01", 1);
        CheckTrue(weapon["ok"].AsBool(), $"买白板剑：{weapon["error"].AsString()}");
        Godot.Collections.Array equipment = weapon["equipment"].AsGodotArray();
        CheckEq(equipment.Count, 1, "生成了一件装备实例");
        if (equipment.Count > 0)
        {
            string instance_id = equipment[0].AsString();
            CheckEq(inventory.base_of(instance_id), "eq_sword_01", "本体是铁剑");
            CheckTrue(inventory.affixes_of(instance_id).Count == 0, "店里卖的是白板，没有词条");
        }

        // stock_limit = 0 表示无限量（第一章全是无限量）
        CheckEq(shop.remaining(Grocery, "item_iron"), -1, "无限量货架返回 -1");
        CheckEq(shop.buy_price(Grocery, "item_iron"), 20, "买入价取自货架");

        // 「差一文」边界：刚好够钱能买、少一文就拒（与治疗那条同口径）
        int count_before = inventory.count("item_iron");
        inventory.money = 19;
        Godot.Collections.Dictionary short_buy = shop.buy(Grocery, "item_iron", 1);
        CheckFalse(short_buy["ok"].AsBool(), "差一文买不了");
        CheckEq(inventory.money, 19, "被拒时一文不扣");
        CheckEq(inventory.count("item_iron"), count_before, "被拒时背包不变");
        inventory.money = 20;
        Godot.Collections.Dictionary exact = shop.buy(Grocery, "item_iron", 1);
        CheckTrue(exact["ok"].AsBool(), $"刚好够钱能买：{exact["error"].AsString()}");
        CheckEq(inventory.money, 0, "刚好花光");
        CheckEq(inventory.count("item_iron"), count_before + 1, "货进了背包");
    }

    /// <summary>卖出：价格用卖出价、扣物品、进回购列表；钥匙、身上穿着的、店里不上架的都要被拒。</summary>
    private void _CheckSell(TableDb db, GodotObject state, ShopService shop)
    {
        Inventory inventory = _InventoryOf(state);
        int iron_before = inventory.count("item_iron");
        Godot.Collections.Dictionary got = shop.sell(Grocery, "item_iron", 2);
        CheckTrue(got["ok"].AsBool(), $"卖 2 块铁矿石：{got["error"].AsString()}");
        CheckEq(got["earned"].AsInt32(), 12, "卖价 6 × 2 = 12 文");
        CheckEq(inventory.count("item_iron"), iron_before - 2, "背包里少 2 块");
        Godot.Collections.Array entries = shop.buyback_rows(Grocery);
        CheckEq(entries.Count, 1, "卖出的东西进回购列表");
        if (entries.Count > 0)
        {
            Godot.Collections.Dictionary entry = entries[0].AsGodotDictionary();
            CheckEq(entry["item_id"].AsString(), "item_iron", "回购条目记的是铁矿石");
            CheckEq(entry["price"].AsInt32(), 6, "回购价就是卖出价");
            CheckEq(entry["qty"].AsInt32(), 2, "回购条目记数量");
        }

        // 钥匙道具不能卖
        inventory.add_item(db, "item_wine_gourd", 1);
        Godot.Collections.Dictionary key_sell = shop.sell(Grocery, "item_wine_gourd", 1);
        CheckFalse(key_sell["ok"].AsBool(), "钥匙道具不能卖");
        CheckTrue(
            key_sell["error"].AsString().Contains("钥匙"),
            $"说明是钥匙道具：{key_sell["error"].AsString()}");

        // 掉落的装备（店里没上架）不收
        string dropped = inventory.add_equipment(db, "eq_sword_03", new Godot.Collections.Array());
        Godot.Collections.Dictionary no_shelf = shop.sell(Smith, dropped, 1);
        CheckFalse(no_shelf["ok"].AsBool(), "店里没上架的装备不收");
        CheckTrue(
            no_shelf["error"].AsString().Contains("不收"),
            $"说明这家店不收：{no_shelf["error"].AsString()}");

        // 上架的装备：身上穿着的不能卖，卸下后能卖，且词条一起进回购列表
        // （白板戒指 eq_ring_01 要 2 级：抬到 3 级；词条手写，免得 0~1 条的凡品掷出空词条）
        state.Get("char_levels").AsGodotDictionary()[SoloCharId] = 3;
        string ring = inventory.add_equipment(
            db,
            "eq_ring_01",
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "affix_id", "af_luk_01" },
                    { "target", "attr:luk" },
                    { "value_kind", "attr_point" },
                    { "value", 2.0 },
                },
            });
        Godot.Collections.Dictionary worn = inventory.equip(db, SoloCharId, 3, "sword", ring);
        CheckTrue(worn["ok"].AsBool(), "先把戒指戴上");
        Godot.Collections.Dictionary worn_sell = shop.sell(Grocery, ring, 1);
        CheckFalse(worn_sell["ok"].AsBool(), "身上穿着的不能卖");
        CheckTrue(
            worn_sell["error"].AsString().Contains("先卸下"),
            $"提示先卸下：{worn_sell["error"].AsString()}");
        inventory.unequip(SoloCharId, "ring", 0);
        Godot.Collections.Dictionary sold = shop.sell(Grocery, ring, 1);
        CheckTrue(sold["ok"].AsBool(), $"卸下后能卖：{sold["error"].AsString()}");
        CheckFalse(inventory.has_equipment(ring), "卖掉的实例从背包里消失");
        Godot.Collections.Array last = shop.buyback_rows(Grocery);
        Godot.Collections.Dictionary last_entry = last[last.Count - 1].AsGodotDictionary();
        CheckEq(_S(last_entry, "instance_id", ""), ring, "回购条目记着实例 id");
        CheckGt(_A(last_entry, "affixes").Count, 0.0, "词条跟着进回购列表");
    }

    /// <summary>回购：价格就是当初的卖出价；带词条的装备原样回来；条目按上限挤掉最早的。</summary>
    private void _CheckBuyback(TableDb db, GodotObject state, ShopService shop)
    {
        Inventory inventory = _InventoryOf(state);
        // 回购上限是**开发侧口径**（模块备注写着「回购＝按卖出价撤销卖出（上限20条）」）：
        // 用 `push_buyback(..., 20)` 造夹具的地方会跟着常量走，所以常量本身得单独钉住
        // （2026-10-03 变异探针：`BUYBACK_LIMIT 20→5` 没被抓住）。
        CheckEq(ShopService.buyback_limit(), 20, "回购列表上限 20 条");
        Godot.Collections.Array before = shop.buyback_rows(Grocery);
        int index = before.Count - 1;
        Godot.Collections.Dictionary entry = before[index].AsGodotDictionary();
        int cost = entry["price"].AsInt32() * entry["qty"].AsInt32();
        int money_before = inventory.money;
        Godot.Collections.Dictionary back = shop.buy_back(Grocery, index);
        CheckTrue(back["ok"].AsBool(), $"能从回购列表买回来：{back["error"].AsString()}");
        CheckEq(back["cost"].AsInt32(), cost, "回购价 = 当初的卖出价");
        CheckEq(inventory.money, money_before - cost, "回购扣钱");
        CheckEq(shop.buyback_rows(Grocery).Count, before.Count - 1, "回购后条目出列");
        string restored = "";
        foreach (string instance_id in inventory.equipment_ids())
        {
            if (inventory.base_of(instance_id) == "eq_ring_01")
            {
                restored = instance_id;
            }
        }
        CheckNe(restored, "", "戒指回到背包");
        if (restored.Length > 0)
        {
            CheckGt(
                inventory.affixes_of(restored).Count,
                0.0,
                $"回购回来的戒指词条没丢：{AffixRoller.summarize(db, inventory.affixes_of(restored))}");
        }

        // 回购列表上限 20：塞 25 条，最早的 5 条被挤掉
        for (int n = 0; n < 25; n++)
        {
            state.Call(
                "push_buyback",
                Smith,
                new Godot.Collections.Dictionary
                {
                    { "item_id", "item_iron" }, { "qty", 1 }, { "price", 6 }, { "tag", n },
                },
                20);
        }
        Godot.Collections.Array capped = state.Call("buyback_of", Smith).AsGodotArray();
        CheckEq(capped.Count, 20, "回购列表封顶 20 条");
        CheckEq(_I(capped[0].AsGodotDictionary(), "tag", -1), 5, "最早的那几条被挤掉");
    }

    /// <summary>
    /// 回购时的「部分入包」：同一堆只剩 2 格空位、回购条目却有 5 个时，
    /// 以前按**全价**扣钱、整条出列 —— 玩家付了 5 个的钱只拿到 2 个，另外 3 个凭空消失。
    /// （买入路径早就写了「只按真正放进去的数量收钱，别把钱扣在空气上」，回购漏了这一条。）
    /// </summary>
    private void _CheckBuybackPartial(TableDb db)
    {
        GodotObject state = SoloState(db);
        ShopService shop = ShopService.create(db, state);
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        Resource row = db.get_row("item_base", "item_iron");
        int cap = System.Math.Max(1, row.Get("stack_max").AsInt32());
        inventory.add_item(db, "item_iron", cap);
        CheckTrue(shop.sell(Grocery, "item_iron", 5)["ok"].AsBool(), "先卖掉 5 块铁矿石（进回购列表）");
        int unit = shop.sell_price(Grocery, "item_iron");
        CheckGt(unit, 0.0, "杂货铺收铁矿石且单价大于 0");
        inventory.add_item(db, "item_iron", 3); // 卖完剩 94，再捡 3 块 → 只剩 2 格空位
        CheckEq(inventory.count("item_iron"), cap - 2, "背包里这一堆只剩 2 格空位");

        int money_before = inventory.money;
        Godot.Collections.Dictionary back = shop.buy_back(Grocery, 0);
        CheckTrue(back["ok"].AsBool(), $"回购能部分成交：{back["error"].AsString()}");
        CheckEq(back["cost"].AsInt32(), unit * 2, "只按真正放进去的 2 个收钱");
        CheckEq(inventory.count("item_iron"), cap, "背包这一堆补满");
        CheckEq(inventory.money, money_before - unit * 2, "钱也只扣 2 个的");
        Godot.Collections.Array left = shop.buyback_rows(Grocery);
        CheckEq(left.Count, 1, "没买完的还留在回购列表");
        if (left.Count > 0)
        {
            CheckEq(left[0].AsGodotDictionary()["qty"].AsInt32(), 3, "剩下的 3 个还在列表里（没被吞掉）");
        }

        // 腾出地方再买回剩下的：条目清掉
        inventory.remove_item(db, "item_iron", 4);
        Godot.Collections.Dictionary again = shop.buy_back(Grocery, 0);
        CheckTrue(again["ok"].AsBool(), $"腾出地方后能把剩下的买回来：{again["error"].AsString()}");
        CheckEq(shop.buyback_rows(Grocery).Count, 0, "买完条目出列");

        // 一格都放不下时：不许扣钱，条目原样保留
        CheckTrue(shop.sell(Grocery, "item_iron", 5)["ok"].AsBool(), "再卖 5 块（新条目）");
        // 用 room_for 补齐（别手算格数——上一版就是手算错了，断言才红的）
        inventory.add_item(db, "item_iron", inventory.room_for(db, "item_iron"));
        CheckEq(inventory.room_for(db, "item_iron"), 0, "这一堆现在是满的");
        int full_money = inventory.money;
        Godot.Collections.Dictionary refused = shop.buy_back(Grocery, shop.buyback_rows(Grocery).Count - 1);
        CheckFalse(refused["ok"].AsBool(), "背包放不下时不许回购");
        CheckTrue(
            refused["error"].AsString().Contains("放不下"),
            $"说明是背包放不下：{refused["error"].AsString()}");
        CheckEq(inventory.money, full_money, "被拒时一文不扣");
    }

    /// <summary>医馆治疗：费用 = 缺失气血 × service_price。战斗外气血 v11 起进存档，所以这里是**真治**。</summary>
    private void _CheckHeal(TableDb db, GodotObject state, ShopService shop)
    {
        Inventory inventory = _InventoryOf(state);
        CheckEq(shop.heal_price(Clinic), 2.0, "每点气血 2 文");
        CheckEq(shop.heal_cost(Clinic, 30), 60, "缺 30 点气血要 60 文");
        CheckEq(shop.heal_cost(Smith, 30), 0, "没有治疗服务的店费用为 0");
        CheckFalse(shop.heal_preview(Smith)["ok"].AsBool(), "铁匠铺没有治疗服务");

        // 满血：预览缺 0、治疗被拒
        Godot.Collections.Dictionary full = shop.heal_preview(Clinic);
        CheckTrue(full["ok"].AsBool(), "医馆能出治疗预览");
        CheckEq(full["missing"].AsInt32(), 0, "没受伤时缺失为 0");
        Godot.Collections.Dictionary full_heal = shop.heal_party(Clinic);
        CheckFalse(full_heal["ok"].AsBool(), "满血不用治");
        CheckTrue(
            full_heal["error"].AsString().Contains("满"),
            $"说明气血是满的：{full_heal["error"].AsString()}");

        // 把主角打伤 30 点：预览按缺失算钱，治疗后回满
        CharacterSheet sheet = CharacterSheet.create(db, state, SoloCharId);
        int cap = sheet.max_hp();
        state.Call("set_current_hp", SoloCharId, cap - 30);
        Godot.Collections.Dictionary preview = shop.heal_preview(Clinic);
        CheckEq(preview["missing"].AsInt32(), 30, "缺 30 点");
        CheckEq(preview["cost"].AsInt32(), 60, "缺 30 点要 60 文");
        Godot.Collections.Dictionary first_row = preview["rows"].AsGodotArray()[0].AsGodotDictionary();
        CheckEq(first_row["hp"].AsInt32(), cap - 30, "预览里写着当前气血");
        inventory.money = 59;
        Godot.Collections.Dictionary poor = shop.heal_party(Clinic);
        CheckFalse(poor["ok"].AsBool(), "钱不够治不了");
        CheckTrue(
            poor["error"].AsString().Contains("铜钱不够"),
            $"写清缺钱：{poor["error"].AsString()}");
        inventory.money = 60;
        Godot.Collections.Dictionary healed = shop.heal_party(Clinic);
        CheckTrue(healed["ok"].AsBool(), $"钱够就能治：{healed["error"].AsString()}");
        CheckEq(healed["cost"].AsInt32(), 60, "花 60 文");
        CheckEq(healed["healed"].AsInt32(), 30, "回复点数 = 缺失气血");
        CheckEq(inventory.money, 0, "钱扣掉了");
        CheckFalse(state.Call("is_wounded", SoloCharId).AsBool(), "治完全队不带伤");
        CheckEq(shop.heal_preview(Clinic)["missing"].AsInt32(), 0, "治完预览归零");
    }

    /// <summary>交易状态进存档：累计买入次数与回购列表都要能读回来。</summary>
    private void _CheckSave(TableDb db, GodotObject state)
    {
        Godot.Collections.Dictionary data = state.Call("to_dict").AsGodotDictionary();
        CheckTrue(data.ContainsKey("shop_state"), "存档里有 shop_state");
        GameState back = GameState.from_dict(data, db);
        CheckNotNull(back, "带交易状态的存档能读回来");
        if (back == null)
        {
            return;
        }
        CheckEq(back.migrated_from, 0, "同版本往返不需要迁移");
        CheckEq(
            back.purchased_count(Grocery, "item_iron"),
            _Purchased(state, Grocery, "item_iron"),
            "累计买入次数往返一致");
        CheckEq(
            back.buyback_of(Smith).Count,
            state.Call("buyback_of", Smith).AsGodotArray().Count,
            "回购列表往返一致");
        CheckEq(
            _ShopStateKeys(back.shop_state),
            _ShopStateKeys(state.Get("shop_state").AsGodotDictionary()),
            "参与过交易的店铺一致");

        // v5 老档（没有 shop_state）读进来是空的，版本升到 v6
        Godot.Collections.Dictionary legacy = data.Duplicate(true);
        legacy["version"] = 5;
        legacy.Remove("shop_state");
        GameState migrated = GameState.from_dict(legacy, db);
        CheckEq(migrated.migrated_from, 5, "记下从 v5 迁移");
        CheckEq(migrated.version, GameState.save_version(), "版本升到当前");
        CheckEq(migrated.buyback_of(Smith).Count, 0, "老档没有回购列表");
        CheckEq(migrated.purchased_count(Grocery, "item_iron"), 0, "老档没有累计买入次数");
    }

    /// <summary>
    /// 限量货架：第一章的货架全是无限量（`stock_limit=0`），所以「限量」这条路从来没被真数据跑过——
    /// 这里复制一份 `shop_stock` 只改内存，把杂货铺的铁矿石改成限 3 个，服务层与界面层各走一遍。
    /// </summary>
    private void _CheckLimitedStock(TableDb db)
    {
        TableDb limited = _StockTableWith("shop_grocery", "item_iron", 3);
        GodotObject state = SoloState(db);
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        ShopService shop = ShopService.create(limited, state);
        CheckEq(shop.remaining(Grocery, "item_iron"), 3, "限量货架初始还剩 3");
        Godot.Collections.Dictionary two = shop.buy(Grocery, "item_iron", 2);
        CheckTrue(two["ok"].AsBool(), $"买 2 个可以：{two["error"].AsString()}");
        CheckEq(shop.remaining(Grocery, "item_iron"), 1, "买完剩 1");
        CheckEq(_Purchased(state, Grocery, "item_iron"), 2, "累计买入次数跟着涨");

        Godot.Collections.Dictionary over = shop.buy(Grocery, "item_iron", 2);
        CheckFalse(over["ok"].AsBool(), "只剩 1 个时买 2 个被拒");
        CheckTrue(
            over["error"].AsString().Contains("库存不够"),
            $"说明是库存不够：{over["error"].AsString()}");
        CheckEq(inventory.money, 960, "被拒时一文不扣");
        CheckEq(_Purchased(state, Grocery, "item_iron"), 2, "被拒时累计次数不变");

        CheckTrue(shop.buy(Grocery, "item_iron", 1)["ok"].AsBool(), "把最后 1 个买走");
        CheckEq(shop.remaining(Grocery, "item_iron"), 0, "卖光后的余量是 0（限量和无限量的 -1 要分得开）");
        Godot.Collections.Dictionary sold_out = shop.can_buy(Grocery, "item_iron", 1);
        CheckFalse(sold_out["ok"].AsBool(), "卖光后再买被拒");
        CheckTrue(
            sold_out["error"].AsString().Contains("还能买 0 个"),
            $"写清还能买 0 个：{sold_out["error"].AsString()}");
        CheckEq(shop.remaining(Grocery, "item_herb"), -1, "改的是那一行：同店其它货架还是无限量");

        // 累计买入次数进存档 → 读回来仍然「卖光了」
        GameState back = GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), limited);
        CheckNotNull(back, "限量进度随存档走");
        if (back != null)
        {
            ShopService shop2 = ShopService.create(limited, back);
            CheckEq(shop2.remaining(Grocery, "item_iron"), 0, "读档后仍记得这家店卖光了");
        }
    }

    /// <summary>
    /// 「一次买多件」两条路：装备买 5 把要生成 5 个**独立实例**（用得上、卖得出、各带各的词条）；
    /// 材料堆到上限时**只按真正放进去的数量收钱**（买入侧的 overflow 以前没人验——回购侧补过同类）。
    /// </summary>
    private void _CheckBulkBuy(TableDb db)
    {
        GodotObject state = SoloState(db);
        ShopService shop = ShopService.create(db, state);
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        Godot.Collections.Dictionary swords = shop.buy(Smith, "eq_sword_01", 5);
        CheckTrue(swords["ok"].AsBool(), $"一次买 5 把剑：{swords["error"].AsString()}");
        Godot.Collections.Array equipment = swords["equipment"].AsGodotArray();
        CheckEq(equipment.Count, 5, "生成 5 个装备实例");
        var unique = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in equipment)
        {
            unique.Add(entry.AsString());
        }
        CheckEq(unique.Count, 5, "5 个实例 id 各不相同");
        CheckFalse(unique.Contains(""), "没有造空的实例 id");
        CheckEq(swords["spent"].AsInt32(), 120 * 5, "按 5 件收钱");
        CheckEq(inventory.money, 1000 - 600, "钱扣对");

        // 材料：先堆到只剩 2 格空位，再买 5 个 → 只收 2 个的钱，其余在 overflow 里报出来
        inventory.add_item(db, "item_iron", 97);
        inventory.money = 1000;
        Godot.Collections.Dictionary partial = shop.buy(Grocery, "item_iron", 5);
        CheckTrue(partial["ok"].AsBool(), $"堆快满时也能买（部分入包）：{partial["error"].AsString()}");
        CheckEq(partial["spent"].AsInt32(), 20 * 2, "只按真正放进去的 2 个收钱");
        CheckEq(inventory.count("item_iron"), 99, "这一堆补满了");
        CheckEq(inventory.money, 1000 - 40, "钱只扣 2 个的");
        Godot.Collections.Dictionary first_got = partial["got"].AsGodotArray()[0].AsGodotDictionary();
        CheckEq(_I(first_got, "overflow", 0), 3, "没放进去的 3 个在 overflow 里报出来");
        CheckEq(_Purchased(state, Grocery, "item_iron"), 2, "累计买入次数按真正入包的数量记");
    }

    /// <summary>
    /// 卖出侧的另一半：**一次卖光**（数量正好等于持有量）→ 背包那一行**消失**（不留 0 个的空行，
    /// 否则背包里会出现「铁矿石 ×0」），回购条目记下整批的数量与单价；卖得比手上多要被拒。
    /// </summary>
    private void _CheckSellAll(TableDb db)
    {
        GodotObject state = SoloState(db);
        ShopService shop = ShopService.create(db, state);
        Inventory inventory = _InventoryOf(state);
        inventory.add_item(db, "item_iron", 7);
        Godot.Collections.Dictionary too_many = shop.sell(Grocery, "item_iron", 8);
        CheckFalse(too_many["ok"].AsBool(), "卖得比手上多要被拒");
        CheckTrue(
            too_many["error"].AsString().Contains("数量不足"),
            $"写明数量不足：{too_many["error"].AsString()}");
        CheckEq(inventory.count("item_iron"), 7, "被拒后数量不变");

        Godot.Collections.Dictionary sold = shop.sell(Grocery, "item_iron", 7);
        CheckTrue(sold["ok"].AsBool(), $"一次卖光 7 块：{sold["error"].AsString()}");
        CheckEq(sold["earned"].AsInt32(), 6 * 7, "按卖出价 ×7 收钱");
        CheckEq(inventory.count("item_iron"), 0, "数量归 0");
        CheckFalse(inventory.has("item_iron", 1), "背包里不再算「有」它");
        CheckFalse(
            System.Array.IndexOf(inventory.item_ids(), "item_iron") >= 0,
            "背包清单里也不再有它（不留 ×0 的空行）");
        // `item_ids()` 会过滤 0，所以「留不留 0 键」从外面看不出来——但它**会被写进存档**（多一条垃圾，
        // 而且堆叠表会随着买卖慢慢涨），所以直接看内部表
        CheckFalse(inventory.stacks.ContainsKey("item_iron"), "内部堆叠表也不留 0 键（存档里不该有垃圾条目）");
        Godot.Collections.Array rows = shop.buyback_rows(Grocery);
        CheckEq(rows.Count, 1, "整批进回购列表一条");
        if (rows.Count > 0)
        {
            Godot.Collections.Dictionary first = rows[0].AsGodotDictionary();
            CheckEq(first["qty"].AsInt32(), 7, "条目记 7 个");
            CheckEq(first["price"].AsInt32(), 6, "单价还是卖出价");
        }
    }

    /// <summary>
    /// 商店服务页的多人分支：4 人队伍时预览要**逐人列一行**、总价按全队缺失求和，治疗一次全队回满。
    /// 服务层的「全队求和」在 test_end_to_end 里验过，这一条补的是**界面行数与文案**——
    /// 自检只跑过 1 人，4 行预览的排布与「谁缺多少」没人看过。
    /// </summary>
    private void _CheckClinicMultiMember(TableDb db)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，医馆多人用例无法进行");
            return;
        }
        TableDb wide = TableWithNChars(4);
        GodotObject state = PartyState(wide, 4);
        string[] ids = state.Get("char_ids").AsStringArray();
        CheckEq(ids.Length, 4, "造了 4 人队伍");
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        int cap_1 = CharacterSheet.create(wide, state, ids[1]).max_hp();
        int cap_2 = CharacterSheet.create(wide, state, ids[2]).max_hp();
        state.Call("set_current_hp", ids[1], cap_1 - 10);
        state.Call("set_current_hp", ids[2], cap_2 - 20);
        ShopScreen screen = GD.Load<PackedScene>(ShopScene).Instantiate<ShopScreen>();
        screen.db_override = wide;
        screen.state_override = state;
        Tree.Root.AddChild(screen);
        screen.setup();
        screen.open_building(Clinic);
        screen.select_tab("service");
        Label heal_label = screen.FindChild("HealLabel", true, false) as Label;
        CheckNotNull(heal_label, "有治疗预览行");
        if (heal_label != null)
        {
            CheckTrue(heal_label.Text.Contains("全队缺 30 点"), $"总缺失 = 10 + 20：{heal_label.Text}");
            CheckTrue(heal_label.Text.Contains("要 60 文"), $"总价 = 30 点 × 2 文：{heal_label.Text}");
        }
        Godot.Collections.Array<Node> heal_rows = screen.FindChildren("Heal_*", "Label", true, false);
        CheckEq(heal_rows.Count, 4, "四个队员各有一行");
        Label second_row = screen.FindChild($"Heal_{ids[1]}", true, false) as Label;
        CheckNotNull(second_row, "带伤队员有自己的行");
        if (second_row != null)
        {
            CheckTrue(second_row.Text.Contains("缺 10"), $"写清他缺多少：{second_row.Text}");
        }
        Button heal_button = screen.FindChild("HealButton", true, false) as Button;
        if (heal_button != null)
        {
            CheckFalse(heal_button.Disabled, "钱够就能按");
        }
        Godot.Collections.Dictionary healed = screen.press_heal();
        CheckTrue(healed["ok"].AsBool(), $"全队治疗成功：{healed["error"].AsString()}");
        CheckEq(healed["cost"].AsInt32(), 60, "花 60 文");
        CheckEq(inventory.money, 940, "钱扣掉 60");
        foreach (string char_id in ids)
        {
            CheckFalse(state.Call("is_wounded", char_id).AsBool(), $"治完都不带伤（{char_id}）");
        }
        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>限量在界面上要看得见：行里写「限量：还剩 N」；要买的数量超过剩余时按钮置灰并写明原因。</summary>
    private void _CheckLimitedStockUi(TableDb db)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，限量界面用例无法进行");
            return;
        }
        TableDb limited = _StockTableWith("shop_grocery", "item_iron", 3);
        GodotObject state = SoloState(db);
        Inventory inventory = _InventoryOf(state);
        inventory.money = 1000;
        ShopScreen screen = GD.Load<PackedScene>(ShopScene).Instantiate<ShopScreen>();
        screen.db_override = limited;
        screen.state_override = state;
        Tree.Root.AddChild(screen);
        screen.setup();
        screen.open_building(Grocery);
        screen.select_tab("buy");

        Label label = screen.FindChild("BuyLabelitem_iron", true, false) as Label;
        CheckNotNull(label, "限量商品行在");
        if (label != null)
        {
            CheckTrue(label.Text.Contains("限量：还剩 3"), $"行里写明还剩几个：{label.Text}");
        }
        Button buy = screen.FindChild("BuyButtonitem_iron", true, false) as Button;
        CheckNotNull(buy, "限量商品有买入按钮");
        if (buy != null)
        {
            CheckFalse(buy.Disabled, "数量 1 时能买");
        }

        // 数量轮到 5（> 剩余 3）：按钮置灰、行里写明库存不够（QTY_STEPS = 1 → 5 → 10 → 1）
        CheckEq(screen.quantity(), 1, "默认数量 1");
        screen.press_qty();
        CheckEq(screen.quantity(), 5, "点一下轮到 5 个");
        Button buy5 = screen.FindChild("BuyButtonitem_iron", true, false) as Button;
        CheckNotNull(buy5, "刷新后的行里还有买入按钮");
        if (buy5 != null)
        {
            CheckTrue(buy5.Disabled, "要买 5 个但只剩 3 个 → 按钮置灰");
        }
        Label label5 = screen.FindChild("BuyLabelitem_iron", true, false) as Label;
        if (label5 != null)
        {
            CheckTrue(label5.Text.Contains("库存不够"), $"行里写明库存不够：{label5.Text}");
        }

        // 轮回到 1：买三次就把这 3 个卖光 → 行还在、写「还剩 0」、按钮仍置灰
        screen.press_qty();
        CheckEq(screen.quantity(), 10, "再点一下轮到 10 个");
        screen.press_qty();
        CheckEq(screen.quantity(), 1, "数量轮回到 1");
        CheckTrue(screen.press_buy("item_iron")["ok"].AsBool(), "买第 1 个");
        CheckTrue(screen.press_buy("item_iron")["ok"].AsBool(), "买第 2 个");
        CheckTrue(screen.press_buy("item_iron")["ok"].AsBool(), "买第 3 个（卖光）");
        CheckEq(inventory.count("item_iron"), 3, "背包里进了 3 个");
        Label sold_label = screen.FindChild("BuyLabelitem_iron", true, false) as Label;
        CheckNotNull(sold_label, "卖光后商品行还在（不是整行消失）");
        if (sold_label != null)
        {
            CheckTrue(sold_label.Text.Contains("还剩 0"), $"写清还剩 0：{sold_label.Text}");
        }
        Button sold_buy = screen.FindChild("BuyButtonitem_iron", true, false) as Button;
        if (sold_buy != null)
        {
            CheckTrue(sold_buy.Disabled, "卖光后按钮置灰");
        }
        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>
    /// 复制一份表库，把某一行 `shop_stock` 的 `stock_limit` 改掉（**只改内存副本，不碰策划的 CSV**）。
    /// 表资源是 Godot 按路径缓存的，所以照施工单坑53 的形状：复制表 → 逐行 `Duplicate(true)` → 写回。
    /// </summary>
    private TableDb _StockTableWith(string shop_id, string item_id, int limit)
    {
        TableDb custom = FreshDb();
        var table = (TableResource)custom.tables["shop_stock"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (ShopStockRow)entry.As<TableRow>().Duplicate(true);
            if (row.shop_id == shop_id && row.item_id == item_id)
            {
                row.stock_limit = limit;
            }
            rows.Add(row);
        }
        table.rows = rows;
        custom.tables["shop_stock"] = table;
        return custom;
    }

    /// <summary>参与过交易的店铺（键排序后拼成一行字符串，便于直接比对两份存档）。</summary>
    private static string _ShopStateKeys(Godot.Collections.Dictionary shop_state)
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (Variant key in shop_state.Keys)
        {
            keys.Add(key.AsString());
        }
        keys.Sort(System.StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static Inventory _InventoryOf(GodotObject state) =>
        state.Get("inventory").As<Inventory>();

    private static int _Purchased(GodotObject state, string building_id, string item_id) =>
        state.Call("purchased_count", building_id, item_id).AsInt32();

    /// <summary>`Godot.Collections.Dictionary` 没有带默认值的取法，自己包一层。</summary>
    private static int _I(Godot.Collections.Dictionary dict, string key, int fallback) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static string _S(Godot.Collections.Dictionary dict, string key, string fallback) =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static Godot.Collections.Array _A(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotArray() : new Godot.Collections.Array();
}
