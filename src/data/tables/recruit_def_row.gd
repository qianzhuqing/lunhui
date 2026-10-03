## recruit_def.csv 行：谁开局就在队里、谁在什么条件下加入。
##
## 0.10.0 新增（设计 09 §3.2）：开局只有书生一人，同伴在剧情里陆续加入
## （燕小七在清风驿客栈 → 林铁山落雁坡 → 苏九娘毒堂 → 白清和荒村）。
extends "res://src/data/table_row.gd"

@export var char_id: String = ""
## 初始成员（0.10.0：只有书生一行是 1，**不再取 character_base 前 4 行**）
@export var is_initial: bool = false
## 加入条件（旗标名，如 flag_board_read）；初始成员留空
@export var join_condition: String = ""
## 在哪个场景加入（引用 map_local.scene_id）
@export var join_scene: String = ""
@export var join_note: String = ""
