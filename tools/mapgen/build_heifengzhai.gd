## 生成黑风寨副本 scenes/maps/scene_heifengzhai.tscn。
##
## 按 07_地图资源需求.md 4.2 与第五节：三层 19 个房间放**一张场景图**，纵向堆叠，每层约 40×30 格。
## 房间矩形是本脚本手排的（策划只给了房间表，没给坐标）；走廊按 `dungeon_room.exit_rooms` 连，
## 连通关系反过来由 verify_maps.gd 校验。
##
## 已知空档（脚本里也标了）：
##   * `dungeon_room.exit_rooms` 没有声明**层与层**的连通，所以一层→二层的石阶是我补的，
##     Marker 名 `stairs_to_floor2`（Portal_ 前缀绑的是 scene_id，装不了层间楼梯）。
##
## 用法：
##   godot --headless --path . --log-file .logs\mapgen_heifengzhai.log --script res://tools/mapgen/build_heifengzhai.gd
extends SceneTree

const MapKit := preload("res://tools/mapgen/map_kit.gd")

const SCENE_PATH := "res://scenes/maps/scene_heifengzhai.tscn"
const DUMP_PATH := "res://.logs/scene_heifengzhai_preview.json"
const MAP_W := 40
const MAP_H := 84

## 19 个房间的矩形（列 x, 行 y, 宽, 高）。三层纵向堆叠：一层 y2–28、二层 y31–52、三层 y58–81。
const ROOMS := {
	# 一层 前寨
	"hf1_gate": Rect2i(3, 2, 10, 6),
	"hf1_yard": Rect2i(16, 2, 12, 8),
	"hf1_path": Rect2i(30, 2, 8, 6),
	"hf1_shed": Rect2i(30, 11, 8, 6),
	"hf1_brazier": Rect2i(16, 13, 11, 8),
	"hf1_vault": Rect2i(3, 13, 9, 8),
	"hf1_secret": Rect2i(3, 23, 10, 6),
	"hf1_cell": Rect2i(16, 23, 10, 6),
	"hf1_deep": Rect2i(30, 22, 8, 7),
	# 二层 聚义厅
	"hf2_stairs": Rect2i(30, 31, 8, 6),
	"hf2_yard": Rect2i(16, 31, 12, 8),
	"hf2_poison": Rect2i(3, 31, 10, 8),
	"hf2_hall": Rect2i(14, 44, 13, 9),
	"hf2_vault": Rect2i(3, 44, 8, 7),
	"hf2_tunnel": Rect2i(30, 44, 8, 7),
	# 三层 后寨
	"hf3_gate": Rect2i(26, 58, 12, 6),
	"hf3_patrol": Rect2i(14, 62, 12, 8),
	"hf3_boss": Rect2i(2, 62, 11, 9),
	"hf3_dungeon": Rect2i(14, 74, 12, 8),
}

## 走廊连接：`dungeon_room.exit_rooms` 的原样（双向由脚本去重），外加一条层间石阶。
const LINKS := [
	["hf1_gate", "hf1_yard"], ["hf1_gate", "hf1_path"],
	["hf1_yard", "hf1_shed"], ["hf1_yard", "hf1_path"],
	["hf1_shed", "hf1_yard"],
	["hf1_path", "hf1_cell"], ["hf1_path", "hf1_brazier"],
	["hf1_brazier", "hf1_vault"],
	["hf1_vault", "hf1_brazier"], ["hf1_vault", "hf1_secret"],
	["hf1_secret", "hf1_vault"],
	["hf1_cell", "hf1_deep"], ["hf1_deep", "hf1_cell"],
	["hf2_stairs", "hf2_yard"], ["hf2_stairs", "hf2_poison"],
	["hf2_yard", "hf2_hall"],
	["hf2_poison", "hf2_hall"], ["hf2_poison", "hf2_tunnel"],
	["hf2_hall", "hf2_vault"], ["hf2_hall", "hf3_gate"],
	["hf2_vault", "hf2_hall"],
	["hf2_tunnel", "hf3_gate"],
	["hf3_gate", "hf3_patrol"], ["hf3_gate", "hf3_dungeon"],
	["hf3_patrol", "hf3_boss"],
	# 表里没声明层间连通，这条是补的（一层最深处下到二层石阶）。
	["hf1_deep", "hf2_stairs"],
]

## 宝箱：Chest_<chest_id>。两个银箱分别在 hf1_vault / hf1_secret，靠房间文件夹分开。
const CHESTS := {
	"hf1_shed": ["Chest_drop_chest_copper"],
	"hf1_vault": ["Chest_drop_chest_silver"],
	"hf1_secret": ["Chest_drop_chest_silver"],
	"hf2_vault": ["Chest_drop_chest_gold"],
}

## 隐藏触发（hidden_trigger.csv）：trig_wine 占两处（hf1_deep 与 hf3_dungeon）。
const TRIGGERS := {
	"hf1_deep": ["Trigger_trig_wine"],
	"hf1_brazier": ["Trigger_trig_brazier"],
	"hf1_vault": ["Trigger_trig_chest_all"],
	"hf2_hall": ["Trigger_trig_rusty_sword"],
	"hf2_poison": ["Trigger_trig_poison_kill"],
	"hf3_gate": ["Trigger_trig_stealth_clear"],
	"hf3_dungeon": ["Trigger_trig_wine"],
}

## 事件判定（event_check.csv 里 scene_id = scene_heifengzhai 的 4 条）。
const EVENTS := {
	"hf1_shed": ["Event_ev_shed_trap"],
	"hf1_cell": ["Event_ev_cell_heal"],
	"hf2_poison": ["Event_ev_poison_identify"],
	"hf2_hall": ["Event_ev_hall_inscription"],
}

## 房间内挂钩点的落位（相对房间左上角的偏移，格）。
const SPOTS := {
	"Chest_drop_chest_copper": Vector2i(3, 3),
	"Chest_drop_chest_silver": Vector2i(3, 3),
	"Chest_drop_chest_gold": Vector2i(3, 3),
	"Trigger_trig_wine": Vector2i(2, 2),
	"Trigger_trig_brazier": Vector2i(4, 3),
	"Trigger_trig_chest_all": Vector2i(6, 2),
	"Trigger_trig_rusty_sword": Vector2i(6, 2),
	"Trigger_trig_poison_kill": Vector2i(3, 3),
	"Trigger_trig_stealth_clear": Vector2i(5, 2),
	"Event_ev_shed_trap": Vector2i(2, 4),
	"Event_ev_cell_heal": Vector2i(3, 3),
	"Event_ev_poison_identify": Vector2i(6, 3),
	"Event_ev_hall_inscription": Vector2i(9, 3),
}

## 房间内的固定敌人：`Enemies/<room_id>/Team_<team_id>`（07 文档第二、五节，共 7 处，本场景 6 处）。
## 落点由 `MapKit.room_entrance_inside` 算，站在门口内侧但不堵门。
const TEAMS := {
	"hf1_yard": "team_gate_sentry",
	"hf2_yard": "team_elite_blade",
	"hf2_poison": "team_poison_hand",
	"hf2_hall": "team_boss_guards",
	"hf3_patrol": "team_heifeng_elite",
	"hf3_boss": "team_boss",
}

## 入口房间的回程出口（小地图 → 大地图），07 文档 4.0。
const EXIT_ROOM := "hf1_gate"

var _problems: Array[String] = []


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	var tile_set := MapKit.save_theme_tile_set("heifengzhai")
	if tile_set == null:
		printerr("[mapgen] 山寨主题 TileSet 生成失败")
		return 1
	var shell := MapKit.new_shell("scene_heifengzhai", tile_set)
	var root: Node2D = shell["root"]
	var ground: TileMapLayer = shell["ground"]
	var decor: TileMapLayer = shell["decor"]

	var floor_cells := {}
	for room_id: String in ROOMS:
		MapKit.mark_rect(floor_cells, ROOMS[room_id])
	for link: Array in LINKS:
		MapKit.carve_corridor(floor_cells, ROOMS[link[0]], ROOMS[link[1]], 3)
	MapKit.paint_cells(ground, floor_cells, MapKit.T_FLOOR)
	var walls := MapKit.wall_shell(floor_cells)
	MapKit.paint_cells(decor, walls, MapKit.T_BATTLEMENT)
	_paint_room_details(decor, walls)

	for room_id: String in ROOMS:
		MapKit.add_room(shell["rooms"], room_id, ROOMS[room_id])
	_place_markers(shell)
	_place_team_markers(shell, floor_cells)
	_hidden_layer_tone(shell)
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(6, 5)).set_meta("role", "player")
	MapKit.fit_camera(shell["camera"], MAP_W, MAP_H)

	var counts := {"ground": ground.get_used_cells().size(), "decor": decor.get_used_cells().size()}
	var save_error := MapKit.save_scene(root, SCENE_PATH)
	if save_error != OK:
		printerr("[mapgen] 保存场景失败：%d" % save_error)
		return 1
	root.free()
	MapKit.dump_preview(SCENE_PATH, DUMP_PATH, MAP_W, MAP_H)
	print("[mapgen] 黑风寨：房间 %d / 宝箱 %d / 触发 %d / 判定 %d / 敌人位点 %d / 回程出口 1" % [
		ROOMS.size(), _count(CHESTS), _count(TRIGGERS), _count(EVENTS), TEAMS.size(),
	])
	print("[mapgen] 瓦片用量 ground=%d decor=%d（上限 5000）" % [counts["ground"], counts["decor"]])
	for problem: String in _problems:
		printerr("  - " + problem)
	if not _problems.is_empty():
		return 1
	print("[mapgen] MAPGEN: OK -> %s" % SCENE_PATH)
	return 0


func _count(table: Dictionary) -> int:
	var total := 0
	for key: String in table:
		total += table[key].size()
	return total


## 房间里的招牌物件：门口留出侧向通路，进门的房间都能从侧面绕（07 文档验收 4）。
func _paint_room_details(decor: TileMapLayer, walls: Dictionary) -> void:
	for room_id: String in ROOMS:
		var rect: Rect2i = ROOMS[room_id]
		var corner := Vector2i(rect.position.x + 1, rect.position.y + 1)
		decor.set_cell(corner, MapKit.SOURCE_ID, MapKit.T_CRATE)
		decor.set_cell(Vector2i(rect.position.x + rect.size.x - 2, rect.position.y + 1),
			MapKit.SOURCE_ID, MapKit.T_BARREL)
	# 火盆厅的三个火盆：顺序藏在 hf2_hall 的题字里。
	var brazier: Rect2i = ROOMS["hf1_brazier"]
	for index in 3:
		var cell := Vector2i(brazier.position.x + 2 + index * 3, brazier.position.y + 5)
		decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_SIGN)
	# 门口留一格空，别把入口堵死。
	for room_id: String in ROOMS:
		var rect: Rect2i = ROOMS[room_id]
		walls.erase(Vector2i(rect.position.x + rect.size.x / 2, rect.position.y - 1))


## 后山地牢（隐藏层）压暗，让「隐藏层」一眼能看出来。
func _hidden_layer_tone(shell: Dictionary) -> void:
	var tone := Color(0.62, 0.62, 0.72, 1.0)
	(shell["decor"] as TileMapLayer).modulate = tone
	(shell["ground"] as TileMapLayer).modulate = Color(0.86, 0.86, 0.94, 1.0)


func _place_markers(shell: Dictionary) -> void:
	var markers: Node2D = shell["markers"]
	# 回程出口放在入口房间里，名字按 map_local.scene_id。
	_room_item(markers, EXIT_ROOM, "Exit_scene_heifengzhai")
	MapKit.add_marker(markers, "stairs_to_floor2", Vector2i(33, 28))
	for room_id: String in CHESTS:
		for marker_name: String in CHESTS[room_id]:
			_room_item(markers, room_id, marker_name)
	for room_id: String in TRIGGERS:
		for marker_name: String in TRIGGERS[room_id]:
			_room_item(markers, room_id, marker_name)
	for room_id: String in EVENTS:
		for marker_name: String in EVENTS[room_id]:
			_room_item(markers, room_id, marker_name)


## 固定敌人挂在 Enemies/<room_id>/ 下（唯一带父目录的约定）。
func _place_team_markers(shell: Dictionary, floor_cells: Dictionary) -> void:
	var enemies: Node2D = shell["enemies"]
	var decor: TileMapLayer = shell["decor"]
	for room_id: String in TEAMS:
		var holder := MapKit.folder(enemies, room_id)
		var cell := MapKit.room_entrance_inside(decor, floor_cells, ROOMS[room_id])
		MapKit.add_marker(holder, "Team_" + str(TEAMS[room_id]), cell)


func _room_item(markers: Node2D, room_id: String, marker_name: String) -> void:
	var holder: Node2D = markers.get_node_or_null(room_id)
	if holder == null:
		holder = MapKit.folder(markers, room_id)
	var rect: Rect2i = ROOMS[room_id]
	# 出口没有「房间里的小偏移」语义，固定在入口房间的左下角（进出都走这里）。
	var offset: Vector2i = Vector2i(2, 3) if marker_name.begins_with("Exit_") \
		else SPOTS.get(marker_name, Vector2i(2, 2))
	MapKit.add_marker(holder, marker_name, Vector2i(rect.position.x + offset.x, rect.position.y + offset.y))
