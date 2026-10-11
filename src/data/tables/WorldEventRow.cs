using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/world_event.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class WorldEventRow : TableRow
{
    // world_event.csv 行：大地图随机事件（设计 19 §四）。
    //
    // 明雷默认关之后，大地图除了地形什么都没有——这一张表给它补上**非战斗**的内容：
    // 按区域权重抽，`kind` 决定玩法（货商／门派弟子／江湖人／官府／天象／野物）。
    //
    // 三条纪律（19 §四）：以非战斗为主（战斗只留玩家主动选的切磋）、权重按区域给、事件要给东西或信息。
    [Export]
    public string event_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    [Export]
    public string kind { get; set; } = "";

    // 适用区域（`map_region.node_id`，多个用 ";" 分隔）——每个区域的池子不同
    [Export]
    public string region_tags { get; set; } = "";

    // 抽取权重（越大越常见）
    [Export]
    public int weight { get; set; } = 0;

    // 额外条件（旗标；空 = 无条件）
    [Export]
    public string condition { get; set; } = "";

    [Export]
    public int repeatable { get; set; } = 1;

    // 效果类型：trade／spar／hint／gift／check
    [Export]
    public string effect_kind { get; set; } = "";

    // 效果指向的东西（掉落组／队伍／线索 id／武学 id……由 `effect_kind` 决定怎么读）
    [Export]
    public string effect_id { get; set; } = "";

    // 事件发生时的提示（HUD／弹窗上那句话）
    [Export]
    public string prompt_text_cn { get; set; } = "";

    [Export]
    public string text_cn { get; set; } = "";

    [Export]
    public string note { get; set; } = "";
}
