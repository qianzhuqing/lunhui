using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 成长系统：槽位上限、熟练度、打坐费用、图鉴奖励、面板显示——
/// 对位 `tests/test_growth.gd`（2026-10-11 P4 第十五批搬 C#）。
///
/// **类名取 `TestGrowthRules`：`TestGrowth.cs` 已被占**（`GrowthCalculator` 搬 C# 时写的那份，
/// suite 名带「（C# 侧）」，另加天赋「藏书癖」那几条倍数断言）。这一份是旧链原来那份，
/// **多出的一节是面板显示**（`_check_panel`：要 `CharacterSheet` ＋ `GameState`，
/// 那时候它们还没搬过来，所以上一批搬 `GrowthCalculator` 时留着没动）。
///
/// 期望值全部写死「公式推出来的那个数」：数据或公式一改它就红，这是故意的。
/// </summary>
public partial class TestGrowthRules : TestCase
{
    public override string SuiteName() => "成长系统：槽位与熟练度";

    public override void Run()
    {
        TableDb db = GetDb();
        GrowthCalculator growth = GrowthCalculator.create(db);
        _check_slots(growth);
        _check_mastery(growth);
        _check_cultivate(growth);
        _check_codex(growth);
        _check_passive_fit(db, growth);
        _check_panel(db);
    }

    /// <summary>
    /// 公式：招式槽 = 2 + floor(等级/5) + floor(悟性/8)（上限 9）
    ///       内功容量 = 2 + floor(等级/6) + floor(根骨/10)（上限 7）
    /// </summary>
    private void _check_slots(GrowthCalculator growth)
    {
        // 模板初始资质是 悟 10 / 根 5
        CheckEq(growth.active_slots(1, _attrs(10, 5)), 3, "1 级书生：招式槽 3");
        CheckEq(
            growth.passive_capacity(1, _attrs(10, 5)),
            2,
            "1 级书生：内功容量 2（连一部 ★5 都装不下）");
        CheckEq(growth.active_slots(20, _attrs(10, 5)), 7, "20 级不投资资质：招式槽 7");
        CheckEq(growth.passive_capacity(20, _attrs(10, 5)), 5, "20 级不投资资质：容量 5");
        CheckEq(growth.active_slots(20, _attrs(30, 5)), 9, "20 级专注悟性：招式槽满 9");
        CheckEq(growth.passive_capacity(20, _attrs(5, 25)), 7, "20 级专注根骨：容量满 7");
        CheckEq(growth.active_slots(20, _attrs(5, 25)), 6, "20 级专注根骨：招式槽只有 6");
        // 硬上限
        CheckEq(growth.active_slots(99, _single("wu", 999)), 9, "招式槽不会超过硬上限 9");
        CheckEq(growth.passive_capacity(99, _single("gen", 999)), 7, "内功容量不会超过硬上限 7");
        // 注意：01_角色系统.md 的采样表里「20 级、专注悟性 30」那行把内功容量写成 4，
        // 但容量公式与悟性无关、20 级最低也是 5，所以按公式取 5，已在交接表里回报订正。
        CheckEq(
            growth.passive_capacity(20, _attrs(30, 5)),
            5,
            "内功容量与悟性无关（01 的采样表已按公式订正，并由 test_handshake 逐行核）");
    }

    /// <summary>招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)</summary>
    private void _check_mastery(GrowthCalculator growth)
    {
        CheckEq(growth.mastery_max(), 10, "熟练度上限 10");
        CheckFloat(growth.mastery_multiplier(1, 0), 1.0, "没练过时系数是 1.0");
        CheckFloat(growth.mastery_multiplier(1, 10), 1.5, "★1 练满 ×1.50");
        CheckFloat(growth.mastery_multiplier(2, 10), 1.45, "★2 练满 ×1.45");
        CheckFloat(growth.mastery_multiplier(3, 10), 1.4, "★3 练满 ×1.40");
        CheckFloat(growth.mastery_multiplier(4, 10), 1.3, "★4 练满 ×1.30");
        CheckFloat(growth.mastery_multiplier(5, 10), 1.25, "★5 练满 ×1.25");
        CheckFloat(growth.mastery_multiplier(1, 99), 1.5, "熟练度超过上限按上限算");
        CheckFloat(growth.mastery_multiplier(1, -5), 1.0, "负熟练度按 0 算");
        var star5 = growth.star_row(5) as SkillStarDefRow;
        CheckNotNull(star5, "5 星定义存在");
        if (star5 != null)
        {
            CheckEq(star5.name_cn, "绝学", "★5 叫绝学");
            CheckEq(star5.learn_req_value, 25, "★5 修习门槛 25");
        }
    }

    private void _check_cultivate(GrowthCalculator growth)
    {
        CheckEq(growth.cultivate_cost(1, 1), 50, "★1 的第一级打坐费 50");
        CheckEq(growth.cultivate_cost(1, 2), 80, "第二级 50 × 1.6 = 80");
        CheckEq(growth.cultivate_cost(5, 1), 450, "★5 的第一级 450");
        CheckGt(
            growth.cultivate_cost(5, 10),
            growth.cultivate_cost(5, 1),
            "练得越高越贵");
    }

    private void _check_codex(GrowthCalculator growth)
    {
        CheckEq(growth.codex_bonus(0, 1.0)["times"].AsInt32(), 0, "一部都没有时没有奖励");
        CheckEq(growth.codex_bonus(4, 1.0)["times"].AsInt32(), 0, "收集 4 部还没到 5 部");
        CheckEq(growth.codex_bonus(5, 1.0)["times"].AsInt32(), 1, "5 部给一次");
        CheckEq(growth.codex_bonus(5, 1.0)["bonus"].AsInt32(), 1, "一次奖励七项各 +1");
        CheckEq(growth.codex_bonus(50, 1.0)["times"].AsInt32(), 10, "50 部给满 10 次");
        CheckEq(growth.codex_bonus(500, 1.0)["times"].AsInt32(), 10, "超过上限仍是 10 次");
        Godot.Collections.Dictionary codex_attrs = growth.codex_bonus(10, 1.0)["attrs"].AsGodotDictionary();
        CheckEq(codex_attrs.Count, 7, "图鉴奖励覆盖七项属性");
        foreach (string attr_id in new[] { "str", "con", "agi", "int", "luk", "wu", "gen" })
        {
            CheckEq(codex_attrs[attr_id].AsInt32(), 2, $"{attr_id} 也吃到图鉴奖励");
        }
    }

    private void _check_passive_fit(TableDb db, GrowthCalculator growth)
    {
        var one_small = new[] { "pf_xuanwei_01" }; // ★1 占 1 格
        var one_top = new[] { "pf_xuanwei_05" }; // ★5 占 3 格
        Godot.Collections.Dictionary low = _attrs(10, 5);
        CheckTrue(growth.passive_fit(one_small, 1, low)["fits"].AsBool(), "1 级装得下一部低级内功");
        CheckFalse(
            growth.passive_fit(one_top, 1, low)["fits"].AsBool(),
            "1 级只有 2 格，装不下 ★5（占 3 格）");
        CheckTrue(
            growth.passive_fit(one_top, 20, _attrs(10, 25))["fits"].AsBool(),
            "容量 7 时装得下 ★5");
        CheckFalse(
            growth.passive_fit(new[] { "pf_xuanwei_05", "pf_xuanwei_01" }, 1, low)["fits"].AsBool(),
            "3 + 1 = 4 格超过 1 级的 2 格");
        Godot.Collections.Array contributions =
            growth.passive_contributions(one_small, new Godot.Collections.Dictionary());
        CheckGt(contributions.Count, 0, "内功加成能转成属性贡献");
        _check_mastery_scales_passive(db, growth);
    }

    /// <summary>
    /// 熟练度放大内功加成（0.15.0 口径确认第 3 条）：**只放大 `stat:` 类，不放大 `attr:` 类**。
    ///
    /// 用真实数据挑一部**同时有 attr: 与 stat: 两种加成**的内功，这样一条用例能同时钉两边：
    /// 放大错边（把 attr 也乘了）或者系数公式换了都会红。
    /// </summary>
    private void _check_mastery_scales_passive(TableDb db, GrowthCalculator growth)
    {
        var picked = "";
        // 候选列表来自 `skill_passive_stat` 里**两种前缀都有**的那几部（一个都不能少，
        // 少了这条用例会退化成"夹具找不到"的假绿）
        foreach (string skill_id in new[]
                 { "pf_xuanwei_05", "pf_heifeng_02", "pf_heifeng_03", "pf_tieqiang_01", "pf_wudu_04" })
        {
            var base_row = db.get_row("skill_base", skill_id) as SkillBaseRow;
            if (base_row == null)
            {
                continue;
            }

            var has_attr = false;
            var has_stat = false;
            foreach (Variant entry in db.rows_where("skill_passive_stat", "skill_id", skill_id))
            {
                var stat_row = entry.AsGodotObject() as SkillPassiveStatRow;
                if (stat_row == null)
                {
                    continue;
                }

                string kind = stat_row.parsed_target()["kind"].AsString();
                if (kind == "attr")
                {
                    has_attr = true;
                }
                else if (kind == "stat")
                {
                    has_stat = true;
                }
            }

            if (has_attr && has_stat)
            {
                picked = skill_id;
                break;
            }
        }

        CheckTrue(picked.Length != 0, "找得到一部同时有 attr: 与 stat: 加成的内功（熟练度用例的夹具）");
        if (picked.Length == 0)
        {
            return;
        }

        var ids = new[] { picked };
        var picked_row = db.get_row("skill_base", picked) as SkillBaseRow;
        int star = picked_row?.star ?? 0;
        Godot.Collections.Array baseline =
            growth.passive_contributions(ids, new Godot.Collections.Dictionary());
        Godot.Collections.Array scaled = growth.passive_contributions(
            ids, new Godot.Collections.Dictionary { { picked, 5 } });
        CheckEq(scaled.Count, baseline.Count, "每条加成都在（熟练度不改变条数）");
        double factor = growth.mastery_multiplier(star, 5);
        CheckGt(factor, 1.0, $"5 级熟练度系数 > 1（实际 {factor:F2}）");
        for (var i = 0; i < baseline.Count; i++)
        {
            Godot.Collections.Dictionary before = baseline[i].AsGodotDictionary();
            Godot.Collections.Dictionary after = scaled[i].AsGodotDictionary();
            var is_attr = before["kind"].AsString() == AttributeCalculator.CONTRIB_ATTR_POINT;
            double expected = before["value"].AsDouble() * (is_attr ? 1.0 : factor);
            string target = before["target"].AsString();
            CheckFloat(
                after["value"].AsDouble(),
                expected,
                $"{picked} 的 {target} 加成熟练度 5 级后应为 {expected:F3}"
                    + "（attr 不放大、stat 放大）",
                0.001);
        }
    }

    private void _check_panel(TableDb db)
    {
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CharacterSheet sheet = CharacterSheet.create(db, state, char_id);
        CheckEq(sheet.active_slots(), 3, "面板显示招式槽 3");
        CheckEq(sheet.passive_capacity(), 2, "面板显示内功容量 2");
        CheckEq(sheet.stats()["slot_active"].AsInt32(), 3, "槽位并入派生数值供面板显示");
        CheckEq(sheet.total_attrs().Count, 7, "七维属性都在面板上");
        CheckEq(_i(sheet.naked_attrs(), "wu"), 10, "悟性初始 10");
        CheckEq(_i(sheet.naked_attrs(), "gen"), 5, "根骨初始 5");
        int str_before = sheet.total_attrs()["str"].AsInt32();
        // 设计 0.13.0：悟性与根骨是「资质」，不能靠加点涨（只能靠图鉴奖励与特定内功）
        CheckFalse(
            state.Call("spend_point", db, char_id, "wu").AsGodotDictionary()["ok"].AsBool(),
            "悟性不可加点（资质）");
        CheckFalse(
            state.Call("spend_point", db, char_id, "gen").AsGodotDictionary()["ok"].AsBool(),
            "根骨不可加点（资质）");
        CheckTrue(
            state.Call("spend_point", db, char_id, "str").AsGodotDictionary()["ok"].AsBool(),
            "五维照样能加");
        CharacterSheet after = CharacterSheet.create(db, state, char_id);
        CheckEq(after.total_attrs()["wu"].AsInt32(), 10, "被拒之后悟性没变");
        CheckEq(after.total_attrs()["str"].AsInt32(), str_before + 1, "加了力的那一点面板跟着变");
    }

    private static Godot.Collections.Dictionary _attrs(int wu, int gen) => new()
    {
        { "wu", wu },
        { "gen", gen },
    };

    private static Godot.Collections.Dictionary _single(string key, int value) => new()
    {
        { key, value },
    };

    /// <summary>对位 GDScript 的 `dict.get(key, 0)`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;
}
