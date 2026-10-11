using System.Collections.Generic;
using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 四张副本小地图（塌陷山洞／荒村／废弃渡口／石隙迷窟）的铺装（`BuildLocalMapsCs` 的另一半）。
///
/// 拆出来的理由只有一条：单文件 800 行硬上限（`tests/test_handshake_repo.gd::_check_file_size`）。
/// 入口／清风驿／副本通用骨架留在 `BuildLocalMapsCs.cs`。
/// </summary>
public partial class BuildLocalMapsCs
{
    // ---------------------------------------------------------------- 塌陷山洞

    private const string CaveScene = "scene_cave";
    private const int CaveW = 32;
    private const int CaveH = 24;

    private static readonly Godot.Collections.Dictionary[] CaveRooms =
    {
        Room("cave_01", new Rect2I(3, 8, 10, 8)),
        Room("cave_02", new Rect2I(18, 7, 12, 10)),
    };

    private void BuildCave()
    {
        var shell = DungeonShell(
            CaveScene, "cave", CaveW, CaveH, CaveRooms,
            new Godot.Collections.Array { Link("cave_01", "cave_02") });
        if (shell.Count == 0)
        {
            return;
        }

        // 洞口通大地图；挖通后另一边直通黑风寨后山地牢。
        RoomMarker(shell, "cave_01", ExitPrefix + CaveScene, new Vector2I(5, 15));
        MapKit.add_marker(shell["markers"].As<Node2D>(), "Portal_scene_heifengzhai", new Vector2I(28, 12));
        RoomMarker(shell, "cave_02", "Trigger_trig_dig", new Vector2I(24, 12));
        RoomMarker(shell, "cave_02", "Event_ev_grave_epitaph", new Vector2I(21, 9));
        MapKit.add_marker(shell["characters"].As<Node2D>(), "player_spawn", new Vector2I(5, 12))
            .SetMeta("role", "player");
        Fit(shell, CaveW, CaveH);
        Finish(shell["root"].As<Node2D>(), CaveScene, CaveW, CaveH);
        GD.Print("[mapgen] 塌陷山洞：房间 2（cave_01/cave_02）");
    }

    // ---------------------------------------------------------------- 荒村

    private const string VillageScene = "scene_huangcun";
    private const int VillageW = 40;
    private const int VillageH = 30;

    private static readonly Godot.Collections.Dictionary[] VillageRooms =
    {
        Room("hc_01", new Rect2I(3, 12, 12, 8)),
        Room("hc_02", new Rect2I(22, 10, 14, 11)),
    };

    /// <summary>废屋南侧的院子：白清和蹲在院外给死者盖白布（0.29.0 的 20_第一章剧情），
    /// 位置要「看得见但不挡路」，所以给他一片接在 hc_02 南边的小院。</summary>
    private static readonly Rect2I[] VillageYard = { new(24, 21, 8, 4) };

    private static readonly Vector2I[] VillageNpcSlots =
    {
        new(27, 22),   // 白清和：院外
    };

    /// <summary>观察点（0.29.1）：门槛的靴印／烧断的梁／井沿。
    /// 「看得见」是硬要求，所以这两处的实物也由这里补：村口一口枯井、废屋里一根烧断的梁。</summary>
    private static readonly Godot.Collections.Dictionary VillageObserve = new()
    {
        {
            "hc_02", new Godot.Collections.Dictionary
            {
                { "Observe_ob_huangcun_door_01", new Vector2I(23, 16) },   // 门槛上的靴印
                { "Observe_ob_huangcun_beam_01", new Vector2I(29, 12) },   // 烧断的梁
            }
        },
        {
            "hc_01", new Godot.Collections.Dictionary
            {
                { "Observe_ob_huangcun_well_01", new Vector2I(8, 16) },    // 井沿
            }
        },
    };

    private void BuildHuangcun()
    {
        var shell = DungeonShell(
            VillageScene, "village", VillageW, VillageH, VillageRooms,
            new Godot.Collections.Array { Link("hc_01", "hc_02") }, VillageYard);
        if (shell.Count == 0)
        {
            return;
        }

        RoomMarker(shell, "hc_01", ExitPrefix + VillageScene, new Vector2I(4, 19));
        RoomMarker(shell, "hc_02", "Chest_drop_chest_silver", new Vector2I(33, 17));
        TeamMarker(shell, "hc_02", "team_butcher");
        // 观察点的实物：枯井与烧断的梁（都用既有瓦片，不加新素材）。
        shell["decor"].As<TileMapLayer>().SetCell(
            new Vector2I(8, 15), MapKit.source_id(), MapKit.t_well());
        shell["decor"].As<TileMapLayer>().SetCell(
            new Vector2I(30, 12), MapKit.source_id(), MapKit.t_fence_v());
        foreach (Variant roomIdVariant in VillageObserve.Keys)
        {
            string roomId = roomIdVariant.AsString();
            var points = VillageObserve[roomId].AsGodotDictionary();
            foreach (Variant pointIdVariant in points.Keys)
            {
                string pointId = pointIdVariant.AsString();
                RoomMarker(shell, roomId, pointId, points[pointId].AsVector2I());
            }
        }

        for (int index = 0; index < VillageNpcSlots.Length; index++)
        {
            AddNpcSlot(shell["characters"].As<Node2D>(), index + 1, VillageNpcSlots[index]);
        }

        MapKit.add_marker(shell["characters"].As<Node2D>(), "player_spawn", new Vector2I(4, 16))
            .SetMeta("role", "player");
        Fit(shell, VillageW, VillageH);
        Finish(shell["root"].As<Node2D>(), VillageScene, VillageW, VillageH);
        GD.Print("[mapgen] 荒村：房间 2（hc_01/hc_02）");
    }

    // ---------------------------------------------------------------- 废弃渡口

    private const string FerryScene = "scene_ferry_locked";
    private const int FerryW = 32;
    private const int FerryH = 24;

    /// <summary>渡口那条河（16 §3.5：废弃渡口＝**水岸**——水面 ＋ 栈桥残骸 ＋ 芦苇）。
    /// 封渡的牌坊与墙在河南岸，玩家从南边的街走过来，到墙为止。</summary>
    private static readonly Rect2I FerryRiver = new(1, 1, 26, 8);

    private void BuildFerry()
    {
        var tileSet = MapKit.save_theme_tile_set("village");
        if (tileSet == null)
        {
            _failures.Add("村落主题 TileSet 生成失败");
            return;
        }

        var shell = MapKit.new_shell(FerryScene, tileSet, false, false);
        var root = shell["root"].As<Node2D>();
        var ground = shell["ground"].As<TileMapLayer>();
        var decor = shell["decor"].As<TileMapLayer>();
        var streets = new Godot.Collections.Dictionary();
        MapKit.mark_rect(streets, new Rect2I(4, 12, 20, 6));
        MapKit.paint_grass(ground, FerryW, FerryH);
        MapKit.paint_dirt(ground, streets, FerryW, FerryH);
        var river = PaintFerryRiver(ground, decor);
        // 本章不开放：牌坊 + 破船 + 断桥封锁，一眼看出过不去。
        for (int offset = -2; offset <= 2; offset++)
        {
            decor.SetCell(
                new Vector2I(12 + offset, 10), MapKit.source_id(),
                System.Math.Abs(offset) == 2 ? MapKit.t_wall_top_corner() : MapKit.t_battlement());
        }

        decor.SetCell(new Vector2I(12, 11), MapKit.source_id(), MapKit.t_arch());
        decor.SetCell(new Vector2I(20, 13), MapKit.source_id(), MapKit.t_barrel());
        decor.SetCell(new Vector2I(21, 14), MapKit.source_id(), MapKit.t_crate());
        for (int x = 4; x < 12; x++)
        {
            decor.SetCell(new Vector2I(x, 15), MapKit.source_id(), MapKit.t_fence_h());
        }

        // 栈桥残骸：断掉的三根桥桩伸进浅水，中间那根没了——「一眼看出过不去」（07 §4.4）。
        decor.SetCell(new Vector2I(11, 7), MapKit.source_id(), MapKit.t_fence_post());
        decor.SetCell(new Vector2I(15, 7), MapKit.source_id(), MapKit.t_fence_post());
        decor.SetCell(new Vector2I(17, 6), MapKit.source_id(), MapKit.t_fence_post());
        // 封渡木桩（0.32.0）：美术交付的 **prop**（`prop_ferry_pile.png`，32×32，木桩 ＋ 钉在桩上的
        // 朱印木牌一起出图），挂在观察点位点上——「醉刀客线索改走主线条」的第三条就刻在这根桩上。
        // 摆在封渡牌坊东侧两格的街上（玩家出镇往渡口走，正好经过它）。
        // 桩**不挡路**：验收要求观察点本身可走、且从出生点走得到；以前那两个占位桩 ＋ 通用木牌
        // （`T_FENCE_POST`／`T_SIGN`）由这张图取代。
        // 图的路径**只在 `MapKit.PROP_FERRY_PILE` 写一次**，这里只引用常量。
        var pile = MapKit.add_marker(
            shell["markers"].As<Node2D>(), "Observe_ob_dukou_pile_04", new Vector2I(15, 12));
        MapKit.add_sprite(pile, "pile", MapKit.prop_ferry_pile(), Vector2.Zero);
        // 密林封边不能长到河里（河道自己就是天然边界）。
        var keepOpen = streets.Duplicate();
        foreach (Variant cell in river.Keys)
        {
            keepOpen[cell] = true;
        }

        MapKit.forest_border(decor, keepOpen, FerryW, FerryH, 2);
        // 本章不可进入，按 07 文档 4.0 不做回程出口。
        MapKit.add_marker(shell["characters"].As<Node2D>(), "player_spawn", new Vector2I(5, 14))
            .SetMeta("role", "player");
        Fit(shell, FerryW, FerryH);
        Finish(root, FerryScene, FerryW, FerryH);
        GD.Print("[mapgen] 废弃渡口：1 区域（河道 ＋ 栈桥残骸 ＋ 封锁表现）／封渡木桩 prop ＋ 观察点 1 处");
    }

    /// <summary>渡口的水。**深水画在 `Decor`**——它挡路，而 `Ground` 按约定只能放「走得上去的地面」
    /// （`verify_maps._check_only_decor_blocks` 逐层盯着这条，`Ground`／`Overlay`／`Conditional` 都在内）；
    /// 浅水／水岸／芦苇不挡路，留在 `Ground`。
    /// 返回整片水的格子（含岸边芦苇），好让密林封边绕开河道。</summary>
    private static Godot.Collections.Dictionary PaintFerryRiver(
        TileMapLayer ground, TileMapLayer decor)
    {
        var cells = new Godot.Collections.Dictionary();
        int lastY = FerryRiver.Position.Y + FerryRiver.Size.Y - 1;
        for (int y = FerryRiver.Position.Y; y <= lastY; y++)
        {
            for (int x = FerryRiver.Position.X; x < FerryRiver.Position.X + FerryRiver.Size.X; x++)
            {
                var cell = new Vector2I(x, y);
                cells[cell] = true;
                if (y <= lastY - 2)
                {
                    decor.SetCell(cell, MapKit.source_id(), MapKit.t_water_deep());
                }
                else if (y == lastY - 1)
                {
                    ground.SetCell(cell, MapKit.source_id(), MapKit.t_water_shallow());
                }
                else
                {
                    ground.SetCell(cell, MapKit.source_id(), MapKit.t_water_shore());
                }
            }
        }

        // 岸边芦苇荡（在 `Ground` 上，能走进去——07 §4.4 的侦察靠它）
        foreach (Vector2I at in new[]
        {
            new Vector2I(4, 8), new Vector2I(6, 9), new Vector2I(9, 9), new Vector2I(19, 9),
            new Vector2I(22, 9), new Vector2I(25, 8),
        })
        {
            ground.SetCell(at, MapKit.source_id(), MapKit.t_reeds());
            cells[at] = true;
        }

        return cells;
    }

    // ---------------------------------------------------------------- 石隙迷窟

    private const string ShixiScene = "scene_shixi";
    private const int ShixiW = 36;
    private const int ShixiH = 26;

    /// <summary>六间房：一条主缝（入口→石廊→岔口）分出两条岔缝（空室／遗篇石台），
    /// 入口另接一条往塌方的岔缝。房间矩形是地编排的，出口关系来自 dungeon_room.csv。</summary>
    private static readonly Godot.Collections.Dictionary[] ShixiRooms =
    {
        Room("mz_01", new Rect2I(2, 10, 6, 5)),    // 石隙入口：极窄的入口缝
        Room("mz_02", new Rect2I(11, 9, 12, 6)),   // 石廊：一线天光主要落在这间
        Room("mz_03", new Rect2I(3, 19, 8, 6)),    // 塌方：死路，但有银箱
        Room("mz_04", new Rect2I(26, 9, 8, 7)),    // 岔口：两条岔路一眼看出是两条
        Room("mz_05", new Rect2I(24, 2, 7, 5)),    // 空室：死路（往东留出收窄的余地）
        Room("mz_06", new Rect2I(26, 19, 8, 6)),   // 遗篇石台：隐藏终点
    };

    /// <summary>两处死路都按美术的示意画（`docs/dev/images/石隙死路画法_示意.png`）：
    /// 先接一段 2 格高的缝，**尽头再收成 1 格宽的窄缝**——看着像还能挤过去，
    /// 「死」要等玩家走到最里面才发现（07 §4.6／16 §4.8）。</summary>
    private static readonly Rect2I[] ShixiStubs =
    {
        new(11, 21, 5, 2), new(16, 21, 3, 1),   // mz_03：往东 5 格，再收成 3 格单格缝
        new(31, 3, 3, 2), new(34, 3, 2, 1),     // mz_05：往东 3 格，再收成 2 格单格缝
    };

    /// <summary>入口缝给 2 格宽（极窄），其余主缝 3 格。</summary>
    private static readonly Godot.Collections.Array ShixiLinks = new()
    {
        Link("mz_01", "mz_02", 2),
        Link("mz_02", "mz_04"),
        Link("mz_04", "mz_05"),
        Link("mz_04", "mz_06"),
        Link("mz_01", "mz_03"),
    };

    private void BuildShixi()
    {
        // 美术已交付 shixi 主题（assets/tilesets/ink_jianghu/shixi），不再借用 cave。
        var shell = DungeonShell(
            ShixiScene, "shixi", ShixiW, ShixiH, ShixiRooms, ShixiLinks, ShixiStubs);
        if (shell.Count == 0)
        {
            return;
        }

        PaintShixiDetails(shell);
        // 出生点在入口缝里；回程出口压在入口房间（出生点与出口贴在一起是合法摆法）。
        MapKit.add_marker(shell["characters"].As<Node2D>(), "player_spawn", new Vector2I(5, 12))
            .SetMeta("role", "player");
        RoomMarker(shell, "mz_01", ExitPrefix + ShixiScene, new Vector2I(3, 12));
        TeamMarker(shell, "mz_02", "team_shixi_hound");
        TeamMarker(shell, "mz_04", "team_shixi_hound");
        RoomMarker(shell, "mz_03", "Chest_drop_chest_silver", new Vector2I(5, 21));
        RoomMarker(shell, "mz_06", "Trigger_trig_shixi_reward", new Vector2I(30, 22));
        Fit(shell, ShixiW, ShixiH);
        Finish(shell["root"].As<Node2D>(), ShixiScene, ShixiW, ShixiH);
        GD.Print("[mapgen] 石隙迷窟：房间 6（mz_01~mz_06）／两处死路带窄缝／天光在 mz_02 与 mz_06");
    }

    /// <summary>石隙迷窟的美术要点（07 §4.6、16 §4.8）：一线天光与岩檐走 Overlay（不挡路），
    /// 前朝石台与碎石堆走 Decor；塌方尽头用碎石堆收口，空室尽头什么都不放（只是没路了）。</summary>
    private static void PaintShixiDetails(Godot.Collections.Dictionary shell)
    {
        var decor = shell["decor"].As<TileMapLayer>();
        var overlay = shell["overlay"].As<TileMapLayer>();
        // 主缝的一线天：石廊整整一条
        for (int x = 12; x < 22; x++)
        {
            overlay.SetCell(new Vector2I(x, 10), MapKit.source_id(), MapKit.t_sky_slit());
        }

        // 终点：天光落在前朝石台上（石台不挡路，玩家能站上去）
        for (int x = 28; x < 32; x++)
        {
            overlay.SetCell(new Vector2I(x, 20), MapKit.source_id(), MapKit.t_sky_slit());
        }

        for (int x = 29; x < 32; x++)
        {
            decor.SetCell(new Vector2I(x, 22), MapKit.source_id(), MapKit.t_shrine_platform());
        }

        // 塌方：银箱上方那束光比别处亮（诱饵），碎石堆在窄缝最末端（不是门口）
        overlay.SetCell(new Vector2I(5, 20), MapKit.source_id(), MapKit.t_sky_slit());
        decor.SetCell(new Vector2I(18, 21), MapKit.source_id(), MapKit.t_rubble());
        decor.SetCell(new Vector2I(17, 21), MapKit.source_id(), MapKit.t_rubble());
        // 两条死路的**内段**都压岩檐（A9 拍板选方案 ①，0.32.0）：玩家在岔口只看得到黑，
        // 走进去（程序把 Overlay 半透明化）才看见尽头。**岔口那一格留亮**——要让人看得见
        // 「这里有条路可走」；盖住整条就变成一堵墙，迷宫反而露馅（16 §4.8）。
        var floorCells = shell["floor"].AsGodotDictionary();
        foreach (Vector2I cell in ShixiEaves())
        {
            if (floorCells.ContainsKey(cell))
            {
                overlay.SetCell(cell, MapKit.source_id(), MapKit.t_rock_eave());
            }
        }

        // 墙角碎石当纹理（避开位点与房间开口）
        foreach (Vector2I cell in new[] { new Vector2I(27, 10), new Vector2I(21, 13), new Vector2I(32, 23) })
        {
            decor.SetCell(cell, MapKit.source_id(), MapKit.t_rubble());
        }
    }

    /// <summary>两处死路要压岩檐的格子：**从岔口往里第二格起**，一直盖到尽头。
    /// `mz_03`（塌方）的岔口在 x=11，`mz_05`（空室）的窄缝从 x=31 起——两处都按同一口径：
    /// 露一格进深、盖住后段，所以「站在岔口看不见尽头」，而缝宽与真岔路完全一致。</summary>
    private static List<Vector2I> ShixiEaves()
    {
        var cells = new List<Vector2I>();
        for (int x = 13; x < 19; x++)
        {
            cells.Add(new Vector2I(x, 21));
            cells.Add(new Vector2I(x, 22));
        }

        for (int x = 29; x < 36; x++)
        {
            cells.Add(new Vector2I(x, 3));
            cells.Add(new Vector2I(x, 4));
        }

        return cells;
    }

}
