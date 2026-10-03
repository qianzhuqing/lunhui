## npc_guard.csv 行：宝箱守护者——同一个箱子给「文取」与「武取」两条路。
##
## 0.10.0 新增（设计 09 §一）：宝箱不再由打怪解锁，而是绑给一个具体的人。
## 文取 = 满足 `peace_condition`（`item:<道具>` 或 `skill:<非战斗技能>`，用 `|` 连多条件）；
## 武取 = 打赢 `fight_team`。两条路拿**同一个箱子**（`dungeon_room.chest_id` 仍描述箱内东西）。
extends "res://src/data/table_row.gd"

@export var guard_id: String = ""
## 守护者守的房间（引用 dungeon_room.room_id）
@export var room_id: String = ""
@export var npc_name_cn: String = ""
## 文取的三列（设计 0.14.0 把原来的单列 `peace_condition` 拆开，门槛值终于有了出处——Q53）：
## `peace_item` 要交的道具（引用 item_base）、`peace_check_source` 走哪条判定
## （`skill:<非战斗技能>` 或 `attr:<属性>`）、`peace_check_value` 判定门槛。
## **三字段可叠加，满足任意一条即算过**（06 的口径）；两条都留空时这位守卫就没有文取这条路。
@export var peace_item: String = ""
@export var peace_check_source: String = ""
@export var peace_check_value: int = 0
@export var peace_note: String = ""
## 武取队伍（引用 enemy_team.team_id）
@export var fight_team: String = ""
@export var fight_note: String = ""
## 奖励掉落组（引用 drop_table.drop_group）
@export var reward_group: String = ""
