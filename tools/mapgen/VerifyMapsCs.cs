using System.Collections.Generic;
using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 按 07_地图资源需求.md 第九节的验收标准，逐条校验六张地图（对位旧链 `verify_maps.gd`）。
///
/// 覆盖：
///   1. 命名与归属：Marker 名 = 表里的 id（双向比对）；Room_ 在正确场景、Spawn_ 在正确区域
///   2. 连通性：dungeon_room.exit_rooms 声明的连通关系，在地图上真的走得通；没有进不去的房间
///   3. 可绕与背袭：明雷周围留得下 2 格宽的通路（启发式，见 `CheckSpawnRoom`）
///   4. 战斗房间侧向通路：房间入口开口至少 2 格宽
///   5. 巡逻：Patrol_ 路线存在且端点不卡墙
///   6. 性能：单场景 TileMapLayer ≤ 4、瓦片用量 ≤ 5000、单区域明雷 ≤ 8
///
/// 用法（`--script` 只认 GDScript，C# 侧只能挂场景）：
///   godot --headless --path . --log-file .logs\mapcheck_engine.log ^
///         --quit-after 2000 res://tools/mapgen/verify_maps_cs.tscn
///
/// 拆成三份 `partial` 的理由只有一条：单文件 800 行硬上限。这一份放**入口 ＋ 基础设施
/// （载入场景／洪水填充／挡路判定／瓦片预算）**；逐条验收拆两份——
/// `VerifyMapsCsChecks.cs`（命名与归属／连通性与通路）＋ `VerifyMapsCsChecksAssets.cs`（归属与资产）。
/// </summary>
public partial class VerifyMapsCs : Node
{
    private const string RegionCsv = "res://data/tables/map_region.csv";
    private const string LocalCsv = "res://data/tables/map_local.csv";
    private const string SpawnCsv = "res://data/tables/roaming_spawn.csv";
    private const string RoomCsv = "res://data/tables/dungeon_room.csv";
    private const string TriggerCsv = "res://data/tables/hidden_trigger.csv";
    private const string EventCsv = "res://data/tables/event_check.csv";
    private const string FlavorCsv = "res://data/tables/flavor_point.csv";
    private const string NpcCsv = "res://data/tables/npc_def.csv";
    private const string BuildingCsv = "res://data/tables/building_def.csv";

    private const string Overworld = "res://scenes/maps/overworld.tscn";
    private const string ParentRegion = "jiangnan_east";
    private const int MaxTileLayers = 4;

    /// <summary>大地图 0.32.0 起多一层 **`Conditional`**（条件地表：石隙那条细径，16 §3.2）——
    /// 地面 4 层 ＋ 条件地表 = 5 层。其余五张小地图照旧 ≤4。</summary>
    private const int MaxTileLayersOverworld = 5;
    private const int MaxTiles = 5000;

    /// <summary>大地图 64×48＝**3072 格/层**：光「草底 ＋ 雾」两层就 6000 出头，`5000` 那条是按
    /// 32×24（768 格/层）定的。0.31.2 扩容后按面积重算，大地图单独给一档上限。</summary>
    private const int MaxTilesOverworld = 12000;
    private const int MaxSpawnsPerRegion = 8;

    /// <summary>明雷离所属区域地标的最大格数：归属检查用的宽松半径（表里只给了 region_id）。</summary>
    private const int SpawnRegionRadius = 12;

    /// <summary>挂钩点前缀（07 文档第二节）。Team_ / Exit_ 是 2026-10-02 审计后补的两条。</summary>
    private static readonly string[] MarkerPrefixes =
    {
        "Node_", "Portal_", "Room_", "Chest_", "Spawn_", "Event_",
        "Trigger_", "Patrol_", "Team_", "Exit_", "Observe_", "Sign_",
    };

    private readonly List<string> _problems = new();

    /// <summary>表本身的问题（不是地图的问题），单独回报设计侧，不判地图失败。</summary>
    private readonly List<string> _dataNotes = new();

    /// <summary>地图资产还没交付的东西（待补地编／美术），单独回报、不判地图失败
    /// （与「雾的贴图」「精英贴图」同一个口径：缺资产不该挡住整条验收链）。</summary>
    private readonly List<string> _assetNotes = new();

    /// <summary>载入过的场景，**按载入顺序**留着（GD 那边是插入序的 Dictionary；
    /// 打印与报错的行序都跟着它走，换成 C# 的 Dictionary 就会乱序）。</summary>
    private readonly List<string> _sceneOrder = new();

    private readonly Dictionary<string, SceneInfo> _scenes = new();

    public override void _Ready()
    {
        // 与 `RunTestsCs` 同一条理由（见 `docs/dev/CSharp迁移计划.md` 第三节第 1 条）。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    /// <summary>一个已载入场景的几本账：图层、按名字收集的挂钩点、巡逻线、以及按路径索引的节点。</summary>
    private sealed class SceneInfo
    {
        public Node Root;
        public TileMapLayer Decor;
        public TileMapLayer Ground;
        public Camera2D Camera;
        public readonly List<TileMapLayer> Layers = new();
        public readonly Godot.Collections.Dictionary Names = new();
        public readonly Godot.Collections.Dictionary Paths = new();
        public readonly Godot.Collections.Dictionary Items = new();
    }

    private int Run()
    {
        var localScenes = new List<string>();
        foreach (Variant rowVariant in MapKit.read_csv(LocalCsv))
        {
            localScenes.Add(rowVariant.AsGodotDictionary()["scene_id"].AsString());
        }

        Store(Overworld, LoadScene(Overworld));
        foreach (string sceneId in localScenes)
        {
            string path = $"res://scenes/maps/{sceneId}.tscn";
            if (!FileAccess.FileExists(path))
            {
                _problems.Add($"map_local.csv 里的 {sceneId} 没有对应场景：{path}");
                continue;
            }

            Store(path, LoadScene(path));
        }

        CheckOverworld();
        CheckLocalMaps(localScenes);
        CheckTeamMarkers();
        CheckExitMarkers(localScenes);
        CheckNpcSlots(localScenes);
        CheckFacilityMarkers(localScenes);
        CheckBuildingMarkers(localScenes);
        CheckObservePoints(localScenes);
        CheckOnlyDecorBlocks();
        CheckConditionalLayers();
        CheckBudget();

        foreach (string path in _sceneOrder)
        {
            var scene = _scenes[path];
            if (scene != null)
            {
                scene.Root.Free();
            }
        }

        if (_dataNotes.Count > 0)
        {
            GD.Print($"=== 回报设计侧：配置表内部不一致 {_dataNotes.Count} 条（不影响地图验收）===");
            foreach (string note in _dataNotes)
            {
                GD.Print("  * " + note);
            }
        }

        if (_assetNotes.Count > 0)
        {
            GD.Print($"=== 待补（地编／美术，不影响验收）{_assetNotes.Count} 条 ===");
            foreach (string note in _assetNotes)
            {
                GD.Print("  * " + note);
            }
        }

        if (_problems.Count == 0)
        {
            GD.Print("MAPCHECK: OK");
            return 0;
        }

        GD.PrintErr($"=== 地图验收不通过，共 {_problems.Count} 条 ===");
        foreach (string problem in _problems)
        {
            GD.PrintErr("  - " + problem);
        }

        GD.PrintErr("MAPCHECK: FAIL");
        return 1;
    }

    private void Store(string path, SceneInfo info)
    {
        _sceneOrder.Add(path);
        _scenes[path] = info;
    }

    private SceneInfo Scene(string path) =>
        _scenes.TryGetValue(path, out var info) ? info : null;

    // ---------------------------------------------------------------- 基础设施

    private SceneInfo LoadScene(string path)
    {
        var packed = GD.Load<PackedScene>(path);
        if (packed == null)
        {
            _problems.Add($"场景加载不了：{path}");
            return null;
        }

        var info = new SceneInfo();
        info.Root = packed.Instantiate();
        info.Decor = info.Root.GetNodeOrNull<TileMapLayer>("Decor");
        if (info.Decor == null)
        {
            _problems.Add($"{path.GetFile()} 缺 Decor 图层");
        }

        info.Ground = info.Root.GetNodeOrNull<TileMapLayer>("Ground");
        if (info.Ground == null)
        {
            _problems.Add($"{path.GetFile()} 缺 Ground 图层");
        }

        info.Camera = info.Root.GetNodeOrNull<Camera2D>("Camera");
        if (info.Camera == null)
        {
            _problems.Add($"{path.GetFile()} 缺 Camera 节点（07 文档节点结构要求）");
        }

        // 从根节点的子节点开始走，路径里就不带场景名，
        // 于是 `Enemies/hf1_yard/Team_x`、`Markers/hf1_gate/Exit_x` 能直接当 key 用。
        foreach (Node child in info.Root.GetChildren())
        {
            Walk(child, "", info);
        }

        return info;
    }

    private void Walk(Node node, string parentPath, SceneInfo info)
    {
        string nodeName = node.Name.ToString();
        string nodePath = parentPath.Length == 0 ? nodeName : $"{parentPath}/{nodeName}";
        if (node is TileMapLayer layer)
        {
            info.Layers.Add(layer);
        }
        else if (node is Path2D path)
        {
            info.Paths[nodeName] = path;
            // 巡逻线也要参与「名字比对」，取第一个控制点当它的落位。
            if (path.Curve.PointCount > 0)
            {
                var head = path.Curve.GetPointPosition(0);
                info.Names[nodeName] = ToCell(head);
            }
        }
        else if (node is Node2D node2D)
        {
            // Room_ 是 Node2D（挂 Area2D 当房间边界），其余挂钩点是 Marker2D，一起按名字收集。
            foreach (string prefix in MarkerPrefixes)
            {
                if (!nodeName.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var cell = ToCell(MapKit.accumulated_position(node2D));
                info.Names[nodeName] = cell;
                info.Items[nodePath] = new Godot.Collections.Dictionary
                {
                    { "name", nodeName },
                    { "cell", cell },
                };
                break;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            Walk(child, nodePath, info);
        }
    }

    private static Vector2I ToCell(Vector2 pixel) =>
        new((int)pixel.X / MapKit.tile_px(), (int)pixel.Y / MapKit.tile_px());

    private static Godot.Collections.Dictionary GroupNames(
        Godot.Collections.Dictionary names, string prefix)
    {
        var output = new Godot.Collections.Dictionary();
        foreach (Variant nameVariant in names.Keys)
        {
            string markerName = nameVariant.AsString();
            if (markerName.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                output[markerName] = names[markerName];
            }
        }

        return output;
    }

    /// <summary>地图里的节点名带前缀（Node_/Spawn_/…），表里是裸 id；比对时先把前缀剥掉。</summary>
    private void Compare(
        string label, IEnumerable<string> expected, Godot.Collections.Dictionary actual,
        string prefix)
    {
        var expectedSet = new Godot.Collections.Dictionary();
        foreach (string item in expected)
        {
            expectedSet[item] = true;
        }

        var actualIds = new Godot.Collections.Dictionary();
        foreach (Variant nodeNameVariant in actual.Keys)
        {
            string nodeName = nodeNameVariant.AsString();
            actualIds[nodeName.Substring(prefix.Length)] = true;
        }

        foreach (Variant key in expectedSet.Keys)
        {
            if (!actualIds.ContainsKey(key))
            {
                _problems.Add($"{label}：配置表有 {key.AsString()}，地图里没有对应节点");
            }
        }

        foreach (Variant key in actualIds.Keys)
        {
            if (!expectedSet.ContainsKey(key))
            {
                _problems.Add($"{label}：地图里有 {prefix}{key.AsString()}，配置表查不到");
            }
        }
    }

    private static List<string> Column(
        string path, string column, string filterColumn, string value)
    {
        var output = new List<string>();
        foreach (Variant rowVariant in MapKit.read_csv(path))
        {
            var row = rowVariant.AsGodotDictionary();
            if (filterColumn.Length == 0 || row.ContainsKey(filterColumn) && row[filterColumn].AsString() == value)
            {
                output.Add(row.ContainsKey(column) ? row[column].AsString() : "");
            }
        }

        return output;
    }

    private static bool Blocked(TileMapLayer layer, Vector2I cell)
    {
        if (layer == null)
        {
            return false;
        }

        int sourceId = layer.GetCellSourceId(cell);
        if (sourceId == -1)
        {
            return false;
        }

        var source = layer.TileSet.GetSource(sourceId);
        if (source is not TileSetAtlasSource atlasSource)
        {
            return false;
        }

        var data = atlasSource.GetTileData(
            layer.GetCellAtlasCoords(cell), layer.GetCellAlternativeTile(cell));
        return data != null && data.GetCustomData("blocked").AsBool();
    }

    /// <summary>返回挡住这一格的瓦片图集坐标；没被挡返回 (-1,-1)。调试用，报错里带上坐标好定位。</summary>
    private static Vector2I BlockingTile(TileMapLayer layer, Vector2I cell)
    {
        if (layer == null)
        {
            return new Vector2I(-1, -1);
        }

        int sourceId = layer.GetCellSourceId(cell);
        if (sourceId == -1)
        {
            return new Vector2I(-1, -1);
        }

        var source = layer.TileSet.GetSource(sourceId);
        if (source is not TileSetAtlasSource atlasSource)
        {
            return new Vector2I(-1, -1);
        }

        var atlas = layer.GetCellAtlasCoords(cell);
        var data = atlasSource.GetTileData(atlas, layer.GetCellAlternativeTile(cell));
        if (data == null || !data.GetCustomData("blocked").AsBool())
        {
            return new Vector2I(-1, -1);
        }

        return atlas;
    }

    /// <summary>从起点在地板上洪水填充（只看 Decor 是否挡路，不限图层），返回能走到的格子。</summary>
    private static Godot.Collections.Dictionary Flood(SceneInfo scene, Vector2I origin)
    {
        var decor = scene.Decor;
        // 必须限定在地图范围内：户外场景没有完整墙壳，不设边界会一路扩散到无穷。
        var bounds = decor.GetUsedRect().Grow(2);
        var seen = new Godot.Collections.Dictionary();
        var queue = new List<Vector2I> { origin };
        seen[origin] = true;
        while (queue.Count > 0)
        {
            var cell = queue[^1];
            queue.RemoveAt(queue.Count - 1);
            foreach (Vector2I offset in
                new[] { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down })
            {
                var next = cell + offset;
                if (!bounds.HasPoint(next) || seen.ContainsKey(next) || Blocked(decor, next))
                {
                    continue;
                }

                seen[next] = true;
                queue.Add(next);
            }
        }

        return seen;
    }

    private static Vector2I RectCenter(Rect2I rect) =>
        new(rect.Position.X + rect.Size.X / 2, rect.Position.Y + rect.Size.Y / 2);

    private static bool HasNumberedVariant(Godot.Collections.Dictionary found, string markerName)
    {
        foreach (Variant nameVariant in found.Keys)
        {
            string name = nameVariant.AsString();
            if (!name.StartsWith(markerName + "_", System.StringComparison.Ordinal))
            {
                continue;
            }

            if (name.Substring(markerName.Length + 1).IsValidInt())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>`Characters/&lt;name&gt;` 落在哪个格子；找不到返回 (-1,-1)。</summary>
    private static Vector2I CharacterCell(Node root, string nodeName)
    {
        var node = root.GetNodeOrNull<Node2D>($"Characters/{nodeName}");
        return node == null ? new Vector2I(-1, -1) : ToCell(MapKit.accumulated_position(node));
    }

    /// <summary>本场景对应的父区域节点（`map_local.parent_node`）——按 id 绑的位点允许指向父区域上的人
    /// （与 `NpcService.npcs_at` 的口径一致）。</summary>
    private static string ParentNodeOf(string sceneId)
    {
        foreach (Variant rowVariant in MapKit.read_csv(LocalCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            if (row.ContainsKey("scene_id") && row["scene_id"].AsString() == sceneId)
            {
                return row.ContainsKey("parent_node") ? row["parent_node"].AsString() : "";
            }
        }

        return "";
    }

    private static Godot.Collections.Dictionary RowFor(Godot.Collections.Array rows, string roomId)
    {
        foreach (Variant rowVariant in rows)
        {
            var row = rowVariant.AsGodotDictionary();
            if (row["room_id"].AsString() == roomId)
            {
                return row;
            }
        }

        return new Godot.Collections.Dictionary();
    }

    private static Vector2I EntranceCell(
        Godot.Collections.Array roomRows, Godot.Collections.Dictionary rects)
    {
        foreach (Variant rowVariant in roomRows)
        {
            var row = rowVariant.AsGodotDictionary();
            string roomType = row.ContainsKey("room_type") ? row["room_type"].AsString() : "";
            string roomId = row["room_id"].AsString();
            if ((roomType == "entrance" || roomType == "boss" || roomType == "battle" || roomType == "elite")
                && rects.ContainsKey(roomId))
            {
                return RectCenter(rects[roomId].AsRect2I());
            }
        }

        foreach (Variant rowVariant in roomRows)
        {
            var row = rowVariant.AsGodotDictionary();
            string roomId = row["room_id"].AsString();
            if (rects.ContainsKey(roomId))
            {
                return RectCenter(rects[roomId].AsRect2I());
            }
        }

        return Vector2I.Zero;
    }

    /// <summary>这一格脚下是不是真的挡路：读 TileSet 的碰撞多边形（物理真相），不读 custom_data。</summary>
    private static bool TileHasCollision(TileMapLayer layer, Vector2I cell)
    {
        var data = layer.GetCellTileData(cell);
        return data != null && data.GetCollisionPolygonsCount(0) > 0;
    }

    // ---------------------------------------------------------------- 性能

    private void CheckBudget()
    {
        foreach (string path in _sceneOrder)
        {
            var scene = _scenes[path];
            if (scene == null)
            {
                continue;
            }

            bool isOverworld = path == Overworld;
            int maxLayers = isOverworld ? MaxTileLayersOverworld : MaxTileLayers;
            int maxTiles = isOverworld ? MaxTilesOverworld : MaxTiles;
            if (scene.Layers.Count > maxLayers)
            {
                _problems.Add($"{path.GetFile()} 有 {scene.Layers.Count} 个 TileMapLayer，超过上限 {maxLayers}");
            }

            int total = 0;
            foreach (TileMapLayer layer in scene.Layers)
            {
                total += layer.GetUsedCells().Count;
            }

            if (total > maxTiles)
            {
                _problems.Add($"{path.GetFile()} 瓦片用量 {total}，超过上限 {maxTiles}");
            }

            GD.Print($"[check] {path.GetFile()}：TileMapLayer {scene.Layers.Count} 层 / 瓦片 {total}");
        }
    }

    /// <summary>**挡路的瓦片只许画在 `Decor` 层**（16 §3.2 的「挡路」列：Ground／Overlay／Conditional／Fog 全是「否」）。
    ///
    /// 这一条源自 2026-10-04 玩家报的「副本里无法行走」：主题 TileSet 把地板瓦片
    /// （`T_FLOOR`＝1,9）也列进了 solid，于是副本／洞穴的整张 Ground 都是碰撞盒——</summary>
    private void CheckOnlyDecorBlocks()
    {
        foreach (string path in _sceneOrder)
        {
            var scene = _scenes[path];
            if (scene == null)
            {
                continue;
            }

            foreach (TileMapLayer layer in scene.Layers)
            {
                if (layer.Name.ToString() == "Decor")
                {
                    continue;
                }

                var solidCells = new List<string>();
                int total = 0;
                foreach (Vector2I cell in layer.GetUsedCells())
                {
                    if (!TileHasCollision(layer, cell))
                    {
                        continue;
                    }

                    total += 1;
                    if (solidCells.Count < 5)
                    {
                        solidCells.Add($"({cell.X},{cell.Y})");
                    }
                }

                if (total == 0)
                {
                    continue;
                }

                // Ground 那次的报错措辞留着：它是玩家最可能撞上的那一种，接手的人要能一眼认出来
                string tail = layer.Name.ToString() == "Ground"
                    ? "挡路的瓦片要画在 Decor 层，地板瓦片不能带碰撞盒"
                    : "挡路的东西只许画在 Decor 层（16 §3.2 的分层表）";
                _problems.Add(
                    $"{path.GetFile()} 的 {layer.Name} 层有 {total} 格带物理碰撞（例："
                    + $"{string.Join("、", solidCells)}）——" + tail);
            }
        }
    }

    /// <summary>`Conditional` 层（16 §3.2 第 5 层，0.32.0 新增）：**整层**显隐的条件地表，
    /// 现在唯一用途是石隙迷窟那 22 格碎石细径。地编交付后这里会打印格数；
    /// 还没交付就打一行「待补」提醒（不判失败——缺资产不该挡住整条验收链）。</summary>
    private void CheckConditionalLayers()
    {
        int delivered = 0;
        foreach (string path in _sceneOrder)
        {
            var scene = _scenes[path];
            if (scene == null)
            {
                continue;
            }

            var layer = scene.Root.GetNodeOrNull<TileMapLayer>("Conditional");
            if (layer == null)
            {
                continue;
            }

            delivered += 1;
            GD.Print($"[check] {path.GetFile()}：Conditional 层 {layer.GetUsedCells().Count} 格");
        }

        if (delivered == 0)
        {
            _assetNotes.Add(
                "还没有任何场景交付 `Conditional` 层（0.32.0：石隙细径 22 格铺在它上面，代码已按藏宝图显隐）");
        }
    }
}
