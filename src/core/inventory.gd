## 背包与装备。
##
## 纯逻辑 + 可序列化：物品堆叠、装备实例、穿戴映射、铜钱。
## 不依赖场景树，也不依赖 GameState，所以能被面板、战斗与存档共用。
##
## 装备实例 id 形如 `eq_sword_01#3`（基础 id + 序号），序号存在存档里，读档后不会重复。
## 槽位容量取自 equip_slot_def.max_equip（戒指 2），槽位数组定长，空位存空串。
class_name Inventory
extends RefCounted

const AffixRollerScript := preload("res://src/core/affix_roller.gd")
## 装备自带的常驻 buff（08：装备特效也是 buff）——贡献的换算只写在 BuffService 里
const BuffServiceScript := preload("res://src/core/buff_service.gd")
## 贡献 kind 字符串的**唯一定义处**（写错会被 AttributeCalculator 静默跳过，所以不在这里再写一遍字面量）
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

## 背包**没有自己的版本号**：存档结构的唯一迁移闸门是 `GameState.VERSION`（见 AGENTS「存档结构」），
## `from_dict` 对缺字段一律取默认值（v1 那种没有 `affixes` 的装备条目照样读得进来）。
## 以前这里有一个 `VERSION := 2` 并写进存档，但**全项目没有任何地方读它**（变异探针扫出来的，
## 见框架说明决策 154）——留着一个没人读的版本号，只会让人以为背包会自己迁移。已删。
const UID_SEPARATOR := "#"

## item_id → 数量
var stacks: Dictionary = {}
## instance_id → {"base_id": String, "affixes": [{affix_id, target, value_kind, value}]}
var equipment: Dictionary = {}
## char_id → { slot_id: Array[instance_id] }，定长数组，空位为 ""
var equipped: Dictionary = {}
var money: int = 0
## 下一个装备实例序号
var next_uid: int = 1


# ------------------------------------------------------------------ 物品堆叠

func count(item_id: String) -> int:
	return int(stacks.get(item_id, 0))


func has(item_id: String, qty: int = 1) -> bool:
	return count(item_id) >= qty


func item_ids() -> PackedStringArray:
	var out := PackedStringArray()
	for item_id: String in stacks:
		if int(stacks[item_id]) > 0:
			out.append(item_id)
	out.sort()
	return out


## 放入物品。超过 stack_max 的部分会被截断并在 overflow 里报出来。
## 返回 {ok, added, overflow, error}
func add_item(db, item_id: String, qty: int = 1) -> Dictionary:
	if qty <= 0:
		return {"ok": false, "added": 0, "overflow": 0, "error": "数量必须大于 0"}
	if db.get_row("equip_base", item_id) != null:
		return {"ok": false, "added": 0, "overflow": 0, "error": "%s 是装备，要用 add_equipment" % item_id}
	var row: Resource = db.get_row("item_base", item_id)
	if row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[Inventory] item_base 里没有 %s" % item_id)
		return {"ok": false, "added": 0, "overflow": 0, "error": "这件东西不在配置里（数据错，已记进日志）"}
	var current := count(item_id)
	var added: int = mini(qty, room_for(db, item_id))
	var overflow: int = qty - added
	stacks[item_id] = current + added
	return {"ok": added > 0, "added": added, "overflow": overflow, "error": "" if added > 0 else "背包已满"}


## 这一堆还能放下几个（`stack_max - 现有`）；表里没有这个物品返回 0。
## **`add_item` 与回购的「部分成交」都用它**：同一件事只算一次，免得两边对不上。
func room_for(db, item_id: String) -> int:
	var row: Resource = db.get_row("item_base", item_id)
	if row == null:
		return 0
	return maxi(0, maxi(1, int(row.stack_max)) - count(item_id))


## 丢掉物品。钥匙道具（is_key_item=1）不允许丢弃——它们通常是隐藏内容的载体。
## 返回 {ok, removed, error}
func remove_item(db, item_id: String, qty: int = 1) -> Dictionary:
	if qty <= 0:
		return {"ok": false, "removed": 0, "error": "数量必须大于 0"}
	var row: Resource = db.get_row("item_base", item_id)
	if row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[Inventory] item_base 里没有 %s（丢弃）" % item_id)
		return {"ok": false, "removed": 0, "error": "这件东西不在配置里（数据错，已记进日志）"}
	if bool(row.is_key_item):
		return {"ok": false, "removed": 0, "error": "%s 是钥匙道具，不能丢弃" % row.name_cn}
	var current := count(item_id)
	if current < qty:
		return {"ok": false, "removed": 0, "error": "数量不足（有 %d，需要 %d）" % [current, qty]}
	stacks[item_id] = current - qty
	if int(stacks[item_id]) <= 0:
		stacks.erase(item_id)
	return {"ok": true, "removed": qty, "error": ""}


## 消耗物品（研读秘籍、吃药这类正当用途）。
## 与 `remove_item` 的区别：**消耗不看 is_key_item**——钥匙道具不能「丢」，但可以被「用掉」。
## 返回 {ok, removed, error}
func consume(item_id: String, qty: int = 1) -> Dictionary:
	if qty <= 0:
		return {"ok": false, "removed": 0, "error": "数量必须大于 0"}
	var current := count(item_id)
	if current < qty:
		return {"ok": false, "removed": 0, "error": "数量不足（有 %d，需要 %d）" % [current, qty]}
	stacks[item_id] = current - qty
	if int(stacks[item_id]) <= 0:
		stacks.erase(item_id)
	return {"ok": true, "removed": qty, "error": ""}


# ------------------------------------------------------------------ 装备实例

func equipment_ids() -> PackedStringArray:
	var out := PackedStringArray()
	for instance_id: String in equipment:
		out.append(instance_id)
	out.sort()
	return out


func equipment_count() -> int:
	return equipment.size()


func has_equipment(instance_id: String) -> bool:
	return equipment.has(instance_id)


## 实例对应的本体 id（equip_base.equip_id），不存在返回空串。
func base_of(instance_id: String) -> String:
	var entry: Dictionary = equipment.get(instance_id, {})
	return str(entry.get("base_id", ""))


## 造一个装备实例，返回 instance_id；失败返回空串。
## affixes 由 AffixRoller 掷出来（掉落时生成；初始装备没有词条）。
func add_equipment(db, base_id: String, affixes: Array = []) -> String:
	if db.get_row("equip_base", base_id) == null:
		push_error("[Inventory] equip_base 里没有 %s" % base_id)
		return ""
	var instance_id := "%s%s%d" % [base_id, UID_SEPARATOR, next_uid]
	next_uid += 1
	equipment[instance_id] = {"base_id": base_id, "affixes": affixes.duplicate(true)}
	return instance_id


## 这件装备的随机词条（没有就是空数组）
func affixes_of(instance_id: String) -> Array:
	var entry: Dictionary = equipment.get(instance_id, {})
	var records: Variant = entry.get("affixes", [])
	return Array(records) if records is Array else []


## 删除未穿戴的实例；已穿戴的先卸下。
## 返回 {ok, error}
func remove_equipment(instance_id: String) -> Dictionary:
	if not equipment.has(instance_id):
		return {"ok": false, "error": "没有这件装备"}
	var owner := equipped_by(instance_id)
	if not owner.is_empty():
		return {"ok": false, "error": "先卸下装备再处理"}
	equipment.erase(instance_id)
	return {"ok": true, "error": ""}


## 这件装备穿在谁身上：返回 {"char_id", "slot_id", "index"}，没穿则空字典。
func equipped_by(instance_id: String) -> Dictionary:
	for char_id: String in equipped:
		var slots: Dictionary = equipped[char_id]
		for slot_id: String in slots:
			var entries: Array = slots[slot_id]
			for index in range(entries.size()):
				if str(entries[index]) == instance_id:
					return {"char_id": char_id, "slot_id": slot_id, "index": index}
	return {}


func is_equipped(instance_id: String) -> bool:
	return not equipped_by(instance_id).is_empty()


# ------------------------------------------------------------------ 穿戴

## 角色的槽位视图：slot_id → 定长数组（空位为 ""），按 equip_slot_def.sort_order 排列。
func equipment_slots(db, char_id: String) -> Dictionary:
	var slots: Dictionary = equipped.get(char_id, {})
	var capacities := _slot_capacities(db)
	var out: Dictionary = {}
	for slot_id: String in capacities:
		var capacity: int = int(capacities[slot_id])
		var entries: Array = slots.get(slot_id, [])
		var normalized: Array = []
		for index in range(capacity):
			normalized.append(str(entries[index]) if index < entries.size() else "")
		out[slot_id] = normalized
	return out


## 当前配表认得的穿戴格子：slot_id → 容量（按 equip_slot_def.sort_order 排好）。
##
## **这是「配表认不认得」的唯一出处**：界面视图（`equipment_slots`）与加成／武器判定
## （`equipped_instances`）都从这里来，不许一边看配表、一边直接翻存档里的穿戴记录——
## 那正是「设计删了槽位／把 max_equip 改小，存档里那件还在偷偷加属性、界面却看不见」的成因。
func _slot_capacities(db) -> Dictionary:
	var ordered: Array = []
	for slot: Resource in db.rows("equip_slot_def"):
		ordered.append(slot)
	ordered.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.sort_order) < int(b.sort_order))
	var out: Dictionary = {}
	for slot: Resource in ordered:
		var slot_id := str(slot.slot_id)
		if slot_id.is_empty():
			continue
		out[slot_id] = maxi(1, int(slot.max_equip))
	return out


## 把「当前配表装不下」的穿戴记录退回背包（只脱不删），返回退回来的实例 id。
##
## 什么时候会发生：设计改了 `equip_slot_def`——删掉一类槽位、或把 `max_equip` 改小（戒指 2 → 1），
## 而存档里还按旧配表穿着。以前这些记录**界面看不见、却照常加属性**（实测：容量改成 1 时两枚戒指
## 都还在贡献列表里；把 ring 整类删掉后它们连卸都卸不下来）。
## 改配表不该静默改变玩家属性，所以读档时取下来、放回背包（实例本体不删，玩家可以重新穿）。
## 一个槽位里保留靠前的格子（与界面显示的一致），其余退回；引用已不存在实例的记录直接清掉。
func reclaim_stranded_equipped(db, char_id: String) -> PackedStringArray:
	var reclaimed := PackedStringArray()
	var slots: Dictionary = equipped.get(char_id, {})
	if slots.is_empty():
		return reclaimed
	var capacities := _slot_capacities(db)
	var changed := false
	for slot_id: Variant in slots.keys():
		var key := str(slot_id)
		var entries: Array = slots[key]
		var capacity: int = int(capacities.get(key, 0))
		for index in range(entries.size()):
			var instance_id := str(entries[index])
			if instance_id.is_empty():
				continue
			if index < capacity and equipment.has(instance_id):
				continue
			entries[index] = ""
			changed = true
			if equipment.has(instance_id):
				reclaimed.append(instance_id)
		if entries.is_empty() and capacity <= 0:
			slots.erase(key)
			changed = true
	if changed:
		equipped[char_id] = slots
	return reclaimed


## 装备一件背包里的装备。
## 校验：角色等级 ≥ level_req；武器槽只能装模板武器类型。
## 槽位有空位就放空位，满了替换第一格并把被替换的实例 id 放在 replaced 里。
## 返回 {ok, error, slot_id, index, replaced}
func equip(db, char_id: String, level: int, weapon_type: String, instance_id: String) -> Dictionary:
	var check := can_equip(db, char_id, level, weapon_type, instance_id)
	if not check["ok"]:
		return {"ok": false, "error": check["error"], "slot_id": check["slot_id"], "index": -1, "replaced": ""}

	# 同一件装备不能同时在两处：先脱下
	var owner := equipped_by(instance_id)
	if not owner.is_empty():
		_clear_slot(str(owner["char_id"]), str(owner["slot_id"]), int(owner["index"]))

	var slot_id := str(check["slot_id"])
	var capacity := int(check["capacity"])
	var slots: Dictionary = equipped.get(char_id, {})
	var entries: Array = slots.get(slot_id, [])
	while entries.size() < capacity:
		entries.append("")
	var index := entries.find("")
	var replaced := ""
	if index < 0:
		index = 0
		replaced = str(entries[index])
	entries[index] = instance_id
	slots[slot_id] = entries
	equipped[char_id] = slots
	return {"ok": true, "error": "", "slot_id": slot_id, "index": index, "replaced": replaced}


## 能不能装（不改状态），给界面用来置灰与写原因。
## 返回 {ok, error, slot_id, capacity}
func can_equip(db, char_id: String, level: int, weapon_type: String, instance_id: String) -> Dictionary:
	if not equipment.has(instance_id):
		return {"ok": false, "error": "背包里没有这件装备", "slot_id": "", "capacity": 0}
	var base: Resource = db.get_row("equip_base", base_of(instance_id))
	if base == null:
		return {"ok": false, "error": "装备本体不存在", "slot_id": "", "capacity": 0}
	if int(base.level_req) > level:
		return {
			"ok": false, "error": "%s 需要 %d 级" % [base.name_cn, int(base.level_req)],
			"slot_id": "", "capacity": 0,
		}
	var slot_id := str(base.slot)
	var slot_row: Resource = db.get_row("equip_slot_def", slot_id)
	if slot_row == null:
		return {"ok": false, "error": "槽位 %s 未定义" % slot_id, "slot_id": "", "capacity": 0}
	var weapon := str(base.weapon_type)
	if slot_row.is_weapon_slot() and not weapon_type.is_empty() and weapon != weapon_type:
		return {
			"ok": false,
			"error": "%s 是%s，本角色只能用%s" % [
				base.name_cn, str(base.weapon_type), _weapon_type_name(db, weapon_type),
			],
			"slot_id": slot_id, "capacity": int(slot_row.max_equip),
		}
	return {"ok": true, "error": "", "slot_id": slot_id, "capacity": maxi(1, int(slot_row.max_equip))}


## 卸下某个槽位格子的装备。
## 返回 {ok, error, instance_id}
func unequip(char_id: String, slot_id: String, index: int) -> Dictionary:
	var slots: Dictionary = equipped.get(char_id, {})
	var entries: Array = slots.get(slot_id, [])
	if index < 0 or index >= entries.size() or str(entries[index]).is_empty():
		return {"ok": false, "error": "这一格没有装备", "instance_id": ""}
	var instance_id := str(entries[index])
	_clear_slot(char_id, slot_id, index)
	return {"ok": true, "error": "", "instance_id": instance_id}


func _clear_slot(char_id: String, slot_id: String, index: int) -> void:
	var slots: Dictionary = equipped.get(char_id, {})
	var entries: Array = slots.get(slot_id, [])
	if index >= 0 and index < entries.size():
		entries[index] = ""
	slots[slot_id] = entries
	equipped[char_id] = slots


# ------------------------------------------------------------------ 派生加成

## 产出 AttributeCalculator 的贡献列表：装备的 attr_* 走属性点层，bonus_* 走固定值层。
func contributions_for(db, char_id: String) -> Array:
	var out: Array = []
	# 一个 BuffService 用完这一次调用：它内部的 grants／stat_mods 缓存按调用生效，
	# 免得每件装备各建一个实例、各扫一遍 buff_grant（性能观测里量到过这条）
	var buff_service = BuffServiceScript.new(db)
	for instance_id: String in _equipped_instances(db, char_id):
		out.append_array(equipment_contributions(db, base_of(instance_id), instance_id, buff_service))
		# 随机词条走同一条贡献通道：属性点层与固定值层在 AffixRoller 里就分好了
		out.append_array(AffixRollerScript.contributions_of(db, affixes_of(instance_id)))
	return out


## **一件装备本体**的贡献：`equip_base` 的 `attr_*`（属性点层）与 `bonus_*`（固定值层）两列，
## 外加它自带的**常驻** buff（`buff_grant.trigger=on_equip` 且 `duration=0`；08 原文「装备即生效」，
## 所以面板与战斗必须读到同一个数，走同一条贡献通道——临时 buff 才由 `BattleActor` 现算）。
##
## 角色（背包里的实例）与**敌人**（`enemy_equip` 配表）共用这一处：设计 10 §五写着
## 「敌人属性 = 七维 + 等级 + 装备加成，与角色完全同一条管线」。
## `source` 只是贡献列表里的溯源标签（角色传实例 id、敌人传 `enemy_equip:<装备 id>`）。
static func equipment_contributions(db, equip_id: String, source: String = "", service = null) -> Array:
	var base: Resource = db.get_row("equip_base", equip_id)
	if base == null:
		return []
	var tag := source if not source.is_empty() else equip_id
	var buff_service = service if service != null else BuffServiceScript.new(db)
	var out: Array = []
	for attr_id: String in base.attr_bonuses():
		var value := int(base.attr_bonuses()[attr_id])
		if value != 0:
			out.append({
				"kind": AttributeCalculatorScript.CONTRIB_ATTR_POINT, "target": attr_id, "value": value, "source": tag,
			})
	for stat_id: String in base.stat_bonuses():
		var value := float(base.stat_bonuses()[stat_id])
		if value != 0.0:
			out.append({
				"kind": AttributeCalculatorScript.CONTRIB_STAT_FLAT, "target": stat_id, "value": value, "source": tag,
			})
	out.append_array(_equip_buff_contributions(buff_service, equip_id, tag))
	return out


## 某件装备本体发的常驻 buff 的贡献。
## `source` 写成 `buff:<buff_id>@<装备实例 id>`：既能追溯是哪条 buff、哪一件装备发的，
## 又和「装备自己的 bonus_* 列」（source = 实例 id）区分开——面板与用例都能分开看。
static func _equip_buff_contributions(service, base_id: String, instance_id: String) -> Array:
	var out: Array = []
	for grant: Resource in service.persistent_grants_for("equip", base_id):
		var buff_id := str(grant.buff_id)
		out.append_array(service.contributions_of(buff_id, "buff:%s@%s" % [buff_id, instance_id]))
	return out


## 某角色身上**当前配表认得的**已穿戴实例 id（不含空位，也不含配表装不下的那份记录）。
func _equipped_instances(db, char_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	var slots: Dictionary = equipped.get(char_id, {})
	var capacities := _slot_capacities(db)
	for slot_id: String in capacities:
		var entries: Array = slots.get(slot_id, [])
		for index in range(mini(int(capacities[slot_id]), entries.size())):
			var instance_id := str(entries[index])
			if not instance_id.is_empty() and equipment.has(instance_id):
				out.append(instance_id)
	return out


## 同上，给界面／战斗构造用（需要 db 才能知道槽位容量）。
func equipped_instances(db, char_id: String) -> PackedStringArray:
	return _equipped_instances(db, char_id)


func _weapon_type_name(db, weapon_type: String) -> String:
	var row: Resource = db.get_row("weapon_type_def", weapon_type)
	return str(row.name_cn) if row != null else weapon_type


# ------------------------------------------------------------------ 序列化

func to_dict() -> Dictionary:
	return {
		"stacks": stacks.duplicate(),
		"equipment": equipment.duplicate(true),
		"equipped": equipped.duplicate(true),
		"money": money,
		"next_uid": next_uid,
	}


static func from_dict(data: Variant) -> Inventory:
	var inventory := Inventory.new()
	if not (data is Dictionary):
		return inventory
	var dictionary: Dictionary = data
	if dictionary.get("stacks") is Dictionary:
		for item_id: Variant in Dictionary(dictionary["stacks"]):
			inventory.stacks[str(item_id)] = int(dictionary["stacks"][item_id])
	if dictionary.get("equipment") is Dictionary:
		for instance_id: Variant in Dictionary(dictionary["equipment"]):
			var entry: Variant = dictionary["equipment"][instance_id]
			if entry is Dictionary:
				var affixes: Variant = Dictionary(entry).get("affixes", [])
				inventory.equipment[str(instance_id)] = {
					"base_id": str(Dictionary(entry).get("base_id", "")),
					"affixes": Array(affixes).duplicate(true) if affixes is Array else [],
				}
	if dictionary.get("equipped") is Dictionary:
		for char_id: Variant in Dictionary(dictionary["equipped"]):
			var slots_data: Variant = dictionary["equipped"][char_id]
			if not (slots_data is Dictionary):
				continue
			var slots: Dictionary = {}
			for slot_id: Variant in Dictionary(slots_data):
				var entries: Variant = Dictionary(slots_data)[slot_id]
				if entries is Array:
					var normalized: Array = []
					for entry: Variant in Array(entries):
						normalized.append(str(entry))
					slots[str(slot_id)] = normalized
			inventory.equipped[str(char_id)] = slots
	inventory.money = int(dictionary.get("money", 0))
	inventory.next_uid = maxi(1, int(dictionary.get("next_uid", inventory.equipment.size() + 1)))
	return inventory
