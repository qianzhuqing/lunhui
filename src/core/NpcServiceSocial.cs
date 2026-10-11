using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `NpcService` 的社交三块（对位原 `src/core/npc_service.gd` 的「赠礼／切磋／偷窃」三段）：
///   · **赠礼**——喜好分两级（`gift_preview` 与 `gift` 同一处算，界面写的数就是真加的数）；
///   · **切磋**——城镇 NPC 打 `spar_team_id` 那支队伍、同伴打「他自己的镜像」（Q88 拍板 ①），
///     赢了加好感、输了不罚；
///   · **偷窃**——走事件判定（`EventCheckService`），`steal_difficulty=0` 不可偷，失败扣好感。
/// </summary>
public partial class NpcService
{

    // ------------------------------------------------------------------ 赠礼

    /// <summary>
    /// 送礼的**预期**收益：只算不落。面板要在按钮上写「+8·最喜」，而这条规则必须与 `gift()` 同一处，
    /// 否则界面写的数与真加的数会各改各的（设计 19 §2.3 的「喜好分两级」）。
    /// 返回 `{delta, kind, reaction}`；`kind` ∈ `best`（最爱）／`same`（同类）／`dislike`（不喜）／`plain`。
    /// </summary>
    public static Godot.Collections.Dictionary gift_preview(TableDb db, string npc_id, string item_id)
    {
        if (favor_row_of(db, npc_id) is not NpcFavorRow row)
        {
            return new Godot.Collections.Dictionary { { "delta", 0 }, { "kind", "plain" }, { "reaction", "" } };
        }
        int delta = row.gift_favor;
        string kind = "plain";
        string reaction = "收下了";
        if (_in_list(row.like_item_ids, item_id))
        {
            delta += LIKE_ITEM_BONUS;
            kind = "best";
            reaction = "眼前一亮——正是他喜欢的东西";
        }
        else if (_in_list(row.dislike_item_ids, item_id))
        {
            delta = DISLIKE_PENALTY;
            kind = "dislike";
            reaction = "皱了皱眉，还是收下了";
        }
        else if (db.get_row("item_base", item_id) is ItemBaseRow item
                 && _in_list(row.like_categories, item.item_type))
        {
            delta += LIKE_CATEGORY_BONUS;
            kind = "same";
            reaction = "道了声谢";
        }
        return new Godot.Collections.Dictionary
        {
            { "delta", delta }, { "kind", kind }, { "reaction", reaction },
        };
    }

    /// <summary>送礼：从背包扣一件，按喜好给好感。返回 `{ok, delta, favor, text, error}`。</summary>
    public static Godot.Collections.Dictionary gift(
        TableDb db, GodotObject state, string npc_id, string item_id)
    {
        Resource row = favor_row_of(db, npc_id);
        Resource def = def_of(db, npc_id);
        string name_cn = def is NpcDefRow def_row ? def_row.name_cn : npc_id;
        if (row == null)
        {
            return _GiftResult(false, 0, 0, "", "这个人还不能交往");
        }
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        if (!inventory.Call("has", item_id, 1).AsBool())
        {
            return _GiftResult(false, 0, favor_of(db, state, npc_id), "", "背包里没有这件东西");
        }
        Godot.Collections.Dictionary preview = gift_preview(db, npc_id, item_id);
        int delta = preview["delta"].AsInt32();
        string reaction = preview["reaction"].AsString();
        Godot.Collections.Dictionary taken = inventory.Call("remove_item", db, item_id, 1).AsGodotDictionary();
        if (!(taken.ContainsKey("ok") && taken["ok"].AsBool()))
        {
            return _GiftResult(false, 0, favor_of(db, state, npc_id), "",
                taken.ContainsKey("error") ? taken["error"].AsString() : "拿不出来");
        }
        Godot.Collections.Dictionary changed = add_favor(db, state, npc_id, delta);
        int applied = changed["delta"].AsInt32();
        int after = changed["after"].AsInt32();
        return _GiftResult(true, applied, after,
            $"{name_cn} {reaction}（好感 {_signed(applied)} → {after}）", "");
    }

    private static Godot.Collections.Dictionary _GiftResult(
        bool ok, int delta, int favor, string text, string error) =>
        new()
        {
            { "ok", ok }, { "delta", delta }, { "favor", favor }, { "text", text }, { "error", error },
        };

    // ------------------------------------------------------------------ 切磋

    /// <summary>能不能切磋：表里给了 `spar_team_id` 才能（19 §2.2 的「切磋赢了加好感」）。</summary>
    public static string spar_team(TableDb db, string npc_id) =>
        def_of(db, npc_id) is NpcDefRow def ? def.spar_team_id : "";

    /// <summary>
    /// 同伴与城镇 NPC 的区分只有一处：**没有 `npc_def` 行、但有 `character_base` 行**的人就是同伴
    /// （`person_of` 合成的那一行就是从这儿来的）。
    /// </summary>
    public static bool is_companion(TableDb db, string npc_id)
    {
        if (db == null || string.IsNullOrEmpty(npc_id))
        {
            return false;
        }
        return def_of(db, npc_id) == null && db.get_row("character_base", npc_id) != null;
    }

    /// <summary>
    /// 切磋**跟谁打**（`docs/dev/待策划确认.md` Q88 拍板 ①，2026-10-04）：
    ///   · 城镇 NPC（有 `npc_def` 行）→ 按 `spar_team_id` 那支队伍；
    ///   · **同伴**（没有 `npc_def` 行）→ **镜像他自己**（按 `character_base` ＋当前等级与配装现造），
    ///     **不新增 `enemy_team` 行、不加列**——所以这里给 `kind=mirror` 与空队伍 id。
    /// 返回 `{kind, team_id}`：`kind` ∈ `team`／`mirror`／`none`（`none` = 面板不摆切磋这一段）。
    /// </summary>
    public static Godot.Collections.Dictionary spar_opponent(TableDb db, string npc_id)
    {
        string team = spar_team(db, npc_id);
        if (!string.IsNullOrEmpty(team))
        {
            return new Godot.Collections.Dictionary { { "kind", "team" }, { "team_id", team } };
        }
        if (is_companion(db, npc_id) && favor_row_of(db, npc_id) != null)
        {
            return new Godot.Collections.Dictionary { { "kind", "mirror" }, { "team_id", "" } };
        }
        return new Godot.Collections.Dictionary { { "kind", "none" }, { "team_id", "" } };
    }

    /// <summary>切磋赢了：加好感。返回与 `add_favor` 同形，另附一句文案。</summary>
    public static Godot.Collections.Dictionary win_spar(TableDb db, GodotObject state, string npc_id)
    {
        Resource row = favor_row_of(db, npc_id);
        Resource def = def_of(db, npc_id);
        string name_cn = def is NpcDefRow def_row ? def_row.name_cn : npc_id;
        if (row is not NpcFavorRow favor_row || favor_row.spar_favor <= 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "delta", 0 }, { "after", favor_of(db, state, npc_id) },
                { "text", "" }, { "error", "这个人不切磋" },
            };
        }
        Godot.Collections.Dictionary changed = add_favor(db, state, npc_id, favor_row.spar_favor);
        changed["text"] =
            $"{name_cn} 抱拳：「承让。」（好感 {_signed(changed["delta"].AsInt32())} → {changed["after"].AsInt32()}）";
        return changed;
    }

    // ------------------------------------------------------------------ 偷窃

    /// <summary>
    /// 偷窃判定值：取全队「敏／运」判定值里高的那个（19 §2.3：偷窃走事件判定，敏或运）。
    /// 复用**事件判定那一套**（`EventCheckService.best_check_value`），不另算一套。
    /// </summary>
    public static int steal_value(TableDb db, GodotObject state)
    {
        EventCheckService service = _event_check(db, state);
        return System.Math.Max(
            _check_value(service, "attr:agi"),
            _check_value(service, "attr:luk"));
    }

    /// <summary>偷窃成功率：与事件判定的 soft 口径同一条公式（`0.5 + 差值 × 0.1`，夹 5%~95%）。</summary>
    public static double steal_chance(TableDb db, GodotObject state, int difficulty)
    {
        EventCheckService service = _event_check(db, state);
        return service.soft_chance(steal_value(db, state), difficulty);
    }

    /// <summary>
    /// 偷一件东西。`roll` 传 0~1 之间的随机数（用例固定种子）。
    /// 返回 `{ok, success, text, favor, error}`——**失败扣好感**（19 §2.3），但不进战斗。
    /// </summary>
    public static Godot.Collections.Dictionary steal(
        TableDb db, GodotObject state, string npc_id, string item_id, double roll)
    {
        Resource def = def_of(db, npc_id);
        string name_cn = def is NpcDefRow def_row ? def_row.name_cn : npc_id;
        if (favor_row_of(db, npc_id) is not NpcFavorRow favor_row)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "success", false }, { "text", "" }, { "favor", 0 },
                { "error", "这个人还不能交往" },
            };
        }
        int difficulty = favor_row.steal_difficulty;
        if (difficulty <= 0)
        {
            // `steal_difficulty=0` = 不可偷：纯善意的 NPC 留的口子（例：救出来的阿福）
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "success", false }, { "text", "" },
                { "favor", favor_of(db, state, npc_id) },
                { "error", $"{name_cn} 身上没有可偷的东西" },
            };
        }
        double chance = steal_chance(db, state, difficulty);
        int percent = Mathf.RoundToInt(chance * 100.0);
        if (roll <= chance)
        {
            Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, item_id, 1, null);
            bool ok = granted.ContainsKey("ok") && granted["ok"].AsBool();
            return new Godot.Collections.Dictionary
            {
                { "ok", true }, { "success", true }, { "favor", favor_of(db, state, npc_id) },
                { "text", $"得手了：{_item_name(db, item_id)}（成功率 {percent}%）" },
                { "error", ok ? "" : (granted.ContainsKey("error") ? granted["error"].AsString() : "") },
            };
        }
        Godot.Collections.Dictionary changed = add_favor(db, state, npc_id, STEAL_FAIL_PENALTY);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "success", false }, { "favor", changed["after"].AsInt32() },
            { "text", $"被发现了（成功率 {percent}%）：好感 {_signed(changed["delta"].AsInt32())} → {changed["after"].AsInt32()}" },
            { "error", "" },
        };
    }

    /// <summary>起一个事件判定服务（已是 C#，直调）。</summary>
    private static EventCheckService _event_check(TableDb db, GodotObject state) =>
        EventCheckService.create(db, state, null);

    private static int _check_value(EventCheckService service, string source)
    {
        Godot.Collections.Dictionary best = service.best_check_value(source);
        return best.ContainsKey("value") ? best["value"].AsInt32() : 0;
    }
}
