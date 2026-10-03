## roaming_spawn.csv 行：明雷刷新点。
extends "res://src/data/table_row.gd"

@export var spawn_id: String = ""
## 引用 map_region.node_id
@export var region_id: String = ""
## 引用 enemy_team.team_id
@export var team_id: String = ""
## idle / patrol / wander / chase / sleep
@export var behavior: String = "idle"
@export var patrol_path_id: String = ""
@export var alert_radius: float = 0.0
@export var chase_speed: float = 0.0
@export var respawn_sec: int = 0
@export var is_elite: bool = false
@export var elite_marker: String = ""
@export var note: String = ""
