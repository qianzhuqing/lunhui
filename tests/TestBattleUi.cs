using Godot;
using Lunhui.Autoload;
using Lunhui.Audio;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 战斗界面：真实场景、真实按钮。对位 `tests/test_battle_ui.gd`
/// （2026-10-11 P4 第四十三批整体搬 C#，按旧链顺序拆三份 partial：本文件是骨架与走查／
/// `TestBattleUiSettles.cs` 管结算那几支／`TestBattleUiCommands.cs` 管指令按钮那一支）。
///
/// 这一份把 `battle_screen.tscn` 挂进真场景树、按真按钮、读真文案，钉住那些「数据层绿、
/// 界面却在说假话」的口径：结算卡片最多三行且放不下的必须进战报、败北／平局／练习战三支
/// 各自的零收获、逃跑按钮三态与成功率现算、招式按钮置灰必须写明原因、跳字挂在浮层上、
/// 三种战斗资源条的读数跟着单位走、自动战斗能自己打到底。
/// </summary>
public partial class TestBattleUi : TestCase
{
    private const string BattleScene = "res://scenes/battle_screen.tscn";
    private const int Seed = 20261003;

    // 存档目录沿用旧链那几个（两条链不同时跑，路径不必改名）。
    private const string RunBattleDir = "res://.logs/test_save_timing/run_battle";
    private const string SettleCardDir = "res://.logs/test_save_timing/settle_card";
    private const string FleeUiDir = "res://.logs/test_save_timing/flee_ui";
    private const string DefeatDir = "res://.logs/test_save_timing/defeat";
    private const string PracticeDir = "res://.logs/test_save_timing/practice";
    private const string DrawDir = "res://.logs/test_save_timing/draw";
    private const string SkillButtonsDir = "res://.logs/test_save_timing/skill_buttons";
    private const string SupportSkillDir = "res://.logs/test_save_timing/support_skill";
    private const string CommandUiDir = "res://.logs/test_save_timing/command_ui";
    private const string ParryUiDir = "res://.logs/test_save_timing/parry_ui";
    private const string AutoBattleDir = "res://.logs/test_save_timing/auto_battle";
    private const string FirstKillDir = "res://.logs/test_save_timing/first_kill";
    private const string AutoBattleSettingDir = "res://.logs/test_settings/battle_auto";

    public override string SuiteName() => "战斗界面";

    public override void Run()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树");
            return;
        }

        TableDb db = GetDb();
        // 战斗界面的手感／文案闸门常量（变异探针点名「没人钉」的四条）——写死绝对值。
        // 2026-10-11 搬 C# 后 GDScript **读不到 C# 的 `const`**，所以这四个走取值口（坑⑩）。
        CheckFloat(BattleScreen.auto_interval(), 0.8, "自动战斗的推进间隔 0.8 秒", 0.0001);
        CheckEq(BattleScreen.selftest_seed(), 20261003, "战斗自检的固定种子（决策 236：不固定就会时红时绿）");
        CheckEq(BattleScreen.settle_drop_items(), 3, "结算卡片最多列 3 件掉落");
        CheckEq(BattleScreen.settle_drop_max_chars(), 48, "结算卡片掉落行最多 48 字");

        GodotObject state = SoloState(db);
        BattleScreen battle = _MakeBattle(state);
        // 打完自动存档：给个槽位与临时目录，验证结算真的落盘
        var store = SaveStore.create(RunBattleDir, 3);
        store.ensure_dir();
        ((GameState)state).slot = 1;
        battle.save_store_override = store;
        var returns = new Godot.Collections.Array();
        battle.return_handler = Callable.From(() => returns.Add(true));
        // 这一场要**两个以上敌人**（下面 `_CheckRounds` 会点 `enemies[1]` 选目标），
        // 又要 1 级单人打得过。默认回退遭遇是**野狼群**（3 只）：0.28.0 把敌人命中基准统一成
        // 1.0 之后（设计 10 §七），1 级单人对它的余量只剩 33%，而这条用例是**脚本化的手打**
        // （选目标、切自动战斗、只按第一个招式），比模拟器的自动策略笨，于是翻成败北。
        // 它不是难度断言、是界面走查，所以打**两只**野狼（决策 287）。
        battle.encounter_override = new Encounter
        {
            team_name = "野狼",
            members = "en_wolf:2",
            source_scene = "overworld",
            source_key = "test_two_wolves",
        };
        Tree.Root.AddChild(battle);
        battle.setup();

        _CheckLayout(battle);
        _CheckRounds(battle);
        _CheckStatusUi(battle);
        _CheckEffectIcons(db, battle);
        _CheckResourceBars(battle);
        _CheckFinish(battle, returns);
        // 结算时自动存档的原因要在 free 之前取出来（对象释放后就问不到了）
        string settle_reason = battle.save_service().last_reason;

        _DropBattle(battle);
        _CheckFirstKillOnce(db);
        _CheckBossKillsUnlockNightmare(db, store);
        _CheckTeamWinFlags(db, store);
        _CheckAutoBattle(db);
        _CheckAutoBattleSetting(db);
        _CheckParryUi(db);
        _CheckFleeUi(db);
        _CheckDefeatSettle(db);
        _CheckPracticeSettle(db);
        _CheckDrawSettle(db);
        _CheckSkillButtonsUi(db);
        _CheckSupportSkillButton(db);
        _CheckCommandUi(db);
        _CheckSettleCardContract(db);
        _CheckAutosaveFailure(db);
        CheckTrue(store.slot_exists(1), "战斗结算后自动落盘");
        CheckEq(settle_reason, "战斗结算", "记下存档原因（设计 02：副本内自动存）");

        _CheckEnemySprites(db);
    }

    // ---------------------------------------------------------------- 骨架走查

    private void _CheckLayout(BattleScreen battle)
    {
        CheckGt(battle.allies.Count, 0.0, "我方有战斗单位");
        CheckGt(battle.enemies.Count, 0.0, "敌方有战斗单位");
        var headline = battle.FindChild("Headline", true, false) as Label;
        CheckNotNull(headline, "有开场文案");
        if (headline != null)
        {
            CheckTrue(
                headline.Text.Contains("遭遇") || headline.Text.Contains("偷袭"),
                $"文案说明接触方式：{headline.Text}");
        }

        CheckNotNull(battle.FindChild("Log", true, false), "有战斗日志");
        var flee = battle.FindChild("FleeButton", true, false) as Button;
        CheckNotNull(flee, "有逃跑按钮");
        if (flee != null)
        {
            CheckTrue(flee.Disabled, $"开打前（还没轮到我方）逃跑置灰：{flee.TooltipText}");
        }

        var actor_label = battle.FindChild($"Name_{_Actor(battle.allies[0]).actor_id}", true, false) as Label;
        CheckNotNull(actor_label, "我方面板列出战斗单位");
        if (actor_label != null)
        {
            CheckTrue(actor_label.Text.Contains("气血"), $"显示气血：{actor_label.Text}");
        }

        _CheckSceneLayout(battle);
    }

    /// <summary>
    /// 界面骨架必须是 `scenes/battle_screen.tscn` 里的真实节点（本轮从代码搭建改成 .tscn），
    /// 卡片行仍然按战斗状态动态生成，但要挂在场景里那两个列表节点下。
    /// （「整页塞得进设计分辨率」由真实场景自检 `--battle-selftest` 断言：那里才跑过帧、版式算得出来）
    /// </summary>
    private void _CheckSceneLayout(BattleScreen battle)
    {
        CheckNotNull(battle.GetNodeOrNull("Margin/Column/Headline"), "Headline 是场景节点");
        CheckNotNull(battle.GetNodeOrNull("Margin/Column/Buttons/NextRoundButton"), "按钮是场景节点");
        const string enemy_list_path = "Margin/Column/Front/TeamEnemies/EnemyScroll/ListEnemies";
        CheckNotNull(battle.GetNodeOrNull(enemy_list_path), "敌方列表是场景节点");
        var scroll = battle.GetNode<ScrollContainer>("Margin/Column/Front/TeamEnemies/EnemyScroll");
        Node enemy_list = battle.GetNode(enemy_list_path);
        CheckGt(scroll.CustomMinimumSize.Y, 0.0, "敌方列表留了滚动区高度");
        var pick = battle.FindChild($"TargetButton_{_Actor(battle.enemies[0]).actor_id}", true, false) as Button;
        CheckNotNull(pick, "敌人卡片有选目标按钮");
        if (pick != null)
        {
            CheckTrue(enemy_list.IsAncestorOf(pick), "动态卡片挂在场景的敌方列表下");
        }
    }

    /// <summary>异常状态在战斗界面上的表现：卡片上有状态行、持续伤害跳字用状态颜色。</summary>
    private void _CheckStatusUi(BattleScreen battle)
    {
        BattleActor target = _Actor(battle.enemies[0]);
        target.add_status("poison", "test", 7.0, 5, 3, "", true);
        battle._refresh();
        var label = battle.FindChild($"Status_{target.actor_id}", true, false) as Label;
        CheckNotNull(label, "卡片上有异常状态行");
        if (label != null)
        {
            CheckTrue(label.Text.Contains("中毒"), $"状态行写清是中毒：{label.Text}");
            CheckTrue(label.Text.Contains("×1"), $"状态行写清层数：{label.Text}");
            CheckEq(label.GetThemeColor("font_color"), new Color("7fd35a"), "中毒用伤害类型的颜色");
        }

        // 持续伤害跳字：颜色跟着状态走
        battle._spawn_floats(new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary
            {
                { "kind", "dot" }, { "target", target.actor_id }, { "damage", 12 }, { "status_id", "poison" },
            },
        });
        var float_label = battle.FindChild($"Float_{target.actor_id}", true, false) as Label;
        CheckNotNull(float_label, "持续伤害也有跳字");
        if (float_label != null)
        {
            CheckEq(float_label.GetThemeColor("font_color"), new Color("7fd35a"), "毒伤跳字用中毒色");
            float_label.QueueFree();
        }

        target.statuses = new Godot.Collections.Array();
        battle._refresh();

        // 多段跳字要错开位置，不然两段数字叠在一起
        battle._spawn_floats(new Godot.Collections.Array
        {
            _HitEvent(target.actor_id, 10, 0, 2),
            _HitEvent(target.actor_id, 12, 1, 2),
        });
        var floats = new System.Collections.Generic.List<Label>();
        // 跳字挂在 FloatLayer 上（不受卡片容器排版），所以要满场景找
        foreach (Node node in battle.FindChildren("Float_*", "Label", true, false))
        {
            // 前一段用例 queue_free 过的还在树上（要等帧末才真的走），别数进来
            if (node.IsQueuedForDeletion())
            {
                continue;
            }

            floats.Add((Label)node);
        }

        CheckEq(floats.Count, 2, "两段各有一个跳字");
        if (floats.Count == 2)
        {
            CheckNe(
                floats[0].Position.X, floats[1].Position.X,
                $"第二段跳字横向错开：{floats[0].Position} vs {floats[1].Position}");
            // 跳字必须挂在**不受容器排版**的浮层上：以前挂在卡片里，VBoxContainer 每次排版
            // 都把它按到卡片左边、半个数字被滚动区裁掉（测试全绿但画面是坏的）。
            // 版式要跑过帧才算得出来，所以这里钉结构、真实位置交给 --battle-selftest 与截图。
            CheckEq(floats[0].GetParent().Name.ToString(), "FloatLayer", "跳字挂在浮层上");
            var card = battle.FindChild($"Actor_{target.actor_id}", true, false) as Control;
            CheckNotNull(card, "目标卡片在");
            if (card != null)
            {
                CheckFalse(card.IsAncestorOf(floats[0]), "跳字不再挂在卡片里（那样会被容器顶到左边）");
            }
        }

        // 浮层有尺寸时，落点必须被**夹进浮层**——这是**护栏测试**，不是复现线上问题：
        // 真窗口实测（`--battle-selftest` 带窗口跑，决策 199）跳字落点是 x≈610（敌卡）／232（我方卡），
        // **今天不会出界**；但落点是"卡片位置 + 210 + 26×段数"这种写死的算术，没人拦着它以后变。
        // **headless 里卡片没有真实坐标**（容器还没排版）→ 不夹取也照样"在界内"，那是假绿；
        // 所以这里手动把卡片摆到右边缘，让落点**明确越界**再验（写完先反向验证过一次才发现，见决策 199）。
        var layer = battle.FindChild("FloatLayer", true, false) as Control;
        var clamp_card = battle.FindChild($"Actor_{target.actor_id}", true, false) as Control;
        if (layer != null && clamp_card != null)
        {
            Vector2 saved_card_pos = clamp_card.Position;
            layer.Size = new Vector2(1152, 648);
            clamp_card.Position = new Vector2(1100, 120);
            // 用 hit_index=4 起个不重名的（前几段跳字还挂在树上等帧末，同名会被引擎改名）；
            // 4 也是**表里真有的**最大段序号（`skill_active.hit_count` 最大 5 → 序号 0..4），
            // 所以这条夹具对应「五段招式打在右列敌人身上」，不是造一个数据到不了的场景。
            battle._spawn_floats(new Godot.Collections.Array { _HitEvent(target.actor_id, 9, 4, 0) });
            var placed = battle.FindChild($"Float_{target.actor_id}_4", true, false) as Label;
            CheckNotNull(placed, "夹取用的跳字建出来了");
            if (placed != null)
            {
                CheckTrue(
                    placed.Position.X >= 0.0 && placed.Position.X + placed.Size.X <= 1152.5,
                    $"卡片贴右边缘时跳字仍被夹在界内（x={placed.Position.X:F0} 宽={placed.Size.X:F0}）");
                placed.QueueFree();
            }

            clamp_card.Position = saved_card_pos;
        }

        foreach (Label node in floats)
        {
            node.QueueFree();
        }

        // 阵亡：卡片压暗 + 名字标「（倒）」
        target.hp = 0;
        battle._refresh();
        var dead_label = battle.FindChild($"Name_{target.actor_id}", true, false) as Label;
        CheckNotNull(dead_label, "阵亡单位还在名单上");
        if (dead_label != null)
        {
            CheckTrue(dead_label.Text.Contains("倒"), $"阵亡标「（倒）」：{dead_label.Text}");
            var row = battle.FindChild($"Actor_{target.actor_id}", true, false) as Control;
            CheckNotNull(row, "阵亡单位卡片还在");
            if (row != null)
            {
                CheckLt(row.Modulate.A, 0.6, $"阵亡卡片压暗（alpha {row.Modulate.A:F2}）");
            }
        }

        // 出手动作：出手者的卡片会闪一下暖光（挂在容器里不能动位置，容器会顶掉补间）
        BattleActor ally = _Actor(battle.allies[0]);
        var attacker_row = battle.FindChild($"Actor_{ally.actor_id}", true, false) as Control;
        CheckNotNull(attacker_row, "出手者的卡片在（不然出手动画那几条会静默跳过）");
        if (attacker_row != null)
        {
            battle._lunge_attackers(new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "kind", "hit" }, { "source", ally.actor_id }, { "target", target.actor_id },
                    { "damage", 5 },
                },
            });
            CheckGt(attacker_row.Modulate.R, 1.0, $"出手者闪暖光（r={attacker_row.Modulate.R:F2}）");
            // 分帧：第一帧是「蓄势」——先压一下再探身出去（补间跑起来之前就能看到）
            CheckLt(attacker_row.Scale.X, 1.0, $"出手第一帧是蓄势压一下（scale={attacker_row.Scale.X:F2}）");
            CheckGt(attacker_row.PivotOffset.X, 0.0, "缩放绕卡片中心，不是从左上角涨");
        }

        // 受击分帧：被打的那一方先被压扁再弹回（用还活着的 2 号敌人，别借刚做阵亡断言的那个）
        BattleActor victim = _Actor(battle.enemies[1]);
        var victim_row = battle.FindChild($"Actor_{victim.actor_id}", true, false) as Control;
        CheckNotNull(victim_row, "受击者的卡片在");
        if (victim_row != null)
        {
            battle._spawn_floats(new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "kind", "hit" }, { "source", ally.actor_id }, { "target", victim.actor_id },
                    { "damage", 7 },
                },
            });
            CheckLt(victim_row.Scale.X, 1.0, $"受击第一帧被压扁（scale={victim_row.Scale.X:F2}）");
            CheckGt(victim_row.Modulate.R, 1.0, "受击也闪一下");
            foreach (Node node in battle._enemies_box.GetChildren())
            {
                foreach (Node child in node.GetChildren())
                {
                    if (child is Label child_label && child_label.Name.ToString().StartsWith("Float_"))
                    {
                        child_label.QueueFree();
                    }
                }
            }
        }

        // 濒死提示：血量 ≤25% 时名字转红并标「危急」
        ally.hp = Mathf.Max(1, (int)(ally.max_hp() * 0.2));
        battle._refresh();
        var hurt = battle.FindChild($"Name_{ally.actor_id}", true, false) as Label;
        CheckNotNull(hurt, "我方卡片还在");
        if (hurt != null)
        {
            CheckTrue(hurt.Text.Contains("危急"), $"濒死标注：{hurt.Text}");
        }

        // 结算的掉落明细：物品与装备都写名字（装备带词条）
        var inventory = battle.state.Get("inventory").As<Inventory>();
        string fake_instance = inventory.add_equipment(
            (TableDb)battle.db, "eq_sword_01",
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "affix_id", "af_str_01" }, { "target", "attr:str" },
                    { "value_kind", "attr_point" }, { "value", 3.0 },
                },
            });
        string described = battle._describe_drops(new Godot.Collections.Dictionary
        {
            {
                "items", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary { { "item_id", "item_herb" }, { "qty", 2 } },
                }
            },
            { "equipment", new Godot.Collections.Array { fake_instance } },
        });
        CheckTrue(described.Contains("草药 ×2"), $"掉落明细写物品名与数量：{described}");
        CheckTrue(described.Contains("铁剑"), $"掉落明细写装备名：{described}");
        CheckTrue(described.Contains("力 +3"), $"掉落明细带词条：{described}");
    }

    private void _CheckRounds(BattleScreen battle)
    {
        CheckEq(battle.round_text(), "第 1 回合　进行中", "开局停在第 1 回合");
        battle.press_next_round();
        CheckEq(_Sim(battle).rounds_played(), 1, "点下一回合推进一回合");
        battle.toggle_speed();
        CheckFloat(battle.speed(), 2.0, "速度切到 2x");
        battle.toggle_speed();
        CheckFloat(battle.speed(), 4.0, "速度切到 4x");
        battle.toggle_speed();
        CheckFloat(battle.speed(), 1.0, "速度回到 1x");
        // 自动战斗策略：按钮轮换并把当前策略写在按钮上（只影响我方自动战斗）
        CheckTrue(battle.strategy_text().Contains("均衡"), $"策略按钮默认写均衡：{battle.strategy_text()}");
        CheckEq(battle.press_strategy(), "all_out", "轮换到全力");
        CheckTrue(battle.strategy_text().Contains("全力"), $"按钮跟上：{battle.strategy_text()}");
        battle.press_strategy();
        CheckTrue(battle.strategy_text().Contains("保守"), $"再轮换到保守：{battle.strategy_text()}");
        battle.press_strategy();
        CheckTrue(battle.strategy_text().Contains("均衡"), "轮换回均衡（不影响后面的断言）");
        // 逃跑的完整行为另有专门的用例（_CheckFleeUi）；这里只确认按钮状态与原因一致，
        // 不能真按下去——按下去可能直接结束战斗，后面的出招断言就没得测了
        var flee = battle.FindChild("FleeButton", true, false) as Button;
        CheckNotNull(flee, "有逃跑按钮");
        if (flee != null)
        {
            CheckFalse(flee.Text.Contains("未实现"), $"逃跑按钮不再是占位文案：{flee.Text}");
            CheckFalse(flee.Disabled, $"轮到我方时逃跑可按（原因：{flee.TooltipText}）");
        }

        battle.press_auto();
        CheckTrue(battle.auto_running(), "点了自动战斗会进入自动状态");
        battle.press_stop_auto();
        CheckFalse(battle.auto_running(), "可以停下自动战斗");

        // 点敌人选目标：选第二个敌人后，出招应该打它而不是"血量最低"
        BattleActor second = _Actor(battle.enemies[1]);
        var pick = battle.FindChild($"TargetButton_{second.actor_id}", true, false) as Button;
        CheckNotNull(pick, "敌人卡片上有选目标的按钮");
        if (pick != null)
        {
            int hp_before = _EnemyHpTotal(battle);
            pick.EmitSignal(Button.SignalName.Pressed);
            CheckEq(battle.selected_target_id, second.actor_id, "选中状态记在界面上");
            BattleSimulator sim = _Sim(battle);
            BattleActor actor = sim.in_round() ? sim.current_actor() as BattleActor : null;
            if (actor == null || !battle.allies.Contains(actor))
            {
                battle.press_next_round();
                actor = sim.in_round() ? sim.current_actor() as BattleActor : null;
            }

            if (actor != null && battle.allies.Contains(actor))
            {
                Godot.Collections.Array skills = sim.available_skills(actor);
                if (skills.Count > 0)
                {
                    int second_hp_before = second.hp;
                    battle.press_skill(skills[0].As<SkillActiveRow>().skill_id);
                    CheckLt(second.hp, second_hp_before, "出招打的是选中的敌人");
                    CheckLt(_EnemyHpTotal(battle), hp_before, "总血量也下降了");
                    CheckNotNull(
                        battle.FindChild($"Float_{second.actor_id}", true, false),
                        "命中后目标卡片上有伤害跳字");
                }
            }
        }
    }

    private void _CheckFinish(BattleScreen battle, Godot.Collections.Array returns)
    {
        int guard = 0;
        Sfx.clear_log();
        while (!battle.finished() && guard < 120)
        {
            // 「下一回合」现在只推进到我方行动，所以轮到我方时要出招
            BattleSimulator sim = _Sim(battle);
            BattleActor actor = sim.in_round() ? sim.current_actor() as BattleActor : null;
            if (actor != null && battle.allies.Contains(actor))
            {
                Godot.Collections.Array skills = sim.available_skills(actor);
                if (skills.Count == 0)
                {
                    battle.press_next_round();
                }
                else
                {
                    int before_hp = _EnemyHpTotal(battle);
                    Godot.Collections.Dictionary result = battle.press_skill(skills[0].As<SkillActiveRow>().skill_id);
                    CheckTrue(result["ok"].AsBool(), $"手选招式能出招：{result}");
                    CheckLt(_EnemyHpTotal(battle), before_hp, "出招后敌人总血量下降");
                }
            }
            else
            {
                battle.press_next_round();
            }

            guard += 1;
        }

        CheckTrue(battle.finished(), "战斗能打完");
        // 音效（设计 07 §8.4）：这一场真打完了 → 命中那一刻必须请求过音效
        CheckTrue(Sfx.has_played("hit"), "命中会请求音效（伤害跳字那一刻）");
        CheckTrue(battle.result_label_text() != "", "打完显示结果");
        // 熟练度：打完这一场，真正施放过的招式要涨熟练度并写回存档
        string char_id = _Actor(battle.allies[0]).actor_id;
        string[] learned_ids = ((GameState)battle.state).masteries_of(char_id);
        CheckGt(
            ((GameState)battle.state).mastery_of(char_id, "sk_xuanwei_01"), 0.0,
            $"打完把熟练度写回存档（{string.Join(",", learned_ids)}）");
        CheckTrue(
            battle.result_label_text().Contains("熟练度"),
            $"结算里播报熟练度：{battle.result_label_text()}");
        var next = battle.FindChild("NextRoundButton", true, false) as Button;
        CheckNotNull(next, "有「下一回合」按钮");
        if (next != null)
        {
            CheckTrue(next.Disabled, "打完后不能再点下一回合");
        }

        var back = battle.FindChild("ReturnButton", true, false) as Button;
        CheckNotNull(back, "有返回大地图按钮");
        if (back != null)
        {
            back.EmitSignal(Button.SignalName.Pressed);
            CheckEq(returns.Count, 1, "返回按钮触发回程");
        }
    }

    /// <summary>
    /// 三种战斗资源都要在界面上看得见（设计 04：气血／内力／架势），且读数跟着单位走。
    ///
    /// 以前没有任何用例找过 `HpBar_*`／`QiBar_*`／`PoiseBar_*`——删掉或改名这三条资源条，
    /// 用例照样全绿（「三种战斗资源」那条在对照表里也标着 ✅）。
    /// </summary>
    private void _CheckResourceBars(BattleScreen battle)
    {
        foreach (BattleActor actor in new[] { _Actor(battle.allies[0]), _Actor(battle.enemies[0]) })
        {
            string actor_id = actor.actor_id;
            CheckNotNull(battle.FindChild($"HpBar_{actor_id}", true, false), $"有气血条（{actor_id}）");
            CheckNotNull(battle.FindChild($"QiBar_{actor_id}", true, false), $"有内力条（{actor_id}）");
            CheckNotNull(battle.FindChild($"PoiseBar_{actor_id}", true, false), $"有架势条（{actor_id}）");
        }

        BattleActor ally = _Actor(battle.allies[0]);
        string ally_id = ally.actor_id;
        var qi = battle.FindChild($"QiBar_{ally_id}", true, false) as ProgressBar;
        var poise = battle.FindChild($"PoiseBar_{ally_id}", true, false) as ProgressBar;
        CheckEq((int)qi.MaxValue, Mathf.Max(1, ally.max_qi()), "内力条上限 = 内力上限");
        CheckEq((int)qi.Value, ally.qi, "内力条读数 = 当前内力");
        CheckEq((int)poise.MaxValue, Mathf.Max(1, ally.max_poise()), "架势条上限 = 架势上限");
        CheckEq((int)poise.Value, ally.poise, "架势条读数 = 当前架势");

        // 界面要真的跟着状态刷新：掉血之后重建的行里，气血条是新值（_refresh 是重建行，不是就地改）
        ally.take_damage(5);
        battle._refresh();
        var hp_after = battle.FindChild($"HpBar_{ally_id}", true, false) as ProgressBar;
        CheckNotNull(hp_after, "刷新后气血条还在");
        CheckEq((int)hp_after.Value, ally.hp, $"掉血后气血条跟着变（{ally.hp}）");
        CheckEq((int)hp_after.MaxValue, Mathf.Max(1, ally.max_hp()), "气血条上限 = 气血上限");
        // 架势条的「即将破防」（15 §4.5）：条本身的颜色归代码，美术那两张贴图只管「加什么」——
        // 低于临界线叠 `poise_critical`（裂纹），打空那一帧换成 `poise_break`（横条压亮＋炸光）。
        // 贴图原生 32×12，比 8px 的条上下各溢 2px，是**表现**、不进版式预算。
        CheckNull(
            battle.FindChild($"PoiseFx_{ally_id}", true, false),
            "架势还满着时不摆临界表现（只在低架势／破防时才加）");
        ally.poise = 2;
        battle._refresh();
        var critical = battle.FindChild($"PoiseFx_{ally_id}", true, false) as TextureRect;
        CheckNotNull(critical, "架势压到临界线以下 → 叠上临界贴图");
        if (critical != null)
        {
            CheckTrue(
                critical.Texture.ResourcePath.Contains("poise_critical"),
                $"用的是 poise_critical：{critical.Texture.ResourcePath}");
        }

        ally.take_poise_damage(999);
        CheckTrue(ally.is_broken(), "架势打空 → 进入破绽");
        battle._refresh();
        var broken = battle.FindChild($"PoiseFx_{ally_id}", true, false) as TextureRect;
        CheckNotNull(broken, "破防那一帧叠的是爆发贴图");
        if (broken != null)
        {
            CheckTrue(
                broken.Texture.ResourcePath.Contains("poise_break"),
                $"用的是 poise_break：{broken.Texture.ResourcePath}");
        }
    }

    /// <summary>
    /// 增益／异常的图标：**优先表里的 `icon`，空则退回行 id**（与 `item_base.icon` 同一套优先级），
    /// 而且**有图才摆**（美术还没交付时卡片尺寸不变）。
    ///
    /// 由来（2026-10-04）：`buff_def.icon` 与 `status_effect.icon` 这两列**全项目没有一个地方读**
    /// ——配表审计按**列名**查"有没有人在 src 里提过"，而 `icon` 这个名字被 `item_base`／`map_region`
    /// 用着，于是这两列的死活**被同名的活列盖住了**，谁也看不见。美术一旦按这两列的值交图，
    /// 结果就是"图来了但不显示"（决策 304／309 同一个坑）。这条用例把"读哪一列、怎么退回"钉住。
    /// </summary>
    private void _CheckEffectIcons(TableDb db, BattleScreen battle)
    {
        // ① 表里的 icon 值优先：用真表的值算期望，不写死 id（美术/设计改名也不会假红）
        var venom = db.get_row("buff_def", "buff_venom_edge") as BuffDefRow;
        CheckNotNull(venom, "表里有 buff_venom_edge（这条用例依赖它）");
        string venom_icon = venom != null ? venom.icon : "";
        CheckTrue(venom_icon.Length > 0, $"淬毒那条 buff 的 icon 列有值：{venom_icon}");
        Button chip = battle._make_effect_chip(new Godot.Collections.Dictionary
        {
            { "kind", "buff" }, { "buff_id", "buff_venom_edge" }, { "entry_id", "buff_venom_edge" },
            { "icon", venom_icon }, { "name", "淬毒" }, { "stacks", 1 }, { "remaining", 3 },
            { "is_debuff", true },
        });
        CheckEq(
            chip.GetMeta("icon_path").AsString(), IconPaths.buff(venom_icon),
            $"增益图标按 buff_def.icon 拼路径（{venom_icon}）");
        chip.Free();

        // ② icon 列空 → 退回行 id（两条命名口径都能对上，美术按哪个交都不会漏）
        Button fallback = battle._make_effect_chip(new Godot.Collections.Dictionary
        {
            { "kind", "buff" }, { "buff_id", "buff_guard" }, { "entry_id", "buff_guard" },
            { "icon", "" }, { "name", "防御姿态" }, { "stacks", 1 }, { "remaining", 1 },
            { "is_debuff", false },
        });
        CheckEq(
            fallback.GetMeta("icon_path").AsString(), IconPaths.buff("buff_guard"),
            "icon 列为空时退回行 id");
        fallback.Free();

        // ③ 异常状态走 `icons/status/`（15 §4.3 的「异常状态 4」）
        var poison = db.get_row("status_effect", "poison") as StatusEffectRow;
        CheckNotNull(poison, "表里有 poison 这条异常状态");
        string poison_icon = poison != null ? poison.icon : "";
        Button status_chip = battle._make_effect_chip(new Godot.Collections.Dictionary
        {
            { "kind", "status" }, { "status_id", "poison" }, { "entry_id", "poison" },
            { "icon", poison_icon }, { "name", "中毒" }, { "stacks", 2 }, { "remaining", 2 },
            { "is_debuff", true },
        });
        CheckEq(
            status_chip.GetMeta("icon_path").AsString(),
            IconPaths.status(poison_icon.Length > 0 ? poison_icon : "poison"),
            "异常状态图标按 status_effect.icon 拼路径（空则退回行 id）");
        // 「有图才摆」：**不写死"现在没图"**（美术交付后那条会假红），只要求「有图 ⟺ 文件在」
        string status_path = status_chip.GetMeta("icon_path").AsString();
        CheckEq(
            status_chip.Icon != null, ResourceLoader.Exists(status_path),
            $"有图才摆（{status_path} 存在吗：{ResourceLoader.Exists(status_path)}）");
        status_chip.Free();

        // ④ 端到端：真战斗里那条 chip 的 icon_path 也来自表（证明 BattleActor 把 icon 递到了界面）
        var real = battle.FindChild($"Buff_{_Actor(battle.allies[0]).actor_id}_buff_yunqi", true, false) as Button;
        if (real != null)
        {
            var yunqi = db.get_row("buff_def", "buff_yunqi") as BuffDefRow;
            string want = yunqi != null ? yunqi.icon : "";
            if (want.Length == 0)
            {
                want = "buff_yunqi";
            }

            CheckEq(
                real.GetMeta("icon_path").AsString(), IconPaths.buff(want),
                "真战斗里的 chip 也带表里的图标路径");
        }
    }

    // ---------------------------------------------------------------- 小工具

    private BattleScreen _MakeBattle(GodotObject state)
    {
        var battle = GD.Load<PackedScene>(BattleScene).Instantiate() as BattleScreen;
        battle.state_override = state;
        // 固定种子：命中／闪避是掷点决定的，不固定的话「出招后血量下降」这类断言会随机红
        battle.rng_seed = Seed;
        return battle;
    }

    private void _DropBattle(Node battle)
    {
        Tree.Root.RemoveChild(battle);
        battle.Free();
    }

    private int _EnemyHpTotal(BattleScreen battle)
    {
        int total = 0;
        foreach (BattleActor enemy in _Actors(battle.enemies))
        {
            total += Mathf.Max(0, enemy.hp);
        }

        return total;
    }

    /// <summary>造一条「命中」跳字事件。`hit_index &lt; 0` 不带段序号（单段招式），`hit_count &lt;= 0` 不带总段数。</summary>
    private static Godot.Collections.Dictionary _HitEvent(string target_id, int damage, int hit_index, int hit_count)
    {
        var row = new Godot.Collections.Dictionary
        {
            { "kind", "hit" }, { "target", target_id }, { "damage", damage },
        };
        if (hit_index >= 0)
        {
            row["hit_index"] = hit_index;
        }

        if (hit_count > 0)
        {
            row["hit_count"] = hit_count;
        }

        return row;
    }

    private static BattleActor _Actor(Variant value) => value.As<BattleActor>();

    private static BattleSimulator _Sim(BattleScreen battle) => (BattleSimulator)battle.sim;

    private static System.Collections.Generic.List<BattleActor> _Actors(Godot.Collections.Array array)
    {
        var actors = new System.Collections.Generic.List<BattleActor>();
        foreach (Variant entry in array)
        {
            actors.Add(entry.As<BattleActor>());
        }

        return actors;
    }

    private static GameSession _Session(Node node) => node as GameSession;
}
