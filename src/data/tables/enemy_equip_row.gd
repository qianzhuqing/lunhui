## enemy_equip.csv 行：敌人身上穿的一件装备（0.11.1 新增，见 06 与设计 10 §四）。
##
## 设计口径（0.11.1）：**敌人身上穿的，就是能掉的**——掉落不再单配，直接从这张表推
## （按 `rarity_def.drop_weight` 掷稀有度概率，首杀必掉一件）。
## 这样「他用着青锋剑却掉柴刀」这类两处定义不同步的问题从结构上消失。
##
## 注意：`slot_id` 与 `equip_id` 的合法性与角色那边同一套（`equip_slot_def` / `equip_base`）；
## 「装备影响敌人属性」那一半要等 0.11.0 第二步（敌人七维模板）落地后一起接（见框架说明）。
extends "res://src/data/table_row.gd"

@export var enemy_id: String = ""
## 装备槽（引用 equip_slot_def.slot_id）
@export var slot_id: String = ""
## 穿的那件装备（引用 equip_base.equip_id）
@export var equip_id: String = ""
@export var note: String = ""
