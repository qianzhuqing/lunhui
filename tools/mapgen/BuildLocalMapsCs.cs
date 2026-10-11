using System.Collections.Generic;
using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 生成五张小地图（对位旧链 `build_local_maps.gd`，该 `.gd` 已退场）：
/// 清风驿（城镇）、塌陷山洞、荒村、废弃渡口、石隙迷窟。
///
/// 按 07_地图资源需求.md 第四节；房间／宝箱／触发／判定位点全部来自配置表，
/// Marker 名字就是表里的 id（Room_/Chest_/Trigger_/Event_/Portal_）。
///
/// 用法（`--script` 只认 GDScript，C# 侧只能挂场景）：
///   godot --headless --path . --log-file .logs\mapgen_local_cs.log ^
///         res://tools/mapgen/build_local_maps_cs.tscn
///
/// **常量表一律用 `Godot.Collections.Dictionary`／`Array`**（保插入序）：地图是逐格铺出来的，
/// 换掉遍历次序就可能改出另一个 `.tscn`，所以不换成 C# 的 `Dictionary`（不保序）。
/// </summary>
public partial class BuildLocalMapsCs : Node
{
    private const string LocalCsv = "res://data/tables/map_local.csv";
    private const string RoomCsv = "res://data/tables/dungeon_room.csv";
    private const string TriggerCsv = "res://data/tables/hidden_trigger.csv";
    private const string EventCsv = "res://data/tables/event_check.csv";
    private const string BuildingCsv = "res://data/tables/building_def.csv";

    /// <summary>小地图回大地图的出口。07 文档把 Portal_ 绑到 map_local.scene_id（5 个，都是大地图上的入口），
    /// 回程出口按 07 文档 4.0 用 `Exit_&lt;scene_id&gt;`；废弃渡口本章不可进入，不做出口。</summary>
    private const string ExitPrefix = "Exit_";

    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        // 与 `RunTestsCs` 同一条理由（见 `docs/dev/CSharp迁移计划.md` 第三节第 1 条）：
        // `_Ready` 里直接跑会在「根节点正在装配子节点」的窗口里挂节点。延到本帧空闲。
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();
    }

    private int Run()
    {
        BuildTown();
        BuildCave();
        BuildHuangcun();
        BuildFerry();
        BuildShixi();
        if (_failures.Count > 0)
        {
            foreach (string message in _failures)
            {
                GD.PrintErr("  - " + message);
            }

            return 1;
        }

        GD.Print("[mapgen] MAPGEN: OK（清风驿 / 塌陷山洞 / 荒村 / 废弃渡口 / 石隙迷窟）");
        return 0;
    }

    // ---------------------------------------------------------------- 清风驿

    private const string TownScene = "scene_qingfengyi";
    private const int TownW = 40;
    private const int TownH = 30;
    private const int TownMainY = 15;
    private const int TownCrossX = 19;
    private const int TownNorthRow = 14;
    private const int TownSouthRow = 17;

    /// <summary>门面槽位：先北后南、先西后东；表里几间店就占几个槽，其余用街景民居补满。</summary>
    private static readonly Godot.Collections.Array TownSlots = new()
    {
        Slot(4, "south", "HOUSE_RED_GRAY"),
        Slot(10, "south", "HOUSE_SLATE_WOOD"),
        Slot(25, "south", "HOUSE_RED_GRAY"),
        Slot(31, "south", "HOUSE_SLATE_GRAY"),
        Slot(4, "north", "HOUSE_SLATE_WOOD"),
        Slot(10, "north", "HOUSE_SLATE_GRAY"),
        Slot(25, "north", "HOUSE_RED_GRAY"),
        Slot(31, "north", "HOUSE_SLATE_WOOD"),
    };

    /// <summary>07 文档 4.1 要求的地面设施：表里没有 id 的（当铺／悬赏板／客栈）也要在图上。</summary>
    private static readonly Godot.Collections.Array TownFacilities = new()
    {
        Facility("facility_pawnshop", new Vector2I(16, 20)),
        Facility("facility_inn", new Vector2I(24, 20)),
        Facility("facility_bounty_board", new Vector2I(19, 22)),
        // 0.31.0 新增：书铺。陆文昭（书生）的「本命机遇·书箱底」落在这一处（21 号 §九），
        // 与当铺／悬赏板一样是**无表信息点**——只有地上的可交互物，不进 building_def。
        Facility("facility_bookshop", new Vector2I(14, 20), bookshop: true),
    };

    private static readonly Vector2I[] TownNpcSlots =
    {
        new(14, 17), new(22, 17), new(15, 14),
        new(26, 16), new(21, 24),
    };

    /// <summary>观察点（0.29.1）：`Observe_&lt;point_id&gt;`，名字是临时名，等 `flavor_point` 表落地再按真 id 改。
    /// 悬赏板（19,22 的牌子＋20,22 的木箱）与客栈（24,20 的招牌＋25,20 的木箱）旁边各摆几个。</summary>
    private static readonly Godot.Collections.Dictionary TownObserve = new()
    {
        { "Observe_ob_qingfengyi_board_01", new Vector2I(18, 22) },   // 告示的纸边
        { "Observe_ob_qingfengyi_board_02", new Vector2I(20, 23) },   // 墙上的旧浆糊
        { "Observe_ob_qingfengyi_inn_01", new Vector2I(23, 21) },     // 门框上的刀痕
        { "Observe_ob_qingfengyi_inn_02", new Vector2I(25, 21) },     // 燕小七那把无鞘的刀
        { "Observe_ob_qingfengyi_inn_03", new Vector2I(24, 22) },     // 门槛上的骰子
    };

    private void BuildTown()
    {
        var tileSet = MapKit.save_theme_tile_set("town");
        if (tileSet == null)
        {
            _failures.Add("城镇主题 TileSet 生成失败");
            return;
        }

        var shell = MapKit.new_shell(TownScene, tileSet, false, false);
        var root = shell["root"].As<Node2D>();
        var ground = shell["ground"].As<TileMapLayer>();
        var decor = shell["decor"].As<TileMapLayer>();
        var markers = shell["markers"].As<Node2D>();
        var characters = shell["characters"].As<Node2D>();

        // 地面：草地打底，十字街 + 集市石板广场。
        var streets = new Godot.Collections.Dictionary();
        MapKit.mark_rect(streets, new Rect2I(2, TownMainY, TownW - 4, 2));
        MapKit.mark_rect(streets, new Rect2I(TownCrossX, 1, 2, TownH - 2));
        var plaza = new Godot.Collections.Dictionary();
        MapKit.mark_rect(plaza, new Rect2I(16, 12, 8, 7));
        MapKit.paint_grass(ground, TownW, TownH);
        MapKit.paint_dirt(ground, streets, TownW, TownH);
        MapKit.paint_cells(ground, plaza, MapKit.t_floor());

        // 建筑：表里有几行摆几间。
        var plan = TownPlan();
        foreach (Variant specVariant in plan)
        {
            PlaceHouse(decor, specVariant.AsGodotDictionary());
        }

        foreach (Variant facilityVariant in TownFacilities)
        {
            var facility = facilityVariant.AsGodotDictionary();
            var cell = facility["cell"].AsVector2I();
            if (BoolField(facility, "bookshop", false))
            {
                // 书铺要显眼：书架＋账桌＋摊前的招牌／纸笔（都用既有瓦片，暖色提亮归美术）。
                decor.SetCell(cell, MapKit.source_id(), MapKit.t_crate());                 // 书架
                decor.SetCell(cell + new Vector2I(1, 0), MapKit.source_id(), MapKit.t_bench()); // 账桌
                decor.SetCell(cell + new Vector2I(1, 1), MapKit.source_id(), MapKit.t_sign());  // 纸笔摊
                continue;
            }

            decor.SetCell(cell, MapKit.source_id(), MapKit.t_sign());
            decor.SetCell(cell + new Vector2I(1, 0), MapKit.source_id(), MapKit.t_crate());
        }

        // 栅栏围镇，南北各留一个口。
        MapKit.mark_rect(streets, new Rect2I(TownCrossX, TownH - 2, 2, 2));
        FenceBorder(decor, streets);
        decor.SetCell(new Vector2I(22, 12), MapKit.source_id(), MapKit.t_well());
        decor.SetCell(new Vector2I(18, 19), MapKit.source_id(), MapKit.t_bench());
        decor.SetCell(new Vector2I(27, 18), MapKit.source_id(), MapKit.t_barrel());

        var reserved = new Godot.Collections.Dictionary();
        foreach (Variant specVariant in plan)
        {
            reserved[FrontCell(specVariant.AsGodotDictionary())] = true;
        }

        foreach (Variant facilityVariant in TownFacilities)
        {
            var cell = facilityVariant.AsGodotDictionary()["cell"].AsVector2I();
            reserved[cell] = true;
            reserved[cell + new Vector2I(0, 1)] = true;
        }

        foreach (Vector2I cell in TownNpcSlots)
        {
            reserved[cell] = true;
        }

        foreach (Variant pointIdVariant in TownObserve.Keys)
        {
            reserved[TownObserve[pointIdVariant.AsString()].AsVector2I()] = true;
        }

        reserved[new Vector2I(TownCrossX, TownH - 3)] = true;
        MapKit.scatter_trees(decor, streets, reserved, TownW, TownH, 0.04);

        // 店招牌挂在门前街上；表里没有 id 的设施用 facility_ 前缀，不装成表主键。
        var buildings = MapKit.folder(markers, "Buildings");
        foreach (Variant specVariant in plan)
        {
            var spec = specVariant.AsGodotDictionary();
            string buildingId = spec["id"].AsString();
            if (buildingId.Length == 0)
            {
                continue;
            }

            MapKit.add_marker(buildings, buildingId, FrontCell(spec));
        }

        // 07 文档 4.1 要求的三处无表设施：节点名挂 facility_ 前缀，别装成表主键。
        var facilities = MapKit.folder(markers, "Facilities");
        foreach (Variant facilityVariant in TownFacilities)
        {
            var facility = facilityVariant.AsGodotDictionary();
            MapKit.add_marker(
                facilities, facility["name"].AsString(),
                facility["cell"].AsVector2I() + new Vector2I(0, 1));
        }

        // 赌局位点（07 文档 §6：清风驿·客栈）。
        MapKit.add_marker(markers, "Event_ev_gamble", new Vector2I(24, 19));
        foreach (Variant pointIdVariant in TownObserve.Keys)
        {
            string pointId = pointIdVariant.AsString();
            MapKit.add_marker(markers, pointId, TownObserve[pointId].AsVector2I());
        }

        // 城镇没有 Room_（dungeon_room 里没有城镇行），出口直接挂在 Markers 下。
        MapKit.add_marker(markers, ExitPrefix + TownScene, new Vector2I(TownCrossX, TownH - 3));
        PlaceNpcs(characters);
        MapKit.fit_camera(shell["camera"].As<Camera2D>(), TownW, TownH);
        // 城镇的「1 个房间」在 dungeon_room.csv 里没有 id，所以没有 Room_ 标记（已回报）。
        GD.Print($"[mapgen] 清风驿：店铺 {plan.Count - SceneryCount(plan)} / 街景 "
            + $"{SceneryCount(plan)} / 设施 {TownFacilities.Count} / NPC {TownNpcSlots.Length}");
        Finish(root, TownScene, TownW, TownH);
    }

    private Godot.Collections.Array TownPlan()
    {
        var rows = MapKit.read_csv(BuildingCsv);
        // 只有 `building_type=shop` 的行占门面槽位。**服务类不占**——`bld_dummy`（木桩）要由地编
        // 摆到校场（A6），把它当第 5 间店塞进北排民居会挂着一块「木桩」招牌，
        // 也会和 `test_local_map` 的临时位点用例撞成重名（2026-10-04，见 `框架说明.md` 决策 269）。
        var shops = new Godot.Collections.Array();
        foreach (Variant rowVariant in rows)
        {
            var row = rowVariant.AsGodotDictionary();
            if (CellStr(row, "building_type") == "shop")
            {
                shops.Add(row);
            }
        }

        if (shops.Count > TownSlots.Count)
        {
            _failures.Add(
                $"building_def.csv 有 {shops.Count} 间店，门面槽位只有 {TownSlots.Count} 个");
            return new Godot.Collections.Array();
        }

        var plan = new Godot.Collections.Array();
        for (int index = 0; index < TownSlots.Count; index++)
        {
            var slot = TownSlots[index].AsGodotDictionary();
            plan.Add(new Godot.Collections.Dictionary
            {
                { "id", index < shops.Count ? shops[index].AsGodotDictionary()["building_id"].AsString() : "" },
                { "x", slot["x"] },
                { "facing", slot["facing"] },
                { "style", slot["style"] },
            });
        }

        return plan;
    }

    private static int SceneryCount(Godot.Collections.Array plan)
    {
        int count = 0;
        foreach (Variant specVariant in plan)
        {
            if (specVariant.AsGodotDictionary()["id"].AsString().Length == 0)
            {
                count++;
            }
        }

        return count;
    }

    private static void PlaceHouse(TileMapLayer decor, Godot.Collections.Dictionary spec)
    {
        var style = Style(spec["style"].AsString());
        if (spec["facing"].AsString() == "south")
        {
            MapKit.house_down(decor, spec["x"].AsInt32(), TownNorthRow, style);
        }
        else
        {
            MapKit.house_up(decor, spec["x"].AsInt32(), TownSouthRow, style);
        }
    }

    private static Godot.Collections.Dictionary Style(string styleName) => styleName switch
    {
        "HOUSE_RED_GRAY" => MapKit.house_red_gray(),
        "HOUSE_SLATE_GRAY" => MapKit.house_slate_gray(),
        _ => MapKit.house_slate_wood(),
    };

    /// <summary>标记落在门前可交互的那一格（街上），不压在门瓦片上。</summary>
    private static Vector2I FrontCell(Godot.Collections.Dictionary spec)
    {
        int x = spec["x"].AsInt32() + 3;
        return spec["facing"].AsString() == "south"
            ? new Vector2I(x, TownNorthRow + 1)
            : new Vector2I(x, TownSouthRow - 1);
    }

    private static void FenceBorder(TileMapLayer decor, Godot.Collections.Dictionary skip)
    {
        for (int x = 0; x < TownW; x++)
        {
            foreach (var cell in new[] { new Vector2I(x, 1), new Vector2I(x, TownH - 2) })
            {
                if (!skip.ContainsKey(cell))
                {
                    decor.SetCell(cell, MapKit.source_id(), MapKit.t_fence_h());
                }
            }
        }

        for (int y = 0; y < TownH; y++)
        {
            foreach (var cell in new[] { new Vector2I(1, y), new Vector2I(TownW - 2, y) })
            {
                if (!skip.ContainsKey(cell))
                {
                    decor.SetCell(cell, MapKit.source_id(), MapKit.t_fence_v());
                }
            }
        }
    }

    private static void PlaceNpcs(Node2D characters)
    {
        for (int index = 0; index < TownNpcSlots.Length; index++)
        {
            AddNpcSlot(characters, index + 1, TownNpcSlots[index]);
        }

        var spawn = MapKit.add_marker(characters, "player_spawn", new Vector2I(TownCrossX, TownH - 3));
        spawn.SetMeta("role", "player");
    }

    /// <summary>NPC 站位：Marker2D + 占位贴图。现在只摆位置，等对话表落地再按 id 绑身份（07 §十一 待补第 4 条）。</summary>
    private static void AddNpcSlot(Node2D characters, int index, Vector2I cell)
    {
        var marker = MapKit.add_marker(characters, $"npc_slot_{index:00}", cell);
        MapKit.add_sprite(marker, "placeholder", MapKit.npc_texture(), Vector2.Zero);
    }

    // ---------------------------------------------------------------- 副本通用

    private Godot.Collections.Dictionary DungeonShell(
        string sceneId, string theme, int mapW, int mapH,
        Godot.Collections.Dictionary[] roomSpecs, Godot.Collections.Array links,
        Rect2I[] extraFloor = null)
    {
        var tileSet = MapKit.save_theme_tile_set(theme);
        if (tileSet == null)
        {
            _failures.Add($"{theme} 主题 TileSet 生成失败");
            return new Godot.Collections.Dictionary();
        }

        var shell = MapKit.new_shell(sceneId, tileSet, false, false);
        var floorCells = new Godot.Collections.Dictionary();
        var rects = new Godot.Collections.Dictionary();
        foreach (var spec in roomSpecs)
        {
            MapKit.mark_rect(floorCells, spec["rect"].AsRect2I());
            rects[spec["id"].AsString()] = spec["rect"];
        }

        foreach (Variant linkVariant in links)
        {
            var link = linkVariant.AsGodotArray();
            // 第三段可选＝走廊宽度：入口缝要更窄，主缝给 3 格（背袭要留 2 格可走）。
            int width = link.Count > 2 ? link[2].AsInt32() : 3;
            MapKit.carve_corridor(
                floorCells, rects[link[0].AsString()].AsRect2I(), rects[link[1].AsString()].AsRect2I(), width);
        }

        // 额外的地板块（石隙迷窟用它做「看着还能往前走」的死路窄缝）。
        if (extraFloor != null)
        {
            foreach (Rect2I rect in extraFloor)
            {
                MapKit.mark_rect(floorCells, rect);
            }
        }

        // 地板与墙一起算，墙贴着地板外圈自动生成，不会漏角。
        MapKit.paint_cells(shell["ground"].As<TileMapLayer>(), floorCells, MapKit.t_floor());
        MapKit.paint_cells(
            shell["decor"].As<TileMapLayer>(), MapKit.wall_shell(floorCells), MapKit.t_battlement());
        foreach (var spec in roomSpecs)
        {
            MapKit.add_room(
                shell["rooms"].As<Node2D>(), spec["id"].AsString(), spec["rect"].AsRect2I());
        }

        shell["floor"] = floorCells;
        return shell;
    }

    /// <summary>副本房间内的挂钩点：在 Markers 下按 room_id 分文件夹，避免同名 id（两个银箱）撞车。</summary>
    private static void RoomMarker(
        Godot.Collections.Dictionary shell, string roomId, string markerName, Vector2I cell)
    {
        var markers = shell["markers"].As<Node2D>();
        var holder = markers.GetNodeOrNull<Node2D>(roomId);
        if (holder == null)
        {
            holder = MapKit.folder(markers, roomId);
        }

        MapKit.add_marker(holder, markerName, cell);
    }

    /// <summary>固定敌人：`Enemies/&lt;room_id&gt;/Team_&lt;team_id&gt;`（07 文档第二节，唯一带父目录的约定）。
    /// 落点放在房间入口内侧，看得见但不堵门。</summary>
    private static void TeamMarker(Godot.Collections.Dictionary shell, string roomId, string teamId)
    {
        var enemies = shell["enemies"].As<Node2D>();
        var holder = enemies.GetNodeOrNull<Node2D>(roomId);
        if (holder == null)
        {
            holder = MapKit.folder(enemies, roomId);
        }

        var cell = MapKit.room_entrance_inside(
            shell["decor"].As<TileMapLayer>(), shell["floor"].AsGodotDictionary(), RoomRect(roomId));
        MapKit.add_marker(holder, "Team_" + teamId, cell);
    }

    private static Rect2I RoomRect(string roomId)
    {
        foreach (var spec in CaveRooms)
        {
            if (spec["id"].AsString() == roomId)
            {
                return spec["rect"].AsRect2I();
            }
        }

        foreach (var spec in VillageRooms)
        {
            if (spec["id"].AsString() == roomId)
            {
                return spec["rect"].AsRect2I();
            }
        }

        foreach (var spec in ShixiRooms)
        {
            if (spec["id"].AsString() == roomId)
            {
                return spec["rect"].AsRect2I();
            }
        }

        return new Rect2I();
    }

    private static void Fit(Godot.Collections.Dictionary shell, int mapW, int mapH) =>
        MapKit.fit_camera(shell["camera"].As<Camera2D>(), mapW, mapH);

    /// <summary>存档 + 出预览数据（GDScript 版尾部那几个"只在需要统计时给"的参数在这里没用到，
    /// 直接不列——它们在原版函数体里是 `_` 前缀的弃用形参）。</summary>
    private void Finish(Node2D root, string sceneId, int mapW, int mapH)
    {
        string path = $"res://scenes/maps/{sceneId}.tscn";
        var saveError = MapKit.save_scene(root, path);
        if (saveError != (int)Error.Ok)
        {
            _failures.Add($"保存 {path} 失败：{saveError}");
            return;
        }

        root.Free();
        MapKit.dump_preview(path, $"res://.logs/{sceneId}_preview.json", mapW, mapH);
    }

    // ---------------------------------------------------------------- 常量小工具

    private static Godot.Collections.Dictionary Slot(int x, string facing, string style) => new()
    {
        { "x", x },
        { "facing", facing },
        { "style", style },
    };

    private static Godot.Collections.Dictionary Facility(string name, Vector2I cell, bool bookshop = false)
    {
        var entry = new Godot.Collections.Dictionary { { "name", name }, { "cell", cell } };
        if (bookshop)
        {
            entry["bookshop"] = true;
        }

        return entry;
    }

    private static Godot.Collections.Dictionary Room(string id, Rect2I rect) => new()
    {
        { "id", id },
        { "rect", rect },
    };

    private static Godot.Collections.Array Link(string a, string b, int width = 0)
    {
        var link = new Godot.Collections.Array { a, b };
        if (width > 0)
        {
            link.Add(width);
        }

        return link;
    }

    private static string CellStr(
        Godot.Collections.Dictionary row, string column, string fallback = "") =>
        row.ContainsKey(column) ? row[column].AsString() : fallback;

    private static bool BoolField(
        Godot.Collections.Dictionary row, string column, bool fallback) =>
        row.ContainsKey(column) ? row[column].AsBool() : fallback;
}
