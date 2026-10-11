using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 招式冷却（`skill_active.cooldown`）——对位 `tests/test_cooldown.gd`（2026-10-11 P4 首批搬 C#）。
///
/// 口径：冷却 N = **放完之后接下来 N 个回合不能再用**（第 N+1 个回合起可用）。
/// 界面上会灰着写清「第几回合起可用」，自动策略与手选都遵守。
/// </summary>
public partial class TestCooldown : TestCase
{
    private const int Seed = 20261003;

    private static readonly Godot.Collections.Dictionary Fixed = new()
    {
        { "force_hit", true },
        { "no_variance", true },
        { "no_block", true },
    };

    public override string SuiteName() => "招式冷却";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_table(db);
        _check_cooldown_cycle(db);
        _check_options_and_ai(db);
        _check_independence(db);
    }

    /// <summary>表里 20 个招式有冷却（2~5 回合）。</summary>
    private void _check_table(TableDb db)
    {
        int with_cooldown = 0;
        foreach (Variant entry in db.rows("skill_active"))
        {
            var row = entry.AsGodotObject() as SkillActiveRow;
            if (row != null && row.cooldown > 0)
            {
                with_cooldown += 1;
            }
        }

        CheckEq(with_cooldown, 20, "20 个招式配了冷却");
        CheckEq(_active(db, "sk_xuanwei_05").cooldown, 2, "玄微剑法·点星冷却 2");
        CheckEq(_active(db, "sk_drunk_01").cooldown, 5, "醉里乾坤·颠倒冷却 5");
        CheckEq(_active(db, "sk_xuanwei_01").cooldown, 0, "起手式没有冷却");
    }

    /// <summary>放完进冷却、第 N+1 回合又能用。</summary>
    private void _check_cooldown_cycle(TableDb db)
    {
        BattleActor hero = MeleeActor("hero", BattleActor.side_ally());
        hero.skills = new[] { "sk_xuanwei_05", "sk_xuanwei_01" };
        BattleActor foe = MeleeActor(
            "foe",
            BattleActor.side_enemy(),
            new Godot.Collections.Dictionary { { "hp_max", 9999.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        SkillActiveRow skill = _active(db, "sk_xuanwei_05");
        CheckEq(sim.cooldown_left("hero", "sk_xuanwei_05"), 0, "开打前没有冷却");

        sim.begin_round(); // 第 1 回合
        sim.act(hero, skill, foe);
        CheckGt(sim.cooldown_left("hero", "sk_xuanwei_05"), 0.0, "放完就进冷却");
        CheckEq(sim.cooldown_ready_round("hero", "sk_xuanwei_05"), 4, "冷却 2 → 第 4 回合起可用");
        CheckFalse(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 1 回合里已经不可用");

        sim.begin_round(); // 第 2 回合
        CheckFalse(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 2 回合还在冷却");
        sim.begin_round(); // 第 3 回合
        CheckFalse(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 3 回合还在冷却");
        sim.begin_round(); // 第 4 回合
        CheckTrue(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 4 回合冷却结束");
        CheckEq(sim.cooldown_left("hero", "sk_xuanwei_05"), 0, "冷却归零");
        // 再放一次又进冷却
        sim.act(hero, skill, foe);
        CheckEq(sim.cooldown_ready_round("hero", "sk_xuanwei_05"), 7, "第 4 回合再放 → 第 7 回合起可用");
    }

    /// <summary>体检表把冷却写成原因；自动策略也不会挑冷却中的招式。</summary>
    private void _check_options_and_ai(TableDb db)
    {
        BattleActor hero = MeleeActor("hero", BattleActor.side_ally());
        hero.skills = new[] { "sk_xuanwei_05", "sk_xuanwei_01" };
        BattleActor foe = MeleeActor(
            "foe",
            BattleActor.side_enemy(),
            new Godot.Collections.Dictionary { { "hp_max", 9999.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        sim.act(hero, _active(db, "sk_xuanwei_05"), foe);

        var options = new Godot.Collections.Dictionary();
        foreach (Variant option_variant in sim.skill_options(hero))
        {
            Godot.Collections.Dictionary option = option_variant.AsGodotDictionary();
            options[option["skill_id"].AsString()] = option;
        }

        CheckTrue(options.ContainsKey("sk_xuanwei_05"), "冷却中的招式仍然列在体检表里");
        if (options.ContainsKey("sk_xuanwei_05"))
        {
            Godot.Collections.Dictionary cooling = options["sk_xuanwei_05"].AsGodotDictionary();
            CheckFalse(cooling["ok"].AsBool(), "冷却中的招式标记为不可用");
            CheckTrue(
                cooling["reason"].AsString().Contains("第 4 回合起可用"),
                $"写清第几回合起可用：{cooling["reason"].AsString()}");
        }

        Godot.Collections.Dictionary starter = options["sk_xuanwei_01"].AsGodotDictionary();
        CheckTrue(starter["ok"].AsBool(), "没冷却的招式照常可用");

        // 自动策略：点星冷却时只能选起手式
        var picked = sim.pick_skill(hero) as SkillActiveRow;
        CheckEq(picked?.skill_id, "sk_xuanwei_01", "AI 不挑冷却中的招式，退而用起手式");
    }

    /// <summary>冷却按「单位 + 招式」记：别人的同一招、自己的别招都不受影响。</summary>
    private void _check_independence(TableDb db)
    {
        BattleActor a = MeleeActor("a", BattleActor.side_ally());
        BattleActor b = MeleeActor("b", BattleActor.side_ally());
        a.skills = new[] { "sk_xuanwei_05", "sk_xuanwei_01" };
        b.skills = new[] { "sk_xuanwei_05", "sk_xuanwei_01" };
        BattleActor foe = MeleeActor(
            "foe",
            BattleActor.side_enemy(),
            new Godot.Collections.Dictionary { { "hp_max", 9999.0 } });
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { a, b },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary { { "modifiers", Fixed } });
        sim.begin_round();
        sim.act(a, _active(db, "sk_xuanwei_05"), foe);
        CheckGt(sim.cooldown_left("a", "sk_xuanwei_05"), 0.0, "a 用过的招在冷却");
        CheckEq(sim.cooldown_left("b", "sk_xuanwei_05"), 0, "b 的同一招不受影响");
        CheckEq(sim.cooldown_left("a", "sk_xuanwei_01"), 0, "a 的另一招不受影响");
    }

    private static SkillActiveRow _active(TableDb db, string skill_id) =>
        db.get_row("skill_active", skill_id) as SkillActiveRow;

    private static bool _has(Godot.Collections.Array skills, string skill_id)
    {
        foreach (Variant entry in skills)
        {
            var skill = entry.AsGodotObject() as SkillActiveRow;
            if (skill != null && skill.skill_id == skill_id)
            {
                return true;
            }
        }

        return false;
    }

}
