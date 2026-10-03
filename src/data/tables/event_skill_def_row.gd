## event_skill_def.csv 行：非战斗技能定义（事件判定用）。
extends "res://src/data/table_row.gd"

@export var skill_id: String = ""
@export var name_cn: String = ""
## 关联属性，引用 attribute_def.attr_id
@export var related_attr: String = ""
@export var max_level: int = 1
@export var desc: String = ""
