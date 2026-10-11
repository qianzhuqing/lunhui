using System.Collections.Generic;
using Godot;

namespace Lunhui.Tools.Mapgen;

/// <summary>
/// 地图搭建工具箱（C# 版，对位原 `tools/mapgen/map_kit.gd`）：32px 瓦片、六套主题 TileSet、
/// 场景骨架、房间／走廊、Marker 绑定。
///
/// 约定全部来自 docs/design/07_地图资源需求.md：
///   瓦片 32×32、物理层 1-6、节点结构（Ground/Decor/Overlay/Conditional/Fog/Rooms/Markers/
///   Characters/Enemies/YSort/Camera）、Marker 名字前缀（Node_/Portal_/Room_/Chest_/Spawn_/
///   Event_/Trigger_/Patrol_）。
///
/// **为什么是 `RefCounted` ＋ 全静态**：四个生成器 2026-10-11 搬成 C#（施工单「三点零一」），
/// 最后一个 GDScript 调用方（`verify_maps.gd`）也在第四十六批搬完——地图工具链现在是纯 C#。
/// 下面那些 `t_*()`／`tile_px()` 取值口是**历史遗留**：GDScript 侧 `preload` 之后读不到 C# 的
/// `const`／静态字段（`Invalid access to property or key … on 'CSharpScript'`，2026-10-11 用探针实测：
/// `const int`／`const string`／`static readonly` 一律读不到），当年与 `src/data/TableRegistry.cs` 的
/// `tables()`／`tables_dir()` 是同一套办法。**现在没有 GDScript 调用方了，可以随时收回只留字段**
/// （留着无害，先不动——收回是 P5 收尾的事）。
/// </summary>
public partial class MapKit : RefCounted
{
    // ---------------------------------------------------------------- 常量

    /// <summary>图集按**主题**分开（15_美术风格需求 §六：`tilesets/ink_jianghu/&lt;主题&gt;/tilemap_packed.png`）。
    /// 六套主题的图集坐标完全一致，差别在画风与主色——16_区域美术设定要求「相邻区域不许撞主色」，
    /// 共用一张图集就做不到这件事。</summary>
    public const string ATLAS_DIR = "res://assets/tilesets/ink_jianghu";
    public const string FOG_ATLAS = "res://assets/tilesets/ink_jianghu/fog_tile.png";
    public const string PLACEHOLDER_TEXTURE = "res://assets/sprites/props/prop_placeholder.png";
    public const string NPC_TEXTURE = "res://assets/sprites/characters/npc_placeholder.png";

    /// <summary>场景里的 prop（0.32.0 第一条线索链要用）：废弃渡口的**封渡木桩**——木桩 ＋ 钉在桩上的
    /// 朱印木牌（不写字，32×32 塞不下），美术 2026-10-04 交付。
    /// **路径只写在这一处**，由生成器摆（`build_local_maps._build_ferry()`）：手往 `.tscn` 里贴一张
    /// 贴图的话，下次重跑生成器就把它冲掉了——而且「同一个路径抄两份」正是「同一事实只许有一处定义」要防的漂移。</summary>
    public const string PROP_FERRY_PILE = "res://assets/sprites/props/prop_ferry_pile.png";

    /// <summary>旧镖车的 prop（0.32.0 交付，小策划 Q83 ①）：落雁坡西边官道旁那辆车——
    /// 「翻倒的车厢 ＋ 散落的镖货 ＋ 断掉的车辕 ＋ 只剩一个字的镖旗」，**64×64 ＝ 2×2 格**。
    /// 尺寸是硬约束：差一格就和旁边三个 `Observe_ob_luoyanpo_cart_0N` 与林铁山的 `npc_slot_01` 错位。
    /// 与上面那条同一条规矩：**路径只写在这一处**，场景由 `build_overworld._paint_old_cart()` 摆。</summary>
    public const string PROP_LUOYANPO_CART = "res://assets/sprites/props/prop_luoyanpo_jiuche.png";

    public const int TILE_PX = 32;
    public const int SOURCE_ID = 0;
    /// <summary>迷雾走第二个图集源，免得混进 Kenney 那套索引。</summary>
    public const int PLACEHOLDER_SOURCE_ID = 1;
    public static readonly Vector2I T_FOG = new(0, 0);

    // --- 物理层（07 文档「碰撞与物理层约定」，值是 Godot 的 layer 位）---
    public const int LAYER_BLOCK = 2;
    public const int LAYER_INTERACT = 4;
    public const int LAYER_ALERT = 8;
    public const int LAYER_ROOM = 16;
    public const int LAYER_EVENT = 32;

    // --- 地面 ---
    public static readonly Vector2I T_GRASS = new(0, 0);
    public static readonly Vector2I T_GRASS_TUFT = new(1, 0);
    public static readonly Vector2I T_GRASS_FLOWER = new(2, 0);
    public static readonly Vector2I T_GRASS_PEBBLE = new(7, 3);
    public static readonly Vector2I T_DIRT = new(1, 2);
    public static readonly Vector2I T_DIRT_T = new(1, 1);
    public static readonly Vector2I T_DIRT_B = new(1, 3);
    public static readonly Vector2I T_DIRT_L = new(0, 2);
    public static readonly Vector2I T_DIRT_R = new(2, 2);
    public static readonly Vector2I T_DIRT_TL = new(0, 1);
    public static readonly Vector2I T_DIRT_TR = new(2, 1);
    public static readonly Vector2I T_DIRT_BL = new(0, 3);
    public static readonly Vector2I T_DIRT_BR = new(2, 3);
    public static readonly Vector2I T_FLOOR = new(1, 9);
    public static readonly Vector2I T_FLOOR_TOP = new(1, 8);

    // --- 植被 ---
    public static readonly Vector2I T_TREE_PINE = new(4, 0);
    public static readonly Vector2I T_TREE_ROUND = new(4, 1);
    public static readonly Vector2I T_TREE_AUTUMN = new(3, 1);
    public static readonly Vector2I T_TREE_SMALL = new(7, 2);
    public static readonly Vector2I T_BUSH = new(5, 0);
    public static readonly Vector2I T_FOREST = new(7, 0);
    public static readonly Vector2I T_FOREST_AUTUMN = new(10, 1);
    public static readonly Vector2I T_MUSHROOM = new(5, 2);

    // --- 建筑与石作 ---
    public static readonly Vector2I T_ROOF_RED_TOP = new(5, 4);
    public static readonly Vector2I T_ROOF_RED = new(5, 5);
    public static readonly Vector2I T_ROOF_SLATE_TOP = new(1, 4);
    public static readonly Vector2I T_ROOF_SLATE = new(1, 5);
    public static readonly Vector2I T_GABLE = new(7, 5);
    public static readonly Vector2I T_WALL_GRAY = new(5, 6);
    public static readonly Vector2I T_WINDOW_GRAY = new(4, 7);
    public static readonly Vector2I T_DOOR_GRAY = new(7, 7);
    public static readonly Vector2I T_WALL_WOOD = new(1, 6);
    public static readonly Vector2I T_WINDOW_WOOD = new(0, 7);
    public static readonly Vector2I T_DOOR_WOOD = new(1, 7);
    public static readonly Vector2I T_BATTLEMENT = new(4, 8);
    public static readonly Vector2I T_WALL_TOP_CORNER = new(3, 8);
    public static readonly Vector2I T_ARCH = new(4, 9);

    // --- 物件 ---
    public static readonly Vector2I T_FENCE_H = new(9, 3);
    public static readonly Vector2I T_FENCE_POST = new(10, 3);
    public static readonly Vector2I T_FENCE_V = new(11, 3);
    public static readonly Vector2I T_WELL = new(10, 7);
    public static readonly Vector2I T_SIGN = new(10, 6);
    public static readonly Vector2I T_BENCH = new(9, 6);
    public static readonly Vector2I T_CRATE = new(11, 6);
    public static readonly Vector2I T_CHEST = new(10, 8);
    public static readonly Vector2I T_BARREL = new(11, 8);

    // --- 特殊（15 §4.1：雾层、水面、血迹、脚印）---
    // 图集里一直留着这几格，但主题的收图清单没收它们；渡口那张图因此只能拿栅栏＋木箱顶替
    // （见 docs/dev/地图搭建说明.md「还缺的地图资产」第 10 条）。
    public static readonly Vector2I T_WATER_DEEP = new(6, 0);
    public static readonly Vector2I T_WATER_SHALLOW = new(6, 1);
    public static readonly Vector2I T_WATER_SHORE = new(6, 2);
    public static readonly Vector2I T_REEDS = new(6, 3);
    public static readonly Vector2I T_FOOTPRINT = new(6, 4);
    public static readonly Vector2I T_BLOOD = new(6, 5);

    // --- 石隙迷窟专用（16 §4.8：窄岩缝里的一线天光）---
    // 这三格是「死路看起来能走」那条要求的落点：岩檐压在岔缝内段上，玩家在缝口读不出深浅；
    // 天光是诱饵；石台是终点。
    public static readonly Vector2I T_SKY_SLIT = new(6, 6);
    public static readonly Vector2I T_SHRINE_PLATFORM = new(6, 7);
    public static readonly Vector2I T_RUBBLE = new(6, 8);
    public static readonly Vector2I T_ROCK_EAVE = new(6, 9);

    // --- 大地图的山（16 §3.5：山脉成片、有厚度；洞口是"山体上的开口"）---
    // 山体走**九宫格**（与土路那套同一种用法）：mask 说「这一格哪几条边还是山」。
    public static readonly Vector2I T_MTN_TL = new(2, 4);
    public static readonly Vector2I T_MTN_T = new(3, 4);
    public static readonly Vector2I T_MTN_TR = new(4, 4);
    public static readonly Vector2I T_MTN_L = new(2, 5);
    public static readonly Vector2I T_MTN_C = new(3, 5);
    public static readonly Vector2I T_MTN_C2 = new(8, 4);
    public static readonly Vector2I T_MTN_R = new(4, 5);
    public static readonly Vector2I T_MTN_BL = new(2, 6);
    public static readonly Vector2I T_MTN_B = new(3, 6);
    public static readonly Vector2I T_MTN_BR = new(4, 6);
    public static readonly Vector2I T_MTN_PEAK_LOW = new(0, 4);
    public static readonly Vector2I T_MTN_PEAK_HIGH = new(0, 5);

    /// <summary>两个洞口是**山体上的开口**：塌陷山洞＝塌出来的凹陷，石隙迷窟＝劈出来的窄缝。
    /// 它们**不挡路**（玩家要能走进去），四周的山体挡路。</summary>
    public static readonly Vector2I T_CAVE_RECESS = new(0, 6);
    public static readonly Vector2I T_CRACK_MOUTH = new(7, 4);

    /// <summary>路牌（主路四个节点各一块）。牌面只画箭头与字痕，地名由 Label 叠上去。</summary>
    public static readonly Vector2I T_SIGNPOST = new(7, 6);

    public static readonly Godot.Collections.Dictionary HOUSE_SLATE_WOOD = new()
    {
        { "roof_top", T_ROOF_SLATE_TOP }, { "roof", T_ROOF_SLATE },
        { "wall", T_WALL_WOOD }, { "window", T_WINDOW_WOOD }, { "door", T_DOOR_WOOD },
    };
    public static readonly Godot.Collections.Dictionary HOUSE_RED_GRAY = new()
    {
        { "roof_top", T_ROOF_RED_TOP }, { "roof", T_ROOF_RED },
        { "wall", T_WALL_GRAY }, { "window", T_WINDOW_GRAY }, { "door", T_DOOR_GRAY },
    };
    public static readonly Godot.Collections.Dictionary HOUSE_SLATE_GRAY = new()
    {
        { "roof_top", T_ROOF_SLATE_TOP }, { "roof", T_ROOF_SLATE },
        { "wall", T_WALL_GRAY }, { "window", T_WINDOW_GRAY }, { "door", T_DOOR_GRAY },
    };

    public static readonly Vector2I[] TILES_TERRAIN =
    {
        T_GRASS, T_GRASS_TUFT, T_GRASS_FLOWER, T_GRASS_PEBBLE,
        T_DIRT, T_DIRT_T, T_DIRT_B, T_DIRT_L, T_DIRT_R,
        T_DIRT_TL, T_DIRT_TR, T_DIRT_BL, T_DIRT_BR,
    };
    public static readonly Vector2I[] TILES_PLANTS =
    {
        T_TREE_PINE, T_TREE_ROUND, T_TREE_AUTUMN, T_TREE_SMALL,
        T_BUSH, T_FOREST, T_FOREST_AUTUMN, T_MUSHROOM,
    };
    public static readonly Vector2I[] TILES_STONE =
        { T_FLOOR, T_FLOOR_TOP, T_BATTLEMENT, T_WALL_TOP_CORNER, T_ARCH };

    /// <summary>石作里**真的挡路**的那些：墙与拱顶。
    ///
    /// `TILES_STONE` 一份清单曾经同时当两种用途使——「要收进图集的瓦片」和「挡路的瓦片」——
    /// 于是地板 `T_FLOOR`／`T_FLOOR_TOP` 也被写成了实心格。它们铺在 Ground 层上，
    /// **玩家一进副本就卡死在原地**（脚下整格都是碰撞盒，move_and_slide 推不动）。
    /// 2026-10-04 玩家报的正是这个；地图验收与场景自检当时都是绿的，因为
    /// verify_maps 只看 Decor、场景自检全靠瞬移（见 `框架说明.md` 决策 269）。</summary>
    public static readonly Vector2I[] TILES_STONE_SOLID = { T_BATTLEMENT, T_WALL_TOP_CORNER, T_ARCH };

    public static readonly Vector2I[] TILES_PROPS =
    {
        T_FENCE_H, T_FENCE_POST, T_FENCE_V, T_WELL, T_SIGN, T_BENCH, T_CRATE, T_CHEST, T_BARREL,
    };
    public static readonly Vector2I[] TILES_SPECIAL =
    {
        T_WATER_DEEP, T_WATER_SHALLOW, T_WATER_SHORE, T_REEDS, T_FOOTPRINT, T_BLOOD,
    };

    /// <summary>特殊瓦片里**真的挡路**的只有深水（07 文档：墙、水、悬崖都不能穿过）。
    /// 浅水／水岸／芦苇留给地编自己决定——芦苇荡要能走进去，不然渡口那张图没法侦察。</summary>
    public static readonly Vector2I[] TILES_SPECIAL_SOLID = { T_WATER_DEEP };

    /// <summary>石隙迷窟专用：一线天光与岩檐走 `Overlay` 层（不挡路），碎石堆挡路，石台不挡路。</summary>
    public static readonly Vector2I[] TILES_SHIXI =
        { T_SKY_SLIT, T_SHRINE_PLATFORM, T_RUBBLE, T_ROCK_EAVE };
    public static readonly Vector2I[] TILES_SHIXI_SOLID = { T_RUBBLE };

    /// <summary>**遮挡类**瓦片：压在 `Overlay` 层、会盖住玩家的那些——玩家走到它下方时整层半透明
    /// （设计 0.32.0／07 §节点结构「玩家走到下面时半透明」，落在 `T_ROCK_EAVE` 上）。
    /// 将来加树冠／屋檐就往这里加，**别在 `src/world/` 再抄一份坐标清单**：
    /// 运行期读的是这份清单生成的 TileSet 自定义数据 `covering`。</summary>
    public static readonly Vector2I[] TILES_COVERING = { T_ROCK_EAVE };

    /// <summary>大地图的山：成片的九宫格 ＋ 两档山峰 ＋ 岩面第二变体 ＋ 路牌；两个洞口**不挡路**。</summary>
    public static readonly Vector2I[] TILES_MOUNTAIN =
    {
        T_MTN_TL, T_MTN_T, T_MTN_TR, T_MTN_L, T_MTN_C, T_MTN_R, T_MTN_BL, T_MTN_B, T_MTN_BR,
        T_MTN_C2, T_MTN_PEAK_LOW, T_MTN_PEAK_HIGH,
    };
    public static readonly Vector2I[] TILES_MOUNTAIN_SOLID =
        Concat(TILES_MOUNTAIN, new[] { T_SIGNPOST });

    /// <summary>洞口单独列：它们是**走进去的开口**，不在 solid 里（否则玩家进不去洞）。</summary>
    public static readonly Vector2I[] TILES_MOUNTAIN_OPEN = { T_CAVE_RECESS, T_CRACK_MOUTH };

    public static readonly Vector2I[] TILES_BUILDING =
    {
        T_ROOF_RED_TOP, T_ROOF_RED, T_ROOF_SLATE_TOP, T_ROOF_SLATE, T_GABLE,
        T_WALL_GRAY, T_WINDOW_GRAY, T_DOOR_GRAY, T_WALL_WOOD, T_WINDOW_WOOD, T_DOOR_WOOD,
    };

    /// <summary>六套主题（07 文档 8.6：江南野外、山寨、洞穴、村落、城镇、石隙）。
    /// 现在共用一张占位图集，主题之间差在「收哪些瓦片、哪些挡路」，将来换真美术只换图集。</summary>
    public static readonly Godot.Collections.Dictionary THEMES = new()
    {
        // 大地图多一套「山」：九宫格 ＋ 山峰 ＋ 岩面变体 ＋ 路牌（挡路），洞口不挡路（走得进去）
        {
            "jiangnan_wild", Theme(
                Concat(TILES_TERRAIN, TILES_PLANTS, TILES_STONE, TILES_PROPS, TILES_BUILDING,
                    TILES_SPECIAL, TILES_MOUNTAIN, TILES_MOUNTAIN_OPEN, new[] { T_SIGNPOST }),
                Concat(TILES_PLANTS, TILES_STONE_SOLID, TILES_BUILDING, TILES_SPECIAL_SOLID,
                    TILES_MOUNTAIN_SOLID))
        },
        {
            "town", Theme(
                Concat(TILES_TERRAIN, TILES_PLANTS, TILES_STONE, TILES_PROPS, TILES_BUILDING,
                    TILES_SPECIAL),
                Concat(TILES_PLANTS, TILES_STONE_SOLID, TILES_BUILDING, TILES_SPECIAL_SOLID))
        },
        {
            "heifengzhai", Theme(
                Concat(TILES_STONE, TILES_TERRAIN, TILES_PROPS, TILES_BUILDING, TILES_SPECIAL),
                Concat(TILES_STONE_SOLID, TILES_PROPS, TILES_BUILDING, TILES_SPECIAL_SOLID))
        },
        {
            "cave", Theme(
                Concat(TILES_STONE, TILES_TERRAIN, TILES_SPECIAL),
                Concat(TILES_STONE_SOLID, TILES_SPECIAL_SOLID))
        },
        {
            "village", Theme(
                Concat(TILES_TERRAIN, TILES_PLANTS, TILES_STONE, TILES_PROPS, TILES_BUILDING,
                    TILES_SPECIAL),
                Concat(TILES_PLANTS, TILES_PROPS, TILES_BUILDING, TILES_SPECIAL_SOLID))
        },
        // 石隙迷窟：只有岩、碎石与前朝石台，没有植被与建筑（16 §4.8）。
        // `wallStyle = crack` 让主墙体走「笔直有裂缝」的画法，与塌陷山洞的砌块岩壁区分开。
        {
            "shixi", Theme(
                Concat(TILES_STONE, TILES_TERRAIN, TILES_SPECIAL, TILES_SHIXI),
                Concat(TILES_STONE_SOLID, TILES_SPECIAL_SOLID, TILES_SHIXI_SOLID))
        },
    };

    private static Vector2I[] Concat(Vector2I[] first, params Vector2I[][] rest)
    {
        var list = new List<Vector2I>(first);
        foreach (var part in rest)
        {
            list.AddRange(part);
        }

        return list.ToArray();
    }

    private static Godot.Collections.Dictionary Theme(Vector2I[] tiles, Vector2I[] solid) =>
        new() { { "tiles", ToArray(tiles) }, { "solid", ToArray(solid) } };

    private static Godot.Collections.Array ToArray(Vector2I[] tiles)
    {
        var array = new Godot.Collections.Array();
        foreach (var tile in tiles)
        {
            array.Add(tile);
        }

        return array;
    }

    private static bool Contains(Vector2I[] tiles, Vector2I tile) => System.Array.IndexOf(tiles, tile) >= 0;

    // ---------------------------------------------------------------- 取值口（给还在 GDScript 里的生成器）

    // GDScript 读不到 C# 的 `const`／静态字段（见类头注释），所以下面每一个都要有方法口。
    // 这些方法只是取值，别的什么都不做——生成器搬成 C# 之后整块可以删。
    public static string atlas_dir() => ATLAS_DIR;
    public static string fog_atlas() => FOG_ATLAS;
    public static string placeholder_texture() => PLACEHOLDER_TEXTURE;
    public static string npc_texture() => NPC_TEXTURE;
    public static string prop_ferry_pile() => PROP_FERRY_PILE;
    public static string prop_luoyanpo_cart() => PROP_LUOYANPO_CART;
    public static int tile_px() => TILE_PX;
    public static int source_id() => SOURCE_ID;
    public static int placeholder_source_id() => PLACEHOLDER_SOURCE_ID;
    public static Vector2I t_fog() => T_FOG;
    public static int layer_block() => LAYER_BLOCK;
    public static int layer_interact() => LAYER_INTERACT;
    public static int layer_alert() => LAYER_ALERT;
    public static int layer_room() => LAYER_ROOM;
    public static int layer_event() => LAYER_EVENT;
    public static Vector2I t_grass() => T_GRASS;
    public static Vector2I t_grass_tuft() => T_GRASS_TUFT;
    public static Vector2I t_grass_flower() => T_GRASS_FLOWER;
    public static Vector2I t_grass_pebble() => T_GRASS_PEBBLE;
    public static Vector2I t_dirt() => T_DIRT;
    public static Vector2I t_dirt_t() => T_DIRT_T;
    public static Vector2I t_dirt_b() => T_DIRT_B;
    public static Vector2I t_dirt_l() => T_DIRT_L;
    public static Vector2I t_dirt_r() => T_DIRT_R;
    public static Vector2I t_dirt_tl() => T_DIRT_TL;
    public static Vector2I t_dirt_tr() => T_DIRT_TR;
    public static Vector2I t_dirt_bl() => T_DIRT_BL;
    public static Vector2I t_dirt_br() => T_DIRT_BR;
    public static Vector2I t_floor() => T_FLOOR;
    public static Vector2I t_floor_top() => T_FLOOR_TOP;
    public static Vector2I t_tree_pine() => T_TREE_PINE;
    public static Vector2I t_tree_round() => T_TREE_ROUND;
    public static Vector2I t_tree_autumn() => T_TREE_AUTUMN;
    public static Vector2I t_tree_small() => T_TREE_SMALL;
    public static Vector2I t_bush() => T_BUSH;
    public static Vector2I t_forest() => T_FOREST;
    public static Vector2I t_forest_autumn() => T_FOREST_AUTUMN;
    public static Vector2I t_mushroom() => T_MUSHROOM;
    public static Vector2I t_roof_red_top() => T_ROOF_RED_TOP;
    public static Vector2I t_roof_red() => T_ROOF_RED;
    public static Vector2I t_roof_slate_top() => T_ROOF_SLATE_TOP;
    public static Vector2I t_roof_slate() => T_ROOF_SLATE;
    public static Vector2I t_gable() => T_GABLE;
    public static Vector2I t_wall_gray() => T_WALL_GRAY;
    public static Vector2I t_window_gray() => T_WINDOW_GRAY;
    public static Vector2I t_door_gray() => T_DOOR_GRAY;
    public static Vector2I t_wall_wood() => T_WALL_WOOD;
    public static Vector2I t_window_wood() => T_WINDOW_WOOD;
    public static Vector2I t_door_wood() => T_DOOR_WOOD;
    public static Vector2I t_battlement() => T_BATTLEMENT;
    public static Vector2I t_wall_top_corner() => T_WALL_TOP_CORNER;
    public static Vector2I t_arch() => T_ARCH;
    public static Vector2I t_fence_h() => T_FENCE_H;
    public static Vector2I t_fence_post() => T_FENCE_POST;
    public static Vector2I t_fence_v() => T_FENCE_V;
    public static Vector2I t_well() => T_WELL;
    public static Vector2I t_sign() => T_SIGN;
    public static Vector2I t_bench() => T_BENCH;
    public static Vector2I t_crate() => T_CRATE;
    public static Vector2I t_chest() => T_CHEST;
    public static Vector2I t_barrel() => T_BARREL;
    public static Vector2I t_water_deep() => T_WATER_DEEP;
    public static Vector2I t_water_shallow() => T_WATER_SHALLOW;
    public static Vector2I t_water_shore() => T_WATER_SHORE;
    public static Vector2I t_reeds() => T_REEDS;
    public static Vector2I t_footprint() => T_FOOTPRINT;
    public static Vector2I t_blood() => T_BLOOD;
    public static Vector2I t_sky_slit() => T_SKY_SLIT;
    public static Vector2I t_shrine_platform() => T_SHRINE_PLATFORM;
    public static Vector2I t_rubble() => T_RUBBLE;
    public static Vector2I t_rock_eave() => T_ROCK_EAVE;
    public static Vector2I t_mtn_tl() => T_MTN_TL;
    public static Vector2I t_mtn_t() => T_MTN_T;
    public static Vector2I t_mtn_tr() => T_MTN_TR;
    public static Vector2I t_mtn_l() => T_MTN_L;
    public static Vector2I t_mtn_c() => T_MTN_C;
    public static Vector2I t_mtn_c2() => T_MTN_C2;
    public static Vector2I t_mtn_r() => T_MTN_R;
    public static Vector2I t_mtn_bl() => T_MTN_BL;
    public static Vector2I t_mtn_b() => T_MTN_B;
    public static Vector2I t_mtn_br() => T_MTN_BR;
    public static Vector2I t_mtn_peak_low() => T_MTN_PEAK_LOW;
    public static Vector2I t_mtn_peak_high() => T_MTN_PEAK_HIGH;
    public static Vector2I t_cave_recess() => T_CAVE_RECESS;
    public static Vector2I t_crack_mouth() => T_CRACK_MOUTH;
    public static Vector2I t_signpost() => T_SIGNPOST;
    public static Godot.Collections.Dictionary house_slate_wood() => HOUSE_SLATE_WOOD;
    public static Godot.Collections.Dictionary house_red_gray() => HOUSE_RED_GRAY;
    public static Godot.Collections.Dictionary house_slate_gray() => HOUSE_SLATE_GRAY;
    public static Godot.Collections.Dictionary themes() => THEMES;

    // ---------------------------------------------------------------- TileSet

    public static string theme_tileset_path(string theme) =>
        $"res://assets/tilesets/{theme}/{theme}_tileset.tres";

    public static string theme_atlas_path(string theme) => $"{ATLAS_DIR}/{theme}/tilemap_packed.png";

    public static TileSet build_theme_tile_set(string theme)
    {
        var atlas = GD.Load<Texture2D>(theme_atlas_path(theme));
        var fog = GD.Load<Texture2D>(FOG_ATLAS);
        if (atlas == null || fog == null)
        {
            return null;
        }

        if (!THEMES.ContainsKey(theme))
        {
            GD.PushError($"未知主题：{theme}");
            return null;
        }

        var tileSet = new TileSet();
        tileSet.TileSize = new Vector2I(TILE_PX, TILE_PX);
        tileSet.AddPhysicsLayer();
        tileSet.SetPhysicsLayerCollisionLayer(0, LAYER_BLOCK);
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(0, "blocked");
        tileSet.SetCustomDataLayerType(0, Variant.Type.Bool);
        // 第二层：`covering`＝这一格会不会盖住玩家（`Overlay` 上的岩檐／树冠）。
        // 运行期（`src/world/LocalMapController.cs`）只读它，不认坐标清单。
        tileSet.AddCustomDataLayer();
        tileSet.SetCustomDataLayerName(1, "covering");
        tileSet.SetCustomDataLayerType(1, Variant.Type.Bool);

        var source = new TileSetAtlasSource();
        source.Texture = atlas;
        source.TextureRegionSize = new Vector2I(TILE_PX, TILE_PX);
        tileSet.AddSource(source, SOURCE_ID);

        var fogSource = new TileSetAtlasSource();
        fogSource.Texture = fog;
        fogSource.TextureRegionSize = new Vector2I(TILE_PX, TILE_PX);
        tileSet.AddSource(fogSource, PLACEHOLDER_SOURCE_ID);
        fogSource.CreateTile(T_FOG);
        fogSource.GetTileData(T_FOG, 0).SetCustomData("blocked", false);
        fogSource.GetTileData(T_FOG, 0).SetCustomData("covering", false);

        float half = TILE_PX / 2.0f;
        Vector2[] square =
        {
            new(-half, -half), new(half, -half), new(half, half), new(-half, half),
        };
        var themeEntry = THEMES[theme].AsGodotDictionary();
        var solid = new Godot.Collections.Dictionary();
        foreach (Variant tile in themeEntry["solid"].AsGodotArray())
        {
            solid[tile] = true;
        }

        var seen = new Godot.Collections.Dictionary();
        foreach (Variant tileVariant in themeEntry["tiles"].AsGodotArray())
        {
            var tile = tileVariant.AsVector2I();
            if (seen.ContainsKey(tile))
            {
                continue;
            }

            seen[tile] = true;
            source.CreateTile(tile);
            var data = source.GetTileData(tile, 0);
            bool isSolid = solid.ContainsKey(tile);
            data.SetCustomData("blocked", isSolid);
            data.SetCustomData("covering", Contains(TILES_COVERING, tile));
            if (isSolid)
            {
                data.SetCollisionPolygonsCount(0, 1);
                data.SetCollisionPolygonPoints(0, 0, square);
            }
        }

        return tileSet;
    }

    /// <summary>存 TileSet 并回读，让场景引用共享的 .tres 而不是把它内联进去。</summary>
    public static TileSet save_theme_tile_set(string theme)
    {
        var tileSet = build_theme_tile_set(theme);
        if (tileSet == null)
        {
            return null;
        }

        string path = theme_tileset_path(theme);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        var err = ResourceSaver.Save(tileSet, path);
        // err=19（ERR_CANT_OPEN）在这台机器上偶发：刚写过的文件会被杀软/索引器短暂占用，
        // 重试几次就过去了。不重试的话整张图会被静默跳过（场景留在旧版本上）。
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (err == Error.Ok)
            {
                break;
            }

            OS.DelayMsec(80);
            err = ResourceSaver.Save(tileSet, path);
        }

        if (err != Error.Ok)
        {
            GD.PushError($"[map_kit] 保存 {path} 失败：err={(int)err}");
            return null;
        }

        return ResourceLoader.Load<TileSet>(path, "", ResourceLoader.CacheMode.Replace);
    }

    // ---------------------------------------------------------------- 场景骨架

    /// <summary>按 07 文档的节点结构搭一张地图的壳。
    /// with_fog 只给大地图开（探索迷雾层，占一个 TileMapLayer 名额）。
    ///
    /// `with_conditional` 是 0.32.0 新加的第 5 层（16 §3.2「条件地表」）：**整层显隐**的地表，
    /// 石隙那条 22 格碎石细径铺在这里——拿到 `item_treasure_map` 之前它在地图上不存在。
    /// 它与 `Overlay` 的分工写死在 16 §3.2：`Overlay` 一直在（玩家走到下面才半透明），
    /// `Conditional` 是程序按条件整层开关。层序 Ground → Decor → Overlay → Conditional → Fog。</summary>
    public static Godot.Collections.Dictionary new_shell(
        string root_name, TileSet tile_set, bool with_fog = false, bool with_conditional = false)
    {
        var root = new Node2D { Name = root_name };
        var shell = new Godot.Collections.Dictionary { { "root", root } };

        var ground = _layer("Ground", tile_set, false);
        var decor = _layer("Decor", tile_set, true);
        var overlay = _layer("Overlay", tile_set, false);
        root.AddChild(ground);
        root.AddChild(decor);
        root.AddChild(overlay);
        shell["ground"] = ground;
        shell["decor"] = decor;
        shell["overlay"] = overlay;

        if (with_conditional)
        {
            var conditional = _layer("Conditional", tile_set, false);
            root.AddChild(conditional);
            shell["conditional"] = conditional;
        }

        if (with_fog)
        {
            var fog = _layer("Fog", tile_set, false);
            root.AddChild(fog);
            shell["fog"] = fog;
        }

        var rooms = _group("Rooms", root);
        var markers = _group("Markers", root);
        var characters = _group("Characters", root);
        var enemies = _group("Enemies", root);
        var ysort = _group("YSort", root);
        shell["rooms"] = rooms;
        shell["markers"] = markers;
        shell["characters"] = characters;
        shell["enemies"] = enemies;
        shell["ysort"] = ysort;

        var camera = new Camera2D { Name = "Camera" };
        root.AddChild(camera);
        shell["camera"] = camera;
        return shell;
    }

    private static TileMapLayer _layer(string layer_name, TileSet tile_set, bool y_sort) =>
        new() { Name = layer_name, TileSet = tile_set, YSortEnabled = y_sort };

    private static Node2D _group(string group_name, Node parent)
    {
        var holder = new Node2D { Name = group_name, YSortEnabled = true };
        parent.AddChild(holder);
        return holder;
    }

    public static Node2D folder(Node parent, string folder_name)
    {
        var holder = new Node2D { Name = folder_name };
        parent.AddChild(holder);
        return holder;
    }

    /// <summary>相机对齐地图边界（07 文档节点结构里的 Camera）。</summary>
    public static void fit_camera(Camera2D camera, int map_w, int map_h)
    {
        camera.LimitLeft = 0;
        camera.LimitTop = 0;
        camera.LimitRight = map_w * TILE_PX;
        camera.LimitBottom = map_h * TILE_PX;
        camera.Position = new Vector2(map_w * TILE_PX / 2.0f, map_h * TILE_PX / 2.0f);
    }

    // ---------------------------------------------------------------- Marker 与实体

    public static Marker2D add_marker(Node parent, string marker_name, Vector2I cell)
    {
        var marker = new Marker2D { Name = marker_name, Position = cell_center(cell) };
        parent.AddChild(marker);
        return marker;
    }

    /// <summary>大地图的地标坐标直接用表里的 pos_x/pos_y（像素），不做格子吸附。</summary>
    public static Marker2D add_marker_at(Node parent, string marker_name, Vector2 pixel)
    {
        var marker = new Marker2D { Name = marker_name, Position = pixel };
        parent.AddChild(marker);
        return marker;
    }

    public static Sprite2D add_sprite(
        Node parent, string sprite_name, string texture_path, Vector2 offset = default)
    {
        var sprite = new Sprite2D
        {
            Name = sprite_name,
            Texture = GD.Load<Texture2D>(texture_path),
            Position = offset,
        };
        parent.AddChild(sprite);
        return sprite;
    }

    public static Path2D add_path(Node parent, string path_id, Godot.Collections.Array points)
    {
        var path = new Path2D { Name = path_id };
        var curve = new Curve2D();
        foreach (Variant point in points)
        {
            curve.AddPoint(cell_center(point.AsVector2I()));
        }

        path.Curve = curve;
        parent.AddChild(path);
        return path;
    }

    /// <summary>房间：Node2D 挂 Area2D（层 5 房间边界），名字 = Room_&lt;room_id&gt;。</summary>
    public static Node2D add_room(Node rooms, string room_id, Rect2I rect)
    {
        var room = new Node2D
        {
            Name = "Room_" + room_id,
            Position = new Vector2(rect.Position.X * TILE_PX, rect.Position.Y * TILE_PX),
        };
        var area = new Area2D { Name = "bounds", CollisionLayer = LAYER_ROOM, CollisionMask = 0 };
        var shape = new CollisionShape2D();
        var rectShape = new RectangleShape2D
        {
            Size = new Vector2(rect.Size.X * TILE_PX, rect.Size.Y * TILE_PX),
        };
        shape.Shape = rectShape;
        shape.Position = new Vector2(
            rect.Size.X * TILE_PX / 2.0f, rect.Size.Y * TILE_PX / 2.0f);
        area.AddChild(shape);
        room.AddChild(area);
        rooms.AddChild(room);
        return room;
    }

    /// <summary>明雷警戒圈：Area2D + CircleShape2D，半径 = alert_radius 格 × 32px（07 文档物理层 4）。</summary>
    public static Area2D add_alert(Node2D marker, double radius_cells)
    {
        var area = new Area2D { Name = "alert", CollisionLayer = LAYER_ALERT, CollisionMask = 0 };
        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = (float)(radius_cells * TILE_PX) };
        area.AddChild(shape);
        marker.AddChild(area);
        return area;
    }

    public static Vector2 cell_center(Vector2I cell) =>
        new(cell.X * TILE_PX + TILE_PX / 2.0f, cell.Y * TILE_PX + TILE_PX / 2.0f);

    public static Vector2 accumulated_position(Node2D node)
    {
        var pos = node.Position;
        var parent = node.GetParent();
        while (parent != null && parent is Node2D parent2D)
        {
            pos += parent2D.Position;
            parent = parent.GetParent();
        }

        return pos;
    }
}
