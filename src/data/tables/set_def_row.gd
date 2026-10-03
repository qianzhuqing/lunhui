## set_def.csv 行：套装本体。见 docs/design/08_增益与套装.md。
extends "res://src/data/table_row.gd"

@export var set_id: String = ""
@export var name_cn: String = ""
## equip（按穿在身上的件数）／skill_active（按装配的招数）／skill_passive（按装配的占格数之和）
@export var set_kind: String = ""
@export var desc: String = ""
