using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 商店与经济（05_装备与掉落.md「商店与经济」）。C# 版，对位原 `src/core/shop_service.gd`（373 行）。
///
/// 规则全在表里：卖什么／什么价／限不限量看 `shop_stock.csv`，治疗单价看 `building_def.service_price`。
/// 交易状态（累计买入次数、回购列表）按设计**写进存档**，所以这里只做校验与记账，
/// 存档字段的读写走 GameState，不在这里碰 JSON。
///
/// 三条自己定的口径（设计没写，写在这里免得以后猜）：
/// - **只收店里有 `sell_price` 的东西**：掉落装备如果这家店没上架，它就不收。设计没给「估价回收」的公式。
/// - **回购＝撤销卖出**：价格就是当初的卖出价，上限 20 条，满了挤掉最早的。
/// - **医馆治疗**：战斗外气血 v11 起进存档（`GameState.char_hp`），`heal_preview()` 算
///   「全队缺失气血 × service_price」，`heal_party()` 扣钱回满。
/// </summary>
public partial class ShopService : RefCounted
{
    /// <summary>回购列表上限（05 文档建议 20 条）</summary>
    public const int BUYBACK_LIMIT = 20;

    public TableDb db;
    public GodotObject state;

    public ShopService(TableDb table_db, GodotObject game_state)
    {
        db = table_db;
        state = game_state;
    }

    public static ShopService create(TableDb db, GodotObject state) => new(db, state);

    /// <summary>
    /// GDScript 读不到 C# 的 `const`（施工单坑⑩）：`tests/test_shop.gd` 靠这个取值口钉「上限 20」。
    /// （`public static` 那道「只被测试调用」的门限看不见——它的正则认不出 `public static`。）
    /// </summary>
    public static int buyback_limit() => BUYBACK_LIMIT;

    // ------------------------------------------------------------------ 建筑与货架

    public Resource building(string building_id) => db.get_row("building_def", building_id);

    /// <summary>
    /// 货架组 id → 开店用的建筑 id。
    ///
    /// `world_event.we_caravan` 的 `effect_id` 填的是**货架组** `shop_caravan`，而界面与这套服务都按
    /// `building_def.building_id`（`bld_caravan`）走——两张表各自记账，**只有这里知道它们是一回事**。
    /// 认不出就返回空串（调用方如实报「没有对应的货架」，不自己去猜一个店名）。
    /// </summary>
    public static string building_for_shop_group(TableDb db, string shop_id)
    {
        if (db == null || shop_id.Length == 0)
        {
            return "";
        }
        if (db.get_row("building_def", shop_id) != null)
        {
            return shop_id;
        }
        foreach (Variant row_variant in db.rows("building_def"))
        {
            if (row_variant.AsGodotObject() is Resource row
                && row.Get("stock_group").AsString() == shop_id)
            {
                return row.Get("building_id").AsString();
            }
        }
        return "";
    }

    public string building_name(string building_id)
    {
        Resource row = building(building_id);
        return row != null ? row.Get("name_cn").AsString() : building_id;
    }

    /// <summary>货架（按 sort_order）</summary>
    public Godot.Collections.Array stock_rows(string building_id)
    {
        var out_rows = new Godot.Collections.Array();
        Resource row = building(building_id);
        string group = row != null ? row.Get("stock_group").AsString() : "";
        if (group.Length == 0)
        {
            return out_rows;
        }
        var rows = new System.Collections.Generic.List<ShopStockRow>();
        foreach (Variant row_variant in db.rows_where("shop_stock", "shop_id", group))
        {
            if (row_variant.AsGodotObject() is ShopStockRow stock)
            {
                rows.Add(stock);
            }
        }
        rows.Sort((a, b) => a.sort_order.CompareTo(b.sort_order));
        foreach (ShopStockRow stock in rows)
        {
            out_rows.Add(stock);
        }
        return out_rows;
    }

    public Resource stock_row(string building_id, string item_id)
    {
        Resource row = building(building_id);
        string group = row != null ? row.Get("stock_group").AsString() : "";
        if (group.Length == 0)
        {
            return null;
        }
        return db.get_row("shop_stock", $"{group}|{item_id}");
    }

    /// <summary>
    /// 买入价（0 表示不卖）。买入价**可以被打折**：天赋「过日子」那条（`rule:shop_buy_price 0.10`）
    /// 由 `_buy_discount()` 读出来。打折只压买入价、不动卖出价与回购价
    /// （那是「撤销卖出」，改它等于把玩家的钱变少）。
    /// </summary>
    public int buy_price(string building_id, string item_id)
    {
        Resource row = stock_row(building_id, item_id);
        if (row == null)
        {
            return 0;
        }
        int base_price = System.Math.Max(0, row.Get("buy_price").AsInt32());
        if (base_price <= 0)
        {
            return 0;
        }
        double discount = _buy_discount();
        if (discount <= 0.0)
        {
            return base_price;
        }
        // 便宜之后至少 1 文：0 会变成「白送」，那是另一套语义（设计没要）
        return System.Math.Max(
            1,
            (int)System.Math.Floor(base_price * (1.0 - Mathf.Clamp(discount, 0.0, 0.9))));
    }

    /// <summary>
    /// 队伍里最大的那一份「买东西便宜几成」。上限夹在 90%：再高就等于白送，而表里的值是设计配的。
    /// </summary>
    private double _buy_discount() =>
        Mathf.Clamp(TalentService.party_rule_value(db, state, "shop_buy_price", 0.0), 0.0, 0.9);

    public int sell_price(string building_id, string item_id)
    {
        Resource row = stock_row(building_id, item_id);
        return row != null ? System.Math.Max(0, row.Get("sell_price").AsInt32()) : 0;
    }

    /// <summary>还能买几个（stock_limit = 0 表示无限量，返回 −1）</summary>
    public int remaining(string building_id, string item_id)
    {
        Resource row = stock_row(building_id, item_id);
        if (row == null)
        {
            return 0;
        }
        int limit = row.Get("stock_limit").AsInt32();
        if (limit <= 0)
        {
            return -1;
        }
        return System.Math.Max(0, limit - purchased(building_id, item_id));
    }

    public int purchased(string building_id, string item_id) =>
        state != null ? state.Call("purchased_count", building_id, item_id).AsInt32() : 0;

    public int money()
    {
        GodotObject inventory = state?.Get("inventory").AsGodotObject();
        return inventory != null ? inventory.Get("money").AsInt32() : 0;
    }

    public string item_name(string item_id)
    {
        Resource equip = db.get_row("equip_base", item_id);
        if (equip != null)
        {
            return equip.Get("name_cn").AsString();
        }
        Resource item = db.get_row("item_base", item_id);
        return item != null ? item.Get("name_cn").AsString() : item_id;
    }

    // ------------------------------------------------------------------ 买入

    /// <summary>
    /// 能不能买：{ok, error, unit_price, total, remaining, short}。
    /// `short` ＝ 铜钱不够时**还差多少**（够就是 0）——界面那行「还差 172」直接读它，
    /// 别在面板里再减一遍（改价改规则不会只改一半）。
    /// </summary>
    public Godot.Collections.Dictionary can_buy(string building_id, string item_id, int qty = 1)
    {
        int unit = buy_price(building_id, item_id);
        int count = System.Math.Max(1, qty);
        if (state == null || state.Get("inventory").AsGodotObject() == null)
        {
            return _deny("没有会话状态", unit, count);
        }
        Resource row = stock_row(building_id, item_id);
        if (row == null || unit <= 0)
        {
            return _deny("这家店不卖这个", unit, count);
        }
        Resource equip = db.get_row("equip_base", item_id);
        if (equip != null && equip.Get("drop_only").AsBool())
        {
            string drop_only_name = equip.Get("name_cn").AsString();
            return _deny($"{drop_only_name} 只能掉落获得，店里不卖", unit, count);
        }
        if (db.get_row("item_base", item_id) == null && equip == null)
        {
            return _deny($"表里没有这个商品：{item_id}", unit, count);
        }
        int left = remaining(building_id, item_id);
        if (left >= 0 && count > left)
        {
            return _deny($"库存不够（还能买 {left} 个）", unit, count);
        }
        int total = unit * count;
        if (money() < total)
        {
            Godot.Collections.Dictionary denied =
                _deny($"铜钱不够（需要 {total}，现有 {money()}）", unit, count);
            denied["short"] = total - money();
            return denied;
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "unit_price", unit },
            { "total", total }, { "remaining", left }, { "short", 0 },
        };
    }

    private static Godot.Collections.Dictionary _deny(string reason, int unit, int qty) =>
        new()
        {
            { "ok", false }, { "error", reason }, { "unit_price", unit },
            { "total", unit * qty }, { "remaining", 0 }, { "short", 0 },
        };

    /// <summary>买入。返回 {ok, error, spent, got: [{item_id, qty}], equipment: [instance_id]}</summary>
    public Godot.Collections.Dictionary buy(string building_id, string item_id, int qty = 1)
    {
        int count = System.Math.Max(1, qty);
        Godot.Collections.Dictionary check = can_buy(building_id, item_id, count);
        if (!check["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", check["error"].AsString() },
                { "spent", 0 }, { "got", new Godot.Collections.Array() },
                { "equipment", new Godot.Collections.Array() },
            };
        }
        var equipment = new Godot.Collections.Array();
        var got = new Godot.Collections.Array();
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        if (db.get_row("equip_base", item_id) != null)
        {
            for (int index = 0; index < count; index++)
            {
                // 店里卖的是白板装备：不带随机词条
                string instance_id = inventory
                    .Call("add_equipment", db, item_id, new Godot.Collections.Array())
                    .AsString();
                if (instance_id.Length == 0)
                {
                    return new Godot.Collections.Dictionary
                    {
                        { "ok", false }, { "error", "生成装备失败" },
                        { "spent", 0 }, { "got", new Godot.Collections.Array() },
                        { "equipment", new Godot.Collections.Array() },
                    };
                }
                equipment.Add(instance_id);
            }
            got.Add(new Godot.Collections.Dictionary { { "item_id", item_id }, { "qty", count } });
        }
        else
        {
            Godot.Collections.Dictionary added = inventory
                .Call("add_item", db, item_id, count).AsGodotDictionary();
            if (!added["ok"].AsBool())
            {
                return new Godot.Collections.Dictionary
                {
                    { "ok", false }, { "error", added["error"].AsString() },
                    { "spent", 0 }, { "got", new Godot.Collections.Array() },
                    { "equipment", new Godot.Collections.Array() },
                };
            }
            got.Add(new Godot.Collections.Dictionary
            {
                { "item_id", item_id },
                { "qty", added["added"].AsInt32() },
                { "overflow", added["overflow"].AsInt32() },
            });
            if (added["overflow"].AsInt32() > 0)
            {
                // 背包满了：只按真正放进去的数量收钱，别把玩家的钱扣在空气上
                count = added["added"].AsInt32();
            }
        }
        int spent = check["unit_price"].AsInt32() * count;
        inventory.Set("money", inventory.Get("money").AsInt32() - spent);
        state.Call("record_purchase", building_id, item_id, count);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "spent", spent },
            { "got", got }, { "equipment", equipment },
        };
    }

    // ------------------------------------------------------------------ 卖出

    /// <summary>能不能卖：{ok, error, unit_price, total}。sell_id 传物品 id 或装备实例 id（`eq_sword_01#3`）都行。</summary>
    public Godot.Collections.Dictionary can_sell(string building_id, string sell_id, int qty = 1)
    {
        int count = System.Math.Max(1, qty);
        GodotObject inventory = state?.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return _sell_deny("没有会话状态");
        }
        string base_id = sell_id;
        bool is_equipment = inventory.Call("has_equipment", sell_id).AsBool();
        if (is_equipment)
        {
            base_id = inventory.Call("base_of", sell_id).AsString();
            if (count > 1)
            {
                return _sell_deny("装备一次只卖一件");
            }
            if (inventory.Call("is_equipped", sell_id).AsBool())
            {
                return _sell_deny("先卸下装备再卖");
            }
        }
        else
        {
            Resource item = db.get_row("item_base", sell_id);
            if (item == null)
            {
                return _sell_deny("背包里没有这个");
            }
            if (item.Get("is_key_item").AsBool())
            {
                string key_name = item.Get("name_cn").AsString();
                return _sell_deny($"{key_name} 是钥匙道具，不能卖");
            }
            int owned = inventory.Call("count", sell_id).AsInt32();
            if (owned < count)
            {
                return _sell_deny($"数量不足（有 {owned}）");
            }
        }
        int unit = sell_price(building_id, base_id);
        if (unit <= 0)
        {
            return _sell_deny($"{item_name(base_id)} 这家店不收");
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "unit_price", unit },
            { "total", unit * count }, { "base_id", base_id },
        };
    }

    private static Godot.Collections.Dictionary _sell_deny(string reason) =>
        new()
        {
            { "ok", false }, { "error", reason }, { "unit_price", 0 }, { "total", 0 },
        };

    /// <summary>卖出。返回 {ok, error, earned, entry}</summary>
    public Godot.Collections.Dictionary sell(string building_id, string sell_id, int qty = 1)
    {
        int count = System.Math.Max(1, qty);
        Godot.Collections.Dictionary check = can_sell(building_id, sell_id, count);
        if (!check["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", check["error"].AsString() },
                { "earned", 0 }, { "entry", new Godot.Collections.Dictionary() },
            };
        }
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        string base_id = check["base_id"].AsString();
        bool is_equipment = inventory.Call("has_equipment", sell_id).AsBool();
        var entry = new Godot.Collections.Dictionary
        {
            { "item_id", base_id }, { "qty", count }, { "price", check["unit_price"].AsInt32() },
        };
        if (is_equipment)
        {
            entry["instance_id"] = sell_id;
            entry["affixes"] = inventory.Call("affixes_of", sell_id).AsGodotArray();
            Godot.Collections.Dictionary removed = inventory
                .Call("remove_equipment", sell_id).AsGodotDictionary();
            if (!removed["ok"].AsBool())
            {
                return new Godot.Collections.Dictionary
                {
                    { "ok", false }, { "error", removed["error"].AsString() },
                    { "earned", 0 }, { "entry", new Godot.Collections.Dictionary() },
                };
            }
        }
        else
        {
            Godot.Collections.Dictionary removed_item = inventory
                .Call("remove_item", db, sell_id, count).AsGodotDictionary();
            if (!removed_item["ok"].AsBool())
            {
                return new Godot.Collections.Dictionary
                {
                    { "ok", false }, { "error", removed_item["error"].AsString() },
                    { "earned", 0 }, { "entry", new Godot.Collections.Dictionary() },
                };
            }
        }
        int earned = check["total"].AsInt32();
        inventory.Set("money", inventory.Get("money").AsInt32() + earned);
        state.Call("push_buyback", building_id, entry, BUYBACK_LIMIT);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "earned", earned }, { "entry", entry },
        };
    }

    // ------------------------------------------------------------------ 回购

    public Godot.Collections.Array buyback_rows(string building_id) =>
        state != null
            ? state.Call("buyback_of", building_id).AsGodotArray()
            : new Godot.Collections.Array();

    /// <summary>
    /// 从回购列表买回来（价格就是当初的卖出价）。
    ///
    /// **背包放不下时只买放得下的那些**：与买入路径同一口径（只按真正放进去的数量收钱），
    /// 剩下的数量留在回购列表里。以前这里按全价扣钱、整条出列，多出来的部分就被吞了。
    /// </summary>
    public Godot.Collections.Dictionary buy_back(string building_id, int index)
    {
        if (state == null || state.Get("inventory").AsGodotObject() == null)
        {
            return _back_deny("没有会话状态", 0);
        }
        Godot.Collections.Array rows = buyback_rows(building_id);
        if (index < 0 || index >= rows.Count)
        {
            return _back_deny("回购列表里没有这一条", 0);
        }
        Godot.Collections.Dictionary entry = rows[index].AsGodotDictionary();
        int price = entry.ContainsKey("price") ? entry["price"].AsInt32() : 0;
        int wanted = System.Math.Max(1, entry.ContainsKey("qty") ? entry["qty"].AsInt32() : 1);
        string base_id = entry.ContainsKey("item_id") ? entry["item_id"].AsString() : "";
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        bool is_equipment = db.get_row("equip_base", base_id) != null;
        int fits = is_equipment
            ? 1
            : System.Math.Min(wanted, inventory.Call("room_for", db, base_id).AsInt32());
        if (fits <= 0)
        {
            int stacked = inventory.Call("count", base_id).AsInt32();
            return _back_deny(
                $"背包放不下（{item_name(base_id)} 已经堆满 {stacked} 个）",
                0);
        }
        int cost = price * fits;
        if (money() < cost)
        {
            return _back_deny($"铜钱不够（需要 {cost}）", cost);
        }
        if (is_equipment)
        {
            var affixes = new Godot.Collections.Array();
            string instance_id = entry.ContainsKey("instance_id") ? entry["instance_id"].AsString() : "";
            if (instance_id.Length != 0)
            {
                affixes = entry.ContainsKey("affixes")
                    ? entry["affixes"].AsGodotArray()
                    : new Godot.Collections.Array();
            }
            string persisted = inventory.Call("add_equipment", db, base_id, affixes).AsString();
            if (persisted.Length == 0)
            {
                return _back_deny("回购失败（造不出装备）", cost);
            }
        }
        else
        {
            Godot.Collections.Dictionary added = inventory
                .Call("add_item", db, base_id, fits).AsGodotDictionary();
            if (!added["ok"].AsBool() || added["added"].AsInt32() != fits)
            {
                return _back_deny(added["error"].AsString(), cost);
            }
        }
        inventory.Set("money", inventory.Get("money").AsInt32() - cost);
        if (fits < wanted)
        {
            state.Call("set_buyback_qty", building_id, index, wanted - fits);
        }
        else
        {
            state.Call("pop_buyback", building_id, index);
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "cost", cost }, { "item_id", base_id },
            { "received", fits }, { "partial", fits < wanted },
        };
    }

    private static Godot.Collections.Dictionary _back_deny(string reason, int cost) =>
        new() { { "ok", false }, { "error", reason }, { "cost", cost } };

    // ------------------------------------------------------------------ 治疗服务

    public bool has_heal(string building_id)
    {
        Resource row = building(building_id);
        return row != null && row.Get("service_id").AsString() == "heal";
    }

    /// <summary>治疗费用 = 缺失气血 × service_price（05 文档）</summary>
    public int heal_cost(string building_id, int missing_hp)
    {
        Resource row = building(building_id);
        if (row == null || row.Get("service_id").AsString() != "heal")
        {
            return 0;
        }
        return System.Math.Max(
            0,
            (int)System.Math.Ceiling(
                System.Math.Max(0, missing_hp) * row.Get("service_price").AsDouble()));
    }

    public double heal_price(string building_id)
    {
        Resource row = building(building_id);
        return row != null ? row.Get("service_price").AsDouble() : 0.0;
    }

    /// <summary>
    /// 治疗预览：全队缺多少点气血、要花多少钱（不改任何状态，界面用它决定按钮亮不亮）。
    /// 返回 {ok, error, missing, cost, rows: [{char_id, name, hp, max_hp, missing}]}
    /// </summary>
    public Godot.Collections.Dictionary heal_preview(string building_id)
    {
        var out_result = new Godot.Collections.Dictionary
        {
            { "ok", false }, { "error", "" }, { "missing", 0 },
            { "cost", 0 }, { "rows", new Godot.Collections.Array() },
        };
        if (state == null || state.Get("inventory").AsGodotObject() == null)
        {
            out_result["error"] = "没有会话状态";
            return out_result;
        }
        if (!has_heal(building_id))
        {
            out_result["error"] = "这家店没有治疗服务";
            return out_result;
        }
        var rows = new Godot.Collections.Array();
        int missing = 0;
        // 用 PartyBuilder 走同一套「等级 + 加点 + 装备 + 战斗外气血」，
        // 免得医馆按另一套算法算上限（两套算法迟早对不上）
        foreach (string char_id in state.Get("char_ids").AsStringArray())
        {
            BattleActor actor = PartyBuilder.build_actor(db, state, char_id);
            if (actor == null)
            {
                continue;
            }
            int max_hp = actor.max_hp();
            int hp = Mathf.Clamp(actor.hp, 1, max_hp);
            int gap = System.Math.Max(0, max_hp - hp);
            missing += gap;
            rows.Add(new Godot.Collections.Dictionary
            {
                { "char_id", char_id },
                { "name", actor.display_name },
                { "hp", hp },
                { "max_hp", max_hp },
                { "missing", gap },
            });
        }
        out_result["ok"] = true;
        out_result["missing"] = missing;
        out_result["cost"] = heal_cost(building_id, missing);
        out_result["rows"] = rows;
        return out_result;
    }

    /// <summary>治疗：扣钱、全队回满。返回 {ok, error, cost, healed}</summary>
    public Godot.Collections.Dictionary heal_party(string building_id)
    {
        Godot.Collections.Dictionary preview = heal_preview(building_id);
        if (!preview["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", preview["error"].AsString() },
                { "cost", 0 }, { "healed", 0 },
            };
        }
        int missing = preview["missing"].AsInt32();
        if (missing <= 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "气血是满的" }, { "cost", 0 }, { "healed", 0 },
            };
        }
        int cost = preview["cost"].AsInt32();
        if (money() < cost)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", $"铜钱不够（需要 {cost}）" },
                { "cost", cost }, { "healed", 0 },
            };
        }
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        inventory.Set("money", inventory.Get("money").AsInt32() - cost);
        state.Call("heal_all");
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "cost", cost }, { "healed", missing },
        };
    }
}
