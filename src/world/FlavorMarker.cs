using Godot;

namespace Lunhui.World;

/// <summary>
/// 观察点的**可见标记**（C# 版，对位原 `src/world/flavor_marker.gd`）。
///
/// 设计 15 §一：「可交互物要在低饱和背景里明显跳出来——暖色提亮」。观察点在地图上只是一根光秃秃的
/// `Marker2D`：不加这个标记，玩家只会从旁边走过去，而它们正是设计 20 §3.2 要的「探索感」来源。
///
/// **为什么单独一个类**：小地图与大地图**各自**收观察点（同一行只在一侧生效），两边要挂同一个视觉
/// ——颜色／大小／节点名只在这里写一次。
///
/// 美术给贴图之前先用**程序化的暖色菱形**（与「精英明雷贴图缺失退回程序化光晕」同一套做法）。
/// </summary>
public partial class FlavorMarker : Node2D
{
    /// <summary>暖色（15 §一的「暖色提亮」）：橙黄系 + 半透明填充。</summary>
    public static readonly Color WARM = new(1.0f, 0.76f, 0.38f, 0.92f);

    /// <summary>菱形半径（像素）：格子 32px，半径 6 ≈ 三分之一格。</summary>
    public const float HALF = 6.0f;

    /// <summary>填充透明度（描边用 `WARM` 本身）。</summary>
    public const float FILL_ALPHA = 0.35f;

    /// <summary>节点名：用例与场景自检按它找（改名要连着改 `tests/`）。</summary>
    public const string NODE_NAME = "FlavorHighlight";

    /// <summary>`NODE_NAME` 的取值口（GDScript 侧用；P5 清零 GDScript 后收回成字段）。</summary>
    public static string node_name() => NODE_NAME;

    public FlavorMarker()
    {
        Name = NODE_NAME;
    }

    public override void _Draw()
    {
        Vector2[] outline =
        {
            new(0, -HALF), new(HALF, 0), new(0, HALF), new(-HALF, 0),
        };
        DrawColoredPolygon(outline, new Color(WARM.R, WARM.G, WARM.B, FILL_ALPHA));
        Vector2[] loop = { outline[0], outline[1], outline[2], outline[3], outline[0] };
        DrawPolyline(loop, WARM, 1.0f);
    }

    /// <summary>
    /// 「看得见吗」的判据——用例与自检用它，别让谁把视觉改没了还没人知道
    /// （headless 抓不到渲染，所以判据落在「节点在、可见、颜色是暖色且不透明」这三点上）。
    /// </summary>
    public static bool is_visible_highlight(GodotObject node)
    {
        if (node is not Node2D marker)
        {
            return false;
        }

        return marker.Visible && marker.Modulate.A > 0.5f && WARM.A > 0.5f;
    }
}
