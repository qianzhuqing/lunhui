## equip_slot_def.csv 行：装备槽位定义。
##
## 槽位清单是这张表说了算：equip_base.slot 与 affix_pool.allow_slots 都引用它，
## 代码里不再写死槽位枚举（增删槽位不必改校验器）。
extends "res://src/data/table_row.gd"

@export var slot_id: String = ""
@export var name_cn: String = ""
## 该槽位能同时装备几件（戒指为 2）
@export var max_equip: int = 1
@export var sort_order: int = 0
## 允许的武器类型，"|" 分隔，引用 weapon_type_def；非武器槽留空
@export var allow_weapon_types: String = ""
@export var desc: String = ""


## 允许的武器类型列表。
func allowed_weapon_types() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in allow_weapon_types.split("|", false):
		out.append(part.strip_edges())
	return out


## 是否武器槽（以「有没有允许的武器类型」判定，避免再写死 slot_id）。
func is_weapon_slot() -> bool:
	return not allow_weapon_types.strip_edges().is_empty()
