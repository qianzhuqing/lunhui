using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 剧情节点（C# 版，对位原 `src/core/story_service.gd`；设计 18 §3.1）：
/// `story_node` 是「条件 → 效果」的落地处。
///
/// 它补的是设计侧端到端梳理里最要命的两个断点（18 号文档的 4／6）：
/// **6 部 `source_type=story` 的武学原来一部都没有落地处**——`skill_base.source_id`
/// 指向的 `chapter1_end`／`xuanwei`／`yaowang` 在表里根本不存在。
///
/// 形状与招募链（`RecruitService`）完全一样：**条件与效果都在表里**，代码只做
/// 「地点匹配 + 条件旗标已点亮 + 还没领过」三件事，**谁点亮条件旗标是各系统的事**。
///
/// 完成记账用一枚派生旗标 `story_done_&lt;node_id&gt;`（名字的唯一出处是本文件的 `done_flag()`）——
/// 不新开存档字段：旗标本来就可序列化。
///
/// 两个常量（`TITLE_CONDITION_FORMAT`／`TITLE_SUFFIXES`）被 `character_screen.gd` 读，
/// 而 GDScript 读不到 C# 的 `const`，所以配了 `title_condition_format()`／`title_suffixes()`
/// 两个过渡取值口（P5 清零后收回）。条件判定与武学发放都直调 `GuideService`／`SkillGrant`
/// （两者都已是 C#，不再走跨语言只读通道）。
/// </summary>
public partial class StoryService : RefCounted
{
    /// <summary>
    /// 心性称号的条件串（设计 20 §四「显示落点」段 ＋ `docs/dev/待策划确认.md` Q74① 拍板）：
    /// **没到章节末，整行不显示**。名字只在 `ui_text` 里，代码不抄第二份。
    /// 这一串与 `dialogue_node.dl_end_*` 的 `condition` 必须一字一致，用例两边都读，漂了就红。
    /// </summary>
    public const string TITLE_CONDITION_FORMAT =
        "flag_open_%s&flag_ledger_public|flag_ledger_buried|flag_ledger_scattered";

    /// <summary>三个后缀（义／谋／利）与 `flag_open_*`／`heart_*`／`dl_end_*` 同一个后缀体系。</summary>
    private static readonly string[] TITLE_SUFFIX_VALUES = { "yi", "mou", "li" };

    // ---------------------------------------------------------------- 过渡取值口

    public static string title_condition_format() => TITLE_CONDITION_FORMAT;
    public static string[] title_suffixes() => (string[])TITLE_SUFFIX_VALUES.Clone();

    // ---------------------------------------------------------------- 记账旗标

    public static string done_flag(string node_id) => $"story_done_{node_id}";

    // ---------------------------------------------------------------- 称号

    /// <summary>心性称号的显示名；没到章节末（条件不满足）返回空串。</summary>
    public static string title_of(TableDb db, GodotObject state)
    {
        if (db == null || state == null)
        {
            return "";
        }

        foreach (string suffix in TITLE_SUFFIX_VALUES)
        {
            string condition = TITLE_CONDITION_FORMAT.Replace("%s", suffix);
            if (!_condition_met(state, condition))
            {
                continue;
            }

            var row = db.get_row("ui_text", $"title_{suffix}") as UiTextRow;
            if (row == null)
            {
                GD.PushError($"[StoryService] ui_text 缺 title_{suffix}——心性称号没有出处");
                return "";
            }

            return row.text_cn;
        }

        return "";
    }

    // ---------------------------------------------------------------- 抉择的永久增益

    /// <summary>
    /// 抉择的**永久增益**（设计 20 §八，0.29.1）：`kind=choice` 的节点只要**条件满足**就一直在生效
    /// ——条件就是那枚抉择旗标，所以"选完立刻生效"，不必再跑回触发点去领。
    ///
    /// 目标是**主角**（`state.char_ids[0]`）：抉择是主角做的，而这三条增益改的是**资质**
    /// （悟性／根骨）与派生上限——发给全队会变成"四个人的资质一起涨"，那是另一回事。
    /// 贡献形状与图鉴奖励完全一样（`attr_point`／`stat_flat`），**不另起一路属性合成**。
    /// </summary>
    public static Godot.Collections.Array permanent_contributions(TableDb db, GodotObject state, string char_id)
    {
        var out_contribs = new Godot.Collections.Array();
        if (db == null || state == null || char_id.Length == 0)
        {
            return out_contribs;
        }

        string[] char_ids = state.Get("char_ids").AsStringArray();
        if (char_ids.Length == 0 || char_id != char_ids[0])
        {
            return out_contribs;
        }

        foreach (Variant entry in db.rows("story_node"))
        {
            var row = entry.AsGodotObject() as StoryNodeRow;
            if (row == null || row.kind != "choice")
            {
                continue;
            }

            if (!_condition_met(state, row.trigger_condition))
            {
                continue;
            }

            out_contribs.AddRange(row.bonus_contributions());
        }

        return out_contribs;
    }

    /// <summary>
    /// 生效中的抉择增益，翻译成**给人看的一条**（角色面板用）：`["悟性 +2", "内力上限 +20"]`。
    /// 名字从 `attribute_def.name_cn`／`stat_def.name_cn` 取，**面板不自己翻译 id**。
    /// </summary>
    public static string[] bonus_labels(TableDb db, GodotObject state, string char_id)
    {
        var out_labels = new System.Collections.Generic.List<string>();
        if (db == null || state == null || char_id.Length == 0)
        {
            return out_labels.ToArray();
        }

        string[] char_ids = state.Get("char_ids").AsStringArray();
        if (char_ids.Length == 0 || char_id != char_ids[0])
        {
            return out_labels.ToArray();
        }

        foreach (Variant entry in db.rows("story_node"))
        {
            var row = entry.AsGodotObject() as StoryNodeRow;
            if (row == null || row.kind != "choice" || !_condition_met(state, row.trigger_condition))
            {
                continue;
            }

            if (row.bonus_attr_id.Length > 0 && row.bonus_attr_value != 0)
            {
                var attr = db.get_row("attribute_def", row.bonus_attr_id) as AttributeDefRow;
                out_labels.Add($"{attr?.name_cn ?? row.bonus_attr_id} +{row.bonus_attr_value}");
            }

            if (row.bonus_stat_id.Length > 0 && row.bonus_stat_value != 0.0)
            {
                var stat = db.get_row("stat_def", row.bonus_stat_id) as StatDefRow;
                string amount = stat != null && stat.is_percent
                    ? $"+{(int)System.Math.Round(row.bonus_stat_value * 100.0, System.MidpointRounding.AwayFromZero)}%"
                    : $"+{(int)System.Math.Round(row.bonus_stat_value, System.MidpointRounding.AwayFromZero)}";
                out_labels.Add($"{stat?.name_cn ?? row.bonus_stat_id} {amount}");
            }
        }

        return out_labels.ToArray();
    }

    // ---------------------------------------------------------------- 地点匹配与领取

    /// <summary>
    /// 地点匹配：`place_id` 命中这张图（或它的父区域节点）。
    /// **`place_id` 留空 = 不限地点**（药王谷那条就是在哪儿都能领）。
    /// </summary>
    public static bool place_matches(TableDb db, Resource row, string place_id)
    {
        string want = (row as StoryNodeRow)?.place_id ?? "";
        if (want.Length == 0)
        {
            return true;
        }

        if (place_id.Length == 0)
        {
            return false;
        }

        if (want == place_id)
        {
            return true;
        }

        Variant local = db.get_row("map_local", place_id)?.Get("parent_node") ?? default;
        return local.VariantType != Variant.Type.Nil && local.AsString() == want;
    }

    /// <summary>这个地点上还能领的剧情节点（条件满足、还没领过、地点对得上）。</summary>
    public static Godot.Collections.Array pending_for(TableDb db, GodotObject state, string place_id)
    {
        var out_nodes = new Godot.Collections.Array();
        if (db == null || state == null)
        {
            return out_nodes;
        }

        foreach (Variant entry in db.rows("story_node"))
        {
            var row = entry.AsGodotObject() as StoryNodeRow;
            if (row == null)
            {
                continue;
            }

            if (state.Call("has_flag", done_flag(row.node_id)).AsBool())
            {
                continue;
            }

            if (!place_matches(db, row, place_id))
            {
                continue;
            }

            if (!_condition_met(state, row.trigger_condition))
            {
                continue;
            }

            out_nodes.Add(row);
        }

        return out_nodes;
    }

    /// <summary>
    /// 在某地点领取剧情节点：发武学；`kind=chapter_end` 的节点**同时置上本章的完成条件**。
    /// 返回 `[{node_id, text_cn, granted, chapter_done}]`；`granted` 的形状见 `SkillGrant`。
    /// </summary>
    public static Godot.Collections.Array claim_for(TableDb db, GodotObject state, string place_id)
    {
        var out_claims = new Godot.Collections.Array();
        foreach (Variant entry in pending_for(db, state, place_id))
        {
            var row = entry.AsGodotObject() as StoryNodeRow;
            if (row == null)
            {
                continue;
            }

            // 发放通道按 kind 分：`origin_gift`（出身本命机遇）走 `origin`，其余走 `story`。
            // 两条通道的 `source_id` 都是 `story_node.node_id`，只是条件不同。
            string source_type = row.kind == "origin_gift" ? "origin" : "story";
            Godot.Collections.Array granted =
                SkillGrant.grant_from_source(db, state, source_type, row.node_id);
            var chapter_done = false;
            if (row.kind == "chapter_end")
            {
                var chapter = db.get_row("chapter_def", row.chapter_id) as ChapterDefRow;
                if (chapter != null && chapter.complete_condition.Length > 0)
                {
                    state.Call("set_flag", chapter.complete_condition, true);
                    chapter_done = true;
                }
            }

            state.Call("set_flag", done_flag(row.node_id), true);
            out_claims.Add(new Godot.Collections.Dictionary
            {
                { "node_id", row.node_id },
                { "text_cn", row.text_cn },
                { "granted", granted },
                { "chapter_done", chapter_done },
            });
        }

        return out_claims;
    }

    /// <summary>条件判定：与引导／招募／剧情节点共用一份条件语言（`GuideService.condition_met`）。</summary>
    private static bool _condition_met(GodotObject state, string condition) =>
        GuideService.condition_met(state, condition);
}
