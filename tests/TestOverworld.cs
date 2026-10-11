using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 大地图与明雷（C# 版，对位原 `tests/test_overworld.gd`）：
/// 明雷生成、行为、接触判定、遭遇交接到战斗、打完回图后的清怪状态。
///
/// 这一份用例**真的把 `world_run.tscn` 装起来跑**（不是纯逻辑）——控制器是 C#，
/// 所以这里直接调它的公开口，GDScript 侧的东西（`GameState`／`GameSession`／战斗界面）
/// 一律走 `Call`／`Get`／`Set`。
/// </summary>
public partial class TestOverworld : TestCase
{
    private const string OverworldRun = "res://scenes/world_run.tscn";
    private const string BattleScene = "res://scenes/battle_screen.tscn";
    private const string SaveTestDir = "res://.logs/test_save_timing/run_world";
    /// <summary>一格 32px（`proximity_N` 的口径：`<= N × 32px` 才算走到跟前）。</summary>
    private const float Tile = 32.0f;

    public override string SuiteName() => "大地图与明雷";

    // ---- GDScript 侧对象的取值口（迁移期它们还是 .gd，收紧类型要等那一批） ----
    private static GodotObject _gd(string path) => GD.Load<GDScript>(path);
    private static GodotObject _new_game(TableDb db, string difficulty, string[] charIds) =>
        GameState.new_game(db, difficulty, charIds, 0);
    private static bool _has_flag(GodotObject state, string flag) => state.Call("has_flag", flag).AsBool();
    private static Godot.Collections.Dictionary _flags(GodotObject state) => state.Get("flags").AsGodotDictionary();
    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();
    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;
    private static string _join_cn(Variant value)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant item in value.AsGodotArray())
        {
            parts.Add(item.AsString());
        }

        return string.Join("、", parts);
    }

    /// <summary>`test_case.gd::satisfy_condition` 的对位实现：把条件里的旗标都点亮。</summary>
    private static void SatisfyCondition(GodotObject state, string condition)
    {
        foreach (string raw in condition.Replace("|", "&").Split('&'))
        {
            string token = raw.StripEdges();
            if (token.Length == 0 || token.StartsWith("!"))
            {
                continue;
            }

            if (token.StartsWith("flag_"))
            {
                state.Call("set_flag", token, true);
            }
        }
    }

    private static OverworldController _load_overworld()
    {
        var packed = GD.Load<PackedScene>(OverworldRun);
        return (OverworldController)packed.Instantiate();
    }

    private static RoamingEnemy _enemy(OverworldController controller, string spawn_id)
    {
        foreach (Variant enemy_variant in controller.enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy != null && enemy.spawn_id == spawn_id)
            {
                return enemy;
            }
        }

        return null;
    }

    public override void Run()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树");
            return;
        }

        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        _CheckWalkSpeedCriterion(db);
        // 0.8.1：大地图明雷默认**关着**（`feature_toggle.overworld_roaming_enemy=0`）。
        // 这个用例验的是明雷机制本身，所以先把会话级开关打开（跑完在 _CheckRoamingToggle 里还原）。
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        sessionNode?.Set("roaming_enabled_override", 1);
        // 会话里的 `state` 必须是本用例这一份：`enter_local_map` 走的是
        // `GameSession.set_world_position()`，它优先写 `state`、**只在 state 为空时才用 fallback**。
        // 前一个用例（小地图）会把会话的 state 留成它自己那份，不重设的话
        // 「落点同步进存档的 world_pos」这条断言会写到别人身上（2026-10-11 实测）。
        sessionNode?.Call("set_state", state);
        OverworldController controller = _load_overworld();
        controller.state_override = state;
        var captures = new Godot.Collections.Array();
        controller.battle_switch_handler = Callable.From<Variant>(encounter => captures.Add(encounter));
        Tree.Root.AddChild(controller);
        controller.setup();

        _CheckSpawn(db, controller);
        _CheckContact(controller, captures);
        _CheckNeutral(controller, captures);
        _CheckSneak(controller, captures);
        _CheckWorldMap(controller, state);
        _CheckWorldMapService(db);
        _CheckNodeIcons(controller);
        _CheckSaveOnEnter(controller, state);
        _CheckLoop(db, controller, captures, state);
        _CheckPortalArm(db, controller, state);
        _CheckLockedPortal(db, controller, state);
        _CheckEnterPositionRule(db, controller);
        _CheckCleared(db, state);
        _CheckRespawn(db, controller, state);
        _CheckRespawnUnderPlayer(db, controller);
        _CheckRetreatPushOut(db, state);
        _CheckPinnedInteractionDistances();
        _CheckRoamingToggle(db, state);
        _CheckFieldBuffHud(controller);
        _CheckFlavorHighlights(controller);
        // 区域招募（09 §3.2 的落雁坡那条路）：用**独立的一份存档与大地图实例**跑，
        // 免得把上面那份共用 state（队伍人数会被它改）带偏
        _CheckRegionRecruit(db);
        _CheckRegionOriginGift(db);
        _CheckConditionalLayer(db);
        _CheckOverlayLayer(controller);
        Tree.Root.RemoveChild(controller);
        controller.Free();
    }

    /// <summary>
    /// 大地图侧同一个坑（2026-10-04 实测的「NPC 面板打不开」）：`npc_panel` 的根是 `Control`，
    /// 挂到世界节点下会跟着相机跑出屏幕——修法是 `OverlayStack.mount()` 包一层画布层。
    /// </summary>
    private void _CheckOverlayLayer(OverworldController controller)
    {
        Godot.Collections.Dictionary opened = controller.open_npc("npc_qian_dafu");
        CheckTrue(_b(opened, "ok"), $"大地图上的 NPC 面板开得起来：{_s(opened, "error")}");
        if (controller.npc_panel != null)
        {
            CheckTrue(controller.npc_panel.GetParent() is CanvasLayer,
                "大地图上的 NPC 面板挂在画布层下（不是世界节点下）");
        }
    }

    /// <summary>
    /// 条件地表层（`Conditional`，0.32.0）：大地图上那条「落雁坡西 → 石隙迷窟」的碎石细径，
    /// 道具是 `item_treasure_map`——到手之前它在地图上不存在。
    /// </summary>
    private void _CheckConditionalLayer(TableDb db)
    {
        GodotObject state = SoloState(db);
        OverworldController map = _load_overworld();
        map.state_override = state;
        Tree.Root.AddChild(map);
        map.setup();
        var layer = map.world.GetNodeOrNull<TileMapLayer>("Conditional");
        CheckNotNull(layer, "大地图有 `Conditional` 层（藏宝图那条细径铺在它上面）");
        CheckNotNull(map.player, "独立实例里玩家在场");
        if (layer == null)
        {
            Tree.Root.RemoveChild(map);
            map.Free();
            return;
        }

        CheckGt(layer.GetUsedCells().Count, 0.0, $"这条细径不是空的（{layer.GetUsedCells().Count} 格）");
        Godot.Collections.Dictionary rule =
            WorldMapService.conditional_layer_rule(db, map._landmark_node_ids());
        CheckEq(_join_cn(_v(rule, "items")), "item_treasure_map",
            "条件是藏宝图（来自表里石隙迷窟的 unlock_condition，不是代码常量）");
        CheckFalse(layer.Visible, "开局没有藏宝图 → 细径不在地图上");
        // 走到石隙迷窟的地标跟前也不该露出来（它自己是「持图才揭开」）
        var node = map.world.GetNodeOrNull<Node2D>("Markers/Node_n_shixi");
        if (node != null)
        {
            map.player.GlobalPosition = node.GlobalPosition;
            map._check_reveals();
        }

        CheckFalse(map.world_map.is_revealed("n_shixi"), "没图时石隙迷窟本身也不在地图上");
        CheckFalse(layer.Visible, "走到跟前也还是看不到那条细径");
        Godot.Collections.Dictionary added = state.Get("inventory").As<Inventory>()
            .add_item(db, "item_treasure_map", 1);
        CheckTrue(_b(added, "ok"), $"藏宝图能放进背包：{_s(added, "error")}");
        map._apply_conditional_layer();
        CheckTrue(layer.Visible, "拿到藏宝图 → 细径当场出现（不用回大地图再进一次）");
        map._check_reveals();
        CheckTrue(map.world_map.is_revealed("n_shixi"), "图到手，石隙迷窟这个地标也跟着揭开");
        Tree.Root.RemoveChild(map);
        map.Free();
    }

    /// <summary>
    /// 走到 `Markers/Node_n_luoyanpo` 跟前 → 把林铁山收进来（设计 09 §3.2 的区域招募）。
    /// </summary>
    private void _CheckRegionRecruit(TableDb db)
    {
        GodotObject state = SoloState(db);
        const string charId = "ch_gang"; // 落雁坡那位（`recruit_def.join_scene = n_luoyanpo`）
        Resource row = db.get_row("recruit_def", charId);
        CheckNotNull(row, "表里有林铁山那一行");
        if (row == null)
        {
            return;
        }

        CheckEq(row.Get("join_scene").AsString(), "n_luoyanpo", "他的加入点写在区域节点上（不是小地图）");
        SatisfyCondition(state, row.Get("join_condition").AsString());
        int beforeParty = state.Call("party_size").AsInt32();
        OverworldController map = _load_overworld();
        map.state_override = state;
        Tree.Root.AddChild(map);
        map.setup();
        var node = map.world.GetNodeOrNull<Node2D>("Markers/Node_n_luoyanpo");
        CheckNotNull(node, "大地图上有落雁坡地标");
        // 出生点在清风驿，离落雁坡很远：进图那一下不该已经收人
        CheckEq(state.Call("party_size").AsInt32(), beforeParty, "刚进图（人在清风驿）还没收到人");
        if (node != null)
        {
            map.player.GlobalPosition = node.GlobalPosition;
            Godot.Collections.Array joined = map._check_region_recruits();
            CheckEq(joined.Count, 1, "走到落雁坡地标就收到一位同伴");
            CheckEq(state.Call("party_size").AsInt32(), beforeParty + 1, "队伍多了一个人");
            CheckTrue(
                System.Array.Exists(state.Get("char_ids").AsStringArray(), id => id == charId),
                "林铁山进了队伍名单");
            CheckTrue(_has_flag(state, $"flag_{charId}_joined"), $"入队点亮 flag_{charId}_joined");
            // 走开再走回来不该重复收（大地图每帧都会调这个检查）
            map.player.GlobalPosition = node.GlobalPosition + new Vector2(400, 0);
            map._check_region_recruits();
            map.player.GlobalPosition = node.GlobalPosition;
            Godot.Collections.Array again = map._check_region_recruits();
            CheckEq(again.Count, 0, "已经入队的人不会再收一次");
            CheckEq(state.Call("party_size").AsInt32(), beforeParty + 1, "队伍人数没变");
        }

        Tree.Root.RemoveChild(map);
        map.Free();
    }

    /// <summary>战斗外增益 HUD（08）：大地图上也必须看得见还剩几分钟（与城镇共用 `FieldBuffHud` 的文案）。</summary>
    private void _CheckFieldBuffHud(OverworldController controller)
    {
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        CheckNotNull(sessionNode, "用例能拿到会话");
        if (sessionNode == null)
        {
            return;
        }

        sessionNode.Call("clear_field_buffs");
        sessionNode.Call("add_field_buff", "buff_meditated", 10, "", 1);
        controller._refresh_field_buffs();
        var label = controller.FindChild("FieldBuffs", true, false) as Label;
        CheckNotNull(label, "大地图 HUD 上有战斗外增益那一行");
        if (label != null)
        {
            CheckTrue(label.Text.Contains("打坐余韵"), $"写清是哪个增益：{label.Text}");
            CheckTrue(label.Text.Contains("剩 10 分钟"), $"写清还剩几分钟：{label.Text}");
        }

        // 同一条 buff 再来一次 = 刷新，不叠一行（08：`buff_meditated` 的 stack_rule=refresh）
        sessionNode.Call("add_field_buff", "buff_meditated", 10, "", 1);
        CheckEq(sessionNode.Call("active_field_buffs", -1).AsGodotArray().Count, 1, "重复获得只刷新，不变成两条");
        sessionNode.Call("clear_field_buffs");
        controller._refresh_field_buffs();
        if (label != null)
        {
            CheckEq(label.Text, "", "清掉之后这一行是空的");
        }
    }

    /// <summary>
    /// 0.8.1 的大地图明雷开关：默认（读表）关着；开关注 1 时 17 个位点照旧全部生成。
    /// </summary>
    private void _CheckRoamingToggle(TableDb db, GodotObject state)
    {
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");

        // ① 会话覆盖 = 0：一张大地图上不该有明雷
        sessionNode?.Set("roaming_enabled_override", 0);
        OverworldController off = _load_overworld();
        off.state_override = state;
        Tree.Root.AddChild(off);
        off.setup();
        CheckEq(off.enemies.Count, 0, "开关 0：大地图不生成明雷");
        CheckFalse(off.roaming_enabled(), "开关 0：roaming_enabled() 为假");
        Tree.Root.RemoveChild(off);
        off.Free();

        // ② 会话覆盖 = 1：17 个位点照旧全部生成（机制没被删，只是默认关）
        sessionNode?.Set("roaming_enabled_override", 1);
        OverworldController on = _load_overworld();
        on.state_override = state;
        Tree.Root.AddChild(on);
        on.setup();
        CheckEq(on.enemies.Count, db.rows("roaming_spawn").Count,
            $"开关 1：{db.rows("roaming_spawn").Count} 个明雷位点照旧全部生成");
        CheckTrue(on.roaming_enabled(), "开关 1：roaming_enabled() 为真");
        Tree.Root.RemoveChild(on);
        on.Free();

        // ③ 恢复"读表"：默认值就是 design 定的 0
        sessionNode?.Set("roaming_enabled_override", -1);
        OverworldController byTable = _load_overworld();
        byTable.state_override = state;
        Tree.Root.AddChild(byTable);
        byTable.setup();
        CheckFalse(byTable.roaming_enabled(), "不覆盖时读表：默认 0（大地图不出现明雷）");
        CheckEq(db.get_row("feature_toggle", "overworld_roaming_enemy").Get("value").AsInt32(), 0,
            "表里 overworld_roaming_enemy 的默认值就是 0");
        Tree.Root.RemoveChild(byTable);
        byTable.Free();
    }

    /// <summary>
    /// 大地图三个「交互距离」都是开发侧定的（表里没有这些列），而且各自钉着一条既有规则。
    /// </summary>
    private void _CheckPinnedInteractionDistances()
    {
        CheckFloat(OverworldController.portal_distance(), 26.0, "门户/驿站交互距离 26px");
        CheckFloat(OverworldController.portal_arm_margin(), 8.0,
            "出图武装边距 8px（决策 48：出生点压在出口上时『先走出去一次』才武装）");
        CheckFloat(OverworldController.discover_distance(), 96.0, "地标揭开距离 96px（设计 02：走进去才揭开）");
    }

    /// <summary>本章不开放的小地图（废弃渡口）：走过去要**被挡住**，而不是进图。</summary>
    private void _CheckLockedPortal(TableDb db, OverworldController controller, GodotObject state)
    {
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        sessionNode.Set("pending_local_scene", "");
        var switched = new Godot.Collections.Array();
        controller.scene_change_handler = Callable.From<string>(path => switched.Add(path));
        var ferry = controller.world.GetNodeOrNull<Node2D>("Markers/Portal_scene_ferry_locked");
        CheckNotNull(ferry, "大地图上有通往废弃渡口的入口位点");
        if (ferry == null)
        {
            return;
        }

        // 先站远处让入口武装（PORTAL_ARM_MARGIN），再走进圈里
        controller.player.GlobalPosition = ferry.Position + new Vector2(400, 0);
        controller._check_portal();
        controller.player.GlobalPosition = ferry.Position;
        controller._check_portal();
        CheckEq(switched.Count, 0, "本章不开放的图：不会切场景");
        CheckTrue(controller._status.Text.Contains("本章不可前往"),
            $"状态栏说明为什么进不去：{controller._status.Text}");
        CheckEq(sessionNode.Get("pending_local_scene").AsString(), "", "也没把「待进入的小地图」写进会话");
    }

    /// <summary>
    /// 进图时的**回程落点**规则（设计 02「返回大地图原位置」的两种情形）：
    /// 走进去 → 原位置 = 你当时站的地方；驿站传送 → 原位置 = **目的地的地标**。
    /// </summary>
    private void _CheckEnterPositionRule(TableDb db, OverworldController controller)
    {
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        var switched = new Godot.Collections.Array();
        controller.scene_change_handler = Callable.From<string>(path => switched.Add(path));

        var walkSpot = new Vector2(700, 300);
        controller.player.GlobalPosition = walkSpot;
        controller.enter_local_map("scene_heifengzhai");
        CheckEq(switched.Count, 1, "走进去：切场景被触发");
        CheckEq(sessionNode.Get("world_position").AsVector2(), walkSpot, "走进去：回程落点 = 你站的位置");
        CheckEq(sessionNode.Get("pending_local_scene").AsString(), "scene_heifengzhai", "走进去：写清进哪张图");
        // 设计 09 §二：这个落点要跟着存档走（v13）
        CheckEq(controller.current_state().Call("world_position").AsVector2(), walkSpot,
            "走进去：落点同步进存档的 world_pos（v13）");

        Resource huangcun = db.get_row("map_region", "n_huangcun");
        var expect = new Vector2(
            (float)huangcun.Get("pos_x").AsDouble(), (float)huangcun.Get("pos_y").AsDouble());
        controller.enter_local_map("scene_huangcun", true);
        CheckEq(switched.Count, 2, "传送：也切场景");
        CheckEq(sessionNode.Get("world_position").AsVector2(), expect, "驿站传送：回程落点 = 目的地地标（不拽回驿站）");
        CheckEq(sessionNode.Get("pending_local_scene").AsString(), "scene_huangcun", "传送：写清进哪张图");

        // 收尾：别把注入的 handler 留给后面的用例
        controller.scene_change_handler = new Callable();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 入口武装：人正站在入口位点上时**不**自动进图；先走出圈外、再走近才进。
    /// </summary>
    private void _CheckPortalArm(TableDb db, OverworldController controller, GodotObject state)
    {
        var portalMarker = controller.world.GetNodeOrNull<Node2D>("Markers/Portal_scene_qingfengyi");
        CheckNotNull(portalMarker, "大地图上有清风驿的入口位点");
        if (portalMarker == null)
        {
            return;
        }

        Vector2 portalPos = portalMarker.GlobalPosition;
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        var switched = new Godot.Collections.Array();
        controller.scene_change_handler = Callable.From<string>(path => switched.Add(path));
        controller._portal_armed = false;
        sessionNode.Set("pending_local_scene", "");

        // ① 刚回图，人就站在入口上 → 不进图
        controller.player.GlobalPosition = portalPos;
        controller._check_portal();
        CheckEq(switched.Count, 0, "刚落在大地图入口上时不会自动进图（不然出图就被送回去）");
        CheckTrue(sessionNode.Get("pending_local_scene").AsString().Length == 0, "也没写待进入的小地图");

        // ② 走出圈外 → 入口武装
        controller.player.GlobalPosition = portalPos + new Vector2(200, 0);
        controller._check_portal();
        CheckEq(switched.Count, 0, "走开的过程不进图");

        // ③ 再走回来 → 这次要进图，并写清进哪张
        controller.player.GlobalPosition = portalPos;
        controller._check_portal();
        CheckEq(switched.Count, 1, "走出圈外再走近才进图");
        CheckEq(sessionNode.Get("pending_local_scene").AsString(), "scene_qingfengyi", "写清进哪张小地图");

        // 收尾：别把注入的 handler 和状态留给后面的用例
        controller.scene_change_handler = new Callable();
        sessionNode.Set("pending_local_scene", "");
        controller._portal_armed = true;
    }

    /// <summary>
    /// 地标图标（设计 02 + 07 §8.1 + 16 §3.5）：已揭开画正常图标、没揭开不画、
    /// 本章去不了的用暗版、脚下最近的高亮。
    /// </summary>
    private void _CheckNodeIcons(OverworldController controller)
    {
        Sprite2D city = controller.node_icon("n_qingfengyi");
        CheckNotNull(city, "清风驿有图标节点");
        if (city != null)
        {
            CheckTrue(city.Visible, "已揭开的清风驿图标可见");
            CheckTrue(city.Texture.ResourcePath.Contains("icon_town"),
                $"用的是表里配的 icon_town：{city.Texture.ResourcePath}");
        }

        Sprite2D hiddenIcon = controller.node_icon("n_huangcun");
        CheckNotNull(hiddenIcon, "荒村有图标节点");
        if (hiddenIcon != null)
        {
            CheckFalse(hiddenIcon.Visible, "还没揭开的地标不画图标（不然是剧透）");
        }

        CheckTrue(controller.node_icon("n_missing") == null, "表里没有的地标没有图标");
        // 地标名字：规则与图标一致（已揭开才有名字，未揭开不写——写出来就是剧透）
        var cityLabel = controller.FindChild("NodeLabel_n_qingfengyi", true, false) as Label;
        CheckNotNull(cityLabel, "清风驿有名字标签");
        if (cityLabel != null)
        {
            CheckEq(cityLabel.Text, "清风驿", "名字取自 map_region.name_cn");
            CheckTrue(cityLabel.Visible, "已揭开的地标名字可见");
        }

        var hiddenLabel = controller.FindChild("NodeLabel_n_huangcun", true, false) as Label;
        CheckNotNull(hiddenLabel, "荒村也有名字节点");
        if (hiddenLabel != null)
        {
            CheckFalse(hiddenLabel.Visible, "还没揭开的地标不写名字");
        }

        // 走到荒村跟前揭开：**名字当场出现，图标仍然不画**（它是兴趣点，0.32.0 靠地形认）
        var ruin = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_huangcun");
        CheckNotNull(ruin, "地图上有荒村位点");
        if (ruin != null)
        {
            controller.player.GlobalPosition = ruin.GlobalPosition + new Vector2(0, 40);
            controller._check_reveals();
            var ruinLabel = controller.FindChild("NodeLabel_n_huangcun", true, false) as Label;
            if (ruinLabel != null)
            {
                CheckTrue(ruinLabel.Visible, "揭开后名字跟着出现");
                CheckEq(ruinLabel.Text, "荒村", "名字是荒村");
            }

            Sprite2D ruinIcon = controller.node_icon("n_huangcun");
            if (ruinIcon != null)
            {
                CheckFalse(ruinIcon.Visible, "揭开了也不画图标：荒村是兴趣点（16 §3.5）");
            }
        }

        // 走到落雁坡跟前揭开：这个有 `icon_wild`，揭开后要用**正常版**（它没被锁）
        var slope = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_luoyanpo");
        CheckNotNull(slope, "地图上有落雁坡位点");
        if (slope != null)
        {
            controller.player.GlobalPosition = slope.GlobalPosition + new Vector2(0, 40);
            controller._check_reveals();
            Sprite2D slopeIcon = controller.node_icon("n_luoyanpo");
            CheckNotNull(slopeIcon, "落雁坡有图标节点");
            if (slopeIcon != null)
            {
                CheckTrue(slopeIcon.Visible, "揭开后落雁坡图标出现");
                CheckTrue(slopeIcon.Texture.ResourcePath.Contains("icon_wild")
                    && !slopeIcon.Texture.ResourcePath.Contains("_dim"),
                    $"没锁的地标用正常版图标：{slopeIcon.Texture.ResourcePath}");
            }
        }

        // 走到废弃渡口跟前：它是本章去不了的**兴趣点**——所以是「有名字（压暗）、没有图标」
        var ferry = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_ferry_abandoned");
        CheckNotNull(ferry, "地图上有废弃渡口位点");
        if (ferry != null)
        {
            controller.player.GlobalPosition = ferry.GlobalPosition + new Vector2(0, 40);
            controller._check_reveals();
            Sprite2D ferryIcon = controller.node_icon("n_ferry_abandoned");
            CheckNotNull(ferryIcon, "废弃渡口有图标节点");
            if (ferryIcon != null)
            {
                CheckFalse(ferryIcon.Visible, "渡口是兴趣点：揭开了也不画图标");
            }

            var ferryLabel = controller.FindChild("NodeLabel_n_ferry_abandoned", true, false) as Label;
            CheckNotNull(ferryLabel, "废弃渡口有名字节点");
            if (ferryLabel != null)
            {
                CheckTrue(ferryLabel.Visible, "揭开后名字出现");
                CheckTrue(ferryLabel.Modulate.R < 0.9f, $"本章去不了的地标名字压暗：{ferryLabel.Modulate}");
            }
        }

        // 高亮：站到清风驿旁边才亮，站远了不亮
        var cityMarker = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_qingfengyi");
        CheckNotNull(cityMarker, "地图上有清风驿地标位点");
        if (cityMarker != null)
        {
            controller.player.GlobalPosition = cityMarker.GlobalPosition;
            controller._update_highlight();
            CheckTrue(controller.highlight_visible(), "站在地标旁边有高亮");
            controller.player.GlobalPosition = cityMarker.GlobalPosition + new Vector2(400, 400);
            controller._update_highlight();
            CheckFalse(controller.highlight_visible(), "走远了高亮消失");
        }
    }

    /// <summary>设计 02：大地图在「切换小地图时」自动存档。</summary>
    private void _CheckSaveOnEnter(OverworldController controller, GodotObject state)
    {
        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        state.Set("slot", 3);
        controller.save_store_override = store;
        controller._save_service = null;
        var changes = new Godot.Collections.Array();
        controller.scene_change_handler = Callable.From<string>(path => changes.Add(path));
        controller.enter_local_map("scene_qingfengyi");
        CheckEq(changes.Count, 1, "进入小地图会切场景");
        CheckTrue(changes[0].AsString().Contains("local_run"), $"切的是小地图场景：{changes[0]}");
        CheckEq(controller.save_service().last_reason, "切换小地图", "切图前自动存档");
        CheckTrue(controller.save_service().last_auto, "标记为自动存档");
        CheckTrue(store.slot_exists(3), "自动存档真的落盘了");
    }

    /// <summary>潜行：移速 60% 之外，**被发现判定也要打折**（combat_const.sneak_detect_reduce = 0.4）。</summary>
    private void _CheckSneak(OverworldController controller, Godot.Collections.Array captures)
    {
        RoamingEnemy wolf = _enemy(controller, "sp_lp_lonewolf");
        CheckNotNull(wolf, "大地图上有追击型明雷（独目狼）");
        if (wolf == null)
        {
            return;
        }

        CheckEq(wolf.behavior, "chase", "独目狼是追击型");
        CheckGt(wolf.sneak_detect_reduce, 0.0, "控制层把潜行折扣注入了明雷");
        float fullRadius = wolf.alert_radius * 32.0f;
        // 站到「警戒圈内、潜行后的判断圈外」的位置
        controller.player.GlobalPosition = wolf.GlobalPosition + new Vector2(fullRadius * 0.8f, 0);
        CheckGt(wolf.effective_alert_radius(), fullRadius * 0.8, "潜行时有效半径缩小");
        controller.player.sneaking = true;
        int before = captures.Count;
        wolf.reset_latch();
        wolf._check_contact();
        CheckEq(captures.Count, before, "潜行时站在警戒圈边缘不会被发现");
        controller.player.sneaking = false;
        wolf.reset_latch();
        wolf._check_contact();
        CheckEq(captures.Count, before + 1, "不潜行时同样位置就会被发现");
        if (captures.Count > before)
        {
            CheckEq(((Encounter)captures[before].AsGodotObject()).contact, "spotted",
                "被发现是 spotted（潜行失败进入追击流程）");
        }

        // 潜行提示
        controller.player.sneaking = true;
        controller._track_sneak();
        CheckTrue(controller._status.Text.Contains("潜行中"), $"状态栏提示潜行：{controller._status.Text}");
        controller.player.sneaking = false;
        controller._track_sneak();
        CheckFalse(controller._status.Text.Contains("潜行中"), "退出潜行后提示恢复");
    }

    /// <summary>揭雾与驿站：走近地标自动揭开并写存档；走到驿站按 E 能开面板、传送与改难度。</summary>
    private void _CheckWorldMap(OverworldController controller, GodotObject state)
    {
        CheckNotNull(controller.world_map, "大地图有揭开服务");
        CheckTrue(controller.map_progress_text().Contains("地图"), $"HUD 显示已探索进度：{controller.map_progress_text()}");
        // 0.32.0：开局公开的只有城镇与驿站（野外要自己走到跟前）
        CheckTrue(controller.world_map.is_revealed("n_qingfengyi"),
            "清风驿一开始就可见（城镇默认公开）");
        CheckTrue(controller.world_map.is_revealed("n_post_station"), "驿站一开始就可见");
        CheckFalse(controller.world_map.is_revealed("n_luoyanpo"), "落雁坡是野外，开局不亮（0.32.0）");
        // 揭雾在画面上要看得见：已经揭开的地标周围，雾格被擦掉
        int fogBefore = controller.fog_cells();
        CheckGt(fogBefore, 0.0, $"地图上有雾层（{fogBefore} 格）");
        var fogLayer = controller.world.GetNodeOrNull<TileMapLayer>("Fog");
        CheckNotNull(fogLayer, "大地图有 Fog 图层");
        if (fogLayer != null)
        {
            foreach (string nodeId in new[] { "n_qingfengyi", "n_post_station" })
            {
                var marker = controller.world.GetNodeOrNull<Node2D>($"Markers/Node_{nodeId}");
                CheckNotNull(marker, $"地图上有 {nodeId} 位点");
                if (marker == null)
                {
                    continue;
                }

                Vector2I cell = fogLayer.LocalToMap(fogLayer.ToLocal(marker.GlobalPosition));
                CheckEq(fogLayer.GetCellSourceId(cell), -1, $"{nodeId} 脚下的雾已经擦掉");
            }

            var hidden = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_heifengzhai");
            if (hidden != null)
            {
                Vector2I hiddenCell = fogLayer.LocalToMap(fogLayer.ToLocal(hidden.GlobalPosition));
                CheckNe(fogLayer.GetCellSourceId(hiddenCell), -1, "没揭开的地标还盖着雾（不剧透）");
            }
        }

        // 走到塌陷山洞跟前 → 揭开（proximity_3）
        var cave = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_cave_collapse");
        CheckNotNull(cave, "地图上有塌陷山洞位点");
        if (cave != null)
        {
            CheckFalse(controller.world_map.is_revealed("n_cave_collapse"), "走近之前山洞是暗的");
            controller.player.GlobalPosition = cave.GlobalPosition + new Vector2(0, 40);
            controller._check_reveals();
            CheckTrue(controller.world_map.is_revealed("n_cave_collapse"), "走近后山洞揭开");
            CheckTrue(state.Call("is_node_revealed", "n_cave_collapse").AsBool(), "揭开状态写进存档");
            CheckLt(controller.fog_cells(), fogBefore, "揭开后雾又少了一些");
            CheckTrue(controller.map_progress_text().Contains("地图"),
                $"HUD 仍然正常：{controller.map_progress_text()}");
        }

        // 驿站：站过去按 E
        var post = controller.world.GetNodeOrNull<Node2D>("Markers/Node_n_post_station");
        controller.player.GlobalPosition = post.GlobalPosition;
        CheckTrue(controller.post_station_near(), "站到驿站旁边能认出来");
        Godot.Collections.Dictionary opened = controller.open_waypoint();
        CheckTrue(_b(opened, "ok"), $"按 E 打开驿站面板：{_s(opened, "error")}");
        CheckNotNull(controller.waypoint_panel, "驿站面板挂上了");
        if (controller.waypoint_panel != null)
        {
            Node panel = controller.waypoint_panel;
            CheckTrue(panel.Call("progress_text").AsString().Contains("地图"),
                $"面板显示探索进度：{panel.Call("progress_text").AsString()}");
            // 传送：把落点交给测试自己的回调，避免真去切场景
            var travelled = new Godot.Collections.Array();
            panel.Set("travel_handler", Callable.From<string, string>((sceneId, name) =>
                travelled.Add($"{sceneId}|{name}")));
            Godot.Collections.Dictionary result = panel.Call("press_travel", "n_qingfengyi").AsGodotDictionary();
            CheckTrue(_b(result, "ok"), $"面板能传送到清风驿：{_s(result, "error")}");
            CheckEq(travelled.Count, 1, $"传送回调被调用一次：{travelled}");
            if (travelled.Count > 0)
            {
                CheckEq(travelled[0].AsString(), "scene_qingfengyi|清风驿", "落点与名字都对");
            }

            // 难度：普通已解锁、困难要先打大寨主
            CheckTrue(panel.Call("press_difficulty", "normal").AsGodotDictionary()["ok"].AsBool(), "普通难度能切");
            Godot.Collections.Dictionary hard = panel.Call("press_difficulty", "hard").AsGodotDictionary();
            CheckFalse(hard["ok"].AsBool(), "困难还没解锁");
            CheckTrue(hard["error"].AsString().Contains("大寨主"), $"写明差哪个 Boss：{hard["error"]}");
        }

        controller.close_waypoint();
        CheckTrue(controller.waypoint_panel == null, "关掉后引用清空");

        // 事件判定：翻墙（敏 9 ≥ 9 过）、推门（力 5 < 8 失败）
        CheckEq(controller.events.Count, 8, $"大地图有 8 个事件判定位点：{controller.events.Count}");
        var wall = controller.world.GetNodeOrNull<Node2D>("Markers/Event_ev_climb_wall");
        CheckNotNull(wall, "地图上有翻墙判定位点");
        if (wall != null)
        {
            controller.player.GlobalPosition = wall.GlobalPosition;
            CheckEq(controller.event_near_player(), "ev_climb_wall", "站到寨墙旁边认得出这条判定");
            Godot.Collections.Dictionary climbed = controller.resolve_event("ev_climb_wall");
            CheckTrue(climbed["success"].AsBool(), $"敏 9 够翻墙：{climbed["text"]}");
            CheckEq(state.Call("event_check_result", "ev_climb_wall").AsString(), "done", "翻墙结果写存档");
        }

        var gate = controller.world.GetNodeOrNull<Node2D>("Markers/Event_ev_force_gate");
        CheckNotNull(gate, "地图上有推门判定位点");
        if (gate != null)
        {
            controller.player.GlobalPosition = gate.GlobalPosition;
            Godot.Collections.Dictionary pushed = controller.resolve_event("ev_force_gate");
            CheckFalse(pushed["success"].AsBool(), "力 5 推不开木闩");
            CheckTrue(pushed["text"].AsString().Contains("推不开"), $"失败文案：{pushed["text"]}");
        }
    }

    private void _CheckSpawn(TableDb db, OverworldController controller)
    {
        CheckEq(controller.enemies.Count, 17, "roaming_spawn 的 17 个明雷都生成");
        CheckNotNull(controller.player, "玩家在场");
        CheckNotNull(controller.camera, "相机在场");
        // 画布是设计 0.31.2 定死的 **2048×1536**（64×48 格）——相机边界必须跟着它
        CheckEq(controller.camera.LimitRight, 2048, "相机右边界 = 画布宽 2048");
        CheckEq(controller.camera.LimitBottom, 1536, "相机下边界 = 画布高 1536");

        RoamingEnemy patrol = _enemy(controller, "sp_lp_patrol_01");
        CheckNotNull(patrol, "巡逻明雷在场");
        if (patrol != null)
        {
            CheckEq(patrol.behavior, "patrol", "行为取自表");
            CheckNotNull(patrol.patrol_path, "巡逻明雷挂上了 Path2D");
        }

        RoamingEnemy wolf = _enemy(controller, "sp_lp_wolf_01");
        CheckEq(wolf.behavior, "wander", "野狼是游荡");
        CheckNotNull(wolf.GetNodeOrNull("Body"), "明雷有占位外观");
        RoamingEnemy sleeper = _enemy(controller, "sp_hc_sleeper");
        CheckEq(sleeper.behavior, "sleep", "醉汉是沉睡");
        RoamingEnemy lone = _enemy(controller, "sp_lp_lonewolf");
        CheckTrue(lone.is_elite, "独目狼是精英");
        CheckFloat(lone.alert_radius, 6.0, "警戒半径取自表");
        CheckNotNull(lone.GetNodeOrNull("AlertRing"), "有警戒圈的可见范围");
        // 「看得见的状态」要和配表对得上：威胁色环、精英个头、沉睡的半透明。
        var loneBody = lone.GetNodeOrNull("Body") as Polygon2D;
        CheckEq(loneBody.Color, RoamingEnemy.threat_colors()["red"].AsColor(),
            "威胁色环/身体颜色跟着队伍 threat_tag 走（独目狼是红）");
        var wolfBody = wolf.GetNodeOrNull("Body") as Polygon2D;
        CheckEq(wolfBody.Color, RoamingEnemy.threat_colors()["green"].AsColor(),
            "野狼是绿（明显弱于我方）");
        CheckFloat(loneBody.Polygon[0].X, -13.0, "精英占位块比普通明雷大（13 vs 10）");
        CheckFloat(wolfBody.Polygon[0].X, -10.0, "普通明雷块是 10");
        CheckLt(sleeper.Modulate.A, 1.0, "沉睡的明雷是半透明（可绕开的视觉信号）");
        CheckFloat(wolf.Modulate.A, 1.0, "非沉睡的明雷不透明");
        CheckNull(wolf.GetNodeOrNull("AlertRing"), "没有警戒半径的明雷不画色环");
        // 四种威胁色都要有对应配色，不然表里写个新 tag 会静默变成白色
        foreach (string tag in new[] { "green", "yellow", "red", "purple" })
        {
            CheckTrue(RoamingEnemy.threat_colors().ContainsKey(tag),
                $"威胁色 {tag} 有配色（设计 02 的绿/黄/红/紫）");
        }

        // 精英发光（设计 07）：精英挂光晕、普通明雷不挂
        CheckTrue(lone.has_elite_glow(), "精英明雷挂上了发光标识");
        Node loneGlow = lone.GetNodeOrNull("EliteGlow");
        CheckNotNull(loneGlow, "发光节点叫 EliteGlow（编辑器里能直接看到）");
        CheckEq(lone.elite_marker_id(), "marker_elite_red", "地编的精英贴图 id 在表里");
        var glowSprite = loneGlow as Sprite2D;
        CheckNotNull(glowSprite, "精英标识用的是贴图（不再拿程序化光晕顶）");
        if (glowSprite != null)
        {
            CheckEq(glowSprite.Texture.ResourcePath,
                RoamingEnemy.elite_marker_texture_path("marker_elite_red"),
                "用的就是表里点名的那张贴图（按 id 拼路径，表里换图只改一处）");
        }

        // 兜底：贴图缺失时退回程序化光晕——资产没到不该变成「精英看不出是精英」
        CheckEq(RoamingEnemy.elite_marker_texture_path(""), "", "空 id 不拼路径");
        CheckFalse(ResourceLoader.Exists(RoamingEnemy.elite_marker_texture_path("no_such_marker")),
            "不存在的贴图 id 不会凭空出现（拼错时走兜底）");
        var probe = new RoamingEnemy { is_elite = true, row = new Godot.Collections.Dictionary { { "elite_marker", "no_such_marker" } } };
        Node2D fallback = probe._make_elite_glow();
        CheckTrue(fallback is Polygon2D, "贴图缺失时退回程序化光晕（不静默失去精英标识）");
        fallback.Free();
        probe.Free();
        CheckFalse(wolf.has_elite_glow(), "普通明雷不发光");
        CheckEq(wolf.elite_marker_id(), "", "普通明雷没有精英贴图 id");
    }

    private void _CheckContact(OverworldController controller, Godot.Collections.Array captures)
    {
        // 正面贴上野狼 → front
        RoamingEnemy wolf = _enemy(controller, "sp_lp_wolf_01");
        controller.player.GlobalPosition = wolf.GlobalPosition + new Vector2(0, 8);
        wolf.reset_latch();
        wolf._check_contact();
        CheckEq(captures.Count, 1, "贴上明雷会触发遭遇");
        if (captures.Count >= 1)
        {
            var first = (Encounter)captures[0].AsGodotObject();
            CheckEq(first.contact, "front", "从正面贴上算正面遭遇");
            CheckEq(first.team_id, "team_wolf_pack", "遭遇带上队伍");
        }

        // 从上方贴近沉睡的醉汉 → 奇袭
        RoamingEnemy sleeper = _enemy(controller, "sp_hc_sleeper");
        controller.player.GlobalPosition = sleeper.GlobalPosition + new Vector2(0, -8);
        sleeper.reset_latch();
        sleeper._check_contact();
        CheckEq(captures.Count, 2, "第二次接触也会触发");
        if (captures.Count >= 2)
        {
            CheckEq(((Encounter)captures[1].AsGodotObject()).contact, "ambush_sleep", "背后贴沉睡敌人算奇袭");
        }
    }

    private void _CheckNeutral(OverworldController controller, Godot.Collections.Array captures)
    {
        RoamingEnemy herbalist = _enemy(controller, "sp_lp_herbalist");
        if (herbalist == null)
        {
            Fail("中立明雷没生成");
            return;
        }

        int before = captures.Count;
        controller.player.GlobalPosition = herbalist.GlobalPosition + new Vector2(0, 6);
        herbalist.reset_latch();
        herbalist._check_contact();
        CheckEq(captures.Count, before, "中立明雷（采药人）贴身也不开战");
    }

    /// <summary>完整闭环：遭遇 → 战斗 → 奖励落账 → 明雷被清。</summary>
    private void _CheckLoop(TableDb db, OverworldController controller, Godot.Collections.Array captures, GodotObject state)
    {
        if (captures.Count == 0)
        {
            Fail("没有可用的遭遇");
            return;
        }

        var encounter = (Encounter)captures[0].AsGodotObject();
        GodotObject sessionNode = controller.session();
        CheckNotNull(sessionNode, "GameSession 在场");
        sessionNode.Set("pending_encounter", encounter);
        int expBefore = state.Get("party_exp").AsInt32();

        var battle = GD.Load<PackedScene>(BattleScene).Instantiate();
        battle.Set("state_override", state);
        // 固定种子：这场战斗会写经验与「明雷已清」，随机种子会让后面的断言时红时绿
        battle.Set("rng_seed", 20261003);
        Tree.Root.AddChild(battle);
        battle.Call("setup");
        CheckEq(battle.Get("enemies").AsGodotArray().Count, 3, "战斗按队伍配置出 3 只野狼");
        int guard = 0;
        while (!battle.Call("finished").AsBool() && guard < 120)
        {
            GodotObject sim = battle.Get("sim").AsGodotObject();
            GodotObject actor = sim.Call("in_round").AsBool() ? sim.Call("current_actor").AsGodotObject() : null;
            var allies = battle.Get("allies").AsGodotArray();
            if (actor != null && allies.Contains(actor))
            {
                Godot.Collections.Array skills = sim.Call("available_skills", actor).AsGodotArray();
                if (skills.Count == 0)
                {
                    battle.Call("press_next_round");
                }
                else
                {
                    battle.Call("press_skill", skills[0].AsGodotObject().Get("skill_id").AsString());
                }
            }
            else
            {
                battle.Call("press_next_round");
            }

            guard += 1;
        }

        CheckTrue(battle.Call("finished").AsBool(), "战斗会结束");
        CheckTrue(battle.Call("status_text").AsString().Length > 0
            || battle.Call("result_label_text").AsString().Length > 0, "结束时有结果文案");
        CheckGt(state.Get("party_exp").AsInt32(), expBefore, "经验落进队伍池");
        // 战斗外气血（v11）：打完这一场，剩余气血要写回存档（下一场带着伤上）
        string firstActorId = battle.Get("allies").AsGodotArray()[0].AsGodotObject().Get("actor_id").AsString();
        CheckGt(state.Call("current_hp_of", firstActorId).AsInt32(), 0,
            $"打完把剩余气血写回存档（{state.Call("current_hp_of", firstActorId).AsInt32()}）");
        CheckTrue(sessionNode.Get("cleared_spawns").AsGodotDictionary().ContainsKey(encounter.spawn_id),
            "打完把明雷标成已清");
        CheckTrue(battle.Call("result_label_text").AsString().Contains("胜利"),
            $"胜利文案：{battle.Call("result_label_text").AsString()}");
        Tree.Root.RemoveChild(battle);
        battle.Free();
    }

    /// <summary>
    /// 撤退（或败北）后回到大地图：上次的接触点在明雷身边，落点必须被推开。
    /// </summary>
    private void _CheckRetreatPushOut(TableDb db, GodotObject state)
    {
        OverworldController probe = _load_overworld();
        probe.state_override = state;
        Tree.Root.AddChild(probe);
        probe.setup();
        RoamingEnemy live = null;
        foreach (Variant enemy_variant in probe.enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy != null && !enemy.defeated)
            {
                live = enemy;
                break;
            }
        }

        CheckNotNull(live, "地图上有还没清的明雷");
        if (live == null)
        {
            Tree.Root.RemoveChild(probe);
            probe.Free();
            return;
        }

        Vector2 spot = live.GlobalPosition;
        string spawnId = live.spawn_id;
        Tree.Root.RemoveChild(probe);
        probe.Free();

        // 把「上次遭遇点」摆到明雷身上，重开一张大地图
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        sessionNode?.Set("world_position", spot);
        OverworldController again = _load_overworld();
        again.state_override = state;
        Tree.Root.AddChild(again);
        again.setup();
        float nearest = 100000.0f;
        foreach (Variant enemy_variant in again.enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as RoamingEnemy;
            if (enemy != null && !enemy.defeated)
            {
                nearest = Mathf.Min(nearest, again.player.GlobalPosition.DistanceTo(enemy.GlobalPosition));
            }
        }

        CheckGt(nearest, 22.0, $"落点在明雷身上时会被推开（最近 {nearest:F1} px）");
        CheckTrue(again._status.Text.Contains("脱身"), $"状态栏说明为什么退开：{again._status.Text}");
        // 推开之后不该还存着上次那个落点，否则每次回图都从明雷身上开始
        sessionNode?.Set("world_position", Vector2.Zero);
        Tree.Root.RemoveChild(again);
        again.Free();
        CheckNe(spawnId, "", "拿到的是一只真实存在的明雷");
    }

    /// <summary>
    /// 刷新：`respawn_sec` 到期**回原位**。两条路径都要成立：① 留在图上待够时间 → 自己回来；
    /// ② 期间回过大地图 → 重建时按绝对时间戳补出来（过期就当没清过）。
    /// </summary>
    private void _CheckRespawn(TableDb db, OverworldController controller, GodotObject state)
    {
        RoamingEnemy enemy = _enemy(controller, "sp_lp_wolf_01");
        CheckNotNull(enemy, "有用来验刷新的明雷");
        if (enemy == null)
        {
            return;
        }

        Vector2 home = enemy.home;
        enemy.GlobalPosition = home + new Vector2(60, 0); // 先挪开：回来必须是**原位**
        enemy.mark_defeated(2);
        CheckTrue(enemy.defeated, "打完先进入已清状态");
        CheckFalse(enemy.Visible, "已清的明雷不可见");
        CheckTrue(enemy.IsPhysicsProcessing(), "已清但还没到点：倒计时要继续跑（关掉物理处理就永远不刷新）");
        // 正好推 2 秒（respawn_left 2.0 → 1.0 → 0.0）就停：**别多推一帧**。
        for (int i = 0; i < 2; i++)
        {
            enemy._PhysicsProcess(1.0);
        }

        CheckFalse(enemy.defeated, "留在图上待够 respawn_sec，明雷自己回来");
        CheckTrue(enemy.Visible, "回来的明雷可见");
        CheckLt(enemy.GlobalPosition.DistanceTo(home), 1.0, "回来站在原位");

        // respawn_sec=0（不刷新）的明雷不会自己回来
        enemy.mark_defeated(0);
        CheckFalse(enemy.IsPhysicsProcessing(), "respawn_sec=0 表示不刷新，连倒计时都不跑");
        for (int i = 0; i < 3; i++)
        {
            enemy._PhysicsProcess(1.0);
        }

        CheckTrue(enemy.defeated, "respawn_sec=0 表示不刷新，等多久都不回来");

        // ② 跨图重建：已清时间戳过期 → 当成没清过，明雷在原位出现
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        sessionNode.Get("cleared_spawns").AsGodotDictionary()["sp_lp_wolf_02"] =
            (long)Time.GetUnixTimeFromSystem() - 1;
        OverworldController again = _load_overworld();
        again.state_override = state;
        Tree.Root.AddChild(again);
        again.setup();
        RoamingEnemy back = _enemy(again, "sp_lp_wolf_02");
        CheckNotNull(back, "重建的大地图里有这只明雷");
        if (back != null)
        {
            CheckFalse(back.defeated, "已清时间戳过期 → 重建时它回来了");
            CheckLt(back.GlobalPosition.DistanceTo(back.home), 1.0, "重建后也在原位");
        }

        // 到期的记录要被**删掉**，别留着：留着的话两种状态同时成立，读它的人就被骗了。
        CheckFalse(sessionNode.Get("cleared_spawns").AsGodotDictionary().ContainsKey("sp_lp_wolf_02"),
            "过期的「已清」记录被清掉了（不再同时是「已清」和「在场上」）");
        Tree.Root.RemoveChild(again);
        again.Free();
    }

    /// <summary>
    /// 到期那一瞬间玩家正站在位点上：不能「凭空出现在脚下、同一帧就开战」——
    /// 得先走出接触圈才恢复判定（与地图出口「先走出去一次」同一套做法）。
    /// </summary>
    private void _CheckRespawnUnderPlayer(TableDb db, OverworldController controller)
    {
        RoamingEnemy enemy = _enemy(controller, "sp_lp_wolf_01");
        CheckNotNull(enemy, "有用来验重生站位的明雷");
        if (enemy == null)
        {
            return;
        }

        var captured = new Godot.Collections.Array();
        // C# 的信号名不转 snake_case：暴露出来叫 `Encountered`
        enemy.Encountered += (_spawnId, contact) => captured.Add(contact);
        enemy.player = controller.player;
        enemy.mark_defeated(1);
        controller.player.GlobalPosition = enemy.home; // 玩家就站在位点上等它回来
        enemy._PhysicsProcess(1.0);                    // 倒计时到 → 重生（这一帧里 return，不跑 AI）
        CheckFalse(enemy.defeated, "明雷按时间回来了");
        CheckFalse(enemy._contact_armed, "重生时先不武装接触判定");
        enemy._check_contact();
        CheckEq(captured.Count, 0, "站在重生点上不会当场开战");

        // 走出接触圈再回来：恢复正常判定
        controller.player.GlobalPosition = enemy.home + new Vector2(500, 0);
        enemy._check_contact();
        CheckTrue(enemy._contact_armed, "走出接触圈后恢复武装");
        controller.player.GlobalPosition = enemy.home;
        enemy._check_contact();
        CheckEq(captured.Count, 1, "走出去一次之后再撞上才会开战");

        // 反向风险：别让「先不武装」变成「永远不武装」
        captured.Clear();
        enemy.mark_defeated(1);
        controller.player.GlobalPosition = enemy.home + new Vector2(600, 0);
        enemy._PhysicsProcess(1.0);
        CheckFalse(enemy.defeated, "第二次按时间回来");
        enemy._check_contact();
        CheckTrue(enemy._contact_armed, "玩家离得远时，重生后立刻恢复武装");
        controller.player.GlobalPosition = enemy.home;
        enemy._check_contact();
        CheckEq(captured.Count, 1, "离得远重生后照常能开战（不是摆设）");
    }

    /// <summary>已清的明雷在新的大地图实例里不再出现（respawn_sec 之前）。</summary>
    private void _CheckCleared(TableDb db, GodotObject state)
    {
        OverworldController controller = _load_overworld();
        controller.state_override = state;
        Tree.Root.AddChild(controller);
        controller.setup();
        int clearedCount = 0;
        foreach (Variant enemy_variant in controller.enemies)
        {
            if (enemy_variant.AsGodotObject() is RoamingEnemy { defeated: true })
            {
                clearedCount += 1;
            }
        }

        CheckGt(clearedCount, 0.0, "回到大地图后已清的明雷是隐藏状态");
        Tree.Root.RemoveChild(controller);
        controller.Free();
    }

    /// <summary>
    /// 观察点在图上**看得见**（设计 15 §一「可交互物要在低饱和背景里暖色提亮」，决策 337）。
    /// </summary>
    private void _CheckFlavorHighlights(OverworldController controller)
    {
        Godot.Collections.Array points = controller.flavor_points;
        CheckGt(points.Count, 0.0, $"大地图收了观察点位点（{points.Count} 个）");
        foreach (Variant point_variant in points)
        {
            Godot.Collections.Dictionary point = point_variant.AsGodotDictionary();
            var node = point["node"].AsGodotObject() as Node2D;
            GodotObject mark = node.GetNodeOrNull(FlavorMarker.node_name());
            CheckNotNull(mark, $"大地图观察点 {point["point_id"]} 挂着可见标记");
            if (mark != null)
            {
                CheckTrue(FlavorMarker.is_visible_highlight(mark),
                    $"标记是暖色提亮且可见（{point["point_id"]}）");
                CheckEq(((Node2D)mark).Position, Vector2.Zero, "标记贴着位点（不偏）");
            }
        }
    }

    /// <summary>
    /// 本命机遇里唯一把地点写在**区域节点**上的那条（21 §九：林铁山的「镖车暗格」在落雁坡）。
    /// </summary>
    private void _CheckRegionOriginGift(TableDb db)
    {
        // ① 人在清风驿（另一个区域）时：这条**不该**被领
        GodotObject idle = _new_game(db, "normal", new[] { "ch_gang" });
        OverworldController idleMap = _load_overworld();
        idleMap.state_override = idle;
        Tree.Root.AddChild(idleMap);
        idleMap.setup();
        CheckFalse(_has_flag(idle, StoryService.done_flag("opp_gang")),
            "人在清风驿区域时领不到落雁坡那条（区域要匹配）");
        Tree.Root.RemoveChild(idleMap);
        idleMap.Free();

        // ② 力不够 18（林铁山 1 级只有 12）：走到落雁坡会**记成领过**，但不假装学会
        GodotObject weak = _new_game(db, "normal", new[] { "ch_gang" });
        OverworldController weakMap = _load_overworld();
        weakMap.state_override = weak;
        Tree.Root.AddChild(weakMap);
        weakMap.setup();
        var weakNode = weakMap.world.GetNodeOrNull<Node2D>("Markers/Node_n_luoyanpo");
        CheckNotNull(weakNode, "大地图上有落雁坡地标");
        if (weakNode != null)
        {
            weakMap.player.GlobalPosition = weakNode.GlobalPosition;
            // 每帧那条路（`_process` 里跟区域变化）——测试里直接调它，等价于「走过去」
            weakMap._track_region_for_story();
            CheckTrue(_has_flag(weak, StoryService.done_flag("opp_gang")),
                "走到落雁坡就记成领过（不会一直挂在待领名单里反复弹）");
            CheckFalse(weak.Call("is_learned", "ch_gang", "pf_chensha_buhuan").AsBool(),
                "力不够 18 时不假装学会");
            CheckTrue(weakMap._status.Text.Contains("沉沙心法·不还"),
                $"状态栏把这条播报出来（{weakMap._status.Text}）");
            // 同一区域再走一遍不该重复播报
            weakMap._track_region_for_story();
            CheckTrue(_has_flag(weak, StoryService.done_flag("opp_gang")), "同一条只领一次");
        }

        Tree.Root.RemoveChild(weakMap);
        weakMap.Free();

        // ③ 把本命可加点属性（力）加到 18：同一段路真的把 ★4 发到手
        GodotObject ready = _new_game(db, "normal", new[] { "ch_gang" });
        ready.Get("char_levels").AsGodotDictionary()["ch_gang"] = 3;
        for (int i = 0; i < 6; i++)
        {
            ready.Call("spend_point", db, "ch_gang", "str");
        }

        CheckEq(ready.Call("allocations_of", "ch_gang").AsGodotDictionary()["str"].AsInt32(), 6,
            "力加了 6 点（12 ＋ 6 ＝ 18）");
        OverworldController map = _load_overworld();
        map.state_override = ready;
        Tree.Root.AddChild(map);
        map.setup();
        var node = map.world.GetNodeOrNull<Node2D>("Markers/Node_n_luoyanpo");
        if (node != null)
        {
            map.player.GlobalPosition = node.GlobalPosition;
            map._track_region_for_story();
            CheckTrue(ready.Call("is_learned", "ch_gang", "pf_chensha_buhuan").AsBool(),
                "力够了就把《沉沙心法·不还》真发到手（这条路以前走不通）");
            CheckTrue(_has_flag(ready, StoryService.done_flag("opp_gang")), "领过就记账");
        }

        Tree.Root.RemoveChild(map);
        map.Free();
    }

    /// <summary>
    /// 走路速度的判据（设计 0.31.2）：相机改 2× 之后按「**清风驿走到驿站不短于约 4 秒**」重估。
    /// 口径：用**直线距离**当**下界**——下界都得 ≥4 秒，真实路程自然满足。
    /// </summary>
    private void _CheckWalkSpeedCriterion(TableDb db)
    {
        Resource from = db.get_row("map_region", "n_qingfengyi");
        Resource to = db.get_row("map_region", "n_post_station");
        CheckNotNull(from, "表里有清风驿这个地标");
        CheckNotNull(to, "表里有驿站这个地标");
        if (from == null || to == null)
        {
            return;
        }

        float distance = new Vector2(
            (float)from.Get("pos_x").AsDouble(), (float)from.Get("pos_y").AsDouble())
            .DistanceTo(new Vector2((float)to.Get("pos_x").AsDouble(), (float)to.Get("pos_y").AsDouble()));
        float speed = PlayerController.walk_speed();
        CheckGt(speed, 0.0, $"WALK_SPEED 是正数（{speed:F1}）");
        float seconds = distance / speed;
        CheckTrue(seconds >= 4.0,
            $"清风驿→驿站直线 {distance:F1}px ÷ WALK_SPEED {speed:F1} = **{seconds:F2} 秒**，"
            + "短于设计判据的 4 秒——要么把 WALK_SPEED 压下来、要么确认地标坐标是不是又被放近了（设计 0.31.2）");
    }

    // ------------------------------------------------------------------ 揭雾与驿站（服务层）

    /// <summary>
    /// `tests/test_world_map.gd` 的七节服务层口径（2026-10-11 P4 第三十三批并进来）。
    /// 上面 `_CheckWorldMap` 走的是**真场景**（雾层、位点、驿站面板），这里补的是**服务层**：
    /// 开局点亮哪些、走近多少格才揭、链式揭开、0.32.0 的两条表侧规则（含改坏了构建期要红）、
    /// 传送目的地的取舍、难度解锁条件与切难度、以及揭开状态与难度进存档（含 v7 迁移）。
    ///
    /// 自己开一份全新存档：真场景那一节会把塌陷山洞揭开、还会切难度，
    /// 混用同一份 state 会让「开局几张图亮着」这种断言看起来时红时绿。
    /// </summary>
    private void _CheckWorldMapService(TableDb db)
    {
        GodotObject state = SoloState(db);
        WorldMapService service = WorldMapService.create(db, state);
        _CheckInitialReveals(db, state, service);
        _CheckProximityReveals(db, state);
        _CheckDiscoverChain(db);
        _CheckRegionRules(db);
        _CheckTravel(db, service);
        _CheckDifficulty(db, state, service);
        _CheckWorldMapSave(db, state);
    }

    /// <summary>
    /// `reveal_on_map=1` 的地标一开始就点亮（设计 0.32.0：**只有城镇与驿站**）。
    ///
    /// 落雁坡曾是第三个默认公开的地点（野外），0.32.0 按「除城镇外都要自己走出来」改成 `proximity_5`。
    /// 所以这里连同 `_CheckProximityReveals` 一起走一遍真实顺序：开局两处 → 走到落雁坡才亮 → 黑风寨靠它链式揭开。
    /// </summary>
    private void _CheckInitialReveals(TableDb db, GodotObject state, WorldMapService service)
    {
        string[] revealed = service.apply_initial_reveals();
        CheckEq(revealed.Length, 2, $"初始点亮 2 个地标（城镇＋驿站）：{_Join(revealed)}");
        CheckTrue(service.is_revealed("n_qingfengyi"), "清风驿一开始就看得见");
        CheckTrue(service.is_revealed("n_post_station"), "驿站一开始就看得见");
        CheckFalse(service.is_revealed("n_luoyanpo"), "落雁坡是野外，开局不亮（0.32.0）");
        CheckFalse(service.is_revealed("n_heifengzhai"), "黑风寨要先探索");
        CheckEq(service.revealed_count(), 2, "已探索 2 个");
        // 分母是**表里的行数**（不是"有图标的行数"：0.32.0 起三个兴趣点不带图标，
        // 拿图标数当分母会算成 5）
        int total = db.rows("map_region").Count;
        CheckTrue(
            service.progress_text().Contains($"地图 2/{total}"),
            $"HUD 文案：{service.progress_text()}");
        CheckTrue(service.progress_text().Contains("落雁坡"), "未探索清单里有落雁坡");
        CheckTrue(service.progress_text().Contains("黑风寨"), "未探索清单里有黑风寨");
        // 重复调用不会再报新揭开
        CheckTrue(service.apply_initial_reveals().Length == 0, "初始揭开是幂等的");
        // 持有类（`item_<id>`，设计 0.26 §六）：没拿到藏宝图，石隙迷窟不该出现
        CheckFalse(service.is_revealed("n_shixi"), "没有藏宝图时石隙迷窟不在地图上");
        Inventory inventory = _InventoryOf(state);
        Godot.Collections.Dictionary added = inventory.add_item(db, "item_treasure_map", 1);
        CheckTrue(_b(added, "ok"), $"藏宝图能放进背包：{_s(added, "error")}");
        CheckTrue(inventory.has("item_treasure_map", 1), "背包里确实有藏宝图（下一句才验揭开）");
        string[] held = service.apply_held_reveals();
        CheckTrue(
            System.Array.IndexOf(held, "n_shixi") >= 0,
            $"拿到藏宝图后石隙迷窟当场揭开：{_Join(held)}");
        CheckTrue(service.is_revealed("n_shixi"), "石隙迷窟出现在大地图上");
    }

    /// <summary>proximity_N：走到 N 格内自动揭开（塌陷山洞 3 格、荒村 5 格）。</summary>
    private void _CheckProximityReveals(TableDb db, GodotObject state)
    {
        WorldMapService service = WorldMapService.create(db, state);
        var markers = new Godot.Collections.Dictionary
        {
            { "n_cave_collapse", new Vector2(1000, 1000) },
            { "n_huangcun", new Vector2(2000, 2000) },
            { "n_luoyanpo", Vector2.Zero },
        };
        // 离山洞 4 格（128px）→ 还差一点
        service.apply_proximity_reveals(new Vector2(1000 + 4 * Tile, 1000), markers);
        CheckFalse(service.is_revealed("n_cave_collapse"), "4 格外揭不开（门槛 3 格）");
        // 走到 2 格内 → 揭开
        string[] near = service.apply_proximity_reveals(new Vector2(1000 + 2 * Tile, 1000), markers);
        CheckTrue(
            System.Array.IndexOf(near, "n_cave_collapse") >= 0,
            $"2 格内揭开塌陷山洞：{_Join(near)}");
        // 荒村要 5 格
        CheckFalse(service.is_revealed("n_huangcun"), "荒村还没揭开");
        service.apply_proximity_reveals(new Vector2(2000 + 4 * Tile, 2000), markers);
        CheckTrue(service.is_revealed("n_huangcun"), "4 格内揭开荒村（门槛 5 格）");

        // 正好卡在门槛上：口径是 `<= 半径 × 32px`——**3 格整要揭、3 格多 1 像素不揭**。
        // 这是典型的 off-by-one：改成 `<` 就会「站在门槛上看不见」，而平时很难察觉（差一点点而已）。
        WorldMapService edge_far = WorldMapService.create(db, SoloState(db));
        edge_far.apply_proximity_reveals(new Vector2(1000 + 3 * Tile + 1, 1000), markers);
        CheckFalse(edge_far.is_revealed("n_cave_collapse"), "多 1 像素不揭（门槛不含外沿）");
        WorldMapService edge_on = WorldMapService.create(db, SoloState(db));
        edge_on.apply_proximity_reveals(new Vector2(1000 + 3 * Tile, 1000), markers);
        CheckTrue(edge_on.is_revealed("n_cave_collapse"), "正好 3 格揭开（<= 口径）");
        WorldMapService edge_five = WorldMapService.create(db, SoloState(db));
        edge_five.apply_proximity_reveals(new Vector2(2000 + 5 * Tile, 2000), markers);
        CheckTrue(edge_five.is_revealed("n_huangcun"), "正好 5 格也揭开（荒村门槛）");

        // 主路第三站：**落雁坡也要自己走到跟前**（0.32.0 由默认公开改成 `proximity_5`）。
        // 它一揭开，链式规则就把黑风寨带出来（`discover_luoyanpo`）——后面 `_CheckTravel`
        // 正是按「黑风寨已揭开」验的，所以这一步同时是那一条的前提。
        CheckFalse(service.is_revealed("n_luoyanpo"), "在别处转了一圈，落雁坡还没亮（野外≠城镇）");
        string[] road = service.apply_proximity_reveals(new Vector2(2 * Tile, 0), markers);
        CheckTrue(
            System.Array.IndexOf(road, "n_luoyanpo") >= 0,
            $"走到落雁坡 2 格内才揭开：{_Join(road)}");
        CheckTrue(
            System.Array.IndexOf(road, "n_heifengzhai") >= 0,
            $"落雁坡一揭开，黑风寨跟着链式揭开：{_Join(road)}");
    }

    /// <summary>
    /// discover_X：X 揭开之后，依赖它的地标跟着揭开（黑风寨要 discover_luoyanpo）。
    /// 用全新存档跑，免得前面几步已经把黑风寨揭开、看不出「链式」这一步做了什么。
    /// </summary>
    private void _CheckDiscoverChain(TableDb db)
    {
        WorldMapService service = WorldMapService.create(db, SoloState(db));
        service.apply_initial_reveals();
        CheckFalse(service.is_revealed("n_luoyanpo"), "链式揭开前落雁坡也还没亮（它是野外，要走近）");
        CheckFalse(service.is_revealed("n_heifengzhai"), "链式揭开前黑风寨还是暗的");
        // 没走到落雁坡跟前：黑风寨不许自己冒出来
        service.apply_proximity_reveals(new Vector2(10000, 10000), new Godot.Collections.Dictionary());
        CheckFalse(service.is_revealed("n_heifengzhai"), "落雁坡没亮时黑风寨不会自己冒出来");
        string[] revealed = service.apply_proximity_reveals(
            new Vector2(2 * Tile, 0),
            new Godot.Collections.Dictionary { { "n_luoyanpo", Vector2.Zero } });
        CheckTrue(
            System.Array.IndexOf(revealed, "n_luoyanpo") >= 0,
            $"走到跟前落雁坡揭开：{_Join(revealed)}");
        CheckTrue(
            System.Array.IndexOf(revealed, "n_heifengzhai") >= 0,
            $"落雁坡已揭开 → 黑风寨跟着揭开：{_Join(revealed)}");
        CheckTrue(service.is_revealed("n_heifengzhai"), "黑风寨点亮");
    }

    /// <summary>
    /// 0.32.0 的两条表侧规则（构建期 `TableValidator._check_map_region_rules` 的同源验证）：
    /// ① `reveal_on_map=1` 只许城镇／驿站；② 兴趣点不给图标（例外：持有类解锁的那一个）。
    ///
    /// 两半都要：**发行数据长这样**（逐行点名，不写"数量对就行"——数量对、换了行也能过）
    /// ＋**改坏了构建期真的会红**（复制表库、只改内存副本）。
    /// </summary>
    private void _CheckRegionRules(TableDb db)
    {
        var public_ids = new System.Collections.Generic.List<string>();
        var poi_icons = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("map_region"))
        {
            var row = (MapRegionRow)entry.As<TableRow>();
            if (row.reveal_on_map)
            {
                public_ids.Add(row.node_id);
            }
            if (row.node_type == "poi" && row.icon.Trim().Length > 0)
            {
                poi_icons.Add($"{row.node_id}→{row.icon}");
            }
        }
        CheckEq(
            string.Join("、", public_ids),
            "n_qingfengyi、n_post_station",
            $"开局公开的正好是清风驿与驿站（实际：{string.Join("、", public_ids)}）");
        CheckEq(
            string.Join("、", poi_icons),
            "n_shixi→icon_shixi",
            $"兴趣点里只有石隙迷窟带图标（藏宝图上有位置）；实际：{string.Join("、", poi_icons)}");

        TableDb broken = _MapRegionTableWith(db, "n_luoyanpo", "reveal_on_map", true);
        bool named = false;
        foreach (string message in TableValidator.validate(broken))
        {
            if (message.Contains("n_luoyanpo") && message.Contains("reveal_on_map"))
            {
                named = true;
            }
        }
        CheckTrue(named, "把落雁坡改回「开局公开」→ 构建期点名 n_luoyanpo");

        TableDb broken_icon = _MapRegionTableWith(db, "n_cave_collapse", "icon", "icon_cave");
        bool icon_named = false;
        foreach (string message in TableValidator.validate(broken_icon))
        {
            if (message.Contains("n_cave_collapse") && message.Contains("icon"))
            {
                icon_named = true;
            }
        }
        CheckTrue(icon_named, "给兴趣点配图标 → 构建期点名 n_cave_collapse");
    }

    /// <summary>驿站传送：只列已探索且有可进入小地图的节点；没探索的给明确理由。</summary>
    private void _CheckTravel(TableDb db, WorldMapService service)
    {
        Godot.Collections.Array targets = service.travel_targets("");
        var ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in targets)
        {
            ids.Add(entry.AsGodotDictionary()["node_id"].AsString());
        }
        CheckTrue(ids.Contains("n_qingfengyi"), $"能传送到清风驿：{string.Join("、", ids)}");
        CheckTrue(ids.Contains("n_heifengzhai"), "黑风寨揭开后也能传送");
        CheckFalse(ids.Contains("n_luoyanpo"), "落雁坡是野外区域（没有 enter_scene），不作为目的地");
        CheckFalse(ids.Contains("n_ferry_abandoned"), "本章不开放的渡口不列出来");

        Godot.Collections.Dictionary current = service.travel("n_qingfengyi", "scene_qingfengyi");
        CheckTrue(_b(current, "ok"), "传送到当前所在地也是合法的");
        CheckEq(_s(current, "scene_id"), "scene_qingfengyi", "落点是清风驿的场景 id");

        Godot.Collections.Dictionary unknown = service.travel("n_nowhere", "");
        CheckFalse(_b(unknown, "ok"), "没有的节点传不了");
        CheckTrue(
            _s(unknown, "error").Contains("没有这个地标"),
            $"理由写明没有这个地标：{_s(unknown, "error")}");

        // 没探索的地方不能传送
        WorldMapService bare = WorldMapService.create(db, SoloState(db));
        Godot.Collections.Dictionary blocked = bare.travel("n_huangcun", "");
        CheckFalse(_b(blocked, "ok"), "没探索过的地方不能传送");
        CheckTrue(_s(blocked, "error").Contains("还没探索"), $"提示先走过去：{_s(blocked, "error")}");
    }

    /// <summary>难度切换：普通随便切；困难要击败大寨主；绝境还要加上醉刀客。</summary>
    private void _CheckDifficulty(TableDb db, GodotObject state, WorldMapService service)
    {
        CheckTrue(_b(service.can_switch_difficulty("normal"), "ok"), "普通默认解锁");
        Godot.Collections.Dictionary hard = service.can_switch_difficulty("hard");
        CheckFalse(_b(hard, "ok"), "困难要通关第一章");
        CheckTrue(_s(hard, "error").Contains("大寨主"), $"说明差大寨主：{_s(hard, "error")}");
        Godot.Collections.Dictionary nightmare = service.can_switch_difficulty("nightmare");
        CheckFalse(_b(nightmare, "ok"), "绝境也还没解锁");

        state.Call("record_dungeon", "scene_heifengzhai", "bosses", "en_bd_boss");
        CheckTrue(_b(service.can_switch_difficulty("hard"), "ok"), "击败大寨主后困难解锁");
        CheckFalse(_b(service.can_switch_difficulty("nightmare"), "ok"), "绝境还要醉刀客");
        state.Call("record_dungeon", "scene_heifengzhai", "bosses", "en_hidden_drunk");
        CheckTrue(_b(service.can_switch_difficulty("nightmare"), "ok"), "两个 Boss 都打完绝境解锁");

        Godot.Collections.Dictionary switched = service.switch_difficulty("nightmare");
        CheckTrue(_b(switched, "ok"), $"切难度成功：{_s(switched, "error")}");
        CheckEq(state.Get("difficulty_id").AsString(), "nightmare", "存档里的难度跟着改");
        CheckEq(_s(switched, "previous"), "normal", "记下切换前的难度");
        Godot.Collections.Array rows = service.difficulty_rows();
        CheckEq(rows.Count, 3, "三个难度都列出来");
        int current_rows = 0;
        foreach (Variant entry in rows)
        {
            if (entry.AsGodotDictionary()["current"].AsBool())
            {
                current_rows++;
            }
        }
        CheckEq(current_rows, 1, "只有一个难度标着当前");
    }

    /// <summary>揭开状态与难度写存档（含 v7 老档：没有 `revealed_nodes`，版本升到 8）。</summary>
    private void _CheckWorldMapSave(TableDb db, GodotObject state)
    {
        GameState back = GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), db);
        CheckNotNull(back, "带揭开状态的存档能读回来");
        if (back == null)
        {
            return;
        }
        CheckEq(back.migrated_from, 0, "同版本往返不需要迁移");
        CheckTrue(back.is_node_revealed("n_heifengzhai"), "揭开的节点往返一致");
        CheckEq(back.difficulty_id, "nightmare", "难度往返一致");

        Godot.Collections.Dictionary legacy = state.Call("to_dict").AsGodotDictionary().Duplicate(true);
        legacy["version"] = 7;
        legacy.Remove("revealed_nodes");
        GameState migrated = GameState.from_dict(legacy, db);
        CheckEq(migrated.migrated_from, 7, "记下从 v7 迁移");
        CheckEq(migrated.version, GameState.save_version(), "版本升到当前");
        CheckEq(migrated.revealed_nodes.Count, 0, "老档没有揭开记录");
    }

    /// <summary>复制一份表库、把 `map_region` 某一行某一列改掉（只改内存副本，不碰策划的 CSV）。</summary>
    private static TableDb _MapRegionTableWith(TableDb db, string node_id, string column, Variant value)
    {
        TableDb custom = new TableDb();
        custom.tables = db.tables.Duplicate();
        var table = (TableResource)db.tables["map_region"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (MapRegionRow)entry.As<TableRow>().Duplicate(true);
            if (row.node_id == node_id)
            {
                row.Set(column, value);
            }
            rows.Add(row);
        }
        table.rows = rows;
        custom.tables["map_region"] = table;
        return custom;
    }

    private static Inventory _InventoryOf(GodotObject state) =>
        state.Get("inventory").As<Inventory>();

    private static string _Join(string[] values) => string.Join("、", values);
}
