using System.Collections.Generic;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Tests;
using Lunhui.Ui;

namespace Lunhui.Tools;

/// <summary>
/// 体量观测（C# 版，对位 `tools/analysis_perf.gd`，213 行）：把「人最多 / 列表最长 / 存档最满」的几组极端数据
/// 跑一遍，打印构造与查询耗时，以及静态内存占用。
///
/// 用法：`tools\run_perf_analysis.bat`
///
/// 与 <see cref="AnalysisBalance"/> 同类：**不改数据、不写存档、不判通过／不通过**——耗时随机器与当时负载波动，
/// 做成门限只会让正常波动变成假红。它给的是一条「现在这些极端规模要花多少」的量级基线，
/// 人工整理进 `docs/dev/性能观测.md`。
///
/// 口径（改了这里也要改那份报告）：
///   ① 4 人满装（每人 6 件实例）+ 大背包（15 种道具各堆满）：构造 → 造战斗单位 → 打一场 4v4
///   ② 线索本 40 条（隐藏内容复制 6 倍）：查询 → 打开面板并建行
///   ③ 副本完成度 30 层（第 1 层复制成 30 层）：取完成度与分层 → 开面板建行
///   ④ **每项在同一次会话里连测 3 次，报「中位数（最小–最大）」**：2026-10-03 实测，同一份代码
///      在不同会话里能差 2–3 倍（构造 1.6ms vs 5.5ms，见框架说明决策 247），**单次数字没法跨会话比**；
///      而同一会话里的三次很稳（±0.2ms）——所以中位＋波动范围才是能看的东西。
/// </summary>
public partial class AnalysisPerf : Node
{
    /// <summary>同一项在一趟里连测几次（取中位；见文件头的口径 ④）。</summary>
    private const int REPEATS = 3;

    /// <summary>满装那一档每人穿的六件（部位固定的基础装）。</summary>
    private static readonly string[] EquipBases =
    {
        "eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01", "eq_acc_01", "eq_belt_01",
    };

    public override void _Ready()
    {
        // 与其余 C# 工具同形状：延后一帧跑（_Ready 正处在装配子节点的窗口里），跑完 Quit(code)。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private int Run()
    {
        var db = new TableDb();
        db.load_all();
        // 复用测试夹具（复制行只改内存副本，不碰策划的 CSV）
        var helper = new TestCase();
        GD.Print($"---- 体量观测（口径见 docs/dev/性能观测.md；同会话连测 {REPEATS} 次取中位）----");

        var partyRuns = new List<RichRun>();
        for (int index = 0; index < REPEATS; index++)
        {
            partyRuns.Add(_measure_rich_party(db, helper));
        }

        _print_rich_party(partyRuns);

        var clueRuns = new List<ClueRun>();
        for (int index = 0; index < REPEATS; index++)
        {
            clueRuns.Add(_measure_long_clue_list(db, helper));
        }

        _print_long_clue_list(clueRuns);

        var floorRuns = new List<FloorRun>();
        for (int index = 0; index < REPEATS; index++)
        {
            floorRuns.Add(_measure_many_floors(db, helper));
        }

        _print_many_floors(floorRuns);

        GD.Print($"· 静态内存 {NumberFormat.Fmt(_memory_mb(), 1)} MB（进程级，含引擎本身，只作量级参考）");
        // 同 `AnalysisBalance`：给"跑完了"一个可 findstr 的凭证。
        // 计时本来就是量级参考，但**半截的表**（运行期错误掐断）连量级都不成立。
        GD.Print("PERF SNAPSHOT: OK");
        return 0;
    }

    private static double _ms_since(ulong start_usec)
    {
        return (Time.GetTicksUsec() - start_usec) / 1000.0;
    }

    private static double _memory_mb()
    {
        return Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
    }

    /// <summary>一组数字的中位与范围，如 `5.6ms（5.5–5.7）`。</summary>
    private static string _median_ms(List<double> values)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        int count = sorted.Count;
        double median = count % 2 == 1
            ? sorted[count / 2]
            : (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        return $"{NumberFormat.Fmt(median, 1)}ms（{NumberFormat.Fmt(sorted[0], 1)}–{NumberFormat.Fmt(sorted[count - 1], 1)}）";
    }

    private static List<double> _pick<T>(List<T> runs, System.Func<T, double> pick)
    {
        var out_values = new List<double>();
        foreach (T run in runs)
        {
            out_values.Add(pick(run));
        }

        return out_values;
    }

    /// <summary>满装 4 人那一档的一次测量。</summary>
    private sealed class RichRun
    {
        public double build_ms;
        public double party_ms;
        public double sim_ms;
        public double save_ms;
        public double load_ms;
        public int actors;
        public string winner = "?";
        public int rounds;
        public double kb;
        public bool read_back;
    }

    /// <summary>线索本那一档的一次测量。</summary>
    private sealed class ClueRun
    {
        public double query_ms;
        public double panel_ms;
        public int entries;
        public int rows;
    }

    /// <summary>副本层数那一档的一次测量。</summary>
    private sealed class FloorRun
    {
        public double data_ms;
        public double panel_ms;
        public int floors;
        public int percent;
        public int rows;
    }

    /// <summary>4 人满装 + 大背包：从造数据到打完一场 4v4，再到存档往返（只测不打印）。</summary>
    private RichRun _measure_rich_party(TableDb db, TestCase helper)
    {
        ulong t0 = Time.GetTicksUsec();
        TableDb wide = helper.TableWithNChars(4);
        // 队伍必须**显式点名 4 人**：0.10.0 起 `new_game()` 只给 `recruit_def.is_initial` 的成员
        // （开局书生一人），不点名的话这一段会静默退化成「1 人满装」——2026-10-03 实测过一次
        // （打印里出现「（1 人）」、模拟 1 回合就结束），量到的不再是本页要测的最重夹具。
        var state = (GameState)helper.PartyState(wide, 4);
        // 夹具自检：这一段的名字就叫「4 人满装」，队伍少于 4 人说明夹具又跟着设计状态漂了
        // （2026-10-03 真的退化过一次：new_game 只给初始成员，于是量成了「1 人满装」）。
        if (state.char_ids.Count != 4)
        {
            GD.PushError($"[PerfAnalysis] 夹具队伍只有 {state.char_ids.Count} 人（应为 4）——量到的不是最重的夹具");
        }

        var inventory = (Inventory)state.inventory;
        foreach (Variant charEntry in state.char_ids)
        {
            string charId = charEntry.AsString();
            var baseRow = (CharacterBaseRow)wide.get_row("character_base", charId);
            string weaponType = baseRow.weapon_type;
            foreach (string baseId in EquipBases)
            {
                string instanceId = inventory.add_equipment(wide, baseId, new Godot.Collections.Array());
                inventory.equip(wide, charId, state.level_of(charId), weaponType, instanceId);
            }
        }

        foreach (Variant itemEntry in wide.rows("item_base"))
        {
            if (itemEntry.AsGodotObject() is not ItemBaseRow row)
            {
                continue;
            }

            if (row.item_type == "currency")
            {
                continue;
            }

            inventory.add_item(wide, row.item_id, Mathf.Max(1, row.stack_max));
        }

        double buildMs = _ms_since(t0);

        ulong t1 = Time.GetTicksUsec();
        Godot.Collections.Array actors = PartyBuilder.build_actors(wide, state, new Godot.Collections.Array());
        double partyMs = _ms_since(t1);

        Godot.Collections.Array enemies =
            EnemyFactory.create(wide).create_team("team_heifeng_elite", "nightmare");
        ulong t2 = Time.GetTicksUsec();
        BattleSimulator sim = BattleSimulator.create(wide, new RngService(20261003));
        var options = new Godot.Collections.Dictionary { { "modifiers", new Godot.Collections.Dictionary() } };
        Godot.Collections.Dictionary result = sim.simulate(actors, enemies, options);
        double simMs = _ms_since(t2);

        ulong t3 = Time.GetTicksUsec();
        string text = Json.Stringify(state.to_dict(), "  ");
        double saveMs = _ms_since(t3);
        ulong t4 = Time.GetTicksUsec();
        GameState back = GameState.from_dict(Json.ParseString(text).AsGodotDictionary(), wide);
        double loadMs = _ms_since(t4);
        return new RichRun
        {
            build_ms = buildMs,
            party_ms = partyMs,
            sim_ms = simMs,
            save_ms = saveMs,
            load_ms = loadMs,
            actors = actors.Count,
            winner = result.ContainsKey("winner") ? result["winner"].AsString() : "?",
            rounds = result.ContainsKey("rounds") ? result["rounds"].AsInt32() : 0,
            kb = text.Length / 1024.0,
            read_back = back != null,
        };
    }

    private static void _print_rich_party(List<RichRun> runs)
    {
        RichRun last = runs[runs.Count - 1];
        GD.Print(
            $"· 4 人满装 + 大背包：构造 {_median_ms(_pick(runs, r => r.build_ms))}"
            + $"｜造战斗单位 {_median_ms(_pick(runs, r => r.party_ms))}（{last.actors} 人）"
            + $"｜4v4 模拟 {_median_ms(_pick(runs, r => r.sim_ms))}（{last.winner}，{last.rounds} 回合）"
            + $"｜存档 {_median_ms(_pick(runs, r => r.save_ms))}（{NumberFormat.Fmt(last.kb, 1)} KB）"
            + $"｜读档 {_median_ms(_pick(runs, r => r.load_ms))}（读回={_bool(last.read_back)}）");
    }

    /// <summary>线索本 40 条：查询 + 打开真面板并建行（只测不打印）。</summary>
    private ClueRun _measure_long_clue_list(TableDb db, TestCase helper)
    {
        TableDb wide = helper.TableWithDuplicatedTriggers("scene_heifengzhai", 6);
        var state = GameState.new_game(wide, "normal", System.Array.Empty<string>(), 0);

        ulong t0 = Time.GetTicksUsec();
        ClueService service = ClueService.create(wide, state);
        Godot.Collections.Array entries = service.clues_for_scene("scene_heifengzhai");
        double queryMs = _ms_since(t0);

        var screen = (ClueScreen)GD.Load<PackedScene>("res://scenes/clue_screen.tscn").Instantiate();
        screen.db_override = wide;
        screen.state_override = state;
        screen.scope = "scene";
        screen.scene_id = "scene_heifengzhai";
        ulong t1 = Time.GetTicksUsec();
        // **`--script` 模式下 `add_child` 不会触发 `_ready`**（本项目的用例都显式调 `setup()`），
        // 所以建 UI 的耗时要连 `setup()` 一起量（第一版只量了 add_child，量出来 0 行）。
        GetTree().Root.AddChild(screen);
        screen.setup();
        double panelMs = _ms_since(t1);
        // 面板现在是五类分页，长列表那批全在「隐藏」页里
        int rows = screen.section_row_count("hidden");

        GetTree().Root.RemoveChild(screen);
        screen.Free();
        return new ClueRun { query_ms = queryMs, panel_ms = panelMs, entries = entries.Count, rows = rows };
    }

    private static void _print_long_clue_list(List<ClueRun> runs)
    {
        ClueRun last = runs[runs.Count - 1];
        GD.Print(
            $"· 线索本 {last.entries} 条：查询 {_median_ms(_pick(runs, r => r.query_ms))}"
            + $"｜开面板并建 {last.rows} 行 {_median_ms(_pick(runs, r => r.panel_ms))}");
    }

    /// <summary>副本完成度 30 层：取完成度+分层 + 打开真面板建行（只测不打印）。</summary>
    private FloorRun _measure_many_floors(TableDb db, TestCase helper)
    {
        TableDb wide = helper.TableWithManyFloors(db, "scene_heifengzhai", 30);
        var state = GameState.new_game(wide, "normal", System.Array.Empty<string>(), 0);
        DungeonService service = DungeonService.create(wide, state, null);

        ulong t0 = Time.GetTicksUsec();
        Godot.Collections.Dictionary snapshot = service.progress("scene_heifengzhai");
        Godot.Collections.Array floors = service.floors("scene_heifengzhai");
        double dataMs = _ms_since(t0);

        var screen = (DungeonScreen)GD.Load<PackedScene>("res://scenes/dungeon_screen.tscn").Instantiate();
        screen.db_override = wide;
        screen.state_override = state;
        screen.scene_id = "scene_heifengzhai";
        ulong t1 = Time.GetTicksUsec();
        GetTree().Root.AddChild(screen);
        screen.setup();
        double panelMs = _ms_since(t1);
        int rows = screen.FindChildren("FloorRow*", "HBoxContainer", true, false).Count;
        GetTree().Root.RemoveChild(screen);
        screen.Free();
        return new FloorRun
        {
            data_ms = dataMs,
            panel_ms = panelMs,
            floors = floors.Count,
            percent = snapshot["percent"].AsInt32(),
            rows = rows,
        };
    }

    private static void _print_many_floors(List<FloorRun> runs)
    {
        FloorRun last = runs[runs.Count - 1];
        GD.Print(
            $"· 完成度 {last.floors} 层：取完成度+分层 {_median_ms(_pick(runs, r => r.data_ms))}"
            + $"（总计 {last.percent}%）｜开面板并建 {last.rows} 行 {_median_ms(_pick(runs, r => r.panel_ms))}");
    }

    /// <summary>GDScript 的 `str(bool)` 是小写。</summary>
    private static string _bool(bool value)
    {
        return value ? "true" : "false";
    }
}