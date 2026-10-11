using System;
using System.Collections.Generic;
using Godot;
using Lunhui.World;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 地图验收的**前两块逐条检查**（`VerifyMapsCs` 的另一半，拆自同一个类）。
///
/// 拆出来的理由只有一条：单文件 800 行硬上限（`tests/test_handshake_repo.gd::_check_file_size`）。
/// 入口与基础设施（载入／洪水填充／挡路判定／瓦片预算）留在 `VerifyMapsCs.cs`。
/// 第三块（归属与资产：固定敌人／回程出口／NPC 位点／观察点／建筑与设施）在
/// `VerifyMapsCsChecksAssets.cs`。
///
/// **打印顺序照旧**：`[check]` 那些行是按 `_run` 里调用检查的顺序打出来的，
/// `check_maps.bat` 用 findstr 把它们捞给用户看——换顺序会让前后两次跑的输出没法直接比。
/// </summary>
public partial class VerifyMapsCs
{
    // ---------------------------------------------------------------- 命名与归属

    private void CheckOverworld()
    {
        var scene = Scene(Overworld);
        if (scene == null)
        {
            return;
        }

        var names = scene.Names;

        Compare("大地图·Node_", Column(RegionCsv, "node_id", "parent_region", ParentRegion),
            GroupNames(names, "Node_"), "Node_");
        Compare("大地图·Portal_", Column(LocalCsv, "scene_id", "", ""),
            GroupNames(names, "Portal_"), "Portal_");
        Compare("大地图·Spawn_", Column(SpawnCsv, "spawn_id", "", ""),
            GroupNames(names, "Spawn_"), "Spawn_");

        var patrols = new List<string>();
        foreach (Variant rowVariant in MapKit.read_csv(SpawnCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string pathId = row.ContainsKey("patrol_path_id") ? row["patrol_path_id"].AsString() : "";
            if (pathId.Length > 0 && !patrols.Contains(pathId))
            {
                patrols.Add(pathId);
            }
        }

        Compare("大地图·Patrol_", patrols, GroupNames(names, "Patrol_"), "Patrol_");

        // 判定位点在 07 文档里分了大地图/小地图两处：`scene_id` 非空的归小地图；
        // `scene_id` 空的默认归大地图——**除非它的位点被摆进了某张小地图**（赌局就在清风驿·客栈里）。
        // 这里以前写死 `check_id != "ev_gamble"`：一条数据改了名，这个「例外」就变成谎言，
        // 而且它正是「位点摆好了、代码却没接上」那个真 bug 的遮羞布（见框架说明决策 75）。
        var localEventNames = new Godot.Collections.Dictionary();
        foreach (string path in _sceneOrder)
        {
            if (path == Overworld)
            {
                continue;    // 只统计**小地图**里的位点；把大地图自己算进来会让每条都「不算大地图」，
                //             结果是期望列表直接空掉（第一版就这么错，被下面的双向比对当场抓出来）
            }

            var localScene = _scenes[path];
            if (localScene == null)
            {
                continue;
            }

            foreach (Variant markerNameVariant in localScene.Names.Keys)
            {
                string markerName = markerNameVariant.AsString();
                if (markerName.StartsWith("Event_", StringComparison.Ordinal))
                {
                    localEventNames[markerName] = true;
                }
            }
        }

        var overworldEvents = new List<string>();
        var overworldEventNames = GroupNames(names, "Event_");
        foreach (Variant rowVariant in MapKit.read_csv(EventCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = row.ContainsKey("scene_id") ? row["scene_id"].AsString() : "";
            string checkId = row["check_id"].AsString();
            if (sceneId.Length > 0)
            {
                continue;
            }

            string markerName = "Event_" + checkId;
            // 只在**小地图**里摆了位点、大地图上没有的（赌局）→ 不算大地图那一边；
            // 两边都摆了的（塌陷山洞的碑文：寨外的地标点 + 洞里那半截石碑）→ 大地图这边照样要查。
            if (localEventNames.ContainsKey(markerName) && !overworldEventNames.ContainsKey(markerName))
            {
                continue;
            }

            overworldEvents.Add(checkId);
        }

        Compare("大地图·Event_", overworldEvents, GroupNames(names, "Event_"), "Event_");

        // Spawn_ 必须落在它 region_id 指的区域附近。
        var nodeCells = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.rows_where(RegionCsv, "parent_region", ParentRegion))
        {
            var row = rowVariant.AsGodotDictionary();
            float posX = (float)row["pos_x"].AsString().ToFloat();
            float posY = (float)row["pos_y"].AsString().ToFloat();
            nodeCells[row["node_id"].AsString()] = new Vector2I(
                (int)posX / MapKit.tile_px(), (int)posY / MapKit.tile_px());
        }

        var regionTotals = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(SpawnCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string spawnId = row["spawn_id"].AsString();
            string markerName = "Spawn_" + spawnId;
            if (!names.ContainsKey(markerName))
            {
                continue;
            }

            string regionId = row["region_id"].AsString();
            string regionNode = RegionNodeName(regionId);
            var cell = names[markerName].AsVector2I();
            if (regionNode.Length == 0 || !nodeCells.ContainsKey(regionNode))
            {
                _problems.Add($"大地图·明雷：{spawnId} 的 region_id {regionId} 找不到对应地标");
                continue;
            }

            var anchor = nodeCells[regionNode].AsVector2I();
            if (cell.DistanceTo(anchor) > SpawnRegionRadius)
            {
                _problems.Add(
                    $"大地图·明雷：{spawnId} 离 {regionId} 太远（{(int)cell.DistanceTo(anchor)} 格），疑似放错区域");
            }

            int seen = regionTotals.ContainsKey(regionId) ? regionTotals[regionId].AsInt32() : 0;
            regionTotals[regionId] = seen + 1;
            CheckSpawnRoom(scene, markerName, cell);
        }

        foreach (Variant regionIdVariant in regionTotals.Keys)
        {
            string regionId = regionIdVariant.AsString();
            int count = regionTotals[regionId].AsInt32();
            if (count > MaxSpawnsPerRegion)
            {
                _problems.Add($"大地图：{regionId} 有 {count} 个明雷，超过同屏上限 {MaxSpawnsPerRegion}");
            }
        }

        CheckWalkable(scene, "大地图·地标", GroupNames(names, "Node_"));
        CheckWalkable(scene, "大地图·明雷", GroupNames(names, "Spawn_"));
        CheckWalkable(scene, "大地图·判定位点", GroupNames(names, "Event_"));
        CheckWalkable(scene, "大地图·观察点", GroupNames(names, "Observe_"));
        CheckPatrols(scene);
        GD.Print($"[check] 大地图：Node {GroupNames(names, "Node_").Count} / "
            + $"Portal {GroupNames(names, "Portal_").Count} / Spawn {GroupNames(names, "Spawn_").Count} / "
            + $"Patrol {GroupNames(names, "Patrol_").Count} / Event {GroupNames(names, "Event_").Count}");
    }

    /// <summary>明雷区域 → 大地图地标节点（与 `build_overworld._spawn_cell` 用的那张表同一份口径）。</summary>
    private static string RegionNodeName(string regionId) => regionId switch
    {
        "n_luoyanpo" => "n_luoyanpo",
        "n_heifengzhai" => "n_heifengzhai",
        "n_huangcun" => "n_huangcun",
        _ => "",
    };

    private void CheckLocalMaps(List<string> localScenes)
    {
        var roomsByScene = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(RoomCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = row["scene_id"].AsString();
            if (!roomsByScene.ContainsKey(sceneId))
            {
                roomsByScene[sceneId] = new Godot.Collections.Array();
            }

            roomsByScene[sceneId].AsGodotArray().Add(row["room_id"].AsString());
        }

        // 位点归属要用到的两张反查表：小地图 → 它挂的大地图节点；check_id → 判定行
        var parentByScene = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(LocalCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            parentByScene[row["scene_id"].AsString()] =
                row.ContainsKey("parent_node") ? row["parent_node"].AsString() : "";
        }

        var eventById = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(EventCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            eventById[row["check_id"].AsString()] = row;
        }

        foreach (string sceneId in localScenes)
        {
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            var names = scene.Names;
            var expectedRooms = roomsByScene.ContainsKey(sceneId)
                ? roomsByScene[sceneId].AsGodotArray()
                : new Godot.Collections.Array();
            if (expectedRooms.Count > 0)
            {
                Compare($"{sceneId}·Room_", ToStrings(expectedRooms), GroupNames(names, "Room_"), "Room_");
                CheckRooms(scene, expectedRooms, sceneId);
            }

            // 小地图里的判定位点必须**接得上**：位点的 check_id 在表里，且这条判定属于这张图
            // （`scene_id` 命中，或 `scene_id` 空而 `region_id` 正好是这张图的父节点）。
            // 运行期要接上它还得两个条件同时成立（见 `LocalMapController._collect_events`），
            // 所以这里是对着同一套判据查——**位点摆好了却接不上，以前是验收全绿也发现不了的**：
            // 赌局（Region 行、位点在小地图里）就是这么漏了一整轮。
            string parentNode = parentByScene.ContainsKey(sceneId)
                ? parentByScene[sceneId].AsString()
                : "";
            foreach (Variant markerNameVariant in GroupNames(names, "Event_").Keys)
            {
                string markerName = markerNameVariant.AsString();
                string checkId = markerName.Substring("Event_".Length);
                if (!eventById.ContainsKey(checkId))
                {
                    _problems.Add($"{sceneId}：地图里有 {markerName}，配置表查不到");
                    continue;
                }

                var row = eventById[checkId].AsGodotDictionary();
                string rowScene = row.ContainsKey("scene_id") ? row["scene_id"].AsString() : "";
                string rowRegion = row.ContainsKey("region_id") ? row["region_id"].AsString() : "";
                bool allowed = rowScene == sceneId
                    || (rowScene.Length == 0 && parentNode.Length > 0 && rowRegion == parentNode);
                if (!allowed)
                {
                    _problems.Add(
                        $"{sceneId}：地图里有 {markerName}，但这条判定不属于这张图"
                        + $"（scene_id={rowScene} region_id={rowRegion}）");
                }
            }

            GD.Print($"[check] {sceneId}：Room {GroupNames(names, "Room_").Count} / "
                + $"Chest {GroupNames(names, "Chest_").Count} / "
                + $"Trigger {GroupNames(names, "Trigger_").Count} / "
                + $"Event {GroupNames(names, "Event_").Count}");
        }

        // 宝箱 / 触发 / 判定：数量与归属都要对得上。
        var chestExpected = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(RoomCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string chestId = row.ContainsKey("chest_id") ? row["chest_id"].AsString() : "";
            if (chestId.Length == 0)
            {
                continue;
            }

            chestExpected[row["room_id"].AsString()] = "Chest_" + chestId;
        }

        CompareRoomItems("Chest_", chestExpected, "dungeon_room.chest_id");

        var triggerExpected = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(TriggerCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            triggerExpected[row["room_id"].AsString()] = "Trigger_" + row["trigger_id"].AsString();
        }

        // trig_wine 在 hf1_deep 与 hf3_dungeon 各一个，表里只写了一行。
        triggerExpected["hf3_dungeon"] = "Trigger_trig_wine";
        triggerExpected["cave_02"] = "Trigger_trig_dig";
        CompareRoomItems("Trigger_", triggerExpected, "hidden_trigger.trigger_id");
        CheckSequenceMarkers();

        var eventExpected = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(EventCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = row.ContainsKey("scene_id") ? row["scene_id"].AsString() : "";
            if (sceneId.Length > 0)
            {
                eventExpected[row["room_id"].AsString()] = "Event_" + row["check_id"].AsString();
            }
        }

        CompareRoomItems("Event_", eventExpected, "event_check.check_id");

        // 观察点（设计 20 §3.2／0.29.1）：表里 `flavor_point.csv` 一条一个位点，
        // 位点名 `Observe_<point_id>`。房间级的按房间比；`region_id` 那几条在大地图上，
        // 由「大地图 Marker 并集」那条统一收（前缀已进 MARKER_PREFIXES）。
        var flavorExpected = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(FlavorCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = row.ContainsKey("scene_id") ? row["scene_id"].AsString() : "";
            if (sceneId.Length == 0)
            {
                continue;
            }

            flavorExpected[row["room_id"].AsString()] = "Observe_" + row["point_id"].AsString();
        }

        CompareRoomItems("Observe_", flavorExpected, "flavor_point.point_id");
    }

    private static List<string> ToStrings(Godot.Collections.Array values)
    {
        var output = new List<string>();
        foreach (Variant value in values)
        {
            output.Add(value.AsString());
        }

        return output;
    }

    /// <summary>房间内的挂钩点：同名 id 分在不同房间文件夹下，这里按「名字出现过」比对。</summary>
    private void CompareRoomItems(string label, Godot.Collections.Dictionary expected, string source)
    {
        var found = new Godot.Collections.Dictionary();
        foreach (string path in _sceneOrder)
        {
            var scene = _scenes[path];
            if (scene == null)
            {
                continue;
            }

            foreach (Variant nameVariant in scene.Names.Keys)
            {
                string markerName = nameVariant.AsString();
                if (markerName.StartsWith(label, StringComparison.Ordinal))
                {
                    found[markerName] = true;
                }
            }
        }

        foreach (Variant roomIdVariant in expected.Keys)
        {
            string roomId = roomIdVariant.AsString();
            string markerName = expected[roomId].AsString();
            // 顺序谜题（三火盆）是**同一行触发 + 三个编号位点**：`Trigger_trig_brazier_1/2/3`。
            // 所以带编号后缀的位点也算数（命名约定见 07「地图资源需求」与 `TriggerPoint.cs`）。
            if (!found.ContainsKey(markerName) && !HasNumberedVariant(found, markerName))
            {
                _problems.Add($"{source}：{roomId} 声明了 {markerName}，地图里没有");
            }
        }
    }

    /// <summary>顺序谜题（三火盆）：代码侧 2026-10-03 已支持，缺的是**三个编号位点**。
    /// 这一条**不判失败**（缺资产不该挡住整条验收链，与雾／精英贴图同一口径），
    /// 但每次跑地图验收都打印出来——否则「谜题摆不上」这件事没有任何地方提醒。</summary>
    private void CheckSequenceMarkers()
    {
        foreach (Variant rowVariant in MapKit.read_csv(TriggerCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string triggerType = row.ContainsKey("trigger_type") ? row["trigger_type"].AsString() : "";
            if (triggerType != "sequence")
            {
                continue;
            }

            string triggerId = row["trigger_id"].AsString();
            string stem = "Trigger_" + triggerId;
            int steps = 0;
            string condition = row.ContainsKey("required_condition")
                ? row["required_condition"].AsString()
                : "";
            foreach (string piece in condition.Split(";", StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = piece.Split("=", StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length == 2 && kv[0].Trim() == "sequence")
                {
                    steps = Math.Max(
                        steps, kv[1].Trim().Split("-", StringSplitOptions.RemoveEmptyEntries).Length);
                }
            }

            var indexed = new Godot.Collections.Dictionary();
            foreach (string path in _sceneOrder)
            {
                var scene = _scenes[path];
                if (scene == null)
                {
                    continue;
                }

                foreach (Variant nameVariant in scene.Names.Keys)
                {
                    string markerName = nameVariant.AsString();
                    if (!markerName.StartsWith(stem + "_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string suffix = markerName.Substring(stem.Length + 1);
                    if (suffix.IsValidInt())
                    {
                        indexed[suffix.ToInt()] = true;
                    }
                }
            }

            var missing = new List<string>();
            for (int index = 1; index <= steps; index++)
            {
                if (!indexed.ContainsKey(index))
                {
                    missing.Add(index.ToString());
                }
            }

            if (missing.Count == 0)
            {
                continue;
            }

            string nameCn = row.ContainsKey("name_cn") ? row["name_cn"].AsString() : "";
            _assetNotes.Add(
                $"{triggerId}（{nameCn}）：需要 {steps} 个编号位点 {stem}_1…_{steps}，"
                + $"地图里缺第 {string.Join(", ", missing)} 个——"
                + "代码侧已支持（点错提示＋重置＋灭／燃两态），只差摆点");
        }
    }

    // ---------------------------------------------------------------- 连通性与通路

    private void CheckRooms(SceneInfo scene, Godot.Collections.Array expectedRooms, string sceneId)
    {
        var rects = new Godot.Collections.Dictionary();
        foreach (Variant roomIdVariant in expectedRooms)
        {
            string roomId = roomIdVariant.AsString();
            var node = scene.Root.GetNodeOrNull<Node2D>("Rooms/Room_" + roomId);
            if (node == null)
            {
                continue;
            }

            var area = node.GetNodeOrNull<Area2D>("bounds");
            if (area == null)
            {
                _problems.Add($"{sceneId}：Room_{roomId} 缺 Area2D（房间边界，物理层 5）");
                continue;
            }

            var shape = area.GetNodeOrNull<CollisionShape2D>("bounds");
            if (shape == null)
            {
                // MapKit 里 CollisionShape2D 用的是默认名，这里按类型兜一次底。
                foreach (Node child in area.GetChildren())
                {
                    if (child is CollisionShape2D childShape)
                    {
                        shape = childShape;
                        break;
                    }
                }
            }

            if (shape?.Shape is not RectangleShape2D rectShape)
            {
                _problems.Add($"{sceneId}：Room_{roomId} 的房间边界没有形状");
                continue;
            }

            rects[roomId] = new Rect2I(
                (int)node.Position.X / MapKit.tile_px(), (int)node.Position.Y / MapKit.tile_px(),
                (int)rectShape.Size.X / MapKit.tile_px(), (int)rectShape.Size.Y / MapKit.tile_px());
        }

        // 出口对称 + 真的走得通（在地板上洪水填充）。
        var roomRows = MapKit.rows_where(RoomCsv, "scene_id", sceneId);
        var reachable = Flood(scene, EntranceCell(roomRows, rects));
        foreach (Variant rowVariant in roomRows)
        {
            var row = rowVariant.AsGodotDictionary();
            string roomId = row["room_id"].AsString();
            string exits = row.ContainsKey("exit_rooms") ? row["exit_rooms"].AsString() : "";
            foreach (string exitPiece in exits.Split("|", StringSplitOptions.RemoveEmptyEntries))
            {
                string target = exitPiece.Trim();
                if (target.Length == 0 || !rects.ContainsKey(target))
                {
                    continue;
                }

                var back = RowFor(roomRows, target);
                string backExits = back.ContainsKey("exit_rooms") ? back["exit_rooms"].AsString() : "";
                bool symmetric = false;
                foreach (string backPiece in backExits.Split("|", StringSplitOptions.RemoveEmptyEntries))
                {
                    if (backPiece.Trim() == roomId)
                    {
                        symmetric = true;
                        break;
                    }
                }

                if (!symmetric)
                {
                    _dataNotes.Add(
                        $"{sceneId}：{roomId} → {target} 只声明了单向（{target} 的 exit_rooms 里没有 "
                        + $"{roomId}）；地图上走廊是双向的，但表要补回来");
                }

                if (!reachable.ContainsKey(RectCenter(rects[target].AsRect2I())))
                {
                    _problems.Add($"{sceneId}：{roomId} 到 {target} 走不通（走廊被墙堵了或没连）");
                }
            }
        }

        // 没有进不去的房间。
        foreach (Variant roomIdVariant in rects.Keys)
        {
            string roomId = roomIdVariant.AsString();
            var rect = rects[roomId].AsRect2I();
            if (!reachable.ContainsKey(RectCenter(rect)))
            {
                _problems.Add($"{sceneId}：房间 {roomId} 从入口走不到");
            }

            CheckRoomGap(scene, roomId, rect, sceneId);
        }
    }

    /// <summary>战斗房间要能「进门瞬间从侧面绕过」：房间边界上的开口至少 2 格宽。</summary>
    private void CheckRoomGap(SceneInfo scene, string roomId, Rect2I rect, string sceneId)
    {
        var decor = scene.Decor;
        // 四条边分开数：混在一个列表里，相邻边之间会把开口打断。
        var edges = new List<List<bool>>();
        var top = new List<bool>();
        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        {
            top.Add(!Blocked(decor, new Vector2I(x, rect.Position.Y - 1)));
        }

        edges.Add(top);
        var bottom = new List<bool>();
        for (int x = rect.Position.X; x < rect.Position.X + rect.Size.X; x++)
        {
            bottom.Add(!Blocked(decor, new Vector2I(x, rect.Position.Y + rect.Size.Y)));
        }

        edges.Add(bottom);
        var left = new List<bool>();
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            left.Add(!Blocked(decor, new Vector2I(rect.Position.X - 1, y)));
        }

        edges.Add(left);
        var right = new List<bool>();
        for (int y = rect.Position.Y; y < rect.Position.Y + rect.Size.Y; y++)
        {
            right.Add(!Blocked(decor, new Vector2I(rect.Position.X + rect.Size.X, y)));
        }

        edges.Add(right);

        int longest = 0;
        foreach (var edge in edges)
        {
            int current = 0;
            foreach (bool isOpen in edge)
            {
                current = isOpen ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }
        }

        if (longest < 2)
        {
            _problems.Add($"{sceneId}：房间 {roomId} 的开口不足 2 格宽（侧绕突袭做不了）");
        }
    }

    /// <summary>明雷周围的通路检查（07 文档验收 3）。表里只给了警戒半径，没给朝向，
    /// 所以这里用「能不能绕过去」的启发式：横竖至少一个方向有 ≥2 格连续可走，
    /// 且 3×3 邻域里至少 5 格能站人——满足这两条才走得进背面。</summary>
    private void CheckSpawnRoom(SceneInfo scene, string markerName, Vector2I cell)
    {
        var decor = scene.Decor;
        if (Blocked(decor, cell))
        {
            _problems.Add($"大地图·明雷：{markerName} 站在障碍里");
            return;
        }

        int bestRun = 0;
        foreach (Vector2I axis in new[] { Vector2I.Right, Vector2I.Down })
        {
            int run = 1;
            for (int step = 1; step <= 3; step++)
            {
                if (!Blocked(decor, cell + axis * step))
                {
                    run += 1;
                }
                else
                {
                    break;
                }
            }

            for (int step = 1; step <= 3; step++)
            {
                if (!Blocked(decor, cell - axis * step))
                {
                    run += 1;
                }
                else
                {
                    break;
                }
            }

            bestRun = Math.Max(bestRun, run);
        }

        int openNeighbours = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!Blocked(decor, cell + new Vector2I(dx, dy)))
                {
                    openNeighbours += 1;
                }
            }
        }

        if (bestRun < 2 || openNeighbours < 5)
        {
            _problems.Add(
                $"大地图·明雷：{markerName} 周围没有 2 格宽的通路"
                + $"（可站 {openNeighbours}/9，最长直通 {bestRun}）");
        }
    }

    private void CheckPatrols(SceneInfo scene)
    {
        var decor = scene.Decor;
        foreach (Variant pathNameVariant in scene.Paths.Keys)
        {
            string pathName = pathNameVariant.AsString();
            var path = scene.Paths[pathName].As<Path2D>();
            for (int index = 0; index < path.Curve.PointCount; index++)
            {
                var point = path.Curve.GetPointPosition(index);
                var cell = new Vector2I(
                    (int)point.X / MapKit.tile_px(), (int)point.Y / MapKit.tile_px());
                if (Blocked(decor, cell))
                {
                    _problems.Add($"大地图·巡逻线：{pathName} 的端点 {cell} 卡在障碍里");
                }
            }
        }
    }

    private void CheckWalkable(
        SceneInfo scene, string label, Godot.Collections.Dictionary markers)
    {
        var decor = scene.Decor;
        foreach (Variant markerNameVariant in markers.Keys)
        {
            string markerName = markerNameVariant.AsString();
            var cell = markers[markerName].AsVector2I();
            var blocking = BlockingTile(decor, cell);
            if (blocking.X >= 0)
            {
                _problems.Add($"{label}：{markerName} 的格子 {cell} 被障碍 {blocking} 挡住");
            }
        }
    }
}
