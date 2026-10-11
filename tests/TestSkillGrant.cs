using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 武学来源。
///
/// 盯四件事：**查来源**（`skill_base.source_type`+`source_id` 命中才发，未实现的来源一律不发）、
/// **击败领悟**（写进已学、重复不再播报、一人带多部）、**门槛挡住就不写进已学但要说清原因**、
/// **秘籍研读**（门槛不够不消耗，学会才消耗；没有对应武学的残卷给出「要集齐」）。
///
/// **2026-10-11（P4 第二十七批）起这份就是唯一一份**：旧链 `tests/test_skill_grant.gd` 已并入本文件后退场
/// ——这一批补进的是当初「要场景树」的那一节：真打一场巡山队，看结算卡片或战报里有没有「领悟」。
/// C# 这条链本来就有真场景树（`RunTestsCs` 往 `TestCase.Tree` 注入，小地图那几支已在用），所以接得上。
/// </summary>
public partial class TestSkillGrant : TestCase
{
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "武学来源与领悟";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        _CheckImplementedSources();
        _CheckSourceLookup(db);
        _CheckGrantFromDefeated(db, state);
        _CheckRequirementBlocks(db, state);
        _CheckHiddenSource(db);
        _CheckSkillbook(db, state);
        _CheckBattleFlow(db);
    }

    /// <summary>
    /// 来源名单写死绝对值：`validate_tables` 与 `test_handshake_flags` 都拿它算「这部武学拿不拿得到」，
    /// 少一类就有一批武学被判成拿不到（而它们其实能拿到）。
    /// </summary>
    private void _CheckImplementedSources()
    {
        CheckEq(
            string.Join(",", SkillGrant.implemented_sources()),
            "drop,hidden,item,story,origin",
            "已接入的来源五类：drop／hidden／item／story／origin（npc／shop 未接入）");
    }

    private void _CheckSourceLookup(TableDb db)
    {
        string[] scout = SkillGrant.skills_from_enemy(db, "en_bd_scout");
        CheckTrue(System.Array.IndexOf(scout, "sk_bandit_scout") >= 0, "巡山兵身上带黑风刀法·劈山");
        CheckEq(SkillGrant.skills_from_enemy(db, "en_bd_missing").Length, 0, "不存在的敌人不掉武学");
        // npc 还没接（要对话表）：即便 source_id 撞上了也不该被当成掉落
        CheckEq(SkillGrant.skills_from_enemy(db, "xuanwei").Length, 0, "未实现的来源不发放");
        CheckEq(SkillGrant.skills_from_source(db, "npc", "xuanwei").Length, 0, "npc 来源未接入");
        // 0.22.0 起 story 接上了（设计 18 §3.1）
        CheckEq(SkillGrant.skills_from_source(db, "story", "chapter1_end").Length, 3, "story 来源已接入：chapter1_end 带 3 部武学");
        CheckEq(SkillGrant.skills_from_source(db, "hidden", "trig_wine").Length, 3, "醉刀客点位带 3 部武学");
        CheckEq(SkillGrant.skills_from_source(db, "item", "item_scroll_wudu").Length, 2, "五毒残页带招式与内功各一");
        CheckEq(SkillGrant.skills_from_source(db, "drop", "en_bd_scout").Length, 1, "按来源查与按敌人查是同一件事");
        CheckEq(SkillGrant.skills_from_source(db, "drop", "").Length, 0, "空 source_id 不发（别把空串当通配）");
    }

    private void _CheckGrantFromDefeated(TableDb db, GodotObject state)
    {
        Godot.Collections.Array granted = SkillGrant.grant_from_defeated(db, state, new[] { "en_bd_scout" });
        CheckEq(granted.Count, 1, "击败巡山兵只领悟一部武学");
        if (granted.Count == 0)
        {
            return;
        }
        var entry = granted[0].AsGodotDictionary();
        CheckEq(entry["skill_id"].AsString(), "sk_bandit_scout", "领悟的是黑风刀法·劈山");
        CheckTrue(entry["learned"].AsStringArray().Length > 0, "写进已学");
        CheckTrue(state.Call("is_learned", CharId, "sk_bandit_scout").AsBool(), "存档里能查到");
        CheckTrue(SkillGrant.describe(entry).Contains("领悟"), $"结算文案说明领悟：{SkillGrant.describe(entry)}");

        // 同一支队伍再打一遍：已经学过就不再刷屏
        Godot.Collections.Array again = SkillGrant.grant_from_defeated(db, state, new[] { "en_bd_scout" });
        CheckEq(again.Count, 0, "已学过的武学不再重复播报");

        // 一部敌人带多部武学（喽啰：黑风刀法·乱劈 + 沉沙掌·裂石 + 黑风心法·蛮力）
        Godot.Collections.Array thug = SkillGrant.grant_from_defeated(db, state, new[] { "en_bd_thug" });
        CheckEq(thug.Count, 3, "喽啰同时掉两部招式与一部内功");

        // 同一个 enemy_id 传两遍也只报一次（去重按 skill_id）——两边各用一份干净存档，别让先跑的那次把武学学走
        GodotObject once_state = SoloState(db);
        Godot.Collections.Array once = SkillGrant.grant_from_defeated(db, once_state, new[] { "en_bd_thug" });
        GodotObject twice_state = SoloState(db);
        Godot.Collections.Array twice = SkillGrant.grant_from_defeated(db, twice_state, new[] { "en_bd_thug", "en_bd_thug" });
        CheckEq(twice.Count, once.Count, "同一支队伍传两遍不重复播报");

        string[] lines = SkillGrant.summarize(once);
        CheckTrue(
            lines.Length > 0 && lines[0].StartsWith("领悟：", System.StringComparison.Ordinal),
            "汇总里学到的合成一行「领悟：」");
        CheckTrue(SkillGrant.summarize(again).Length == 0, "什么都没学到时汇总为空（不占结算框）");
    }

    private void _CheckRequirementBlocks(TableDb db, GodotObject state)
    {
        // 巡山队长带 3 部 ★3 武学（悟 12 / 力 12 / 根骨 12），1 级书生一个都够不着
        Godot.Collections.Array blocked = SkillGrant.grant_from_defeated(db, state, new[] { "en_bd_patrol_leader" });
        CheckEq(blocked.Count, 3, "门槛不够的武学也要进结算（好告诉玩家为什么没学到）");
        if (blocked.Count == 0)
        {
            return;
        }
        foreach (Variant each_variant in blocked)
        {
            CheckEq(each_variant.AsGodotDictionary()["learned"].AsStringArray().Length, 0, "门槛不够时没学会");
        }
        var entry = blocked[0].AsGodotDictionary();
        Godot.Collections.Array reasons = entry["blocked"].AsGodotArray();
        CheckGt(reasons.Count, 0, "列出被门槛挡住的角色");
        string reason = reasons[0].AsGodotDictionary()["reason"].AsString();
        CheckTrue(reason.Contains("修习门槛"), $"原因写明修习门槛：{reason}");
        CheckTrue(SkillGrant.describe(entry).Contains("没能领悟"), "结算文案也说明没学会");
        CheckFalse(state.Call("is_learned", CharId, "sk_xuanwei_05").AsBool(), "被挡住就不写进已学");
        CheckTrue(SkillGrant.summarize(blocked)[0].StartsWith("未习得：", System.StringComparison.Ordinal), "汇总里写「未习得」");
    }

    /// <summary>隐藏点位（source_type=hidden）：触发成功就按门槛发放。</summary>
    private void _CheckHiddenSource(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Array blocked = SkillGrant.grant_from_source(db, state, "hidden", "trig_wine");
        CheckEq(blocked.Count, 3, "三部武学都进了结果（好告诉玩家差多少）");
        foreach (Variant entry_variant in blocked)
        {
            var entry = entry_variant.AsGodotDictionary();
            CheckEq(entry["learned"].AsStringArray().Length, 0, $"{entry["name"].AsString()} 没学会");
            CheckGt(entry["blocked"].AsGodotArray().Count, 0, $"{entry["name"].AsString()} 写明被门槛挡住");
        }
        CheckTrue(SkillGrant.summarize(blocked)[0].Contains("未习得"), "汇总里写「未习得」");
        CheckFalse(state.Call("is_learned", CharId, "sk_drunk_zuibu").AsBool(), "被挡住的不写进已学");

        // 悟性是**资质**（设计 0.13.0：不能加点），第一章能把它抬到 12 的现成途径是图鉴奖励。
        // 这条验的是「门槛放行之后学不学得会」，所以用复制模板行的夹具直接把资质给到位。
        TableDb raised_db = TableWithTalents(12, 5);
        GodotObject raised_state = SoloState(raised_db);
        Godot.Collections.Array mixed = SkillGrant.grant_from_source(raised_db, raised_state, "hidden", "trig_wine");
        var learned_names = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in mixed)
        {
            var entry = entry_variant.AsGodotDictionary();
            if (entry["learned"].AsStringArray().Length > 0)
            {
                learned_names.Add(entry["name"].AsString());
            }
        }
        CheckTrue(learned_names.Contains("醉里乾坤·醉步"), $"悟性 12 后学会醉步：{string.Join("、", learned_names)}");
        CheckTrue(raised_state.Call("is_learned", CharId, "sk_drunk_zuibu").AsBool(), "醉步写进已学");
        CheckFalse(raised_state.Call("is_learned", CharId, "sk_drunk_jiuzhongdao").AsBool(), "悟性还够不到 ★4 的酒中刀");
    }

    /// <summary>秘籍（source_type=item）：研读学会并消耗一本；门槛不够就不消耗。</summary>
    private void _CheckSkillbook(TableDb db, GodotObject state)
    {
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        inventory.Call("add_item", db, "item_scroll_wudu", 1);
        CheckEq(inventory.Call("count", "item_scroll_wudu").AsInt32(), 1, "背包里有 1 张五毒残页");

        // 智 10 < 12：研读被门槛挡住，而且**不消耗**（别浪费稀有残页）
        Godot.Collections.Dictionary refused = SkillGrant.study(db, state, "item_scroll_wudu");
        CheckFalse(refused["ok"].AsBool(), "智不够时研读不了");
        CheckTrue(refused["error"].AsString().Contains("修习门槛"), $"说明是修习门槛：{refused["error"].AsString()}");
        CheckEq(inventory.Call("count", "item_scroll_wudu").AsInt32(), 1, "门槛不够不消耗秘籍");
        CheckEq(refused["learned"].AsStringArray().Length, 0, "拒绝时的已学名单是空的");
        CheckFalse(refused["consumed"].AsBool(), "拒绝时 consumed=false");

        // 界面置灰的口径：`study_gate` 这时也该说不，并且给一条能读的原因
        Godot.Collections.Dictionary gate_refused = SkillGrant.study_gate(db, state, "item_scroll_wudu");
        CheckFalse(gate_refused["ok"].AsBool(), "门槛不够时按钮置灰");
        CheckTrue(gate_refused["error"].AsString().Contains("修习门槛"), "置灰的原因也是修习门槛");

        // 智抬到 12：学会蚀骨（招式），蚀脉要根骨 12 还差一点
        int need = 12 - _TotalAttr(db, state, "int");
        for (int index = 0; index < need; index++)
        {
            state.Call("spend_point", db, CharId, "int");
        }
        Godot.Collections.Dictionary studied = SkillGrant.study(db, state, "item_scroll_wudu");
        CheckTrue(studied["ok"].AsBool(), $"智 12 后研读成功：{studied["error"].AsString()}");
        CheckTrue(state.Call("is_learned", CharId, "sk_wudu_03").AsBool(), "学会五毒掌·蚀骨");
        CheckEq(inventory.Call("count", "item_scroll_wudu").AsInt32(), 0, "研读成功消耗一本");
        CheckTrue(studied["consumed"].AsBool(), "成功时 consumed=true");
        CheckTrue(
            studied["error"].AsString().Length == 0 || studied["error"].AsString().Contains("没学会"),
            $"部分没学会时也说明：{studied["error"].AsString()}");
        CheckFalse(SkillGrant.study_gate(db, state, "item_scroll_wudu")["ok"].AsBool(), "残页用光后研读口也要如实说不");

        // 没有残页时给明确提示
        Godot.Collections.Dictionary missing = SkillGrant.study(db, state, "item_scroll_wudu");
        CheckFalse(missing["ok"].AsBool(), "没残页研读不了");
        CheckTrue(missing["error"].AsString().Contains("没有"), $"说明背包里没有：{missing["error"].AsString()}");

        // 钥匙类的残卷（醉里乾坤）现在没有对应武学：集齐机制未做，提示要说清
        inventory.Call("add_item", db, "item_scroll_drunk", 1);
        Godot.Collections.Dictionary no_skill = SkillGrant.can_study(db, state, "item_scroll_drunk");
        CheckFalse(no_skill["ok"].AsBool(), "没有对应武学的残卷不能研读");
        CheckTrue(no_skill["error"].AsString().Contains("集齐"), $"说明要集齐：{no_skill["error"].AsString()}");
        CheckEq(no_skill["skills"].AsStringArray().Length, 0, "它的可学名单是空的");
    }

    /// <summary>面板口径的含装备五维：现建一个 `CharacterSheet` 来读（与面板同一份合成）。</summary>
    private static int _TotalAttr(TableDb db, GodotObject state, string attr_id)
    {
        return new CharacterSheet(db, state, CharId).total_attrs()[attr_id].AsInt32();
    }

    /// <summary>
    /// 真实战斗闭环：打山贼巡山队 → 胜利结算里出现「领悟」。
    ///
    /// 随机种子固定（`rng_seed`）：掉落件数是掷出来的，不固定的话「结算卡片里有没有这一行」会时红时绿。
    /// </summary>
    private void _CheckBattleFlow(TableDb db)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，战斗闭环无法进行");
            return;
        }

        GodotObject state = SoloState(db);
        // 造一个打得赢的队伍：升到 12 级，点数全塞力量
        state.Get("char_levels").AsGodotDictionary()[CharId] = 12;
        for (var index = 0; index < 33; index++)
        {
            state.Call("spend_point", db, CharId, "str");
        }

        var spawn_row = db.get_row("roaming_spawn", "sp_lp_patrol_01");
        CheckNotNull(spawn_row, "有巡山队的明雷");
        if (spawn_row == null)
        {
            return;
        }

        var team_row = db.get_row("enemy_team", spawn_row.Get("team_id").AsString());
        var encounter = Encounter.build(db, spawn_row, team_row, Encounter.contact_front(), "normal");
        Node session_node = Tree.Root.GetNodeOrNull<Node>("GameSession");
        CheckNotNull(session_node, "GameSession 在场");
        if (session_node == null)
        {
            return;
        }

        session_node.Set("pending_encounter", encounter);

        var battle = GD.Load<PackedScene>("res://scenes/battle_screen.tscn").Instantiate() as BattleScreen;
        CheckNotNull(battle, "战斗场景挂得上 BattleScreen");
        if (battle == null)
        {
            return;
        }

        battle.state_override = state;
        battle.rng_seed = 20261003;
        Tree.Root.AddChild(battle);
        battle.setup();
        CheckEq(battle.enemies.Count, 3, "巡山队 3 人");
        var guard = 0;
        while (!battle.finished() && guard < 200)
        {
            var sim = battle.sim as BattleSimulator;
            GodotObject actor = sim != null && sim.in_round() ? sim.current_actor() : null;
            if (actor != null && battle.allies.Contains(actor))
            {
                Godot.Collections.Array skills = sim.available_skills(actor as BattleActor);
                if (skills.Count == 0)
                {
                    battle.press_next_round();
                }
                else
                {
                    battle.press_skill(skills[0].As<SkillActiveRow>().skill_id);
                }
            }
            else
            {
                battle.press_next_round();
            }

            guard++;
        }

        CheckTrue(battle.finished(), "12 级的队伍能打完巡山队");
        string result_text = battle.result_label_text();
        string battle_log = battle._log.GetParsedText();
        CheckTrue(result_text.Contains("胜利"), $"打赢了：{result_text}");
        // 卡片只有三行，放不下的行进战报——所以「播报」要卡片或战报里有一处
        CheckTrue(
            result_text.Contains("领悟") || battle_log.Contains("领悟"),
            $"结算里播报领悟（卡片或战报）：{result_text}");
        CheckTrue(
            state.Call("is_learned", CharId, "sk_chensha_02").AsBool()
                || state.Call("is_learned", CharId, "sk_bandit_arrow").AsBool(),
            "喽啰或弓手的武学进了已学");

        Tree.Root.RemoveChild(battle);
        battle.Free();
        session_node.Set("pending_encounter", default(Variant));
    }
}
