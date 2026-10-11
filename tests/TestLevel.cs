using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 经验与升级（C# 版，对位 `tests/test_level.gd`）。
///
/// GDScript 那条用例还留着跑「升级带动加点／基础属性」与「战斗入账顺手升级」这两段整合
/// （要 `CharacterSheet`／`BattleReward`，它们还在 GDScript 里）；这里覆盖表口径、
/// 经验池兑换等级、满级封顶与面板进度。
///
/// `state` 是 GDScript 的 `GameState`（`TestCase.SoloState` 造出来的 `GodotObject`），
/// 只用来读 `char_ids`／`party_exp`，以及调 `level_of`／`append_level`／`char_name`。
/// </summary>
public partial class TestLevel : TestCase
{
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "经验与升级（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        var levels = LevelService.create(db, state);
        _check_table(levels);
        _check_level_up(state, levels);
        _check_cap(state, levels);
        _check_progress(state, levels);
    }

    /// <summary>经验表本身：1→2 要 100，之后每级递增，20 级封顶。</summary>
    private void _check_table(LevelService levels)
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

    /// <summary>付得起才升：99 不升、100 升一级、跨级一次到位、不够的留着。</summary>
    private void _check_level_up(GodotObject state, LevelService levels)
    {
        state.Set("party_exp", 99);
        Godot.Collections.Dictionary none = levels.apply_available_levels();
        CheckEq(none["changes"].AsGodotArray().Count, 0, "99 经验不够升一级");
        CheckEq(state.Call("level_of", CharId).AsInt32(), 1, "等级没变");
        CheckEq(state.Get("party_exp").AsInt32(), 99, "池子里的经验留着");

        state.Set("party_exp", 100);
        Godot.Collections.Dictionary once = levels.apply_available_levels();
        CheckEq(once["changes"].AsGodotArray().Count, 1, "100 经验升一级");
        CheckEq(state.Call("level_of", CharId).AsInt32(), 2, "等级到 2");
        CheckEq(state.Get("party_exp").AsInt32(), 0, "扣掉 100 经验");
        Godot.Collections.Dictionary change = once["changes"].AsGodotArray()[0].AsGodotDictionary();
        CheckEq(change["char_id"].AsString(), CharId, "升级的是队伍里那个人");
        CheckEq(change["from"].AsInt32(), 1, "从 1 级");
        CheckEq(change["to"].AsInt32(), 2, "升到 2 级");
        CheckEq(change["points"].AsInt32(), 5, "这次给了 5 点");
        CheckEq(once["spent"].AsInt32(), 100, "花掉 100 经验");
        string text = levels.describe(once["changes"].AsGodotArray());
        CheckTrue(text.Contains("Lv1 → Lv2"), $"文案写清级别变化：{text}");
        CheckTrue(text.Contains("（+5 点）"), $"文案写清给了几点：{text}");
        CheckEq(levels.describe(new Godot.Collections.Array()), "", "没升级就没有文案");

        // 一路升到 4 级：240 + 420
        state.Set("party_exp", 240 + 420);
        Godot.Collections.Dictionary multi = levels.apply_available_levels();
        CheckEq(state.Call("level_of", CharId).AsInt32(), 4, "经验够就连续升（到 4 级）");
        CheckEq(state.Get("party_exp").AsInt32(), 0, "跨级把经验全花掉");
        CheckEq(multi["changes"].AsGodotArray().Count, 2, "这次升了两级");
        CheckEq(multi["spent"].AsInt32(), 660, "两级的经验一起扣（240 + 420）");

        // 经验不够下一级时留着
        state.Set("party_exp", 300);
        levels.apply_available_levels();
        CheckEq(state.Call("level_of", CharId).AsInt32(), 4, "300 不够 4→5（要 640）");
        CheckEq(state.Get("party_exp").AsInt32(), 300, "不够的经验不扣");
    }

    /// <summary>满级之后经验只记账不消耗，结果里标着已满级。</summary>
    private void _check_cap(GodotObject state, LevelService levels)
    {
        Godot.Collections.Dictionary char_levels = state.Get("char_levels").AsGodotDictionary();
        char_levels[CharId] = 20;
        state.Set("char_levels", char_levels);
        state.Set("party_exp", 99999);
        Godot.Collections.Dictionary capped = levels.apply_available_levels();
        CheckEq(capped["changes"].AsGodotArray().Count, 0, "满级不再升级");
        CheckTrue(capped["capped"].AsBool(), "结果里标着已满级");
        CheckEq(state.Get("party_exp").AsInt32(), 99999, "满级后经验留在池子里");
    }

    /// <summary>面板那条进度：离下一级还差多少、池子里有多少、够不够。</summary>
    private void _check_progress(GodotObject state, LevelService levels)
    {
        Godot.Collections.Dictionary char_levels = state.Get("char_levels").AsGodotDictionary();
        char_levels[CharId] = 1;
        state.Set("char_levels", char_levels);
        state.Set("party_exp", 50);
        Godot.Collections.Dictionary progress = levels.exp_progress(CharId);
        CheckEq(progress["level"].AsInt32(), 1, "面板读到 1 级");
        CheckEq(progress["max_level"].AsInt32(), 20, "面板读到等级上限");
        CheckEq(progress["need"].AsInt32(), 100, "离 2 级还差 100");
        CheckEq(progress["pool"].AsInt32(), 50, "池子里 50 点");
        CheckFalse(progress["maxed"].AsBool(), "1 级没满级");

        char_levels[CharId] = 20;
        state.Set("char_levels", char_levels);
        Godot.Collections.Dictionary maxed = levels.exp_progress(CharId);
        CheckEq(maxed["level"].AsInt32(), 20, "面板读到 20 级");
        CheckEq(maxed["need"].AsInt32(), 0, "满级还差 0 经验");
        CheckTrue(maxed["maxed"].AsBool(), "面板能看出已经满级");
    }
}
