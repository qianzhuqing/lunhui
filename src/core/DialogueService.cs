using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 对话容器（C# 版，对位原 `src/core/dialogue_service.gd`；设计 20 §十一）：
/// **台词与选项都在表里**，这里只做三件事——「这个人现在说哪句」「这句有哪些选项可选」
/// 「选了之后世界改了什么」。
///
/// 为什么单独立一张表而不是塞进 `npc_def`／`npc_quest`：
/// 剧情的落点是「对话」（一个人说的话 ＋ 玩家的选择），而 `npc_def` 那一行是**身份卡**、
/// `npc_quest` 是**委托**——它们都装不下"一句话 ＋ 三四个带前置的选项"。
///
/// 纯逻辑、不碰场景：面板与用例共用同一份判定，谁也别自己再解析一遍条件。
/// `GuideService` 的条件语言与 `NpcService` 的好感**都已是 C#**（2026-10-11），直调静态口；
/// `state`（`GameState`）也是 C#，但按跨语言惯例仍走 `Get/Set`／`Call`。
/// </summary>
public partial class DialogueService : RefCounted
{
    /// <summary>
    /// 序幕·择念那个节点（设计 20 §3.1）：读清风驿的悬赏板时摆出来，问过一次就不再问。
    /// **唯一出处**——「什么时候摆」（`local_map_controller`）与「选项要顺带置 `flag_open_*`」
    /// （下面的 `choose`）都读它，谁都不许再抄一份节点 id。
    /// </summary>
    public const string OPENING_NODE = "dl_opening_choice";

    /// <summary>过渡期取值口（GDScript 读不到 C# 的 `const`，见施工单坑⑩）。</summary>
    public static string opening_node() => OPENING_NODE;

    /// <summary>这个人有没有配对话（面板用它决定要不要摆「聊聊」那一段）。</summary>
    public static bool has_dialogue(TableDb db, string speaker_id)
    {
        if (db == null || speaker_id.Length == 0)
        {
            return false;
        }
        foreach (Variant entry in db.rows("dialogue_node"))
        {
            if (entry.AsGodotObject() is DialogueNodeRow row && row.speaker_id == speaker_id)
            {
                return true;
            }
        }
        return false;
    }

    public static Resource node_of(TableDb db, string node_id) =>
        db != null && node_id.Length > 0 ? db.get_row("dialogue_node", node_id) : null;

    /// <summary>
    /// 这个人**现在**该说的那一句：按 `sort_order` 取第一条条件满足的节点；没有就 `null`。
    ///
    /// **「选完之后的接续节点」不算入口**：被某个 `dialogue_option.next_node_id` 指到的那些
    /// （序幕三条心理独白 `dl_open_*`、终局三选一的幕结 `dl_ledger_reply_*`）是**点选项跳过去**的，
    /// 不是「再找这个人说话时他该说的第一句」。不排掉的话，选完终局难题再点她一次，
    /// 她会把幕结那句（「你选了我想要的……」）当问候重念一遍——2026-10-04 文案批次落表时踩到。
    ///
    /// 「条件满足」用与引导／招募／剧情节点**同一份**条件语言（`GuideService.condition_met`）——
    /// 不另造一套，免得"同一句话在两处判定不一样"。
    /// </summary>
    public static Resource entry_node(TableDb db, GodotObject state, string speaker_id)
    {
        if (db == null || speaker_id.Length == 0)
        {
            return null;
        }
        Godot.Collections.Dictionary continuations = _option_targets(db);
        var rows = new System.Collections.Generic.List<DialogueNodeRow>();
        foreach (Variant entry in db.rows("dialogue_node"))
        {
            if (entry.AsGodotObject() is DialogueNodeRow row && row.speaker_id == speaker_id)
            {
                rows.Add(row);
            }
        }
        rows.Sort((a, b) => a.sort_order.CompareTo(b.sort_order));
        foreach (DialogueNodeRow row in rows)
        {
            if (continuations.ContainsKey(row.node_id))
            {
                continue;
            }
            if (condition_ok(state, row.condition))
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>被选项指到的节点（`dialogue_option.next_node_id`）：它们是**接续**，不是入口。</summary>
    private static Godot.Collections.Dictionary _option_targets(TableDb db)
    {
        var target_ids = new Godot.Collections.Dictionary();
        foreach (Variant entry in db.rows("dialogue_option"))
        {
            if (entry.AsGodotObject() is not DialogueOptionRow row)
            {
                continue;
            }
            string next_id = row.next_node_id.StripEdges();
            if (next_id.Length > 0)
            {
                target_ids[next_id] = true;
            }
        }
        return target_ids;
    }

    /// <summary>某个节点上**现在可选**的选项（按 `sort_order`；`condition` 是设计 20 §四 的「前置」）。</summary>
    public static Godot.Collections.Array options_for(TableDb db, GodotObject state, string node_id)
    {
        var out_rows = new Godot.Collections.Array();
        if (db == null || node_id.Length == 0)
        {
            return out_rows;
        }
        var rows = new System.Collections.Generic.List<DialogueOptionRow>();
        foreach (Variant entry in db.rows("dialogue_option"))
        {
            if (entry.AsGodotObject() is not DialogueOptionRow row || row.node_id != node_id)
            {
                continue;
            }
            if (!condition_ok(state, row.condition))
            {
                continue;
            }
            rows.Add(row);
        }
        rows.Sort((a, b) => a.sort_order.CompareTo(b.sort_order));
        foreach (DialogueOptionRow row in rows)
        {
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>条件语言：留空 = 无条件；其余交给 `GuideService.condition_met`（含 `&` 串接与 `origin:`）。</summary>
    public static bool condition_ok(GodotObject state, string condition)
    {
        string trimmed = condition == null ? "" : condition.StripEdges();
        if (trimmed.Length == 0)
        {
            return true;
        }
        return GuideService.condition_met(state, trimmed);
    }

    /// <summary>
    /// 选一个选项：先**落地效果**（置旗标 → 好感 → 给东西），再告诉调用方下一句是哪句。
    ///
    /// 返回 `{ok, option_id, text, next_node_id, flag, favor, item, error}`——
    /// 效果按这个顺序结算，任何一步失败都**如实写进 error**，不静默吞掉（配错 id 的代价是
    /// 玩家选了没反应，那比报错难查得多）。
    /// </summary>
    public static Godot.Collections.Dictionary choose(TableDb db, GodotObject state, string option_id)
    {
        var result = new Godot.Collections.Dictionary
        {
            { "ok", false },
            { "option_id", option_id },
            { "text", "" },
            { "next_node_id", "" },
            { "flag", "" },
            { "favor", 0 },
            { "item", "" },
            { "error", "" },
        };
        if (db == null || state == null)
        {
            result["error"] = "没有会话状态";
            return result;
        }
        if (db.get_row("dialogue_option", option_id) is not DialogueOptionRow row)
        {
            result["error"] = "没有这个选项";
            return result;
        }
        if (!condition_ok(state, row.condition))
        {
            result["error"] = "这个选项现在不该出现（前置没满足）";
            return result;
        }
        result["text"] = row.text_cn;
        result["next_node_id"] = row.next_node_id;

        string flag_text = row.set_flag;
        if (flag_text.Length > 0)
        {
            // 一列可以写多个旗标（分号隔开，Q83）：幕二「拔剑」那条要同时置
            // `flag_lin_silent` 与 `flag_luoyanpo_met`。**拆法只有一处**（行类的 `parse_set_flags`），
            // 与构建期校验、PS1 那份校验同一口径；单值（旧数据）行为一字不变。
            string[] flags = DialogueOptionRow.parse_set_flags(flag_text);
            foreach (string flag in flags)
            {
                state.Call("set_flag", flag, true);
            }
            result["flag"] = flag_text;
            // 序幕·择念（设计 20 §3.1，0.32.0 补）：**心性由序幕一次定死**——称号与幕结旁白只看
            // `flag_open_*`，而后面各幕的选项照旧只置当场反应的 `heart_*`。
            // 对应关系是同一个后缀（`heart_yi` → `flag_open_yi`），所以既不逐条抄选项 id、
            // 也不用给 `dialogue_option` 加列（表侧不加列是设计侧这一版的拍板）。
            // 多值时按"这一份里带 `heart_*` 的每一个"走。
            if (row.node_id == OPENING_NODE)
            {
                foreach (string flag in flags)
                {
                    if (flag.StartsWith("heart_"))
                    {
                        state.Call("set_flag", $"flag_open_{flag.Substring("heart_".Length)}", true);
                    }
                }
            }
        }

        if (row.favor_delta != 0)
        {
            string speaker = _speaker_of(db, row.node_id);
            if (speaker.Length == 0)
            {
                result["error"] = "这个选项要改好感，但节点没写说话人";
                return result;
            }
            Godot.Collections.Dictionary favor = AddFavor(db, state, speaker, row.favor_delta);
            if (!_Flag(favor, "ok", false))
            {
                result["error"] = _Text(favor, "error", "好感没改上");
                return result;
            }
            result["favor"] = row.favor_delta;
        }

        string item_id = row.grant_item_id;
        if (item_id.Length > 0)
        {
            Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, item_id, 1, null);
            if (!_Flag(granted, "ok", false))
            {
                result["error"] = _Text(granted, "error", "东西没发出去");
                return result;
            }
            result["item"] = item_id;
        }

        result["ok"] = true;
        return result;
    }

    /// <summary>选项挂在哪个人身上（`favor_delta` 要改谁的好感）。</summary>
    private static string _speaker_of(TableDb db, string node_id)
    {
        Resource node = node_of(db, node_id);
        return node is DialogueNodeRow row ? row.speaker_id : "";
    }

    /// <summary>好感的口径只有一处（`NpcService.add_favor`：`npc_favor` ＋上下限），直调。</summary>
    private static Godot.Collections.Dictionary AddFavor(
        TableDb db, GodotObject state, string npc_id, int delta) =>
        NpcService.add_favor(db, state, npc_id, delta);

    private static bool _Flag(Godot.Collections.Dictionary data, string key, bool fallback) =>
        data.ContainsKey(key) ? data[key].AsBool() : fallback;

    private static string _Text(Godot.Collections.Dictionary data, string key, string fallback) =>
        data.ContainsKey(key) ? data[key].AsString() : fallback;
}
