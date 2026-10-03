## enemy_passive.csv 行：敌人装配的一部内功（0.12.0 新增，见 06 与设计 10 §二）。
##
## 设计口径（0.12.0）：敌人与角色在**五个维度**上完全一致——七维、等级、武器类型、招式、装备、内功。
## 所以敌人的抗性也不再是 `enemy_base` 的特权列：抗性是 `stat_def` 里的通用派生数值，
## 谁练了对应内功（如五毒心法给毒抗）谁就有——玩家练同一部拿到的也是同一份。
extends "res://src/data/table_row.gd"

@export var enemy_id: String = ""
## 装配的内功（引用 skill_base 里 skill_kind=passive 的行）
@export var skill_id: String = ""
@export var note: String = ""
