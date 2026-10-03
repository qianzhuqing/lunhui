## feature_toggle.csv 行：**全局开关**（构建期决定的功能开关）。
##
## 只放"要不要开某个功能"这种**一行一个开关**的东西；`value` 的取值语义写在 `desc` 里。
extends "res://src/data/table_row.gd"

@export var toggle_id: String = ""
@export var name_cn: String = ""
@export var value: int = 0
@export var desc: String = ""
