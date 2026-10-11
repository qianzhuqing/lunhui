using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// stat_def.csv 行：派生数值定义。
//
// max_value 为空时按 0 存储，约定 max_value > 0 才视为有上限（0 表示无上限）。
public partial class StatDefRow : TableRow
{
    // int / float
    [Export]
    public string stat_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // int / float
    [Export]
    public string value_kind { get; set; } = "int";

    [Export]
    public bool is_percent { get; set; }

    [Export]
    public double min_value { get; set; }

    [Export]
    public double max_value { get; set; }

    [Export]
    public int show_decimals { get; set; }

    [Export]
    public string desc { get; set; } = "";

    /// <summary>是否有上限（表里留空即无上限，存储为 0）。</summary>
    public bool has_max() => max_value > 0.0;
}
