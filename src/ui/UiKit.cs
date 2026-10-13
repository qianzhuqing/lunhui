using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 界面共享构件（C# 版，对位原 `src/ui/ui_kit.gd`）：**卡片**（标题条 ＋ 副字 ＋ 内容列）。
///
/// 为什么单开一份：策划的 12 屏示意图（`docs/dev/UI示意落地清单.md`）里，每一屏都是
/// 「几张带标题条的卡片」（网页版的 `.lu-panel` ＋ `.lu-panel-h` ＋ `.lu-dim`），
/// 而现在的浮层是裸 `Label` 堆成一列——没有卡片就没有信息层级，看上去就"不像示意图"。
/// 卡片收在一处，**换皮肤只改 `assets/ui/theme_lunhui.tres` 里的 `UiKit/colors/*`**
/// （15 §4.4 的色值只写在那儿，决策 375），代码里不再抄第二份色值。
///
/// **`add_card` 只有一个四参签名**：C# 的默认参数不进 Godot 的方法签名（坑 87），
/// 而**重载也不行**——GDScript 解析脚本类的静态调用时，一个名字出现两次就报
/// `Static function "add_card()" not found in base "CSharpScript"`（坑 92），
/// 所以调用方一律把四个实参给全。
/// </summary>
public partial class UiKit : RefCounted
{
    public const string THEME_PATH = "res://assets/ui/theme_lunhui.tres";

    /// <summary>主题资源只读一次（每建一张卡都 `load()` 一次没必要；`ResourceLoader` 本来也缓存）。</summary>
    private static Theme _theme_cache;

    /// <summary>卡片／标题条／小节的色值**统一从这里取**——色值表在主题资源里，不在代码里抄第二份。</summary>
    public static Color color(string name)
    {
        Theme theme = _theme();
        if (theme != null && theme.HasColor(name, "UiKit"))
        {
            return theme.GetColor(name, "UiKit");
        }

        GD.PushError($"[UiKit] 主题 {THEME_PATH} 里没有 UiKit/colors/{name}");
        return Colors.Magenta;
    }

    /// <summary>
    /// 一块卡片：外面是面板九宫格背板（`assets/ui/common/panel_bg.png`），
    /// 顶上一条**标题条**，返回**内容列**给调用方往里加行。
    /// `hint` 是标题后面那截次要字（示意图里的 `.lu-dim`，例：「心法占格　已占 13 / 14」）。
    /// `node_name` 空着就用 `Card`——**四个实参都要给**（见类注释的坑 92）。
    /// </summary>
    public static VBoxContainer add_card(Node parent, string title, string hint, string node_name)
    {
        var card = new PanelContainer
        {
            Name = string.IsNullOrEmpty(node_name) ? "Card" : node_name,
        };
        card.AddThemeStyleboxOverride("panel", panel_style());
        var column = new VBoxContainer { Name = "CardColumn" };
        column.AddThemeConstantOverride("separation", 4);
        card.AddChild(column);
        if (!string.IsNullOrEmpty(title))
        {
            column.AddChild(_header(title, hint));
        }

        var body = new VBoxContainer { Name = "Body" };
        body.AddThemeConstantOverride("separation", 4);
        column.AddChild(body);
        parent.AddChild(card);
        return body;
    }

    /// <summary>
    /// 把浮层的背板刷成宣纸（色值在主题 `UiKit/colors/backdrop` **一处**）。
    ///
    /// 面板过去是「比地图更沉」的近黑，8 个浮层各在 `.tscn`／代码里写一份 `Color(0.07,0.08,0.1,0.97)`
    /// ——皮肤一翻成宣纸，那 8 份色值就是散在八个文件里的旧口径。现在统一由这里刷：
    /// 浮层挂上屏幕时（`OverlayStack.mount()`）按名字找到 `Backdrop` 调一次。
    /// **刷不上不报错**：面板没有背板这件事 `LayoutBudget.has_opaque_backdrop()` 已经在盯着。
    /// </summary>
    public static void paint_backdrop(Node screen)
    {
        if (screen == null)
        {
            return;
        }

        if (screen.FindChild("Backdrop", true, false) is ColorRect backdrop)
        {
            backdrop.Color = color("backdrop");
        }
    }

    /// <summary>
    /// 一块**空面板**（不带标题条）：主题里那件九宫格背板（`panel_bg.png`）＋ 1px 描边。
    /// 浮窗外壳（`UiChrome.window`）与自绘的行卡片都用它当底板——底板只有一个来源，
    /// 各面板别再自己 `load()` 贴图拼九宫格。
    /// </summary>
    public static PanelContainer make_panel(string node_name)
    {
        var panel = new PanelContainer { Name = node_name };
        panel.AddThemeStyleboxOverride("panel", panel_style());
        return panel;
    }

    /// <summary>
    /// 一块**纯色底**（圆角 2）：计数徽标、分类标签、格子这类比九宫格小得多的色块用它——
    /// 一张 32px 的格子挂 9-patch 背板会把边缘糊掉。色值仍然只从 `UiKit/colors/*` 取。
    /// </summary>
    public static StyleBoxFlat flat(Color color)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.CornerRadiusTopLeft = 2;
        style.CornerRadiusTopRight = 2;
        style.CornerRadiusBottomLeft = 2;
        style.CornerRadiusBottomRight = 2;
        return style;
    }

    /// <summary>
    /// 面板背板：主题里那件九宫格 `StyleBoxTexture`（15 §4.4 的 `panel_bg.png`）。
    /// 主题缺了它也不要红——退回一块同色底的方块，界面还能看。
    /// **公开出来是给悬停小卡的**（`HoverCard`）：清单 §二-12 点名小卡「与面板同一套观感、
    /// 不用新出图」，所以卡与 `add_card()`／`make_panel()` 共用这一件，
    /// 各屏别再自己 `load()` 贴图拼九宫格。
    /// </summary>
    public static StyleBox panel_style()
    {
        Theme theme = _theme();
        if (theme != null && theme.HasStylebox("panel", "PanelContainer"))
        {
            return theme.GetStylebox("panel", "PanelContainer");
        }

        return new StyleBoxFlat { BgColor = new Color(0.929412f, 0.890196f, 0.796078f, 1.0f) };
    }

    /// <summary>
    /// 标题条：照示意图的 `.lu-panel-h`——**不是一条深色带**（深色带是旧皮肤的事），
    /// 而是「墨色标题 ＋ 右边一截次要字 ＋ 下面一道 1px 细线」。
    /// 标题走 `ink` 而不是 `gold`：15 §4.4 的枯黄在宣纸底上对比度只有 1.7：1，读不清。
    /// </summary>
    private static VBoxContainer _header(string title, string hint)
    {
        var box = new VBoxContainer { Name = "Header" };
        box.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer { Name = "HeaderRow" };
        row.AddThemeConstantOverride("separation", 8);
        var title_label = new Label { Name = "HeaderTitle", Text = title };
        title_label.AddThemeColorOverride("font_color", color("ink"));
        row.AddChild(title_label);
        if (!string.IsNullOrEmpty(hint))
        {
            var hint_label = new Label { Name = "HeaderHint", Text = hint };
            hint_label.AddThemeColorOverride("font_color", color("ink_dim"));
            hint_label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hint_label.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(hint_label);
        }

        box.AddChild(row);
        var rule = new ColorRect
        {
            Name = "HeaderRule",
            Color = color("line"),
            CustomMinimumSize = new Vector2(0, 1),
        };
        box.AddChild(rule);
        return box;
    }

    private static Theme _theme()
    {
        _theme_cache ??= GD.Load<Theme>(THEME_PATH);
        return _theme_cache;
    }
}
