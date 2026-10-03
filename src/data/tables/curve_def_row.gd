## curve_def.csv 行：成长曲线枚举与公式说明（公式本身在 CurveEvaluator 里实现）。
extends "res://src/data/table_row.gd"

## linear / diminishing / piecewise
@export var curve_id: String = ""
@export var formula: String = ""
@export var desc: String = ""
