using Godot;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的**结算自检与两件小工具**——从 `BattleScreenSelfTest.cs` 拆出来的 partial
/// （那份要守 800 行硬上限；这一批只挪位置、不改行为）。搬过来四件：
/// `_check_settle_frame`（22 §1.6 口径④：结算帧屏上只剩三样）、`_shown`、
/// `_check_settle_panel`（面板版式：自身高／五块齐／升级行数）、`_level_change`（造一条升级记录）。
/// </summary>
public partial class BattleScreen
{
    /// <summary>
    /// 口径④（第八版收口，2026-10-12 与策划拍板）：**结算那一帧屏上只剩三样**——结算面板、
    /// 贴底那枚「确定」、右边居中那枚「战报」钮。其余挂件**整组收起**：回合标签／那行状态播报／
    /// Boss 长血条／暂停条／指令行／展开浮层／战报面板本身，**外加 `FloatLayer`**（跳字挂在结算面板
    /// 之上，最后一下的伤害数字会从面板上闪过去）。再顺手验一次「战报钮按下＝把战报盖到面板之上」——
    /// 面板上那句「还有 N 件放不下（见战报）」是承诺，入口得真在、真盖得住。
    ///
    /// 必须**在自检把战报按出来量「日志跟到底」之前**调它，否则战报面板正开着，这条会假红。
    /// </summary>
    private bool _check_settle_frame(System.Collections.Generic.List<string> lines)
    {
        string[] must_show = { "SettleRoot", "ResultConfirm", "LogButton" };
        string[] must_hide =
        {
            "RoundLabel", "MsgLine", "BossBar", "PauseBar", "CommandRow", "Layer", "LogPanel", "FloatLayer",
        };
        var wrong = new System.Collections.Generic.List<string>();
        foreach (string name in must_show)
        {
            if (!_shown(name))
            {
                wrong.Add($"{name} 该在却不在");
            }
        }

        foreach (string name in must_hide)
        {
            if (_shown(name))
            {
                wrong.Add($"{name} 该收起来却还在");
            }
        }

        bool ok = wrong.Count == 0;
        lines.Add($"结算帧只剩三样（面板／确定／战报钮）={_bool(ok)}"
            + (wrong.Count > 0 ? $"：{string.Join("、", wrong)}" : ""));
        toggle_log_panel();
        bool on_top = _log_panel != null && _log_panel.Visible
            && _log_panel.GetIndex() == GetChildCount() - 1;
        toggle_log_panel();
        lines.Add($"战报钮把战报盖到面板之上={_bool(on_top)}");
        return ok && on_top;
    }

    /// <summary>这个节点在不在树上、显示着（缺节点也算"没显示"——节点缺失另有门限盯）</summary>
    private bool _shown(string node_name)
    {
        Control control = GetNodeOrNull<Control>(node_name);
        return control != null && control.IsVisibleInTree();
    }

    /// <summary>
    /// 结算面板的版式（22 §1.6 第八版 ＋ §八「结算面板＝整屏分区」）：面板**自身**高 ≤ 设计高减掉
    /// 贴底那枚「确定」那一带（口径②：真超出由滚动区整块滚）、宽 ≤ 设计宽、且不与「确定」叠在一起；
    /// `harvest` 时那几块该在的都在（战果标题／经验／铜钱／掉落／武学），零收获时四块**都不在**（口径③）。
    /// `level_rows` &lt; 0 表示这一档不查升级行数（真打那场升了几级取决于经验，不是这一档要钉的）。
    /// </summary>
    private bool _check_settle_panel(
        string tag, bool harvest, int level_rows, System.Collections.Generic.List<string> lines)
    {
        var panel = GetNodeOrNull<Control>("SettleRoot/SettlePanel");
        var confirm = GetNodeOrNull<Control>("ResultConfirm");
        if (panel == null || confirm == null)
        {
            lines.Add($"结算面板缺节点（{tag}）");
            return false;
        }

        double limit = _settle_max_height();
        bool size_ok = panel.Size.Y <= limit + 1.0 && panel.Size.X <= _stage_size().X;
        bool apart_ok = panel.GetGlobalRect().End.Y <= confirm.GetGlobalRect().Position.Y + 1.0;
        bool blocks_ok = panel.FindChild("SettleTitleBox", true, false) != null;
        if (harvest)
        {
            blocks_ok = blocks_ok && panel.FindChild("SettleExp", true, false) != null
                && panel.FindChild("SettleMoneyRow", true, false) != null
                && panel.FindChild("SettleDrops", true, false) != null
                && panel.FindChild("SettleMartial", true, false) != null;
        }
        else
        {
            blocks_ok = blocks_ok && panel.FindChild("SettleExp", true, false) == null
                && panel.FindChild("SettleMoneyRow", true, false) == null
                && panel.FindChild("SettleDrops", true, false) == null
                && panel.FindChildren("SettleLevelRow*", "Label", true, false).Count == 0;
        }

        int rows = panel.FindChildren("SettleLevelRow*", "Label", true, false).Count;
        bool rows_ok = level_rows < 0 || rows == level_rows;
        bool ok = size_ok && apart_ok && blocks_ok && rows_ok;
        lines.Add($"LAYOUT[{tag}] 面板 {panel.Size.X:0}x{panel.Size.Y:0}（上限 {limit:0}）={_bool(size_ok)}；"
            + $"与「确定」不叠={_bool(apart_ok)}；块齐={_bool(blocks_ok)}；升级 {rows} 行={_bool(rows_ok)}");
        return ok;
    }

    /// <summary>造一条升级记录（形状与 `LevelService.apply_available_levels()` 里那一项一致）</summary>
    private static Godot.Collections.Dictionary _level_change(
        string char_id, string name, int from, int to, int points) =>
        new()
        {
            { "char_id", char_id }, { "name", name }, { "from", from }, { "to", to },
            { "points", points },
        };
}
