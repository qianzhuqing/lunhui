## difficulty_drop_rate.csv 行：「难度不改变掉落清单，只改变掉落几率」的唯一实现处。
extends "res://src/data/table_row.gd"

@export var difficulty_id: String = ""
@export var rarity_id: String = ""
@export var rate_multiplier: float = 1.0
@export var note: String = ""
