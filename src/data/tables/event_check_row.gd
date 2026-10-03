## event_check.csv 行：事件判定（属性／非战斗技能两套来源）。
##
## check_source 带前缀：`attr:` 用裸属性（不含装备加成），`skill:` 用非战斗技能等级。
extends "res://src/data/table_row.gd"

@export var check_id: String = ""
## 大地图节点，引用 map_region.node_id，留空表示在小地图里
@export var region_id: String = ""
## 小地图场景，引用 map_local.scene_id
@export var scene_id: String = ""
## 房间，引用 dungeon_room.room_id
@export var room_id: String = ""
@export var check_source: String = ""
## 判定难度值（裸属性或技能等级要对到这个数）
@export var difficulty: int = 0
## hard 失败有代价 / soft 失败只是少拿好处
@export var check_type: String = "soft"
## none / item / room / event
@export var reward_type: String = "none"
@export var reward_id: String = ""
@export var once_only: bool = false
@export var fail_note: String = ""
@export var clue_source: String = ""
@export var note: String = ""


## 解析 check_source，返回 {"kind": "attr"|"skill"|"", "target_id": String}
func parsed_source() -> Dictionary:
	var separator := check_source.find(":")
	if separator < 0:
		return {"kind": "", "target_id": check_source}
	return {
		"kind": check_source.substr(0, separator),
		"target_id": check_source.substr(separator + 1),
	}


## 线索来源列表。
func clues() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in clue_source.split(";", false):
		out.append(part.strip_edges())
	return out
