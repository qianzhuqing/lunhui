using Godot;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// `TestBattleUi` 的指令那一半：招式按钮（能用的可点、不能用的置灰并写明原因）、增益招式、
/// 六指令与增益减益面板、预兆与拆招、自动战斗（含持久开关）、敌人剪影「有图才摆」。
/// 拆文件只为不撞 800 行硬上限（施工单「二点九八」的做法）。
/// </summary>
public partial class TestBattleUi
{
    /// <summary>
    /// 战斗界面的招式按钮：能用的可点、不能用的**置灰并在按钮上写原因**。
    /// 以前没有任何用例找过 `SkillButton_*`（grep 全项目为零）——把 `disabled` 写死成 false、
    /// 或把原因从按钮文案里删掉，自检都不会红（体检表 `skill_options` 里标了不可用，但那是另一层）。
    /// 这类「体检表说不行、按钮却照旧能点」的洞本项目出现过（逃跑／买入按钮都是「按钮态跟着原因走」）。
    /// </summary>
    private void _CheckSkillButtonsUi(TableDb db)
    {
        GodotObject state = SoloState(db);
        var game_state = (GameState)state;
        string char_id = game_state.char_ids[0].AsString();
        // 装配两部招式：起手式（qi_cost=0）＋进步（要 8 点内力），两种按钮态都能看到
        game_state.set_loadout(
            char_id,
            new[] { "sk_xuanwei_01", "sk_xuanwei_02" },
            game_state.loadout_of(char_id)["passive"].AsStringArray());
        var store = SaveStore.create(SkillButtonsDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        battle.press_next_round(); // 推进到我方行动，招式行才会建出来
        BattleActor actor = _Sim(battle).current_actor() as BattleActor;
        CheckTrue(
            actor != null && actor.side == BattleActor.SIDE_ALLY,
            "轮到我方，招式行已建出来");
        var qi_button = battle.FindChild("SkillButton_sk_xuanwei_02", true, false) as Button;
        var free_button = battle.FindChild("SkillButton_sk_xuanwei_01", true, false) as Button;
        CheckNotNull(qi_button, "耗内力那部招式有按钮");
        CheckNotNull(free_button, "不耗内力那部招式有按钮");
        if (free_button != null)
        {
            CheckFalse(free_button.Disabled, $"不耗内力的招式可点：{free_button.Text}");
        }

        // 内力清零：耗内力的那部要置灰、并在按钮上写「内力不足」
        actor.qi = 0;
        battle._refresh();
        var qi_button_zero = battle.FindChild("SkillButton_sk_xuanwei_02", true, false) as Button;
        CheckNotNull(qi_button_zero, "内力清零后按钮还在（不是整行消失）");
        if (qi_button_zero != null)
        {
            CheckTrue(qi_button_zero.Disabled, "内力不足时置灰");
            CheckTrue(qi_button_zero.Text.Contains("内力不足"), $"按钮上写明原因：{qi_button_zero.Text}");
        }

        Godot.Collections.Dictionary refused = battle.press_skill("sk_xuanwei_02");
        CheckFalse(refused["ok"].AsBool(), $"绕过置灰硬按也要被拒：{_D(refused, "error")}");
        _DropBattle(battle);
    }

    /// <summary>
    /// 增益招式在真实战斗界面上：按钮上写「增益」（不是「倍率 0.0」），按下去给自己上 buff、不打敌人。
    /// 用的就是发行数据里那条醉里乾坤·醉步（以前它连按钮都没有，见框架说明决策 221）。
    /// </summary>
    private void _CheckSupportSkillButton(TableDb db)
    {
        GodotObject state = SoloState(db);
        var game_state = (GameState)state;
        string char_id = game_state.char_ids[0].AsString();
        game_state.learn_skill(char_id, "sk_drunk_zuibu");
        game_state.set_loadout(
            char_id,
            new[] { "sk_xuanwei_01", "sk_drunk_zuibu" },
            game_state.loadout_of(char_id)["passive"].AsStringArray());
        var store = SaveStore.create(SupportSkillDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        battle.press_next_round();
        BattleActor actor = _Sim(battle).current_actor() as BattleActor;
        CheckTrue(actor != null && actor.side == BattleActor.SIDE_ALLY, "轮到我方");
        var button = battle.FindChild("SkillButton_sk_drunk_zuibu", true, false) as Button;
        CheckNotNull(button, "增益招式有按钮（以前没有）");
        if (button != null)
        {
            CheckFalse(button.Disabled, $"能按（原因：{button.TooltipText}）");
            CheckTrue(button.Text.Contains("增益"), $"按钮上写明是增益：{button.Text}");
            CheckFalse(button.Text.Contains("倍率 0.0"), $"不再显示「倍率 0.0」：{button.Text}");
            BattleActor enemy = _Actor(battle.enemies[0]);
            int enemy_hp = enemy.hp;
            button.EmitSignal(Button.SignalName.Pressed);
            CheckTrue(_Actor(battle.allies[0]).has_buff("buff_zuibu"), "按下后自己拿到忘忧");
            CheckEq(enemy.hp, enemy_hp, "没打敌人（增益招式不走伤害管线）");
        }

        _DropBattle(battle);
    }

    /// <summary>
    /// 六指令在真实界面上的表现：普通攻击／内功／道具／主动防御四个按钮，加上增益减益面板。
    /// （招式与逃跑分别由 `_CheckSkillButtonsUi` 与 `_CheckFleeUi` 盖住。）
    /// </summary>
    private void _CheckCommandUi(TableDb db)
    {
        GodotObject state = SoloState(db);
        var game_state = (GameState)state;
        string char_id = game_state.char_ids[0].AsString();
        // 装一部内功：内功指令才有东西可列（引气：运功＝它的内力上限加成翻倍）
        game_state.set_loadout(
            char_id,
            game_state.loadout_of(char_id)["active"].AsStringArray(),
            new[] { "pf_xuanwei_01" });
        var store = SaveStore.create(CommandUiDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        battle.press_next_round();
        BattleActor actor = _Sim(battle).current_actor() as BattleActor;
        CheckTrue(
            actor != null && actor.side == BattleActor.SIDE_ALLY,
            "轮到我方，六指令有行动对象");

        foreach (string node_name in new[] { "BasicAttackButton", "InnerButton", "ItemButton", "DefendButton" })
        {
            CheckNotNull(battle.FindChild(node_name, true, false), $"指令按钮 {node_name} 在场景里");
        }

        // 道具指令：把 `use_context=battle` 的道具列出来，并如实写「效果列未配」（不假装能用）
        battle.press_item_list();
        var potion = battle.FindChild("ItemButton_item_potion_small", true, false) as Button;
        CheckNotNull(potion, "道具指令列出战斗道具（金创药）");
        if (potion != null)
        {
            CheckTrue(potion.Disabled, "效果列未配 → 道具置灰");
            CheckTrue(potion.Text.Contains("效果"), $"按钮上写明原因：{potion.Text}");
        }

        battle.press_item_list(); // 切回招式

        // 内功指令：切出已装内功的按钮 → 催动 → 拿到运功 buff
        battle.press_inner_list();
        var inner_skill = battle.FindChild("InnerButton_pf_xuanwei_01", true, false) as Button;
        CheckNotNull(inner_skill, "内功指令列出已装内功");
        if (inner_skill != null)
        {
            // 内力消耗取自 `skill_passive.qi_cost`（Q87 落表）——按钮上的数要对得上表，不再写「未配」
            int qi_cost = ((SkillPassiveRow)db.get_row("skill_passive", "pf_xuanwei_01")).qi_cost;
            CheckTrue(
                inner_skill.Text.Contains($"内力 {qi_cost}"),
                $"按钮上如实写催动的内力消耗（表里 {qi_cost}）：{inner_skill.Text}");
            inner_skill.EmitSignal(Button.SignalName.Pressed);
        }

        BattleActor ally = _Actor(battle.allies[0]);
        CheckTrue(ally.has_buff("buff_yunqi"), "催动内功后拿到运功 buff");
        var yunqi_chip = battle.FindChild($"Buff_{ally.actor_id}_buff_yunqi", true, false) as Button;
        CheckNotNull(yunqi_chip, "增益面板列出运功");
        if (yunqi_chip != null)
        {
            CheckTrue(yunqi_chip.Text.Contains("增"), $"增益用「增」字标极性：{yunqi_chip.Text}");
            CheckTrue(yunqi_chip.Text.Contains("▲"), $"增益还带形状（色盲可辨，不只看红蓝）：{yunqi_chip.Text}");
            CheckTrue(yunqi_chip.Text.Contains("3"), $"标出剩余回合数：{yunqi_chip.Text}");
        }

        // 主动防御：上 buff 后面板上能悬浮看到它给了什么
        var defend = battle.FindChild("DefendButton", true, false) as Button;
        CheckNotNull(defend, "有主动防御按钮");
        if (defend != null)
        {
            CheckFalse(defend.Disabled, $"能按（原因：{defend.TooltipText}）");
            defend.EmitSignal(Button.SignalName.Pressed);
        }

        CheckTrue(ally.has_buff("buff_guard"), "防御按钮上了防御姿态");
        var chip = battle.FindChild($"Buff_{ally.actor_id}_buff_guard", true, false) as Button;
        CheckNotNull(chip, "增益减益面板出现防御姿态");
        if (chip != null)
        {
            CheckTrue(chip.Text.Contains("防御姿态"), $"条目写清名字：{chip.Text}");
            CheckTrue(
                chip.TooltipText.Contains("外防") || chip.TooltipText.Contains("减伤"),
                $"悬浮说明给数值：{chip.TooltipText}");
        }

        // 普通攻击：按下就掉血，而且不写进招式使用记录
        BattleActor victim = _Actor(battle.enemies[0]);
        int hp_before = victim.hp;
        var basic = battle.FindChild("BasicAttackButton", true, false) as Button;
        CheckNotNull(basic, "有普通攻击按钮");
        if (basic != null)
        {
            basic.EmitSignal(Button.SignalName.Pressed);
        }

        CheckLt(victim.hp, hp_before, $"普通攻击按钮真的打出了伤害（{hp_before} → {victim.hp}）");
        CheckTrue(
            _Sim(battle).skill_uses(ally.actor_id).Count == 0,
            "普通攻击不算武学（隐藏内容的招式使用判定不该被它污染）");

        // 08 的 UI 要求还没钉住的几条：**合并展示**（异常状态也进同一条列表）、
        // 减益形状、层数 ×N、常驻 ∞、以及固定排序（减益在前）
        string label_ally = ally.actor_id;
        string label_enemy = victim.actor_id;
        ally.add_buff("buff_rusty_moon", "equip", "eq_sword_04"); // duration=0 → 常驻
        // 异常状态（DoT）：叠两层，才能验「可叠层的显示 ×N」
        victim.add_status("poison", "test", 5.0, 5, 3, "", true);
        victim.add_status("poison", "test", 5.0, 5, 3, "", true);
        battle._refresh();
        var rusty = battle.FindChild($"Buff_{label_ally}_buff_rusty_moon", true, false) as Button;
        CheckNotNull(rusty, "常驻 buff 也进面板");
        if (rusty != null)
        {
            CheckTrue(rusty.Text.Contains("∞"), $"常驻显示 ∞（不是 0 回合）：{rusty.Text}");
        }

        var poison = battle.FindChild($"Buff_{label_enemy}_poison", true, false) as Button;
        CheckNotNull(poison, "异常状态进同一条合并列表（status_effect 也在面板上）");
        if (poison != null)
        {
            CheckTrue(poison.Text.Contains("▼") && poison.Text.Contains("减"), $"减益用 ▼／减 标极性：{poison.Text}");
            CheckTrue(poison.Text.Contains("中毒"), $"写中文名而不是 status_id：{poison.Text}");
            CheckTrue(poison.Text.Contains("×2"), $"标出层数：{poison.Text}");
            CheckTrue(poison.Text.Contains("敌"), $"合并列表里写清挂在谁身上：{poison.Text}");
            CheckTrue(poison.TooltipText.Contains("减益"), $"悬浮说明给极性：{poison.TooltipText}");
        }

        // 排序：减益在前（玩家先看到危机），同类按剩余回合升序——这里比两个条目的先后
        var panel = battle.FindChild("BuffPanel", true, false) as HFlowContainer;
        CheckNotNull(panel, "增益减益面板是场景节点");
        if (panel != null && poison != null && rusty != null)
        {
            CheckLt(
                poison.GetIndex(), rusty.GetIndex(),
                $"减益排在增益前面（{poison.GetIndex()} < {rusty.GetIndex()}）");
        }

        _DropBattle(battle);
    }

    /// <summary>预兆与拆招在真实界面上的表现：敌人卡片有预兆行与拆招按钮，按下去播报并改预兆行。</summary>
    private void _CheckParryUi(TableDb db)
    {
        GodotObject state = SoloState(db);
        var store = SaveStore.create(ParryUiDir, 3);
        store.ensure_dir();
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();
        battle.press_next_round();

        // 架势拉低一点才看得出「反涨」（满架势时被上限夹住 +0 是预期行为）
        BattleActor ally = _Actor(battle.allies[0]);
        ally.poise = 10;
        battle._refresh();
        BattleActor actor = _Sim(battle).current_actor() as BattleActor;
        CheckTrue(
            actor != null && actor.side == BattleActor.SIDE_ALLY,
            "第 1 回合预兆已给，轮到我方行动");
        BattleActor enemy = _Actor(battle.enemies[0]);
        var intent = battle.FindChild($"Intent_{enemy.actor_id}", true, false) as Label;
        CheckNotNull(intent, "敌人卡片上有预兆行");
        if (intent != null)
        {
            CheckTrue(intent.Text.Contains("预兆："), $"预兆行写清出什么招：{intent.Text}");
        }

        var parry = battle.FindChild($"ParryButton_{enemy.actor_id}", true, false) as Button;
        CheckNotNull(parry, "敌人卡片上有拆招按钮");
        if (parry != null)
        {
            CheckFalse(parry.Disabled, $"轮到我方时拆招按钮可用（原因：{parry.TooltipText}）");
            // 能拆时把**效果**说清楚（三条都是已实现的行为，别让玩家靠试）；
            // 打折的成数**现算**自 `combat_const.parry_damage_mult`，改表这里跟着变。
            double parry_mult = ((CombatConstRow)db.get_row("combat_const", "parry_damage_mult")).value;
            CheckTrue(
                parry.TooltipText.Contains($"{parry_mult * 100:F0}%"),
                $"能拆时说明伤害打几折（表里 {parry_mult * 100:F0}%）：{parry.TooltipText}");
            CheckTrue(parry.TooltipText.Contains("架势"), $"能拆时说明「反涨架势」：{parry.TooltipText}");
            int poise_before = ally.poise;
            parry.EmitSignal(Button.SignalName.Pressed);
            CheckTrue(battle.status_text().Contains("拆招"), $"状态栏播报拆招：{battle.status_text()}");
            CheckGt(
                ally.poise, poise_before,
                $"拆招后我方架势反涨（{poise_before} → {ally.poise}）");
            var after = battle.FindChild($"Intent_{enemy.actor_id}", true, false) as Label;
            CheckNotNull(after, "拆招后预兆行还在");
            if (after != null)
            {
                CheckTrue(after.Text.Contains("已被拆招"), $"预兆行标出已被拆招：{after.Text}");
            }
        }

        _DropBattle(battle);
    }

    /// <summary>
    /// 自动战斗：按下去要能**自己打到底**。
    ///
    /// 曾经的实现是「`_process` 里反复 `press_next_round()`」，而 `press_next_round()` 只推进到
    /// 「轮到我方」就停（那是给手选招式用的）——所以自动战斗只有敌方回合会自动跑，
    /// 轮到玩家就永远卡住（`--battle-selftest` 的注释里写着「曾经把自检挂死」，但游戏里的
    /// 自动战斗按钮一直踩这个坑）。这条用例按固定种子把它钉死：有限次 tick 内必须打完并获胜。
    /// </summary>
    private void _CheckAutoBattle(TableDb db)
    {
        GodotObject state = SoloState(db);
        var game_state = (GameState)state;
        // 等级拉满：这里验的是「自动战斗能不能自己推进」，不是平衡
        foreach (Variant entry in game_state.char_ids)
        {
            game_state.append_level(entry.AsString(), 19);
        }

        var store = SaveStore.create(AutoBattleDir, 3);
        store.ensure_dir();
        game_state.slot = 1;
        BattleScreen battle = _MakeBattle(state);
        battle.save_store_override = store;
        Tree.Root.AddChild(battle);
        battle.setup();

        battle.press_auto();
        CheckTrue(battle.auto_running(), "按下自动战斗后进入自动状态");
        Sfx.clear_log();
        int ticks = 0;
        BattleSimulator sim = _Sim(battle);
        while (!sim.finished() && ticks < 200)
        {
            // AUTO_INTERVAL = 0.8：一次 tick 至少够触发一次出手
            battle._Process(0.8);
            ticks += 1;
        }

        CheckTrue(sim.finished(), $"自动战斗能自己打到结束（用了 {ticks} 次 tick）");
        CheckEq(sim.winner(), "ally", $"自动战斗打赢了弱队（现实现在 {sim.winner()}）");
        CheckGt(sim.rounds_played(), 1.0, $"真的打了多个回合（{sim.rounds_played()}）");
        CheckFalse(battle.auto_running(), "打完后自动状态自己关掉");
        CheckTrue(battle.result_label_text().Contains("胜利"), $"打完有结算卡片：{battle.result_label_text()}");
        CheckTrue(Sfx.has_played("victory"), "胜利结算会请求「胜利」音效（07 §8.4）");
        _DropBattle(battle);
    }

    /// <summary>
    /// 自动战斗的**持久开关**（设计 11 §四）：设置里开着 → 进战斗就自动；
    /// 玩家点任意指令 → **立刻接管**（自动是省事不是夺权）。
    /// </summary>
    private void _CheckAutoBattleSetting(TableDb db)
    {
        const string dir = AutoBattleSettingDir;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        SettingsStore store = SettingsStore.create(dir);
        DirAccess.RemoveAbsolute(store.file_path());

        // ① 设置里关着（默认）：进战斗不自动
        BattleScreen off = _MakeBattle(SoloState(db));
        off.settings_override = store;
        Tree.Root.AddChild(off);
        off.setup();
        CheckFalse(off.auto_running(), "设置关着时进战斗不自动（默认关）");
        _DropBattle(off);

        // ② 设置里开着：进战斗即自动
        store.save_auto_battle(true);
        BattleScreen on = _MakeBattle(SoloState(db));
        on.settings_override = store;
        Tree.Root.AddChild(on);
        on.setup();
        CheckTrue(on.auto_running(), "设置开着时进战斗就自动");
        // ③ 玩家点一条指令 → 立刻接管
        _Sim(on).begin_round();
        bool took = on.take_over();
        CheckTrue(took, "点指令时确实收回了自动（take_over 返回 true）");
        CheckFalse(on.auto_running(), "点任意指令后不再是自动（不用先关自动）");
        // 再点一次没有副作用（已经手动）
        CheckFalse(on.take_over(), "已经手动时再调用不重复动作");
        _DropBattle(on);
    }

    /// <summary>
    /// 敌人剪影（15 §六：`assets/sprites/&lt;faction&gt;/&lt;enemy_id&gt;.png`）：**有图才摆**。
    ///
    /// 美术先出了两条做对照（山寨喽啰／别派弟子），其余 13 个敌人还没出——
    /// 所以这里同时钉两件事：**有图的那一行挂着图**、**没图的那一行不留空位**。
    /// 以前这两张图躺在库里没人读（"先出图、后接线"），这一条就是那条接线的证据。
    /// </summary>
    private void _CheckEnemySprites(TableDb db)
    {
        CheckEq(
            IconPaths.actor("bandit", "en_bd_thug"),
            "res://assets/sprites/bandit/en_bd_thug.png",
            "剪影路径按 `<faction>/<enemy_id>` 拼（15 §六）");
        var encounter = new Encounter
        {
            team_name = "混合队",
            members = "en_bd_thug:1;en_wolf:1",
            source_scene = "overworld",
            source_key = "test_enemy_sprites",
        };
        BattleScreen battle = _MakeBattle(SoloState(db));
        battle.encounter_override = encounter;
        Tree.Root.AddChild(battle);
        battle.setup();
        Godot.Collections.Array<Node> thug_rows = battle.FindChildren("Actor_en_bd_thug*", "VBoxContainer", true, false);
        CheckEq(thug_rows.Count, 1, "山寨喽啰那一行在（美术已交付剪影）");
        if (thug_rows.Count == 1)
        {
            CheckEq(
                thug_rows[0].FindChildren("Sprite_*", "TextureRect", true, false).Count, 1,
                "有剪影的敌人行前面挂着图");
        }

        Godot.Collections.Array<Node> wolf_rows = battle.FindChildren("Actor_en_wolf*", "VBoxContainer", true, false);
        CheckEq(wolf_rows.Count, 1, "野狼那一行在（美术还没出它的剪影）");
        if (wolf_rows.Count == 1)
        {
            CheckEq(
                wolf_rows[0].FindChildren("Sprite_*", "TextureRect", true, false).Count, 0,
                "没图的敌人行不留空位");
        }

        _DropBattle(battle);
    }

    private static string _D(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
