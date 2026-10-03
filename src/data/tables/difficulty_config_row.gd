## difficulty_config.csv 行：难度与敌方数值倍率。
extends "res://src/data/table_row.gd"

@export var difficulty_id: String = ""
@export var name_cn: String = ""
@export var enemy_hp_mul: float = 1.0
@export var enemy_atk_mul: float = 1.0
@export var enemy_def_mul: float = 1.0
@export var exp_mul: float = 1.0
@export var money_mul: float = 1.0
@export var unlock_condition: String = ""
@export var note: String = ""
