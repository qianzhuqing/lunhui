## 生成五张小地图：清风驿（城镇）、塌陷山洞、荒村、废弃渡口、石隙迷窟。
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
	_build_shixi()
	if not _failures.is_empty():
		for message: String in _failures:
			printerr("  - " + message)
		return 1
	print("[mapgen] MAPGEN: OK（清风驿 / 塌陷山洞 / 荒村 / 废弃渡口 / 石隙迷窟）")
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
	# 0.31.0 新增：书铺。陆文昭（书生）的「本命机遇·书箱底」落在这一处（21 号 §九），
	# 与当铺／悬赏板一样是**无表信息点**——只有地上的可交互物，不进 building_def。
	{"name": "facility_bookshop", "cell": Vector2i(14, 20), "bookshop": true},
]

const TOWN_NPC_SLOTS := [
	Vector2i(14, 17), Vector2i(22, 17), Vector2i(15, 14),
	Vector2i(26, 16), Vector2i(21, 24),
]

## 观察点（0.29.1）：`Observe_<point_id>`，名字是临时名，等 `flavor_point` 表落地再按真 id 改。
## 悬赏板（19,22 的牌子＋20,22 的木箱）与客栈（24,20 的招牌＋25,20 的木箱）旁边各摆几个。
const TOWN_OBSERVE := {
	"Observe_ob_qingfengyi_board_01": Vector2i(18, 22),   # 告示的纸边
	"Observe_ob_qingfengyi_board_02": Vector2i(20, 23),   # 墙上的旧浆糊
	"Observe_ob_qingfengyi_inn_01": Vector2i(23, 21),     # 门框上的刀痕
	"Observe_ob_qingfengyi_inn_02": Vector2i(25, 21),     # 燕小七那把无鞘的刀
	"Observe_ob_qingfengyi_inn_03": Vector2i(24, 22),     # 门槛上的骰子
}


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
		if bool(facility.get("bookshop", false)):
			# 书铺要显眼：书架＋账桌＋摊前的招牌／纸笔（都用既有瓦片，暖色提亮归美术）。
			decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_CRATE)              # 书架
			decor.set_cell(cell + Vector2i(1, 0), MapKit.SOURCE_ID, MapKit.T_BENCH)   # 账桌
			decor.set_cell(cell + Vector2i(1, 1), MapKit.SOURCE_ID, MapKit.T_SIGN)    # 纸笔摊
			continue
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
		reserved[facility["cell"] + Vector2i(0, 1)] = true
	for cell: Vector2i in TOWN_NPC_SLOTS:
		reserved[cell] = true
	for point_id: String in TOWN_OBSERVE:
		reserved[TOWN_OBSERVE[point_id]] = true
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
	for point_id: String in TOWN_OBSERVE:
		MapKit.add_marker(markers, point_id, TOWN_OBSERVE[point_id])
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
		_add_npc_slot(characters, index + 1, TOWN_NPC_SLOTS[index])
	var spawn := MapKit.add_marker(characters, "player_spawn", Vector2i(TOWN_CROSS_X, TOWN_H - 3))
	spawn.set_meta("role", "player")


## NPC 站位：Marker2D + 占位贴图。现在只摆位置，等对话表落地再按 id 绑身份（07 §十一 待补第 4 条）。
func _add_npc_slot(characters: Node2D, index: int, cell: Vector2i) -> void:
	var marker := MapKit.add_marker(characters, "npc_slot_%02d" % index, cell)
	MapKit.add_sprite(marker, "placeholder", MapKit.NPC_TEXTURE)


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
## 废屋南侧的院子：白清和蹲在院外给死者盖白布（0.29.0 的 20_第一章剧情），
## 位置要「看得见但不挡路」，所以给他一片接在 hc_02 南边的小院。
const VILLAGE_YARD := [Rect2i(24, 21, 8, 4)]
const VILLAGE_NPC_SLOTS := [
	Vector2i(27, 22),   # 白清和：院外
]

## 观察点（0.29.1）：门槛的靴印／烧断的梁／井沿。
## 「看得见」是硬要求，所以这两处的实物也由这里补：村口一口枯井、废屋里一根烧断的梁。
const VILLAGE_OBSERVE := {
	"hc_02": {
		"Observe_ob_huangcun_door_01": Vector2i(23, 16),   # 门槛上的靴印
		"Observe_ob_huangcun_beam_01": Vector2i(29, 12),   # 烧断的梁
	},
	"hc_01": {
		"Observe_ob_huangcun_well_01": Vector2i(8, 16),    # 井沿
	},
}


func _build_huangcun() -> void:
	var shell := _dungeon_shell(VILLAGE_SCENE, "village", VILLAGE_W, VILLAGE_H, VILLAGE_ROOMS,
		[["hc_01", "hc_02"]], VILLAGE_YARD)
	if shell.is_empty():
		return
	_room_marker(shell, "Markers", "hc_01", EXIT_PREFIX + VILLAGE_SCENE, Vector2i(4, 19))
	_room_marker(shell, "Markers", "hc_02", "Chest_drop_chest_silver", Vector2i(33, 17))
	_team_marker(shell, "hc_02", "team_butcher")
	# 观察点的实物：枯井与烧断的梁（都用既有瓦片，不加新素材）。
	shell["decor"].set_cell(Vector2i(8, 15), MapKit.SOURCE_ID, MapKit.T_WELL)
	shell["decor"].set_cell(Vector2i(30, 12), MapKit.SOURCE_ID, MapKit.T_FENCE_V)
	for room_id: String in VILLAGE_OBSERVE:
		for point_id: String in VILLAGE_OBSERVE[room_id]:
			_room_marker(shell, "Markers", room_id, point_id, VILLAGE_OBSERVE[room_id][point_id])
	for index in VILLAGE_NPC_SLOTS.size():
		_add_npc_slot(shell["characters"], index + 1, VILLAGE_NPC_SLOTS[index])
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(4, 16)).set_meta("role", "player")
	_fit(shell, VILLAGE_W, VILLAGE_H)
	_finish(shell["root"], VILLAGE_SCENE, VILLAGE_W, VILLAGE_H)
	print("[mapgen] 荒村：房间 2（hc_01/hc_02）")


# ---------------------------------------------------------------- 废弃渡口

const FERRY_SCENE := "scene_ferry_locked"
const FERRY_W := 32
const FERRY_H := 24
## 渡口那條河（16 §3.5：废弃渡口＝**水岸**——水面 ＋ 栈桥残骸 ＋ 芦苇）。
## 封渡的牌坊与墙在河南岸，玩家从南边的街走过来，到墙为止。
const FERRY_RIVER := Rect2i(1, 1, 26, 8)


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
	var river := _paint_ferry_river(ground, decor)
	# 本章不开放：牌坊 + 破船 + 断桥封锁，一眼看出过不去。
	for offset in range(-2, 3):
		decor.set_cell(Vector2i(12 + offset, 10), MapKit.SOURCE_ID,
			MapKit.T_WALL_TOP_CORNER if abs(offset) == 2 else MapKit.T_BATTLEMENT)
	decor.set_cell(Vector2i(12, 11), MapKit.SOURCE_ID, MapKit.T_ARCH)
	decor.set_cell(Vector2i(20, 13), MapKit.SOURCE_ID, MapKit.T_BARREL)
	decor.set_cell(Vector2i(21, 14), MapKit.SOURCE_ID, MapKit.T_CRATE)
	for x in range(4, 12):
		decor.set_cell(Vector2i(x, 15), MapKit.SOURCE_ID, MapKit.T_FENCE_H)
	# 栈桥残骸：断掉的三根桥桩伸进浅水，中间那根没了——「一眼看出过不去」（07 §4.4）。
	decor.set_cell(Vector2i(11, 7), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	decor.set_cell(Vector2i(15, 7), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	decor.set_cell(Vector2i(17, 6), MapKit.SOURCE_ID, MapKit.T_FENCE_POST)
	# 封渡木桩（0.32.0）：美术交付的 **prop**（`prop_ferry_pile.png`，32×32，木桩 ＋ 钉在桩上的
	# 朱印木牌一起出图），挂在观察点位点上——「醉刀客线索改走主线条」的第三条就刻在这根桩上。
	# 摆在封渡牌坊东侧两格的街上（玩家出镇往渡口走，正好经过它）。
	# 桩**不挡路**：验收要求观察点本身可走、且从出生点走得到；以前那两个占位桩 ＋ 通用木牌
	# （`T_FENCE_POST`／`T_SIGN`）由这张图取代。
	# 图的路径**只在 `map_kit.PROP_FERRY_PILE` 写一次**，这里只引用常量。
	var pile := MapKit.add_marker(shell["markers"], "Observe_ob_dukou_pile_04", Vector2i(15, 12))
	MapKit.add_sprite(pile, "pile", MapKit.PROP_FERRY_PILE)
	# 密林封边不能长到河里（河道自己就是天然边界）。
	var keep_open := streets.duplicate()
	for cell: Vector2i in river:
		keep_open[cell] = true
	MapKit.forest_border(decor, keep_open, FERRY_W, FERRY_H, 2)
	# 本章不可进入，按 07 文档 4.0 不做回程出口。
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(5, 14)).set_meta("role", "player")
	_fit(shell, FERRY_W, FERRY_H)
	_finish(shell["root"], FERRY_SCENE, FERRY_W, FERRY_H, streets, ground, decor)
	print("[mapgen] 废弃渡口：1 区域（河道 ＋ 栈桥残骸 ＋ 封锁表现）／封渡木桩 prop ＋ 观察点 1 处")


## 渡口的水。**深水画在 `Decor`**——它挡路，而 `Ground` 按约定只能放「走得上去的地面」
## （`verify_maps._check_only_decor_blocks` 逐层盯着这条，`Ground`／`Overlay`／`Conditional` 都在内）；
## 浅水／水岸／芦苇不挡路，留在 `Ground`。
## 返回整片水的格子（含岸边芦苇），好让密林封边绕开河道。
func _paint_ferry_river(ground: TileMapLayer, decor: TileMapLayer) -> Dictionary:
	var cells := {}
	var last_y := FERRY_RIVER.position.y + FERRY_RIVER.size.y - 1
	for y in range(FERRY_RIVER.position.y, last_y + 1):
		for x in range(FERRY_RIVER.position.x, FERRY_RIVER.position.x + FERRY_RIVER.size.x):
			var cell := Vector2i(x, y)
			cells[cell] = true
			if y <= last_y - 2:
				decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_WATER_DEEP)
			elif y == last_y - 1:
				ground.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_WATER_SHALLOW)
			else:
				ground.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_WATER_SHORE)
	# 岸边芦苇荡（在 `Ground` 上，能走进去——07 §4.4 的侦察靠它）
	for at: Vector2i in [Vector2i(4, 8), Vector2i(6, 9), Vector2i(9, 9), Vector2i(19, 9),
			Vector2i(22, 9), Vector2i(25, 8)]:
		ground.set_cell(at, MapKit.SOURCE_ID, MapKit.T_REEDS)
		cells[at] = true
	return cells


# ---------------------------------------------------------------- 副本通用

# ---------------------------------------------------------------- 石隙迷窟

const SHIXI_SCENE := "scene_shixi"
const SHIXI_W := 36
const SHIXI_H := 26

## 六间房：一条主缝（入口→石廊→岔口）分出两条岔缝（空室／遗篇石台），
## 入口另接一条往塌方的岔缝。房间矩形是地编排的，出口关系来自 dungeon_room.csv。
const SHIXI_ROOMS := [
	{"id": "mz_01", "rect": Rect2i(2, 10, 6, 5)},    # 石隙入口：极窄的入口缝
	{"id": "mz_02", "rect": Rect2i(11, 9, 12, 6)},   # 石廊：一线天光主要落在这间
	{"id": "mz_03", "rect": Rect2i(3, 19, 8, 6)},    # 塌方：死路，但有银箱
	{"id": "mz_04", "rect": Rect2i(26, 9, 8, 7)},    # 岔口：两条岔路一眼看出是两条
	{"id": "mz_05", "rect": Rect2i(24, 2, 7, 5)},    # 空室：死路（往东留出收窄的余地）
	{"id": "mz_06", "rect": Rect2i(26, 19, 8, 6)},   # 遗篇石台：隐藏终点
]

## 两处死路都按美术的示意画（`docs/dev/images/石隙死路画法_示意.png`）：
## 先接一段 2 格高的缝，**尽头再收成 1 格宽的窄缝**——看着像还能挤过去，
## 「死」要等玩家走到最里面才发现（07 §4.6／16 §4.8）。
const SHIXI_STUBS := [
	Rect2i(11, 21, 5, 2), Rect2i(16, 21, 3, 1),   # mz_03：往东 5 格，再收成 3 格单格缝
	Rect2i(31, 3, 3, 2), Rect2i(34, 3, 2, 1),     # mz_05：往东 3 格，再收成 2 格单格缝
]

## 入口缝给 2 格宽（极窄），其余主缝 3 格。
const SHIXI_LINKS := [
	["mz_01", "mz_02", 2], ["mz_02", "mz_04"],
	["mz_04", "mz_05"], ["mz_04", "mz_06"],
	["mz_01", "mz_03"],
]


func _build_shixi() -> void:
	# 美术已交付 shixi 主题（assets/tilesets/ink_jianghu/shixi），不再借用 cave。
	var shell := _dungeon_shell(SHIXI_SCENE, "shixi", SHIXI_W, SHIXI_H,
		SHIXI_ROOMS, SHIXI_LINKS, SHIXI_STUBS)
	if shell.is_empty():
		return
	_paint_shixi_details(shell)
	# 出生点在入口缝里；回程出口压在入口房间（出生点与出口贴在一起是合法摆法）。
	MapKit.add_marker(shell["characters"], "player_spawn", Vector2i(5, 12)).set_meta("role", "player")
	_room_marker(shell, "Markers", "mz_01", EXIT_PREFIX + SHIXI_SCENE, Vector2i(3, 12))
	_team_marker(shell, "mz_02", "team_shixi_hound")
	_team_marker(shell, "mz_04", "team_shixi_hound")
	_room_marker(shell, "Markers", "mz_03", "Chest_drop_chest_silver", Vector2i(5, 21))
	_room_marker(shell, "Markers", "mz_06", "Trigger_trig_shixi_reward", Vector2i(30, 22))
	_fit(shell, SHIXI_W, SHIXI_H)
	_finish(shell["root"], SHIXI_SCENE, SHIXI_W, SHIXI_H)
	print("[mapgen] 石隙迷窟：房间 6（mz_01~mz_06）／两处死路带窄缝／天光在 mz_02 与 mz_06")


## 石隙迷窟的美术要点（07 §4.6、16 §4.8）：一线天光与岩檐走 Overlay（不挡路），
## 前朝石台与碎石堆走 Decor；塌方尽头用碎石堆收口，空室尽头什么都不放（只是没路了）。
func _paint_shixi_details(shell: Dictionary) -> void:
	var decor: TileMapLayer = shell["decor"]
	var overlay: TileMapLayer = shell["overlay"]
	# 主缝的一线天：石廊整整一条
	for x in range(12, 22):
		overlay.set_cell(Vector2i(x, 10), MapKit.SOURCE_ID, MapKit.T_SKY_SLIT)
	# 终点：天光落在前朝石台上（石台不挡路，玩家能站上去）
	for x in range(28, 32):
		overlay.set_cell(Vector2i(x, 20), MapKit.SOURCE_ID, MapKit.T_SKY_SLIT)
	for x in range(29, 32):
		decor.set_cell(Vector2i(x, 22), MapKit.SOURCE_ID, MapKit.T_SHRINE_PLATFORM)
	# 塌方：银箱上方那束光比别处亮（诱饵），碎石堆在窄缝最末端（不是门口）
	overlay.set_cell(Vector2i(5, 20), MapKit.SOURCE_ID, MapKit.T_SKY_SLIT)
	decor.set_cell(Vector2i(18, 21), MapKit.SOURCE_ID, MapKit.T_RUBBLE)
	decor.set_cell(Vector2i(17, 21), MapKit.SOURCE_ID, MapKit.T_RUBBLE)
	# 两条死路的**内段**都压岩檐（A9 拍板选方案 ①，0.32.0）：玩家在岔口只看得到黑，
	# 走进去（程序把 Overlay 半透明化）才看见尽头。**岔口那一格留亮**——要让人看得见
	# 「这里有条路可走」；盖住整条就变成一堵墙，迷宫反而露馅（16 §4.8）。
	for cell: Vector2i in _shixi_eaves():
		if (shell["floor"] as Dictionary).has(cell):
			overlay.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_ROCK_EAVE)
	# 墙角碎石当纹理（避开位点与房间开口）
	for cell: Vector2i in [Vector2i(27, 10), Vector2i(21, 13), Vector2i(32, 23)]:
		decor.set_cell(cell, MapKit.SOURCE_ID, MapKit.T_RUBBLE)


## 两处死路要压岩檐的格子：**从岔口往里第二格起**，一直盖到尽头。
## `mz_03`（塌方）的岔口在 x=11，`mz_05`（空室）的窄缝从 x=31 起——两处都按同一口径：
## 露一格进深、盖住后段，所以「站在岔口看不见尽头」，而缝宽与真岔路完全一致。
func _shixi_eaves() -> Array:
	var cells := []
	for x in range(13, 19):
		cells.append(Vector2i(x, 21))
		cells.append(Vector2i(x, 22))
	for x in range(29, 36):
		cells.append(Vector2i(x, 3))
		cells.append(Vector2i(x, 4))
	return cells


# ---------------------------------------------------------------- 副本通用

func _dungeon_shell(scene_id: String, theme: String, map_w: int, map_h: int,
		room_specs: Array, links: Array, extra_floor: Array = []) -> Dictionary:
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
		# 第三段可选＝走廊宽度：入口缝要更窄，主缝给 3 格（背袭要留 2 格可走）。
		var width := int(link[2]) if link.size() > 2 else 3
		MapKit.carve_corridor(floor_cells, rects[link[0]], rects[link[1]], width)
	# 额外的地板块（石隙迷窟用它做「看着还能往前走」的死路窄缝）。
	for rect: Rect2i in extra_floor:
		MapKit.mark_rect(floor_cells, rect)
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
	for spec: Dictionary in CAVE_ROOMS + VILLAGE_ROOMS + SHIXI_ROOMS:
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
