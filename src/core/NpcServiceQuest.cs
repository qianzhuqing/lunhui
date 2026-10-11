using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `NpcService` 的兑换与专属任务两块（对位原 `src/core/npc_service.gd` 的「兑换／专属任务」两段）：
///   · **兑换**——`npc_offer.kind=offer` 的行：好感够 + 钱够才换得动，发不出去把钱退回去；
///   · **专属任务**——`npc_quest` 按 `sort_order` 串成线，条件复用既有条件语言
///     （`item:`／`event:`／`kill_style:`／`flag_xxx`），交了记一枚 `npc_quest_done_*` 旗标，只能交一次。
/// </summary>
public partial class NpcService
{
    // ------------------------------------------------------------------ 兑换

    public static Godot.Collections.Array offers_of(TableDb db, string npc_id, string kind = "offer")
    {
        var out_rows = new Godot.Collections.Array();
        if (db == null)
        {
            return out_rows;
        }
        foreach (Variant entry in db.rows_where("npc_offer", "npc_id", npc_id))
        {
            if (entry.AsGodotObject() is NpcOfferRow row && row.kind == kind)
            {
                out_rows.Add(row);
            }
        }
        return out_rows;
    }

    /// <summary>能不能换：好感够 + 钱够（`price` 为空＝只靠好感）。</summary>
    public static Godot.Collections.Dictionary can_redeem(TableDb db, GodotObject state, Resource offer)
    {
        if (offer is not NpcOfferRow row)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有这个条目" } };
        }
        int need = row.favor_required;
        int have = favor_of(db, state, row.npc_id);
        if (have < need)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", $"好感不够（{have}／{need}）" } };
        }
        int price = row.price;
        int money = _money(state);
        if (price > 0 && money < price)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", $"钱不够（{money}／{price}）" } };
        }
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    /// <summary>兑换一件：扣钱（如果有价）＋ 进背包。返回 `{ok, item_id, text, error}`。</summary>
    public static Godot.Collections.Dictionary redeem(TableDb db, GodotObject state, string offer_id)
    {
        var offer = db != null ? db.get_row("npc_offer", offer_id) as NpcOfferRow : null;
        if (offer == null)
        {
            return _RedeemResult(false, "", "", "没有这个条目");
        }
        if (offer.kind != "offer")
        {
            return _RedeemResult(false, "", "", "这件东西不是用来换的");
        }
        Godot.Collections.Dictionary check = can_redeem(db, state, offer);
        if (!(check.ContainsKey("ok") && check["ok"].AsBool()))
        {
            return _RedeemResult(false, "", "", check.ContainsKey("error") ? check["error"].AsString() : "");
        }
        int price = offer.price;
        if (price > 0)
        {
            _set_money(state, _money(state) - price);
        }
        Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, offer.item_id, 1, null);
        if (!(granted.ContainsKey("ok") && granted["ok"].AsBool()))
        {
            if (price > 0)
            {
                _set_money(state, _money(state) + price);   // 发不出去就把钱退回去
            }
            return _RedeemResult(false, offer.item_id, "",
                granted.ContainsKey("error") ? granted["error"].AsString() : "拿不到");
        }
        Resource def = def_of(db, offer.npc_id);
        string name_cn = def is NpcDefRow def_row ? def_row.name_cn : offer.npc_id;
        string price_text = price <= 0 ? "" : $"（{price} 文）";
        return _RedeemResult(true, offer.item_id,
            $"{name_cn} 把 {_item_name(db, offer.item_id)} 交给了你{price_text}", "");
    }

    private static Godot.Collections.Dictionary _RedeemResult(
        bool ok, string item_id, string text, string error) =>
        new()
        {
            { "ok", ok }, { "item_id", item_id }, { "text", text }, { "error", error },
        };

    // ------------------------------------------------------------------ 专属任务

    public static Godot.Collections.Array quests_of(TableDb db, string npc_id)
    {
        var rows = new System.Collections.Generic.List<NpcQuestRow>();
        if (db != null)
        {
            foreach (Variant entry in db.rows_where("npc_quest", "npc_id", npc_id))
            {
                if (entry.AsGodotObject() is NpcQuestRow row)
                {
                    rows.Add(row);
                }
            }
        }
        rows.Sort((a, b) => a.sort_order.CompareTo(b.sort_order));
        var out_rows = new Godot.Collections.Array();
        foreach (NpcQuestRow row in rows)
        {
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>
    /// 任务条件是否达成——**复用既有条件语言**（19 §2.5）。
    /// `context` 可带 `kill_styles`（击杀方式记在会话的 `last_battle` 里，状态里没有）。
    /// </summary>
    public static bool requirement_met(
        TableDb db, GodotObject state, string requirement, Godot.Collections.Dictionary context = null)
    {
        string text = requirement.Trim();
        if (text.Length == 0)
        {
            return true;
        }
        string[] parts = text.Split(':');
        switch (parts[0])
        {
            case "item":
                if (parts.Length < 3)
                {
                    return false;
                }
                return _inventory_count(state, parts[1]) >= _to_int(parts[2]);
            case "event":
                if (parts.Length < 2)
                {
                    return false;
                }
                // 事件判定的结果记在存档里（`record_event_check` 的取值：done／failed）
                return state.Call("event_check_result", parts[1]).AsString() == "done";
            case "kill_style":
                if (parts.Length < 2)
                {
                    return false;
                }
                Godot.Collections.Dictionary styles = context != null && context.ContainsKey("kill_styles")
                    ? context["kill_styles"].AsGodotDictionary()
                    : new Godot.Collections.Dictionary();
                return styles.ContainsKey(parts[1]) && styles[parts[1]].AsInt32() > 0;
            default:
                // `flag_xxx` 这类旗标条件
                return GuideService.condition_met(state, text);
        }
    }

    public static bool quest_done(GodotObject state, string quest_id) =>
        state != null && state.Call("has_flag", quest_done_flag(quest_id)).AsBool();

    public static string quest_done_flag(string quest_id) => $"npc_quest_done_{quest_id}";

    /// <summary>这个 NPC 当前能接的那一条（按 sort_order 第一条「没做过且条件达成」）。</summary>
    public static Resource available_quest(
        TableDb db, GodotObject state, string npc_id, Godot.Collections.Dictionary context = null)
    {
        foreach (Variant entry in quests_of(db, npc_id))
        {
            if (entry.AsGodotObject() is not NpcQuestRow row)
            {
                continue;
            }
            if (quest_done(state, row.quest_id))
            {
                continue;
            }
            if (requirement_met(db, state, row.requirement, context))
            {
                return row;
            }
        }
        return null;
    }

    /// <summary>交任务：发奖励 + 加好感 + 记账（只能交一次）。返回 `{ok, text, rewards, favor, error}`。</summary>
    public static Godot.Collections.Dictionary complete_quest(
        TableDb db, GodotObject state, string quest_id, Godot.Collections.Dictionary context = null)
    {
        var row = db != null ? db.get_row("npc_quest", quest_id) as NpcQuestRow : null;
        if (row == null)
        {
            return _QuestResult(false, "", new Godot.Collections.Array(), 0, "没有这条任务");
        }
        if (quest_done(state, quest_id))
        {
            return _QuestResult(false, "", new Godot.Collections.Array(), 0, "这条已经交过了");
        }
        if (!requirement_met(db, state, row.requirement, context))
        {
            return _QuestResult(false, "", new Godot.Collections.Array(), 0, "条件还没达成");
        }
        var rewards = new Godot.Collections.Array();
        foreach (string raw in row.reward_item_ids.Split(';'))
        {
            if (raw.Length == 0)
            {
                continue;   // 口径同 `split(";", false)`：空段跳过
            }
            string item_id = raw.Trim();
            Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, item_id, 1, null);
            rewards.Add(new Godot.Collections.Dictionary
            {
                { "item_id", item_id }, { "ok", granted.ContainsKey("ok") && granted["ok"].AsBool() },
            });
        }
        Godot.Collections.Dictionary favor_result = add_favor(db, state, row.npc_id, row.reward_favor);
        // 「这条支线还会给**别人**加好感」（设计 20 号 §九 #8：孙掌柜那条给的是**燕小七** ＋10）。
        // 表里没有「给谁」这一列（Q72 给了三个选项），所以先写在这里；设计给列就挪进表。
        var extra = new Godot.Collections.Array();
        if (EXTRA_FAVOR.ContainsKey(quest_id))
        {
            foreach (Variant entry in EXTRA_FAVOR[quest_id].AsGodotArray())
            {
                Godot.Collections.Dictionary spec = entry.AsGodotDictionary();
                string who = spec.ContainsKey("npc_id") ? spec["npc_id"].AsString() : "";
                int value = spec.ContainsKey("value") ? spec["value"].AsInt32() : 0;
                Godot.Collections.Dictionary added = add_favor(db, state, who, value);
                if (added.ContainsKey("ok") && added["ok"].AsBool())
                {
                    extra.Add($"{_display_name(db, who)} 好感 +{value}");
                }
            }
        }
        state.Call("set_flag", quest_done_flag(quest_id), true);
        Godot.Collections.Dictionary result = _QuestResult(
            true, row.text_cn, rewards, favor_result["after"].AsInt32(), "");
        result["extra_favor"] = extra;
        return result;
    }

    private static Godot.Collections.Dictionary _QuestResult(
        bool ok, string text, Godot.Collections.Array rewards, int favor, string error) =>
        new()
        {
            { "ok", ok }, { "text", text }, { "rewards", rewards }, { "favor", favor }, { "error", error },
        };

    /// <summary>
    /// 「这条支线还给谁加好感」：设计 20 号 §九 #8（孙掌柜《带一句话》）——信是给燕小七的，
    /// 所以加的是**她**的好感 ＋10。**暂时写在这里**：`npc_quest.reward_favor` 只能加委托人自己
    /// （Q72 ③ 给了三个选项：加一列 `favor_target_id`／改口径／挪到对话里）。
    ///
    /// 改坏这张表 → 玩家交完《带一句话》只有孙掌柜涨好感、燕小七那边不动（用例在 `test_dialogue`）。
    /// </summary>
    public static readonly Godot.Collections.Dictionary EXTRA_FAVOR = new()
    {
        {
            "nq_sun_01",
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "npc_id", "ch_ci" }, { "value", 10 } },
            }
        },
    };

    // ------------------------------------------------------------------ 小工具（任务侧）

    private static int _inventory_count(GodotObject state, string item_id) =>
        state.Get("inventory").AsGodotObject().Call("count", item_id).AsInt32();

    private static int _money(GodotObject state) =>
        state.Get("inventory").AsGodotObject().Get("money").AsInt32();

    private static void _set_money(GodotObject state, int value) =>
        state.Get("inventory").AsGodotObject().Set("money", value);

    /// <summary>`int(...)` 的同款：解析不出来就当 0（表里写错不该把交接判成崩）。</summary>
    private static int _to_int(string text) =>
        int.TryParse(text.Trim(), out int value) ? value : 0;
}
