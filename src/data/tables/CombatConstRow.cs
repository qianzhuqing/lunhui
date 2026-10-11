using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

/// <summary>
/// `combat_const.csv` 行：伤害管线依赖的全局常数。
///
/// 只放参数，不放公式：公式仍在 DamageResolver 里实现。
/// 这是**第一个**搬成 C# 的行类（P2 试点），成员名与 CSV 列名逐字相同，见 `TableRow.cs` 的说明。
/// </summary>
public partial class CombatConstRow : TableRow
{
    [Export]
    public string const_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // GDScript 的 float 是 64 位，C# 侧一律用 double：用 float 会把表里的常数截断，
    // 而用例里到处是 check_eq(值, 字面量)——0.1 这种数一旦被截就永远比不平。
    [Export]
    public double value { get; set; }

    [Export]
    public string desc { get; set; } = "";
}
