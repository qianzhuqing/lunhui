using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 生成大地图 `scenes/maps/overworld.tscn`（对位旧链 `build_overworld.gd`，该 `.gd` 已退场）。
///
/// 按 07_地图资源需求.md 第三节（0.31.2 扩容 ＋ 0.32.0 探索口径）：
///   画布 **2048×1536 px（64×48 格）**；地标坐标**直接用 map_region.pos_x/pos_y**（像素，不吸附格子）。
///   图层 Ground／Decor／Overlay／**Conditional**（条件地表）／Fog——0.32.0 起第 5 层。
///   Marker：Node_×8、Portal_×6、Sign_×4、Spawn_×17、Patrol_×4、Event_×8、Observe_×3。
///
/// 用法（`--script` 只认 GDScript，C# 侧只能挂场景，同 `RunTestsCs`）：
///   godot --headless --path . --log-file .logs\mapgen_overworld_cs.log ^
///         res://tools/mapgen/build_overworld_cs.tscn
///
/// **数据形状照抄 GDScript 版**（`Godot.Collections.Dictionary` 保插入序）：大地图是靠
/// 逐格 `set_cell` 铺出来的，改动遍历次序就可能改出另一个 `.tscn`，所以这里不图省事换成
/// C# 的 `Dictionary`（它不保序）。
/// </summary>
public partial class BuildOverworldCs : Node
{
    private const string ScenePath = "res://scenes/maps/overworld.tscn";
    private const string DumpPath = "res://.logs/overworld_preview.json";
    private const string RegionCsv = "res://data/tables/map_region.csv";
    private const string SpawnCsv = "res://data/tables/roaming_spawn.csv";
    private const string IconDir = "res://assets/sprites/icons/";
    private const string ParentRegion = "jiangnan_east";

    /// <summary>大地图上的地标数（设计侧加一个区域，这一行跟着改）。</summary>
    private const int NodeCount = 8;

    /// <summary>画布 2048×1536 px（64×48 格）——0.31.2 实机反馈「太小」之后从 32×24 扩到 4 倍面积。</summary>
    private const int MapW = 64;
    private const int MapH = 48;

    /// <summary>开局就探明的范围（格）：`reveal_on_map = 1` 的地标周边。
    /// **0.32.0 起只有清风驿（城镇）与驿站默认公开**——其余全在雾里，要玩家自己走过去才出现。</summary>
    private const int RevealRadius = 6;

    /// <summary>道路分两级（16 §3.4 / A13）：主路一条最宽，支路窄一档，不要第三条档。</summary>
    private const int MainRoadWidth = 3;
    private const int BranchWidth = 2;

    /// <summary>路网（名单以 16 §3.4 为准）。`via` 是拐点：`carve_corridor` 只会画 L 形，
    /// 多给几个拐点才画得出「点连着点」的连续路面（0.31.2：不能靠草地凑合能走）。
    ///
    /// 注意 07 §3.2 与旧交接单里写的「清风驿→废弃渡口」是漂移，**驿站→废弃渡口**才对。</summary>
    private static readonly Godot.Collections.Dictionary[] Roads =
    {
        // 主路：清风驿 → 驿站（沿 y=32 往东，再北上）
        new()
        {
            { "width", MainRoadWidth },
            { "nodes", new Godot.Collections.Array { "n_qingfengyi", "n_post_station" } },
            { "via", new Godot.Collections.Array { new Vector2I(25, 32) } },
        },
        // 主路：驿站 → 落雁坡（沿 y=25 往东，再南下）
        new()
        {
            { "width", MainRoadWidth },
            { "nodes", new Godot.Collections.Array { "n_post_station", "n_luoyanpo" } },
            { "via", new Godot.Collections.Array { new Vector2I(39, 25) } },
        },
        // 主路：落雁坡 → 黑风寨（沿 y=34 往东，再北上——黑风寨是从落雁坡「发现」出来的）
        new()
        {
            { "width", MainRoadWidth },
            { "nodes", new Godot.Collections.Array { "n_luoyanpo", "n_heifengzhai" } },
            { "via", new Godot.Collections.Array { new Vector2I(54, 34) } },
        },
        // 支路：落雁坡 → 塌陷山洞（南下进山，洞口在路的尽头）
        new()
        {
            { "width", BranchWidth },
            { "nodes", new Godot.Collections.Array { "n_luoyanpo", "n_cave_collapse" } },
            { "via", new Godot.Collections.Array { new Vector2I(43, 34) } },
        },
        // 支路：落雁坡 → 荒村（往东）
        new()
        {
            { "width", BranchWidth },
            { "nodes", new Godot.Collections.Array { "n_luoyanpo", "n_huangcun" } },
            { "via", new Godot.Collections.Array { new Vector2I(57, 34) } },
        },
        // 支路：驿站 → 废弃渡口（往西南，绕开清风驿那一圈）
        new()
        {
            { "width", BranchWidth },
            { "nodes", new Godot.Collections.Array { "n_post_station", "n_ferry_abandoned" } },
            { "via", new Godot.Collections.Array { new Vector2I(16, 25), new Vector2I(16, 42) } },
        },
    };

    /// <summary>石隙迷窟那条支路（16 §3.4 的最后一条）：**1 格宽**、走碎石草地而不是土路
    /// （「藏宝图上的一条线」），而且**整条铺在 0.32.0 新增的 `Conditional` 层上**——
    /// 程序按 `item_treasure_map` 整层显隐：没拿到图之前，这条线在地图上不存在。</summary>
    private static readonly Godot.Collections.Dictionary ShixiTrail = new()
    {
        { "nodes", new Godot.Collections.Array { "n_luoyanpo", "n_shixi" } },
        { "via", new Godot.Collections.Array { new Vector2I(23, 34) } },
        { "width", 1 },
    };

    /// <summary>山体区块（格）：**成片是硬要求**（16 §3.5）——孤立一格山在 2× 下就是一块石头，
    /// 一层山至少铺 2–3 屏（相机 2× 时一屏约 16×9 格）。</summary>
    private static readonly Rect2I[] MountainBlocks =
    {
        new(30, 0, 34, 16),    // 北岭：横贯东北，罩在黑风寨背后
        new(34, 37, 20, 10),   // 塌陷山洞那一片山（洞口嵌在山体里）
        new(12, 37, 20, 10),   // 石隙迷窟那一片山（岩缝嵌在山体里）
        new(0, 2, 22, 12),     // 西北小岭
        new(0, 24, 8, 12),     // 西缘中段，把清风驿西南封住
    };

    /// <summary>废弃渡口那片水（格）：外缘的自然边界。深水挡路（07 §二：水不能穿过），
    /// 浅水／水岸／芦苇不挡路——芦苇荡要能钻进去侦察（16 §4.4）。</summary>
    private static readonly Rect2I WaterRect = new(0, 44, 21, 4);

    /// <summary>明雷刷新点：相对所属地标的格子偏移（表里没有坐标，属于地图数据）。
    /// 硬性要求：清风驿一圈是安全区，所以落雁坡／黑风寨外围／荒村的点都离它很远。</summary>
    private static readonly Godot.Collections.Dictionary SpawnOffsets = new()
    {
        { "sp_lp_wolf_01", new Vector2I(-9, -4) },
        { "sp_lp_wolf_02", new Vector2I(-8, 2) },
        { "sp_lp_wolf_03", new Vector2I(-4, -6) },
        { "sp_lp_boar_01", new Vector2I(5, -5) },
        { "sp_lp_patrol_01", new Vector2I(-3, -6) },
        { "sp_lp_patrol_02", new Vector2I(4, -6) },
        { "sp_lp_lonewolf", new Vector2I(-7, 3) },
        { "sp_lp_herbalist", new Vector2I(-11, 0) },
        { "sp_hf_gate_01", new Vector2I(-3, -2) },
        { "sp_hf_gate_02", new Vector2I(-2, -1) },
        { "sp_hf_tower", new Vector2I(2, -3) },
        { "sp_hf_relief_01", new Vector2I(-4, 2) },
        { "sp_hf_relief_02", new Vector2I(3, 2) },
        { "sp_hc_brigand_01", new Vector2I(-3, -2) },
        { "sp_hc_brigand_02", new Vector2I(-1, -3) },
        { "sp_hc_butcher", new Vector2I(-3, 1) },
        { "sp_hc_sleeper", new Vector2I(-1, 3) },
    };

    /// <summary>明雷归属：region_id -> 地标 node_id。</summary>
    private static readonly Godot.Collections.Dictionary RegionNode = new()
    {
        { "n_luoyanpo", "n_luoyanpo" },
        { "n_heifengzhai", "n_heifengzhai" },
        { "n_huangcun", "n_huangcun" },
    };

    /// <summary>巡逻路线（4 条，名字对应 roaming_spawn.patrol_path_id），用相对地标的偏移写。</summary>
    private static readonly Godot.Collections.Dictionary PatrolPaths = new()
    {
        {
            "path_lp_a", new Godot.Collections.Dictionary
            {
                { "anchor", "n_luoyanpo" },
                {
                    "points", new Godot.Collections.Array
                    {
                        new Vector2I(-8, -4), new Vector2I(-5, -6),
                        new Vector2I(-2, -4), new Vector2I(-5, -2),
                    }
                },
            }
        },
        {
            "path_lp_b", new Godot.Collections.Dictionary
            {
                { "anchor", "n_luoyanpo" },
                {
                    "points", new Godot.Collections.Array
                    {
                        new Vector2I(2, -5), new Vector2I(5, -6),
                        new Vector2I(6, -4), new Vector2I(3, -3),
                    }
                },
            }
        },
        {
            "path_hf_a", new Godot.Collections.Dictionary
            {
                { "anchor", "n_heifengzhai" },
                {
                    "points", new Godot.Collections.Array
                    {
                        new Vector2I(-6, 2), new Vector2I(-4, 0),
                        new Vector2I(-1, 2), new Vector2I(-4, 4),
                    }
                },
            }
        },
        {
            "path_hf_b", new Godot.Collections.Dictionary
            {
                { "anchor", "n_heifengzhai" },
                {
                    "points", new Godot.Collections.Array
                    {
                        new Vector2I(2, 3), new Vector2I(5, 2),
                        new Vector2I(4, 5), new Vector2I(1, 5),
                    }
                },
            }
        },
    };

    /// <summary>大地图判定位点（07 第三节 8 处）：相对地标的偏移。</summary>
    private static readonly Godot.Collections.Dictionary EventSites = new()
    {
        { "ev_wild_track", Site("n_luoyanpo", new Vector2I(-9, 1)) },
        // 与 Spawn_sp_lp_herbalist 同一人，故意压在同一个格子上。
        { "ev_herbalist_help", Site("n_luoyanpo", new Vector2I(-11, 0)) },
        { "ev_night_watch", Site("n_luoyanpo", new Vector2I(4, -3)) },
        { "ev_bandit_parley", Site("n_huangcun", new Vector2I(-3, -1)) },
        { "ev_grave_epitaph", Site("n_cave_collapse", new Vector2I(0, -1)) },
        { "ev_climb_wall", Site("n_heifengzhai", new Vector2I(3, -1)) },
        { "ev_force_gate", Site("n_heifengzhai", new Vector2I(0, -1)) },
        // 镇抚司巡查的关卡：驿站南侧官道上（07 §三／§十一 第 12 条）。
        // 它同时是随机事件 we_patrol 的判定目标，但图上仍要有这个位点——玩家走到关卡也能按 E 触发。
        { "ev_patrol_check", Site("n_post_station", new Vector2I(-2, 1)) },
    };

    /// <summary>观察点（0.29.1 的 20_第一章剧情）：`Observe_<point_id>`，围绕官道南侧的旧镖车。
    /// 名字按设计给的形如 `Observe_ob_<地点>_<物>_<序号>`；`flavor_point` 表里就是这三条。</summary>
    private static readonly Godot.Collections.Dictionary ObserveOffsets = new()
    {
        { "ob_luoyanpo_cart_01", new Vector2I(0, 1) },   // 旧镖车的车辙
        { "ob_luoyanpo_cart_02", new Vector2I(-2, 0) },  // 褪色的镖旗
        { "ob_luoyanpo_cart_03", new Vector2I(2, 0) },   // 车斗里的灰
    };

    /// <summary>指路牌（0.31.2 新增）：**主路 4 个节点**附近各一块，位点 `Sign_<node_id>`。
    /// 文案在 `map_region.signpost_cn`（表里没填就不显示字），牌子本身摆在这里。</summary>
    private static readonly string[] SignpostNodes =
    {
        "n_qingfengyi", "n_post_station", "n_luoyanpo", "n_heifengzhai",
    };

    private static readonly Godot.Collections.Dictionary SignpostOffsets = new()
    {
        { "n_qingfengyi", new Vector2I(-3, -1) },
        { "n_post_station", new Vector2I(1, -2) },
        { "n_luoyanpo", new Vector2I(-6, -2) },
        { "n_heifengzhai", new Vector2I(-4, 1) },
    };

    public override void _Ready()
    {
        // 与 `RunTestsCs` 同一条理由：`_Ready` 正处在根节点装配子节点的窗口里，往树上挂
        // 新节点会被拒（挂不上还照样往下跑）。延到本帧空闲，与 GDScript 的 `_initialize`
        // （主循环之前）等价。见 `docs/dev/CSharp迁移计划.md` 第三节第 1 条。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private static int Run()
    {
        var nodes = RegionNodes();
        // 地标数量跟着 map_region 走：设计侧加一个区域就改 NodeCount，
        // 别再写死数字（0.28.0 加石隙迷窟时就因为这里写死 7 而整张图生成不出来）。
        if (nodes.Count != NodeCount)
        {
            GD.PrintErr($"[mapgen] {ParentRegion} 的地标应有 {NodeCount} 个，实际 {nodes.Count} 个");
            return 1;
        }

        var tileSet = MapKit.save_theme_tile_set("jiangnan_wild");
        if (tileSet == null)
        {
            GD.PrintErr("[mapgen] TileSet 生成失败：检查 assets/tilesets/_common/ 的图集是否已导入");
            return 1;
        }

        var shell = MapKit.new_shell("overworld", tileSet, true, true);
        var root = shell["root"].As<Node2D>();
        var ground = shell["ground"].As<TileMapLayer>();
        var decor = shell["decor"].As<TileMapLayer>();
        var conditional = shell["conditional"].As<TileMapLayer>();
        var fog = shell["fog"].As<TileMapLayer>();
        var markers = shell["markers"].As<Node2D>();

        var roads = PlanRoads(nodes);
        var trail = PlanShixiTrail(nodes);
        var reserved = ReservedCells(nodes);
        var openings = OpeningCells(nodes);
        var water = WaterCells();

        // 顺序：草底 → 水 → 路 → 细径 → 山 → 洞口 → 地标装饰。
        // 路压过水岸，就成了「车马道一直通到水边」；山最后铺（除洞口），免得被别的装饰盖出洞。
        MapKit.paint_grass(ground, MapW, MapH);
        PaintWater(ground, decor);
        PaintWaterSurface(ground);
        MapKit.paint_dirt(ground, roads, MapW, MapH);
        // 石隙细径铺在 `Conditional` 层：拿到藏宝图之前整条不显示（0.32.0 拍板）。
        MapKit.paint_cells(conditional, trail, MapKit.t_grass_pebble());
        MapKit.paint_mountains(decor, MountainCells(roads, trail, reserved, water, openings));
        foreach (Variant cellVariant in openings.Keys)
        {
            var cell = cellVariant.AsVector2I();
            decor.SetCell(cell, MapKit.source_id(), openings[cell].AsVector2I());
        }

        PaintTown(ground, decor, nodes);
        PaintPostStation(ground, decor, nodes);
        PaintVillage(decor, nodes);
        PaintGrove(decor, nodes);
        PaintPatrolCheckpoint(decor, nodes);
        PaintOldCart(decor, nodes);
        PaintSignposts(decor, markers, nodes);

        MapKit.paint_fog(fog, MapW, MapH, PlanFog(nodes));
        PlaceMarkers(markers, nodes);
        PlaceNpcSlots(shell["characters"].As<Node2D>(), nodes);
        MapKit.fit_camera(shell["camera"].As<Camera2D>(), MapW, MapH);

        var saveError = MapKit.save_scene(root, ScenePath);
        if (saveError != (int)Error.Ok)
        {
            GD.PrintErr($"[mapgen] 保存场景失败：{saveError}");
            return 1;
        }

        int groundCount = ground.GetUsedCells().Count;
        int decorCount = decor.GetUsedCells().Count;
        int conditionalCount = conditional.GetUsedCells().Count;
        int fogCount = fog.GetUsedCells().Count;
        int total = groundCount + decorCount + conditionalCount + fogCount;
        root.Free();
        MapKit.dump_preview(ScenePath, DumpPath, MapW, MapH);
        GD.Print($"[mapgen] 大地图：地标 {NodeCount} / 明雷 {SpawnOffsets.Count} / 巡逻线 "
            + $"{PatrolPaths.Count} / 判定位点 {EventSites.Count} / 指路牌 {SignpostNodes.Length}");
        GD.Print($"[mapgen] 瓦片用量 ground={groundCount} decor={decorCount} "
            + $"conditional={conditionalCount} fog={fogCount} 合计={total}");
        GD.Print($"[mapgen] MAPGEN: OK -> {ScenePath}");
        return 0;
    }

    // ---------------------------------------------------------------- 表与几何

    /// <summary>地标坐标直接用表里的像素坐标（pos_x/pos_y 即像素，画布 2048×1536）。</summary>
    private static Godot.Collections.Dictionary RegionNodes()
    {
        var output = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.rows_where(RegionCsv, "parent_region", ParentRegion))
        {
            var row = rowVariant.AsGodotDictionary();
            string nodeId = row["node_id"].AsString();
            float posX = (float)row["pos_x"].AsString().ToFloat();
            float posY = (float)row["pos_y"].AsString().ToFloat();
            output[nodeId] = new Godot.Collections.Dictionary
            {
                { "pixel", new Vector2(posX, posY) },
                { "cell", new Vector2I((int)posX / MapKit.tile_px(), (int)posY / MapKit.tile_px()) },
                { "icon", row["icon"].AsString() },
                { "reveal", CellStr(row, "reveal_on_map", "0") == "1" },
                // 设计 16 §3.5：**城镇是最大最显眼的一类**（地形 3×3、图标 48×48）。
                { "node_type", CellStr(row, "node_type") },
            };
        }

        return output;
    }

    private static Godot.Collections.Array Polyline(
        Godot.Collections.Dictionary nodes, Godot.Collections.Array ids, Godot.Collections.Array via)
    {
        var points = new Godot.Collections.Array { NodeCell(nodes, ids[0].AsString()) };
        foreach (Variant cell in via)
        {
            points.Add(cell.AsVector2I());
        }

        points.Add(NodeCell(nodes, ids[1].AsString()));
        return points;
    }

    private static void CarvePolyline(
        Godot.Collections.Dictionary cells, Godot.Collections.Array points, int width)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            MapKit.carve_corridor(
                cells,
                new Rect2I(points[index].AsVector2I(), Vector2I.One),
                new Rect2I(points[index + 1].AsVector2I(), Vector2I.One),
                width);
        }
    }

    private static Godot.Collections.Dictionary PlanRoads(Godot.Collections.Dictionary nodes)
    {
        var roads = new Godot.Collections.Dictionary();
        foreach (var edge in Roads)
        {
            CarvePolyline(
                roads,
                Polyline(nodes, edge["nodes"].AsGodotArray(), edge["via"].AsGodotArray()),
                edge["width"].AsInt32());
        }

        // 城镇／驿站／副本门口留一圈空地：节点格子不能被墙盖住（验收第一条查的就是它）。
        // 兴趣点不铺地——洞口本身要嵌在山体里，铺一圈反而把山挖出个洞。
        foreach (Variant nodeIdVariant in nodes.Keys)
        {
            string nodeId = nodeIdVariant.AsString();
            if (NodeStr(nodes, nodeId, "node_type") == "poi")
            {
                continue;
            }

            MapKit.mark_disc(roads, NodeCell(nodes, nodeId), 1);
        }

        return roads;
    }

    /// <summary>石隙迷窟那条 1 格细径：单独一张格子表，因为它铺的是碎石草地而不是土路，
    /// 不能混进 `paint_dirt` 的边缘瓦片算法；而且它整条要铺在 `Conditional` 层上。</summary>
    private static Godot.Collections.Dictionary PlanShixiTrail(Godot.Collections.Dictionary nodes) =>
        CarveTrail(nodes);

    private static Godot.Collections.Dictionary CarveTrail(Godot.Collections.Dictionary nodes)
    {
        var trail = new Godot.Collections.Dictionary();
        CarvePolyline(
            trail,
            Polyline(nodes, ShixiTrail["nodes"].AsGodotArray(), ShixiTrail["via"].AsGodotArray()),
            ShixiTrail["width"].AsInt32());
        return trail;
    }

    /// <summary>明雷、判定位点、巡逻路线、观察点、地标、镖车、指路牌都不能被山压住。</summary>
    private static Godot.Collections.Dictionary ReservedCells(Godot.Collections.Dictionary nodes)
    {
        var reserved = new Godot.Collections.Dictionary();
        foreach (Variant spawnIdVariant in SpawnOffsets.Keys)
        {
            reserved[SpawnCell(nodes, spawnIdVariant.AsString())] = true;
        }

        foreach (Variant pathIdVariant in PatrolPaths.Keys)
        {
            var path = PatrolPaths[pathIdVariant.AsString()].AsGodotDictionary();
            var anchor = NodeCell(nodes, path["anchor"].AsString());
            foreach (Variant offset in path["points"].AsGodotArray())
            {
                reserved[anchor + offset.AsVector2I()] = true;
            }
        }

        foreach (Variant eventIdVariant in EventSites.Keys)
        {
            reserved[EventCell(nodes, eventIdVariant.AsString())] = true;
        }

        foreach (Variant pointIdVariant in ObserveOffsets.Keys)
        {
            reserved[ObserveCell(nodes, pointIdVariant.AsString())] = true;
        }

        foreach (Variant nodeIdVariant in nodes.Keys)
        {
            reserved[NodeCell(nodes, nodeIdVariant.AsString())] = true;
        }

        foreach (string nodeId in SignpostNodes)
        {
            reserved[NodeCell(nodes, nodeId) + SignpostOffsets[nodeId].AsVector2I()] = true;
        }

        reserved[CartCell(nodes)] = true;
        reserved[CartCell(nodes) + new Vector2I(0, 2)] = true;
        return reserved;
    }

    /// <summary>山洞／岩缝：**山体上的一个开口**（16 §3.5），不是摆在地上的洞图标。
    /// 它的格子不挡路（走得进去），落在支路的尽头。</summary>
    private static Godot.Collections.Dictionary OpeningCells(Godot.Collections.Dictionary nodes)
    {
        var cave = NodeCell(nodes, "n_cave_collapse");
        return new Godot.Collections.Dictionary
        {
            { cave, MapKit.t_cave_recess() },
            { cave + Vector2I.Right, MapKit.t_cave_recess() },
            { NodeCell(nodes, "n_shixi"), MapKit.t_crack_mouth() },
        };
    }

    private static Vector2I SpawnCell(Godot.Collections.Dictionary nodes, string spawnId)
    {
        string region = SpawnRegion(spawnId);
        var anchor = NodeCell(nodes, RegionNode[region].AsString());
        return anchor + SpawnOffsets[spawnId].AsVector2I();
    }

    private static string SpawnRegion(string spawnId)
    {
        foreach (Variant rowVariant in MapKit.read_csv(SpawnCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            if (row["spawn_id"].AsString() == spawnId)
            {
                return row["region_id"].AsString();
            }
        }

        return "";
    }

    private static Vector2I EventCell(Godot.Collections.Dictionary nodes, string eventId)
    {
        var site = EventSites[eventId].AsGodotDictionary();
        return NodeCell(nodes, site["anchor"].AsString()) + site["offset"].AsVector2I();
    }

    private static Vector2I ObserveCell(Godot.Collections.Dictionary nodes, string pointId) =>
        CartCell(nodes) + ObserveOffsets[pointId].AsVector2I();

    /// <summary>旧镖车（0.29.0 的 20_第一章剧情）：官道南侧，摆在路肩、不挡路。</summary>
    private static Vector2I CartCell(Godot.Collections.Dictionary nodes) =>
        NodeCell(nodes, "n_luoyanpo") + new Vector2I(-6, -6);

    private static Godot.Collections.Dictionary PlanFog(Godot.Collections.Dictionary nodes)
    {
        var clear = new Godot.Collections.Dictionary();
        foreach (Variant nodeIdVariant in nodes.Keys)
        {
            string nodeId = nodeIdVariant.AsString();
            if (NodeBool(nodes, nodeId, "reveal"))
            {
                MapKit.mark_disc(clear, NodeCell(nodes, nodeId), RevealRadius);
            }
        }

        return clear;
    }

    // ---------------------------------------------------------------- 自然边界（山、水）

    /// <summary>边缘是**看起来像山／水的自然边界**，不是空气墙直线（07 §三）——
    /// 内缘按散列往里多推几格，免得一眼看出「到这里就被挡住了」。</summary>
    private static Godot.Collections.Dictionary MountainCells(
        Godot.Collections.Dictionary roads, Godot.Collections.Dictionary trail,
        Godot.Collections.Dictionary reserved, Godot.Collections.Dictionary water,
        Godot.Collections.Dictionary openings)
    {
        var cells = new Godot.Collections.Dictionary();
        for (int y = 0; y < MapH; y++)
        {
            for (int x = 0; x < MapW; x++)
            {
                var cell = new Vector2I(x, y);
                if (IsBorder(cell))
                {
                    cells[cell] = true;
                }
            }
        }

        foreach (Rect2I rect in MountainBlocks)
        {
            MapKit.mark_rect(cells, rect);
        }

        // 水、洞口、路、细径、明雷／判定位点／观察点／地标：一律从山体里挖掉。
        EraseAll(cells, water);
        EraseAll(cells, openings);
        EraseAll(cells, roads);
        EraseAll(cells, trail);
        EraseAll(cells, reserved);
        return cells;
    }

    private static void EraseAll(Godot.Collections.Dictionary cells, Godot.Collections.Dictionary from)
    {
        foreach (Variant cell in from.Keys)
        {
            cells.Remove(cell);
        }
    }

    private static bool IsBorder(Vector2I cell)
    {
        if (cell.X < EdgeDepth(cell.Y * 7 + 1))
        {
            return true;
        }

        if (cell.X > MapW - 1 - EdgeDepth(cell.Y * 11 + 3))
        {
            return true;
        }

        if (cell.Y < EdgeDepth(cell.X * 13 + 5))
        {
            return true;
        }

        // 南缘西段是水（渡口），那里的天然边界交给 `PaintWater`
        return cell.X >= WaterRect.Position.X + WaterRect.Size.X
            && cell.Y > MapH - 1 - EdgeDepth(cell.X * 17 + 7);
    }

    private static int EdgeDepth(int seed) =>
        2 + (int)(MapKit.hash01(seed, seed * 3 + 1) * 3.0);

    private static Godot.Collections.Dictionary WaterCells()
    {
        var cells = new Godot.Collections.Dictionary();
        MapKit.mark_rect(cells, WaterRect);
        // 水岸那一排（不挡路）也算水域：山不能长到水里
        for (int x = WaterRect.Position.X; x < WaterRect.Position.X + WaterRect.Size.X; x++)
        {
            cells[new Vector2I(x, WaterRect.Position.Y - 1)] = true;
        }

        return cells;
    }

    /// <summary>深水画在 **Decor** 层：它挡路，而 `Ground` 按约定只能是「走得上去的地面」
    /// （`verify_maps._check_ground_not_solid` 盯着这条——副本里「脚下全是墙」那个真 bug 的根因）。</summary>
    private static void PaintWater(TileMapLayer ground, TileMapLayer decor)
    {
        for (int y = WaterRect.Position.Y; y < WaterRect.Position.Y + WaterRect.Size.Y; y++)
        {
            for (int x = WaterRect.Position.X; x < WaterRect.Position.X + WaterRect.Size.X; x++)
            {
                var cell = new Vector2I(x, y);
                if (y == WaterRect.Position.Y)
                {
                    ground.SetCell(cell, MapKit.source_id(), MapKit.t_water_shallow());
                }
                else
                {
                    decor.SetCell(cell, MapKit.source_id(), MapKit.t_water_deep());
                }
            }
        }
    }

    /// <summary>水岸 ＋ 芦苇荡（都在 Ground 层，不挡路）。</summary>
    private static void PaintWaterSurface(TileMapLayer ground)
    {
        for (int x = WaterRect.Position.X; x < WaterRect.Position.X + WaterRect.Size.X; x++)
        {
            ground.SetCell(
                new Vector2I(x, WaterRect.Position.Y - 1), MapKit.source_id(), MapKit.t_water_shore());
        }

        foreach (Vector2I at in new[]
        {
            new Vector2I(4, 42), new Vector2I(7, 42), new Vector2I(13, 42), new Vector2I(17, 43),
            new Vector2I(2, 43), new Vector2I(20, 42),
        })
        {
            ground.SetCell(at, MapKit.source_id(), MapKit.t_reeds());
        }
    }

    // ---------------------------------------------------------------- 小工具

    private static Godot.Collections.Dictionary Site(string anchor, Vector2I offset) => new()
    {
        { "anchor", anchor },
        { "offset", offset },
    };

    private static Vector2I NodeCell(Godot.Collections.Dictionary nodes, string nodeId) =>
        nodes[nodeId].AsGodotDictionary()["cell"].AsVector2I();

    /// <summary>读 `nodes[id]` 里的一个字段（值在字典里都是 Variant，取出来按类型转——
    /// **别拿 `AsString()` 去读 bool**，那不会替你转成 "true"）。</summary>
    private static string NodeStr(
        Godot.Collections.Dictionary nodes, string nodeId, string field) =>
        nodes[nodeId].AsGodotDictionary()[field].AsString();

    private static bool NodeBool(
        Godot.Collections.Dictionary nodes, string nodeId, string field) =>
        nodes[nodeId].AsGodotDictionary()[field].AsBool();

    /// <summary>CSV 单元格取值：列不在表头里就给兜底（`Dictionary.Get(key, default)` 那个语义）。</summary>
    private static string CellStr(
        Godot.Collections.Dictionary row, string column, string fallback = "") =>
        row.ContainsKey(column) ? row[column].AsString() : fallback;
}
