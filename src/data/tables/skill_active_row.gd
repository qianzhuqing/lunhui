## skill_active.csv 行：招式明细（战斗数值）。
##
## 每条 skill_kind=active 的武学必须有且只有一行，技能本身在 skill_base。
extends "res://src/data/table_row.gd"

@export var skill_id: String = ""
## 系别，引用 element_counter 的 external / internal / odd
@export var element: String = ""
## 引用 damage_type.type_id
@export var damage_type: String = ""
@export var power_ratio: float = 0.0
@export var qi_cost: int = 0
@export var poise_damage: int = 0
@export var hit_count: int = 1
## single / self / all_enemy
@export var target_type: String = "single"
## 附带异常状态，引用 status_effect.status_id
@export var status_id: String = ""
@export var status_chance_mul: float = 0.0
@export var cooldown: int = 0


## 是不是要打伤害的招式（辅助招式倍率为 0）。
func is_attack() -> bool:
	return not damage_type.is_empty() and power_ratio > 0.0
