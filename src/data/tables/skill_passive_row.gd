## skill_passive.csv 行：内功明细。
##
## 内功不互斥，改为**占格制**：每部按星级占 1~3 格，装上的总格数不超过 passive_capacity。
extends "res://src/data/table_row.gd"

@export var skill_id: String = ""
## 占格数，1~3（低级 ★1–2 占 1、高级 ★3–4 占 2、顶级 ★5 占 3）
@export var slot_cost: int = 1
## 特殊被动效果 id，空为无
@export var passive_effect: String = ""
@export var desc: String = ""
