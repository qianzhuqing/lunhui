using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 界面外壳构件（C# 新写）：**顶栏／底栏／浮窗标题条／底部说明行／状态条**——
/// 也就是策划 12 屏示意图里那几件「每屏都有、但现在的界面一件都没有」的东西。
///
/// 为什么单开一份：`UiKit` 管的是「一块卡片」（标题 ＋ 内容列），管不了整屏的骨架。
/// 而骨架过去是各面板自己拼的——于是 8 个浮层各写一份 `MarginContainer ＋ VBox ＋ Label`，
/// 结果是**没有一个浮层长得像示意图**：没有顶栏（地名／铜钱）、没有底栏（七入口）、
/// 标题是一行普通 `Label` 而不是「深墨条 ＋ 2px 朱红」。
///
/// **色值一律走 `UiKit.color()`**（单一出处是 `assets/ui/theme_lunhui.tres`，决策 375），
/// 本文件里不出现第二个色值字面量。字号同理只有 12／24 两档（15 §4.4，门限 `CheckFontSizeTiers`）。
/// </summary>
public partial class UiChrome : RefCounted
{
    /// <summary>顶栏高度（示意图 `.lu-topbar` 的 42px）。</summary>
    public const int TOP_H = 42;

    /// <summary>底栏高度（示意图 `.lu-bottombar` 的 58px）。</summary>
    public const int BOTTOM_H = 58;

    /// <summary>状态条高度（15 §4.4「控件规格」：12px，不是示意图那个 8）。</summary>
    public const int BAR_H = 12;

    /// <summary>标题档字号（15 §4.4：只有 12／24 两档）。</summary>
    public const int TITLE_SIZE = 24;

    /// <summary>
    /// 顶栏（示意图 `.lu-topbar`）：**唯一的大块深色**（15 §4.4），下边线 2px 朱红。
    /// 左边地名（标题档），右边那一段给铜钱／经验这类常驻数字。
    /// `sub` 空着就不摆中间那一截（现在的存档里没有天时／天气，硬摆就是编）。
    /// </summary>
    public static Control top_bar(string place, string sub, string right_text, string node_name)
    {
        var bar = new PanelContainer
        {
            Name = node_name,
            CustomMinimumSize = new Vector2(0, TOP_H),
        };
        var style = _flat(UiKit.color("topbar_bg"));
        style.BorderWidthBottom = 2;
        style.BorderColor = UiKit.color("cinnabar");
        style.ContentMarginLeft = 18;
        style.ContentMarginRight = 18;
        bar.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer { Name = "TopRow" };
        row.AddThemeConstantOverride("separation", 18);
        bar.AddChild(row);

        var place_label = _label("Place", place, TITLE_SIZE, "topbar_text");
        row.AddChild(place_label);
        if (!string.IsNullOrEmpty(sub))
        {
            row.AddChild(_label("Sub", sub, 12, "topbar_dim"));
        }

        // 中段吃掉剩余宽度：右边的数字永远贴右（示意图是 `margin-left: auto`）
        row.AddChild(new Control { Name = "Spacer", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(_label("Right", right_text, 12, "topbar_money"));
        return bar;
    }

    /// <summary>
    /// 底栏（示意图 `.lu-bottombar`）：**七入口常驻**，当前所在那一项走朱红实底（`.lu-fn.is-on`）。
    /// `current_id` 取 `UiNav` 里的 id；`nav` 是「点了哪一个入口」的回调，收一个字符串实参。
    ///
    /// 没接回调也照摆（只做显示）——面板单独跑自检时就没有控制器，这时候按钮点了不响应、
    /// 但不能因此不画底栏。不能点的入口（`UiNav.disabled_reason` 非空）置灰并把原因写进 tooltip。
    /// </summary>
    public static Control bottom_bar(string current_id, Callable nav, string node_name)
    {
        var bar = new PanelContainer
        {
            Name = node_name,
            CustomMinimumSize = new Vector2(0, BOTTOM_H),
        };
        var style = _flat(UiKit.color("bottombar_bg"));
        style.BorderWidthTop = 1;
        style.BorderColor = UiKit.color("line");
        bar.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer { Name = "NavRow", Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);

        foreach (Variant entry_value in UiNav.entries())
        {
            Godot.Collections.Dictionary entry = entry_value.AsGodotDictionary();
            string id = entry["id"].AsString();
            bool on = id == current_id;
            string reason = UiNav.disabled_reason(id);
            row.AddChild(_nav_button(id, entry["label"].AsString(), entry["key"].AsString(), on, reason, nav));
        }

        return bar;
    }

    /// <summary>
    /// 一个底栏入口：`Button` ＋ 里面两个 `Label`（名字 ＋ 键位提示）。
    /// 键位提示走次要色——它是「鼠标路」的对照物，不该跟名字抢。
    /// </summary>
    private static Button _nav_button(
        string id, string label, string key, bool on, string reason, Callable nav)
    {
        var button = new Button
        {
            Name = UiNav.node_name_of(id),
            // 名字与键位提示写进 `Text`，**不要**用子 Label 拼：`Button` 的最小尺寸只按自己的
            // `Text`／图标算，**不含子节点**——子 Label 撑不出按钮，底栏会挤成一排小方块（踩过）。
            Text = key.Length > 0 ? $"{label} {key}" : label,
        };
        button.AddThemeStyleboxOverride("normal", _fn_style(false, on));
        button.AddThemeStyleboxOverride("hover", _fn_style(true, on));
        button.AddThemeStyleboxOverride("pressed", _fn_style(true, on));
        button.AddThemeStyleboxOverride("focus", _fn_style(true, on));
        button.AddThemeColorOverride("font_color", UiKit.color(on ? "on_text" : "ink"));
        button.AddThemeColorOverride("font_hover_color", UiKit.color(on ? "on_text" : "cinnabar"));
        button.AddThemeColorOverride("font_pressed_color", UiKit.color(on ? "on_text" : "cinnabar"));
        button.Disabled = reason.Length > 0;
        if (reason.Length > 0)
        {
            button.TooltipText = reason;
        }
        else
        {
            button.Pressed += () => _invoke(nav, id);
        }
        return button;
    }

    /// <summary>
    /// 浮窗外壳（示意图 `.lu-window`／`.lu-win-title`）：宣纸板 ＋ **深墨标题条（标题档 ＋ 右边一截副字）
    /// ＋ 2px 朱红下边线**，返回**内容列**给调用方往里加东西。
    /// 副字放「持有 5」「铜钱 320」这类常驻数字（示意图的 `.lu-win-sub`）。
    /// **壳由这里挂上去**（`parent` 是它该去的地方）——返回的只是内容列，
    /// 调用方拿不到壳本身；壳离了父级就是一棵游离的树，后面的 `AddChild` 会撞
    /// 「already has a parent」。
    /// </summary>
    public static VBoxContainer window(Node parent, string title, string sub, string node_name)
    {
        var panel = UiKit.make_panel(node_name);
        parent.AddChild(panel);
        var column = new VBoxContainer { Name = "WindowColumn" };
        column.AddThemeConstantOverride("separation", 0);
        panel.AddChild(column);

        var bar = new PanelContainer { Name = "WindowTitle" };
        var bar_style = _flat(UiKit.color("topbar_bg"));
        bar_style.BorderWidthBottom = 2;
        bar_style.BorderColor = UiKit.color("cinnabar");
        bar_style.ContentMarginLeft = 14;
        bar_style.ContentMarginRight = 14;
        bar_style.ContentMarginTop = 6;
        bar_style.ContentMarginBottom = 6;
        bar.AddThemeStyleboxOverride("panel", bar_style);
        column.AddChild(bar);

        var title_row = new HBoxContainer { Name = "TitleRow" };
        title_row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(title_row);
        title_row.AddChild(_label("WindowTitleLabel", title, TITLE_SIZE, "topbar_text"));
        if (!string.IsNullOrEmpty(sub))
        {
            Label sub_label = _label("WindowSub", sub, 12, "topbar_dim");
            sub_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            sub_label.HorizontalAlignment = HorizontalAlignment.Right;
            title_row.AddChild(sub_label);
        }

        var margin = new MarginContainer { Name = "WindowMargin" };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        column.AddChild(margin);

        var body = new VBoxContainer { Name = "WindowBody" };
        body.AddThemeConstantOverride("separation", 8);
        margin.AddChild(body);
        return body;
    }

    /// <summary>
    /// 底部说明行（示意图 `.lu-win-foot`）：左边一句状态／提示，右边键位提示。
    /// **键位提示必须写在界面上**（`TestCopyAudit.CheckHudMentionsEveryKey` 盯着：
    /// 用了 `is_action_pressed("xxx")` 就得在提示行里出现对应的键）。
    /// </summary>
    public static Control foot_row(string left_text, string right_text, string node_name)
    {
        var foot = new PanelContainer
        {
            Name = node_name,
            CustomMinimumSize = new Vector2(0, 28),
        };
        var style = _flat(new Color(UiKit.color("paper_bright"), 0.35f));
        style.BorderWidthTop = 1;
        style.BorderColor = UiKit.color("line_soft");
        style.ContentMarginLeft = 14;
        style.ContentMarginRight = 14;
        foot.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer { Name = "FootRow" };
        row.AddThemeConstantOverride("separation", 12);
        foot.AddChild(row);
        Label left = _label("FootLeft", left_text, 12, "ink_dim");
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(left);
        row.AddChild(_label("FootRight", right_text, 12, "ink_off"));
        return foot;
    }

    /// <summary>
    /// 往 `window()` 建出来的壳上补一条底部说明行——**贴在窗体下沿、跨整宽**（示意图里
    /// 那一行是画在窗体边线上的，塞进内容列会被 12px 边距缩进去）。壳是 `window()` 挂的、
    /// 它返回的只是中间那层内容列，所以这里从内容列往上摸到壳那一列再挂：
    /// **壳的内部结构只有这个文件知道**，别让每个浮层自己去摸父节点。
    /// </summary>
    public static Control window_foot(
        VBoxContainer body, string left_text, string right_text, string node_name)
    {
        Control foot = foot_row(left_text, right_text, node_name);
        if (body != null && body.GetParent() != null && body.GetParent().GetParent() is Node shell)
        {
            shell.AddChild(foot);
        }

        return foot;
    }

    /// <summary>
    /// 分类标签的底（示意图 `.lu-cat`）：`on` ＝ 朱红实底 ＋ 深朱描边——与底栏「当前项」
    /// 同一套色（`cinnabar`／`cinnabar_deep`）。分类标签是竖排一列，所以上下留白比底栏宽一点。
    /// </summary>
    public static StyleBoxFlat chip_style(bool on)
    {
        var style = UiKit.flat(on ? UiKit.color("cinnabar") : UiKit.color("paper"));
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        _border(style, 1, on ? UiKit.color("cinnabar_deep") : UiKit.color("line"));
        return style;
    }

    /// <summary>
    /// 格子的底（示意图 `.lu-cell`）：`selected` ＝ 朱红描边（选中的那一格）；`empty` ＝ 空格子
    /// ——**空格子也要画**，网格铺满才看得出「还剩几格」（那半截信息与「有几件」同等重要）。
    /// </summary>
    public static StyleBoxFlat cell_style(bool selected, bool empty)
    {
        // 空格子要**看得出来是空的**（比有东西那格淡一档）——不然玩家以为那儿也摆着东西、只是没图标
        var style = UiKit.flat(empty
            ? new Color(UiKit.color("paper_bright"), 0.22f)
            : UiKit.color("paper_bright"));
        _border(style, selected ? 2 : 1, selected
            ? UiKit.color("cinnabar")
            : empty ? UiKit.color("line_soft") : UiKit.color("line"));
        return style;
    }

    /// <summary>
    /// 一小节标题（示意图 `.lu-panel-h`）：墨色字 ＋ 下面一道细线。
    /// **18px 在这一版里没有**（15 §4.4：字集只有 12 的原生档与 24 的标题档，中间档会糊），
    /// 所以层级靠「墨色 ＋ 细线 ＋ 行距」拉开，不靠字号。
    /// </summary>
    public static VBoxContainer section_head(string title, string hint, string node_name)
    {
        var box = new VBoxContainer { Name = node_name };
        box.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer { Name = "SectionRow" };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(_label("SectionTitle", title, 12, "ink"));
        if (!string.IsNullOrEmpty(hint))
        {
            Label hint_label = _label("SectionHint", hint, 12, "ink_off");
            hint_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hint_label.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(hint_label);
        }

        box.AddChild(row);
        var rule = new ColorRect
        {
            Name = "SectionRule",
            Color = UiKit.color("line_soft"),
            CustomMinimumSize = new Vector2(0, 1),
        };
        box.AddChild(rule);
        return box;
    }

    /// <summary>
    /// 一条状态条（15 §4.4：高 12px；颜色是**信息色**——气血／架势／内力三支，换肤不动它们）。
    /// 左段是填充、右段是底槽，两段都带圆角；`ratio` 夹在 0～1。
    /// </summary>
    public static Control stat_bar(string color_name, float ratio, int width, string node_name)
    {
        float clamped = Mathf.Clamp(ratio, 0f, 1f);
        var row = new HBoxContainer
        {
            Name = node_name,
            CustomMinimumSize = new Vector2(width, BAR_H),
        };
        row.AddThemeConstantOverride("separation", 0);

        var fill = new PanelContainer
        {
            Name = "Fill",
            CustomMinimumSize = new Vector2(Mathf.Round(width * clamped), BAR_H),
        };
        fill.AddThemeStyleboxOverride("panel", _bar_style(UiKit.color(color_name)));
        row.AddChild(fill);

        var track = new PanelContainer { Name = "Track", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        track.AddThemeStyleboxOverride("panel", _bar_style(new Color(UiKit.color("ink"), 0.18f)));
        row.AddChild(track);
        return row;
    }

    private static StyleBoxFlat _bar_style(Color color)
    {
        StyleBoxFlat style = _flat(color);
        style.CornerRadiusTopLeft = 4;
        style.CornerRadiusTopRight = 4;
        style.CornerRadiusBottomLeft = 4;
        style.CornerRadiusBottomRight = 4;
        return style;
    }

    /// <summary>底栏入口的四态样式：常态只有一圈透明边（悬停才亮出朱红边），当前项走朱红实底。</summary>
    private static StyleBoxFlat _fn_style(bool hot, bool on)
    {
        StyleBoxFlat style = _flat(on
            ? UiKit.color("cinnabar_hot")
            : hot ? UiKit.color("paper_bright") : new Color(0, 0, 0, 0));
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        style.CornerRadiusTopLeft = 2;
        style.CornerRadiusTopRight = 2;
        style.CornerRadiusBottomLeft = 2;
        style.CornerRadiusBottomRight = 2;
        if (on)
        {
            style.BorderWidthLeft = 1;
            style.BorderWidthTop = 1;
            style.BorderWidthRight = 1;
            style.BorderWidthBottom = 1;
            style.BorderColor = UiKit.color("cinnabar_deep");
        }
        else if (hot)
        {
            style.BorderWidthLeft = 1;
            style.BorderWidthTop = 1;
            style.BorderWidthRight = 1;
            style.BorderWidthBottom = 1;
            style.BorderColor = UiKit.color("cinnabar");
        }

        return style;
    }

    private static StyleBoxFlat _flat(Color color)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.CornerRadiusTopLeft = 2;
        style.CornerRadiusTopRight = 2;
        style.CornerRadiusBottomLeft = 2;
        style.CornerRadiusBottomRight = 2;
        return style;
    }

    /// <summary>四边同宽的一道描边（`StyleBoxFlat` 的四条边要逐个设，写四遍容易漏一条）。</summary>
    private static void _border(StyleBoxFlat style, int width, Color color)
    {
        style.BorderWidthLeft = width;
        style.BorderWidthTop = width;
        style.BorderWidthRight = width;
        style.BorderWidthBottom = width;
        style.BorderColor = color;
    }

    private static Label _label(string node_name, string text, int font_size, string color_name)
    {
        var label = new Label { Name = node_name, Text = text };
        label.AddThemeFontSizeOverride("font_size", font_size);
        label.AddThemeColorOverride("font_color", UiKit.color(color_name));
        return label;
    }

    /// <summary>调底栏那个回调；没接就什么都不做（面板单独跑自检时没有控制器）。</summary>
    private static void _invoke(Callable callable, string id)
    {
        if (callable.Delegate == null && (callable.Target == null || callable.Method.IsEmpty))
        {
            return;
        }

        callable.Call(id);
    }
}
