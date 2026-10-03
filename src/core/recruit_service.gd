## 剧情招募（设计 09 §3.2）：开局只有 `recruit_def.is_initial=1` 的人，其余同伴按剧情加入。
##
## 数据源 `recruit_def.csv`：`join_condition` 是旗标 id，`join_scene` 既可能是
## `map_local.scene_id`（小地图／副本，如清风驿、黑风寨），也可能是 `map_region.node_id`
## （大地图上的区域，如落雁坡——它没有自己的小地图）。两条都在这里认。
##
## 口径：**「条件旗标已点亮 ∧ 人在那张图（或站到那个区域地标前）∧ 还没入队」→ 入队**。
## 谁来点亮条件旗标是各系统的事（燕小七要的 `flag_board_read` 由清风驿的悬赏板发出；
## 另外三位的 `flag_luoyanpo_met`／`flag_poison_hall`／`flag_huangcun_done` 目前**没有任何来源**——
## 那是设计侧的剧情内容，没到之前他们不会入队，代码这边不自己编触发点）。
##
## 入队动作本身只有一处：`GameState.add_character()`（发等级／起始装备／起始武学并铺装配）。
class_name RecruitService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")


## 某一行「是不是在这张图里等着」：`join_scene` 命中 scene_id，或命中这张图的父区域节点。
static func scene_matches(db, row: Resource, scene_id: String) -> bool:
	var want := str(row.join_scene)
	if want.is_empty() or scene_id.is_empty():
		return false
	if want == scene_id:
		return true
	var local: Resource = db.get_row("map_local", scene_id)
	return local != null and str(local.parent_node) == want


## 还没入队、条件也满足的招募行（`is_initial=1` 的初始成员不算「待加入」）。
static func _ready_rows(db, state, match_call: Callable) -> Array:
	var out: Array = []
	if state == null:
		return out
	for row: Resource in db.rows("recruit_def"):
		if int(row.is_initial) == 1:
			continue
		if state.char_ids.has(str(row.char_id)):
			continue
		if not match_call.call(row):
			continue
		if not GuideServiceScript.condition_met(state, str(row.join_condition)):
			continue
		out.append(row)
	return out


## 小地图／副本里等着加入的人（按 `map_local.scene_id` 或它的父区域节点匹配）。
static func pending_for_scene(db, state, scene_id: String) -> Array:
	return _ready_rows(db, state, func(row: Resource) -> bool:
		return scene_matches(db, row, scene_id)
	)


## 大地图上等着加入的人（按 `map_region.node_id` 匹配——落雁坡这类没有小地图的区域）。
static func pending_for_region(db, state, node_id: String) -> Array:
	return _ready_rows(db, state, func(row: Resource) -> bool:
		return not node_id.is_empty() and str(row.join_scene) == node_id
	)


## 把一位同伴加进队伍。返回 {ok, char_id, name_cn, note, error}。
static func join(db, state, char_id: String) -> Dictionary:
	var row: Resource = db.get_row("character_base", char_id)
	var name_cn := str(row.name_cn) if row != null else char_id
	var result: Dictionary = state.add_character(db, char_id) if state != null else {"ok": false, "error": "没有会话状态"}
	if not bool(result.get("ok", false)):
		return {
			"ok": false, "char_id": char_id, "name_cn": name_cn,
			"note": "", "error": str(result.get("error", "入队失败")),
		}
	var recruit_row: Resource = db.get_row("recruit_def", char_id)
	# 入队点亮一枚旗标 `flag_<char_id>_joined`：设计侧的 `guide_step` 就是拿它当下一步的条件的
	# （现例：`flag_ch_ci_joined`），别在别处再手写一遍这个字符串。
	state.set_flag("flag_%s_joined" % char_id)
	return {
		"ok": true, "char_id": char_id, "name_cn": name_cn,
		"note": str(recruit_row.join_note) if recruit_row != null else "",
		"error": "",
	}


## 一次处理完某个场景里所有待加入的人（按表里的顺序），返回结果数组。
static func join_all_for_scene(db, state, scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in pending_for_scene(db, state, scene_id):
		out.append(join(db, state, str(row.char_id)))
	return out


## 大地图上的版本（按区域节点 id）。
static func join_all_for_region(db, state, node_id: String) -> Array:
	var out: Array = []
	for row: Resource in pending_for_region(db, state, node_id):
		out.append(join(db, state, str(row.char_id)))
	return out
