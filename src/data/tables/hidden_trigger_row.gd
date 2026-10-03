## hidden_trigger.csv 行：隐藏内容触发条件与奖励。
extends "res://src/data/table_row.gd"

@export var trigger_id: String = ""
@export var name_cn: String = ""
## 引用 map_local.scene_id
@export var scene_id: String = ""
## 引用 dungeon_room.room_id
@export var room_id: String = ""
## item / space / sequence / behavior / kill_style / completion / carry
@export var trigger_type: String = ""
## 需要的道具，引用 item_base.item_id
@export var required_item: String = ""
## 条件表达式，由具体 trigger_type 解释（公式不进表，解释在代码里）
@export var required_condition: String = ""
## boss / equip / skillbook / room / event
@export var reward_type: String = ""
## 奖励 id，随 reward_type 指向敌表／装备表／物品表／房间表；event 为自由文本
@export var reward_id: String = ""
@export var once_only: bool = false
## 线索来源，多个用 ";" 分隔
@export var clue_source: String = ""
@export var note: String = ""


## 线索来源列表。
func clues() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in clue_source.split(";", false):
		out.append(part.strip_edges())
	return out
