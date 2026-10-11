using System;
using System.Collections.Generic;
using Godot;
using Lunhui.World;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 地图验收的**归属与资产类检查**（`VerifyMapsCs` 的第三块，拆自同一个类）。
///
/// 拆出来的理由只有一条：单文件 800 行硬上限（`tests/test_handshake_repo.gd::_check_file_size`）。
/// 前两块在 `VerifyMapsCsChecks.cs`（命名与归属／连通性与通路），入口与基础设施在 `VerifyMapsCs.cs`。
/// **打印顺序照旧**：这几条 `[check]` 行紧接在通路类之后，`check_maps.bat` 用 findstr 捞给用户看。
/// </summary>
public partial class VerifyMapsCs
{
    // ---------------------------------------------------------------- 归属与资产

    /// <summary>固定敌人：`dungeon_room.enemy_team` 非空的房间都要有 `Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt;`。</summary>
    private void CheckTeamMarkers()
    {
        foreach (Variant rowVariant in MapKit.read_csv(RoomCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string teamId = row.ContainsKey("enemy_team") ? row["enemy_team"].AsString() : "";
            if (teamId.Length == 0)
            {
                continue;
            }

            string sceneId = row["scene_id"].AsString();
            string roomId = row["room_id"].AsString();
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                _problems.Add($"{sceneId}：{roomId} 声明了敌人 {teamId}，但场景读不到");
                continue;
            }

            string holder = $"Enemies/{roomId}/Team_{teamId}";
            if (!scene.Items.ContainsKey(holder))
            {
                _problems.Add($"{sceneId}：{roomId} 要有 {holder}（dungeon_room.enemy_team）");
            }
        }
    }

    /// <summary>回程出口：可进入的小地图都要有 `Exit_&lt;scene_id&gt;`，且落在入口房间。</summary>
    private void CheckExitMarkers(List<string> localScenes)
    {
        var entranceByScene = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(RoomCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string sceneId = row["scene_id"].AsString();
            // 黑风寨有三层、每层一个 entrance 房间，取**第一个**才是从大地图进来的那个。
            string roomType = row.ContainsKey("room_type") ? row["room_type"].AsString() : "";
            if (roomType == "entrance" && !entranceByScene.ContainsKey(sceneId))
            {
                entranceByScene[sceneId] = row["room_id"].AsString();
            }
        }

        foreach (string sceneId in localScenes)
        {
            if (sceneId == "scene_ferry_locked")
            {
                continue;  // 本章不可进入，按 07 文档 4.0 不做出口
            }

            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            string exitName = "Exit_" + sceneId;
            bool found = false;
            foreach (Variant itemPathVariant in scene.Items.Keys)
            {
                string itemPath = itemPathVariant.AsString();
                var item = scene.Items[itemPath].AsGodotDictionary();
                if (item["name"].AsString() != exitName)
                {
                    continue;
                }

                found = true;
                string entrance = entranceByScene.ContainsKey(sceneId)
                    ? entranceByScene[sceneId].AsString()
                    : "";
                if (entrance.Length > 0 && !itemPath.Contains($"Markers/{entrance}/", StringComparison.Ordinal))
                {
                    _problems.Add($"{sceneId}：{exitName} 应放在入口房间 {entrance} 下，现在在 {itemPath}");
                }
            }

            if (!found)
            {
                _problems.Add($"{sceneId}：缺少回程出口 {exitName}");
            }
        }
    }

    /// <summary>07 §9 第 12 条 ＋ §12：城镇 NPC 站位**两种命名都认**——占位 `Characters/npc_slot_0N`，
    /// 或按 id 绑的 `Characters/npc_&lt;npc_id&gt;`（对话表 0.31.0 落表后就可以这么改，代码侧已支持，见决策 332）。
    ///
    /// 没表可对不等于没得查——这里钉四件**能钉的事**：**命名**（含按 id 绑的那个 id 真的在这个地点上）、
    /// **没卡在障碍里**、**从出生点走得到**、**城镇的站位数量**。
    /// 一个卡在墙里／走不到的 NPC，等对话表到了也点不着；而这类问题在验收里原本**完全看不见**
    /// （`_walk` 只收 `MARKER_PREFIXES` 里那几种，`npc_slot_` 不在其中）。</summary>
    private void CheckNpcSlots(List<string> localScenes)
    {
        var typeByScene = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(LocalCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            typeByScene[row["scene_id"].AsString()] =
                row.ContainsKey("scene_type") ? row["scene_type"].AsString() : "";
        }

        // 按 id 绑的位点要能核：`npc_<npc_id>` 得是 `npc_def` 里的人，而且**就在这张图上**
        // （`place_id` 命中本场景，或命中本场景的父区域节点——与 `NpcService.npcs_at` 同一口径）。
        var npcPlace = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(NpcCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            npcPlace[row.ContainsKey("npc_id") ? row["npc_id"].AsString() : ""] =
                row.ContainsKey("place_id") ? row["place_id"].AsString() : "";
        }

        foreach (string sceneId in localScenes)
        {
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            var root = scene.Root;
            var slots = root.FindChildren("npc_*", "Node2D", true, false);
            bool isTown = (typeByScene.ContainsKey(sceneId)
                ? typeByScene[sceneId].AsString()
                : "") == "town";
            if (slots.Count == 0)
            {
                if (isTown)
                {
                    _problems.Add($"{sceneId} 是城镇却没有 `Characters/npc_slot_0N`（07 §9 第 12 条／§12）");
                }

                continue;
            }

            var decor = scene.Decor;
            var spawnCell = CharacterCell(root, "player_spawn");
            if (spawnCell.X < 0)
            {
                _problems.Add($"{sceneId}：有 NPC 站位，但找不到 `Characters/player_spawn`（没法验「走得到」）");
            }

            var reachable = spawnCell.X >= 0 && decor != null
                ? Flood(scene, spawnCell)
                : new Godot.Collections.Dictionary();
            foreach (Node slot in slots)
            {
                string slotName = slot.Name.ToString();
                if (slotName.StartsWith("npc_slot_", StringComparison.Ordinal))
                {
                    string suffix = slotName.Substring("npc_slot_".Length);
                    if (!suffix.IsValidInt())
                    {
                        _problems.Add($"{sceneId}：{slotName} 的编号不是数字（07 §12 约定 `npc_slot_0N`）");
                    }
                }
                else if (!npcPlace.ContainsKey(slotName))
                {
                    // 按 id 绑：名字本身就是 `npc_<npc_id>`
                    _problems.Add(
                        $"{sceneId}：{slotName} 是「按 id 绑」的位点，但 npc_def 里没有这个人（07 §九 第 12 条）");
                }
                else
                {
                    string want = npcPlace[slotName].AsString();
                    if (want != sceneId && want != ParentNodeOf(sceneId))
                    {
                        _problems.Add(
                            $"{sceneId}：{slotName} 这个人不在本图（npc_def 里他绑的是 {want}）——位点摆错地图了");
                    }
                }

                var parent = slot.GetParent();
                if (parent == null || parent.Name.ToString() != "Characters")
                {
                    _problems.Add($"{sceneId}：{slotName} 不在 `Characters/` 下（07 §2 的节点结构）");
                }

                var cell = ToCell(MapKit.accumulated_position(slot as Node2D));
                var blocking = BlockingTile(decor, cell);
                if (blocking.X >= 0)
                {
                    _problems.Add(
                        $"{sceneId}：NPC 站位 {slotName} 的格子 {cell} 被障碍 {blocking} 挡住（NPC 会卡在墙里）");
                }

                if (reachable.Count > 0 && !reachable.ContainsKey(cell))
                {
                    _problems.Add($"{sceneId}：NPC 站位 {slotName}（{cell}）从出生点走不到——玩家点不到他");
                }
            }

            if (isTown && slots.Count != 5)
            {
                _assetNotes.Add($"{sceneId}：NPC 站位 {slots.Count} 个（07 §8.6 的速览写 5 个）");
            }

            GD.Print($"[check] {sceneId}：NPC 站位 {slots.Count} 个");
        }
    }

    /// <summary>观察点（0.31.0 起 `flavor_point` 表已上线）：这里**只查几何**——不卡在墙里、
    /// 且从出生点走得到，也就是设计要的「必须看得见」。
    /// 名字与房间归属由 `tests/TestMapAssets.cs::_check_flavor_points` 按表硬校验（那边是权威）；
    /// 大地图那 3 条在 `CheckOverworld` 里查。</summary>
    private void CheckObservePoints(List<string> localScenes)
    {
        int total = 0;
        foreach (string sceneId in localScenes)
        {
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            var points = GroupNames(scene.Names, "Observe_");
            if (points.Count == 0)
            {
                continue;
            }

            total += points.Count;
            CheckWalkable(scene, $"{sceneId}·观察点", points);
            var spawnCell = CharacterCell(scene.Root, "player_spawn");
            if (spawnCell.X >= 0)
            {
                var reachable = Flood(scene, spawnCell);
                foreach (Variant pointIdVariant in points.Keys)
                {
                    string pointId = pointIdVariant.AsString();
                    if (!reachable.ContainsKey(points[pointId]))
                    {
                        _problems.Add(
                            $"{sceneId}：观察点 {pointId}（{points[pointId].AsVector2I()}）"
                            + "从出生点走不到——玩家看不见它");
                    }
                }
            }

            GD.Print($"[check] {sceneId}：观察点 {points.Count} 个");
        }

        if (total > 0)
        {
            // 大地图那几处同源，**当场算**：写死「另加大地图 3 个 = 17」只会随表行数漂移
            // （0.32.0 加了渡口封渡木桩那一条，这个数字当场就过期了）。
            var worldScene = Scene(Overworld);
            int worldPoints = worldScene == null
                ? 0
                : GroupNames(worldScene.Names, "Observe_").Count;
            GD.Print($"[check] 小地图观察点合计 {total} 个（另加大地图 {worldPoints} 个 = "
                + $"{total + worldPoints}）");
        }
    }

    /// <summary>建筑位点（`Markers/Buildings/&lt;building_id&gt;`）两道门限：
    ///   ① **名字必须是 `building_def` 里的 id**——代码就是按 id 查表的（`building_near_player`），
    ///      查不到就**静默忽略**：玩家走到店门口按 E 什么都不发生，而地图验收原本一声不吭；
    ///   ② **站得到跟前**——交互半径是 `BUILDING_DISTANCE`(48px)，位点周围 48px 内至少要有一个
    ///      「从出生点走得到」的格子，否则**这家店永远开不了**。
    ///
    /// 以前这两件一条都没查（只查了 NPC 站位／观察点／无表设施）。2026-10-04 玩家报的
    /// 「店铺经常打不开」里，一半是浮层挂错父节点（见 `OverlayStack.mount`），另一半正是这一类：
    /// 位点写错名字或离可站格太远。**按 id 查表 + 半径站得到**这两条都得在构建期拦住。</summary>
    private void CheckBuildingMarkers(List<string> localScenes)
    {
        var known = new Godot.Collections.Dictionary();
        foreach (Variant rowVariant in MapKit.read_csv(BuildingCsv))
        {
            var row = rowVariant.AsGodotDictionary();
            string buildingId = row.ContainsKey("building_id") ? row["building_id"].AsString() : "";
            if (buildingId.Length > 0)
            {
                known[buildingId] = true;
            }
        }

        if (known.Count == 0)
        {
            _problems.Add("读不到 building_def.csv——建筑位点这条门限会静默失效");
        }

        float radius = LocalMapController.BUILDING_DISTANCE;
        foreach (string sceneId in localScenes)
        {
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            // 建筑位点**不在** `Names` 那本字典里（它只收 `MARKER_PREFIXES` 里那些前缀的节点，
            // `bld_*` 不在其中），所以直接按控制器的取法遍历 `Markers/Buildings` 的子节点。
            var holder = scene.Root.GetNodeOrNull<Node>("Markers/Buildings");
            if (holder == null)
            {
                continue;
            }

            var markers = holder.GetChildren();
            if (markers.Count == 0)
            {
                continue;
            }

            var spawnCell = CharacterCell(scene.Root, "player_spawn");
            var reachable = spawnCell.X >= 0
                ? Flood(scene, spawnCell)
                : new Godot.Collections.Dictionary();
            if (reachable.Count == 0)
            {
                _problems.Add($"{sceneId}：有建筑位点却从出生点走不到任何格子（没法验「站得到跟前」）");
            }

            foreach (Node marker in markers)
            {
                string buildingId = marker.Name.ToString();
                if (!known.ContainsKey(buildingId))
                {
                    _problems.Add(
                        $"{sceneId}：建筑位点 {buildingId} 不是 building_def 里的 id——"
                        + "代码按 id 查表，查不到就静默忽略（走到跟前按 E 什么都不发生）");
                }

                var markerPx = MapKit.accumulated_position(marker as Node2D);
                float nearest = -1.0f;
                foreach (Variant reachCellVariant in reachable.Keys)
                {
                    var reachCell = reachCellVariant.AsVector2I();
                    var reachPx = new Vector2(
                        reachCell.X * MapKit.tile_px() + MapKit.tile_px() * 0.5f,
                        reachCell.Y * MapKit.tile_px() + MapKit.tile_px() * 0.5f);
                    float distance = reachPx.DistanceTo(markerPx);
                    if (nearest < 0.0f || distance < nearest)
                    {
                        nearest = distance;
                    }

                    if (nearest <= radius)
                    {
                        break;
                    }
                }

                if (nearest > radius)
                {
                    _problems.Add(
                        $"{sceneId}：建筑位点 {buildingId}（{markerPx.X:F0},{markerPx.Y:F0}）"
                        + $"最近的可站格在 {nearest:F0}px 外，超过交互半径 {radius:F0}px——"
                        + "玩家站不到跟前、这家店开不了");
                }
            }

            GD.Print($"[check] {sceneId}：建筑位点 {markers.Count} 个（交互半径 {radius:F0}px）");
        }
    }

    /// <summary>无表设施（当铺／悬赏板／客栈）的位点名必须在控制器的 `FACILITY_LABELS` 里。
    ///
    /// 这三处在 07 文档里是「无表信息点」，所以没有表可以比对——但代码认的名字只有那三个：
    /// 改名（`facility_smith` 之类）会让玩家走到跟前按 E 得到「数据错」（运行时已出声，决策 242），
    /// 而**地图验收原本一声不吭**。这里对着控制器那份唯一白名单查，把它挪到构建期拦。</summary>
    private void CheckFacilityMarkers(List<string> localScenes)
    {
        var known = new List<string>();
        foreach (Variant key in LocalMapController.FACILITY_LABELS.Keys)
        {
            known.Add(key.AsString());
        }

        foreach (string sceneId in localScenes)
        {
            var scene = Scene($"res://scenes/maps/{sceneId}.tscn");
            if (scene == null)
            {
                continue;
            }

            var markers = scene.Root.FindChildren("facility_*", "Node2D", true, false);
            foreach (Node marker in markers)
            {
                string markerName = marker.Name.ToString();
                if (known.Contains(markerName))
                {
                    continue;
                }

                _problems.Add(
                    $"{sceneId}：设施位点 {markerName} 不在代码认的清单里（{string.Join(", ", known)}）"
                    + "——改名会让这一处按 E 只得到「数据错」");
            }

            if (markers.Count > 0)
            {
                GD.Print($"[check] {sceneId}：设施位点 {markers.Count} 个");
            }
        }
    }
}
