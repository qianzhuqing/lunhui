using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 「人」那一侧的口径（NPC 与同伴）：`npc_def.place_id` **既认小地图也认大地图节点**、
/// 同伴没有 `npc_def` 行所以 `person_of` 按同一形状**合成一行**、好感行里赠送／切磋／偷窃三个数，
/// 以及「委托人之外的额外好感」（`npc_quest.reward_favor` 只能加委托人自己）。
///
/// 对位 `tests/test_dialogue.gd` 的三节（2026-10-11 P4 第三十九批整体搬过来）。
/// 对话容器与逐条台词那半边在 `tests/TestDialogue.cs` —— 两份各注册一次，
/// 断言的账分别落在各自那份上。
/// </summary>
public partial class TestDialogueNpc : TestCase
{
    public override string SuiteName() => "人物口径（NPC 与同伴）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckRegionNpcs(db);
        _CheckCompanionPerson(db);
        _CheckQuestExtraFavor(db);
    }

    /// <summary>
    /// 地点口径（设计 19）：落雁坡没有自己的小地图，采药人老周挂在大地图的区域节点上。
    ///
    /// 这条以前只在小地图那一侧接（大地图上按 E 什么也不发生），现在两边都认；
    /// 顺便钉住「地点口径不串台」——老周不该在清风驿冒出来。
    /// </summary>
    private void _CheckRegionNpcs(TableDb db)
    {
        var slope_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in NpcService.npcs_at(db, "n_luoyanpo"))
        {
            slope_ids.Add(((NpcDefRow)entry.AsGodotObject()).npc_id);
        }

        CheckTrue(
            slope_ids.Contains("npc_caiyao"),
            $"大地图区域节点上有老周（{string.Join(", ", slope_ids)}）");

        var town_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in NpcService.npcs_at(db, "scene_qingfengyi"))
        {
            town_ids.Add(((NpcDefRow)entry.AsGodotObject()).npc_id);
        }

        CheckTrue(town_ids.Contains("npc_wang_tie"), $"清风驿上有王铁（{string.Join(", ", town_ids)}）");
        CheckFalse(town_ids.Contains("npc_caiyao"), "老周不在清风驿（地点口径不串台）");
    }

    /// <summary>
    /// 同伴也是「人」（设计 20 §十：同伴与城镇 NPC 共用一套好感规则）：他们没有 `npc_def` 行，
    /// 所以 `person_of()` 按同一形状**合成一行**——名字／称号从 `character_base`、所在地从
    /// `recruit_def`。合成行不进表，谁也改不动它。
    ///
    /// 好感行里那三个数一起钉：赠送 5、**可切磋**（2026-10-04 Q88 改判，走「按同伴当前配装
    /// 镜像成一支队伍」，不再各造一行 `enemy_base`）、偷窃难度 0（设计明写不可偷）。
    /// </summary>
    private void _CheckCompanionPerson(TableDb db)
    {
        GodotObject state = SoloState(db);
        const string char_id = "ch_ci";
        CheckTrue(NpcService.def_of(db, char_id) == null, "同伴确实没有 npc_def 行");
        var person = (NpcDefRow)NpcService.person_of(db, state, char_id);
        CheckNotNull(person, "但 person_of 认得出他");
        if (person == null)
        {
            return;
        }

        var char_row = (CharacterBaseRow)db.get_row("character_base", char_id);
        CheckEq(person.name_cn, char_row.name_cn, "名字来自 character_base");
        CheckEq(person.title_cn, char_row.role_tag, "称号用他的路线标签");
        CheckEq(
            person.place_id,
            ((RecruitDefRow)db.get_row("recruit_def", char_id)).join_scene,
            "所在地来自 recruit_def.join_scene");

        // NPC 那一侧不受影响：真表行还是原样返回
        var npc = (NpcDefRow)NpcService.person_of(db, state, "npc_wang_tie");
        CheckEq(npc.npc_id, "npc_wang_tie", "NPC 走的是真表行（不是合成）");

        // 好感行（赠送 5、可切磋、不可偷）
        var favor_row = (NpcFavorRow)NpcService.favor_row_of(db, char_id);
        CheckNotNull(favor_row, "同伴有好感行（设计写的「＋4 四位同伴」）");
        if (favor_row == null)
        {
            return;
        }

        CheckEq(favor_row.gift_favor, 5, "赠送 +5");
        CheckEq(favor_row.spar_favor, 6, "切磋 +6（燕小七 `ch_ci`）");
        CheckEq(((NpcFavorRow)db.get_row("npc_favor", "ch_gang")).spar_favor, 8, "切磋 +8（林铁山，最能打）");
        CheckEq(((NpcFavorRow)db.get_row("npc_favor", "ch_qi")).spar_favor, 7, "切磋 +7（白清和）");
        CheckEq(((NpcFavorRow)db.get_row("npc_favor", "ch_du")).spar_favor, 5, "切磋 +5（苏九娘）");
        CheckEq(favor_row.steal_difficulty, 0, "同伴不可偷（设计明写）");
    }

    /// <summary>
    /// 20 号 §九 #8：孙掌柜那条支线给的是**燕小七**的好感 ＋10（信是给她的）。
    ///
    /// `npc_quest.reward_favor` 只能加**委托人自己**，所以这条额外的好感写在
    /// `NpcService.EXTRA_FAVOR`（Q72 ③ 还在等设计给列）；结果里要带上给人看的短句。
    /// </summary>
    private void _CheckQuestExtraFavor(TableDb db)
    {
        GodotObject state = SoloState(db);
        state.Call("set_flag", "flag_ch_ci_joined", true);
        int ci_before = NpcService.favor_of(db, state, "ch_ci");
        int sun_before = NpcService.favor_of(db, state, "npc_sun_zhanggui");

        // `context` 实参写满：C# 默认参数对 GDScript 不生效（施工单坑⑲）
        Godot.Collections.Dictionary done =
            NpcService.complete_quest(db, state, "nq_sun_01", new Godot.Collections.Dictionary());
        CheckTrue(done["ok"].AsBool(), $"孙掌柜那条交得了：{done["error"].AsString()}");
        CheckEq(NpcService.favor_of(db, state, "ch_ci"), ci_before + 10, "燕小七好感 ＋10（信是给她的）");
        CheckEq(
            NpcService.favor_of(db, state, "npc_sun_zhanggui"), sun_before + 20,
            "孙掌柜自己那条 ＋20（表里的 reward_favor）");

        Godot.Collections.Array extra = done.ContainsKey("extra_favor")
            ? done["extra_favor"].AsGodotArray()
            : new Godot.Collections.Array();
        CheckTrue(
            extra.Contains("燕小七 好感 +10"),
            $"结果里带上了给人看的短句：{string.Join(", ", extra)}");
    }
}
