## 生成大地图 scenes/maps/overworld.tscn。
##
## 按 07_地图资源需求.md 第三节（0.31.2 扩容 ＋ 0.32.0 探索口径）：
##   画布 **2048×1536 px（64×48 格）**；地标坐标**直接用 map_region.pos_x/pos_y**（像素，不吸附格子）。
##   图层 Ground／Decor／Overlay／**Conditional**（条件地表）／Fog——0.32.0 起第 5 层。
##   Marker：Node_×8、Portal_×6、Sign_×4、Spawn_×17、Patrol_×4、Event_×8、Observe_×3。
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
## 大地图上的地标数（设计侧加一个区域，这一行跟着改）。
const NODE_COUNT := 8

## 画布 2048×1536 px（64×48 格）——0.31.2 实机反馈「太小」之后从 32×24 扩到 4 倍面积。
const MAP_W := 64
const MAP_H := 48

## 开局就探明的范围（格）：`reveal_on_map = 1` 的地标周边。
## **0.32.0 起只有清风驿（城镇）与驿站默认公开**——其余全在雾里，要玩家自己走过去才出现。
const REVEAL_RADIUS := 6

## 道路分两级（16 §3.4 / A13）：主路一条最宽，支路窄一档，不要第三条档。
const MAIN_ROAD_WIDTH := 3
const BRANCH_WIDTH := 2

## 路网（名单以 16 §3.4 为准）。`via` 是拐点：`carve_corridor` 只会画 L 形，
## 多给几个拐点才画得出「点连着点」的连续路面（0.31.2：不能靠草地凑合能走）。
##
## 注意 07 §3.2 与旧交接单里写的「清风驿→废弃渡口」是漂移，**驿站→废弃渡口**才对。
const ROADS := [
	# 主路：清风驿 → 驿站（沿 y=32 往东，再北上）
	{"width": MAIN_ROAD_WIDTH, "nodes": ["n_qingfengyi", "n_post_station"], "via": [Vector2i(25, 32)]},
	# 主路：驿站 → 落雁坡（沿 y=25 往东，再南下）
	{"width": MAIN_ROAD_WIDTH, "nodes": ["n_post_station", "n_luoyanpo"], "via": [Vector2i(39, 25)]},
	# 主路：落雁坡 → 黑风寨（沿 y=34 往东，再北上——黑风寨是从落雁坡「发现」出来的）
	{"width": MAIN_ROAD_WIDTH, "nodes": ["n_luoyanpo", "n_heifengzhai"], "via": [Vector2i(54, 34)]},
	# 支路：落雁坡 → 塌陷山洞（南下进山，洞口在路的尽头）
	{"width": BRANCH_WIDTH, "nodes": ["n_luoyanpo", "n_cave_collapse"], "via": [Vector2i(43, 34)]},
	# 支路：落雁坡 → 荒村（往东）
	{"width": BRANCH_WIDTH, "nodes": ["n_luoyanpo", "n_huangcun"], "via": [Vector2i(57, 34)]},
	# 支路：驿站 → 废弃渡口（往西南，绕开清风驿那一圈）
	{"width": BRANCH_WIDTH, "nodes": ["n_post_station", "n_ferry_abandoned"],
		"via": [Vector2i(16, 25), Vector2i(16, 42)]},
]

## 石隙迷窟那条支路（16 §3.4 的最后一条）：**1 格宽**、走碎石草地而不是土路
## （「藏宝图上的一条线」），而且**整条铺在 0.32.0 新增的 `Conditional` 层上**——
## 程序按 `item_treasure_map` 整层显隐：没拿到图之前，这条线在地图上不存在。
const SHIXI_TRAIL := {"nodes": ["n_luoyanpo", "n_shixi"], "via": [Vector2i(23, 34)], "width": 1}

## 山体区块（格）：**成片是硬要求**（16 §3.5）——孤立一格山在 2× 下就是一块石头，
## 一层山至少铺 2–3 屏（相机 2× 时一屏约 16×9 格）。
const MOUNTAIN_BLOCKS := [
	Rect2i(30, 0, 34, 16),    # 北岭：横贯东北，罩在黑风寨背后
	Rect2i(34, 37, 20, 10),   # 塌陷山洞那一片山（洞口嵌在山体里）
	Rect2i(12, 37, 20, 10),   # 石隙迷窟那一片山（岩缝嵌在山体里）
	Rect2i(0, 2, 22, 12),     # 西北小岭
	Rect2i(0, 24, 8, 12),     # 西缘中段，把清风驿西南封住
]

## 废弃渡口那片水（格）：外缘的自然边界。深水挡路（07 §二：水不能穿过），
## 浅水／水岸／芦苇不挡路——芦苇荡要能钻进去侦察（16 §4.4）。
const WATER_RECT := Rect2i(0, 44, 21, 4)

## 明雷刷新点：相对所属地标的格子偏移（表里没有坐标，属于地图数据）。
## 硬性要求：清风驿一圈是安全区，所以落雁坡／黑风寨外围／荒村的点都离它很远。
const SPAWN_OFFSETS := {
	"sp_lp_wolf_01": Vector2i(-9, -4),
	"sp_lp_wolf_02": Vector2i(-8, 2),
	"sp_lp_wolf_03": Vector2i(-4, -6),
	"sp_lp_boar_01": Vector2i(5, -5),
	"sp_lp_patrol_01": Vector2i(-3, -6),
	"sp_lp_patrol_02": Vector2i(4, -6),
	"sp_lp_lonewolf": Vector2i(-7, 3),
	"sp_lp_herbalist": Vector2i(-11, 0),
	"sp_hf_gate_01": Vector2i(-3, -2),
	"sp_hf_gate_02": Vector2i(-2, -1),
	"sp_hf_tower": Vector2i(2, -3),
	"sp_hf_relief_01": Vector2i(-4, 2),
	"sp_hf_relief_02": Vector2i(3, 2),
	"sp_hc_brigand_01": Vector2i(-3, -2),
	"sp_hc_brigand_02": Vector2i(-1, -3),
	"sp_hc_butcher": Vector2i(-3, 1),
	"sp_hc_sleeper": Vector2i(-1, 3),
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
		"points": [Vector2i(-8, -4), Vector2i(-5, -6), Vector2i(-2, -4), Vector2i(-5, -2)]},
	"path_lp_b": {"anchor": "n_luoyanpo",
		"points": [Vector2i(2, -5), Vector2i(5, -6), Vector2i(6, -4), Vector2i(3, -3)]},
	"path_hf_a": {"anchor": "n_heifengzhai",
		"points": [Vector2i(-6, 2), Vector2i(-4, 0), Vector2i(-1, 2), Vector2i(-4, 4)]},
	"path_hf_b": {"anchor": "n_heifengzhai",
		"points": [Vector2i(2, 3), Vector2i(5, 2), Vector2i(4, 5), Vector2i(1, 5)]},
}

## 大地图判定位点（07 第三节 8 处）：相对地标的偏移。
const EVENT_SITES := {
	"ev_wild_track": {"anchor": "n_luoyanpo", "offset": Vector2i(-9, 1)},
	# 与 Spawn_sp_lp_herbalist 同一人，故意压在同一个格子上。
	"ev_herbalist_help": {"anchor": "n_luoyanpo", "offset": Vector2i(-11, 0)},
	"ev_night_watch": {"anchor": "n_luoyanpo", "offset": Vector2i(4, -3)},
	"ev_bandit_parley": {"anchor": "n_huangcun", "offset": Vector2i(-3, -1)},
	"ev_grave_epitaph": {"anchor": "n_cave_collapse", "offset": Vector2i(0, -1)},
	"ev_climb_wall": {"anchor": "n_heifengzhai", "offset": Vector2i(3, -1)},
	"ev_force_gate": {"anchor": "n_heifengzhai", "offset": Vector2i(0, -1)},
	# 镇抚司巡查的关卡：驿站南侧官道上（07 §三／§十一 第 12 条）。
	# 它同时是随机事件 we_patrol 的判定目标，但图上仍要有这个位点——玩家走到关卡也能按 E 触发。
	"ev_patrol_check": {"anchor": "n_post_station", "offset": Vector2i(-2, 1)},
}

## 观察点（0.29.1 的 20_第一章剧情）：`Observe_<point_id>`，围绕官道南侧的旧镖车。
## 名字按设计给的形如 `Observe_ob_<地点>_<物>_<序号>`；`flavor_point` 表里就是这三条。
const OBSERVE_OFFSETS := {
	"ob_luoyanpo_cart_01": Vector2i(0, 1),   # 旧镖车的车辙
	"ob_luoyanpo_cart_02": Vector2i(-2, 0),  # 褪色的镖旗
	"ob_luoyanpo_cart_03": Vector2i(2, 0),   # 车斗里的灰
}

## 指路牌（0.31.2 新增）：**主路 4 个节点**附近各一块，位点 `Sign_<node_id>`。
## 文案在 `map_region.signpost_cn`（表里没填就不显示字），牌子本身摆在这里。
const SIGNPOST_NODES := ["n_qingfengyi", "n_post_station", "n_luoyanpo", "n_heifengzhai"]
const SIGNPOST_OFFSETS := {
	"n_qingfengyi": Vector2i(-3, -1),
	"n_post_station": Vector2i(1, -2),
	"n_luoyanpo": Vector2i(-6, -2),
	"n_heifengzhai": Vector2i(-4, 1),
}


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	var nodes := _region_nodes()
	# 地标数量跟着 map_region 走：设计侧加一个区域就改 NODE_COUNT，
	# 别再写死数字（0.28.0 加石隙迷窟时就因为这里写死 7 而整张图生成不出来）。
	if nodes.size() != NODE_COUNT:
		printerr("[mapgen] %s 的地标应有 %d 个，实际 %d 个" % [PARENT_REGION, NODE_COUNT, nodes.size()])
		return 1
	var tile_set := MapKit.save_theme_tile_set("jiangnan_wild")
	if tile_set == null:
		printerr("[mapgen] TileSet 生成失败：检查 assets/tilesets/_common/ 的图集是否已导入")
		return 1

	var shell := MapKit.new_shell("overworld", tile_set, true, true)
	var root: Node2D = shell["root"]
	var ground: TileMapLayer = shell["ground"]
	var decor: TileMapLayer = shell["decor"]
	var conditional: TileMapLayer = shell["conditional"]
	var fog: TileMapLayer = shell["fog"]
	var markers: Node2D = shell["markers"]

	var roads := _plan_roads(nodes)
	var trail := _plan_shixi_trail(nodes)
	var reserved := _reserved_cells(nodes)
	var openings := _opening_cells(nodes)
	var water := _water_cells()

	# 顺序：草底 → 水 → 路 → 细径 → 山 → 洞口 → 地标装饰。
	# 路压过水岸，就成了「车马道一直通到水边」；山最后铺（除洞口），免得被别的装饰盖出洞。
	MapKit.paint_grass(ground, MAP_W, MAP_H)
	_paint_water(ground, decor)
	_paint_water_surface(ground)
	MapKit.paint_dirt(ground, roads, MAP_W, MAP_H)
	# 石隙细径铺在 `Conditional` 层：拿到藏宝图之前整条不显示（0.32.0 拍板）。
	MapKit.paint_cells(conditional, trail, MapKit.T_GRASS_PEBBLE)
	MapKit.paint_mountains(decor, _mountain_cells(roads, trail, reserved, water, openings))
	for cell: Vector2i in openings:
		decor.set_cell(cell, MapKit.SOURCE_ID, openings[cell])
	_paint_town(ground, decor, nodes)
	_paint_post_station(ground, decor, nodes)
	_paint_village(decor, nodes)
	_paint_grove(decor, nodes)
	_paint_patrol_checkpoint(decor, nodes)
	_paint_old_cart(decor, nodes)
	_paint_signposts(decor, markers, nodes)

	MapKit.paint_fog(fog, MAP_W, MAP_H, _plan_fog(nodes))
	_place_markers(markers, nodes)
	_place_npc_slots(shell["characters"], nodes)
	MapKit.fit_camera(shell["camera"], MAP_W, MAP_H)

	var save_error := MapKit.save_scene(root, SCENE_PATH)
	if save_error != OK:
		printerr("[mapgen] 保存场景失败：%d" % save_error)
		return 1
	var counts := {
		"ground": ground.get_used_cells().size(),
		"decor": decor.get_used_cells().size(),
		"conditional": conditional.get_used_cells().size(),
		"fog": fog.get_used_cells().size(),
	}
	var total: int = counts["ground"] + counts["decor"] + counts["conditional"] + counts["fog"]
	root.free()
	MapKit.dump_preview(SCENE_PATH, DUMP_PATH, MAP_W, MAP_H)
	print("[mapgen] 大地图：地标 %d / 明雷 %d / 巡逻线 %d / 判定位点 %d / 指路牌 %d" % [NODE_COUNT,
		SPAWN_OFFSETS.size(), PATROL_PATHS.size(), EVENT_SITES.size(), SIGNPOST_NODES.size(),
	])
	print("[mapgen] 瓦片用量 ground=%d decor=%d conditional=%d fog=%d 合计=%d" % [
		counts["ground"], counts["decor"], counts["conditional"], counts["fog"], total,
	])
	print("[mapgen] MAPGEN: OK -> %s" % SCENE_PATH)
	return 0


## 地标坐标直接用表里的像素坐标（pos_x/pos_y 即像素，画布 2048×1536）。
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
			# 设计 16 §3.5：**城镇是最大最显眼的一类**（地形 3×3、图标 48×48）。
			"node_type": str(row.get("node_type", "")),
		}
	return out


func _polyline(nodes: Dictionary, ids: Array, via: Array) -> Array:
	var points := [nodes[ids[0]]["cell"]]
	for cell: Vector2i in via:
		points.append(cell)
	points.append(nodes[ids[1]]["cell"])
	return points


func _carve_polyline(cells: Dictionary, points: Array, width: int) -> void:
	for index in range(points.size() - 1):
		MapKit.carve_corridor(cells, Rect2i(points[index], Vector2i.ONE),
			Rect2i(points[index + 1], Vector2i.ONE), width)


func _plan_roads(nodes: Dictionary) -> Dictionary:
	var roads := {}
	for edge: Dictionary in ROADS:
		_carve_polyline(roads, _polyline(nodes, edge["nodes"], edge["via"]), int(edge["width"]))
	# 城镇／驿站／副本门口留一圈空地：节点格子不能被墙盖住（验收第一条查的就是它）。
	# 兴趣点不铺地——洞口本身要嵌在山体里，铺一圈反而把山挖出个洞。
	for node_id: String in nodes:
		if str(nodes[node_id]["node_type"]) == "poi":
			continue
		MapKit.mark_disc(roads, nodes[node_id]["cell"], 1)
	return roads


## 石隙迷窟那条 1 格细径：单独一张格子表，因为它铺的是碎石草地而不是土路，
## 不能混进 `paint_dirt` 的边缘瓦片算法；而且它整条要铺在 `Conditional` 层上。
func _plan_shixi_trail(nodes: Dictionary) -> Dictionary:
	var trail := {}
	_carve_polyline(trail, _polyline(nodes, SHIXI_TRAIL["nodes"], SHIXI_TRAIL["via"]),
		int(SHIXI_TRAIL["width"]))
	return trail


## 明雷、判定位点、巡逻路线、观察点、地标、镖车、指路牌都不能被山压住。
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
	for point_id: String in OBSERVE_OFFSETS:
		reserved[_observe_cell(nodes, point_id)] = true
	for node_id: String in nodes:
		reserved[nodes[node_id]["cell"]] = true
	for node_id: String in SIGNPOST_NODES:
		reserved[nodes[node_id]["cell"] + SIGNPOST_OFFSETS[node_id]] = true
	reserved[_cart_cell(nodes)] = true
	reserved[_cart_cell(nodes) + Vector2i(0, 2)] = true
	return reserved


## 山洞／岩缝：**山体上的一个开口**（16 §3.5），不是摆在地上的洞图标。
## 它的格子不挡路（走得进去），落在支路的尽头。
func _opening_cells(nodes: Dictionary) -> Dictionary:
	var cave: Vector2i = nodes["n_cave_collapse"]["cell"]
	return {
		cave: MapKit.T_CAVE_RECESS,
		cave + Vector2i.RIGHT: MapKit.T_CAVE_RECESS,
		nodes["n_shixi"]["cell"]: MapKit.T_CRACK_MOUTH,
	}


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


func _observe_cell(nodes: Dictionary, point_id: String) -> Vector2i:
	return _cart_cell(nodes) + OBSERVE_OFFSETS[point_id]


## 旧镖车（0.29.0 的 20_第一章剧情）：官道南侧，摆在路肩、不挡路。
func _cart_cell(nodes: Dictionary) -> Vector2i:
	return nodes["n_luoyanpo"]["cell"] + Vector2i(-6, -6)


func _plan_fog(nodes: Dictionary) -> Dictionary:
	var clear := {}
	for node_id: String in nodes:
		if nodes[node_id]["reveal"]:
			MapKit.mark_disc(clear, nodes[node_id]["cell"], REVEAL_RADIUS)
	return clear


# ---------------------------------------------------------------- 自然边界（山、水）

## 边缘是**看起来像山／水的自然边界**，不是空气墙直线（07 §三）——
## 内缘按散列往里多推几格，免得一眼看出「到这里就被挡住了」。
func _mountain_cells(roads: Dictionary, trail: Dictionary, reserved: Dictionary,
		water: Dictionary, openings: Dictionary) -> Dictionary:
	var cells := {}
	for y in MAP_H:
		for x in MAP_W:
			var cell := Vector2i(x, y)
			if _is_border(cell):
				cells[cell] = true
	for rect: Rect2i in MOUNTAIN_BLOCKS:
		MapKit.mark_rect(cells, rect)
	# 水、洞口、路、细径、明雷／判定位点／观察点／地标：一律从山体里挖掉。
	for cell: Vector2i in water:
		cells.erase(cell)
	for cell: Vector2i in openings:
		cells.erase(cell)
	for cell: Vector2i in roads:
		cells.erase(cell)
	for cell: Vector2i in trail:
		cells.erase(cell)
	for cell: Vector2i in reserved:
		cells.erase(cell)
	return cells


func _is_border(cell: Vector2i) -> bool:
	if cell.x < _edge_depth(cell.y * 7 + 1):
		return true
	if cell.x > MAP_W - 1 - _edge_depth(cell.y * 11 + 3):
		return true
	if cell.y < _edge_depth(cell.x * 13 + 5):
		return true
	# 南缘西段是水（渡口），那里的天然边界交给 `_paint_water`
	if cell.x >= WATER_RECT.position.x + WATER_RECT.size.x \
			and cell.y > MAP_H - 1 - _edge_depth(cell.x * 17 + 7):
		return true
	return false


func _edge_depth(seed: int) -> int:
	return 2 + int(MapKit.hash01(seed, seed * 3 + 1) * 3.0)


func _water_cells() -> Dictionary:
	var cells := {}
	MapKit.mark_rect(cells, WATER_RECT)
	# 水岸那一排（不挡路）也算水域：山不能长到水里
	for x in range(WATER_RECT.position.x, WATER_RECT.position.x + WATER_RECT.size.x):
		cells[Vector2i(x, WATER_RECT.position.y - 1)] = true
	return cells


## 深水画在 **Decor** 层：它挡路，而 `Ground` 按约定只能是「走得上去的地面」
## （`verify_maps._check_ground_not_solid` 盯着这条——副本里「脚下全是墙」那个真 bug 的根因）。
func _paint_water(ground: TileMapLayer, decor: TileMapLayer) -> void:
	for y in range(WATER_RECT.position.y, WATER_RECT.position.y + WATER_RECT.size.y):
		for x in range(WATER_RECT.position.x, WATER_RECT.position.x + WATER_RECT.size.x):
			var cell := Vector2i(x, y)
			if y == WATER_RECT.position.y:
				ground.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_WATER_SHALLOW)
			else:
				decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_WATER_DEEP)


## 水岸 ＋ 芦苇荡（都在 Ground 层，不挡路）。
func _paint_water_surface(ground: TileMapLayer) -> void:
	for x in range(WATER_RECT.position.x, WATER_RECT.position.x + WATER_RECT.size.x):
		ground.set_cell(Vector2i(x, WATER_RECT.position.y - 1), MapKit.SOURCE_ID,
			MapKit.T_WATER_SHORE)
	for at: Vector2i in [Vector2i(4, 42), Vector2i(7, 42), Vector2i(13, 42), Vector2i(17, 43),
			Vector2i(2, 43), Vector2i(20, 42)]:
		ground.set_cell(at, MapKit.SOURCE_ID, MapKit.T_REEDS)


# ---------------------------------------------------------------- 地标表现

## 16 号写得很清楚：**城镇不是一个贴上去的图标，而是一片拼出来的地形**——
## 屋舍／院墙／道路一起构成轮廓，图标（48×48）只叠在上面强调。
## 屋顶与院墙压在节点的**北面**：官道从东侧过，那一圈必须空着才走得出去。
func _paint_town(ground: TileMapLayer, decor: TileMapLayer, nodes: Dictionary) -> void:
	var center: Vector2i = nodes["n_qingfengyi"]["cell"]
	for dy in range(-2, 3):
		for dx in range(-2, 3):
			ground.set_cell(center + Vector2i(dx, dy), MapKit.SOURCE_ID, MapKit.T_DIRT)
	for dx in range(-2, 3):
		decor.set_cell(center + Vector2i(dx, -2), MapKit.SOURCE_ID, MapKit.T_ROOF_SLATE)
		var wall := MapKit.T_WALL_GRAY
		if dx == -1:
			wall = MapKit.T_WINDOW_GRAY
		elif dx == 1:
			wall = MapKit.T_DOOR_GRAY
		decor.set_cell(center + Vector2i(dx, -1), MapKit.SOURCE_ID, wall)
	decor.set_cell(center + Vector2i(-3, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(center + Vector2i(3, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)


## 驿站：旗杆马桩的剪影，与城镇的屋舍轮廓区分开（16 §3.5）。
func _paint_post_station(ground: TileMapLayer, decor: TileMapLayer, nodes: Dictionary) -> void:
	var center: Vector2i = nodes["n_post_station"]["cell"]
	for dx in range(1, 4):
		ground.set_cell(center + Vector2i(dx, -2), MapKit.SOURCE_ID, MapKit.T_DIRT)
	decor.set_cell(center + Vector2i(2, -3), MapKit.SOURCE_ID, MapKit.T_ROOF_SLATE_TOP)
	decor.set_cell(center + Vector2i(3, -3), MapKit.SOURCE_ID, MapKit.T_ROOF_SLATE_TOP)
	decor.set_cell(center + Vector2i(2, -2), MapKit.SOURCE_ID, MapKit.T_WALL_WOOD)
	decor.set_cell(center + Vector2i(3, -2), MapKit.SOURCE_ID, MapKit.T_DOOR_WOOD)
	decor.set_cell(center + Vector2i(2, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(center + Vector2i(4, -1), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)


## 荒村：**焦黑屋舍群**（16 §3.5）——几间塌了顶的房子成组出现，不是一枚图标。
func _paint_village(decor: TileMapLayer, nodes: Dictionary) -> void:
	var center: Vector2i = nodes["n_huangcun"]["cell"]
	_ruined_house(decor, center + Vector2i(2, -3))
	_ruined_house(decor, center + Vector2i(3, 1))
	_ruined_house(decor, center + Vector2i(1, 4))


func _ruined_house(decor: TileMapLayer, at: Vector2i) -> void:
	decor.set_cell(at, MapKit.SOURCE_ID, MapKit.T_WALL_WOOD)
	decor.set_cell(at + Vector2i.RIGHT, MapKit.SOURCE_ID, MapKit.T_WALL_GRAY)
	decor.set_cell(at + Vector2i.DOWN, MapKit.SOURCE_ID, MapKit.T_WINDOW_WOOD)
	decor.set_cell(at + Vector2i(1, 1), MapKit.SOURCE_ID, MapKit.T_WALL_GRAY)


## 落雁坡：靠主路一侧几株显眼的孤树（16 §4.1 的视觉签名，玩家用来定位）。
func _paint_grove(decor: TileMapLayer, nodes: Dictionary) -> void:
	var at: Vector2i = nodes["n_luoyanpo"]["cell"] + Vector2i(7, -3)
	decor.set_cell(at, MapKit.SOURCE_ID, MapKit.T_TREE_PINE)
	decor.set_cell(at + Vector2i(1, 1), MapKit.SOURCE_ID, MapKit.T_TREE_ROUND)
	decor.set_cell(at + Vector2i(-1, 2), MapKit.SOURCE_ID, MapKit.T_TREE_AUTUMN)


## 官道关卡（07 §十一 第 12 条）：拒马／木栅栏／官差旗摆在**路肩北侧**，
## 判定点本身留在可走的官道上——玩家是走到关卡按 E，不是撞进栅栏里。
func _paint_patrol_checkpoint(decor: TileMapLayer, nodes: Dictionary) -> void:
	var cell := _event_cell(nodes, "ev_patrol_check")
	decor.set_cell(cell + Vector2i(-1, -3), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	decor.set_cell(cell + Vector2i(1, -3), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	decor.set_cell(cell + Vector2i(-2, -3), MapKit.SOURCE_ID, MapKit.T_CRATE)
	decor.set_cell(cell + Vector2i(2, -3), MapKit.SOURCE_ID, MapKit.T_SIGN)


## 旧镖车：车辕断、镖旗褪色，是林铁山守了十年的那辆——**摆在路肩，不挡路**。
##
## 0.32.0：美术的 prop（`prop_luoyanpo_jiuche.png`，**64×64 ＝ 2×2 格**）一交付就顶掉原来的
## 4 格占位瓦片——「翻倒的车厢 ＋ 散落的镖货 ＋ 断掉的车辕 ＋ 只剩一个字的镖旗」四样都画在图里了，
## 所以四格一起撤。
##
## **挂 `Decor` 下、不挂 `Markers` 下**：大地图有雾层，挂 Markers 的会盖在雾上面——
## 车队所在那段路还没揭开时就会露出来。挂 Decor 才是「世界里的东西」，雾照样能盖住它。
## 图缺失时退回那 4 格瓦片：别让这辆车**静静地消失**（那是这套约定最怕的一种坏法）。
func _paint_old_cart(decor: TileMapLayer, nodes: Dictionary) -> void:
	var cell := _cart_cell(nodes)
	if ResourceLoader.exists(MapKit.PROP_LUOYANPO_CART):
		# 64×64 对 2×2 格：左上角压在 `_cart_cell()` 上，所以中心落在「(cell + 1) 个格」的格心。
		var center := Vector2((cell.x + 1) * MapKit.TILE_PX, (cell.y + 1) * MapKit.TILE_PX)
		MapKit.add_sprite(decor, "prop_luoyanpo_jiuche", MapKit.PROP_LUOYANPO_CART, center)
		return
	decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_CRATE)                     # 翻倒的车厢
	decor.set_cell(cell + Vector2i(1, 0), MapKit.SOURCE_ID, MapKit.T_BARREL)   # 散落的镖货
	decor.set_cell(cell + Vector2i(-1, 0), MapKit.SOURCE_ID, MapKit.T_FENCE_H) # 断掉的车辕
	decor.set_cell(cell + Vector2i(1, 1), MapKit.SOURCE_ID, MapKit.T_SIGN)     # 只剩「威」字的镖旗


## 指路牌（0.31.2）：主路 4 个节点附近各一块，`Sign_<node_id>` 与表里的 id 对齐。
func _paint_signposts(decor: TileMapLayer, markers: Node2D, nodes: Dictionary) -> void:
	for node_id: String in SIGNPOST_NODES:
		var cell: Vector2i = nodes[node_id]["cell"] + SIGNPOST_OFFSETS[node_id]
		decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_SIGNPOST)
		MapKit.add_marker(markers, "Sign_" + node_id, cell)


func _place_npc_slots(characters: Node2D, nodes: Dictionary) -> void:
	# 林铁山：蹲在镖车边。NPC 站位沿用 `npc_slot_0N` 占位（等对话表落地再按 id 绑）。
	var marker := MapKit.add_marker(characters, "npc_slot_01", _cart_cell(nodes) + Vector2i(0, 2))
	MapKit.add_sprite(marker, "placeholder", MapKit.NPC_TEXTURE)


func _place_markers(markers: Node2D, nodes: Dictionary) -> void:
	for node_id: String in nodes:
		var data: Dictionary = nodes[node_id]
		var marker := MapKit.add_marker_at(markers, "Node_" + node_id, data["pixel"])
		if not str(data["icon"]).is_empty():
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

	for point_id: String in OBSERVE_OFFSETS:
		MapKit.add_marker(markers, "Observe_" + point_id, _observe_cell(nodes, point_id))
