## 宝箱守卫（设计 09 §一）：宝箱不再由「打怪」解锁，而是绑给一个 NPC，文取／武取任选其一。
##
## 数据源 `npc_guard.csv`：`room_id` 指到 `dungeon_room`（那间房 + 房里那个箱子），
## `peace_condition` 是文取的条件（`|` 分隔的 `item:<id>`／`skill:<id>`），`fight_team` 是
## 武取要打的那支队伍——它与 `dungeon_room.enemy_team` 是同一条，所以**武取就是房间里那场仗**。
##
## 两条路径给**同一个宝箱**（`reward_group` 与 `dungeon_room.chest_id` 一致，构建期
## `_check_npc_guards` 盯着）：差别在代价，不在奖励——武取多拿战斗经验与掉落，文取多拿线索。
##
## 「解决了吗」写在**存档**里，两条路各一份：
##   武取 → `dungeon_records[scene].rooms` 记下这间房（打赢房间战本来就是永久记录）
##   文取 → 旗标 `flag_guard_<guard_id>_peace`
## **没有守卫的箱子照旧「进房间就能开」**：`guard_for_room()` 返回 null 时 `is_resolved()` 直接为真。
class_name GuardService
extends RefCounted

const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")


## 文取解决的旗标名（唯一出处：判「解决了吗」与「付完点亮」都用它）
static func peace_flag(guard_id: String) -> String:
	return "flag_guard_%s_peace" % guard_id


## 这间房有没有守卫（`npc_guard.room_id`）；没有就是普通箱子
static func guard_for_room(db, room_id: String) -> Resource:
	if room_id.is_empty():
		return null
	for row: Resource in db.rows("npc_guard"):
		if str(row.room_id) == room_id:
			return row
	return null


## 宝箱的会话键是 `"<room_id>|<掉落组>"`（`local_map_controller._spawn_chests` 写的），
## 这里取房间那半边，用来查守卫。
static func room_of_chest_key(chest_key: String) -> String:
	if not chest_key.contains("|"):
		return ""
	return chest_key.split("|")[0]


## 守卫解决了没有。**没有守卫的箱子恒为真**（照旧能直接开）。
static func is_resolved(db, state, scene_id: String, room_id: String) -> bool:
	var row: Resource = guard_for_room(db, room_id)
	if row == null:
		return true
	if state == null:
		return false
	if state.has_flag(peace_flag(str(row.guard_id))):
		return true
	return Array(state.dungeon_record(scene_id).get("rooms", [])).has(room_id)


## 文取的可选项（解析 `peace_condition`）。每条：
## 0.14.0 起文取是三列（`peace_item`／`peace_check_source`／`peace_check_value`），
## **可叠加、满足任意一条即算过**（06 的口径）。返回每条：
## {kind:"item"/"skill"/"attr", id, name_cn, need, have, ok, reason}
static func peace_options(db, state, row: Resource) -> Array:
	var out: Array = []
	if row == null:
		return out
	# ① 交道具
	var item_id := str(row.peace_item).strip_edges()
	if not item_id.is_empty():
		var item_row: Resource = db.get_row("item_base", item_id)
		var have := 0
		if state != null and state.inventory != null:
			have = state.inventory.count(item_id)
		out.append({
			"kind": "item", "id": item_id, "have": have, "need": 1,
			"name_cn": str(item_row.name_cn) if item_row != null else item_id,
			"ok": have >= 1,
			"reason": "" if have >= 1 else "背包里没有这份东西",
		})
	# ② 过一条判定（skill:<非战斗技能> / attr:<属性>），门槛取 `peace_check_value`
	var source := str(row.peace_check_source).strip_edges()
	if not source.is_empty():
		var parts := source.split(":", false)
		var kind := parts[0].strip_edges() if parts.size() >= 2 else ""
		var target_id := parts[1].strip_edges() if parts.size() >= 2 else source
		var need := int(row.peace_check_value)
		var have := check_value_of(db, state, source)
		out.append({
			"kind": kind, "id": target_id, "have": have, "need": need,
			"name_cn": _check_source_name(db, kind, target_id),
			"ok": need > 0 and have >= need,
			"reason": "" if (need > 0 and have >= need) else "%s %d ＜ 门槛 %d" % [
				_check_source_name(db, kind, target_id), have, need,
			],
		})
	return out


## 这条判定的名字（给玩家看的）：`skill:` 查非战斗技能表、`attr:` 查属性表
static func _check_source_name(db, kind: String, target_id: String) -> String:
	match kind:
		"skill":
			var skill_row: Resource = db.get_row("event_skill_def", target_id)
			return str(skill_row.name_cn) if skill_row != null else target_id
		"attr":
			var attr_row: Resource = db.get_row("attribute_def", target_id)
			return str(attr_row.name_cn) if attr_row != null else target_id
		_:
			return target_id


## 队伍在这条判定上的**最好成绩**（与事件判定同一套算法：属性取裸值、技能取
## 「等级 + floor(裸属性/5)」，见 `EventCheckService.best_check_value`）。
## 复用那一个服务而不是自己再算一遍——同一事实只许有一处定义（决策 88）。
static func check_value_of(db, state, source: String) -> int:
	if state == null:
		return 0
	var service = EventCheckServiceScript.new(db, state)
	var best: Dictionary = service.best_check_value(source)
	return int(best.get("value", 0))


## 给玩家的一句话：谁挡着、他要什么、另一条路是什么。
## 文案取自表里的 `peace_note`／`fight_note`（设计原文），不自己编。
static func demand_text(db, state, row: Resource) -> String:
	if row == null:
		return ""
	var wants := PackedStringArray()
	var confirm := ""
	for option: Dictionary in peace_options(db, state, row):
		# 每条都把「差多少」写出来：道具写背包里有几份，判定写「当前值／门槛」
		if str(option["kind"]) == "item":
			wants.append("%s（背包里 %d 份）" % [str(option["name_cn"]), int(option["have"])])
		else:
			wants.append("%s（%d／%d）" % [str(option["name_cn"]), int(option["have"]), int(option["need"])])
		if bool(option["ok"]) and confirm.is_empty():
			confirm = "再按一次 E 就走这条：%s" % str(option["name_cn"])
	var head := "%s挡在宝箱前，要 %s。" % [str(row.npc_name_cn), "、".join(wants)]
	var hint := str(row.fight_note)
	if hint.is_empty():
		hint = "打赢照样拿箱子"
	return "%s%s；或者直接动手——%s。" % [head, ("（%s）" % confirm if not confirm.is_empty() else ""), hint]


## 付文取的账（0.14.0：两条路都能付）：
##   `item`        → 扣掉一件道具
##   `skill`／`attr` → 判定**再过一次**（不凭调用方说「够了」就放行；门槛的唯一出处是表）
## 返回 {ok, error, text}——`text` 是表里的 `peace_note`（设计写的放行台词／线索）。
static func pay_peace(db, state, row: Resource, kind: String, target_id: String) -> Dictionary:
	if row == null or state == null:
		return {"ok": false, "error": "没有守卫或会话状态", "text": ""}
	match kind:
		"item":
			if state.inventory == null or not state.inventory.has(target_id):
				return {"ok": false, "error": "背包里没有他要的东西", "text": ""}
			var removed: Dictionary = state.inventory.remove_item(db, target_id, 1)
			if not bool(removed.get("ok", false)):
				return {"ok": false, "error": str(removed.get("error", "给不出去")), "text": ""}
		"skill", "attr":
			var source := str(row.peace_check_source).strip_edges()
			var need := int(row.peace_check_value)
			var have := check_value_of(db, state, source)
			if need <= 0 or have < need:
				return {
					"ok": false, "text": "",
					"error": "%s %d ＜ 门槛 %d" % [_check_source_name(db, kind, target_id), have, need],
				}
		_:
			return {"ok": false, "error": "文取条件类型不认识：%s" % kind, "text": ""}
	state.set_flag(peace_flag(str(row.guard_id)))
	return {"ok": true, "error": "", "text": str(row.peace_note)}
