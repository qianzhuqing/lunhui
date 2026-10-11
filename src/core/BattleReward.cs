using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 战斗结算入账：把经验、铜钱与掉落写进存档（C# 版，对位原 `src/core/battle_reward.gd`）。
///
/// 战斗界面（`battle_screen.gd`）打完就调 `apply()`；扫荡、宝箱、隐藏内容也走这里；
/// 掉落条目可以来自 `DropResolver`，也可以来自宝箱表。
///
/// **`state` 还是 GDScript 的 `GameState`**，所以这里整份走 `Get/Set/Call`
/// （与 `LevelService`／`PracticeService` 同一套迁移期写法）；
/// 背包（`Inventory`）已是 C#，`state.inventory` 拿到的就是 `Inventory` 实例。
/// </summary>
public partial class BattleReward : RefCounted
{
    /// <summary>
    /// `result` 传 `BattleSimulator.simulate()` 的返回值。
    /// `roller` 传 `AffixRoller` 时，掉落的装备会滚随机词条（战斗界面与小地图都会传；
    /// 传 `null` 就现开一个随机源）。
    /// </summary>
    public static Godot.Collections.Dictionary apply(
        TableDb db,
        GodotObject state,
        Godot.Collections.Dictionary result,
        Godot.Collections.Array drops,
        AffixRoller roller)
    {
        Godot.Collections.Dictionary rewards = result.ContainsKey("rewards")
            ? result["rewards"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        int exp = rewards.ContainsKey("exp") ? rewards["exp"].AsInt32() : 0;
        int money = rewards.ContainsKey("money") ? rewards["money"].AsInt32() : 0;
        return grant(db, state, exp, money, drops, roller);
    }

    /// <summary>直接入账。返回 `{ok, exp, money, items, equipment, errors, overflow, level_ups, level_up_text}`。</summary>
    public static Godot.Collections.Dictionary grant(
        TableDb db,
        GodotObject state,
        int exp,
        int money,
        Godot.Collections.Array drops,
        AffixRoller roller)
    {
        var items = new Godot.Collections.Array();
        var equipment = new Godot.Collections.Array();
        var errors = new Godot.Collections.Array();
        // 背包放不下的掉落：算「结算成功但没全捡起来」，不把整场判成失败
        var overflow = new Godot.Collections.Array();
        var level_ups = new Godot.Collections.Array();
        var out_dict = new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "exp", Mathf.Max(exp, 0) },
            { "money", Mathf.Max(money, 0) },
            { "items", items },
            { "equipment", equipment },
            { "errors", errors },
            { "overflow", overflow },
            { "level_ups", level_ups },
            { "level_up_text", "" },
        };
        if (state == null)
        {
            out_dict["ok"] = false;
            errors.Add("没有会话状态");
            return out_dict;
        }

        GodotObject inventory = state.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            inventory = new Inventory();
            state.Set("inventory", inventory);
        }
        AffixRoller affix_roller = roller ?? AffixRoller.create(db, RngService.create_random());

        // 经验进队伍池，然后立刻把付得起的等级换出来（level_growth.exp_to_next）
        state.Set("party_exp", state.Get("party_exp").AsInt32() + out_dict["exp"].AsInt32());
        LevelService levels = LevelService.create(db, state);
        Godot.Collections.Dictionary level_result = levels.apply_available_levels();
        Godot.Collections.Array changes = level_result["changes"].AsGodotArray();
        foreach (Variant change in changes)
        {
            level_ups.Add(change);
        }
        out_dict["level_ups"] = level_ups;
        out_dict["level_up_text"] = levels.describe(changes);
        inventory.Set("money", inventory.Get("money").AsInt32() + out_dict["money"].AsInt32());

        foreach (Variant entry_value in drops)
        {
            Godot.Collections.Dictionary entry = entry_value.AsGodotDictionary();
            string item_id = entry.ContainsKey("item_id") ? entry["item_id"].AsString() : "";
            int qty = Mathf.Max(1, entry.ContainsKey("qty") ? entry["qty"].AsInt32() : 1);
            if (item_id.Length == 0)
            {
                continue;
            }
            Godot.Collections.Dictionary one = grant_item(db, state, item_id, qty, affix_roller);
            string kind = one["kind"].AsString();
            if (kind == "money")
            {
                // 钱在 grant_item 里已经加到 inventory.money 了
                continue;
            }
            if (kind == "equip")
            {
                if (one["ok"].AsBool())
                {
                    foreach (Variant instance_id in one["instance_ids"].AsGodotArray())
                    {
                        equipment.Add(instance_id);
                    }
                }
                else
                {
                    errors.Add($"掉落的 {item_id} 没能生成装备实例");
                }
                continue;
            }
            if (!one["ok"].AsBool())
            {
                overflow.Add(new Godot.Collections.Dictionary
                {
                    { "item_id", item_id },
                    { "qty", qty },
                    { "reason", one["error"].AsString() },
                });
                continue;
            }
            int leftover = one["overflow"].AsInt32();
            items.Add(new Godot.Collections.Dictionary
            {
                { "item_id", item_id },
                { "qty", one["qty"].AsInt32() },
                { "overflow", leftover },
            });
            if (leftover > 0)
            {
                overflow.Add(new Godot.Collections.Dictionary
                {
                    { "item_id", item_id },
                    { "qty", leftover },
                    { "reason", "背包已满" },
                });
            }
        }
        out_dict["ok"] = errors.Count == 0;
        return out_dict;
    }

    /// <summary>
    /// 把**一件奖励物品**入账：货币直接进钱、装备建实例、其余堆叠。
    /// 返回 `{ok, kind, item_id, qty, money, instance_ids, overflow, error}`。
    ///
    /// 这是「掉落条目怎么变成玩家资产」的**唯一口径**——别在别处再写一遍 `if item_id == "item_money"`。
    /// 起因：`ev_gamble`（赌局赢钱）走的是 `EventCheckService` 自己的 `add_item`，
    /// 于是「赢钱」把 `item_money` 当成一件可堆叠物品塞进了背包，`inventory.money` 一文没动——
    /// 玩家在客栈赌赢一把，得到的是背包里一行「铜钱」，买不了任何东西。
    ///
    /// `qty` 与 `roller` **不留可选参数**（C# 的默认值对 GDScript 不生效，见施工单坑②）：
    /// 调用方写全 `grant_item(db, state, item_id, 1, null)`。
    /// </summary>
    public static Godot.Collections.Dictionary grant_item(
        TableDb db,
        GodotObject state,
        string item_id,
        int qty,
        AffixRoller roller)
    {
        var result = new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "kind", "" },
            { "item_id", item_id },
            { "qty", 0 },
            { "money", 0 },
            { "instance_ids", new Godot.Collections.Array() },
            { "overflow", 0 },
            { "error", "" },
        };
        GodotObject inventory = state?.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            result["ok"] = false;
            result["error"] = "没有会话状态";
            return result;
        }
        if (item_id.Length == 0)
        {
            result["ok"] = false;
            result["error"] = "没有奖励物品 id";
            return result;
        }
        int amount = Mathf.Max(1, qty);
        if (item_id == "item_money")
        {
            inventory.Set("money", inventory.Get("money").AsInt32() + amount);
            result["kind"] = "money";
            result["money"] = amount;
            result["qty"] = amount;
            return result;
        }
        if (db.get_row("equip_base", item_id) != null)
        {
            AffixRoller use_roller = roller ?? AffixRoller.create(db, RngService.create_random());
            Godot.Collections.Array instance_ids = result["instance_ids"].AsGodotArray();
            for (int index = 0; index < amount; index++)
            {
                string instance_id = inventory
                    .Call("add_equipment", db, item_id, use_roller.roll_for(item_id))
                    .AsString();
                if (instance_id.Length == 0)
                {
                    result["ok"] = false;
                    result["error"] = $"装备 {item_id} 没能生成实例";
                    return result;
                }
                instance_ids.Add(instance_id);
            }
            result["kind"] = "equip";
            result["qty"] = amount;
            return result;
        }
        Godot.Collections.Dictionary added = inventory
            .Call("add_item", db, item_id, amount)
            .AsGodotDictionary();
        result["kind"] = "item";
        result["qty"] = added["added"].AsInt32();
        result["overflow"] = added["overflow"].AsInt32();
        result["ok"] = added["ok"].AsBool();
        if (!added["ok"].AsBool())
        {
            result["error"] = added["error"].AsString();
        }
        return result;
    }
}
