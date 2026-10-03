## item_base.csv 行：货币／材料／消耗品／钥匙道具／残页。
extends "res://src/data/table_row.gd"

@export var item_id: String = ""
@export var name_cn: String = ""
## currency / material / consumable / key / tool / skillbook
@export var item_type: String = ""
## 引用 rarity_def.rarity_id
@export var rarity: String = "common"
## 使用场景：field 非战斗回血 / battle 战斗中回血 / 空表示不是回血道具
@export var use_context: String = ""
@export var stack_max: int = 1
@export var sell_price: int = 0
@export var is_key_item: bool = false
@export var icon: String = ""
@export var desc: String = ""


func is_field_use() -> bool:
	return use_context == "field"


func is_battle_use() -> bool:
	return use_context == "battle"
