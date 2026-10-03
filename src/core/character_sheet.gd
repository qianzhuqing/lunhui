## 角色面板的数据集合。
##
## 把「模板 + 加点 + 装备」合成面板要显示的一切：五维、派生数值、非战斗技能与判定值、
## 装备槽位视图、招式列表、未分配点数。界面只读它，不自己算数。
##
## 判定值口径按 docs/design/01_角色系统.md：**只用裸属性（模板 + 加点，不含装备）**，
## `attr:x` 取裸值，`skill:y` = 技能等级 + floor(相关裸属性 / 5)。
class_name CharacterSheet
extends RefCounted

const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
## 套装档位与装备特效都是 buff（08）：贡献的换算只写在 BuffService／SetService 里
const SetServiceScript := preload("res://src/core/set_service.gd")
const BuffServiceScript := preload("res://src/core/buff_service.gd")

## 非战斗技能的判定缩放：floor(裸属性 / 5)
const EVENT_SKILL_ATTR_DIVISOR := 5

## 面板的战斗属性分组（覆盖 stat_def 全部 22 项）。
const STAT_GROUPS := {
	"攻击": ["atk_phys", "atk_qi", "pen_rate", "crit_rate", "crit_dmg", "poise_break", "debuff_power", "debuff_chance"],
	"防御": ["def_phys", "def_qi", "hp_max", "block_rate", "block_reduction", "dmg_reduction", "dodge_rate", "res_internal"],
	"资源与行动": ["qi_max", "hp_regen", "qi_regen", "speed", "hit_rate", "drop_rate"],
	"成长": ["slot_active", "passive_capacity", "mastery_gain"],
}

var db
var state
var char_id: String = ""
var template: Resource = null
var level: int = 1

var _calculator
var _growth
var _loadout


func _init(table_db, game_state, member_id: String) -> void:
	db = table_db
	state = game_state
	char_id = member_id
	template = db.get_row("character_base", char_id)
	level = state.level_of(char_id) if state != null else 1
	_calculator = AttributeCalculatorScript.new(db)
	_growth = GrowthCalculatorScript.new(db)
	_loadout = SkillLoadoutScript.new(db, state, char_id)
	# 门槛与槽位判定复用面板这份属性，两边不会各算一套
	_loadout.attrs_provider = Callable(self, "loadout_attrs")


## 武学装配（学 + 装）。界面与战斗都从这里走，别在别处重算槽位。
func loadout():
	return _loadout


func slot_summary() -> Dictionary:
	return _loadout.slot_summary() if _loadout != null else {
		"active_used": 0, "active_slots": 0, "passive_used": 0, "passive_capacity": 0,
	}


func valid() -> bool:
	return template != null


func display_name() -> String:
	return str(template.name_cn) if template != null else char_id


func role_tag() -> String:
	return str(template.role_tag) if template != null else ""


func weapon_type_id() -> String:
	return str(template.weapon_type) if template != null else ""


func weapon_type_name() -> String:
	var row: Resource = db.get_row("weapon_type_def", weapon_type_id())
	return str(row.name_cn) if row != null else weapon_type_id()


func template_desc() -> String:
	return str(template.desc) if template != null else ""


# ------------------------------------------------------------------ 属性

func base_attrs() -> Dictionary:
	return template.initial_attrs() if template != null else {}


func allocations() -> Dictionary:
	return state.allocations_of(char_id) if state != null else {}


## 图鉴奖励（设计 01「收集本身也是成长」）：每收集 `codex_step` 部武学，七项属性各 +`codex_bonus`，
## 最多 `codex_cap` 次。常数在 `growth_const`、公式在 `GrowthCalculator` —— 这里只负责**接进属性合成**。
##
## 口径（设计没写细节，按「与加点同类的永久成长」处理）：
##   · 进战斗属性（`contributions()` → `stats()`）
##   · 进判定值（`naked_attrs()` 也算它——判定只排除「装备」，收集成长不是装备）
##   · 进门槛与槽位（`loadout_attrs()` 也算它——上限只有 +10，不像内功那样自我循环）
func codex_bonus() -> Dictionary:
	return _growth.codex_bonus(collected_skill_count())


func collected_skill_count() -> int:
	return state.collected_skill_count() if state != null else 0


## 图鉴奖励转成属性点层贡献；换算在 `GrowthCalculator.codex_contributions()` 里只写一份
func codex_contributions() -> Array:
	return _growth.codex_contributions(collected_skill_count())


## 裸属性：模板 + 加点 + 图鉴奖励，**不含装备**（事件判定与面板上的「裸值」都用它）。
func naked_attrs() -> Dictionary:
	return _calculator.attr_totals_of(base_attrs(), allocations(), codex_contributions())


## 含装备属性点的五维合计（面板五维显示用）。
func total_attrs() -> Dictionary:
	return _calculator.attr_totals_of(base_attrs(), allocations(), contributions())


## 武学判定用的属性：模板 + 加点 + 装备，**不含已装内功**。
## 内功自己给悟性／根骨的话，「装内功 → 槽位涨 → 又能装一部」会变成顺序游戏（决策 22），
## 所以门槛与槽位都按这份算；内功加成只进派生数值。
func loadout_attrs() -> Dictionary:
	var equipment_only: Array = []
	if state != null and state.inventory != null:
		equipment_only = state.inventory.contributions_for(db, char_id)
	equipment_only.append_array(codex_contributions())
	return _calculator.attr_totals_of(base_attrs(), allocations(), equipment_only)


func contributions() -> Array:
	if state == null or state.inventory == null:
		return _loadout.contributions() if _loadout != null else []
	var out: Array = state.inventory.contributions_for(db, char_id)
	# 内功「装备即生效」：装配的内功加成走同一条贡献通道，属性点层与固定值层已分开
	out.append_array(_loadout.contributions())
	# 套装档位（08）：只算已装备／已装配的成员，档位向下兼容
	out.append_array(set_contributions())
	# 图鉴奖励也走同一条通道（属性点层）
	out.append_array(codex_contributions())
	return out


## 生效的套装档位 buff 转成贡献。
##
## **故意不进 `loadout_attrs()`**：套装按「装配了几招／几格」计数，如果它的加成又回头影响
## 招式槽与内功容量，就变成「装上去→属性涨→又能装」的顺序游戏（与内功同一条纪律，见决策 22）。
func set_contributions() -> Array:
	if state == null or state.inventory == null:
		return []
	var sets = SetServiceScript.new(db)
	var buffs_service = BuffServiceScript.new(db)
	var out: Array = []
	for row: Resource in sets.all_defs():
		var set_id := str(row.set_id)
		var count := sets.count_for(set_id, char_id, state.inventory, state)
		if count <= 0:
			continue
		for buff_id: String in sets.active_buff_ids(set_id, count):
			out.append_array(buffs_service.contributions_of(buff_id, "set:%s" % set_id))
	return out


## 全部派生数值：四层公式 + 装备加成。
## slot_active / passive_capacity 不来自 attr_to_stat，由 growth_const 公式算出来合并进来。
func stats() -> Dictionary:
	var computed: Dictionary = _calculator.compute(level, base_attrs(), allocations(), contributions())
	# 槽位那两个数必须与装配判定的口径一致，否则面板会显示一个装不下的容量
	computed.merge(_growth.derived_stats(level, loadout_attrs()))
	return computed


## 招式槽上限（个数）与内功容量（容量点）
func active_slots() -> int:
	return _growth.active_slots(level, total_attrs())


## 气血上限（走同一份派生数值，别另算一套）
func max_hp() -> int:
	return maxi(1, int(stats().get("hp_max", 0.0)))


## 当前气血（战斗外气血 v11 起进存档；没有记录 = 满血）
func current_hp() -> int:
	if state == null:
		return max_hp()
	var saved: int = state.current_hp_of(char_id)
	if saved < 0:
		return max_hp()
	return clampi(saved, 1, max_hp())


func passive_capacity() -> int:
	return _growth.passive_capacity(level, total_attrs())


## 武学熟练度（0~mastery_max，存在存档里）
func mastery_of(skill_id: String) -> int:
	if state == null:
		return 0
	return state.mastery_of(char_id, skill_id)


func stat_value(stat_id: String) -> float:
	return float(stats().get(stat_id, 0.0))


## 显示用文本：百分比类乘 100 加 %，其余按 show_decimals 取整。
func stat_label(stat_id: String) -> String:
	var row: Resource = db.get_row("stat_def", stat_id)
	var value := stat_value(stat_id)
	if row == null:
		return str(value)
	if bool(row.is_percent):
		return "%.1f%%" % (value * 100.0)
	if int(row.show_decimals) > 0:
		return "%.1f" % value
	return str(int(value))


func stat_name(stat_id: String) -> String:
	var row: Resource = db.get_row("stat_def", stat_id)
	return str(row.name_cn) if row != null else stat_id


func attribute_name(attr_id: String) -> String:
	var row: Resource = db.get_row("attribute_def", attr_id)
	return str(row.name_cn) if row != null else attr_id


# ------------------------------------------------------------------ 非战斗技能与判定

func event_skill_level(skill_id: String) -> int:
	for row: Resource in db.rows("character_base_skill"):
		if str(row.char_id) == char_id and str(row.skill_id) == skill_id:
			return int(row.level)
	return 0


## 面板上的非战斗技能行：技能名、等级、关联属性、判定值。
func event_skill_rows() -> Array:
	var out: Array = []
	for row: Resource in db.rows("event_skill_def"):
		var skill_id := str(row.skill_id)
		var related := str(row.related_attr)
		var check := event_check_value("skill:%s" % skill_id)
		out.append({
			"skill_id": skill_id,
			"name": str(row.name_cn),
			"level": event_skill_level(skill_id),
			"max_level": int(row.max_level),
			"related_attr": related,
			"related_attr_name": attribute_name(related),
			"check_value": int(check["value"]),
		})
	return out


## 判定值。返回 {kind, target_id, value, naked}
func event_check_value(source: String) -> Dictionary:
	var parts := source.split(":", false)
	var kind := parts[0] if parts.size() > 0 else ""
	var target_id := parts[1] if parts.size() > 1 else ""
	var naked := naked_attrs()
	match kind:
		"attr":
			return {"kind": kind, "target_id": target_id, "value": int(floor(float(naked.get(target_id, 0.0)))), "naked": true}
		"skill":
			var skill: Resource = db.get_row("event_skill_def", target_id)
			var related := str(skill.related_attr) if skill != null else ""
			var bonus := int(floor(float(naked.get(related, 0.0)) / float(EVENT_SKILL_ATTR_DIVISOR)))
			return {
				"kind": kind, "target_id": target_id,
				"value": event_skill_level(target_id) + bonus, "naked": true,
			}
	return {"kind": kind, "target_id": target_id, "value": 0, "naked": true}


# ------------------------------------------------------------------ 装备与招式

## 槽位视图：slot_id → Array（长度 = max_equip），每格是
## {slot_id, index, empty, instance_id, base, name, rarity_color, summary, level_req}
func equipped_rows() -> Array:
	var out: Array = []
	if state == null or state.inventory == null:
		return out
	var slots: Dictionary = state.inventory.equipment_slots(db, char_id)
	for slot: Resource in db.rows("equip_slot_def"):
		var slot_id := str(slot.slot_id)
		var entries: Array = slots.get(slot_id, [""])
		for index in range(entries.size()):
			var instance_id := str(entries[index])
			var base: Resource = null
			if not instance_id.is_empty():
				base = db.get_row("equip_base", state.inventory.base_of(instance_id))
			out.append({
				"slot_id": slot_id,
				"slot_name": str(slot.name_cn),
				"index": index,
				"empty": instance_id.is_empty() or base == null,
				"instance_id": instance_id,
				"base": base,
				"name": str(base.name_cn) if base != null else "空",
				"rarity_color": rarity_color(str(base.rarity)) if base != null else "",
				"summary": equipment_summary(base),
				"affix_summary": affix_summary_of(instance_id),
				"level_req": int(base.level_req) if base != null else 0,
			})
	return out


## 这件装备的随机词条文案（空串 = 没有词条）
func affix_summary_of(instance_id: String) -> String:
	if state == null or state.inventory == null or instance_id.is_empty():
		return ""
	return AffixRollerScript.summarize(db, state.inventory.affixes_of(instance_id))


## 装备的关键属性摘要（前几条，供槽位与背包列表显示）。
func equipment_summary(base: Resource) -> String:
	if base == null:
		return ""
	var parts := PackedStringArray()
	for attr_id: String in base.attr_bonuses():
		var value := int(base.attr_bonuses()[attr_id])
		if value != 0:
			parts.append("%s+%d" % [attribute_name(attr_id), value])
	for stat_id: String in base.stat_bonuses():
		var value := float(base.stat_bonuses()[stat_id])
		if value == 0.0:
			continue
		var row: Resource = db.get_row("stat_def", stat_id)
		if row != null and bool(row.is_percent):
			parts.append("%s+%.1f%%" % [str(row.name_cn), value * 100.0])
		else:
			parts.append("%s+%d" % [str(row.name_cn) if row != null else stat_id, int(value)])
	if parts.is_empty():
		return "无附加属性"
	return "、".join(parts)


func rarity_color(rarity_id: String) -> String:
	var row: Resource = db.get_row("rarity_def", rarity_id)
	return str(row.color) if row != null else "#FFFFFF"


func rarity_name(rarity_id: String) -> String:
	var row: Resource = db.get_row("rarity_def", rarity_id)
	return str(row.name_cn) if row != null else rarity_id


func weapon_type_name_of(base: Resource) -> String:
	if base == null or str(base.weapon_type).is_empty():
		return ""
	var row: Resource = db.get_row("weapon_type_def", str(base.weapon_type))
	return str(row.name_cn) if row != null else str(base.weapon_type)


## 武学列表：**已学**的武学（模板起始 + 存档记录）。
## 每行带上星级、熟练度、熟练度倍率、是否已装配、能不能装（不能装时写清原因）；
## 招式附战斗数值，内功附占格与加成摘要。界面的装配按钮就用这里的 can_equip／equip_error。
func skill_rows() -> Array:
	var out: Array = []
	if template == null:
		return out
	var seen: Dictionary = {}
	var ids := PackedStringArray()
	for skill_id: String in _loadout.learned_ids():
		if not seen.has(skill_id):
			seen[skill_id] = true
			ids.append(skill_id)
	if state != null:
		for skill_id: String in state.masteries_of(char_id):
			if not seen.has(skill_id):
				seen[skill_id] = true
				ids.append(skill_id)
	for skill_id: String in ids:
		var row: Resource = db.get_row("skill_base", skill_id)
		if row == null:
			continue
		var star_row = _growth.star_row(int(row.star))
		var entry := {
			"skill_id": skill_id,
			"name": str(row.name_cn),
			"kind": str(row.skill_kind),
			"star": int(row.star),
			"star_name": str(star_row.name_cn) if star_row != null else "",
			"star_color": str(star_row.color) if star_row != null else "#FFFFFF",
			"weapon_type": str(row.weapon_type),
			"school_id": str(row.school_id),
			"mastery": mastery_of(skill_id),
			"mastery_max": _growth.mastery_max(),
			"mastery_multiplier": _growth.mastery_multiplier(int(row.star), mastery_of(skill_id)),
			"learn_req_attr": str(row.learn_req_attr),
			"learn_req_value": int(row.learn_req_value),
			"learn_req_reason": str(_loadout.requirement_of(skill_id)["reason"]),
			"equipped": _loadout.active_ids().has(skill_id) or _loadout.passive_ids().has(skill_id),
			"equip_error": "" if _loadout.active_ids().has(skill_id) or _loadout.passive_ids().has(skill_id)
				else str(_loadout.can_equip(skill_id)["error"]),
			"desc": str(row.desc),
		}
		if row.is_active():
			var active: Resource = db.get_row("skill_active", skill_id)
			if active != null:
				entry["element"] = str(active.element)
				entry["damage_type"] = str(active.damage_type)
				entry["power_ratio"] = float(active.power_ratio)
				entry["qi_cost"] = int(active.qi_cost)
				entry["poise_damage"] = int(active.poise_damage)
				entry["hit_count"] = int(active.hit_count)
				entry["target_type"] = str(active.target_type)
				# 「这招在战斗里能不能用出来」：伤害型看 `is_attack()`（有伤害类型且倍率 > 0）；
				# 增益招式（例：醉里乾坤·醉步）：没有伤害、但表里配了 `on_cast` 发放 → 战斗里能用，
				# 施放一次给自己上 buff。判定与战斗侧同一处（`BuffService.has_cast_grant`），别各写一套。
				var support: bool = (
					not active.is_attack()
					and BuffServiceScript.new(db).has_cast_grant(str(skill_id))
				)
				entry["battle_usable"] = active.is_attack() or support
				if active.is_attack():
					entry["battle_note"] = ""
				elif support:
					entry["battle_note"] = "增益招式：施放时给自己上 buff"
				else:
					entry["battle_note"] = "战斗里用不出来（效果未配）"
		else:
			var passive: Resource = db.get_row("skill_passive", skill_id)
			if passive != null:
				entry["slot_cost"] = int(passive.slot_cost)
				entry["passive_effect"] = str(passive.passive_effect)
				entry["bonus_summary"] = passive_bonus_summary(skill_id)
				# 表里填了「特殊效果 id」但**没有定义表、代码里也没有落点**（4 部高星内功都是这样）：
				# 面板要如实说一句，别让玩家以为「装了这部就该有个特别效果，怎么没反应」。
				# 注意**不要把 id 原样显示**（界面不许出现表内英文 id，有文案审计盯着），只说「暂未生效」。
				entry["special_effect_pending"] = not str(passive.passive_effect).strip_edges().is_empty()
		out.append(entry)
	return out


## 内功加成摘要，如「内力上限+30、智+5」
func passive_bonus_summary(skill_id: String) -> String:
	var parts := PackedStringArray()
	for row: Resource in db.rows_where("skill_passive_stat", "skill_id", skill_id):
		var parsed: Dictionary = row.parsed_target()
		var kind: String = parsed["kind"]
		var target_id: String = parsed["target_id"]
		if kind == "attr":
			parts.append("%s+%d" % [attribute_name(target_id), int(row.value)])
		elif kind == "stat":
			parts.append("%s+%s" % [stat_name(target_id), _format_plain_stat(target_id, float(row.value))])
	if parts.is_empty():
		return "无加成"
	return "、".join(parts)


func _format_plain_stat(stat_id: String, value: float) -> String:
	var row: Resource = db.get_row("stat_def", stat_id)
	if row != null and bool(row.is_percent):
		return "%.1f%%" % (value * 100.0)
	if row != null and int(row.show_decimals) > 0:
		return "%.1f" % value
	return str(int(value))


# ------------------------------------------------------------------ 加点

func available_points() -> int:
	return state.available_points(db, char_id) if state != null else 0


## 加一点并返回 {ok, error, remaining}
func spend_point(attr_id: String) -> Dictionary:
	if state == null:
		return {"ok": false, "error": "没有会话状态", "remaining": 0}
	return state.spend_point(db, char_id, attr_id)


## 这个属性能不能加点；能加返回空串，不能加返回原因（设计 0.13.0：悟性／根骨是资质）。
func allocation_block_reason(attr_id: String) -> String:
	if state == null:
		return "没有会话状态"
	return state.allocation_block_reason(db, attr_id)
