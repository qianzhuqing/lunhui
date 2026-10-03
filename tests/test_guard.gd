## 宝箱守卫（设计 09 §一）：文取／武取两条路给**同一个**箱子；没有守卫的箱子照旧直接开。
##
## 用例全部从 `npc_guard` 里挑**真实行**跑（不写死 guard_hc_02／item_med_01），
## 表改了这里跟着走。
extends "res://tests/test_case.gd"

const GuardServiceScript := preload("res://src/core/guard_service.gd")


func suite_name() -> String:
	return "宝箱守卫"


func run() -> void:
	var db = get_db()
	_check_table_pairing(db)
	_check_no_guard_room(db)
	_check_item_path(db)
	_check_check_path(db)
	_check_fight_path(db)


## 表侧配对：守卫的 `reward_group` 就是那间房的 `chest_id`（两条路给同一个箱子）
func _check_table_pairing(db) -> void:
	var guards: Array = db.rows("npc_guard")
	check_gt(float(guards.size()), 0.0, "表里有守卫（%d 条）" % guards.size())
	for row: Resource in guards:
		var room: Resource = db.get_row("dungeon_room", str(row.room_id))
		check_not_null(room, "%s 的 room_id 在 dungeon_room 里" % str(row.guard_id))
		if room != null:
			check_eq(
				str(row.reward_group), str(room.chest_id),
				"%s 的 reward_group 就是那间房的箱子（两条路同一个箱子）" % str(row.guard_id)
			)


## 没有守卫的房间：`is_resolved` 恒真——照旧「进房间就能开」
func _check_no_guard_room(db) -> void:
	var state = solo_state(db)
	check_null(GuardServiceScript.guard_for_room(db, "hf1_shed"), "柴房确实没有守卫行")
	check_true(GuardServiceScript.is_resolved(db, state, "scene_heifengzhai", "hf1_shed"), "没有守卫的房间照旧可直接开")
	check_eq(GuardServiceScript.room_of_chest_key("hf1_shed|drop_chest_copper"), "hf1_shed", "宝箱键能解析出房间那半边")
	check_eq(GuardServiceScript.room_of_chest_key("没有分隔符"), "", "认不出的键返回空（不当成某个房间）")


## 文取的物品路：没东西时不可用；给一份 → 扣掉 + 点亮旗标 + 箱子解锁
func _check_item_path(db) -> void:
	var guard := _first_guard(db)
	check_not_null(guard, "表里第一位守卫（用例对象）")
	if guard == null:
		return
	var scene_id := _scene_of(db, guard)
	var room_id := str(guard.room_id)
	var state = solo_state(db)
	check_false(GuardServiceScript.is_resolved(db, state, scene_id, room_id), "新档：守卫还没解决")

	var item_id := ""
	for option: Dictionary in GuardServiceScript.peace_options(db, state, guard):
		if str(option["kind"]) != "item":
			continue
		item_id = str(option["id"])
		check_false(bool(option["ok"]), "身上没有那份东西时这条路不可用：%s" % str(option["reason"]))
		check_true(str(option["reason"]).contains("背包"), "原因说清是背包里没有")
	check_ne(item_id, "", "这位守卫的文取里写了物品那半")

	var demand := GuardServiceScript.demand_text(db, state, guard)
	check_true(demand.contains(str(guard.npc_name_cn)), "开口文案写清是谁挡着：%s" % demand)
	check_true(demand.contains(str(guard.fight_note)), "另一条路直接取表里的 fight_note（不自己编）")
	check_false(demand.contains("Q53"), "给玩家看的话里不出现内部编号：%s" % demand)

	# 付账：只扣一份，旗标进存档
	state.inventory.add_item(db, item_id, 2)
	var paid: Dictionary = GuardServiceScript.pay_peace(db, state, guard, "item", item_id)
	check_true(bool(paid["ok"]), "给了东西就能付账：%s" % str(paid["error"]))
	check_eq(state.inventory.count(item_id), 1, "只扣一份")
	check_true(state.has_flag(GuardServiceScript.peace_flag(str(guard.guard_id))), "点亮文取旗标")
	check_true(GuardServiceScript.is_resolved(db, state, scene_id, room_id), "付过之后这个箱子解锁")
	check_eq(str(paid["text"]), str(guard.peace_note), "放行台词取表里的 peace_note")

	# 没有东西时付不了账（控制器第二次按 E 会走到这里）
	var empty = solo_state(db)
	var refused: Dictionary = GuardServiceScript.pay_peace(db, empty, guard, "item", item_id)
	check_false(bool(refused["ok"]), "身上没有时付不了账")
	check_false(empty.has_flag(GuardServiceScript.peace_flag(str(guard.guard_id))), "付失败不点旗标")


## 判定路（0.14.0 的门槛拆列之后**真的能走**）：过门槛就放行，且**不消耗任何东西**。
##
## 设计 0.14.0 把原来那列散文里的「医术判定 3」搬进了 `peace_check_value`（Q53），
## 所以这条路从「明确不可用」变成「可用」——旧的 `_check_skill_path_pending` 随之作废。
func _check_check_path(db) -> void:
	var guard := _first_guard(db)
	if guard == null:
		return
	var source := str(guard.peace_check_source)
	check_ne(source, "", "这位守卫写了判定路（peace_check_source）")
	check_gt(float(guard.peace_check_value), 0.0, "判定路有门槛值（peace_check_value）：%d" % int(guard.peace_check_value))
	var state = solo_state(db)
	var found := false
	for option: Dictionary in GuardServiceScript.peace_options(db, state, guard):
		if str(option["kind"]) == "item":
			continue
		found = true
		# 书生在医术上的判定值 = 等级 3 + 智 10/5 = 5（门槛 3）→ 应当**已经够**
		check_gt(float(option["have"]), 0.0, "判定路的当前值算得出来：%s" % str(option))
		check_eq(int(option["need"]), int(guard.peace_check_value), "门槛取表里的值")
		var passed: bool = int(option["have"]) >= int(option["need"])
		check_eq(bool(option["ok"]), passed, "够门槛就标可用（当前 %d／门槛 %d）" % [int(option["have"]), int(option["need"])])
		if passed:
			var before: int = state.inventory.item_ids().size()
			var paid: Dictionary = GuardServiceScript.pay_peace(db, state, guard, str(option["kind"]), str(option["id"]))
			check_true(bool(paid["ok"]), "判定过了就能付账：%s" % str(paid["error"]))
			check_true(state.has_flag(GuardServiceScript.peace_flag(str(guard.guard_id))), "判定路也点亮同一枚旗标")
			check_eq(state.inventory.item_ids().size(), before, "判定路**不消耗任何东西**")
			break
	check_true(found, "这位守卫的判定路确实进了选项列表（否则上面几条是空跑）")
	# 门槛抬到不可能达成的值 → 这条路要如实不可用（不静默放行）
	var local = TableDbScript.new()
	local.load_all()
	var table: Resource = local.tables["npc_guard"].duplicate(true)
	table.rows[0].peace_check_value = 999
	table.rows[0].peace_item = ""
	var strict = solo_state(local)
	var blocked := false
	for option: Dictionary in GuardServiceScript.peace_options(local, strict, table.rows[0]):
		if str(option["kind"]) != "item" and not bool(option["ok"]):
			blocked = true
	check_true(blocked, "门槛高到达不到时如实标不可用（不是悄悄放行）")
	var refused: Dictionary = GuardServiceScript.pay_peace(local, strict, table.rows[0], "skill", str(guard.peace_check_source).split(":")[1])
	check_false(bool(refused["ok"]), "达不到门槛时付不了账")
	check_false(strict.has_flag(GuardServiceScript.peace_flag(str(guard.guard_id))), "失败不点旗标")
	# 边界：**刚好等于门槛算过**（`have >= need`，不是 `>`）——把门槛设成当前判定值，再高一点
	var exact = TableDbScript.new()
	exact.load_all()
	var exact_table: Resource = exact.tables["npc_guard"].duplicate(true)
	exact_table.rows[0].peace_item = ""
	var exact_state = solo_state(exact)
	var value := GuardServiceScript.check_value_of(exact, exact_state, str(exact_table.rows[0].peace_check_source))
	check_gt(float(value), 0.0, "先拿到当前判定值（%d）" % value)
	exact_table.rows[0].peace_check_value = value
	var boundary_ok := false
	for option: Dictionary in GuardServiceScript.peace_options(exact, exact_state, exact_table.rows[0]):
		if str(option["kind"]) != "item":
			boundary_ok = bool(option["ok"])
	check_true(boundary_ok, "判定值 == 门槛时算通过（含等号）")
	exact_table.rows[0].peace_check_value = value + 1
	var above_ok := false
	for option: Dictionary in GuardServiceScript.peace_options(exact, exact_state, exact_table.rows[0]):
		if str(option["kind"]) != "item":
			above_ok = bool(option["ok"])
	check_false(above_ok, "门槛再高一点就不过（不是「永远算过」）")


## 武取：房间记录一写（＝打赢房里那场仗）守卫就算解决，不需要额外按键
func _check_fight_path(db) -> void:
	var guard := _first_guard(db)
	if guard == null:
		return
	var state = solo_state(db)
	var scene_id := _scene_of(db, guard)
	var room_id := str(guard.room_id)
	var room: Resource = db.get_row("dungeon_room", room_id)
	check_not_null(room, "守卫那间房在 dungeon_room 里")
	if room == null:
		return
	check_eq(str(guard.fight_team), str(room.enemy_team), "武取打的就是房里那支队伍（表里是同一条）")
	check_false(GuardServiceScript.is_resolved(db, state, scene_id, room_id), "还没打：先确认初始状态")
	state.record_dungeon(scene_id, "rooms", room_id)
	check_true(GuardServiceScript.is_resolved(db, state, scene_id, room_id), "打赢房间之后箱子解锁")


# ------------------------------------------------------------------ 夹具

func _first_guard(db) -> Resource:
	var rows: Array = db.rows("npc_guard")
	return rows[0] if not rows.is_empty() else null


func _scene_of(db, row: Resource) -> String:
	var room: Resource = db.get_row("dungeon_room", str(row.room_id))
	return str(room.scene_id) if room != null else ""
