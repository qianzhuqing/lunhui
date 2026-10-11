using System.Collections.Generic;
using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 生成黑风寨副本 `scenes/maps/scene_heifengzhai.tscn`（对位旧链 `build_heifengzhai.gd`，该 `.gd` 已退场）。
///
/// 按 07_地图资源需求.md 4.2 与第五节：三层 19 个房间放**一张场景图**，纵向堆叠，每层约 40×30 格。
/// 房间矩形是本脚本手排的（策划只给了房间表，没给坐标）；走廊按 `dungeon_room.exit_rooms` 连，
/// 连通关系反过来由 verify_maps 校验。
///
/// 已知空档（脚本里也标了）：
///   * `dungeon_room.exit_rooms` 没有声明**层与层**的连通，所以一层→二层的石阶是补的，
///     Marker 名 `stairs_to_floor2`（Portal_ 前缀绑的是 scene_id，装不了层间楼梯）。
///
/// 用法（`--script` 只认 GDScript，C# 侧只能挂场景）：
///   godot --headless --path . --log-file .logs\mapgen_heifengzhai_cs.log ^
///         res://tools/mapgen/build_heifengzhai_cs.tscn
/// </summary>
public partial class BuildHeifengzhaiCs : Node
{
    private const string ScenePath = "res://scenes/maps/scene_heifengzhai.tscn";
    private const string DumpPath = "res://.logs/scene_heifengzhai_preview.json";
    private const int MapW = 40;
    private const int MapH = 84;

    /// <summary>19 个房间的矩形（列 x, 行 y, 宽, 高）。三层纵向堆叠：一层 y2–28、二层 y31–52、三层 y58–81。</summary>
    private static readonly Godot.Collections.Dictionary Rooms = new()
    {
        // 一层 前寨
        { "hf1_gate", new Rect2I(3, 2, 10, 6) },
        { "hf1_yard", new Rect2I(16, 2, 12, 8) },
        { "hf1_path", new Rect2I(30, 2, 8, 6) },
        { "hf1_shed", new Rect2I(30, 11, 8, 6) },
        { "hf1_brazier", new Rect2I(16, 13, 11, 8) },
        { "hf1_vault", new Rect2I(3, 13, 9, 8) },
        { "hf1_secret", new Rect2I(3, 23, 10, 6) },
        { "hf1_cell", new Rect2I(16, 23, 10, 6) },
        { "hf1_deep", new Rect2I(30, 22, 8, 7) },
        // 二层 聚义厅
        { "hf2_stairs", new Rect2I(30, 31, 8, 6) },
        { "hf2_yard", new Rect2I(16, 31, 12, 8) },
        { "hf2_poison", new Rect2I(3, 31, 10, 8) },
        { "hf2_hall", new Rect2I(14, 44, 13, 9) },
        { "hf2_vault", new Rect2I(3, 44, 8, 7) },
        { "hf2_tunnel", new Rect2I(30, 44, 8, 7) },
        // 三层 后寨
        { "hf3_gate", new Rect2I(26, 58, 12, 6) },
        { "hf3_patrol", new Rect2I(14, 62, 12, 8) },
        { "hf3_boss", new Rect2I(2, 62, 11, 9) },
        { "hf3_dungeon", new Rect2I(14, 74, 12, 8) },
    };

    /// <summary>走廊连接：`dungeon_room.exit_rooms` 的原样（双向由脚本去重），外加一条层间石阶。</summary>
    private static readonly string[][] Links =
    {
        new[] { "hf1_gate", "hf1_yard" }, new[] { "hf1_gate", "hf1_path" },
        new[] { "hf1_yard", "hf1_shed" }, new[] { "hf1_yard", "hf1_path" },
        new[] { "hf1_shed", "hf1_yard" },
        new[] { "hf1_path", "hf1_cell" }, new[] { "hf1_path", "hf1_brazier" },
        new[] { "hf1_brazier", "hf1_vault" },
        new[] { "hf1_vault", "hf1_brazier" }, new[] { "hf1_vault", "hf1_secret" },
        new[] { "hf1_secret", "hf1_vault" },
        new[] { "hf1_cell", "hf1_deep" }, new[] { "hf1_deep", "hf1_cell" },
        new[] { "hf2_stairs", "hf2_yard" }, new[] { "hf2_stairs", "hf2_poison" },
        new[] { "hf2_yard", "hf2_hall" },
        new[] { "hf2_poison", "hf2_hall" }, new[] { "hf2_poison", "hf2_tunnel" },
        new[] { "hf2_hall", "hf2_vault" }, new[] { "hf2_hall", "hf3_gate" },
        new[] { "hf2_vault", "hf2_hall" },
        new[] { "hf2_tunnel", "hf3_gate" },
        new[] { "hf3_gate", "hf3_patrol" }, new[] { "hf3_gate", "hf3_dungeon" },
        new[] { "hf3_patrol", "hf3_boss" },
        // 表里没声明层间连通，这条是补的（一层最深处下到二层石阶）。
        new[] { "hf1_deep", "hf2_stairs" },
    };

    /// <summary>宝箱：Chest_&lt;chest_id&gt;。两个银箱分别在 hf1_vault / hf1_secret，靠房间文件夹分开。</summary>
    private static readonly Godot.Collections.Dictionary Chests = new()
    {
        { "hf1_shed", Names("Chest_drop_chest_copper") },
        { "hf1_vault", Names("Chest_drop_chest_silver") },
        { "hf1_secret", Names("Chest_drop_chest_silver") },
        { "hf2_vault", Names("Chest_drop_chest_gold") },
    };

    /// <summary>隐藏触发（hidden_trigger.csv）：trig_wine 占两处（hf1_deep 与 hf3_dungeon）。</summary>
    private static readonly Godot.Collections.Dictionary Triggers = new()
    {
        { "hf1_deep", Names("Trigger_trig_wine") },
        { "hf1_brazier", Names("Trigger_trig_brazier") },
        { "hf1_vault", Names("Trigger_trig_chest_all") },
        { "hf2_hall", Names("Trigger_trig_rusty_sword") },
        { "hf2_poison", Names("Trigger_trig_poison_kill") },
        { "hf3_gate", Names("Trigger_trig_stealth_clear") },
        { "hf3_dungeon", Names("Trigger_trig_wine") },
    };

    /// <summary>事件判定（event_check.csv 里 scene_id = scene_heifengzhai 的 4 条）。</summary>
    private static readonly Godot.Collections.Dictionary Events = new()
    {
        { "hf1_shed", Names("Event_ev_shed_trap") },
        // hf1_cell 里是**两个人两处**：救治被囚村民（ev_cell_heal）与被掳的沈雁回（ev_shen_rescue，0.29.0）。
        { "hf1_cell", Names("Event_ev_cell_heal", "Event_ev_shen_rescue") },
        { "hf2_poison", Names("Event_ev_poison_identify") },
        { "hf2_hall", Names("Event_ev_hall_inscription") },
    };

    /// <summary>房间内挂钩点的落位（相对房间左上角的偏移，格）。</summary>
    private static readonly Godot.Collections.Dictionary Spots = new()
    {
        { "Chest_drop_chest_copper", new Vector2I(3, 3) },
        { "Chest_drop_chest_silver", new Vector2I(3, 3) },
        { "Chest_drop_chest_gold", new Vector2I(3, 3) },
        { "Trigger_trig_wine", new Vector2I(2, 2) },
        { "Trigger_trig_brazier", new Vector2I(4, 3) },
        { "Trigger_trig_chest_all", new Vector2I(6, 2) },
        { "Trigger_trig_rusty_sword", new Vector2I(6, 2) },
        { "Trigger_trig_poison_kill", new Vector2I(3, 3) },
        { "Trigger_trig_stealth_clear", new Vector2I(5, 2) },
        { "Event_ev_shed_trap", new Vector2I(2, 4) },
        { "Event_ev_cell_heal", new Vector2I(3, 3) },
        { "Event_ev_shen_rescue", new Vector2I(6, 3) },
        { "Event_ev_poison_identify", new Vector2I(6, 3) },
        { "Event_ev_hall_inscription", new Vector2I(9, 3) },
    };

    /// <summary>剧情 NPC 站位（0.29.0 的 20_第一章剧情）：现在是 `npc_slot_0N` 占位，
    /// 等 NPC／对话表落地再按 id 绑（07 §十一 待补第 4 条）。</summary>
    private static readonly Vector2I[] NpcSlots =
    {
        new(5, 36),   // 苏九娘：毒堂里侧，与守着她的毒手（Team 位点）分开
    };

    /// <summary>观察点（0.29.1）：地牢三处、毒堂三处。
    /// 地牢那三处与 `Event_ev_cell_heal`／`Event_ev_shen_rescue` 是**同一房间里的另外三处**，不合并。</summary>
    private static readonly Godot.Collections.Dictionary ObservePoints = new()
    {
        {
            "hf1_cell", new Godot.Collections.Dictionary
            {
                { "Observe_ob_heifengzhai_cell_01", new Vector2I(18, 24) },    // 墙上的刻痕
                { "Observe_ob_heifengzhai_cell_02", new Vector2I(21, 24) },    // 铁栏
                { "Observe_ob_heifengzhai_cell_03", new Vector2I(23, 26) },    // 她的手
            }
        },
        {
            "hf2_poison", new Godot.Collections.Dictionary
            {
                { "Observe_ob_heifengzhai_poison_01", new Vector2I(6, 33) },   // 药罐
                { "Observe_ob_heifengzhai_poison_02", new Vector2I(9, 33) },   // 见光就变的毒
                { "Observe_ob_heifengzhai_poison_03", new Vector2I(10, 36) },  // 毒手脚上的靴
            }
        },
    };

    /// <summary>房间内的固定敌人：`Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt;`（07 文档第二、五节，共 7 处，本场景 6 处）。
    /// 落点由 `MapKit.room_entrance_inside` 算，站在门口内侧但不堵门。</summary>
    private static readonly Godot.Collections.Dictionary Teams = new()
    {
        { "hf1_yard", "team_gate_sentry" },
        { "hf2_yard", "team_elite_blade" },
        { "hf2_poison", "team_poison_hand" },
        { "hf2_hall", "team_boss_guards" },
        { "hf3_patrol", "team_heifeng_elite" },
        { "hf3_boss", "team_boss" },
    };

    /// <summary>入口房间的回程出口（小地图 → 大地图），07 文档 4.0。</summary>
    private const string ExitRoom = "hf1_gate";

    private readonly List<string> _problems = new();

    public override void _Ready()
    {
        // 与 `RunTestsCs` 同一条理由（见 `docs/dev/CSharp迁移计划.md` 第三节第 1 条）。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private int Run()
    {
        var tileSet = MapKit.save_theme_tile_set("heifengzhai");
        if (tileSet == null)
        {
            GD.PrintErr("[mapgen] 山寨主题 TileSet 生成失败");
            return 1;
        }

        var shell = MapKit.new_shell("scene_heifengzhai", tileSet, false, false);
        var root = shell["root"].As<Node2D>();
        var ground = shell["ground"].As<TileMapLayer>();
        var decor = shell["decor"].As<TileMapLayer>();

        var floorCells = new Godot.Collections.Dictionary();
        foreach (Variant roomIdVariant in Rooms.Keys)
        {
            MapKit.mark_rect(floorCells, Rooms[roomIdVariant].AsRect2I());
        }

        foreach (string[] link in Links)
        {
            MapKit.carve_corridor(
                floorCells, Rooms[link[0]].AsRect2I(), Rooms[link[1]].AsRect2I(), 3);
        }

        MapKit.paint_cells(ground, floorCells, MapKit.t_floor());
        var walls = MapKit.wall_shell(floorCells);
        MapKit.paint_cells(decor, walls, MapKit.t_battlement());
        PaintRoomDetails(decor, walls);

        foreach (Variant roomIdVariant in Rooms.Keys)
        {
            string roomId = roomIdVariant.AsString();
            MapKit.add_room(shell["rooms"].As<Node2D>(), roomId, Rooms[roomId].AsRect2I());
        }

        PlaceMarkers(shell);
        PlaceTeamMarkers(shell, floorCells);
        PlaceNpcSlots(shell);
        PlaceObservePoints(shell);
        HiddenLayerTone(shell);
        MapKit.add_marker(shell["characters"].As<Node2D>(), "player_spawn", new Vector2I(6, 5))
            .SetMeta("role", "player");
        MapKit.fit_camera(shell["camera"].As<Camera2D>(), MapW, MapH);

        int groundCount = ground.GetUsedCells().Count;
        int decorCount = decor.GetUsedCells().Count;
        var saveError = MapKit.save_scene(root, ScenePath);
        if (saveError != (int)Error.Ok)
        {
            GD.PrintErr($"[mapgen] 保存场景失败：{saveError}");
            return 1;
        }

        root.Free();
        MapKit.dump_preview(ScenePath, DumpPath, MapW, MapH);
        GD.Print($"[mapgen] 黑风寨：房间 {Rooms.Count} / 宝箱 {Count(Chests)} / 触发 {Count(Triggers)}"
            + $" / 判定 {Count(Events)} / 敌人位点 {Teams.Count} / 回程出口 1");
        GD.Print($"[mapgen] 瓦片用量 ground={groundCount} decor={decorCount}（上限 5000）");
        foreach (string problem in _problems)
        {
            GD.PrintErr("  - " + problem);
        }

        if (_problems.Count > 0)
        {
            return 1;
        }

        GD.Print($"[mapgen] MAPGEN: OK -> {ScenePath}");
        return 0;
    }

    private static int Count(Godot.Collections.Dictionary table)
    {
        int total = 0;
        foreach (Variant key in table.Keys)
        {
            total += table[key].AsGodotArray().Count;
        }

        return total;
    }

    /// <summary>房间里的招牌物件：门口留出侧向通路，进门的房间都能从侧面绕（07 文档验收 4）。</summary>
    private static void PaintRoomDetails(
        TileMapLayer decor, Godot.Collections.Dictionary walls)
    {
        foreach (Variant roomIdVariant in Rooms.Keys)
        {
            var rect = Rooms[roomIdVariant].AsRect2I();
            var corner = new Vector2I(rect.Position.X + 1, rect.Position.Y + 1);
            decor.SetCell(corner, MapKit.source_id(), MapKit.t_crate());
            decor.SetCell(
                new Vector2I(rect.Position.X + rect.Size.X - 2, rect.Position.Y + 1),
                MapKit.source_id(), MapKit.t_barrel());
        }

        // 火盆厅的三个火盆：顺序藏在 hf2_hall 的题字里。
        var brazier = Rooms["hf1_brazier"].AsRect2I();
        for (int index = 0; index < 3; index++)
        {
            var cell = new Vector2I(
                brazier.Position.X + 2 + index * 3, brazier.Position.Y + 5);
            decor.SetCell(cell, MapKit.source_id(), MapKit.t_sign());
        }

        // 门口留一格空，别把入口堵死。
        foreach (Variant roomIdVariant in Rooms.Keys)
        {
            var rect = Rooms[roomIdVariant].AsRect2I();
            walls.Remove(new Vector2I(
                rect.Position.X + rect.Size.X / 2, rect.Position.Y - 1));
        }
    }

    /// <summary>后山地牢（隐藏层）压暗，让「隐藏层」一眼能看出来。</summary>
    private static void HiddenLayerTone(Godot.Collections.Dictionary shell)
    {
        var tone = new Color(0.62f, 0.62f, 0.72f, 1.0f);
        shell["decor"].As<TileMapLayer>().Modulate = tone;
        shell["ground"].As<TileMapLayer>().Modulate = new Color(0.86f, 0.86f, 0.94f, 1.0f);
    }

    private static void PlaceMarkers(Godot.Collections.Dictionary shell)
    {
        var markers = shell["markers"].As<Node2D>();
        // 回程出口放在入口房间里，名字按 map_local.scene_id。
        RoomItem(markers, ExitRoom, "Exit_scene_heifengzhai");
        MapKit.add_marker(markers, "stairs_to_floor2", new Vector2I(33, 28));
        foreach (Variant roomIdVariant in Chests.Keys)
        {
            string roomId = roomIdVariant.AsString();
            foreach (Variant markerName in Chests[roomId].AsGodotArray())
            {
                RoomItem(markers, roomId, markerName.AsString());
            }
        }

        foreach (Variant roomIdVariant in Triggers.Keys)
        {
            string roomId = roomIdVariant.AsString();
            foreach (Variant markerName in Triggers[roomId].AsGodotArray())
            {
                RoomItem(markers, roomId, markerName.AsString());
            }
        }

        foreach (Variant roomIdVariant in Events.Keys)
        {
            string roomId = roomIdVariant.AsString();
            foreach (Variant markerName in Events[roomId].AsGodotArray())
            {
                RoomItem(markers, roomId, markerName.AsString());
            }
        }
    }

    /// <summary>固定敌人挂在 Enemies/&lt;room_id&gt;/ 下（唯一带父目录的约定）。</summary>
    private static void PlaceTeamMarkers(
        Godot.Collections.Dictionary shell, Godot.Collections.Dictionary floorCells)
    {
        var enemies = shell["enemies"].As<Node2D>();
        var decor = shell["decor"].As<TileMapLayer>();
        foreach (Variant roomIdVariant in Teams.Keys)
        {
            string roomId = roomIdVariant.AsString();
            var holder = MapKit.folder(enemies, roomId);
            var cell = MapKit.room_entrance_inside(decor, floorCells, Rooms[roomId].AsRect2I());
            MapKit.add_marker(holder, "Team_" + Teams[roomId].AsString(), cell);
        }
    }

    /// <summary>NPC 站位：Marker2D + 占位贴图（与城镇同一套 `npc_slot_0N` 约定）。</summary>
    private static void PlaceNpcSlots(Godot.Collections.Dictionary shell)
    {
        var characters = shell["characters"].As<Node2D>();
        for (int index = 0; index < NpcSlots.Length; index++)
        {
            var marker = MapKit.add_marker(characters, $"npc_slot_{index + 1:00}", NpcSlots[index]);
            MapKit.add_sprite(marker, "placeholder", MapKit.npc_texture(), Vector2.Zero);
        }
    }

    /// <summary>观察点：Marker2D 挂在 `Markers/&lt;room_id&gt;/` 下（与宝箱／触发同一套）。
    /// 毒堂里补一个木桶当「药罐」的实物，让位点看得见。</summary>
    private static void PlaceObservePoints(Godot.Collections.Dictionary shell)
    {
        shell["decor"].As<TileMapLayer>().SetCell(
            new Vector2I(6, 32), MapKit.source_id(), MapKit.t_barrel());
        var markers = shell["markers"].As<Node2D>();
        foreach (Variant roomIdVariant in ObservePoints.Keys)
        {
            string roomId = roomIdVariant.AsString();
            var points = ObservePoints[roomId].AsGodotDictionary();
            foreach (Variant pointIdVariant in points.Keys)
            {
                string pointId = pointIdVariant.AsString();
                // 观察点的格子是逐个挑的绝对坐标，不走 `SPOTS` 那套「相对房间左上角」的偏移。
                RoomItemAt(markers, roomId, pointId, points[pointId].AsVector2I());
            }
        }
    }

    private static void RoomItemAt(
        Node2D markers, string roomId, string markerName, Vector2I cell)
    {
        var holder = markers.GetNodeOrNull<Node2D>(roomId);
        if (holder == null)
        {
            holder = MapKit.folder(markers, roomId);
        }

        MapKit.add_marker(holder, markerName, cell);
    }

    private static void RoomItem(Node2D markers, string roomId, string markerName)
    {
        var holder = markers.GetNodeOrNull<Node2D>(roomId);
        if (holder == null)
        {
            holder = MapKit.folder(markers, roomId);
        }

        var rect = Rooms[roomId].AsRect2I();
        // 出口没有「房间里的小偏移」语义，固定在入口房间的左下角（进出都走这里）。
        var offset = markerName.StartsWith("Exit_", System.StringComparison.Ordinal)
            ? new Vector2I(2, 3)
            : (Spots.ContainsKey(markerName) ? Spots[markerName].AsVector2I() : new Vector2I(2, 2));
        MapKit.add_marker(
            holder, markerName, new Vector2I(rect.Position.X + offset.X, rect.Position.Y + offset.Y));
    }

    private static Godot.Collections.Array Names(params string[] markers)
    {
        var array = new Godot.Collections.Array();
        foreach (string marker in markers)
        {
            array.Add(marker);
        }

        return array;
    }
}
