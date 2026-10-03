## dungeon_room.csv 行：副本房间流程与出口。
extends "res://src/data/table_row.gd"

@export var room_id: String = ""
## 引用 map_local.scene_id
@export var scene_id: String = ""
@export var floor: int = 1
@export var room_name: String = ""
## entrance / battle / treasure / trap / secret / elite / story / boss
@export var room_type: String = ""
## main / side / hidden
@export var branch_group: String = "main"
## 引用 enemy_team.team_id
@export var enemy_team: String = ""
## 引用 drop_table.drop_group
@export var chest_id: String = ""
## 出口房间，"|" 分隔，引用 dungeon_room.room_id
@export var exit_rooms: String = ""
## 引用 hidden_trigger.trigger_id
@export var hidden_trigger: String = ""
@export var note: String = ""


## 出口房间 id 列表。
func exits() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in exit_rooms.split("|", false):
		out.append(part.strip_edges())
	return out
