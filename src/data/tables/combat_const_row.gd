## combat_const.csv 行：伤害管线依赖的全局常数。
##
## 只放参数，不放公式：公式仍在 DamageResolver 里实现。
extends "res://src/data/table_row.gd"

@export var const_id: String = ""
@export var name_cn: String = ""
@export var value: float = 0.0
@export var desc: String = ""
