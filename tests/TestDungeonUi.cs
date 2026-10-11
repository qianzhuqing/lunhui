using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 副本完成度界面：四项完成度、楼层列表、扫荡按钮（真实场景）——
/// 对位 `tests/test_dungeon_ui.gd`（2026-10-11 P4 第十三批搬 C#）。
///
/// 这份要**真实场景树**（运行器注 `Tree`，没有就 `Fail`）：面板从 `scenes/dungeon_screen.tscn`
/// 实例化、挂到根上、跑 `setup()`，然后按节点名查行、按按钮真按一次。
/// 四块：① 四项完成度与百分比（宝箱 0/4／隐藏房间 0/5／隐藏 Boss 0/1／事件 0/2，
/// 分母全部来自表）；② 楼层列表（三层都在、未通关时写「已清 0／还差」并置灰扫荡）；
/// ③ 扫荡（走调用方的 handler、动画推进与逐间打勾、防连点、没落点时说明原因）；
/// ④ 长列表不截断（把第 1 层复制成 30 层，30 行必须全建出来）。
/// </summary>
public partial class TestDungeonUi : TestCase
{
    private const string UiScene = "res://scenes/dungeon_screen.tscn";
    private const string SceneId = "scene_heifengzhai";

    public override string SuiteName() => "副本完成度界面";

    public override void Run()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，界面用例无法进行");
            return;
        }

        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        DungeonScreen panel = GD.Load<PackedScene>(UiScene).Instantiate<DungeonScreen>();
        panel.state_override = state;
        panel.scene_id = SceneId;
        var closes = new Godot.Collections.Array();
        panel.return_handler = Callable.From(() => closes.Add(true));
        Tree.Root.AddChild(panel);
        panel.setup();

        _check_summary(panel);
        _check_floors(panel, state);
        _check_sweep(panel, state);
        _check_close(panel, closes);

        Tree.Root.RemoveChild(panel);
        panel.Free();
        _check_long_floor_list(db, state);
    }

    /// <summary>
    /// 几十层的长列表：完成度面板是「一层一行」，夹具把第 1 层复制成 30 层，
    /// 面板要**逐层建行**（30 个 FloorRow），不许截断或只画前几层——真实数据只有 3 层，
    /// 「列表被截断」在界面上看起来只是「少了几层」，很难被发现。
    /// </summary>
    private void _check_long_floor_list(TableDb db, GodotObject state)
    {
        TableDb wide = TableWithManyFloors(db, SceneId, 30);
        DungeonScreen panel = GD.Load<PackedScene>(UiScene).Instantiate<DungeonScreen>();
        panel.db_override = wide;
        panel.state_override = state;
        panel.scene_id = SceneId;
        Tree.Root.AddChild(panel);
        panel.setup();
        var rows = 0;
        foreach (Node node in panel.FindChildren("FloorRow*", "HBoxContainer", true, false))
        {
            rows += 1;
        }

        CheckEq(rows, 30, $"30 层都建了行（实际 {rows}）");
        CheckNotNull(panel.FindChild("FloorRow30", true, false), "第 30 层的行在（没被截断）");
        CheckNotNull(panel.FindChild("FloorRow3", true, false), "第 3 层的行也在");
        Tree.Root.RemoveChild(panel);
        panel.Free();
    }

    /// <summary>四项完成度与百分比。</summary>
    private void _check_summary(DungeonScreen panel)
    {
        CheckTrue(panel.summary_text().Contains("总计 0%"), $"新档完成度 0%：{panel.summary_text()}");
        string detail = panel.detail_text();
        CheckTrue(detail.Contains("宝箱 0/4"), $"宝箱分母来自表：{detail}");
        CheckTrue(detail.Contains("隐藏房间 0/5"), $"隐藏房间分母：{detail}");
        CheckTrue(detail.Contains("隐藏 Boss 0/1"), $"隐藏 Boss 分母（只算醉刀客）：{detail}");
        CheckTrue(detail.Contains("事件 0/2"), $"事件分母：{detail}");
    }

    /// <summary>楼层列表：每层的房间数、清怪进度、能不能扫荡。</summary>
    private void _check_floors(DungeonScreen panel, GodotObject state)
    {
        CheckEq(panel.floor_rows().Count, 3, "黑风寨三层都在列表里");
        var floor1 = panel.FindChild("FloorLabel1", true, false) as Label;
        CheckNotNull(floor1, "第 1 层有一行");
        if (floor1 != null)
        {
            CheckTrue(floor1.Text.Contains("已清 0"), $"还没打就写着已清 0：{floor1.Text}");
            CheckTrue(floor1.Text.Contains("还差"), $"没通关的层写清还差哪些房间：{floor1.Text}");
        }

        var sweep1 = panel.FindChild("SweepButton1", true, false) as Button;
        CheckNotNull(sweep1, "第 1 层有扫荡按钮");
        if (sweep1 != null)
        {
            CheckTrue(sweep1.Disabled, "没通关时扫荡按钮置灰");
        }

        // 打通第 1 层（前院）后：可扫荡，完成度不变（房间清怪不算完成度四项）
        state.Call("record_dungeon", SceneId, "rooms", "hf1_yard");
        panel.refresh();
        var after = panel.FindChild("SweepButton1", true, false) as Button;
        CheckFalse(after.Disabled, "通关后扫荡按钮可点");
        var label = panel.FindChild("FloorLabel1", true, false) as Label;
        CheckTrue(label.Text.Contains("可扫荡"), $"行里标出可扫荡：{label.Text}");
    }

    /// <summary>扫荡按钮：走调用方的 handler，并把结果播在状态栏。</summary>
    private void _check_sweep(DungeonScreen panel, GodotObject state)
    {
        var calls = new Godot.Collections.Array();
        panel.sweep_handler = Callable.From<int, Godot.Collections.Dictionary>(floorNumber =>
        {
            calls.Add(floorNumber);
            return new Godot.Collections.Dictionary
            {
                { "ok", true },
                { "summary", "铜钱 +9　物品 兽皮×1" },
                { "floor", floorNumber },
            };
        });
        var button = panel.FindChild("SweepButton1", true, false) as Button;
        if (button != null)
        {
            button.EmitSignal("pressed");
        }

        CheckEq(calls.Count, 1, "点扫荡会通知调用方一次");
        CheckEq(calls.Count > 0 ? calls[0].AsInt32() : -1, 1, "扫的是第 1 层");
        // 扫荡动画：进度条推进、逐间打勾、按钮防连点；掉落其实按下时就结算了
        CheckTrue(panel.is_sweeping(), "按下扫荡进入动画状态");
        CheckNotNull(panel.FindChild("SweepProgress", true, false), "有扫荡进度条");
        CheckLt(panel.sweep_progress(), 1.0, "刚按下进度还没满");
        CheckTrue(button != null && button.Disabled, "动画期间扫荡按钮置灰（防连点）");
        panel.step_sweep_animation(0.15);
        CheckGt(panel.sweep_progress(), 0.0, $"推进后进度在涨（{panel.sweep_progress():F2}）");
        panel.step_sweep_animation(0.2);
        CheckGt(
            panel.sweep_ticked_rooms().Length,
            0.0,
            $"过半时至少打勾一间（进度 {panel.sweep_progress():F2}）");
        var sweepingRow = panel.FindChild("FloorLabel1", true, false) as Label;
        CheckTrue(
            sweepingRow != null && sweepingRow.Text.Contains("✓"),
            $"扫荡时逐间打勾：{(sweepingRow != null ? sweepingRow.Text : "(没有行)")}");
        panel.step_sweep_animation(1.0);
        CheckFalse(panel.is_sweeping(), "动画走完");
        CheckTrue(panel.status_text().Contains("铜钱 +9"), $"状态栏播报扫荡结果：{panel.status_text()}");
        CheckTrue(panel.FindChild("SweepProgress", true, false) == null, "动画结束后进度条收走");
        var afterRow = panel.FindChild("SweepButton1", true, false) as Button;
        CheckTrue(afterRow != null && !afterRow.Disabled, "动画结束后按钮恢复可点");
        var otherRow = panel.FindChild("SweepButton2", true, false) as Button;
        CheckTrue(
            otherRow != null && otherRow.Disabled,
            "动画结束后没通关的层仍然是灰的（别一律打开）");
        // 没有 handler（比如误开在城镇）时给明确提示
        panel.sweep_handler = default;
        panel.press_sweep(2);
        CheckTrue(panel.status_text().Contains("不能扫荡"), $"没有落点时说明原因：{panel.status_text()}");
    }

    private void _check_close(DungeonScreen panel, Godot.Collections.Array closes)
    {
        panel.press_return();
        CheckEq(closes.Count, 1, "离开按钮触发回退");
    }
}
