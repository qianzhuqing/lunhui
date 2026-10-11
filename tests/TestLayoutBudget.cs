using Godot;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 版式预算这个门限**自己**也要有反例（C# 版，对位原 `tests/test_layout_budget.gd`）。
///
/// 由来（2026-10-03）：`LayoutBudget.fits()` 是十个场景自检 + 角色面板那群"版式塞得下"断言的
/// **唯一裁判**——但它自己**一条反例都没有**（全项目只在 test_character_ui 里被正面调用过）。
/// 把 `fits()` 改成 `return true`、或让 `measure_target()` 永远返回一个尺寸很小的节点，
/// **所有"塞得下"的断言照样全绿**，而那意味着整页被裁掉也没人知道。
/// 这跟决策 181 那句"自检直调 API 会绕过……"是同一家族：**门限的裁判要自己有条反例**。
///
/// 这里用**合成的面板**（不依赖任何真实场景）把四个口径的正反面都钉住：
///   `fits`：太大 → false；两个方向各量一次；量不出来（没有 Margin/Column） → false（"量不出来 ≠ 塞得下"）；
///   `content_fits`：滚动内容超限 → false；没有滚动区 → false；
///   `has_opaque_backdrop`：背板太透 → false；没有背板 → false；
///   最后把 `ascii_line` 的判定与 `fits()` 对齐（那几行是给验收脚本 findstr 抓的，别自己说另一套）。
/// </summary>
public partial class TestLayoutBudget : TestCase
{
    public override string SuiteName() => "版式预算门限自身";

    public override void Run()
    {
        _CheckFits();
        _CheckContent();
        _CheckBackdrop();
        _CheckClamp();
    }

    /// <summary>造一个"有可量节点"的合成面板：Control 里放一个叫 Margin 的 MarginContainer。</summary>
    private static Control _Panel(Vector2 size)
    {
        var screen = new Control();
        var margin = new MarginContainer { Name = "Margin", CustomMinimumSize = size };
        screen.AddChild(margin);
        return screen;
    }

    private void _CheckFits()
    {
        // 基准分辨率是**设计文档写死的**（07 §1：1152×648）——上面那两条用 `DESIGN_WIDTH + 48` 造夹具，
        // 所以把常量本身改掉时它们会跟着放宽、**谁都不会红**（2026-10-03 的变异探针实测：
        // `DESIGN_WIDTH 1152→1280` 没被抓住）。这里钉绝对值，改基准分辨率必须是有意为之。
        CheckEq(LayoutBudget.DESIGN_WIDTH, 1152, "基准分辨率宽 = 1152（07 §1）");
        CheckEq(LayoutBudget.DESIGN_HEIGHT, 648, "基准分辨率高 = 648（07 §1）");
        Control small = _Panel(new Vector2(400, 300));
        CheckTrue(LayoutBudget.fits(small), "400×300 的合成面板：塞得下");
        CheckTrue(
            LayoutBudget.ascii_line(small).Contains("ok=true"),
            $"LAYOUT 行与 fits() 同口径：{LayoutBudget.ascii_line(small)}");
        CheckTrue(
            Mathf.Abs(LayoutBudget.min_width(small) - 400.0) < 0.5,
            $"量到的是可量节点的宽度（{LayoutBudget.min_width(small):0}）");
        small.Free();

        Control too_wide = _Panel(new Vector2(LayoutBudget.DESIGN_WIDTH + 48.0f, 300.0f));
        CheckFalse(LayoutBudget.fits(too_wide), "宽出设计分辨率 48px：判不通过（只量高度的那种旧口径会漏掉）");
        CheckTrue(
            LayoutBudget.ascii_line(too_wide).Contains("ok=false"),
            $"LAYOUT 行如实写 ok=false：{LayoutBudget.ascii_line(too_wide)}");
        too_wide.Free();

        Control too_tall = _Panel(new Vector2(400.0f, LayoutBudget.DESIGN_HEIGHT + 15.0f));
        CheckFalse(LayoutBudget.fits(too_tall), "高出设计分辨率 15px：判不通过");
        too_tall.Free();

        // 「量不出来 ≠ 塞得下」：没有任何 Margin/Column 的面板要判**失败**，不能默认通过
        var unmeasurable = new Control();
        CheckFalse(LayoutBudget.fits(unmeasurable), "量不出来的面板判失败（量不出来 ≠ 塞得下）");
        CheckFalse(
            LayoutBudget.ascii_line(unmeasurable).Contains("ok=true"),
            "量不出来时 LAYOUT 行也不许写 ok=true");
        unmeasurable.Free();
    }

    private void _CheckContent()
    {
        var screen = new Control();
        var scroll = new ScrollContainer();
        screen.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new Vector2(2000, 10) };
        scroll.AddChild(content);
        CheckFalse(LayoutBudget.content_fits(screen), "滚动内容 2000 > 上限 1152：判被裁");
        CheckTrue(LayoutBudget.content_fits_limit(screen, 2500.0), "把上限放宽到 2500 就通过（上限是参数，不是写死的）");
        CheckTrue(
            LayoutBudget.ascii_content_line(screen).Contains("ok=false"),
            $"CONTENT 行与 content_fits() 同口径：{LayoutBudget.ascii_content_line(screen)}");
        screen.Free();

        var no_scroll = new Control();
        CheckFalse(LayoutBudget.content_fits(no_scroll), "连滚动区都没有：判失败（量不出来 ≠ 塞得下）");
        no_scroll.Free();
    }

    private void _CheckBackdrop()
    {
        var thin = new Control();
        var faint = new ColorRect { Name = "Backdrop", Color = new Color(0.0f, 0.0f, 0.0f, 0.5f) };
        thin.AddChild(faint);
        CheckFalse(
            LayoutBudget.has_opaque_backdrop(thin),
            "背板只有 0.5 不透明：判太透（地图会透上来把字糊掉）");
        thin.Free();

        var ok_screen = new Control();
        var solid = new ColorRect { Name = "Backdrop", Color = new Color(0.0f, 0.0f, 0.0f, 0.97f) };
        ok_screen.AddChild(solid);
        CheckTrue(LayoutBudget.has_opaque_backdrop(ok_screen), "背板 0.97：通过");
        ok_screen.Free();

        var no_backdrop = new Control();
        CheckFalse(LayoutBudget.has_opaque_backdrop(no_backdrop), "连背板都没有：判失败");
        no_backdrop.Free();
    }

    /// <summary>`clamp_into`（跳字落点用）：夹进画布、边界情况都不许把位置搞成 0。</summary>
    private void _CheckClamp()
    {
        var bounds = new Vector2(1152, 648);
        var item = new Vector2(60, 24);
        Vector2 inside = LayoutBudget.clamp_into(bounds, new Vector2(300, 200), item);
        CheckEq(inside, new Vector2(300, 200), "本来就在画布里的位置不动它");
        Vector2 pushed = LayoutBudget.clamp_into(bounds, new Vector2(1400, 700), item);
        CheckEq(pushed, new Vector2(1152 - 60, 648 - 24), "推出去的位置夹回画布内（含内容自身尺寸）");
        Vector2 negative = LayoutBudget.clamp_into(bounds, new Vector2(-40, -40), item);
        CheckEq(negative, Vector2.Zero, "跑到左上角外面的位置夹回 0");
        Vector2 unsized = LayoutBudget.clamp_into(Vector2.Zero, new Vector2(300, 200), item);
        CheckEq(unsized, new Vector2(300, 200), "画布还没排版（尺寸为 0）时原样返回，不许夹成 0");
        Vector2 huge = LayoutBudget.clamp_into(new Vector2(100, 100), new Vector2(50, 50), new Vector2(400, 400));
        CheckEq(huge, Vector2.Zero, "内容比画布还大时退到左上角，不出现负数");
    }
}
