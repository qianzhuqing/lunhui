## buff_def.csv 行：buff 本体（极性／作用域／持续／叠加规则）。
##
## 统一模型见 docs/design/08_增益与套装.md：装备特效、内功运功、套装档位、招式增益
## 全部收敛成这个表里的一个 buff；会逐回合掉血的减益仍走 status_effect。
extends "res://src/data/table_row.gd"

@export var buff_id: String = ""
@export var name_cn: String = ""
## 0 增益 / 1 减益（同一套机制，只是极性不同）
@export var is_debuff: bool = false
## battle 进战斗清空／field 跨场景保留、进战斗后按回合递减
@export var scope: String = ""
## >0 剩余回合数；0 常驻（随来源存在而存在）；-1 持续到本场结束
@export var duration: int = 0
## **仅 `scope=field` 用**：战斗外的有效分钟数（现实时间）；进战斗后转为**整场有效**（0.8.0 口径）。
## `battle` 的 buff 看 `duration`（回合），两者别混。
@export var field_minutes: int = 0
## refresh 刷新回合／stack 叠层（上限 max_stack）／unique 全局唯一
@export var stack_rule: String = ""
@export var max_stack: int = 1
## stat 走 buff_stat 的数值修正／computed 由代码算／special 特殊实现
@export var effect_kind: String = ""
@export var icon: String = ""
@export var desc: String = ""
