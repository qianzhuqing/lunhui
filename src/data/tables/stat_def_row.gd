## stat_def.csv 行：派生数值定义。
##
## max_value 为空时按 0 存储，约定 max_value > 0 才视为有上限（0 表示无上限）。
extends "res://src/data/table_row.gd"

## int / float
@export var stat_id: String = ""
@export var name_cn: String = ""
## int / float
@export var value_kind: String = "int"
@export var is_percent: bool = false
@export var min_value: float = 0.0
@export var max_value: float = 0.0
@export var show_decimals: int = 0
@export var desc: String = ""


## 是否有上限（表里留空即无上限，存储为 0）。
func has_max() -> bool:
	return max_value > 0.0
