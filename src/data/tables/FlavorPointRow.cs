using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// flavor_point.csv 行：观察点（设计 20 §3.2，0.29.1）。
//
// 这是**最薄的一张表**：一个位点、一句话。它**不参与任何数值、不发奖励、不锁任何路**
// ——玩家不看也能通关，看了就先一步猜到真相（探索的回报）。
//
// 为什么不复用既有通道（设计原文的口径）：`event_check` 会被算进「事件判定」并进线索本
// 与副本完成度；`hidden_trigger` 会被当成「隐藏内容」计数——观察点两样都不是，
// 占那两个数会把副本完成度的分母改错。
//
// 位点名 `Observe_<point_id>`：**前缀的唯一出处是本文件的 `MARKER_PREFIX`／`marker_name_of()`**
// （地编与小地图／大地图两边都按它认位点）。C# 静态方法可以从 GDScript 经
// `preload("…/FlavorPointRow.cs").marker_name_of(x)` 调用（2026-10-10 实测）。
public partial class FlavorPointRow : TableRow
{
    public const string MARKER_PREFIX = "Observe_";

    [Export]
    public string point_id { get; set; } = "";

    // 小地图（scene_id）与大地图节点（region_id）**二选一**
    [Export]
    public string scene_id { get; set; } = "";

    // 房间级位点：挂在 `Markers/<room_id>/` 下（与宝箱／触发同一套找法）
    [Export]
    public string room_id { get; set; } = "";

    [Export]
    public string region_id { get; set; } = "";

    [Export]
    public string text_cn { get; set; } = "";

    public static string marker_name_of(string point_id) => MARKER_PREFIX + point_id;

    public string marker_name() => marker_name_of(point_id);
}
