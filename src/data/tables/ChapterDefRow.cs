using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/chapter_def.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class ChapterDefRow : TableRow
{
    // chapter_def.csv 行：章节定义（0.22.0 新增，设计 18 §3.1）。
    //
    // 这一张表存在的理由：`GameState.chapter_id` 原来是硬编码常量（`DEFAULT_CHAPTER`），
    // 枢纽页那行「章节：第 1 章」是个假象——没有任何推进逻辑，也没有「第一章结束」这个实体。
    // 现在章节的入口条件／完成条件／下一章都在表里，推进由 `ChapterService` 按表算。
    [Export]
    public string chapter_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public int sort_order { get; set; } = 0;

    // 进入本章的条件：`start`（开局）或剧情旗标
    [Export]
    public string entry_condition { get; set; } = "";

    // 本章完成的条件（旗标）；由 `story_node` 的章节结束节点置上
    [Export]
    public string complete_condition { get; set; } = "";

    // 完成时给玩家看的一句话
    [Export]
    public string complete_text_cn { get; set; } = "";

    // 下一章（留空 = 没有下一章）；第二章现在只是占位
    [Export]
    public string next_chapter_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
