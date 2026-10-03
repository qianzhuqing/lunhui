## buff_grant.csv 行：来源 → buff（谁在什么时机发哪个 buff）。
##
## source_type 决定 source_id 去哪张表查：skill_passive / skill_active / equip / set。
extends "res://src/data/table_row.gd"

@export var grant_id: String = ""
## skill_passive / skill_active / equip / set
@export var source_type: String = ""
@export var source_id: String = ""
@export var buff_id: String = ""
## on_cast（催动/施放时）／on_equip（装备即生效）／on_hit（命中时）／on_battle_start（进战斗时）
@export var trigger: String = ""
@export var stacks: int = 1
@export var note: String = ""
