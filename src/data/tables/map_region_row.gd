## map_region.csv 行：大地图节点（地标／兴趣点／驿站）。
extends "res://src/data/table_row.gd"

@export var node_id: String = ""
@export var name_cn: String = ""
## town / fast_travel / wild / dungeon / poi
@export var node_type: String = ""
@export var parent_region: String = ""
@export var pos_x: float = 0.0
@export var pos_y: float = 0.0
@export var icon: String = ""
@export var unlock_condition: String = ""
@export var reveal_on_map: bool = false
## 进入的小地图，引用 map_local.scene_id
@export var enter_scene: String = ""
@export var respawn_group: String = ""
@export var note: String = ""
