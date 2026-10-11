using Godot;

namespace Lunhui.Core;

/// <summary>
/// 异常状态行上的数值列：灼伤每层削掉多少外功防御、内伤把内力回复压到几成
/// （C# 版，对位原 `src/core/status_rules.gd`）。
///
/// 这两个数在 2026-10-04 由设计补进 `status_effect`（`burn_def_cut` / `qi_regen_mul`，见 08 号
/// 「参数口径 Q86」）。**读法只写在这一处**：`BattleActor` 算防御乘数与内力回复时都调这里，
/// 别在别处再抄一遍列名。用例里手搓、不带表行的状态会走 `fallback`（就是原来那两个代码常量）。
/// </summary>
public partial class StatusRules : RefCounted
{
    /// <summary>状态行上的一个数值列；缺表／缺列／用例里的手搓状态一律退回兜底值。</summary>
    public static double rate(TableDb db, Godot.Collections.Dictionary entry, string column, double fallback)
    {
        string status_id = entry.ContainsKey("status_id") ? entry["status_id"].AsString() : "";
        Resource row = db?.get_row("status_effect", status_id);
        if (row == null)
        {
            return fallback;
        }

        // 列名是运行期给的（`burn_def_cut` / `qi_regen_mul`），走 Object.get 的同义口；
        // 列不存在时拿到的是空 Variant，与 GDScript 的 `row.get(column) == null` 同义。
        Variant value = row.Get(column);
        return value.VariantType == Variant.Type.Nil ? fallback : value.AsDouble();
    }
}
