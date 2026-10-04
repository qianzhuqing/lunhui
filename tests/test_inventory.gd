## 背包与装备：堆叠、实例、穿戴规则、贡献列表、序列化。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const InventoryScript := preload("res://src/core/inventory.gd")


func suite_name() -> String:
	return "背包与装备"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	# 抬到 10 级：让等级需求不挡后面几组用例（等级限制本身单独验）
	state.char_levels[str(state.char_ids[0])] = 10
	var inventory = state.inventory
	_check_starting_equipment(db, state, inventory)
	_check_stacks(db, inventory)
	_check_equip_rules(db, state, inventory)
	_check_slot_capacity(db, state, inventory)
	_check_contributions(db, state)
	_check_slot_table_change(db)
	_check_per_character_isolation(db)
	_check_round_trip(db, state)
	_check_equip_invariants(db, state)


func _check_starting_equipment(db, state, inventory) -> void:
	var char_id := str(state.char_ids[0])
	var equipped: PackedStringArray = inventory.equipped_instances(db, char_id)
	check_gt(float(equipped.size()), 0.0, "新建游戏会按模板穿上初始装备")
	var owner: Dictionary = inventory.equipped_by(equipped[0])
	check_eq(str(owner.get("slot_id", "")), "weapon", "初始装备穿在武器槽")
	# 0.10.2：`start_equip_ids` 由单件改成两件（武器 ＋ `eq_armor_01` 布衣）——
	# 原来只给武器，等于新同伴入队时光着上身（设计 09「入队自带武器与上衣」）
	check_eq(equipped.size(), 2, "新建游戏穿上**两件**初始装备（武器＋上衣）")
	if equipped.size() > 1:
		var armor_owner: Dictionary = inventory.equipped_by(equipped[1])
		check_eq(str(armor_owner.get("slot_id", "")), "body", "第二件穿在身上（布衣）")
	var slots: Dictionary = inventory.equipment_slots(db, char_id)
	check_eq(slots.size(), 7, "槽位视图覆盖 7 类槽位")
	check_eq((slots["ring"] as Array).size(), 2, "戒指槽有两个格子")
	# 初始装备现在占掉 weapon 与 body 两格，所以「空槽」改用下装（7 类槽位里没有「头」——
	# 第一版我写成 `slots["head"]`，运行期直接报 `Invalid access to property or key 'head'`，
	# 被引擎日志门限抓住；槽位清单以 `equip_slot_def` 为准：武器／身体／腰带／肩部／下装／戒指／项链）
	check_eq(str(slots["legs"][0]), "", "没装备的槽位是空串")


func _check_stacks(db, inventory) -> void:
	var added: Dictionary = inventory.add_item(db, "item_iron", 5)
	check_true(added["ok"], "放入材料成功")
	check_eq(inventory.count("item_iron"), 5, "数量累加")

	# stack_max = 99：再加 100 只进 94，其余在 overflow 里报出来
	var overflow: Dictionary = inventory.add_item(db, "item_iron", 100)
	check_eq(int(overflow["added"]), 94, "超出 stack_max 的部分被截断")
	check_eq(int(overflow["overflow"]), 6, "截断数量报在 overflow")
	check_eq(inventory.count("item_iron"), 99, "堆叠上限生效")

	var removed: Dictionary = inventory.remove_item(db, "item_iron", 9)
	check_true(removed["ok"], "丢弃材料成功")
	check_eq(inventory.count("item_iron"), 90, "丢弃后数量正确")

	# 钥匙道具不能丢
	inventory.add_item(db, "item_wine_gourd", 1)
	var refused: Dictionary = inventory.remove_item(db, "item_wine_gourd", 1)
	check_false(refused["ok"], "钥匙道具不能丢弃")
	check_true(str(refused["error"]).contains("钥匙"), "拒绝原因说明是钥匙道具")
	check_eq(inventory.count("item_wine_gourd"), 1, "被拒后数量没变")

	# 装备不能用 add_item 塞
	var wrong: Dictionary = inventory.add_item(db, "eq_sword_01", 1)
	check_false(wrong["ok"], "装备要走 add_equipment")


func _check_equip_rules(db, state, inventory) -> void:
	var char_id := str(state.char_ids[0])
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)

	# 武器类型不匹配（书生用剑，铁拳套是拳）
	var fist: String = inventory.add_equipment(db, "eq_fist_01")
	var fist_check: Dictionary = inventory.can_equip(db, char_id, level, weapon_type, fist)
	check_false(fist_check["ok"], "拳套装不到用剑的角色身上")
	check_true(str(fist_check["error"]).contains("拳"), "拒绝原因说明武器类型")

	# 等级不足（锈月要 10 级）
	var legendary: String = inventory.add_equipment(db, "eq_sword_04")
	var level_check: Dictionary = inventory.can_equip(db, char_id, 1, weapon_type, legendary)
	check_false(level_check["ok"], "等级不足的装备不能穿")
	check_true(str(level_check["error"]).contains("级"), "拒绝原因说明等级需求")
	check_true(bool(inventory.can_equip(db, char_id, 10, weapon_type, legendary)["ok"]), "等级够了就能穿")

	# 卸下 → 外功下降；装回 → 恢复
	var equipped: PackedStringArray = inventory.equipped_instances(db, char_id)
	var sword := str(equipped[0])
	var atk_before: float = _atk_of(db, state)
	var removed: Dictionary = inventory.unequip(char_id, "weapon", 0)
	check_true(removed["ok"], "卸下武器成功")
	check_eq(removed["instance_id"], sword, "卸下的正是那把武器")
	var atk_without: float = _atk_of(db, state)
	check_lt(atk_without, atk_before, "卸下武器后外功下降")
	var again: Dictionary = inventory.equip(db, char_id, level, weapon_type, sword)
	check_true(again["ok"], "重新装上武器成功")
	check_float(_atk_of(db, state), atk_before, "装回后外功恢复")

	# 已装备的实例不能直接删
	var delete_equipped: Dictionary = inventory.remove_equipment(sword)
	check_false(delete_equipped["ok"], "已装备的实例不能直接删掉")
	var delete_idle: Dictionary = inventory.remove_equipment(fist)
	check_true(delete_idle["ok"], "没装备的实例可以删掉")


func _check_slot_capacity(db, state, inventory) -> void:
	var char_id := str(state.char_ids[0])
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)
	var ring_a: String = inventory.add_equipment(db, "eq_ring_01")
	var ring_b: String = inventory.add_equipment(db, "eq_ring_02")
	var ring_c: String = inventory.add_equipment(db, "eq_ring_03")
	check_true(bool(inventory.equip(db, char_id, level, weapon_type, ring_a)["ok"]), "第一枚戒指装上")
	check_true(bool(inventory.equip(db, char_id, level, weapon_type, ring_b)["ok"]), "第二枚戒指装上")
	var slots: Dictionary = inventory.equipment_slots(db, char_id)
	check_eq(str(slots["ring"][0]), ring_a, "第一格是戒指 A")
	check_eq(str(slots["ring"][1]), ring_b, "第二格是戒指 B")
	var third: Dictionary = inventory.equip(db, char_id, level, weapon_type, ring_c)
	check_true(third["ok"], "第三枚戒指会替换第一格")
	check_eq(str(third["replaced"]), ring_a, "被替换的是第一格的戒指")
	check_eq(str(inventory.equipment_slots(db, char_id)["ring"][0]), ring_c, "第一格换成戒指 C")


func _check_contributions(db, state) -> void:
	var char_id := str(state.char_ids[0])
	var level: int = state.level_of(char_id)
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)
	# 青锋剑：敏 +2（属性点层）＋ 外功 +14（固定值层）
	var sword: String = state.inventory.add_equipment(db, "eq_sword_02")
	state.inventory.equip(db, char_id, level, weapon_type, sword)
	var contributions: Array = state.inventory.contributions_for(db, char_id)
	var has_attr := false
	var has_flat := false
	for entry: Dictionary in contributions:
		if entry["kind"] == "attr_point" and entry["target"] == "agi" and int(entry["value"]) == 2:
			has_attr = true
		if entry["kind"] == "stat_flat" and entry["target"] == "atk_phys" and float(entry["value"]) == 14.0:
			has_flat = true
	check_true(has_attr, "装备的属性点加成走 attr_point 层")
	check_true(has_flat, "装备的派生数值加成走 stat_flat 层")


## 设计改了槽位表（删掉一类槽位／把 max_equip 改小）之后，存档里多出来的那些穿戴记录怎么办。
##
## 以前它们**界面看不见、却照常加属性**（探针实测：戒指容量改成 1 时，两枚都还在贡献列表里；
## 把 ring 整类删掉后它们连卸都卸不下来）。槽位是配表驱动的（增删槽位不必改代码），
## 所以这条必须守住：**配表认不出的记录不算加成，读档时退回背包**（实例本体不删）。
func _check_slot_table_change(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	state.char_levels[char_id] = 10
	var weapon_type := str(db.get_row("character_base", char_id).weapon_type)
	var inventory = state.inventory
	var ring_a: String = inventory.add_equipment(db, "eq_ring_01")
	var ring_b: String = inventory.add_equipment(db, "eq_ring_02")
	check_true(bool(inventory.equip(db, char_id, 10, weapon_type, ring_a)["ok"]), "旧配表下第 1 枚戒指装上")
	check_true(bool(inventory.equip(db, char_id, 10, weapon_type, ring_b)["ok"]), "旧配表下第 2 枚戒指装上")
	check_eq(inventory.equipped_instances(db, char_id).size(), 4, "旧配表下身上是武器＋上衣＋两枚戒指")

	# ① 容量改小：戒指从 2 格改成 1 格
	var narrow = _slot_table_with(db, {"ring": 1}, [])
	check_eq(Array(inventory.equipment_slots(narrow, char_id)["ring"]).size(), 1, "新配表只显示 1 个戒指格")
	check_eq(inventory.equipped_instances(narrow, char_id).size(), 3, "新配表下只认武器＋上衣＋第 1 枚戒指")
	check_eq(_sources_of(inventory.contributions_for(narrow, char_id), [ring_b]), 0, "装不下的那枚不再加属性")
	var reclaimed: PackedStringArray = inventory.reclaim_stranded_equipped(narrow, char_id)
	check_eq(reclaimed.size(), 1, "装不下的那枚被退回背包")
	check_eq(str(reclaimed[0]), ring_b, "退回的正是第 2 枚（保留界面显示的第 1 格）")
	check_false(inventory.is_equipped(ring_b), "退回后它不再算穿着")
	check_true(inventory.has_equipment(ring_b), "实例本体留在背包里（没被删掉）")
	check_eq(inventory.equipped_instances(narrow, char_id).size(), 3, "退回后身上是武器＋上衣＋第 1 枚戒指")

	# ② 整类槽位被删（例：设计把 ring 从 equip_slot_def 里拿掉）
	var cut = _slot_table_with(db, {}, ["ring"])
	check_false(inventory.equipment_slots(cut, char_id).has("ring"), "新配表里没有戒指槽了")
	check_eq(inventory.equipped_instances(cut, char_id).size(), 2, "删槽位后身上只剩武器＋上衣")
	check_eq(_sources_of(inventory.contributions_for(cut, char_id), [ring_a, ring_b]), 0, "删槽位后戒指不再加属性")
	var cut_reclaimed: PackedStringArray = inventory.reclaim_stranded_equipped(cut, char_id)
	check_eq(cut_reclaimed.size(), 1, "还在身上的那枚也被退回")
	check_false(inventory.is_equipped(ring_a), "退回后戒指不算穿着")
	check_true(inventory.has_equipment(ring_a), "戒指实例还在背包里")

	# ③ 真走一次读档：`GameState.from_dict` 会按当前配表把装不下的记录退回背包
	var fresh = solo_state(db)
	fresh.char_levels[char_id] = 10
	var r1: String = fresh.inventory.add_equipment(db, "eq_ring_01")
	var r2: String = fresh.inventory.add_equipment(db, "eq_ring_02")
	fresh.inventory.equip(db, char_id, 10, weapon_type, r1)
	fresh.inventory.equip(db, char_id, 10, weapon_type, r2)
	var back = GameStateScript.from_dict(fresh.to_dict(), narrow)
	check_not_null(back, "换了槽位表的存档仍能读入")
	if back != null:
		check_eq(back.inventory.equipped_instances(narrow, char_id).size(), 3, "读档后只认武器＋上衣＋第 1 枚戒指")
		check_false(back.inventory.is_equipped(r2), "读档时第 2 枚被退回背包（不再偷偷加属性）")
		check_true(back.inventory.has_equipment(r2), "退回的实例还在背包里")
		check_eq(back.reclaimed_equipped, 1, "读档记下按配表退回了 1 件（启动菜单据此写回存档）")

	# ④ 实例序号不许复用（含存档往返）：`next_uid` 必须随存档走，否则删掉一件再加会拿到同一个序号，
	# `equipment[id] = …` 会**静默覆盖**背包里已有的那件（写错不报错的那种）。
	var temp: String = inventory.add_equipment(db, "eq_ring_01")
	check_true(bool(inventory.remove_equipment(temp)["ok"]), "临时那件装备能删掉")
	var reloaded = InventoryScript.from_dict(inventory.to_dict())
	var existing: PackedStringArray = reloaded.equipment_ids()
	var again: String = reloaded.add_equipment(db, "eq_ring_01")
	check_ne(again, temp, "读档后再新增不会复用刚删掉的序号")
	check_false(existing.has(again), "新实例 id 不和现有任何一件撞号（撞上就是静默覆盖）")
	check_false(reloaded.has_equipment(temp), "旧实例确实不在了")
	check_true(reloaded.has_equipment(again), "新实例在背包里")


## 装备与加点是**按人**的：给 A 换武器／加点，B 的属性与身上装备一点都不能动。
## 跨人串号（把一个人的加成算到另一个人头上）是极难发现的错——数值只是「有点不对」，
## 没有断言就只能靠玩家觉得「怎么这么疼」。多人队伍（缺口 #1 的数据）到位后尤其要守。
func _check_per_character_isolation(db) -> void:
	var wide = table_with_n_chars(4)
	var state = party_state(wide, 4)
	var ids := PackedStringArray(state.char_ids)
	check_eq(ids.size(), 4, "造了 4 人队伍")
	var a := ids[0]
	var b := ids[1]
	var weapon_type := str(wide.get_row("character_base", a).weapon_type)
	var atk_a_before := _atk_of_char(wide, state, a)
	var atk_b_before := _atk_of_char(wide, state, b)
	var b_equipped_before := str(state.inventory.equipped_instances(wide, b))
	var b_weapon_before := str(state.inventory.equipment_slots(wide, b)["weapon"][0])

	state.append_level(a, 4)          # 青锋剑要 5 级
	var sword: String = state.inventory.add_equipment(wide, "eq_sword_02")
	var equipped: Dictionary = state.inventory.equip(wide, a, state.level_of(a), weapon_type, sword)
	check_true(bool(equipped["ok"]), "A 换上青锋剑：%s" % equipped["error"])
	check_gt(_atk_of_char(wide, state, a), atk_a_before, "A 的外功涨了")
	check_eq(_atk_of_char(wide, state, b), atk_b_before, "B 的外功一点没动")
	check_eq(str(state.inventory.equipped_instances(wide, b)), b_equipped_before, "B 身上的装备没被换过")
	check_eq(
		str(state.inventory.equipment_slots(wide, b)["weapon"][0]), b_weapon_before,
		"B 的武器槽还是原来那件（A 换的是自己的）",
	)

	state.spend_point(wide, a, "str")
	check_eq(int(state.allocations_of(a).get("str", 0)), 1, "A 的加点记在 A 头上")
	check_eq(int(state.allocations_of(b).get("str", 0)), 0, "B 的加点还是 0")
	check_eq(_atk_of_char(wide, state, b), atk_b_before, "给 A 加点之后 B 的外功仍不变")


## 复制一份表库并按参数改 equip_slot_def：`overrides` 改容量、`remove` 删整类槽位。
## **只改内存副本，不碰策划的 CSV。**
func _slot_table_with(db, overrides: Dictionary, remove: Array):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["equip_slot_def"].duplicate(true)
	var kept: Array = []
	for row: Resource in table.rows:
		var slot_id := str(row.slot_id)
		if remove.has(slot_id):
			continue
		if overrides.has(slot_id):
			row.max_equip = int(overrides[slot_id])
		kept.append(row)
	table.rows = kept
	custom.tables["equip_slot_def"] = table
	return custom


## 贡献列表里来自这几件实例的条数（用来验「装不下的那件不再加属性」）。
func _sources_of(contributions: Array, instance_ids: Array) -> int:
	var total := 0
	for entry: Dictionary in contributions:
		if instance_ids.has(str(entry.get("source", ""))):
			total += 1
	return total


func _check_round_trip(db, state) -> void:
	state.inventory.money = 777
	state.inventory.add_item(db, "item_herb", 3)
	var restored = InventoryScript.from_dict(state.inventory.to_dict())
	check_eq(restored.money, 777, "铜钱读回一致")
	check_eq(restored.count("item_herb"), 3, "堆叠读回一致")
	check_eq(restored.equipment_count(), state.inventory.equipment_count(), "装备数量读回一致")
	check_eq(
		str(restored.equipment_slots(db, str(state.char_ids[0]))),
		str(state.inventory.equipment_slots(db, str(state.char_ids[0]))),
		"穿戴映射读回一致"
	)
	# 读档后再捡一件装备**不能撞上已有的实例 id**：`next_uid` 忘了存，新装备会覆盖旧的那件
	# （玩家看到"捡到了"，背包里却少了一件）——doc 注释一直写着「序号存在存档里，读档后不会重复」，
	# 但在这一条之前**没有任何断言钉着它**。
	var pending: PackedStringArray = state.inventory.equipment_ids()
	var fresh_id: String = restored.add_equipment(db, "eq_sword_01")
	check_false(fresh_id.is_empty(), "读档后还能造装备实例")
	check_false(pending.has(fresh_id), "新实例 id 不与存档里已有的撞车：%s" % fresh_id)
	check_eq(
		restored.equipment_count(), state.inventory.equipment_count() + 1,
		"新实例是**进背包**（+1），不是覆盖旧的那件",
	)


## 穿戴的不变量：确定性小模糊测试（**不用随机数**——步数取模就够了，
## 免得踩「用例里的随机必须固定种子」那条门限，也省得失败信息不可复现）。
##
## 为什么值得单独有一条：**被拒绝的操作玩家看不见**。`equip()`／`unequip()` 拒绝时
## （槽满／等级不够／武器类型不符／空格）如果已经写了一半——把旧的那件腾了、或把新实例塞进
## `equipment` 却不进槽——玩家看到的是"没穿上"，而背包已经少了一件或挂着一个悬空 id。
## 这类**静默丢装备**现有用例一条都不查（它们查的是"成功路径算得对不对"）。
##
## 每一步都查五件事：① 槽是**定长**的（长度 == `equip_slot_def.max_equip`）；
## ② 没有**悬空 id**（槽里的实例必须还在背包里）；③ 同一个实例**不会同时穿在两处**；
## ④ 每格用量不超过容量；⑤ `next_uid` 必须大于所有已存在的序号（否则下一件就会撞车）。
## 另外：**被拒绝的那一次不许改动任何东西**（拿 `to_dict()` 前后对拍）。
func _check_equip_invariants(db, state) -> void:
	var char_id := str(state.char_ids[0])
	var inventory = state.inventory
	var capacities := {}
	for row: Resource in db.rows("equip_slot_def"):
		capacities[str(row.slot_id)] = int(row.max_equip)
	check_gt(float(capacities.size()), 3.0, "读到足够多的槽位定义（%d 个）" % capacities.size())
	var slot_ids := PackedStringArray()
	for slot_id: String in capacities.keys():
		slot_ids.append(slot_id)
	var pool := PackedStringArray()
	for row: Resource in db.rows("equip_base"):
		pool.append(str(row.equip_id))
	check_gt(float(pool.size()), 5.0, "读到足够多的装备模板（%d 件）" % pool.size())

	var problems := PackedStringArray()
	var added := 0
	var equipped_ok := 0
	var unequipped_ok := 0
	var refused := 0
	for step in range(1, 161):
		match step % 4:
			0:
				var base_id: String = pool[(step * 7) % pool.size()]
				if not inventory.add_equipment(db, base_id).is_empty():
					added += 1
			1:
				var ids: PackedStringArray = inventory.equipment_ids()
				if ids.is_empty():
					continue
				var wanted: String = ids[(step * 5) % ids.size()]
				var before := str(inventory.to_dict())
				var result: Dictionary = inventory.equip(db, char_id, 20, "sword", wanted)
				if bool(result.get("ok", false)):
					equipped_ok += 1
				else:
					refused += 1
					if str(inventory.to_dict()) != before:
						problems.append("第 %d 步：穿戴被拒（%s）却改动了背包" % [step, str(result.get("error", ""))])
			2:
				var slot_id: String = slot_ids[(step * 3) % slot_ids.size()]
				var view: Dictionary = inventory.equipment_slots(db, char_id)
				var entries: Array = view.get(slot_id, [])
				var before_unequip := str(inventory.to_dict())
				var result2: Dictionary = inventory.unequip(char_id, slot_id, (step * 11) % maxi(1, entries.size()))
				if bool(result2.get("ok", false)):
					unequipped_ok += 1
				else:
					refused += 1
					if str(inventory.to_dict()) != before_unequip:
						problems.append("第 %d 步：卸下被拒（%s）却改动了背包" % [step, str(result2.get("error", ""))])
			_:
				var ids2: PackedStringArray = inventory.equipment_ids()
				if ids2.is_empty():
					continue
				# 穿在身上的会被拒（"先卸下装备再处理"）——这条拒绝也必须不改动任何东西
				var victim: String = ids2[(step * 13) % ids2.size()]
				var before_remove := str(inventory.to_dict())
				var result3: Dictionary = inventory.remove_equipment(victim)
				if not bool(result3.get("ok", false)):
					refused += 1
					if str(inventory.to_dict()) != before_remove:
						problems.append("第 %d 步：丢弃被拒却改动了背包" % step)
		problems.append_array(_equip_state_problems(db, inventory, char_id, capacities, step))

	check_gt(float(added), 0.0, "模糊测试真的造过装备（%d 件）" % added)
	check_gt(float(equipped_ok), 0.0, "真的穿上过（%d 次）" % equipped_ok)
	check_gt(float(unequipped_ok), 0.0, "真的卸下过（%d 次）" % unequipped_ok)
	check_gt(float(refused), 0.0, "也碰到过被拒绝的操作（%d 次）——那条不变量才算真验过" % refused)
	check_eq(problems.size(), 0, "160 步里没有一步破坏穿戴的不变量：%s" % "；".join(problems))


## 当前背包／穿戴的五条不变量，返回所有违规描述（空 = 全过）。
func _equip_state_problems(db, inventory, char_id: String, capacities: Dictionary, step: int) -> PackedStringArray:
	var problems := PackedStringArray()
	var view: Dictionary = inventory.equipment_slots(db, char_id)
	var seen := {}
	for slot_id: String in view:
		var entries: Array = view[slot_id]
		var capacity := int(capacities.get(slot_id, 1))
		if entries.size() != capacity:
			problems.append("第 %d 步：%s 槽不是定长（%d ≠ %d）" % [step, slot_id, entries.size(), capacity])
		var used := 0
		for entry: Variant in entries:
			var instance_id := str(entry)
			if instance_id.is_empty():
				continue
			used += 1
			if not inventory.has_equipment(instance_id):
				problems.append("第 %d 步：%s 槽挂着不存在的实例 %s（悬空 id）" % [step, slot_id, instance_id])
			if seen.has(instance_id):
				problems.append("第 %d 步：实例 %s 同时穿在两处" % [step, instance_id])
			seen[instance_id] = true
		if used > capacity:
			problems.append("第 %d 步：%s 槽超容量（%d > %d）" % [step, slot_id, used, capacity])
	# `next_uid` 必须大于所有已存在的序号，否则下一件装备就会撞上旧的那件
	var max_uid := 0
	var prefix := "%s#" % ""
	for instance_id: String in inventory.equipment_ids():
		var cut := instance_id.rfind("#")
		if cut < 0:
			continue
		max_uid = maxi(max_uid, int(instance_id.substr(cut + 1)))
		prefix = instance_id.substr(0, cut + 1)
	if max_uid >= int(inventory.next_uid):
		problems.append("第 %d 步：next_uid=%d 不大于已有的最大序号 %d（下一件会撞车，%s）"
			% [step, int(inventory.next_uid), max_uid, prefix])
	return problems


func _atk_of(db, state) -> float:
	return _atk_of_char(db, state, str(state.char_ids[0]))


func _atk_of_char(db, state, char_id: String) -> float:
	var sheet = load("res://src/core/character_sheet.gd").new(db, state, char_id)
	return sheet.stat_value("atk_phys")
