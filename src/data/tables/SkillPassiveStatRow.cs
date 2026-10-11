using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// skill_passive_stat.csv 行：内功提供的属性／派生数值加成。
//
// target 带前缀：`attr:wu` 加属性点，`stat:qi_max` 加派生数值。
public partial class SkillPassiveStatRow : TableRow
{
    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public string target { get; set; } = "";

    [Export]
    public double value { get; set; }

    [Export]
    public string note { get; set; } = "";

    /// <summary>解析 target，返回 {"kind": "attr"|"stat"|"", "target_id": String}。</summary>
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
