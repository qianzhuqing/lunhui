## affix_pool.csv 行：装备随机词条池。
##
## target 带前缀："attr:" 加属性点，"stat:" 加派生数值；
## value_kind：attr_point / flat / rate。
extends "res://src/data/table_row.gd"

@export var affix_id: String = ""
@export var name_cn: String = ""
@export var target: String = ""
@export var value_kind: String = "flat"
@export var value_min: float = 0.0
@export var value_max: float = 0.0
## 允许的槽位，用 "|" 分隔
@export var allow_slots: String = ""
## 引用 rarity_def.rarity_id
@export var min_rarity: String = "common"
@export var weight: int = 0
@export var desc: String = ""


## 解析 target 前缀，返回 {"kind": "attr"|"stat", "target_id": "..."}。
func parsed_target() -> Dictionary:
	var separator := target.find(":")
	if separator < 0:
		return {"kind": "", "target_id": target}
	return {
		"kind": target.substr(0, separator),
		"target_id": target.substr(separator + 1),
	}


## 允许槽位列表。
func slots() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in allow_slots.split("|", false):
		out.append(part.strip_edges())
	return out
