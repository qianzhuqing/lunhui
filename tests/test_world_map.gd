## 大地图：区域揭开（揭雾）与驿站传送／难度切换。
##
## 设计出处：02_地图与明雷.md「未探索的部分用云雾盖着，走进去才揭开」「发现驿站后解锁传送」；
## 03_副本_黑风寨.md「难度可在大地图任意驿站切换」。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")

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
	_check_travel(db, state, service)
	_check_difficulty(db, state, service)
	_check_save(db, state)


## `reveal_on_map=1` 的地标一开始就点亮（清风驿／驿站／落雁坡）
func _check_initial(db, state, service) -> void:
	var revealed: PackedStringArray = service.apply_initial_reveals()
	check_eq(revealed.size(), 3, "初始点亮 3 个地标：%s" % str(revealed))
	check_true(service.is_revealed("n_qingfengyi"), "清风驿一开始就看得见")
	check_true(service.is_revealed("n_post_station"), "驿站一开始就看得见")
	check_false(service.is_revealed("n_heifengzhai"), "黑风寨要先探索")
	check_eq(service.revealed_count(), 3, "已探索 3 个")
	check_true(service.progress_text().contains("地图 3/7"), "HUD 文案：%s" % service.progress_text())
	check_true(service.progress_text().contains("黑风寨"), "未探索清单里有黑风寨")
	# 重复调用不会再报新揭开
	check_true(service.apply_initial_reveals().is_empty(), "初始揭开是幂等的")


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


## discover_X：X 揭开之后，依赖它的地标跟着揭开（黑风寨要 discover_luoyanpo）
## 用全新存档跑，免得前面几步已经把黑风寨揭开、看不出「链式」这一步做了什么
func _check_discover_chain(db) -> void:
	var fresh = solo_state(db)
	var service = WorldMapServiceScript.new(db, fresh)
	service.apply_initial_reveals()
	check_true(service.is_revealed("n_luoyanpo"), "落雁坡一开始就点亮")
	check_false(service.is_revealed("n_heifengzhai"), "链式揭开前黑风寨还是暗的")
	var revealed: PackedStringArray = service.apply_proximity_reveals(Vector2.ZERO, {})
	check_true(revealed.has("n_heifengzhai"), "落雁坡已揭开 → 黑风寨跟着揭开：%s" % str(revealed))
	check_true(service.is_revealed("n_heifengzhai"), "黑风寨点亮")


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
