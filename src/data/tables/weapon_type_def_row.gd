## weapon_type_def.csv 行：武器类型定义。
extends "res://src/data/table_row.gd"

@export var weapon_type: String = ""
@export var name_cn: String = ""
## external / internal / odd
@export var default_element: String = "external"
## 主堆属性，"|" 分隔，引用 attribute_def.attr_id
@export var attr_focus: String = ""
## 关联武学流派，对应 skill_base.school_id，留空表示暂无专属流派
@export var skill_school: String = ""
@export var desc: String = ""


## 主堆属性列表。
func focus_attrs() -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in attr_focus.split("|", false):
		out.append(part.strip_edges())
	return out
