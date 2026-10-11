using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 开局引导（设计 09 §3.1 → 0.22.0 设计 18 §3.2）：`guide_step` 的 6 步按剧情旗标自动推进——对位
/// `tests/test_guide.gd`（2026-10-11 P4 第七批搬 C#）。
///
/// 这里**不写死**每一步的旗标名：直接从表里读 `condition` 再逐个点亮，
/// 这样设计改旗标名／加一步都不会让用例变成"永远真"的空壳。
///
/// **类名与 `TestGuide.cs` 区分**：那一份是 `GuideService` 搬 C# 时写的（常量绝对值／条件语言那些），
/// 这一份是旧链原来那份（表形状／逐步推进／退回／HUD 文案）。
/// </summary>
public partial class TestGuideSteps : TestCase
{
    public override string SuiteName() => "开局引导";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_steps_table(db);
        _check_progression(db);
        _check_hud_text(db);
    }

    /// <summary>
    /// 表本身：6 步（0.22.0 由 4 步扩到 6 步，补上「进寨」与「救人复命」）、
    /// sort_order 从 1 连续、每步有文案与 condition_spec、第一步是 `start` 哨兵。
    /// </summary>
    private void _check_steps_table(TableDb db)
    {
        Godot.Collections.Array rows = GuideService.steps(db);
        CheckEq(rows.Count, 6, "引导 6 步（guide_step 的行数；0.22.0 由 4 步扩到 6 步）");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index].AsGodotObject() as GuideStepRow;
            CheckEq(row.sort_order, index + 1, $"第 {index + 1} 步的 sort_order 从 1 连续");
            CheckFalse(row.text_cn.Length == 0, $"第 {index + 1} 步有给玩家看的文案");
            CheckFalse(row.condition.Length == 0, $"第 {index + 1} 步写清了触发条件");
        }

        CheckEq(
            (rows[0].AsGodotObject() as GuideStepRow).condition,
            GuideService.start_condition(),
            "第一步用 start 哨兵（开局就显示）");
        CheckNe(
            (rows[rows.Count - 1].AsGodotObject() as GuideStepRow).condition,
            GuideService.start_condition(),
            "最后一步等一个真实旗标");
    }

    /// <summary>推进：新档停在第一步；按表把条件一个个点亮，当前目标就一步步往前走。</summary>
    private void _check_progression(TableDb db)
    {
        GodotObject state = SoloState(db);
        var gd_state = (GameState)state;
        Godot.Collections.Array rows = GuideService.steps(db);
        Godot.Collections.Dictionary first = GuideService.current(db, state);
        CheckEq(_s(first, "step_id"), (rows[0].AsGodotObject() as GuideStepRow).step_id, "新档停在第一步");
        CheckEq(_i(first, "index"), 1, "第一步的序号是 1");

        // 无关旗标不推进（免得"点了个什么都往前跳"）
        gd_state.set_flag("flag_not_in_guide", true);
        CheckEq(
            _s(GuideService.current(db, state), "step_id"),
            (rows[0].AsGodotObject() as GuideStepRow).step_id,
            "无关旗标不推进");

        for (var index = 1; index < rows.Count; index++)
        {
            var expected = rows[index].AsGodotObject() as GuideStepRow;
            gd_state.set_flag(expected.condition, true);
            Godot.Collections.Dictionary row = GuideService.current(db, state);
            CheckEq(_s(row, "step_id"), expected.step_id, $"点亮 {expected.condition} 后推进到第 {index + 1} 步");
            CheckEq(_i(row, "index"), index + 1, $"第 {index + 1} 步的序号对得上");
        }

        // 反向：把最后一步的条件清掉，目标要退回去（旗标是唯一判据）
        gd_state.set_flag((rows[rows.Count - 1].AsGodotObject() as GuideStepRow).condition, false);
        CheckEq(
            _s(GuideService.current(db, state), "step_id"),
            (rows[rows.Count - 2].AsGodotObject() as GuideStepRow).step_id,
            "清掉最后一步的旗标后退回上一步");
    }

    /// <summary>HUD 文案：带「当前目标」与进度分母；每一步的文案都来自表。</summary>
    private void _check_hud_text(TableDb db)
    {
        GodotObject state = SoloState(db);
        var gd_state = (GameState)state;
        string text = GuideService.hud_text(db, state);
        Godot.Collections.Array rows = GuideService.steps(db);
        CheckTrue(text.StartsWith("当前目标："), $"HUD 那一行以「当前目标：」开头：{text}");
        CheckTrue(text.Contains((rows[0].AsGodotObject() as GuideStepRow).text_cn), "文案取表里的第一步原文");
        CheckTrue(text.Contains($"1/{rows.Count}"), $"带上进度（1/{rows.Count}）：{text}");
        gd_state.set_flag((rows[rows.Count - 1].AsGodotObject() as GuideStepRow).condition, true);
        string last_text = GuideService.hud_text(db, state);
        CheckTrue(
            last_text.Contains((rows[rows.Count - 1].AsGodotObject() as GuideStepRow).text_cn),
            "走到最后一步显示最后一步的文案");
        CheckTrue(last_text.Contains($"{rows.Count}/{rows.Count}"), $"进度显示满格：{last_text}");
        // 玩家可见的文案里不许出现 flag_ 形态的旗标 id（决策 242 的同一条纪律）
        CheckFalse(last_text.Contains("flag_"), $"HUD 文案不出现旗标 id：{last_text}");
    }

    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;
}
