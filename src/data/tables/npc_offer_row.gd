## npc_offer.csv 行：NPC 的可兑换与可偷道具（设计 19 §2.3／§2.4）。
##
## `kind=offer`（好感度兑换，需要 `favor_required`＋`price`，price 可空＝只靠好感）
## `kind=steal`（可偷，难度在 `npc_favor.steal_difficulty`）
##
## 这是「特有道具」的出口：NPC 独有装备只走这条路（`drop_only=1` 保证它们不从别处冒出来）。
extends "res://src/data/table_row.gd"

@export var offer_id: String = ""
@export var npc_id: String = ""
@export var kind: String = "offer"
## 引用 equip_base.equip_id 或 item_base.item_id
@export var item_id: String = ""
@export var favor_required: int = 0
## 价格（空 = 只靠好感换）
@export var price: int = 0
@export var note: String = ""
