using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 角色面板数据：五维合成、派生数值、判定值、加点（C# 版，对位原 `tests/test_character_sheet.gd`）。
/// </summary>
public partial class TestCharacterSheet : TestCase
{
    public override string SuiteName() => "角色面板数据";

    public override void Run()
    {
        // 01 的判定值公式 `等级 + floor(裸属性 / 5)`——除数写死在代码里，而下面那几条断言
        // 都是用「实际值 vs 手算期望」比的，把除数从 5 改成 4 时**期望值也得跟着改才红**，
        // 于是变异探针（2026-10-03：`EVENT_SKILL_ATTR_DIVISOR 5→4`）没抓住。这里把除数本身钉住。
        CheckEq(CharacterSheet.EVENT_SKILL_ATTR_DIVISOR, 5, "判定值公式的除数是 5（01 原文）");
        TableDb db = GetDb();
        GodotObject state = NewGame(db, "normal");
        string char_id = state.Get("char_ids").AsStringArray()[0];
        var sheet = new CharacterSheet(db, state, char_id);
        _CheckBasics(db, state, sheet);
        _CheckFiveAttributes(db, state, sheet);
        _CheckStatsMatchCalculator(db, state, sheet);
        _CheckEventCheck(db, state, sheet);
        _CheckAllocation(db, state);
        _CheckViews(sheet);
        _CheckLoadout(db, state);
        _CheckCodexReward(db);
        _CheckUnusableSkillNote(db, state);
    }

    /// <summary>
    /// 图鉴奖励（设计 01「收集本身也是成长」）：每收集 5 部武学、七项各 +1、上限 10 次，
    /// 而且**面板与门槛必须看到同一套属性**（否则会出现「面板显示够门槛、实际学不会」）。
    ///
    /// 招式的两种「能不能用」，面板都要如实说清楚：
    ///   · **增益招式**（`sk_drunk_zuibu` 醉里乾坤·醉步）：整行 0（无伤害／倍率 0／段数 0、`target_type=self`），
    ///     但表里配了 `on_cast` 发放（醉步 → 忘忧）→ **战斗里能用**，施放一次给自己上 buff，
    ///     面板写「增益招式：施放时给自己上 buff」（2026-10-03 起；在那之前它被标成「用不出来」，见决策 221）。
    ///   · **真的没效果**的招式（既不打伤害、也没有 `on_cast` 发放）：写「战斗里用不出来（效果未配）」。
    /// </summary>
    private void _CheckUnusableSkillNote(TableDb db, GodotObject state)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        state.Call("learn_skill", char_id, "sk_drunk_zuibu");
        var found = new System.Collections.Generic.Dictionary<string, Godot.Collections.Dictionary>();
        foreach (Variant row_variant in new CharacterSheet(db, state, char_id).skill_rows())
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            found[row["skill_id"].AsString()] = row;
        }
        CheckTrue(found.ContainsKey("sk_drunk_zuibu"), "醉步已学会、会出现在武学列表里");
        if (found.ContainsKey("sk_drunk_zuibu"))
        {
            Godot.Collections.Dictionary broken = found["sk_drunk_zuibu"];
            CheckTrue(broken["battle_usable"].AsBool(), "醉步是**增益招式**：战斗里能用");
            CheckTrue(
                broken["battle_note"].AsString().Contains("增益"),
                $"面板会写清它是增益招式：{broken["battle_note"].AsString()}");
        }
        if (found.ContainsKey("sk_xuanwei_01"))
        {
            CheckTrue(found["sk_xuanwei_01"]["battle_usable"].AsBool(), "对照：玄微剑法·起手能用");
            CheckEq(found["sk_xuanwei_01"]["battle_note"].AsString(), "", "对照：能用的招式不带这条说明");
        }

        // 内功的「特殊效果 id」：表里填了、但没有定义表也没有代码落点（4 部高星内功都是这样）。
        // 面板要如实标出来（且**不能显示 id**），别让玩家以为装了没反应是 bug。
        state.Call("learn_skill", char_id, "pf_drunk_02");   // 醉里乾坤·忘忧 ★5：passive_effect = effect_drunk_mastery
        state.Call("learn_skill", char_id, "pf_xuanwei_01"); // 玄微心法·引气：没有 passive_effect
        var passive_rows = new System.Collections.Generic.Dictionary<string, Godot.Collections.Dictionary>();
        foreach (Variant row_variant in new CharacterSheet(db, state, char_id).skill_rows())
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            passive_rows[row["skill_id"].AsString()] = row;
        }
        CheckTrue(
            passive_rows.ContainsKey("pf_drunk_02") && passive_rows.ContainsKey("pf_xuanwei_01"),
            "两部内功都在列表里");
        if (passive_rows.ContainsKey("pf_drunk_02"))
        {
            CheckTrue(
                passive_rows["pf_drunk_02"]["special_effect_pending"].AsBool(),
                "忘忧标了「特殊效果暂未生效」");
        }
        if (passive_rows.ContainsKey("pf_xuanwei_01"))
        {
            CheckFalse(
                passive_rows["pf_xuanwei_01"]["special_effect_pending"].AsBool(),
                "对照：没填特殊效果的引气不标");
        }
    }

    private void _CheckCodexReward(TableDb db)
    {
        // 图鉴是**全队共有**的收集进度，而这个用例的剧本是「起始 1 部 → 再学 4 部到 5 部」。
        // 队伍人数会随 `character_base` 的行数变化（1 → 3~4），所以这里**钉住单人文书生**，
        // 才能验到「刚好 5 部」那条边界（框架说明决策 161）。
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        state.Get("char_levels").AsGodotDictionary()[char_id] = 12;
        var sheet = new CharacterSheet(db, state, char_id);
        CheckEq(sheet.collected_skill_count(), 1, "开局只有起始武学（图鉴 1 部）");
        CheckEq(sheet.codex_bonus()["times"].AsInt32(), 0, "不到 5 部没有奖励");
        int before_naked = sheet.naked_attrs().ContainsKey("int") ? sheet.naked_attrs()["int"].AsInt32() : 0;
        int before_total = sheet.total_attrs().ContainsKey("int") ? sheet.total_attrs()["int"].AsInt32() : 0;
        double before_atk = sheet.stats().ContainsKey("atk_phys") ? sheet.stats()["atk_phys"].AsDouble() : 0.0;

        // 再学 4 部凑够 5 部（用表里真实存在的武学 id，挑没有门槛的）
        foreach (string skill_id in new[] { "sk_wudu_02", "sk_bandit_slash", "sk_bandit_scout", "sk_chensha_02" })
        {
            state.Call("learn_skill", char_id, skill_id);
        }
        CheckEq(sheet.collected_skill_count(), 5, "凑够 5 部");
        CheckEq(sheet.codex_bonus()["times"].AsInt32(), 1, "5 部给一次奖励");
        CheckEq(sheet.codex_bonus()["bonus"].AsInt32(), 1, "一次奖励七项各 +1");
        CheckEq(
            sheet.naked_attrs().ContainsKey("int") ? sheet.naked_attrs()["int"].AsInt32() : 0,
            before_naked + 1,
            "裸属性也涨（判定值跟着涨）");
        CheckEq(
            sheet.total_attrs().ContainsKey("int") ? sheet.total_attrs()["int"].AsInt32() : 0,
            before_total + 1,
            "五维合计涨 1");
        CheckGt(
            sheet.stats().ContainsKey("atk_phys") ? sheet.stats()["atk_phys"].AsDouble() : 0.0,
            before_atk,
            "派生数值跟着涨（内功取智）");

        // 门槛与面板必须一致：把智抬到刚好够「五毒掌·蚀骨（智 12）」这条线
        int need = 12 - (sheet.loadout_attrs().ContainsKey("int") ? sheet.loadout_attrs()["int"].AsInt32() : 0);
        for (int index = 0; index < System.Math.Max(0, need); index++)
        {
            state.Call("spend_point", db, char_id, "int");
        }
        SkillLoadout loadout = SkillLoadout.create(db, state, char_id);
        CheckEq(
            loadout.attrs().ContainsKey("int") ? loadout.attrs()["int"].AsInt32() : 0,
            sheet.loadout_attrs().ContainsKey("int") ? sheet.loadout_attrs()["int"].AsInt32() : 0,
            "门槛看到的智与面板 loadout 一致（图鉴奖励两边都算）");
        CheckTrue(
            loadout.requirement_of("sk_wudu_03")["ok"].AsBool(),
            $"智到线了，门槛放行：{loadout.requirement_of("sk_wudu_03")["reason"].AsString()}");

        // 全收集：把表里所有武学都记下（`learn_skill` 是低层记录器，不过门槛）
        foreach (Variant row_variant in db.rows("skill_base"))
        {
            state.Call("learn_skill", char_id, row_variant.As<SkillBaseRow>().skill_id);
        }
        int total_skills = db.rows("skill_base").Count;
        CheckEq(sheet.collected_skill_count(), total_skills, $"表里 {total_skills} 部全部计入图鉴");
        Godot.Collections.Dictionary full = sheet.codex_bonus();
        // 全收集的奖励次数 = min(部数 ÷ 步长, 上限)：表里现在 52 部 → 52/5 = 10，正好压在上限上
        int raw_times = (int)System.Math.Floor(total_skills / 5.0);
        int expect_times = System.Math.Min(raw_times, full["cap"].AsInt32());
        CheckEq(
            full["times"].AsInt32(),
            expect_times,
            $"全收集 {total_skills} 部 → {expect_times} 次奖励（÷5 与上限 {full["cap"].AsInt32()} 取小）");
        CheckEq(full["bonus"].AsInt32(), expect_times, $"每次 +1，全收集共 +{expect_times}");
    }

    /// <summary>面板上的武学：槽位与公式一致、每行带「装了没／为什么装不上」，并占用同一套装配数据</summary>
    private void _CheckLoadout(TableDb db, GodotObject state)
    {
        string char_id = state.Get("char_ids").AsStringArray()[0];
        var sheet = new CharacterSheet(db, state, char_id);
        Godot.Collections.Dictionary summary = sheet.slot_summary();
        // 这一步之前用例把角色抬到了 10 级（见 _check_five_attributes）：
        // 招式槽 2 + 10/5 + 悟10/8 = 5，内功容量 2 + 10/6 + 根5/10 = 3
        CheckEq(summary["active_slots"].AsInt32(), 5, "10 级书生招式槽 5");
        CheckEq(summary["passive_capacity"].AsInt32(), 3, "10 级书生内功容量 3");
        CheckEq(summary["active_used"].AsInt32(), 1, "起始武学占着一个招式槽");
        CheckEq(summary["passive_used"].AsInt32(), 0, "内功容量还空着");

        Godot.Collections.Array rows = sheet.skill_rows();
        CheckEq(rows.Count, 1, "开局只有一部已学武学");
        if (rows.Count == 0)
        {
            return;
        }
        Godot.Collections.Dictionary row = rows[0].AsGodotDictionary();
        CheckEq(row["skill_id"].AsString(), "sk_xuanwei_01", "列出的是起始武学");
        CheckTrue(row["equipped"].AsBool(), "标记为已装配");
        CheckEq(row["equip_error"].AsString(), "", "已装配的行没有错误原因");

        // 卸下之后：行变成未装配，且给出「已经装配」以外的原因（槽位空着所以能再装）
        sheet.loadout().unequip("sk_xuanwei_01");
        var after = new CharacterSheet(db, state, char_id);
        CheckEq(after.slot_summary()["active_used"].AsInt32(), 0, "卸下后面板槽位空出");
        Godot.Collections.Dictionary after_row = after.skill_rows()[0].AsGodotDictionary();
        CheckFalse(after_row["equipped"].AsBool(), "卸下后不再是已装配");
        CheckEq(after_row["equip_error"].AsString(), "", "能装回来时不给错误原因");
        after.loadout().equip("sk_xuanwei_01");
    }

    private void _CheckBasics(TableDb db, GodotObject state, CharacterSheet sheet)
    {
        CheckTrue(sheet.valid(), "角色模板有效");
        CheckEq(sheet.display_name(), "家道失落的书生", "显示名取自模板");
        CheckEq(sheet.weapon_type_id(), "sword", "模板武器类型");
        CheckEq(sheet.weapon_type_name(), "剑", "武器类型显示名");
        CheckEq(sheet.level, state.Call("level_of", state.Get("char_ids").AsStringArray()[0]).AsInt32(), "等级取自存档");
        CheckEq(sheet.role_tag(), "探索型", "出身标记");
    }

    private void _CheckFiveAttributes(TableDb db, GodotObject state, CharacterSheet sheet)
    {
        Godot.Collections.Dictionary base_attrs = sheet.base_attrs();
        Godot.Collections.Dictionary naked = sheet.naked_attrs();
        Godot.Collections.Dictionary total = sheet.total_attrs();
        CheckEq(naked["str"].AsInt32(), base_attrs["str"].AsInt32(), "没加点时裸值等于模板值");
        CheckEq(total["agi"].AsInt32(), naked["agi"].AsInt32(), "初始武器没有属性点加成");

        // 加一点力：裸值与合计都涨
        state.Call("spend_point", db, state.Get("char_ids").AsStringArray()[0], "str");
        string char_id = state.Get("char_ids").AsStringArray()[0];
        sheet = new CharacterSheet(db, state, char_id);
        CheckEq(sheet.naked_attrs()["str"].AsInt32(), base_attrs["str"].AsInt32() + 1, "加点进入裸值");

        // 换上一件带属性的装备：合计涨、裸值不变
        state.Get("char_levels").AsGodotDictionary()[char_id] = 10; // 青锋剑要 5 级，抬到 10 级一次说清
        sheet = new CharacterSheet(db, state, char_id);
        Inventory inventory = state.Get("inventory").As<Inventory>();
        string sword = inventory.add_equipment(db, "eq_sword_02", new Godot.Collections.Array());
        inventory.equip(db, char_id, sheet.level, "sword", sword);
        sheet = new CharacterSheet(db, state, char_id);
        CheckEq(
            sheet.total_attrs()["agi"].AsInt32(),
            sheet.naked_attrs()["agi"].AsInt32() + 2,
            "装备的敏 +2 只进合计");
    }

    private void _CheckStatsMatchCalculator(TableDb db, GodotObject state, CharacterSheet sheet)
    {
        AttributeCalculator calculator = AttributeCalculator.create(db);
        Godot.Collections.Dictionary expected = calculator.compute(
            sheet.level, sheet.base_attrs(), sheet.allocations(), sheet.contributions(), true);
        Godot.Collections.Dictionary actual = sheet.stats();
        foreach (string stat_id in new[] { "atk_phys", "atk_qi", "hp_max", "def_phys", "speed" })
        {
            CheckEq(
                actual.ContainsKey(stat_id) ? actual[stat_id].AsInt32() : 0,
                expected.ContainsKey(stat_id) ? expected[stat_id].AsInt32() : 0,
                $"{stat_id} 与属性计算器一致");
        }
    }

    private void _CheckEventCheck(TableDb db, GodotObject state, CharacterSheet sheet)
    {
        // 文学 3 + floor(智裸值 10 / 5) = 5
        Godot.Collections.Dictionary wenxue = sheet.event_check_value("skill:wenxue");
        CheckEq(wenxue["value"].AsInt32(), 5, "文学判定 = 等级 3 + 智裸值/5");
        CheckTrue(wenxue["naked"].AsBool(), "判定值标记为裸属性");

        Godot.Collections.Dictionary qimen = sheet.event_check_value("skill:qimen");
        CheckEq(qimen["value"].AsInt32(), 3, "奇门判定 = 等级 1 + 智裸值/5 = 3");

        Godot.Collections.Dictionary strength = sheet.event_check_value("attr:str");
        CheckEq(
            strength["value"].AsInt32(),
            sheet.naked_attrs()["str"].AsInt32(),
            "属性判定取裸值");

        // 判定只用裸属性：换装不改变判定值
        string char_id = state.Get("char_ids").AsStringArray()[0];
        state.Get("char_levels").AsGodotDictionary()[char_id] = 10; // 药王玉佩要 6 级
        sheet = new CharacterSheet(db, state, char_id);
        int before = sheet.event_check_value("skill:wenxue")["value"].AsInt32();
        Inventory inventory = state.Get("inventory").As<Inventory>();
        string int_ring = inventory.add_equipment(db, "eq_acc_02", new Godot.Collections.Array());
        inventory.equip(db, char_id, sheet.level, sheet.weapon_type_id(), int_ring);
        var after_sheet = new CharacterSheet(db, state, char_id);
        CheckEq(
            after_sheet.event_check_value("skill:wenxue")["value"].AsInt32(),
            before,
            "换装不改变判定值（判定只用裸属性）");
        CheckGt(
            after_sheet.total_attrs()["int"].AsInt32(),
            after_sheet.naked_attrs()["int"].AsInt32(),
            "但装备确实加了智的合计");

        Godot.Collections.Array rows = after_sheet.event_skill_rows();
        CheckEq(rows.Count, 5, "五项目非战斗技能都在面板上");
        var wenxue_row = new Godot.Collections.Dictionary();
        foreach (Variant row_variant in rows)
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            if (row["skill_id"].AsString() == "wenxue")
            {
                wenxue_row = row;
            }
        }
        CheckEq(
            wenxue_row.ContainsKey("level") ? wenxue_row["level"].AsInt32() : -1,
            3,
            "文学初始等级 3");
        CheckEq(
            wenxue_row.ContainsKey("related_attr_name") ? wenxue_row["related_attr_name"].AsString() : "",
            "智",
            "文学关联属性是智");
    }

    private void _CheckAllocation(TableDb db, GodotObject state)
    {
        GodotObject fresh = NewGame(db, "normal");
        string char_id = fresh.Get("char_ids").AsStringArray()[0];
        // 1 级给 5 点（level_growth.upgrade_points；设计 0.13.0 由 3 提到 5）
        CheckEq(fresh.Call("available_points", db, char_id).AsInt32(), 5, "1 级有 5 点可分配");
        CheckTrue(fresh.Call("spend_point", db, char_id, "agi").AsGodotDictionary()["ok"].AsBool(), "加点成功");
        CheckTrue(fresh.Call("spend_point", db, char_id, "agi").AsGodotDictionary()["ok"].AsBool(), "再加一点");
        CheckTrue(fresh.Call("spend_point", db, char_id, "con").AsGodotDictionary()["ok"].AsBool(), "第三点也能加");
        CheckTrue(fresh.Call("spend_point", db, char_id, "int").AsGodotDictionary()["ok"].AsBool(), "第四点也能加");
        CheckTrue(fresh.Call("spend_point", db, char_id, "luk").AsGodotDictionary()["ok"].AsBool(), "第五点也能加");
        CheckEq(fresh.Call("available_points", db, char_id).AsInt32(), 0, "用完就没有了");
        Godot.Collections.Dictionary overflow = fresh.Call("spend_point", db, char_id, "agi").AsGodotDictionary();
        CheckFalse(overflow["ok"].AsBool(), "没有点数时加点失败");
        CheckEq(
            fresh.Call("allocations_of", char_id).AsGodotDictionary()["agi"].AsInt32(),
            2,
            "加点记在存档里");
        Godot.Collections.Dictionary bad = fresh.Call("spend_point", db, char_id, "luck").AsGodotDictionary();
        CheckFalse(bad["ok"].AsBool(), "不存在的属性不能加");
        // 设计 0.13.0：悟性／根骨是「资质」——创建时定死，只能靠图鉴奖励与特定内功提升
        Godot.Collections.Dictionary talent = fresh.Call("spend_point", db, char_id, "wu").AsGodotDictionary();
        CheckFalse(talent["ok"].AsBool(), "悟性是不可加点的资质");
        CheckTrue(
            talent["error"].AsString().Contains("资质"),
            $"原因写的是资质而不是「没有点数」：{talent["error"].AsString()}");
        Godot.Collections.Dictionary alloc = fresh.Call("allocations_of", char_id).AsGodotDictionary();
        CheckEq(alloc.ContainsKey("wu") ? alloc["wu"].AsInt32() : 0, 0, "被拒之后没写进存档");
        CheckFalse(
            fresh.Call("allocation_block_reason", db, "gen").AsString().Length == 0,
            "根骨同样算资质");
        CheckEq(fresh.Call("allocation_block_reason", db, "str").AsString(), "", "五维可以加点");

        // 升级后点数变多：1~3 级每级 5 点 → 累计 15，前面已经用掉 5 点
        fresh.Get("char_levels").AsGodotDictionary()[char_id] = 3;
        CheckEq(fresh.Call("available_points", db, char_id).AsInt32(), 10, "3 级累计 15 点，已用 5 点，剩 10 点");
    }

    private void _CheckViews(CharacterSheet sheet)
    {
        Godot.Collections.Array slots = sheet.equipped_rows();
        CheckEq(slots.Count, 8, "7 类槽位＋戒指多一格 = 8 行");
        int weapon_rows = 0;
        int empty_rows = 0;
        foreach (Variant row_variant in slots)
        {
            Godot.Collections.Dictionary row = row_variant.AsGodotDictionary();
            if (row["slot_id"].AsString() == "weapon")
            {
                weapon_rows++;
                CheckFalse(row["empty"].AsBool(), "武器槽装了初始武器");
                CheckTrue(row["summary"].AsString().Contains("外功攻击"), "槽位摘要显示关键属性");
            }
            if (row["empty"].AsBool())
            {
                empty_rows++;
            }
        }
        CheckEq(weapon_rows, 1, "武器槽只有一格");
        CheckGt(empty_rows, 0, "没装备的槽位标记为空");

        Godot.Collections.Array skills = sheet.skill_rows();
        CheckEq(skills.Count, 1, "模板带一门起始武学");
        Godot.Collections.Dictionary first = skills[0].AsGodotDictionary();
        CheckEq(first["skill_id"].AsString(), "sk_xuanwei_01", "起始武学是玄微剑法·起手");
        CheckTrue(first["battle_usable"].AsBool(), "起始武学在战斗里能用（有伤害类型且倍率 > 0）");
        CheckEq(first["battle_note"].AsString(), "", "能用的招式不挂「用不出来」的说明");

        string hit_label = sheet.stat_label("hit_rate");
        CheckTrue(hit_label.Contains("%"), "百分比类派生数值按百分比显示");
        CheckEq(sheet.stat_name("atk_phys"), "外功攻击", "派生数值显示名");
    }
}
