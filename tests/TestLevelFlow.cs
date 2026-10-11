using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 经验与升级（01_角色系统.md「等级与经验」＋ `level_growth`）——
/// 对位 `tests/test_level.gd`（2026-10-11 P4 第十二批搬 C#）。
///
/// 表里 `exp_to_next` 是「升下一级所需经验」，满级（20）那一行是 0。
/// 经验进队伍池 `party_exp`，付得起就把等级换出来（加点与等级成长由 `AttributeCalculator` 自动跟上）。
///
/// **类名取 `TestLevelFlow`：`TestLevel.cs` 已被占**（`LevelService` 搬 C# 时写的那份，
/// suite 名「经验与升级（C# 侧）」，管表口径／经验池兑换／满级封顶／面板进度）。
/// 这一份是旧链原来那份，**多出的两块是整合面**：
/// ① 升级带动加点上限与等级成长（要 `CharacterSheet`）；② 战斗入账顺手升级（要 `BattleReward`）。
/// 两份都留——它们是同一份口径的两份实现。
/// </summary>
public partial class TestLevelFlow : TestCase
{
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "经验与升级";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        var levels = LevelService.create(db, state);
        _check_table(db, levels);
        _check_level_up(db, state, levels);
        _check_points_and_stats(db, state);
        _check_cap(db, state, levels);
        _check_battle_reward(db);
    }

    /// <summary>经验表本身：1→2 要 100，之后每级递增，20 级封顶。</summary>
    private void _check_table(TableDb db, LevelService levels)
    {
        CheckEq(levels.exp_to_next(1), 100, "1 级升 2 级要 100 经验");
        CheckEq(levels.exp_to_next(2), 240, "2 级升 3 级要 240 经验");
        CheckEq(levels.exp_to_next(19), 9650, "19 级升 20 级要 9650 经验");
        CheckEq(levels.exp_to_next(20), 0, "满级不再需要经验");
        CheckEq(levels.max_level(), 20, "等级上限 20");
        CheckEq(levels.upgrade_points(1), 5, "每级给 5 点加点（设计 0.13.0 由 3 提到 5）");
        var previous = 0;
        for (var level = 1; level < 20; level++)
        {
            CheckGt(levels.exp_to_next(level), previous, $"{level} 级的需求比上一级高");
            previous = levels.exp_to_next(level);
        }
    }

    /// <summary>付得起才升：99 不升、100 升一级、跨级一次到位。</summary>
    private void _check_level_up(TableDb db, GodotObject state, LevelService levels)
    {
        state.Set("party_exp", 99);
        Godot.Collections.Dictionary none = levels.apply_available_levels();
        CheckTrue(none["changes"].AsGodotArray().Count == 0, "99 经验不够升一级");
        CheckEq(state.Call("level_of", CharId).AsInt32(), 1, "等级没变");
        CheckEq(state.Get("party_exp").AsInt32(), 99, "池子里的经验留着");

        state.Set("party_exp", 100);
        Godot.Collections.Dictionary once = levels.apply_available_levels();
        CheckEq(once["changes"].AsGodotArray().Count, 1, "100 经验升一级");
        CheckEq(state.Call("level_of", CharId).AsInt32(), 2, "等级到 2");
        CheckEq(state.Get("party_exp").AsInt32(), 0, "扣掉 100 经验");
        if (once["changes"].AsGodotArray().Count > 0)
        {
            Godot.Collections.Dictionary change = once["changes"].AsGodotArray()[0].AsGodotDictionary();
            CheckEq(change["from"].AsInt32(), 1, "从 1 级");
            CheckEq(change["to"].AsInt32(), 2, "升到 2 级");
            CheckEq(change["points"].AsInt32(), 5, "这次给了 5 点");
        }

        string once_text = levels.describe(once["changes"].AsGodotArray());
        CheckTrue(once_text.Contains("Lv1 → Lv2"), $"文案写清级别变化：{once_text}");

        // 一路升到 4 级：240 + 420
        state.Set("party_exp", 240 + 420);
        Godot.Collections.Dictionary multi = levels.apply_available_levels();
        CheckEq(state.Call("level_of", CharId).AsInt32(), 4, "经验够就连续升（到 4 级）");
        CheckEq(state.Get("party_exp").AsInt32(), 0, "跨级把经验全花掉");
        CheckEq(multi["changes"].AsGodotArray().Count, 2, "这次升了两级");

        // 经验不够下一级时留着
        state.Set("party_exp", 300);
        levels.apply_available_levels();
        CheckEq(state.Call("level_of", CharId).AsInt32(), 4, "300 不够 4→5（要 640）");
        CheckEq(state.Get("party_exp").AsInt32(), 300, "不够的经验不扣");
    }

    /// <summary>升级带动加点上限与等级成长（这几条本来就接在 `level_growth` 上）。</summary>
    private void _check_points_and_stats(TableDb db, GodotObject state)
    {
        state.Get("char_levels").AsGodotDictionary()[CharId] = 1;
        state.Set("char_allocations", new Godot.Collections.Dictionary());
        CheckEq(state.Call("available_points", db, CharId).AsInt32(), 5, "1 级有 5 点");
        state.Call("append_level", CharId, 1); // 直接升一级，绕过经验池
        CheckEq(state.Call("level_of", CharId).AsInt32(), 2, "升到 2 级");
        CheckEq(state.Call("available_points", db, CharId).AsInt32(), 10, "2 级累计 10 点");
        CharacterSheet sheet = CharacterSheet.create(db, state, CharId);
        double hp_at_2 = sheet.stat_value("hp_max");
        CheckTrue(
            state.Call("spend_point", db, CharId, "str").AsGodotDictionary()["ok"].AsBool(),
            "升级得来的点能加");
        state.Call("append_level", CharId, 1);
        CheckEq(
            state.Call("available_points", db, CharId).AsInt32(),
            14,
            "3 级累计 15 点，加了 1 点还剩 14");
        double hp_at_3 = CharacterSheet.create(db, state, CharId).stat_value("hp_max");
        CheckGt(hp_at_3, hp_at_2, $"等级成长进了基础气血（{hp_at_2:F0} → {hp_at_3:F0}）");
    }

    /// <summary>满级之后经验只记账不消耗。</summary>
    private void _check_cap(TableDb db, GodotObject state, LevelService levels)
    {
        state.Get("char_levels").AsGodotDictionary()[CharId] = 20;
        state.Set("party_exp", 99999);
        Godot.Collections.Dictionary capped = levels.apply_available_levels();
        CheckTrue(capped["changes"].AsGodotArray().Count == 0, "满级不再升级");
        CheckTrue(capped["capped"].AsBool(), "结果里标着已满级");
        CheckEq(state.Get("party_exp").AsInt32(), 99999, "满级后经验留在池子里");
        Godot.Collections.Dictionary progress = levels.exp_progress(CharId);
        CheckTrue(progress["maxed"].AsBool(), "面板能看出已经满级");
    }

    /// <summary>战斗入账顺手升级：经验够的当场升，结算里能看到。</summary>
    private void _check_battle_reward(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Dictionary applied = BattleReward.grant(
            db, state, 100, 0, new Godot.Collections.Array(), null);
        CheckEq(state.Call("level_of", CharId).AsInt32(), 2, "打完拿到 100 经验就升到 2 级");
        CheckEq(state.Get("party_exp").AsInt32(), 0, "经验扣进等级里");
        string level_up_text = _s(applied, "level_up_text");
        CheckTrue(level_up_text.Contains("Lv1 → Lv2"), $"入账结果里带升级文案：{level_up_text}");
        CheckEq(applied["level_ups"].AsGodotArray().Count, 1, "升级记录也能读出来");

        // 经验不够就不升，但仍然记账
        GodotObject state2 = SoloState(db);
        Godot.Collections.Dictionary small = BattleReward.grant(
            db, state2, 36, 0, new Godot.Collections.Array(), null);
        CheckEq(state2.Call("level_of", CharId).AsInt32(), 1, "36 经验不够升级");
        CheckEq(state2.Get("party_exp").AsInt32(), 36, "经验先记在队伍池里");
        CheckTrue(_s(small, "level_up_text").Length == 0, "没升级就没有升级文案");
    }

    /// <summary>对位 GDScript 的 `dict.get(key, 默认值)`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;
}
