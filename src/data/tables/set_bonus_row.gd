## set_bonus.csv 行：套装的件数档位 → buff（复合主键 set_id + required_count）。
##
## 档位**向下兼容、各自独立叠加**：达到 4 件时 2 件档的 buff 也生效。
extends "res://src/data/table_row.gd"

@export var set_id: String = ""
@export var required_count: int = 0
@export var buff_id: String = ""
@export var desc: String = ""
