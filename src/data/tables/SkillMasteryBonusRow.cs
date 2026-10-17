using Godot;
using Godot.Collections;

namespace Lunhui.Data.Tables;

// skill_mastery_bonus.csv 行：**内功满级签名**（设计 08 §内功满级签名，2026-10-13 拍板）。
//
// 一条 = 「这部内功练到 `mastery` 熟练时给什么」。主键是前三列
// （`skill_id` ＋ `mastery` ＋ `target`），所以同一部内功在同一档可以挂两条互不相干的效果。
//
// target 带前缀，与 `skill_passive_stat`／`affix_pool` 同一套：
//   • `stat:<stat_id>`   → 走贡献通道（与内功本身的加成同一条路，**满级才生效**）
//   • `effect:<标记>`    → 机制类，战斗各结算点按标记判断（`MasteryBonus` 收着"哪些已经接线"）
// `value` 是那条效果的数字（标记类也照填：例 `effect:qi_refund` 的 0.30＝回三成气），
// 系数留在表里、判断留在代码里。
public partial class SkillMasteryBonusRow : TableRow
{
    [Export]
    public string skill_id { get; set; } = "";

    /// <summary>门槛熟练度：满级是 `growth_const.mastery_max`（现 10）。</summary>
    [Export]
    public int mastery { get; set; }

    [Export]
    public string target { get; set; } = "";

    [Export]
    public double value { get; set; }

    /// <summary>给人看的那一句（武学页「满级效果：…」直接读它，界面不再自己编一份文案）。</summary>
    [Export]
    public string note { get; set; } = "";

    /// <summary>解析 target，返回 {"kind": "stat"|"effect"|"", "target_id": String}。</summary>
    public Dictionary parsed_target()
    {
        var separator = target.IndexOf(':');
        if (separator < 0)
        {
            return new Dictionary { { "kind", "" }, { "target_id", target } };
        }

        return new Dictionary
        {
            { "kind", target.Substring(0, separator) },
            { "target_id", target.Substring(separator + 1) },
        };
    }
}
