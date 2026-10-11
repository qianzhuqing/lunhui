using System;
using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// dialogue_option.csv 行：玩家在某个对话节点上能选的**一句话**，以及选完世界改了什么。
//
// 效果三样，都可留空：**置旗标**（心性与立场就记在这儿——设计 20 §四 的「义／谋／利 +1」）、
// **好感增减**（`npc_favor` 那一套）、**给一件东西**（走 `BattleReward.grant_item`，
// 与掉落／事件奖励同一条入账口径）。
//
// **没有数值列**：设计 20 §十一 写死「选项改立场与旁白、**不改数值**」——
// 战力成长走武学／装备／加点三条既有通道，不从对话里发属性。
public partial class DialogueOptionRow : TableRow
{
    /// <summary>
    /// `set_flag` 的分隔符：一列可以写多个旗标（2026-10-04，Q83）。
    ///
    /// **拆法只有这一处**：运行期 `DialogueService` 与构建期 `TableValidator` 都调它；
    /// `tools/validate_tables.ps1` 那份是镜像（PS1 读不到 GDScript／C#），改这里要连着改那边。
    /// </summary>
    public const string SET_FLAG_SEPARATOR = ";";

    /// <summary>拆 `set_flag`：空串 → 空；单值 → 一个元素。空白段（`flag_a;;flag_b`）跳过，不当错误。</summary>
    public static string[] parse_set_flags(string text)
    {
        var parts = text.Split(SET_FLAG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
        var output = new System.Collections.Generic.List<string>(parts.Length);
        foreach (var part in parts)
        {
            var flag = part.Trim();
            if (flag.Length > 0)
            {
                output.Add(flag);
            }
        }

        return output.ToArray();
    }

    [Export]
    public string option_id { get; set; } = "";

    [Export]
    public string node_id { get; set; } = "";

    [Export]
    public int sort_order { get; set; } = 1;

    [Export]
    public string text_cn { get; set; } = "";

    // 显示条件（设计 20 §四 的「前置」那一列，例：看过车辙观察点）；留空 = 一直可选
    [Export]
    public string condition { get; set; } = "";

    // 选完跳到哪个节点；留空 = 结束这次对话
    [Export]
    public string next_node_id { get; set; } = "";

    // 选完置的旗标；**多个用分号隔开**（Q83）。心性写 `heart_*`（义／谋／利）
    [Export]
    public string set_flag { get; set; } = "";

    [Export]
    public int favor_delta { get; set; }

    [Export]
    public string grant_item_id { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
