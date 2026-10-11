using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 大地图随机事件（C# 版，对位 `tests/test_world_event.gd` 的判定那几节；设计 19 §四）。
///
/// 五类效果分两层：`gift`／`hint`／`check` 在**服务层**结算完（发物品、记线索、走判定）；
/// `trade`／`spar` 只交出 `scene_action` 请求——开货架与切战斗要挂在当前场景的节点上。
/// 反面那两条（`effect_id` 空着、效果类型认不出）**必须显式说话**，不假装发过东西。
/// </summary>
public partial class TestWorldEvent : TestCase
{
    private const int Seed = 20261004;

    public override string SuiteName() => "大地图随机事件（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckConstants();
        _CheckWiredRows(db);
        _CheckGift(db);
        _CheckHint(db);
        _CheckCheckEffect(db);
        _CheckSceneActions(db);
        _CheckUnwired(db);
        _CheckPool(db);
    }

    /// <summary>过渡期取值口与常量同值（控制器现在读小写方法名）。</summary>
    private void _CheckConstants()
    {
        CheckFloat(WorldEventService.lifetime(), 45.0, "事件存活 45 秒", 0.0001);
        CheckFloat(WorldEventService.despawn_distance(), 320.0, "走开 320px 就消失", 0.0001);
        CheckFloat(WorldEventService.trigger_distance(), 24.0, "触发半径 24px", 0.0001);
        CheckFloat(WorldEventService.spawn_min(), 96.0, "生成内半径 96px", 0.0001);
        CheckFloat(WorldEventService.spawn_max(), 224.0, "生成外半径 224px", 0.0001);
        CheckFloat(WorldEventService.cooldown(), 20.0, "两次事件最短间隔 20 秒", 0.0001);
        CheckEq(WorldEventService.done_flag("we_hermit"), "world_event_done_we_hermit", "记账旗标的名字形状");
        CheckEq(WorldEventService.hint_flag("trig_wine"), "clue_hint_trig_wine", "线索旗标的名字形状");
    }

    /// <summary>八条事件的 `effect_kind`／`effect_id` 与 0.28.0 的 Q64 表逐条一致（空一行就会拿到占位文案）。</summary>
    private void _CheckWiredRows(TableDb db)
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
        int rows = 0;
        foreach (Variant entry in db.rows("world_event"))
        {
            var row = (WorldEventRow)entry.AsGodotObject();
            rows++;
            CheckTrue(row.effect_id.Length > 0, $"{row.event_id} 的效果有指向（0.28.0 之前八行全空）");
            CheckTrue(expected.ContainsKey(row.event_id), $"{row.event_id} 在用例登记的八条里");
        }
        CheckEq(rows, 8, "大地图随机事件 8 条");
        foreach (var pair in expected)
        {
            var row = (WorldEventRow)db.get_row("world_event", pair.Key);
            CheckEq(row.effect_kind, pair.Value.Kind, $"{pair.Key} 的效果类型");
            CheckEq(row.effect_id, pair.Value.Id, $"{pair.Key} 的效果指向");
        }
    }

    /// <summary>赠礼：隐士给良品剑（一次性事件触发后记账）；材料走堆叠。</summary>
    private void _CheckGift(TableDb db)
    {
        GodotObject state = SoloState(db);
        int before = _EquipmentCount(state);
        Godot.Collections.Dictionary effect =
            WorldEventService.trigger(db, state, "we_hermit", RngService.create(Seed))["effect"].AsGodotDictionary();
        CheckTrue(effect["applied"].AsBool(), "隐士奇遇的赠礼真的发出来了");
        CheckEq(effect["kind"].AsString(), "gift", "它是赠礼类");
        CheckEq(effect["id"].AsString(), "eq_sword_04", "给的是锈月");
        CheckEq(_EquipmentCount(state), before + 1, "背包里多出 1 件装备实例");
        CheckEq(effect["scene_action"].AsString(), "", "赠礼不需要场景层动手");
        CheckTrue(
            state.Call("has_flag", WorldEventService.done_flag("we_hermit")).AsBool(),
            "一次性事件触发后记了账");

        GodotObject item_state = SoloState(db);
        WorldEventService.trigger(db, item_state, "we_dog", RngService.create(Seed));
        CheckEq(_InventoryCount(item_state, "item_pelt"), 1, "野狗拦路给到 1 张兽皮（非装备走堆叠）");
    }

    /// <summary>线索：驿站风声记进线索本的那枚旗标（不再只是「看一眼就走的空气」）。</summary>
    private void _CheckHint(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckFalse(
            state.Call("has_flag", WorldEventService.hint_flag("trig_wine")).AsBool(),
            "触发之前还没「听说过」这条线索");
        Godot.Collections.Dictionary effect =
            WorldEventService.trigger(db, state, "we_rumor", RngService.create(Seed))["effect"].AsGodotDictionary();
        CheckTrue(effect["applied"].AsBool(), "驿站风声给到了信息");
        CheckEq(effect["id"].AsString(), "trig_wine", "指向醉刀客那条隐藏内容");
        CheckTrue(
            state.Call("has_flag", WorldEventService.hint_flag("trig_wine")).AsBool(),
            "线索旗标写进了存档");
    }

    /// <summary>判定：`we_patrol` 走直接结算，不要求玩家走到官道那个固定关卡（两个入口共用一行）。</summary>
    private void _CheckCheckEffect(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary effect =
            WorldEventService.trigger(db, state, "we_patrol", RngService.create(Seed))["effect"].AsGodotDictionary();
        CheckTrue(effect["applied"].AsBool(), "镇抚司巡查当场判定了");
        CheckEq(effect["kind"].AsString(), "check", "它是判定类");
        CheckEq(effect["id"].AsString(), "ev_patrol_check", "走的是共享的那一行判定");
        CheckTrue(effect["text"].AsString().Length > 0, "判定结果有给玩家看的文案");
        string outcome = state.Call("event_check_result", "ev_patrol_check").AsString();
        CheckTrue(outcome == "done" || outcome == "failed", $"判定结果写进了存档（{outcome}）");
        CheckEq(
            ((EventCheckRow)db.get_row("event_check", "ev_patrol_check")).scene_id, "",
            "ev_patrol_check 本来就不挂小地图（入口只能是随机事件）");
    }

    /// <summary>开货架与开战：服务层只交请求，id 必须是能落到表里的那种。</summary>
    private void _CheckSceneActions(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary trade =
            WorldEventService.trigger(db, state, "we_caravan", RngService.create(Seed))["effect"].AsGodotDictionary();
        CheckEq(trade["scene_action"].AsString(), "open_shop", "货商车队交出「开货架」请求");
        CheckEq(trade["id"].AsString(), "shop_caravan", "指向的是货架组");

        Godot.Collections.Dictionary spar =
            WorldEventService.trigger(db, state, "we_disciple", RngService.create(Seed))["effect"].AsGodotDictionary();
        CheckEq(spar["scene_action"].AsString(), "start_battle", "门派弟子交出「开战」请求");
        CheckEq(spar["id"].AsString(), "team_wanderer_disciple", "打的是那支历练队伍");
    }

    /// <summary>反面：`effect_id` 空着、或效果类型认不出时，显式说话，不假装发过东西。</summary>
    private void _CheckUnwired(TableDb db)
    {
        GodotObject state = SoloState(db);
        var blank = (WorldEventRow)((WorldEventRow)db.get_row("world_event", "we_dog")).Duplicate(true);
        blank.effect_id = "";
        Godot.Collections.Dictionary blank_effect = WorldEventService.apply_effect(db, state, blank, null);
        CheckFalse(blank_effect["applied"].AsBool(), "没配效果时不假装发过东西");
        CheckTrue(blank_effect["note"].AsString().Contains("还没配"), "并且明说没配：" + blank_effect["note"]);

        var odd = (WorldEventRow)((WorldEventRow)db.get_row("world_event", "we_dog")).Duplicate(true);
        odd.effect_kind = "teleport";
        Godot.Collections.Dictionary odd_effect = WorldEventService.apply_effect(db, state, odd, null);
        CheckFalse(odd_effect["applied"].AsBool(), "没见过的效果类型不发东西");
        CheckTrue(odd_effect["note"].AsString().Contains("没见过的效果类型"), "并且点名报错：" + odd_effect["note"]);
    }

    /// <summary>池子：`region_tags` 按 `;` 拆、一次性事件出过就退出池子、空池子返回 `null`。</summary>
    private void _CheckPool(TableDb db)
    {
        CheckEq(WorldEventService.rows_for_region(db, "n_post_station").Count, 2, "驿站能出两条事件（巡查／风声）");
        CheckEq(
            ((WorldEventRow)WorldEventService.pick(db, SoloState(db), "n_post_station", 0.0)).event_id,
            "we_patrol", "roll=0 取第一条");
        CheckEq(
            ((WorldEventRow)WorldEventService.pick(db, SoloState(db), "n_post_station", 0.999999)).event_id,
            "we_rumor", "roll→1 取最后一条");

        GodotObject state = SoloState(db);
        CheckTrue(_EligibleIds(db, state, "n_cave_collapse").Contains("we_hermit"), "隐士奇遇一开始可出");
        WorldEventService.trigger(db, state, "we_hermit", RngService.create(Seed));
        CheckFalse(_EligibleIds(db, state, "n_cave_collapse").Contains("we_hermit"), "一次性事件出过之后不再出");
        CheckNull(WorldEventService.pick(db, state, "n_nowhere", 0.5), "没有池子时 pick 返回 null");
    }

    private static System.Collections.Generic.HashSet<string> _EligibleIds(
        TableDb db, GodotObject state, string region_id)
    {
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in WorldEventService.eligible(db, state, region_id))
        {
            ids.Add(((WorldEventRow)entry.AsGodotObject()).event_id);
        }
        return ids;
    }

    private static int _InventoryCount(GodotObject state, string item_id) =>
        state.Get("inventory").AsGodotObject().Call("count", item_id).AsInt32();

    /// <summary>装备实例在背包里存成 `equipment` 字典（实例 id → 条目），不是数组——按 `equipment_count()` 数。</summary>
    private static int _EquipmentCount(GodotObject state) =>
        state.Get("inventory").AsGodotObject().Call("equipment_count").AsInt32();
}
