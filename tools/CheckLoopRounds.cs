using System.Threading.Tasks;
using Godot;
using Lunhui.Autoload;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;
using Lunhui.World;

namespace Lunhui.Tools;

/// <summary>
/// 跨场景闭环自检的**后五段 ＋ 小工具**（`CheckLoopRunner` 的另一半，拆自同一个类）。
///
/// 拆出来的理由只有一条：单文件 800 行硬上限。入口与编排（①–⑧ 段）留在 `CheckLoopRunner.cs`；
/// 这一份是 ⑨ 隐藏 Boss／⑩ 招募链／⑪ 宝箱守卫／⑫ 终局三选一，外加存档指纹。
/// </summary>
public partial class CheckLoopRunner
{
    /// <summary>
    /// 酒葫芦 → 隐藏 Boss（醉刀客）→ 回小地图：真跨场景走一遍，顺带验回图落点是战斗前站的位置。
    ///
    /// 用例只验了这条链的两半（触发器建 encounter／结算记紫名 Boss），中间那段交接没人跑过。
    /// </summary>
    private async Task<bool> WineBossRound(TableDb db, GameSession session)
    {
        var state = session.state as GameState;
        if (state == null)
        {
            _lines.Add("隐藏Boss：没有会话状态");
            return false;
        }
        session.pending_local_scene = "scene_heifengzhai";
        session.pending_local_room = "hf1_deep";
        session.local_position_scene = "";
        ((Inventory)state.inventory).add_item(db, "item_wine_gourd", 1);
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var map = GetTree().CurrentScene as LocalMapController;
        if (map == null)
        {
            _lines.Add("隐藏Boss：进不了黑风寨");
            return false;
        }
        TriggerPoint trigger = null;
        foreach (Variant triggerVariant in map.triggers)
        {
            var candidate = triggerVariant.As<TriggerPoint>();
            if (candidate.trigger_id == "trig_wine")
            {
                trigger = candidate;
            }
        }
        if (trigger == null)
        {
            _lines.Add("隐藏Boss：图里没有酒葫芦触发点");
            return false;
        }
        // 站到触发点上按 E（真交互）→ 控制器把 encounter 交给 GameSession 并切到战斗场景
        map.player.GlobalPosition = trigger.GlobalPosition;
        string spotRoom = map.current_room_id();
        map.interact();
        await Frames(8);
        var battle = GetTree().CurrentScene as BattleScreen;
        if (battle == null || battle.encounter == null)
        {
            _lines.Add("隐藏Boss：按 E 没进战斗");
            return false;
        }
        var encounter = (Encounter)battle.encounter;
        bool carried = encounter.source_scene == "scene_heifengzhai" && encounter.source_key == "trig_wine";
        // 把敌人打死再走真结算（和用例同一手法：验的是结算与记录，不是打得赢打不赢）
        foreach (Variant enemyVariant in battle.enemies)
        {
            enemyVariant.As<BattleActor>().hp = 0;
        }
        battle._settle();
        var bosses = Arr(state.dungeon_record("scene_heifengzhai"), "bosses");
        bool recorded = bosses.Contains("en_hidden_drunk");
        battle.press_return();
        await Frames(8);
        var back = GetTree().CurrentScene as LocalMapController;
        bool backOk = back != null;
        string backRoom = backOk ? back.current_room_id() : "-";
        bool kept = backOk && Arr(state.dungeon_record("scene_heifengzhai"), "bosses").Contains("en_hidden_drunk");
        _lines.Add(
            $"隐藏Boss（酒葫芦→醉刀客）：交接={_bool(carried)}（source_scene={encounter.source_scene} "
            + $"key={encounter.source_key}）记成Boss={_bool(recorded)} 回图={_bool(backOk)} 落点房间={backRoom}"
            + $"（战前 {spotRoom}）记录仍在={_bool(kept)}"
        );
        return carried && recorded && backOk && kept && backRoom == spotRoom;
    }

    /// <summary>
    /// ⑩ 招募链（设计 09 §3.2）：读告示板点亮条件 → **出图** → 再回来在**客栈**入队 → 存档往返仍 2 人。
    ///
    /// 这一段专门跑「跨场景」那一步：条件旗标是上一次进图点的，人是下一次进图收的
    /// （数据里 `recruit_def.join_scene=scene_qingfengyi`、条件 `flag_board_read`）。
    /// </summary>
    private async Task<bool> RecruitRound(TableDb db, GameSession session)
    {
        var state = session.state as GameState;
        if (state == null)
        {
            _lines.Add("招募链：没有会话状态");
            return false;
        }
        int beforeParty = state.party_size();
        bool stateBeforeBoard = state.has_flag("flag_board_read");

        // 第一次进清风驿：读悬赏板（引导第一步的目标、也是招募的条件）
        session.pending_local_scene = "scene_qingfengyi";
        session.pending_local_room = "";
        session.local_position_scene = "";
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var town = GetTree().CurrentScene as LocalMapController;
        if (town == null)
        {
            _lines.Add("招募链：进不了清风驿");
            return false;
        }
        var board = town.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_bounty_board");
        bool boardOk = false;
        string guideText = "";
        bool openingOk = false;
        bool openingOnce = false;
        // 序幕·择念（20 §3.1）：同一按摆出来、**真选一条**（面板走旁白模式、心性真的进档）。
        // 这里走的是**真场景 ＋ 真按键**：用例那一条是同一个控制器上的直调，
        // 而这一段验的是"跨场景之后它还认不认账"（第二次进图不该再问）。
        if (board != null)
        {
            town.player.GlobalPosition = board.GlobalPosition;
            var posted = town.interact();
            boardOk = Bool(posted, "ok") && state.has_flag("flag_board_read");
            guideText = GuideService.hud_text(db, state);
            var npc = town.npc_panel as NpcPanel;
            openingOk = Str(posted, "story") == "dl_opening_choice" && npc != null && npc.mode == "story";
            if (openingOk)
            {
                var picked = npc.choose_dialogue("opt_open_li");
                openingOk = Bool(picked, "ok") && state.has_flag("heart_li");
                town.close_npc();
            }
        }

        // 出图（回大地图），再进来——「条件在上一次进图点亮」这件事只有跨场景才验得到
        town.leave_to_overworld();
        await Frames(4);
        bool backToWorld = GetTree().CurrentScene is OverworldController;
        session.pending_local_scene = "scene_qingfengyi";
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var town2 = GetTree().CurrentScene as LocalMapController;
        bool joined = false;
        bool refusedElsewhere = false;
        if (town2 != null)
        {
            // 先回悬赏板再按一次：心性已经定过（选过一条），这句**不该再弹**
            var board2 = town2.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_bounty_board");
            if (board2 != null)
            {
                town2.player.GlobalPosition = board2.GlobalPosition;
                var postedAgain = town2.interact();
                openingOnce = !postedAgain.ContainsKey("story");
                town2.close_npc();
            }
            // 先站在**当铺**按一次 E：城镇只认客栈，这一步不该收人（09 §3.2 的口径）
            var pawnshop = town2.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_pawnshop");
            if (pawnshop != null)
            {
                town2.player.GlobalPosition = pawnshop.GlobalPosition;
                town2.interact();
                refusedElsewhere = state.party_size() == beforeParty;
            }
            var inn = town2.world.GetNodeOrNull<Node2D>("Markers/Facilities/facility_inn");
            if (inn != null)
            {
                town2.player.GlobalPosition = inn.GlobalPosition;
                var atInn = town2.interact();
                joined = Bool(atInn, "ok") && Arr(atInn, "recruited").Count >= 1;
            }
        }

        // 存档往返：入队结果（队伍 ＋ 旗标）要跟着存档走
        var store = SaveStore.create("res://.logs/loopcheck/saves", 3);
        var saved = store.save_slot(2, state);
        var loaded = store.load_slot(2, db);
        var loadedState = loaded["state"].As<GameState>();
        // 分母**不写死**：引导步数会随设计扩表（0.22.0 由 4 步扩到 6 步）。
        // 写死 2/4 的那版在扩表当天就假红——这里改成按表算，分母错了照样抓得住。
        string guideSecond = $"2/{GuideService.steps(db).Count}";
        bool persisted = Bool(loaded, "ok") && loadedState != null
            && loadedState.party_size() == state.party_size() && loadedState.has_flag("flag_ch_ci_joined");
        _lines.Add(
            $"招募链：读告示板={_bool(boardOk && !stateBeforeBoard)} 引导「{guideText}」｜"
            + $"序幕择念={_bool(openingOk)}（再进图不再问={_bool(openingOnce)}）｜出图回大地图={_bool(backToWorld)}｜"
            + $"当铺不收人={_bool(refusedElsewhere)}｜客栈入队={_bool(joined)}（{beforeParty}→{state.party_size()} 人）｜"
            + $"存档往返={_bool(persisted)}"
        );
        return boardOk && !stateBeforeBoard && guideText.Contains(guideSecond)
            && openingOk && openingOnce && backToWorld && refusedElsewhere && joined && persisted;
    }

    /// <summary>
    /// ⑫ 幕五对质 → 账册 → 终局三选一：**真打一场大寨主**、真回图、真选一条。
    ///
    /// 交接面：战斗结算把这一场的队伍 id 写进会话 → 回到小地图时 `setup()` 按 `TEAM_WIN_DIALOGUES`
    /// 翻出 `dl_ledger_choice` 摆成面板。所以这一段要看四件事：
    ///   ① 首杀打赢大寨主 → **账册真的进背包**（`TEAM_WIN_ITEMS`）；
    ///   ② 回图时面板**自动摆出来**、说的就是那条（条件：对质打过 ＋ 账册在手 ＋ 一条都没选过）；
    ///   ③ 选一条 → 旗标落地（这里选「呈官」），三样永久增益由此拿得到；
    ///   ④ 存档往返仍认账（这条决定第二章开场关系，不能只活在本局里）。
    /// </summary>
    private async Task<bool> LedgerRound(TableDb db, GameSession session)
    {
        var state = session.state as GameState;
        if (state == null)
        {
            _lines.Add("终局三选一：没有会话状态");
            return false;
        }
        session.pending_local_scene = "scene_heifengzhai";
        session.pending_local_room = "hf3_boss";
        session.local_position_scene = "";
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var map = GetTree().CurrentScene as LocalMapController;
        if (map == null)
        {
            _lines.Add("终局三选一：进不了黑风寨");
            return false;
        }
        // 大寨主那支队伍（房间敌人是接触开战，与明雷同一套）
        RoamingEnemy boss = null;
        foreach (Variant candidateVariant in map.teams)
        {
            var candidate = candidateVariant.As<RoamingEnemy>();
            // `team_row` 的类型随来源不同（明雷是 `RoamingSpawnRow`、房间队伍是 `EnemyTeamRow`），
            // 老链本来就是鸭子类型取 `team_id`——这里照旧按属性名取，别把它钉死成某一个行类。
            string teamRowId = candidate.team_row != null ? candidate.team_row.Get("team_id").AsString() : "";
            if (candidate.spawn_id == "team_boss" || teamRowId == "team_boss")
            {
                boss = candidate;
                break;
            }
        }
        if (boss == null)
        {
            _lines.Add("终局三选一：图里没有大寨主那支队伍");
            return false;
        }
        map.player.GlobalPosition = boss.GlobalPosition;
        await Frames(8);
        var battle = GetTree().CurrentScene as BattleScreen;
        if (battle == null || battle.encounter == null)
        {
            _lines.Add("终局三选一：接触没进战斗");
            return false;
        }
        string teamId = ((Encounter)battle.encounter).team_id;
        // 打死再走真结算（验的是结算与交接，不是打得赢打不赢）
        foreach (Variant enemyVariant in battle.enemies)
        {
            enemyVariant.As<BattleActor>().hp = 0;
        }
        battle._settle();
        bool gotLedger = ((Inventory)state.inventory).has("item_bd_ledger", 1);
        battle.press_return();
        await Frames(8);
        var back = GetTree().CurrentScene as LocalMapController;
        bool backOk = back != null;
        var panel = backOk ? back.npc_panel as NpcPanel : null;
        bool panelOk = panel != null && panel.dialogue_node_id == "dl_ledger_choice";
        bool flagBefore = state.has_flag("flag_ledger_public");
        bool chosenOk = false;
        if (panelOk)
        {
            var picked = panel.choose_dialogue("opt_ledger_public");
            chosenOk = Bool(picked, "ok") && state.has_flag("flag_ledger_public");
            back.close_npc();
        }
        // 存档往返：这条旗标决定第二章开场，必须跟着存档走
        var store = SaveStore.create("res://.logs/loopcheck/saves", 3);
        store.save_slot(2, state);
        var loaded = store.load_slot(2, db);
        var loadedState = loaded["state"].As<GameState>();
        bool persisted = Bool(loaded, "ok") && loadedState != null
            && loadedState.has_flag("flag_ledger_public")
            && ((Inventory)loadedState.inventory).has("item_bd_ledger", 1);
        _lines.Add(
            $"终局三选一（打赢大寨主→账册→三选一）：队伍={teamId} 拿到账册={_bool(gotLedger)} 回图={_bool(backOk)} "
            + $"摆出面板={_bool(panelOk)} 选「呈官」={_bool(chosenOk)}（选前={_bool(flagBefore)}）存档往返={_bool(persisted)}"
        );
        return teamId == "team_boss" && gotLedger && backOk && panelOk
            && chosenOk && !flagBefore && persisted;
    }

    /// <summary>
    /// ⑪ 宝箱守卫（设计 09 §一）：荒村 `hc_02` 的银箱被屠夫守着——
    /// 第一次按 E 只是让他开口、第二次付账、第三次才开箱；
    /// 存档往返之后「文取已解决」要跟着走（下次进图屠夫不再挡）。
    /// </summary>
    private async Task<bool> GuardRound(TableDb db, GameSession session)
    {
        var state = session.state as GameState;
        if (state == null)
        {
            _lines.Add("宝箱守卫：没有会话状态");
            return false;
        }
        const string guardId = "guard_hc_02";
        bool peaceAlready = state.has_flag(GuardService.peace_flag(guardId));
        var inventory = (Inventory)state.inventory;
        inventory.add_item(db, "item_med_01", 1);
        session.pending_local_scene = "scene_huangcun";
        session.pending_local_room = "";
        session.local_position_scene = "";
        GetTree().ChangeSceneToFile(LocalRun);
        await Frames(6);
        var map = GetTree().CurrentScene as LocalMapController;
        if (map == null)
        {
            _lines.Add("宝箱守卫：进不了荒村");
            return false;
        }
        Chest chest = null;
        foreach (Variant chestVariant in map.chests)
        {
            var candidate = chestVariant.As<Chest>();
            if (candidate.GetMeta("key", "").AsString() == "hc_02|drop_chest_silver")
            {
                chest = candidate;
            }
        }
        if (chest == null)
        {
            _lines.Add("宝箱守卫：荒村里没找到 hc_02 的银箱");
            return false;
        }
        map.player.GlobalPosition = chest.GlobalPosition;
        // 进图时它开没开（**必须在按 E 之前取**：下面第三次按 E 会把它标成已开）
        bool wasOpen = chest.opened_already;
        var demand = map.interact();
        int beforePay = inventory.count("item_med_01");
        var paid = map.interact();
        int afterPay = inventory.count("item_med_01");
        var opened = map.interact();
        bool demandOk = Str(demand, "stage") == "demand" && !wasOpen;
        // 0.14.0：文取拆成三列之后，书生在**判定路**（医术 5 ≥ 门槛 3）上直接达标 →
        // 控制器优先走「不花东西」的那条，所以草药汤**不该被扣**（旧断言写的是 item 路，已过期）。
        bool paidOk = Str(paid, "stage") == "peace" && afterPay == beforePay;
        bool flagOk = state.has_flag(GuardService.peace_flag(guardId));
        bool openedOk = Bool(opened, "ok") && chest.opened_already;
        // 存档往返：文取的旗标跟着走（不然下次进图屠夫又挡回去）
        var store = SaveStore.create("res://.logs/loopcheck/saves", 3);
        store.save_slot(2, state);
        var loaded = store.load_slot(2, db);
        var loadedState = loaded["state"].As<GameState>();
        bool persisted = Bool(loaded, "ok") && loadedState != null
            && loadedState.has_flag(GuardService.peace_flag(guardId));
        _lines.Add(
            $"宝箱守卫：开口={_bool(demandOk)} 付账={_bool(paidOk)}（草药汤 {beforePay}→{afterPay}）旗标={_bool(flagOk)} "
            + $"开箱={_bool(openedOk)} 旗标进档={_bool(persisted)}（进图前已解决={_bool(peaceAlready)} 进图时已开={_bool(wasOpen)}）"
        );
        return demandOk && paidOk && flagOk && openedOk && persisted;
    }

    /// <summary>存档 → 读档：真文件走一遍，比对一份指纹（气血／经验／难度／揭雾／副本记录／旗标）。</summary>
    private bool SaveRound(GameState state, TableDb db)
    {
        var store = SaveStore.create("res://.logs/loopcheck", 3);
        store.ensure_dir();
        state.slot = 1;
        string charId = state.char_ids[0].AsString();
        state.set_current_hp(charId, 77);
        state.reveal_node("n_huangcun");
        state.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper");
        state.set_flag("loopcheck_flag", true);
        state.party_exp = 123;
        string before = Fingerprint(state);
        var saved = store.save_slot(1, state);
        var loaded = store.load_slot(1, db);
        bool loadedOk = Bool(loaded, "ok");
        string after = loadedOk ? Fingerprint(loaded["state"].As<GameState>()) : "读档失败";
        bool same = before == after;
        _lines.Add($"存档→读档：写={_bool(Bool(saved, "ok"))} 读={_bool(loadedOk)} 指纹一致={_bool(same)}（{before}）");
        return Bool(saved, "ok") && loadedOk && same;
    }

    /// <summary>存档指纹：只放「该被存下来的东西」（时间戳、槽位这些会变的项不进）。</summary>
    private static string Fingerprint(GameState state)
    {
        var record = state.dungeon_record("scene_heifengzhai");
        return $"hp={state.current_hp_of(state.char_ids[0].AsString())} exp={state.party_exp} "
            + $"diff={state.difficulty_id} node={_bool(state.is_node_revealed("n_huangcun"))} "
            + $"chests={Arr(record, "chests").Count} flag={_bool(state.has_flag("loopcheck_flag"))}";
    }
}
