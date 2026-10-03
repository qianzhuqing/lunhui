## ui_text.csv 行：界面文案（按 id 取一句话，不写死在代码里）。
##
## 0.10.2 新增（设计 09）：第一条是木桩到顶的提示 `dummy_cap_reached`。
## 为什么要这张表：这类"玩家可能以为卡住了"的提示以后还会加，写在代码里就得改代码。
extends "res://src/data/table_row.gd"

@export var text_id: String = ""
@export var text_cn: String = ""
## 给设计/开发看的说明：什么时候用它
@export var note: String = ""
