## 剧情招募（设计 09 §3.2）：开局只有初始成员，其余同伴按「条件旗标 + 场景」入队。
##
## 用例从 `recruit_def` 里**挑真实行**来验（不写死 ch_ci／n_luoyanpo）：
## 一条 `join_scene` 是小地图的、一条是区域节点的，两条路径都走到。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
## 入队奖励里那条「好感 ＋20」加在同伴头上，断言要读好感
const NpcServiceScript := preload("res://src/core/npc_service.gd")
const OverworldScript := preload("res://src/world/overworld_controller.gd")
## 「打赢某支队伍 → 置旗标」那张表在战斗界面里（招募链的条件来源之一）
const BattleScreenScript := preload("res://src/ui/battle_screen.gd")


func suite_name() -> String:
	return "剧情招募"


func run() -> void:
	var db = get_db()
	# 队伍上限写死绝对值（设计 00 总览：3–4 人）。它现在只影响「配表出问题时的兜底组队」，
	# 但仍然是设计写死的值——变异探针第三块点名过它（MAX_PARTY 4→5 没人钉）。
	check_eq(GameStateScript.MAX_PARTY, 4, "队伍上限 4")
	_check_initial_members(db)
	_check_initial_member_can_join_when_not_protagonist(db)
	_check_condition_gate(db)
	_check_join_seeds_character(db)
	_check_region_rows(db)
	_check_region_radius()
	_check_no_double_join(db)
	_check_join_conditions_are_reachable(db)
	_check_join_rewards(db)


## 入队奖励（设计 20 号 §九 那张表：四条招募支线各写「入队 ＋ 某物」＋ 好感 ＋20）。
##
## 表里两条**不用**额外发：林铁山的柴刀就是他的起始武器（`start_equip_ids`）、
## 苏九娘的五毒散手就是她的起始武学（`start_skill_ids`）——那两样 `add_character` 已经发了。
## 所以这里只钉「起始装备／武学之外」的两件（回气散／药酒）＋ 四条的好感。
func _check_join_rewards(db) -> void:
	# 燕小七：回气散 ＋ 好感 20
	var state = solo_state(db)
	state.set_flag("flag_board_read")
	var qi_before: int = state.inventory.count("item_potion_qi")
	var joined: Array = RecruitServiceScript.join_all_for_scene(db, state, "scene_qingfengyi")
	var ci: Dictionary = {}
	for entry: Dictionary in joined:
		if str(entry.get("char_id", "")) == "ch_ci":
			ci = entry
	check_false(ci.is_empty(), "燕小七真的入队了")
	check_eq(
		state.inventory.count("item_potion_qi"), qi_before + 1,
		"入队给了回气散（§九：「入队 ＋ 回气散」）",
	)
	check_eq(NpcServiceScript.favor_of(db, state, "ch_ci"), 20, "入队好感 ＋20")
	check_true(
		Array(ci.get("rewards", [])).has("回气散 ×1"), "结果里带上了给人看的奖励短句：%s" % str(ci.get("rewards", [])),
	)
	# 林铁山：柴刀来自起始装备（不是这里发的），好感同样 ＋20
	var gang = solo_state(db)
	gang.set_flag("flag_luoyanpo_met")
	RecruitServiceScript.join_all_for_region(db, gang, "n_luoyanpo")
	check_eq(NpcServiceScript.favor_of(db, gang, "ch_gang"), 20, "林铁山入队也给 ＋20 好感")
	check_true(
		gang.inventory.equipment_ids().size() > 0, "他的柴刀来自起始装备（入队就有装备在身）",
	)


## 四条招募链的条件**必须有人能点亮**（设计 20 §十一 那张表）。
##
## 这一条是「断链」的门限：`flag_luoyanpo_met`／`flag_poison_hall`／`flag_huangcun_done`
## 原来**一个来源都没有**（Q51），于是除了燕小七之外的三个同伴永远入不了队。
## 现在前两个的后两个由**战斗收尾**点亮（打赢屠夫／毒手，见 `battle_screen.TEAM_WIN_FLAGS`），
## 落雁坡那条还等着地编的旧镖车位点——所以这里只钉「条件与设计表一致」，
## 不钉「必须有来源」（那一条等位点到位再收口）。
func _check_join_conditions_are_reachable(db) -> void:
	var expected := {
		"scholar_fallen": "flag_board_read",
		"ch_ci": "flag_board_read",
		"ch_gang": "flag_luoyanpo_met",
		"ch_du": "flag_poison_hall",
		"ch_qi": "flag_huangcun_done",
	}
	for char_id: String in expected:
		var row: Resource = db.get_row("recruit_def", char_id)
		check_not_null(row, "%s 在 recruit_def 里" % char_id)
		if row == null:
			continue
		check_eq(str(row.join_condition), str(expected[char_id]), "%s 的加入条件" % char_id)
	# 打赢队伍置的那两枚旗标，与这里的条件**是同一串字符串**（改一处会红）
	check_true(
		BattleScreenScript.TEAM_WIN_FLAGS.values().has("flag_huangcun_done"),
		"荒村的旗标由战斗收尾点亮（battle_screen.TEAM_WIN_FLAGS）",
	)
	check_true(
		BattleScreenScript.TEAM_WIN_FLAGS.values().has("flag_poison_hall"),
		"毒堂的旗标也由战斗收尾点亮",
	)


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


## 主角选**别人**时，初始行照样能按剧情入队（设计 0.30.0）
##
## 由来：`_ready_rows()` 以前无条件跳过 `is_initial=1` 的行，而 `scholar_fallen` 是**唯一**
## 一行初始成员——主角的 `char_id` 又来自玩家选的 `origin_id`，于是"选谁就是谁"，
## 结果**主角选别人时书生永远无法入队**。判重本来就有 `state.char_ids.has()` 兜着，
## 那句跳过是多余且致病的。
func _check_initial_member_can_join_when_not_protagonist(db) -> void:
	var initial := _first_row(db, func(r: Resource) -> bool: return int(r.is_initial) == 1)
	var other := _first_row(db, func(r: Resource) -> bool: return int(r.is_initial) == 0)
	check_not_null(initial, "recruit_def 里有初始成员")
	check_not_null(other, "recruit_def 里有非初始成员")
	if initial == null or other == null:
		return
	var initial_id := str(initial.char_id)
	var condition := str(initial.join_condition)
	var scene_id := _scene_for_row(db, initial)
	check_false(condition.is_empty(), "%s 表里有加入条件（0.30.0 补的）" % initial_id)
	check_false(scene_id.is_empty(), "%s 表里有加入场景（0.30.0 补的）" % initial_id)
	if condition.is_empty() or scene_id.is_empty():
		return
	var state = GameStateScript.new_game(db, "normal", PackedStringArray([str(other.char_id)]))
	check_false(state.char_ids.has(initial_id), "主角换成 %s 时 %s 不在开局队伍里" % [str(other.char_id), initial_id])
	check_false(_pending_ids(db, state, scene_id).has(initial_id), "条件没点亮时他还是不入队")
	state.set_flag(condition)
	check_true(
		_pending_ids(db, state, scene_id).has(initial_id),
		"点亮 %s 后他能按剧情入队（%s）" % [condition, scene_id],
	)


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
