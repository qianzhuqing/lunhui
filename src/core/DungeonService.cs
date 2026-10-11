using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 副本完成度与扫荡（设计 03「商业与重复性」「完成度」，对位原 `src/core/dungeon_service.gd`）。
///
/// 完成度四项（只作为追求目标，**不影响奖励**）：宝箱开启数／隐藏房间进入数／隐藏 Boss 击败／事件触发。
/// 数据来自两张表 + 存档里的永久记录 `GameState.dungeon_records`：
///   `dungeon_room`   房间、层、宝箱（chest_id）、敌人队伍、房间类型
///   `hidden_trigger` 隐藏点位的类型与奖励类型
///
/// 扫荡：**已通关层**可扫荡，只结算掉落（不给经验）——「已通关」＝该层所有带敌人的房间都清过，
/// 记录同样落在 `dungeon_records.rooms` 里（打一场就记一次，永久保留）。
///
/// **跨语言面**：`GameState`／`Inventory` 是 C#（`PartyBuilder` 也在第三十二批搬完了），所以 `dungeon_record`／
/// `pity_tracker`／`store_pity`／`build_actors` 走 `Call(...)`，`state.difficulty_id` 走 `Get(...)`
/// （施工单坑⑥／坑67：属性用 `Get`，方法用 `Call`）。`state` 本体收 `GodotObject`。
/// </summary>
public partial class DungeonService : RefCounted
{
    /// <summary>隐藏 Boss 的判定：威胁标记为紫色，且不在 boss 房间的队伍里（那些是章节 Boss）。</summary>
    public const string HIDDEN_BOSS_THREAT = "purple";

    private readonly TableDb _db;
    private readonly GodotObject _state;
    private readonly RngService _rng;

    public DungeonService(TableDb db, GodotObject state, RngService rng)
    {
        _db = db;
        _state = state;
        _rng = rng ?? RngService.create_random();
    }

    /// <summary>对位 GDScript 的 `.new(db, state, rng)`。`rng` 传 `null` 就用随机种子。</summary>
    public static DungeonService create(TableDb db, GodotObject state, RngService rng) =>
        new(db, state, rng);

    /// <summary>这张图的房间行（按层、按表顺序）。同层保持表里的原序，保证确定（对位 `sort_custom`）。</summary>
    public Godot.Collections.Array rooms_of(string scene_id)
    {
        var picked = new System.Collections.Generic.List<(int Floor, int Index, DungeonRoomRow Row)>();
        int index = 0;
        foreach (Variant entry in _db.rows("dungeon_room"))
        {
            if (entry.AsGodotObject() is DungeonRoomRow row && row.scene_id == scene_id)
            {
                picked.Add((row.floor, index, row));
            }

            index += 1;
        }

        picked.Sort((a, b) =>
            a.Floor != b.Floor ? a.Floor.CompareTo(b.Floor) : a.Index.CompareTo(b.Index));
        var out_rows = new Godot.Collections.Array();
        foreach ((int Floor, int Index, DungeonRoomRow Row) item in picked)
        {
            out_rows.Add(item.Row);
        }

        return out_rows;
    }

    public Godot.Collections.Array trigger_rows(string scene_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in _db.rows("hidden_trigger"))
        {
            if (entry.AsGodotObject() is HiddenTriggerRow row && row.scene_id == scene_id)
            {
                out_rows.Add(row);
            }
        }

        return out_rows;
    }

    /// <summary>章节 Boss 的敌人 id（boss 房间的队伍成员）——用来把「隐藏 Boss」和它区分开。</summary>
    public string[] chapter_boss_ids(string scene_id)
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in rooms_of(scene_id))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row == null || row.room_type != "boss")
            {
                continue;
            }

            if (_db.get_row("enemy_team", row.enemy_team) is not EnemyTeamRow team)
            {
                continue;
            }

            foreach (Variant member_entry in team.parsed_members())
            {
                Godot.Collections.Dictionary member = member_entry.AsGodotDictionary();
                out_ids.Add(member.ContainsKey("enemy_id") ? member["enemy_id"].AsString() : "");
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>这张图里所有「紫名但不是章节 Boss」的敌人 id（打完记一笔就是隐藏 Boss 击败）。</summary>
    public string[] hidden_boss_ids(string scene_id)
    {
        string[] chapter = chapter_boss_ids(scene_id);
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _db.rows("enemy_base"))
        {
            var row = entry.AsGodotObject() as EnemyBaseRow;
            if (row == null || row.threat_tag != HIDDEN_BOSS_THREAT)
            {
                continue;
            }

            string enemy_id = row.enemy_id;
            if (System.Array.IndexOf(chapter, enemy_id) >= 0 || out_ids.Contains(enemy_id))
            {
                continue;
            }

            // 只算这张图里出现过的（房间队伍 / 隐藏点位的 Boss 奖励）
            if (!_appears_in(scene_id, enemy_id))
            {
                continue;
            }

            out_ids.Add(enemy_id);
        }

        return out_ids.ToArray();
    }

    private bool _appears_in(string scene_id, string enemy_id)
    {
        foreach (Variant entry in rooms_of(scene_id))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row == null)
            {
                continue;
            }

            if (_db.get_row("enemy_team", row.enemy_team) is EnemyTeamRow team)
            {
                foreach (Variant member_entry in team.parsed_members())
                {
                    Godot.Collections.Dictionary member = member_entry.AsGodotDictionary();
                    string member_id = member.ContainsKey("enemy_id")
                        ? member["enemy_id"].AsString()
                        : "";
                    if (member_id == enemy_id)
                    {
                        return true;
                    }
                }
            }
        }

        foreach (Variant trigger_entry in trigger_rows(scene_id))
        {
            if (trigger_entry.AsGodotObject() is HiddenTriggerRow trigger
                && trigger.reward_type == "boss" && trigger.reward_id == enemy_id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 完成度快照：`{chests:{done,total}, hidden_rooms:{done,total}, hidden_boss:{done,total},
    /// events:{done,total}, done, total, percent}`。
    /// </summary>
    public Godot.Collections.Dictionary progress(string scene_id)
    {
        Godot.Collections.Dictionary record = _record_of(scene_id);
        Godot.Collections.Array room_rows = rooms_of(scene_id);
        int chest_total = 0;
        foreach (Variant entry in room_rows)
        {
            if (entry.AsGodotObject() is DungeonRoomRow row && row.chest_id.Length > 0)
            {
                chest_total += 1;
            }
        }

        int hidden_rooms = 0;
        foreach (Variant entry in room_rows)
        {
            // 表里没有 room_type=hidden：隐藏房间就是 branch_group = hidden（密室／牢房深处／密道／后山地牢）
            if (entry.AsGodotObject() is DungeonRoomRow row && row.branch_group == "hidden")
            {
                hidden_rooms += 1;
            }
        }

        var event_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in trigger_rows(scene_id))
        {
            if (entry.AsGodotObject() is HiddenTriggerRow trigger && trigger.reward_type == "event")
            {
                event_ids.Add(trigger.trigger_id);
            }
        }

        string[] boss_ids = hidden_boss_ids(scene_id);
        int chest_done = _count_intersect(_array_of(record, "chests"), _chest_keys(scene_id));
        int rooms_done = _count_intersect(_array_of(record, "rooms_entered"), _hidden_room_ids(scene_id));
        int boss_done = _count_intersect(_array_of(record, "bosses"), _strings_to_array(boss_ids));
        int event_done = _count_intersect(
            _array_of(record, "triggers"), _strings_to_array(event_ids.ToArray()));
        int done = chest_done + rooms_done + boss_done + event_done;
        int total = chest_total + hidden_rooms + boss_ids.Length + event_ids.Count;
        return new Godot.Collections.Dictionary
        {
            {
                "chests",
                new Godot.Collections.Dictionary { { "done", chest_done }, { "total", chest_total } }
            },
            {
                "hidden_rooms",
                new Godot.Collections.Dictionary { { "done", rooms_done }, { "total", hidden_rooms } }
            },
            {
                "hidden_boss",
                new Godot.Collections.Dictionary { { "done", boss_done }, { "total", boss_ids.Length } }
            },
            {
                "events",
                new Godot.Collections.Dictionary { { "done", event_done }, { "total", event_ids.Count } }
            },
            { "done", done },
            { "total", total },
            {
                "percent",
                total <= 0
                    ? 0
                    : (int)System.Math.Round(
                        done * 100.0 / total, System.MidpointRounding.AwayFromZero)
            },
        };
    }

    /// <summary>一行给 HUD 的文案。</summary>
    public string progress_text(string scene_id)
    {
        Godot.Collections.Dictionary snapshot = progress(scene_id);
        Godot.Collections.Dictionary chests = snapshot["chests"].AsGodotDictionary();
        Godot.Collections.Dictionary hidden = snapshot["hidden_rooms"].AsGodotDictionary();
        Godot.Collections.Dictionary boss = snapshot["hidden_boss"].AsGodotDictionary();
        Godot.Collections.Dictionary events = snapshot["events"].AsGodotDictionary();
        return string.Format(
            "完成度 {0}%　宝箱 {1}/{2}　隐藏房间 {3}/{4}　隐藏Boss {5}/{6}　事件 {7}/{8}",
            snapshot["percent"].AsInt32(),
            chests["done"].AsInt32(), chests["total"].AsInt32(),
            hidden["done"].AsInt32(), hidden["total"].AsInt32(),
            boss["done"].AsInt32(), boss["total"].AsInt32(),
            events["done"].AsInt32(), events["total"].AsInt32());
    }

    private Godot.Collections.Array _chest_keys(string scene_id)
    {
        var out_keys = new Godot.Collections.Array();
        foreach (Variant entry in rooms_of(scene_id))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row == null || row.chest_id.Length == 0)
            {
                continue;
            }

            // 同一个掉落组可能被两个房间各放一个宝箱：键必须带上房间，和小地图里的一致
            out_keys.Add($"{row.room_id}|{row.chest_id}");
        }

        return out_keys;
    }

    private Godot.Collections.Array _hidden_room_ids(string scene_id)
    {
        var out_ids = new Godot.Collections.Array();
        foreach (Variant entry in rooms_of(scene_id))
        {
            if (entry.AsGodotObject() is DungeonRoomRow row && row.branch_group == "hidden")
            {
                out_ids.Add(row.room_id);
            }
        }

        return out_ids;
    }

    private static int _count_intersect(
        Godot.Collections.Array record, Godot.Collections.Array wanted)
    {
        int count = 0;
        foreach (Variant key in wanted)
        {
            if (record.Contains(key.AsString()))
            {
                count += 1;
            }
        }

        return count;
    }

    // ------------------------------------------------------------------ 扫荡

    /// <summary>按层列出：`{floor, rooms:[room_id], enemy_rooms:[room_id], cleared:[room_id], sweepable}`。</summary>
    public Godot.Collections.Array floors(string scene_id)
    {
        Godot.Collections.Dictionary record = _record_of(scene_id);
        Godot.Collections.Array cleared = _array_of(record, "rooms");
        var by_floor = new System.Collections.Generic.Dictionary<int, Godot.Collections.Dictionary>();
        var order = new System.Collections.Generic.List<int>();
        foreach (Variant entry in rooms_of(scene_id))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row == null)
            {
                continue;
            }

            int floor = row.floor;
            if (!by_floor.ContainsKey(floor))
            {
                by_floor[floor] = new Godot.Collections.Dictionary
                {
                    { "floor", floor },
                    { "rooms", new Godot.Collections.Array() },
                    { "enemy_rooms", new Godot.Collections.Array() },
                    { "cleared", new Godot.Collections.Array() },
                };
                order.Add(floor);
            }

            Godot.Collections.Dictionary item = by_floor[floor];
            string room_id = row.room_id;
            item["rooms"].AsGodotArray().Add(room_id);
            if (row.enemy_team.Length > 0)
            {
                item["enemy_rooms"].AsGodotArray().Add(room_id);
                if (cleared.Contains(room_id))
                {
                    item["cleared"].AsGodotArray().Add(room_id);
                }
            }
        }

        order.Sort();
        var out_items = new Godot.Collections.Array();
        foreach (int floor in order)
        {
            Godot.Collections.Dictionary item = by_floor[floor];
            Godot.Collections.Array enemy_rooms = item["enemy_rooms"].AsGodotArray();
            item["sweepable"] =
                enemy_rooms.Count > 0
                && enemy_rooms.Count == item["cleared"].AsGodotArray().Count;
            out_items.Add(item);
        }

        return out_items;
    }

    public Godot.Collections.Array sweepable_floors(string scene_id)
    {
        var out_floors = new Godot.Collections.Array();
        foreach (Variant entry in floors(scene_id))
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            if (item["sweepable"].AsBool())
            {
                out_floors.Add(item["floor"].AsInt32());
            }
        }

        return out_floors;
    }

    /// <summary>
    /// 扫荡某一层：只结算掉落，不给经验（设计：「仅结算掉落」）。
    /// 返回 `{ok, error, floor, drops, applied, summary}`。
    /// </summary>
    public Godot.Collections.Dictionary sweep(string scene_id, int floor_number)
    {
        if (_state == null || _state.Get("inventory").AsGodotObject() == null)
        {
            return _failure(floor_number, "没有会话状态");
        }

        Godot.Collections.Dictionary target = new();
        foreach (Variant entry in floors(scene_id))
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            if (item["floor"].AsInt32() == floor_number)
            {
                target = item;
                break;
            }
        }

        if (target.Count == 0)
        {
            return _failure(floor_number, "没有这一层");
        }

        if (!target["sweepable"].AsBool())
        {
            var left = new System.Collections.Generic.List<string>();
            Godot.Collections.Array cleared = target["cleared"].AsGodotArray();
            foreach (Variant room in target["enemy_rooms"].AsGodotArray())
            {
                if (!cleared.Contains(room))
                {
                    left.Add(_db.display_name(room.AsString()));
                }
            }

            return _failure(floor_number, $"还没通关这一层（没打过的房间：{string.Join("、", left)}）");
        }

        PityTracker pity = _state.Call("pity_tracker").As<PityTracker>();
        DropResolver resolver = DropResolver.create(_db, _rng, pity);
        Godot.Collections.Array allies =
            PartyBuilder.build_actors(_db, _state, new Godot.Collections.Array());
        double bonus = DropResolver.party_drop_bonus(allies);
        var drops = new Godot.Collections.Array();
        foreach (Variant entry in rooms_of(scene_id))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row == null || row.floor != floor_number || row.enemy_team.Length == 0)
            {
                continue;
            }

            if (_db.get_row("enemy_team", row.enemy_team) is not EnemyTeamRow team)
            {
                continue;
            }

            foreach (Variant member_entry in team.parsed_members())
            {
                Godot.Collections.Dictionary member = member_entry.AsGodotDictionary();
                string enemy_id = member.ContainsKey("enemy_id")
                    ? member["enemy_id"].AsString()
                    : "";
                int count = System.Math.Max(
                    1, member.ContainsKey("count") ? member["count"].AsInt32() : 1);
                if (_db.get_row("enemy_base", enemy_id) is not EnemyBaseRow enemy
                    || enemy.drop_group.Length == 0)
                {
                    continue;
                }

                for (int index = 0; index < count; index++)
                {
                    drops.AddRange(resolver.roll_group(
                        enemy.drop_group,
                        _state.Get("difficulty_id").AsString(),
                        new Godot.Collections.Dictionary
                        {
                            { "first_kill", false },
                            { "drop_rate_bonus", bonus },
                        }));
                }
            }
        }

        Godot.Collections.Dictionary applied = BattleReward.grant(
            _db, _state, 0, 0, drops, AffixRoller.create(_db, _rng));
        // 扫荡也要把保底计数存回去（跨战斗、跨难度继承）
        _state.Call("store_pity", pity);
        bool ok = applied["ok"].AsBool();
        return new Godot.Collections.Dictionary
        {
            { "ok", ok },
            { "error", ok ? "" : string.Join("、", _string_list(applied, "errors")) },
            { "floor", floor_number },
            { "drops", drops },
            { "applied", applied },
            { "summary", _summary(applied) },
        };
    }

    private string _summary(Godot.Collections.Dictionary applied)
    {
        var parts = new System.Collections.Generic.List<string>();
        parts.Add($"铜钱 +{_int_of(applied, "money")}");
        Godot.Collections.Array items = _array_of(applied, "items");
        if (items.Count > 0)
        {
            var item_names = new System.Collections.Generic.List<string>();
            foreach (Variant entry in items)
            {
                Godot.Collections.Dictionary item = entry.AsGodotDictionary();
                item_names.Add($"{item_name(item["item_id"].AsString())}×{item["qty"].AsInt32()}");
            }

            parts.Add("物品 " + string.Join("、", item_names));
        }

        Godot.Collections.Array equipment = _array_of(applied, "equipment");
        if (equipment.Count > 0)
        {
            parts.Add($"装备 {equipment.Count} 件");
        }

        Godot.Collections.Array overflow = _array_of(applied, "overflow");
        if (overflow.Count > 0)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Variant entry in overflow)
            {
                Godot.Collections.Dictionary item = entry.AsGodotDictionary();
                names.Add($"{item_name(item["item_id"].AsString())}×{item["qty"].AsInt32()}");
            }

            parts.Add("背包满，没捡起：" + string.Join("、", names));
        }

        return string.Join("　", parts);
    }

    /// <summary>装备实例 id（`eq_sword_01#1`）也走这里：`TableDb.display_name` 会剥掉 #序号。</summary>
    public string item_name(string item_id) => _db.display_name(item_id);

    private Godot.Collections.Dictionary _record_of(string scene_id) =>
        _state != null
            ? _state.Call("dungeon_record", scene_id).AsGodotDictionary()
            : new Godot.Collections.Dictionary();

    private static Godot.Collections.Dictionary _failure(int floor_number, string error) => new()
    {
        { "ok", false },
        { "error", error },
        { "floor", floor_number },
        { "drops", new Godot.Collections.Array() },
        { "applied", new Godot.Collections.Dictionary() },
        { "summary", "" },
    };

    private static Godot.Collections.Array _array_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotArray() : new Godot.Collections.Array();

    private static int _int_of(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static System.Collections.Generic.List<string> _string_list(
        Godot.Collections.Dictionary dict, string key)
    {
        var out_values = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _array_of(dict, key))
        {
            out_values.Add(entry.AsString());
        }

        return out_values;
    }

    private static Godot.Collections.Array _strings_to_array(string[] values)
    {
        var out_values = new Godot.Collections.Array();
        foreach (string value in values)
        {
            out_values.Add(value);
        }

        return out_values;
    }
}
