using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 剧情节点与章节推进（C# 版）。
///
/// **2026-10-11（P4 第二十五批）起这份就是唯一一份**：旧链 `tests/test_story.gd` 已并入本文件后退场。
/// 那一批补进来的是当初被写成「还在 GDScript 里」的整合部分——备货／进寨两枚派生旗标、
/// 抉择增益穿过 `CharacterSheet` 真的进面板（悟性 +2／内力上限 +20）、剧情节点发武学的
/// 「门槛不够写明原因／资质到位真学会／只能领一次／不限地点的门派节点」、本命机遇那张表的形状。
/// </summary>
public partial class TestStory : TestCase
{
    private const string Heifengzhai = "scene_heifengzhai";
    private const string Qingfengyi = "scene_qingfengyi";

    public override string SuiteName() => "剧情节点与章节推进";

    public override void Run()
    {
        TableDb db = GetDb();
        _check_flags_and_title(db);
        _check_place_matching(db);
        _check_pending_and_claim(db);
        _check_choice_bonus(db);
        _check_chapter(db);
        _check_supplies_flag(db);
        _check_arrived_flag(db);
        _check_origin_gift(db);
    }

    /// <summary>完成旗标的命名与心性称号的条件串（两处都是「只此一份」的事实）。</summary>
    private void _check_flags_and_title(TableDb db)
    {
        CheckEq(StoryService.done_flag("opp_gang"), "story_done_opp_gang", "完成旗标的名字只在 done_flag 一处");
        CheckEq(StoryService.title_suffixes().Length, 3, "心性称号三个后缀");
        CheckEq(StoryService.title_suffixes()[0], "yi", "第一个是「义」");
        CheckEq(StoryService.title_suffixes()[2], "li", "第三个是「利」");
        CheckTrue(
            StoryService.title_condition_format().Contains("flag_open_%s"),
            "条件串里带后缀占位（面板按它逐条比）");
        CheckTrue(
            StoryService.title_condition_format().Contains("flag_ledger_public"),
            "条件串与幕结旁白同一条件（账册三选一任一）");

        GodotObject state = SoloState(db);
        CheckEq(StoryService.title_of(db, state), "", "没到章节末，称号整行不显示");
        CheckEq(StoryService.title_of(null, state), "", "没有表就没有称号（不报错、给空串）");
        state.Call("set_flag", "flag_open_yi", true);
        state.Call("set_flag", "flag_ledger_public", true);
        CheckEq(StoryService.title_of(db, state), "仗义", "条件满足后取 ui_text.title_yi 的名字");
    }

    /// <summary>地点匹配：留空＝不限地点，有值就得对得上（含「空 place_id 不算命中」这条翻车过的口径）。</summary>
    private void _check_place_matching(TableDb db)
    {
        var unlimited = db.get_row("story_node", "yaowang") as StoryNodeRow;
        CheckNotNull(unlimited, "药王谷那条在表里");
        CheckTrue(StoryService.place_matches(db, unlimited, ""), "place_id 留空 = 不限地点");
        CheckTrue(StoryService.place_matches(db, unlimited, "scene_anywhere"), "留空时任何地点都算命中");

        var bound = db.get_row("story_node", "chapter1_end") as StoryNodeRow;
        CheckNotNull(bound, "第一章结算那条在表里");
        CheckTrue(StoryService.place_matches(db, bound, "scene_qingfengyi"), "同一地点命中");
        CheckFalse(StoryService.place_matches(db, bound, "n_luoyanpo"), "别的地点不命中");
        CheckFalse(StoryService.place_matches(db, bound, ""), "有地点要求时，空 place_id 不算命中");
    }

    /// <summary>待领名单与领取：条件、地点、记账三件事，以及 chapter_end 顺带置完成条件。</summary>
    private void _check_pending_and_claim(TableDb db)
    {
        GodotObject scholar = SoloState(db);
        CheckEq(StoryService.pending_for(db, scholar, "scene_qingfengyi").Count, 0, "开局一条都领不到");
        scholar.Call("set_flag", "flag_board_read", true);
        string[] here = _node_ids(StoryService.pending_for(db, scholar, "scene_qingfengyi"));
        CheckEq(here.Length, 2, "看过告示后清风驿有两条（门派支线＋本命机遇）");
        CheckTrue(IndexOf(here, "opp_scholar") >= 0, $"书生的本命机遇领得到：{Join(here)}");
        CheckTrue(IndexOf(here, "xuanwei") >= 0, "门派支线也出来了");
        CheckFalse(IndexOf(here, "opp_qi") >= 0, "白清和那条轮不到他");

        // 林铁山那条在落雁坡（大地图节点）——地点不对就领不到
        GodotObject gang = NewGame(db, "normal", new[] { "ch_gang" });
        CheckFalse(
            IndexOf(_node_ids(StoryService.pending_for(db, gang, "scene_qingfengyi")), "opp_gang") >= 0,
            "林铁山在清风驿领不到");
        CheckTrue(
            IndexOf(_node_ids(StoryService.pending_for(db, gang, "n_luoyanpo")), "opp_gang") >= 0,
            "走到落雁坡就领得到");
        Godot.Collections.Array claimed = StoryService.claim_for(db, gang, "n_luoyanpo");
        CheckEq(claimed.Count, 1, "领了一条");
        Godot.Collections.Dictionary entry = claimed[0].AsGodotDictionary();
        CheckEq(entry["node_id"].AsString(), "opp_gang", "领的是那一条");
        CheckTrue(entry["granted"].AsGodotArray().Count >= 1, "granted 里带发放明细");
        CheckFalse(entry["chapter_done"].AsBool(), "本命机遇不是章末，不置完成条件");
        CheckTrue(gang.Call("has_flag", StoryService.done_flag("opp_gang")).AsBool(), "领过就记账");
        CheckEq(StoryService.pending_for(db, gang, "n_luoyanpo").Count, 0, "同一条不会第二次进待领名单");

        // chapter_end：领的同时置上本章的完成条件
        GodotObject ready = SoloState(db);
        ready.Call("set_flag", "flag_shen_rescued", true);
        Godot.Collections.Array closed = StoryService.claim_for(db, ready, "scene_qingfengyi");
        CheckEq(closed.Count, 1, "救出沈小姐后清风驿只有章末那一条");
        Godot.Collections.Dictionary closing = closed[0].AsGodotDictionary();
        CheckEq(closing["node_id"].AsString(), "chapter1_end", "领的是章末节点");
        CheckTrue(closing["chapter_done"].AsBool(), "章末节点标记了「本章完成」");
        CheckTrue(ready.Call("has_flag", "flag_chapter1_done").AsBool(), "顺带置上 chapter_def 的完成条件");

        // 章末发的是 3 部武学；1 级书生资质够不上门槛时，**必须在返回里写明为什么学不会**
        // （不静默），资质够了就该真的学会
        Godot.Collections.Array granted = closing["granted"].AsGodotArray();
        CheckEq(granted.Count, 3, "chapter1_end 发 3 部武学（长歌／破军式／太清）");
        var blocked_reasons = 0;
        foreach (Variant grant_variant in granted)
        {
            Godot.Collections.Dictionary grant = grant_variant.AsGodotDictionary();
            Godot.Collections.Array learned = grant["learned"].AsGodotArray();
            Godot.Collections.Array blocked = grant["blocked"].AsGodotArray();
            CheckTrue(
                learned.Count + blocked.Count >= 1,
                $"{grant["skill_id"].AsString()}：要么学会、要么写明门槛不够");
            if (blocked.Count > 0)
            {
                blocked_reasons += 1;
                Godot.Collections.Dictionary first = blocked[0].AsGodotDictionary();
                CheckTrue(
                    first.ContainsKey("reason") && first["reason"].AsString().Length > 0,
                    $"被门槛挡住时带原因：{_Text(first, "reason")}");
            }
        }

        CheckGt(blocked_reasons, 0, "1 级书生确实够不上这几部的门槛（门槛真的在判）");

        // 资质够了就该真的学会（换一份「资质到位」的存档，验证 happy path）
        GodotObject talent_ready = SoloState(db);
        string ready_id = talent_ready.Get("char_ids").AsStringArray()[0];
        talent_ready.Get("char_allocations").AsGodotDictionary()[ready_id] =
            new Godot.Collections.Dictionary { { "wu", 20 }, { "gen", 25 }, { "str", 12 } };
        talent_ready.Call("set_flag", "flag_shen_rescued", true);
        Godot.Collections.Array claimed_ready = StoryService.claim_for(db, talent_ready, Qingfengyi);
        SkillLoadout loadout = SkillLoadout.create(db, talent_ready, ready_id);
        bool all_learned = claimed_ready.Count == 1;
        foreach (string skill_id in new[] { "sk_xuanwei_08", "sk_common_02", "pf_xuanwei_05" })
        {
            if (!loadout.is_learned(skill_id))
            {
                all_learned = false;
            }
        }

        CheckTrue(all_learned, "资质到位后这 3 部真的学会（章节奖励不是空头支票）");
        // 一次性：再领一次什么都没有
        CheckEq(StoryService.claim_for(db, ready, Qingfengyi).Count, 0, "剧情节点只能领一次");

        // 「不限地点」的门派节点（药王谷那条）：站在哪儿都能领
        GodotObject anywhere_state = SoloState(db);
        anywhere_state.Call("set_flag", "flag_poison_hall", true);
        Godot.Collections.Array anywhere = StoryService.claim_for(db, anywhere_state, "");
        CheckEq(anywhere.Count, 1, "place_id 留空的节点在任何地方都能领（药王谷）");
        CheckEq(
            anywhere[0].AsGodotDictionary()["granted"].AsGodotArray().Count, 1,
            "药王谷发 1 部（续命诀）");
    }

    /// <summary>抉择的永久增益与面板文案：跟旗标走、只发主角。</summary>
    private void _check_choice_bonus(TableDb db)
    {
        // ① 表里三条抉择都真的带增益（配成空节点会被构建期挡，但"三行都在"是设计状态，值得钉）
        var choices = new System.Collections.Generic.List<StoryNodeRow>();
        foreach (Variant entry in db.rows("story_node"))
        {
            var candidate = entry.AsGodotObject() as StoryNodeRow;
            if (candidate != null && candidate.kind == "choice")
            {
                choices.Add(candidate);
                CheckFalse(candidate.bonus_contributions().Count == 0, $"{candidate.node_id} 带着永久增益");
            }
        }

        CheckEq(choices.Count, 3, "账册三选一 = 3 条 kind=choice 节点");

        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CheckEq(StoryService.permanent_contributions(db, state, char_id).Count, 0, "没做过抉择 → 没有永久增益");
        CheckEq(StoryService.bonus_labels(db, state, char_id).Length, 0, "也没文案");

        // ② 旗标没点亮时一点加成都没有；③ 点亮后属性点层与固定值层都进面板
        var public_row = choices.Find(row => row.node_id == "ledger_public");
        CheckNotNull(public_row, "找得到「清名」那条（呈官）");
        if (public_row != null)
        {
            CharacterSheet before = CharacterSheet.create(db, state, char_id);
            int wu_before = _int(before.naked_attrs(), "wu");
            double qi_before = _Number(before.stats(), "qi_max");
            CheckFalse(
                state.Call("has_flag", public_row.trigger_condition).AsBool(),
                "开局还没做过这个抉择");

            state.Call("set_flag", public_row.trigger_condition, true);
            CharacterSheet after = CharacterSheet.create(db, state, char_id);
            CheckEq(
                _int(after.naked_attrs(), "wu"), wu_before + 2,
                "「清名」给悟性 +2（属性点层，进判定与门槛）");
            CheckFloat(
                _Number(after.stats(), "qi_max"), qi_before + 20.0,
                "「清名」给内力上限 +20（固定值层）", 0.01);
        }

        state.Call("set_flag", "flag_ledger_public", true);
        Godot.Collections.Array contribs = StoryService.permanent_contributions(db, state, char_id);
        CheckEq(contribs.Count, 2, "「清名」给属性点层＋固定值层各一条");
        CheckEq(contribs[0].AsGodotDictionary()["source"].AsString(), "story_node:ledger_public", "贡献的来源能追溯到那一条");
        string[] labels = StoryService.bonus_labels(db, state, char_id);
        CheckEq(labels.Length, 2, "面板文案两条");
        CheckTrue(IndexOf(labels, "悟性 +2") >= 0, $"文案里写出「悟性 +2」：{Join(labels)}");
        CheckTrue(IndexOf(labels, "内力上限 +20") >= 0, $"文案里写出「内力上限 +20」：{Join(labels)}");
        CheckEq(StoryService.permanent_contributions(db, state, "ch_gang").Count, 0, "同一个抉择问别人是空的（只发主角）");
        CheckEq(StoryService.bonus_labels(db, state, "").Length, 0, "空 char_id 直接给空");
    }

    /// <summary>章节推进：开局章、认不出的 id 退回开局章、一次只推一格、最后一章停在原地。</summary>
    private void _check_chapter(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckEq(state.Get("chapter_id").AsString(), "chapter_01", "开局是第一章");
        CheckEq(ChapterService.starting_row(db).chapter_id, "chapter_01", "开局章是 entry_condition=start 那条");
        CheckEq(ChapterService.row_of(db, state).chapter_id, "chapter_01", "认得出存档里的章节 id");
        CheckEq(ChapterService.label_of(db, state), "第一章·黑风寨", "章节名从 chapter_def.name_cn 取（不给玩家看 id）");
        CheckNull(ChapterService.row_of(null, state), "没有表就没有章节行");
        CheckEq(ChapterService.label_of(null, state), "", "没有表就不显示章节名");

        state.Set("chapter_id", "chapter_bogus");
        CheckEq(ChapterService.label_of(db, state), "第一章·黑风寨", "存档里的 id 认不出来就退回开局章");
        state.Set("chapter_id", "chapter_01");
        CheckFalse(ChapterService.try_advance(db, state)["advanced"].AsBool(), "还没完成 → 不推进");

        state.Call("set_flag", "flag_chapter1_done", true);
        Godot.Collections.Dictionary advanced = ChapterService.try_advance(db, state);
        CheckTrue(advanced["advanced"].AsBool(), "完成条件满足 → 推进");
        CheckEq(advanced["from"].AsString(), "chapter_01", "从第一章");
        CheckEq(advanced["to"].AsString(), "chapter_02", "推进到第二章");
        CheckEq(state.Get("chapter_id").AsString(), "chapter_02", "新的章节 id 写回存档");
        CheckEq(advanced["name"].AsString(), "第二章（待定）", "播报用表里的名字");
        CheckEq(advanced["text"].AsString(), "第一章告一段落", "完成文案取自 chapter_def.complete_text_cn");
        CheckFalse(ChapterService.try_advance(db, state)["advanced"].AsBool(), "最后一章停在原地（不越界）");
    }

    /// <summary>备货旗标（设计 18 断点 1）：判据是「等级 ≥ 5 且背包里有回血道具」。</summary>
    private void _check_supplies_flag(TableDb db)
    {
        GodotObject state = SoloState(db);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CheckFalse(state.Call("has_flag", GuideService.supplies_flag()).AsBool(), "开局没备货");
        CheckFalse(GuideService.supplies_ready(db, state), "1 级 + 空背包 = 没备货");

        // 只有等级、没有消耗品 → 还不算
        state.Get("char_levels").AsGodotDictionary()[char_id] = 5;
        GuideService.refresh_derived_flags(db, state);
        CheckFalse(state.Call("has_flag", GuideService.supplies_flag()).AsBool(), "光到 5 级、背包空的还是没备货");

        // 有消耗品了 → 旗帜点亮
        state.Get("inventory").As<Inventory>().add_item(db, "item_med_01", 1);
        string[] set_now = GuideService.refresh_derived_flags(db, state);
        CheckTrue(state.Call("has_flag", GuideService.supplies_flag()).AsBool(), "5 级 + 带药 → 备货完成");
        CheckTrue(IndexOf(set_now, GuideService.supplies_flag()) >= 0, "返回值告诉调用方刚点亮了哪一枚");
        // 幂等：再算一次不该重复播报
        CheckTrue(GuideService.refresh_derived_flags(db, state).Length == 0, "已经点亮的不再重复报");
        // 引导 HUD 跟着走到第 4 步（分母按表算，别写死）
        state.Call("set_flag", "flag_board_read", true);
        state.Call("set_flag", "flag_ch_ci_joined", true);
        string want = $"4/{GuideService.steps(db).Count}";
        CheckTrue(
            GuideService.hud_text(db, state).Contains(want),
            $"备货完成后引导走到第 4 步：{GuideService.hud_text(db, state)}");
    }

    /// <summary>进寨旗标（设计 18 断点 2）：判据是入口房间，不是「进了这张图」。</summary>
    private void _check_arrived_flag(TableDb db)
    {
        GodotObject state = SoloState(db);
        string entrance = GuideService.entrance_room_of(db, Heifengzhai);
        CheckTrue(entrance.Length > 0, "黑风寨配了入口房间（room_type=entrance）");
        // 后山地牢（隐藏层的房间）不算进寨
        GuideService.note_room_entered(db, state, Heifengzhai, "hf3_dungeon");
        CheckFalse(
            state.Call("has_flag", GuideService.arrived_heifengzhai_flag()).AsBool(),
            "从后山地牢进来不算进寨");
        // 别的图的入口房间也不算
        GuideService.note_room_entered(db, state, Qingfengyi, entrance);
        CheckFalse(
            state.Call("has_flag", GuideService.arrived_heifengzhai_flag()).AsBool(),
            "别张图的房间不点亮进寨旗标");
        // 真正的入口房间才算
        string[] set_now = GuideService.note_room_entered(db, state, Heifengzhai, entrance);
        CheckTrue(
            state.Call("has_flag", GuideService.arrived_heifengzhai_flag()).AsBool(),
            "走进寨门 → 进寨");
        CheckTrue(
            IndexOf(set_now, GuideService.arrived_heifengzhai_flag()) >= 0,
            "返回值告诉调用方点亮了它");
    }

    /// <summary>
    /// 本命机遇（设计 21 §九／0.31.0）：**按出身给**的那部 ★4 内功。
    ///
    /// 三条口径各钉一条：① 表里 5 条 `origin_gift`，每条发一部内功；② 条件是 `origin:&lt;char_id&gt;`
    /// （＋该条需要的旗标），所以只有那个出身的主角领得到；③ 领过就记账、不会重复发。
    /// **「真的学会了」不在这里验**——1 级主角够不到 18 的门槛，那条口径在 `test_overworld` 真场景用例里。
    /// </summary>
    private void _check_origin_gift(TableDb db)
    {
        var nodes = new System.Collections.Generic.List<StoryNodeRow>();
        foreach (Variant entry in db.rows("story_node"))
        {
            var candidate = entry.AsGodotObject() as StoryNodeRow;
            if (candidate != null && candidate.kind == "origin_gift")
            {
                nodes.Add(candidate);
            }
        }

        CheckEq(nodes.Count, 5, "五条本命机遇（五个出身各一条）");
        foreach (StoryNodeRow row in nodes)
        {
            CheckFalse(row.grant_skill_ids.Length == 0, $"{row.node_id} 发了东西");
            CheckTrue(row.trigger_condition.Contains("origin:"), $"{row.node_id} 的条件里写出身");
            var skill = db.get_row("skill_base", row.grant_skill_ids.Split(';')[0]) as SkillBaseRow;
            CheckNotNull(skill, $"{row.node_id} 发的内功在表里（{row.grant_skill_ids}）");
            if (skill == null)
            {
                continue;
            }

            CheckEq(skill.source_type, "origin", $"{skill.skill_id} 的来源类型是 origin");
            CheckEq(skill.star, 4, $"{skill.skill_id} 是 ★4");
            CheckEq(skill.learn_req_value, 18, $"{skill.skill_id} 的门槛值 18");
            CheckFalse(skill.learn_req_attr.Length == 0, $"{skill.skill_id} 写了门槛属性");
        }

        // 书生当主角：看过告示后，在清风驿领得到自己那条
        GodotObject scholar = NewGame(db, "normal", new[] { "scholar_fallen" });
        scholar.Call("set_flag", "flag_board_read", true);
        string[] scholar_here = _node_ids(StoryService.pending_for(db, scholar, Qingfengyi));
        CheckTrue(
            IndexOf(scholar_here, "opp_scholar") >= 0,
            $"书生在清风驿领得到自己的那条（{Join(scholar_here)}）");
        CheckFalse(IndexOf(scholar_here, "opp_qi") >= 0, "白清和那条轮不到他（复命后、且出身不对）");

        // 林铁山当主角：他那条在落雁坡（大地图节点，没有小地图）
        GodotObject gang = NewGame(db, "normal", new[] { "ch_gang" });
        CheckFalse(
            IndexOf(_node_ids(StoryService.pending_for(db, gang, Qingfengyi)), "opp_gang") >= 0,
            "林铁山在清风驿领不到（他的在落雁坡）");
        CheckTrue(
            IndexOf(_node_ids(StoryService.pending_for(db, gang, "n_luoyanpo")), "opp_gang") >= 0,
            "走到落雁坡就领得到");

        // 这一步只验「发的是哪一部」（`granted` 里连被门槛挡下的条目也返回）
        Godot.Collections.Array claimed = StoryService.claim_for(db, gang, "n_luoyanpo");
        var granted_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in claimed)
        {
            foreach (Variant item_variant in entry_variant.AsGodotDictionary()["granted"].AsGodotArray())
            {
                granted_ids.Add(_Text(item_variant.AsGodotDictionary(), "skill_id"));
            }
        }

        CheckTrue(
            granted_ids.Contains("pf_chensha_buhuan"),
            $"领到的是《沉沙心法·不还》（{Join(granted_ids.ToArray())}）");
        CheckTrue(
            gang.Call("has_flag", StoryService.done_flag("opp_gang")).AsBool(),
            "领过就记账（不会重复发）");
        CheckTrue(
            StoryService.pending_for(db, gang, "n_luoyanpo").Count == 0,
            "同一条不会第二次进待领名单");
    }

    private static string[] _node_ids(Godot.Collections.Array rows)
    {
        var out_ids = new string[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            out_ids[index] = (rows[index].AsGodotObject() as StoryNodeRow)?.node_id ?? "";
        }

        return out_ids;
    }

    private static int IndexOf(string[] values, string wanted) => System.Array.IndexOf(values, wanted);

    private static string Join(string[] values) => string.Join("／", values);

    private static int _int(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static double _Number(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    private static string _Text(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";
}
