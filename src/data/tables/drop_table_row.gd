## drop_table.csv 行：掉落表，每个掉落槽独立判定。
extends "res://src/data/table_row.gd"

@export var drop_row_id: String = ""
## 掉落组，敌人通过 enemy_base.drop_group 引用
@export var drop_group: String = ""
@export var slot: int = 0
## 物品或装备 id，按 item_type 决定查 item_base 还是 equip_base
@export var item_id: String = ""
## money / material / consumable / equip / key / tool / skillbook
@export var item_type: String = ""
@export var qty_min: int = 1
@export var qty_max: int = 1
## 基础掉率，1.0 表示必掉
@export var base_rate: float = 0.0
## independent 独立判定 / exclusive 互斥判定
@export var roll_type: String = "independent"
## 保底击杀次数，0 表示无保底
@export var pity_count: int = 0
@export var difficulty_scaled: bool = false
@export var first_kill_only: bool = false
@export var note: String = ""


func is_guaranteed() -> bool:
	return base_rate >= 1.0


func has_pity() -> bool:
	return pity_count > 0
