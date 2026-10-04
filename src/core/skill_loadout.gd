## 武学装配：学（修习门槛）＋ 装（招式槽／内功容量）。
##
## 规则按 docs/design/01_角色系统.md 落地，数值全在表里：
##   招式槽上限 = 2 + floor(等级/5) + floor(悟性/8)   内功容量 = 2 + floor(等级/6) + floor(根骨/10)
##   内功占格制：每部按星级占 1~3 格（`skill_passive.slot_cost`）
##   修习门槛：`skill_base.learn_req_attr` 达到 `learn_req_value`（行里没写值就取 `skill_star_def`）
##   招式绑定武器类型（`any`／留空 = 通用）：装的招式得配得上身上的武器
##
## 口径：门槛与槽位用**含装备**的属性（与 growth_calculator 一致），但**不含已装内功的加成**——
## 否则「装内功 → 属性涨 → 容量涨 → 又能装一部」会自我循环，装哪部内功就变成了顺序游戏。
class_name SkillLoadout
extends RefCounted

const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
## 天赋的 `rule:` 效果（图鉴翻倍那条）——门槛这一侧要与面板用同一个倍数
const TalentServiceScript := preload("res://src/core/talent_service.gd")

var db
var state
var char_id: String = ""
## 可选：面板已经把属性算好了就接进来，保证「面板显示的槽位」与这里判定的槽位是同一份数
var attrs_provider: Callable = Callable()

var _calculator
var _growth
var _template: Resource = null


func _init(table_db, game_state, member_id: String) -> void:
	db = table_db
	state = game_state
	char_id = member_id
	_template = db.get_row("character_base", char_id) if db != null else null
	if db != null:
		_calculator = AttributeCalculatorScript.new(db)
		_growth = GrowthCalculatorScript.new(db)


func valid() -> bool:
	return db != null and state != null and _template != null


func level() -> int:
	return state.level_of(char_id) if state != null else 1


## 判定用的属性合计：模板 + 加点 + 装备（理由见文件头）
func attrs() -> Dictionary:
	if attrs_provider.is_valid():
		return attrs_provider.call()
	var base: Dictionary = _template.initial_attrs() if _template != null else {}
	var allocations: Dictionary = state.allocations_of(char_id) if state != null else {}
	var contributions: Array = []
	if state != null and state.inventory != null:
		contributions = state.inventory.contributions_for(db, char_id)
	# 图鉴奖励（收集本身也是成长）也要算进门槛与槽位：和面板那份属性同一套换算，
	# 不然会出现「面板显示够门槛、实际学不会」（这条踩过）。
	if state != null and _growth != null:
		# 天赋「藏书癖」把每一档翻倍——**门槛这一侧也要用同一个倍数**，
		# 否则会出现「面板算上了翻倍、门槛没算」，正是这条注释上面写过的那个坑
		var mult := TalentServiceScript.rule_value(db, state, char_id, "codex_bonus_multiplier", 0.0)
		contributions.append_array(_growth.codex_contributions(
			state.collected_skill_count(), mult if mult > 0.0 else 1.0
		))
	return _calculator.attr_totals_of(base, allocations, contributions)


# ------------------------------------------------------------------ 已学

func learned_ids() -> PackedStringArray:
	return state.learned_of(char_id) if state != null else PackedStringArray()


func is_learned(skill_id: String) -> bool:
	return state != null and state.is_learned(char_id, skill_id)


## 修习门槛：{ok, attr, value, current, reason}
func requirement_of(skill_id: String) -> Dictionary:
	var row: Resource = db.get_row("skill_base", skill_id)
	if row == null:
		return {"ok": false, "attr": "", "value": 0, "current": 0, "reason": "没有这部武学：%s" % skill_id}
	if not row.has_learn_requirement():
		return {"ok": true, "attr": "", "value": 0, "current": 0, "reason": ""}
	var attr_id := str(row.learn_req_attr)
	var value := int(row.learn_req_value)
	if value <= 0:
		var star: Resource = _growth.star_row(int(row.star))
		value = int(star.learn_req_value) if star != null else 0
	# 天赋「悟剑」（设计 12：高星武学的**修习门槛 −3**）：从要求值上减，最低到 0——
	# 不减到负数（负数等于没有门槛，那是另一套语义；门槛本身还是"够属性才学得会"）
	if value > 0 and state != null:
		value = maxi(0, value - int(TalentServiceScript.rule_value(
			db, state, char_id, "learn_req_reduce", 0.0
		)))
	var current := int(attrs().get(attr_id, 0))
	var ok := current >= value
	return {
		"ok": ok,
		"attr": attr_id,
		"value": value,
		"current": current,
		"reason": "" if ok else "%s需 %d（当前 %d）" % [attribute_name(attr_id), value, current],
	}


func attribute_name(attr_id: String) -> String:
	var row: Resource = db.get_row("attribute_def", attr_id)
	return str(row.name_cn) if row != null else attr_id


## 学一部武学。已经学过不算失败（战斗掉落会反复遇到同一个来源），返回 {ok, new, error}
func learn(skill_id: String) -> Dictionary:
	if db.get_row("skill_base", skill_id) == null:
		return {"ok": false, "new": false, "error": "没有这部武学：%s" % skill_id}
	if is_learned(skill_id):
		return {"ok": true, "new": false, "error": ""}
	var requirement := requirement_of(skill_id)
	if not requirement["ok"]:
		return {"ok": false, "new": false, "error": "修习门槛不足：%s" % requirement["reason"]}
	state.learn_skill(char_id, skill_id)
	return {"ok": true, "new": true, "error": ""}


# ------------------------------------------------------------------ 槽位

func active_ids() -> PackedStringArray:
	return state.loadout_of(char_id).get("active", PackedStringArray())


func passive_ids() -> PackedStringArray:
	return state.loadout_of(char_id).get("passive", PackedStringArray())


func active_slots() -> int:
	return _growth.active_slots(level(), attrs())


func passive_capacity() -> int:
	return _growth.passive_capacity(level(), attrs())


func active_used() -> int:
	return active_ids().size()


## 内功占格合计（不是部数）：容量点口径见 01 文档
func passive_used() -> int:
	var used := 0
	for skill_id: String in passive_ids():
		var row: Resource = db.get_row("skill_passive", skill_id)
		if row != null:
			used += int(row.slot_cost)
	return used


## 面板顶部那行「招式 2/3　内功 1/2 格」
func slot_summary() -> Dictionary:
	return {
		"active_used": active_used(),
		"active_slots": active_slots(),
		"passive_used": passive_used(),
		"passive_capacity": passive_capacity(),
	}


func equipped_weapon_type() -> String:
	if state == null or state.inventory == null:
		return ""
	for instance_id: String in state.inventory.equipped_instances(db, char_id):
		var base: Resource = db.get_row("equip_base", state.inventory.base_of(instance_id))
		if base != null and str(base.slot) == "weapon":
			return str(base.weapon_type)
	return ""


func weapon_type_name(weapon_type: String) -> String:
	var row: Resource = db.get_row("weapon_type_def", weapon_type)
	return str(row.name_cn) if row != null else weapon_type


# ------------------------------------------------------------------ 装配

## 能不能装：{ok, error}
func can_equip(skill_id: String) -> Dictionary:
	var row: Resource = db.get_row("skill_base", skill_id)
	if row == null:
		return {"ok": false, "error": "没有这部武学：%s" % skill_id}
	if not is_learned(skill_id):
		return {"ok": false, "error": "还没学会（来源：%s）" % source_label(row)}
	if active_ids().has(skill_id) or passive_ids().has(skill_id):
		return {"ok": false, "error": "已经装配"}
	if row.is_active():
		if not accepts_current_weapon(row):
			var have := equipped_weapon_type()
			return {
				"ok": false,
				"error": "武器不符（需要%s，%s）" % [
					weapon_type_name(str(row.weapon_type)),
					"当前没拿武器" if have.is_empty() else "当前拿的是%s" % weapon_type_name(have),
				],
			}
		if active_used() >= active_slots():
			return {"ok": false, "error": "招式槽已满（%d/%d）" % [active_used(), active_slots()]}
	else:
		var after := passive_ids()
		after.append(skill_id)
		var fit: Dictionary = _growth.passive_fit(after, level(), attrs())
		if not fit["fits"]:
			return {
				"ok": false,
				"error": "内功容量不足（这部占 %d 格，只剩 %d 格）" % [
					passive_slot_cost(skill_id), maxi(0, passive_capacity() - passive_used()),
				],
			}
	return {"ok": true, "error": ""}


func accepts_current_weapon(row: Resource) -> bool:
	return row.accepts_any_weapon() or str(row.weapon_type) == equipped_weapon_type()


func passive_slot_cost(skill_id: String) -> int:
	var row: Resource = db.get_row("skill_passive", skill_id)
	return int(row.slot_cost) if row != null else 0


func source_label(row: Resource) -> String:
	var kind := str(row.source_type)
	var names := {
		"start": "开局自带", "npc": "门派／NPC传授", "drop": "击败%s领悟" % str(row.source_id),
		"story": "剧情", "hidden": "隐藏内容", "item": "秘籍", "shop": "商店",
	}
	return str(names.get(kind, kind if not kind.is_empty() else "未知"))


## 装上（超过容量会被拒）。返回 {ok, error, replaced}
func equip(skill_id: String) -> Dictionary:
	var gate := can_equip(skill_id)
	if not gate["ok"]:
		return {"ok": false, "error": gate["error"], "replaced": ""}
	var row: Resource = db.get_row("skill_base", skill_id)
	var active := active_ids()
	var passive := passive_ids()
	if row.is_active():
		active.append(skill_id)
	else:
		passive.append(skill_id)
	state.set_loadout(char_id, active, passive)
	return {"ok": true, "error": "", "replaced": ""}


func unequip(skill_id: String) -> Dictionary:
	var active := active_ids()
	var passive := passive_ids()
	var kind := ""
	if active.has(skill_id):
		active.remove_at(active.find(skill_id))
		kind = "active"
	elif passive.has(skill_id):
		passive.remove_at(passive.find(skill_id))
		kind = "passive"
	if kind.is_empty():
		return {"ok": false, "error": "这部武学没有装配"}
	state.set_loadout(char_id, active, passive)
	return {"ok": true, "error": "", "kind": kind}


## 切换：装着的就卸下，没装的就装上（面板一个按钮搞定）
func toggle(skill_id: String) -> Dictionary:
	if active_ids().has(skill_id) or passive_ids().has(skill_id):
		var off := unequip(skill_id)
		return {"ok": off["ok"], "error": off["error"], "equipped": false}
	var on := equip(skill_id)
	return {"ok": on["ok"], "error": on["error"], "equipped": on["ok"]}


## 按顺序把学过的都装上，能装多少装多少（新建游戏与老档迁移铺初始装配）
func fill_from_learned() -> void:
	if not valid():
		return
	var active := PackedStringArray()
	var passive := PackedStringArray()
	for skill_id: String in learned_ids():
		var row: Resource = db.get_row("skill_base", skill_id)
		if row == null:
			continue
		if row.is_active():
			if active.size() < active_slots():
				active.append(skill_id)
		else:
			var after := passive.duplicate()
			after.append(skill_id)
			if bool(_growth.passive_fit(after, level(), attrs())["fits"]):
				passive.append(skill_id)
	state.set_loadout(char_id, active, passive)


# ------------------------------------------------------------------ 战斗

## 战斗里能用的招式 = 装配的招式（空着就打不出来，回合会被跳过：见 battle_simulator 的「无可用招式，跳过」）
func battle_skill_ids() -> PackedStringArray:
	return active_ids()


## 内功装备即生效：加成转成 AttributeCalculator 的贡献列表。
##
## 0.15.0 起把**本队员对每部内功的熟练度**一起传进去——`stat:` 类加成按熟练度放大，
## `attr:` 类不放大（口径见 `GrowthCalculator.passive_contributions`）。
func contributions() -> Array:
	if _growth == null:
		return []
	var levels: Dictionary = {}
	for skill_id: String in passive_ids():
		levels[skill_id] = state.mastery_of(char_id, skill_id) if state != null else 0
	return _growth.passive_contributions(passive_ids(), levels)
