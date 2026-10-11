using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/dialogue_node.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class DialogueNodeRow : TableRow
{
    // dialogue_node.csv 行：一个**说话人**说的一句话（设计 20 §十一：`dialogue_tree`
    // 是剧情层的前置容器，不是可选项）。
    //
    // 开发侧提议的最小容器（口径与理由见 `框架说明.md` 决策 292、待策划确认 Q67）：
    // `dialogue_node`（谁说什么）＋ `dialogue_option`（玩家能选什么、选完世界改了什么）
    //
    // 三条纪律：
    // ① 说话人既可以是**被交互的 NPC**（`npc_def`），也可以是**同伴**（`character_base`）
    // ——设计 20 §四 的选项大半发生在同伴身上（燕小七／林铁山／白清和）。
    // ② 条件用既有条件语言（`start`／`flag_*`／`origin:<char_id>`／`&` 串接；留空 = 无条件）。
    // ③ **台词与选项都在表里**，面板只呈现，不写死一句文案。
    [Export]
    public string node_id { get; set; } = "";

    // `npc_def.npc_id` 或 `character_base.char_id`（同伴）
    [Export]
    public string speaker_id { get; set; } = "";

    // 同一个说话人有多句可用时，取**最小的**那一条（条件满足的前一条胜出）
    [Export]
    public int sort_order { get; set; } = 1;

    [Export]
    public string text_cn { get; set; } = "";

    // 条件语言；留空 = 永远可谈
    [Export]
    public string condition { get; set; } = "";

    // 没有选项时自动接下一句；留空 = 谈到这里
    [Export]
    public string next_node_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
