## map_local.csv 行：小地图场景（城镇／副本）。
extends "res://src/data/table_row.gd"

@export var scene_id: String = ""
@export var name_cn: String = ""
## town / dungeon / poi
@export var scene_type: String = ""
## 所属大地图节点，引用 map_region.node_id
@export var parent_node: String = ""
@export var room_count: int = 0
@export var level_range: String = ""
@export var has_combat: bool = false
@export var has_completion: bool = false
@export var save_allowed: bool = true
@export var note: String = ""
