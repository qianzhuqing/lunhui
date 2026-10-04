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
## **大区中文名**（本章＝「江南道·东部」）：大地图 HUD 左上角**常驻**显示的那一级名字。
## 以前它只作为代码里的兜底字符串存在（`overworld_controller._region_name()` 的 `else`），
## 违反「文案在表里」——设计 11 §三／14 §六 定的（0.31.1）。
@export var region_name_cn: String = ""
## **指路牌文案**（设计 0.31.2）：空 = 不摆；非空时地编要在那张图上摆 `Markers/Sign_<node_id>` 位点，
## 代码把这句话摆在牌子旁边（**文案只在表里**——不许在代码里拼「→ 多少里」这类句子）。
@export var signpost_cn: String = ""
