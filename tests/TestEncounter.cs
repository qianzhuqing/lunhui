using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 明雷遭遇规则（C# 版，对位 `tests/test_encounter.gd` 的规则那几节）。
///
/// GDScript 那条用例还留着跑整合（走 `RoamingEnemy.CONTACT_DISTANCE` 那类世界层的手感值）；
/// 这里钉的是 `Encounter` 自己的读表与开局规则：接触方式 → 先手方／增伤／架势削减、
/// 以及「表行与字典两种来源」的口径。
/// </summary>
public partial class TestEncounter : TestCase
{
    public override string SuiteName() => "明雷遭遇规则（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();

        // 阵营常量的**绝对值**：Encounter 这份现在直接取自 `BattleActor.SIDE_*`（单一出处），
        // 这里与 `test_battle._check_absolute_constants` 一起把 0/1 钉死——改哪一处都会红。
        CheckEq(Encounter.side_ally(), 0, "Encounter 的我方常量 = 0");
        CheckEq(Encounter.side_enemy(), 1, "Encounter 的敌方常量 = 1");

        // 接触方式的字面量：存档、日志与条件里都用它们做字符串比较，改字就静默失效
        CheckEq(Encounter.contact_front(), "front", "正面接触的字面量");
        CheckEq(Encounter.contact_back(), "back", "背袭的字面量");
        CheckEq(Encounter.contact_sleep(), "ambush_sleep", "奇袭的字面量");
        CheckEq(Encounter.contact_spotted(), "spotted", "被发现的字面量");
        CheckEq(Encounter.contact_caught(), "caught", "被追上的字面量");

        GodotObject spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        GodotObject team = db.get_row("enemy_team", "team_wolf_pack");
        CheckNotNull(spawn, "夹具要的明雷刷新点在表里");
        CheckNotNull(team, "夹具要的队伍在表里");

        _CheckFront(db, spawn, team);
        _CheckBack(db, spawn, team);
        _CheckSleep(db, spawn, team);
        _CheckSpottedAndCaught(db, spawn, team);
        _CheckNeutralTeam(db);
        _CheckDictionarySpawnRow(db, spawn, team);
    }

    private void _CheckFront(TableDb db, GodotObject spawn, GodotObject team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        CheckEq(encounter.first_side, -1, "正面遭遇不强制先手，按身法排序");
        CheckFloat(encounter.surprise_bonus, 0.0, "正面没有奇袭加成");
        CheckEq(encounter.pending_rules.Length, 0, "正面没有暂缓规则");
        CheckEq(encounter.contact_label(), "正面遭遇", "接触方式文案");
        CheckEq(encounter.team_name, "野狼群", "队伍名取自 enemy_team");
        CheckEq(encounter.headline(), "正面遭遇　野狼群", "开场文案");
        CheckEq(encounter.spawn_id, "sp_lp_wolf_01", "明雷的 spawn_id 进遭遇");
        CheckEq(encounter.source_scene, "overworld", "明雷默认来自大地图");
        CheckEq(encounter.source_key, "sp_lp_wolf_01", "source_key 缺省就是 spawn_id");
        CheckEq(encounter.members, "en_wolf:3", "成员串取自 enemy_team");
        CheckEq(encounter.difficulty_id, "normal", "难度照传进来的值");
        CheckEq(encounter.is_elite, false, "这行明雷不是精英");
        CheckFloat(encounter.enemy_poise_reduce, 0.0, "正面不削敌方架势");
    }

    private void _CheckBack(TableDb db, GodotObject spawn, GodotObject team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_back(), "normal");
        CheckEq(encounter.first_side, Encounter.side_ally(), "背后接触我方先手");
        CheckFloat(
            encounter.enemy_poise_reduce, 0.3,
            "背袭让敌方架势初始值降 30%（数值取 combat_const.backstab_poise_reduce）");
        CheckEq(encounter.pending_rules.Length, 0, "架势削减已经实现，不再记暂缓");
        CheckEq(
            encounter.round_modifiers(1, Encounter.side_ally()).Count, 0,
            "背袭没有额外增伤（那是奇袭）");
    }

    private void _CheckSleep(TableDb db, GodotObject spawn, GodotObject team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_sleep(), "normal");
        CheckEq(encounter.first_side, Encounter.side_ally(), "奇袭我方先手");
        CheckFloat(
            encounter.surprise_bonus, 0.5,
            "奇袭首回合增伤取 combat_const.surprise_damage_bonus");
        Godot.Collections.Dictionary first = encounter.round_modifiers(1, Encounter.side_ally());
        CheckFloat(first["dmg_up"].AsDouble(), 0.5, "首回合我方增伤 50%");
        CheckEq(
            encounter.round_modifiers(2, Encounter.side_ally()).Count, 0,
            "增伤只在首回合");
        CheckEq(
            encounter.round_modifiers(1, Encounter.side_enemy()).Count, 0,
            "增伤不给敌方");
        // 「敌方首回合无防备」设计只写了六个字、没定效果，代码就如实记成暂缓规则
        CheckEq(encounter.pending_rules.Length, 1, "奇袭留一条暂缓规则（无防备的具体效果未定）");
        CheckTrue(
            encounter.pending_rules[0].Contains("无防备"),
            $"暂缓规则说的是「无防备」这件事：{encounter.pending_rules[0]}");
    }

    private void _CheckSpottedAndCaught(TableDb db, GodotObject spawn, GodotObject team)
    {
        Encounter spotted = Encounter.build(db, spawn, team, Encounter.contact_spotted(), "normal");
        CheckEq(spotted.first_side, Encounter.side_enemy(), "被发现时敌方先手");
        CheckEq(spotted.pending_rules.Length, 0, "被发现没有暂缓规则");
        Encounter caught = Encounter.build(db, spawn, team, Encounter.contact_caught(), "normal");
        CheckEq(caught.first_side, Encounter.side_enemy(), "被追上敌方先手");
        CheckEq(
            caught.pending_rules.Length, 0,
            "被追上的「首回合不能逃」已经由 BattleSimulator 真判了，不再记暂缓（见 test_battle 的逃跑用例）");
    }

    private void _CheckNeutralTeam(TableDb db)
    {
        GodotObject spawn = db.get_row("roaming_spawn", "sp_lp_herbalist");
        GodotObject team = db.get_row("enemy_team", "team_herbalist");
        CheckNotNull(spawn, "采药人那个刷新点在表里");
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        CheckEq(encounter.is_elite, false, "采药人不是精英");
        CheckEq(
            encounter.summary()["team_id"].AsString(), "team_herbalist",
            "中立队伍照样能构造遭遇（是否开战由明雷决定）");
        CheckEq(
            encounter.summary()["contact_label"].AsString(), "正面遭遇",
            "summary 里带的是给玩家看的文案");
    }

    /// <summary>
    /// 字典来源那一半：隐藏 Boss、练习战、随机事件都直接丢一个字典进来（没有刷新点行）。
    /// 队伍行缺 `name_cn` 时退回 `team_id`——这是 GDScript 那版就有的兜底，别在搬运时丢掉。
    /// </summary>
    private void _CheckDictionarySpawnRow(TableDb db, GodotObject spawn, GodotObject team)
    {
        var spawn_dict = new Godot.Collections.Dictionary
        {
            { "spawn_id", "spar_wang" },
            { "source_scene", "scene_qingfengyi" },
            { "source_key", "spar_wang" },
            { "team_id", "team_ghost" },
            { "is_elite", true },
        };
        var team_dict = new Godot.Collections.Dictionary { { "members", "en_wolf:1" } };
        Encounter encounter = Encounter.build(db, spawn_dict, team_dict, Encounter.contact_front(), "hard");
        CheckEq(encounter.spawn_id, "spar_wang", "字典来源的 spawn_id");
        CheckEq(encounter.source_scene, "scene_qingfengyi", "字典来源的 source_scene");
        CheckEq(encounter.source_key, "spar_wang", "字典来源的 source_key");
        CheckEq(encounter.is_elite, true, "字典来源的 is_elite");
        CheckEq(encounter.difficulty_id, "hard", "难度照传进来的值");
        CheckEq(encounter.members, "en_wolf:1", "字典来源的成员串");
        CheckEq(encounter.team_name, "team_ghost", "队伍行没有 name_cn → 退回 team_id");
        CheckEq(encounter.threat_tag, "green", "队伍行没有 threat_tag → 退回 green");
        CheckEq(encounter.region_id, "", "刷新点字典没给 region_id → 空串");

        // 表行来源要真的读表（别把兜底值当成表值）
        CheckEq(
            Encounter.build(db, spawn, team, Encounter.contact_front(), "normal").threat_tag,
            team.Get("threat_tag").AsString(),
            "threat_tag 来自队伍行（不是兜底的 green）");
    }
}
