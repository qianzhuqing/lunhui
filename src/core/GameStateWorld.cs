using Godot;

namespace Lunhui.Core;

/// <summary>
/// `GameState` 的「世界里发生过什么」那一半（对位原 `src/core/game_state.gd` 从 `_read_shop_state`
/// 之后的商店／事件／旗标／首杀／战斗外气血／揭开／副本／保底几节）。拆出来的理由：单文件 800 行硬上限。
///
/// 这些记录的共同点：**都是「一次性事件」的痕迹**，写进去就不该再回头改，所以每个写入口都做成
/// 「已经记过就返回 false／0」的幂等形状——重复触发（每次按 E 都会查一遍）是常态。
/// </summary>
public partial class GameState
{
    /// <summary>
    /// 商店交易状态（05_装备与掉落.md「商品与存档」）：累计买入次数与回购列表。
    /// 读的时候返回一份规整过的副本，不顺手往存档里塞空条目。
    /// </summary>
    public Godot.Collections.Dictionary shop_state_of(string building_id)
    {
        Godot.Collections.Dictionary entry = shop_state.ContainsKey(building_id)
            ? shop_state[building_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            {
                "purchased",
                entry.ContainsKey("purchased")
                    ? entry["purchased"].AsGodotDictionary().Duplicate()
                    : new Godot.Collections.Dictionary()
            },
            {
                "buyback",
                entry.ContainsKey("buyback")
                    ? entry["buyback"].AsGodotArray().Duplicate(true)
                    : new Godot.Collections.Array()
            },
        };
    }

    public int purchased_count(string building_id, string item_id)
    {
        Godot.Collections.Dictionary entry = shop_state.ContainsKey(building_id)
            ? shop_state[building_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        if (!entry.ContainsKey("purchased"))
        {
            return 0;
        }

        Godot.Collections.Dictionary purchased = entry["purchased"].AsGodotDictionary();
        return purchased.ContainsKey(item_id) ? purchased[item_id].AsInt32() : 0;
    }

    /// <summary>记一次买入，返回累计次数（限量商品的剩余量 = stock_limit − 这个数）。</summary>
    public int record_purchase(string building_id, string item_id, int qty)
    {
        Godot.Collections.Dictionary entry = _ensure_shop_entry(building_id);
        Godot.Collections.Dictionary purchased = entry["purchased"].AsGodotDictionary();
        purchased[item_id] = (purchased.ContainsKey(item_id) ? purchased[item_id].AsInt32() : 0) + Mathf.Max(0, qty);
        entry["purchased"] = purchased;
        shop_state[building_id] = entry;
        return purchased[item_id].AsInt32();
    }

    public Godot.Collections.Array buyback_of(string building_id)
    {
        Godot.Collections.Dictionary entry = shop_state.ContainsKey(building_id)
            ? shop_state[building_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        return entry.ContainsKey("buyback")
            ? entry["buyback"].AsGodotArray().Duplicate(true)
            : new Godot.Collections.Array();
    }

    /// <summary>卖出的东西进回购列表；超过上限挤掉最早的（`limit = 0` ＝ 不设上限，读档用）。</summary>
    public void push_buyback(string building_id, Godot.Collections.Dictionary record, int limit)
    {
        Godot.Collections.Dictionary entry = _ensure_shop_entry(building_id);
        Godot.Collections.Array list = entry["buyback"].AsGodotArray();
        list.Add(record.Duplicate(true));
        while (limit > 0 && list.Count > limit)
        {
            list.RemoveAt(0);
        }

        entry["buyback"] = list;
        shop_state[building_id] = entry;
    }

    public void pop_buyback(string building_id, int index)
    {
        Godot.Collections.Dictionary entry = _ensure_shop_entry(building_id);
        Godot.Collections.Array list = entry["buyback"].AsGodotArray();
        if (index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
        }

        entry["buyback"] = list;
        shop_state[building_id] = entry;
    }

    /// <summary>回购条目只买走一部分（背包放不下时）：把这条的数量改成剩下的；qty &lt;= 0 就整条出列。</summary>
    public void set_buyback_qty(string building_id, int index, int qty)
    {
        Godot.Collections.Dictionary entry = _ensure_shop_entry(building_id);
        Godot.Collections.Array list = entry["buyback"].AsGodotArray();
        if (index >= 0 && index < list.Count)
        {
            if (qty <= 0)
            {
                list.RemoveAt(index);
            }
            else
            {
                Godot.Collections.Dictionary record = list[index].AsGodotDictionary();
                record["qty"] = qty;
                list[index] = record;
            }
        }

        entry["buyback"] = list;
        shop_state[building_id] = entry;
    }

    private Godot.Collections.Dictionary _ensure_shop_entry(string building_id)
    {
        Godot.Collections.Dictionary entry = shop_state.ContainsKey(building_id)
            ? shop_state[building_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        if (!entry.ContainsKey("purchased"))
        {
            entry["purchased"] = new Godot.Collections.Dictionary();
        }

        if (!entry.ContainsKey("buyback"))
        {
            entry["buyback"] = new Godot.Collections.Array();
        }

        return entry;
    }

    // ------------------------------------------------------------------ 事件判定与旗标

    /// <summary>事件判定：记结果（done / failed）。</summary>
    public void record_event_check(string check_id, string result)
    {
        if (string.IsNullOrEmpty(check_id))
        {
            return;
        }

        event_checks[check_id] = result;
    }

    public string event_check_result(string check_id)
        => event_checks.ContainsKey(check_id) ? event_checks[check_id].AsString() : "";

    /// <summary>点亮／熄灭一个旗标。**两个参数都要传**（`value` 没有默认值，见坑⑲）。</summary>
    public void set_flag(string flag_id, bool value)
    {
        if (string.IsNullOrEmpty(flag_id))
        {
            return;
        }

        flags[flag_id] = value;
    }

    public bool has_flag(string flag_id) => flags.ContainsKey(flag_id) && flags[flag_id].AsBool();

    // ------------------------------------------------------------------ 首杀（v10）

    /// <summary>
    /// 首杀：某个掉落组第一次被打死时记一次，之后同一组不再出首杀限定掉落。
    /// 返回 true 表示「这次算首杀」——战斗结算拿它去问 DropResolver。
    /// 记录进存档（v10 起），否则退出重进就能反复领首杀固定掉落（刷子游戏的经典漏洞）。
    /// </summary>
    public bool mark_first_kill(string drop_group)
    {
        if (string.IsNullOrEmpty(drop_group))
        {
            return false;
        }

        if (first_kills.ContainsKey(drop_group))
        {
            return false;
        }

        first_kills[drop_group] = true;
        return true;
    }

    public bool has_first_kill(string drop_group)
        => !string.IsNullOrEmpty(drop_group) && first_kills.ContainsKey(drop_group);

    public int first_kill_count() => first_kills.Count;

    // ------------------------------------------------------------------ 战斗外气血（v11）

    /// <summary>
    /// 当前气血；**没有记录返回 -1**（调用方按满血处理）。
    /// 不返回 0 是因为 0 在游戏里表示「倒下」，而「没记录」表示「没受过伤」。
    /// </summary>
    public int current_hp_of(string char_id)
        => string.IsNullOrEmpty(char_id) || !char_hp.ContainsKey(char_id) ? -1 : char_hp[char_id].AsInt32();

    /// <summary>
    /// 记一次战斗后的剩余气血。**下限 1**：倒下的角色回图时留 1 点，
    /// 免得「全队 0 血」把存档卡死（设计没定败北代价，这条口径记在交接表等确认）。
    /// </summary>
    public void set_current_hp(string char_id, int hp)
    {
        if (string.IsNullOrEmpty(char_id))
        {
            return;
        }

        char_hp[char_id] = Mathf.Max(1, hp);
    }

    /// <summary>满血：直接把记录清掉（没有记录就是满血）。</summary>
    public void heal_all() => char_hp.Clear();

    public bool is_wounded(string char_id) => char_hp.ContainsKey(char_id);

    // ------------------------------------------------------------------ 大地图揭开（v8）

    /// <summary>大地图节点揭开（02_地图与明雷.md：玩家的地图是自己走出来的）。</summary>
    public bool reveal_node(string node_id)
    {
        if (string.IsNullOrEmpty(node_id) || revealed_nodes.Contains(node_id))
        {
            return false;
        }

        revealed_nodes.Add(node_id);
        return true;
    }

    public bool is_node_revealed(string node_id) => revealed_nodes.Contains(node_id);

    // ------------------------------------------------------------------ 副本永久记录（v7）

    /// <summary>副本永久记录（完成度用）。kind ∈ chests / triggers / rooms / rooms_entered / bosses。</summary>
    public bool record_dungeon(string scene_id, string kind, string key)
    {
        if (string.IsNullOrEmpty(scene_id) || string.IsNullOrEmpty(key))
        {
            return false;
        }

        Godot.Collections.Dictionary entry = dungeon_records.ContainsKey(scene_id)
            ? dungeon_records[scene_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        Godot.Collections.Array list = entry.ContainsKey(kind)
            ? entry[kind].AsGodotArray()
            : new Godot.Collections.Array();
        if (list.Contains(key))
        {
            return false;
        }

        list.Add(key);
        entry[kind] = list;
        dungeon_records[scene_id] = entry;
        return true;
    }

    public Godot.Collections.Dictionary dungeon_record(string scene_id)
    {
        Godot.Collections.Dictionary entry = dungeon_records.ContainsKey(scene_id)
            ? dungeon_records[scene_id].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        var out_record = new Godot.Collections.Dictionary();
        foreach (string kind in new[] { "chests", "triggers", "rooms", "rooms_entered", "bosses" })
        {
            out_record[kind] = entry.ContainsKey(kind)
                ? entry[kind].AsGodotArray().Duplicate()
                : new Godot.Collections.Array();
        }

        return out_record;
    }

    // ------------------------------------------------------------------ 保底计数器（v7）

    /// <summary>保底计数器：从存档读出来用，打完再存回去（保底要跨战斗、跨难度继承）。</summary>
    public PityTracker pity_tracker()
    {
        PityTracker tracker = PityTracker.create();
        tracker.from_dict(pity);
        return tracker;
    }

    /// <summary>
    /// 把保底计数器写回存档。参数收 `GodotObject`（跨语言口）：GDScript 侧把 `pity_tracker()`
    /// 的返回值原样传回来，`to_dict()` 走 `Call`，换实现也不用改调用方。
    /// </summary>
    public void store_pity(GodotObject tracker)
    {
        if (tracker == null)
        {
            return;
        }

        pity = tracker.Call("to_dict").AsGodotDictionary();
    }
}
