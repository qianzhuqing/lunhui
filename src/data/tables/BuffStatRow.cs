using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// buff_stat.csv 行：buff 提供的属性／派生数值修正（复合主键 buff_id + target）。
//
// target 带前缀，与 skill_passive_stat / affix_pool 同一套：`attr:wu` 加属性点，`stat:qi_max` 加派生数值。
public partial class BuffStatRow : TableRow
{
    [Export]
    public string buff_id { get; set; } = "";

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
