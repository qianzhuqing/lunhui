using Godot;
using Lunhui.Core;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 明雷遭遇规则：接触方式决定战斗开局——对位 `tests/test_encounter.gd`（2026-10-11 P4 第八批搬 C#）。
///
/// **类名与 `TestEncounter.cs` 区分**：那一份是 `Encounter` 搬 C# 时写的（suite 名「明雷遭遇规则（C# 侧）」），
/// 这一份是旧链原来那份；suite 名保留原样「明雷遭遇规则」，两份在日志里差一个后缀。
/// </summary>
public partial class TestEncounterRules : TestCase
{
    public override string SuiteName() => "明雷遭遇规则";

    public override void Run()
    {
        TableDb db = GetDb();
        // 阵营常量的**绝对值**：Encounter 这份现在直接取自 `BattleActor.SIDE_*`（单一出处），
        // 这里与 `test_battle._check_absolute_constants` 一起把 0/1 钉死——改哪一处都会红。
        CheckEq(Encounter.side_ally(), 0, "Encounter 的我方常量 = 0");
        CheckEq(Encounter.side_enemy(), 1, "Encounter 的敌方常量 = 1");
        var spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        var team = db.get_row("enemy_team", "team_wolf_pack");
        _check_front(db, spawn, team);
        _check_back(db, spawn, team);
        _check_sleep(db, spawn, team);
        _check_spotted(db, spawn, team);
        _check_neutral_team(db);
        _check_pinned_contact_distance();
    }

    /// <summary>
    /// 明雷的**接触距离**是开发侧定的手感值（`enemy_team` 没有这一列，已记交接表等设计补）。
    /// 变异探针实测：把它从 22 改成 60，一整套自检照样绿——而它决定「离多远就被拖进战斗」，
    /// 悄悄调大等于玩家在视野外被伏击。按 `test_poise` 里敌人那两行的同一条纪律：
    /// 把**数值本身**钉住，改它就得改这条断言（有意识的行为）。
    /// </summary>
    private void _check_pinned_contact_distance()
    {
        CheckFloat(RoamingEnemy.contact_distance(), 22.0, "明雷接触距离 22px（改它＝改被拖进战斗的远近）");
    }

    private void _check_front(TableDb db, Resource spawn, Resource team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        CheckEq(encounter.first_side, -1, "正面遭遇不强制先手，按身法排序");
        CheckFloat(encounter.surprise_bonus, 0.0, "正面没有奇袭加成");
        CheckEq(encounter.pending_rules.Length, 0, "正面没有暂缓规则");
        CheckEq(encounter.contact_label(), "正面遭遇", "接触方式文案");
        CheckEq(encounter.team_name, "野狼群", "队伍名取自 enemy_team");
        CheckEq(encounter.headline(), "正面遭遇　野狼群", "开场文案");
    }

    private void _check_back(TableDb db, Resource spawn, Resource team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_back(), "normal");
        CheckEq(encounter.first_side, Encounter.side_ally(), "背后接触我方先手");
        CheckFloat(
            encounter.enemy_poise_reduce,
            0.3,
            "背袭让敌方架势初始值降 30%（数值取 combat_const.backstab_poise_reduce）");
        CheckEq(encounter.pending_rules.Length, 0, "架势削减已经实现，不再记暂缓");
        CheckEq(
            encounter.round_modifiers(1, Encounter.side_ally()).Count,
            0,
            "背袭没有额外增伤（那是奇袭）");
    }

    private void _check_sleep(TableDb db, Resource spawn, Resource team)
    {
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_sleep(), "normal");
        CheckEq(encounter.first_side, Encounter.side_ally(), "奇袭我方先手");
        CheckFloat(encounter.surprise_bonus, 0.5, "奇袭首回合增伤取 combat_const.surprise_damage_bonus");
        Godot.Collections.Dictionary first = encounter.round_modifiers(1, Encounter.side_ally());
        CheckFloat(_f(first, "dmg_up"), 0.5, "首回合我方增伤 50%");
        CheckEq(encounter.round_modifiers(2, Encounter.side_ally()).Count, 0, "增伤只在首回合");
        CheckEq(encounter.round_modifiers(1, Encounter.side_enemy()).Count, 0, "增伤不给敌方");
    }

    private void _check_spotted(TableDb db, Resource spawn, Resource team)
    {
        Encounter spotted = Encounter.build(db, spawn, team, Encounter.contact_spotted(), "normal");
        CheckEq(spotted.first_side, Encounter.side_enemy(), "被发现时敌方先手");
        Encounter caught = Encounter.build(db, spawn, team, Encounter.contact_caught(), "normal");
        CheckEq(caught.first_side, Encounter.side_enemy(), "被追上敌方先手");
        CheckEq(
            caught.pending_rules.Length,
            0,
            "被追上的「首回合不能逃」已经由 BattleSimulator 真判了，不再记暂缓（见 test_battle 的逃跑用例）");
    }

    private void _check_neutral_team(TableDb db)
    {
        var spawn = db.get_row("roaming_spawn", "sp_lp_herbalist");
        var team = db.get_row("enemy_team", "team_herbalist");
        Encounter encounter = Encounter.build(db, spawn, team, Encounter.contact_front(), "normal");
        CheckEq(encounter.is_elite, false, "采药人不是精英");
        CheckEq(_s(encounter.summary(), "team_id"), "team_herbalist", "中立队伍照样能构造遭遇（是否开战由明雷决定）");
    }

    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static double _f(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;
}
