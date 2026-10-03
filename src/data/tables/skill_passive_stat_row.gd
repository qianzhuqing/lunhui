## skill_passive_stat.csv 行：内功提供的属性／派生数值加成。
##
## target 带前缀：`attr:wu` 加属性点，`stat:qi_max` 加派生数值。
extends "res://src/data/table_row.gd"

@export var skill_id: String = ""
@export var target: String = ""
@export var value: float = 0.0
@export var note: String = ""


## 解析 target，返回 {"kind": "attr"|"stat"|"", "target_id": String}
func parsed_target() -> Dictionary:
	var separator := target.find(":")
	if separator < 0:
		return {"kind": "", "target_id": target}
	return {
		"kind": target.substr(0, separator),
		"target_id": target.substr(separator + 1),
	}
