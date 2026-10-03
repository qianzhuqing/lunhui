## growth_const.csv 行：成长系统常数（槽位上限、熟练度、图鉴奖励）。
##
## 算法在代码里（src/core/growth_calculator.gd），常数在这里——与伤害管线同一套处理方式。
extends "res://src/data/table_row.gd"

@export var const_id: String = ""
@export var name_cn: String = ""
@export var value: float = 0.0
@export var desc: String = ""
