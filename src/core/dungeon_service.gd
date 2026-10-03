## 副本完成度与扫荡（03_副本_黑风寨.md「商业与重复性」「完成度」）。
##
## 完成度四项（只作为追求目标，**不影响奖励**）：宝箱开启数／隐藏房间进入数／隐藏 Boss 击败／事件触发。
## 数据来自两张表 + 存档里的永久记录 `GameState.dungeon_records`：
##   `dungeon_room`   房间、层、宝箱（chest_id）、敌人队伍、房间类型
##   `hidden_trigger` 隐藏点位的类型与奖励类型
##
## 扫荡：**已通关层**可扫荡，只结算掉落（不给经验）——「已通关」= 该层所有带敌人的房间都清过，
## 记录同样落在 `dungeon_records.rooms` 里（打一场就记一次，永久保留）。
class_name DungeonService
extends RefCounted

const DropResolverScript := preload("res://src/core/drop_resolver.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

## 隐藏 Boss 的判定：威胁标记为紫色，且不在 boss 房间的队伍里（那些是章节 Boss）
const HIDDEN_BOSS_THREAT := "purple"

var db
var state
var rng


func _init(table_db, game_state, rng_service = null) -> void:
	db = table_db
	state = game_state
	rng = rng_service if rng_service != null else RngServiceScript.new()


## 这张图的房间行（按层、按表顺序）
func rooms_of(scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == scene_id:
			out.append(row)
	out.sort_custom(func(a: Resource, b: Resource) -> bool:
		return int(a.floor) < int(b.floor)
	)
	return out


func trigger_rows(scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("hidden_trigger"):
		if str(row.scene_id) == scene_id:
			out.append(row)
	return out


## 章节 Boss 的敌人 id（boss 房间的队伍成员）——用来把「隐藏 Boss」和它区分开
func chapter_boss_ids(scene_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in rooms_of(scene_id):
		if str(row.room_type) != "boss":
			continue
		var team: Resource = db.get_row("enemy_team", str(row.enemy_team))
		if team == null:
			continue
		for member: Dictionary in team.parsed_members():
			out.append(str(member.get("enemy_id", "")))
	return out


## 这张图里所有「紫名但不是章节 Boss」的敌人 id（打完记一笔就是隐藏 Boss 击败）
func hidden_boss_ids(scene_id: String) -> PackedStringArray:
	var chapter := chapter_boss_ids(scene_id)
	var out := PackedStringArray()
	for row: Resource in db.rows("enemy_base"):
		if str(row.threat_tag) != HIDDEN_BOSS_THREAT:
			continue
		var enemy_id := str(row.enemy_id)
		if chapter.has(enemy_id) or out.has(enemy_id):
			continue
		# 只算这张图里出现过的（房间队伍 / 隐藏点位的 Boss 奖励）
		if not _appears_in(scene_id, enemy_id):
			continue
		out.append(enemy_id)
	return out


func _appears_in(scene_id: String, enemy_id: String) -> bool:
	for row: Resource in rooms_of(scene_id):
		var team: Resource = db.get_row("enemy_team", str(row.enemy_team))
		if team != null:
			for member: Dictionary in team.parsed_members():
				if str(member.get("enemy_id", "")) == enemy_id:
					return true
	for trigger: Resource in trigger_rows(scene_id):
		if str(trigger.reward_type) == "boss" and str(trigger.reward_id) == enemy_id:
			return true
	return false


## 完成度快照：{chests:{done,total}, hidden_rooms:{done,total}, hidden_boss:{done,total},
##            events:{done,total}, percent}
func progress(scene_id: String) -> Dictionary:
	var record: Dictionary = state.dungeon_record(scene_id) if state != null else {}
	var room_rows := rooms_of(scene_id)
	var chest_total := 0
	for row: Resource in room_rows:
		if not str(row.chest_id).is_empty():
			chest_total += 1
	var hidden_rooms := 0
	for row: Resource in room_rows:
		# 表里没有 room_type=hidden：隐藏房间就是 branch_group = hidden（密室／牢房深处／密道／后山地牢）
		if str(row.branch_group) == "hidden":
			hidden_rooms += 1
	var event_ids := PackedStringArray()
	for trigger: Resource in trigger_rows(scene_id):
		if str(trigger.reward_type) == "event":
			event_ids.append(str(trigger.trigger_id))
	var boss_ids := hidden_boss_ids(scene_id)
	var chest_done := _count_intersect(Array(record.get("chests", [])), _chest_keys(scene_id))
	var rooms_done := _count_intersect(Array(record.get("rooms_entered", [])), _hidden_room_ids(scene_id))
	var boss_done := _count_intersect(Array(record.get("bosses", [])), Array(boss_ids))
	var event_done := _count_intersect(Array(record.get("triggers", [])), Array(event_ids))
	var done := chest_done + rooms_done + boss_done + event_done
	var total := chest_total + hidden_rooms + boss_ids.size() + event_ids.size()
	return {
		"chests": {"done": chest_done, "total": chest_total},
		"hidden_rooms": {"done": rooms_done, "total": hidden_rooms},
		"hidden_boss": {"done": boss_done, "total": boss_ids.size()},
		"events": {"done": event_done, "total": event_ids.size()},
		"done": done,
		"total": total,
		"percent": 0 if total <= 0 else int(round(float(done) * 100.0 / float(total))),
	}


## 一行给 HUD 的文案
func progress_text(scene_id: String) -> String:
	var snapshot := progress(scene_id)
	return "完成度 %d%%　宝箱 %d/%d　隐藏房间 %d/%d　隐藏Boss %d/%d　事件 %d/%d" % [
		int(snapshot["percent"]),
		int(snapshot["chests"]["done"]), int(snapshot["chests"]["total"]),
		int(snapshot["hidden_rooms"]["done"]), int(snapshot["hidden_rooms"]["total"]),
		int(snapshot["hidden_boss"]["done"]), int(snapshot["hidden_boss"]["total"]),
		int(snapshot["events"]["done"]), int(snapshot["events"]["total"]),
	]


func _chest_keys(scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in rooms_of(scene_id):
		var key := str(row.chest_id)
		if not key.is_empty():
			# 同一个掉落组可能被两个房间各放一个宝箱：键必须带上房间，和小地图里的一致
			out.append("%s|%s" % [str(row.room_id), key])
	return out


func _hidden_room_ids(scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in rooms_of(scene_id):
		if str(row.branch_group) == "hidden":
			out.append(str(row.room_id))
	return out


func _count_intersect(record: Array, wanted: Array) -> int:
	var count := 0
	for key: Variant in wanted:
		if record.has(str(key)):
			count += 1
	return count


# ------------------------------------------------------------------ 扫荡

## 按层列出：{floor, rooms:[room_id], enemy_rooms:[room_id], cleared:[room_id], sweepable}
func floors(scene_id: String) -> Array:
	var record: Dictionary = state.dungeon_record(scene_id) if state != null else {}
	var cleared: Array = record.get("rooms", [])
	var by_floor: Dictionary = {}
	var order: Array = []
	for row: Resource in rooms_of(scene_id):
		var floor := int(row.floor)
		if not by_floor.has(floor):
			by_floor[floor] = {"floor": floor, "rooms": [], "enemy_rooms": [], "cleared": []}
			order.append(floor)
		var entry: Dictionary = by_floor[floor]
		var room_id := str(row.room_id)
		entry["rooms"].append(room_id)
		if not str(row.enemy_team).is_empty():
			entry["enemy_rooms"].append(room_id)
			if cleared.has(room_id):
				entry["cleared"].append(room_id)
		by_floor[floor] = entry
	var out: Array = []
	order.sort()
	for floor: int in order:
		var entry: Dictionary = by_floor[floor]
		var enemy_rooms: Array = entry["enemy_rooms"]
		entry["sweepable"] = not enemy_rooms.is_empty() and enemy_rooms.size() == Array(entry["cleared"]).size()
		out.append(entry)
	return out


func sweepable_floors(scene_id: String) -> Array:
	var out: Array = []
	for entry: Dictionary in floors(scene_id):
		if bool(entry["sweepable"]):
			out.append(int(entry["floor"]))
	return out


## 扫荡某一层：只结算掉落，不给经验（设计：「仅结算掉落」）
## 返回 {ok, error, floor, drops, applied, summary}
func sweep(scene_id: String, floor_number: int) -> Dictionary:
	if state == null or state.inventory == null:
		return {"ok": false, "error": "没有会话状态", "floor": floor_number, "drops": [], "applied": {}, "summary": ""}
	var target: Dictionary = {}
	for entry: Dictionary in floors(scene_id):
		if int(entry["floor"]) == floor_number:
			target = entry
			break
	if target.is_empty():
		return {"ok": false, "error": "没有这一层", "floor": floor_number, "drops": [], "applied": {}, "summary": ""}
	if not bool(target["sweepable"]):
		var left := PackedStringArray()
		for room_id: String in Array(target["enemy_rooms"]):
			if not Array(target["cleared"]).has(room_id):
				left.append(db.display_name(room_id))
		return {
			"ok": false,
			"error": "还没通关这一层（没打过的房间：%s）" % "、".join(left),
			"floor": floor_number, "drops": [], "applied": {}, "summary": "",
		}
	var pity = state.pity_tracker()
	var resolver = DropResolverScript.new(db, rng, pity)
	var bonus := DropResolverScript.party_drop_bonus(PartyBuilderScript.build_actors(db, state))
	var drops: Array = []
	for row: Resource in rooms_of(scene_id):
		if int(row.floor) != floor_number or str(row.enemy_team).is_empty():
			continue
		var team: Resource = db.get_row("enemy_team", str(row.enemy_team))
		if team == null:
			continue
		for member: Dictionary in team.parsed_members():
			var enemy_id := str(member.get("enemy_id", ""))
			var count := maxi(1, int(member.get("count", 1)))
			var enemy: Resource = db.get_row("enemy_base", enemy_id)
			if enemy == null or str(enemy.drop_group).is_empty():
				continue
			for index in range(count):
				drops.append_array(resolver.roll_group(
					str(enemy.drop_group), state.difficulty_id,
					{"first_kill": false, "drop_rate_bonus": bonus}
				))
	var applied: Dictionary = BattleRewardScript.grant(
		db, state, 0, 0, drops, AffixRollerScript.new(db, rng)
	)
	# 扫荡也要把保底计数存回去（跨战斗、跨难度继承）
	state.store_pity(pity)
	return {
		"ok": bool(applied["ok"]),
		"error": "" if bool(applied["ok"]) else "、".join(applied["errors"]),
		"floor": floor_number,
		"drops": drops,
		"applied": applied,
		"summary": _summary(applied),
	}


func _summary(applied: Dictionary) -> String:
	var parts := PackedStringArray()
	parts.append("铜钱 +%d" % int(applied.get("money", 0)))
	if not Array(applied.get("items", [])).is_empty():
		var item_names := PackedStringArray()
		for entry: Dictionary in Array(applied["items"]):
			item_names.append("%s×%d" % [item_name(str(entry["item_id"])), int(entry["qty"])])
		parts.append("物品 " + "、".join(item_names))
	if not Array(applied.get("equipment", [])).is_empty():
		parts.append("装备 %d 件" % Array(applied["equipment"]).size())
	var overflow: Array = Array(applied.get("overflow", []))
	if not overflow.is_empty():
		var names := PackedStringArray()
		for entry: Dictionary in overflow:
			names.append("%s×%d" % [item_name(str(entry["item_id"])), int(entry["qty"])])
		parts.append("背包满，没捡起：" + "、".join(names))
	return "　".join(parts)


func item_name(item_id: String) -> String:
	# 装备实例 id（`eq_sword_01#1`）也走这里：TableDb.display_name 会剥掉 #序号
	return db.display_name(item_id)
