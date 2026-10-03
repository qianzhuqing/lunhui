## 地图资源自检：场景里的 Marker 与配置表双向比对。
##
## 这是 07_地图资源需求.md 第二节承诺的那条自检——地编的场景进仓库后由开发侧卡住：
##   * 表里有、地图没有 → 少放（或名字写错）
##   * 地图有、表里没有 → 多放（或名字写错）
##   * 放错房间/区域 → 归属不对
##
## 关键实现细节：**Marker 用列表存，不能按名字去重**。同一个 id 会在不同房间重复出现
## （两个 `Chest_drop_chest_silver`、两处 `Trigger_trig_wine`），去重会少算，
## 所以每一项都要按「名字 + 所在房间」两段来核对。
extends "res://tests/test_case.gd"

const OVERWORLD := "res://scenes/maps/overworld.tscn"
const PARENT_REGION := "jiangnan_east"
const ALL_SCENES := [
	OVERWORLD,
	"res://scenes/maps/scene_qingfengyi.tscn",
	"res://scenes/maps/scene_cave.tscn",
	"res://scenes/maps/scene_heifengzhai.tscn",
	"res://scenes/maps/scene_huangcun.tscn",
	"res://scenes/maps/scene_ferry_locked.tscn",
]

## Team_ / Exit_ 必须也在白名单里：`_collect` 只收白名单前缀的节点，
## 少了这两个，地编就算把位点摆好，`_check_pending_requirements` 也永远找不到它们。
const MARKER_PREFIXES := [
	"Node_", "Portal_", "Room_", "Chest_", "Spawn_", "Event_", "Trigger_", "Patrol_",
	"Team_", "Exit_",
]

## 2026-10-02 审计后补进 07_地图资源需求.md 的两条约定，地编已交付：
## `Enemies/<room_id>/Team_<team_id>`（7 个）与 `Exit_<scene_id>`（4 个，渡口不开放不做）。
## 开关关掉后这两条就是硬失败，少一个位点自检就会红。
const PENDING_ENEMY_MARKERS := false
const PENDING_EXIT_MARKERS := false


func suite_name() -> String:
	return "地图资源与表 id 比对"


func run() -> void:
	var db = get_db()
	if not FileAccess.file_exists(OVERWORLD):
		fail("没有大地图：%s" % OVERWORLD)
		return
	_check_overworld(db)
	_check_local_scenes(db)
	_check_events_exist(db, _load_all_markers())
	_check_pending_requirements(db)
	_check_theme_tiles()


## 主题 TileSet 的碰撞必须与 `MapKit.THEMES` 的 solid 清单**逐格一致**，而且地板永远不许挡路。
##
## 2026-10-04 玩家报的「副本里无法行走」就出在这条不变量上：`TILES_STONE` 一份清单同时当
## 「要收进图集的瓦片」和「挡路的瓦片」用，地板瓦片（1,9）被写成实心格——副本／洞穴的
## Ground 层整片都是碰撞盒，玩家横着走不动（纵向那点位移是被挤出来的假自由）。
## 地图验收与场景自检当时都是绿的：前者只看 Decor、后者全靠瞬移（见 `框架说明.md` 决策 269）。
func _check_theme_tiles() -> void:
	var kit = load("res://tools/mapgen/map_kit.gd")
	check_false(kit == null, "读得到地图工具箱 map_kit.gd")
	if kit == null:
		return
	for theme: String in kit.THEMES:
		var path: String = kit.theme_tileset_path(theme)
		var tile_set: TileSet = load(path)
		check_true(tile_set != null, "主题 %s 的 TileSet 能加载（%s）" % [theme, path])
		if tile_set == null:
			continue
		var solid: Array = kit.THEMES[theme]["solid"]
		var source: TileSetAtlasSource = tile_set.get_source(kit.SOURCE_ID) as TileSetAtlasSource
		check_true(source != null, "主题 %s 的图集源存在" % theme)
		if source == null:
			continue
		for tile: Vector2i in kit.THEMES[theme]["tiles"]:
			if not source.has_tile(tile):
				continue
			var data := source.get_tile_data(tile, 0)
			var has_collision: bool = data.get_collision_polygons_count(0) > 0
			check_eq(has_collision, solid.has(tile),
				"主题 %s 瓦片 %s：碰撞盒与 solid 清单一致" % [theme, str(tile)])
		for floor_tile: Vector2i in [kit.T_FLOOR, kit.T_FLOOR_TOP]:
			check_false(solid.has(floor_tile),
				"主题 %s 的地板 %s 不许挡路（solid 清单只收墙）" % [theme, str(floor_tile)])


## 新加的两条约定：敌人位点与回程出口
func _check_pending_requirements(db) -> void:
	var pending := PackedStringArray()
	var cache: Dictionary = {}

	# 1. 房间内的固定敌人：Enemies/<room_id>/Team_<team_id>
	for row: Resource in db.rows("dungeon_room"):
		var team_id := str(row.enemy_team)
		if team_id.is_empty():
			continue
		var scene_id := str(row.scene_id)
		var markers := _scene_markers(scene_id, cache)
		var room_id := str(row.room_id)
		var wanted := "Team_%s" % team_id
		var found := false
		for marker: Dictionary in markers:
			if str(marker["name"]) == wanted and str(marker["path"]).contains("Enemies/%s/" % room_id):
				found = true
		if not found:
			pending.append("敌人位点 Enemies/%s/%s" % [room_id, wanted])

	# 2. 小地图的回程出口：Exit_<scene_id>
	for row: Resource in db.rows("map_local"):
		var scene_id := str(row.scene_id)
		if scene_id == "scene_ferry_locked":
			continue  # 本章不可进入
		var exit_name := "Exit_%s" % scene_id
		var markers := _scene_markers(scene_id, cache)
		if not _has_marker(markers, exit_name):
			pending.append("回程出口 %s" % exit_name)
			continue
		var entrance := _entrance_room(db, scene_id)
		if not entrance.is_empty() and not _has_in_room(markers, exit_name, entrance):
			pending.append("回程出口 %s 应放在入口房间 %s" % [exit_name, entrance])

	if pending.is_empty():
		return
	if PENDING_ENEMY_MARKERS or PENDING_EXIT_MARKERS:
		print("  [地图自检·待补] %d 项约定尚未交付：%s" % [pending.size(), "、".join(pending)])
		return
	for item: String in pending:
		fail("%s 未交付（见 07_地图资源需求.md）" % item)


func _scene_markers(scene_id: String, cache: Dictionary) -> Array:
	if not cache.has(scene_id):
		cache[scene_id] = _load_markers("res://scenes/maps/%s.tscn" % scene_id)
	return cache[scene_id]


## 场景里的入口房间（room_type=entrance）；城镇没有房间行，返回空串
func _entrance_room(db, scene_id: String) -> String:
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == scene_id and str(row.room_type) == "entrance":
			return str(row.room_id)
	return ""


# ------------------------------------------------------------------ 大地图

func _check_overworld(db) -> void:
	var markers := _load_markers(OVERWORLD)
	if markers.is_empty():
		fail("大地图读不出 Marker")
		return

	var nodes := _prefix_rows(db, "map_region", "node_id", "Node_", "parent_region", PARENT_REGION)
	var portals := _prefix_rows(db, "map_local", "scene_id", "Portal_", "", "")
	var spawns := _prefix_rows(db, "roaming_spawn", "spawn_id", "Spawn_", "", "")
	var patrols := PackedStringArray()
	for row: Resource in db.rows("roaming_spawn"):
		var path_id := str(row.patrol_path_id)
		if not path_id.is_empty():
			var name := "Patrol_%s" % path_id
			if not patrols.has(name):
				patrols.append(name)
	_compare(names_with(markers, "Node_"), nodes, "大地图 Node_")
	_compare(names_with(markers, "Portal_"), portals, "大地图 Portal_")
	_compare(names_with(markers, "Spawn_"), spawns, "大地图 Spawn_")
	_compare(names_with(markers, "Patrol_"), patrols, "大地图 Patrol_")
	# 判定位点只查「有没有落点」与「房间级是否落在正确房间」，
	# 因为「寨墙在野外、赌桌在客栈」这种归属表里没编码，不强求它必须在大地图上。

	check_eq(names_with(markers, "Node_").size(), 7, "大地图地标 7 处")
	check_eq(names_with(markers, "Portal_").size(), 5, "大地图入口 5 处")
	check_eq(names_with(markers, "Spawn_").size(), 17, "大地图明雷 17 处")
	check_eq(names_with(markers, "Patrol_").size(), 4, "巡逻线 4 条")

	# 地标坐标要跟表里一致
	var positions := _load_marker_positions(OVERWORLD)
	for row: Resource in db.rows("map_region"):
		if str(row.parent_region) != PARENT_REGION:
			continue
		var marker := "Node_%s" % row.node_id
		if not positions.has(marker):
			continue
		var expected := Vector2(float(row.pos_x), float(row.pos_y))
		var actual: Vector2 = positions[marker]
		check_lt((actual - expected).length(), 1.0, "%s 坐标与 map_region 一致" % marker)


# ------------------------------------------------------------------ 小地图与房间

func _check_local_scenes(db) -> void:
	for scene_row: Resource in db.rows("map_local"):
		var scene_id := str(scene_row.scene_id)
		var path := "res://scenes/maps/%s.tscn" % scene_id
		if not FileAccess.file_exists(path):
			fail("map_local.csv 里的 %s 没有对应场景：%s" % [scene_id, path])
			continue
		var markers := _load_markers(path)
		var rooms := PackedStringArray()
		for room_row: Resource in db.rows("dungeon_room"):
			if str(room_row.scene_id) != scene_id:
				continue
			rooms.append("Room_%s" % room_row.room_id)
			_check_room_markers(db, scene_id, room_row, markers)
		_check_triggers(db, scene_id, markers)
		_compare(names_with(markers, "Room_"), rooms, "%s Room_" % scene_id)
		# 不该有不属于本场景的 Room_
		for marker: Dictionary in markers:
			var name: String = marker["name"]
			if not name.begins_with("Room_"):
				continue
			var row: Resource = db.get_row("dungeon_room", name.substr("Room_".length()))
			if row == null or str(row.scene_id) != scene_id:
				fail("%s 里多出房间 %s（表里不属于这个场景）" % [scene_id, name])


## 单个房间：宝箱、隐藏触发、房间级判定要挂在 `Markers/<room_id>/` 下面
func _check_room_markers(db, scene_id: String, room_row: Resource, markers: Array) -> void:
	var room_id := str(room_row.room_id)
	var expected := PackedStringArray()
	var chest_id := str(room_row.chest_id)
	if not chest_id.is_empty():
		expected.append("Chest_%s" % chest_id)
	for event_row: Resource in db.rows("event_check"):
		if str(event_row.room_id) == room_id:
			expected.append("Event_%s" % event_row.check_id)

	for name: String in expected:
		if not _has_in_room(markers, name, room_id):
			fail("%s/%s 缺少 %s" % [scene_id, room_id, name])

	# 反向：这个房间下不该有多余的宝箱与触发
	for marker: Dictionary in markers:
		var name: String = marker["name"]
		if str(marker["room"]) != room_id or name.begins_with("Room_"):
			continue
		if name.begins_with("Chest_") and not expected.has(name):
			fail("%s/%s 多出 %s（表里没有）" % [scene_id, room_id, name])


## 隐藏触发：允许的落点是「hidden_trigger.room_id」加上「dungeon_room.hidden_trigger 指向它的那些房间」。
## 例：三火盆的交互点在火盆厅（hidden_trigger.room_id），而火盆密室只是「被它打开的房间」。
func _check_triggers(db, scene_id: String, markers: Array) -> void:
	for trigger_row: Resource in db.rows("hidden_trigger"):
		if str(trigger_row.scene_id) != scene_id:
			continue
		var trigger_id := str(trigger_row.trigger_id)
		var name := "Trigger_%s" % trigger_id
		var allowed := PackedStringArray()
		if not str(trigger_row.room_id).is_empty():
			allowed.append(str(trigger_row.room_id))
		for room_row: Resource in db.rows("dungeon_room"):
			if str(room_row.scene_id) == scene_id and str(room_row.hidden_trigger) == trigger_id:
				if not allowed.has(str(room_row.room_id)):
					allowed.append(str(room_row.room_id))
		var found := false
		for marker: Dictionary in markers:
			if str(marker["name"]) != name:
				continue
			if allowed.has(str(marker["room"])):
				found = true
			else:
				fail("%s 的 %s 放在 %s，表里允许的位置是 %s" % [
					scene_id, name, marker["room"], ", ".join(allowed),
				])
		if not found:
			fail("%s 缺少 %s（允许位置：%s）" % [scene_id, name, ", ".join(allowed)])


## 每条事件判定都要在地图里找到落点；有 room_id 的必须落在那个房间
func _check_events_exist(db, all_markers: Array) -> void:
	for row: Resource in db.rows("event_check"):
		var name := "Event_%s" % row.check_id
		if not _has_marker(all_markers, name):
			fail("event_check[%s] 在所有地图里都没有落点" % row.check_id)
			continue
		var room_id := str(row.room_id)
		if not room_id.is_empty():
			check_true(_has_in_room(all_markers, name, room_id), "%s 落在 %s" % [name, room_id])


# ------------------------------------------------------------------ 工具

## 收集场景里所有 Marker，返回 [{name, path, room}, ...]
func _load_markers(path: String) -> Array:
	var packed: PackedScene = load(path)
	if packed == null:
		fail("场景加载失败：%s" % path)
		return []
	var root := packed.instantiate()
	var markers: Array = []
	_collect(root, "", markers)
	root.free()
	return markers


func _collect(node: Node, parent_path: String, out: Array) -> void:
	for child in node.get_children():
		var child_path := str(child.name) if parent_path.is_empty() else "%s/%s" % [parent_path, child.name]
		if _is_marker(str(child.name)):
			out.append({"name": str(child.name), "path": child_path, "room": _room_of(child_path)})
		_collect(child, child_path, out)


func _load_marker_positions(path: String) -> Dictionary:
	var packed: PackedScene = load(path)
	if packed == null:
		return {}
	var root := packed.instantiate()
	var out: Dictionary = {}
	_collect_positions(root, out)
	root.free()
	return out


func _collect_positions(node: Node, out: Dictionary) -> void:
	for child in node.get_children():
		if child is Marker2D and _is_marker(str(child.name)):
			out[str(child.name)] = (child as Marker2D).position
		_collect_positions(child, out)


## 六张地图的 Marker 并集，用于「表里的东西到底有没有落点」
func _load_all_markers() -> Array:
	var out: Array = []
	for path: String in ALL_SCENES:
		if not FileAccess.file_exists(path):
			continue
		out.append_array(_load_markers(path))
	return out


## Marker 自身路径 Markers/hf1_shed/Chest_x → hf1_shed；大地图的 Markers/Node_x 返回空
func _room_of(marker_path: String) -> String:
	var parts := marker_path.split("/")
	if parts.size() >= 3 and parts[parts.size() - 3] == "Markers":
		return parts[parts.size() - 2]
	return ""


func _is_marker(node_name: String) -> bool:
	for prefix: String in MARKER_PREFIXES:
		if node_name.begins_with(prefix):
			return true
	return false


func names_with(markers: Array, prefix: String) -> PackedStringArray:
	var out := PackedStringArray()
	for marker: Dictionary in markers:
		if str(marker["name"]).begins_with(prefix):
			out.append(str(marker["name"]))
	return out


func _has_marker(markers: Array, name: String) -> bool:
	for marker: Dictionary in markers:
		if str(marker["name"]) == name:
			return true
	return false


func _has_in_room(markers: Array, name: String, room_id: String) -> bool:
	for marker: Dictionary in markers:
		if str(marker["name"]) == name and str(marker["room"]) == room_id:
			return true
	return false


## 表里某列加上 Marker 前缀作为期望集合（可带一列过滤条件）
func _prefix_rows(
	db, table_name: String, column: String, prefix: String,
	filter_column: String, filter_value: String
) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows(table_name):
		if not filter_column.is_empty() and str(row.get(filter_column)) != filter_value:
			continue
		out.append("%s%s" % [prefix, str(row.get(column))])
	return out


func _compare(actual: PackedStringArray, expected: PackedStringArray, label: String) -> void:
	for name: String in expected:
		if not actual.has(name):
			fail("%s 缺少 %s" % [label, name])
	for name: String in actual:
		if not expected.has(name):
			fail("%s 多出 %s（表里没有）" % [label, name])
