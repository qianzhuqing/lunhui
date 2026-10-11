using Godot;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 小地图与副本（C# 版，对位原 `tests/test_local_map.gd`）：
/// 房间敌人、宝箱、隐藏触发、出口与回大地图的刷新。
///
/// 这一份用例**真的把 `local_run.tscn` 装起来跑**（不是纯逻辑）——控制器与音效层（`Sfx.cs`）
/// 都是 C#，所以这里直接调它们的公开口；GDScript 侧的东西（`GameState`／`GameSession`／面板）
/// 一律走 `Call`／`Get`／`Set`。
/// </summary>
public partial class TestLocalMap : TestCase
{
    private const string LocalRun = "res://scenes/local_run.tscn";
    private const string SaveTestDir = "res://.logs/test_save_timing/run_map";

    public override string SuiteName() => "小地图与副本";

    // ---- GDScript 侧对象的取值口（迁移期它们还是 .gd，收紧类型要等那一批） ----
    private static GodotObject _gd(string path) => GD.Load<GDScript>(path);
    private static bool _has_flag(GodotObject state, string flag) => state.Call("has_flag", flag).AsBool();
    private static void _set_flag(GodotObject state, string flag) => state.Call("set_flag", flag, true);
    private static Godot.Collections.Dictionary _flags(GodotObject state) => state.Get("flags").AsGodotDictionary();
    private static Inventory _inv(GodotObject state) => state.Get("inventory").As<Inventory>();
    private static Godot.Collections.Dictionary _record(GodotObject state, string sceneId) =>
        state.Call("dungeon_record", sceneId).AsGodotDictionary();
    private static string _status(LocalMapController map) => map._status.Text;
    private static Variant _v(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key] : default;
    private static string _s(Godot.Collections.Dictionary dict, string key) => _v(dict, key).AsString();
    private static bool _b(Godot.Collections.Dictionary dict, string key) => _v(dict, key).AsBool();
    private static Godot.Collections.Array _a(Godot.Collections.Dictionary dict, string key) =>
        _v(dict, key).AsGodotArray();
    private static string _join_cn(Variant value)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant item in value.AsGodotArray())
        {
            parts.Add(item.AsString());
        }

        return string.Join("、", parts);
    }

    /// <summary>宝箱／触发点的占位多边形颜色（有贴图时 Body 是 `Sprite2D`，那条路不看颜色）。</summary>
    private static Color _body_color(Node node) => node switch
    {
        Polygon2D poly => poly.Color,
        Sprite2D sprite => sprite.Modulate,
        _ => new Color(),
    };

    private static LocalMapController _load_local_run()
    {
        var packed = GD.Load<PackedScene>(LocalRun);
        return (LocalMapController)packed.Instantiate();
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
        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        CheckNotNull(sessionNode, "GameSession 在场");
        sessionNode.Call("set_state", state);
        sessionNode.Set("pending_local_scene", "scene_heifengzhai");

        LocalMapController map = _load_local_run();
        map.state_override = state;
        // 自动存档要真的落盘才谈得上「什么时机存的」：开头就给槽位 + 临时目录。
        // （以前槽位一直是 0，前面那些自动存档全在空转，只有 `_CheckSaveTiming` 里那几次真写了。）
        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        state.Set("slot", 2);
        map.save_store_override = store;
        var captures = new Godot.Collections.Array();
        map.battle_switch_handler = Callable.From<Variant>(encounter => captures.Add(encounter));
        var returns = new Godot.Collections.Array();
        map.return_handler = Callable.From(() => returns.Add(true));
        Tree.Root.AddChild(map);
        map.setup();

        _CheckSpawn(map);
        _CheckFloorHud(map, db);
        _CheckRoomEncounter(map, captures);
        _CheckChest(db, map, state);
        _CheckChestPity(map, state);
        _CheckChestOverflowCopy(map);
        _CheckTriggerMoneyReward(db, map, state);
        _CheckTriggers(map, state, captures);
        _CheckSequencePuzzle(map, state);
        _CheckEventBossReward(db, map, state, captures);
        _CheckKillStyleNames(map, db);
        _CheckFlavorPoints(map, state);
        _CheckPlayerText(map, db, sessionNode);
        _CheckConditionTriggers(map, state, sessionNode);
        _CheckProgressRecords(map, state);
        _CheckDungeonPanel(map, state);
        _CheckEvents(map, state);
        _CheckStealthTrigger(map, state, sessionNode);
        _CheckOnceOnlyTriggers(state, sessionNode);
        _CheckSaveTiming(map, state, sessionNode);
        _CheckExit(map, sessionNode, returns);
        _CheckTownShop(db, state, sessionNode);
        _CheckOverlaysOnCanvasLayer(db, sessionNode);
        _CheckSparButton(db, sessionNode);
        _CheckGuideAfterShopClose(db, sessionNode);
        _CheckTownFacilities(db, state, sessionNode);
        _CheckEveryFacilityIsInteractable(db, sessionNode);
        _CheckTownRecruitAtInn(db, sessionNode);
        _CheckGuardedChest(db, sessionNode);
        _CheckDummyPracticeEntry(db, sessionNode);
        _CheckTownShelterAndFieldHud(db, sessionNode);
        _CheckTownGamble(db, state, sessionNode);
        _CheckSpawnAndExitGuard(db, state, sessionNode);
        _CheckCaveShortcut(db, state, sessionNode);
        _CheckBattleReturnPosition(db, state, sessionNode);
        _CheckChoiceDialogueAfterBoss(db, map, state, sessionNode);
        _CheckEventDialogue(db, map, state);
        _CheckShixiLayers(db, state, sessionNode);
        Tree.Root.RemoveChild(map);
        map.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 浮层必须挂在**画布层**上（2026-10-04 实测的「面板打不开」根因）。
    ///
    /// `npc_panel.tscn`／`character_screen.tscn` 的根是 `Control`，直接 `add_child` 到 `Node2D`
    /// 场景根下会**跟着相机平移**——面板明明开了，玩家在屏幕上看不到（走远了就整块出了屏幕），
    /// 而自检是「直接调 API、不看屏幕」，所以照样全绿。修法是 `OverlayStack.mount()` 统一收口。
    /// 这条不看坐标、只**抓根**：面板的父节点必须是 `CanvasLayer`。
    /// </summary>
    private void _CheckOverlaysOnCanvasLayer(TableDb db, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = SoloState(db);
        Tree.Root.AddChild(town);
        town.setup();
        Godot.Collections.Dictionary npcOpened = town.open_npc("npc_qian_dafu");
        CheckTrue(_b(npcOpened, "ok"), $"NPC 面板开得起来：{_s(npcOpened, "error")}");
        if (town.npc_panel != null)
        {
            CheckTrue(
                town.npc_panel.GetParent() is CanvasLayer,
                "NPC 面板挂在画布层下（挂世界节点下会跟着相机跑出屏幕）");
        }

        Godot.Collections.Dictionary charOpened = town.open_character_overlay(0);
        CheckTrue(_b(charOpened, "ok"), $"角色／行囊面板开得起来：{_s(charOpened, "error")}");
        Node charPanel = town.FindChild("CharacterPanel", true, false);
        CheckNotNull(charPanel, "角色面板节点在");
        if (charPanel != null)
        {
            CheckTrue(charPanel.GetParent() is CanvasLayer, "角色／行囊面板也挂在画布层下");
        }

        town.close_npc();
        Godot.Collections.Dictionary shopOpened = town.open_shop("bld_grocery");
        CheckTrue(_b(shopOpened, "ok"), "商店面板开得起来");
        CheckTrue(
            town.shop_panel is CanvasLayer,
            "商店面板本来就是 CanvasLayer 根（这条防它哪天被改成 Control）");
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 「切磋」按钮的接线（2026-10-04 玩家报「NPC 面板缺少切磋」）。
    ///
    /// 这个按钮是**两个数据凑齐**才出现的：`npc_favor.spar_favor`（赢了加多少好感）＋
    /// `npc_def.spar_team_id`（打哪支队伍）。而 `spar_team_id` 那列**九个人全是空的** →
    /// `_add_spar_section()` 直接 return、按钮永远不渲染（面板自检也从不走切磋，两边都绿）。
    /// 这条用例把**机制**钉住：给一个人临时配上队伍 → 按钮出现、文案带好感数、按下去把
    /// `(npc_id, team_id)` 交给场景控制器。**数据那半边**（谁该配哪支队伍）由
    /// `validate_tables` 的「配了 spar_favor 却没配 spar_team_id」告警盯着，不在这里判。
    /// </summary>
    private void _CheckSparButton(TableDb db, Node sessionNode)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["npc_def"].As<TableResource>().Duplicate(true);
        NpcDefRow row = null;
        foreach (Variant entry in table.rows)
        {
            if (entry.AsGodotObject() is NpcDefRow candidate && candidate.npc_id == "npc_wang_tie")
            {
                row = candidate;
            }
        }

        CheckNotNull(row, "副本里有铁匠那一行");
        if (row == null)
        {
            return;
        }

        // 借一支**已存在**的队伍当切磋对手（真比赛时换哪支由数据决定，这里只验接线）
        row.spar_team_id = "team_wanderer_disciple";
        custom.tables["npc_def"] = table;

        var panel = (Node)GD.Load<PackedScene>("res://scenes/npc_panel.tscn").Instantiate();
        panel.Set("db_override", custom);
        panel.Set("state_override", SoloState(custom));
        panel.Set("npc_id", "npc_wang_tie");
        var calls = new Godot.Collections.Array();
        panel.Set("spar_handler", Callable.From<string, string>((who, team) =>
            calls.Add(new Godot.Collections.Array { who, team })));
        Tree.Root.AddChild(panel);
        panel.Call("setup");
        panel.Set("mode", "interact");
        panel.Call("refresh");
        Node button = panel.FindChild("SparButton", true, false);
        CheckNotNull(button, "配了 spar_team_id 的人，面板上要出现「切磋」按钮");
        if (button != null)
        {
            string text = button.Get("text").AsString();
            CheckTrue(text.Contains("8"), $"按钮文案带出 spar_favor（王铁 8）：{text}");
            Godot.Collections.Dictionary result = panel.Call("spar").AsGodotDictionary();
            CheckTrue(_b(result, "ok"), $"切磋开得起来：{_s(result, "error")}");
            CheckEq(calls.Count, 1, "按下去把这一场交给场景控制器");
            if (calls.Count == 1)
            {
                var pair = calls[0].AsGodotArray();
                CheckEq(pair[1].AsString(), "team_wanderer_disciple", "交出去的是表里那支队伍");
                CheckEq(pair[0].AsString(), "npc_wang_tie", "以及是哪个人");
            }
        }

        Tree.Root.RemoveChild(panel);
        panel.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 0.32.0 的两条地表分层，在**真控制器**里各走一遍（石隙迷窟是唯一同时用到两层的图）：
    ///   ① `Conditional` 层（那条 22 格碎石细径）跟着 `item_treasure_map` **整层**显隐；
    ///   ② 玩家走到遮挡瓦片（岩檐）下面时 `Overlay` 整层半透明，走开恢复。
    ///
    /// 条件**不写死在代码里**：控制器问的是「这张图的母地标是不是持有类解锁」（`n_shixi` → 藏宝图）。
    /// 地编还没交付 `Conditional` 层时，这里临时挂一个空层验接线。
    /// </summary>
    private void _CheckShixiLayers(TableDb db, GodotObject _state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_shixi");
        LocalMapController shixi = _load_local_run();
        // 用**独立的一份存档**：藏宝图是钥匙道具（`Inventory.remove_item` 拒删），
        // 拿主存档验「没有图」那一侧会把前提带歪（参数里那份 state 这一段不用）。
        GodotObject shixiState = SoloState(db);
        shixi.state_override = shixiState;
        Tree.Root.AddChild(shixi);
        shixi.setup();
        CheckEq(shixi.scene_id, "scene_shixi", "进入石隙迷窟");

        // ① 条件地表的判据：按表问出来（`map_region.n_shixi.unlock_condition`）
        Godot.Collections.Dictionary rule = shixi.conditional_layer_rule();
        CheckTrue(_b(rule, "apply"), $"石隙的条件地表有条件可依：{rule}");
        CheckEq(_join_cn(_v(rule, "items")), "item_treasure_map", "条件是藏宝图（写在 map_region 里，不在代码里）");
        CheckFalse(shixi.conditional_layer_visible(), "没图 → 细径不该显示");

        TileMapLayer layer = shixi.world.GetNodeOrNull<TileMapLayer>("Conditional");
        if (layer == null)
        {
            // 地编还没铺那 22 格：临时挂一个空层，验「代码真的会去显隐这一层」
            layer = new TileMapLayer { Name = "Conditional" };
            shixi.world.AddChild(layer);
            shixi._conditional_layer = layer;
            GD.Print("  [小地图] 石隙还没有 Conditional 层（等地编铺 22 格细径），用临时空层验显隐接线");
        }

        shixi._apply_conditional_layer();
        CheckFalse(layer.Visible, "没图 → 这一层被藏起来");
        Godot.Collections.Dictionary added = _inv(shixiState).add_item(db, "item_treasure_map", 1);
        CheckTrue(_b(added, "ok"), $"藏宝图能放进背包：{_s(added, "error")}");
        CheckTrue(shixi.conditional_layer_visible(), "拿到图 → 细径该显示");
        shixi._apply_conditional_layer(); // 正常路径是 `_process` 里每帧刷（拿到图当场就出现）
        CheckTrue(layer.Visible, "拿到图 → 这一层真的显示出来了");
        // 再回到隐藏：藏宝图是**钥匙道具**，`Inventory.remove_item` 故意拒删（钥匙拒丢），
        // 所以换一份干净存档问同一层——它和背包是「有图才显示」的两侧。
        shixi.state_override = SoloState(db);
        CheckFalse(shixi.conditional_layer_visible(), "换一份没有图的存档 → 细径不该显示");
        shixi._apply_conditional_layer();
        CheckFalse(layer.Visible, "图没了 → 这一层又藏回去");

        // ② 遮挡层半透明：按 TileSet 的 `covering` 找一格岩檐站上去
        TileMapLayer overlay = shixi.world.GetNodeOrNull<TileMapLayer>("Overlay");
        CheckNotNull(overlay, "石隙有 Overlay 层");
        if (overlay != null)
        {
            var eaveCell = new Vector2I(-1, -1);
            var plainCell = new Vector2I(-1, -1);
            foreach (Vector2I cell in overlay.GetUsedCells())
            {
                TileData data = overlay.GetCellTileData(cell);
                if (data != null && data.GetCustomData("covering").AsBool())
                {
                    if (eaveCell.X < 0)
                    {
                        eaveCell = cell;
                    }
                }
                else if (plainCell.X < 0)
                {
                    plainCell = cell;
                }
            }

            CheckTrue(eaveCell.X >= 0, "石隙的 Overlay 上有遮挡瓦片（岩檐压顶）");
            if (eaveCell.X >= 0)
            {
                shixi.player.GlobalPosition = overlay.ToGlobal(overlay.MapToLocal(eaveCell));
                shixi._refresh_overlay_cover();
                CheckTrue(shixi._overlay_covering, "站在岩檐下面判为「被盖住」");
                CheckFloat(
                    overlay.Modulate.A, LocalMapController.OVERLAY_COVER_ALPHA,
                    $"被盖住时整层压到 {LocalMapController.OVERLAY_COVER_ALPHA:F2}");
                shixi.player.GlobalPosition = overlay.ToGlobal(overlay.MapToLocal(plainCell));
                shixi._refresh_overlay_cover();
                CheckFalse(shixi._overlay_covering, "走开就不算被盖住");
                CheckFloat(overlay.Modulate.A, 1.0, "走开后恢复不透明");
            }
        }

        Tree.Root.RemoveChild(shixi);
        shixi.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 判定过了接一段对话（设计 20 §四 幕四）：地牢那次判定（`ev_shen_rescue`）通过之后，
    /// 铁栏后的人才回头说话——以前这里只置一个旗标，她一句台词都没有。
    /// 走的是**真路径**（`resolve_event`，按 E 那一下调的就是它），不是直接调内部函数。
    /// </summary>
    private void _CheckEventDialogue(TableDb db, LocalMapController map, GodotObject state)
    {
        Node2D cell = map.world.GetNodeOrNull<Node2D>("Markers/hf1_cell/Event_ev_shen_rescue");
        CheckNotNull(cell, "地牢里有沈雁回的判定位点");
        if (cell == null)
        {
            return;
        }

        map.close_npc();
        map.player.GlobalPosition = cell.GlobalPosition;
        // 书生的文学判定值是 5 ≥ 门槛 2（soft 达标必过）→ 这一条是确定性的
        Godot.Collections.Dictionary result = map.resolve_event("ev_shen_rescue");
        CheckTrue(_b(result, "success"), $"地牢那次判定过得去：{_s(result, "text")}");
        CheckTrue(_has_flag(state, "flag_shen_rescued"), "判定过了仍然置旗标（章节结束等它）");
        Node panel = map.npc_panel;
        CheckNotNull(panel, "判定过了接上她那段对话");
        if (panel != null)
        {
            CheckEq(panel.Get("npc_id").AsString(), "npc_shen_yanhui", "说话人是沈雁回");
            CheckEq(panel.Get("dialogue_node_id").AsString(), "dl_shen_cell", "直接接上幕四那一句");
            CheckNotNull(
                _actions(panel).FindChild("TalkOptionopt_shen_take", true, false),
                "「我带你走。」是一条真按钮");
            map.close_npc();
        }
    }

    /// <summary>
    /// 终局难题（设计 20 §七）：上一场打赢大寨主 → 回到图上就把那段对话摆出来。
    ///
    /// 三层一起验：`battle_screen` 把这一场的 `team_id` 写进 `last_battle`；小地图按队伍翻
    /// `TEAM_WIN_DIALOGUES`；**能不能问、问过没有写在表的条件里**（三个 `!flag_ledger_*`）。
    /// 这条链以前整条不存在——三个账册旗标全项目没有来源，`story_node` 那三行永久增益谁也拿不到。
    ///
    /// 用的是**已经在场的那张地图**（不另起一份场景）：只在它上面临时改会话状态，
    /// 跑完把自己动过的旗标与 `last_battle` 还原，免得后面的用例看见一份被污染的状态。
    /// </summary>
    private void _CheckChoiceDialogueAfterBoss(
        TableDb db, LocalMapController map, GodotObject state, Node sessionNode)
    {
        bool hadConfront = _has_flag(state, "flag_heifeng_confront");
        Variant hadLast = sessionNode.Get("last_battle");
        // ① 没打过对质 → 不摆
        _flags(state).Remove("flag_heifeng_confront");
        sessionNode.Set(
            "last_battle", new Godot.Collections.Dictionary { { "winner", 0 }, { "team_id", "team_boss" } });
        CheckFalse(map._open_pending_choice_dialogue(), "没打赢大寨主时不摆终局难题");
        CheckFalse(
            map.npc_panel != null && map.npc_panel.Get("dialogue_node_id").AsString() == "dl_ledger_choice",
            "而且没把终局难题那一句塞进面板");
        // ①′ 认得出队伍、但那一队没配对话 → 也不摆（别把「上一场打的是别人」也算进来）
        sessionNode.Set(
            "last_battle", new Godot.Collections.Dictionary { { "winner", 0 }, { "team_id", "team_butcher" } });
        CheckFalse(map._open_pending_choice_dialogue(), "上一场打赢的是别的队伍 → 不摆终局难题");
        // ② 打赢过 + 没选过 → 摆，而且就是表里那一条
        _set_flag(state, "flag_heifeng_confront");
        // 账册也是条件的一部分（表里写了 `item:item_bd_ledger`）：打赢这一场时它是随掉落发的
        // （`battle_screen.TEAM_WIN_ITEMS`），这里补上，验「账册在手 → 摆出来」
        _inv(state).add_item(db, "item_bd_ledger", 1);
        sessionNode.Set(
            "last_battle", new Godot.Collections.Dictionary { { "winner", 0 }, { "team_id", "team_boss" } });
        CheckTrue(map._open_pending_choice_dialogue(), "打赢大寨主之后回图 → 终局难题摆出来");
        Node panel = map.npc_panel;
        CheckNotNull(panel, "摆出来的是一块 NPC 面板（对话容器挂在它上面）");
        if (panel != null)
        {
            CheckEq(panel.Get("npc_id").AsString(), "npc_shen_yanhui", "问话的是沈雁回");
            CheckEq(panel.Get("dialogue_node_id").AsString(), "dl_ledger_choice", "直接说的就是终局难题那一条");
            CheckNotNull(
                _actions(panel).FindChild("TalkOptionopt_ledger_public", true, false),
                "三条路之一（呈官）是一条真按钮");
        }

        // ②′ 读档／重开一局时 `last_battle` 不在会话里：条件满足就必须还问得出来，
        //     否则玩家一读档就再也回答不了那道题（那本账还在背包里，题却没了）
        map.close_npc();
        sessionNode.Set("last_battle", new Godot.Collections.Dictionary());
        CheckTrue(
            map._open_pending_choice_dialogue(),
            "读档进来（认不出上一场是哪支队）也照样摆——不然那道题永远答不了");
        Node reloaded = map.npc_panel;
        CheckNotNull(reloaded, "读档那一路同样摆出面板");
        if (reloaded != null)
        {
            // ③ 选一条 → 旗标落地；再回图就不摆了（条件里那三个 `!flag_ledger_*` 挡住）
            Godot.Collections.Dictionary chosen = reloaded.Call("choose_dialogue", "opt_ledger_public").AsGodotDictionary();
            CheckTrue(_b(chosen, "ok"), $"按钮选得动：{_s(chosen, "error")}");
            CheckTrue(_has_flag(state, "flag_ledger_public"), "选完旗标进存档");
            map.close_npc();
            CheckFalse(map._open_pending_choice_dialogue(), "选过之后回图不再摆（三选一不叠加）");
        }

        // 还原：后面的用例用同一份会话状态，不能留下这一场动过的旗标
        _flags(state).Remove("flag_ledger_public");
        // 钥匙道具不能「丢」，但这是测试夹具——直接清掉那份堆叠
        _inv(state).stacks.Remove("item_bd_ledger");
        if (hadConfront)
        {
            _set_flag(state, "flag_heifeng_confront");
        }
        else
        {
            _flags(state).Remove("flag_heifeng_confront");
        }

        sessionNode.Set("last_battle", hadLast);
    }

    /// <summary>completion（宝箱全开）与 kill_style（毒杀）两类隐藏触发。</summary>
    private void _CheckConditionTriggers(LocalMapController map, GodotObject state, Node sessionNode)
    {
        TriggerPoint chestTrigger = null;
        TriggerPoint poisonTrigger = null;
        foreach (Variant entry in map.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_chest_all")
            {
                chestTrigger = point;
            }

            if (point.trigger_id == "trig_poison_kill")
            {
                poisonTrigger = point;
            }
        }

        CheckNotNull(chestTrigger, "有「宝箱全开」触发点");
        CheckNotNull(poisonTrigger, "有「毒杀毒手」触发点");

        if (chestTrigger != null)
        {
            // 还没开完：给出还差几个，不算触发
            map.player.GlobalPosition = chestTrigger.GlobalPosition;
            Godot.Collections.Dictionary early = map.interact();
            CheckFalse(_b(early, "ok"), "宝箱没开全时不能触发");
            CheckTrue(_status(map).Contains("前寨宝箱"), $"提示还差几个前寨宝箱：{_status(map)}");
            // 计数范围：只算**前寨（第 1 层）**且不在门后那间的宝箱。
            // 以前这里直接 `for chest in map.chests: _open_chest(chest)` —— 把门后面的暗格银箱也强行打开了，
            // 于是「全开」条件看着能过，真实玩法里那条条件根本凑不齐（门后那箱要等门开）。
            Godot.Collections.Array gate = map._completion_gate_chests(chestTrigger.row);
            CheckGt(gate.Count, 0, $"「宝箱全开」有可数的前寨宝箱（{gate.Count} 个）");
            CheckLt(gate.Count, map.chests.Count, $"计数不包含门后那间（全图 {map.chests.Count} 个）");
            Sfx.clear_log();
            foreach (Variant entry in map.chests)
            {
                var chest = entry.As<Chest>();
                // 真实的开门流程只需要开前寨那几个；门后那箱要进去才拿得到
                if (!chest.opened_already && gate.Contains(chest))
                {
                    map._open_chest(chest);
                }
            }

            CheckTrue(Sfx.has_played("chest"), "开箱会请求「宝箱开启」音效（07 §8.4）");
            map.player.GlobalPosition = chestTrigger.GlobalPosition;
            Godot.Collections.Dictionary opened = map.interact();
            CheckTrue(_b(opened, "ok"), $"前寨宝箱全开后触发成功：{_status(map)}");
            // 门要真的开：reward_type=room → 人被带进暗格（以前只播一句「隐藏门浮现」，门没开）
            CheckEq(map.current_room_id(), "hf1_secret", $"隐藏门开了、人被带进「暗格」：{_status(map)}");
            CheckTrue(_status(map).Contains("暗格"), $"文案说清去的是哪间：{_status(map)}");
        }

        if (poisonTrigger != null)
        {
            _inv(state).add_item(map.db, "item_poison_wine", 1);
            // 上一场是正面打死的 → 不算毒杀
            sessionNode.Set("last_battle", _kill_styles("normal"));
            map.player.GlobalPosition = poisonTrigger.GlobalPosition;
            Godot.Collections.Dictionary wrong = map.interact();
            CheckFalse(_b(wrong, "ok"), "正面击杀不算毒杀");
            CheckTrue(_status(map).Contains("毒"), $"提示要用毒杀：{_status(map)}");
            CheckEq(_inv(state).count("item_scroll_wudu"), 0, "没达成条件不给秘籍");
            // 上一场是毒死的 → 给独门秘籍
            sessionNode.Set("last_battle", _kill_styles("poison"));
            Godot.Collections.Dictionary poisoned = map.interact();
            CheckTrue(_b(poisoned, "ok"), $"毒杀后触发成功：{_status(map)}");
            CheckEq(_inv(state).count("item_scroll_wudu"), 1, "拿到五毒秘籍残页");
        }
    }

    /// <summary>「上一场谁怎么死的」那本账（`last_battle.kill_styles`）。</summary>
    private static Godot.Collections.Dictionary _kill_styles(string style) => new()
    {
        { "kill_styles", new Godot.Collections.Dictionary { { "en_bd_poison_hand", style } } },
    };

    /// <summary>存档时机：副本内自动存（开箱/触发/事件/扫荡），手动存只能在城镇。</summary>
    private void _CheckSaveTiming(LocalMapController map, GodotObject state, Node sessionNode)
    {
        // ① 自动存档**失败**不静默（2026-10-03 补）：注入一个写不进去的存档设施，
        //    失败不打断游玩，但状态栏要有「自动存档失败：…」这句（玩家的环境真有可能写不进 user://saves）。
        GodotObject workingStore = map.save_store_override;
        map.save_store_override = UnwritableSaveStore.create(SaveTestDir, 3);
        map._save_service = null;
        Godot.Collections.Dictionary failed = map.autosave("测试注入");
        CheckFalse(_b(failed, "ok"), "写不进去时 autosave 返回失败");
        CheckTrue(_status(map).Contains("自动存档失败"), $"状态栏如实写失败：{_status(map)}");
        CheckTrue(_status(map).Contains("只在本局里"), "并说明后果");
        map.save_store_override = workingStore;
        map._save_service = null;

        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        state.Set("slot", 2);
        map.save_store_override = store;
        map._save_service = null; // 让控制器用新注入的 store 重建
        // 副本里不能手动存档
        Godot.Collections.Dictionary refused = map.press_save();
        CheckFalse(_b(refused, "ok"), "副本里不能手动存档");
        CheckTrue(_status(map).Contains("城镇才是存档点"), $"说明去哪存：{_status(map)}");
        // 副本内的自动存档：做题字判定（成功）之后应该落盘
        Node2D inscription = map.world.GetNodeOrNull<Node2D>("Markers/hf2_hall/Event_ev_hall_inscription");
        CheckNotNull(inscription, "有题字位点");
        if (inscription != null)
        {
            map.player.GlobalPosition = inscription.GlobalPosition;
            map.interact();
            CheckEq(map.save_service().last_reason, "事件判定", "事件判定后自动存档");
            CheckTrue(store.slot_exists(2), "自动存档真的落盘了");
        }

        // 城镇是存档点：换一张图（清风驿）手动存档成功
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        town.save_store_override = store;
        Tree.Root.AddChild(town);
        town.setup();
        CheckTrue(town.can_save_here(), "清风驿是存档点");
        Godot.Collections.Dictionary saved = town.press_save();
        CheckTrue(_b(saved, "ok"), $"城镇能手动存档：{_s(saved, "error")}");
        CheckEq(town.save_service().last_reason, "城镇存档点", "记下存档原因");
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>「潜行入寨」：behavior 类触发点（flag_stealth_full=1）。</summary>
    private void _CheckStealthTrigger(LocalMapController map, GodotObject state, Node sessionNode)
    {
        TriggerPoint stealth = null;
        foreach (Variant entry in map.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_stealth_clear")
            {
                stealth = point;
            }
        }

        CheckNotNull(stealth, "有潜行入寨的触发点");
        if (stealth == null)
        {
            return;
        }

        CheckTrue(stealth.supported(), "behavior 类触发点现在支持了（只剩 sequence 未做）");
        // 打过房间就会标记「潜行失败」
        Godot.Collections.Dictionary local = map.local_state();
        CheckTrue(_b(local, "stealth_broken"), "撞上房间守卫会标记潜行失败");
        map.player.GlobalPosition = stealth.GlobalPosition;
        Godot.Collections.Dictionary refused = map.interact();
        CheckFalse(_b(refused, "ok"), "潜行失败后不能触发潜行入寨");
        CheckTrue(_status(map).Contains("潜行已经失败"), $"说明原因：{_status(map)}");
        // 清掉失败标记（等价于出图重进）就能触发，奖励是剧情旗标
        map.local_state()["stealth_broken"] = false;
        Godot.Collections.Dictionary cleared = map.interact();
        CheckTrue(_b(cleared, "ok"), $"全程没惊动守卫就能触发：{_status(map)}");
        CheckTrue(_has_flag(state, "event_stealth_reward"), "奖励的剧情旗标记下了");
    }

    /// <summary>
    /// once_only 的触发点看**永久记录**：离图（会话清空）再回来不该能重复拿奖励；
    /// 非一次性点位只按这次进图的会话状态判，回来还能再触发。
    /// </summary>
    private void _CheckOnceOnlyTriggers(GodotObject state, Node sessionNode)
    {
        state.Call("record_dungeon", "scene_heifengzhai", "triggers", "trig_rusty_sword");
        // 模拟「回大地图整片刷新」：会话里的已用触发点清空
        Godot.Collections.Dictionary sessionMaps = sessionNode.Get("local_maps").AsGodotDictionary();
        sessionMaps.Remove("scene_heifengzhai");
        sessionNode.Set("local_maps", sessionMaps);

        LocalMapController again = _load_local_run();
        again.state_override = state;
        sessionNode.Set("pending_local_scene", "scene_heifengzhai");
        Tree.Root.AddChild(again);
        again.setup();
        bool rustyUsed = false;
        bool wineUsed = false;
        foreach (Variant entry in again.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_rusty_sword")
            {
                rustyUsed = point.used;
            }
            else if (point.trigger_id == "trig_wine")
            {
                wineUsed = point.used;
            }
        }

        CheckTrue(rustyUsed, "once_only 的锈剑共鸣离图再回也不可用");
        CheckFalse(wineUsed, "非一次性点位回来还能再触发");
        Tree.Root.RemoveChild(again);
        again.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>完成度界面入口：副本里按 M 打开，扫荡按钮走小地图控制器的 sweep_floor。</summary>
    private void _CheckDungeonPanel(LocalMapController map, GodotObject state)
    {
        Godot.Collections.Dictionary opened = map.open_dungeon_panel();
        CheckTrue(_b(opened, "ok"), $"副本里能打开完成度界面：{_s(opened, "error")}");
        CheckNotNull(map.dungeon_panel, "完成度面板挂上了");
        if (map.dungeon_panel == null)
        {
            return;
        }

        CheckEq(map.dungeon_panel.Get("scene_id").AsString(), "scene_heifengzhai", "面板对着当前这张图");
        string detail = map.dungeon_panel.Call("detail_text").AsString();
        CheckTrue(detail.Contains("宝箱"), $"面板显示四项完成度：{detail}");
        // 第 1 层前面已经记成打过，界面里应该可扫荡；扫一次真的走控制器的 sweep_floor
        state.Call("record_dungeon", "scene_heifengzhai", "rooms", "hf1_yard");
        map.dungeon_panel.Call("refresh");
        Godot.Collections.Dictionary swept = map.dungeon_panel.Call("press_sweep", 1).AsGodotDictionary();
        CheckTrue(_b(swept, "ok"), $"面板里能扫荡已通关的层：{_status(map)}");
        CheckEq(map.save_service().last_reason, "扫荡第 1 层", "扫荡后自动存档");
        string sweptStatus = map.dungeon_panel.Call("status_text").AsString();
        CheckTrue(sweptStatus.Contains("扫荡第 1 层"), $"面板上播出扫荡结果：{sweptStatus}");
        map.close_dungeon_panel();
        CheckTrue(map.dungeon_panel == null, "关掉后引用清空");
    }

    /// <summary>事件判定位点：站过去按 E，判定值与难度比大小；结果写存档。</summary>
    private void _CheckEvents(LocalMapController map, GodotObject state)
    {
        // 0.29.0 加了 `ev_shen_rescue`（地牢里救沈雁回），地编同步摆了 `Event_ev_shen_rescue`
        CheckEq(map.events.Count, 5, $"黑风寨有 5 个事件判定位点：{map.events}");
        // 识破柴房机关：奇门 3 ≥ 3，恰好过
        Node2D trap = map.world.GetNodeOrNull<Node2D>("Markers/hf1_shed/Event_ev_shed_trap");
        CheckNotNull(trap, "柴房有机关位点");
        if (trap != null)
        {
            map.player.GlobalPosition = trap.GlobalPosition;
            CheckEq(map.event_near_player(), "ev_shed_trap", "站到石砖旁边认得出是这条判定");
            Godot.Collections.Dictionary result = map.interact();
            CheckTrue(_b(result, "ok") && _b(result, "success"), $"识破机关成功：{_s(result, "text")}");
            CheckTrue(_s(result, "text").Contains("识破柴房机关"), $"文案用表里的 note：{_s(result, "text")}");
            CheckEq(state.Call("event_check_result", "ev_shed_trap").AsString(), "done", "结果写进存档");
        }

        // 题字：文学 5 ≥ 5 也能解出来
        Node2D inscription = map.world.GetNodeOrNull<Node2D>("Markers/hf2_hall/Event_ev_hall_inscription");
        if (inscription != null)
        {
            map.player.GlobalPosition = inscription.GlobalPosition;
            Godot.Collections.Dictionary solved = map.interact();
            CheckTrue(_b(solved, "success"), $"解出三火盆次序：{_s(solved, "text")}");
        }

        // 毒酒：毒术 2 < 3 → 失败并转述代价
        Node2D poison = map.world.GetNodeOrNull<Node2D>("Markers/hf2_poison/Event_ev_poison_identify");
        CheckNotNull(poison, "毒酒辨认的判定位点在（不然下面这段会静默跳过）");
        if (poison != null)
        {
            map.player.GlobalPosition = poison.GlobalPosition;
            Godot.Collections.Dictionary failed = map.interact();
            CheckFalse(_b(failed, "success"), "毒术不够认不出毒酒");
            CheckTrue(_s(failed, "text").Contains("无法辨认"), $"失败文案来自 fail_note：{_s(failed, "text")}");
        }
    }

    /// <summary>完成度 HUD、永久记录与「已通关层扫荡」。</summary>
    private void _CheckProgressRecords(LocalMapController map, GodotObject state)
    {
        CheckTrue(map.has_completion(), "黑风寨是带完成度的副本");
        CheckTrue(map.progress_text().Contains("完成度"), $"HUD 显示完成度：{map.progress_text()}");
        Godot.Collections.Dictionary record = _record(state, "scene_heifengzhai");
        // 记的是**真正开过的**宝箱：前寨那两个走正常流程开了，门后的暗格银箱还没进去拿。
        var openedCount = 0;
        foreach (Variant entry in map.chests)
        {
            if (entry.As<Chest>().opened_already)
            {
                openedCount += 1;
            }
        }

        CheckEq(_a(record, "chests").Count, openedCount, $"开过的宝箱都记进永久记录（{openedCount} 个）");
        CheckGt(_a(record, "triggers").Count, 2, "用过的触发点都记进永久记录");

        // 进过隐藏房间也要记（会话状态回大地图会清，完成度必须永久）
        Node2D deep = map.world.GetNodeOrNull<Node2D>("Rooms/Room_hf1_deep");
        CheckNotNull(deep, "隐藏房间 hf1_deep 在（不然完成度这段会静默跳过）");
        if (deep != null)
        {
            Node2D bounds = deep.GetNodeOrNull<Node2D>("bounds");
            CheckNotNull(bounds, "隐藏房间有 bounds 区（没它 current_room_id 认不出房间）");
            if (bounds != null)
            {
                map.player.GlobalPosition = bounds.GlobalPosition;
                map._track_room();
                CheckTrue(
                    _a(_record(state, "scene_heifengzhai"), "rooms_entered").Contains("hf1_deep"),
                    $"隐藏房间进入被记录：{_a(_record(state, "scene_heifengzhai"), "rooms_entered")}");
            }
        }

        // 第 1 层的前院还没打过 → 不能扫荡
        Godot.Collections.Dictionary early = map.sweep_floor(1);
        CheckFalse(_b(early, "ok"), $"没通关的层扫荡被拒：{_status(map)}");
        CheckTrue(_status(map).Contains("还没通关"), "说明这一层没打完");
        // 记录成已打过之后就能扫荡，HUD 也会提示
        state.Call("record_dungeon", "scene_heifengzhai", "rooms", "hf1_yard");
        map._refresh_progress();
        CheckTrue(map.progress_text().Contains("可扫荡"), $"HUD 提示可扫荡的层：{map.progress_text()}");
        Godot.Collections.Dictionary swept = map.sweep_floor(1);
        CheckTrue(_b(swept, "ok"), $"通关后能扫荡：{_status(map)}");
        CheckTrue(_status(map).Contains("扫荡第 1 层"), $"状态栏播报扫荡结果：{_status(map)}");
        CheckTrue(_status(map).Contains("仅掉落"), $"说明扫荡只结算掉落：{_status(map)}");
    }

    /// <summary>
    /// 战败处理（08）要用「最近到过的城镇」＋战斗外增益 HUD：
    /// 进城镇时登记出生点／城镇；HUD 上要看得见还剩几分钟（两个控制器共用 `FieldBuffHud` 的文案）。
    /// </summary>
    private void _CheckTownShelterAndFieldHud(TableDb db, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        // 先清空（前面的用例可能已经进过城镇），才能验出「这一进图真的登记了」
        sessionNode.Set("last_shelter_scene", "");
        sessionNode.Set("last_shelter_name", "");
        LocalMapController town = _load_local_run();
        Tree.Root.AddChild(town);
        town.setup();
        CheckEq(
            sessionNode.Get("last_shelter_scene").AsString(), "scene_qingfengyi",
            "进城镇就把它记成「最近的出生点／城镇」（战败回城要用）");
        CheckEq(sessionNode.Get("last_shelter_name").AsString(), "清风驿", "连中文名一起记（结算卡片要写给玩家看）");

        // 战斗外增益 HUD：注入一条 10 分钟的，标签上要写清名字与剩余分钟
        sessionNode.Call("clear_field_buffs");
        sessionNode.Call("add_field_buff", "buff_meditated", 10, "", 1);
        town._refresh_field_buffs();
        var fieldLabel = town.FindChild("FieldBuffs", true, false) as Label;
        CheckNotNull(fieldLabel, "城镇 HUD 上有战斗外增益那一行");
        if (fieldLabel != null)
        {
            CheckTrue(fieldLabel.Text.Contains("打坐余韵"), $"写清是哪个增益：{fieldLabel.Text}");
            CheckTrue(fieldLabel.Text.Contains("剩 10 分钟"), $"写清还剩几分钟：{fieldLabel.Text}");
        }

        // 过期（用注入的 now，不用真等 10 分钟）→ 标签变空，不显示过期的东西
        int future = (int)Time.GetUnixTimeFromSystem() + 11 * 60;
        Godot.Collections.Array expired = sessionNode.Call("active_field_buffs", future).AsGodotArray();
        CheckEq(expired.Count, 0, "到点即失效（按现实分钟）");
        town._refresh_field_buffs();
        if (fieldLabel != null)
        {
            CheckEq(fieldLabel.Text, "", "没有生效的增益时这一行是空的（不占视觉）");
        }

        Tree.Root.RemoveChild(town);
        town.Free();
    }

    private void _CheckTownShop(TableDb db, GodotObject state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        Tree.Root.AddChild(town);
        town.setup();
        CheckEq(town.scene_id, "scene_qingfengyi", "进入清风驿城镇");
        // 城镇没配 level_range（表里是空的）→ HUD 不该硬塞一个「推荐等级」
        CheckFalse(
            _status(town).Contains("推荐等级"),
            $"没配推荐等级的图不显示这一项：{_status(town)}");
        CheckEq(town.building_near_player(), "", "刚进镇时身边没有店");

        Node2D smith = town.world.GetNodeOrNull<Node2D>("Markers/Buildings/bld_smith");
        CheckNotNull(smith, "地图上有铁匠铺位点");
        if (smith == null)
        {
            Tree.Root.RemoveChild(town);
            town.Free();
            return;
        }

        town.player.GlobalPosition = smith.GlobalPosition + new Vector2(0, 12);
        CheckEq(town.building_near_player(), "bld_smith", "站到铁匠铺旁边能认出来");
        Godot.Collections.Dictionary opened = town.interact();
        CheckTrue(_b(opened, "ok"), $"按 E 进店：{_s(opened, "error")}");
        CheckEq(_s(opened, "building_id"), "bld_smith", "开的是铁匠铺");
        CheckNotNull(town.shop_panel, "商店界面挂上了");
        if (town.shop_panel != null)
        {
            CheckEq(town.shop_panel.Call("building_name").AsString(), "铁匠铺", "界面标题是铁匠铺");
            string shopStatus = town.shop_panel.Call("status_text").AsString();
            CheckTrue(shopStatus.Contains("进店"), $"状态栏提示进店：{shopStatus}");
        }

        town.close_shop();
        CheckTrue(town.shop_panel == null, "关掉后引用清空");
        _CheckTownNpcs(town);
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 城镇 NPC 站位（07 §9 第 12 条／§12）：地编交付的是 `Characters/npc_slot_0N`，代码这边先接
    /// 「站位 ＋ 交互」（台词等 `dialogue_tree.csv`）——**以前这 5 个位点一个都没人读**，
    /// 地图验收也不看它们（`npc_slot_` 不在 `MARKER_PREFIXES` 里），摆歪了没有任何东西会红。
    /// </summary>
    private void _CheckTownNpcs(LocalMapController town)
    {
        CheckEq(town.npcs.Count, 5, "镇上的 5 个 NPC 站位都被代码接上了");
        if (town.npcs.Count == 0)
        {
            return;
        }

        var first = town.npcs[0].As<Node2D>();
        // 名字来自地编：占位命名 `npc_slot_0N` 与按 id 绑的 `npc_<npc_id>` **两种都合法**（07 §九 第 12 条）
        CheckTrue(first.Name.ToString().StartsWith("npc_"), $"位点的名字来自地编（{first.Name}）");
        CheckNull(town.npc_near_player(), "站在出生点时不误认身边的 NPC");
        town.player.GlobalPosition = first.GlobalPosition;
        CheckEq(town.npc_near_player(), first, "站到路人旁边认得出他");
        Godot.Collections.Dictionary talked = town.interact();
        CheckTrue(_b(talked, "ok"), "按 E 有回应（不静默）");
        CheckEq(_s(talked, "npc"), first.Name.ToString(), "回应里带的是这个站位的名字");
        // 设计 19（0.25.0）：城镇 NPC 从「按 E 一句占位台词」升级成**可以交往的人**——
        // 现在按 E 打开的是 NPC 面板，台词取 `npc_def.greet_text_cn`（不再写死在代码里）。
        CheckFalse(_s(talked, "npc_id").Length == 0, $"这个站位对得上 npc_def 里的人：{_s(talked, "npc_id")}");
        Resource npcRow = GetDb().get_row("npc_def", _s(talked, "npc_id"));
        CheckNotNull(npcRow, "站位绑到的 npc_def 行存在");
        if (npcRow is NpcDefRow row)
        {
            CheckEq(_s(talked, "speaker"), row.name_cn, "说话的人就是这个人");
            CheckEq(_s(talked, "text"), row.greet_text_cn, "开场白取自表里的 greet_text_cn");
            CheckFalse(_s(talked, "text").Contains("npc_"), "玩家可见文案不漏表内 id");
        }

        CheckNotNull(town.npc_panel, "按 E 打开了 NPC 交往面板");
        town.close_npc();
        // **五个站位逐个人都过一遍「按 E 说话」**（决策 329）：第 5 个站位背后没有 `npc_def` 行，
        // 那句兜底文案以前写的是「（这个人还没有配 npc_def 行）」——`npc_def` 正是 CopyGuard 盯的
        // 表内 id 形态（AGENTS 硬规矩），而这条用例只按过第一个人，所以一直没人红。
        // 这里走 `_talk_to_npc`（`interact()` 里那一条分支本身）而不是 `interact()`：后者会先撞上
        // 店铺／设施分支（`facility_bounty_board` 还会动引导旗标），那是另一条用例的事。
        for (int index = 0; index < town.npcs.Count; index++)
        {
            var slot = town.npcs[index].As<Node2D>();
            Godot.Collections.Dictionary said = town._talk_to_npc(slot);
            CheckTrue(_b(said, "ok"), $"第 {index + 1} 个站位都能说话（不静默）");
            string[] hits = LocalMapController.CopyGuardScript().Call("id_tokens", town).AsStringArray();
            CheckEq(hits.Length, 0, $"第 {index + 1} 个站位说话后界面不漏表内 id：{string.Join(", ", hits)}");
            town.close_npc();
        }

        // **按 id 绑**（07 §九 第 12 条）：位点改名成 `npc_<npc_id>` 之后，绑的人跟着名字走，不再靠
        // `npc_def` 的行序——行序一变，占位命名就会**静默把旁边的人认成他**（决策 332）。
        var renamed = town.npcs[0].As<Node2D>();
        string originalName = renamed.Name.ToString();
        renamed.Name = "npc_qian_dafu"; // 钱大夫（按行序第 1 个是王铁，故意挑一个不一样的）
        Godot.Collections.Dictionary byId = town._talk_to_npc(renamed);
        CheckEq(_s(byId, "npc_id"), "npc_qian_dafu", "按 id 绑的位点认到的是名字里那个人");
        CheckEq(_s(byId, "speaker"), "钱大夫", "说话的人对得上（不靠行序）");
        town.close_npc();
        renamed.Name = originalName;
        CheckEq(
            _s(town._talk_to_npc(renamed), "npc_id"), "npc_wang_tie",
            "改回占位命名后仍按行序绑（老口径不退化）");
        town.close_npc();
        // 认不出来的 id **不许退回按顺序**——那会把旁边的人认成他，比认不出来更糟。
        // 这里直接问判据本身（走 `_talk_to_npc` 会按设计打一条 push_error，日志里不必留这个噪声）。
        CheckEq(
            NpcService.npc_for_slot(GetDb(), "npc_bu_cun_zai", "scene_qingfengyi"),
            "", "认不出的 id 不绑人（不退回按顺序）");
        // 优先级：NPC 分支排在店铺之后——站在店门口的路人不能把「进店」抢掉
        Node2D smith = town.world.GetNodeOrNull<Node2D>("Markers/Buildings/bld_smith");
        CheckNotNull(smith, "（前提）铁匠铺位点在");
        if (smith != null)
        {
            Vector2 home = first.GlobalPosition;
            first.GlobalPosition = smith.GlobalPosition;
            town.player.GlobalPosition = smith.GlobalPosition;
            Godot.Collections.Dictionary nearShop = town.interact();
            CheckEq(_s(nearShop, "building_id"), "bld_smith", "店铺优先于路人（不然站在门口的 NPC 会把进店挡掉）");
            town.close_shop();
            first.GlobalPosition = home;
        }
    }

    /// <summary>
    /// 赌局位点：镇上摆着 `Event_ev_gamble`，而判定行只有 `region_id`——它必须真的能被按 E 触发。
    ///
    /// 以前小地图收位点只认 `scene_id`，于是这个位点**永远接不上**（地图验收那边还专门写了
    /// `check_id != "ev_gamble"` 的例外把它从大地图排除掉，两边都知道它特殊、却没人接上）。
    /// </summary>
    private void _CheckTownGamble(TableDb db, GodotObject state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        // 事件判定会触发自动存档——这里必须给临时目录，否则 headless 下写 user:// 会崩（踩过）
        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        town.save_store_override = store;
        Tree.Root.AddChild(town);
        town.setup();
        Node2D marker = town.world.GetNodeOrNull<Node2D>("Markers/Event_ev_gamble");
        CheckNotNull(marker, "镇上摆着赌局位点");
        if (marker != null)
        {
            var wired = false;
            foreach (Variant entry in town.events)
            {
                if (_s(entry.AsGodotDictionary(), "check_id") == "ev_gamble")
                {
                    wired = true;
                }
            }

            CheckTrue(wired, $"赌局位点被接上了（本图共 {town.events.Count} 个判定位点）");
            town.player.GlobalPosition = marker.GlobalPosition;
            CheckEq(town.event_near_player(), "ev_gamble", "站到赌局旁边认得出这条判定");
            int before = _inv(state).money;
            Godot.Collections.Dictionary result = town.resolve_event("ev_gamble");
            CheckTrue(_s(result, "text").Contains("掷骰"), $"赌局是软判定：文案里带掷骰 —— {_s(result, "text")}");
            CheckEq(_inv(state).count("item_money"), 0, "背包里不该多出一行「铜钱」");
            // 表里赌局的奖励是 `item_money` 且没有数量列 → 按 1 文算；金额待设计给（见当前状态缺口 #12）
            if (_b(result, "success"))
            {
                CheckEq(_inv(state).money, before + 1, "赢了：铜钱 +1（走钱，不走背包）");
            }
            else
            {
                CheckEq(_inv(state).money, before, "输了：不掉钱（软判定失败不惩罚）");
            }

            string outcome = state.Call("event_check_result", "ev_gamble").AsString();
            CheckTrue(
                _one_of(outcome, "done", "failed"),
                $"结果写进存档：{outcome}");
        }

        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    private static bool _one_of(string value, params string[] options) =>
        System.Array.IndexOf(options, value) >= 0;

    private void _CheckSpawn(LocalMapController map)
    {
        CheckEq(map.scene_id, "scene_heifengzhai", "进入黑风寨小地图");
        // 推荐等级来自 map_local.level_range（以前这列没人读，玩家看不到「这儿打不打得过」）
        CheckTrue(
            _status(map).Contains("推荐等级 3~15"),
            $"HUD 显示推荐等级：{_status(map)}");
        CheckEq(map.teams.Count, 6, "6 支房间敌人（前院/演武场/毒堂/聚义厅/巡逻道/大堂）");
        CheckEq(map.chests.Count, 4, "4 个宝箱（柴房/火盆密室/暗格/宝库）");
        CheckEq(map.triggers.Count, 7, "7 个触发位点：6 条触发里酒葫芦占两处");
        CheckNotNull(map.player, "玩家在场景里");
    }

    private void _CheckRoomEncounter(LocalMapController map, Godot.Collections.Array captures)
    {
        var enemy = map.teams[0].As<RoamingEnemy>();
        map.player.GlobalPosition = enemy.GlobalPosition + new Vector2(0, 8);
        enemy.reset_latch();
        enemy._check_contact();
        CheckEq(captures.Count, 1, "贴上房间敌人会触发遭遇");
        if (captures.Count >= 1)
        {
            var encounter = captures[0].As<Encounter>();
            CheckEq(encounter.source_scene, "scene_heifengzhai", "遭遇记下来自哪张小地图");
            CheckEq(encounter.source_key, "hf1_yard", "遭遇记下房间 id");
            CheckEq(encounter.contact, "front", "正面接触");
        }
    }

    /// <summary>
    /// 宝箱的掉落也要吃「存档里那一份保底计数」——与战斗结算、扫荡同一个口径
    /// （唯一读写口是 `GameState.pity_tracker()`／`store_pity()`）。
    ///
    /// 今天宝箱组一个保底槽都没有（`pity_count` 全是 0），所以**只能靠夹具**验：
    /// 把铜箱的一条槽改成 `base_rate=0 / pity_count=2`——第二次开箱必须被保底强行顶着出货，
    /// 而且计数要**存回存档**。以前 `_open_chest` `new` 了一个临时 PityTracker，记完就丢：
    /// 每次开箱都从 0 开始数 → 那条槽**永远不出货**。
    /// </summary>
    private void _CheckChestPity(LocalMapController map, GodotObject state)
    {
        var custom = new TableDb();
        custom.load_all();
        var table = (TableResource)custom.tables["drop_table"].As<TableResource>().Duplicate(true);
        var targetRowId = "";
        var targetItem = "";
        foreach (Variant entry in table.rows)
        {
            var row = (DropTableRow)entry.AsGodotObject();
            if (row.drop_group != "drop_chest_copper")
            {
                continue;
            }

            if (targetRowId.Length == 0)
            {
                targetRowId = row.drop_row_id;
                targetItem = row.item_id;
                row.base_rate = 0.0;
                row.pity_count = 2;
            }
            else
            {
                // 组里其余槽：也不掉，免得混进结果里（这条用例只看夹具那一槽）
                row.base_rate = 0.0;
                row.pity_count = 0;
            }
        }

        CheckTrue(targetRowId.Length > 0, "夹具找到了铜箱的一条掉落槽");
        if (targetRowId.Length == 0)
        {
            return;
        }

        custom.tables["drop_table"] = table;
        Chest chest = null;
        foreach (Variant entry in map.chests)
        {
            var each = entry.As<Chest>();
            if (each.tier_id() == "copper")
            {
                chest = each;
            }
        }

        CheckNotNull(chest, "地图里有铜箱可以开");
        if (chest == null)
        {
            return;
        }

        TableDb savedDb = map.db;
        map.db = custom;
        string key = $"drop_chest_copper|{targetRowId}";
        PityTracker tracker = state.Call("pity_tracker").As<PityTracker>();
        tracker.clear();
        state.Call("store_pity", tracker);
        chest.opened_already = false;
        Godot.Collections.Dictionary first = map._open_chest(chest);
        CheckEq(_a(first, "drops").Count, 0, "第一次开箱：rate=0 且保底没到 → 一件不掉");
        CheckEq(state.Call("pity_tracker").As<PityTracker>().attempts(key), 1,
            "那一次尝试记进了**存档里那一份**计数（不是临时计数器）");
        chest.opened_already = false;
        Godot.Collections.Dictionary second = map._open_chest(chest);
        CheckEq(_a(second, "drops").Count, 1, "第二次开箱：保底 2 次到点，强行出一件");
        if (_a(second, "drops").Count > 0)
        {
            Godot.Collections.Dictionary drop = _a(second, "drops")[0].AsGodotDictionary();
            CheckTrue(_b(drop, "from_pity"), "这一件是保底顶出来的（from_pity）");
            CheckEq(_s(drop, "item_id"), targetItem, "出的是夹具那条槽的货");
        }

        CheckEq(state.Call("pity_tracker").As<PityTracker>().attempts(key), 0, "出货后计数清零（写回的仍是存档里那一份）");
        map.db = savedDb;
    }

    private void _CheckChest(TableDb db, LocalMapController map, GodotObject state)
    {
        var chest = map.chests[0].As<Chest>();
        // 三档外观：按掉落组给颜色与个头（设计 07「宝箱开启（三档）」）
        var tiers = new System.Collections.Generic.Dictionary<string, Chest>();
        foreach (Variant entry in map.chests)
        {
            var each = entry.As<Chest>();
            tiers[each.tier_id()] = each;
            CheckTrue(
                _one_of(each.tier_id(), "copper", "silver", "gold"),
                $"宝箱档位认得出来：{each.chest_id} → {each.tier_id()}");
        }

        CheckTrue(tiers.Count >= 2, $"地图上不止一种宝箱（拿到 {tiers.Count} 档）");
        if (tiers.ContainsKey("copper") && tiers.ContainsKey("silver"))
        {
            CheckNe(
                tiers["copper"].tier_color().ToHtml(), tiers["silver"].tier_color().ToHtml(),
                "铜箱与银箱颜色不同");
            // 贴图如果在了，就要用对应档位的那张（地编交付 chest_copper/silver/gold）
            string copperSprite = tiers["copper"].body_texture_path();
            string silverSprite = tiers["silver"].body_texture_path();
            if (copperSprite.Length > 0 && silverSprite.Length > 0)
            {
                CheckTrue(copperSprite.Contains("chest_copper"), $"铜箱用 chest_copper 贴图：{copperSprite}");
                CheckTrue(silverSprite.Contains("chest_silver"), $"银箱用 chest_silver 贴图：{silverSprite}");
                CheckFalse(copperSprite.Contains("_open"), "没开过的箱子用未开贴图");
            }

            CheckNe(
                ((Label)tiers["copper"].GetNode("State")).Text,
                ((Label)tiers["silver"].GetNode("State")).Text,
                $"档位写在箱子上：{((Label)tiers["copper"].GetNode("State")).Text} / "
                    + $"{((Label)tiers["silver"].GetNode("State")).Text}");
        }

        map.player.GlobalPosition = chest.GlobalPosition;
        int moneyBefore = _inv(state).money;
        Godot.Collections.Dictionary result = map.interact();
        CheckTrue(_b(result, "ok"), $"开宝箱成功：{_status(map)}");
        CheckEq(map.save_service().last_reason, "开宝箱", "开宝箱后自动存档（设计 02：副本内自动存）");
        CheckTrue(chest.opened_already, "宝箱标记为已开");
        CheckEq(((Label)chest.GetNode("State")).Text, "已开", "开过的箱子标「已开」");
        // 贴图：地编给了三档 × 开/未开，开过之后应该换成 _open 那张（没贴图才退回色块）
        if (chest.body_texture_path().Length == 0)
        {
            CheckEq(_body_color(chest.GetNode("Body")).ToHtml(), Color.FromHtml("6f6656").ToHtml(),
                "没有贴图时退回色的暗版");
        }
        else
        {
            CheckTrue(
                chest.body_texture_path().Contains($"chest_{chest.tier_id()}")
                    && chest.body_texture_path().Contains("_open"),
                $"开过的箱子换成对应的 _open 贴图：{chest.body_texture_path()}");
        }

        CheckTrue(_inv(state).money >= moneyBefore, "铜钱只增不减");
        // 再开一次：同一个宝箱不能重复领取
        Godot.Collections.Dictionary again = map.interact();
        CheckFalse(_b(again, "ok"), "同一个宝箱不能重复开");
        CheckTrue(_status(map).Contains("没有可交互"), "重复交互给出提示");
    }

    /// <summary>
    /// 隐藏内容的奖励也走「唯一入账口径」：配一条 `reward_type=item, reward_id=item_money`（奖励铜钱）
    /// 必须进 `inventory.money`，**不能**在背包里发一行「铜钱」——`ev_gamble` 当年就是这个坑。
    /// 表里现在没有这种行（所以这是一条防回归的绊线），用合成行直接调发放函数。
    /// </summary>
    private void _CheckTriggerMoneyReward(TableDb db, LocalMapController map, GodotObject state)
    {
        Resource template = db.get_row("hidden_trigger", "trig_chest_all");
        CheckNotNull(template, "拿一条真实隐藏内容当模板");
        if (template == null)
        {
            return;
        }

        var row = (HiddenTriggerRow)template.Duplicate(true);
        row.reward_type = "item";
        row.reward_id = "item_money";
        int moneyBefore = _inv(state).money;
        string text = map._grant_trigger_reward(row, "测试奖励");
        CheckTrue(_inv(state).money > moneyBefore, $"铜钱奖励进钱包：{text}");
        CheckEq(_inv(state).count("item_money"), 0, "背包里不该出现一行「铜钱」");
        CheckTrue(text.Contains("文钱"), $"文案写「得到 N 文钱」：{text}");
    }

    /// <summary>
    /// 背包满时的宝箱文案：`_drops_summary` 必须如实写「没捡起」。
    /// 以前它只看 `items` / `equipment` / `money`，不看 `overflow`——满包开箱会显示「空的」，
    /// 玩家以为箱子里没东西（其实是放不下）。玩家可见的文案不许说假话。
    /// 这里直接喂合成的 `applied`，不靠随机掉落（宝箱掉落是概率掷的，不能拿它当证据）。
    /// </summary>
    private void _CheckChestOverflowCopy(LocalMapController map)
    {
        var partial = new Godot.Collections.Dictionary
        {
            {
                "items", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "item_id", "item_herb" }, { "qty", 2 }, { "overflow", 3 },
                    },
                }
            },
            { "equipment", new Godot.Collections.Array() },
            { "money", 0 },
            {
                "overflow", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "item_id", "item_herb" }, { "qty", 3 }, { "reason", "背包已满" },
                    },
                }
            },
        };
        string text = map._drops_summary(
            partial,
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "item_id", "item_herb" }, { "qty", 5 } },
            });
        CheckTrue(text.Contains("草药 ×2"), $"拿到的部分照实写：{text}");
        CheckTrue(text.Contains("没捡起"), $"没捡起的部分也要写：{text}");
        CheckTrue(text.Contains("×3"), $"写清没捡起几个：{text}");

        // 一个都没进去：不许显示「空的」（箱子不是空的，是放不下）
        var allLost = new Godot.Collections.Dictionary
        {
            { "items", new Godot.Collections.Array() },
            { "equipment", new Godot.Collections.Array() },
            { "money", 0 },
            {
                "overflow", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "item_id", "item_herb" }, { "qty", 5 }, { "reason", "背包已满" },
                    },
                }
            },
        };
        string lostText = map._drops_summary(
            allLost,
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "item_id", "item_herb" }, { "qty", 5 } },
            });
        CheckNe(lostText, "空的", $"不许说「空的」（其实是放不下）：{lostText}");
        CheckTrue(lostText.Contains("没捡起"), $"写清全都没捡起：{lostText}");
    }

    /// <summary>
    /// 观察点（设计 20 §3.2／0.29.1）：按 E 只出一句碎句——
    /// **不发奖励、不进副本完成度**（它是「看一眼」，不是一个事件）。
    ///
    /// **0.32.0 改动**：读过的观察点要记一枚 `flag_obs_<point_id>`——幕二那条「免战」选项的前置
    /// 就写 `flag_obs_ob_luoyanpo_cart_01`（「看过的观察点」由此能被别的判定引用）。
    /// 所以这条断言从「不置任何旗标」改成「只多这一枚」（除了它，仍然什么都不动）。
    /// </summary>
    private void _CheckFlavorPoints(LocalMapController map, GodotObject state)
    {
        Godot.Collections.Array points = map.flavor_points;
        CheckGt(points.Count, 0, $"黑风寨收了观察点位点（{points.Count} 个）");
        if (points.Count == 0)
        {
            return;
        }

        Godot.Collections.Dictionary entry = points[0].AsGodotDictionary();
        string pointId = _s(entry, "point_id");
        Resource row = map.db.get_row("flavor_point", pointId);
        CheckNotNull(row, $"位点认得出对应的表行（{pointId}）");
        if (row == null)
        {
            return;
        }

        map.player.GlobalPosition = ((Node2D)_v(entry, "node").AsGodotObject()).GlobalPosition;
        CheckEq(map.flavor_near_player(), pointId, "站到观察点旁边认得出它");
        int flagsBefore = _flags(state).Count;
        string recordBefore = _record(state, "scene_heifengzhai").ToString();
        string obsFlag = $"flag_obs_{pointId}";
        bool hadObs = _has_flag(state, obsFlag);
        Godot.Collections.Dictionary result = map.read_flavor_point(pointId);
        CheckTrue(_b(result, "ok"), "看得到这一句");
        string rowText = ((FlavorPointRow)row).text_cn;
        CheckEq(_s(result, "text"), rowText, "文案来自表");
        CheckTrue(_status(map).Contains(rowText), "状态栏把碎句显示出来了");
        CheckTrue(_has_flag(state, obsFlag), $"读过就记下 {obsFlag}（幕二免战选项的前置）");
        CheckEq(
            _flags(state).Count, flagsBefore + (hadObs ? 0 : 1),
            "除 flag_obs_<point_id> 之外不多置旗标");
        CheckEq(_record(state, "scene_heifengzhai").ToString(), recordBefore, "也不进副本完成度");
        // **看得见**（设计 15 §一：可交互物要在低饱和背景里暖色提亮）——观察点在地图上只是一根
        // 光秃秃的 Marker2D，没有这枚标记玩家只会从旁边走过去，而它们正是 20 §3.2 要的「探索感」。
        // 逐个位点都查（不是只查第一个）：漏挂一个就等于那一句碎句玩家永远看不见。
        foreach (Variant item in points)
        {
            Godot.Collections.Dictionary point = item.AsGodotDictionary();
            var node = (Node2D)_v(point, "node").AsGodotObject();
            Node mark = node.GetNodeOrNull(FlavorMarker.node_name());
            CheckNotNull(mark, $"观察点 {_s(point, "point_id")} 挂着可见标记");
            if (mark != null)
            {
                CheckTrue(
                    FlavorMarker.is_visible_highlight(mark),
                    $"标记是暖色提亮且可见（{_s(point, "point_id")}）");
            }
        }
    }

    /// <summary>
    /// 击杀方式的中文名必须**查表**（`status_effect.name_cn`）——设计新加一条状态、再拿它当 `kill_with` 时，
    /// 抄一份常量名单的实现会把英文 id 直接甩给玩家（和当年「dot_poison 泄露」同一类）。
    /// 顺带核宝箱档位：从掉落组名解析，且**认不出来要出声**。
    /// </summary>
    private void _CheckKillStyleNames(LocalMapController map, TableDb db)
    {
        var stub = new TableDb();
        stub.load_all();
        var statuses = (TableResource)stub.tables["status_effect"].As<TableResource>().Duplicate(true);
        var wound = (StatusEffectRow)statuses.rows[0].As<TableRow>().Duplicate(true);
        wound.status_id = "wound";
        wound.id = "wound";
        wound.name_cn = "创伤";
        statuses.rows.Add(wound);
        statuses.index["wound"] = statuses.rows.Count - 1;
        stub.tables["status_effect"] = statuses;
        map.db = stub;
        CheckEq(map._style_name("wound"), "创伤", "新状态拿来做 kill_with：名字来自表（不是英文 id）");
        CheckEq(
            map._style_name("poison"),
            ((StatusEffectRow)stub.get_row("status_effect", "poison")).name_cn,
            "既有状态名同样来自表");
        CheckEq(map._style_name("normal"), "正面击杀", "normal 是代码侧的名字（它不是状态行）");
        map.db = db;

        CheckEq(Chest.resolve_tier("drop_chest_copper"), "copper", "铜箱档位从掉落组名解析");
        CheckEq(Chest.resolve_tier("drop_chest_silver"), "silver", "银箱档位从掉落组名解析");
        CheckEq(Chest.resolve_tier("drop_chest_gold"), "gold", "金箱档位从掉落组名解析");
    }

    /// <summary>
    /// `reward_type=boss`（06 允许、2026-10-03 才接上）：判定通过 → **当场开战**。
    ///
    /// 发行数据里还没有哪条判定用 boss 奖励（12 条里用的是 none/item/event/room），所以用夹具：
    /// 复制表、把「识破柴房机关」那条的奖励改成 boss → 真走一遍 `resolve_event()`，
    /// 断言「开战请求被场景层接住」——建出来的 Encounter 会交给 `battle_switch_handler`。
    /// </summary>
    private void _CheckEventBossReward(
        TableDb db, LocalMapController map, GodotObject state, Godot.Collections.Array captures)
    {
        var stub = new TableDb();
        stub.load_all();
        var copy = (TableResource)stub.tables["event_check"].As<TableResource>().Duplicate(true);
        foreach (Variant entry in copy.rows)
        {
            var row = (EventCheckRow)entry.AsGodotObject();
            if (row.check_id == "ev_shed_trap")
            {
                row.reward_type = "boss";
                row.reward_id = "en_bd_boss";
            }
        }

        stub.tables["event_check"] = copy;
        // 事件服务的表库在第一次用时就被缓存了（前面的 `_CheckEvents` 已经用过）——
        // 所以这里直接换掉控制器手上的 db 并清掉缓存，与 `town._save_service = null` 同一套注入手法。
        map.db = stub;
        map._event_service = null;

        Node sessionNode = Tree.Root.GetNodeOrNull<Node>("GameSession");
        CheckNotNull(sessionNode, "用例能拿到会话");
        int before = captures.Count;
        Godot.Collections.Dictionary result = map.resolve_event("ev_shed_trap");
        CheckTrue(_b(result, "success"), $"判定本身成功：{_s(result, "text")}");
        CheckTrue(_s(result, "text").Contains("现身"), $"文案写「现身」：{_s(result, "text")}");
        CheckEq(captures.Count, before + 1, "场景层接住开战请求（切战斗的那条回调被调用）");
        if (captures.Count > before)
        {
            var encounter = captures[before].As<Encounter>();
            CheckEq(encounter.team_id, "team_hidden_en_bd_boss", "用的是单人 Boss 队（与小地图隐藏 Boss 同一口径）");
            CheckEq(encounter.source_key, "ev_shed_trap", "来源记的是这条判定（结算要用它找掉落与首杀）");
        }

        if (sessionNode != null)
        {
            CheckNotNull(sessionNode.Get("pending_encounter").AsGodotObject(), "遭遇写进会话（切场景后战斗场景读它）");
            CheckEq(sessionNode.Get("pending_return_scene").AsString(), "res://scenes/local_run.tscn", "打完回这张小地图");
            CheckEq(sessionNode.Get("pending_local_scene").AsString(), "scene_heifengzhai", "回的是黑风寨");
        }

        map.db = db;
        map._event_service = null;
    }

    /// <summary>
    /// 三火盆：地编现在只摆了 1 个位点（07 待补第 6 条要求三个编号位点 `Trigger_trig_brazier_1/2/3`），
    /// 而「点错重置」「每格独立的灭／燃」「完成时同一行一起收口」这些规则**只有三格都在时才验得出来**——
    /// 所以这里按同一套命名约定补两个内存位点当夹具（**只加在内存里，不动地图资产**）。
    /// </summary>
    private Godot.Collections.Array _BrazierFixture(LocalMapController map)
    {
        TriggerPoint basePoint = null;
        foreach (Variant entry in map.triggers)
        {
            var each = entry.As<TriggerPoint>();
            if (each.trigger_id == "trig_brazier")
            {
                basePoint = each;
            }
        }

        if (basePoint == null)
        {
            return new Godot.Collections.Array();
        }

        basePoint.sequence_index = 1; // 地编把位点改名成 `Trigger_trig_brazier_1` 之后就是这个编号
        var points = new Godot.Collections.Array { basePoint };
        foreach (int index in new[] { 2, 3 })
        {
            var extra = new TriggerPoint { Name = $"Trigger_trig_brazier_{index}" };
            extra.setup(basePoint.row, map.player, false, map.db, index);
            // 摆在地图上真实那个位点旁边：这样 `can_interact()` 的距离判定也跟真地图一致
            // （不然「完成后不再可交互」那条断言会因为距离太远而永远为真——假绿）
            map.AddChild(extra);
            extra.GlobalPosition = basePoint.GlobalPosition;
            map.triggers.Add(extra);
            points.Add(extra);
        }

        return points;
    }

    /// <summary>夹具收尾：只摘掉内存里补出来的那两个，地图里真实的那个位点要留给后面的检查。</summary>
    private void _TeardownBrazierFixture(LocalMapController map, Godot.Collections.Array points)
    {
        for (int index = 1; index < points.Count; index++)
        {
            var each = points[index].As<TriggerPoint>();
            if (!map.triggers.Contains(each))
            {
                continue;
            }

            map.triggers.Remove(each);
            map.RemoveChild(each);
            each.Free();
        }
    }

    /// <summary>
    /// 三火盆顺序谜题（03 的七类隐藏内容最后一类，2026-10-03 深夜接上）：
    /// 点错 → 提示次序不对 + 进度重置 + 不发奖励；按 1 → 3 → 2 点齐 → 拿到前代寨主遗物并记进副本记录。
    /// </summary>
    private void _CheckSequencePuzzle(LocalMapController map, GodotObject state)
    {
        const string off = "res://assets/sprites/props/brazier_off.png";
        const string on = "res://assets/sprites/props/brazier_on.png";
        Godot.Collections.Array points = _BrazierFixture(map);
        CheckEq(points.Count, 3, "三火盆位点齐了（地图里 1 个 ＋ 用例夹具 2 个）");
        if (points.Count != 3)
        {
            return;
        }

        var first = points[0].As<TriggerPoint>();
        var second = points[1].As<TriggerPoint>();
        var third = points[2].As<TriggerPoint>();
        // 玩家得站在火盆跟前：`can_interact()` 里那条距离判定要跟真玩一致，
        // 不然「完成后不再可交互」会变成一条永远为真的假绿
        map.player.GlobalPosition = first.GlobalPosition;
        // 地编交付的两态贴图必须真的被用上（07 §8.3「火盆（灭／燃）」）——
        // 以前火盆只是个紫色多边形，仓库里的 brazier_off/on.png 一次都没被读过
        CheckEq(first.brazier_texture_path(), off, $"火盆用「灭」贴图起手（而不是占位多边形）：{first.brazier_texture_path()}");
        CheckTrue(first.requirement_text().Contains("1 → 3 → 2"), $"需求文案把次序写给玩家：{first.requirement_text()}");
        int equipsBefore = _inv(state).equipment_count();
        CheckFalse(
            _a(_record(state, map.scene_id), "triggers").Contains("trig_brazier"),
            "这段之前没记过这条隐藏（用例前提）");

        // ① 点错：先点第 2 格 → 拒绝、进度重置、不发奖励、不算触发过、视觉回灭
        map._set_sequence_progress("trig_brazier", new Godot.Collections.Array());
        Sfx.clear_log();
        Godot.Collections.Dictionary wrong = map._activate_trigger(second);
        CheckFalse(_b(wrong, "ok"), $"次序不对要拒绝：{_s(wrong, "summary")}");
        CheckFalse(Sfx.has_played("brazier"), "点错不点火（没有点燃音效）");
        CheckTrue(_status(map).Contains("次序不对"), $"提示写清是次序问题：{_status(map)}");
        CheckFalse(_status(map).Contains("trig_brazier"), $"提示不漏表内 id：{_status(map)}");
        CheckEq(_inv(state).equipment_count(), equipsBefore, "点错不发奖励");
        CheckEq(map._sequence_progress("trig_brazier").Count, 0, "点错进度重置");
        CheckFalse(second.used, "没点完不算触发过（点还亮着）");
        CheckEq(second.brazier_texture_path(), off, "点错后火盆回灭（视觉与进度一起重置）");

        // ② 按 1 → 3 点两格：每步都成功但不算完成，进度累加，点着的那两格各自亮起
        Sfx.clear_log();
        Godot.Collections.Dictionary firstStep = map._activate_trigger(first);
        CheckTrue(_b(firstStep, "ok"), $"第 1 格点对了：{_s(firstStep, "summary")}");
        CheckEq(map._sequence_progress("trig_brazier").Count, 1, "进度累加（1）");
        CheckEq(first.brazier_texture_path(), on, "点对的第 1 格亮起（灭 → 燃）");
        CheckEq(third.brazier_texture_path(), off, "还没点的第 3 格仍然是灭的（亮的是哪几格，玩家一眼看得清）");
        Godot.Collections.Dictionary thirdStep = map._activate_trigger(third);
        CheckTrue(_b(thirdStep, "ok"), $"第 3 格点对了：{_s(thirdStep, "summary")}");
        CheckFalse(_b(thirdStep, "complete"), "还没点完不算完成");
        CheckEq(map._sequence_progress("trig_brazier").Count, 2, "进度累加（2）");
        CheckEq(third.brazier_texture_path(), on, "点对的第 3 格也亮起");
        CheckFalse(first.used, "中途仍然不算触发过");
        CheckTrue(Sfx.has_played("brazier"), "点对一格就请求「火盆点燃」音效（07 §8.4）");

        // ③ 第三步点第 2 格 → 完成：发奖励（前代寨主遗物）＋记进副本触发记录
        Godot.Collections.Dictionary done = map._activate_trigger(second);
        CheckTrue(_b(done, "ok") && _b(done, "complete"), $"点齐 1-3-2 完成：{_s(done, "summary")}");
        CheckTrue(_status(map).Contains("前代寨主遗物"), $"发的是表里配的那件装备：{_status(map)}");
        CheckEq(_inv(state).equipment_count(), equipsBefore + 1, "拿到一件装备实例");
        CheckTrue(
            _a(_record(state, map.scene_id), "triggers").Contains("trig_brazier"),
            "记进副本触发记录（完成度要算它）");
        CheckEq(map._sequence_progress("trig_brazier").Count, 0, "完成后进度清空");

        // ④ 收口：同一行的**每一格**都要变成已用并保持燃着——只标最后点的那一格的话，
        //    玩家站在原地再按一遍 1-3-2 就能再领一件（`once_only` 只在**生成位点**时看永久记录）
        var allUsed = true;
        var stillInteractable = false;
        foreach (Variant item in points)
        {
            var each = item.As<TriggerPoint>();
            allUsed = allUsed && each.used;
            stillInteractable = stillInteractable || each.can_interact();
            CheckEq(each.brazier_texture_path(), on, "完成后的火盆保持燃着（不是压成灰）");
        }

        CheckTrue(allUsed, "谜题完成后每个火盆都收口（以前只有最后点的那格被标成已用）");
        CheckFalse(stillInteractable, "完成后火盆不再可交互（玩家路径被 `can_interact()` 挡住）");
        int equipsDone = _inv(state).equipment_count();
        Godot.Collections.Dictionary again = map._activate_trigger(first);
        CheckFalse(_b(again, "ok"), $"第二遍走底层调用也被拦住：{_s(again, "error")}");
        CheckEq(_inv(state).equipment_count(), equipsDone, "重复点不再发第二件「前代寨主遗物」");
        _TeardownBrazierFixture(map, points);
    }

    private void _CheckTriggers(LocalMapController map, GodotObject state, Godot.Collections.Array captures)
    {
        const string off = "res://assets/sprites/props/brazier_off.png";
        const string on = "res://assets/sprites/props/brazier_on.png";
        TriggerPoint wine = null;
        var winePoints = new Godot.Collections.Array();
        TriggerPoint dig = null;
        foreach (Variant entry in map.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_wine")
            {
                winePoints.Add(point);
                if (wine == null)
                {
                    wine = point;
                }
            }

            if (point.trigger_id == "trig_dig")
            {
                dig = point;
            }
        }

        CheckNotNull(wine, "酒葫芦触发点在");
        CheckEq(winePoints.Count, 2, "酒葫芦有两个位点（牢房深处 ＋ 后山地牢）——「一行多位点」这件事本身要钉住");
        // 触发点的**可见状态**要和状态一致（玩家靠这个判断「能不能点」）：
        // 可交互=紫色，做不了=灰色，已用过=暗色
        CheckEq(_body_color(wine.GetNode("Body")).ToHtml(), Color.FromHtml("b07cc6").ToHtml(), "可用触发点是紫色");
        TriggerPoint sequencePoint = null;
        foreach (Variant entry in map.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_brazier")
            {
                sequencePoint = point;
            }
        }

        CheckNotNull(sequencePoint, "三火盆触发位点在（缺位点未做的是「三个交互点」，不是这个点本身）");
        if (sequencePoint != null)
        {
            // 2026-10-03 深夜：`sequence` 类接上了（七类隐藏内容全部实现）——细节流程见
            // `_CheckSequencePuzzle`；这里只钉「它不再是被动灰色块」：
            // 火盆用的是地编交付的**两态贴图**（灭／燃），不再走那条按颜色区分的占位多边形
            CheckTrue(sequencePoint.supported(), "sequence 类现在支持了");
            // 这条跑在 `_CheckSequencePuzzle` 之后，那时它已经是「燃」了——所以只钉
            // 「用的是地编交付的那两张两态贴图之一」，具体灭/燃由上面那段逐格验
            CheckTrue(
                sequencePoint.brazier_texture_path() == off || sequencePoint.brazier_texture_path() == on,
                $"火盆位点用的是地编交付的两态贴图（不是被动灰块）：{sequencePoint.brazier_texture_path()}");
        }

        // 缺道具 → 提示需求，不触发
        map.player.GlobalPosition = wine.GlobalPosition;
        int before = captures.Count;
        Godot.Collections.Dictionary missing = map.interact();
        CheckFalse(_b(missing, "ok"), "没有酒葫芦时触发失败");
        CheckTrue(_status(map).Contains("酒葫芦"), "提示需要哪件道具（说中文名）");
        CheckFalse(_status(map).Contains("item_"), "需求提示不漏表 id");
        CheckEq(captures.Count, before, "缺道具不会开战");
        // 给道具 → 触发隐藏 Boss
        _inv(state).add_item(map.db, "item_wine_gourd", 1);
        Godot.Collections.Dictionary triggered = map.interact();
        CheckTrue(_b(triggered, "ok"), $"有酒葫芦后触发成功：{_status(map)}");
        CheckEq(map.save_service().last_reason, "触发隐藏内容", "触发隐藏内容后自动存档");
        CheckEq(captures.Count, before + 1, "隐藏 Boss 进入战斗");
        CheckEq(_body_color(wine.GetNode("Body")).ToHtml(), Color.FromHtml("3f3f3f").ToHtml(), "用过的触发点变暗色");
        if (captures.Count > before)
        {
            var encounter = captures[before].As<Encounter>();
            CheckTrue(encounter.team_name.Contains("醉刀客"), "打的是醉刀客");
            CheckEq(encounter.members, "en_hidden_drunk:1", "隐藏 Boss 用单只队伍");
        }

        // 隐藏点位同时是武学来源（skill_base.source_type=hidden, source_id=trig_wine）
        Godot.Collections.Array granted = _a(triggered, "granted");
        CheckEq(granted.Count, 3, "醉刀客点位带 3 部武学（醉步／酒中刀／醉意）");
        foreach (Variant item in granted)
        {
            Godot.Collections.Dictionary entry = item.AsGodotDictionary();
            CheckGt(
                _v(entry, "learned").AsStringArray().Length + _v(entry, "blocked").AsGodotArray().Count, 0,
                $"{_s(entry, "name")} 要么学会要么写明为什么没学会");
        }

        CheckTrue(
            _s(triggered, "summary").Contains("修习门槛") || _s(triggered, "summary").Contains("领悟"),
            $"触发文案里说明武学结果：{_s(triggered, "summary")}");
        // 一行两位点：用掉一个之后，**另一个也不该再能用**——不然隐藏 Boss 会在同一趟里再打一遍
        // （三火盆那次是同一个洞：完成时只标「最后点的那格」，见框架说明决策 237）
        if (winePoints.Count > 1)
        {
            var second = (winePoints[0].As<TriggerPoint>() == wine ? winePoints[1] : winePoints[0]).As<TriggerPoint>();
            // 先站到它跟前：`can_interact()` 里有距离判定，站远了这条断言会变成假绿（踩过）
            map.player.GlobalPosition = second.GlobalPosition;
            CheckFalse(second.can_interact(), "同一行的第二个位点跟着一起收口（玩家点不到）");
            CheckEq(_body_color(second.GetNode("Body")).ToHtml(), Color.FromHtml("3f3f3f").ToHtml(), "第二个位点视觉上也变暗了");
            Godot.Collections.Dictionary again = map._activate_trigger(second);
            CheckFalse(_b(again, "ok"), $"走到第二个位点不再触发：{_s(again, "error")}");
            CheckEq(captures.Count, before + 1, "隐藏 Boss 不会被同一趟的第二遍再打一次");
        }

        // 触发奖励的**数据错路径**同样不许把 id 甩给玩家（决策 242）
        var bogusRow = (HiddenTriggerRow)map.db.get_row("hidden_trigger", "trig_wine").Duplicate(true);
        bogusRow.reward_type = "room";
        bogusRow.reward_id = "hf_does_not_exist";
        string bogusText = map._grant_trigger_reward(bogusRow, "测试");
        CheckFalse(bogusText.Contains("hf_does_not_exist"), $"触发奖励写错房间时文案不漏 id：{bogusText}");
        CheckTrue(bogusText.Contains("不在本图"), $"触发奖励写错房间时文案说清是什么毛病：{bogusText}");

        // 「铁镐挖通」的位点在**塌陷山洞**（`trig_dig.scene_id=scene_cave`），不在黑风寨里。
        // 这条以前写成 `if dig != null:`——位点不在这张图，整段断言从来没跑过（假绿）。
        // 现在这里只钉「本图确实没有」，真流程在 `_CheckCaveShortcut` 里用塌陷山洞走一遍。
        CheckNull(dig, "黑风寨里没有 trig_dig 位点（它属于 scene_cave，见 _CheckCaveShortcut）");
    }

    /// <summary>
    /// 玩家可见文案：不许把表里的英文 id 甩到界面上（2026-10-03 换对象审计）。
    ///
    /// 起因：小地图状态栏以前会说「打开宝箱：item_health_pill ×2、eq_sword_01#1」「需要 item_pickaxe」
    /// 「要用 poison 击杀目标」「挖通！直接通到「hf3_dungeon」」——全是内部 id。
    /// 现在一律走 `TableDb.display_name()`，这个用例把四条路径都钉住（有名字、且不漏 id）。
    /// </summary>
    private void _CheckPlayerText(LocalMapController map, TableDb db, Node sessionNode)
    {
        // 1) 触发点需求：酒葫芦（不是 item_wine_gourd）——用本图真有的触发点，别再靠条件跳过
        TriggerPoint wine = null;
        foreach (Variant entry in map.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_wine")
            {
                wine = point;
            }
        }

        CheckNotNull(wine, "黑风寨有酒葫芦触发点");
        if (wine != null)
        {
            string need = wine.requirement_text();
            CheckTrue(need.Contains("酒葫芦"), $"需求文案说道具名：{need}");
            CheckFalse(need.Contains("item_"), $"需求文案不漏表 id：{need}");
        }

        // 2) 开箱／扫荡的掉落摘要：说名字，不带实例序号
        string summary = map._drops_summary(
            new Godot.Collections.Dictionary
            {
                {
                    "items", new Godot.Collections.Array
                    {
                        new Godot.Collections.Dictionary { { "item_id", "item_wine_gourd" }, { "qty", 2 } },
                    }
                },
                { "equipment", new Godot.Collections.Array { "eq_sword_01#1" } },
                { "money", 7 },
            },
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "item_id", "item_wine_gourd" } },
            });
        CheckTrue(summary.Contains("酒葫芦"), $"掉落摘要说道具名：{summary}");
        CheckTrue(summary.Contains("铁剑"), $"掉落摘要说装备名（实例 id 剥掉 #序号）：{summary}");
        CheckFalse(summary.Contains("item_"), $"掉落摘要不漏道具 id：{summary}");
        CheckFalse(summary.Contains("#"), $"掉落摘要不漏装备实例序号：{summary}");
        CheckTrue(summary.Contains("铜钱"), $"掉落摘要带铜钱：{summary}");

        // 3) 挖通捷径：说房间名「后山地牢」（不是 hf3_dungeon）
        Resource spaceRow = db.get_row("hidden_trigger", "trig_dig");
        string dugText = map._trigger_space(spaceRow);
        CheckTrue(dugText.Contains("后山地牢"), $"挖通文案说房间名：{dugText}");
        CheckFalse(dugText.Contains("hf3_dungeon"), $"挖通文案不漏房间 id：{dugText}");

        // 4) kill_style 判定说明：说「中毒」不说 poison
        sessionNode.Set("last_battle", new Godot.Collections.Dictionary());
        Resource poisonRow = db.get_row("hidden_trigger", "trig_poison_kill");
        string blocked = map._condition_block(poisonRow);
        CheckTrue(blocked.Contains("中毒"), $"毒杀判定说中文：{blocked}");
        CheckFalse(blocked.Contains("poison"), $"毒杀判定不漏条件值：{blocked}");
    }

    /// <summary>
    /// HUD 要告诉玩家「我在第几层、哪间房」：黑风寨是**三层同一张图**、19 间房，
    /// 不给这个信息玩家只能靠自己记路。数据都在 `dungeon_room`（floor + room_name）。
    /// </summary>
    private void _CheckFloorHud(LocalMapController map, TableDb db)
    {
        Node2D room = map.world.GetNodeOrNull<Node2D>("Rooms/Room_hf2_yard");
        CheckNotNull(room, "二层聚义厅前院在");
        if (room == null)
        {
            return;
        }

        Area2D bounds = room.GetNodeOrNull<Area2D>("bounds");
        if (bounds != null)
        {
            map.player.GlobalPosition = map._bounds_rect(bounds).GetCenter();
        }

        map._refresh_status();
        var roomRow = (DungeonRoomRow)db.get_row("dungeon_room", "hf2_yard");
        CheckTrue(_status(map).Contains("第 2 层"), $"HUD 报当前楼层：{_status(map)}");
        CheckTrue(_status(map).Contains(roomRow.room_name), $"HUD 报当前房间名（{roomRow.room_name}）：{_status(map)}");
    }

    /// <summary>
    /// 出生点与出口守卫（每张小地图都过一遍）：
    ///   ① 落点必须是地编在 `Characters/player_spawn` 里放的出生点（资产里的第一真相）；
    ///   ② 进图后头几帧不能被判定成「走到出口」而弹回大地图——清风驿的出生点就压在出口位点上，
    ///      所以出口必须「先走出去一次」才武装（`EXIT_ARM_MARGIN`），否则一进城就被弹回去。
    /// </summary>
    private void _CheckSpawnAndExitGuard(TableDb db, GodotObject state, Node sessionNode)
    {
        var checkedCount = 0;
        foreach (Variant entry in db.rows("map_local"))
        {
            GodotObject row = entry.AsGodotObject();
            var sid = row.Get("scene_id").AsString();
            if (!ResourceLoader.Exists($"res://scenes/maps/{sid}.tscn"))
            {
                continue;
            }

            checkedCount += 1;
            sessionNode.Set("pending_local_scene", sid);
            sessionNode.Set("pending_local_room", "");
            sessionNode.Set("local_position_scene", "");
            LocalMapController one = _load_local_run();
            one.state_override = state;
            var left = new Godot.Collections.Array();
            one.return_handler = Callable.From(() => left.Add(true));
            Tree.Root.AddChild(one);
            one.setup();
            Node2D authored = one.world.GetNodeOrNull<Node2D>("Characters/player_spawn");
            CheckNotNull(authored, $"{sid} 有地编放的 player_spawn 位点");
            // 出口也要看得见（`Exit_<scene_id>` 是看不见的坐标，走到它上面就回大地图）
            var exitLabel = one.FindChild("PlaceLabel_Exit", true, false) as Label;
            if (sid == "scene_ferry_locked")
            {
                // 07 文档：「渡口本章不开放可不做」——它既没有出口位点，也不该有出口标识
                CheckNull(exitLabel, "废弃渡口本章不开放，没有出口位点（07 已注明）");
            }
            else
            {
                CheckNotNull(exitLabel, $"{sid} 的出口有可见标识");
                if (exitLabel != null)
                {
                    CheckEq(exitLabel.Text, "出口", $"{sid} 出口标识写「出口」");
                }
            }

            if (authored != null)
            {
                CheckLt(
                    one.player.GlobalPosition.DistanceTo(authored.GlobalPosition), 1.0,
                    $"{sid} 从出生点进场：{one.player.GlobalPosition}");
            }

            // 真跑几帧：出生点压在出口上的图（清风驿）也不能被弹出去
            for (int i = 0; i < 3; i++)
            {
                one._Process(1.0 / 60.0);
            }

            CheckEq(left.Count, 0, $"{sid} 进场后不会被立刻弹回大地图");
            Tree.Root.RemoveChild(one);
            one.Free();
        }

        CheckGt(checkedCount, 3, $"小地图都过了一遍（{checkedCount} 张）");
    }

    /// <summary>
    /// 后山密道：塌陷山洞里用铁镐挖通 → 直通后山地牢（跨图捷径）
    ///
    /// 设计 03/07：「在塌陷山洞用铁镐继续往里挖 → 通往山寨后山的捷径」，07 还专门要求
    /// 塌陷山洞放一个 `Portal_scene_heifengzhai` 位点。以前的实现只会在**本图**找目标房间，
    /// 而目标房间 `hf3_dungeon` 在黑风寨那张图里——挖通等于没用（而且旧用例静默跳过，看不出来）。
    /// </summary>
    private void _CheckCaveShortcut(TableDb db, GodotObject state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_cave");
        sessionNode.Set("pending_local_room", "");
        sessionNode.Set("local_position_scene", "");
        sessionNode.Get("local_maps").AsGodotDictionary().Clear();
        LocalMapController cave = _load_local_run();
        cave.state_override = state;
        var switches = new Godot.Collections.Array();
        cave.portal_switch_handler = Callable.From<string, string>((targetScene, roomId) =>
            switches.Add(new Godot.Collections.Array { targetScene, roomId }));
        Tree.Root.AddChild(cave);
        cave.setup();

        TriggerPoint dig = null;
        foreach (Variant entry in cave.triggers)
        {
            var point = entry.As<TriggerPoint>();
            if (point.trigger_id == "trig_dig")
            {
                dig = point;
            }
        }

        CheckNotNull(dig, "塌陷山洞里有 trig_dig 位点");
        CheckEq(cave.scene_id, "scene_cave", "在塌陷山洞这张图里");
        CheckNotNull(cave.world.GetNodeOrNull<Node2D>("Markers/Portal_scene_heifengzhai"), "有 Portal_scene_heifengzhai 位点");
        var roomRow = (DungeonRoomRow)db.get_row("dungeon_room", "hf3_dungeon");
        CheckTrue(
            roomRow != null && roomRow.scene_id == "scene_heifengzhai",
            "目标房间 hf3_dungeon 在黑风寨那张图里（所以要跨图）");
        if (dig != null)
        {
            // ① 干净档（没有铁镐）→ 只给需求提示，不切图
            //    （不能拿本用例的 state 来试：前面开过柴房宝箱，按设计 07 那口铜箱里就有铁镐）
            cave.state_override = SoloState(db);
            cave.player.GlobalPosition = dig.GlobalPosition;
            Godot.Collections.Dictionary blocked = cave.interact();
            CheckFalse(_b(blocked, "ok"), $"没铁镐挖不动：{_status(cave)}");
            CheckTrue(_status(cave).Contains("铁镐"), $"提示需要铁镐（不是 item_pickaxe）：{_status(cave)}");
            CheckFalse(_status(cave).Contains("item_"), "需求提示不漏表 id");
            CheckEq(switches.Count, 0, "挖不动时不会切图");
            // ② 有铁镐 → 记下目标图与落点，再重进小地图
            cave.state_override = state;
            _inv(state).add_item(db, "item_pickaxe", 1);
            cave.player.GlobalPosition = dig.GlobalPosition;
            Godot.Collections.Dictionary dug = cave.interact();
            CheckTrue(_b(dug, "ok"), $"用铁镐挖通成功：{_status(cave)}");
            CheckEq(switches.Count, 1, "挖通触发切图");
            if (switches.Count == 1)
            {
                Godot.Collections.Array pair = switches[0].AsGodotArray();
                CheckEq(pair[0].AsString(), "scene_heifengzhai", "切到黑风寨那张图");
                CheckEq(pair[1].AsString(), "hf3_dungeon", "落点是后山地牢");
            }

            CheckEq(sessionNode.Get("pending_local_scene").AsString(), "scene_heifengzhai", "pending_local_scene 写好了");
            CheckEq(sessionNode.Get("pending_local_room").AsString(), "hf3_dungeon", "pending_local_room 写好了");
            CheckTrue(_status(cave).Contains("后山地牢"), $"文案说房间名：{_status(cave)}");
            CheckFalse(_status(cave).Contains("hf3_dungeon"), "文案不漏房间 id");
        }

        Tree.Root.RemoveChild(cave);
        cave.Free();

        // 模拟「重进小地图」：新开一份 local_run，玩家应该站在后山地牢而不是黑风寨入口
        LocalMapController reentered = _load_local_run();
        reentered.state_override = state;
        var again = new Godot.Collections.Array();
        reentered.portal_switch_handler = Callable.From<string, string>((t, r) =>
            again.Add(new Godot.Collections.Array { t, r }));
        Tree.Root.AddChild(reentered);
        reentered.setup();
        CheckEq(reentered.scene_id, "scene_heifengzhai", "重进的是黑风寨");
        Node2D dungeonRoom = reentered.world.GetNodeOrNull<Node2D>("Rooms/Room_hf3_dungeon");
        CheckNotNull(dungeonRoom, "后山地牢房间还在");
        // 落点是房间 bounds 矩形的中心（房间节点位置是矩形左上角，站在角上会被墙推出去、
        // current_room_id() 也判不到这个房间），所以这里钉「人在不在这个房间里」
        CheckEq(reentered.current_room_id(), "hf3_dungeon", "挖通后重进图，人就站在后山地牢里");
        var center = Vector2.Zero;
        Area2D bounds = dungeonRoom.GetNodeOrNull<Area2D>("bounds");
        if (bounds != null)
        {
            center = reentered._bounds_rect(bounds).GetCenter();
        }

        CheckLt(reentered.player.Position.DistanceTo(center), 40.0, "落点在房间中心附近（允许物理把 10px 宽的人推出几像素）");
        CheckEq(sessionNode.Get("pending_local_room").AsString(), "", "落点请求用掉就清空（不会一直粘着）");
        Tree.Root.RemoveChild(reentered);
        reentered.Free();
    }

    /// <summary>打完一场回小地图：接着站在原来的位置，而且不会被同一队立刻再抓一次。</summary>
    private void _CheckBattleReturnPosition(TableDb db, GodotObject state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_heifengzhai");
        sessionNode.Set("pending_local_room", "");
        sessionNode.Get("local_maps").AsGodotDictionary().Clear();
        // 挑一个有房间敌人的房间，把「战斗前位置」写在它身上（模拟逃回来的那一瞬间）
        DungeonRoomRow roomRow = null;
        foreach (Variant entry in db.rows("dungeon_room"))
        {
            var row = (DungeonRoomRow)entry.AsGodotObject();
            if (row.scene_id == "scene_heifengzhai" && row.enemy_team.Length > 0)
            {
                roomRow = row;
                break;
            }
        }

        CheckNotNull(roomRow, "黑风寨里有带敌人的房间");
        if (roomRow == null)
        {
            return;
        }

        LocalMapController second = _load_local_run();
        second.state_override = state;
        Tree.Root.AddChild(second);
        second.setup();
        RoamingEnemy team = null;
        foreach (Variant entry in second.teams)
        {
            var enemy = entry.As<RoamingEnemy>();
            if (enemy.spawn_id == roomRow.room_id)
            {
                team = enemy;
            }
        }

        CheckNotNull(team, $"房间敌人生成在 {roomRow.room_id}");
        if (team != null)
        {
            CheckFalse(team.defeated, "这队还没被打掉（逃跑的场景）");
            // 把「战斗前位置」正好写在这队身上：重进图应该被推离接触圈，而不是原地再被抓
            sessionNode.Set("local_position", team.GlobalPosition);
            sessionNode.Set("local_position_scene", "scene_heifengzhai");
            LocalMapController back = _load_local_run();
            back.state_override = state;
            var reentered = new Godot.Collections.Array();
            back.battle_switch_handler = Callable.From<Variant>(encounter => reentered.Add(encounter));
            Tree.Root.AddChild(back);
            back.setup();
            float dist = back.player.Position.DistanceTo(team.GlobalPosition);
            CheckGt(
                dist, RoamingEnemy.contact_distance() + 20.0,
                $"回图时被推到接触圈外足够远（{dist:F0} px；推得少会被墙顶回圈里）");
            CheckTrue(_status(back).Contains("退开"), $"回图提示「先退开几步」：{_status(back)}");
            // 接触宽限期：落点被墙顶回圈里时，敌人这一下接触不该当场又开一场
            CheckGt(back._contact_grace, 0.0, "推过就给接触宽限期");
            back._on_encountered(roomRow.room_id, "front");
            CheckEq(reentered.Count, 0, "宽限期内忽略接触（不会被锁在败仗里）");
            back._contact_grace = 0.0f;
            back._on_encountered(roomRow.room_id, "front");
            CheckEq(reentered.Count, 1, "宽限期过去后照常开战");
            // 位置在别处时照样接着站（不再被弹回入口）
            var hallway = new Vector2(1234.0f, 567.0f);
            sessionNode.Set("local_position", hallway);
            sessionNode.Set("local_position_scene", "scene_heifengzhai");
            LocalMapController resume = _load_local_run();
            resume.state_override = state;
            Tree.Root.AddChild(resume);
            resume.setup();
            CheckLt(resume.player.Position.DistanceTo(hallway), 1.0, "打完回图接着站在原处");
            Tree.Root.RemoveChild(resume);
            resume.Free();
            Tree.Root.RemoveChild(back);
            back.Free();
        }

        Tree.Root.RemoveChild(second);
        second.Free();
    }

    /// <summary>面板上那一排按钮／文案的容器（`npc_panel.gd` 的 `_actions`）。</summary>
    private static Node _actions(Node panel) => panel.Get("_actions").AsGodotObject() as Node;

    /// <summary>城镇设施：客栈按 E 进打坐界面；当铺与悬赏板按 07 文档是无表信息源，给对话线索。</summary>
    private void _CheckTownFacilities(TableDb db, GodotObject state, Node sessionNode)
    {
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        Tree.Root.AddChild(town);
        town.setup();
        Node2D inn = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_inn");
        CheckNotNull(inn, "地图上有客栈位点");
        // 城镇没有楼层/房间，HUD 不该硬凑「第 0 层」这种东西
        town._refresh_status();
        CheckFalse(_status(town).Contains("第 0 层"), $"城镇不硬凑楼层：{_status(town)}");
        CheckFalse(_status(town).Contains("·"), $"城镇不显示房间段：{_status(town)}");
        // 城镇的店与设施必须有**看得见的名字**：场景里它们都是看不见的 Marker2D，
        // 不标名字玩家只能挨个走过去按 E 试（这是真事——巡游时我自己都分不清哪栋是哪家）。
        var shopLabel = town.FindChild("PlaceLabel_bld_grocery", true, false) as Label;
        CheckNotNull(shopLabel, "杂货铺有位点标识");
        if (shopLabel != null)
        {
            CheckEq(shopLabel.Text, "杂货铺", "标识写的是店名（取自 building_def.name_cn）");
        }

        var innLabel = town.FindChild("PlaceLabel_facility_inn", true, false) as Label;
        CheckNotNull(innLabel, "客栈有位点标识");
        if (innLabel != null)
        {
            CheckEq(innLabel.Text, "客栈", "设施名写对");
        }

        CheckEq(
            (town.FindChild("PlaceLabel_bld_grocery", true, false) != null ? 1 : 0)
                + (town.FindChild("PlaceLabel_bld_tavern", true, false) != null ? 1 : 0)
                + (town.FindChild("PlaceLabel_bld_clinic", true, false) != null ? 1 : 0)
                + (town.FindChild("PlaceLabel_bld_smith", true, false) != null ? 1 : 0),
            4, "四家店都有可见标识");
        // 认不出的设施位点 = 地图／数据错：**不许把位点名印给玩家**（id 只进日志）
        var marker = new Marker2D { Name = "facility_does_not_exist" };
        Node facilities = town.world.GetNodeOrNull<Node>("Markers/Facilities");
        if (facilities != null)
        {
            facilities.AddChild(marker);
            marker.GlobalPosition = town.player.GlobalPosition;
            Godot.Collections.Dictionary unknown = town.interact();
            CheckFalse(_b(unknown, "ok"), "认不出的设施不假装能交互");
            CheckFalse(
                _status(town).Contains("facility_does_not_exist"),
                $"认不出设施时也不许把位点名甩给玩家：{_status(town)}");
            CheckTrue(_status(town).Contains("数据错"), $"如实说这是数据错：{_status(town)}");
            facilities.RemoveChild(marker);
            marker.Free();
        }

        if (inn == null)
        {
            Tree.Root.RemoveChild(town);
            town.Free();
            return;
        }

        town.player.GlobalPosition = inn.GlobalPosition + new Vector2(0, 12);
        CheckEq(town.facility_near_player(), "facility_inn", "站到客栈旁边能认出来");
        Godot.Collections.Dictionary opened = town.interact();
        CheckTrue(_b(opened, "ok"), $"按 E 进客栈：{_s(opened, "error")}");
        CheckNotNull(town.cultivate_panel, "打坐界面挂上了");
        if (town.cultivate_panel != null)
        {
            string cultivateStatus = town.cultivate_panel.Call("status_text").AsString();
            CheckTrue(cultivateStatus.Contains("打坐"), $"状态栏提示打坐：{cultivateStatus}");
        }

        town.close_cultivate();
        CheckTrue(town.cultivate_panel == null, "关掉后引用清空");

        // 当铺：07 文档写它是「无表（对话线索）」，要提到「寨里关着个疯子」（醉刀客的线索来源之一）
        Node2D pawn = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_pawnshop");
        CheckNotNull(pawn, "地图上有当铺位点");
        if (pawn != null)
        {
            town.player.GlobalPosition = pawn.GlobalPosition + new Vector2(0, 12);
            CheckEq(town.facility_near_player(), "facility_pawnshop", "站到当铺旁边能认出来");
            Godot.Collections.Dictionary notice = town.interact();
            CheckTrue(_b(notice, "ok"), $"当铺能读到对话线索：{notice}");
            CheckTrue(_status(town).Contains("疯子"), $"当铺老板提到寨里关着个疯子：{_status(town)}");
        }

        // 悬赏板：07 文档写它是「无表（信息源）」，第一章唯一引子——沈家小姐被掳
        Node2D board = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_bounty_board");
        CheckNotNull(board, "地图上有悬赏板位点");
        if (board != null)
        {
            town.player.GlobalPosition = board.GlobalPosition + new Vector2(0, 12);
            CheckEq(town.facility_near_player(), "facility_bounty_board", "站到悬赏板旁边能认出来");
            Godot.Collections.Dictionary posted = town.interact();
            CheckTrue(_b(posted, "ok"), $"悬赏板能读到告示：{posted}");
            CheckTrue(_status(town).Contains("沈家小姐"), $"悬赏板给出第一章引子：{_status(town)}");
            // 读告示板 = 点亮引导第一步的旗标（09 §3.1）：这一条把**旗标名字**钉住——
            // 控制器里的常量与 `guide_step.csv` 的 condition 是两处，改错一边引导就永远停在第 1 步。
            CheckTrue(_has_flag(state, "flag_board_read"), "读悬赏板点亮 flag_board_read");
            // 序幕·择念（20 §3.1／0.29.1 v2 第 4 条）：**与引子同框**——同一按把它摆出来，
            // 而且是**旁白模式**（不含头像／好感条／交往段：那句话是主角替自己说的）。
            CheckEq(_s(posted, "story"), "dl_opening_choice", $"同一按摆出序幕择念：{posted}");
            Node storyPanel = town.npc_panel;
            CheckNotNull(storyPanel, "择念用的是一块面板");
            if (storyPanel != null)
            {
                CheckEq(storyPanel.Get("mode").AsString(), "story", "走的是旁白模式（不是交往菜单）");
                CheckEq(storyPanel.Get("dialogue_node_id").AsString(), "dl_opening_choice", "说的就是那一问");
                var favorLabel = storyPanel.Get("_favor_label").As<Label>();
                CheckEq(favorLabel != null ? favorLabel.Text : "", "", "旁白模式不摆好感条");
                // 交往段整个收起来：面板上不该出现「好感／赠送／切磋／偷窃」这些字样
                var storyWords = "";
                foreach (Node node in _actions(storyPanel).GetChildren())
                {
                    if (node is Label label)
                    {
                        storyWords += label.Text;
                    }
                    else if (node is Button storyButton)
                    {
                        storyWords += storyButton.Text;
                    }
                }

                var hasFavorUi = false;
                foreach (string word in new[] { "好感", "赠送", "切磋", "偷窃" })
                {
                    if (storyWords.Contains(word))
                    {
                        hasFavorUi = true;
                    }
                }

                CheckFalse(hasFavorUi, $"旁白模式不出现交往字样：{storyWords}");
                CheckNotNull(
                    _actions(storyPanel).FindChild("TalkOptionopt_open_yi", true, false),
                    "三选一之一是真按钮");
                Godot.Collections.Dictionary picked = storyPanel.Call("choose_dialogue", "opt_open_yi").AsGodotDictionary();
                CheckTrue(_b(picked, "ok"), $"选得动：{_s(picked, "error")}");
                CheckTrue(_has_flag(state, "heart_yi"), "选完心性进存档");
                town.close_npc();
                // 选过之后同一按不再弹（条件在表里）
                Godot.Collections.Dictionary again = town.interact();
                CheckFalse(again.ContainsKey("story"), $"心性定过之后不再问（{again}）");
            }

            _flags(state).Remove("heart_yi");
        }

        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 政策：**每个「有名字的设施」都必须真的能交互**。
    ///
    /// `FACILITY_LABELS` 是一处清单（界面上给设施起名靠它），`interact()` 里的分支是另一处——
    /// 两边一错位，玩家在图上按 E 就会听到兜底那句「这里还没有可交互的内容（数据错，已记进日志）」，
    /// 而日志里还会真的记一条**假的数据错**。
    ///
    /// 2026-10-04 真事：0.31.0 新加的**书铺**（21 §九 陆文昭本命机遇的地点）进了 `FACILITY_LABELS`
    /// 与地图（地图验收也点了名），却没进 `interact()` 的分支——上面那条「认不出的设施」用例只造了一个
    /// 假位点，验的是「兜底那句在不在」，验不了「真位点是不是都进了分支」。所以这里改成**逐个真位点按一遍 E**。
    ///
    /// 用**独立的存档与场景实例**：客栈那一次会收人（燕小七），别把共用 state 的队伍改了。
    /// </summary>
    private void _CheckEveryFacilityIsInteractable(TableDb db, Node sessionNode)
    {
        GodotObject probe = SoloState(db);
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = probe;
        // 事件判定／触发点会触发自动存档——headless 下写 user:// 会崩，必须给临时目录（踩过）
        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        town.save_store_override = store;
        Tree.Root.AddChild(town);
        town.setup();
        var checkedCount = 0;
        foreach (Variant keyEntry in LocalMapController.FACILITY_LABELS.Keys)
        {
            var facilityId = keyEntry.AsString();
            string label = LocalMapController.FACILITY_LABELS[facilityId].AsString();
            Node2D marker = town.world.GetNodeOrNull<Node2D>($"Markers/Facilities/{facilityId}");
            CheckNotNull(marker, $"地图上有{label}位点（{facilityId}）");
            if (marker == null)
            {
                continue;
            }

            town.player.GlobalPosition = marker.GlobalPosition + new Vector2(0, 12);
            CheckEq(town.facility_near_player(), facilityId, $"站到{label}旁边能认出来");
            if (facilityId == "facility_bookshop")
            {
                // 21 §九 给书生的本命机遇定的地点就是**清风驿·书铺**，条件是「看过悬赏板」——
                // 那条旗标在**同一个城镇里**点亮；书铺这一按会顺手再判一次剧情节点（决策 345 的续），
                // 不然玩家读完板走到书铺会扑空（得当众出镇再进来才发）。
                _set_flag(probe, "flag_board_read");
            }

            Godot.Collections.Dictionary result = town.interact();
            checkedCount += 1;
            CheckNe(
                _s(result, "error"), "facility_unsupported",
                $"{facilityId}（{label}）按 E 走的是自己的分支，不是兜底那句：{result}");
            CheckTrue(_b(result, "ok"), $"{facilityId}（{label}）按 E 有反应：{result}");
            if (facilityId == "facility_bookshop")
            {
                CheckTrue(
                    _has_flag(probe, StoryService.done_flag("opp_scholar")),
                    "书生读过悬赏板之后，站到书铺按 E 就领到本命机遇");
                CheckTrue(_status(town).Contains("知白"), $"状态栏把《玄微心法·知白》念出来：{_status(town)}");
            }

            // 可能开了面板（客栈→打坐／招募，悬赏板→择念）：关掉再测下一个
            town.close_npc();
            town.close_cultivate();
            town.close_shop();
        }

        CheckEq(checkedCount, 4, "四个设施位点逐个按过 E（客栈／当铺／悬赏板／书铺）");
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 剧情招募在城镇里的触发点（09 §3.2）：**只有客栈**那一次交互收人。
    ///
    /// 这条口径是被本文件里那场赌局逼出来的：一开始「在清风驿任意位置按 E 就收人」，
    /// 结果赌局用例跑到时队伍已经从 1 人变 2 人、最高的运从 5 变 7，
    /// 软判定的「掷骰」分支变成「达标必过」，那条断言当场红。所以城镇必须钉到客栈。
    /// </summary>
    private void _CheckTownRecruitAtInn(TableDb db, Node sessionNode)
    {
        GodotObject state = SoloState(db);
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        Tree.Root.AddChild(town);
        town.setup();
        Node2D board = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_bounty_board");
        Node2D inn = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_inn");
        CheckNotNull(board, "悬赏板位点（招录用例）");
        CheckNotNull(inn, "客栈位点（招录用例）");
        // 条件旗标先点亮（等于玩家已经看过告示板）
        _set_flag(state, "flag_board_read");
        int before = state.Call("party_size").AsInt32();
        if (board != null)
        {
            town.player.GlobalPosition = board.GlobalPosition + new Vector2(0, 12);
            town.interact();
            CheckEq(state.Call("party_size").AsInt32(), before, "在悬赏板按 E 不收人（城镇只认客栈）");
            // 悬赏板那一按现在还会摆出序幕择念（20 §3.1）：关掉它再走下一步，
            // 免得把「客栈收人」这一步测在别的浮层开着的情况下
            town.close_npc();
        }

        if (inn != null)
        {
            town.player.GlobalPosition = inn.GlobalPosition + new Vector2(0, 12);
            Godot.Collections.Dictionary joined = town.interact();
            CheckEq(state.Call("party_size").AsInt32(), before + 1, "在客栈按 E 才把同伴收进来");
            CheckEq(_a(joined, "recruited").Count, 1, "返回里写清收的是谁");
        }

        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 城镇木桩的入口（设计 09 §3.3）：按 E 走到木桩那儿 → 到顶时给一句提示、不进战斗。
    ///
    /// 这里只验**不依赖木桩战斗数据**的那半边（到顶提示 + 认得出这个建筑）——「真开打」要等设计侧
    /// 补 `enemy_team`／`enemy_base` 两行（Q52），那半边由 `test_practice` 用内存夹具验。
    /// 位点是**测试时临时挂上去的** Marker：地编还没在清风驿校场摆 `bld_dummy`。
    /// </summary>
    private void _CheckDummyPracticeEntry(TableDb db, Node sessionNode)
    {
        GodotObject state = SoloState(db);
        string charId = state.Get("char_ids").AsStringArray()[0];
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        // 没到顶那次按 E 会**真组一场练习战**：必须像真场景那样把开战请求接住，
        // 否则控制器会走到 `_change_scene("battle_screen.tscn")`——在 C# 用例链里
        // 运行器节点就是当前场景，换场景会把运行器整个摘出场景树，
        // 后面所有用例当场判"没有注入场景树"（2026-10-11 实测：搬完大地图用例才暴露）。
        town.battle_switch_handler = Callable.From<Variant>(_ => { });
        Tree.Root.AddChild(town);
        town.setup();
        Node buildings = town.world.GetNodeOrNull<Node>("Markers/Buildings");
        CheckNotNull(buildings, "清风驿有建筑位点组（木桩入口用例）");
        if (buildings == null)
        {
            Tree.Root.RemoveChild(town);
            town.Free();
            sessionNode.Set("pending_local_scene", "");
            return;
        }

        var marker = new Marker2D { Name = "bld_dummy" };
        buildings.AddChild(marker);
        marker.GlobalPosition = town.player.GlobalPosition;
        CheckEq(town.building_near_player(), "bld_dummy", "站到木桩旁边认得出它");

        // ① 练到顶：给提示、不开战、不组遭遇
        // 前面的用例可能留下「待处理遭遇」——这里先清空，才能把「木桩没有组遭遇」验准
        sessionNode.Set("pending_encounter", default(Variant));
        Godot.Collections.Dictionary levels = state.Get("char_levels").AsGodotDictionary();
        levels[charId] = PracticeService.cap_level(db);
        Godot.Collections.Dictionary capped = town.interact();
        CheckFalse(_b(capped, "ok"), "到顶后按 E 不开战");
        CheckTrue(_b(capped, "capped"), "返回里写明是「到顶」");
        CheckEq(_status(town), PracticeService.cap_notice(db), "状态栏就是表里那句提示（不自己编）");
        CheckNull(sessionNode.Get("pending_encounter").AsGodotObject(), "到顶时没有组出遭遇");
        // ② 没到顶：**两种数据状态都要有诚实的结果**——要么真开打（且标成练习战），
        //    要么点名缺的是哪张表的行。写成这样是为了「设计补表那天不用改用例」
        //    （两边都断言会踩到自检的假绿门限：总有一边的断言跑不到）。
        levels[charId] = 1;
        Godot.Collections.Dictionary second = town.interact();
        bool started = _b(second, "ok");
        CheckTrue(started == _b(second, "practice"), $"开打了就一定是练习战：{second}");
        CheckTrue(
            started || _s(second, "error").Contains("enemy_"),
            $"没开打时必须点名缺的是哪张表的行：{_s(second, "error")}");
        CheckTrue(
            _status(town).Contains("还没准备好") || _status(town).Contains("木桩练习"),
            $"状态栏是两种诚实结果之一：{_status(town)}");

        buildings.RemoveChild(marker);
        marker.Free();
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    /// <summary>
    /// 宝箱守卫（设计 09 §一）：荒村废屋那个箱子被屠夫守着——
    /// 文取要一份草药汤（按两次 E：第一次让他开口、第二次付账），武取就是房里那场仗。
    /// 用**独立的存档与场景实例**跑，不动上面那份共用 state。
    /// </summary>
    private void _CheckGuardedChest(TableDb db, Node sessionNode)
    {
        GodotObject state = SoloState(db);
        sessionNode.Set("pending_local_scene", "scene_huangcun");
        LocalMapController map = _load_local_run();
        map.state_override = state;
        Tree.Root.AddChild(map);
        // 站在箱子边不该顺手把屠夫那支队伍也撞开——把切战斗拦下来
        map.battle_switch_handler = Callable.From<Variant>(_ => { });
        map.setup();
        Chest chest = null;
        foreach (Variant entry in map.chests)
        {
            var candidate = entry.As<Chest>();
            if (candidate.GetMeta("key").AsString().StartsWith("hc_02|"))
            {
                chest = candidate;
            }
        }

        CheckNotNull(chest, "荒村里有 hc_02 的宝箱");
        if (chest == null)
        {
            Tree.Root.RemoveChild(map);
            map.Free();
            sessionNode.Set("pending_local_scene", "");
            return;
        }

        CheckEq(chest.GetMeta("key").AsString(), "hc_02|drop_chest_silver", "认出来的就是屠夫守的那个箱子");
        map.player.GlobalPosition = chest.GlobalPosition;
        // 第一次按 E：只是让屠夫开口（不开箱、不扣东西），而且要把**两条路都摆出来**
        Godot.Collections.Dictionary first = map.interact();
        CheckFalse(_b(first, "ok"), "守卫挡着时开不了箱");
        CheckEq(_s(first, "guard"), "guard_hc_02", "返回里写清是哪位守卫");
        CheckEq(_s(first, "stage"), "demand", "第一次是「他开口」那一阶段");
        CheckTrue(_status(map).Contains("屠夫"), $"状态栏说清谁挡着：{_status(map)}");
        CheckTrue(_status(map).Contains("草药汤"), $"开口文案写了道具那条路：{_status(map)}");
        CheckTrue(_status(map).Contains("医术"), $"开口文案写了判定那条路：{_status(map)}");
        CheckFalse(chest.opened_already, "箱子没被打开");

        // 第二次按 E：书生的医术判定 = 3 + 智10/5 = 5 ≥ 门槛 3 → **判定路免费放行**；
        // 而且不该顺手收走背包里的草药汤（控制器优先走「不花东西」的那条，0.14.0）
        _inv(state).add_item(db, "item_med_01", 1);
        Godot.Collections.Dictionary paid = map.interact();
        CheckTrue(_b(paid, "ok"), $"判定过了就放行：{_s(paid, "error")}");
        CheckEq(_s(paid, "stage"), "peace", "第二阶段是「文取成功」");
        CheckEq(_inv(state).count("item_med_01"), 1, "走判定路不消耗草药汤");
        CheckTrue(_has_flag(state, "flag_guard_guard_hc_02_peace"), "文取解决写进存档（旗标）");
        Godot.Collections.Dictionary opened = map.interact();
        CheckTrue(_b(opened, "ok"), $"让开之后按 E 就能开箱：{_s(opened, "error")}");
        CheckTrue(chest.opened_already, "箱子这时才标成已开");

        // 武取那条路：另一份存档，直接把「房间已清」写进记录 → 箱子直接能开（不再走守卫流程）
        GodotObject fighter = SoloState(db);
        fighter.Call("record_dungeon", "scene_huangcun", "rooms", "hc_02");
        // 第一张图的会话已经把那个箱子记成「开过」了，这里要清掉本图的会话记录，
        // 否则第二张图生成的箱子一出生就是「已开」，压根没有可交互目标
        sessionNode.Get("local_maps").AsGodotDictionary().Remove("scene_huangcun");
        LocalMapController secondMap = _load_local_run();
        secondMap.state_override = fighter;
        Tree.Root.AddChild(secondMap);
        secondMap.battle_switch_handler = Callable.From<Variant>(_ => { });
        secondMap.setup();
        Chest secondChest = null;
        foreach (Variant entry in secondMap.chests)
        {
            var candidate = entry.As<Chest>();
            if (candidate.GetMeta("key").AsString().StartsWith("hc_02|"))
            {
                secondChest = candidate;
            }
        }

        CheckNotNull(secondChest, "第二份存档里也有那个箱子");
        if (secondChest != null)
        {
            secondMap.player.GlobalPosition = secondChest.GlobalPosition;
            Godot.Collections.Dictionary afterFight = secondMap.interact();
            CheckTrue(_b(afterFight, "ok"), $"打赢房间之后按 E 直接开箱：{_s(afterFight, "error")}");
        }

        Tree.Root.RemoveChild(secondMap);
        secondMap.Free();
        Tree.Root.RemoveChild(map);
        map.Free();
        sessionNode.Set("pending_local_scene", "");
    }

    private void _CheckExit(LocalMapController map, Node sessionNode, Godot.Collections.Array returns)
    {
        // 模拟「从大地图某处走进入口」：`enter_local_map()` 会把当时那一步的位置写进 world_position
        var entrySpot = new Vector2(744, 392);
        sessionNode.Set("world_position", entrySpot);
        map.leave_to_overworld();
        CheckEq(returns.Count, 1, "出口触发回大地图");
        CheckFalse(
            sessionNode.Get("local_maps").AsGodotDictionary().ContainsKey("scene_heifengzhai"),
            "回大地图后该图状态整片刷新");
        CheckEq(sessionNode.Get("pending_local_scene").AsString(), "", "清掉待进入的小地图");
        Resource localRow = map.db.get_row("map_local", "scene_heifengzhai");
        Resource region = map.db.get_row("map_region", localRow.Get("parent_node").AsString());
        // 设计 02：「返回大地图**原位置**」。原位置 = 进图那一步记下的坐标，
        // 不是地标坐标本身——不然回程会正好压在入口位点上，被自动进图条立刻送回图（死循环）。
        CheckEq(sessionNode.Get("world_position").AsVector2(), entrySpot, "回程落点是进图时记下的大地图原位置");
        CheckNe(
            sessionNode.Get("world_position").AsVector2(),
            new Vector2(region.Get("pos_x").AsSingle(), region.Get("pos_y").AsSingle()),
            "回程落点不该被改写成地标坐标");
    }

    /// <summary>
    /// 在店里把药买齐之后，HUD **当场**推进（决策 347）。
    ///
    /// 起因：`flag_supplies_ready` 的口径是「等级 ≥ 5 ＋ 背包里有消耗品」，而它原本只在**换图／换房间**时算。
    /// 玩家正站在药铺里把药买齐，HUD 却还停在「把家伙和药备齐」（09 §3.2 的循环就是「铁匠铺与酒楼把家伙和药备齐」），
    /// 要出镇再进来才推进——**同一张图里做完的事，判据却在场景边界上**（与决策 345／346 同一族）。
    /// </summary>
    private void _CheckGuideAfterShopClose(TableDb db, Node sessionNode)
    {
        GodotObject state = SoloState(db);
        sessionNode.Set("pending_local_scene", "scene_qingfengyi");
        LocalMapController town = _load_local_run();
        town.state_override = state;
        SaveStore store = SaveStore.create(SaveTestDir, 3);
        store.ensure_dir();
        town.save_store_override = store;
        Tree.Root.AddChild(town);
        town.setup();
        // 练到 5 级（打副本那种状态），但背包里还没有消耗品
        state.Get("char_levels").AsGodotDictionary()[state.Get("char_ids").AsStringArray()[0]] = 5;
        GuideService.refresh_derived_flags(db, state);
        CheckFalse(_has_flag(state, "flag_supplies_ready"), "等级够了但没药：还没备齐");
        // 在药铺里买齐 → 关掉浮层
        town.open_shop("bld_clinic");
        Godot.Collections.Dictionary bought = _inv(state).add_item(db, "item_potion_small", 1);
        CheckTrue(_b(bought, "ok"), $"买到一份金创药：{bought}");
        town.close_shop();
        CheckTrue(
            _has_flag(state, "flag_supplies_ready"),
            "关掉药铺浮层的那一下就把「备齐」算出来了（不用出镇再进来）");
        // 期望值**不抄文案**：0.32.0 把第 4 步从「出城上山」改成「出城往落雁坡走，从那儿上山。」
        // （落雁坡改成 `proximity_5` 后不点名玩家会摸不着北），抄死子串的写法当场过期成假红。
        // 改成问 `GuideService`「现在该显示哪一步」——状态是在关浮层那一刻才变的，label 若没跟着刷新
        // 就还是上一步的文案，这条照样红（不是同义反复）。
        string expectedHud = GuideService.hud_text(db, state);
        CheckTrue(
            town._guide_label.Text == expectedHud,
            $"HUD 当场换成下一步：{town._guide_label.Text}（表里现在该显示的是 {expectedHud}）");
        // 再钉一层，免得上面那条靠「两边都是空串」假绿：这一步必须真是「备齐之后」那一步。
        var suppliesText = "";
        foreach (Variant entry in GuideService.steps(db))
        {
            GodotObject row = entry.AsGodotObject();
            if (row.Get("condition").AsString() == "flag_supplies_ready")
            {
                suppliesText = row.Get("text_cn").AsString();
            }
        }

        CheckTrue(
            suppliesText.Length > 0 && expectedHud.Contains(suppliesText),
            $"关浮层那一下推进到「备齐之后」那一步（flag_supplies_ready → 「{suppliesText}」）");
        Tree.Root.RemoveChild(town);
        town.Free();
        sessionNode.Set("pending_local_scene", "");
    }
}
