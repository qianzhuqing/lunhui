## damage_type.csv 行：伤害类型（结算方式、能否暴击/闪避、吃哪种防御）。
extends "res://src/data/table_row.gd"

@export var type_id: String = ""
@export var name_cn: String = ""
## instant 直伤 / dot 持续
@export var category: String = "instant"
@export var can_crit: bool = false
@export var can_dodge: bool = false
@export var use_def_phys: bool = false
@export var use_def_qi: bool = false
## immediate / turn_end
@export var tick_timing: String = "immediate"
@export var base_duration: int = 0
@export var max_stack: int = 0
@export var display_color: String = ""
@export var desc: String = ""


func is_dot() -> bool:
	return category == "dot"
