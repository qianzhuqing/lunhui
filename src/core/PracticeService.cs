using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 城镇木桩练习战（C# 版，对位原 `src/core/practice_service.gd`；设计 09 §3.3）：
/// 把玩家从「刚出门」送到「能上山」的练级场。
///
/// 规则全部读表（09 §3.3 的收益上限表）：
///   角色等级    只给到 `growth_const.dummy_xp_cap_level`（5 级），到顶后木桩战给 0 经验
///   招式熟练度  参与木桩战的招式只涨到 `growth_const.dummy_mastery_cap`（3 级）
///   掉落／铜钱／首杀／击败领悟  一律没有（否则它就成了刷级永动机，也就不需要上山了）
/// 到顶时给一句提示（`ui_text.dummy_cap_reached`）：静默不给经验会让玩家以为卡住。
///
/// 入口：城镇里 `building_def.service_id == SERVICE_ID` 的建筑（现在只有清风驿校场的木桩）。
///
/// **队伍 id 是代码侧约定的**：`building_def` 没有指向队伍的列，而设计 09 §3.3 也只写「按 E 进入
/// 战斗」、没给木桩的数值。设计侧补一行 `enemy_team`（`TEAM_ID`）与一行 `enemy_base`
/// （`ENEMY_ID`：中性模板、**不给招式**＝不还手）之后这里就能跑；在那之前 `data_ready()`
/// 会如实说出缺什么（`待策划确认.md` Q52），界面照实说「还没准备好」。
/// </summary>
public partial class PracticeService : RefCounted
{
    /// <summary>木桩这类建筑的 `building_def.service_id`。</summary>
    public const string SERVICE_ID = "dummy_training";

    /// <summary>练习战用的队伍 id（设计侧补表时照这个名字建）。</summary>
    public const string TEAM_ID = "team_dummy_training";

    /// <summary>木桩自己的敌人 id（中性模板、不给招式）。</summary>
    public const string ENEMY_ID = "en_dummy_training";

    // ---------------------------------------------------------------- 过渡期取值口
    // 与 `Encounter` 同一套：GDScript 读不到 C# 的 `const`，而 `test_practice`／
    // `validate_tables.ps1` 现在按 `PracticeServiceScript.SERVICE_ID` 这类名字读它。

    public static string service_id() => SERVICE_ID;
    public static string team_id() => TEAM_ID;
    public static string enemy_id() => ENEMY_ID;

    public static bool is_practice_building(TableDb db, string building_id)
    {
        var row = db?.get_row("building_def", building_id) as BuildingDefRow;
        return row != null && row.service_id == SERVICE_ID;
    }

    /// <summary>木桩能练到几级（`growth_const.dummy_xp_cap_level`；缺行时返回 0 = 不限制，并出声）。</summary>
    public static int cap_level(TableDb db)
    {
        var row = db?.get_row("growth_const", "dummy_xp_cap_level") as GrowthConstRow;
        if (row == null)
        {
            GD.PushError("[PracticeService] growth_const 缺 dummy_xp_cap_level——练习战的经验上限没有出处");
            return 0;
        }
        return (int)row.value;
    }

    /// <summary>木桩战里熟练度能涨到几级（`growth_const.dummy_mastery_cap`）。</summary>
    public static int mastery_cap(TableDb db)
    {
        var row = db?.get_row("growth_const", "dummy_mastery_cap") as GrowthConstRow;
        if (row == null)
        {
            GD.PushError("[PracticeService] growth_const 缺 dummy_mastery_cap——练习战的熟练度上限没有出处");
            return 0;
        }
        return (int)row.value;
    }

    /// <summary>
    /// 还能练吗：队伍里**还有人**没过上限。经验是队伍池，所以按「最低的那个人」判——
    /// 全员到顶之后木桩战就不再给经验（设计：到顶后给 0 经验 + 一句提示）。
    /// </summary>
    public static bool xp_allowed(TableDb db, GodotObject state)
    {
        string[] char_ids = CharIds(state);
        if (state == null || char_ids.Length == 0)
        {
            return false;
        }
        int cap = cap_level(db);
        if (cap <= 0)
        {
            return true;
        }
        foreach (string char_id in char_ids)
        {
            if (LevelOf(state, char_id) < cap)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 练习战该给多少经验。设计 0.14.0（Q54）定了口径：**单场最多补到上限门槛，超出部分丢弃**
    /// （「溢出会催生『多打一场提前拿满』的无意义重复」）。
    ///
    /// 经验进的是**队伍池**（`party_exp`），`LevelService` 先补等级最低的人，所以：
    ///   · 全员到顶 → 0
    ///   · 否则 → `min(原值, 还差的量)`，其中「还差的量」= 把每个成员都送到上限所需经验之和 − 池里已有的
    /// 0.13.0 那版是「还有人没过上限就给原值」，一次给多了会让有人冲过上限——Q54 就是为这个问的。
    /// </summary>
    public static int capped_exp(TableDb db, GodotObject state, int raw_exp)
    {
        int raw = Mathf.Max(0, raw_exp);
        if (!xp_allowed(db, state))
        {
            return 0;
        }
        if (cap_level(db) <= 0)
        {
            // 表里没给上限：不自己编一个，照原值给（`cap_level` 已经 push_error 报缺行）
            return raw;
        }
        return Mathf.Min(raw, exp_room(db, state));
    }

    /// <summary>
    /// 把全队送到上限**还差多少经验**（扣掉池里已经攒着的）：
    /// `Σ_成员(从当前等级到上限的 exp_to_next 之和) − party_exp`，下限 0。
    /// </summary>
    public static int exp_room(TableDb db, GodotObject state)
    {
        if (state == null)
        {
            return 0;
        }
        int cap = cap_level(db);
        if (cap <= 0)
        {
            return 0;
        }
        int need = 0;
        foreach (string char_id in CharIds(state))
        {
            need += ExpToCap(db, LevelOf(state, char_id), cap);
        }
        return Mathf.Max(0, need - state.Get("party_exp").AsInt32());
    }

    /// <summary>从 `level` 升到 `cap` 需要的累计经验（逐级 `level_growth.exp_to_next` 求和；已到顶就是 0）。</summary>
    private static int ExpToCap(TableDb db, int level, int cap)
    {
        int total = 0;
        for (int current = Mathf.Max(1, level); current < cap; current++)
        {
            var row = db?.get_row("level_growth", current) as LevelGrowthRow;
            if (row == null)
            {
                continue;
            }
            total += row.exp_to_next;
        }
        return total;
    }

    /// <summary>到顶时给玩家的一句提示（`ui_text.dummy_cap_reached`）。</summary>
    public static string cap_notice(TableDb db)
    {
        var row = db?.get_row("ui_text", "dummy_cap_reached") as UiTextRow;
        if (row == null)
        {
            GD.PushError("[PracticeService] ui_text 缺 dummy_cap_reached——到顶提示没有出处");
            return "木桩已经练不出东西了。";
        }
        return row.text_cn;
    }

    /// <summary>练习战的数据齐了没有。返回 {ok, error}；缺什么就说什么（不静默、不自己编数值）。</summary>
    public static Godot.Collections.Dictionary data_ready(TableDb db)
    {
        if (db == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有配置表" } };
        }
        var team = db.get_row("enemy_team", TEAM_ID) as EnemyTeamRow;
        if (team == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", $"缺 enemy_team 行 {TEAM_ID}（设计侧补）" },
            };
        }
        var missing = new System.Collections.Generic.List<string>();
        foreach (string member in team.members.Split(";"))
        {
            string enemy_id = member.Split(":")[0].Trim();
            if (enemy_id.Length == 0)
            {
                continue;
            }
            if (db.get_row("enemy_base", enemy_id) == null)
            {
                missing.Add(enemy_id);
            }
        }
        if (missing.Count > 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", $"缺 enemy_base 行 {string.Join("、", missing)}（设计侧补）" },
            };
        }
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    /// <summary>
    /// 组一场练习战。调用方负责把它交给战斗场景（`local_map_controller.start_practice`）。
    /// `encounter.practice = true` 是**后面所有封顶规则的开关**（结算面板按它跳过掉落/铜钱/领悟/首杀）。
    /// </summary>
    public static Encounter build_encounter(TableDb db, string scene_id, string difficulty_id)
    {
        var team = db.get_row("enemy_team", TEAM_ID);
        var spawn = new Godot.Collections.Dictionary
        {
            { "spawn_id", "practice" },
            { "source_scene", scene_id },
            { "source_key", "practice" },     // 不写房间名：练习战不进副本完成度
            { "team_id", TEAM_ID },
            { "is_elite", false },
        };
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.CONTACT_FRONT, difficulty_id);
        encounter.practice = true;
        return encounter;
    }

    // ---------------------------------------------------------------- 读会话的两个小口子
    //
    // `GameState` 还是 GDScript（`game_state.gd`），所以这里走 `Get`／`Call`——
    // 与 `StoryService` 读 `state` 的形状一致；P5 那份搬完再改成直调。

    private static string[] CharIds(GodotObject state)
    {
        if (state == null)
        {
            return System.Array.Empty<string>();
        }
        Variant value = state.Get("char_ids");
        return value.VariantType == Variant.Type.Nil ? System.Array.Empty<string>() : value.AsStringArray();
    }

    private static int LevelOf(GodotObject state, string char_id) =>
        state.Call("level_of", char_id).AsInt32();
}
