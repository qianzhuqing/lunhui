using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 开局引导（C# 版，对位 `tests/test_guide.gd`；设计 09 §3.1 → 18 §3.2）。
/// `guide_step` 的步骤按剧情旗标自动推进，HUD 那一行常驻「当前目标」。
///
/// 这里**不写死**每一步的旗标名：直接从表里读 `condition` 再逐个点亮，
/// 这样设计改旗标名／加一步都不会让用例变成"永远真"的空壳。
/// 条件语言本身（`!`／`&`／`|`／`item:`／`origin:`）也在这份里钉一遍——
/// 它是引导、招募、对话、剧情节点**共用**的那一份。
/// </summary>
public partial class TestGuide : TestCase
{
    public override string SuiteName() => "开局引导（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckConstants();
        _CheckStepsTable(db);
        _CheckProgression(db);
        _CheckHudText(db);
        _CheckConditionLanguage(db);
    }

    /// <summary>
    /// 常量写死绝对值：它们决定「哪一步是当前目标」「什么算备货」「怎样才算进寨」。
    /// 变异探针那条 `START_CONDITION 改字` case 改的是 C# 文件（不重编量不出来），绝对值靠这一段钉。
    /// </summary>
    private void _CheckConstants()
    {
        CheckEq(GuideService.START_CONDITION, "start", "第一步的哨兵是 start");
        CheckEq(GuideService.ORIGIN_PREFIX, "origin:", "出身条件的前缀");
        CheckEq(GuideService.ITEM_PREFIX, "item:", "背包条件的前缀");
        CheckEq(GuideService.SUPPLIES_FLAG, "flag_supplies_ready", "备货旗标名");
        CheckEq(GuideService.SUPPLIES_LEVEL, 5, "备货要求的等级（设计 18 的 condition_spec）");
        CheckEq(GuideService.ARRIVED_HEIFENGZHAI_FLAG, "flag_arrived_heifengzhai", "进寨旗标名");
        CheckEq(GuideService.HEIFENGZHAI_SCENE, "scene_heifengzhai", "前寨那张图的 id");
    }

    /// <summary>
    /// 表本身：6 步（0.22.0 由 4 步扩到 6 步）、`sort_order` 从 1 连续、
    /// 每步有文案与条件、第一步是 `start` 哨兵。
    /// </summary>
    private void _CheckStepsTable(TableDb db)
    {
        Godot.Collections.Array rows = GuideService.steps(db);
        CheckEq(rows.Count, 6, "引导 6 步（guide_step 的行数；0.22.0 由 4 步扩到 6 步）");
        for (int index = 0; index < rows.Count; index++)
        {
            var row = (GuideStepRow)rows[index].AsGodotObject();
            CheckEq(row.sort_order, index + 1, $"第 {index + 1} 步的 sort_order 从 1 连续");
            CheckFalse(row.text_cn.Length == 0, $"第 {index + 1} 步有给玩家看的文案");
            CheckFalse(row.condition.Length == 0, $"第 {index + 1} 步写清了触发条件");
        }
        var first = (GuideStepRow)rows[0].AsGodotObject();
        var last = (GuideStepRow)rows[rows.Count - 1].AsGodotObject();
        CheckEq(first.condition, GuideService.START_CONDITION, "第一步用 start 哨兵（开局就显示）");
        CheckNe(last.condition, GuideService.START_CONDITION, "最后一步等一个真实旗标");
    }

    /// <summary>推进：新档停在第一步；按表把条件一个个点亮，当前目标就一步步往前走。</summary>
    private void _CheckProgression(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Array rows = GuideService.steps(db);
        Godot.Collections.Dictionary first = GuideService.current(db, state);
        CheckEq(first["step_id"].AsString(), ((GuideStepRow)rows[0].AsGodotObject()).step_id, "新档停在第一步");
        CheckEq(first["index"].AsInt32(), 1, "第一步的序号是 1");

        // 无关旗标不推进（免得"点了个什么都往前跳"）
        state.Call("set_flag", "flag_not_in_guide", true);
        CheckEq(
            GuideService.current(db, state)["step_id"].AsString(),
            ((GuideStepRow)rows[0].AsGodotObject()).step_id,
            "无关旗标不推进");

        for (int index = 1; index < rows.Count; index++)
        {
            string condition = ((GuideStepRow)rows[index].AsGodotObject()).condition;
            state.Call("set_flag", condition, true);
            Godot.Collections.Dictionary row = GuideService.current(db, state);
            CheckEq(
                row["step_id"].AsString(), ((GuideStepRow)rows[index].AsGodotObject()).step_id,
                $"点亮 {condition} 后推进到第 {index + 1} 步");
            CheckEq(row["index"].AsInt32(), index + 1, $"第 {index + 1} 步的序号对得上");
        }

        // 反向：把最后一步的条件清掉，目标要退回去（旗标是唯一判据）
        string last_condition = ((GuideStepRow)rows[rows.Count - 1].AsGodotObject()).condition;
        state.Call("set_flag", last_condition, false);
        CheckEq(
            GuideService.current(db, state)["step_id"].AsString(),
            ((GuideStepRow)rows[rows.Count - 2].AsGodotObject()).step_id,
            "清掉最后一步的旗标后退回上一步");
    }

    /// <summary>HUD 文案：带「当前目标」与进度分母；每一步的文案都来自表。</summary>
    private void _CheckHudText(TableDb db)
    {
        GodotObject state = SoloState(db);
        string text = GuideService.hud_text(db, state);
        Godot.Collections.Array rows = GuideService.steps(db);
        CheckTrue(text.StartsWith("当前目标：", System.StringComparison.Ordinal), $"HUD 那一行以「当前目标：」开头：{text}");
        CheckTrue(text.Contains(((GuideStepRow)rows[0].AsGodotObject()).text_cn), "文案取表里的第一步原文");
        CheckTrue(text.Contains($"1/{rows.Count}"), $"带上进度（1/{rows.Count}）：{text}");

        state.Call("set_flag", ((GuideStepRow)rows[rows.Count - 1].AsGodotObject()).condition, true);
        string last_text = GuideService.hud_text(db, state);
        CheckTrue(
            last_text.Contains(((GuideStepRow)rows[rows.Count - 1].AsGodotObject()).text_cn),
            "走到最后一步显示最后一步的文案");
        CheckTrue(last_text.Contains($"{rows.Count}/{rows.Count}"), $"进度显示满格：{last_text}");
        // 玩家可见的文案里不许出现 flag_ 形态的旗标 id（决策 242 的同一条纪律）
        CheckFalse(last_text.Contains("flag_"), $"HUD 文案不出现旗标 id：{last_text}");
    }

    /// <summary>
    /// 条件语言的连接符与两种写法。这一份判据是**引导／招募／对话／剧情节点共用**的，
    /// 所以它的边界（尾部空段、自相矛盾、数量不足）都在这里钉住。
    /// </summary>
    private void _CheckConditionLanguage(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckTrue(GuideService.condition_met(state, GuideService.START_CONDITION), "start 哨兵恒成立");

        CheckFalse(GuideService.condition_met(state, "flag_a"), "没点亮的旗标＝不满足");
        CheckTrue(GuideService.condition_met(state, "!flag_a"), "取反：没点亮时 `!flag_a` 成立");
        state.Call("set_flag", "flag_a", true);
        CheckFalse(GuideService.condition_met(state, "!flag_a"), "点亮之后 `!flag_a` 不成立");
        CheckTrue(GuideService.condition_met(state, "flag_a|flag_b"), "任一满足：左边亮就算过");
        CheckFalse(GuideService.condition_met(state, "flag_b|flag_c"), "两个都没亮＝不满足");
        CheckTrue(GuideService.condition_met(state, "flag_a&!flag_b"), "`&` 先拆、`!` 在后：亮 a 且没亮 b 成立");
        CheckFalse(
            GuideService.condition_met(state, "!flag_a&flag_a"),
            "同一条里自相矛盾＝不成立（写坏了不会静默当成没条件）");
        CheckFalse(GuideService.condition_met(state, "!flag_a|"), "尾部空段＝不成立（与 `&` 同一套纪律）");
        CheckFalse(GuideService.condition_met(state, ""), "空条件＝不成立（这一步算写坏了）");
        CheckFalse(GuideService.condition_met(null, "flag_a"), "没有会话状态时恒不成立");

        // `origin:<char_id>`：主角正是这个出身（主角是开局第一个人）
        string protagonist = state.Get("char_ids").AsStringArray()[0];
        CheckTrue(GuideService.condition_met(state, GuideService.ORIGIN_PREFIX + protagonist), "主角是自己的出身");
        CheckFalse(GuideService.condition_met(state, GuideService.ORIGIN_PREFIX + "ch_ci"), "别人不是主角");
        CheckFalse(GuideService.condition_met(state, GuideService.ORIGIN_PREFIX), "前缀后面空着＝不成立");

        // `item:<id>`／`item:<id>:<数量>`：背包里有没有这件东西（与 npc_quest.requirement 同一种写法）
        CheckFalse(GuideService.condition_met(state, "item:item_bd_ledger"), "背包里没有账册时不成立");
        state.Get("inventory").AsGodotObject().Call("add_item", db, "item_bd_ledger", 1);
        CheckTrue(GuideService.condition_met(state, "item:item_bd_ledger"), "拿到账册之后成立");
        CheckTrue(GuideService.condition_met(state, "item:item_bd_ledger:1"), "带数量的写法也认");
        CheckFalse(GuideService.condition_met(state, "item:item_bd_ledger:2"), "只有一本时 `:2` 不成立");
        CheckFalse(GuideService.condition_met(state, "item:"), "`item:` 后面空着＝不成立");
    }
}
