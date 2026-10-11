using System;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 事件判定（`event_check.csv` ＋ 非战斗技能）。
/// （对位 `tests/test_event_check.gd`，2026-10-11 P4 第三十六批整体搬过来。）
///
/// 判定值来自 `CharacterSheet`：`attr:x` 取裸属性、`skill:y` = 技能等级 + floor(裸属性/5)；
/// `hard` 达标必过、不足做不到；`soft` 达标必过、**不足掷骰**（01 的公式 `0.5 + 差值 × 0.1`，
/// 夹 5%~95%），软判定失败不惩罚。奖励：道具／装备／指向某地／剧情旗标。
/// </summary>
public partial class TestEventCheck : TestCase
{
    private const string CharId = "scholar_fallen";

    /// <summary>软判定要掷骰，用例靠固定种子复现。</summary>
    private const int Seed = 20261003;

    public override string SuiteName() => "事件判定";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        EventCheckService service = EventCheckService.create(db, state, RngService.create(Seed));
        _CheckTable(db, service);
        _CheckJudgement(db, state, service);
        _CheckSoftRoll(db);
        _CheckRewards(db, state, service);
        _CheckOnceOnly(db, state, service);
        _CheckSave(db, state);
        _CheckRewardTypes(db);
        _CheckGambleMoney(db);
        _CheckMarkerOwnership(db);
    }

    /// <summary>
    /// 14 条判定的来源都要能解析，难度都要是正数（0.28.0 加了 `ev_patrol_check`，
    /// 0.29.0 又加了 `ev_shen_rescue`——地牢里救沈雁回，打通 Q61 的那枚旗标）。
    /// </summary>
    private void _CheckTable(TableDb db, EventCheckService service)
    {
        Godot.Collections.Array rows = db.rows("event_check");
        CheckEq(rows.Count, 14, "表里有 14 条事件判定（0.29.0 加了 `ev_shen_rescue`）");
        foreach (Variant entry in rows)
        {
            var row = (EventCheckRow)entry.AsGodotObject();
            string checkId = row.check_id;
            string source = row.check_source;
            CheckTrue(
                source.StartsWith("attr:") || source.StartsWith("skill:"),
                $"{checkId} 的判定来源格式对：{source}");
            CheckGt(row.difficulty, 0.0, $"{checkId} 的难度是正数");
            Godot.Collections.Dictionary best = service.best_check_value(source);
            CheckGt(best["value"].AsDouble(), -900.0, $"{checkId} 的判定值算得出来（{source}）");
        }

        CheckEq(
            service.checks_for_scene("scene_heifengzhai").Count, 5,
            "黑风寨 5 条（0.29.0 加了地牢救人）");
        CheckEq(service.checks_for_region("n_luoyanpo").Count, 3, "落雁坡 3 条");
    }

    /// <summary>
    /// 位点归属：赌局摆在小地图里，判定行却只有 `region_id`。
    ///
    /// 07 文档把赌局摆在清风驿·客栈，但 `ev_gamble` 的 `scene_id` 是空的、只写了 `region_id=n_qingfengyi`。
    /// 小地图收位点以前只认 `scene_id`，于是镇上那个 `Event_ev_gamble` **永远没人接**；
    /// 地图验收那边还专门写了 `check_id != "ev_gamble"` 的例外把它从大地图里排除掉——两边都知道它特殊，
    /// 却没人把它接上。现在判据是「scene_id 命中 **或** region_id 是这张图的父节点」，
    /// 再**与**「这张图上真有 `Event_<check_id>` 位点」取交集。
    /// </summary>
    private void _CheckMarkerOwnership(TableDb db)
    {
        EventCheckService service = EventCheckService.create(
            db, SoloState(db), RngService.create(Seed));
        CheckEq(service.parent_node_of("scene_qingfengyi"), "n_qingfengyi", "清风驿挂在 n_qingfengyi 下");
        CheckTrue(
            service.check_allowed_in_scene("ev_gamble", "scene_qingfengyi"),
            "赌局允许出现在清风驿小地图里");
        CheckFalse(
            service.check_allowed_in_scene("ev_gamble", "scene_heifengzhai"),
            "但不允许出现在黑风寨");
        CheckTrue(
            service.check_allowed_in_scene("ev_shed_trap", "scene_heifengzhai"),
            "柴房机关（scene_id 命中）允许");
        CheckTrue(
            service.check_allowed_in_scene("ev_climb_wall", "scene_heifengzhai"),
            "翻寨墙的 region 与黑风寨相符，所以「允许」——**能不能接上还要看图上有位点**");
        CheckFalse(
            service.check_allowed_in_scene("没这条判定", "scene_qingfengyi"),
            "查不到的判定返回 false");
    }

    /// <summary>
    /// 赌局：赢的是**钱**，不是背包里一行「铜钱」。
    ///
    /// 以前 `_grant` 的 item 分支直接 `add_item`，于是 `item_money` 被当成可堆叠物品进了背包，
    /// `inventory.money` 一文不涨——玩家赢了赌局却买不了任何东西。
    /// </summary>
    private void _CheckGambleMoney(TableDb db)
    {
        var won = false;
        var lost = false;
        for (var candidate = 1; candidate < 200; candidate++)
        {
            GodotObject state = SoloState(db);
            var inventory = (Inventory)((GameState)state).inventory;
            int before = inventory.money;
            EventCheckService service = EventCheckService.create(
                db, state, RngService.create(candidate));
            Godot.Collections.Dictionary result = service.resolve("ev_gamble");
            if (result["success"].AsBool() && !won)
            {
                won = true;
                CheckEq(
                    result["reward"].AsGodotDictionary()["type"].AsString(), "money",
                    $"赌局赢的是钱（种子 {candidate}）");
                CheckGt(
                    inventory.money, before,
                    $"铜钱真的涨了：{before} → {inventory.money}");
                CheckEq(inventory.count("item_money"), 0, "背包里不该多出一行「铜钱」");
                CheckTrue(
                    result["text"].AsString().Contains("文钱"),
                    $"文案说清拿到多少钱：{result["text"].AsString()}");
            }
            else if (!result["success"].AsBool() && !lost)
            {
                lost = true;
                CheckEq(inventory.money, before, "赌输不掉钱（软判定失败不惩罚）");
                CheckEq(inventory.count("item_money"), 0, "输了也不会给钱");
            }

            if (won && lost)
            {
                break;
            }
        }

        CheckTrue(won, "在 200 颗种子里能找到赌赢那次");
        CheckTrue(lost, "也有赌输的时候");
    }

    /// <summary>1 级书生的判定值：奇门 3、文学 5、医术 3、毒术 2、生存 1；敏 9、力 5。</summary>
    private void _CheckJudgement(TableDb db, GodotObject state, EventCheckService service)
    {
        CharacterSheet sheet = CharacterSheet.create(db, state, CharId);
        CheckEq(sheet.event_check_value("skill:qimen")["value"].AsInt32(), 3, "奇门判定值 3（1 + 10/5）");
        CheckTrue(
            sheet.event_check_value("skill:qimen")["naked"].AsBool(),
            "判定值用裸属性（不含装备）");
        CheckEq(sheet.event_check_value("skill:wenxue")["value"].AsInt32(), 5, "文学判定值 5（3 + 2）");
        CheckEq(sheet.event_check_value("skill:dusu")["value"].AsInt32(), 2, "毒术判定值 2（0 + 2）");
        CheckEq(sheet.event_check_value("attr:agi")["value"].AsInt32(), 9, "敏 9 直接当判定值");

        // 恰好达标与差一点
        Godot.Collections.Dictionary passCase = service.judge("ev_shed_trap");    // skill:qimen 3
        CheckTrue(
            passCase["ok"].AsBool() && passCase["success"].AsBool(),
            "奇门 3 ≥ 3：识破柴房机关");
        CheckEq(passCase["difficulty"].AsInt32(), 3, "难度取自表");
        CheckEq(passCase["value"].AsInt32(), 3, "判定值取自面板同一套");
        Godot.Collections.Dictionary failCase = service.judge("ev_force_gate");   // attr:str 5 vs 8
        CheckTrue(
            failCase["ok"].AsBool() && !failCase["success"].AsBool(),
            "力 5 < 8：推不开木闩");
        Godot.Collections.Dictionary wall = service.judge("ev_climb_wall");       // attr:agi 9 vs 9
        CheckTrue(wall["success"].AsBool(), "敏 9 ≥ 9：翻得过寨墙（临界值算过）");
        Godot.Collections.Dictionary dusk = service.judge("ev_night_watch");      // attr:con 5 vs 7
        CheckFalse(dusk["success"].AsBool(), "体 5 < 7：确定性结果是不足（soft 才去掷骰）");
    }

    /// <summary>软判定：不足可掷骰（01 的公式）、失败不惩罚；硬判定不足则确定性地做不到。</summary>
    private void _CheckSoftRoll(TableDb db)
    {
        EventCheckService service = EventCheckService.create(
            db, SoloState(db), RngService.create(Seed));

        // 公式与上下限
        CheckEq(service.soft_chance(5, 7), 0.3, "差值 -2 → 0.5 - 0.2 = 30%");
        CheckEq(service.soft_chance(6, 7), 0.4, "差值 -1 → 40%");
        CheckEq(service.soft_chance(7, 7), 0.5, "差值 0 → 50%（达标时直接过，这里只验公式）");
        CheckEq(service.soft_chance(0, 10), 0.05, "差得太多夹到 5% 下限");
        CheckEq(service.soft_chance(20, 10), 0.95, "高太多夹到 95% 上限");

        // 硬判定不足：不掷骰、没有概率
        Godot.Collections.Dictionary gate = service.judge("ev_force_gate");   // hard，力 5 < 8
        CheckFalse(gate["success"].AsBool(), "硬判定不足：失败");
        CheckFalse(gate["roll_needed"].AsBool(), "硬判定不足不掷骰");
        CheckEq(gate["chance"].AsDouble(), 0.0, "硬判定不足没有概率");

        // 软判定不足：给出概率，judge 本身不掷骰（可以重复调）
        Godot.Collections.Dictionary dusk = service.judge("ev_night_watch");  // soft，体 5 < 7
        CheckFalse(dusk["success"].AsBool(), "软判定不足：确定性结果仍是不足");
        CheckTrue(dusk["roll_needed"].AsBool(), "软判定不足要掷骰");
        CheckEq(dusk["chance"].AsDouble(), 0.3, "守夜成功率 30%");
        CheckTrue(
            dusk["text"].AsString().Contains("掷骰 30%"),
            $"文案写出概率：{dusk["text"].AsString()}");
        Godot.Collections.Dictionary duskAgain = service.judge("ev_night_watch");
        CheckEq(duskAgain["chance"].AsDouble(), dusk["chance"].AsDouble(), "judge 不掷骰，重复调用结果一致");

        // 掷骰真的会翻面：同一颗种子可复现，换种子两边都出现（否则就是「永远失败」）
        var win = 0;
        var lose = 0;
        for (var candidate = 1; candidate < 60; candidate++)
        {
            Godot.Collections.Dictionary one = EventCheckService.create(
                db, SoloState(db), RngService.create(candidate)).resolve("ev_night_watch");
            CheckTrue(_B(one, "rolled"), $"软判定不足真的掷了骰（种子 {candidate}）");
            CheckTrue(
                one["text"].AsString().Contains("掷骰 →"),
                $"文案写出掷骰结果：{one["text"].AsString()}");
            if (one["success"].AsBool())
            {
                win++;
            }
            else
            {
                lose++;
            }
        }

        CheckGt(win, 0.0, "换种子有掷中的时候（不是永远失败）");
        CheckGt(lose, 0.0, "也有掷不中的时候（不是永远成功）");
        // 同一颗种子跑两次必须一模一样
        Godot.Collections.Dictionary repeatA = EventCheckService.create(
            db, SoloState(db), RngService.create(Seed)).resolve("ev_night_watch");
        Godot.Collections.Dictionary repeatB = EventCheckService.create(
            db, SoloState(db), RngService.create(Seed)).resolve("ev_night_watch");
        CheckEq(repeatA["success"].AsBool(), repeatB["success"].AsBool(), "同种子同结果（可复现）");

        // 掷中要发奖励、掷不中不发且不惩罚（拿 ev_cell_heal 当样本：soft，医术 3 < 4 = 40%）
        var won = false;
        var lost = false;
        for (var candidate = 1; candidate < 200; candidate++)
        {
            GodotObject probeState = SoloState(db);
            var probe = (GameState)probeState;
            var probeInventory = (Inventory)probe.inventory;
            EventCheckService probeService = EventCheckService.create(
                db, probeState, RngService.create(candidate));
            Godot.Collections.Dictionary result = probeService.resolve("ev_cell_heal");
            if (result["success"].AsBool() && !won)
            {
                won = true;
                CheckEq(
                    probeInventory.count("item_potion_small"), 1,
                    $"掷中：奖励进背包（种子 {candidate}）");
                CheckEq(probe.event_check_result("ev_cell_heal"), "done", "掷中记 done");
                // 救出阿福 → `flag_qiutu_saved`（钱大夫的委托等的就是它）。
                // 以前这枚旗标**全项目没有任何来源**，那条委托永远交不了（20 号 §九 #6）。
                CheckTrue(probe.has_flag("flag_qiutu_saved"), "掷中＝救出来了，记下 flag_qiutu_saved");
            }
            else if (!result["success"].AsBool() && !lost)
            {
                lost = true;
                CheckEq(probeInventory.count("item_potion_small"), 0, "掷不中：不发东西（失败不惩罚）");
                CheckEq(probe.event_check_result("ev_cell_heal"), "failed", "掷不中记 failed");
                CheckTrue(
                    result["text"].AsString().Contains("拿不到额外药品"),
                    $"失败文案用 fail_note：{result["text"].AsString()}");
                CheckFalse(probe.has_flag("flag_qiutu_saved"), "没救成就不记账");
            }

            if (won && lost)
            {
                break;
            }
        }

        CheckTrue(won, "软判定掷中过");
        CheckTrue(lost, "软判定也掷不中过");
    }

    /// <summary>奖励三类：道具进包、指向某地（顺带揭开地标）、剧情旗标。</summary>
    private void _CheckRewards(TableDb db, GodotObject state, EventCheckService service)
    {
        var game = (GameState)state;
        var inventory = (Inventory)game.inventory;

        // 道具：救治采药人（医术 3 ≥ 2）
        Godot.Collections.Dictionary heal = service.resolve("ev_herbalist_help");
        CheckTrue(heal["success"].AsBool(), $"医术判定成功：{heal["text"].AsString()}");
        CheckEq(inventory.count("item_potion_small"), 1, "奖励的金创药进了背包");
        CheckEq(game.event_check_result("ev_herbalist_help"), "done", "结果写进存档");

        // 剧情旗标：劝退强盗（文学 5 ≥ 4）
        Godot.Collections.Dictionary parley = service.resolve("ev_bandit_parley");
        CheckTrue(parley["success"].AsBool(), $"劝退成功：{parley["text"].AsString()}");
        CheckTrue(game.has_flag("event_parley"), "记下 event_parley 旗标");

        // 碑文（文学 5 ≥ 3）给锈剑
        Godot.Collections.Dictionary epitaph = service.resolve("ev_grave_epitaph");
        CheckTrue(epitaph["success"].AsBool(), $"读碑成功：{epitaph["text"].AsString()}");
        CheckEq(inventory.count("item_rusty_sword"), 1, "拿到锈剑");

        // 失败：转述代价、不写 done
        Godot.Collections.Dictionary gate = service.resolve("ev_force_gate");
        CheckFalse(gate["success"].AsBool(), "推门失败");
        CheckTrue(
            gate["text"].AsString().Contains("推不开粗木闩"),
            $"失败文案用表里的 fail_note：{gate["text"].AsString()}");
        CheckEq(game.event_check_result("ev_force_gate"), "failed", "失败也记下来（但不是 done）");

        // 「指向某地」类：这条现在判定不达标（生存 1 < 3），走白盒验一次奖励落点
        Resource row = db.get_row("event_check", "ev_wild_track");
        CheckNotNull(row, "有追足迹这条");
        if (row != null)
        {
            Godot.Collections.Dictionary granted = service._grant(row);
            CheckEq(granted["type"].AsString(), "room", "奖励类型是「去某个房间」");
            CheckEq(_S(granted, "landmark"), "n_cave_collapse", "顺带把塌陷山洞标到地图上");
            CheckTrue(game.is_node_revealed("n_cave_collapse"), "地标真的揭开了");
            CheckTrue(
                granted["text"].AsString().Contains("塌方深处"),
                $"文案写清去哪：{granted["text"].AsString()}");
        }
    }

    /// <summary>once_only：成功过就不能再做；失败还能再试。</summary>
    private void _CheckOnceOnly(TableDb db, GodotObject state, EventCheckService service)
    {
        var game = (GameState)state;
        game.record_event_check("ev_wild_track", "done");
        Godot.Collections.Dictionary again = service.resolve("ev_wild_track");
        CheckFalse(again["ok"].AsBool(), "once_only 的点位做过就不能再做");
        CheckTrue(_B(again, "already"), "结果里标着「已经做过」");
        CheckTrue(
            again["text"].AsString().Contains("已经做过"),
            $"文案说明原因：{again["text"].AsString()}");

        // 失败过一次的还能重试（失败不算 once_only 的「做过」）
        game.record_event_check("ev_force_gate", "failed");
        Godot.Collections.Dictionary retry = service.judge("ev_force_gate");
        CheckTrue(retry["ok"].AsBool(), "失败过的还能再判一次");
    }

    /// <summary>判定结果与旗标都进存档。</summary>
    private void _CheckSave(TableDb db, GodotObject state)
    {
        var game = (GameState)state;
        GameState back = GameState.from_dict(game.to_dict(), db);
        CheckNotNull(back, "带事件记录的存档能读回来");
        if (back == null)
        {
            return;
        }

        CheckEq(back.migrated_from, 0, "同版本往返不需要迁移");
        CheckEq(back.event_check_result("ev_herbalist_help"), "done", "判定结果往返一致");
        CheckTrue(back.has_flag("event_parley"), "剧情旗标往返一致");
        CheckEq(((Inventory)back.inventory).count("item_rusty_sword"), 1, "奖励道具往返一致");

        // v8 老档没有这两项，读进来是空的
        Godot.Collections.Dictionary legacy = game.to_dict();
        legacy["version"] = 8;
        legacy.Remove("event_checks");
        legacy.Remove("flags");
        GameState migrated = GameState.from_dict(legacy, db);
        CheckEq(migrated.migrated_from, 8, "记下从 v8 迁移");
        CheckEq(migrated.version, GameState.save_version(), "版本升到当前");
        CheckEq(migrated.event_checks.Count, 0, "老档没有事件记录");
    }

    /// <summary>
    /// 奖励类型：`equip` 能真发；设计 06 允许的 `skillbook`／`boss` 各有落点，
    /// 写错 id 的必须**显式报错**，不许静默。
    ///
    /// 以前两边都没有声音：`_grant()` 对这三类落进「没有奖励」的兜底，而 `TableValidator` 只查
    /// `item` 与 `room` 的 id——策划写一条 `reward_type=equip` 会静默什么都不发。
    /// </summary>
    private void _CheckRewardTypes(TableDb db)
    {
        // ① 校验器补齐 id 检查：equip / skillbook / boss 写错 id 要被抓到
        TableDb broken = _StubCheckTable(row =>
        {
            if (row.check_id == "ev_herbalist_help")
            {
                row.reward_type = "equip";
                row.reward_id = "eq_does_not_exist";
            }
        });
        string[] errors = TableValidator.validate(broken);
        CheckGt(errors.Length, 0.0, $"equip 奖励写错 id 要被校验抓到：{string.Join("|", errors)}");
        var named = false;
        foreach (string message in errors)
        {
            if (message.Contains("ev_herbalist_help"))
            {
                named = true;
            }
        }

        CheckTrue(named, $"报错点出是哪条判定：{string.Join("|", errors)}");

        // ② 运行时：equip 真发（建实例进背包）
        GodotObject probeState = SoloState(db);
        var probeInventory = (Inventory)((GameState)probeState).inventory;
        int before = probeInventory.equipment.Count;
        EventCheckService service = EventCheckService.create(
            db, probeState, RngService.create(Seed));
        Resource equipRow = (Resource)db.get_row("event_check", "ev_herbalist_help").Duplicate(true);
        ((EventCheckRow)equipRow).reward_type = "equip";
        ((EventCheckRow)equipRow).reward_id = "eq_sword_01";
        Godot.Collections.Dictionary granted = service._grant(equipRow);
        CheckEq(granted["type"].AsString(), "equip", "equip 奖励不再落进兜底");
        CheckTrue(
            _S(granted, "instance_id").StartsWith("eq_sword_01"),
            $"建出了装备实例：{_S(granted, "instance_id")}");
        CheckEq(probeInventory.equipment.Count, before + 1, "装备实例进了背包");
        CheckTrue(
            granted["text"].AsString().Contains("铁剑"),
            $"文案用中文名：{granted["text"].AsString()}");

        // ③ 设计 06 允许的 7 种奖励**全部有落点**（2026-10-03 补齐最后两种）：
        //    · skillbook —— 与 `item` 同一条路（给一本书，进背包后走研读流程）
        //    · boss —— 返回「开战请求」（纯逻辑服务不碰场景，由 `resolve_event` 发起战斗）
        //    写错 id 仍然显式报错，不静默。
        TableDb skillbookStub = _StubCheckTable(row =>
        {
            if (row.check_id == "ev_herbalist_help")
            {
                row.reward_type = "skillbook";
                row.reward_id = "item_potion_small";
            }
        });
        GodotObject sbState = SoloState(skillbookStub);
        EventCheckService sbService = EventCheckService.create(
            skillbookStub, sbState, RngService.create(Seed));
        Godot.Collections.Dictionary sbRes = sbService.resolve("ev_herbalist_help");
        CheckTrue(sbRes["success"].AsBool(), "skillbook：判定本身成功");
        CheckEq(
            sbRes["reward"].AsGodotDictionary()["type"].AsString(), "skillbook",
            "skillbook 不再落进兜底");
        CheckEq(
            ((Inventory)((GameState)sbState).inventory).count("item_potion_small"), 1,
            "秘籍进了背包（与 item 同一条路）");
        CheckTrue(
            sbRes["text"].AsString().Contains("得到"),
            $"文案写明拿到了什么：{sbRes["text"].AsString()}");

        // boss：指向存在的敌人 → 交出开战请求（含敌人中文名）；指向不存在的敌人 → 显式报错、不发请求
        foreach ((string enemyId, bool exists) in new[] { ("en_bd_boss", true), ("en_does_not_exist", false) })
        {
            string enemy = enemyId;
            TableDb bossStub = _StubCheckTable(row =>
            {
                if (row.check_id == "ev_herbalist_help")
                {
                    row.reward_type = "boss";
                    row.reward_id = enemy;
                }
            });
            GodotObject bossState = SoloState(bossStub);
            EventCheckService bossService = EventCheckService.create(
                bossStub, bossState, RngService.create(Seed));
            Godot.Collections.Dictionary bossRes = bossService.resolve("ev_herbalist_help");
            Godot.Collections.Dictionary reward = bossRes["reward"].AsGodotDictionary();
            CheckEq(reward["type"].AsString(), "boss", $"boss 奖励类型认出来了（{enemy}）");
            if (exists)
            {
                CheckTrue(_B(reward, "start_battle"), "交出开战请求");
                CheckTrue(
                    _S(reward, "enemy_name").Contains("大寨主"),
                    $"带上敌人中文名：{_S(reward, "enemy_name")}");
                CheckTrue(
                    bossRes["text"].AsString().Contains("现身"),
                    $"文案写「现身」：{bossRes["text"].AsString()}");
            }
            else
            {
                CheckFalse(_B(reward, "start_battle"), "敌人在表里不存在时不发开战请求");
                // **规则改了**（2026-10-03，决策 242）：id 只进日志（push_error），玩家看的是人话——
                // 原来这条断言替"把 en_does_not_exist 甩给玩家"站台
                CheckFalse(
                    _S(reward, "error").Contains("en_does_not_exist"),
                    $"错误文案不给玩家看表内 id（只进日志）：{_S(reward, "error")}");
                CheckTrue(
                    _S(reward, "error").Contains("不存在"),
                    $"错误文案说人话：{_S(reward, "error")}");
            }
        }

        // ④ `none`（判定本身就是收益）不该再往状态栏里塞开发腔的说明
        GodotObject noneState = SoloState(db);
        EventCheckService noneService = EventCheckService.create(
            db, noneState, RngService.create(Seed));
        Godot.Collections.Dictionary trap = noneService.resolve("ev_shed_trap"); // hard，奇门 3 ≥ 3
        CheckTrue(trap["success"].AsBool(), "识破柴房机关成功");
        CheckFalse(
            trap["text"].AsString().Contains("场景自己处理"),
            $"none 奖励不给玩家看开发说明：{trap["text"].AsString()}");
        CheckFalse(
            trap["text"].AsString().EndsWith("　"),
            $"文案末尾不留空段：{trap["text"].AsString()}");

        // ⑤ 非法 reward_type 也不能静默（校验器会报，运行时也要报）
        TableDb badStub = _StubCheckTable(row =>
        {
            if (row.check_id == "ev_shed_trap")
            {
                row.reward_type = "bogus";
            }
        });
        GodotObject badState = SoloState(badStub);
        EventCheckService badService = EventCheckService.create(
            badStub, badState, RngService.create(Seed));
        Godot.Collections.Dictionary bad = badService.resolve("ev_shed_trap");
        CheckTrue(
            bad["text"].AsString().Contains("不是设计允许"),
            $"非法奖励类型要讲出来：{bad["text"].AsString()}");
        CheckFalse(
            bad["text"].AsString().Contains("bogus"),
            $"非法类型也不许把配置 token 甩给玩家：{bad["text"].AsString()}");

        // ⑦ **玩家可见文案不许出现表内 id**（AGENTS 硬规矩）：数据错时 id 只进日志。
        //    这一组把 4 条错路径逐条走一遍——坏 id（equip／room／boss）与坏 check_id。
        TableDb leakStub = _StubCheckTable(row =>
        {
            if (row.check_id == "ev_herbalist_help")
            {
                row.reward_type = "equip";
                row.reward_id = "eq_does_not_exist";
            }

            // 换一条**硬判定且必过**的来试 room（软判定会掷骰 → 后半段断言会时跑时不跑）
            if (row.check_id == "ev_shed_trap")
            {
                row.reward_type = "room";
                row.reward_id = "hf_does_not_exist";
            }
        });
        GodotObject leakState = SoloState(leakStub);
        EventCheckService leakService = EventCheckService.create(
            leakStub, leakState, RngService.create(Seed));
        Godot.Collections.Dictionary equipLeak = leakService.resolve("ev_herbalist_help");
        CheckFalse(
            equipLeak["text"].AsString().Contains("eq_does_not_exist"),
            $"equip 奖励写错 id：文案不漏 id（{equipLeak["text"].AsString()}）");
        CheckTrue(
            equipLeak["text"].AsString().Contains("数据错"),
            $"equip 奖励写错 id：文案说清是数据错：{equipLeak["text"].AsString()}");
        Godot.Collections.Dictionary roomLeak = leakService.resolve("ev_shed_trap");
        CheckFalse(
            roomLeak["text"].AsString().Contains("hf_does_not_exist"),
            $"room 奖励写错 id：文案不漏 id（{roomLeak["text"].AsString()}）");
        Godot.Collections.Dictionary missing = leakService.judge("ev_does_not_exist");
        CheckFalse(missing["ok"].AsBool(), "没有这条判定要如实拒绝");
        CheckFalse(
            missing["text"].AsString().Contains("ev_does_not_exist"),
            $"查不到的判定也不漏 id：{missing["text"].AsString()}");

        // ⑥ `fail_note` 写了「会付代价」但设计没给数值的那种，界面要如实说明（同奇袭「暂缓规则」的口径）
        TableDb costStub = _StubCheckTable(row =>
        {
            if (row.check_id == "ev_shed_trap")
            {
                row.difficulty = 99;      // 让 1 级书生（奇门 3）必定失败
            }
        });
        GodotObject costState = SoloState(costStub);
        EventCheckService costService = EventCheckService.create(
            costStub, costState, RngService.create(Seed));
        Godot.Collections.Dictionary trapFail = costService.resolve("ev_shed_trap");
        CheckFalse(trapFail["success"].AsBool(), "难度 99：识破落石陷阱失败");
        CheckTrue(
            trapFail["text"].AsString().Contains("落石"),
            $"转述表里的 fail_note：{trapFail["text"].AsString()}");
        CheckTrue(
            trapFail["text"].AsString().Contains("还没接"),
            $"代价还没接要说出来，别让玩家以为真掉了血：{trapFail["text"].AsString()}");
        // 反例：「推不开粗木闩」是状态描述，不该挂代价说明
        EventCheckService plainGate = EventCheckService.create(
            db, SoloState(db), RngService.create(Seed));
        string plainText = plainGate.resolve("ev_force_gate")["text"].AsString();
        CheckFalse(plainText.Contains("还没接"), $"状态类 fail_note 不挂代价说明：{plainText}");
    }

    /// <summary>
    /// 造一份私有表库，并按 `edit` 改 `event_check` 里的行（坏数据夹具）。
    ///
    /// **逐行 `Duplicate(true)` 再写回 `rows`**：表资源是 Godot 按路径缓存的，直接改行会改到
    /// 别人正在用的那一行（照 `TestCase.TableWithTalents` 的形状）。
    /// </summary>
    private static TableDb _StubCheckTable(Action<EventCheckRow> edit)
    {
        var stub = new TableDb();
        stub.load_all();
        var table = (TableResource)stub.tables["event_check"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            var row = (EventCheckRow)((Resource)entry.AsGodotObject()).Duplicate(true);
            edit(row);
            rows.Add(row);
        }

        table.rows = rows;
        stub.tables["event_check"] = table;
        return stub;
    }

    private static bool _B(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _S(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
