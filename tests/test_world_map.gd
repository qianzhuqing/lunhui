## 大地图：区域揭开（揭雾）与驿站传送／难度切换。
##
## 设计出处：02_地图与明雷.md「未探索的部分用云雾盖着，走进去才揭开」「发现驿站后解锁传送」；
## 03_副本_黑风寨.md「难度可在大地图任意驿站切换」。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
## 表库夹具用基类那份 `TableDbScript`（`test_case.gd` 已经有，子类再定义同名成员会解析失败）
const TableValidatorScript := preload("res://src/core/table_validator.gd")

const TILE := 32.0


func suite_name() -> String:
	return "大地图揭开与驿站"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var service = WorldMapServiceScript.new(db, state)
	_check_initial(db, state, service)
	_check_proximity(db, state)
	_check_discover_chain(db)
	_check_region_rules(db)
	_check_travel(db, state, service)
	_check_difficulty(db, state, service)
	_check_save(db, state)


## `reveal_on_map=1` 的地标一开始就点亮（设计 0.32.0：**只有城镇与驿站**）
##
## 落雁坡曾是第三个默认公开的地点（野外），0.32.0 按「除城镇外都要自己走出来」改成 `proximity_5`。
## 所以这里连同 `_check_proximity` 一起走一遍真实顺序：开局两处 → 走到落雁坡才亮 → 黑风寨靠它链式揭开。
func _check_initial(db, state, service) -> void:
	var revealed: PackedStringArray = service.apply_initial_reveals()
	check_eq(revealed.size(), 2, "初始点亮 2 个地标（城镇＋驿站）：%s" % str(revealed))
	check_true(service.is_revealed("n_qingfengyi"), "清风驿一开始就看得见")
	check_true(service.is_revealed("n_post_station"), "驿站一开始就看得见")
	check_false(service.is_revealed("n_luoyanpo"), "落雁坡是野外，开局不亮（0.32.0）")
	check_false(service.is_revealed("n_heifengzhai"), "黑风寨要先探索")
	check_eq(service.revealed_count(), 2, "已探索 2 个")
	# 分母是**表里的行数**（不是"有图标的行数"：0.32.0 起三个兴趣点不带图标，
	# 拿图标数当分母会算成 5）
	var total: int = db.rows("map_region").size()
	check_true(service.progress_text().contains("地图 2/%d" % total),
		"HUD 文案：%s" % service.progress_text())
	check_true(service.progress_text().contains("落雁坡"), "未探索清单里有落雁坡")
	check_true(service.progress_text().contains("黑风寨"), "未探索清单里有黑风寨")
	# 重复调用不会再报新揭开
	check_true(service.apply_initial_reveals().is_empty(), "初始揭开是幂等的")
	# 持有类（`item_<id>`，设计 0.26 §六）：没拿到藏宝图，石隙迷窟不该出现
	check_false(service.is_revealed("n_shixi"), "没有藏宝图时石隙迷窟不在地图上")
	var added: Dictionary = state.inventory.add_item(db, "item_treasure_map", 1)
	check_true(bool(added.get("ok", false)), "藏宝图能放进背包：%s" % str(added.get("error", "")))
	check_true(state.inventory.has("item_treasure_map", 1), "背包里确实有藏宝图（下一句才验揭开）")
	var held: PackedStringArray = service.apply_held_reveals()
	check_true(held.has("n_shixi"), "拿到藏宝图后石隙迷窟当场揭开：%s" % str(held))
	check_true(service.is_revealed("n_shixi"), "石隙迷窟出现在大地图上")


## proximity_N：走到 N 格内自动揭开（塌陷山洞 3 格、荒村 5 格）
func _check_proximity(db, state) -> void:
	var service = WorldMapServiceScript.new(db, state)
	var markers := {
		"n_cave_collapse": Vector2(1000, 1000),
		"n_huangcun": Vector2(2000, 2000),
		"n_luoyanpo": Vector2(0, 0),
	}
	# 离山洞 4 格（128px）→ 还差一点
	var far: PackedStringArray = service.apply_proximity_reveals(Vector2(1000 + 4 * TILE, 1000), markers)
	check_false(service.is_revealed("n_cave_collapse"), "4 格外揭不开（门槛 3 格）")
	# 走到 2 格内 → 揭开
	var near: PackedStringArray = service.apply_proximity_reveals(Vector2(1000 + 2 * TILE, 1000), markers)
	check_true(near.has("n_cave_collapse"), "2 格内揭开塌陷山洞：%s" % str(near))
	# 荒村要 5 格
	check_false(service.is_revealed("n_huangcun"), "荒村还没揭开")
	service.apply_proximity_reveals(Vector2(2000 + 4 * TILE, 2000), markers)
	check_true(service.is_revealed("n_huangcun"), "4 格内揭开荒村（门槛 5 格）")

	# 正好卡在门槛上：口径是 `<= 半径 × 32px`——**3 格整要揭、3 格多 1 像素不揭**。
	# 这是典型的 off-by-one：改成 `<` 就会「站在门槛上看不见」，而平时很难察觉（差一点点而已）。
	var edge_far = WorldMapServiceScript.new(db, solo_state(db))
	edge_far.apply_proximity_reveals(Vector2(1000 + 3 * TILE + 1, 1000), markers)
	check_false(edge_far.is_revealed("n_cave_collapse"), "多 1 像素不揭（门槛不含外沿）")
	var edge_on = WorldMapServiceScript.new(db, solo_state(db))
	edge_on.apply_proximity_reveals(Vector2(1000 + 3 * TILE, 1000), markers)
	check_true(edge_on.is_revealed("n_cave_collapse"), "正好 3 格揭开（<= 口径）")
	var edge_five = WorldMapServiceScript.new(db, solo_state(db))
	edge_five.apply_proximity_reveals(Vector2(2000 + 5 * TILE, 2000), markers)
	check_true(edge_five.is_revealed("n_huangcun"), "正好 5 格也揭开（荒村门槛）")

	# 主路第三站：**落雁坡也要自己走到跟前**（0.32.0 由默认公开改成 `proximity_5`）。
	# 它一揭开，链式规则就把黑风寨带出来（`discover_luoyanpo`）——后面 `_check_travel`
	# 正是按「黑风寨已揭开」验的，所以这一步同时是那一条的前提。
	check_false(service.is_revealed("n_luoyanpo"), "在别处转了一圈，落雁坡还没亮（野外≠城镇）")
	var road: PackedStringArray = service.apply_proximity_reveals(Vector2(2 * TILE, 0), markers)
	check_true(road.has("n_luoyanpo"), "走到落雁坡 2 格内才揭开：%s" % str(road))
	check_true(road.has("n_heifengzhai"), "落雁坡一揭开，黑风寨跟着链式揭开：%s" % str(road))


## discover_X：X 揭开之后，依赖它的地标跟着揭开（黑风寨要 discover_luoyanpo）
## 用全新存档跑，免得前面几步已经把黑风寨揭开、看不出「链式」这一步做了什么
func _check_discover_chain(db) -> void:
	var fresh = solo_state(db)
	var service = WorldMapServiceScript.new(db, fresh)
	service.apply_initial_reveals()
	check_false(service.is_revealed("n_luoyanpo"), "链式揭开前落雁坡也还没亮（它是野外，要走近）")
	check_false(service.is_revealed("n_heifengzhai"), "链式揭开前黑风寨还是暗的")
	# 没走到落雁坡跟前：黑风寨不许自己冒出来
	service.apply_proximity_reveals(Vector2(10000, 10000), {})
	check_false(service.is_revealed("n_heifengzhai"), "落雁坡没亮时黑风寨不会自己冒出来")
	var revealed: PackedStringArray = service.apply_proximity_reveals(
		Vector2(2 * TILE, 0), {"n_luoyanpo": Vector2.ZERO}
	)
	check_true(revealed.has("n_luoyanpo"), "走到跟前落雁坡揭开：%s" % str(revealed))
	check_true(revealed.has("n_heifengzhai"), "落雁坡已揭开 → 黑风寨跟着揭开：%s" % str(revealed))
	check_true(service.is_revealed("n_heifengzhai"), "黑风寨点亮")


## 0.32.0 的两条表侧规则（构建期 `TableValidator._check_map_region_rules` 的同源验证）：
## ① `reveal_on_map=1` 只许城镇／驿站；② 兴趣点不给图标（例外：持有类解锁的那一个）。
##
## 两半都要：**发行数据长这样**（逐行点名，不写"数量对就行"——数量对、换了行也能过）
## ＋**改坏了构建期真的会红**（复制表库、只改内存副本）。
func _check_region_rules(db) -> void:
	var public_ids := PackedStringArray()
	var poi_icons := PackedStringArray()
	for row: Resource in db.rows("map_region"):
		if bool(row.reveal_on_map):
			public_ids.append(str(row.node_id))
		if str(row.node_type) == "poi" and not str(row.icon).strip_edges().is_empty():
			poi_icons.append("%s→%s" % [str(row.node_id), str(row.icon)])
	check_eq("、".join(public_ids), "n_qingfengyi、n_post_station",
		"开局公开的正好是清风驿与驿站（实际：%s）" % "、".join(public_ids))
	check_eq("、".join(poi_icons), "n_shixi→icon_shixi",
		"兴趣点里只有石隙迷窟带图标（藏宝图上有位置）；实际：%s" % "、".join(poi_icons))

	var broken = TableDbScript.new()
	broken.load_all()
	var table: Resource = broken.tables["map_region"].duplicate(true)
	for row: Resource in table.rows:
		if str(row.node_id) == "n_luoyanpo":
			row.reveal_on_map = true
	broken.tables["map_region"] = table
	var named := false
	for message: String in TableValidatorScript.validate(broken):
		if message.contains("n_luoyanpo") and message.contains("reveal_on_map"):
			named = true
	check_true(named, "把落雁坡改回「开局公开」→ 构建期点名 n_luoyanpo")

	var broken_icon = TableDbScript.new()
	broken_icon.load_all()
	var icon_table: Resource = broken_icon.tables["map_region"].duplicate(true)
	for row: Resource in icon_table.rows:
		if str(row.node_id) == "n_cave_collapse":
			row.icon = "icon_cave"
	broken_icon.tables["map_region"] = icon_table
	var icon_named := false
	for message: String in TableValidatorScript.validate(broken_icon):
		if message.contains("n_cave_collapse") and message.contains("icon"):
			icon_named = true
	check_true(icon_named, "给兴趣点配图标 → 构建期点名 n_cave_collapse")


## 驿站传送：只列已探索且有可进入小地图的节点；没探索的给明确理由
func _check_travel(db, state, service) -> void:
	var targets: Array = service.travel_targets("")
	var ids := PackedStringArray()
	for entry: Dictionary in targets:
		ids.append(str(entry["node_id"]))
	check_true(ids.has("n_qingfengyi"), "能传送到清风驿：%s" % str(ids))
	check_true(ids.has("n_heifengzhai"), "黑风寨揭开后也能传送")
	check_false(ids.has("n_luoyanpo"), "落雁坡是野外区域（没有 enter_scene），不作为目的地")
	check_false(ids.has("n_ferry_abandoned"), "本章不开放的渡口不列出来")

	var current: Dictionary = service.travel("n_qingfengyi", "scene_qingfengyi")
	check_true(bool(current["ok"]), "传送到当前所在地也是合法的")
	check_eq(str(current["scene_id"]), "scene_qingfengyi", "落点是清风驿的场景 id")

	var unknown: Dictionary = service.travel("n_nowhere", "")
	check_false(bool(unknown["ok"]), "没有的节点传不了")
	check_true(str(unknown["error"]).contains("没有这个地标"), "理由写明没有这个地标：%s" % unknown["error"])

	# 没探索的地方不能传送
	var fresh = solo_state(db)
	var bare = WorldMapServiceScript.new(db, fresh)
	var blocked: Dictionary = bare.travel("n_huangcun", "")
	check_false(bool(blocked["ok"]), "没探索过的地方不能传送")
	check_true(str(blocked["error"]).contains("还没探索"), "提示先走过去：%s" % blocked["error"])


## 难度切换：普通随便切；困难要击败大寨主；绝境还要加上醉刀客
func _check_difficulty(db, state, service) -> void:
	check_true(bool(service.can_switch_difficulty("normal")["ok"]), "普通默认解锁")
	var hard: Dictionary = service.can_switch_difficulty("hard")
	check_false(bool(hard["ok"]), "困难要通关第一章")
	check_true(str(hard["error"]).contains("大寨主"), "说明差大寨主：%s" % hard["error"])
	var nightmare: Dictionary = service.can_switch_difficulty("nightmare")
	check_false(bool(nightmare["ok"]), "绝境也还没解锁")

	state.record_dungeon("scene_heifengzhai", "bosses", "en_bd_boss")
	check_true(bool(service.can_switch_difficulty("hard")["ok"]), "击败大寨主后困难解锁")
	check_false(bool(service.can_switch_difficulty("nightmare")["ok"]), "绝境还要醉刀客")
	state.record_dungeon("scene_heifengzhai", "bosses", "en_hidden_drunk")
	check_true(bool(service.can_switch_difficulty("nightmare")["ok"]), "两个 Boss 都打完绝境解锁")

	var switched: Dictionary = service.switch_difficulty("nightmare")
	check_true(bool(switched["ok"]), "切难度成功：%s" % switched["error"])
	check_eq(state.difficulty_id, "nightmare", "存档里的难度跟着改")
	check_eq(str(switched["previous"]), "normal", "记下切换前的难度")
	var rows: Array = service.difficulty_rows()
	check_eq(rows.size(), 3, "三个难度都列出来")
	var current_rows := 0
	for row: Dictionary in rows:
		if bool(row["current"]):
			current_rows += 1
	check_eq(current_rows, 1, "只有一个难度标着当前")


## 揭开状态与难度写存档
func _check_save(db, state) -> void:
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_not_null(back, "带揭开状态的存档能读回来")
	if back == null:
		return
	check_eq(back.migrated_from, 0, "同版本往返不需要迁移")
	check_true(back.is_node_revealed("n_heifengzhai"), "揭开的节点往返一致")
	check_eq(back.difficulty_id, "nightmare", "难度往返一致")

	# v7 老档（没有 revealed_nodes）读进来是空的，版本升到 8
	var legacy: Dictionary = state.to_dict()
	legacy["version"] = 7
	legacy.erase("revealed_nodes")
	var migrated = GameStateScript.from_dict(legacy, db)
	check_eq(migrated.migrated_from, 7, "记下从 v7 迁移")
	check_eq(migrated.version, GameStateScript.VERSION, "版本升到当前")
	check_eq(migrated.revealed_nodes.size(), 0, "老档没有揭开记录")
