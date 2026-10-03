## 剧情招募（设计 09 §3.2）：开局只有初始成员，其余同伴按「条件旗标 + 场景」入队。
##
## 用例从 `recruit_def` 里**挑真实行**来验（不写死 ch_ci／n_luoyanpo）：
## 一条 `join_scene` 是小地图的、一条是区域节点的，两条路径都走到。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
const OverworldScript := preload("res://src/world/overworld_controller.gd")


func suite_name() -> String:
	return "剧情招募"


func run() -> void:
	var db = get_db()
	# 队伍上限写死绝对值（设计 00 总览：3–4 人）。它现在只影响「配表出问题时的兜底组队」，
	# 但仍然是设计写死的值——变异探针第三块点名过它（MAX_PARTY 4→5 没人钉）。
	check_eq(GameStateScript.MAX_PARTY, 4, "队伍上限 4")
	_check_initial_members(db)
	_check_condition_gate(db)
	_check_join_seeds_character(db)
	_check_region_rows(db)
	_check_region_radius()
	_check_no_double_join(db)


## 开局队伍 = `recruit_def.is_initial=1` 的成员；他们不该出现在「待加入」里
func _check_initial_members(db) -> void:
	var state = GameStateScript.new_game(db, "normal")
	var initial := PackedStringArray()
	for row: Resource in db.rows("recruit_def"):
		if int(row.is_initial) == 1:
			initial.append(str(row.char_id))
	check_gt(float(initial.size()), 0.0, "表里有初始成员（%d 人）" % initial.size())
	check_eq(str(state.char_ids), str(initial), "开局队伍就是这些初始成员")
	for char_id: String in initial:
		state.set_flag("flag_board_read")
		check_false(_pending_ids(db, state, "scene_qingfengyi").has(char_id), "初始成员 %s 不算「待加入」" % char_id)


## 条件没点亮就不入队；点亮了才进待加入名单
func _check_condition_gate(db) -> void:
	var row := _first_row(db, func(r: Resource) -> bool: return int(r.is_initial) == 0)
	check_not_null(row, "recruit_def 里有非初始成员（这条用例才有对象可验）")
	if row == null:
		return
	var char_id := str(row.char_id)
	var condition := str(row.join_condition)
	check_false(condition.is_empty(), "%s 写了加入条件" % char_id)
	var scene_id := _scene_for_row(db, row)
	check_false(scene_id.is_empty(), "%s 的 join_scene 认得出对应的小地图" % char_id)
	if scene_id.is_empty():
		return
	var state = solo_state(db)
	check_false(_pending_ids(db, state, scene_id).has(char_id), "条件没点亮时不入队")
	state.set_flag(condition)
	check_true(_pending_ids(db, state, scene_id).has(char_id), "点亮 %s 后进入待加入名单" % condition)
	# 换一张图不该收人（人在清风驿，落雁坡的同伴不会自己跑过来）
	check_false(_pending_ids(db, state, _other_scene(scene_id)).has(char_id), "别的图上不收这位同伴")


## 入队要真的把人加进队伍，并发齐「等级 + 起始装备 + 起始武学 + 装配」
func _check_join_seeds_character(db) -> void:
	var row := _first_row(db, func(r: Resource) -> bool: return int(r.is_initial) == 0)
	check_not_null(row, "入队用例有对象（同上）")
	if row == null:
		return
	var char_id := str(row.char_id)
	var scene_id := _scene_for_row(db, row)
	var state = solo_state(db)
	state.set_flag(str(row.join_condition))
	var before_party := state.party_size()
	var before_equipment: int = state.inventory.equipment_count()
	var results: Array = RecruitServiceScript.join_all_for_scene(db, state, scene_id)
	check_eq(results.size(), 1, "这一张图上只有一位待加入")
	check_true(bool(Array(results)[0]["ok"]), "入队成功")
	check_eq(state.party_size(), before_party + 1, "队伍多了一个人")
	check_true(state.char_ids.has(char_id), "%s 进了队伍名单" % char_id)
	var template: Resource = db.get_row("character_base", char_id)
	check_eq(state.level_of(char_id), int(template.start_level), "等级取模板的 start_level")
	var equipped: PackedStringArray = state.inventory.equipped_instances(db, char_id)
	check_eq(equipped.size(), template.equip_ids().size(), "起始装备都穿上了（%d 件）" % template.equip_ids().size())
	check_gt(float(state.inventory.equipment_count()), float(before_equipment), "背包里多了这几件实例")
	var learned: PackedStringArray = state.learned_of(char_id)
	for skill_id: String in template.skill_ids():
		check_true(learned.has(skill_id), "起始武学 %s 已学会" % skill_id)
	check_false(str(state.loadout_of(char_id)["active"]).is_empty(), "起始招式已经铺进装配")
	# 入队要点亮 `flag_<char_id>_joined`：引导的下一步就是等它（现例：`flag_ch_ci_joined`）
	check_true(state.has_flag("flag_%s_joined" % char_id), "入队点亮 flag_%s_joined（引导拿它推进）" % char_id)


## `join_scene` 指向**区域节点**的那几位（落雁坡这类没有小地图）：按区域查，不按小地图查
func _check_region_rows(db) -> void:
	var region_row := _first_row(db, func(r: Resource) -> bool:
		var want := str(r.join_scene)
		return int(r.is_initial) == 0 and not want.is_empty() and db.get_row("map_region", want) != null
	)
	check_not_null(region_row, "表里有一位「按区域地标加入」的同伴（落雁坡那条路径靠它验）")
	if region_row == null:
		return
	var char_id := str(region_row.char_id)
	var node_id := str(region_row.join_scene)
	var state = solo_state(db)
	state.set_flag(str(region_row.join_condition))
	check_true(_pending_ids_for_region(db, state, node_id).has(char_id), "%s 在 %s 的地标旁等着加入" % [char_id, node_id])
	check_false(_pending_ids(db, state, "scene_heifengzhai").has(char_id), "区域型同伴不会因为进副本而加入")
	var results: Array = RecruitServiceScript.join_all_for_region(db, state, node_id)
	check_eq(results.size(), 1, "走到地标旁就入队")
	check_true(state.char_ids.has(char_id), "区域型同伴真的进了队伍")


## 区域招募的判定半径要**钉住数值**：它决定「走到落雁坡算不算遇上林铁山」，
## 不是纯表现（决策 154：「决定事件发不发」的距离要钉值，「纯移动/表现」的不钉）。
## 取值口径是与脚下地标高亮同值（全项目唯一一处「你就在这个地标旁」的既有写法）。
func _check_region_radius() -> void:
	check_float(OverworldScript.REGION_JOIN_DISTANCE, 70.0, "区域招募半径写死 70px（改了要红）", 0.0001)
	check_eq(
		OverworldScript.REGION_JOIN_DISTANCE, OverworldScript.HIGHLIGHT_DISTANCE,
		"与地标高亮同值（两条半径一起改才不算漂）"
	)


## 重复入队：第二次必须被拒，而且**不再发一份装备**（每次按 E 都会查一遍，这条是常态路径）
func _check_no_double_join(db) -> void:
	var row := _first_row(db, func(r: Resource) -> bool: return int(r.is_initial) == 0)
	check_not_null(row, "重复入队用例有对象（同上）")
	if row == null:
		return
	var char_id := str(row.char_id)
	var scene_id := _scene_for_row(db, row)
	var state = solo_state(db)
	state.set_flag(str(row.join_condition))
	var before_party := state.party_size()
	RecruitServiceScript.join_all_for_scene(db, state, scene_id)
	var equipment_after_first: int = state.inventory.equipment_count()
	var again: Dictionary = RecruitServiceScript.join(db, state, char_id)
	check_false(bool(again["ok"]), "已经在队里时不重复加入")
	check_eq(state.inventory.equipment_count(), equipment_after_first, "也不重复发装备")
	check_eq(state.party_size(), before_party + 1, "队伍人数没变（入队那次加的人还在）")


# ------------------------------------------------------------------ 夹具

func _first_row(db, predicate: Callable) -> Resource:
	for row: Resource in db.rows("recruit_def"):
		if bool(predicate.call(row)):
			return row
	return null


## 这一行的 `join_scene` 对应哪张小地图：直接是 scene_id 就取它；
## 是区域节点就找“父区域是它”的那张小地图（找不到就返回空——`_check_condition_gate` 会 fail）
func _scene_for_row(db, row: Resource) -> String:
	var want := str(row.join_scene)
	if db.get_row("map_local", want) != null:
		return want
	for local: Resource in db.rows("map_local"):
		if str(local.parent_node) == want:
			return str(local.scene_id)
	return ""


func _pending_ids(db, state, scene_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in RecruitServiceScript.pending_for_scene(db, state, scene_id):
		out.append(str(row.char_id))
	return out


## 一张「肯定不是这个场景」的小地图（负向断言用：换张图不该收人）
func _other_scene(scene_id: String) -> String:
	return "scene_qingfengyi" if scene_id != "scene_qingfengyi" else "scene_heifengzhai"


func _pending_ids_for_region(db, state, node_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in RecruitServiceScript.pending_for_region(db, state, node_id):
		out.append(str(row.char_id))
	return out
