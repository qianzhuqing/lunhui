## skill_base.csv 行：武学总表（身份与门槛）。
##
## 0.6.0 起这里是**所有武学的索引**：招式数值在 skill_active.csv，内功在 skill_passive.csv，
## 内功加成在 skill_passive_stat.csv。战斗数值不要再往这张表加列。
extends "res://src/data/table_row.gd"

@export var skill_id: String = ""
@export var name_cn: String = ""
## active 招式 / passive 内功
@export var skill_kind: String = "active"
## 门派／流派标记
@export var school_id: String = ""
## 星级 1~5，引用 skill_star_def
@export var star: int = 1
## 招式绑定的武器类型；通用填 any，内功留空
@export var weapon_type: String = ""
## 修习门槛：某属性达到阈值才能学（留空表示无门槛）
@export var learn_req_attr: String = ""
@export var learn_req_value: int = 0
## start / npc / drop / story / hidden / item / shop
@export var source_type: String = ""
## 来源 id（门派、敌人、触发点等，自由文本）
@export var source_id: String = ""
@export var desc: String = ""


func is_active() -> bool:
	return skill_kind == "active"


func is_passive() -> bool:
	return skill_kind == "passive"


## 武器要求是否通用（any 或留空都算不限制）。
func accepts_any_weapon() -> bool:
	return weapon_type.is_empty() or weapon_type == "any"


func has_learn_requirement() -> bool:
	return not learn_req_attr.is_empty() and learn_req_value > 0
