using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 招式的**实际内力消耗**（唯一一处算法）：`skill_active.qi_cost` × `combat_const.qi_cost_mul`，
/// 四舍五入、**非零下限 1**、`0` 仍是 `0`。
///
/// 设计口径（`04_战斗与伤害.md` §招式循环「拍板结果」，2026-10-13）：降气耗靠**一处调参**，
/// 不去逐行改 35 个数值。
///
/// **为什么单开一份**：这个数有四处要读——战斗的可用判定、真扣气、招式按钮上的字、
/// 角色面板的「消耗内力」。各写一遍就会出现「屏上写 16、实际扣 10」这类**不会报错**的错
/// （玩家只会觉得"气怎么莫名其妙不够"）。与「段数」那条（`extra_hit`）同一套做法：
/// 算法一处，各处读同一份。
/// </summary>
public static class SkillCost
{
    /// <summary>`combat_const` 缺那行时的兜底（不降不升，退回改口径之前的行为）。</summary>
    public const double QI_COST_MUL_FALLBACK = 1.0;

    public static double qi_cost_mul(TableDb db)
    {
        Resource row = db != null ? db.get_row("combat_const", "qi_cost_mul") : null;
        if (row == null)
        {
            GD.PushError("[SkillCost] combat_const 缺 qi_cost_mul，退回 1.0（先重建配置表？）");
            return QI_COST_MUL_FALLBACK;
        }

        return row.Get("value").AsDouble();
    }

    /// <summary>
    /// 实际气耗：四舍五入、**非零下限 1**、`0` 仍 `0`；**系数只作用我方**——敌人不乘（＝原值）。
    ///
    /// `is_ally` 就是那条**敌我判定**，**只有这一处**（设计 04 §招式循环：再加第二条常数
    /// 就是多一个漂移点，不许）。敌人为什么不在内：这条系数买的是**玩家的循环体感**
    /// （内力不再是闸门）；敌人那边"大招偶尔放得出"是**节奏设计**，是预兆／拆招那套的输入，
    /// 统一降耗会把它抹平——实测证据是 `TestEndToEnd` 那一场（5 级裸装两人组 vs 山贼巡山队）
    /// 从 17 回合险胜翻成 6 回合落败，连跑 13 颗种子一致。
    ///
    /// `0` 气招是设计里"替换普攻"那一档（P30）——它们不是"没强度"，只是不花内力，
    /// 所以**不能**被下限抬成 1。面板侧（没有"场"这回事）传 `true`：那是玩家自己的口径。
    /// </summary>
    public static int effective_qi_cost(TableDb db, int base_cost, bool is_ally)
    {
        if (base_cost <= 0)
        {
            return 0;
        }

        if (!is_ally)
        {
            // 敌人那一侧：一分不减（`qi_cost_mul` 对它们不存在）
            return base_cost;
        }

        double scaled = base_cost * qi_cost_mul(db);
        return System.Math.Max(1, (int)System.Math.Round(scaled, System.MidpointRounding.AwayFromZero));
    }
}
