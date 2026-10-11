using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 界面约定（三条）：版式预算 + 背板 + 滚动内容宽度（C# 版，对位原 `src/ui/layout_budget.gd`）。
///
/// 1. **版式预算**：整页最小尺寸要**两个方向**都塞得进设计分辨率（1152×648）。界面「塞不下」肉眼很难发现——
///    内容只会被屏幕底部/右侧裁掉，代码一声不吭。战斗界面最早就是这么坏的（标题被裁掉半行，711 > 648）。
///    2026-10-03 补：以前只量**高度**——`DESIGN_WIDTH` 声明了却没人用过，横向膨胀（多一列、按钮变长、
///    敌人卡片加宽）不会被任何用例发现。现在 `fits()` 两个方向一起量，`LAYOUT` 行也把宽度打出来。
/// 2. **背板**：这些面板是**盖在游戏画面上**的（Tab／按 E 打开），没有不透明背板就会让地图、
///    NPC 和地图自己的 HUD 透上来，把面板文字糊成一团（视觉自查抓到的真问题）。
/// 3. **页签内容宽度**（2026-10-03 补）：面板里的滚动区都是**只滚竖向**（`horizontal_scroll_mode` 关着），
///    所以内容一宽就被裁掉，而外层量不出来——`get_combined_minimum_size()` 到 `ScrollContainer` 就断了，
///    量到的是面板外壳（角色面板那个 760 其实来自 `_tabs.custom_minimum_size` 这个写死的常量）。
///    这是「量不出来 ≠ 塞得下」的又一例：**外面全绿，里面照样被切掉半行**。
///    这条量的是滚动区里那个内容节点的最小宽度，调用方要当门限用。
///
/// 这三条现在都是**绊线**：量过、全过，但以后谁改坏就会立刻红。
///
/// **常量走取值口**：GDScript 读不到 C# 的 `const`（坑 22），`design_width()` 这几个方法就是那条通道。
/// **不带附加参数的那一档才叫原名**：C# 的默认参数不进 Godot 的签名（坑 87），
/// 而**重载也不行**——GDScript 解析脚本类的静态调用时，一个名字出现两次就报
/// `Static function "…" not found in base "CSharpScript"`（坑 92），
/// 所以带 tag／limit 的那几档各改一个名字（`ascii_line_tagged` 等）。
/// </summary>
public partial class LayoutBudget : RefCounted
{
    public const int DESIGN_WIDTH = 1152;
    public const int DESIGN_HEIGHT = 648;
    /// <summary>背板至少要这么不透明，玩家的字才不会被场景糊掉。</summary>
    public const double BACKDROP_MIN_ALPHA = 0.9;

    public static int design_width() => DESIGN_WIDTH;
    public static int design_height() => DESIGN_HEIGHT;
    public static double backdrop_min_alpha() => BACKDROP_MIN_ALPHA;

    /// <summary>
    /// 量「整页最小高度」：优先 MarginContainer（面板的外边距容器），退而找第一个 Column。
    /// 找不到可量的节点返回 0——调用方要把它当成失败（量不出来 ≠ 塞得下）。
    /// </summary>
    public static double min_height(Node screen)
    {
        Control target = measure_target(screen);
        return target == null ? 0.0 : target.GetCombinedMinimumSize().Y;
    }

    /// <summary>量「整页最小宽度」——同一个可量节点的 x（见上：宽度以前从来没量过）。</summary>
    public static double min_width(Node screen)
    {
        Control target = measure_target(screen);
        return target == null ? 0.0 : target.GetCombinedMinimumSize().X;
    }

    public static Control measure_target(Node screen)
    {
        if (screen == null)
        {
            return null;
        }

        foreach (Node node in screen.FindChildren("Margin", "MarginContainer", true, false))
        {
            return node as Control;
        }

        foreach (Node node in screen.FindChildren("Column", "VBoxContainer", true, false))
        {
            return node as Control;
        }

        return null;
    }

    /// <summary>塞得下吗：**两个方向**都量得出来（&gt;0）且不超设计分辨率才算过。</summary>
    public static bool fits(Node screen)
    {
        Control target = measure_target(screen);
        if (target == null)
        {
            return false;
        }

        Vector2 size = target.GetCombinedMinimumSize();
        return size.X > 0.0 && size.Y > 0.0
            && size.X <= DESIGN_WIDTH && size.Y <= DESIGN_HEIGHT;
    }

    public static string ascii_line(Node screen) => ascii_line_tagged(screen, "");

    /// <summary>
    /// 机器可读的一行：验收脚本用 `findstr LAYOUT` 抓它。
    /// 故意用 ASCII 而不用中文——cmd 的 findstr 按控制台代码页匹配，中文匹配不到（踩过）。
    /// </summary>
    public static string ascii_line_tagged(Node screen, string tag)
    {
        Control target = measure_target(screen);
        Vector2 size = target == null ? Vector2.Zero : target.GetCombinedMinimumSize();
        string label = string.IsNullOrEmpty(tag) ? "LAYOUT" : $"LAYOUT[{tag}]";
        bool ok = size.X > 0.0 && size.Y > 0.0 && size.X <= DESIGN_WIDTH && size.Y <= DESIGN_HEIGHT;
        return $"{label} min_width={size.X:0} min_height={size.Y:0}"
            + $" limit={DESIGN_WIDTH}x{DESIGN_HEIGHT} ok={_bool(ok)}";
    }

    public static bool has_opaque_backdrop(Node screen) => has_opaque_backdrop_min(screen, BACKDROP_MIN_ALPHA);

    /// <summary>盖在游戏画面上的面板有没有够不透明的背板（`ColorRect` 名为 `Backdrop`）。</summary>
    public static bool has_opaque_backdrop_min(Node screen, double min_alpha)
    {
        ColorRect backdrop = backdrop_of(screen);
        return backdrop != null && backdrop.Color.A >= min_alpha;
    }

    public static ColorRect backdrop_of(Node screen)
    {
        if (screen == null)
        {
            return null;
        }

        return screen.FindChild("Backdrop", true, false) as ColorRect;
    }

    /// <summary>同上：`findstr BACKDROP` 抓这一行。</summary>
    public static string ascii_backdrop_line(Node screen)
    {
        ColorRect backdrop = backdrop_of(screen);
        double alpha = backdrop == null ? -1.0 : backdrop.Color.A;
        bool ok = backdrop != null && alpha >= BACKDROP_MIN_ALPHA;
        return $"BACKDROP alpha={alpha:0.00} min={BACKDROP_MIN_ALPHA:0.00} ok={_bool(ok)}";
    }

    /// <summary>
    /// 页签内容的最小宽度：滚动区是「只滚竖向」的，内容比面板宽就会被裁掉。
    ///
    /// 量法：取每个 `ScrollContainer` 里那个内容 Control 的 `get_combined_minimum_size().x`，取最大值。
    /// 一个滚动区都没有（或里面没有 Control）返回 0——**调用方要把它当成失败**
    /// （同 `min_height` 的口径：量不出来 ≠ 塞得下）。
    ///
    /// **小值不代表没量到**：带 `autowrap` 的 Label 最小宽度只有 1 个字形（实测 224px 的句子 → 最小宽度 1px），
    /// 因为长文本会换行、不会被横向裁掉——那是安全的。这条量的是**不换行的内容**：
    /// 按钮、固定宽卡片、显式设过 `custom_minimum_size` 的行（这几类宽了才是真会被裁）。
    /// </summary>
    public static double content_min_width(Node screen)
    {
        if (screen == null)
        {
            return 0.0;
        }

        double widest = 0.0;
        foreach (Node node in screen.FindChildren("*", "ScrollContainer", true, false))
        {
            if (node is not ScrollContainer container)
            {
                continue;
            }

            foreach (Node child in container.GetChildren())
            {
                if (child is not Control content)
                {
                    continue;
                }

                widest = Mathf.Max(widest, content.GetCombinedMinimumSize().X);
                break;
            }
        }

        return widest;
    }

    public static bool content_fits(Node screen) => content_fits_limit(screen, DESIGN_WIDTH);

    /// <summary>
    /// 内容塞得进吗：量得出来（&gt; 0）且不超过面板可用宽度。
    ///
    /// 全屏面板的可用宽度是 `DESIGN_WIDTH` 减掉左右边距，这里取设计宽度这个**上限**（宁松勿假绿）：
    /// 连 1152 都超的内容一定是被裁掉的，先让这一层红，再按面板实际边距收紧。
    /// </summary>
    public static bool content_fits_limit(Node screen, double limit)
    {
        double width = content_min_width(screen);
        return width > 0.0 && width <= limit;
    }

    public static string ascii_content_line(Node screen) => ascii_content_line_limit(screen, DESIGN_WIDTH);

    /// <summary>机器可读的一行：验收脚本用 `findstr CONTENT` 抓它（ASCII 的理由同上）。</summary>
    public static string ascii_content_line_limit(Node screen, double limit)
    {
        double width = content_min_width(screen);
        return $"CONTENT min_width={width:0} limit={limit:0} ok={_bool(width > 0.0 && width <= limit)}";
    }

    /// <summary>
    /// 把一块内容（左上角 `point`、尺寸 `item`）夹进 `bounds` 画布里，返回夹好之后的左上角。
    ///
    /// 为什么要有它：跳字（伤害数字）是按「卡片位置 + 固定偏移」摆的，而卡片靠右时那个偏移会把数字
    /// **推出屏幕**——满招式那档的版式预算实测内容宽已经到 1134，离设计宽度 1152 只剩 18px。
    /// 夹一下的代价是"数字可能压在卡片上"，但比"数字看不见"好。
    /// `bounds` 非正（还没排版，headless 里常见）时原样返回，别把位置夹成 0。
    /// </summary>
    public static Vector2 clamp_into(Vector2 bounds, Vector2 point, Vector2 item)
    {
        if (bounds.X <= 0.0 || bounds.Y <= 0.0)
        {
            return point;
        }

        return new Vector2(
            Mathf.Clamp(point.X, 0.0f, Mathf.Max(0.0f, bounds.X - item.X)),
            Mathf.Clamp(point.Y, 0.0f, Mathf.Max(0.0f, bounds.Y - item.Y)));
    }

    private static string _bool(bool value) => value ? "true" : "false";
}
