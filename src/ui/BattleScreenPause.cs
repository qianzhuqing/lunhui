using Godot;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的**暂停条与战场手势**（partial，从 `BattleScreen.cs` 拆出来守 800 行硬上限）：
/// 长按 0.4s／Esc 呼出暂停条（22 §1.6）。第六版起**短点战场不再推进**——这一屏只有这一种手势。
/// </summary>
public partial class BattleScreen
{
    // ------------------------------------------------------------------ 暂停条（22 §1.6）

    /// <summary>暂停条开着吗（用例与自检读它）。</summary>
    public bool pause_open() => _pause_open;

    /// <summary>
    /// 呼出／收起暂停条（22 §1.6：长按战场 0.4s 或 Esc）。它和指令行、展开浮层**三者互斥**，
    /// 而且**用完就收**——工具行整条删掉之后，自动／速度／策略／战报四个按钮就住在这里。
    /// </summary>
    public bool toggle_pause()
    {
        _pause_open = !_pause_open;
        if (_pause_open)
        {
            _panel_mode = ""; // 与展开浮层互斥：开一个就收另一个
        }

        _holding = false;
        _refresh();
        return _pause_open;
    }

    /// <summary>收起暂停条（按了它里面任何一枚按钮、或战斗结束时调它）</summary>
    public void close_pause()
    {
        if (!_pause_open)
        {
            return;
        }

        _pause_open = false;
        _refresh();
    }

    /// <summary>
    /// 战场上的左键（22 §1.6）：**只有一种手势**——按住 0.4s 呼出暂停条。
    /// 第六版起短点战场**不再推进**（这一屏没有任何手动推进入口）；落在单位／按钮上的点击
    /// 被它们自己吃掉（Godot 的 GUI 命中），所以这里收到的正是「点战场空白处」。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouse.Pressed)
        {
            _holding = true;
            _hold_time = 0.0;
            return;
        }

        _holding = false;
    }

    /// <summary>长按计时：够点了就呼出暂停条。</summary>
    private void _tick_hold(double delta)
    {
        if (!_holding)
        {
            return;
        }

        _hold_time += delta;
        if (_hold_time < PAUSE_HOLD_SECONDS)
        {
            return;
        }

        _holding = false;
        toggle_pause();
    }
}
