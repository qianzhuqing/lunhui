## 生成四张小地图：清风驿（城镇）、塌陷山洞、荒村、废弃渡口。
##
## 按 07_地图资源需求.md 第四节；房间／宝箱／触发／判定位点全部来自配置表，
## Marker 名字就是表里的 id（Room_/Chest_/Trigger_/Event_/Portal_）。
##
## 用法：
##   godot --headless --path . --log-file .logs\mapgen_local.log --script res://tools/mapgen/build_local_maps.gd
extends SceneTree

const MapKit := preload("res://tools/mapgen/map_kit.gd")

const LOCAL_CSV := "res://data/tables/map_local.csv"
const ROOM_CSV := "res://data/tables/dungeon_room.csv"
const TRIGGER_CSV := "res://data/tables/hidden_trigger.csv"
const EVENT_CSV := "res://data/tables/event_check.csv"
const BUILDING_CSV := "res://data/tables/building_def.csv"

## 小地图回大地图的出口。07 文档把 Portal_ 绑到 map_local.scene_id（5 个，都是大地图上的入口），
## 回程出口按 07 文档 4.0 用 `Exit_<scene_id>`；废弃渡口本章不可进入，不做出口。
const EXIT_PREFIX := "Exit_"

var _failures: Array[String] = []


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	_build_town()
	_build_cave()
	_build_huangcun()
	_build_ferry()
	if not _failures.is_empty():
		for message: String in _failures:
			printerr("  - " + message)
		return 1
	print("[mapgen] MAPGEN: OK（清风驿 / 塌陷山洞 / 荒村 / 废弃渡口）")
	return 0


# ---------------------------------------------------------------- 清风驿

const TOWN_SCENE := "scene_qingfengyi"
const TOWN_W := 40
const TOWN_H := 30
const TOWN_MAIN_Y := 15
const TOWN_CROSS_X := 19
const TOWN_NORTH_ROW := 14
const TOWN_SOUTH_ROW := 17

## 门面槽位：先北后南、先西后东；表里几间店就占几个槽，其余用街景民居补满。
const TOWN_SLOTS := [
	{"x": 4, "facing": "south", "style": "HOUSE_RED_GRAY"},
	{"x": 10, "facing": "south", "style": "HOUSE_SLATE_WOOD"},
	{"x": 25, "facing": "south", "style": "HOUSE_RED_GRAY"},
	{"x": 31, "facing": "south", "style": "HOUSE_SLATE_GRAY"},
	{"x": 4, "facing": "north", "style": "HOUSE_SLATE_WOOD"},
	{"x": 10, "facing": "north", "style": "HOUSE_SLATE_GRAY"},
	{"x": 25, "facing": "north", "style": "HOUSE_RED_GRAY"},
	{"x": 31, "facing": "north", "style": "HOUSE_SLATE_WOOD"},
]

## 07 文档 4.1 要求的地面设施：表里没有 id 的（当铺／悬赏板／客栈）也要在图上。
const TOWN_FACILITIES := [
	{"name": "facility_pawnshop", "cell": Vector2i(16, 20)},
	{"name": "facility_inn", "cell": Vector2i(24, 20)},
	{"name": "facility_bounty_board", "cell": Vector2i(19, 22)},
]

const TOWN_NPC_SLOTS := [
	Vector2i(14, 17), Vector2i(22, 17), Vector2i(15, 14),
	Vector2i(26, 16), Vector2i(21, 24),
]


func _build_town() -> void:
	var tile_set := MapKit.save_theme_tile_set("town")
	if tile_set == null:
		_failures.append("城镇主题 TileSet 生成失败")
		return
	var shell := MapKit.new_shell(TOWN_SCENE, tile_set)
	var root: Node2D = shell["root"]
	var ground: TileMapLayer = shell["ground"]
	var decor: TileMapLayer = shell["decor"]
	var markers: Node2D = shell["markers"]
	var rooms: Node2D = shell["rooms"]
	var characters: Node2D = shell["characters"]

	# 地面：草地打底，十字街 + 集市石板广场。
	var streets := {}
	MapKit.mark_rect(streets, Rect2i(2, TOWN_MAIN_Y, TOWN_W - 4, 2))
	MapKit.mark_rect(streets, Rect2i(TOWN_CROSS_X, 1, 2, TOWN_H - 2))
	var plaza := {}
	MapKit.mark_rect(plaza, Rect2i(16, 12, 8, 7))
	MapKit.paint_grass(ground, TOWN_W, TOWN_H)
	MapKit.paint_dirt(ground, streets, TOWN_W, TOWN_H)
	MapKit.paint_cells(ground, plaza, MapKit.T_FLOOR)

	# 建筑：表里有几行摆几间。
	var plan := _town_plan()
	for spec: Dictionary in plan:
		_place_house(decor, spec)
	for facility: Dictionary in TOWN_FACILITIES:
		var cell: Vector2i = facility["cell"]
		decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_SIGN)
		decor.set_cell(cell + Vector2i(1, 0), MapKit.SOURCE_ID, MapKit.T_CRATE)

	# 栅栏围镇，南北各留一个口。
	MapKit.mark_rect(streets, Rect2i(TOWN_CROSS_X, TOWN_H - 2, 2, 2))
	_fence_border(decor, streets)
	decor.set_cell(Vector2i(22, 12), MapKit.SOURCE_ID, MapKit.T_WELL)
	decor.set_cell(Vector2i(18, 19), MapKit.SOURCE_ID, MapKit.T_BENCH)
	decor.set_cell(Vector2i(27, 18), MapKit.SOURCE_ID, MapKit.T_BARREL)

	var reserved := {}
	for spec: Dictionary in plan:
		reserved[_front_cell(spec)] = true
	for facility: Dictionary in TOWN_FACILITIES:
		reserved[facility["cell"]] = true
	for cell: Vector2i in TOWN_NPC_SLOTS:
		reserved[cell] = true
	reserved[Vector2i(TOWN_CROSS_X, TOWN_H - 3)] = true
	MapKit.scatter_trees(decor, streets, reserved, TOWN_W, TOWN_H, 0.04)

	# 店招牌挂在门前街上；表里没有 id 的设施用 facility_ 前缀，不装成表主键。
	var buildings := MapKit.folder(markers, "Buildings")
	for spec: Dictionary in plan:
		var building_id := str(spec["id"])
		if building_id.is_empty():
			continue
		MapKit.add_marker(buildings, building_id, _front_cell(spec))

	# 07 文档 4.1 要求的三处无表设施：节点名挂 facility_ 前缀，别装成表主键。
	var facilities := MapKit.folder(markers, "Facilities")
	for facility: Dictionary in TOWN_FACILITIES:
		MapKit.add_marker(facilities, str(facility["name"]), facility["cell"] + Vector2i(0, 1))

	# 赌局位点（07 文档 §6：清风驿·客栈）。
	MapKit.add_marker(markers, "Event_ev_gamble", Vector2i(24, 19))
	# 城镇没有 Room_（dungeon_room 里没有城镇行），出口直接挂在 Markers 下。
	MapKit.add_marker(markers, EXIT_PREFIX + TOWN_SCENE, Vector2i(TOWN_CROSS_X, TOWN_H - 3))
	_place_npcs(characters)
	_fit(shell, TOWN_W, TOWN_H)
	# 城镇的「1 个房间」在 dungeon_room.csv 里没有 id，所以没有 Room_ 标记（已回报）。
	print("[mapgen] 清风驿：店铺 %d / 街景 %d / 设施 %d / NPC %d" % [
		plan.size() - _scenery_count(plan), _scenery_count(plan), TOWN_FACILITIES.size(), TOWN_NPC_SLOTS.size(),
	])
	_finish(root, TOWN_SCENE, TOWN_W, TOWN_H, streets, ground, decor)


func _town_plan() -> Array:
	var rows := MapKit.read_csv(BUILDING_CSV)
	# 只有 `building_type=shop` 的行占门面槽位。**服务类不占**——`bld_dummy`（木桩）要由地编
	# 摆到校场（A6），把它当第 5 间店塞进北排民居会挂着一块「木桩」招牌，
	# 也会和 `test_local_map` 的临时位点用例撞成重名（2026-10-04，见 `框架说明.md` 决策 269）。
	var shops := []
	for row: Dictionary in rows:
		if str(row.get("building_type", "")) == "shop":
			shops.append(row)
	if shops.size() > TOWN_SLOTS.size():
		_failures.append("building_def.csv 有 %d 间店，门面槽位只有 %d 个" % [shops.size(), TOWN_SLOTS.size()])
		return []
	var plan := []
	for index in TOWN_SLOTS.size():
		var slot: Dictionary = TOWN_SLOTS[index]
		plan.append({
			"id": str(shops[index]["building_id"]) if index < shops.size() else "",
			"x": slot["x"], "facing": slot["facing"], "style": slot["style"],
		})
	return plan


func _scenery_count(plan: Array) -> int:
	var count := 0
	for spec: Dictionary in plan:
		if str(spec["id"]).is_empty():
			count += 1
	return count


func _place_house(decor: TileMapLayer, spec: Dictionary) -> void:
	var style := _style(str(spec["style"]))
	if str(spec["facing"]) == "south":
		MapKit.house_down(decor, int(spec["x"]), TOWN_NORTH_ROW, style)
	else:
		MapKit.house_up(decor, int(spec["x"]), TOWN_SOUTH_ROW, style)


func _style(style_name: String) -> Dictionary:
	match style_name:
		"HOUSE_RED_GRAY":
			return MapKit.HOUSE_RED_GRAY
		"HOUSE_SLATE_GRAY":
			return MapKit.HOUSE_SLATE_GRAY
		_:
			return MapKit.HOUSE_SLATE_WOOD


## 标记落在门前可交互的那一格（街上），不压在门瓦片上。
func _front_cell(spec: Dictionary) -> Vector2i:
	var x := int(spec["x"]) + 3
	if str(spec["facing"]) == "south":
		return Vector2i(x, TOWN_NORTH_ROW + 1)
	return Vector2i(x, TOWN_SOUTH_ROW - 1)


func _fence_border(decor: TileMapLayer, skip: Dictionary) -> void:
	for x in TOWN_W:
		for cell in [Vector2i(x, 1), Vector2i(x, TOWN_H - 2)]:
			if not skip.has(cell):
				decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	for y in TOWN_H:
		for cell in [Vector2i(1, y), Vector2i(TOWN_W - 2, y)]:
			if not skip.has(cell):
				decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_FENCE_V)


func _place_npcs(characters: Node2D) -> void:
	for index in TOWN_NPC_SLOTS.size():
		var marker := MapKit.add_marker(characters, "npc_slot_%02d" % (index + 1), TOWN_NPC_SLOTS[index])
		MapKit.add_sprite(marker, "placeholder", MapKit.NPC_TEXTURE)
	var spawn := MapKit.add_marker(characters, "player_spawn", Vector2i(TOWN_CROSS_X, TOWN_H - 3))
	spawn.set_meta("role", "player")


# ---------------------------------------------------------------- 塌陷山洞

const CAVE_SCENE := "scene_cave"
const CAVE_W := 32
const CAVE_H := 24
const CAVE_ROOMS := [
	{"id": "cave_01", "rect": Rect2i(3, 8, 10, 8)},
	{"id": "cave_02", "rect": Rect2i(18, 7, 12, 10)},
]


func _build_cave() -> void:
	var shell := _dungeon_shell(CAVE_SCENE, "cave", CAVE_W, CAVE_H, CAVE_ROOMS, [["cave_01", "cave_02"]])
	if shell.is_empty():
		return
	# 洞口通大地图；挖通后另一边直通黑风寨后山地牢。
	_room_marker(shell, "Markers", "cave_01", EXIT_PREFIX + CAVE_SCENE, Vector2i(5, 15))
	MapKit.add_marker(shell["markers"], "Portal_scene_heifengzhai", Vector2i(28, 12))
	_room_marker(shell, "Markers", "cave_02", "Trigger_trig_dig", Vector2i(24, 12))
	_room_marker(shell, "Markers", "cave_02", "Event_ev_grave_epitaph", Vector2i(21, 9))
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(5, 12)).set_meta("role", "player")
	_fit(shell, CAVE_W, CAVE_H)
	_finish(shell["root"], CAVE_SCENE, CAVE_W, CAVE_H)
	print("[mapgen] 塌陷山洞：房间 2（cave_01/cave_02）")


# ---------------------------------------------------------------- 荒村

const VILLAGE_SCENE := "scene_huangcun"
const VILLAGE_W := 40
const VILLAGE_H := 30
const VILLAGE_ROOMS := [
	{"id": "hc_01", "rect": Rect2i(3, 12, 12, 8)},
	{"id": "hc_02", "rect": Rect2i(22, 10, 14, 11)},
]


func _build_huangcun() -> void:
	var shell := _dungeon_shell(VILLAGE_SCENE, "village", VILLAGE_W, VILLAGE_H, VILLAGE_ROOMS,
		[["hc_01", "hc_02"]])
	if shell.is_empty():
		return
	_room_marker(shell, "Markers", "hc_01", EXIT_PREFIX + VILLAGE_SCENE, Vector2i(4, 19))
	_room_marker(shell, "Markers", "hc_02", "Chest_drop_chest_silver", Vector2i(33, 17))
	_team_marker(shell, "hc_02", "team_butcher")
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(4, 16)).set_meta("role", "player")
	_fit(shell, VILLAGE_W, VILLAGE_H)
	_finish(shell["root"], VILLAGE_SCENE, VILLAGE_W, VILLAGE_H)
	print("[mapgen] 荒村：房间 2（hc_01/hc_02）")


# ---------------------------------------------------------------- 废弃渡口

const FERRY_SCENE := "scene_ferry_locked"
const FERRY_W := 32
const FERRY_H := 24


func _build_ferry() -> void:
	var tile_set := MapKit.save_theme_tile_set("village")
	if tile_set == null:
		_failures.append("村落主题 TileSet 生成失败")
		return
	var shell := MapKit.new_shell(FERRY_SCENE, tile_set)
	var ground: TileMapLayer = shell["ground"]
	var decor: TileMapLayer = shell["decor"]
	var streets := {}
	MapKit.mark_rect(streets, Rect2i(4, 12, 20, 6))
	MapKit.paint_grass(ground, FERRY_W, FERRY_H)
	MapKit.paint_dirt(ground, streets, FERRY_W, FERRY_H)
	# 本章不开放：牌坊 + 破船 + 断桥封锁，一眼看出过不去。
	for offset in range(-2, 3):
		decor.set_cell(Vector2i(12 + offset, 10), MapKit.SOURCE_ID,
			MapKit.T_WALL_TOP_CORNER if abs(offset) == 2 else MapKit.T_BATTLEMENT)
	decor.set_cell(Vector2i(12, 11), MapKit.SOURCE_ID, MapKit.T_ARCH)
	decor.set_cell(Vector2i(20, 13), MapKit.SOURCE_ID, MapKit.T_BARREL)
	decor.set_cell(Vector2i(21, 14), MapKit.SOURCE_ID, MapKit.T_CRATE)
	for x in range(4, 12):
		decor.set_cell(Vector2i(x, 15), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(Vector2i(16, 16), MapKit.SOURCE_ID, MapKit.T_SIGN)
	MapKit.forest_border(decor, streets, FERRY_W, FERRY_H, 2)
	# 本章不可进入，按 07 文档 4.0 不做回程出口。
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(5, 14)).set_meta("role", "player")
	_fit(shell, FERRY_W, FERRY_H)
	_finish(shell["root"], FERRY_SCENE, FERRY_W, FERRY_H, streets, ground, decor)
	print("[mapgen] 废弃渡口：1 区域（本章不开放，做封锁表现）")


# ---------------------------------------------------------------- 副本通用

func _dungeon_shell(scene_id: String, theme: String, map_w: int, map_h: int,
		room_specs: Array, links: Array) -> Dictionary:
	var tile_set := MapKit.save_theme_tile_set(theme)
	if tile_set == null:
		_failures.append("%s 主题 TileSet 生成失败" % theme)
		return {}
	var shell := MapKit.new_shell(scene_id, tile_set)
	var floor_cells := {}
	var rects := {}
	for spec: Dictionary in room_specs:
		MapKit.mark_rect(floor_cells, spec["rect"])
		rects[spec["id"]] = spec["rect"]
	for link: Array in links:
		MapKit.carve_corridor(floor_cells, rects[link[0]], rects[link[1]], 3)
	# 地板与墙一起算，墙贴着地板外圈自动生成，不会漏角。
	MapKit.paint_cells(shell["ground"], floor_cells, MapKit.T_FLOOR)
	MapKit.paint_cells(shell["decor"], MapKit.wall_shell(floor_cells), MapKit.T_BATTLEMENT)
	for spec: Dictionary in room_specs:
		MapKit.add_room(shell["rooms"], str(spec["id"]), spec["rect"])
	shell["floor"] = floor_cells
	return shell


## 副本房间内的挂钩点：在 Markers 下按 room_id 分文件夹，避免同名 id（两个银箱）撞车。
func _room_marker(shell: Dictionary, _group: String, room_id: String, marker_name: String,
		cell: Vector2i) -> void:
	var markers: Node2D = shell["markers"]
	var holder: Node2D = markers.get_node_or_null(room_id)
	if holder == null:
		holder = MapKit.folder(markers, room_id)
	MapKit.add_marker(holder, marker_name, cell)


## 固定敌人：`Enemies/<room_id>/Team_<team_id>`（07 文档第二节，唯一带父目录的约定）。
## 落点放在房间入口内侧，看得见但不堵门。
func _team_marker(shell: Dictionary, room_id: String, team_id: String) -> void:
	var enemies: Node2D = shell["enemies"]
	var holder: Node2D = enemies.get_node_or_null(room_id)
	if holder == null:
		holder = MapKit.folder(enemies, room_id)
	var cell: Vector2i = MapKit.room_entrance_inside(shell["decor"], shell["floor"], _room_rect(room_id))
	MapKit.add_marker(holder, "Team_" + team_id, cell)


func _room_rect(room_id: String) -> Rect2i:
	for spec: Dictionary in CAVE_ROOMS + VILLAGE_ROOMS:
		if str(spec["id"]) == room_id:
			return spec["rect"]
	return Rect2i()


func _fit(shell: Dictionary, map_w: int, map_h: int) -> void:
	MapKit.fit_camera(shell["camera"], map_w, map_h)


## 存档 + 出预览数据；local 参数只在需要统计时给。
func _finish(root: Node2D, scene_id: String, map_w: int, map_h: int, _streets: Dictionary = {},
		_ground: TileMapLayer = null, _decor: TileMapLayer = null) -> void:
	var path := "res://scenes/maps/%s.tscn" % scene_id
	var save_error := MapKit.save_scene(root, path)
	if save_error != OK:
		_failures.append("保存 %s 失败：%d" % [path, save_error])
		return
	root.free()
	MapKit.dump_preview(path, "res://.logs/%s_preview.json" % scene_id, map_w, map_h)
