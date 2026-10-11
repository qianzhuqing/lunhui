using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 剧情招募（C# 版，对位原 `src/core/recruit_service.gd`；设计 09 §3.2）：
/// 开局只有 `recruit_def.is_initial=1` 的人，其余同伴按剧情加入。
///
/// 数据源 `recruit_def.csv`：`join_condition` 是旗标 id，`join_scene` 既可能是
/// `map_local.scene_id`（小地图／副本，如清风驿、黑风寨），也可能是 `map_region.node_id`
/// （大地图上的区域，如落雁坡——它没有自己的小地图）。两条都在这里认。
///
/// 口径：**「条件旗标已点亮 ∧ 人在那张图（或站到那个区域地标前）∧ 还没入队」→ 入队**。
/// 谁来点亮条件旗标是各系统的事（燕小七要的 `flag_board_read` 由清风驿的悬赏板发出；
/// 另外三位的 `flag_luoyanpo_met`／`flag_poison_hall`／`flag_huangcun_done` 目前**没有任何来源**——
/// 那是设计侧的剧情内容，没到之前他们不会入队，代码这边不自己编触发点）。
///
/// 入队动作本身只有一处：`GameState.add_character()`（发等级／起始装备／起始武学并铺装配）。
/// </summary>
public partial class RecruitService : RefCounted
{
    /// <summary>
    /// 入队时发的「支线奖励」（设计 20 号 §九 那张表：四条招募支线各写「入队 ＋ 某物」＋ 好感 ＋20）。
    ///
    /// **暂时写在这里**：`recruit_def` 没有奖励列（**Q72** 给了三个选项：加列／改用招募对话的选项／不发）。
    /// 两条**不用**写进来——林铁山的柴刀就是他的起始武器（`start_equip_ids`）、苏九娘的五毒散手
    /// 就是她的起始武学（`start_skill_ids`），那两样 `GameState.add_character()` 已经发了 ✅。
    /// 所以这里只补「起始装备／起始武学之外」的两件：燕小七的回气散、白清和的药酒。
    ///
    /// 好感那一列四条都是 ＋20（也是 §九 表的数）；加的是**这位同伴自己**的好感
    /// （同伴有 `npc_favor` 行，见 `框架说明.md` 决策 296）。
    /// </summary>
    private static readonly Godot.Collections.Dictionary JOIN_REWARDS = new()
    {
        { "ch_ci", JoinReward(new[] { "item_potion_qi" }, 20) },
        { "ch_gang", JoinReward(System.Array.Empty<string>(), 20) },
        { "ch_du", JoinReward(System.Array.Empty<string>(), 20) },
        { "ch_qi", JoinReward(new[] { "item_med_02" }, 20) },
    };

    private static Godot.Collections.Dictionary JoinReward(string[] items, int favor)
    {
        var list = new Godot.Collections.Array();
        foreach (string item_id in items)
        {
            list.Add(item_id);
        }
        return new Godot.Collections.Dictionary { { "items", list }, { "favor", favor } };
    }

    /// <summary>某一行「是不是在这张图里等着」：`join_scene` 命中 `scene_id`，或命中这张图的父区域节点。</summary>
    public static bool scene_matches(TableDb db, Resource row, string scene_id)
    {
        if (row is not RecruitDefRow recruit)
        {
            return false;
        }
        string want = recruit.join_scene;
        if (want.Length == 0 || scene_id.Length == 0)
        {
            return false;
        }
        if (want == scene_id)
        {
            return true;
        }
        var local = db.get_row("map_local", scene_id) as MapLocalRow;
        return local != null && local.parent_node == want;
    }

    /// <summary>
    /// 还没入队、条件也满足的招募行。
    ///
    /// **`is_initial=1` 不再被跳过**（设计 0.30.0）：主角是**玩家选出来的**（`CreationService.build()`
    /// 把 `origin_id` 传给 `new_game()`），所以"选谁就是谁"，`is_initial` 只作**兜底**。
    /// 以前这里跳过初始行，而 `scholar_fallen` 是**唯一**一行 `is_initial=1`——于是主角选别人时，
    /// **书生永远无法入队**（他是 Q51 四位同伴之一）。下面那句「已经在队里就跳过」已经管住重复：
    /// 主角是书生时他本来就在队里、照样跳过；是别人时他不在队里、可以按剧情入队。
    /// </summary>
    private static Godot.Collections.Array _ReadyRows(
        TableDb db, GodotObject state, System.Func<RecruitDefRow, bool> match)
    {
        var out_rows = new Godot.Collections.Array();
        if (state == null)
        {
            return out_rows;
        }
        string[] party = state.Get("char_ids").AsStringArray();
        foreach (Variant entry in db.rows("recruit_def"))
        {
            if (entry.AsGodotObject() is not RecruitDefRow row)
            {
                continue;
            }
            if (System.Array.IndexOf(party, row.char_id) >= 0)
            {
                continue;
            }
            if (!match(row))
            {
                continue;
            }
            if (!ConditionMet(state, row.join_condition))
            {
                continue;
            }
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>小地图／副本里等着加入的人（按 `map_local.scene_id` 或它的父区域节点匹配）。</summary>
    public static Godot.Collections.Array pending_for_scene(TableDb db, GodotObject state, string scene_id) =>
        _ReadyRows(db, state, row => scene_matches(db, row, scene_id));

    /// <summary>大地图上等着加入的人（按 `map_region.node_id` 匹配——落雁坡这类没有小地图的区域）。</summary>
    public static Godot.Collections.Array pending_for_region(TableDb db, GodotObject state, string node_id) =>
        _ReadyRows(db, state, row => node_id.Length > 0 && row.join_scene == node_id);

    /// <summary>把一位同伴加进队伍。返回 `{ok, char_id, name_cn, note, rewards, error}`。</summary>
    public static Godot.Collections.Dictionary join(TableDb db, GodotObject state, string char_id)
    {
        string name_cn = db.get_row("character_base", char_id) is CharacterBaseRow character
            ? character.name_cn
            : char_id;
        Godot.Collections.Dictionary result = state != null
            ? state.Call("add_character", db, char_id).AsGodotDictionary()
            : new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有会话状态" } };
        if (!(result.ContainsKey("ok") && result["ok"].AsBool()))
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "char_id", char_id },
                { "name_cn", name_cn },
                { "note", "" },
                { "rewards", new Godot.Collections.Array() },
                { "error", result.ContainsKey("error") ? result["error"].AsString() : "入队失败" },
            };
        }
        var recruit_row = db.get_row("recruit_def", char_id) as RecruitDefRow;
        // 入队点亮一枚旗标 `flag_<char_id>_joined`：设计侧的 `guide_step` 就是拿它当下一步的条件的
        // （现例：`flag_ch_ci_joined`），别在别处再手写一遍这个字符串。
        state.Call("set_flag", $"flag_{char_id}_joined", true);
        Godot.Collections.Array rewards = _apply_join_rewards(db, state, char_id);
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "char_id", char_id },
            { "name_cn", name_cn },
            { "note", recruit_row != null ? recruit_row.join_note : "" },
            { "rewards", rewards },
            { "error", "" },
        };
    }

    /// <summary>
    /// 发入队奖励（`JOIN_REWARDS`）：东西走统一入账口径，好感加到这位同伴头上。
    /// 返回**给人看的**奖励短句（例 `["回气散 ×1", "好感 +20"]`），调用方拼进入队提示。
    /// </summary>
    private static Godot.Collections.Array _apply_join_rewards(TableDb db, GodotObject state, string char_id)
    {
        var out_rows = new Godot.Collections.Array();
        if (state == null || !JOIN_REWARDS.ContainsKey(char_id))
        {
            return out_rows;
        }
        Godot.Collections.Dictionary spec = JOIN_REWARDS[char_id].AsGodotDictionary();
        foreach (Variant item in spec["items"].AsGodotArray())
        {
            string item_id = item.AsString();
            Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, item_id, 1, null);
            if (!(granted.ContainsKey("ok") && granted["ok"].AsBool()))
            {
                // 发不出去**出声**（入库口径出问题时，宁可在自检里红，也别静默少一件）
                GD.PushError(
                    $"[RecruitService] {char_id} 的入队奖励 {item_id} 没发出去："
                    + (granted.ContainsKey("error") ? granted["error"].AsString() : ""));
                continue;
            }
            out_rows.Add($"{_item_name(db, item_id)} ×1");
        }
        int favor = spec.ContainsKey("favor") ? spec["favor"].AsInt32() : 0;
        if (favor != 0)
        {
            Godot.Collections.Dictionary added = AddFavor(db, state, char_id, favor);
            if (added.ContainsKey("ok") && added["ok"].AsBool())
            {
                out_rows.Add($"好感 +{favor}");
            }
        }
        return out_rows;
    }

    private static string _item_name(TableDb db, string item_id)
    {
        if (db.get_row("equip_base", item_id) is EquipBaseRow equip)
        {
            return equip.name_cn;
        }
        return db.get_row("item_base", item_id) is ItemBaseRow item ? item.name_cn : item_id;
    }

    /// <summary>一次处理完某个场景里所有待加入的人（按表里的顺序），返回结果数组。</summary>
    public static Godot.Collections.Array join_all_for_scene(TableDb db, GodotObject state, string scene_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in pending_for_scene(db, state, scene_id))
        {
            if (entry.AsGodotObject() is RecruitDefRow row)
            {
                out_rows.Add(join(db, state, row.char_id));
            }
        }
        return out_rows;
    }

    /// <summary>大地图上的版本（按区域节点 id）。</summary>
    public static Godot.Collections.Array join_all_for_region(TableDb db, GodotObject state, string node_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in pending_for_region(db, state, node_id))
        {
            if (entry.AsGodotObject() is RecruitDefRow row)
            {
                out_rows.Add(join(db, state, row.char_id));
            }
        }
        return out_rows;
    }

    /// <summary>条件语言与引导共用一份（`GuideService.condition_met`），别另造一套。</summary>
    private static bool ConditionMet(GodotObject state, string condition) =>
        GuideService.condition_met(state, condition);

    /// <summary>好感的口径只有一处（`NpcService.add_favor`：`npc_favor` ＋上下限），直调。</summary>
    private static Godot.Collections.Dictionary AddFavor(
        TableDb db, GodotObject state, string npc_id, int delta) =>
        NpcService.add_favor(db, state, npc_id, delta);
}
