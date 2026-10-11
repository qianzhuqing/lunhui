using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 战斗的**不变量模糊测试**：随机组队 × 随机敌人队伍 × 随机难度 × 固定种子，跑 120 场，
/// 只问「有没有哪种组合会破坏不变量」——对位 `tests/test_battle_fuzz.gd`（2026-10-11 P4 第十一批搬 C#）。
///
/// 逐条判据都是**静默腐坏**那一类：
///   · 气血／内力跑出 `[0, 上限]` 之外（负血、过量治疗、消耗没扣干净）；
///   · 伤害值出现 `NaN`／`Inf` 或负数（除零、上限公式写错时最先在这里露头）；
///   · 一场架跑不完 或 回合数超过 `BattleSimulator.MAX_ROUNDS`（死循环／不收敛）；
///   · `winner` 落到三个合法值之外。
///
/// 为什么值得单独有一条：现有的战斗用例都是**定点**的（指定队伍、指定等级、指定种子），
/// 覆盖的是"设计写下来的那些组合"；而**组合爆炸**（1~4 人 × 1~15 级 × 三种难度 × 18 支队伍）
/// 里任何一处边缘都可能只在某个没写过的组合上炸。固定种子保证可复现——
/// 失败信息里带 `seed=/队伍/难度`，照着那三个数就能重跑出同一场。
///
/// 顺带一条覆盖保证：跑完必须**两种胜负都出现过**（否则说明种子挑得不对／战斗提前退化成一种结果）。
/// </summary>
public partial class TestBattleFuzz : TestCase
{
    /// <summary>固定种子表：1..120（数小、可读、可复现）。改这里等于换一批对局，不是"调松门限"。</summary>
    private const int Seeds = 120;

    private static readonly string[] PartyPool =
        { "scholar_fallen", "ch_gang", "ch_du", "ch_ci", "ch_qi" };

    private static readonly string[] Difficulties = { "normal", "hard", "nightmare" };

    public override string SuiteName() => "战斗不变量模糊测试（120 场随机对局）";

    public override void Run()
    {
        TableDb db = GetDb();
        var teams = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("enemy_team"))
        {
            if (entry.AsGodotObject() is EnemyTeamRow row)
            {
                teams.Add(row.team_id);
            }
        }

        CheckGt(teams.Count, 5.0, $"读到足够多的敌人队伍（{teams.Count} 支）");

        var problems = new System.Collections.Generic.List<string>();
        int checkedCount = 0;
        var winners = new Godot.Collections.Dictionary();
        for (int seedValue = 1; seedValue <= Seeds; seedValue++)
        {
            string teamId = teams[seedValue % teams.Count];
            string difficulty = Difficulties[seedValue % Difficulties.Length];
            int partySize = 1 + (seedValue % 4);
            int level = 1 + (seedValue % 15);
            var ids = new System.Collections.Generic.List<string>();
            for (int index = 0; index < partySize; index++)
            {
                string charId = PartyPool[(seedValue + index) % PartyPool.Length];
                if (!ids.Contains(charId))
                {
                    ids.Add(charId);
                }
            }

            GodotObject state = GameState.new_game(db, difficulty, ids.ToArray(), 0);
            foreach (string charId in ids)
            {
                // 等级来自存档，直接点名（同 `analysis_balance` 的口径）
                state.Get("char_levels").AsGodotDictionary()[charId] = level;
            }

            Godot.Collections.Array allies =
                PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
            Godot.Collections.Array enemies = EnemyFactory.create(db).create_team(teamId, difficulty);
            BattleSimulator sim = BattleSimulator.create(db, RngService.create(seedValue * 7919));
            Godot.Collections.Dictionary result =
                sim.simulate(allies, enemies, new Godot.Collections.Dictionary());
            checkedCount += 1;
            string winner = _s(result, "winner");
            string tag =
                $"seed={seedValue} {teamId}/{difficulty} 队伍{ids.Count}人/{level}级";
            winners[winner] = _i(winners, winner) + 1;
            if (!sim.finished())
            {
                problems.Add($"{tag}：simulate() 跑完却没结束");
            }

            int rounds = _i(result, "rounds", -1);
            if (rounds < 0 || rounds > BattleSimulator.max_rounds())
            {
                problems.Add($"{tag}：回合数越界 {rounds}（上限 {BattleSimulator.max_rounds()}）");
            }

            if (_arr(result, "log").Count == 0)
            {
                problems.Add($"{tag}：战报是空的");
            }

            foreach (Variant unitEntry in _concat(allies, enemies))
            {
                var unit = unitEntry.AsGodotObject() as BattleActor;
                if (unit == null)
                {
                    continue;
                }

                if (unit.hp < 0 || unit.hp > unit.max_hp())
                {
                    problems.Add($"{tag}：{unit.display_name} 气血越界 {unit.hp}／{unit.max_hp()}");
                }

                if (unit.qi < 0 || unit.qi > unit.max_qi())
                {
                    problems.Add($"{tag}：{unit.display_name} 内力越界 {unit.qi}／{unit.max_qi()}");
                }
            }

            Godot.Collections.Dictionary dealt = _d(result, "damage_dealt");
            foreach (Variant key in dealt.Keys)
            {
                double value = dealt[key].AsDouble();
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
                {
                    problems.Add($"{tag}：伤害值不合法 {key.AsString()}={value}");
                }
            }

            if (winner != BattleSimulator.winner_ally()
                && winner != BattleSimulator.winner_enemy()
                && winner != BattleSimulator.winner_draw())
            {
                problems.Add($"{tag}：winner 取值意外 '{winner}'");
            }
        }

        CheckEq(checkedCount, Seeds, $"每一场都真的跑了（跑了 {checkedCount} 场）");
        CheckEq(problems.Count, 0, $"120 场随机对局里没有一场破坏不变量：{string.Join("；", problems)}");
        // 覆盖保证：两边都得赢过——只有一边赢说明这批种子退化成了"同一种架"
        CheckGt(
            _i(winners, BattleSimulator.winner_ally()),
            0.0,
            $"有打赢的对局（{_i(winners, BattleSimulator.winner_ally())} 场）");
        CheckGt(
            _i(winners, BattleSimulator.winner_enemy()),
            0.0,
            $"有打输的对局（{_i(winners, BattleSimulator.winner_enemy())} 场）");
    }

    /// <summary>`allies + enemies`：两边单位拼成一串再逐个体检。</summary>
    private static Godot.Collections.Array _concat(
        Godot.Collections.Array left, Godot.Collections.Array right)
    {
        var outArr = new Godot.Collections.Array();
        foreach (Variant entry in left)
        {
            outArr.Add(entry);
        }

        foreach (Variant entry in right)
        {
            outArr.Add(entry);
        }

        return outArr;
    }

    /// <summary>对位 GDScript 的 `dict.get(key, 默认值)`（`Dictionary` 没有这个重载，坑④）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static int _i(Godot.Collections.Dictionary dict, string key, int fallback = 0) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;

    private static Godot.Collections.Dictionary _d(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotDictionary() : new Godot.Collections.Dictionary();

    /// <summary>对位 GDScript 的 `Array(result.get(key, []))`：**别拿 `AsGodotDictionary()` 读数组**，
    /// 它不会报错但会给出一个空字典（本批实测：120 场全被判成「战报是空的」）。</summary>
    private static Godot.Collections.Array _arr(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsGodotArray() : new Godot.Collections.Array();
}
