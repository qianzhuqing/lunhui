## npc_quest.csv 行：NPC 专属任务线（设计 19 §2.5）。
##
## 同一个 NPC 的多条任务按 `sort_order` 串成一条线。
## `requirement` **复用既有条件语言**，不新造一套：
##   `item:<item_id>:<数量>`／`flag_<旗标>`／`event:<check_id>`／`kill_style:<要素>`
extends "res://src/data/table_row.gd"

@export var quest_id: String = ""
@export var npc_id: String = ""
@export var sort_order: int = 0
@export var requirement: String = ""
## 奖励道具（分号分隔，引用 item_base／equip_base）
@export var reward_item_ids: String = ""
@export var reward_favor: int = 0
@export var text_cn: String = ""
@export var note: String = ""
