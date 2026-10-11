using System.Collections.Generic;
using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Tools.Mapgen;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 地图资源自检（C# 版，对位原 `tests/test_map_assets.gd`）：场景里的 Marker 与配置表双向比对。
///
/// 这是 07_地图资源需求.md 第二节承诺的那条自检——地编的场景进仓库后由开发侧卡住：
///   * 表里有、地图没有 → 少放（或名字写错）
///   * 地图有、表里没有 → 多放（或名字写错）
///   * 放错房间/区域 → 归属不对
///
/// 关键实现细节：**Marker 用列表存，不能按名字去重**。同一个 id 会在不同房间重复出现
/// （两个 `Chest_drop_chest_silver`、两处 `Trigger_trig_wine`），去重会少算，
/// 所以每一项都要按「名字 + 所在房间」两段来核对。
/// </summary>
public partial class TestMapAssets : TestCase
{
    private const string Overworld = "res://scenes/maps/overworld.tscn";
    private const string ParentRegion = "jiangnan_east";
    private static readonly string[] AllScenes =
    {
        Overworld,
        "res://scenes/maps/scene_qingfengyi.tscn",
        "res://scenes/maps/scene_cave.tscn",
        "res://scenes/maps/scene_heifengzhai.tscn",
        "res://scenes/maps/scene_huangcun.tscn",
        "res://scenes/maps/scene_ferry_locked.tscn",
        "res://scenes/maps/scene_shixi.tscn",
    };

    /// <summary>Team_ / Exit_ 必须也在白名单里：`_Collect` 只收白名单前缀的节点，
    /// 少了这两个，地编就算把位点摆好，`_CheckPendingRequirements` 也永远找不到它们。</summary>
    private static readonly string[] MarkerPrefixes =
    {
        "Node_", "Portal_", "Room_", "Chest_", "Spawn_", "Event_", "Trigger_", "Patrol_",
        "Team_", "Exit_", "Observe_", "Sign_",
    };

    /// <summary>2026-10-02 审计后补进 07_地图资源需求.md 的两条约定，地编已交付：
    /// `Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt;`（7 个）与 `Exit_&lt;scene_id&gt;`（4 个，渡口不开放不做）。
    /// 开关关掉后这两条就是硬失败，少一个位点自检就会红。</summary>
    // 用 `static readonly` 而不是 `const`：两个都是 `false` 的话，下面那条 `if` 会被编译器
    // 判成死分支（CS0162 无法访问的代码）——开关留着是为了"地编还没交付时先放行"，
    // 不该因为它的当前取值把那条路编译掉。
    private static readonly bool PendingEnemyMarkers = false;
    private static readonly bool PendingExitMarkers = false;

    public override string SuiteName() => "地图资源与表 id 比对";

    public override void Run()
    {
        var db = GetDb();
        if (!FileAccess.FileExists(Overworld))
        {
            Fail($"没有大地图：{Overworld}");
            return;
        }

        _CheckOverworld(db);
        _CheckLocalScenes(db);
        _CheckFlavorPoints(db, _LoadAllMarkers());
        _CheckEventsExist(db, _LoadAllMarkers());
        _CheckPendingRequirements(db);
        _CheckThemeTiles();
        _CheckRegionIcons(db);
        _CheckIconFileNames(db);
        _CheckEquipIconFileNames(db);
        _CheckSignposts(db);
        _CheckMapLayers();
    }


    /// <summary>新加的两条约定：敌人位点与回程出口。</summary>
    private void _CheckPendingRequirements(TableDb db)
    {
        var pending = new List<string>();
        var cache = new Godot.Collections.Dictionary();

        // 1. 房间内的固定敌人：Enemies/<room_id>/Team_<team_id>
        foreach (Variant entry in db.rows("dungeon_room"))
        {
            var row = (DungeonRoomRow)entry.AsGodotObject();
            if (row.enemy_team.Length == 0)
            {
                continue;
            }

            var markers = _SceneMarkers(row.scene_id, cache);
            string wanted = $"Team_{row.enemy_team}";
            bool found = false;
            foreach (Variant markerVariant in markers)
            {
                var marker = markerVariant.AsGodotDictionary();
                if (marker["name"].AsString() == wanted
                    && marker["path"].AsString().Contains($"Enemies/{row.room_id}/"))
                {
                    found = true;
                }
            }

            if (!found)
            {
                pending.Add($"敌人位点 Enemies/{row.room_id}/{wanted}");
            }
        }

        // 2. 小地图的回程出口：Exit_<scene_id>
        foreach (Variant entry in db.rows("map_local"))
        {
            string sceneId = ((MapLocalRow)entry.AsGodotObject()).scene_id;
            if (sceneId == "scene_ferry_locked")
            {
                continue; // 本章不可进入
            }

            string exitName = $"Exit_{sceneId}";
            var markers = _SceneMarkers(sceneId, cache);
            if (!_HasMarker(markers, exitName))
            {
                pending.Add($"回程出口 {exitName}");
                continue;
            }

            string entrance = _EntranceRoom(db, sceneId);
            if (entrance.Length > 0 && !_HasInRoom(markers, exitName, entrance))
            {
                pending.Add($"回程出口 {exitName} 应放在入口房间 {entrance}");
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        if (PendingEnemyMarkers || PendingExitMarkers)
        {
            GD.Print($"  [地图自检·待补] {pending.Count} 项约定尚未交付：{Join(pending)}");
            return;
        }

        foreach (string item in pending)
        {
            Fail($"{item} 未交付（见 07_地图资源需求.md）");
        }
    }

    private Godot.Collections.Array _SceneMarkers(string sceneId, Godot.Collections.Dictionary cache)
    {
        if (!cache.ContainsKey(sceneId))
        {
            cache[sceneId] = _LoadMarkers($"res://scenes/maps/{sceneId}.tscn");
        }

        return cache[sceneId].AsGodotArray();
    }

    /// <summary>场景里的入口房间（room_type=entrance）；城镇没有房间行，返回空串。</summary>
    private string _EntranceRoom(TableDb db, string sceneId)
    {
        foreach (Variant entry in db.rows("dungeon_room"))
        {
            var row = (DungeonRoomRow)entry.AsGodotObject();
            if (row.scene_id == sceneId && row.room_type == "entrance")
            {
                return row.room_id;
            }
        }

        return "";
    }

    // ------------------------------------------------------------------ 大地图

    private void _CheckOverworld(TableDb db)
    {
        var markers = _LoadMarkers(Overworld);
        if (markers.Count == 0)
        {
            Fail("大地图读不出 Marker");
            return;
        }

        var nodes = _PrefixRows(db, "map_region", "node_id", "Node_", "parent_region", ParentRegion);
        var portals = _PrefixRows(db, "map_local", "scene_id", "Portal_", "", "");
        var spawns = _PrefixRows(db, "roaming_spawn", "spawn_id", "Spawn_", "", "");
        var patrols = new List<string>();
        foreach (Variant entry in db.rows("roaming_spawn"))
        {
            string pathId = ((RoamingSpawnRow)entry.AsGodotObject()).patrol_path_id;
            if (pathId.Length == 0)
            {
                continue;
            }

            string name = $"Patrol_{pathId}";
            if (!patrols.Contains(name))
            {
                patrols.Add(name);
            }
        }

        _Compare(NamesWith(markers, "Node_"), nodes, "大地图 Node_");
        _Compare(NamesWith(markers, "Portal_"), portals, "大地图 Portal_");
        _Compare(NamesWith(markers, "Spawn_"), spawns, "大地图 Spawn_");
        _Compare(NamesWith(markers, "Patrol_"), patrols, "大地图 Patrol_");
        // 判定位点只查「有没有落点」与「房间级是否落在正确房间」，
        // 因为「寨墙在野外、赌桌在客栈」这种归属表里没编码，不强求它必须在大地图上。

        // 0.28.0 的 A7 让石隙迷窟进大地图：`Node_n_shixi`（持藏宝图才出现）＋ `Portal_scene_shixi`
        CheckEq(NamesWith(markers, "Node_").Count, 8, "大地图地标 8 处（0.28.0 加了石隙迷窟）");
        CheckEq(NamesWith(markers, "Portal_").Count, 6, "大地图入口 6 处（0.28.0 加了石隙迷窟）");
        CheckEq(NamesWith(markers, "Spawn_").Count, 17, "大地图明雷 17 处");
        CheckEq(NamesWith(markers, "Patrol_").Count, 4, "巡逻线 4 条");

        // 地标坐标要跟表里一致
        var positions = _LoadMarkerPositions(Overworld);
        foreach (Variant entry in db.rows("map_region"))
        {
            var row = (MapRegionRow)entry.AsGodotObject();
            if (row.parent_region != ParentRegion)
            {
                continue;
            }

            string marker = $"Node_{row.node_id}";
            if (!positions.ContainsKey(marker))
            {
                continue;
            }

            var expected = new Vector2((float)row.pos_x, (float)row.pos_y);
            var actual = positions[marker].AsVector2();
            CheckLt((actual - expected).Length(), 1.0, $"{marker} 坐标与 map_region 一致");
        }
    }

    // ------------------------------------------------------------------ 小地图与房间

    private void _CheckLocalScenes(TableDb db)
    {
        foreach (Variant sceneEntry in db.rows("map_local"))
        {
            string sceneId = ((MapLocalRow)sceneEntry.AsGodotObject()).scene_id;
            string path = $"res://scenes/maps/{sceneId}.tscn";
            if (!FileAccess.FileExists(path))
            {
                Fail($"map_local.csv 里的 {sceneId} 没有对应场景：{path}");
                continue;
            }

            var markers = _LoadMarkers(path);
            var rooms = new List<string>();
            foreach (Variant roomEntry in db.rows("dungeon_room"))
            {
                var roomRow = (DungeonRoomRow)roomEntry.AsGodotObject();
                if (roomRow.scene_id != sceneId)
                {
                    continue;
                }

                rooms.Add($"Room_{roomRow.room_id}");
                _CheckRoomMarkers(db, sceneId, roomRow, markers);
            }

            _CheckTriggers(db, sceneId, markers);
            _Compare(NamesWith(markers, "Room_"), rooms, $"{sceneId} Room_");
            // 不该有不属于本场景的 Room_
            foreach (Variant markerVariant in markers)
            {
                string name = markerVariant.AsGodotDictionary()["name"].AsString();
                if (!name.StartsWith("Room_"))
                {
                    continue;
                }

                var row = db.get_row("dungeon_room", name.Substring("Room_".Length));
                if (row == null || ((DungeonRoomRow)row).scene_id != sceneId)
                {
                    Fail($"{sceneId} 里多出房间 {name}（表里不属于这个场景）");
                }
            }
        }
    }

    /// <summary>单个房间：宝箱、隐藏触发、房间级判定要挂在 `Markers/&lt;room_id&gt;/` 下面。</summary>
    private void _CheckRoomMarkers(
        TableDb db, string sceneId, DungeonRoomRow roomRow, Godot.Collections.Array markers)
    {
        string roomId = roomRow.room_id;
        var expected = new List<string>();
        if (roomRow.chest_id.Length > 0)
        {
            expected.Add($"Chest_{roomRow.chest_id}");
        }

        foreach (Variant entry in db.rows("event_check"))
        {
            var eventRow = (EventCheckRow)entry.AsGodotObject();
            if (eventRow.room_id == roomId)
            {
                expected.Add($"Event_{eventRow.check_id}");
            }
        }

        foreach (string name in expected)
        {
            if (!_HasInRoom(markers, name, roomId))
            {
                Fail($"{sceneId}/{roomId} 缺少 {name}");
            }
        }

        // 反向：这个房间下不该有多余的宝箱与触发
        foreach (Variant markerVariant in markers)
        {
            var marker = markerVariant.AsGodotDictionary();
            string name = marker["name"].AsString();
            if (marker["room"].AsString() != roomId || name.StartsWith("Room_"))
            {
                continue;
            }

            if (name.StartsWith("Chest_") && !expected.Contains(name))
            {
                Fail($"{sceneId}/{roomId} 多出 {name}（表里没有）");
            }
        }
    }

    /// <summary>隐藏触发：允许的落点是「hidden_trigger.room_id」加上「dungeon_room.hidden_trigger 指向它的那些房间」。
    /// 例：三火盆的交互点在火盆厅（hidden_trigger.room_id），而火盆密室只是「被它打开的房间」。</summary>
    private void _CheckTriggers(TableDb db, string sceneId, Godot.Collections.Array markers)
    {
        foreach (Variant entry in db.rows("hidden_trigger"))
        {
            var triggerRow = (HiddenTriggerRow)entry.AsGodotObject();
            if (triggerRow.scene_id != sceneId)
            {
                continue;
            }

            string name = $"Trigger_{triggerRow.trigger_id}";
            var allowed = new List<string>();
            if (triggerRow.room_id.Length > 0)
            {
                allowed.Add(triggerRow.room_id);
            }

            foreach (Variant roomEntry in db.rows("dungeon_room"))
            {
                var roomRow = (DungeonRoomRow)roomEntry.AsGodotObject();
                if (roomRow.scene_id == sceneId && roomRow.hidden_trigger == triggerRow.trigger_id
                    && !allowed.Contains(roomRow.room_id))
                {
                    allowed.Add(roomRow.room_id);
                }
            }

            bool found = false;
            foreach (Variant markerVariant in markers)
            {
                var marker = markerVariant.AsGodotDictionary();
                if (marker["name"].AsString() != name)
                {
                    continue;
                }

                if (allowed.Contains(marker["room"].AsString()))
                {
                    found = true;
                }
                else
                {
                    Fail(
                        $"{sceneId} 的 {name} 放在 {marker["room"]}，表里允许的位置是 "
                            + $"{string.Join(", ", allowed)}");
                }
            }

            if (!found)
            {
                Fail($"{sceneId} 缺少 {name}（允许位置：{string.Join(", ", allowed)}）");
            }
        }
    }

    /// <summary>观察点（设计 20 §3.2／0.29.1）也走同一条纪律：表里有这一行，地图上就得有那个位点。
    ///
    /// 判据只按名字（`Observe_&lt;point_id&gt;`）：小地图与大地图各自收自己那几条
    /// （表里 `scene_id`／`region_id` 二选一，构建期已经卡住），所以这里只要"在**某张图**里找得到"；
    /// 房间级的那几条还要落在写明的房间下（与宝箱／判定同一套）。</summary>
    private void _CheckFlavorPoints(TableDb db, Godot.Collections.Array markers)
    {
        int missing = 0;
        foreach (Variant entry in db.rows("flavor_point"))
        {
            var row = (FlavorPointRow)entry.AsGodotObject();
            string name = $"Observe_{row.point_id}";
            if (!_HasMarker(markers, name))
            {
                missing++;
                Fail($"flavor_point[{row.point_id}] 的位点 {name} 还没有地图交付（见 20 号 §十二）");
                continue;
            }

            if (row.room_id.Length > 0)
            {
                CheckTrue(_HasInRoom(markers, name, row.room_id), $"{name} 落在 {row.room_id}");
            }
        }

        CheckEq(missing, 0, $"观察点位点全部交付（缺 {missing} 个）");
    }

    /// <summary>每条事件判定都要在地图里找到落点；有 room_id 的必须落在那个房间。
    ///
    /// **例外：随机事件直接结算的那一条**（0.28.0 Q64）。`ev_patrol_check` 有两个入口，
    /// 其中一个（`world_event.we_patrol`）**不要求玩家走到关卡**——判定在服务层直接结算，
    /// 所以它没有 `Event_` 位点是设计原样，不是地编漏摆（07 只把官道那个固定关卡列成位点）。
    /// 判据不写死 id：**只要某条 `world_event` 的 `effect_kind=check` 指向它**，它就有入口。</summary>
    private void _CheckEventsExist(TableDb db, Godot.Collections.Array allMarkers)
    {
        var fromEvents = new List<string>();
        foreach (Variant entry in db.rows("world_event"))
        {
            var row = (WorldEventRow)entry.AsGodotObject();
            if (row.effect_kind == "check" && row.effect_id.Length > 0)
            {
                fromEvents.Add(row.effect_id);
            }
        }

        foreach (Variant entry in db.rows("event_check"))
        {
            var row = (EventCheckRow)entry.AsGodotObject();
            string name = $"Event_{row.check_id}";
            if (!_HasMarker(allMarkers, name))
            {
                if (!fromEvents.Contains(row.check_id))
                {
                    Fail($"event_check[{row.check_id}] 在所有地图里都没有落点");
                }

                continue;
            }

            if (row.room_id.Length > 0)
            {
                CheckTrue(_HasInRoom(allMarkers, name, row.room_id), $"{name} 落在 {row.room_id}");
            }
        }
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>收集场景里所有 Marker，返回 [{name, path, room}, ...]。</summary>
    private Godot.Collections.Array _LoadMarkers(string path)
    {
        var packed = GD.Load<PackedScene>(path);
        var markers = new Godot.Collections.Array();
        if (packed == null)
        {
            Fail($"场景加载失败：{path}");
            return markers;
        }

        var root = packed.Instantiate();
        _Collect(root, "", markers);
        root.Free();
        return markers;
    }

    private void _Collect(Node node, string parentPath, Godot.Collections.Array output)
    {
        foreach (Node child in node.GetChildren())
        {
            string childName = child.Name.ToString();
            string childPath = parentPath.Length == 0 ? childName : $"{parentPath}/{childName}";
            if (_IsMarker(childName))
            {
                output.Add(new Godot.Collections.Dictionary
                {
                    { "name", childName }, { "path", childPath }, { "room", _RoomOf(childPath) },
                });
            }

            _Collect(child, childPath, output);
        }
    }

    private Godot.Collections.Dictionary _LoadMarkerPositions(string path)
    {
        var output = new Godot.Collections.Dictionary();
        var packed = GD.Load<PackedScene>(path);
        if (packed == null)
        {
            return output;
        }

        var root = packed.Instantiate();
        _CollectPositions(root, output);
        root.Free();
        return output;
    }

    private void _CollectPositions(Node node, Godot.Collections.Dictionary output)
    {
        foreach (Node child in node.GetChildren())
        {
            string childName = child.Name.ToString();
            if (child is Marker2D marker && _IsMarker(childName))
            {
                output[childName] = marker.Position;
            }

            _CollectPositions(child, output);
        }
    }

    /// <summary>六张地图的 Marker 并集，用于「表里的东西到底有没有落点」。</summary>
    private Godot.Collections.Array _LoadAllMarkers()
    {
        var output = new Godot.Collections.Array();
        foreach (string path in AllScenes)
        {
            if (!FileAccess.FileExists(path))
            {
                continue;
            }

            output.AddRange(_LoadMarkers(path));
        }

        return output;
    }

    /// <summary>Marker 自身路径 Markers/hf1_shed/Chest_x → hf1_shed；大地图的 Markers/Node_x 返回空。</summary>
    private static string _RoomOf(string markerPath)
    {
        string[] parts = markerPath.Split('/');
        if (parts.Length >= 3 && parts[parts.Length - 3] == "Markers")
        {
            return parts[parts.Length - 2];
        }

        return "";
    }

    private static bool _IsMarker(string nodeName)
    {
        foreach (string prefix in MarkerPrefixes)
        {
            if (nodeName.StartsWith(prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> NamesWith(Godot.Collections.Array markers, string prefix)
    {
        var output = new List<string>();
        foreach (Variant markerVariant in markers)
        {
            string name = markerVariant.AsGodotDictionary()["name"].AsString();
            if (name.StartsWith(prefix))
            {
                output.Add(name);
            }
        }

        return output;
    }

    private static bool _HasMarker(Godot.Collections.Array markers, string name)
    {
        foreach (Variant markerVariant in markers)
        {
            if (markerVariant.AsGodotDictionary()["name"].AsString() == name)
            {
                return true;
            }
        }

        return false;
    }

    private static bool _HasInRoom(Godot.Collections.Array markers, string name, string roomId)
    {
        foreach (Variant markerVariant in markers)
        {
            var marker = markerVariant.AsGodotDictionary();
            if (marker["name"].AsString() == name && marker["room"].AsString() == roomId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>表里某列加上 Marker 前缀作为期望集合（可带一列过滤条件）。</summary>
    private static List<string> _PrefixRows(
        TableDb db, string tableName, string column, string prefix,
        string filterColumn, string filterValue)
    {
        var output = new List<string>();
        foreach (Variant entry in db.rows(tableName))
        {
            var row = entry.AsGodotObject();
            if (filterColumn.Length > 0 && row.Get(filterColumn).AsString() != filterValue)
            {
                continue;
            }

            output.Add($"{prefix}{row.Get(column).AsString()}");
        }

        return output;
    }

    private void _Compare(List<string> actual, List<string> expected, string label)
    {
        foreach (string name in expected)
        {
            if (!actual.Contains(name))
            {
                Fail($"{label} 缺少 {name}");
            }
        }

        foreach (string name in actual)
        {
            if (!expected.Contains(name))
            {
                Fail($"{label} 多出 {name}（表里没有）");
            }
        }
    }

    private static string StripPng(string fileName) =>
        fileName.Substring(0, fileName.Length - ".png".Length);

    private static string Join(List<string> items) => string.Join("、", items);
}
