## 套装效果（docs/design/08_增益与套装.md）。
##
## 三类套装共用一套规则，区别只在**计数口径**：
##   - `equip`         按当前**穿在身上**的件数
##   - `skill_active`  按当前**装配**的招式数
##   - `skill_passive` 按当前**装配**的内功**占格数之和**（内功本来就是占格制）
##
## 两条关键规则（设计原文）：
##   ① **只算已装备／已装配的成员**——背包里、学会了但没装的不算，否则玩家会为了凑套装全带上；
##   ② **档位向下兼容、各自独立叠加**——达到 4 件时 2 件档的 buff 也生效。
class_name SetService
extends RefCounted

const KIND_EQUIP := "equip"
const KIND_ACTIVE := "skill_active"
const KIND_PASSIVE := "skill_passive"

var db


func _init(table_db) -> void:
	db = table_db


func all_defs() -> Array:
	return db.rows("set_def")


func def_of(set_id: String) -> Resource:
	return db.get_row("set_def", set_id)


func exists(set_id: String) -> bool:
	return def_of(set_id) != null


func kind_of(set_id: String) -> String:
	var row: Resource = def_of(set_id)
	return str(row.set_kind) if row != null else ""


func members_of(set_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows("set_member"):
		if str(row.set_id) == set_id:
			out.append(str(row.member_id))
	return out


## 档位表，按 required_count 升序（0 件档在前）。
func bonuses_of(set_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("set_bonus"):
		if str(row.set_id) == set_id:
			out.append(row)
	out.sort_custom(func(a, b): return int(a.required_count) < int(b.required_count))
	return out


## 当前达成几件／几招／几格。
##
## `inventory` 给已穿戴记录（装备套用），`state` 给装配表（招式／内功套用）。
func count_for(set_id: String, char_id: String, inventory, state) -> int:
	var members := members_of(set_id)
	if members.is_empty():
		return 0
	var kind := kind_of(set_id)
	var loadout: Dictionary = state.loadout_of(char_id) if state != null else {}
	match kind:
		KIND_EQUIP:
			if inventory == null:
				return 0
			var equipped: Dictionary = {}
			var slots: Dictionary = inventory.equipment_slots(db, char_id)
			for slot_id: String in slots:
				for instance_id: String in Array(slots[slot_id]):
					var entry: Dictionary = inventory.equipment.get(instance_id, {})
					equipped[str(entry.get("base_id", ""))] = true
			var worn := 0
			for member_id: String in members:
				if equipped.has(member_id):
					worn += 1
			return worn
		KIND_ACTIVE:
			var actives: PackedStringArray = PackedStringArray(loadout.get("active", PackedStringArray()))
			var assembled := 0
			for member_id: String in members:
				if actives.has(member_id):
					assembled += 1
			return assembled
		KIND_PASSIVE:
			# 内功套按**占格数之和**：每部成员贡献 `skill_passive.slot_cost` 格
			var passives: PackedStringArray = PackedStringArray(loadout.get("passive", PackedStringArray()))
			var slots := 0
			for member_id: String in members:
				if not passives.has(member_id):
					continue
				var row: Resource = db.get_row("skill_passive", member_id)
				slots += int(row.slot_cost) if row != null else 0
			return slots
	return 0


## 当前生效的档位（`required_count <= count`，**向下兼容**）：返回 `set_bonus` 行数组。
func active_bonuses(set_id: String, count: int) -> Array:
	var out: Array = []
	for row: Resource in bonuses_of(set_id):
		if int(row.required_count) <= count:
			out.append(row)
	return out


## 当前生效档位对应的 buff_id（按档位升序）。
func active_buff_ids(set_id: String, count: int) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in active_bonuses(set_id, count):
		out.append(str(row.buff_id))
	return out
