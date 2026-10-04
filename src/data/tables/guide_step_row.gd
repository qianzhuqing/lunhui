## guide_step.csv 行：开局引导的目标链（HUD 上常驻「当前目标」）。
##
## 0.10.0 新增（设计 09 §3.1）4 步；**0.22.0 扩到 6 步**（设计 18 §3.2）：
## 告示板 → 招募同伴 → 木桩练到 5 级并备货 → 上山 → 进寨 → 救出沈小姐回城复命。
## `condition` 是「这一步成为当前目标」的判据（剧情旗标；`start` 表示开局即满足）；
## `condition_spec` 是**给程序看的判定口径**——原来只有一个裸旗标名，
## 程序得猜「什么时候算备齐了」，现在是表里的契约（设计 18 的断点 7）。
extends "res://src/data/table_row.gd"

@export var step_id: String = ""
@export var chapter_id: String = ""
@export var sort_order: int = 0
@export var condition: String = ""
@export var text_cn: String = ""
@export var condition_spec: String = ""
@export var note: String = ""
