## status_effect.csv 行：异常状态（层数、持续、特殊规则）。
##
## 触发率统一归运，强度统一归智（trigger_stat / power_stat），见 docs/design/04_战斗与伤害.md。
extends "res://src/data/table_row.gd"

@export var status_id: String = ""
@export var name_cn: String = ""
## 关联伤害类型，引用 damage_type.type_id
@export var damage_type: String = ""
@export var trigger_stat: String = ""
@export var power_stat: String = ""
@export var base_chance: float = 0.0
@export var max_stack: int = 1
@export var duration: int = 0
@export var dispellable: bool = false
@export var extra_rule: String = ""
@export var icon: String = ""
@export var desc: String = ""
