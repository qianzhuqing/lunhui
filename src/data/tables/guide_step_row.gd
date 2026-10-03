## guide_step.csv 行：开局引导的目标链（HUD 上常驻「当前目标」）。
##
## 0.10.0 新增（设计 09 §3.1）：4 步——告示板 → 招募同伴 → 木桩练到 5 级并备货 → 上山。
## `condition` 是"这一步算完成"的条件，取值由 `GuideService` 认（`start` 表示开局即满足）。
extends "res://src/data/table_row.gd"

@export var step_id: String = ""
@export var sort_order: int = 0
@export var condition: String = ""
@export var text_cn: String = ""
@export var note: String = ""
