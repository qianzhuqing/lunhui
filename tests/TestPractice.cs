using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 城镇木桩练习战（设计 09 §3.3）：收益封顶、到顶给提示、数据没到就说清楚。
///
/// 木桩的 `enemy_team`／`enemy_base` 两行**设计侧还没给**（Q52），所以分两层验：
///   ① 服务层：封顶规则与提示全部读表（不依赖那两行）；`data_ready()` 如实报告缺什么；
///   ② 夹具层：临时往**内存副本**里补两行，验证「组出来的遭遇是练习战 + 木桩不还手」——不碰策划的 CSV。
///
/// **2026-10-11（P4 第二十批）起这份就是唯一一份**：旧链的 `tests/test_practice.gd` 已并入本文件后退场。
/// 并进来的是 ③ `_check_mastery_cap`（练习战里熟练度**只压涨、不压低**）、
/// ④ `_check_fixture_encounter`、⑤ `_check_dummy_never_attacks` 三节 ＋ `_with_dummy_rows`
/// 内存夹具（那时它们要 `EnemyFactory`／`PartyBuilder`／`BattleSimulator`，还没搬过来）。
/// C# 侧行对象直接 `new` 就是干净的一份，省掉旧链那个「先复制首行、再按类型逐字段清空」的 `_blank_row`。
/// </summary>
public partial class TestPractice : TestCase
{
    public override string SuiteName() => "木桩练习战";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckEntryDetection(db);
        _CheckCaps(db);
        _CheckNoticeAndReadiness(db);
        _CheckMasteryCap(db);
        _CheckFixtureEncounter(db);
        _CheckDummyNeverAttacks(db);
        _CheckBuildEncounter(db);
    }

    /// <summary>入口识别：`building_def.service_id == dummy_training` 的房子就是木桩，别的不是。</summary>
    private void _CheckEntryDetection(TableDb db)
    {
        CheckEq(PracticeService.service_id(), "dummy_training", "木桩的 service_id 写死为 dummy_training");
        CheckEq(PracticeService.team_id(), "team_dummy_training", "木桩队伍的 id 是代码侧约定的那一个");
        CheckEq(PracticeService.enemy_id(), "en_dummy_training", "木桩敌人的 id 是代码侧约定的那一个");

        string dummy_id = "";
        foreach (Variant entry in db.rows("building_def"))
        {
            var row = entry.AsGodotObject() as BuildingDefRow;
            if (row != null && row.service_id == PracticeService.service_id())
            {
                dummy_id = row.building_id;
            }
        }
        CheckEq(dummy_id, "bld_dummy", "表里的木桩建筑是 bld_dummy");
        CheckTrue(PracticeService.is_practice_building(db, "bld_dummy"), "认得木桩");
        CheckFalse(PracticeService.is_practice_building(db, "bld_grocery"), "杂货铺不是木桩");
        CheckFalse(PracticeService.is_practice_building(db, "不存在的建筑"), "不存在的建筑不冒充木桩");
        CheckFalse(PracticeService.is_practice_building(null, "bld_dummy"), "没有表库时不冒充木桩");
        CheckEq(
            db.get_row("building_def", "bld_dummy").Get("service_price").AsDouble(), 0.0,
            "木桩不收钱（表里 service_price=0）");
    }

    /// <summary>封顶规则：等级上限读表、到顶就不给经验、熟练度上限读表。</summary>
    private void _CheckCaps(TableDb db)
    {
        CheckEq(PracticeService.cap_level(db), 5, "经验上限读 growth_const.dummy_xp_cap_level（5 级）");
        CheckEq(PracticeService.mastery_cap(db), 3, "熟练度上限读 growth_const.dummy_mastery_cap（3 级）");

        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CheckEq(state.Call("level_of", char_id).AsInt32(), 1, "新档 1 级");
        CheckTrue(PracticeService.xp_allowed(db, state), "1 级还能练");
        CheckEq(PracticeService.capped_exp(db, state, 30), 30, "没到顶：经验原样给");

        SetLevel(state, char_id, 4);
        CheckTrue(PracticeService.xp_allowed(db, state), "4 级还能练（上限是 5）");
        SetLevel(state, char_id, 5);
        CheckFalse(PracticeService.xp_allowed(db, state), "到 5 级就练不出东西了");
        CheckEq(PracticeService.capped_exp(db, state, 30), 0, "到顶后经验给 0");
        CheckEq(PracticeService.capped_exp(db, state, -5), 0, "负数当 0（不给负经验）");

        // Q54（0.14.0）：**单场最多补到上限门槛，超出丢弃**——1 级到 5 级要 100+240+420+640 = 1400
        SetLevel(state, char_id, 1);
        state.Set("party_exp", 0);
        CheckEq(PracticeService.exp_room(db, state), 1400, "1 级到 5 级还差 1400 经验");
        CheckEq(PracticeService.capped_exp(db, state, 9999), 1400, "一次给再多也只补到门槛（超出丢弃）");
        state.Set("party_exp", 1300);
        CheckEq(PracticeService.capped_exp(db, state, 9999), 100, "池里已有 1300 → 这一场最多再给 100");
        state.Set("party_exp", 1400);
        CheckEq(PracticeService.capped_exp(db, state, 9999), 0, "池子已经够送到门槛 → 这一场给 0");
        state.Set("party_exp", 0);

        // 队伍里只要还有一个人没过上限，就还能练（经验是队伍池，按「最低的那个人」算）
        GodotObject party = NewGame(db, "normal", new[] { "scholar_fallen", "ch_gang", "ch_du" });
        CheckEq(party.Get("char_ids").AsStringArray().Length, 3, "三个人都在队里");
        CheckEq(
            PracticeService.exp_room(db, party), 4200,
            "三个人都要送到上限 → 3 × 1400");
        string[] ids = party.Get("char_ids").AsStringArray();
        SetLevel(party, ids[0], 5);
        SetLevel(party, ids[1], 5);
        SetLevel(party, ids[2], 3);
        CheckEq(
            PracticeService.exp_room(db, party), 1060,
            "只剩第三个人（3 级）要补到 5 级 → 420+640");
        CheckTrue(PracticeService.xp_allowed(db, party), "队里还有人没过上限 → 还能练");
        SetLevel(party, ids[2], 5);
        CheckFalse(PracticeService.xp_allowed(db, party), "全员到顶 → 不再给经验");
        CheckEq(PracticeService.capped_exp(db, party, 9999), 0, "全员到顶 → 这一场给 0");
    }

    /// <summary>到顶提示与「数据齐了没有」：都要读表／如实报告，不能自己编。</summary>
    private void _CheckNoticeAndReadiness(TableDb db)
    {
        string notice = PracticeService.cap_notice(db);
        GodotObject row = db.get_row("ui_text", "dummy_cap_reached");
        CheckNotNull(row, "ui_text 里有 dummy_cap_reached");
        CheckEq(notice, row.Get("text_cn").AsString(), $"到顶提示取表里那句原文：{notice}");
        CheckTrue(notice.Contains("上山"), $"提示里指了下一步（上山）：{notice}");

        Godot.Collections.Dictionary ready = PracticeService.data_ready(db);
        // 这一条**不写死「现在缺」**：设计补上那两行之后它自然变成 ok=true，
        // 这里只钉「报告自洽」——缺就说缺什么，齐了就 error 为空。
        CheckEq(
            ready["ok"].AsBool(), ready["error"].AsString().Length == 0,
            "data_ready 的 ok 与 error 自洽");
        CheckEq(PracticeService.data_ready(null)["ok"].AsBool(), false, "没有表库 → 不 ok");
    }

    /// <summary>
    /// 熟练度封顶：练习战里只涨到 `dummy_mastery_cap`，而且**只压涨、不压低**——
    /// 已经练到 5 级的招式打木桩不该掉回 3 级（那等于打木桩惩罚玩家）。
    /// </summary>
    private void _CheckMasteryCap(TableDb db)
    {
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        MasteryService mastery = MasteryService.create(db, state);

        state.Call("set_mastery", char_id, "sk_xuanwei_01", 0, 10);
        Godot.Collections.Array gained = mastery.apply_combat_usage(
            char_id,
            new Godot.Collections.Dictionary { { "sk_xuanwei_01", 5 } },
            PracticeService.mastery_cap(db));
        CheckEq(
            state.Call("mastery_of", char_id, "sk_xuanwei_01").AsInt32(), 3,
            "5 次施放只涨到上限 3（不是 5）");
        CheckFalse(gained.Count == 0, "涨了就要有回执（结算面板要写出来）");

        state.Call("set_mastery", char_id, "sk_xuanwei_02", 5, 10);
        mastery.apply_combat_usage(
            char_id,
            new Godot.Collections.Dictionary { { "sk_xuanwei_02", 5 } },
            PracticeService.mastery_cap(db));
        CheckEq(
            state.Call("mastery_of", char_id, "sk_xuanwei_02").AsInt32(), 5,
            "已经 5 级的招式打木桩不掉级");

        // 不传上限（0）时照旧按 mastery_max（10）——别的战斗不受影响
        state.Call("set_mastery", char_id, "sk_xuanwei_03", 0, 10);
        mastery.apply_combat_usage(
            char_id,
            new Godot.Collections.Dictionary { { "sk_xuanwei_03", 2 } },
            0);
        CheckEq(
            state.Call("mastery_of", char_id, "sk_xuanwei_03").AsInt32(), 2,
            "不传上限时按普通战斗的规则涨（每次 +1）");
    }

    /// <summary>夹具层：往内存副本里补两支行（木桩队伍 + 木桩敌人）→ 组出来的遭遇是练习战。</summary>
    private void _CheckFixtureEncounter(TableDb db)
    {
        TableDb local = _WithDummyRows(db);
        CheckNotNull(
            local.get_row("enemy_team", PracticeService.team_id()), "夹具里补上了木桩队伍");

        Godot.Collections.Dictionary ready = PracticeService.data_ready(local);
        CheckTrue(
            ready["ok"].AsBool(), $"补完两行之后数据就齐了：{ready["error"].AsString()}");

        Encounter encounter = PracticeService.build_encounter(local, "scene_qingfengyi", "normal");
        CheckNotNull(encounter, "能组出练习战遭遇");
        if (encounter == null)
        {
            return;
        }

        CheckTrue(encounter.practice, "打成练习战标记（结算按它封顶）");
        CheckEq(encounter.team_id, PracticeService.team_id(), "用的是木桩队伍");
        CheckEq(encounter.source_key, "practice", "source_key 不占房间名（不进副本完成度）");
        CheckEq(encounter.source_scene, "scene_qingfengyi", "source_scene 保留（打完回这张图）");
    }

    /// <summary>
    /// 木桩**不还手**：它没有招式（`ai_neutral` 且 `enemy_skill` 里一条都没有）→ 模拟器跳过它的回合。
    /// </summary>
    private void _CheckDummyNeverAttacks(TableDb db)
    {
        TableDb local = _WithDummyRows(db);
        GodotObject state = SoloState(local);
        Godot.Collections.Array allies =
            PartyBuilder.build_actors(local, state, new Godot.Collections.Array());
        Godot.Collections.Array enemies =
            EnemyFactory.create(local).create_team(PracticeService.team_id(), "normal");
        CheckEq(enemies.Count, 1, "木桩就一个目标");
        if (enemies.Count == 0)
        {
            return;
        }

        var dummy = (BattleActor)enemies[0].AsGodotObject();
        CheckTrue(dummy.skills.Length == 0, "木桩没有招式（不还手）");

        var ally = (BattleActor)allies[0].AsGodotObject();
        int hp_before = ally.hp;
        BattleSimulator sim = BattleSimulator.create(local, RngService.create(20261003));
        sim.setup(
            allies,
            enemies,
            new Godot.Collections.Dictionary
            {
                { "modifiers", new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } } },
            });
        Godot.Collections.Array lines = sim.step_round();
        CheckEq(ally.hp, hp_before, "打过一回合，我方一点血都没掉（木桩不还手）");
        CheckTrue(
            lines.ToString().Contains("无可用招式"), $"战报里写明木桩没有可用招式：{lines}");
    }

    /// <summary>
    /// 组出来的遭遇必须是「练习战」（结算按这个标记封顶），且 `source_key` 不占房间名——
    /// 木桩战不是副本进度。队伍行缺不缺**不影响**这里：缺的时候 `team_name` 退回队伍 id。
    /// </summary>
    private void _CheckBuildEncounter(TableDb db)
    {
        Encounter encounter = PracticeService.build_encounter(db, "scene_qingfengyi", "normal");
        CheckNotNull(encounter, "能组出练习战遭遇");
        CheckTrue(encounter.practice, "打成练习战标记（结算按它封顶）");
        CheckEq(encounter.team_id, PracticeService.team_id(), "用的是木桩队伍");
        CheckEq(encounter.source_key, "practice", "source_key 不占房间名（不进副本完成度）");
        CheckEq(encounter.source_scene, "scene_qingfengyi", "source_scene 保留（打完回这张图）");
        CheckEq(encounter.spawn_id, "practice", "spawn_id 固定为 practice");
        CheckEq(encounter.first_side, -1, "练习战按身法排序（不强制先手）");
        CheckEq(encounter.pending_rules.Length, 0, "练习战没有暂缓规则");
        CheckEq(encounter.difficulty_id, "normal", "难度照传进来的值");
    }

    /// <summary>`state.char_levels[char_id] = level`——GameState 还是 GDScript，走 Get/Set（字典是引用类型，就地改）。</summary>
    private static void SetLevel(GodotObject state, string char_id, int level)
    {
        Godot.Collections.Dictionary levels = state.Get("char_levels").AsGodotDictionary();
        levels[char_id] = level;
    }

    // ------------------------------------------------------------------ 夹具

    /// <summary>
    /// 往**内存副本**里补木桩那两行（enemy_team + enemy_base）。只改内存，绝不碰 CSV。
    /// 数值只是夹具用（真值等设计给，Q52）：木桩 40 血、经验 10、不给招式。
    ///
    /// **必须一起设 `id`**：表容器按 `row.id`（= 主键值）建索引，只改 `team_id` 会查不到这一行
    /// （而且会顺手把原来那行的索引覆盖掉——旧链夹具踩过一次）。行对象直接 `new` 就是干净的一份，
    /// 不必像旧链那样复制首行再逐字段清空。
    /// </summary>
    private TableDb _WithDummyRows(TableDb db)
    {
        TableDb local = FreshDb();

        TableResource team_table =
            (TableResource)local.tables["enemy_team"].As<TableResource>().Duplicate(true);
        var team_row = new EnemyTeamRow
        {
            id = PracticeService.team_id(),
            team_id = PracticeService.team_id(),
            name_cn = "木桩（夹具）",
            members = $"{PracticeService.enemy_id()}:1",
            threat_tag = "green",
            team_buff = "",
            note = "夹具：只存在于内存里",
        };
        team_table.rows.Add(team_row);
        team_table.index[team_row.id] = team_table.rows.Count - 1;
        local.tables["enemy_team"] = team_table;

        TableResource enemy_table =
            (TableResource)local.tables["enemy_base"].As<TableResource>().Duplicate(true);
        var enemy_row = new EnemyBaseRow
        {
            id = PracticeService.enemy_id(),
            enemy_id = PracticeService.enemy_id(),
            name_cn = "木桩（夹具）",
            faction = "neutral",
            level = 1,
            hp_base = 40,
            atk_phys = 0,
            atk_qi = 0,
            def_phys = 0,
            def_qi = 0,
            speed = 1,
            hit_rate = 0.0,
            dodge_rate = 0.0,
            crit_rate = 0.0,
            poise = 10,
            res_internal = 0.0,
            drop_group = "",
            exp_reward = 10,
            money = 0,
            threat_tag = "green",
            ai_template = "ai_neutral",
            desc = "夹具：不还手的木桩",
        };
        enemy_table.rows.Add(enemy_row);
        enemy_table.index[enemy_row.id] = enemy_table.rows.Count - 1;
        local.tables["enemy_base"] = enemy_table;
        return local;
    }
}
