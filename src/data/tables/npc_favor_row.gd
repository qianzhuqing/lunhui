## npc_favor.csv 行：好感度规则（设计 19 §2.2）。
##
## 喜好分两级：`like_item_ids`（具体物品＝最爱，送一件加很多）与
## `like_categories`（类别，如 material，送同类只加一点）——
## **送对具体物品是惊喜，送对类别只是礼貌**。
extends "res://src/data/table_row.gd"

@export var npc_id: String = ""
@export var initial_favor: int = 0
@export var favor_max: int = 100
## 具体物品（最爱）；多个用 ";" 分隔，引用 item_base.item_id
@export var like_item_ids: String = ""
## 类别（如 material／consumable）；多个用 ";" 分隔，引用 item_base.item_type
@export var like_categories: String = ""
@export var dislike_item_ids: String = ""
## 送一件礼物加多少好感（送对喜好再加 `like_bonus` 的份）
@export var gift_favor: int = 5
## 切磋赢了加多少好感（0 = 不能切磋）
@export var spar_favor: int = 0
## 偷窃难度（事件判定的门槛）；**0 = 不可偷**（纯善意的 NPC 留的口子）
@export var steal_difficulty: int = 0
