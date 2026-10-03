## set_member.csv 行：套装包含哪些成员（复合主键 set_id + member_id）。
##
## member_id 归哪张表由 set_def.set_kind 决定，**只有已装备／已装配的成员才算数**。
extends "res://src/data/table_row.gd"

@export var set_id: String = ""
@export var member_id: String = ""
@export var note: String = ""
