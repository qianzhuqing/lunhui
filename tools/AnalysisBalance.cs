using System.Collections.Generic;
using System.Globalization;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Tests;

namespace Lunhui.Tools;

/// <summary>
/// 平衡观测（C# 版，对位 `tools/analysis_balance.gd`）：给策划看「现在的数值打起来是什么样」。
///
/// 用法：`tools\run_balance_analysis.bat`
///
/// 它**不改任何数值、不写任何存档**，只跑战斗模拟并把原始数据打到 stdout，由人工整理进
/// `docs/dev/平衡观测.md`。之所以不进 `run_all_checks`：它输出的是一张表而不是「通过／不通过」，
/// 而且策划改数值时这张表本来就该变，做成门限只会让正常的数据调整变成假红。
///
/// 口径（与报告里写的一致，改了这里也要改报告）：
///   两档队伍：① **1 人＝真实开局**（0.10.0 起队伍由 `recruit_def.is_initial` 决定，开局只有书生）
///             ② **4 人＝设计目标队伍**（`character_base` 前 4 行，与 `MAX_PARTY` 一致）
///   ＋ 起始装备、普通难度、满血开局、等级直接设成表里的等级，每档用 20 个固定种子各打一场。
///
/// **队伍来自测试夹具**（`tests/TestCase.cs::PartyState`，GDScript 那版也是 `load("tests/test_case.gd")`）——
/// 0.10.0 起 `new_game()` 只给初始成员，不显式点名就会静默量成单人队（性能工具踩过同一个坑）。
/// </summary>
public partial class AnalysisBalance : Node
{
    private const int SeedBase = 20261003;
    private const int Tries = 20;
    private static readonly int[] Levels = { 1, 3, 5, 8, 10, 12, 15 };

    /// <summary>队伍规模：1 = 真实开局（招募驱动，只有书生）；4 = 设计目标队伍（01 的角色卡，取前 4 行）。</summary>
    private static readonly int[] PartySizes = { 1, 4 };

    private static readonly string[] Teams =
    {
        "team_wolf_pack", "team_boar_pair", "team_bandit_patrol", "team_lone_wolf",
        "team_gate_sentry", "team_elite_blade", "team_poison_hand", "team_boss_guards",
        "team_heifeng_elite", "team_boss", "team_butcher", "team_shixi_hound",
        // 隐藏 Boss（酔刀客）**没有 `enemy_team` 行**：小地图是拿 `team_hidden_<enemy_id>`
        // 这个合成 id 现拉一支单人队（`LocalMapController._boss_encounter`）。平衡表要看得见它
        // ——它是三条 ★5 内功路线之一，设计 10 §七 明写「必须可胜，10 级险胜」。
        "team_hidden_en_hidden_drunk",
    };

    /// <summary>合成队伍的前缀（见 <see cref="Teams"/> 里那条注释）。</summary>
    private const string HiddenTeamPrefix = "team_hidden_";

    /// <summary>落雁坡明雷一次全清的收益按这个区算（报告里的「一次全清」）。</summary>
    private const string SweepRegion = "n_luoyanpo";

    /// <summary>
    /// 「含天赋」那一档用的**代表 build**（2026-10-04 补，共 5 点、全是**已经接上**的效果）：
    /// 天生武胆（3 点：招式伤害 +15%、内伤抗性 −20%）＋ 眼疾手快（2 点：暴击率 +5%、命中 +5%）。
    /// 为什么用这一套：① 与武器类型无关（剑／拳／刀／枪的队伍都能吃）；② 两条效果都已实现；
    /// ③ 它**不是最优解**，只是一套"玩家真可能选、而且偏战斗向"的 build——设计要换一套，改这里即可。
    /// </summary>
    private static readonly string[] RepresentativeTalents = { "tal_dan_shi", "tal_yan_ji" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// 全流程共用一支 EnemyFactory：它在 GDScript 那版是每次现拉一个 EnemyFactory.new(db)，
    /// 靠引用计数当场回收；C# 侧要等 GC 才放，而这张表一轮要拉上万次（20 种子 × 12 队 × 7 级 × 2 档），
    /// 原生对象堆到几千个就会把 mono 的 user-type 绑定打崩——2026-10-11 实测跑到中段报
    /// 	ie_user_managed_to_unmanaged: script.is_null() 整个进程挂掉（同一份旧链连着跑就不出事）。
    /// 工厂本身无状态，共用一份不影响任何输出。
    /// </summary>
    private static EnemyFactory _factory;

    /// <summary>取共用工厂（首次调用时建）。</summary>
    private static EnemyFactory Factory(TableDb db) => _factory ??= EnemyFactory.create(db);

    public override void _Ready()
    {
        // 与其余 C# 工具同形状：延后一帧跑（_Ready 正处在装配子节点的窗口里），跑完 Quit(code)。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private int Run()
    {
        var db = new TableDb();
        db.load_all();

        DumpWinRates(db, false);
        DumpWinRates(db, true);
        // 第三档（2026-10-04）：**含加点 ＋ 一套代表天赋**——玩家开局一定会花那 5 点，
        // 前两档量的都是"没选天赋"的队伍。
        DumpWinRates(db, true, RepresentativeTalents);
        DumpTeamRewards(db);
        DumpLevelGates(db);
        DumpSweepReward(db);
        DumpEnemyAttrs(db);

        // 收尾标记 + 主动退出。**放在最后一段之后**：它的意思是「整张表都打完了」。
        // 为什么必须有个标记：光靠 --quit-after 的退出码分不出"跑到最后"和"半路被运行期错误掐断"，
        // 而这个脚本打的**是一张表**——半截表和完整表长得一模一样。包装脚本用 findstr 找这一行。
        GD.Print("BALANCE SNAPSHOT: OK");
        return 0;
    }

    /// <summary>
    /// 胜率／平均回合／平均剩余气血：等级 × 敌人队伍。
    ///
    /// `withPoints` 是**队伍模型**这一维：false ＝ 只把等级设过去、**升级点数一分不花**（老口径，留着做对照）；
    /// true ＝ **把点数花掉**的真实队伍。为什么必须补这一档：`level_growth` 的基础列很平，裸队从
    /// 1 级到 12 级只涨约 40%，于是设计 10 §七 的目标曲线（「精英 5～7 级有压力／8 级稳过」
    /// 「大寨主 8 级险胜、10 级稳过」）**在裸队口径下数学上不可同时成立**——设计说的是
    /// **玩到那一步的队伍**，不是「把等级设成 10、点数没花」的队伍。
    /// </summary>
    private static void DumpWinRates(TableDb db, bool withPoints, string[] talents = null)
    {
        talents ??= System.Array.Empty<string>();
        TestCase helper = new();
        foreach (int partySize in PartySizes)
        {
            if (withPoints && partySize < 4)
            {
                continue;   // 单人真实开局本来就没有点数可花（1 级 0 点），不必重复出表
            }

            var sample = (GameState)helper.PartyState(db, partySize);
            var names = new List<string>();
            foreach (string charId in sample.Get("char_ids").AsStringArray())
            {
                var row = db.get_row("character_base", charId) as CharacterBaseRow;
                names.Add(row != null ? row.name_cn : charId);
            }

            string mode;
            if (withPoints && talents.Length > 0)
            {
                mode = $"；**含加点 ＋ 代表天赋 {string.Join("＋", TalentNames(db, talents))}**";
            }
            else if (withPoints)
            {
                mode = "；**含加点**";
            }
            else
            {
                mode = "；不含加点（只设等级）";
            }

            GD.Print($"---- 胜率（{partySize} 人队：{string.Join("、", names)}{mode}；每档 {Tries} 个固定种子） ----");
            GD.Print("等级\t队伍\t敌人等级\t胜\t负\t打不完\t平均回合\t平均剩余气血%");

            foreach (int level in Levels)
            {
                foreach (string teamId in Teams)
                {
                    var enemyLevels = new List<string>();
                    if (!CollectEnemyLevels(db, teamId, enemyLevels))
                    {
                        continue;
                    }

                    int wins = 0;
                    int losses = 0;
                    int draws = 0;
                    int roundsTotal = 0;
                    double hpRatioTotal = 0.0;
                    for (int attempt = 0; attempt < Tries; attempt++)
                    {
                        var state = (GameState)helper.PartyState(db, partySize);
                        foreach (string charId in state.Get("char_ids").AsStringArray())
                        {
                            state.char_levels[charId] = level;
                            state.char_hp.Remove(charId);
                        }

                        if (withPoints)
                        {
                            SpendAllPoints(db, state);
                        }

                        // 天赋：同一套代表 build 发给队里每个人（天赋是**每个角色**各选各的）
                        if (talents.Length > 0)
                        {
                            foreach (string charId in state.Get("char_ids").AsStringArray())
                            {
                                var picks = new Godot.Collections.Array();
                                foreach (string talentId in talents)
                                {
                                    picks.Add(talentId);
                                }

                                state.talent_picks[charId] = picks;
                            }
                        }

                        var allies = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
                        var enemies = TeamEnemies(db, teamId);
                        var sim = BattleSimulator.create(db, new RngService(SeedBase + attempt));
                        var result = sim.simulate(allies, enemies);
                        string winner = result["winner"].AsString();
                        if (winner == "ally")
                        {
                            wins += 1;
                        }
                        else if (winner == "enemy")
                        {
                            losses += 1;
                        }
                        else
                        {
                            draws += 1;
                        }

                        roundsTotal += result["rounds"].AsInt32();

                        double hpLeft = 0.0;
                        double hpMax = 0.0;
                        foreach (Variant allyEntry in allies)
                        {
                            BattleActor ally = allyEntry.As<BattleActor>();
                            hpLeft += System.Math.Max(0, ally.hp);
                            hpMax += ally.max_hp();
                        }

                        hpRatioTotal += hpMax <= 0.0 ? 0.0 : hpLeft / hpMax * 100.0;
                    }

                    GD.Print(
                        $"{level}\t{teamId}\t{string.Join(",", enemyLevels)}\t{wins}\t{losses}\t{draws}"
                            + $"\t{NumberFormat.Fmt((double)roundsTotal / Tries, 1)}\t{NumberFormat.Fmt(hpRatioTotal / Tries, 0)}"
                    );
                }
            }
        }
    }

    /// <summary>
    /// 取一支队伍的敌人等级列表（打印用）。普通队伍查 `enemy_team` 的 members；
    /// `team_hidden_&lt;enemy_id&gt;` 按单人现拉（与 `LocalMapController._boss_encounter` 同一个约定）。
    /// 返回 false 表示这支队伍当前不该出现在表里（表里没有、合成 id 也解不出敌人）。
    /// </summary>
    private static bool CollectEnemyLevels(TableDb db, string teamId, List<string> enemyLevels)
    {
        var team = db.get_row("enemy_team", teamId) as EnemyTeamRow;
        if (team == null)
        {
            if (!teamId.StartsWith(HiddenTeamPrefix, System.StringComparison.Ordinal))
            {
                return false;
            }

            var solo = db.get_row("enemy_base", teamId.Substring(HiddenTeamPrefix.Length)) as EnemyBaseRow;
            if (solo == null)
            {
                return false;
            }

            enemyLevels.Add(solo.level.ToString(Inv));
            return true;
        }

        foreach (Variant memberEntry in team.parsed_members())
        {
            Godot.Collections.Dictionary member = memberEntry.AsGodotDictionary();
            var enemy = db.get_row("enemy_base", MemberString(member, "enemy_id")) as EnemyBaseRow;
            if (enemy != null)
            {
                enemyLevels.Add(enemy.level.ToString(Inv));
            }
        }

        return true;
    }
    /// <summary>
    /// 把「1 级到当前等级」的升级点数**全花掉**（真实玩家不会攒着不用）。
    ///
    /// 口径（开发侧定的，写在唯一的这一处）：**一半进体质（con，保命）、一半进伤害属性**——
    /// 伤害属性看角色**起始武学的系别**：`external`（外功）→ 力 `str`；其余（内伤／毒／火…）→ 智 `int`。
    /// 只投 `attribute_def.allocatable=1` 的那五项（悟性／根骨是资质，本来就投不了——
    /// 具体由 `GameState.spend_point` 自己拦，这里不重写规则）。
    /// </summary>
    private static void SpendAllPoints(TableDb db, GameState state)
    {
        foreach (string charId in state.Get("char_ids").AsStringArray())
        {
            var row = db.get_row("character_base", charId) as CharacterBaseRow;
            if (row == null)
            {
                continue;
            }

            string damageAttr = "str";
            foreach (string skillId in row.skill_ids())
            {
                var active = db.get_row("skill_active", skillId) as SkillActiveRow;
                if (active != null)
                {
                    damageAttr = active.element == "external" ? "str" : "int";
                    break;
                }
            }

            int guard = 0;
            while (state.available_points(db, charId) > 0 && guard < 1000)
            {
                guard += 1;
                string target = state.available_points(db, charId) % 2 == 0 ? "con" : damageAttr;
                Godot.Collections.Dictionary spent = state.spend_point(db, charId, target);
                if (!spent.ContainsKey("ok") || !spent["ok"].AsBool())
                {
                    break;
                }
            }
        }
    }

    /// <summary>取一支队伍的打手：普通队伍查 `enemy_team`；`team_hidden_&lt;enemy_id&gt;` 按单人现拉。</summary>
    private static Godot.Collections.Array TeamEnemies(TableDb db, string teamId)
    {
        EnemyFactory factory = Factory(db);
        if (db.get_row("enemy_team", teamId) != null)
        {
            return factory.create_team(teamId, "normal");
        }

        string enemyId = teamId.StartsWith(HiddenTeamPrefix, System.StringComparison.Ordinal)
            ? teamId.Substring(HiddenTeamPrefix.Length)
            : teamId;
        if (enemyId == teamId)
        {
            return new Godot.Collections.Array();
        }

        GodotObject actor = factory.create_enemy(enemyId, "normal");
        return actor != null
            ? new Godot.Collections.Array { actor }
            : new Godot.Collections.Array();
    }

    /// <summary>天赋 id → 中文名（表里的名字，报告头直接用，别在打印里出现 id）。</summary>
    private static List<string> TalentNames(TableDb db, string[] talents)
    {
        var names = new List<string>();
        foreach (string talentId in talents)
        {
            var row = db.get_row("talent_def", talentId) as TalentDefRow;
            names.Add(row != null ? row.name_cn : talentId);
        }

        return names;
    }


    private static string MemberString(Godot.Collections.Dictionary member, string key) =>
        member.ContainsKey(key) ? member[key].AsString() : "";

    private static int MemberInt(Godot.Collections.Dictionary member, string key, int fallback) =>
        member.ContainsKey(key) ? member[key].AsInt32() : fallback;

    private static int AttrValue(Godot.Collections.Dictionary attrs, string key) =>
        attrs.ContainsKey(key) ? attrs[key].AsInt32() : 0;

    /// <summary>每支队伍的经验／铜钱（按 members 展开，普通难度无加成）。</summary>
    private static void DumpTeamRewards(TableDb db)
    {
        GD.Print("---- 每支队伍收益（普通难度） ----");
        GD.Print("队伍\t敌人等级\t经验\t铜钱");
        foreach (string teamId in Teams)
        {
            var team = db.get_row("enemy_team", teamId) as EnemyTeamRow;
            if (team == null)
            {
                continue;
            }

            int expSum = 0;
            int moneySum = 0;
            var levels = new List<string>();
            foreach (Variant memberEntry in team.parsed_members())
            {
                Godot.Collections.Dictionary member = memberEntry.AsGodotDictionary();
                var enemy = db.get_row("enemy_base", MemberString(member, "enemy_id")) as EnemyBaseRow;
                if (enemy == null)
                {
                    continue;
                }

                int count = System.Math.Max(1, MemberInt(member, "count", 1));
                levels.Add(enemy.level.ToString(Inv));
                expSum += enemy.exp_reward * count;
                moneySum += enemy.money * count;
            }

            GD.Print($"{teamId}\t{string.Join(",", levels)}\t{expSum}\t{moneySum}");
        }
    }

    /// <summary>升级门槛：`level_growth.exp_to_next` 逐级累加（每行是「本级 → 下一级」）。</summary>
    private static void DumpLevelGates(TableDb db)
    {
        GD.Print("---- 升级门槛（累计经验） ----");
        var rows = new List<LevelGrowthRow>();
        foreach (Variant entry in db.rows("level_growth"))
        {
            if (entry.AsGodotObject() is LevelGrowthRow row)
            {
                rows.Add(row);
            }
        }

        rows.Sort((left, right) => left.level.CompareTo(right.level));
        int total = 0;
        foreach (LevelGrowthRow row in rows)
        {
            total += row.exp_to_next;
            if (row.level <= 15)
            {
                GD.Print($"到 {row.level} 级累计 {total}");
            }
        }
    }

    /// <summary>一张图上的明雷一次全清能拿多少（按队伍配置展开）。</summary>
    private static void DumpSweepReward(TableDb db)
    {
        GD.Print($"---- {SweepRegion} 明雷一次全清（按队伍配置） ----");
        int expSum = 0;
        int moneySum = 0;
        foreach (Variant entry in db.rows("roaming_spawn"))
        {
            if (entry.AsGodotObject() is not RoamingSpawnRow row || row.region_id != SweepRegion)
            {
                continue;
            }

            var team = db.get_row("enemy_team", row.team_id) as EnemyTeamRow;
            if (team == null)
            {
                continue;
            }

            foreach (Variant memberEntry in team.parsed_members())
            {
                Godot.Collections.Dictionary member = memberEntry.AsGodotDictionary();
                var enemy = db.get_row("enemy_base", MemberString(member, "enemy_id")) as EnemyBaseRow;
                if (enemy == null)
                {
                    continue;
                }

                int count = System.Math.Max(1, MemberInt(member, "count", 1));
                expSum += enemy.exp_reward * count;
                moneySum += enemy.money * count;
            }
        }

        GD.Print($"经验合计 {expSum}　铜钱合计 {moneySum}");
    }

    /// <summary>
    /// 敌人七维的**正式口径**（设计 10 §七，0.28.0 答 Q57）：敌人**不吃** `level_growth` 的基础列，
    /// 强度 ＝ 七维 → `attr_to_stat` ＋ 装备／内功固定值（＋百分比），难度倍率照旧最后乘。
    ///
    /// 这一节把每一行**表里真实配着的**七维与它算出来的派生数值并排打出来——它就是线上
    /// `EnemyFactory.create_enemy()` 走的那条路（同一个 `AttributeCalculator`，只差
    /// `include_level_base=false` 这一个开关），所以这里**不需要第二份算式**。
    /// 还空着的行会被点名：那意味着它仍在走过渡回退（`_stats_from_derived`）。
    /// </summary>
    private static void DumpEnemyAttrs(TableDb db)
    {
        EnemyFactory factory = Factory(db);
        GD.Print("---- 敌人七维（正式口径：不吃等级基础；强度＝七维 ＋ 装备／内功固定值） ----");
        GD.Print("敌人\t等级\tstr/con/agi/int/luk/wu/gen\thp/atk_phys/atk_qi/def_phys/def_qi/speed\thit/dodge/crit\tqi_max\t破架势\tdrop/威胁色");
        var pending = new List<string>();
        foreach (Variant entry in db.rows("enemy_base"))
        {
            if (entry.AsGodotObject() is not EnemyBaseRow row)
            {
                continue;
            }

            Godot.Collections.Dictionary attrs = row.attr_map();
            if (attrs.Count == 0)
            {
                pending.Add(row.enemy_id);
            }

            if (factory.create_enemy(row.enemy_id, "normal") is not BattleActor actor)
            {
                continue;
            }

            GD.Print(
                $"{row.enemy_id}\t{row.level}"
                    + $"\t{AttrValue(attrs, "str")}/{AttrValue(attrs, "con")}/{AttrValue(attrs, "agi")}"
                    + $"/{AttrValue(attrs, "int")}/{AttrValue(attrs, "luk")}/{AttrValue(attrs, "wu")}"
                    + $"/{AttrValue(attrs, "gen")}"
                    + $"\t{NumberFormat.Fmt(actor.stat("hp_max"), 0)}/{NumberFormat.Fmt(actor.stat("atk_phys"), 0)}"
                    + $"/{NumberFormat.Fmt(actor.stat("atk_qi"), 0)}/{NumberFormat.Fmt(actor.stat("def_phys"), 0)}"
                    + $"/{NumberFormat.Fmt(actor.stat("def_qi"), 0)}/{NumberFormat.Fmt(actor.stat("speed"), 1)}"
                    + $"\t{NumberFormat.Fmt(actor.stat("hit_rate"), 2)}/{NumberFormat.Fmt(actor.stat("dodge_rate"), 2)}"
                    + $"/{NumberFormat.Fmt(actor.stat("crit_rate"), 2)}"
                    + $"\t{NumberFormat.Fmt(actor.stat("qi_max"), 0)}\t{NumberFormat.Fmt(actor.stat("poise_break"), 0)}"
                    + $"\t{(string.IsNullOrEmpty(row.drop_group) ? "—" : row.drop_group)}/{row.threat_tag}"
            );
        }

        if (pending.Count == 0)
        {
            GD.Print($"七维配齐：全部 {db.rows("enemy_base").Count} 行走正式口径（过渡回退只剩代码里的兜底，没有数据用它）");
        }
        else
        {
            GD.Print($"**还没配七维的行（仍在走过渡回退）**：{string.Join("、", pending)}");
        }

        GD.Print("注：`hit` 是敏经 diminishing 曲线给的**加成**（cap 0.95／param 80），敌我命中基准都是 1.0；");
        GD.Print("    真正决定打不打得中的是对方的 `dodge_rate`（10 §五：想让敌人「血厚防薄」用敏表达）。");
        GD.Print("    `qi_max` 由智／根骨派生（每点 6.0／4.0）——不再写死 0，配了高星招式才有内力可放。");
    }
}