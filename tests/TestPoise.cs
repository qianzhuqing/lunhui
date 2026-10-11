using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 架势与破绽：打空架势 → 破绽窗口（受伤提高）→ 窗口结束重置；背袭降低初始架势——对位
/// `tests/test_poise.gd`（2026-10-11 P4 第三批搬 C#）。
/// </summary>
public partial class TestPoise : TestCase
{
    private const int Seed = 8080;

    public override string SuiteName() => "架势与破绽";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_pool(db);
        _check_break_and_bonus(db);
        _check_break_damage_bonus(db);
        _check_round_reset(db);
        _check_battle_regen(db);
        _check_backstab_opening(db);
        _check_exact_break(db);
    }

    private void _check_pool(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        var wolf = factory.create_enemy("en_wolf", "normal") as BattleActor;
        CheckEq(wolf.max_poise(), 20, "野狼架势取自 enemy_base.poise");
        CheckEq(wolf.poise, 20, "开局架势是满的");
        var boss = factory.create_enemy("en_bd_boss", "normal") as BattleActor;
        CheckEq(boss.max_poise(), 160, "大寨主架势 160");
        // 「回复极快」的数值是开发侧定的代码常量（enemy_base 没有这一列，已记进对接表等设计补），
        // 所以这里把**数值本身**钉住：改常量就得改这条断言（有意识的行为）。
        CheckFloat(wolf.poise_regen_ratio, 0.1, "普通敌人每回合回 10% 架势上限");
        CheckFloat(boss.poise_regen_ratio, 0.3, "大寨主每回合回 30%（设计：回复极快）");
        BattleActor hero = _character(db, 5);
        // 玩家架势上限的两个常数同样是开发侧定的（`character_base` 还没有架势列，已记交接表），
        // 按上面同一条纪律把**数值本身**钉住。变异探针实测：只写 `> 0` 时，把 40 改成 60 也不会红。
        CheckEq(hero.max_poise(), 80, $"5 级玩家架势上限 = 40 + 8 × 5 = 80（实得 {hero.max_poise()}）");
        BattleActor rookie = _character(db, 1);
        CheckEq(rookie.max_poise(), 48, $"1 级玩家架势上限 = 40 + 8 × 1 = 48（实得 {rookie.max_poise()}）");
    }

    private void _check_break_and_bonus(TableDb db)
    {
        EnemyFactory factory = EnemyFactory.create(db);
        var allies = new Godot.Collections.Array { _character(db, 10) };
        var wolf = factory.create_enemy("en_wolf", "normal") as BattleActor;
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            allies,
            new Godot.Collections.Array { wolf },
            new Godot.Collections.Dictionary
            {
                { "modifiers", new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } } },
            });
        var skill = db.get_row("skill_active", "sk_chensha_01"); // 破架势高
        CheckNotNull(skill, "沉沙掌可用");
        int before = wolf.poise;
        sim.act(allies[0].AsGodotObject() as BattleActor, skill, wolf);
        CheckLt(wolf.poise, before, "命中会扣架势（招式破架势 + 攻击者破架势）");
        var guard = 0;
        while (!wolf.is_broken() && guard < 10)
        {
            sim.act(allies[0].AsGodotObject() as BattleActor, skill, wolf);
            guard += 1;
        }

        CheckTrue(wolf.is_broken(), "架势打空后进入破绽");
        CheckEq(wolf.poise, 0, "破绽时架势为 0");
        var log = _log_lines(sim.result());
        CheckTrue(string.Join("；", log).Contains("破绽"), "日志会提示架势被打破");
    }

    private void _check_round_reset(TableDb db)
    {
        var wolf = EnemyFactory.create(db).create_enemy("en_wolf", "normal") as BattleActor;
        wolf.take_poise_damage(wolf.max_poise());
        CheckTrue(wolf.is_broken(), "打空即破绽");
        wolf.end_of_round_poise();
        CheckFalse(wolf.is_broken(), "破绽窗口只有 1 回合");
        CheckEq(wolf.poise, wolf.max_poise(), "窗口结束架势重置");
        wolf.take_poise_damage(5);
        int regen_before = wolf.poise;
        wolf.end_of_round_poise();
        CheckGt(wolf.poise, regen_before, "没破绽时每回合回复架势");
        CheckEq(wolf.poise - regen_before, (int)(20 * 0.1), "回的量 = 上限的 10%（20×0.1=2）");
        // 回复要封顶在上限（不然打两回合就超上限了）
        wolf.poise = wolf.max_poise() - 1;
        wolf.end_of_round_poise();
        CheckEq(wolf.poise, wolf.max_poise(), "回复封顶在架势上限");
    }

    /// <summary>
    /// 破绽窗口的伤害加成：设计 04「打空架势 → 破绽窗口 → 绝招爆发」，
    /// 加成值 `POISE_BREAK_DAMAGE_BONUS = 0.5`——0.32.0（Q85）起正式值在
    /// `combat_const.poise_break_bonus`，代码里那个常量只剩「表里没这行时的兜底」
    /// （兜底值另有绝对值断言，见 `test_battle._check_absolute_constants`）。
    /// 以前用例只断言了「进破绽」与日志文案，**挨打更疼这件事没人验**——把加成调成 0 也不会红。
    /// </summary>
    private void _check_break_damage_bonus(TableDb db)
    {
        var mods = new Godot.Collections.Dictionary
        {
            { "force_hit", true },
            { "no_variance", true },
            { "no_block", true },
        };
        int normal = _damage_once(db, mods, false);
        int broken = _damage_once(db, mods, true);
        CheckGt(broken, normal, "破绽中挨同一招更疼");
        // 期望值**不许写成 `1.0 + 常量`**：那样常量一改、期望值跟着改，断言照样绿——这正是
        // 变异探针抓出来的假绿（框架说明决策 153）。这里写死 1.5 倍，常量被动过就红。
        CheckFloat(
            broken,
            normal * 1.5,
            $"破绽窗口伤害 = 普通 × 1.5：普通 {normal} → 破绽 {broken}",
            1.5);
    }

    /// <summary>用同一招打同一个目标一次，返回打掉的血量。target_broken=true 时先把架势打空。</summary>
    private int _damage_once(TableDb db, Godot.Collections.Dictionary mods, bool target_broken)
    {
        BattleActor hero = _character(db, 10);
        hero.stats["crit_rate"] = 0.0; // 没有「关暴击」的开关，直接把这个变量去掉
        var wolf = EnemyFactory.create(db).create_enemy("en_wolf", "normal") as BattleActor;
        if (target_broken)
        {
            wolf.take_poise_damage(wolf.max_poise());
        }

        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { wolf },
            new Godot.Collections.Dictionary { { "modifiers", mods } });
        int before = wolf.hp;
        sim.act(hero, db.get_row("skill_active", "sk_xuanwei_01"), wolf);
        return before - wolf.hp;
    }

    /// <summary>
    /// 回复要在**真战斗的回合末**发生（不是只有一个能被调用的方法）。
    /// 让我方没招可出，这样这一回合里没人碰大寨主的架势，能单独观察回复量。
    /// </summary>
    private void _check_battle_regen(TableDb db)
    {
        BattleActor hero = _character(db, 10);
        hero.skills = System.Array.Empty<string>();
        var boss = EnemyFactory.create(db).create_enemy("en_bd_boss", "normal") as BattleActor;
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { boss },
            new Godot.Collections.Dictionary
            {
                { "modifiers", new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } } },
            });
        boss.take_poise_damage(80); // 160 → 80
        CheckEq(boss.poise, 80, "先把大寨主架势打到一半");
        sim.step_round();
        CheckEq(boss.poise, 80 + (int)(160 * 0.3), "打完一回合，大寨主按 30% 上限回复（+48）");
    }

    private void _check_backstab_opening(TableDb db)
    {
        var spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        var team = db.get_row("enemy_team", "team_wolf_pack");
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_back(), "normal");
        var allies = new Godot.Collections.Array { _character(db, 5) };
        Godot.Collections.Array enemies = EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        var sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            allies,
            enemies,
            new Godot.Collections.Dictionary { { "encounter", encounter } });
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = enemy_variant.AsGodotObject() as BattleActor;
            CheckLt(enemy.poise, enemy.max_poise(), $"背袭开局敌方架势被压低：{enemy.display_name}");
            CheckFloat(enemy.poise, enemy.max_poise() * 0.7, "压到 70%（-30%）", 1.0);
        }
    }

    /// <summary>
    /// 「架势正好被打空」：`take_poise_damage(poise)` 必须刚好进破绽（扣到 0、不出负数），
    /// 差 1 点则不能；背袭比例拉满也只压到 1——`reduce_poise_ratio` 有下限，
    /// 不然一进战斗就破绽（设计只写「压低初始架势」，没写能压到 0）。
    /// </summary>
    private void _check_exact_break(TableDb db)
    {
        var wolf = EnemyFactory.create(db).create_enemy("en_wolf", "normal") as BattleActor;
        int poise0 = wolf.poise;
        CheckGt(poise0, 1.0, "野狼开局架势够做边界样本");
        CheckEq(wolf.take_poise_damage(poise0 - 1), poise0 - 1, "差 1 点的伤害全吃下");
        CheckFalse(wolf.is_broken(), "差 1 点不进破绽");
        CheckEq(wolf.poise, 1, "架势正好剩 1");
        CheckEq(wolf.take_poise_damage(1), 1, "再打 1 点正好打空");
        CheckTrue(wolf.is_broken(), "正好打空 → 进破绽");
        CheckEq(wolf.poise, 0, "打空后架势是 0（不出现负数）");

        BattleActor hero = _character(db, 5);
        hero.reduce_poise_ratio(1.0);
        CheckEq(hero.poise, 1, "背袭比例拉满也只压到 1");
        CheckFalse(hero.is_broken(), "背袭压低架势不会一上来就破绽");
    }

    /// <summary>`from_character` 的五个实参写全（C# 默认参数只服务 C# 侧，见施工单坑⑤）。</summary>
    private static BattleActor _character(TableDb db, int level) =>
        BattleActor.from_character(
            db,
            db.get_row("character_base", "scholar_fallen"),
            level,
            new Godot.Collections.Dictionary(),
            new Godot.Collections.Array());

    /// <summary>`sim.result()["log"]` 是一串行，界面／断言里按整段拼起来看。</summary>
    private static string[] _log_lines(Godot.Collections.Dictionary result)
    {
        var lines = new System.Collections.Generic.List<string>();
        if (result.ContainsKey("log"))
        {
            foreach (Variant line in result["log"].AsGodotArray())
            {
                lines.Add(line.AsString());
            }
        }

        return lines.ToArray();
    }
}
