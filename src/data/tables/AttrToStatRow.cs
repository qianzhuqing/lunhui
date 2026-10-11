using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// attr_to_stat.csv 行：属性 → 派生数值映射（核心表）。
//
// cap 为空时按 0 存储，约定 cap > 0 才视为有上限。
// diminishing 曲线只吃 cap 与 param（见 docs/design/01_角色系统.md 与 curve_def.csv），rate 仅作说明。
public partial class AttrToStatRow : TableRow
{
    // CSV 列名为 id，与基类字段重名，故改名为 row_id（表名→成员名的别名登记在
    // TableRegistry.cs 的 ALIAS_TRIPLES 与 tests/TestRows.cs 里，两处必须一致）。
    [Export]
    public int row_id { get; set; }

    [Export]
    public string attr_id { get; set; } = "";

    [Export]
    public string stat_id { get; set; } = "";

    [Export]
    public double rate { get; set; }

    // linear / diminishing / piecewise
    [Export]
    public string curve { get; set; } = "linear";

    [Export]
    public double cap { get; set; }

    [Export]
    public double param { get; set; }

    [Export]
    public string note { get; set; } = "";

    /// <summary>有没有上限（表里留空即无上限，存储为 0）。</summary>
    public bool has_cap() => cap > 0.0;
}
