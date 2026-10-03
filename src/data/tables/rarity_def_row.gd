## rarity_def.csv 行：稀有度定义。
extends "res://src/data/table_row.gd"

@export var rarity_id: String = ""
@export var name_cn: String = ""
@export var color: String = ""
@export var affix_min: int = 0
@export var affix_max: int = 0
## 掉落权重，仅作参考；实际掉率走 drop_table.base_rate
@export var drop_weight: float = 0.0
@export var desc: String = ""
