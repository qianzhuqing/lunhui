## 熟练度成长（01_角色系统.md「熟练度（修炼）」）。
##
## 三种来源：**战斗中施放**（慢）、**打坐修炼**（客栈／门派，快，花铜钱）、**秘籍灌注**（一次性，稀有材料）。
## 本文件实现前两种，数值全在表里：
##   `growth_const.mastery_combat_gain` 每次施放 +1（表里写死的注释就是这个口径）
##   `growth_const.mastery_max` 上限 10
##   `skill_star_def.cultivate_cost_base` × `growth_const.cultivate_cost_growth`^(熟练度-1) 是打坐费用
##
## 口径说明：**一次打坐 = 熟练度 +1**，费用按「当前等级 → 下一级」算
## （设计写「把一部武学从 1 练到 10 是一笔真实开销」，与逐级递增的费用公式一致；
## 若设计要一次打坐加更多级，改 `train()` 里的 `amount` 一处即可）。
##
## 秘籍灌注没做：它要「稀有材料」的消耗规则，设计只写了「一次性」，没给材料与幅度。
class_name MasteryService
extends RefCounted

const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")

var db
var state
var _growth


func _init(table_db, game_state) -> void:
	db = table_db
	state = game_state
	_growth = GrowthCalculatorScript.new(db) if db != null else null


func mastery_max() -> int:
	return _growth.mastery_max() if _growth != null else 10


func mastery_of(char_id: String, skill_id: String) -> int:
	return state.mastery_of(char_id, skill_id) if state != null else 0


func star_of(skill_id: String) -> int:
	var row: Resource = db.get_row("skill_base", skill_id)
	return int(row.star) if row != null else 0


func skill_name(skill_id: String) -> String:
	var row: Resource = db.get_row("skill_base", skill_id)
	return str(row.name_cn) if row != null else skill_id


# ------------------------------------------------------------------ 打坐

## 打坐一次的费用：按「当前熟练度 → 下一级」查 star 表
func train_cost(char_id: String, skill_id: String) -> int:
	if _growth == null:
		return 0
	return _growth.cultivate_cost(star_of(skill_id), mastery_of(char_id, skill_id) + 1)


## 能不能打坐：{ok, error, cost, mastery, max}
func can_train(char_id: String, skill_id: String) -> Dictionary:
	var current := mastery_of(char_id, skill_id)
	var top := mastery_max()
	var cost := train_cost(char_id, skill_id)
	var out := {"ok": false, "error": "", "cost": cost, "mastery": current, "max": top}
	if state == null or state.inventory == null:
		out["error"] = "没有会话状态"
		return out
	if db.get_row("skill_base", skill_id) == null:
		out["error"] = "没有这部武学：%s" % skill_id
		return out
	if not state.is_learned(char_id, skill_id):
		out["error"] = "还没学会这部武学"
		return out
	if current >= top:
		out["error"] = "已经练满（%d/%d）" % [current, top]
		return out
	if int(state.inventory.money) < cost:
		out["error"] = "铜钱不够（需要 %d，现有 %d）" % [cost, int(state.inventory.money)]
		return out
	out["ok"] = true
	return out


## 打坐一次：扣铜钱、熟练度 +1。返回 {ok, error, cost, mastery, before}
func train(char_id: String, skill_id: String) -> Dictionary:
	var check := can_train(char_id, skill_id)
	if not check["ok"]:
		return {"ok": false, "error": str(check["error"]), "cost": 0, "before": int(check["mastery"]), "mastery": int(check["mastery"])}
	var before := int(check["mastery"])
	var cost := int(check["cost"])
	state.inventory.money -= cost
	var after: int = state.set_mastery(char_id, skill_id, before + 1, mastery_max())
	return {"ok": true, "error": "", "cost": cost, "before": before, "mastery": after}


# ------------------------------------------------------------------ 战斗施放

## 战斗后结算：usage 是「实际施放过的招式 → 次数」，每次施放 +mastery_combat_gain。
## 返回 [{skill_id, name, before, after, gained}]（没有涨的不列出来）
## `cap` > 0 时把这一次的成长卡在该值（练习战的 `growth_const.dummy_mastery_cap`）：
## **只压涨、不压低**——已经练到 5 级的招式打木桩不该掉回 3 级。
func apply_combat_usage(char_id: String, usage: Dictionary, cap: int = 0) -> Array:
	var out: Array = []
	if state == null or usage.is_empty():
		return out
	var per_cast := int(round(_growth.constant("mastery_combat_gain")))
	if per_cast <= 0:
		return out
	for skill_id: String in usage:
		var gained := _gain(char_id, skill_id, per_cast * maxi(0, int(usage[skill_id])), cap)
		if gained["gained"] > 0:
			out.append(gained)
	return out


func _gain(char_id: String, skill_id: String, amount: int, cap: int = 0) -> Dictionary:
	var before := mastery_of(char_id, skill_id)
	var limit := mastery_max()
	var target := before + amount
	if cap > 0:
		# 上限只压「这一次涨到哪」，不压低已有的（打木桩不该让练好的招式退步）
		limit = mini(cap, limit)
		target = maxi(before, mini(target, limit))
	var after: int = state.set_mastery(char_id, skill_id, target, mastery_max())
	return {
		"skill_id": skill_id,
		"name": skill_name(skill_id),
		"before": before,
		"after": after,
		"gained": after - before,
	}


## 结算面板上的汇总文案：「玄微剑法·起手 2/10（+2）」
func describe_usage(entries: Array) -> String:
	var parts := PackedStringArray()
	for entry: Dictionary in entries:
		parts.append("%s %d/%d（+%d）" % [
			str(entry["name"]), int(entry["after"]), mastery_max(), int(entry["gained"]),
		])
	return "、".join(parts)
