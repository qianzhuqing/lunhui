## 经验与升级（01_角色系统.md「等级与经验」 + `level_growth.csv`）。
##
## 表里 `exp_to_next` 是「升下一级所需经验」（不是累计值），`upgrade_points` 是这一级给的加点，
## 满级那一行 `exp_to_next=0`（当前 20 级封顶）。
##
## 口径：经验进**队伍池**（存档里的 `party_exp`），谁升级就从这个池子里扣一份。
## 多人时**先补等级最低的那个**（避免一直练同一个人），池子付不起就停。
## 这条「共享池」是开发侧定的（设计没写分配规则，原来只记账不消费），已记进对接表等设计确认。
class_name LevelService
extends RefCounted

const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")

var db
var state
var _growth


func _init(table_db, game_state) -> void:
	db = table_db
	state = game_state


## 升下一级要多少经验；满级（或表里没这级）返回 0
func exp_to_next(level: int) -> int:
	var row: Resource = db.get_row("level_growth", level)
	return maxi(0, int(row.exp_to_next)) if row != null else 0


func max_level() -> int:
	var top := 1
	for row: Resource in db.rows("level_growth"):
		top = maxi(top, int(row.level))
	return top


## 这一级给的加点（用于面板「升级 +N 点」的提示）
func upgrade_points(level: int) -> int:
	var row: Resource = db.get_row("level_growth", level)
	return maxi(0, int(row.upgrade_points)) if row != null else 0


## 把队伍经验池换成等级。返回 {changes: [{char_id, name, from, to, points}], spent, remaining, capped}
func apply_available_levels() -> Dictionary:
	var changes: Array = []
	var spent := 0
	if state == null:
		return {"changes": changes, "spent": spent, "remaining": 0, "capped": false}
	var guard := 0
	var limit := max_level() * maxi(1, state.char_ids.size()) + 1
	while guard < limit:
		guard += 1
		# 挑「等级最低、且池子付得起」的那个人升一级
		var chosen := ""
		var chosen_level := 0
		var chosen_cost := 0
		for char_id: String in state.char_ids:
			var level: int = state.level_of(char_id)
			var cost := exp_to_next(level)
			if cost <= 0 or int(state.party_exp) < cost:
				continue
			if chosen.is_empty() or level < chosen_level:
				chosen = char_id
				chosen_level = level
				chosen_cost = cost
		if chosen.is_empty():
			break
		state.party_exp -= chosen_cost
		spent += chosen_cost
		state.append_level(chosen)
		changes.append({
			"char_id": chosen,
			"name": state.char_name(db, chosen),
			"from": chosen_level,
			"to": chosen_level + 1,
			"points": upgrade_points(chosen_level),
		})
	var capped := true
	for char_id: String in state.char_ids:
		if exp_to_next(state.level_of(char_id)) > 0:
			capped = false
			break
	return {"changes": changes, "spent": spent, "remaining": int(state.party_exp), "capped": capped}


## 结算面板／面板用的一行文案：「家道失落的书生 Lv1 → Lv2（+3 点）」
func describe(changes: Array) -> String:
	var parts := PackedStringArray()
	for entry: Dictionary in changes:
		parts.append("%s Lv%d → Lv%d（+%d 点）" % [
			str(entry["name"]), int(entry["from"]), int(entry["to"]), int(entry["points"]),
		])
	return "、".join(parts)


## 面板用：某个角色离下一级还差多少经验
func exp_progress(char_id: String) -> Dictionary:
	var level: int = state.level_of(char_id) if state != null else 1
	var need := exp_to_next(level)
	return {
		"level": level,
		"max_level": max_level(),
		"need": need,
		"pool": int(state.party_exp) if state != null else 0,
		"maxed": need <= 0,
	}
