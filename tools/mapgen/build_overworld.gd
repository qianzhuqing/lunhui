## 生成大地图 scenes/maps/overworld.tscn。
##
## 按 07_地图资源需求.md 第三节：
##   画布 1024×768 px（32×24 格，32px 瓦片）；地标坐标**直接用 map_region.pos_x/pos_y**（像素，不吸附格子）。
##   Marker：Node_×7、Portal_×5、Spawn_×17、Patrol_×4、Event_×7。
##
## 用法：
##   godot --headless --path . --log-file .logs\mapgen_overworld.log --script res://tools/mapgen/build_overworld.gd
extends SceneTree

const MapKit := preload("res://tools/mapgen/map_kit.gd")

const SCENE_PATH := "res://scenes/maps/overworld.tscn"
const DUMP_PATH := "res://.logs/overworld_preview.json"
const REGION_CSV := "res://data/tables/map_region.csv"
const SPAWN_CSV := "res://data/tables/roaming_spawn.csv"
const ICON_DIR := "res://assets/sprites/icons/"
const PARENT_REGION := "jiangnan_east"

const MAP_W := 32
const MAP_H := 24

## 开局就探明的范围（格）：reveal_on_map = 1 的地标周边。
const REVEAL_RADIUS := 5

## 地标之间的通行关系（设计文档 02：清风驿是枢纽，落雁坡是野外中枢）。
const ROADS := [
	["n_qingfengyi", "n_post_station"],
	["n_post_station", "n_heifengzhai"],
	["n_qingfengyi", "n_luoyanpo"],
	["n_luoyanpo", "n_cave_collapse"],
	["n_luoyanpo", "n_huangcun"],
	["n_qingfengyi", "n_ferry_abandoned"],
]

## 明雷刷新点：相对所属地标的格子偏移（表里没有坐标，属于地图数据）。
## 硬性要求：清风驿一圈是安全区，所以落雁坡/黑风寨外围/荒村的点都离它很远。
const SPAWN_OFFSETS := {
	"sp_lp_wolf_01": Vector2i(-4, -2),
	"sp_lp_wolf_02": Vector2i(-2, 2),
	"sp_lp_wolf_03": Vector2i(2, -2),
	"sp_lp_boar_01": Vector2i(4, 2),
	"sp_lp_patrol_01": Vector2i(-2, -4),
	"sp_lp_patrol_02": Vector2i(3, -4),
	"sp_lp_lonewolf": Vector2i(0, 2),
	"sp_lp_herbalist": Vector2i(-5, 0),
	"sp_hf_gate_01": Vector2i(-3, -2),
	"sp_hf_gate_02": Vector2i(-2, -1),
	"sp_hf_tower": Vector2i(2, -3),
	"sp_hf_relief_01": Vector2i(-1, 2),
	"sp_hf_relief_02": Vector2i(2, 2),
	"sp_hc_brigand_01": Vector2i(-2, -2),
	"sp_hc_brigand_02": Vector2i(2, -2),
	"sp_hc_butcher": Vector2i(-1, 0),
	"sp_hc_sleeper": Vector2i(1, -1),
}

## 明雷归属：region_id -> 地标 node_id。
const REGION_NODE := {
	"n_luoyanpo": "n_luoyanpo",
	"n_heifengzhai": "n_heifengzhai",
	"n_huangcun": "n_huangcun",
}

## 巡逻路线（4 条，名字对应 roaming_spawn.patrol_path_id），用相对地标的偏移写。
const PATROL_PATHS := {
	"path_lp_a": {"anchor": "n_luoyanpo",
		"points": [Vector2i(-4, -5), Vector2i(-1, -6), Vector2i(2, -5), Vector2i(-1, -3)]},
	"path_lp_b": {"anchor": "n_luoyanpo",
		"points": [Vector2i(2, -5), Vector2i(5, -6), Vector2i(6, -3), Vector2i(3, -2)]},
	"path_hf_a": {"anchor": "n_heifengzhai",
		"points": [Vector2i(-4, -3), Vector2i(-2, -4), Vector2i(1, -3), Vector2i(-2, -1)]},
	"path_hf_b": {"anchor": "n_heifengzhai",
		"points": [Vector2i(2, -2), Vector2i(5, -3), Vector2i(4, -1), Vector2i(1, 0)]},
}

## 大地图判定位点（07 文档第三节 7 处）：相对地标的偏移。
const EVENT_SITES := {
	"ev_wild_track": {"anchor": "n_luoyanpo", "offset": Vector2i(-3, 1)},
	# 与 Spawn_sp_lp_herbalist 同一人，故意压在同一个格子上。
	"ev_herbalist_help": {"anchor": "n_luoyanpo", "offset": Vector2i(-5, 0)},
	"ev_night_watch": {"anchor": "n_luoyanpo", "offset": Vector2i(-1, 4)},
	"ev_bandit_parley": {"anchor": "n_huangcun", "offset": Vector2i(-3, -1)},
	"ev_grave_epitaph": {"anchor": "n_cave_collapse", "offset": Vector2i(0, -1)},
	"ev_climb_wall": {"anchor": "n_heifengzhai", "offset": Vector2i(3, -1)},
	"ev_force_gate": {"anchor": "n_heifengzhai", "offset": Vector2i(0, -1)},
}


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	var nodes := _region_nodes()
	if nodes.size() != 7:
		printerr("[mapgen] %s 的地标应有 7 个，实际 %d 个" % [PARENT_REGION, nodes.size()])
		return 1
	var tile_set := MapKit.save_theme_tile_set("jiangnan_wild")
	if tile_set == null:
		printerr("[mapgen] TileSet 生成失败：检查 assets/tilesets/_common/ 的图集是否已导入")
		return 1

	var shell := MapKit.new_shell("overworld", tile_set, true)
	var root: Node2D = shell["root"]
	var ground: TileMapLayer = shell["ground"]
	var decor: TileMapLayer = shell["decor"]
	var fog: TileMapLayer = shell["fog"]
	var markers: Node2D = shell["markers"]

	var reserved := _reserved_cells(nodes)
	var roads := _plan_roads(nodes)
	MapKit.paint_grass(ground, MAP_W, MAP_H)
	MapKit.paint_dirt(ground, roads, MAP_W, MAP_H)
	_paint_safe_zone(decor, nodes)
	# 密林既不能压住路，也不能压住明雷／判定位点／地标本身。
	var keep_clear := roads.duplicate()
	for cell: Vector2i in reserved:
		keep_clear[cell] = true
	for node_id: String in nodes:
		keep_clear[nodes[node_id]["cell"]] = true
	MapKit.forest_border(decor, keep_clear, MAP_W, MAP_H, 2)
	MapKit.scatter_trees(decor, roads, reserved, MAP_W, MAP_H, 0.05)
	MapKit.paint_fog(fog, MAP_W, MAP_H, _plan_fog(nodes))
	_place_markers(markers, nodes)
	MapKit.fit_camera(shell["camera"], MAP_W, MAP_H)

	var save_error := MapKit.save_scene(root, SCENE_PATH)
	if save_error != OK:
		printerr("[mapgen] 保存场景失败：%d" % save_error)
		return 1
	var counts := {
		"ground": ground.get_used_cells().size(),
		"decor": decor.get_used_cells().size(),
		"fog": fog.get_used_cells().size(),
	}
	root.free()
	MapKit.dump_preview(SCENE_PATH, DUMP_PATH, MAP_W, MAP_H)
	print("[mapgen] 大地图：地标 7 / 明雷 %d / 巡逻线 %d / 判定位点 %d" % [
		SPAWN_OFFSETS.size(), PATROL_PATHS.size(), EVENT_SITES.size(),
	])
	print("[mapgen] 瓦片用量 ground=%d decor=%d fog=%d（上限 5000）" % [
		counts["ground"], counts["decor"], counts["fog"],
	])
	print("[mapgen] MAPGEN: OK -> %s" % SCENE_PATH)
	return 0


## 地标坐标直接用表里的像素坐标（07 文档：1024×768 画布，pos_x/pos_y 即像素）。
func _region_nodes() -> Dictionary:
	var out := {}
	for row: Dictionary in MapKit.rows_where(REGION_CSV, "parent_region", PARENT_REGION):
		var node_id := str(row["node_id"])
		out[node_id] = {
			"pixel": Vector2(float(row["pos_x"]), float(row["pos_y"])),
			"cell": Vector2i(int(float(row["pos_x"])) / MapKit.TILE_PX,
				int(float(row["pos_y"])) / MapKit.TILE_PX),
			"icon": str(row["icon"]),
			"reveal": str(row.get("reveal_on_map", "0")) == "1",
		}
	return out


func _plan_roads(nodes: Dictionary) -> Dictionary:
	var roads := {}
	for edge: Array in ROADS:
		var a: Rect2i = Rect2i(nodes[edge[0]]["cell"], Vector2i.ONE)
		var b: Rect2i = Rect2i(nodes[edge[1]]["cell"], Vector2i.ONE)
		MapKit.carve_corridor(roads, a, b, 2)
	for node_id: String in nodes:
		MapKit.mark_disc(roads, nodes[node_id]["cell"], 1)
	return roads


## 明雷、判定位点、巡逻路线都不能被树压住。
func _reserved_cells(nodes: Dictionary) -> Dictionary:
	var reserved := {}
	for spawn_id: String in SPAWN_OFFSETS:
		reserved[_spawn_cell(nodes, spawn_id)] = true
	for path_id: String in PATROL_PATHS:
		var anchor: Vector2i = nodes[PATROL_PATHS[path_id]["anchor"]]["cell"]
		for offset: Vector2i in PATROL_PATHS[path_id]["points"]:
			reserved[anchor + offset] = true
	for event_id: String in EVENT_SITES:
		reserved[_event_cell(nodes, event_id)] = true
	return reserved


func _spawn_cell(nodes: Dictionary, spawn_id: String) -> Vector2i:
	var region := _spawn_region(spawn_id)
	var anchor: Vector2i = nodes[REGION_NODE[region]]["cell"]
	return anchor + SPAWN_OFFSETS[spawn_id]


func _spawn_region(spawn_id: String) -> String:
	for row: Dictionary in MapKit.read_csv(SPAWN_CSV):
		if str(row["spawn_id"]) == spawn_id:
			return str(row["region_id"])
	return ""


func _event_cell(nodes: Dictionary, event_id: String) -> Vector2i:
	var site: Dictionary = EVENT_SITES[event_id]
	var anchor: Vector2i = nodes[site["anchor"]]["cell"]
	return anchor + site["offset"]


func _plan_fog(nodes: Dictionary) -> Dictionary:
	var clear := {}
	for node_id: String in nodes:
		if nodes[node_id]["reveal"]:
			MapKit.mark_disc(clear, nodes[node_id]["cell"], REVEAL_RADIUS)
	return clear


## 大地图是**图标制**（07 文档第三节：地标用图标表现，不摆建筑）。
## 只有清风驿要额外做「一圈安全区」的表现——不刷明雷的安全感靠地面给。
func _paint_safe_zone(decor: TileMapLayer, nodes: Dictionary) -> void:
	var cell: Vector2i = nodes["n_qingfengyi"]["cell"]
	decor.set_cell(cell + Vector2i(-2, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(cell + Vector2i(-1, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	decor.set_cell(cell + Vector2i(2, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(cell + Vector2i(3, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	decor.set_cell(cell + Vector2i(0, 2), MapKit.SOURCE_ID, MapKit.T_SIGN)
	decor.set_cell(cell + Vector2i(2, 2), MapKit.SOURCE_ID, MapKit.T_WELL)


func _place_markers(markers: Node2D, nodes: Dictionary) -> void:
	for node_id: String in nodes:
		var data: Dictionary = nodes[node_id]
		var marker := MapKit.add_marker_at(markers, "Node_" + node_id, data["pixel"])
		MapKit.add_sprite(marker, "icon", ICON_DIR + data["icon"] + ".png")

	for row: Dictionary in MapKit.rows_where(REGION_CSV, "parent_region", PARENT_REGION):
		var scene_id := str(row.get("enter_scene", ""))
		if scene_id.is_empty():
			continue
		MapKit.add_marker_at(markers, "Portal_" + scene_id, nodes[str(row["node_id"])]["pixel"])

	for row: Dictionary in MapKit.read_csv(SPAWN_CSV):
		var spawn_id := str(row["spawn_id"])
		if not SPAWN_OFFSETS.has(spawn_id):
			continue
		var marker := MapKit.add_marker(markers, "Spawn_" + spawn_id, _spawn_cell(nodes, spawn_id))
		var alert := float(row.get("alert_radius", "0"))
		if alert > 0.0:
			MapKit.add_alert(marker, alert)

	for path_id: String in PATROL_PATHS:
		var anchor: Vector2i = nodes[PATROL_PATHS[path_id]["anchor"]]["cell"]
		var points := []
		for offset: Vector2i in PATROL_PATHS[path_id]["points"]:
			points.append(anchor + offset)
		MapKit.add_path(markers, "Patrol_" + path_id, points)

	for event_id: String in EVENT_SITES:
		MapKit.add_marker(markers, "Event_" + event_id, _event_cell(nodes, event_id))
