## attr_to_stat.csv 行：属性 → 派生数值映射（核心表）。
##
## cap 为空时按 0 存储，约定 cap > 0 才视为有上限。
## diminishing 曲线只吃 cap 与 param（见 docs/design/01_角色系统.md 与 curve_def.csv），rate 仅作说明。
extends "res://src/data/table_row.gd"

## CSV 列名为 id，与基类字段重名，故改名为 row_id。
@export var row_id: int = 0
@export var attr_id: String = ""
@export var stat_id: String = ""
@export var rate: float = 0.0
## linear / diminishing / piecewise
@export var curve: String = "linear"
@export var cap: float = 0.0
@export var param: float = 0.0
@export var note: String = ""


func has_cap() -> bool:
	return cap > 0.0
