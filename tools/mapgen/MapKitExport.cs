using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 地图工具箱的**存盘、预览与 CSV**那一半（对位原 `map_kit.gd` 的「存盘与预览」「CSV」两节）。
/// 拆法见 `MapKitPaint.cs` 的说明。
/// </summary>
public partial class MapKit : RefCounted
{
    // ---------------------------------------------------------------- 存盘与预览

    /// <summary>存场景，返回 `Error` 码（0 = OK）——生成器照旧拿它跟 `OK` 比。</summary>
    public static int save_scene(Node root, string path)
    {
        // PackedScene 只会保存 owner 指向根节点的子树，漏设就会得到空场景。
        _assign_owner(root, root);
        var scene = new PackedScene();
        var packError = scene.Pack(root);
        if (packError != Error.Ok)
        {
            return (int)packError;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        return (int)ResourceSaver.Save(scene, path);
    }

    private static void _assign_owner(Node node, Node root)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;
            _assign_owner(child, root);
        }
    }

    public static void dump_preview(
        string scene_path, string dump_path, int map_w, int map_h)
    {
        var scene = GD.Load<PackedScene>(scene_path);
        if (scene == null)
        {
            return;
        }

        var root = scene.Instantiate();
        // 四个清单先握在本地变量里再塞进 payload：`payload["layers"].AsGodotArray()` 拿到的是
        // **另一个 Array 包装**，往里 Add 未必回到 payload 那一份（Godot 的 Array 是值语义的包装）。
        var layers = new Godot.Collections.Array();
        var markers = new Godot.Collections.Array();
        var paths = new Godot.Collections.Array();
        var sprites = new Godot.Collections.Array();
        var payload = new Godot.Collections.Dictionary
        {
            { "tile_px", TILE_PX },
            { "atlas", _scene_atlas_path(root) },
            {
                "sources", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "id", SOURCE_ID }, { "texture", _scene_atlas_path(root) },
                    },
                    new Godot.Collections.Dictionary
                    {
                        { "id", PLACEHOLDER_SOURCE_ID }, { "texture", FOG_ATLAS },
                    },
                }
            },
            { "map_w", map_w },
            { "map_h", map_h },
            { "layers", layers },
            { "markers", markers },
            { "paths", paths },
            { "sprites", sprites },
        };
        _collect(root, layers, markers, paths, sprites);
        var file = FileAccess.Open(dump_path, FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(Json.Stringify(payload));
            file.Close();
        }

        root.Free();
    }

    /// <summary>预览脚本要按主题取图集，而这里的入口只有场景对象——所以从场景的 TileSet 里反查。</summary>
    private static string _scene_atlas_path(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is not TileMapLayer layer || layer.TileSet == null)
            {
                continue;
            }

            var source = layer.TileSet.GetSource(SOURCE_ID);
            if (source is TileSetAtlasSource atlasSource && atlasSource.Texture != null)
            {
                return atlasSource.Texture.ResourcePath;
            }
        }

        return "";
    }

    private static void _collect(
        Node node, Godot.Collections.Array layers, Godot.Collections.Array markers,
        Godot.Collections.Array paths, Godot.Collections.Array sprites)
    {
        if (node is TileMapLayer layer)
        {
            var cells = new Godot.Collections.Array();
            foreach (Vector2I cell in layer.GetUsedCells())
            {
                var atlas = layer.GetCellAtlasCoords(cell);
                int source = layer.GetCellSourceId(cell);
                cells.Add(new Godot.Collections.Array { cell.X, cell.Y, source, atlas.X, atlas.Y });
            }

            layers.Add(new Godot.Collections.Dictionary
            {
                { "name", layer.Name.ToString() }, { "cells", cells },
            });
        }
        else if (node is Marker2D marker)
        {
            var pos = accumulated_position(marker);
            markers.Add(new Godot.Collections.Dictionary
            {
                { "name", marker.Name.ToString() }, { "pos", new Godot.Collections.Array { pos.X, pos.Y } },
            });
        }
        else if (node is Path2D path)
        {
            var points = new Godot.Collections.Array();
            for (int index = 0; index < path.Curve.PointCount; index++)
            {
                var point = path.Curve.GetPointPosition(index);
                points.Add(new Godot.Collections.Array { point.X, point.Y });
            }

            paths.Add(new Godot.Collections.Dictionary
            {
                { "name", path.Name.ToString() }, { "points", points },
            });
        }
        else if (node is Sprite2D sprite)
        {
            string texturePath = sprite.Texture != null ? sprite.Texture.ResourcePath : "";
            var spritePos = accumulated_position(sprite);
            sprites.Add(new Godot.Collections.Dictionary
            {
                { "name", sprite.Name.ToString() },
                { "pos", new Godot.Collections.Array { spritePos.X, spritePos.Y } },
                { "texture", texturePath },
            });
        }

        foreach (Node child in node.GetChildren())
        {
            _collect(child, layers, markers, paths, sprites);
        }
    }

    // ---------------------------------------------------------------- CSV

    /// <summary>表里没有带逗号的字段，直接按逗号切就够了；真出现带逗号的字段再换正经解析。
    ///
    /// **BOM 判据用第一个字符比，不用 `StartsWith`**（2026-10-11 实测）：`string.StartsWith(string)`
    /// 走的是**文化敏感**比较，而 U+FEFF（零宽不换行空格）在 CLDR 里是"可忽略字符"——
    /// 于是 `"building_id".StartsWith("\uFEFF")` 会返回 **true**，把表头首列削掉一个字符
    /// （`building_id` → `uilding_id`），全表按列名取值的地方全部静默落空。
    /// 非 ASCII 前缀（BOM 就是一个）一律用序数比较或逐字符比。</summary>
    public static Godot.Collections.Array read_csv(string path)
    {
        var rows = new Godot.Collections.Array();
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return rows;
        }

        // 顺带一提：Godot 的 `get_csv_line()` **已经把 UTF-8 BOM 吃掉了**（实测首字符就是 'b'），
        // 这一段只是给"别的读法喂进来的表头"兜底，别当它天天在生效。
        string[] header = file.GetCsvLine();
        if (header.Length > 0 && header[0].Length > 0 && header[0][0] == '\ufeff')
        {
            header[0] = header[0].Substring(1);
        }

        while (!file.EofReached())
        {
            string[] line = file.GetCsvLine();
            if (line == null || line.Length == 0 || line.Length < header.Length
                || line[0].Length == 0)
            {
                continue;
            }

            var row = new Godot.Collections.Dictionary();
            for (int i = 0; i < header.Length; i++)
            {
                row[header[i]] = line[i];
            }

            rows.Add(row);
        }

        return rows;
    }

    public static Godot.Collections.Array rows_where(string path, string column, string value)
    {
        var output = new Godot.Collections.Array();
        foreach (Variant rowVariant in read_csv(path))
        {
            var row = rowVariant.AsGodotDictionary();
            string cell = row.ContainsKey(column) ? row[column].AsString() : "";
            if (cell == value)
            {
                output.Add(row);
            }
        }

        return output;
    }
}
