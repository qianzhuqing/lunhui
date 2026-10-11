using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 熟练度与打坐。
///
/// **2026-10-11（P4 第十九批）起这份就是唯一一份**：旧链的 `tests/test_mastery.gd` 已并入本文件后退场。
/// 并进来的三节是：① `_check_multiplier`（熟练度直接决定招式倍率 ＋ 熟练度进存档往返）；
/// ② `_check_multi_member_ui`（**打坐界面的多人分支**：4 人队伍各有成员按钮、切人不串号）；
/// ③ `_check_full_train_budget`（**整套**打坐的累积边界：钱刚好够从 0 连练到上限时最后一次正好花光，
/// 少一文则最后一次被拒、那一文留着）；外加把 `_check_combat_usage` 补上「真打一场看施放次数」。
///
/// 为什么这几节当时没搬：它们要 `cultivate_screen`／`battle_simulator`／`GameState` 序列化，
/// 那时还在 GDScript 里。**这一批把缺口补齐，旧文件退场**——没有另外克隆一个 300 行的重复文件。
///
/// `state` 是 GDScript 的 `GameState`（`TestCase.SoloState` 造出来的 `GodotObject`）。
/// </summary>
public partial class TestMastery : TestCase
{
    private const string CharId = "scholar_fallen";
    private const string Skill = "sk_xuanwei_01";
    private const string CultivateScene = "res://scenes/cultivate_screen.tscn";

    /// <summary>`growth_const.mastery_max` 的当前值——多人界面那一节没有 `MasteryService` 在手，
    /// 照旧链写死同一个数（改上限时这条会红，正是想要的）。</summary>
    private const int MasteryMax = 10;

    public override string SuiteName() => "熟练度与打坐";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        var mastery = MasteryService.create(db, state);
        _check_identity(db, mastery);
        _check_cost_curve(db, state, mastery);
        _check_train(state, mastery);
        _check_combat_usage(db, state, mastery);
        _check_multiplier(db, state);
        _check_multi_member_ui(db);
        _check_full_train_budget(state, mastery);
        _check_describe(state, mastery);
    }

    /// <summary>上限、星级与招式名的取值口径。</summary>
    private void _check_identity(TableDb db, MasteryService mastery)
    {
        CheckEq(mastery.mastery_max(), 10, "熟练度上限 10（growth_const.mastery_max）");
        CheckEq(mastery.skill_name(Skill), "玄微剑法·起手", "读得到招式名");
        CheckEq(mastery.star_of(Skill), 1, "玄微剑法·起手是 ★1");
        CheckEq(mastery.star_of("sk_not_exist"), 0, "表里没有的武学 0 星");
        CheckEq(mastery.skill_name("sk_not_exist"), "sk_not_exist", "没有就退回 id");
        CheckEq(db.get_row("skill_base", Skill) != null, true, "这条用例依赖 sk_xuanwei_01 在表里");
    }

    /// <summary>打坐费用按「当前等级 → 下一级」逐级变贵。</summary>
    private void _check_cost_curve(TableDb db, GodotObject state, MasteryService mastery)
    {
        var growth = GrowthCalculator.create(db);
        var star = db.get_row("skill_star_def", "1") as SkillStarDefRow;
        double growth_rate = growth.constant("cultivate_cost_growth");
        CheckFloat(mastery.train_cost(CharId, Skill), star.cultivate_cost_base, "0 → 1 用基础价");
        var previous = 0;
        for (var level = 1; level < 5; level++)
        {
            state.Call("set_mastery", CharId, Skill, level, mastery.mastery_max());
            var expected = (int)System.Math.Round(
                star.cultivate_cost_base * System.Math.Pow(growth_rate, level),
                System.MidpointRounding.AwayFromZero);
            CheckEq(mastery.train_cost(CharId, Skill), expected, $"{level} → {level + 1} 的费用");
            CheckGt(mastery.train_cost(CharId, Skill), previous, "费用逐级上涨");
            previous = mastery.train_cost(CharId, Skill);
        }

        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());
    }

    /// <summary>未学／满级／缺钱都要被拒；成功就扣钱 +1 并写进存档。</summary>
    private void _check_train(GodotObject state, MasteryService mastery)
    {
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        inventory.Set("money", 0);
        Godot.Collections.Dictionary poor = mastery.can_train(CharId, Skill);
        CheckFalse(poor["ok"].AsBool(), "没钱不能打坐");
        CheckTrue(poor["error"].AsString().Contains("铜钱"), $"说明是铜钱不够：{poor["error"].AsString()}");

        Godot.Collections.Dictionary unknown = mastery.can_train(CharId, "sk_chensha_01");
        CheckFalse(unknown["ok"].AsBool(), "没学过的武学不能打坐");
        CheckTrue(
            unknown["error"].AsString().Contains("还没学会"),
            $"说明没学会：{unknown["error"].AsString()}");

        inventory.Set("money", 1000);
        int cost = mastery.train_cost(CharId, Skill);
        Godot.Collections.Dictionary trained = mastery.train(CharId, Skill);
        CheckTrue(trained["ok"].AsBool(), $"打坐成功：{trained["error"].AsString()}");
        CheckEq(trained["cost"].AsInt32(), cost, "按表里的费用扣钱");
        CheckEq(trained["before"].AsInt32(), 0, "打坐前 0 级");
        CheckEq(inventory.Get("money").AsInt32(), 1000 - cost, "铜钱扣掉");
        CheckEq(mastery.mastery_of(CharId, Skill), 1, "熟练度 +1");
        CheckEq(state.Call("mastery_of", CharId, Skill).AsInt32(), 1, "写进存档");

        state.Call("set_mastery", CharId, Skill, mastery.mastery_max(), mastery.mastery_max());
        Godot.Collections.Dictionary maxed = mastery.can_train(CharId, Skill);
        CheckFalse(maxed["ok"].AsBool(), "练满不能再打坐");
        CheckTrue(maxed["error"].AsString().Contains("练满"), $"说明已经练满：{maxed["error"].AsString()}");
        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());

        // 「差一文」边界：刚好够钱能打坐、少一文就拒（这类 off-by-one 最容易被后来的改动踩坏）
        int exact_cost = mastery.train_cost(CharId, Skill);
        inventory.Set("money", exact_cost - 1);
        Godot.Collections.Dictionary short_of = mastery.train(CharId, Skill);
        CheckFalse(short_of["ok"].AsBool(), "差一文打不了坐");
        CheckEq(inventory.Get("money").AsInt32(), exact_cost - 1, "被拒时一文不扣");
        CheckEq(mastery.mastery_of(CharId, Skill), 0, "被拒时熟练度不动");
        inventory.Set("money", exact_cost);
        Godot.Collections.Dictionary exact = mastery.train(CharId, Skill);
        CheckTrue(exact["ok"].AsBool(), $"刚好够钱能打坐：{exact["error"].AsString()}");
        CheckEq(inventory.Get("money").AsInt32(), 0, "刚好花光");
        CheckEq(mastery.mastery_of(CharId, Skill), 1, "熟练度 +1");
        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());
    }

    /// <summary>战斗施放：每次施放 +1，封顶；`cap` 只压涨不压低；只统计真正施放过的招式。</summary>
    private void _check_combat_usage(TableDb db, GodotObject state, MasteryService mastery)
    {
        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());
        Godot.Collections.Array gained = mastery.apply_combat_usage(
            CharId, new Godot.Collections.Dictionary { { Skill, 3 } }, 0);
        CheckEq(gained.Count, 1, "只有施放过的招式进结算");
        CheckEq(gained[0].AsGodotDictionary()["gained"].AsInt32(), 3, "施放 3 次 +3");
        CheckEq(gained[0].AsGodotDictionary()["before"].AsInt32(), 0, "结算里带涨之前的等级");
        CheckEq(gained[0].AsGodotDictionary()["after"].AsInt32(), 3, "结算里带涨之后的等级");
        CheckEq(mastery.mastery_of(CharId, Skill), 3, "熟练度到 3");

        // 封顶：再施放 20 次也只到上限；练满之后不再有回执
        mastery.apply_combat_usage(CharId, new Godot.Collections.Dictionary { { Skill, 20 } }, 0);
        CheckEq(mastery.mastery_of(CharId, Skill), mastery.mastery_max(), "熟练度封顶在 mastery_max");
        Godot.Collections.Array no_gain = mastery.apply_combat_usage(
            CharId, new Godot.Collections.Dictionary { { Skill, 5 } }, 0);
        CheckEq(no_gain.Count, 0, "练满之后不再增长");

        // 练习战的 cap：5 次施放只涨到 3；已经练过的不因为 cap 掉级
        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());
        mastery.apply_combat_usage(CharId, new Godot.Collections.Dictionary { { Skill, 5 } }, 3);
        CheckEq(mastery.mastery_of(CharId, Skill), 3, "cap=3 时 5 次施放只涨到 3");
        state.Call("set_mastery", CharId, Skill, 5, mastery.mastery_max());
        mastery.apply_combat_usage(CharId, new Godot.Collections.Dictionary { { Skill, 5 } }, 3);
        CheckEq(mastery.mastery_of(CharId, Skill), 5, "已经 5 级时打木桩不掉回 cap");

        // 没施放过就没有回执
        Godot.Collections.Array empty = mastery.apply_combat_usage(
            CharId, new Godot.Collections.Dictionary(), 0);
        CheckEq(empty.Count, 0, "空 usage 没有回执");
        state.Call("set_mastery", CharId, Skill, 0, mastery.mastery_max());

        // 真打一场：simulator 会记下谁施放了什么
        BattleActor actor = BattleActor.from_character(
            db,
            db.get_row("character_base", CharId),
            12,
            new Godot.Collections.Dictionary(),
            new Godot.Collections.Array());
        actor.skills = new[] { Skill };
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(4242));
        sim.setup(
            new Godot.Collections.Array { actor }, enemies, new Godot.Collections.Dictionary());
        var guard = 0;
        while (!sim.finished() && guard < 40)
        {
            guard += 1;
            if (!sim.in_round())
            {
                sim.begin_round();
            }

            if (sim.current_actor() == actor)
            {
                Godot.Collections.Array skills = sim.available_skills(actor);
                if (skills.Count == 0)
                {
                    sim.auto_act();
                }
                else
                {
                    sim.act(actor, skills[0].As<Resource>(), enemies[0].AsGodotObject() as BattleActor);
                }
            }
            else
            {
                sim.auto_act();
            }
        }

        Godot.Collections.Dictionary uses = sim.skill_uses(actor.actor_id);
        CheckGt(_i(uses, Skill), 0.0, $"战斗记录下了施放次数：{uses}");
        Godot.Collections.Array settled = mastery.apply_combat_usage(CharId, uses, 0);
        CheckGt(settled.Count, 0.0, "打完能结算出熟练度");
        CheckEq(
            mastery.mastery_of(CharId, Skill),
            System.Math.Min(_i(uses, Skill), mastery.mastery_max()),
            $"熟练度增量 = 施放次数（封顶 {mastery.mastery_max()}，本场施放 {_i(uses, Skill)} 次）");
    }

    /// <summary>熟练度直接决定招式倍率（伤害管线第 1 步用的就是这个乘数）。</summary>
    private void _check_multiplier(TableDb db, GodotObject state)
    {
        var growth = GrowthCalculator.create(db);
        double baseline = growth.mastery_multiplier(1, 0);
        double trained = growth.mastery_multiplier(1, 5);
        double maxed = growth.mastery_multiplier(1, growth.mastery_max());
        CheckFloat(baseline, 1.0, "0 熟练度不加成");
        CheckGt(trained, baseline, "练了就更强");
        CheckGt(maxed, trained, "练满更强");
        // ★1 练满 ×1.5（skill_star_def.mastery_gain = 0.05）
        CheckFloat(maxed, 1.5, "★1 练满 ×1.5", 0.001);

        // 存档往返带上熟练度
        state.Call("set_mastery", CharId, Skill, 4, growth.mastery_max());
        GameState back = GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), db);
        CheckEq(back.mastery_of(CharId, Skill), 4, "熟练度往返一致");
    }

    /// <summary>
    /// 打坐界面的多人分支：4 人队伍时的成员按钮、切人后的列表、以及「给谁练谁涨」。
    /// 自检只跑过 1 个角色（`character_base` 现在也只有 1 行），多人分支以前没人验——
    /// 切人串号（给 B 打坐却涨到 A 头上）在单角色自检里永远看不出来。
    /// </summary>
    private void _check_multi_member_ui(TableDb db)
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，打坐界面用例无法进行");
            return;
        }

        TableDb wide = TableWithNChars(4);
        GodotObject state = PartyState(wide, 4);
        string[] ids = state.Get("char_ids").AsStringArray();
        CheckEq(ids.Length, 4, "造了 4 人队伍");
        string a = ids[0];
        string b = ids[1];
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        inventory.Set("money", 1000);
        // 只给 B 一点熟练度：切过去应当看得到 3/10，A 还是 0
        state.Call("set_mastery", b, Skill, 3, MasteryMax);
        CultivateScreen screen = GD.Load<PackedScene>(CultivateScene).Instantiate<CultivateScreen>();
        screen.db_override = wide;
        screen.state_override = state;
        Tree.Root.AddChild(screen);
        screen.setup();
        var members = 0;
        foreach (Node node in screen.FindChildren("CultivateMemberButton*", "Button", true, false))
        {
            members += 1;
        }

        CheckEq(members, 4, "4 人队伍在打坐界面各有成员按钮");
        screen.select_char(a);
        CheckEq(screen.selected_char, a, "切到第 1 个角色");
        CheckEq(_mastery_of(screen, a), 0, "A 的熟练度是 0");
        screen.select_char(b);
        CheckEq(screen.selected_char, b, "切到第 2 个角色");
        CheckEq(_mastery_of(screen, b), 3, "B 的熟练度是 3");
        int money_before = inventory.Get("money").AsInt32();
        int cost = ((MasteryService)screen.mastery).train_cost(b, Skill);
        Godot.Collections.Dictionary trained = screen.press_train(Skill);
        CheckTrue(trained["ok"].AsBool(), $"给 B 打坐：{trained["error"].AsString()}");
        CheckEq(_mastery_of(screen, b), 4, "B 涨到 4");
        CheckEq(_mastery_of(screen, a), 0, "A 一点没涨（没串号）");
        CheckEq(inventory.Get("money").AsInt32(), money_before - cost, "只扣一份钱");
        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>
    /// 打坐的「一整套」累积边界：钱**刚好够**从 0 连练到上限时，最后一次正好花光；
    /// 少一文则最后一次被拒、那一文留着。单价曲线是逐级上涨的（`base × growth^level`），
    /// 所以这里验的是「逐级费用之和」这个合计数与扣钱口径对不对——单次边界在 `_check_train` 里另有对照。
    /// </summary>
    private void _check_full_train_budget(GodotObject state, MasteryService mastery)
    {
        int top = mastery.mastery_max();
        var total = 0;
        for (var level = 0; level < top; level++)
        {
            state.Call("set_mastery", CharId, Skill, level, top);
            total += mastery.train_cost(CharId, Skill);
        }

        CheckGt(total, 0.0, $"从 0 练到上限一共要花钱（{total} 文）");
        CheckGt(total, mastery.train_cost(CharId, Skill), "整套比第一级贵（曲线确实在涨）");

        GodotObject inventory = state.Get("inventory").AsGodotObject();
        state.Call("set_mastery", CharId, Skill, 0, top);
        inventory.Set("money", total);
        var done = 0;
        while (mastery.mastery_of(CharId, Skill) < top && done < top + 5)
        {
            Godot.Collections.Dictionary result = mastery.train(CharId, Skill);
            CheckTrue(result["ok"].AsBool(), $"第 {done + 1} 次打坐成功：{result["error"].AsString()}");
            done += 1;
        }

        CheckEq(mastery.mastery_of(CharId, Skill), top, "练到上限");
        CheckEq(inventory.Get("money").AsInt32(), 0, $"刚好花光（合计 {total} 文）");
        CheckEq(done, top, "正好练了上限那么多次");

        // 少一文：只能练到上限前一级，最后一次被拒且那一文留着
        state.Call("set_mastery", CharId, Skill, 0, top);
        inventory.Set("money", total - 1);
        var done_short = 0;
        while (mastery.mastery_of(CharId, Skill) < top - 1 && done_short < top + 5)
        {
            mastery.train(CharId, Skill);
            done_short += 1;
        }

        CheckEq(mastery.mastery_of(CharId, Skill), top - 1, "差一文只能练到上限前一级");
        // 此刻余额应当正好是「最后一级的费用 − 1」——差的那一文就是它
        int last_cost = mastery.train_cost(CharId, Skill);
        CheckEq(inventory.Get("money").AsInt32(), last_cost - 1, "余额 = 最后一级费用 − 1（差一文）");
        Godot.Collections.Dictionary last = mastery.train(CharId, Skill);
        CheckFalse(last["ok"].AsBool(), "最后一练被拒（钱不够）");
        CheckEq(inventory.Get("money").AsInt32(), last_cost - 1, "被拒时余额一分没动");
        state.Call("set_mastery", CharId, Skill, 0, top);
    }

    /// <summary>结算面板上的汇总文案。</summary>
    private void _check_describe(GodotObject state, MasteryService mastery)
    {
        Godot.Collections.Array entries = mastery.apply_combat_usage(
            CharId, new Godot.Collections.Dictionary { { Skill, 2 } }, 0);
        string text = mastery.describe_usage(entries);
        CheckTrue(text.Contains(mastery.skill_name(Skill)), $"文案带上招式名：{text}");
        CheckTrue(text.Contains("2/10"), $"文案写清当前／上限：{text}");
        CheckTrue(text.Contains("（+2）"), $"文案写清这次涨了多少：{text}");
        CheckEq(mastery.describe_usage(new Godot.Collections.Array()), "", "没涨就没有文案");
    }

    /// <summary>读界面手里那套熟练度服务里某人某招的熟练度（多人界面用来查"有没有串号"）。</summary>
    private static int _mastery_of(CultivateScreen screen, string char_id) =>
        ((MasteryService)screen.mastery).mastery_of(char_id, Skill);

    /// <summary>对位 GDScript 的 `dict.get(key, 0)`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;
}
