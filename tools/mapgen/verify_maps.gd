## 按 07_地图资源需求.md 第九节的验收标准，逐条校验六张地图。
##
## 覆盖：
##   1. 命名与归属：Marker 名 = 表里的 id（双向比对）；Room_ 在正确场景、Spawn_ 在正确区域
##   2. 连通性：dungeon_room.exit_rooms 声明的连通关系，在地图上真的走得通；没有进不去的房间
##   3. 可绕与背袭：明雷周围留得下 2 格宽的通路（启发式，见 _check_spawn_room）
##   4. 战斗房间侧向通路：房间入口开口至少 2 格宽
##   5. 巡逻：Patrol_ 路线存在且端点不卡墙
##   6. 性能：单场景 TileMapLayer ≤ 4、瓦片用量 ≤ 5000、单区域明雷 ≤ 8
##
## 用法：
##   godot --headless --path . --log-file .logs\mapcheck.log --script res://tools/mapgen/verify_maps.gd
extends SceneTree

const MapKit := preload("res://tools/mapgen/map_kit.gd")
## 读它的 `FACILITY_LABELS`（无表设施的白名单）：地图侧的位点名要对得上这份清单，
## 免得「铁匠铺改成 facility_smith」这种改名在地图上悄悄生效、而代码不认（决策 245）。
const LocalMapScript := preload("res://src/world/local_map_controller.gd")

const REGION_CSV := "res://data/tables/map_region.csv"
const LOCAL_CSV := "res://data/tables/map_local.csv"
const SPAWN_CSV := "res://data/tables/roaming_spawn.csv"
const ROOM_CSV := "res://data/tables/dungeon_room.csv"
const TRIGGER_CSV := "res://data/tables/hidden_trigger.csv"
const EVENT_CSV := "res://data/tables/event_check.csv"
const FLAVOR_CSV := "res://data/tables/flavor_point.csv"
const NPC_CSV := "res://data/tables/npc_def.csv"
const BUILDING_CSV := "res://data/tables/building_def.csv"

const OVERWORLD := "res://scenes/maps/overworld.tscn"
const PARENT_REGION := "jiangnan_east"
const MAX_TILE_LAYERS := 4
## 大地图 0.32.0 起多一层 **`Conditional`**（条件地表：石隙那条细径，16 §3.2）——
## 地面 4 层 ＋ 条件地表 = 5 层。其余五张小地图照旧 ≤4。
const MAX_TILE_LAYERS_OVERWORLD := 5
const MAX_TILES := 5000
## 大地图 64×48＝**3072 格/层**：光「草底 ＋ 雾」两层就 6000 出头，`5000` 那条是按
## 32×24（768 格/层）定的。0.31.2 扩容后按面积重算，大地图单独给一档上限。
const MAX_TILES_OVERWORLD := 12000
const MAX_SPAWNS_PER_REGION := 8
## 明雷离所属区域地标的最大格数：归属检查用的宽松半径（表里只给了 region_id）。
const SPAWN_REGION_RADIUS := 12
## 挂钩点前缀（07 文档第二节）。Team_ / Exit_ 是 2026-10-02 审计后补的两条。
const MARKER_PREFIXES := ["Node_", "Portal_", "Room_", "Chest_", "Spawn_", "Event_",
	"Trigger_", "Patrol_", "Team_", "Exit_", "Observe_", "Sign_"]

var _problems: Array[String] = []
## 表本身的问题（不是地图的问题），单独回报设计侧，不判地图失败。
var _data_notes: Array[String] = []
## 地图资产还没交付的东西（待补地编／美术），单独回报、不判地图失败
## （与「雾的贴图」「精英贴图」同一个口径：缺资产不该挡住整条验收链）。
var _asset_notes: Array[String] = []
var _scenes: Dictionary = {}


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	var local_scenes := []
	for row: Dictionary in MapKit.read_csv(LOCAL_CSV):
		local_scenes.append(str(row["scene_id"]))
	_scenes[OVERWORLD] = _load(OVERWORLD)
	for scene_id: String in local_scenes:
		var path := "res://scenes/maps/%s.tscn" % scene_id
		if not FileAccess.file_exists(path):
			_problems.append("map_local.csv 里的 %s 没有对应场景：%s" % [scene_id, path])
			continue
		_scenes[path] = _load(path)

	_check_overworld()
	_check_local_maps(local_scenes)
	_check_team_markers()
	_check_exit_markers(local_scenes)
	_check_npc_slots(local_scenes)
	_check_facility_markers(local_scenes)
	_check_building_markers(local_scenes)
	_check_observe_points(local_scenes)
	_check_only_decor_blocks()
	_check_conditional_layers()
	_check_budget()

	for path: String in _scenes:
		var scene: Dictionary = _scenes[path]
		if not scene.is_empty():
			(scene["root"] as Node).free()
	if not _data_notes.is_empty():
		print("=== 回报设计侧：配置表内部不一致 %d 条（不影响地图验收）===" % _data_notes.size())
		for note: String in _data_notes:
			print("  * " + note)
	if not _asset_notes.is_empty():
		print("=== 待补（地编／美术，不影响验收）%d 条 ===" % _asset_notes.size())
		for note: String in _asset_notes:
			print("  * " + note)
	if _problems.is_empty():
		print("MAPCHECK: OK")
		return 0
	printerr("=== 地图验收不通过，共 %d 条 ===" % _problems.size())
	for problem: String in _problems:
		printerr("  - " + problem)
	printerr("MAPCHECK: FAIL")
	return 1


# ---------------------------------------------------------------- 命名与归属

func _check_overworld() -> void:
	var scene: Dictionary = _scenes.get(OVERWORLD, {})
	if scene.is_empty():
		return
	var names: Dictionary = scene["names"]

	_compare("大地图·Node_", _column(REGION_CSV, "node_id", "parent_region", PARENT_REGION),
		_group_names(names, "Node_"), "Node_")
	_compare("大地图·Portal_", _column(LOCAL_CSV, "scene_id", "", ""),
		_group_names(names, "Portal_"), "Portal_")
	_compare("大地图·Spawn_", _column(SPAWN_CSV, "spawn_id", "", ""),
		_group_names(names, "Spawn_"), "Spawn_")

	var patrols := []
	for row: Dictionary in MapKit.read_csv(SPAWN_CSV):
		var path_id := str(row.get("patrol_path_id", ""))
		if not path_id.is_empty() and not patrols.has(path_id):
			patrols.append(path_id)
	_compare("大地图·Patrol_", patrols, _group_names(names, "Patrol_"), "Patrol_")

	# 判定位点在 07 文档里分了大地图/小地图两处：`scene_id` 非空的归小地图；
	# `scene_id` 空的默认归大地图——**除非它的位点被摆进了某张小地图**（赌局就在清风驿·客栈里）。
	# 这里以前写死 `check_id != "ev_gamble"`：一条数据改了名，这个「例外」就变成谎言，
	# 而且它正是「位点摆好了、代码却没接上」那个真 bug 的遮羞布（见框架说明决策 75）。
	var local_event_names := {}
	for path: String in _scenes:
		if path == OVERWORLD:
			continue    # 只统计**小地图**里的位点；把大地图自己算进来会让每条都「不算大地图」，
			#             结果是期望列表直接空掉（第一版就这么错，被下面的双向比对当场抓出来）
		var local_scene: Dictionary = _scenes.get(path, {})
		if local_scene.is_empty():
			continue
		for marker_name: String in local_scene["names"]:
			if marker_name.begins_with("Event_"):
				local_event_names[marker_name] = true
	var overworld_events := []
	var overworld_event_names: Dictionary = _group_names(names, "Event_")
	for row: Dictionary in MapKit.read_csv(EVENT_CSV):
		var scene_id := str(row.get("scene_id", ""))
		var check_id := str(row["check_id"])
		if not scene_id.is_empty():
			continue
		var marker_name := "Event_" + check_id
		# 只在**小地图**里摆了位点、大地图上没有的（赌局）→ 不算大地图那一边；
		# 两边都摆了的（塌陷山洞的碑文：寨外的地标点 + 洞里那半截石碑）→ 大地图这边照样要查。
		if local_event_names.has(marker_name) and not overworld_event_names.has(marker_name):
			continue
		overworld_events.append(check_id)
	_compare("大地图·Event_", overworld_events, _group_names(names, "Event_"), "Event_")

	# Spawn_ 必须落在它 region_id 指的区域附近。
	var node_cells := {}
	for row: Dictionary in MapKit.rows_where(REGION_CSV, "parent_region", PARENT_REGION):
		node_cells[str(row["node_id"])] = Vector2i(int(float(row["pos_x"])) / MapKit.TILE_PX,
			int(float(row["pos_y"])) / MapKit.TILE_PX)
	var region_totals := {}
	for row: Dictionary in MapKit.read_csv(SPAWN_CSV):
		var spawn_id := str(row["spawn_id"])
		var marker_name := "Spawn_" + spawn_id
		if not names.has(marker_name):
			continue
		var region_id := str(row["region_id"])
		var region_node := str({"n_luoyanpo": "n_luoyanpo", "n_heifengzhai": "n_heifengzhai",
			"n_huangcun": "n_huangcun"}.get(region_id, ""))
		var cell: Vector2i = names[marker_name]
		if region_node.is_empty() or not node_cells.has(region_node):
			_problems.append("大地图·明雷：%s 的 region_id %s 找不到对应地标" % [spawn_id, region_id])
			continue
		var anchor: Vector2i = node_cells[region_node]
		if cell.distance_to(anchor) > SPAWN_REGION_RADIUS:
			_problems.append("大地图·明雷：%s 离 %s 太远（%d 格），疑似放错区域" % [
				spawn_id, region_id, int(cell.distance_to(anchor))])
		region_totals[region_id] = int(region_totals.get(region_id, 0)) + 1
		_check_spawn_room(scene, marker_name, cell)
	for region_id: String in region_totals:
		if int(region_totals[region_id]) > MAX_SPAWNS_PER_REGION:
			_problems.append("大地图：%s 有 %d 个明雷，超过同屏上限 %d" % [
				region_id, region_totals[region_id], MAX_SPAWNS_PER_REGION])

	_check_walkable(scene, "大地图·地标", _group_names(names, "Node_"))
	_check_walkable(scene, "大地图·明雷", _group_names(names, "Spawn_"))
	_check_walkable(scene, "大地图·判定位点", _group_names(names, "Event_"))
	_check_walkable(scene, "大地图·观察点", _group_names(names, "Observe_"))
	_check_patrols(scene)
	print("[check] 大地图：Node %d / Portal %d / Spawn %d / Patrol %d / Event %d" % [
		_group_names(names, "Node_").size(), _group_names(names, "Portal_").size(),
		_group_names(names, "Spawn_").size(), _group_names(names, "Patrol_").size(),
		_group_names(names, "Event_").size(),
	])


func _check_local_maps(local_scenes: Array) -> void:
	var rooms_by_scene := {}
	for row: Dictionary in MapKit.read_csv(ROOM_CSV):
		var scene_id := str(row["scene_id"])
		if not rooms_by_scene.has(scene_id):
			rooms_by_scene[scene_id] = []
		rooms_by_scene[scene_id].append(str(row["room_id"]))

	# 位点归属要用到的两张反查表：小地图 → 它挂的大地图节点；check_id → 判定行
	var parent_by_scene := {}
	for row: Dictionary in MapKit.read_csv(LOCAL_CSV):
		parent_by_scene[str(row["scene_id"])] = str(row.get("parent_node", ""))
	var event_by_id := {}
	for row: Dictionary in MapKit.read_csv(EVENT_CSV):
		event_by_id[str(row["check_id"])] = row

	for scene_id: String in local_scenes:
		var path := "res://scenes/maps/%s.tscn" % scene_id
		var scene: Dictionary = _scenes.get(path, {})
		if scene.is_empty():
			continue
		var names: Dictionary = scene["names"]
		var expected_rooms: Array = rooms_by_scene.get(scene_id, [])
		if not expected_rooms.is_empty():
			_compare("%s·Room_" % scene_id, expected_rooms, _group_names(names, "Room_"), "Room_")
			_check_rooms(scene, expected_rooms, scene_id)
		# 小地图里的判定位点必须**接得上**：位点的 check_id 在表里，且这条判定属于这张图
		# （`scene_id` 命中，或 `scene_id` 空而 `region_id` 正好是这张图的父节点）。
		# 运行期要接上它还得两个条件同时成立（见 `local_map_controller._collect_events`），
		# 所以这里是对着同一套判据查——**位点摆好了却接不上，以前是验收全绿也发现不了的**：
		# 赌局（Region 行、位点在小地图里）就是这么漏了一整轮。
		var parent_node := str(parent_by_scene.get(scene_id, ""))
		for marker_name: String in _group_names(names, "Event_"):
			var check_id := marker_name.substr("Event_".length())
			if not event_by_id.has(check_id):
				_problems.append("%s：地图里有 %s，配置表查不到" % [scene_id, marker_name])
				continue
			var row: Dictionary = event_by_id[check_id]
			var row_scene := str(row.get("scene_id", ""))
			var row_region := str(row.get("region_id", ""))
			var allowed: bool = row_scene == scene_id \
				or (row_scene.is_empty() and not parent_node.is_empty() and row_region == parent_node)
			if not allowed:
				_problems.append(
					"%s：地图里有 %s，但这条判定不属于这张图（scene_id=%s region_id=%s）"
					% [scene_id, marker_name, row_scene, row_region]
				)
		print("[check] %s：Room %d / Chest %d / Trigger %d / Event %d" % [
			scene_id, _group_names(names, "Room_").size(), _group_names(names, "Chest_").size(),
			_group_names(names, "Trigger_").size(), _group_names(names, "Event_").size(),
		])

	# 宝箱 / 触发 / 判定：数量与归属都要对得上。
	var chest_expected := {}
	for row: Dictionary in MapKit.read_csv(ROOM_CSV):
		var chest_id := str(row.get("chest_id", ""))
		if chest_id.is_empty():
			continue
		var key := str(row["room_id"])
		chest_expected[key] = "Chest_" + chest_id
	_compare_room_items("Chest_", chest_expected, "dungeon_room.chest_id")

	var trigger_expected := {}
	for row: Dictionary in MapKit.read_csv(TRIGGER_CSV):
		trigger_expected[str(row["room_id"])] = "Trigger_" + str(row["trigger_id"])
	# trig_wine 在 hf1_deep 与 hf3_dungeon 各一个，表里只写了一行。
	trigger_expected["hf3_dungeon"] = "Trigger_trig_wine"
	trigger_expected["cave_02"] = "Trigger_trig_dig"
	_compare_room_items("Trigger_", trigger_expected, "hidden_trigger.trigger_id")
	_check_sequence_markers()

	var event_expected := {}
	for row: Dictionary in MapKit.read_csv(EVENT_CSV):
		var scene_id := str(row.get("scene_id", ""))
		if not scene_id.is_empty():
			event_expected[str(row["room_id"])] = "Event_" + str(row["check_id"])
	_compare_room_items("Event_", event_expected, "event_check.check_id")

	# 观察点（设计 20 §3.2／0.29.1）：表里 `flavor_point.csv` 一条一个位点，
	# 位点名 `Observe_<point_id>`。房间级的按房间比；`region_id` 那几条在大地图上，
	# 由「大地图 Marker 并集」那条统一收（前缀已进 MARKER_PREFIXES）。
	var flavor_expected := {}
	for row: Dictionary in MapKit.read_csv(FLAVOR_CSV):
		var scene_id := str(row.get("scene_id", ""))
		if scene_id.is_empty():
			continue
		flavor_expected[str(row["room_id"])] = "Observe_" + str(row["point_id"])
	_compare_room_items("Observe_", flavor_expected, "flavor_point.point_id")


## 房间内的挂钩点：同名 id 分在不同房间文件夹下，这里按「名字出现过」比对。
func _compare_room_items(label: String, expected: Dictionary, source: String) -> void:
	var found := {}
	for path: String in _scenes:
		var scene: Dictionary = _scenes[path]
		if scene.is_empty():
			continue
		for marker_name: String in scene["names"]:
			if marker_name.begins_with(label):
				found[marker_name] = true
	for room_id: String in expected:
		var marker_name: String = expected[room_id]
		# 顺序谜题（三火盆）是**同一行触发 + 三个编号位点**：`Trigger_trig_brazier_1/2/3`。
		# 所以带编号后缀的位点也算数（命名约定见 07「地图资源需求」与 `trigger_point.gd`）。
		if not found.has(marker_name) and not _has_numbered_variant(found, marker_name):
			_problems.append("%s：%s 声明了 %s，地图里没有" % [source, room_id, marker_name])


func _has_numbered_variant(found: Dictionary, marker_name: String) -> bool:
	for name: String in found:
		if not name.begins_with(marker_name + "_"):
			continue
		var suffix := name.substr(marker_name.length() + 1)
		if suffix.is_valid_int():
			return true
	return false


## 顺序谜题（三火盆）：代码侧 2026-10-03 已支持，缺的是**三个编号位点**。
## 这一条**不判失败**（缺资产不该挡住整条验收链，与雾／精英贴图同一口径），
## 但每次跑地图验收都打印出来——否则「谜题摆不上」这件事没有任何地方提醒。
func _check_sequence_markers() -> void:
	for row: Dictionary in MapKit.read_csv(TRIGGER_CSV):
		if str(row.get("trigger_type", "")) != "sequence":
			continue
		var trigger_id := str(row["trigger_id"])
		var stem := "Trigger_" + trigger_id
		var steps := 0
		for piece: String in str(row.get("required_condition", "")).split(";", false):
			var kv := piece.split("=", false)
			if kv.size() == 2 and kv[0].strip_edges() == "sequence":
				steps = maxi(steps, kv[1].strip_edges().split("-", false).size())
		var indexed := {}
		for path: String in _scenes:
			var scene: Dictionary = _scenes[path]
			if scene.is_empty():
				continue
			for marker_name: String in scene["names"]:
				if not marker_name.begins_with(stem + "_"):
					continue
				var suffix := marker_name.substr(stem.length() + 1)
				if suffix.is_valid_int():
					indexed[int(suffix)] = true
		var missing := PackedStringArray()
		for index in range(1, steps + 1):
			if not indexed.has(index):
				missing.append(str(index))
		if missing.is_empty():
			continue
		_asset_notes.append(
			"%s（%s）：需要 %d 个编号位点 %s_1…_%d，地图里缺第 %s 个——代码侧已支持（点错提示＋重置＋灭／燃两态），只差摆点"
				% [trigger_id, str(row.get("name_cn", "")), steps, stem, steps, ", ".join(missing)]
		)


# ---------------------------------------------------------------- 连通性与通路

func _check_rooms(scene: Dictionary, expected_rooms: Array, scene_id: String) -> void:
	var rects := {}
	for room_id: String in expected_rooms:
		var node: Node = scene["root"].get_node_or_null("Rooms/Room_" + room_id)
		if node == null:
			continue
		var area: Area2D = node.get_node_or_null("bounds")
		if area == null:
			_problems.append("%s：Room_%s 缺 Area2D（房间边界，物理层 5）" % [scene_id, room_id])
			continue
		var shape: CollisionShape2D = area.get_node_or_null("bounds")
		if shape == null:
			# map_kit 里 CollisionShape2D 用的是默认名，这里按类型兜一次底。
			for child in area.get_children():
				if child is CollisionShape2D:
					shape = child
					break
		if shape == null or shape.shape == null:
			_problems.append("%s：Room_%s 的房间边界没有形状" % [scene_id, room_id])
			continue
		var rect_shape: RectangleShape2D = shape.shape
		rects[room_id] = Rect2i(
			int(node.position.x) / MapKit.TILE_PX, int(node.position.y) / MapKit.TILE_PX,
			int(rect_shape.size.x) / MapKit.TILE_PX, int(rect_shape.size.y) / MapKit.TILE_PX)

	# 出口对称 + 真的走得通（在地板上洪水填充）。
	var room_rows := MapKit.rows_where(ROOM_CSV, "scene_id", scene_id)
	var reachable := _flood(scene, _entrance_cell(room_rows, rects))
	for row: Dictionary in room_rows:
		var room_id := str(row["room_id"])
		for exit_id in str(row.get("exit_rooms", "")).split("|", false):
			var target := str(exit_id).strip_edges()
			if target.is_empty() or not rects.has(target):
				continue
			var back := str(_row_for(room_rows, target).get("exit_rooms", ""))
			if not back.split("|", false).has(room_id):
				_data_notes.append("%s：%s → %s 只声明了单向（%s 的 exit_rooms 里没有 %s）；地图上走廊是双向的，但表要补回来" % [
					scene_id, room_id, target, target, room_id])
			var b: Vector2i = _rect_center(rects[target])
			if not reachable.has(b):
				_problems.append("%s：%s 到 %s 走不通（走廊被墙堵了或没连）" % [scene_id, room_id, target])
	# 没有进不去的房间。
	for room_id: String in rects:
		if not reachable.has(_rect_center(rects[room_id])):
			_problems.append("%s：房间 %s 从入口走不到" % [scene_id, room_id])
		_check_room_gap(scene, room_id, rects[room_id], scene_id)


func _entrance_cell(room_rows: Array, rects: Dictionary) -> Vector2i:
	for row: Dictionary in room_rows:
		if str(row.get("room_type", "")) in ["entrance", "boss", "battle", "elite"]:
			var room_id := str(row["room_id"])
			if rects.has(room_id):
				return _rect_center(rects[room_id])
	for row: Dictionary in room_rows:
		var room_id := str(row["room_id"])
		if rects.has(room_id):
			return _rect_center(rects[room_id])
	return Vector2i.ZERO


func _row_for(rows: Array, room_id: String) -> Dictionary:
	for row: Dictionary in rows:
		if str(row["room_id"]) == room_id:
			return row
	return {}


## 战斗房间要能「进门瞬间从侧面绕过」：房间边界上的开口至少 2 格宽。
func _check_room_gap(scene: Dictionary, room_id: String, rect: Rect2i, scene_id: String) -> void:
	var decor: TileMapLayer = scene["decor"]
	# 四条边分开数：混在一个列表里，相邻边之间会把开口打断。
	var edges := []
	var top := []
	for x in range(rect.position.x, rect.position.x + rect.size.x):
		top.append(not _blocked(decor, Vector2i(x, rect.position.y - 1)))
	edges.append(top)
	var bottom := []
	for x in range(rect.position.x, rect.position.x + rect.size.x):
		bottom.append(not _blocked(decor, Vector2i(x, rect.position.y + rect.size.y)))
	edges.append(bottom)
	var left := []
	for y in range(rect.position.y, rect.position.y + rect.size.y):
		left.append(not _blocked(decor, Vector2i(rect.position.x - 1, y)))
	edges.append(left)
	var right := []
	for y in range(rect.position.y, rect.position.y + rect.size.y):
		right.append(not _blocked(decor, Vector2i(rect.position.x + rect.size.x, y)))
	edges.append(right)

	var longest := 0
	for edge: Array in edges:
		var current := 0
		for is_open: bool in edge:
			current = current + 1 if is_open else 0
			longest = maxi(longest, current)
	if longest < 2:
		_problems.append("%s：房间 %s 的开口不足 2 格宽（侧绕突袭做不了）" % [scene_id, room_id])


## 明雷周围的通路检查（07 文档验收 3）。表里只给了警戒半径，没给朝向，
## 所以这里用「能不能绕过去」的启发式：横竖至少一个方向有 ≥2 格连续可走，
## 且 3×3 邻域里至少 5 格能站人——满足这两条才走得进背面。
func _check_spawn_room(scene: Dictionary, marker_name: String, cell: Vector2i) -> void:
	var decor: TileMapLayer = scene["decor"]
	if _blocked(decor, cell):
		_problems.append("大地图·明雷：%s 站在障碍里" % marker_name)
		return
	var best_run := 0
	for axis: Vector2i in [Vector2i.RIGHT, Vector2i.DOWN]:
		var run := 1
		for step in range(1, 4):
			if not _blocked(decor, cell + axis * step):
				run += 1
			else:
				break
		for step in range(1, 4):
			if not _blocked(decor, cell - axis * step):
				run += 1
			else:
				break
		best_run = maxi(best_run, run)
	var open_neighbours := 0
	for dy in range(-1, 2):
		for dx in range(-1, 2):
			if not _blocked(decor, cell + Vector2i(dx, dy)):
				open_neighbours += 1
	if best_run < 2 or open_neighbours < 5:
		_problems.append("大地图·明雷：%s 周围没有 2 格宽的通路（可站 %d/9，最长直通 %d）" % [
			marker_name, open_neighbours, best_run])


func _check_patrols(scene: Dictionary) -> void:
	var decor: TileMapLayer = scene["decor"]
	for path_name: String in scene["paths"]:
		var path: Path2D = scene["paths"][path_name]
		for index in path.curve.point_count:
			var point := path.curve.get_point_position(index)
			var cell := Vector2i(int(point.x) / MapKit.TILE_PX, int(point.y) / MapKit.TILE_PX)
			if _blocked(decor, cell):
				_problems.append("大地图·巡逻线：%s 的端点 %s 卡在障碍里" % [path_name, str(cell)])


func _check_walkable(scene: Dictionary, label: String, markers: Dictionary) -> void:
	var decor: TileMapLayer = scene["decor"]
	for marker_name: String in markers:
		var cell: Vector2i = markers[marker_name]
		var blocking := _blocking_tile(decor, cell)
		if blocking.x >= 0:
			_problems.append("%s：%s 的格子 %s 被障碍 %s 挡住" % [
				label, marker_name, str(cell), str(blocking)])


## 返回挡住这一格的瓦片图集坐标；没被挡返回 (-1,-1)。调试用，报错里带上坐标好定位。
func _blocking_tile(layer: TileMapLayer, cell: Vector2i) -> Vector2i:
	if layer == null:
		return Vector2i(-1, -1)
	var source_id := layer.get_cell_source_id(cell)
	if source_id == -1:
		return Vector2i(-1, -1)
	var source := layer.tile_set.get_source(source_id)
	if source == null:
		return Vector2i(-1, -1)
	var atlas: Vector2i = layer.get_cell_atlas_coords(cell)
	var data: TileData = source.get_tile_data(atlas, layer.get_cell_alternative_tile(cell))
	if data == null or not bool(data.get_custom_data("blocked")):
		return Vector2i(-1, -1)
	return atlas


# ---------------------------------------------------------------- 性能

## 固定敌人：`dungeon_room.enemy_team` 非空的房间都要有 `Enemies/<room_id>/Team_<team_id>`。
func _check_team_markers() -> void:
	for row: Dictionary in MapKit.read_csv(ROOM_CSV):
		var team_id := str(row.get("enemy_team", ""))
		if team_id.is_empty():
			continue
		var scene_id := str(row["scene_id"])
		var room_id := str(row["room_id"])
		var path := "res://scenes/maps/%s.tscn" % scene_id
		var scene: Dictionary = _scenes.get(path, {})
		if scene.is_empty():
			_problems.append("%s：%s 声明了敌人 %s，但场景读不到" % [scene_id, room_id, team_id])
			continue
		var holder := "Enemies/%s/Team_%s" % [room_id, team_id]
		if not scene["items"].has(holder):
			_problems.append("%s：%s 要有 %s（dungeon_room.enemy_team）" % [scene_id, room_id, holder])


## 回程出口：可进入的小地图都要有 `Exit_<scene_id>`，且落在入口房间。
func _check_exit_markers(local_scenes: Array) -> void:
	var entrance_by_scene := {}
	for row: Dictionary in MapKit.read_csv(ROOM_CSV):
		# 黑风寨有三层、每层一个 entrance 房间，取**第一个**才是从大地图进来的那个。
		if str(row.get("room_type", "")) == "entrance" \
				and not entrance_by_scene.has(str(row["scene_id"])):
			entrance_by_scene[str(row["scene_id"])] = str(row["room_id"])
	for scene_id: String in local_scenes:
		if scene_id == "scene_ferry_locked":
			continue  # 本章不可进入，按 07 文档 4.0 不做出口
		var scene: Dictionary = _scenes.get("res://scenes/maps/%s.tscn" % scene_id, {})
		if scene.is_empty():
			continue
		var exit_name := "Exit_" + scene_id
		var found := false
		for path: String in scene["items"]:
			if str(scene["items"][path]["name"]) != exit_name:
				continue
			found = true
			var entrance := str(entrance_by_scene.get(scene_id, ""))
			if not entrance.is_empty() and not path.contains("Markers/%s/" % entrance):
				_problems.append("%s：%s 应放在入口房间 %s 下，现在在 %s" % [
					scene_id, exit_name, entrance, path])
		if not found:
			_problems.append("%s：缺少回程出口 %s" % [scene_id, exit_name])


## 07 §9 第 12 条 ＋ §12：城镇 NPC 站位**两种命名都认**——占位 `Characters/npc_slot_0N`，
## 或按 id 绑的 `Characters/npc_<npc_id>`（对话表 0.31.0 落表后就可以这么改，代码侧已支持，见决策 332）。
##
## 没表可对不等于没得查——这里钉四件**能钉的事**：**命名**（含按 id 绑的那个 id 真的在这个地点上）、
## **没卡在障碍里**、**从出生点走得到**、**城镇的站位数量**。
## 一个卡在墙里／走不到的 NPC，等对话表到了也点不着；而这类问题在验收里原本**完全看不见**
## （`_walk` 只收 `MARKER_PREFIXES` 里那几种，`npc_slot_` 不在其中）。
func _check_npc_slots(local_scenes: Array) -> void:
	var type_by_scene := {}
	for row: Dictionary in MapKit.read_csv(LOCAL_CSV):
		type_by_scene[str(row["scene_id"])] = str(row.get("scene_type", ""))
	# 按 id 绑的位点要能核：`npc_<npc_id>` 得是 `npc_def` 里的人，而且**就在这张图上**
	# （`place_id` 命中本场景，或命中本场景的父区域节点——与 `NpcService.npcs_at` 同一口径）。
	var npc_place := {}
	for row: Dictionary in MapKit.read_csv(NPC_CSV):
		npc_place[str(row.get("npc_id", ""))] = str(row.get("place_id", ""))
	for scene_id: String in local_scenes:
		var path := "res://scenes/maps/%s.tscn" % scene_id
		var scene: Dictionary = _scenes.get(path, {})
		if scene.is_empty():
			continue
		var root: Node = scene["root"]
		var slots: Array = root.find_children("npc_*", "Node2D", true, false)
		var is_town := str(type_by_scene.get(scene_id, "")) == "town"
		if slots.is_empty():
			if is_town:
				_problems.append("%s 是城镇却没有 `Characters/npc_slot_0N`（07 §9 第 12 条／§12）" % scene_id)
			continue
		var decor: TileMapLayer = scene["decor"]
		var spawn_cell := _character_cell(root, "player_spawn")
		if spawn_cell.x < 0:
			_problems.append("%s：有 NPC 站位，但找不到 `Characters/player_spawn`（没法验「走得到」）" % scene_id)
		var reachable: Dictionary = _flood(scene, spawn_cell) if spawn_cell.x >= 0 and decor != null else {}
		for slot: Node in slots:
			var slot_name := String(slot.name)
			if slot_name.begins_with("npc_slot_"):
				var suffix := slot_name.substr("npc_slot_".length())
				if not suffix.is_valid_int():
					_problems.append("%s：%s 的编号不是数字（07 §12 约定 `npc_slot_0N`）" % [scene_id, slot_name])
			else:
				# 按 id 绑：名字本身就是 `npc_<npc_id>`
				if not npc_place.has(slot_name):
					_problems.append("%s：%s 是「按 id 绑」的位点，但 npc_def 里没有这个人（07 §九 第 12 条）" % [
						scene_id, slot_name])
				else:
					var want: String = str(npc_place[slot_name])
					if want != scene_id and want != _parent_node_of(scene_id):
						_problems.append("%s：%s 这个人不在本图（npc_def 里他绑的是 %s）——位点摆错地图了" % [
							scene_id, slot_name, want])
			var parent := slot.get_parent()
			if parent == null or String(parent.name) != "Characters":
				_problems.append("%s：%s 不在 `Characters/` 下（07 §2 的节点结构）" % [scene_id, slot_name])
			var pos := MapKit.accumulated_position(slot)
			var cell := Vector2i(int(pos.x) / MapKit.TILE_PX, int(pos.y) / MapKit.TILE_PX)
			var blocking := _blocking_tile(decor, cell)
			if blocking.x >= 0:
				_problems.append("%s：NPC 站位 %s 的格子 %s 被障碍 %s 挡住（NPC 会卡在墙里）" % [
					scene_id, slot_name, str(cell), str(blocking)])
			if not reachable.is_empty() and not reachable.has(cell):
				_problems.append("%s：NPC 站位 %s（%s）从出生点走不到——玩家点不到他" % [
					scene_id, slot_name, str(cell)])
		if is_town and slots.size() != 5:
			_asset_notes.append("%s：NPC 站位 %d 个（07 §8.6 的速览写 5 个）" % [scene_id, slots.size()])
		print("[check] %s：NPC 站位 %d 个" % [scene_id, slots.size()])


## 本场景对应的父区域节点（`map_local.parent_node`）——按 id 绑的位点允许指向父区域上的人
## （与 `NpcService.npcs_at` 的口径一致）。
func _parent_node_of(scene_id: String) -> String:
	for row: Dictionary in MapKit.read_csv(LOCAL_CSV):
		if str(row.get("scene_id", "")) == scene_id:
			return str(row.get("parent_node", ""))
	return ""


## `Characters/<name>` 落在哪个格子；找不到返回 (-1,-1)
func _character_cell(root: Node, node_name: String) -> Vector2i:
	var node: Node2D = root.get_node_or_null("Characters/%s" % node_name)
	if node == null:
		return Vector2i(-1, -1)
	var pos := MapKit.accumulated_position(node)
	return Vector2i(int(pos.x) / MapKit.TILE_PX, int(pos.y) / MapKit.TILE_PX)


## 观察点（0.31.0 起 `flavor_point` 表已上线）：这里**只查几何**——不卡在墙里、
## 且从出生点走得到，也就是设计要的「必须看得见」。
## 名字与房间归属由 `tests/test_map_assets.gd::_check_flavor_points` 按表硬校验（那边是权威）；
## 大地图那 3 条在 `_check_overworld` 里查。
func _check_observe_points(local_scenes: Array) -> void:
	var total := 0
	for scene_id: String in local_scenes:
		var scene: Dictionary = _scenes.get("res://scenes/maps/%s.tscn" % scene_id, {})
		if scene.is_empty():
			continue
		var points: Dictionary = _group_names(scene["names"], "Observe_")
		if points.is_empty():
			continue
		total += points.size()
		_check_walkable(scene, "%s·观察点" % scene_id, points)
		var spawn_cell := _character_cell(scene["root"], "player_spawn")
		if spawn_cell.x >= 0:
			var reachable: Dictionary = _flood(scene, spawn_cell)
			for point_id: String in points:
				if not reachable.has(points[point_id]):
					_problems.append("%s：观察点 %s（%s）从出生点走不到——玩家看不见它" % [
						scene_id, point_id, str(points[point_id])])
		print("[check] %s：观察点 %d 个" % [scene_id, points.size()])
	if total > 0:
		# 大地图那几处同源，**当场算**：写死「另加大地图 3 个 = 17」只会随表行数漂移
		# （0.32.0 加了渡口封渡木桩那一条，这个数字当场就过期了）。
		var world_scene: Dictionary = _scenes.get(OVERWORLD, {})
		var world_points := _group_names(world_scene.get("names", {}), "Observe_").size()
		print("[check] 小地图观察点合计 %d 个（另加大地图 %d 个 = %d）" % [
			total, world_points, total + world_points,
		])


## 建筑位点（`Markers/Buildings/<building_id>`）两道门限：
##   ① **名字必须是 `building_def` 里的 id**——代码就是按 id 查表的（`building_near_player`），
##      查不到就**静默忽略**：玩家走到店门口按 E 什么都不发生，而地图验收原本一声不吭；
##   ② **站得到跟前**——交互半径是 `BUILDING_DISTANCE`(48px)，位点周围 48px 内至少要有一个
##      「从出生点走得到」的格子，否则**这家店永远开不了**。
##
## 以前这两件一条都没查（只查了 NPC 站位／观察点／无表设施）。2026-10-04 玩家报的
## 「店铺经常打不开」里，一半是浮层挂错父节点（见 `OverlayStack.mount`），另一半正是这一类：
## 位点写错名字或离可站格太远。**按 id 查表 + 半径站得到**这两条都得在构建期拦住。
func _check_building_markers(local_scenes: Array) -> void:
	var known := {}
	for row: Dictionary in MapKit.read_csv(BUILDING_CSV):
		var building_id := str(row.get("building_id", ""))
		if not building_id.is_empty():
			known[building_id] = true
	if known.is_empty():
		_problems.append("读不到 building_def.csv——建筑位点这条门限会静默失效")
	var radius: float = float(LocalMapScript.BUILDING_DISTANCE)
	for scene_id: String in local_scenes:
		var scene: Dictionary = _scenes.get("res://scenes/maps/%s.tscn" % scene_id, {})
		if scene.is_empty():
			continue
		# 建筑位点**不在** `names` 那本字典里（它只收 `MARKER_PREFIXES` 里那些前缀的节点，
		# `bld_*` 不在其中），所以直接按控制器的取法遍历 `Markers/Buildings` 的子节点。
		var holder: Node = scene["root"].get_node_or_null("Markers/Buildings")
		if holder == null:
			continue
		var markers: Array = holder.get_children()
		if markers.is_empty():
			continue
		var spawn_cell := _character_cell(scene["root"], "player_spawn")
		var reachable: Dictionary = _flood(scene, spawn_cell) if spawn_cell.x >= 0 else {}
		if reachable.is_empty():
			_problems.append("%s：有建筑位点却从出生点走不到任何格子（没法验「站得到跟前」）" % scene_id)
		for marker: Node in markers:
			var building_id := String(marker.name)
			if not known.has(building_id):
				_problems.append("%s：建筑位点 %s 不是 building_def 里的 id——代码按 id 查表，查不到就静默忽略（走到跟前按 E 什么都不发生）" % [
					scene_id, building_id])
			var marker_px := MapKit.accumulated_position(marker)
			var nearest := -1.0
			for reach_cell: Vector2i in reachable:
				var reach_px := Vector2(
					float(reach_cell.x) * MapKit.TILE_PX + MapKit.TILE_PX * 0.5,
					float(reach_cell.y) * MapKit.TILE_PX + MapKit.TILE_PX * 0.5)
				var distance := reach_px.distance_to(marker_px)
				if nearest < 0.0 or distance < nearest:
					nearest = distance
				if nearest <= radius:
					break
			if nearest > radius:
				_problems.append("%s：建筑位点 %s（%.0f,%.0f）最近的可站格在 %.0fpx 外，超过交互半径 %.0fpx——玩家站不到跟前、这家店开不了" % [
					scene_id, building_id, marker_px.x, marker_px.y, nearest, radius])
		print("[check] %s：建筑位点 %d 个（交互半径 %.0fpx）" % [scene_id, markers.size(), radius])


## 无表设施（当铺／悬赏板／客栈）的位点名必须在控制器的 `FACILITY_LABELS` 里。
##
## 这三处在 07 文档里是「无表信息点」，所以没有表可以比对——但代码认的名字只有那三个：
## 改名（`facility_smith` 之类）会让玩家走到跟前按 E 得到「数据错」（运行时已出声，决策 242），
## 而**地图验收原本一声不吭**。这里对着控制器那份唯一白名单查，把它挪到构建期拦。
func _check_facility_markers(local_scenes: Array) -> void:
	var known: Array = LocalMapScript.FACILITY_LABELS.keys()
	for scene_id: String in local_scenes:
		var scene: Dictionary = _scenes.get("res://scenes/maps/%s.tscn" % scene_id, {})
		if scene.is_empty():
			continue
		var root: Node = scene["root"]
		var markers: Array = root.find_children("facility_*", "Node2D", true, false)
		for marker: Node in markers:
			var marker_name := String(marker.name)
			if known.has(marker_name):
				continue
			_problems.append("%s：设施位点 %s 不在代码认的清单里（%s）——改名会让这一处按 E 只得到「数据错」" % [
				scene_id, marker_name, ", ".join(PackedStringArray(known))])
		if not markers.is_empty():
			print("[check] %s：设施位点 %d 个" % [scene_id, markers.size()])


# ---------------------------------------------------------------- 性能

func _check_budget() -> void:
	for path: String in _scenes:
		var scene: Dictionary = _scenes[path]
		if scene.is_empty():
			continue
		var is_overworld: bool = path == OVERWORLD
		var max_layers: int = MAX_TILE_LAYERS_OVERWORLD if is_overworld else MAX_TILE_LAYERS
		var max_tiles: int = MAX_TILES_OVERWORLD if is_overworld else MAX_TILES
		var layers: Array = scene["layers"]
		if layers.size() > max_layers:
			_problems.append("%s 有 %d 个 TileMapLayer，超过上限 %d" % [
				path.get_file(), layers.size(), max_layers])
		var total := 0
		for layer: TileMapLayer in layers:
			total += layer.get_used_cells().size()
		if total > max_tiles:
			_problems.append("%s 瓦片用量 %d，超过上限 %d" % [path.get_file(), total, max_tiles])
		print("[check] %s：TileMapLayer %d 层 / 瓦片 %d" % [path.get_file(), layers.size(), total])


## **挡路的瓦片只许画在 `Decor` 层**（16 §3.2 的「挡路」列：Ground／Overlay／Conditional／Fog 全是「否」）。
##
## 这一条源自 2026-10-04 玩家报的「副本里无法行走」：主题 TileSet 把地板瓦片
## （`T_FLOOR`＝1,9）也列进了 solid，于是副本／洞穴的整张 Ground 都是碰撞盒——
## 玩家脚下就是墙，`move_and_slide` 一步也推不动。当时地图验收与场景自检**都是绿的**：
## verify_maps 只看 Decor 挡不挡，场景自检全靠瞬移（决策 269）。
## 那次只补了 Ground 一条；0.32.0 加了第 5 层 `Conditional`（石隙细径：整层随藏宝图显隐）
## 与「岩檐压在 Overlay 上」之后，`Overlay`／`Conditional` 同样必须干净——
## 否则玩家会在某些格子上被莫名顶住，而 `Conditional` 那层还会「拿到图才出现」。
func _check_only_decor_blocks() -> void:
	for path: String in _scenes:
		var scene: Dictionary = _scenes[path]
		if scene.is_empty():
			continue
		for layer: TileMapLayer in scene["layers"]:
			if layer.name == "Decor":
				continue
			var solid_cells := PackedStringArray()
			var total := 0
			for cell: Vector2i in layer.get_used_cells():
				if not _tile_has_collision(layer, cell):
					continue
				total += 1
				if solid_cells.size() < 5:
					solid_cells.append("(%d,%d)" % [cell.x, cell.y])
			if total == 0:
				continue
			# Ground 那次的报错措辞留着：它是玩家最可能撞上的那一种，接手的人要能一眼认出来
			var tail := (
				"挡路的瓦片要画在 Decor 层，地板瓦片不能带碰撞盒"
				if layer.name == "Ground"
				else "挡路的东西只许画在 Decor 层（16 §3.2 的分层表）"
			)
			_problems.append(
				"%s 的 %s 层有 %d 格带物理碰撞（例：%s）——"
					% [path.get_file(), layer.name, total, "、".join(solid_cells)]
				+ tail
			)


## `Conditional` 层（16 §3.2 第 5 层，0.32.0 新增）：**整层**显隐的条件地表，
## 现在唯一用途是石隙迷窟那 22 格碎石细径（绑 `item_treasure_map`，代码侧已接）。
## 地编交付后这里会打印格数；还没交付就打一行「待补」提醒（不判失败——缺资产不该挡住整条验收链）。
func _check_conditional_layers() -> void:
	var delivered := 0
	for path: String in _scenes:
		var scene: Dictionary = _scenes[path]
		if scene.is_empty():
			continue
		var layer: TileMapLayer = scene["root"].get_node_or_null("Conditional")
		if layer == null:
			continue
		delivered += 1
		print("[check] %s：Conditional 层 %d 格" % [path.get_file(), layer.get_used_cells().size()])
	if delivered == 0:
		_asset_notes.append(
			"还没有任何场景交付 `Conditional` 层（0.32.0：石隙细径 22 格铺在它上面，代码已按藏宝图显隐）"
		)


## 这一格脚下是不是真的挡路：读 TileSet 的碰撞多边形（物理真相），不读 custom_data。
func _tile_has_collision(layer: TileMapLayer, cell: Vector2i) -> bool:
	var data := layer.get_cell_tile_data(cell)
	if data == null:
		return false
	return data.get_collision_polygons_count(0) > 0


# ---------------------------------------------------------------- 基础设施

func _load(path: String) -> Dictionary:
	var packed: PackedScene = load(path)
	if packed == null:
		_problems.append("场景加载不了：%s" % path)
		return {}
	var root: Node = packed.instantiate()
	var layers := []
	var names := {}
	var paths := {}
	var items := {}
	var decor: TileMapLayer = root.get_node_or_null("Decor")
	if decor == null:
		_problems.append("%s 缺 Decor 图层" % path.get_file())
	var ground: TileMapLayer = root.get_node_or_null("Ground")
	if ground == null:
		_problems.append("%s 缺 Ground 图层" % path.get_file())
	var camera: Camera2D = root.get_node_or_null("Camera")
	if camera == null:
		_problems.append("%s 缺 Camera 节点（07 文档节点结构要求）" % path.get_file())
	# 从根节点的子节点开始走，路径里就不带场景名，
	# 于是 `Enemies/hf1_yard/Team_x`、`Markers/hf1_gate/Exit_x` 能直接当 key 用。
	for child in root.get_children():
		_walk(child, "", layers, names, paths, items)
	return {"root": root, "layers": layers, "names": names, "paths": paths, "decor": decor,
		"ground": ground, "camera": camera, "items": items}


func _walk(node: Node, parent_path: String, layers: Array, names: Dictionary, paths: Dictionary,
		items: Dictionary) -> void:
	var node_path := String(node.name) if parent_path.is_empty() else "%s/%s" % [parent_path, node.name]
	if node is TileMapLayer:
		layers.append(node)
	elif node is Path2D:
		var path: Path2D = node
		paths[String(path.name)] = path
		# 巡逻线也要参与「名字比对」，取第一个控制点当它的落位。
		if path.curve.point_count > 0:
			var head := path.curve.get_point_position(0)
			names[String(path.name)] = Vector2i(int(head.x) / MapKit.TILE_PX, int(head.y) / MapKit.TILE_PX)
	elif node is Node2D:
		# Room_ 是 Node2D（挂 Area2D 当房间边界），其余挂钩点是 Marker2D，一起按名字收集。
		for prefix in MARKER_PREFIXES:
			if String(node.name).begins_with(prefix):
				var pos := MapKit.accumulated_position(node)
				names[String(node.name)] = Vector2i(int(pos.x) / MapKit.TILE_PX, int(pos.y) / MapKit.TILE_PX)
				items[node_path] = {"name": String(node.name),
					"cell": Vector2i(int(pos.x) / MapKit.TILE_PX, int(pos.y) / MapKit.TILE_PX)}
				break
	for child in node.get_children():
		_walk(child, node_path, layers, names, paths, items)


func _group_names(names: Dictionary, prefix: String) -> Dictionary:
	var out := {}
	for marker_name: String in names:
		if marker_name.begins_with(prefix):
			out[marker_name] = names[marker_name]
	return out


## 地图里的节点名带前缀（Node_/Spawn_/…），表里是裸 id；比对时先把前缀剥掉。
func _compare(label: String, expected: Array, actual: Dictionary, prefix: String) -> void:
	var expected_set := {}
	for item in expected:
		expected_set[str(item)] = true
	var actual_ids := {}
	for node_name in actual:
		actual_ids[str(node_name).substr(prefix.length())] = true
	for key in expected_set:
		if not actual_ids.has(key):
			_problems.append("%s：配置表有 %s，地图里没有对应节点" % [label, key])
	for key in actual_ids:
		if not expected_set.has(key):
			_problems.append("%s：地图里有 %s%s，配置表查不到" % [label, prefix, key])


func _column(path: String, column: String, filter_column: String, value: String) -> Array:
	var out := []
	for row: Dictionary in MapKit.read_csv(path):
		if filter_column.is_empty() or str(row.get(filter_column, "")) == value:
			out.append(str(row.get(column, "")))
	return out


func _blocked(layer: TileMapLayer, cell: Vector2i) -> bool:
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
	if data == null:
		return false
	return bool(data.get_custom_data("blocked"))


## 从起点在地板上洪水填充（只看 Decor 是否挡路，不限图层），返回能走到的格子。
func _flood(scene: Dictionary, origin: Vector2i) -> Dictionary:
	var decor: TileMapLayer = scene["decor"]
	# 必须限定在地图范围内：户外场景没有完整墙壳，不设边界会一路扩散到无穷。
	var bounds := decor.get_used_rect().grow(2)
	var seen := {}
	var queue := [origin]
	seen[origin] = true
	while not queue.is_empty():
		var cell: Vector2i = queue.pop_back()
		for offset: Vector2i in [Vector2i.RIGHT, Vector2i.LEFT, Vector2i.UP, Vector2i.DOWN]:
			var next := cell + offset
			if not bounds.has_point(next):
				continue
			if seen.has(next) or _blocked(decor, next):
				continue
			seen[next] = true
			queue.append(next)
	return seen


func _rect_center(rect: Rect2i) -> Vector2i:
	return Vector2i(rect.position.x + rect.size.x / 2, rect.position.y + rect.size.y / 2)
