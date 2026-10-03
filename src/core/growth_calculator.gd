## 成长系统的计算：槽位上限、熟练度、图鉴奖励。
##
## 与伤害管线同一套处理方式：**算法在代码、常数在表**（growth_const.csv），
## 星级相关的系数在 skill_star_def.csv。
##
## 口径说明：槽位上限里的悟性／根骨取**含装备的属性合计**（设计只说「悟性／根骨」，
## 没有像事件判定那样注明「裸属性」）；若设计要改成裸值，只改 ActiveSlots/PassiveCapacity 的入参即可。
class_name GrowthCalculator
extends RefCounted

## 贡献 kind 字符串的**唯一定义处**（`AttributeCalculator`）——这里不再写字面量，写错会被静默跳过。
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

## growth_const 缺行时的兜底值（正常运行时这些值都在表里）。
const DEFAULTS := {
	"active_slot_base": 2.0,
	"active_slot_lv_div": 5.0,
	"active_slot_attr_div": 8.0,
	"active_slot_cap": 9.0,
	"passive_cap_base": 2.0,
	"passive_cap_lv_div": 6.0,
	"passive_cap_attr_div": 10.0,
	"passive_cap_max": 7.0,
	"mastery_max": 10.0,
	"mastery_combat_gain": 1.0,
	"cultivate_cost_growth": 1.6,
	"codex_step": 5.0,
	"codex_bonus": 1.0,
	"codex_cap": 10.0,
}

## 图鉴奖励覆盖七项属性
const ALL_ATTRS := ["str", "con", "agi", "int", "luk", "wu", "gen"]

var _db
var _consts: Dictionary = {}


func _init(db) -> void:
	_db = db
	_consts.clear()
	for row: Resource in _db.rows("growth_const"):
		_consts[str(row.const_id)] = float(row.value)


## 取成长常数；表里缺行就报错并退回默认值。
func constant(const_id: String) -> float:
	if _consts.has(const_id):
		return float(_consts[const_id])
	push_error("[GrowthCalculator] growth_const 缺少 %s" % const_id)
	return float(DEFAULTS.get(const_id, 0.0))


func constant_int(const_id: String) -> int:
	return int(round(constant(const_id)))


## 招式槽上限 = base + floor(等级 / lv_div) + floor(悟性 / attr_div)，硬上限 cap。
func active_slots(level: int, attrs: Dictionary) -> int:
	var base := constant("active_slot_base")
	var by_level := floori(float(maxi(level, 1)) / maxf(constant("active_slot_lv_div"), 1.0))
	var by_attr := floori(float(attrs.get("wu", 0.0)) / maxf(constant("active_slot_attr_div"), 1.0))
	return clampi(int(base + by_level + by_attr), 0, constant_int("active_slot_cap"))


## 内功容量 = base + floor(等级 / lv_div) + floor(根骨 / attr_div)，硬上限 cap（容量点）。
func passive_capacity(level: int, attrs: Dictionary) -> int:
	var base := constant("passive_cap_base")
	var by_level := floori(float(maxi(level, 1)) / maxf(constant("passive_cap_lv_div"), 1.0))
	var by_attr := floori(float(attrs.get("gen", 0.0)) / maxf(constant("passive_cap_attr_div"), 1.0))
	return clampi(int(base + by_level + by_attr), 0, constant_int("passive_cap_max"))


## 两个公式型派生数值（不来自 attr_to_stat，单独算好合并进面板）。
func derived_stats(level: int, attrs: Dictionary) -> Dictionary:
	return {
		"slot_active": slot_active_stat(level, attrs),
		"passive_capacity": passive_capacity(level, attrs),
	}


## 招式槽的数值形态（int 派生数值用同一套显示逻辑）。
func slot_active_stat(level: int, attrs: Dictionary) -> int:
	return active_slots(level, attrs)


func star_row(star: int) -> Resource:
	var row: Resource = _db.get_row("skill_star_def", star)
	if row == null:
		push_error("[GrowthCalculator] skill_star_def 缺少 %d 星" % star)
	return row


func mastery_max() -> int:
	return constant_int("mastery_max")


## 招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)。
func mastery_multiplier(star: int, mastery_level: int) -> float:
	var row := star_row(star)
	if row == null:
		return 1.0
	var level := clampi(mastery_level, 0, mastery_max())
	return 1.0 + float(row.mastery_gain) * float(level)


## 打坐费用 = cultivate_cost_base × cultivate_cost_growth^(熟练度 - 1)。
func cultivate_cost(star: int, mastery_level: int) -> int:
	var row := star_row(star)
	if row == null:
		return 0
	var level := clampi(mastery_level, 1, maxi(1, mastery_max()))
	return int(round(float(row.cultivate_cost_base) * pow(constant("cultivate_cost_growth"), float(level - 1))))


## 图鉴奖励：每收集 step 部武学，七项属性各 +bonus，最多 cap 次。
## 返回 {times, bonus, attrs: {attr_id: 加成}}
func codex_bonus(collected: int) -> Dictionary:
	var step := maxi(1, constant_int("codex_step"))
	var cap := maxi(0, constant_int("codex_cap"))
	var bonus := constant_int("codex_bonus")
	var times := clampi(int(floor(float(maxi(collected, 0)) / float(step))), 0, cap)
	var attrs: Dictionary = {}
	for attr_id: String in ALL_ATTRS:
		attrs[attr_id] = times * bonus
	# step／cap 一起返回：界面要拿它显示「已收集 X／下一步还差几部」
	return {"times": times, "bonus": times * bonus, "attrs": attrs, "step": step, "cap": cap}


## 图鉴奖励转成属性点层贡献（`AttributeCalculator` 的贡献格式）。
##
## **换算只写这一处**：面板（`CharacterSheet`）与门槛判定（`SkillLoadout.attrs()` 的兜底）
## 都调它，免得两边各算一套、迟早对不上——这一条踩过：门槛走的是裸 SkillLoadout，
## 图鉴奖励只加进了面板那份属性，于是「面板显示够门槛了、实际却学不会」。
func codex_contributions(collected: int) -> Array:
	var out: Array = []
	var attrs: Dictionary = codex_bonus(collected)["attrs"]
	for attr_id: String in attrs:
		var value := int(attrs[attr_id])
		if value != 0:
			out.append({"kind": AttributeCalculatorScript.CONTRIB_ATTR_POINT, "target": attr_id, "value": value, "source": "codex"})
	return out


## 内功加成转成 AttributeCalculator 的贡献列表（装配系统接入后直接可用）。
func passive_contributions(passive_ids: PackedStringArray) -> Array:
	var out: Array = []
	for skill_id: String in passive_ids:
		for row: Resource in _db.rows_where("skill_passive_stat", "skill_id", skill_id):
			var parsed: Dictionary = row.parsed_target()
			var kind: String = parsed["kind"]
			if kind == "attr":
				out.append({
					"kind": AttributeCalculatorScript.CONTRIB_ATTR_POINT, "target": parsed["target_id"],
					"value": float(row.value), "source": skill_id,
				})
			elif kind == "stat":
				out.append({
					"kind": AttributeCalculatorScript.CONTRIB_STAT_FLAT, "target": parsed["target_id"],
					"value": float(row.value), "source": skill_id,
				})
	return out


## 装得下吗（内功占格制）：返回 {used, capacity, fits}
func passive_fit(passive_ids: PackedStringArray, level: int, attrs: Dictionary) -> Dictionary:
	var used := 0
	for skill_id: String in passive_ids:
		var row: Resource = _db.get_row("skill_passive", skill_id)
		if row != null:
			used += int(row.slot_cost)
	var capacity := passive_capacity(level, attrs)
	return {"used": used, "capacity": capacity, "fits": used <= capacity}
