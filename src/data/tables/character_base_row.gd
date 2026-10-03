## character_base.csv 行：可操控角色的初始五维与初始武学。
##
## 文档只给了等级成长与属性分层，没写角色初始五维的来源，这张表补上这一层：
## initial_* 是四层公式里的「基础值」，加点与装备在 AttributeCalculator 里叠加。
extends "res://src/data/table_row.gd"

@export var char_id: String = ""
@export var name_cn: String = ""
## 刚猛流 / 毒功流 / 刺客流 / 内功续航流，仅作标记与筛选用
@export var role_tag: String = ""
## 武器类型，引用 weapon_type_def.weapon_type
@export var weapon_type: String = ""
@export var initial_str: int = 0
@export var initial_con: int = 0
@export var initial_agi: int = 0
@export var initial_int: int = 0
@export var initial_luk: int = 0
## 0.6.0 新增：悟性（招式槽上限与武学熟练度成长）
@export var initial_wu: int = 0
## 0.6.0 新增：根骨（内功容量与内力上限）
@export var initial_gen: int = 0
## 初始五维之和，必须等于 initial_* 相加（校验器会查）
@export var attr_total: int = 0
@export var start_level: int = 1
## 初始武学，多个用 ";" 分隔，引用 skill_base.skill_id
@export var start_skill_ids: String = ""
## 初始装备，多个用 ";" 分隔，引用 equip_base.equip_id
@export var start_equip_ids: String = ""
@export var desc: String = ""


## 初始五维，键为 attribute_def.attr_id。
func initial_attrs() -> Dictionary:
	return {
		"str": initial_str,
		"con": initial_con,
		"agi": initial_agi,
		"int": initial_int,
		"luk": initial_luk,
		"wu": initial_wu,
		"gen": initial_gen,
	}


## 初始武学 id 列表。
func skill_ids() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in start_skill_ids.split(";", false):
		out.append(part.strip_edges())
	return out


## 初始五维之和。
func attr_sum() -> int:
	return initial_str + initial_con + initial_agi + initial_int + initial_luk + initial_wu + initial_gen


## 初始装备 id 列表。
func equip_ids() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in start_equip_ids.split(";", false):
		out.append(part.strip_edges())
	return out
