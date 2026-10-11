using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// story_node.csv 行：剧情节点（0.22.0 新增，设计 18 §3.1）。
//
// 它解决的是「`source_type=story` 的武学没有落地处」：`skill_base.source_id`
// 指向的就是本表的 `node_id`，条件满足时由 `StoryService` 发放 `grant_skill_ids`。
// `kind`：`chapter_end`（章节收束，同时置章节完成旗标）／`faction`（门派支线）／
//         `choice`（抉择的永久增益，设计 20 §八）／`origin_gift`（出身本命机遇，设计 21 §九）。
//
// `kind=choice` 的四种增益列（设计 20 §八，0.29.1）：**不新建表**——`story_node` 本来就是
// 「按旗标触发、发东西」的节点表，抉择也是一个节点。增益走 `AttributeCalculator` 的**贡献通道**
// （`attr_point`／`stat_flat`，与图鉴奖励同一条路），**不新增字段、不动存档版本**：
// 「选过没选过」就是那枚抉择旗标（`trigger_condition`）。
public partial class StoryNodeRow : TableRow
{
    [Export]
    public string node_id { get; set; } = "";

    [Export]
    public string kind { get; set; } = "";

    [Export]
    public string chapter_id { get; set; } = "";

    // 触发地点（引用 map_local.scene_id）；留空 = 地点不限（例如药王谷那条）
    [Export]
    public string place_id { get; set; } = "";

    // 触发条件（剧情旗标）
    [Export]
    public string trigger_condition { get; set; } = "";

    // 发放的武学（引用 skill_base.skill_id，分号分隔）
    [Export]
    public string grant_skill_ids { get; set; } = "";

    // 永久增益（`kind=choice` 用）：属性点层与固定值层各一对，分别引用 attribute_def／stat_def
    [Export]
    public string bonus_attr_id { get; set; } = "";

    [Export]
    public int bonus_attr_value { get; set; }

    [Export]
    public string bonus_stat_id { get; set; } = "";

    [Export]
    public double bonus_stat_value { get; set; }

    [Export]
    public string text_cn { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    /// <summary>
    /// 这条节点的增益贡献（`AttributeCalculator` 的贡献形状）。
    /// 两对各填一条；都不填就返回空（那不是增益节点）。
    ///
    /// 方法名留 snake_case：还没搬走的 GDScript（`story_service.gd`／
    /// `tests/test_story.gd`）与构建期校验器按这个名字调（见 `docs/dev/CSharp迁移计划.md` 二点七）。
    /// </summary>
    public Godot.Collections.Array bonus_contributions()
    {
        var outRows = new Godot.Collections.Array();
        if (!string.IsNullOrEmpty(bonus_attr_id) && bonus_attr_value != 0)
        {
            outRows.Add(new Godot.Collections.Dictionary
            {
                { "kind", AttributeCalculator.CONTRIB_ATTR_POINT },
                { "target", bonus_attr_id },
                { "value", bonus_attr_value },
                { "source", $"story_node:{node_id}" },
            });
        }

        if (!string.IsNullOrEmpty(bonus_stat_id) && bonus_stat_value != 0.0)
        {
            outRows.Add(new Godot.Collections.Dictionary
            {
                { "kind", AttributeCalculator.CONTRIB_STAT_FLAT },
                { "target", bonus_stat_id },
                { "value", bonus_stat_value },
                { "source", $"story_node:{node_id}" },
            });
        }

        return outRows;
    }
}
