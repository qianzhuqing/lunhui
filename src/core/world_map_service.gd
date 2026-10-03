## 大地图：区域揭开与驿站传送／难度切换。
##
## 设计出处：
##   02_地图与明雷.md —— 「已探索的地标显示在地图上，未探索的部分用云雾盖着，走进去才揭开」；
##                        「步行 + 驿站传送（发现驿站后解锁）」。
##   03_副本_黑风寨.md —— 「难度可在大地图任意驿站切换」。
##
## 规则全部读 `map_region.csv`：`reveal_on_map=1` 一开始就点亮，
## `unlock_condition=proximity_N` 走近 N 格自动揭开，`discover_<node_id>` 要先发现那个地标。
## 揭开状态写存档（`GameState.revealed_nodes`），所以「地图是自己走出来的」能留住。
class_name WorldMapService
extends RefCounted

## 一格多少像素（与地编的 32×32 一致）
const TILE := 32.0

## 难度解锁条件 → 需要击败的敌人 id。
## `difficulty_config.unlock_condition` 是中文短语，不是 id，所以这里用短语当键做映射；
## 表里出现没登记过的短语就报错并按「未解锁」处理（改文案时能立刻发现）。
const DIFFICULTY_RULES := {
	"默认解锁": [],
	"通关第一章": ["en_bd_boss"],
	"通关第一章并击败醉刀客": ["en_bd_boss", "en_hidden_drunk"],
}
## 本章不开放的小地图（与 overworld_controller 的 LOCKED_SCENES 同一口径）
const LOCKED_SCENES := ["scene_ferry_locked"]
## 章节所在副本（解锁「通关第一章」看这里记录过的 Boss）
const CHAPTER_SCENE := "scene_heifengzhai"

var db
var state


func _init(table_db, game_state) -> void:
	db = table_db
	state = game_state


func all_nodes() -> Array:
	var out: Array = []
	for row: Resource in db.rows("map_region"):
		out.append(row)
	return out


func node_row(node_id: String) -> Resource:
	return db.get_row("map_region", node_id)


func node_name(node_id: String) -> String:
	var row: Resource = node_row(node_id)
	return str(row.name_cn) if row != null else node_id


func is_revealed(node_id: String) -> bool:
	return state != null and state.is_node_revealed(node_id)


## `unlock_condition=discover_X` 里的 X 没带 `n_` 前缀（表里写 discover_luoyanpo，
## 节点 id 却是 n_luoyanpo），所以两种写法都认一下。
func discovery_target_revealed(suffix: String) -> bool:
	return is_revealed(suffix) or is_revealed("n_%s" % suffix)


# ------------------------------------------------------------------ 揭开

## 开工先点亮「一开始就该看见」的地标（`reveal_on_map=1`）。返回新揭开的 id。
func apply_initial_reveals() -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in all_nodes():
		if bool(row.reveal_on_map) and _reveal(str(row.node_id)):
			out.append(str(row.node_id))
	return out


## 按玩家位置揭开：proximity_N 走进去就揭开。markers 传 {node_id: 位置(Vector2)}
## 返回这一次新揭开的 id（调用方拿去提示玩家）。
func apply_proximity_reveals(player_position: Vector2, markers: Dictionary) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in all_nodes():
		var node_id := str(row.node_id)
		if is_revealed(node_id):
			continue
		var condition := str(row.unlock_condition)
		if not condition.begins_with("proximity_"):
			continue
		if not markers.has(node_id):
			continue
		var tiles := float(condition.substr("proximity_".length()).to_float())
		var position: Vector2 = markers[node_id]
		if player_position.distance_to(position) <= tiles * TILE:
			if _reveal(node_id):
				out.append(node_id)
	# 发现前置类：discover_X 要求 X 已揭开（同一次刷新里继续传播，处理链式依赖）
	var changed := true
	var guard := 0
	while changed and guard < all_nodes().size():
		changed = false
		guard += 1
		for row: Resource in all_nodes():
			var node_id := str(row.node_id)
			if is_revealed(node_id):
				continue
			var condition := str(row.unlock_condition)
			if not condition.begins_with("discover_"):
				continue
			if discovery_target_revealed(condition.substr("discover_".length())):
				if _reveal(node_id):
					out.append(node_id)
					changed = true
	return out


func _reveal(node_id: String) -> bool:
	return state.reveal_node(node_id) if state != null else false


func revealed_count() -> int:
	var count := 0
	for row: Resource in all_nodes():
		if is_revealed(str(row.node_id)):
			count += 1
	return count


## HUD 文案：「地图 3/7（未探索：黑风寨、塌陷山洞…）」
func progress_text() -> String:
	var hidden := PackedStringArray()
	for row: Resource in all_nodes():
		if not is_revealed(str(row.node_id)):
			hidden.append(str(row.name_cn))
	return "地图 %d/%d%s" % [
		revealed_count(), all_nodes().size(),
		"" if hidden.is_empty() else "　（未探索：%s）" % "、".join(hidden),
	]


# ------------------------------------------------------------------ 驿站传送

## 能从驿站去哪些地方：已揭开、有可进入的小地图、不是本章锁着的、也不是当前这张图
func travel_targets(current_scene_id: String = "") -> Array:
	var out: Array = []
	for row: Resource in all_nodes():
		var node_id := str(row.node_id)
		var scene_id := str(row.enter_scene)
		if scene_id.is_empty() or LOCKED_SCENES.has(scene_id):
			continue
		if not is_revealed(node_id):
			continue
		out.append({
			"node_id": node_id,
			"name": str(row.name_cn),
			"scene_id": scene_id,
			"current": scene_id == current_scene_id,
		})
	return out


## 传送（只做校验与返回目标场景，切场景交给调用方）
func travel(node_id: String, current_scene_id: String = "") -> Dictionary:
	for entry: Dictionary in travel_targets(current_scene_id):
		if str(entry["node_id"]) == node_id:
			return {"ok": true, "error": "", "scene_id": str(entry["scene_id"]), "name": str(entry["name"])}
	var row: Resource = node_row(node_id)
	if row == null:
		return {"ok": false, "error": "没有这个地标：%s" % node_id, "scene_id": "", "name": ""}
	if not is_revealed(node_id):
		return {"ok": false, "error": "%s 还没探索到（先走过去）" % str(row.name_cn), "scene_id": "", "name": str(row.name_cn)}
	if str(row.enter_scene).is_empty():
		return {"ok": false, "error": "%s 不是可进入的地图" % str(row.name_cn), "scene_id": "", "name": str(row.name_cn)}
	return {"ok": false, "error": "%s 本章不可前往" % str(row.name_cn), "scene_id": "", "name": str(row.name_cn)}


# ------------------------------------------------------------------ 难度切换

## 难度能不能选：{ok, error, defeated, need}
func can_switch_difficulty(difficulty_id: String) -> Dictionary:
	var row: Resource = db.get_row("difficulty_config", difficulty_id)
	if row == null:
		return {"ok": false, "error": "没有这个难度：%s" % difficulty_id, "defeated": [], "need": []}
	var condition := str(row.unlock_condition)
	if not DIFFICULTY_RULES.has(condition):
		push_error("[WorldMapService] difficulty_config 里没登记过的解锁条件：%s" % condition)
		return {"ok": false, "error": "解锁条件未登记：%s" % condition, "defeated": [], "need": []}
	var need: Array = DIFFICULTY_RULES[condition]
	if need.is_empty():
		return {"ok": true, "error": "", "defeated": [], "need": []}
	var defeated: Array = state.dungeon_record(CHAPTER_SCENE).get("bosses", []) if state != null else []
	var missing := PackedStringArray()
	for enemy_id: String in need:
		if not defeated.has(enemy_id):
			var enemy: Resource = db.get_row("enemy_base", enemy_id)
			missing.append(str(enemy.name_cn) if enemy != null else enemy_id)
	if missing.is_empty():
		return {"ok": true, "error": "", "defeated": defeated, "need": need}
	return {
		"ok": false,
		"error": "还没满足条件：先击败 %s" % "、".join(missing),
		"defeated": defeated, "need": need,
	}


## 切换难度（只改存档里的 difficulty_id，战斗与掉落都会跟着换）
func switch_difficulty(difficulty_id: String) -> Dictionary:
	var check := can_switch_difficulty(difficulty_id)
	if not bool(check["ok"]):
		return check
	if state == null:
		return {"ok": false, "error": "没有会话状态", "defeated": [], "need": []}
	var previous: String = state.difficulty_id
	state.difficulty_id = difficulty_id
	var row: Resource = db.get_row("difficulty_config", difficulty_id)
	return {
		"ok": true, "error": "", "defeated": check["defeated"], "need": check["need"],
		"name": str(row.name_cn) if row != null else difficulty_id,
		"previous": previous,
	}


## 驿站面板用：三个难度的状态一览
func difficulty_rows() -> Array:
	var out: Array = []
	for row: Resource in db.rows("difficulty_config"):
		var check := can_switch_difficulty(str(row.difficulty_id))
		out.append({
			"difficulty_id": str(row.difficulty_id),
			"name": str(row.name_cn),
			"condition": str(row.unlock_condition),
			"ok": bool(check["ok"]),
			"error": str(check["error"]),
			"current": state != null and state.difficulty_id == str(row.difficulty_id),
		})
	return out
