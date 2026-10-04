## 地图搭建工具箱：32px 瓦片、五套主题 TileSet、场景骨架、房间／走廊、Marker 绑定。
##
## 约定全部来自 docs/design/07_地图资源需求.md：
##   瓦片 32×32、物理层 1-6、节点结构（Ground/Decor/Overlay/Rooms/Markers/Characters/Enemies/YSort/Camera）、
##   Marker 名字前缀（Node_/Portal_/Room_/Chest_/Spawn_/Event_/Trigger_/Patrol_）。
##
## 用法：`const MapKit := preload("res://tools/mapgen/map_kit.gd")` 然后调静态函数。
extends RefCounted

## 图集按**主题**分开（15_美术风格需求 §六：`tilesets/ink_jianghu/<主题>/tilemap_packed.png`）。
## 五套主题的图集坐标完全一致，差别在画风与主色——16_区域美术设定要求
## 「相邻区域不许撞主色」，共用一张图集就做不到这件事。
const ATLAS_DIR := "res://assets/tilesets/ink_jianghu"
const FOG_ATLAS := "res://assets/tilesets/ink_jianghu/fog_tile.png"
const PLACEHOLDER_TEXTURE := "res://assets/sprites/props/prop_placeholder.png"
const NPC_TEXTURE := "res://assets/sprites/characters/npc_placeholder.png"

const TILE_PX := 32
const SOURCE_ID := 0
## 迷雾走第二个图集源，免得混进 Kenney 那套索引。
const PLACEHOLDER_SOURCE_ID := 1
const T_FOG := Vector2i(0, 0)

# --- 物理层（07 文档「碰撞与物理层约定」，值是 Godot 的 layer 位）---
const LAYER_BLOCK := 2
const LAYER_INTERACT := 4
const LAYER_ALERT := 8
const LAYER_ROOM := 16
const LAYER_EVENT := 32

# --- 地面 ---
const T_GRASS := Vector2i(0, 0)
const T_GRASS_TUFT := Vector2i(1, 0)
const T_GRASS_FLOWER := Vector2i(2, 0)
const T_GRASS_PEBBLE := Vector2i(7, 3)
const T_DIRT := Vector2i(1, 2)
const T_DIRT_T := Vector2i(1, 1)
const T_DIRT_B := Vector2i(1, 3)
const T_DIRT_L := Vector2i(0, 2)
const T_DIRT_R := Vector2i(2, 2)
const T_DIRT_TL := Vector2i(0, 1)
const T_DIRT_TR := Vector2i(2, 1)
const T_DIRT_BL := Vector2i(0, 3)
const T_DIRT_BR := Vector2i(2, 3)
const T_FLOOR := Vector2i(1, 9)
const T_FLOOR_TOP := Vector2i(1, 8)

# --- 植被 ---
const T_TREE_PINE := Vector2i(4, 0)
const T_TREE_ROUND := Vector2i(4, 1)
const T_TREE_AUTUMN := Vector2i(3, 1)
const T_TREE_SMALL := Vector2i(7, 2)
const T_BUSH := Vector2i(5, 0)
const T_FOREST := Vector2i(7, 0)
const T_FOREST_AUTUMN := Vector2i(10, 1)
const T_MUSHROOM := Vector2i(5, 2)

# --- 建筑与石作 ---
const T_ROOF_RED_TOP := Vector2i(5, 4)
const T_ROOF_RED := Vector2i(5, 5)
const T_ROOF_SLATE_TOP := Vector2i(1, 4)
const T_ROOF_SLATE := Vector2i(1, 5)
const T_GABLE := Vector2i(7, 5)
const T_WALL_GRAY := Vector2i(5, 6)
const T_WINDOW_GRAY := Vector2i(4, 7)
const T_DOOR_GRAY := Vector2i(7, 7)
const T_WALL_WOOD := Vector2i(1, 6)
const T_WINDOW_WOOD := Vector2i(0, 7)
const T_DOOR_WOOD := Vector2i(1, 7)
const T_BATTLEMENT := Vector2i(4, 8)
const T_WALL_TOP_CORNER := Vector2i(3, 8)
const T_ARCH := Vector2i(4, 9)

# --- 物件 ---
const T_FENCE_H := Vector2i(9, 3)
const T_FENCE_POST := Vector2i(10, 3)
const T_FENCE_V := Vector2i(11, 3)
const T_WELL := Vector2i(10, 7)
const T_SIGN := Vector2i(10, 6)
const T_BENCH := Vector2i(9, 6)
const T_CRATE := Vector2i(11, 6)
const T_CHEST := Vector2i(10, 8)
const T_BARREL := Vector2i(11, 8)

# --- 特殊（15 §4.1：雾层、水面、血迹、脚印）---
# 图集里一直留着这几格，但主题的收图清单没收它们；渡口那张图因此只能拿栅栏＋木箱顶替
# （见 docs/dev/地图搭建说明.md「还缺的地图资产」第 10 条）。
const T_WATER_DEEP := Vector2i(6, 0)
const T_WATER_SHALLOW := Vector2i(6, 1)
const T_WATER_SHORE := Vector2i(6, 2)
const T_REEDS := Vector2i(6, 3)
const T_FOOTPRINT := Vector2i(6, 4)
const T_BLOOD := Vector2i(6, 5)

# --- 石隙迷窟专用（16 §4.8：窄岩缝里的一线天光）---
# 这三格是「死路看起来能走」那条要求的落点，见 `docs/dev/地图搭建说明.md`：
# 岩檐压在岔缝内段上，玩家在缝口读不出深浅；天光是诱饵；石台是终点。
const T_SKY_SLIT := Vector2i(6, 6)
const T_SHRINE_PLATFORM := Vector2i(6, 7)
const T_RUBBLE := Vector2i(6, 8)
const T_ROCK_EAVE := Vector2i(6, 9)

# --- 大地图的山（16 §3.5：山脉成片、有厚度；洞口是"山体上的开口"）---
# 山体走**九宫格**（与土路那套同一种用法）：mask 说「这一格哪几条边还是山」。
const T_MTN_TL := Vector2i(2, 4)
const T_MTN_T := Vector2i(3, 4)
const T_MTN_TR := Vector2i(4, 4)
const T_MTN_L := Vector2i(2, 5)
const T_MTN_C := Vector2i(3, 5)
const T_MTN_C2 := Vector2i(8, 4)
const T_MTN_R := Vector2i(4, 5)
const T_MTN_BL := Vector2i(2, 6)
const T_MTN_B := Vector2i(3, 6)
const T_MTN_BR := Vector2i(4, 6)
const T_MTN_PEAK_LOW := Vector2i(0, 4)
const T_MTN_PEAK_HIGH := Vector2i(0, 5)
## 两个洞口是**山体上的开口**：塌陷山洞＝塌出来的凹陷，石隙迷窟＝劈出来的窄缝。
## 它们**不挡路**（玩家要能走进去），四周的山体挡路。
const T_CAVE_RECESS := Vector2i(0, 6)
const T_CRACK_MOUTH := Vector2i(7, 4)
## 路牌（主路四个节点各一块）。牌面只画箭头与字痕，地名由 Label 叠上去。
const T_SIGNPOST := Vector2i(7, 6)

const HOUSE_SLATE_WOOD := {
	"roof_top": T_ROOF_SLATE_TOP, "roof": T_ROOF_SLATE,
	"wall": T_WALL_WOOD, "window": T_WINDOW_WOOD, "door": T_DOOR_WOOD,
}
const HOUSE_RED_GRAY := {
	"roof_top": T_ROOF_RED_TOP, "roof": T_ROOF_RED,
	"wall": T_WALL_GRAY, "window": T_WINDOW_GRAY, "door": T_DOOR_GRAY,
}
const HOUSE_SLATE_GRAY := {
	"roof_top": T_ROOF_SLATE_TOP, "roof": T_ROOF_SLATE,
	"wall": T_WALL_GRAY, "window": T_WINDOW_GRAY, "door": T_DOOR_GRAY,
}

const TILES_TERRAIN := [
	T_GRASS, T_GRASS_TUFT, T_GRASS_FLOWER, T_GRASS_PEBBLE,
	T_DIRT, T_DIRT_T, T_DIRT_B, T_DIRT_L, T_DIRT_R,
	T_DIRT_TL, T_DIRT_TR, T_DIRT_BL, T_DIRT_BR,
]
const TILES_PLANTS := [
	T_TREE_PINE, T_TREE_ROUND, T_TREE_AUTUMN, T_TREE_SMALL,
	T_BUSH, T_FOREST, T_FOREST_AUTUMN, T_MUSHROOM,
]
const TILES_STONE := [T_FLOOR, T_FLOOR_TOP, T_BATTLEMENT, T_WALL_TOP_CORNER, T_ARCH]
## 石作里**真的挡路**的那些：墙与拱顶。
##
## `TILES_STONE` 一份清单曾经同时当两种用途使——「要收进图集的瓦片」和「挡路的瓦片」——
## 于是地板 `T_FLOOR`／`T_FLOOR_TOP` 也被写成了实心格。它们铺在 Ground 层上，
## **玩家一进副本就卡死在原地**（脚下整格都是碰撞盒，move_and_slide 推不动）。
## 2026-10-04 玩家报的正是这个；地图验收与场景自检当时都是绿的，因为
## verify_maps 只看 Decor、场景自检全靠瞬移（见 `框架说明.md` 决策 269）。
const TILES_STONE_SOLID := [T_BATTLEMENT, T_WALL_TOP_CORNER, T_ARCH]
const TILES_PROPS := [
	T_FENCE_H, T_FENCE_POST, T_FENCE_V, T_WELL, T_SIGN, T_BENCH, T_CRATE, T_CHEST, T_BARREL,
]
const TILES_SPECIAL := [
	T_WATER_DEEP, T_WATER_SHALLOW, T_WATER_SHORE, T_REEDS, T_FOOTPRINT, T_BLOOD,
]
## 特殊瓦片里**真的挡路**的只有深水（07 文档：墙、水、悬崖都不能穿过）。
## 浅水／水岸／芦苇留给地编自己决定——芦苇荡要能走进去，不然渡口那张图没法侦察。
const TILES_SPECIAL_SOLID := [T_WATER_DEEP]
## 石隙迷窟专用：一线天光与岩檐走 `Overlay` 层（不挡路），碎石堆挡路，石台不挡路。
const TILES_SHIXI := [T_SKY_SLIT, T_SHRINE_PLATFORM, T_RUBBLE, T_ROCK_EAVE]
const TILES_SHIXI_SOLID := [T_RUBBLE]
## **遮挡类**瓦片：压在 `Overlay` 层、会盖住玩家的那些——玩家走到它下方时整层半透明
## （设计 0.32.0／07 §节点结构「玩家走到下面时半透明」，落在 `T_ROCK_EAVE` 上）。
## 将来加树冠／屋檐就往这里加，**别在 `src/world/` 再抄一份坐标清单**：
## 运行期读的是这份清单生成的 TileSet 自定义数据 `covering`。
const TILES_COVERING := [T_ROCK_EAVE]
## 大地图的山：成片的九宫格 ＋ 两档山峰 ＋ 岩面第二变体 ＋ 路牌；两个洞口**不挡路**。
const TILES_MOUNTAIN := [
	T_MTN_TL, T_MTN_T, T_MTN_TR, T_MTN_L, T_MTN_C, T_MTN_R, T_MTN_BL, T_MTN_B, T_MTN_BR,
	T_MTN_C2, T_MTN_PEAK_LOW, T_MTN_PEAK_HIGH,
]
const TILES_MOUNTAIN_SOLID := TILES_MOUNTAIN + [T_SIGNPOST]
## 洞口单独列：它们是**走进去的开口**，不在 solid 里（否则玩家进不去洞）
const TILES_MOUNTAIN_OPEN := [T_CAVE_RECESS, T_CRACK_MOUTH]
const TILES_BUILDING := [
	T_ROOF_RED_TOP, T_ROOF_RED, T_ROOF_SLATE_TOP, T_ROOF_SLATE, T_GABLE,
	T_WALL_GRAY, T_WINDOW_GRAY, T_DOOR_GRAY, T_WALL_WOOD, T_WINDOW_WOOD, T_DOOR_WOOD,
]

## 五套主题（07 文档 8.6：江南野外、山寨、洞穴、村落、城镇）。
## 现在共用一张占位图集，主题之间差在「收哪些瓦片、哪些挡路」，将来换真美术只换图集。
const THEMES := {
	# 大地图多一套「山」：九宫格 ＋ 山峰 ＋ 岩面变体 ＋ 路牌（挡路），洞口不挡路（走得进去）
	"jiangnan_wild": {"tiles": TILES_TERRAIN + TILES_PLANTS + TILES_STONE + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL
			+ TILES_MOUNTAIN + TILES_MOUNTAIN_OPEN + [T_SIGNPOST],
		"solid": TILES_PLANTS + TILES_STONE_SOLID + TILES_BUILDING + TILES_SPECIAL_SOLID + TILES_MOUNTAIN_SOLID},
	"town": {"tiles": TILES_TERRAIN + TILES_PLANTS + TILES_STONE + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL,
		"solid": TILES_PLANTS + TILES_STONE_SOLID + TILES_BUILDING + TILES_SPECIAL_SOLID},
	"heifengzhai": {"tiles": TILES_STONE + TILES_TERRAIN + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL,
		"solid": TILES_STONE_SOLID + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL_SOLID},
	"cave": {"tiles": TILES_STONE + TILES_TERRAIN + TILES_SPECIAL,
		"solid": TILES_STONE_SOLID + TILES_SPECIAL_SOLID},
	"village": {"tiles": TILES_TERRAIN + TILES_PLANTS + TILES_STONE + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL,
		"solid": TILES_PLANTS + TILES_PROPS + TILES_BUILDING + TILES_SPECIAL_SOLID},
	# 石隙迷窟：只有岩、碎石与前朝石台，没有植被与建筑（16 §4.8）。
	# `wallStyle = crack` 让主墙体走「笔直有裂缝」的画法，与塌陷山洞的砌块岩壁区分开。
	"shixi": {"tiles": TILES_STONE + TILES_TERRAIN + TILES_SPECIAL + TILES_SHIXI,
		"solid": TILES_STONE_SOLID + TILES_SPECIAL_SOLID + TILES_SHIXI_SOLID},
}


# ---------------------------------------------------------------- TileSet

static func theme_tileset_path(theme: String) -> String:
	return "res://assets/tilesets/%s/%s_tileset.tres" % [theme, theme]


static func theme_atlas_path(theme: String) -> String:
	return "%s/%s/tilemap_packed.png" % [ATLAS_DIR, theme]


static func build_theme_tile_set(theme: String) -> TileSet:
	var atlas: Texture2D = load(theme_atlas_path(theme))
	var fog: Texture2D = load(FOG_ATLAS)
	if atlas == null or fog == null:
		return null
	if not THEMES.has(theme):
		push_error("未知主题：%s" % theme)
		return null

	var tile_set := TileSet.new()
	tile_set.tile_size = Vector2i(TILE_PX, TILE_PX)
	tile_set.add_physics_layer()
	tile_set.set_physics_layer_collision_layer(0, LAYER_BLOCK)
	tile_set.add_custom_data_layer()
	tile_set.set_custom_data_layer_name(0, "blocked")
	tile_set.set_custom_data_layer_type(0, TYPE_BOOL)
	# 第二层：`covering`＝这一格会不会盖住玩家（`Overlay` 上的岩檐／树冠）。
	# 运行期（`src/world/local_map_controller.gd`）只读它，不认坐标清单。
	tile_set.add_custom_data_layer()
	tile_set.set_custom_data_layer_name(1, "covering")
	tile_set.set_custom_data_layer_type(1, TYPE_BOOL)

	var source := TileSetAtlasSource.new()
	source.texture = atlas
	source.texture_region_size = Vector2i(TILE_PX, TILE_PX)
	tile_set.add_source(source, SOURCE_ID)

	var fog_source := TileSetAtlasSource.new()
	fog_source.texture = fog
	fog_source.texture_region_size = Vector2i(TILE_PX, TILE_PX)
	tile_set.add_source(fog_source, PLACEHOLDER_SOURCE_ID)
	fog_source.create_tile(T_FOG)
	fog_source.get_tile_data(T_FOG, 0).set_custom_data("blocked", false)
	fog_source.get_tile_data(T_FOG, 0).set_custom_data("covering", false)

	var half := TILE_PX / 2.0
	var square := PackedVector2Array([
		Vector2(-half, -half), Vector2(half, -half), Vector2(half, half), Vector2(-half, half),
	])
	var solid := {}
	for tile: Vector2i in THEMES[theme]["solid"]:
		solid[tile] = true
	var seen := {}
	for tile: Vector2i in THEMES[theme]["tiles"]:
		if seen.has(tile):
			continue
		seen[tile] = true
		source.create_tile(tile)
		var data := source.get_tile_data(tile, 0)
		var is_solid: bool = solid.has(tile)
		data.set_custom_data("blocked", is_solid)
		data.set_custom_data("covering", TILES_COVERING.has(tile))
		if is_solid:
			data.set_collision_polygons_count(0, 1)
			data.set_collision_polygon_points(0, 0, square)
	return tile_set


## 存 TileSet 并回读，让场景引用共享的 .tres 而不是把它内联进去。
static func save_theme_tile_set(theme: String) -> TileSet:
	var tile_set := build_theme_tile_set(theme)
	if tile_set == null:
		return null
	var path := theme_tileset_path(theme)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path.get_base_dir()))
	var err := ResourceSaver.save(tile_set, path)
	# err=19（ERR_CANT_OPEN）在这台机器上偶发：刚写过的文件会被杀软/索引器短暂占用，
	# 重试几次就过去了。不重试的话整张图会被静默跳过（场景留在旧版本上）。
	for _attempt in 3:
		if err == OK:
			break
		OS.delay_msec(80)
		err = ResourceSaver.save(tile_set, path)
	if err != OK:
		printerr("[map_kit] 保存 %s 失败：err=%d" % [path, err])
		return null
	return ResourceLoader.load(path, "", ResourceLoader.CACHE_MODE_REPLACE)


# ---------------------------------------------------------------- 场景骨架

## 按 07 文档的节点结构搭一张地图的壳。
## with_fog 只给大地图开（探索迷雾层，占一个 TileMapLayer 名额）。
##
## `with_conditional` 是 0.32.0 新加的第 5 层（16 §3.2「条件地表」）：**整层显隐**的地表，
## 石隙那条 22 格碎石细径铺在这里——拿到 `item_treasure_map` 之前它在地图上不存在。
## 它与 `Overlay` 的分工写死在 16 §3.2：`Overlay` 一直在（玩家走到下面才半透明），
## `Conditional` 是程序按条件整层开关。层序 Ground → Decor → Overlay → Conditional → Fog。
static func new_shell(root_name: String, tile_set: TileSet, with_fog: bool = false,
		with_conditional: bool = false) -> Dictionary:
	var root := Node2D.new()
	root.name = root_name
	var shell := {"root": root}

	var ground := _layer("Ground", tile_set, false)
	var decor := _layer("Decor", tile_set, true)
	var overlay := _layer("Overlay", tile_set, false)
	root.add_child(ground)
	root.add_child(decor)
	root.add_child(overlay)
	shell["ground"] = ground
	shell["decor"] = decor
	shell["overlay"] = overlay

	if with_conditional:
		var conditional := _layer("Conditional", tile_set, false)
		root.add_child(conditional)
		shell["conditional"] = conditional

	if with_fog:
		var fog := _layer("Fog", tile_set, false)
		root.add_child(fog)
		shell["fog"] = fog

	var rooms := _group("Rooms", root)
	var markers := _group("Markers", root)
	var characters := _group("Characters", root)
	var enemies := _group("Enemies", root)
	var ysort := _group("YSort", root)
	shell["rooms"] = rooms
	shell["markers"] = markers
	shell["characters"] = characters
	shell["enemies"] = enemies
	shell["ysort"] = ysort

	var camera := Camera2D.new()
	camera.name = "Camera"
	root.add_child(camera)
	shell["camera"] = camera
	return shell


static func _layer(layer_name: String, tile_set: TileSet, y_sort: bool) -> TileMapLayer:
	var layer := TileMapLayer.new()
	layer.name = layer_name
	layer.tile_set = tile_set
	layer.y_sort_enabled = y_sort
	return layer


static func _group(group_name: String, parent: Node) -> Node2D:
	var holder := Node2D.new()
	holder.name = group_name
	holder.y_sort_enabled = true
	parent.add_child(holder)
	return holder


static func folder(parent: Node, folder_name: String) -> Node2D:
	var holder := Node2D.new()
	holder.name = folder_name
	parent.add_child(holder)
	return holder


## 相机对齐地图边界（07 文档节点结构里的 Camera）。
static func fit_camera(camera: Camera2D, map_w: int, map_h: int) -> void:
	camera.limit_left = 0
	camera.limit_top = 0
	camera.limit_right = map_w * TILE_PX
	camera.limit_bottom = map_h * TILE_PX
	camera.position = Vector2(map_w * TILE_PX / 2.0, map_h * TILE_PX / 2.0)


# ---------------------------------------------------------------- Marker 与实体

static func add_marker(parent: Node, marker_name: String, cell: Vector2i) -> Marker2D:
	var marker := Marker2D.new()
	marker.name = marker_name
	marker.position = cell_center(cell)
	parent.add_child(marker)
	return marker


## 大地图的地标坐标直接用表里的 pos_x/pos_y（像素），不做格子吸附。
static func add_marker_at(parent: Node, marker_name: String, pixel: Vector2) -> Marker2D:
	var marker := Marker2D.new()
	marker.name = marker_name
	marker.position = pixel
	parent.add_child(marker)
	return marker


static func add_sprite(parent: Node, sprite_name: String, texture_path: String,
		offset: Vector2 = Vector2.ZERO) -> Sprite2D:
	var sprite := Sprite2D.new()
	sprite.name = sprite_name
	sprite.texture = load(texture_path)
	sprite.position = offset
	parent.add_child(sprite)
	return sprite


static func add_path(parent: Node, path_id: String, points: Array) -> Path2D:
	var path := Path2D.new()
	path.name = path_id
	var curve := Curve2D.new()
	for point: Vector2i in points:
		curve.add_point(cell_center(point))
	path.curve = curve
	parent.add_child(path)
	return path


## 房间：Node2D 挂 Area2D（层 5 房间边界），名字 = Room_<room_id>。
static func add_room(rooms: Node, room_id: String, rect: Rect2i) -> Node2D:
	var room := Node2D.new()
	room.name = "Room_" + room_id
	room.position = Vector2(rect.position.x * TILE_PX, rect.position.y * TILE_PX)
	var area := Area2D.new()
	area.name = "bounds"
	area.collision_layer = LAYER_ROOM
	area.collision_mask = 0
	var shape := CollisionShape2D.new()
	var rect_shape := RectangleShape2D.new()
	rect_shape.size = Vector2(rect.size.x * TILE_PX, rect.size.y * TILE_PX)
	shape.shape = rect_shape
	shape.position = Vector2(rect.size.x * TILE_PX / 2.0, rect.size.y * TILE_PX / 2.0)
	area.add_child(shape)
	room.add_child(area)
	rooms.add_child(room)
	return room


## 明雷警戒圈：Area2D + CircleShape2D，半径 = alert_radius 格 × 32px（07 文档物理层 4）。
static func add_alert(marker: Node2D, radius_cells: float) -> Area2D:
	var area := Area2D.new()
	area.name = "alert"
	area.collision_layer = LAYER_ALERT
	area.collision_mask = 0
	var shape := CollisionShape2D.new()
	var circle := CircleShape2D.new()
	circle.radius = radius_cells * TILE_PX
	shape.shape = circle
	area.add_child(shape)
	marker.add_child(area)
	return area


static func cell_center(cell: Vector2i) -> Vector2:
	return Vector2(cell.x * TILE_PX + TILE_PX / 2.0, cell.y * TILE_PX + TILE_PX / 2.0)


static func accumulated_position(node: Node2D) -> Vector2:
	var pos := node.position
	var parent := node.get_parent()
	while parent != null and parent is Node2D:
		pos += (parent as Node2D).position
		parent = parent.get_parent()
	return pos


# ---------------------------------------------------------------- 地形算法

static func mark_rect(cells: Dictionary, rect: Rect2i) -> void:
	for y in range(rect.position.y, rect.position.y + rect.size.y):
		for x in range(rect.position.x, rect.position.x + rect.size.x):
			cells[Vector2i(x, y)] = true


static func mark_disc(cells: Dictionary, center: Vector2i, radius: int) -> void:
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			if dx * dx + dy * dy <= radius * radius:
				cells[center + Vector2i(dx, dy)] = true


## L 形走廊：先横后竖，宽度给 3 格，保证「至少 2 格可通行」的背袭空间。
static func carve_corridor(cells: Dictionary, a: Rect2i, b: Rect2i, width: int = 3) -> void:
	var start := _rect_center(a)
	var end := _rect_center(b)
	var x0 := mini(start.x, end.x)
	var x1 := maxi(start.x, end.x)
	mark_rect(cells, Rect2i(x0, start.y, x1 - x0 + 1, width))
	var y0 := mini(start.y, end.y)
	var y1 := maxi(start.y, end.y)
	mark_rect(cells, Rect2i(end.x, y0, width, y1 - y0 + 1))


static func _rect_center(rect: Rect2i) -> Vector2i:
	return Vector2i(rect.position.x + rect.size.x / 2, rect.position.y + rect.size.y / 2)


## 自动砌墙：贴着地板一圈、又不属于地板的格子全变墙。省得手画墙线还漏角。
static func wall_shell(floor_cells: Dictionary) -> Dictionary:
	var walls := {}
	for cell: Vector2i in floor_cells:
		for dy in range(-1, 2):
			for dx in range(-1, 2):
				var neighbour := cell + Vector2i(dx, dy)
				if not floor_cells.has(neighbour):
					walls[neighbour] = true
	return walls


## 把土路／空地按四邻挑边缘瓦片，接缝才自然。
static func paint_dirt(layer: TileMapLayer, cells: Dictionary, map_w: int, map_h: int) -> void:
	for y in map_h:
		for x in map_w:
			var cell := Vector2i(x, y)
			if cells.has(cell):
				layer.set_cell(cell, SOURCE_ID, _dirt_tile(cells, cell))


static func _dirt_tile(cells: Dictionary, cell: Vector2i) -> Vector2i:
	var up := not cells.has(cell + Vector2i.UP)
	var down := not cells.has(cell + Vector2i.DOWN)
	var left := not cells.has(cell + Vector2i.LEFT)
	var right := not cells.has(cell + Vector2i.RIGHT)
	if up and left:
		return T_DIRT_TL
	if up and right:
		return T_DIRT_TR
	if down and left:
		return T_DIRT_BL
	if down and right:
		return T_DIRT_BR
	if up:
		return T_DIRT_T
	if down:
		return T_DIRT_B
	if left:
		return T_DIRT_L
	if right:
		return T_DIRT_R
	return T_DIRT


## 户外底子：整张图铺草，再按散列加草簇／花。
static func paint_grass(layer: TileMapLayer, map_w: int, map_h: int) -> void:
	for y in map_h:
		for x in map_w:
			layer.set_cell(Vector2i(x, y), SOURCE_ID, grass_tile(x, y))


static func grass_tile(x: int, y: int) -> Vector2i:
	var roll := hash01(x, y)
	if roll > 0.94:
		return T_GRASS_FLOWER
	if roll > 0.72:
		return T_GRASS_TUFT
	return T_GRASS


static func paint_cells(layer: TileMapLayer, cells: Dictionary, tile: Vector2i) -> void:
	for cell: Vector2i in cells:
		layer.set_cell(cell, SOURCE_ID, tile)


## 山体九宫格：`cells` 是「属于山体」的格子表，按四邻挑边缘瓦片（与土路 `paint_dirt` 同一套用法）。
## 上脊只在最上一排、坡脚只在最下一排——铺出来才是「一座山」，而不是一块块地砖。
static func mountain_tile(cells: Dictionary, cell: Vector2i) -> Vector2i:
	var up := not cells.has(cell + Vector2i.UP)
	var down := not cells.has(cell + Vector2i.DOWN)
	var left := not cells.has(cell + Vector2i.LEFT)
	var right := not cells.has(cell + Vector2i.RIGHT)
	if up and left:
		return T_MTN_TL
	if up and right:
		return T_MTN_TR
	if down and left:
		return T_MTN_BL
	if down and right:
		return T_MTN_BR
	if up:
		return T_MTN_T
	if down:
		return T_MTN_B
	if left:
		return T_MTN_L
	if right:
		return T_MTN_R
	# 一片山会铺几百格：`C`／`C2` 交替才不露「瓦片格子」
	return T_MTN_C2 if hash01(cell.x, cell.y) > 0.5 else T_MTN_C


## 铺一片山（16 §3.5「成片、有厚度」）：九宫格 ＋ 内部稀疏插山峰。
## 山峰只插在**四邻都是山**的内部格、概率也很低——一屏（16×9 格）1–3 座就够，
## 密了会把山面打成筛子（`tools/artgen/README.md` 那条画法教训：整片同色 = 一面灰墙）。
static func paint_mountains(decor: TileMapLayer, cells: Dictionary) -> void:
	for cell: Vector2i in cells:
		if _is_peak_cell(cells, cell):
			var high := hash01(cell.x * 5 + 1, cell.y * 7 + 3) > 0.5
			decor.set_cell(cell, SOURCE_ID, T_MTN_PEAK_HIGH if high else T_MTN_PEAK_LOW)
			continue
		decor.set_cell(cell, SOURCE_ID, mountain_tile(cells, cell))


static func _is_peak_cell(cells: Dictionary, cell: Vector2i) -> bool:
	for offset: Vector2i in [Vector2i.UP, Vector2i.DOWN, Vector2i.LEFT, Vector2i.RIGHT]:
		if not cells.has(cell + offset):
			return false
	return hash01(cell.x + 31, cell.y + 977) < 0.022


## 这一格是不是挡路的（看 TileSet 的自定义数据层 blocked）。
static func is_blocked(layer: TileMapLayer, cell: Vector2i) -> bool:
	if layer == null:
		return false
	var source_id := layer.get_cell_source_id(cell)
	if source_id == -1:
		return false
	var source := layer.tile_set.get_source(source_id)
	if source == null:
		return false
	var data: TileData = source.get_tile_data(layer.get_cell_atlas_coords(cell),
		layer.get_cell_alternative_tile(cell))
	return data != null and bool(data.get_custom_data("blocked"))


## 房间入口内侧的落点：找一个开口，从门口往里退 2 格、再横向错 1 格。
## 这样敌人「进门能看见」，又不正对门口站着挡路（07 文档第五节：别堵住侧向通路）。
static func room_entrance_inside(decor: TileMapLayer, floor_cells: Dictionary, rect: Rect2i) -> Vector2i:
	for probe: Dictionary in _edge_probes(rect):
		var cell: Vector2i = probe["cell"]
		if floor_cells.has(cell) and not is_blocked(decor, cell):
			return cell + (probe["inward"] as Vector2i) * 2 + (probe["side"] as Vector2i)
	return _rect_center(rect)


## 房间四条边上的格子和「往里」的方向，按上→下→左→右顺序探。
static func _edge_probes(rect: Rect2i) -> Array:
	var probes := []
	for x in range(rect.position.x, rect.position.x + rect.size.x):
		probes.append({"cell": Vector2i(x, rect.position.y - 1),
			"inward": Vector2i.DOWN, "side": Vector2i.RIGHT})
	for x in range(rect.position.x, rect.position.x + rect.size.x):
		probes.append({"cell": Vector2i(x, rect.position.y + rect.size.y),
			"inward": Vector2i.UP, "side": Vector2i.RIGHT})
	for y in range(rect.position.y, rect.position.y + rect.size.y):
		probes.append({"cell": Vector2i(rect.position.x - 1, y),
			"inward": Vector2i.RIGHT, "side": Vector2i.DOWN})
	for y in range(rect.position.y, rect.position.y + rect.size.y):
		probes.append({"cell": Vector2i(rect.position.x + rect.size.x, y),
			"inward": Vector2i.LEFT, "side": Vector2i.DOWN})
	return probes


## 自然边界：用密林封边，看起来像山林而不是空气墙。
static func forest_border(decor: TileMapLayer, skip: Dictionary, map_w: int, map_h: int,
		thickness: int = 2) -> void:
	for y in map_h:
		for x in map_w:
			var edge := x < thickness or x >= map_w - thickness or y < thickness or y >= map_h - thickness
			if not edge or skip.has(Vector2i(x, y)):
				continue
			decor.set_cell(Vector2i(x, y), SOURCE_ID, forest_tile(x, y))


static func forest_tile(x: int, y: int) -> Vector2i:
	var roll := hash01(x * 7 + 3, y * 5 + 11)
	if roll > 0.72:
		return T_FOREST_AUTUMN
	if roll > 0.34:
		return T_FOREST
	return T_TREE_PINE


static func tree_tile(x: int, y: int) -> Vector2i:
	var roll := hash01(x * 3 + 1, y * 11 + 7)
	if roll > 0.86:
		return T_MUSHROOM
	if roll > 0.66:
		return T_TREE_AUTUMN
	if roll > 0.46:
		return T_TREE_ROUND
	if roll > 0.30:
		return T_BUSH
	return T_TREE_PINE


static func scatter_trees(decor: TileMapLayer, blocked: Dictionary, reserved: Dictionary,
		map_w: int, map_h: int, chance: float = 0.05) -> void:
	for y in range(2, map_h - 2):
		for x in range(2, map_w - 2):
			var cell := Vector2i(x, y)
			if blocked.has(cell) or reserved.has(cell) or decor.get_cell_source_id(cell) != -1:
				continue
			if hash01(x * 13 + 5, y * 17 + 9) > chance:
				continue
			decor.set_cell(cell, SOURCE_ID, tree_tile(x, y))


static func hash01(x: int, y: int) -> float:
	var h := (x * 374761393 + y * 668265263) & 0x7FFFFFFF
	h = (h ^ (h >> 13)) * 1274126177
	return float(h & 0xFFFF) / 65535.0


# ---------------------------------------------------------------- 建筑

## 门朝下（房子在北侧、门前街道在南侧）：墙在 y，屋顶在 y-2 / y-1。
static func house_down(decor: TileMapLayer, x: int, y: int, style: Dictionary) -> void:
	_house_front(decor, x, y, style)
	for i in 4:
		decor.set_cell(Vector2i(x + i, y - 1), SOURCE_ID, style["roof"])
		decor.set_cell(Vector2i(x + i, y - 2), SOURCE_ID, style["roof_top"])


## 门朝上（房子在南侧）：墙在 y，屋顶摆到 y+1 / y+2。
static func house_up(decor: TileMapLayer, x: int, y: int, style: Dictionary) -> void:
	_house_front(decor, x, y, style)
	for i in 4:
		decor.set_cell(Vector2i(x + i, y + 1), SOURCE_ID, style["roof"])
		decor.set_cell(Vector2i(x + i, y + 2), SOURCE_ID, style["roof_top"])


static func _house_front(decor: TileMapLayer, x: int, y: int, style: Dictionary) -> void:
	decor.set_cell(Vector2i(x, y), SOURCE_ID, style["wall"])
	decor.set_cell(Vector2i(x + 1, y), SOURCE_ID, style["window"])
	decor.set_cell(Vector2i(x + 2, y), SOURCE_ID, style["wall"])
	decor.set_cell(Vector2i(x + 3, y), SOURCE_ID, style["door"])


# ---------------------------------------------------------------- 迷雾

## 铺迷雾：clear 里的格子保持可见（开局已探明的地标周边）。
static func paint_fog(fog: TileMapLayer, map_w: int, map_h: int, clear: Dictionary) -> void:
	for y in map_h:
		for x in map_w:
			var cell := Vector2i(x, y)
			if clear.has(cell):
				continue
			fog.set_cell(cell, PLACEHOLDER_SOURCE_ID, T_FOG)


# ---------------------------------------------------------------- 存盘与预览

static func save_scene(root: Node, path: String) -> int:
	# PackedScene 只会保存 owner 指向根节点的子树，漏设就会得到空场景。
	_assign_owner(root, root)
	var scene := PackedScene.new()
	var pack_error := scene.pack(root)
	if pack_error != OK:
		return pack_error
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path.get_base_dir()))
	return ResourceSaver.save(scene, path)


static func _assign_owner(node: Node, root: Node) -> void:
	for child in node.get_children():
		child.owner = root
		_assign_owner(child, root)


static func dump_preview(scene_path: String, dump_path: String, map_w: int, map_h: int) -> void:
	var scene: PackedScene = load(scene_path)
	if scene == null:
		return
	var root: Node = scene.instantiate()
	var payload := {
		"tile_px": TILE_PX,
		"atlas": _scene_atlas_path(root),
		"sources": [
			{"id": SOURCE_ID, "texture": _scene_atlas_path(root)},
			{"id": PLACEHOLDER_SOURCE_ID, "texture": FOG_ATLAS},
		],
		"map_w": map_w,
		"map_h": map_h,
		"layers": [],
		"markers": [],
		"paths": [],
		"sprites": [],
	}
	_collect(root, payload)
	var file := FileAccess.open(dump_path, FileAccess.WRITE)
	if file != null:
		file.store_string(JSON.stringify(payload))
		file.close()
	root.free()


## 预览脚本要按主题取图集，而这里的入口只有场景对象——所以从场景的 TileSet 里反查。
static func _scene_atlas_path(root: Node) -> String:
	for child in root.get_children():
		if child is TileMapLayer:
			var layer: TileMapLayer = child
			if layer.tile_set == null:
				continue
			var source := layer.tile_set.get_source(SOURCE_ID)
			if source is TileSetAtlasSource and (source as TileSetAtlasSource).texture != null:
				return (source as TileSetAtlasSource).texture.resource_path
	return ""


static func _collect(node: Node, payload: Dictionary) -> void:
	if node is TileMapLayer:
		var layer: TileMapLayer = node
		var cells := []
		for cell: Vector2i in layer.get_used_cells():
			var atlas: Vector2i = layer.get_cell_atlas_coords(cell)
			var source := layer.get_cell_source_id(cell)
			cells.append([cell.x, cell.y, source, atlas.x, atlas.y])
		payload["layers"].append({"name": String(layer.name), "cells": cells})
	elif node is Marker2D:
		var marker: Marker2D = node
		var pos := accumulated_position(marker)
		payload["markers"].append({
			"name": String(marker.name), "pos": [pos.x, pos.y],
		})
	elif node is Path2D:
		var path: Path2D = node
		var points := []
		for index in path.curve.point_count:
			var point: Vector2 = path.curve.get_point_position(index)
			points.append([point.x, point.y])
		payload["paths"].append({"name": String(path.name), "points": points})
	elif node is Sprite2D:
		var sprite: Sprite2D = node
		var texture_path := ""
		if sprite.texture != null:
			texture_path = sprite.texture.resource_path
		var sprite_pos := accumulated_position(sprite)
		payload["sprites"].append({
			"name": String(sprite.name), "pos": [sprite_pos.x, sprite_pos.y], "texture": texture_path,
		})
	for child in node.get_children():
		_collect(child, payload)


# ---------------------------------------------------------------- CSV

## 表里没有带逗号的字段，直接按逗号切就够了；真出现带逗号的字段再换正经解析。
static func read_csv(path: String) -> Array:
	var rows := []
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return rows
	var header := file.get_csv_line()
	if header.size() > 0 and header[0].begins_with("\ufeff"):
		header[0] = header[0].substr(1)
	while not file.eof_reached():
		var line := file.get_csv_line()
		if line.size() < header.size() or line[0].is_empty():
			continue
		var row := {}
		for i in header.size():
			row[header[i]] = line[i]
		rows.append(row)
	return rows


static func rows_where(path: String, column: String, value: String) -> Array:
	var out := []
	for row: Dictionary in read_csv(path):
		if str(row.get(column, "")) == value:
			out.append(row)
	return out
