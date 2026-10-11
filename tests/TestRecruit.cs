using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 剧情招募（设计 09 §3.2）。
///
/// 口径一句话：**条件旗标已点亮 ∧ 人在那张图（或站到那个区域地标前）∧ 还没入队 → 入队**。
///
/// **2026-10-11（P4 第二十八批）起这份就是唯一一份**：旧链 `tests/test_recruit.gd` 已并入本文件后退场
/// ——这一批补进六节：开局队伍按 `is_initial` 现取且不算「待加入」、入队真的发齐「等级 ＋ 起始装备 ＋
/// 起始武学 ＋ 装配 ＋ `flag_<char>_joined`」、区域路径**按表选行**（不写死 ch_gang）、
/// 条件语言的 `|` 在招募上真的生效、区域判定半径钉数值、四条招募链的条件与设计表一致（断链门限）。
/// </summary>
public partial class TestRecruit : TestCase
{
    public override string SuiteName() => "剧情招募";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckMaxParty();
        _CheckSceneMatches(db);
        _CheckInitialMembers(db);
        _CheckInitialMemberJoinsWhenNotProtagonist(db);
        _CheckConditionGate(db);
        _CheckJoinSeedsCharacter(db);
        _CheckRegionRows(db);
        _CheckOrJoinCondition();
        _CheckRegionRadius();
        _CheckNoDoubleJoin(db);
        _CheckJoinConditionsAreReachable(db);
        _CheckJoinRewards(db);
    }

    /// <summary>队伍上限写死绝对值（设计 00 总览：3–4 人）——变异探针点过它。</summary>
    private void _CheckMaxParty()
    {
        CheckEq(GameState.max_party(), 4, "队伍上限 4");
    }

    /// <summary>`join_scene` 既是小地图 id，也可能是区域节点；两条都能对上，且不串台。</summary>
    private void _CheckSceneMatches(TableDb db)
    {
        var gang = (RecruitDefRow)db.get_row("recruit_def", "ch_gang");
        CheckFalse(RecruitService.scene_matches(db, gang, "scene_qingfengyi"), "落雁坡的同伴不会在清风驿等着");
        CheckTrue(
            RecruitService.scene_matches(db, db.get_row("recruit_def", "ch_du"), "scene_heifengzhai"),
            "join_scene 直接是小地图时直配");
        // 父区域那一条分支：拿内存副本把 join_scene 改成区域节点，进它的小地图就该算在这张图里
        var region_fixture = (RecruitDefRow)gang.Duplicate(true);
        region_fixture.join_scene = "n_heifengzhai";
        CheckTrue(
            RecruitService.scene_matches(db, region_fixture, "scene_heifengzhai"),
            "join_scene 是父区域节点时，它的小地图也算在这张图里");
        CheckFalse(
            RecruitService.scene_matches(db, region_fixture, "scene_qingfengyi"),
            "别的区域的小地图不算（地点口径不串台）");
    }

    /// <summary>
    /// 开局队伍 = `recruit_def.is_initial` 的那些人（**按表现取**，不写死是哪几位）；
    /// 而且他们**不该**出现在「待加入」里——否则进图那一下会再发一次入队奖励。
    /// </summary>
    private void _CheckInitialMembers(TableDb db)
    {
        GodotObject state = NewGame(db, "normal", System.Array.Empty<string>());
        var initial = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("recruit_def"))
        {
            var row = (RecruitDefRow)entry.AsGodotObject();
            if (row.is_initial)
            {
                initial.Add(row.char_id);
            }
        }

        CheckGt(initial.Count, 0, $"表里有初始成员（{initial.Count} 人）");
        CheckEq(
            string.Join(",", state.Get("char_ids").AsStringArray()),
            string.Join(",", initial),
            "开局队伍就是这些初始成员");
        foreach (string char_id in initial)
        {
            state.Call("set_flag", "flag_board_read", true);
            CheckFalse(
                _PendingIds(db, state, "scene_qingfengyi").Contains(char_id),
                $"初始成员 {char_id} 不算「待加入」");
        }
    }

    /// <summary>
    /// 入队要真的把人加进队伍，并发齐「等级 ＋ 起始装备 ＋ 起始武学 ＋ 装配」，顺带点亮
    /// `flag_&lt;char_id&gt;_joined`（引导的下一步就是等它）。
    /// </summary>
    private void _CheckJoinSeedsCharacter(TableDb db)
    {
        var row = _FirstRow(db, r => !r.is_initial);
        CheckNotNull(row, "入队用例有对象（recruit_def 里有非初始成员）");
        if (row == null)
        {
            return;
        }

        string char_id = row.char_id;
        string scene_id = _SceneForRow(db, row);
        CheckFalse(scene_id.Length == 0, $"{char_id} 的 join_scene 认得出对应的小地图");
        if (scene_id.Length == 0)
        {
            return;
        }

        GodotObject state = SoloState(db);
        _SatisfyCondition(state, row.join_condition);
        int before_party = state.Call("party_size").AsInt32();
        int before_equipment = _EquipmentCount(state);
        Godot.Collections.Array results = RecruitService.join_all_for_scene(db, state, scene_id);
        CheckEq(results.Count, 1, "这一张图上只有一位待加入");
        CheckTrue(results[0].AsGodotDictionary()["ok"].AsBool(), "入队成功");
        CheckEq(state.Call("party_size").AsInt32(), before_party + 1, "队伍多了一个人");
        CheckTrue(_PartyIds(state).Contains(char_id), $"{char_id} 进了队伍名单");
        var template = db.get_row("character_base", char_id) as CharacterBaseRow;
        CheckNotNull(template, $"{char_id} 有角色模板");
        if (template == null)
        {
            return;
        }

        CheckEq(state.Call("level_of", char_id).AsInt32(), template.start_level, "等级取模板的 start_level");
        string[] equipped = _EquippedInstances(state, db, char_id);
        CheckEq(equipped.Length, template.equip_ids().Length, $"起始装备都穿上了（{template.equip_ids().Length} 件）");
        CheckGt(_EquipmentCount(state), before_equipment, "背包里多了这几件实例");
        string[] learned = state.Call("learned_of", char_id).AsStringArray();
        foreach (string skill_id in template.skill_ids())
        {
            CheckTrue(System.Array.IndexOf(learned, skill_id) >= 0, $"起始武学 {skill_id} 已学会");
        }

        CheckFalse(
            state.Call("loadout_of", char_id).AsGodotDictionary()["active"].AsStringArray().Length == 0,
            "起始招式已经铺进装配");
        CheckTrue(
            state.Call("has_flag", $"flag_{char_id}_joined").AsBool(),
            $"入队点亮 flag_{char_id}_joined（引导拿它推进）");
    }

    private void _CheckConditionGate(TableDb db)
    {
        var ci = (RecruitDefRow)db.get_row("recruit_def", "ch_ci");
        CheckEq(ci.join_condition, "flag_board_read", "燕小七的加入条件是看过告示板");
        GodotObject state = SoloState(db);
        CheckFalse(_PendingIds(db, state, "scene_qingfengyi").Contains("ch_ci"), "条件没点亮时不入队");
        state.Call("set_flag", ci.join_condition, true);
        CheckTrue(_PendingIds(db, state, "scene_qingfengyi").Contains("ch_ci"), "点亮条件后进入待加入名单");
        CheckFalse(_PendingIds(db, state, "scene_heifengzhai").Contains("ch_ci"), "换张图不收这位同伴");
    }

    /// <summary>
    /// 区域路径（落雁坡这类**没有小地图**的位点）：`join_scene` 指向 `map_region` 的行只按区域地标收人，
    /// 不认副本——**按表选行**，不把 ch_gang 写死（换人换了位点这条不该跟着红）。
    /// </summary>
    private void _CheckRegionRows(TableDb db)
    {
        var region_row = _FirstRow(db, r =>
            !r.is_initial && r.join_scene.Length > 0 && db.get_row("map_region", r.join_scene) != null);
        CheckNotNull(region_row, "表里有一位「按区域地标加入」的同伴（落雁坡那条路径靠它验）");
        if (region_row == null)
        {
            return;
        }

        string char_id = region_row.char_id;
        string node_id = region_row.join_scene;
        GodotObject state = SoloState(db);
        CheckFalse(_PendingRegionIds(db, state, node_id).Contains(char_id), "条件没点亮时地标旁没人等");
        _SatisfyCondition(state, region_row.join_condition);
        CheckTrue(_PendingRegionIds(db, state, node_id).Contains(char_id), $"点亮后 {char_id} 在 {node_id} 地标旁等着加入");
        CheckFalse(_PendingIds(db, state, "scene_heifengzhai").Contains(char_id), "区域型同伴不会因为进副本而加入");

        Godot.Collections.Array results = RecruitService.join_all_for_region(db, state, node_id);
        CheckEq(results.Count, 1, "走到地标旁就入队");
        CheckTrue(results[0].AsGodotDictionary()["ok"].AsBool(), "入队成功");
        CheckTrue(_PartyIds(state).Contains(char_id), "区域型同伴真的进了队伍");
        CheckTrue(state.Call("has_flag", $"flag_{char_id}_joined").AsBool(), $"入队点亮 flag_{char_id}_joined");
    }

    /// <summary>
    /// 条件语言的 `|`（任一满足）在**招募的加入条件**上也要真的生效（Q83 那条 `flag_a|flag_b`）。
    ///
    /// 夹具以前直接 `set_flag(join_condition, true)`：条件里一带 `|`／`&` 就置进去一个假旗标
    /// （名字叫 `flag_a|flag_b`），条件仍然不成立、人永远不入队，而用例看起来「已经把条件点亮了」。
    /// 现在夹具走 `_SatisfyCondition`（逐枚真旗标），这条用例把「真能入队」钉住。
    /// </summary>
    private void _CheckOrJoinCondition()
    {
        TableDb custom = FreshDb();
        var table = (TableResource)custom.tables["recruit_def"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        RecruitDefRow row = null;
        foreach (Variant entry in table.rows)
        {
            var candidate = (RecruitDefRow)entry.As<TableRow>().Duplicate(true);
            if (candidate.char_id == "ch_gang")
            {
                candidate.join_condition = "flag_q83_never|flag_luoyanpo_met";
                row = candidate;
            }

            rows.Add(candidate);
        }

        table.rows = rows;
        // 副本要放回去，否则查的还是原表（改了个没人看的副本）
        custom.tables["recruit_def"] = table;
        CheckNotNull(row, "副本里有林铁山那一行（落雁坡那条区域路径）");
        if (row == null)
        {
            return;
        }

        string node_id = row.join_scene;
        GodotObject state = SoloState(custom);
        CheckTrue(node_id == "n_luoyanpo", $"这条条件压在区域节点上（{node_id}）——`|` 要在这条路径上验");
        CheckFalse(_PendingRegionIds(custom, state, node_id).Contains("ch_gang"), "两枚旗标都没点亮时不入队");
        _SatisfyCondition(state, row.join_condition);
        CheckTrue(
            state.Call("has_flag", "flag_luoyanpo_met").AsBool(),
            "夹具点亮的是**真旗标**（不是把整串 a|b 当一个旗标名）");
        CheckTrue(
            _PendingRegionIds(custom, state, node_id).Contains("ch_gang"),
            "`|` 任一满足 → 他在落雁坡地标旁等着加入");
    }

    /// <summary>
    /// 区域招募的判定半径**钉死数值**（决策 154：「决定事件发不发」的距离要钉值）——
    /// 它决定「走到落雁坡算不算遇上林铁山」，而且与脚下地标高亮同值（两条半径一起改才不算漂）。
    /// </summary>
    private void _CheckRegionRadius()
    {
        CheckFloat(OverworldController.region_join_distance(), 70.0, "区域招募半径写死 70px（改了要红）", 0.0001);
        CheckEq(
            OverworldController.region_join_distance(),
            OverworldController.highlight_distance(),
            "与地标高亮同值（两条半径一起改才不算漂）");
    }

    /// <summary>
    /// 四条招募链的条件必须与设计表一致（设计 20 §十一），且**由战斗收尾点亮的那两枚旗标是同一串字符串**
    /// （改一处会红）。这一条是「断链」门限：`flag_luoyanpo_met`／`flag_poison_hall`／`flag_huangcun_done`
    /// 曾经一个来源都没有，那样除燕小七之外的三个同伴永远入不了队（Q51）。
    /// </summary>
    private void _CheckJoinConditionsAreReachable(TableDb db)
    {
        var expected = new System.Collections.Generic.Dictionary<string, string>
        {
            { "scholar_fallen", "flag_board_read" },
            { "ch_ci", "flag_board_read" },
            { "ch_gang", "flag_luoyanpo_met" },
            { "ch_du", "flag_poison_hall" },
            { "ch_qi", "flag_huangcun_done" },
        };
        foreach (var pair in expected)
        {
            var row = db.get_row("recruit_def", pair.Key) as RecruitDefRow;
            CheckNotNull(row, $"{pair.Key} 在 recruit_def 里");
            if (row == null)
            {
                continue;
            }

            CheckEq(row.join_condition, pair.Value, $"{pair.Key} 的加入条件");
        }

        var win_values = BattleScreen.team_win_flags().Values;
        CheckTrue(
            win_values.Contains("flag_huangcun_done"),
            "荒村的旗标由战斗收尾点亮（BattleScreen.team_win_flags()）");
        CheckTrue(
            win_values.Contains("flag_poison_hall"),
            "毒堂的旗标也由战斗收尾点亮");
    }

    /// <summary>
    /// 主角选**别人**时，初始行照样能按剧情入队（0.30.0）。
    /// 旧口径无条件跳过 `is_initial=1`，而书生是**唯一**一行初始成员——主角选别人时他永远入不了队。
    /// </summary>
    private void _CheckInitialMemberJoinsWhenNotProtagonist(TableDb db)
    {
        GodotObject state = NewGame(db, "normal", new[] { "ch_ci" });
        CheckFalse(_PartyIds(state).Contains("scholar_fallen"), "主角换成燕小七时书生不在开局队伍里");
        CheckFalse(
            _PendingIds(db, state, "scene_qingfengyi").Contains("scholar_fallen"),
            "条件没点亮时他还是不入队");
        state.Call("set_flag", "flag_board_read", true);
        CheckTrue(
            _PendingIds(db, state, "scene_qingfengyi").Contains("scholar_fallen"),
            "点亮 flag_board_read 后他能按剧情入队");
    }

    /// <summary>入队奖励（设计 20 §九）：燕小七那条 = 回气散 ＋ 好感 ＋20，结果里带给人看的短句。</summary>
    private void _CheckJoinRewards(TableDb db)
    {
        GodotObject state = SoloState(db);
        state.Call("set_flag", "flag_board_read", true);
        int qi_before = _InventoryCount(state, "item_potion_qi");
        Godot.Collections.Array joined = RecruitService.join_all_for_scene(db, state, "scene_qingfengyi");
        CheckEq(joined.Count, 1, "清风驿这一张图上只有一位待加入");
        Godot.Collections.Dictionary ci = joined[0].AsGodotDictionary();
        CheckTrue(ci["ok"].AsBool(), "燕小七真的入队了");
        CheckEq(_InventoryCount(state, "item_potion_qi"), qi_before + 1, "入队给了回气散（§九：「入队 ＋ 回气散」）");
        CheckEq(_FavorOf(db, state, "ch_ci"), 20, "入队好感 ＋20");
        bool reward_named = false;
        foreach (Variant reward in ci["rewards"].AsGodotArray())
        {
            reward_named = reward_named || reward.AsString() == "回气散 ×1";
        }
        CheckTrue(reward_named, "结果里带上了给人看的奖励短句：" + ci["rewards"]);

        // 林铁山：柴刀来自起始装备（不是这里发的），好感同样 ＋20
        GodotObject gang = SoloState(db);
        gang.Call("set_flag", "flag_luoyanpo_met", true);
        RecruitService.join_all_for_region(db, gang, "n_luoyanpo");
        CheckEq(_FavorOf(db, gang, "ch_gang"), 20, "林铁山入队也给 ＋20 好感");
        CheckTrue(_EquipmentCount(gang) > 0, "他的柴刀来自起始装备（入队就有装备在身）");
    }

    /// <summary>重复入队：第二次必须被拒，而且**不再发一份装备**（每次按 E 都会查一遍）。</summary>
    private void _CheckNoDoubleJoin(TableDb db)
    {
        var ci = (RecruitDefRow)db.get_row("recruit_def", "ch_ci");
        GodotObject state = SoloState(db);
        state.Call("set_flag", ci.join_condition, true);
        RecruitService.join_all_for_scene(db, state, "scene_qingfengyi");
        int equipment_after_first = _EquipmentCount(state);
        Godot.Collections.Dictionary again = RecruitService.join(db, state, "ch_ci");
        CheckFalse(again["ok"].AsBool(), "已经在队里时不重复加入");
        CheckEq(_EquipmentCount(state), equipment_after_first, "也不重复发装备");
        CheckEq(_PartyIds(state).Count, 2, "队伍人数没变（入队那次加的人还在）");
    }

    private static System.Collections.Generic.List<string> _PartyIds(GodotObject state) =>
        new(state.Get("char_ids").AsStringArray());

    private static RecruitDefRow _FirstRow(TableDb db, System.Func<RecruitDefRow, bool> predicate)
    {
        foreach (Variant entry in db.rows("recruit_def"))
        {
            var row = (RecruitDefRow)entry.AsGodotObject();
            if (predicate(row))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 这一行的 `join_scene` 对应哪张小地图：直接是 scene_id 就取它；是区域节点就找「父区域是它」的那张小地图
    /// （找不到返回空——调用方会 fail，而不是静默用错图）。
    /// </summary>
    private static string _SceneForRow(TableDb db, RecruitDefRow row)
    {
        string want = row.join_scene;
        if (db.get_row("map_local", want) != null)
        {
            return want;
        }

        foreach (Variant entry in db.rows("map_local"))
        {
            var local = (MapLocalRow)entry.AsGodotObject();
            if (local.parent_node == want)
            {
                return local.scene_id;
            }
        }

        return "";
    }

    /// <summary>
    /// 把条件串里该点亮的那几枚旗标**真点亮**（对位 GDScript 夹具 `tests/test_case.gd::satisfy_condition`）。
    /// `|`／`&` 都按「各自独立的旗标」处理——直接把整串 `a|b` 当一个旗标名置进去是假点亮。
    /// </summary>
    private static void _SatisfyCondition(GodotObject state, string condition)
    {
        foreach (string group in condition.Replace("|", "&").Split('&'))
        {
            string token = group.Trim();
            if (token.Length == 0 || token.StartsWith("!"))
            {
                continue;
            }

            if (token.StartsWith("flag_"))
            {
                state.Call("set_flag", token, true);
            }
        }
    }

    private static string[] _EquippedInstances(GodotObject state, TableDb db, string char_id) =>
        state.Get("inventory").AsGodotObject().Call("equipped_instances", db, char_id).AsStringArray();

    private static System.Collections.Generic.HashSet<string> _PendingIds(
        TableDb db, GodotObject state, string scene_id)
    {
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in RecruitService.pending_for_scene(db, state, scene_id))
        {
            ids.Add(((RecruitDefRow)entry.AsGodotObject()).char_id);
        }
        return ids;
    }

    private static System.Collections.Generic.HashSet<string> _PendingRegionIds(
        TableDb db, GodotObject state, string node_id)
    {
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in RecruitService.pending_for_region(db, state, node_id))
        {
            ids.Add(((RecruitDefRow)entry.AsGodotObject()).char_id);
        }
        return ids;
    }

    private static int _InventoryCount(GodotObject state, string item_id) =>
        state.Get("inventory").AsGodotObject().Call("count", item_id).AsInt32();

    /// <summary>装备实例在背包里存成 `equipment` 字典（实例 id → 条目），不是数组——按 `equipment_count()` 数。</summary>
    private static int _EquipmentCount(GodotObject state) =>
        state.Get("inventory").AsGodotObject().Call("equipment_count").AsInt32();

    private static int _FavorOf(TableDb db, GodotObject state, string npc_id) =>
        NpcService.favor_of(db, state, npc_id);
}
