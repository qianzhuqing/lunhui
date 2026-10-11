using Godot;

namespace Lunhui.Tools;

/// <summary>
/// 字体试排（C# 版，对位 `tools/artgen/font_preview.gd`）：把「中文像素字体在真实字号下长什么样」
/// 渲成一张 PNG（15 §3 的落地验收物）。
///
/// 用法（**必须带窗口**，headless 抓不到字形渲染；2026-10-11 起 C# 进不了 `--script`，入口是场景）：
/// <code>
///   "%GODOT_BIN%" --path . --log-file .logs\font_preview.log --quit-after 600 \
///       res://tools/artgen/font_preview_cs.tscn
/// </code>
///
/// 为什么要有这一条：字体是整套界面的观感前提，而「12px 的汉字糊不糊」只能看，不能推。
/// 这一页同时把 15 §4.4 的两档字号并排、与 UI 实际配色一起摆出来，
/// 好让设计直接判「这版字形能不能用」——顺便把「字号是 12 的整数倍」这条纪律摆在眼前。
/// </summary>
public partial class FontPreview : Node
{
    private const string FontZh = "res://assets/fonts/ark-pixel-12px-proportional-zh_hans.ttf.woff2";
    private const string OutPath = "res://docs/dev/images/字体落地_试排.png";

    // 15 §4.4 的配色，照样摆一遍——字形要跟真背景一起看才判得准。
    private static readonly Color ColorPanel = Color.FromHtml("1e2224");
    private static readonly Color ColorBorder = Color.FromHtml("6e6a63");
    private static readonly Color ColorTitleBg = Color.FromHtml("2c3236");
    private static readonly Color ColorBody = Color.FromHtml("d8d2c4");
    private static readonly Color ColorMinor = Color.FromHtml("8a8578");
    private static readonly Color ColorGold = Color.FromHtml("c8a24a");

    private int _frames;
    private bool _shot;

    public override void _Ready() =>
        // `_Ready` 正处在「根节点装配子节点」的窗口里，直接 `AddChild` 会失败——延后一帧。
        Callable.From(() => GetTree().Root.AddChild(_build_page())).CallDeferred();

    public override void _Process(double delta)
    {
        // 等两帧：第一帧建完 UI，第二帧才拿得到排好版的视口
        _frames += 1;
        if (_frames < 3 || _shot)
        {
            return;
        }

        _shot = true;
        Image image = GetTree().Root.GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(OutPath));
        GD.Print($"字体试排 → {OutPath}（{(error == Error.Ok ? "OK" : $"失败 {(int)error}")}，"
            + $"{image.GetWidth()}x{image.GetHeight()}）");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static Control _build_page()
    {
        var page = new Control();
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Color.FromHtml("14181a") };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        page.AddChild(bg);

        var panel = new PanelContainer
        {
            Position = new Vector2(48, 36),
            CustomMinimumSize = new Vector2(1056, 576),
        };
        panel.AddThemeStyleboxOverride("panel", _panel_style());
        page.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);
        column.AddChild(_title_bar("字体落地 · ark-pixel-font 12px（15 §3 / §4.4）"));

        // 正文 12px（原生）——这是 UI 里出现次数最多的一档
        column.AddChild(_row("正文 12px（原生）", "清风驿有铁匠铺、药铺、客栈与校场；打坐可得气血与内力。"));
        column.AddChild(_row("正文 12px ×2＝24px（显示档）",
            "黑风寨三层：前寨杂乱、聚义厅规整、后寨压抑；火把越少，位置越深。"));
        // 标题那一档：16px 原生在 2026.09 已被上游废弃，这里把两种落地方式都摆出来
        column.AddChild(_row("标题方案 A：12px 字体 ×3＝36px", "石隙迷窟 · 一线天光", 36, ColorGold));
        column.AddChild(_row("标题方案 B：12px 字体 ×2＝24px（只靠颜色区分）", "石隙迷窟 · 一线天光", 24, ColorGold));
        column.AddChild(_row("非整数字号（禁止）：13px",
            "这条会糊：12px 原生的字形按 13px 画，笔画会宽窄不一。", 13, ColorMinor));
        column.AddChild(_row("拉丁与数字（同一套字体）", "Lv.12  气血 148/148  内力 32/32  A1 B2 C3  0.85x"));
        column.AddChild(_row("三级文字层级（只能靠颜色）", "正文 · 次要文字 · 禁用文字", 12, ColorBody));

        var note = new Label
        {
            Text = "说明：本页由 tools/artgen/FontPreview.cs 在真窗口里渲染，字号全部为 12 的整数倍。",
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", ColorMinor);
        column.AddChild(note);
        return page;
    }

    private static VBoxContainer _row(string label_text, string sample, int size = 12, Color? sample_color = null)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        var caption = new Label { Text = label_text };
        caption.AddThemeFontSizeOverride("font_size", 12);
        caption.AddThemeColorOverride("font_color", ColorMinor);
        row.AddChild(caption);
        var sampleLabel = new Label { Text = sample };
        sampleLabel.AddThemeFontSizeOverride("font_size", size);
        sampleLabel.AddThemeColorOverride("font_color", sample_color ?? ColorBody);
        // 两档字体都显式指向那一份，免得「看到的是回退字体」还以为字体没生效
        var font = GD.Load<FontFile>(FontZh);
        if (font != null)
        {
            sampleLabel.AddThemeFontOverride("font", font);
        }

        row.AddChild(sampleLabel);
        return row;
    }

    private static Control _title_bar(string text)
    {
        var bar = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = ColorTitleBg,
            BorderColor = ColorBorder,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        style.SetBorderWidthAll(1);
        bar.AddThemeStyleboxOverride("panel", style);
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        label.AddThemeColorOverride("font_color", ColorBody);
        var font = GD.Load<FontFile>(FontZh);
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
        }

        bar.AddChild(label);
        return bar;
    }

    private static StyleBoxFlat _panel_style()
    {
        var style = new StyleBoxFlat
        {
            BgColor = ColorPanel,
            BorderColor = ColorBorder,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        };
        style.SetBorderWidthAll(1);
        return style;
    }
}