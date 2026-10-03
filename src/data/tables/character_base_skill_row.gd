## character_base_skill.csv 行：角色的初始非战斗技能等级。
##
## 非战斗技能定义在 event_skill_def.csv，事件判定（event_check）用它们做 check_source。
extends "res://src/data/table_row.gd"

@export var char_id: String = ""
## 引用 event_skill_def.skill_id
@export var skill_id: String = ""
@export var level: int = 0
