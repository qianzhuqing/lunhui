using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 地图工具箱的**绘制与地形算法**那一半（对位原 `map_kit.gd` 的「地形算法／建筑／迷雾」三节）。
/// 与 `MapKit.cs`／`MapKitExport.cs` 是同一个 `partial` 类，只是按用途分开写——原文件 876 行，
/// 撞了「单文件 800 行」的硬上限（`docs/dev/代码拆分清单.md`）。
/// </summary>
public partial class MapKit : RefCounted
{
    // ---------------------------------------------------------------- 地形算法

    public static void mark_rect(Godot.Collections.Dictionary cells, Rect2I rect)
    {
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
            {
                cells[new Vector2I(x, y)] = true;
            }
        }
    }

    public static void mark_disc(Godot.Collections.Dictionary cells, Vector2I center, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy <= radius * radius)
                {
                    cells[center + new Vector2I(dx, dy)] = true;
                }
            }
        }
    }

    /// <summary>L 形走廊：先横后竖，宽度给 3 格，保证「至少 2 格可通行」的背袭空间。</summary>
    public static void carve_corridor(
        Godot.Collections.Dictionary cells, Rect2I a, Rect2I b, int width = 3)
    {
        var start = _rect_center(a);
        var end = _rect_center(b);
        int x0 = System.Math.Min(start.X, end.X);
        int x1 = System.Math.Max(start.X, end.X);
        mark_rect(cells, new Rect2I(x0, start.Y, x1 - x0 + 1, width));
        int y0 = System.Math.Min(start.Y, end.Y);
        int y1 = System.Math.Max(start.Y, end.Y);
        mark_rect(cells, new Rect2I(end.X, y0, width, y1 - y0 + 1));
    }

    private static Vector2I _rect_center(Rect2I rect) =>
        new(rect.Position.X + rect.Size.X / 2, rect.Position.Y + rect.Size.Y / 2);

    /// <summary>自动砌墙：贴着地板一圈、又不属于地板的格子全变墙。省得手画墙线还漏角。</summary>
    public static Godot.Collections.Dictionary wall_shell(Godot.Collections.Dictionary floor_cells)
    {
        var walls = new Godot.Collections.Dictionary();
        foreach (Variant cellVariant in floor_cells.Keys)
        {
            var cell = cellVariant.AsVector2I();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var neighbour = cell + new Vector2I(dx, dy);
                    if (!floor_cells.ContainsKey(neighbour))
                    {
                        walls[neighbour] = true;
                    }
                }
            }
        }

        return walls;
    }

    /// <summary>把土路／空地按四邻挑边缘瓦片，接缝才自然。</summary>
    public static void paint_dirt(
        TileMapLayer layer, Godot.Collections.Dictionary cells, int map_w, int map_h)
    {
        for (int y = 0; y < map_h; y++)
        {
            for (int x = 0; x < map_w; x++)
            {
                var cell = new Vector2I(x, y);
                if (cells.ContainsKey(cell))
                {
                    layer.SetCell(cell, SOURCE_ID, _dirt_tile(cells, cell));
                }
            }
        }
    }

    private static Vector2I _dirt_tile(Godot.Collections.Dictionary cells, Vector2I cell)
    {
        bool up = !cells.ContainsKey(cell + Vector2I.Up);
        bool down = !cells.ContainsKey(cell + Vector2I.Down);
        bool left = !cells.ContainsKey(cell + Vector2I.Left);
        bool right = !cells.ContainsKey(cell + Vector2I.Right);
        if (up && left)
        {
            return T_DIRT_TL;
        }

        if (up && right)
        {
            return T_DIRT_TR;
        }

        if (down && left)
        {
            return T_DIRT_BL;
        }

        if (down && right)
        {
            return T_DIRT_BR;
        }

        if (up)
        {
            return T_DIRT_T;
        }

        if (down)
        {
            return T_DIRT_B;
        }

        if (left)
        {
            return T_DIRT_L;
        }

        if (right)
        {
            return T_DIRT_R;
        }

        return T_DIRT;
    }

    /// <summary>户外底子：整张图铺草，再按散列加草簇／花。</summary>
    public static void paint_grass(TileMapLayer layer, int map_w, int map_h)
    {
        for (int y = 0; y < map_h; y++)
        {
            for (int x = 0; x < map_w; x++)
            {
                layer.SetCell(new Vector2I(x, y), SOURCE_ID, grass_tile(x, y));
            }
        }
    }

    public static Vector2I grass_tile(int x, int y)
    {
        double roll = hash01(x, y);
        if (roll > 0.94)
        {
            return T_GRASS_FLOWER;
        }

        if (roll > 0.72)
        {
            return T_GRASS_TUFT;
        }

        return T_GRASS;
    }

    public static void paint_cells(
        TileMapLayer layer, Godot.Collections.Dictionary cells, Vector2I tile)
    {
        foreach (Variant cell in cells.Keys)
        {
            layer.SetCell(cell.AsVector2I(), SOURCE_ID, tile);
        }
    }

    /// <summary>山体九宫格：`cells` 是「属于山体」的格子表，按四邻挑边缘瓦片（与土路 `paint_dirt` 同一套用法）。
    /// 上脊只在最上一排、坡脚只在最下一排——铺出来才是「一座山」，而不是一块块地砖。</summary>
    public static Vector2I mountain_tile(Godot.Collections.Dictionary cells, Vector2I cell)
    {
        bool up = !cells.ContainsKey(cell + Vector2I.Up);
        bool down = !cells.ContainsKey(cell + Vector2I.Down);
        bool left = !cells.ContainsKey(cell + Vector2I.Left);
        bool right = !cells.ContainsKey(cell + Vector2I.Right);
        if (up && left)
        {
            return T_MTN_TL;
        }

        if (up && right)
        {
            return T_MTN_TR;
        }

        if (down && left)
        {
            return T_MTN_BL;
        }

        if (down && right)
        {
            return T_MTN_BR;
        }

        if (up)
        {
            return T_MTN_T;
        }

        if (down)
        {
            return T_MTN_B;
        }

        if (left)
        {
            return T_MTN_L;
        }

        if (right)
        {
            return T_MTN_R;
        }

        // 一片山会铺几百格：`C`／`C2` 交替才不露「瓦片格子」
        return hash01(cell.X, cell.Y) > 0.5 ? T_MTN_C2 : T_MTN_C;
    }

    /// <summary>铺一片山（16 §3.5「成片、有厚度」）：九宫格 ＋ 内部稀疏插山峰。
    /// 山峰只插在**四邻都是山**的内部格、概率也很低——一屏（16×9 格）1–3 座就够，
    /// 密了会把山面打成筛子（`tools/artgen/README.md` 那条画法教训：整片同色 = 一面灰墙）。</summary>
    public static void paint_mountains(TileMapLayer decor, Godot.Collections.Dictionary cells)
    {
        foreach (Variant cellVariant in cells.Keys)
        {
            var cell = cellVariant.AsVector2I();
            if (_is_peak_cell(cells, cell))
            {
                bool high = hash01(cell.X * 5 + 1, cell.Y * 7 + 3) > 0.5;
                decor.SetCell(cell, SOURCE_ID, high ? T_MTN_PEAK_HIGH : T_MTN_PEAK_LOW);
                continue;
            }

            decor.SetCell(cell, SOURCE_ID, mountain_tile(cells, cell));
        }
    }

    private static bool _is_peak_cell(Godot.Collections.Dictionary cells, Vector2I cell)
    {
        foreach (var offset in new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right })
        {
            if (!cells.ContainsKey(cell + offset))
            {
                return false;
            }
        }

        return hash01(cell.X + 31, cell.Y + 977) < 0.022;
    }

    /// <summary>这一格是不是挡路的（看 TileSet 的自定义数据层 blocked）。</summary>
    public static bool is_blocked(TileMapLayer layer, Vector2I cell)
    {
        if (layer == null || layer.TileSet == null)
        {
            return false;
        }

        int sourceId = layer.GetCellSourceId(cell);
        if (sourceId == -1)
        {
            return false;
        }

        if (layer.TileSet.GetSource(sourceId) is not TileSetAtlasSource source)
        {
            return false;
        }

        var data = source.GetTileData(
            layer.GetCellAtlasCoords(cell), layer.GetCellAlternativeTile(cell));
        return data != null && data.GetCustomData("blocked").AsBool();
    }

    /// <summary>房间入口内侧的落点：找一个开口，从门口往里退 2 格、再横向错 1 格。
    /// 这样敌人「进门能看见」，又不正对门口站着挡路（07 文档第五节：别堵住侧向通路）。</summary>
    public static Vector2I room_entrance_inside(
        TileMapLayer decor, Godot.Collections.Dictionary floor_cells, Rect2I rect)
    {
        foreach (Variant probeVariant in _edge_probes(rect))
        {
            var probe = probeVariant.AsGodotDictionary();
            var cell = probe["cell"].AsVector2I();
            if (floor_cells.ContainsKey(cell) && !is_blocked(decor, cell))
            {
                return cell + probe["inward"].AsVector2I() * 2 + probe["side"].AsVector2I();
            }
        }

        return _rect_center(rect);
    }

    /// <summary>房间四条边上的格子和「往里」的方向，按上→下→左→右顺序探。</summary>
    private static Godot.Collections.Array _edge_probes(Rect2I rect)
    {
        var probes = new Godot.Collections.Array();
        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        {
            probes.Add(new Godot.Collections.Dictionary
            {
                { "cell", new Vector2I(x, rect.Position.Y - 1) },
                { "inward", Vector2I.Down },
                { "side", Vector2I.Right },
            });
        }

        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        {
            probes.Add(new Godot.Collections.Dictionary
            {
                { "cell", new Vector2I(x, rect.Position.Y + rect.Size.Y) },
                { "inward", Vector2I.Up },
                { "side", Vector2I.Right },
            });
        }

        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            probes.Add(new Godot.Collections.Dictionary
            {
                { "cell", new Vector2I(rect.Position.X - 1, y) },
                { "inward", Vector2I.Right },
                { "side", Vector2I.Down },
            });
        }

        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            probes.Add(new Godot.Collections.Dictionary
            {
                { "cell", new Vector2I(rect.Position.X + rect.Size.X, y) },
                { "inward", Vector2I.Left },
                { "side", Vector2I.Down },
            });
        }

        return probes;
    }

    /// <summary>自然边界：用密林封边，看起来像山林而不是空气墙。</summary>
    public static void forest_border(
        TileMapLayer decor, Godot.Collections.Dictionary skip, int map_w, int map_h,
        int thickness = 2)
    {
        for (int y = 0; y < map_h; y++)
        {
            for (int x = 0; x < map_w; x++)
            {
                bool edge = x < thickness || x >= map_w - thickness
                    || y < thickness || y >= map_h - thickness;
                if (!edge || skip.ContainsKey(new Vector2I(x, y)))
                {
                    continue;
                }

                decor.SetCell(new Vector2I(x, y), SOURCE_ID, forest_tile(x, y));
            }
        }
    }

    public static Vector2I forest_tile(int x, int y)
    {
        double roll = hash01(x * 7 + 3, y * 5 + 11);
        if (roll > 0.72)
        {
            return T_FOREST_AUTUMN;
        }

        if (roll > 0.34)
        {
            return T_FOREST;
        }

        return T_TREE_PINE;
    }

    public static Vector2I tree_tile(int x, int y)
    {
        double roll = hash01(x * 3 + 1, y * 11 + 7);
        if (roll > 0.86)
        {
            return T_MUSHROOM;
        }

        if (roll > 0.66)
        {
            return T_TREE_AUTUMN;
        }

        if (roll > 0.46)
        {
            return T_TREE_ROUND;
        }

        if (roll > 0.30)
        {
            return T_BUSH;
        }

        return T_TREE_PINE;
    }

    public static void scatter_trees(
        TileMapLayer decor, Godot.Collections.Dictionary blocked,
        Godot.Collections.Dictionary reserved, int map_w, int map_h, double chance = 0.05)
    {
        for (int y = 2; y < map_h - 2; y++)
        {
            for (int x = 2; x < map_w - 2; x++)
            {
                var cell = new Vector2I(x, y);
                if (blocked.ContainsKey(cell) || reserved.ContainsKey(cell)
                    || decor.GetCellSourceId(cell) != -1)
                {
                    continue;
                }

                if (hash01(x * 13 + 5, y * 17 + 9) > chance)
                {
                    continue;
                }

                decor.SetCell(cell, SOURCE_ID, tree_tile(x, y));
            }
        }
    }

    /// <summary>
    /// 稳定的散列噪声（0…1）。**返回 double 而不是 float**：GDScript 的 `float` 就是 64 位，
    /// 生成器里那句 `int(hash01(…) * 3.0)` 卡在整数边界上——收成 32 位会改出另一张图。
    /// 全程按 64 位整数算，`&amp; 0x7FFFFFFF` 之后再散，与原实现逐位一致。
    /// </summary>
    public static double hash01(int x, int y)
    {
        long h = (x * 374761393L + y * 668265263L) & 0x7FFFFFFFL;
        h = (h ^ (h >> 13)) * 1274126177L;
        return (double)(h & 0xFFFFL) / 65535.0;
    }

    // ---------------------------------------------------------------- 建筑

    /// <summary>门朝下（房子在北侧、门前街道在南侧）：墙在 y，屋顶在 y-2 / y-1。</summary>
    public static void house_down(
        TileMapLayer decor, int x, int y, Godot.Collections.Dictionary style)
    {
        _house_front(decor, x, y, style);
        for (int i = 0; i < 4; i++)
        {
            decor.SetCell(new Vector2I(x + i, y - 1), SOURCE_ID, style["roof"].AsVector2I());
            decor.SetCell(new Vector2I(x + i, y - 2), SOURCE_ID, style["roof_top"].AsVector2I());
        }
    }

    /// <summary>门朝上（房子在南侧）：墙在 y，屋顶摆到 y+1 / y+2。</summary>
    public static void house_up(TileMapLayer decor, int x, int y, Godot.Collections.Dictionary style)
    {
        _house_front(decor, x, y, style);
        for (int i = 0; i < 4; i++)
        {
            decor.SetCell(new Vector2I(x + i, y + 1), SOURCE_ID, style["roof"].AsVector2I());
            decor.SetCell(new Vector2I(x + i, y + 2), SOURCE_ID, style["roof_top"].AsVector2I());
        }
    }

    private static void _house_front(
        TileMapLayer decor, int x, int y, Godot.Collections.Dictionary style)
    {
        decor.SetCell(new Vector2I(x, y), SOURCE_ID, style["wall"].AsVector2I());
        decor.SetCell(new Vector2I(x + 1, y), SOURCE_ID, style["window"].AsVector2I());
        decor.SetCell(new Vector2I(x + 2, y), SOURCE_ID, style["wall"].AsVector2I());
        decor.SetCell(new Vector2I(x + 3, y), SOURCE_ID, style["door"].AsVector2I());
    }

    // ---------------------------------------------------------------- 迷雾

    /// <summary>铺迷雾：clear 里的格子保持可见（开局已探明的地标周边）。</summary>
    public static void paint_fog(
        TileMapLayer fog, int map_w, int map_h, Godot.Collections.Dictionary clear)
    {
        for (int y = 0; y < map_h; y++)
        {
            for (int x = 0; x < map_w; x++)
            {
                var cell = new Vector2I(x, y);
                if (clear.ContainsKey(cell))
                {
                    continue;
                }

                fog.SetCell(cell, PLACEHOLDER_SOURCE_ID, T_FOG);
            }
        }
    }
}
