## skill_star_def.csv 行：星级定义（强度与获取难度）。
##
## 熟练度成长随星级递减：★1 每级 +5%，★5 只有 +2.5%——刻意如此，
## 免得玩家每拿到一部高星武学，之前练的全白练。
extends "res://src/data/table_row.gd"

@export var star: int = 1
@export var name_cn: String = ""
@export var color: String = ""
## 每级熟练度提供的倍率加成
@export var mastery_gain: float = 0.0
## 修习门槛的属性阈值
@export var learn_req_value: int = 0
## 打坐基础费用
@export var cultivate_cost_base: int = 0
@export var desc: String = ""
