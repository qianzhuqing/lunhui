using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 章节推进（C# 版，对位原 `src/core/chapter_service.gd`；设计 18 §3.1）。
///
/// `chapter_def` 是「第一章从哪开始、到哪结束、下一章是谁」的唯一出处。
/// 原 `GameState.chapter_id` 是硬编码常量，枢纽页那行「章节：第 1 章」是个假象——
/// 既没有推进逻辑，也没有「第一章结束」这个实体（设计侧端到端梳理的断点 5）。
/// 现在判定口径在表里，代码只做匹配。
///
/// 条件判定借 `GuideService.condition_met`——**条件语言只有那一份**，不另造一套。
/// </summary>
public partial class ChapterService : RefCounted
{
    /// <summary>
    /// 当前章节行。存档里的 id 认不出来时退回「开局那一章」（`entry_condition=start`）——
    /// 老档或空值不该让整局没有章节。
    /// </summary>
    public static ChapterDefRow row_of(TableDb db, GodotObject state)
    {
        if (db == null || state == null)
        {
            return null;
        }

        var row = db.get_row("chapter_def", state.Get("chapter_id").AsString()) as ChapterDefRow;
        return row ?? starting_row(db);
    }

    public static ChapterDefRow starting_row(TableDb db)
    {
        if (db == null)
        {
            return null;
        }

        foreach (Variant entry in db.rows("chapter_def"))
        {
            var row = entry.AsGodotObject() as ChapterDefRow;
            if (row != null && row.entry_condition == "start")
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>章节显示名（表里的 `name_cn`）。取不到时给空串——**绝不把 id 甩给玩家**。</summary>
    public static string label_of(TableDb db, GodotObject state) => row_of(db, state)?.name_cn ?? "";

    /// <summary>
    /// 完成条件满足就推进到下一章。返回 `{advanced, from, to, name, text}`。
    /// 只推进**一格**：一次调用换一章，避免"链式跳章"把中间的剧情吞掉。
    /// </summary>
    public static Godot.Collections.Dictionary try_advance(TableDb db, GodotObject state)
    {
        ChapterDefRow row = row_of(db, state);
        if (row == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "advanced", false }, { "from", "" }, { "to", "" }, { "name", "" }, { "text", "" },
            };
        }

        string from = row.chapter_id;
        string done_text = row.complete_text_cn;
        if (!_condition_met(state, row.complete_condition))
        {
            return new Godot.Collections.Dictionary
            {
                { "advanced", false }, { "from", from }, { "to", "" }, { "name", row.name_cn }, { "text", "" },
            };
        }

        string next = row.next_chapter_id;
        if (next.Length == 0)
        {
            // 最后一章：完成了也停在原地（设计把第二章当占位，第二章没有下一章）
            return new Godot.Collections.Dictionary
            {
                { "advanced", false }, { "from", from }, { "to", "" },
                { "name", row.name_cn }, { "text", done_text },
            };
        }

        var next_row = db.get_row("chapter_def", next) as ChapterDefRow;
        if (next_row == null)
        {
            GD.PushError($"[ChapterService] chapter_def 里没有下一章 {next}（{from} 的 next_chapter_id 写错了）");
            return new Godot.Collections.Dictionary
            {
                { "advanced", false }, { "from", from }, { "to", "" }, { "name", row.name_cn }, { "text", "" },
            };
        }

        state.Set("chapter_id", next);
        // `text` 是表里的 `complete_text_cn`（「第一章告一段落」）——**给玩家看的那一句**，
        // 由场景层播报；空着说明这一章没配完成文案。
        return new Godot.Collections.Dictionary
        {
            { "advanced", true }, { "from", from }, { "to", next },
            { "name", next_row.name_cn }, { "text", done_text },
        };
    }

    /// <summary>条件判定：与引导／招募／剧情节点共用一份条件语言（`GuideService.condition_met`）。</summary>
    private static bool _condition_met(GodotObject state, string condition) =>
        GuideService.condition_met(state, condition);
}
