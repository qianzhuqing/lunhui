using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 宝箱守卫（C# 版，对位原 `src/core/guard_service.gd`；设计 09 §一）：
/// 宝箱不再由「打怪」解锁，而是绑给一个 NPC，文取／武取任选其一。
///
/// 数据源 `npc_guard.csv`：`room_id` 指到 `dungeon_room`（那间房 + 房里那个箱子），
/// `peace_item`／`peace_check_source`／`peace_check_value` 是文取的三列（**可叠加，满足任意一条即过**，
/// 0.14.0 拆列、Q53），`fight_team` 是武取要打的那支队伍——它与 `dungeon_room.enemy_team`
/// 是同一条，所以**武取就是房间里那场仗**。
///
/// 两条路径给**同一个宝箱**（`reward_group` 与 `dungeon_room.chest_id` 一致，构建期
/// `_check_npc_guards` 盯着）：差别在代价，不在奖励。
///
/// 「解决了吗」写在**存档**里，两条路各一份：
///   武取 → `dungeon_records[scene].rooms` 记下这间房（打赢房间战本来就是永久记录）
///   文取 → 旗标 `flag_guard_<guard_id>_peace`
/// **没有守卫的箱子照旧「进房间就能开」**：`guard_for_room()` 返回 null 时 `is_resolved()` 直接为真。
///
/// **过渡期的 `state` 收成 `GodotObject`**：`GameState` 还是 GDScript，读写走 `Get`／`Call`，
/// 等它搬过来再收紧类型；`row` 收 `Resource`（行类已经是 C#，但 GDScript 那边传进来的是 Variant）。
/// 判定值复用 `EventCheckService.best_check_value`（同一事实只许有一处定义，决策 88），
/// 而那个服务还是 GDScript，所以过渡期 `GD.Load&lt;GDScript&gt;(...).Call("new", ...)`。
/// </summary>
public partial class GuardService : RefCounted
{

    /// <summary>文取解决的旗标名（唯一出处：判「解决了吗」与「付完点亮」都用它）。</summary>
    public static string peace_flag(string guard_id) => $"flag_guard_{guard_id}_peace";

    /// <summary>这间房有没有守卫（`npc_guard.room_id`）；没有就是普通箱子。</summary>
    public static Resource guard_for_room(TableDb db, string room_id)
    {
        if (db == null || room_id.Length == 0)
        {
            return null;
        }
        foreach (Variant entry in db.rows("npc_guard"))
        {
            if (entry.AsGodotObject() is NpcGuardRow row && row.room_id == room_id)
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>
    /// 宝箱的会话键是 `"&lt;room_id&gt;|&lt;掉落组&gt;"`（`local_map_controller._spawn_chests` 写的），
    /// 这里取房间那半边，用来查守卫。
    /// </summary>
    public static string room_of_chest_key(string chest_key)
    {
        if (chest_key == null || !chest_key.Contains('|'))
        {
            return "";
        }
        return chest_key.Split('|')[0];
    }

    /// <summary>守卫解决了没有。**没有守卫的箱子恒为真**（照旧能直接开）。</summary>
    public static bool is_resolved(TableDb db, GodotObject state, string scene_id, string room_id)
    {
        Resource row = guard_for_room(db, room_id);
        if (row == null)
        {
            return true;
        }
        if (state == null)
        {
            return false;
        }
        var guard = (NpcGuardRow)row;
        if (state.Call("has_flag", peace_flag(guard.guard_id)).AsBool())
        {
            return true;
        }
        Godot.Collections.Dictionary record = state.Call("dungeon_record", scene_id).AsGodotDictionary();
        if (record == null || !record.ContainsKey("rooms"))
        {
            return false;
        }
        foreach (Variant key in record["rooms"].AsGodotArray())
        {
            if (key.AsString() == room_id)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 文取的可选项。每条：`{kind, id, name_cn, need, have, ok, reason}`——
    /// 三列可叠加、满足任意一条即算过（06 的口径）；两条都留空时这位守卫就没有文取这条路。
    /// </summary>
    public static Godot.Collections.Array peace_options(TableDb db, GodotObject state, Resource row)
    {
        var out_options = new Godot.Collections.Array();
        if (row is not NpcGuardRow guard)
        {
            return out_options;
        }

        // ① 交道具
        string item_id = guard.peace_item.StripEdges();
        if (item_id.Length > 0)
        {
            Resource item_row = db.get_row("item_base", item_id);
            int have = 0;
            if (state != null)
            {
                GodotObject inventory = state.Get("inventory").AsGodotObject();
                if (inventory != null)
                {
                    have = inventory.Call("count", item_id).AsInt32();
                }
            }
            out_options.Add(new Godot.Collections.Dictionary
            {
                { "kind", "item" },
                { "id", item_id },
                { "have", have },
                { "need", 1 },
                { "name_cn", item_row is ItemBaseRow item ? item.name_cn : item_id },
                { "ok", have >= 1 },
                { "reason", have >= 1 ? "" : "背包里没有这份东西" },
            });
        }

        // ② 过一条判定（skill:<非战斗技能> / attr:<属性>），门槛取 `peace_check_value`
        string source = guard.peace_check_source.StripEdges();
        if (source.Length > 0)
        {
            string[] parts = source.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
            string kind = parts.Length >= 2 ? parts[0].StripEdges() : "";
            string target_id = parts.Length >= 2 ? parts[1].StripEdges() : source;
            int need = guard.peace_check_value;
            int have = check_value_of(db, state, source);
            bool ok = need > 0 && have >= need;
            string name_cn = _check_source_name(db, kind, target_id);
            out_options.Add(new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target_id },
                { "have", have },
                { "need", need },
                { "name_cn", name_cn },
                { "ok", ok },
                { "reason", ok ? "" : $"{name_cn} {have} ＜ 门槛 {need}" },
            });
        }
        return out_options;
    }

    /// <summary>这条判定的名字（给玩家看的）：`skill:` 查非战斗技能表、`attr:` 查属性表。</summary>
    private static string _check_source_name(TableDb db, string kind, string target_id)
    {
        if (kind == "skill")
        {
            return db.get_row("event_skill_def", target_id) is EventSkillDefRow skill
                ? skill.name_cn
                : target_id;
        }
        if (kind == "attr")
        {
            return db.get_row("attribute_def", target_id) is AttributeDefRow attr
                ? attr.name_cn
                : target_id;
        }
        return target_id;
    }

    /// <summary>
    /// 队伍在这条判定上的**最好成绩**（与事件判定同一套算法：属性取裸值、技能取
    /// 「等级 + floor(裸属性/5)」，见 `EventCheckService.best_check_value`）。
    /// 复用那一个服务而不是自己再算一遍——同一事实只许有一处定义（决策 88）。
    /// </summary>
    public static int check_value_of(TableDb db, GodotObject state, string source)
    {
        if (state == null)
        {
            return 0;
        }
        EventCheckService service = EventCheckService.create(db, state, null);
        Godot.Collections.Dictionary best = service.best_check_value(source);
        return best.ContainsKey("value") ? best["value"].AsInt32() : 0;
    }

    /// <summary>
    /// 给玩家的一句话：谁挡着、他要什么、另一条路是什么。
    /// 文案取自表里的 `peace_note`／`fight_note`（设计原文），不自己编。
    /// </summary>
    public static string demand_text(TableDb db, GodotObject state, Resource row)
    {
        if (row is not NpcGuardRow guard)
        {
            return "";
        }
        var wants = new System.Collections.Generic.List<string>();
        string confirm = "";
        foreach (Variant entry in peace_options(db, state, row))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            string name_cn = option["name_cn"].AsString();
            int have = option["have"].AsInt32();
            int need = option["need"].AsInt32();
            // 每条都把「差多少」写出来：道具写背包里有几份，判定写「当前值／门槛」
            wants.Add(option["kind"].AsString() == "item"
                ? $"{name_cn}（背包里 {have} 份）"
                : $"{name_cn}（{have}／{need}）");
            if (option["ok"].AsBool() && confirm.Length == 0)
            {
                confirm = $"再按一次 E 就走这条：{name_cn}";
            }
        }
        string head = $"{guard.npc_name_cn}挡在宝箱前，要 {string.Join("、", wants)}。";
        string hint = guard.fight_note;
        if (hint.Length == 0)
        {
            hint = "打赢照样拿箱子";
        }
        string middle = confirm.Length > 0 ? $"（{confirm}）" : "";
        return $"{head}{middle}；或者直接动手——{hint}。";
    }

    /// <summary>
    /// 付文取的账（0.14.0：两条路都能付）：
    ///   `item` → 扣掉一件道具
    ///   `skill`／`attr` → 判定**再过一次**（不凭调用方说「够了」就放行；门槛的唯一出处是表）
    /// 返回 `{ok, error, text}`——`text` 是表里的 `peace_note`（设计写的放行台词／线索）。
    /// </summary>
    public static Godot.Collections.Dictionary pay_peace(
        TableDb db, GodotObject state, Resource row, string kind, string target_id)
    {
        if (row is not NpcGuardRow guard || state == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "没有守卫或会话状态" }, { "text", "" },
            };
        }
        if (kind == "item")
        {
            GodotObject inventory = state.Get("inventory").AsGodotObject();
            if (inventory == null || !inventory.Call("has", target_id, 1).AsBool())
            {
                return new Godot.Collections.Dictionary
                {
                    { "ok", false }, { "error", "背包里没有他要的东西" }, { "text", "" },
                };
            }
            Godot.Collections.Dictionary removed =
                inventory.Call("remove_item", db, target_id, 1).AsGodotDictionary();
            if (!(removed != null && removed.ContainsKey("ok") && removed["ok"].AsBool()))
            {
                string why = removed != null && removed.ContainsKey("error")
                    ? removed["error"].AsString()
                    : "给不出去";
                return new Godot.Collections.Dictionary
                {
                    { "ok", false }, { "error", why }, { "text", "" },
                };
            }
        }
        else if (kind == "skill" || kind == "attr")
        {
            string source = guard.peace_check_source.StripEdges();
            int need = guard.peace_check_value;
            int have = check_value_of(db, state, source);
            if (need <= 0 || have < need)
            {
                return new Godot.Collections.Dictionary
                {
                    { "ok", false },
                    { "text", "" },
                    { "error", $"{_check_source_name(db, kind, target_id)} {have} ＜ 门槛 {need}" },
                };
            }
        }
        else
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", $"文取条件类型不认识：{kind}" }, { "text", "" },
            };
        }
        state.Call("set_flag", peace_flag(guard.guard_id), true);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "text", guard.peace_note },
        };
    }
}
