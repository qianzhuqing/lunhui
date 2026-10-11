using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Lunhui.Autoload;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;
using Lunhui.World;

namespace Lunhui.Tools;

/// <summary>
/// 跨场景闭环自检的**跑手**（C# 版，对位旧链 `tools/check_loop.gd`）：在真引擎里跑**真实的场景切换**，
/// 逐条验证衔接处——大地图↔小地图、明雷战斗（胜／败两种结局）、驿站传送、房间战斗、
/// 副本巡游、存档读写、主菜单新建→小镇、隐藏 Boss、招募链、宝箱守卫、终局三选一。
///
/// **为什么单独有这一条**：用例大多注入 `handler` 拦下切场景（不然会把测试的树换掉），
/// 于是「切过去之后落点对不对、会话清没清、会不会立刻被弹回去」这类问题用例看不见。
/// 2026-10-02~03 连着挖出的四个真 bug 全在这一层（见 `docs/dev/decisions/` 决策 48／49／50）。
///
/// 通过标记 `LOOPCHECK: OK`（`tools/check_loop.bat` 按标记判定，不看退出码）。
/// 拆成两份 `partial` 的理由只有一条：单文件 800 行硬上限——这一份放**入口 ＋ 编排 ＋ 前六段**，
/// `CheckLoopRounds.cs` 放**后五段 ＋ 小工具**。
/// </summary>
public partial class CheckLoopRunner : Node
{
    private const string WorldScene = "res://scenes/world_run.tscn";
    private const string LocalRun = "res://scenes/local_run.tscn";
    private const string MenuScene = "res://scenes/main_menu.tscn";

    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        // 同 `RunTestsCs`：`_Ready` 里直接跑会撞「根节点正在装配子节点」，延后一帧。
        Callable.From(RunDeferred).CallDeferred();
    }

    private async void RunDeferred()
    {
        int code;
        try
        {
            code = await Run();
        }
        catch (Exception error)
        {
            // 诊断工具绝不能静默挂住：真出异常也要留标记 ＋ 非零退出码（run_bat 另有 --quit-after 兜底）。
            GD.PrintErr($"LOOPCHECK: FAIL 闭环自检中断：{error}");
            code = 1;
        }
        GetTree().Quit(code);
    }

    private async Task<int> Run()
    {
        var db = new TableDb();
        db.load_all();
        var session = GetTree().Root.GetNodeOrNull<GameSession>("GameSession");
        var state = GameState.new_game(db, "normal", Array.Empty<string>(), 0);
        session.set_state(state);
        session.world_position = Vector2.Zero;
        foreach (Variant charIdVariant in state.char_ids)
        {
            state.append_level(charIdVariant.AsString(), 19);
        }
        // 只留目标明雷：追击型／巡逻型会在回图那一瞬间发现玩家并立刻开第二场（那是它们该有的行为），
        // 会把「回图落点」这件事盖住，这里先只留一只。
        KeepOnly(db, session, "sp_lp_wolf_01");
        // 0.8.1：大地图明雷默认关着（`feature_toggle.overworld_roaming_enemy=0`），而这一条验的正是明雷机制
        // 本身——所以在闭环里**把开关打开**（设计原文："开关打开时，闭环里的「明雷战斗（胜／败两种结局）」
        // 那一整段照旧能跑"）。放会话上：大地图每次重建场景都从会话读，控制器上的字段活不过重建（踩过）。
        session.roaming_enabled_override = 1;

        bool ok = true;

        // ---------- ① 大地图 → 小地图 ----------
        GetTree().ChangeSceneToFile(WorldScene);
        await Frames(3);
        var world = GetTree().CurrentScene as OverworldController;
        if (world == null)
        {
            GD.Print("LOOPCHECK: FAIL 大地图没起来");
            return 1;
        }
        var region = (MapRegionRow)db.get_row("map_region", "n_heifengzhai");
        var portal = new Vector2((float)region.pos_x, (float)region.pos_y);
        // 0.32.0：**开局只有城镇与驿站亮着**，落雁坡（野外）要走到跟前才揭开；而黑风寨的入口条件
        // 正是 `discover_luoyanpo`——所以先按真实走位把主路走一趟（别绕过入口条件去开那扇门）。
        var slope = world.world.GetNodeOrNull<Node2D>("Markers/Node_n_luoyanpo");
        if (slope != null)
        {
            world.player.GlobalPosition = slope.GlobalPosition;
            await Frames(2);
        }
        // 地标名字：已揭开的才有（未揭开写出来就是剧透）——走完这一趟是清风驿／驿站／落雁坡／黑风寨四处
        int named = 0;
        foreach (Node label in world.FindChildren("NodeLabel_*", "Label", true, false))
        {
            if (label is CanvasItem item && item.Visible)
            {
                named++;
            }
        }
        // 模拟真实走位：先站在圈外（入口武装），再走进圈里。直接摆在入口上会被武装条挡住——
        // 那条正是防「回图落点压在入口上又立刻进图」的。
        world.player.GlobalPosition = portal + new Vector2(200, 0);
        await Frames(2);
        world.player.GlobalPosition = portal;
        await Frames(4);
        var local = GetTree().CurrentScene as LocalMapController;
        bool inLocal = local != null;
        ok = ok && inLocal;
        _lines.Add(
            $"大地图→小地图：切过去了={_bool(inLocal)}（图={(inLocal ? local.scene_id : "-")} "
            + $"宝箱={(inLocal ? local.chests.Count : -1)} 触发={(inLocal ? local.triggers.Count : -1)} 地标名={named}）"
        );
        ok = ok && named >= 4;
        if (!inLocal)
        {
            GD.Print("LOOPCHECK: FAIL 进不了小地图");
            return 1;
        }

        // ---------- ② 小地图 → 回大地图（原位置，且不会被立刻送回） ----------
        var entrySpot = session.world_position;
        local.leave_to_overworld();
        await Frames(4);
        var backWorld = GetTree().CurrentScene as OverworldController;
        bool backIsWorld = backWorld != null;
        var backPos = backIsWorld ? backWorld.player.GlobalPosition : Vector2.Inf;
        bool clearedState = !session.local_maps.ContainsKey("scene_heifengzhai");
        bool backOk = backIsWorld && backPos.DistanceTo(entrySpot) <= 1.0f && clearedState;
        ok = ok && backOk;
        _lines.Add(
            $"小地图→大地图：回去了={_bool(backIsWorld)} "
            + $"落点=原位置={_bool(backIsWorld && backPos.DistanceTo(entrySpot) <= 1.0f)} 会话已清={_bool(clearedState)}"
        );
        // 站在入口上再跑几帧也不该被自动送回小地图（这就是那个死循环）
        await Frames(6);
        var stillScene = GetTree().CurrentScene;
        bool stillWorld = stillScene != null && stillScene.Name == "WorldRun";
        ok = ok && stillWorld;
        _lines.Add($"站回入口再跑 6 帧：还留在大地图={_bool(stillWorld)}");

        // ---------- ③ 明雷战斗 → 回大地图（胜负两种结局都验） ----------
        ok = ok && await BattleRound(session, "sp_lp_wolf_01", false);
        // ③b 同一条明雷**再打一次**：把队伍打残（1 级 ＋ 1 点气血），逼出"没赢"的结局。
        // 它不算新的段标记（属于 ③ 那一"段"：同一只明雷、胜负两种结局都验）。为什么单开一次：
        // ③ 那句"胜负两种结局都验"以前是**假的**——循环把全队 +19 级，`winner == "ally"` 恒成立，
        // `if winner != "ally"` 那条分支（明雷不清 ＋ 人被推离接触圈）**写了但从没跑过**（决策 206）。
        // 顺便给这一场准备「最近到过的城镇」与一条战斗外增益：8.0 的战败处理要回城镇、清增益、回满气血。
        session.note_shelter("scene_qingfengyi", "清风驿");
        session.add_field_buff("buff_meditated", 10, "", 1);
        ok = ok && await BattleRound(session, "sp_lp_wolf_01", true);

        // ---------- ④ 驿站传送 → 进图 → 从目的地回图 ----------
        ok = ok && await WaypointRound(db, state, session);

        // ---------- ⑤ 房间战斗 → 回小地图 ----------
        ok = ok && await RoomBattleRound(state);

        // ---------- ⑥ 副本巡游：开箱 → 完成度 → 可扫荡 ----------
        ok = ok && DungeonRound(state);

        // ---------- ⑦ 存档 → 读档（真文件，走 SaveStore 的实际读写路径） ----------
        ok = ok && SaveRound(state, db);

        // ---------- ⑧ 主菜单新建游戏 →（第二局）小镇：进店／打坐／手动存档读档 ----------
        ok = ok && await MenuAndTownRound(db);

        // ---------- ⑨ 酒葫芦 → 隐藏 Boss → 回小地图（跨场景交接） ----------
        // 这一段跑在 ⑧ 新建的那一局上。为什么单独有这一段：这条链以前只有**两半**用例
        // （用例验「触发器建出正确的 encounter」与「结算把紫名 Boss 记下来」，而后者那场是**手工**建的
        // encounter）。中间「触发器的 encounter 过 GameSession 到战斗场景、打完再回图」没人跑过——
        // 而跨场景交接正是历史上最爱出错的一层。
        ok = ok && await WineBossRound(db, session);

        // ---------- ⑩ 招募链：读告示板 → 出图 → 回来在客栈入队 → 存档往返仍是 2 人 ----------
        // 为什么单独有这一段：招募是**跨场景 ＋ 存档**的行为——条件旗标在**这一次**进图点亮、
        // 人在**下一次**进图入队。两半用例各验一半，中间「出图再回来」这一步没人跑过。
        ok = ok && await RecruitRound(db, session);

        // ---------- ⑪ 宝箱守卫：荒村屠夫挡箱 → 文取 → 开箱 → 存档往返不复活 ----------
        ok = ok && await GuardRound(db, session);

        // ---------- ⑫ 幕五对质 → 账册 → 终局三选一（跨场景：战斗场景写、小地图读） ----------
        // 为什么单独有这一段：这条链的**交接面**是「战斗结算（写 `last_battle`）→ 回到小地图
        // （`setup()` 里按它摆出择念）」。两半用例各验一半，中间那一跳没人跑过。
        ok = ok && await LedgerRound(db, session);

        foreach (string line in _lines)
        {
            // 行首留 ASCII 标记：run_bat 用 findstr 抓 `LOOP`（中文模式 findstr 抓不到）
            GD.Print("LOOP " + line);
        }
        if (ok)
        {
            GD.Print("LOOPCHECK: OK");
            return 0;
        }
        GD.PrintErr("LOOPCHECK: FAIL");
        return 1;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    // ---------------------------------------------------------------- 段

    /// <summary>撞一只明雷 → 真打一场 → 回大地图 → 按结局检查「明雷清没清 ＋ 人落在哪」。</summary>
    private async Task<bool> BattleRound(GameSession session, string spawnId, bool crippled)
    {
        var state = (GameState)session.state;
        var savedLevels = new Godot.Collections.Dictionary();
        var savedHp = new Godot.Collections.Dictionary();
        if (crippled)
        {
            // 复活它（③ 赢了之后它被记成"已清"），再重进大地图让明雷按会话重建。
            session.cleared_spawns.Remove(spawnId);
            GetTree().ChangeSceneToFile(WorldScene);
            await Frames(4);
            // 打残：等级 1 ＋ 气血 1。等级只能加不能减（`append_level`），所以这里直接写 `char_levels`——
            // 这是诊断脚本，跟直接摆 `world_position` 一个性质；打完**原样还原**，别影响后面几段。
            foreach (Variant charIdVariant in state.char_ids)
            {
                string charId = charIdVariant.AsString();
                savedLevels[charId] = state.level_of(charId);
                savedHp[charId] = state.current_hp_of(charId);
                state.char_levels[charId] = 1;
                state.set_current_hp(charId, 1);
            }
        }
        var world = GetTree().CurrentScene as OverworldController;
        RoamingEnemy enemy = null;
        if (world != null)
        {
            foreach (Variant enemyVariant in world.enemies)
            {
                var candidate = enemyVariant.As<RoamingEnemy>();
                if (candidate.spawn_id == spawnId)
                {
                    enemy = candidate;
                }
            }
        }
        if (enemy == null)
        {
            _lines.Add($"{spawnId}：地图上找不到这只明雷");
            return false;
        }
        var spot = enemy.GlobalPosition;
        world.player.GlobalPosition = spot;
        await Frames(8);
        var battle = GetTree().CurrentScene as BattleScreen;
        if (battle == null)
        {
            _lines.Add($"{spawnId}：没进战斗场景");
            return false;
        }
        var result = AutoBattle(battle); // press_return 之后对象会被释放，结果先取出来
        battle.press_return();
        await Frames(8);
        var backScene = GetTree().CurrentScene;
        string defeatLine = "";
        // 战败处理（08 已定）：全队回到**最近到过的城镇**、气血回满、战斗外增益清空。
        // 这一局前面进过清风驿（`note_shelter`），所以回程应当是小地图里的那座城镇——
        // 验完再离开城镇回大地图，后面几段才接得上。
        if (crippled && result.Winner != "ally")
        {
            string shelter = session.last_shelter_scene;
            string shelterName = session.last_shelter_name;
            var town = backScene as LocalMapController;
            bool townReturn = town != null && town.scene_id == shelter;
            bool healed = true;
            foreach (Variant charIdVariant in state.char_ids)
            {
                if (state.is_wounded(charIdVariant.AsString()))
                {
                    healed = false;
                }
            }
            bool fieldCleared = session.active_field_buffs(-1).Count == 0;
            bool defeatOk = townReturn && healed && fieldCleared;
            defeatLine = $"战败回城：回{shelter}={_bool(townReturn)}（{shelterName}）气血回满={_bool(healed)} 战斗外增益已清={_bool(fieldCleared)}";
            if (!defeatOk)
            {
                _lines.Add(defeatLine);
                return false;
            }
            if (town != null)
            {
                town.leave_to_overworld();
                await Frames(6);
                backScene = GetTree().CurrentScene;
            }
        }
        var back = backScene as OverworldController;
        if (back == null)
        {
            string debugSpawn = session.pending_encounter.VariantType != Variant.Type.Nil
                ? session.pending_encounter.As<Encounter>().spawn_id
                : "";
            _lines.Add(
                $"{spawnId}：回程不是大地图（当前 {(backScene != null ? backScene.Name.ToString() : "无")}；"
                + $"pending_return={session.pending_return_scene} pending_local={session.pending_local_scene} "
                + $"已清记录={session.cleared_spawns.Count} 待处理遭遇={debugSpawn}）"
            );
            return false;
        }
        bool cleared = session.cleared_spawns.ContainsKey(spawnId);
        float dist = back.player.GlobalPosition.DistanceTo(spot);
        bool alive = false;
        foreach (Variant enemyVariant in back.enemies)
        {
            var candidate = enemyVariant.As<RoamingEnemy>();
            if (candidate.spawn_id == spawnId && !candidate.defeated && candidate.Visible)
            {
                alive = true;
            }
        }
        _lines.Add(
            $"明雷战斗（{spawnId}）：胜者={result.Winner} 回合={result.Rounds} → "
            + $"明雷已清={_bool(cleared)} 明雷还在={_bool(alive)} 回图距接触点={dist:F1}"
        );
        if (defeatLine.Length > 0)
        {
            _lines.Add(defeatLine);
        }
        foreach (Variant charIdKey in savedLevels.Keys)
        {
            string charId = charIdKey.AsString();
            state.char_levels[charId] = savedLevels[charIdKey];
            state.set_current_hp(charId, savedHp[charIdKey].AsInt32());
        }
        if (crippled && result.Winner == "ally")
        {
            // 这一段存在的意义就是"没赢"：真赢了说明打残没生效（或这只明雷太弱），当场说清，
            // 否则它会变成第二次"赢"、这条路径仍然是没跑过。
            _lines.Add($"{spawnId}：打残后仍然赢了——这一段没验到败北路径");
            return false;
        }
        if (result.Winner == "ally")
        {
            // 赢了：明雷记清、人就落在接触点（不用被推开）
            return cleared && !alive && dist <= 40.0f;
        }
        // 没赢（败北／撤退）：明雷不清，人被推离接触圈（否则回图瞬间再被抓）
        return !cleared && alive && dist > 22.0f;
    }

    /// <summary>
    /// 主菜单新建游戏 → 小镇：把整局游戏的头尾串上——主菜单真按钮新建、落盘，
    /// 然后进清风驿走一遍进店／打坐／手动存档／读档一致。前面几段都是「已有存档」的路径，
    /// 这一段从**没有存档**开始。
    /// </summary>
    private async Task<bool> MenuAndTownRound(TableDb db)
    {
        var store = SaveStore.create("res://.logs/loopcheck/saves", 3);
        store.ensure_dir();
        for (int slot = 1; slot <= 3; slot++)
        {
            store.delete_slot(slot);
        }
        GetTree().ChangeSceneToFile(MenuScene);
        await Frames(3);
        var menu = GetTree().CurrentScene as MainMenu;
        if (menu == null)
        {
            _lines.Add("主菜单没起来");
            return false;
        }
        menu.store_override = store;
        menu.controller = null; // 让 setup 用注入的 store 重建
        menu.setup();
        var created = menu.press_new_game();
        // 0.17.0 起「新建游戏」先过**创建角色界面**（创建没走完不写存档），
        // 所以这里要接着把创建界面走完——落盘发生在「确认创建」那一刻。
        bool wentCreation = menu.pending_scene == MainMenu.PLACEHOLDER_SCENE;
        GetTree().ChangeSceneToFile(MainMenu.CREATION_SCENE);
        await Frames(3);
        var creation = GetTree().CurrentScene as CreationScreen;
        bool creationOk = creation != null;
        if (creationOk)
        {
            creation.store_override = store;
            creation.setup();
            creation.pick_origin("ori_scholar");
            creationOk = Bool(creation.confirm(), "ok");
        }
        var session = GetTree().Root.GetNodeOrNull<GameSession>("GameSession");
        var state = session.state as GameState;
        bool newOk = Bool(created, "ok") && wentCreation && creationOk && state != null && store.slot_exists(1);
        _lines.Add($"主菜单新建游戏（经创建界面）：ok={_bool(creationOk)} 落盘={_bool(store.slot_exists(1))}");
        if (!newOk)
        {
            return false;
        }

        // 进清风驿（城镇）：进店 → 打坐 → 手动存档 → 读档一致
        session.pending_local_scene = "scene_qingfengyi";
        session.pending_local_room = "";
        session.local_position_scene = "";
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var town = GetTree().CurrentScene as LocalMapController;
        if (town == null)
        {
            _lines.Add("进不了清风驿");
            return false;
        }
        town.save_store_override = store;
        town._save_service = null;
        var grocery = town.world.GetNodeOrNull<Node2D>("Markers/Buildings/bld_grocery");
        bool shopOk = false;
        if (grocery != null)
        {
            town.player.GlobalPosition = grocery.GlobalPosition;
            var opened = town.interact();
            shopOk = Bool(opened, "ok") && town.shop_panel != null;
            town.close_shop();
        }
        var inn = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_inn");
        bool innOk = false;
        bool trained = false;
        bool meditated = false;
        var inventory = (Inventory)state.inventory;
        if (inn != null)
        {
            // 打坐要花钱，而新档是 0 文——诊断脚本直接给一笔（和直接摆 world_position 一个性质）
            inventory.money = Math.Max(inventory.money, 500);
            town.player.GlobalPosition = inn.GlobalPosition;
            var innOpened = town.interact();
            innOk = Bool(innOpened, "ok") && town.cultivate_panel != null;
            // **真打坐一次**（不只是开面板）：这样才验到「打坐 → 战斗外增益进存档」（v12）
            var trainButton = town.cultivate_panel.FindChild("TrainButton*", true, false) as Button;
            if (trainButton != null && !trainButton.Disabled)
            {
                trainButton.EmitSignal(Button.SignalName.Pressed);
                trained = true;
                meditated = state.field_buffs.Count > 0;
            }
            town.close_cultivate();
        }
        var saved = town.press_save();
        var loaded = store.load_slot(state.slot, db);
        var loadedState = loaded["state"].As<GameState>();
        bool loadedOk = Bool(loaded, "ok");
        bool sameMoney = loadedOk && loadedState != null && ((Inventory)loadedState.inventory).money == inventory.money;
        // 战斗外增益（v12）：打坐余韵要跟着手动存档一起进档、读回来还在
        bool fieldSaved = loadedOk && loadedState != null && meditated
            && loadedState.field_buffs.Count == state.field_buffs.Count;
        // 赌局位点：判定行只填了 region_id，位点却摆在小地图里——真按一次 E，确认它接上了。
        // 这条以前是**接不上**的（小地图收位点只认 scene_id），而这轮同时修了「赢钱当成背包物品」。
        bool gambleOk = false;
        string gambleText = "";
        var gamble = town.world.GetNodeOrNull<Node2D>("Markers/Event_ev_gamble");
        if (gamble != null)
        {
            town.player.GlobalPosition = gamble.GlobalPosition;
            string near = town.event_near_player();
            var rolled = near == "ev_gamble" ? town.resolve_event(near) : new Godot.Collections.Dictionary();
            gambleText = Str(rolled, "text");
            // 只钉两件真事：**位点接得上**（能按出文案）、**奖励没有落进背包**（钱的唯一口径是钱包）。
            // 原来还要求文案里有「掷骰」——那只在**软判定不足**时才出现，而队伍人数一变（1 → 4），
            // 判定值够高就直接成功、不再掷骰，这条断言于是跟"表里有几个角色"绑在一起了（决策 161）。
            gambleOk = near == "ev_gamble" && gambleText.Length > 0 && inventory.count("item_money") == 0;
        }
        // 城镇里的可见标识：4 家店 ＋ 3 个设施 ＋ 出口（场景里都是看不见的 Marker，全靠代码补名字）
        int placeLabels = town.FindChildren("PlaceLabel_*", "Label", true, false).Count;
        // 「余韵进档」印的是**读回来的存档里还有没有这条增益**（`fieldSaved`），不是内存里那个标记——
        // 第一版印错成 `meditated`，于是把 `to_dict` 里的 field_buffs 删掉时这行还显示 true
        // （检查本身是红的，只有文案骗人）。
        _lines.Add(
            $"清风驿：进店={_bool(shopOk)} 打坐面板={_bool(innOk)} 打坐={_bool(meditated)}（余韵进档={_bool(fieldSaved)}）"
            + $"手动存档={_bool(Bool(saved, "ok"))} 读档一致={_bool(sameMoney)} 可见标识={placeLabels} 赌局={_bool(gambleOk)}（{gambleText}）"
        );
        return shopOk && innOk && trained && meditated && fieldSaved
            && Bool(saved, "ok") && sameMoney && placeLabels >= 8 && gambleOk;
    }

    /// <summary>
    /// 副本巡游（接在第 ⑤ 段之后，人还在黑风寨里）：开箱 → 完成度计数 → 「可扫荡」提示。
    /// **注意**：这里是把玩家摆到箱子旁按 E（等于「走到了」），验的是**开箱→完成度→扫荡**这条接线；
    /// 火盆密室与门后暗格那两箱在真实玩法里还进不去（等三火盆位点），不是这条要管的事。
    /// </summary>
    private bool DungeonRound(GameState state)
    {
        var map = GetTree().CurrentScene as LocalMapController;
        if (map == null)
        {
            _lines.Add("副本巡游：当前不在小地图");
            return false;
        }
        var service = (DungeonService)map.dungeon_service();
        int before = service.progress("scene_heifengzhai")["percent"].AsInt32();
        int opened = 0;
        foreach (Variant chestVariant in map.chests)
        {
            var chest = chestVariant.As<Chest>();
            if (chest.opened_already)
            {
                continue;
            }
            map.player.GlobalPosition = chest.GlobalPosition;
            if (Bool(map.interact(), "ok"))
            {
                opened++;
            }
        }
        var snapshot = service.progress("scene_heifengzhai");
        var chests = snapshot["chests"].AsGodotDictionary();
        bool chestOk = chests["done"].AsInt32() == chests["total"].AsInt32() && chests["total"].AsInt32() == 4;
        bool sweepOk = map.progress_text().Contains("可扫荡");
        bool rose = snapshot["percent"].AsInt32() > before;
        // 顺带带上「我在第几层哪间房」——副本是三层同一张图，HUD 该报这个
        string floorText = map._floor_label();
        _lines.Add(
            $"副本巡游：所在={floorText} 开箱 {opened} 个 → 完成度 {before}%→{snapshot["percent"].AsInt32()}%"
            + $"（宝箱 {chests["done"].AsInt32()}/{chests["total"].AsInt32()}）可扫荡={_bool(sweepOk)}"
        );
        return opened >= 4 && chestOk && sweepOk && rose && floorText.Length > 0;
    }

    /// <summary>驿站传送 → 进图 → 从目的地回图：回程落点 ＝ **目的地地标**（不是驿站），且不会被立刻送回。</summary>
    private async Task<bool> WaypointRound(TableDb db, GameState state, GameSession session)
    {
        var world = GetTree().CurrentScene as OverworldController;
        if (world == null)
        {
            _lines.Add("驿站传送：当前不在大地图");
            return false;
        }
        var post = world.world.GetNodeOrNull<Node2D>("Markers/Node_n_post_station");
        if (post == null)
        {
            _lines.Add("驿站传送：地图上没有驿站位点");
            return false;
        }
        state.reveal_node("n_huangcun"); // 没揭开的地标不进传送列表
        world.player.GlobalPosition = post.GlobalPosition;
        var opened = world.open_waypoint();
        if (!Bool(opened, "ok") || world.waypoint_panel == null)
        {
            _lines.Add($"驿站传送：面板没打开（{Str(opened, "error")}）");
            return false;
        }
        var panel = (WaypointScreen)world.waypoint_panel;
        panel.press_travel("n_huangcun"); // 走真实的 travel_handler → enter_local_map(..., true)
        await Frames(8);
        var local = GetTree().CurrentScene as LocalMapController;
        bool targetOk = local != null && local.scene_id == "scene_huangcun";
        var region = (MapRegionRow)db.get_row("map_region", "n_huangcun");
        var expect = new Vector2((float)region.pos_x, (float)region.pos_y);
        bool spotOk = session.world_position.DistanceTo(expect) <= 1.0f;
        _lines.Add($"驿站传送（→荒村）：切过去了={_bool(targetOk)} 回程落点=目的地地标={_bool(spotOk)}");
        if (!(targetOk && spotOk))
        {
            return false;
        }
        // 从荒村出来：站在荒村门口（不是被拽回落雁坡的驿站），而且不会又被立刻送回
        local.leave_to_overworld();
        await Frames(8);
        var back = GetTree().CurrentScene as OverworldController;
        bool backOk = back != null;
        float dist = -1.0f;
        if (backOk)
        {
            dist = back.player.GlobalPosition.DistanceTo(expect);
            backOk = dist <= 1.0f;
        }
        _lines.Add($"驿站传送回程：回到大地图={_bool(backOk)} 距荒村地标={dist:F1}");
        return backOk;
    }

    /// <summary>房间战斗 → 回小地图：回的是**战斗前的位置**（不是入口），那间房按胜负记成已清／未清。</summary>
    private async Task<bool> RoomBattleRound(GameState state)
    {
        var world = GetTree().CurrentScene as OverworldController;
        if (world == null)
        {
            _lines.Add("房间战斗：当前不在大地图");
            return false;
        }
        world.enter_local_map("scene_heifengzhai");
        await Frames(6);
        var local = GetTree().CurrentScene as LocalMapController;
        if (local == null || local.teams.Count == 0)
        {
            _lines.Add("房间战斗：没进小地图或没有房间队伍");
            return false;
        }
        var team = local.teams[0].As<RoamingEnemy>();
        string roomId = team.spawn_id;
        var spot = team.GlobalPosition;
        local.player.GlobalPosition = spot;
        await Frames(6);
        var battle = GetTree().CurrentScene as BattleScreen;
        if (battle == null)
        {
            _lines.Add("房间战斗：没进战斗场景");
            return false;
        }
        var result = AutoBattle(battle);
        battle.press_return();
        await Frames(8);
        var back = GetTree().CurrentScene as LocalMapController;
        if (back == null)
        {
            var current = GetTree().CurrentScene;
            _lines.Add($"房间战斗：回程不是小地图（当前 {(current != null ? current.Name.ToString() : "无")}）");
            return false;
        }
        var cleared = Arr(state.dungeon_record("scene_heifengzhai"), "rooms");
        float dist = back.player.GlobalPosition.DistanceTo(spot);
        _lines.Add(
            $"房间战斗（{roomId}）：胜者={result.Winner} 回合={result.Rounds} → 回图={back.scene_id} "
            + $"距战斗前位置={dist:F1} 房间已清={_bool(cleared.Contains(roomId))}"
        );
        if (back.scene_id != "scene_heifengzhai")
        {
            return false;
        }
        if (result.Winner == "ally")
        {
            // 赢了：房间记清，人接着站在打之前的位置（不是被弹回入口）
            return cleared.Contains(roomId) && dist <= 40.0f;
        }
        // 没赢：房间不清，人被推离接触圈
        return !cleared.Contains(roomId) && dist > 22.0f;
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>自动战斗打到结束（按 AUTO_INTERVAL 的节奏 tick，返回胜者／回合／次数）。</summary>
    private static (string Winner, int Rounds, int Ticks) AutoBattle(BattleScreen battle)
    {
        battle.press_auto();
        int ticks = 0;
        var sim = (BattleSimulator)battle.sim;
        while (!sim.finished() && ticks < 400)
        {
            battle._Process(0.8);
            ticks++;
        }
        return (sim.winner(), sim.rounds_played(), ticks);
    }

    /// <summary>把其它明雷全部记成"已清"（`-1` ＝ 不参与刷新），只留 `keepId` 那一只。</summary>
    private static void KeepOnly(TableDb db, GameSession session, string keepId)
    {
        session.cleared_spawns.Clear();
        foreach (Variant rowVariant in db.rows("roaming_spawn"))
        {
            var row = rowVariant.As<RoamingSpawnRow>();
            if (row.spawn_id != keepId)
            {
                session.cleared_spawns[row.spawn_id] = -1;
            }
        }
    }

    /// <summary>`Dictionary.get(key, false)` 的对应物（缺键给 false，别把 Variant 的 nil 当真）。</summary>
    private static bool Bool(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) && source[key].AsBool();

    /// <summary>老链 `%s` 打布尔值走的是 GDScript 的 `str()`＝小写 `true`／`false`——C# 的
    /// `bool.ToString()` 是大写，直接插值会让两次运行的输出没法逐字比（同 `src/ui/` 那批 `_bool()`）。</summary>
    private static string _bool(bool value) => value ? "true" : "false";

    /// <summary>`str(dict.get(key, ""))` 的对应物。</summary>
    private static string Str(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) ? source[key].AsString() : "";

    /// <summary>`Array(dict.get(key, []))` 的对应物：缺键给空数组。</summary>
    private static Godot.Collections.Array Arr(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) ? source[key].AsGodotArray() : new Godot.Collections.Array();
}
