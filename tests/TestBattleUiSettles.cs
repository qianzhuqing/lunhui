using Godot;
using Lunhui.Autoload;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// `TestBattleUi` 的结算那一半：结算面板契约、逃跑三态、败北／平局／练习战三支零收获、
/// 自动存档失败不静默、首杀只给一次、两个 Boss 真打完才解锁绝境、打赢某支队伍置旗标。
/// 拆文件只为不撞 800 行硬上限（施工单「二点九八」的做法）。
/// </summary>
public partial class TestBattleUi
{
    /// <summary>
    /// 结算面板契约（22 §1.6 第八版 ＋ §八「结算面板＝整屏分区」）：打完一场**同时触发掉落／升级／武学**
    /// 的战斗 → 面板上**五块都在**（战果标题／经验／铜钱／掉落／武学）＋ 「确定」；**升级的人每人一行**
    /// （两条升级＝两行，不是拼成一行），那行的 `Lv a → b` 与 `+N 点` **与 `LevelService.describe` 同源**；
    /// 面板**自身高 ≤ 设计高 648**（口径②）；零收获那四块在败北／平局／撤退三支里各自验（`_CheckNoHarvestBlocks`）。
    /// </summary>
    private void _CheckSettlePanelContract(TableDb db)
    {
        // 两个人 → 「两人升级＝两行」这一条才验得出来（单人队最多一行）
        GodotObject state = NewGame(db, "normal", new[] { "scholar_fallen", "ch_ci" });
        // 两位各升一级正好花光（1 级那一档 100 点）：再多一点就会有人连升两级，行数不再是「两人两行」
        ((GameState)state).party_exp = LevelService.create(db, state).exp_to_next(1) * 2;
        var store = SaveStore.create(SettleCardDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        battle.encounter_override = _BossEncounter();
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        CheckEq(battle.result_label_text(), "", "开局时面板还是空的（没到结算那一屏）");
        // 先钉住"打之前它在"——不然下面那条「结算时 Boss 长血条收起」可能只是一场没有 Boss 的空断言
        CheckTrue(_Visible(battle, "BossBar"), "Boss 战里贴顶长血条本来在（结算那一帧才收它）");
        // 跳字层：走真路径造一个伤害跳字（口径④点名要收的那个），先钉住"打的时候它在"
        string first_ally = battle.allies[0].AsGodotObject().Get("actor_id").AsString();
        battle._spawn_floats(new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary { { "target", first_ally }, { "damage", 7 } },
        });
        CheckTrue(_Visible(battle, "FloatLayer"), "打的时候伤害跳字层在（结算那一帧才收它）");
        foreach (BattleActor enemy in _Actors(battle.enemies))
        {
            enemy.hp = 0;
        }

        battle._settle();
        var panel = battle.FindChild("SettlePanel", true, false) as Control;
        var confirm = battle.FindChild("ResultConfirm", true, false) as Control;
        CheckNotNull(panel, "结算面板在（第八版：整屏分区面板）");
        CheckNotNull(battle.FindChild("SettleTitleBox", true, false), "① 战果标题块（上下细金线夹大字）");
        CheckEq(_SettleText(battle, "SettleTitle"), "胜利", "① 标题写「胜利」");
        CheckNotNull(battle.FindChild("SettleExp", true, false), "② 经验块");
        CheckNotNull(battle.FindChild("SettleMoneyRow", true, false), "③ 铜钱块");
        CheckNotNull(battle.FindChild("SettleDropGrid", true, false), "④ 掉落块（两列往外铺）");
        CheckNotNull(battle.FindChild("SettleMartial", true, false), "⑤ 武学结算块");
        CheckNotNull(confirm, "⑥ 「确定」（＝离场，第七版那枚不变）");

        // 第八版：结算那一帧**其余挂件整组收起**——面板底下只是 66% 不透明的压暗底，留着它们会从面板
        // 边上透出来（真窗口截图里见过「第 N 回合　已结束」、Boss 长血条与战报面板浮在结算面板旁边）。
        // 战报**面板**收起来、**入口那枚钮留着**：面板只摆得下 8 格掉落，全量明细还得够得着。
        CheckFalse(_Visible(battle, "RoundLabel"), "结算时回合标签收起");
        CheckFalse(_Visible(battle, "MsgLine"), "结算时那行状态播报收起");
        CheckFalse(_Visible(battle, "BossBar"), "结算时 Boss 长血条收起");
        CheckFalse(_Visible(battle, "LogPanel"), "结算时战报面板收起");
        CheckTrue(_Visible(battle, "LogButton"), "战报入口那枚钮还在（全量明细够得着）");

        // 升级的人**每人一行**，而且那一行与 `LevelService.describe` 同源（不许自己再算一遍格式）
        LevelService levels = LevelService.create(db, state);
        System.Collections.Generic.List<Label> rows = _SettleLabels(battle, "SettleLevelRow");
        CheckGt(rows.Count, 1, $"两位都升了级 → 每人占一行（不是拼成一行）：{rows.Count} 行");
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (Label row in rows)
        {
            Godot.Collections.Dictionary entry = row.GetMeta("level_up").AsGodotDictionary();
            names.Add(entry["name"].AsString());
            CheckEq(
                row.Text,
                $"▲ {levels.describe(new Godot.Collections.Array { entry })}",
                $"升级行与 LevelService.describe 同源：{row.Text}");
        }

        CheckEq(names.Count, 2, $"两位升级的角色各占一行：{string.Join("、", names)}");
        // 「一条升级一行」这一条要的是"不合并"：喂两条就该出两行（口径②：每区不限行）
        battle._refresh_settle_panel(_TwoLevelView());
        CheckEq(_SettleLabels(battle, "SettleLevelRow").Count, 2, "两条升级 → 两行");

        // 面板自身高 ≤ 设计高，且不压住贴底那枚「确定」（口径②：真超出由滚动区整块滚）
        if (panel != null && confirm != null)
        {
            CheckTrue(
                panel.Size.Y <= LayoutBudget.design_height() + 1.0,
                $"面板自身高 ≤ 648（实际 {panel.Size.Y:0}）");
            CheckTrue(
                panel.Size.X <= LayoutBudget.design_width() + 1.0,
                $"面板宽 ≤ 1152（实际 {panel.Size.X:0}）");
            CheckTrue(
                panel.GetGlobalRect().End.Y <= confirm.GetGlobalRect().Position.Y + 1.0,
                "面板不压住贴底那枚「确定」");
        }

        // 口径④：跳字层也收（它挂在结算面板**之上**，最后一下的数字会从面板上闪过去）
        CheckFalse(_Visible(battle, "FloatLayer"), "结算帧伤害跳字层收起（口径④）");
        // 口径④：按压暗底之上那枚「战报」，战报要盖到结算面板**之上**——面板上那句
        // 「还有 N 件放不下（见战报）」是承诺，入口得真在、真盖得住
        battle.toggle_log_panel();
        var log_panel = battle.FindChild("LogPanel", true, false) as Control;
        CheckTrue(log_panel != null && log_panel.Visible, "结算帧按「战报」能把战报按出来");
        CheckTrue(
            log_panel != null && log_panel.GetIndex() == battle.GetChildCount() - 1,
            "按出来的战报盖在结算面板之上");
        battle.toggle_log_panel();

        // 掉落超过 8 格：面板上留一句「还有 N 件放不下（见战报）」（口径②「不许丢信息」要让玩家看得见）
        battle._refresh_settle_panel(_ManyDropsView());
        CheckNotNull(battle.FindChild("SettleDropMore", true, false), "溢出掉落有「还有 N 件放不下」那一行");
        CheckTrue(
            _SettleText(battle, "SettleDropMore").Contains("4"),
            $"那一行写清还有几件：{_SettleText(battle, "SettleDropMore")}");

        _DropBattle(battle);
    }

    /// <summary>
    /// 口径③：零收获（败北／平局／撤退）→ 面板上**经验／铜钱／掉落／升级四块整块不出现**，
    /// 只剩标题 ＋ 说明 ＋「确定」。
    /// </summary>
    private void _CheckNoHarvestBlocks(BattleScreen battle, string tag)
    {
        CheckNull(battle.FindChild("SettleExp", true, false), $"{tag}：经验块整块不出现（口径③）");
        CheckNull(battle.FindChild("SettleMoneyRow", true, false), $"{tag}：铜钱块整块不出现");
        CheckNull(battle.FindChild("SettleDrops", true, false), $"{tag}：掉落块整块不出现");
        CheckEq(
            battle.FindChildren("SettleLevelRow*", "Label", true, false).Count, 0,
            $"{tag}：升级行不出现");
        CheckNotNull(battle.FindChild("SettleTitleBox", true, false), $"{tag}：标题还在");
        CheckNotNull(battle.FindChild("ResultConfirm", true, false), $"{tag}：「确定」还在");
    }

    /// <summary>面板上某一类行（按节点名前缀找，顺序＝节点顺序）</summary>
    private static System.Collections.Generic.List<Label> _SettleLabels(Node root, string prefix)
    {
        var labels = new System.Collections.Generic.List<Label>();
        foreach (Node node in root.FindChildren($"{prefix}*", "Label", true, false))
        {
            if (node is Label label)
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    private static string _SettleText(Node root, string node_name) =>
        root.FindChild(node_name, true, false) is Label label ? label.Text : "";

    private static bool _Visible(Node root, string node_name) =>
        root.FindChild(node_name, true, false) is Control control && control.Visible;

    /// <summary>
    /// 喂给面板的「掉落溢出」view：8 格铺满 ＋ 总数 12——面板只摆 8 格，溢出的那 4 件要并进
    /// 底下一行「还有 4 件放不下（见战报）」（口径②）。
    /// </summary>
    private static Godot.Collections.Dictionary _ManyDropsView()
    {
        var cells = new Godot.Collections.Array();
        for (int index = 0; index < 8; index++)
        {
            cells.Add(new Godot.Collections.Dictionary
            {
                { "icon", "" }, { "text", $"草药 ×{index + 1}" },
            });
        }

        return new Godot.Collections.Dictionary
        {
            { "title", "胜利" },
            { "harvest", true },
            { "exp", 200 },
            { "money", 30 },
            { "levels", new Godot.Collections.Array() },
            { "drops", cells },
            { "drops_total", 12 },
            { "martial", new Godot.Collections.Array() },
            { "notes", new Godot.Collections.Array() },
        };
    }

    /// <summary>喂给面板的最小 view：两条升级（→ 两行）＋ 一格掉落，`_CheckSettlePanelContract` 用</summary>
    private static Godot.Collections.Dictionary _TwoLevelView() => new()
    {
        { "title", "胜利" },
        { "harvest", true },
        { "exp", 200 },
        { "money", 30 },
        {
            "levels", new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "char_id", "scholar_fallen" }, { "name", "家道失落的书生" },
                    { "from", 1 }, { "to", 2 }, { "points", 5 },
                },
                new Godot.Collections.Dictionary
                {
                    { "char_id", "ch_ci" }, { "name", "燕小七" },
                    { "from", 2 }, { "to", 3 }, { "points", 5 },
                },
            }
        },
        {
            "drops", new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "icon", "" }, { "text", "野狼皮 ×2" } },
            }
        },
        { "martial", new Godot.Collections.Array() },
        { "notes", new Godot.Collections.Array() },
    };

    /// <summary>逃跑在真实界面上的三态：开打前不可按、轮到我方可按、按下后（强制成功）撤退收场且不结算奖励。</summary>
    private void _CheckFleeUi(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(FleeUiDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        // 逃跑是掷点判定的，界面用例靠 force_flee 把结局钉死
        battle.modifiers_override = new Godot.Collections.Dictionary { { "force_flee", true } };
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        var flee = battle.FindChild("FleeButton", true, false) as Button;
        CheckNotNull(flee, "有逃跑按钮");
        if (flee == null)
        {
            _DropBattle(battle);
            return;
        }

        CheckFalse(flee.Text.Contains("未实现"), $"逃跑按钮不再是「未实现」占位：{flee.Text}");
        CheckTrue(flee.Disabled, $"还没轮到我方时逃跑置灰：{flee.TooltipText}");
        Node session_node = Tree.Root.GetNodeOrNull("GameSession");
        string spawn_key = ((Encounter)battle.encounter).source_key;
        bool cleared_before = session_node != null && ((GameSession)session_node).cleared_spawns.ContainsKey(spawn_key);
        var inventory = state.Get("inventory").As<Inventory>();
        int money_before = inventory.money;

        battle.press_next_round();
        CheckFalse(flee.Disabled, $"轮到我方时逃跑可按（原因：{flee.TooltipText}）");
        // 能撤时 tooltip 要写出**现算的**成功率（和判定面板同一口径；写死数字的话设计改公式就假了）
        BattleSimulator sim = _Sim(battle);
        BattleActor flee_actor = sim.current_actor() as BattleActor;
        int expected_pct = (int)Mathf.Round(sim.flee_chance(flee_actor) * 100.0);
        CheckTrue(
            flee.TooltipText.Contains($"{expected_pct}%"),
            $"可撤退时 tooltip 写出成功率（期望 {expected_pct}%，实际「{flee.TooltipText}」）");
        Godot.Collections.Dictionary result = battle.press_flee();
        CheckTrue(result["ok"].AsBool(), $"按下逃跑能判定：{result}");
        CheckTrue(result["escaped"].AsBool(), "force_flee 下撤退成功");
        CheckEq(sim.winner(), "flee", "战斗以撤退收场");
        CheckTrue(battle.result_label_text().Contains("撤退"), $"结算卡片写撤退：{battle.result_label_text()}");
        CheckEq(inventory.money, money_before, "撤退不结算铜钱");
        CheckEq(((GameState)state).party_exp, 0, "撤退不给经验");
        CheckTrue(flee.Disabled, "撤退后按钮置灰");
        bool cleared_after = session_node != null && ((GameSession)session_node).cleared_spawns.ContainsKey(spawn_key);
        CheckFalse(cleared_after && !cleared_before, "撤退不会把明雷标成已清（回来还能打）");
        _CheckNoHarvestBlocks(battle, "撤退");
        _DropBattle(battle);
    }

    /// <summary>
    /// 败北结算是**整条分支从来没被跑过**（现有用例只打过胜利／撤退／拆招／自动战斗）：
    /// 把败北误判成胜利、败北时顺手发奖励、或者顺序写反，以前一条断言都不会响。
    /// 这条真的把全队按到 0 血 → 走真场景的 `_settle()`，钉住设计口径：
    /// 败北零收获（不发经验／铜钱／掉落、不记首杀、不清明雷）＋**战败处理**（08 已定）：
    /// 全队回到最近到过的出生点／城镇、气血回满、战斗外增益全部清除；没到过城镇就退回大地图。
    /// </summary>
    private void _CheckDefeatSettle(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(DefeatDir, 3);
        store.ensure_dir();
        var game_state = (GameState)state;
        game_state.slot = 1;
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        Node session_node = Tree.Root.GetNodeOrNull("GameSession");
        // 带一个战斗外增益进战斗：败北要把它清掉（08 的败北处理第 ④ 条）
        CheckNotNull(session_node, "用例能拿到会话（战败处理写在会话里）");
        GameSession session = _Session(session_node);
        if (session != null)
        {
            session.clear_field_buffs();
            session.add_field_buff("buff_meditated", 10, "", 1);
            CheckEq(session.active_field_buffs(-1).Count, 1, "开打前带着一条战斗外增益");
            // 用例前提：这一局没到过城镇。C# 链的会话是**全链共享**的（前面那些用例可能进过城），
            // 所以这里显式清一次——旧链一个用例一个进程，前提天然成立。
            session.last_shelter_scene = "";
            session.last_shelter_name = "";
        }

        var inventory = state.Get("inventory").As<Inventory>();
        int money_before = inventory.money;
        int exp_before = game_state.party_exp;
        int equips_before = inventory.equipment_count();
        string items_before = string.Join(",", inventory.item_ids());
        foreach (BattleActor ally in _Actors(battle.allies))
        {
            ally.hp = 0;
        }

        BattleSimulator sim = _Sim(battle);
        bool defeated = sim.winner() == "enemy";
        Sfx.clear_log();
        battle._settle();

        CheckTrue(defeated, $"全队倒下时胜负判定是败北（实际 {sim.winner()}）");
        CheckTrue(battle.settled, "败北也走结算（不会卡在半场：按钮与自动存档都要收尾）");
        CheckTrue(Sfx.has_played("defeat"), "败北结算会请求「败北」音效（07 §8.4）");
        string card = battle.result_label_text();
        CheckTrue(card.Contains("败北"), $"结算卡片写清败北：{card}");
        CheckFalse(card.Contains("胜利"), $"败北卡片不许出现「胜利」：{card}");
        _CheckNoHarvestBlocks(battle, "败北");
        CheckEq(inventory.money, money_before, "败北不发铜钱");
        CheckEq(game_state.party_exp, exp_before, "败北不给经验");
        CheckEq(inventory.equipment_count(), equips_before, "败北不掉装备（没有奖励入账）");
        CheckEq(string.Join(",", inventory.item_ids()), items_before, "败北不掉材料／消耗品");
        CheckEq(game_state.first_kill_count(), 0, "败北不记首杀（首杀只认打赢那一次）");
        foreach (Variant entry in game_state.char_ids)
        {
            string char_id = entry.AsString();
            CheckTrue(
                game_state.current_hp_of(char_id) < 0 && !game_state.is_wounded(char_id),
                $"败北后气血回满（没有 char_hp 记录 = 满血；{char_id} 读到 {game_state.current_hp_of(char_id)}）");
        }

        if (session != null)
        {
            CheckEq(session.active_field_buffs(-1).Count, 0, "败北清掉全部战斗外增益（08 第 ④ 条）");
            // 这一局从没进过城镇 → 退回大地图（「最近到过的出生点或城镇」为空时的兜底）
            CheckEq(session.last_shelter_scene, "", "这一局没到过城镇（用例前提）");
            CheckEq(
                session.pending_return_scene, "res://scenes/world_run.tscn",
                "没到过城镇 → 败北退回大地图");
        }

        CheckEq(battle.save_service().last_reason, "战斗结算", "败北也自动存档（设计 02：副本内自动存）");
        _DropBattle(battle);
    }

    /// <summary>
    /// 平局：回合打满（MAX_ROUNDS=50）时谁也没赢。这条分支以前**没有**——`_settle()` 只有
    /// 胜利／撤退／else，于是平局掉进 else、告诉玩家「败北」（假话），而 `last_battle.winner`
    /// 记的却是 "draw"。红名精英线现在常打到上限，这条挺现实。
    ///
    /// 练习战结算（设计 09 §3.3）：**没有掉落／铜钱／首杀／领悟**，经验按 `dummy_xp_cap_level`
    /// 封顶、熟练度按 `dummy_mastery_cap` 封顶——否则木桩就成了刷级/刷装备的永动机。
    /// </summary>
    private void _CheckPracticeSettle(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(PracticeDir, 3);
        store.ensure_dir();
        var game_state = (GameState)state;
        game_state.slot = 1;
        string char_id = game_state.char_ids[0].AsString();
        // 把角色抬到木桩的上限等级：这时练习战应当**一点经验都不给**
        game_state.char_levels[char_id] = PracticeService.cap_level(db);
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        var encounter = (Encounter)battle.encounter;
        encounter.practice = true; // ← 这一局是练习战

        var inventory = state.Get("inventory").As<Inventory>();
        int money_before = inventory.money;
        int exp_before = game_state.party_exp;
        int equips_before = inventory.equipment_count();
        string items_before = string.Join(",", inventory.item_ids());
        // 熟练度封顶：先把这一招练到练习战的上限（3 级），这一场再施放一次也不该涨到 4
        game_state.set_mastery(
            char_id, "sk_xuanwei_01", PracticeService.mastery_cap(db), PracticeService.mastery_cap(db));
        var reference_skill = (SkillActiveRow)db.get_row("skill_active", "sk_xuanwei_01");
        BattleSimulator sim = _Sim(battle);
        sim.act(_Actor(battle.allies[0]), reference_skill, _Actor(battle.enemies[0]));
        foreach (BattleActor enemy in _Actors(battle.enemies))
        {
            enemy.hp = 0;
        }

        bool won = sim.winner() == "ally";
        battle._settle();

        CheckTrue(won, $"把木桩打空算我方胜利（实际 {sim.winner()}）");
        string card = battle.result_label_text();
        CheckTrue(card.Contains("练习战"), $"结算卡片写清这是练习战：{card}");
        CheckEq(inventory.money, money_before, "练习战不发铜钱");
        CheckEq(game_state.party_exp, exp_before, "到顶之后练习战一点经验都不给");
        CheckEq(inventory.equipment_count(), equips_before, "练习战不掉装备");
        CheckEq(string.Join(",", inventory.item_ids()), items_before, "练习战不掉材料");
        CheckEq(game_state.first_kill_count(), 0, "练习战不记首杀");
        CheckEq(
            game_state.mastery_of(char_id, "sk_xuanwei_01"), PracticeService.mastery_cap(db),
            "练习战里熟练度卡在上限（已经 3 级，再施放一次也不涨）");
        Godot.Collections.Dictionary record = game_state.dungeon_record(encounter.source_scene);
        CheckFalse(_Arr(record, "rooms").Contains(encounter.source_key), "练习战不进副本完成度");
        _DropBattle(battle);
    }

    private void _CheckDrawSettle(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(DrawDir, 3);
        store.ensure_dir();
        var game_state = (GameState)state;
        game_state.slot = 1;
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        var inventory = state.Get("inventory").As<Inventory>();
        int money_before = inventory.money;
        int exp_before = game_state.party_exp;
        string items_before = string.Join(",", inventory.item_ids());
        int equips_before = inventory.equipment_count();
        BattleSimulator sim = _Sim(battle);
        // 回合上限压到 1、双方血量拉高：谁也打不死谁，必然打满判平局
        sim._options["max_rounds"] = 1;
        foreach (BattleActor actor in _Actors(battle.allies))
        {
            actor.hp = 9999;
        }

        foreach (BattleActor actor in _Actors(battle.enemies))
        {
            actor.hp = 9999;
        }

        battle.press_auto();
        int ticks = 0;
        while (!sim.finished() && ticks < 80)
        {
            battle._Process(0.8);
            ticks += 1;
        }

        CheckTrue(sim.finished(), $"有限次 tick 内打满回合上限（用了 {ticks} 次）");
        CheckEq(sim.winner(), "draw", "双方都活着打满上限 → 平局");
        string card = battle.result_label_text();
        CheckTrue(card.Contains("平局"), $"结算卡片写「平局」：{card}");
        CheckFalse(card.Contains("败北"), $"平局不许写成「败北」（玩家没输）：{card}");
        CheckFalse(card.Contains("胜利"), $"平局也不是胜利：{card}");
        _CheckNoHarvestBlocks(battle, "平局");
        CheckEq(inventory.money, money_before, "平局不发铜钱");
        CheckEq(game_state.party_exp, exp_before, "平局不给经验");
        CheckEq(inventory.equipment_count(), equips_before, "平局不掉装备");
        CheckEq(string.Join(",", inventory.item_ids()), items_before, "平局不掉材料");
        CheckEq(game_state.first_kill_count(), 0, "平局不记首杀");
        CheckEq(battle.save_service().last_reason, "战斗结算", "平局也自动存档（设计 02）");
        _DropBattle(battle);
    }

    /// <summary>
    /// 自动存档失败**不静默**（2026-10-03 补）：失败不打断结算（AGENTS：不许挡住开局/结算），
    /// 但战报里要有一句「自动存档失败：…（这一场的结果只在本局里）」——
    /// 玩家的运行环境真有可能写不进 `user://saves`（本工程的沙箱就出过 `user://` 写失败的事故）。
    /// </summary>
    private void _CheckAutosaveFailure(TableDb db)
    {
        GodotObject state = SoloState(db);
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = new FailingSaveStore();
        Tree.Root.AddChild(battle);
        battle.setup();
        CheckNotNull(battle.save_service(), "存档设施建得起来（注入的是写不进去的那个）");
        foreach (BattleActor ally in _Actors(battle.allies))
        {
            ally.hp = 0;
        }

        battle._settle();
        string log_text = battle._log.GetParsedText();
        CheckTrue(log_text.Contains("自动存档失败"), $"战报里如实写「自动存档失败」：{log_text}");
        CheckTrue(log_text.Contains("只在本局里"), "并说明后果：这一段的进度没存上");
        CheckTrue(battle.settled, "失败也不打断结算（卡片与按钮照常收尾）");
        _DropBattle(battle);
    }

    /// <summary>
    /// 以前首杀记录放在 GameSession 会话里，退出重进（= 读档）就能重复领；
    /// 这条走「真场景 → 真 `_settle()`」的路径，钉住「记录写进 GameState 并随存档走」。
    /// </summary>
    private void _CheckFirstKillOnce(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(FirstKillDir, 3);
        store.ensure_dir();
        int before = _CountInstances(state, "eq_sword_03");
        _SettleBoss(db, state, store);
        int after_first = _CountInstances(state, "eq_sword_03");
        CheckEq(after_first - before, 1, "第一次击败 Boss 拿到首杀固定珍品武器");
        CheckTrue(((GameState)state).has_first_kill("drop_bd_boss"), "首杀记录写进 GameState（不再留在会话内存）");

        _SettleBoss(db, state, store);
        CheckEq(_CountInstances(state, "eq_sword_03"), after_first, "同一存档再打一次不再给首杀武器");

        // 退出重进 = 真的落盘再读回，然后用读回来的状态再打一场
        Godot.Collections.Dictionary saved = store.save_slot(1, state);
        CheckTrue(saved["ok"].AsBool(), $"首杀后的存档能落盘：{saved}");
        Godot.Collections.Dictionary loaded = store.load_slot(1, db);
        GodotObject reloaded = loaded["state"].AsGodotObject();
        CheckTrue(
            reloaded != null && ((GameState)reloaded).has_first_kill("drop_bd_boss"),
            "读档后首杀记录仍在");
        _SettleBoss(db, reloaded, store);
        CheckEq(_CountInstances(reloaded, "eq_sword_03"), after_first, "退出重进后仍然不给首杀武器");
    }

    /// <summary>
    /// 真打两场紫名 Boss（大寨主 → 醉刀客）→ 都写进副本记录 →「绝境」难度解锁。
    ///
    /// 这条链以前只有**白盒**覆盖：`test_world_map` 与 `test_dungeon` 都是直接
    /// `state.record_dungeon(scene, "bosses", "en_hidden_drunk")` 把 Boss 塞进去，
    /// 而「打赢一场隐藏 Boss 会不会被记成 Boss」这一段没人验——它恰恰是绝境解锁的唯一入口。
    /// 醉刀客那场按 `local_map_controller._trigger_item` 的真实形状建 encounter（source_scene=场景、
    /// source_key=trig_wine、单只队伍），所以这条用例走的是和游戏里一样的结算路径。
    /// </summary>
    private void _CheckBossKillsUnlockNightmare(TableDb db, SaveStore store)
    {
        GodotObject state = SoloState(db);
        ((GameState)state).slot = 1;
        WorldMapService world_map = WorldMapService.create(db, state);
        CheckFalse(world_map.can_switch_difficulty("nightmare")["ok"].AsBool(), "新档：绝境锁着");
        CheckFalse(world_map.can_switch_difficulty("hard")["ok"].AsBool(), "新档：困难也锁着（要通关第一章）");

        _SettleNamedEncounter(db, state, store, "黑风寨大寨主", "en_bd_boss:1", "hf3_boss");
        Godot.Collections.Array after_boss = _Arr(((GameState)state).dungeon_record("scene_heifengzhai"), "bosses");
        CheckTrue(after_boss.Contains("en_bd_boss"), $"打赢大寨主被记成 Boss：{after_boss}");
        CheckTrue(world_map.can_switch_difficulty("hard")["ok"].AsBool(), "通关第一章后困难解锁");
        CheckFalse(world_map.can_switch_difficulty("nightmare")["ok"].AsBool(), "只有大寨主时绝境还锁着");

        _SettleNamedEncounter(db, state, store, "醉刀客", "en_hidden_drunk:1", "trig_wine");
        Godot.Collections.Array after_all = _Arr(((GameState)state).dungeon_record("scene_heifengzhai"), "bosses");
        CheckTrue(after_all.Contains("en_hidden_drunk"), $"打赢醉刀客被记成 Boss：{after_all}");
        CheckTrue(world_map.can_switch_difficulty("nightmare")["ok"].AsBool(), "两个 Boss 都真打完：绝境解锁");
    }

    /// <summary>
    /// 「打赢某支队伍 → 置旗标」（设计 20 §十一 那条链）：
    /// 荒村废屋的屠夫 → `flag_huangcun_done`（白清和的招募条件）；
    /// 毒堂的毒手 → `flag_poison_hall`（苏九娘的招募条件）。这两个旗标以前**一个来源都没有**，
    /// 于是那两条招募链整条断着（Q51）。
    /// </summary>
    private void _CheckTeamWinFlags(TableDb db, SaveStore store)
    {
        GodotObject state = SoloState(db);
        var game_state = (GameState)state;
        game_state.slot = 2;
        CheckFalse(game_state.has_flag("flag_huangcun_done"), "开局没处理过荒村的事");
        CheckFalse(game_state.has_flag("flag_poison_hall"), "开局也没处理过毒堂");
        _SettleNamedEncounter(
            db, state, store, "荒村屠夫", "en_bd_butcher:1", "hc_02", "scene_huangcun", "team_butcher");
        CheckTrue(game_state.has_flag("flag_huangcun_done"), "打赢屠夫 → 荒村的事算是了结了");
        CheckFalse(game_state.has_flag("flag_poison_hall"), "另一支队伍的旗标不受影响");
        _SettleNamedEncounter(
            db, state, store, "毒堂守卫", "en_bd_poison_hand:1", "hf2_poison",
            "scene_heifengzhai", "team_poison_hand");
        CheckTrue(game_state.has_flag("flag_poison_hall"), "打赢毒手 → 毒堂的事算是了结了");
        // 幕五「聚义厅对质」的进入条件（20 号 §十一：战大寨主后）
        _SettleNamedEncounter(
            db, state, store, "大寨主", "en_bd_boss:1", "hf3_boss", "scene_heifengzhai", "team_boss");
        CheckTrue(game_state.has_flag("flag_heifeng_confront"), "打赢大寨主 → 置 flag_heifeng_confront");
        // 账册（设计 20 §七）：**打赢大寨主那一下**才到手——前面两场打完时不该有，
        // 重复刷也不该再给一本（钥匙道具不能丢、也不在货架上，所以「已持有」＝「领过了」）。
        var inventory = state.Get("inventory").As<Inventory>();
        CheckEq(
            inventory.count("item_bd_ledger"), 1,
            "第一次打赢大寨主 → 账册进背包（设计 20 §七 的终局难题靠它）");
        _SettleNamedEncounter(
            db, state, store, "大寨主", "en_bd_boss:1", "hf3_boss", "scene_heifengzhai", "team_boss");
        CheckEq(inventory.count("item_bd_ledger"), 1, "重复刷大寨主不会再发第二本账册");
    }

    /// <summary>用黑风寨大寨主打一场（真场景、真结算）：直接把敌人打死再调 `_settle()`。</summary>
    private Godot.Collections.Dictionary _SettleBoss(TableDb db, GodotObject state, SaveStore store)
    {
        BattleScreen battle = _MakeBattle(state);
        battle.encounter_override = _BossEncounter();
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        foreach (BattleActor enemy in _Actors(battle.enemies))
        {
            enemy.hp = 0;
        }

        battle._settle();
        var lines = new Godot.Collections.Array();
        foreach (string line in battle.result_label_text().Split("\n", System.StringSplitOptions.None))
        {
            lines.Add(line);
        }

        var settled = new Godot.Collections.Dictionary
        {
            { "card", battle.result_label_text() },
            { "log", battle._log.GetParsedText() },
            { "lines", lines },
        };
        _DropBattle(battle);
        return settled;
    }

    private static Encounter _BossEncounter() => new()
    {
        team_name = "黑风寨大寨主",
        members = "en_bd_boss:1",
        difficulty_id = "normal",
        source_scene = "dungeon",
        source_key = "test_boss_room",
    };

    /// <summary>
    /// 建一场「小地图里打完的」战斗、把敌人打死、走真结算。成员串与 `enemy_team` 同格式。
    /// `scene_id`／`team_id` 可省（默认按黑风寨、不带队伍 id）——带 `team_id` 时才走
    /// 「打赢某支队伍 → 置旗标」那条（设计 20 §十一）。
    /// </summary>
    private void _SettleNamedEncounter(
        TableDb db, GodotObject state, SaveStore store, string team_name, string members, string source_key,
        string scene_id = "scene_heifengzhai", string team_id = "")
    {
        var encounter = new Encounter
        {
            team_name = team_name,
            members = members,
            difficulty_id = ((GameState)state).difficulty_id,
            source_scene = scene_id,
            source_key = source_key,
            team_id = team_id,
        };
        BattleScreen battle = _MakeBattle(state);
        battle.encounter_override = encounter;
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        foreach (BattleActor enemy in _Actors(battle.enemies))
        {
            enemy.hp = 0;
        }

        battle._settle();
        _DropBattle(battle);
    }

    private int _CountInstances(GodotObject state, string base_id)
    {
        var inventory = state.Get("inventory").As<Inventory>();
        int total = 0;
        foreach (string instance_id in inventory.equipment_ids())
        {
            if (inventory.base_of(instance_id) == base_id)
            {
                total += 1;
            }
        }

        return total;
    }

    private static Godot.Collections.Array _Arr(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotArray() : new Godot.Collections.Array();
}
