using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 大地图随机事件（设计 19 §四；0.28.0 Q64 把八行的 `effect_id` 填齐之后的接线）——
/// 对位 `tests/test_world_event.gd`（2026-10-11 P4 第十四批搬 C#）。
///
/// 五类效果分两层验：
/// - `gift`／`hint`／`check` 在**服务层**就结算完（发物品、记线索、走判定）；
/// - `trade`／`spar` 只交出 `scene_action` 请求——开货架与切战斗要挂在当前场景的节点上，
///   由 `OverworldController._apply_world_event_effect()` 动手（那一层在场景自检里验）。
///
/// 这一份用例的另一半作用是把「八行 effect_id 都填过」钉住：空一行就会有人拿到一句
/// 「效果还没接上」的占位文案，而那正是 0.28.0 要消掉的东西。
///
/// **类名取 `TestWorldEventWiring`：`TestWorldEvent.cs` 已被占**（`WorldEventService` 搬 C# 时
/// 写的那份，suite 名带「（C# 侧）」，另加池子那几条）。这一份是旧链原来那份，
/// **多出的两块是别的服务**：③ 线索本真的标成「已听说」并写明来源（要 `ClueService`）；
/// ⑤ 货架组能找回临时建筑、货架不是空的（要 `ShopService`），以及历练队伍那条表行。
/// </summary>
public partial class TestWorldEventWiring : TestCase
{
    /// <summary>软判定要掷骰，用例靠固定种子复现。</summary>
    private const int Seed = 20261004;

    public override string SuiteName() => "大地图随机事件";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_wired_rows(db);
        _check_gift(db);
        _check_hint(db);
        _check_check_effect(db);
        _check_scene_actions(db);
        _check_unwired(db);
    }

    /// <summary>八条事件的 effect_kind／effect_id 与 0.28.0 的 Q64 表逐条一致。</summary>
    private void _check_wired_rows(TableDb db)
    {
        var expected = new System.Collections.Generic.Dictionary<string, (string Kind, string Id)>
        {
            { "we_caravan", ("trade", "shop_caravan") },
            { "we_disciple", ("spar", "team_wanderer_disciple") },
            { "we_wanderer", ("hint", "trig_stealth_clear") },
            { "we_hermit", ("gift", "eq_sword_04") },
            { "we_lost_item", ("gift", "item_herb") },
            { "we_patrol", ("check", "ev_patrol_check") },
            { "we_rumor", ("hint", "trig_wine") },
            { "we_dog", ("gift", "item_pelt") },
        };
        var rows = 0;
        foreach (Variant entry in db.rows("world_event"))
        {
            var row = (WorldEventRow)entry.AsGodotObject();
            rows += 1;
            CheckTrue(
                row.effect_id.Length > 0,
                $"{row.event_id} 的效果有指向（0.28.0 之前八行全空）");
            CheckTrue(expected.ContainsKey(row.event_id), $"{row.event_id} 在用例登记的八条里");
            if (!expected.TryGetValue(row.event_id, out var want))
            {
                continue;
            }

            CheckEq(row.effect_kind, want.Kind, $"{row.event_id} 的效果类型");
            CheckEq(row.effect_id, want.Id, $"{row.event_id} 的效果指向");
        }

        CheckEq(rows, 8, "大地图随机事件 8 条");
    }

    /// <summary>赠礼：`we_hermit` 给良品剑「锈月」——它原先**没有任何来源**（0.28.0 才补上）。</summary>
    private void _check_gift(TableDb db)
    {
        GodotObject state = SoloState(db);
        // 开局本来就穿着模板给的初始装备，所以按**增量**断言，不写死总数
        int before = _equipment_count(state);
        Godot.Collections.Dictionary result =
            WorldEventService.trigger(db, state, "we_hermit", RngService.create(Seed));
        Godot.Collections.Dictionary effect = _d(result, "effect");
        CheckTrue(_b(effect, "applied"), "隐士奇遇的赠礼真的发出来了");
        CheckEq(_s(effect, "kind"), "gift", "它是赠礼类");
        CheckEq(_s(effect, "id"), "eq_sword_04", "给的是锈月");
        CheckEq(_equipment_count(state), before + 1, "背包里多出 1 件装备实例");
        CheckTrue(_s(effect, "scene_action").Length == 0, "赠礼不需要场景层动手");
        // 一次性事件（repeatable=0）触发过就记账
        CheckTrue(
            state.Call("has_flag", WorldEventService.done_flag("we_hermit")).AsBool(),
            "一次性事件触发后记了账");

        // 材料同理（`we_dog` 的兽皮），顺带验非装备走堆叠
        GodotObject itemState = SoloState(db);
        WorldEventService.trigger(db, itemState, "we_dog", RngService.create(Seed));
        CheckEq(_inventory_count(itemState, "item_pelt"), 1, "野狗拦路给到 1 张兽皮");
    }

    /// <summary>线索：`we_rumor` 给醉刀客的那条线索记进线索本（不再只是「看一眼就走的空气」）。</summary>
    private void _check_hint(TableDb db)
    {
        GodotObject state = SoloState(db);
        ClueService clueService = ClueService.create(db, state);
        Godot.Collections.Dictionary before = _wine_entry(clueService);
        CheckFalse(_b(before, "hint_known"), "触发之前这条线索还没「听说」过");

        Godot.Collections.Dictionary result =
            WorldEventService.trigger(db, state, "we_rumor", RngService.create(Seed));
        Godot.Collections.Dictionary effect = _d(result, "effect");
        CheckTrue(_b(effect, "applied"), "驿站风声给到了信息");
        CheckEq(_s(effect, "id"), "trig_wine", "指向的是醉刀客那条隐藏内容");
        CheckTrue(
            state.Call("has_flag", WorldEventService.hint_flag("trig_wine")).AsBool(),
            "线索旗标写进了存档");
        Godot.Collections.Dictionary after = _wine_entry(clueService);
        CheckTrue(_b(after, "hint_known"), "线索本这时标成「已听说」");
        CheckTrue(
            clueService.describe(after).Contains("已从传闻中听说"),
            $"线索本那一行写明了来源：{clueService.describe(after)}");
    }

    private Godot.Collections.Dictionary _wine_entry(ClueService clueService)
    {
        foreach (Variant entry in clueService.clues_for_scene("scene_heifengzhai"))
        {
            Godot.Collections.Dictionary dict = entry.AsGodotDictionary();
            if (_s(dict, "id") == "trig_wine")
            {
                return dict;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    /// <summary>
    /// 判定：`we_patrol` 走**直接结算**，不要求玩家走到官道那个固定关卡（Q64 两个入口共用一行）。
    /// </summary>
    private void _check_check_effect(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary result =
            WorldEventService.trigger(db, state, "we_patrol", RngService.create(Seed));
        Godot.Collections.Dictionary effect = _d(result, "effect");
        CheckTrue(_b(effect, "applied"), "镇抚司巡查当场判定了");
        CheckEq(_s(effect, "kind"), "check", "它是判定类");
        CheckEq(_s(effect, "id"), "ev_patrol_check", "走的是共享的那一行判定");
        CheckTrue(_s(effect, "text").Length > 0, "判定结果有给玩家看的文案");
        string outcome = state.Call("event_check_result", "ev_patrol_check").AsString();
        CheckTrue(
            outcome == "done" || outcome == "failed",
            $"判定结果写进了存档（{outcome}）");
        // 这一行判定在设计里**没有小地图位点**，所以它的入口只能是随机事件
        CheckTrue(
            ((EventCheckRow)db.get_row("event_check", "ev_patrol_check")).scene_id.Length == 0,
            "ev_patrol_check 本来就不挂小地图");
    }

    /// <summary>开货架与开战：服务层只交请求，id 必须是**能落到表里的**那种（货架组要能找回建筑）。</summary>
    private void _check_scene_actions(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary trade = _d(
            WorldEventService.trigger(db, state, "we_caravan", RngService.create(Seed)), "effect");
        CheckEq(_s(trade, "scene_action"), "open_shop", "货商车队交出「开货架」请求");
        CheckEq(_s(trade, "id"), "shop_caravan", "指向的是货架组");
        CheckEq(
            ShopService.building_for_shop_group(db, "shop_caravan"),
            "bld_caravan",
            "货架组能找回那个临时建筑");
        CheckTrue(
            ShopService.create(db, state).stock_rows("bld_caravan").Count > 0,
            "行商的货架不是空的");

        Godot.Collections.Dictionary spar = _d(
            WorldEventService.trigger(db, state, "we_disciple", RngService.create(Seed)), "effect");
        CheckEq(_s(spar, "scene_action"), "start_battle", "门派弟子交出「开战」请求");
        CheckEq(_s(spar, "id"), "team_wanderer_disciple", "打的是那支历练队伍");
        var team = (EnemyTeamRow)db.get_row("enemy_team", "team_wanderer_disciple");
        CheckTrue(team != null, "那支队伍在表里");
        if (team != null)
        {
            CheckEq(team.members, "en_wanderer_disciple:2", "两人都是别派弟子");
        }

        CheckEq(
            ((EnemyBaseRow)db.get_row("enemy_base", "en_wanderer_disciple")).drop_group,
            "",
            "别派弟子不掉东西（切磋不是抢东西）");
    }

    /// <summary>反面：`effect_id` 空着、或效果类型认不出时，**显式说话**，不假装发过东西。</summary>
    private void _check_unwired(TableDb db)
    {
        GodotObject state = SoloState(db);
        var blank = (WorldEventRow)((WorldEventRow)db.get_row("world_event", "we_dog")).Duplicate();
        blank.effect_id = "";
        Godot.Collections.Dictionary blankEffect = WorldEventService.apply_effect(db, state, blank, null);
        CheckFalse(_b(blankEffect, "applied"), "没配效果时不假装发过东西");
        CheckTrue(_s(blankEffect, "note").Contains("还没配"), $"并且明说没配：{_s(blankEffect, "note")}");

        var odd = (WorldEventRow)((WorldEventRow)db.get_row("world_event", "we_dog")).Duplicate();
        odd.effect_kind = "teleport";
        Godot.Collections.Dictionary oddEffect = WorldEventService.apply_effect(db, state, odd, null);
        CheckFalse(_b(oddEffect, "applied"), "没见过的效果类型不发东西");
        CheckTrue(
            _s(oddEffect, "note").Contains("没见过的效果类型"),
            $"并且点名报错：{_s(oddEffect, "note")}");
    }

    private static int _inventory_count(GodotObject state, string itemId) =>
        state.Get("inventory").AsGodotObject().Call("count", itemId).AsInt32();

    /// <summary>装备实例在背包里存成 `equipment` 条目——按 `equipment_count()` 数（同 `TestWorldEvent`）。</summary>
    private static int _equipment_count(GodotObject state) =>
        state.Get("inventory").AsGodotObject().Call("equipment_count").AsInt32();

    /// <summary>对位 GDScript 的 `dict.get(key, 默认值)`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static Godot.Collections.Dictionary _d(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotDictionary() : new Godot.Collections.Dictionary();
}
