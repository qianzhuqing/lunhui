using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 大地图的**地标表现与位点摆放**（`BuildOverworldCs` 的另一半，拆自同一个类）。
///
/// 拆出来的理由只有一条：单文件 800 行硬上限（`tests/test_handshake_repo.gd::_check_file_size`）。
/// 骨架／表／几何／自然边界留在 `BuildOverworldCs.cs`，这里只管「把地标和位点画上去」。
/// </summary>
public partial class BuildOverworldCs
{
    // ---------------------------------------------------------------- 地标表现

    /// <summary>16 号写得很清楚：**城镇不是一个贴上去的图标，而是一片拼出来的地形**——
    /// 屋舍／院墙／道路一起构成轮廓，图标（48×48）只叠在上面强调。
    /// 屋顶与院墙压在节点的**北面**：官道从东侧过，那一圈必须空着才走得出去。</summary>
    private static void PaintTown(
        TileMapLayer ground, TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var center = NodeCell(nodes, "n_qingfengyi");
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                ground.SetCell(center + new Vector2I(dx, dy), MapKit.source_id(), MapKit.t_dirt());
            }
        }

        for (int dx = -2; dx <= 2; dx++)
        {
            decor.SetCell(center + new Vector2I(dx, -2), MapKit.source_id(), MapKit.t_roof_slate());
            var wall = MapKit.t_wall_gray();
            if (dx == -1)
            {
                wall = MapKit.t_window_gray();
            }
            else if (dx == 1)
            {
                wall = MapKit.t_door_gray();
            }

            decor.SetCell(center + new Vector2I(dx, -1), MapKit.source_id(), wall);
        }

        decor.SetCell(center + new Vector2I(-3, -1), MapKit.source_id(), MapKit.t_fence_h());
        decor.SetCell(center + new Vector2I(3, -1), MapKit.source_id(), MapKit.t_fence_post());
    }

    /// <summary>驿站：旗杆马桩的剪影，与城镇的屋舍轮廓区分开（16 §3.5）。</summary>
    private static void PaintPostStation(
        TileMapLayer ground, TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var center = NodeCell(nodes, "n_post_station");
        for (int dx = 1; dx <= 3; dx++)
        {
            ground.SetCell(center + new Vector2I(dx, -2), MapKit.source_id(), MapKit.t_dirt());
        }

        decor.SetCell(center + new Vector2I(2, -3), MapKit.source_id(), MapKit.t_roof_slate_top());
        decor.SetCell(center + new Vector2I(3, -3), MapKit.source_id(), MapKit.t_roof_slate_top());
        decor.SetCell(center + new Vector2I(2, -2), MapKit.source_id(), MapKit.t_wall_wood());
        decor.SetCell(center + new Vector2I(3, -2), MapKit.source_id(), MapKit.t_door_wood());
        decor.SetCell(center + new Vector2I(2, -1), MapKit.source_id(), MapKit.t_fence_h());
        decor.SetCell(center + new Vector2I(4, -1), MapKit.source_id(), MapKit.t_fence_post());
    }

    /// <summary>荒村：**焦黑屋舍群**（16 §3.5）——几间塌了顶的房子成组出现，不是一枚图标。</summary>
    private static void PaintVillage(TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var center = NodeCell(nodes, "n_huangcun");
        RuinedHouse(decor, center + new Vector2I(2, -3));
        RuinedHouse(decor, center + new Vector2I(3, 1));
        RuinedHouse(decor, center + new Vector2I(1, 4));
    }

    private static void RuinedHouse(TileMapLayer decor, Vector2I at)
    {
        decor.SetCell(at, MapKit.source_id(), MapKit.t_wall_wood());
        decor.SetCell(at + Vector2I.Right, MapKit.source_id(), MapKit.t_wall_gray());
        decor.SetCell(at + Vector2I.Down, MapKit.source_id(), MapKit.t_window_wood());
        decor.SetCell(at + new Vector2I(1, 1), MapKit.source_id(), MapKit.t_wall_gray());
    }

    /// <summary>落雁坡：靠主路一侧几株显眼的孤树（16 §4.1 的视觉签名，玩家用来定位）。</summary>
    private static void PaintGrove(TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var at = NodeCell(nodes, "n_luoyanpo") + new Vector2I(7, -3);
        decor.SetCell(at, MapKit.source_id(), MapKit.t_tree_pine());
        decor.SetCell(at + new Vector2I(1, 1), MapKit.source_id(), MapKit.t_tree_round());
        decor.SetCell(at + new Vector2I(-1, 2), MapKit.source_id(), MapKit.t_tree_autumn());
    }

    /// <summary>官道关卡（07 §十一 第 12 条）：拒马／木栅栏／官差旗摆在**路肩北侧**，
    /// 判定点本身留在可走的官道上——玩家是走到关卡按 E，不是撞进栅栏里。</summary>
    private static void PaintPatrolCheckpoint(
        TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var cell = EventCell(nodes, "ev_patrol_check");
        decor.SetCell(cell + new Vector2I(-1, -3), MapKit.source_id(), MapKit.t_fence_h());
        decor.SetCell(cell + new Vector2I(1, -3), MapKit.source_id(), MapKit.t_fence_post());
        decor.SetCell(cell + new Vector2I(-2, -3), MapKit.source_id(), MapKit.t_crate());
        decor.SetCell(cell + new Vector2I(2, -3), MapKit.source_id(), MapKit.t_sign());
    }

    /// <summary>旧镖车：车辕断、镖旗褪色，是林铁山守了十年的那辆——**摆在路肩，不挡路**。
    ///
    /// 0.32.0：美术的 prop（`prop_luoyanpo_jiuche.png`，**64×64 ＝ 2×2 格**）一交付就顶掉原来的
    /// 4 格占位瓦片——「翻倒的车厢 ＋ 散落的镖货 ＋ 断掉的车辕 ＋ 只剩一个字的镖旗」四样都画在图里了，
    /// 所以四格一起撤。
    ///
    /// **挂 `Decor` 下、不挂 `Markers` 下**：大地图有雾层，挂 Markers 的会盖在雾上面——
    /// 车队所在那段路还没揭开时就会露出来。挂 Decor 才是「世界里的东西」，雾照样能盖住它。
    /// 图缺失时退回那 4 格瓦片：别让这辆车**静静地消失**（那是这套约定最怕的一种坏法）。</summary>
    private static void PaintOldCart(TileMapLayer decor, Godot.Collections.Dictionary nodes)
    {
        var cell = CartCell(nodes);
        if (ResourceLoader.Exists(MapKit.prop_luoyanpo_cart()))
        {
            // 64×64 对 2×2 格：左上角压在 `CartCell()` 上，所以中心落在「(cell + 1) 个格」的格心。
            var center = new Vector2(
                (cell.X + 1) * MapKit.tile_px(), (cell.Y + 1) * MapKit.tile_px());
            MapKit.add_sprite(decor, "prop_luoyanpo_jiuche", MapKit.prop_luoyanpo_cart(), center);
            return;
        }

        decor.SetCell(cell, MapKit.source_id(), MapKit.t_crate());                    // 翻倒的车厢
        decor.SetCell(cell + new Vector2I(1, 0), MapKit.source_id(), MapKit.t_barrel());  // 散落的镖货
        decor.SetCell(cell + new Vector2I(-1, 0), MapKit.source_id(), MapKit.t_fence_h()); // 断掉的车辕
        decor.SetCell(cell + new Vector2I(1, 1), MapKit.source_id(), MapKit.t_sign());    // 只剩「威」字的镖旗
    }

    /// <summary>指路牌（0.31.2）：主路 4 个节点附近各一块，`Sign_&lt;node_id&gt;` 与表里的 id 对齐。</summary>
    private static void PaintSignposts(
        TileMapLayer decor, Node2D markers, Godot.Collections.Dictionary nodes)
    {
        foreach (string nodeId in SignpostNodes)
        {
            var cell = NodeCell(nodes, nodeId) + SignpostOffsets[nodeId].AsVector2I();
            decor.SetCell(cell, MapKit.source_id(), MapKit.t_signpost());
            MapKit.add_marker(markers, "Sign_" + nodeId, cell);
        }
    }

    private static void PlaceNpcSlots(Node2D characters, Godot.Collections.Dictionary nodes)
    {
        // 林铁山：蹲在镖车边。NPC 站位沿用 `npc_slot_0N` 占位（等对话表落地再按 id 绑）。
        var marker = MapKit.add_marker(
            characters, "npc_slot_01", CartCell(nodes) + new Vector2I(0, 2));
        MapKit.add_sprite(marker, "placeholder", MapKit.npc_texture(), Vector2.Zero);
    }

    private static void PlaceMarkers(Node2D markers, Godot.Collections.Dictionary nodes)
    {
        foreach (Variant nodeIdVariant in nodes.Keys)
        {
            string nodeId = nodeIdVariant.AsString();
            var data = nodes[nodeId].AsGodotDictionary();
            var marker = MapKit.add_marker_at(markers, "Node_" + nodeId, data["pixel"].AsVector2());
            string icon = data["icon"].AsString();
            if (icon.Length > 0)
            {
                MapKit.add_sprite(marker, "icon", IconDir + icon + ".png", Vector2.Zero);
            }
        }

        foreach (Variant rowVariant in MapKit.rows_where(RegionCsv, "parent_region", ParentRegion))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = CellStr(row, "enter_scene");
            if (sceneId.Length == 0)
            {
                continue;
            }

            var anchor = nodes[row["node_id"].AsString()].AsGodotDictionary();
            MapKit.add_marker_at(markers, "Portal_" + sceneId, anchor["pixel"].AsVector2());
        }

        foreach (Variant rowVariant in MapKit.read_csv(SpawnCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string spawnId = row["spawn_id"].AsString();
            if (!SpawnOffsets.ContainsKey(spawnId))
            {
                continue;
            }

            var marker = MapKit.add_marker(markers, "Spawn_" + spawnId, SpawnCell(nodes, spawnId));
            double alert = CellStr(row, "alert_radius", "0").ToFloat();
            if (alert > 0.0)
            {
                MapKit.add_alert(marker, alert);
            }
        }

        foreach (Variant pathIdVariant in PatrolPaths.Keys)
        {
            string pathId = pathIdVariant.AsString();
            var path = PatrolPaths[pathId].AsGodotDictionary();
            var anchor = NodeCell(nodes, path["anchor"].AsString());
            var points = new Godot.Collections.Array();
            foreach (Variant offset in path["points"].AsGodotArray())
            {
                points.Add(anchor + offset.AsVector2I());
            }

            MapKit.add_path(markers, "Patrol_" + pathId, points);
        }

        foreach (Variant eventIdVariant in EventSites.Keys)
        {
            string eventId = eventIdVariant.AsString();
            MapKit.add_marker(markers, "Event_" + eventId, EventCell(nodes, eventId));
        }

        foreach (Variant pointIdVariant in ObserveOffsets.Keys)
        {
            string pointId = pointIdVariant.AsString();
            MapKit.add_marker(markers, "Observe_" + pointId, ObserveCell(nodes, pointId));
        }
    }

}
