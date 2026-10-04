## dialogue_option.csv 行：玩家在某个对话节点上能选的**一句话**，以及选完世界改了什么。
##
## 效果三样，都可留空：**置旗标**（心性与立场就记在这儿——设计 20 §四 的「义／谋／利 +1」）、
## **好感增减**（`npc_favor` 那一套）、**给一件东西**（走 `BattleReward.grant_item`，
## 与掉落／事件奖励同一条入账口径）。
##
## **没有数值列**：设计 20 §十一 写死「选项改立场与旁白、**不改数值**」——
## 战力成长走武学／装备／加点三条既有通道，不从对话里发属性。
extends "res://src/data/table_row.gd"

@export var option_id: String = ""
@export var node_id: String = ""
@export var sort_order: int = 1
@export var text_cn: String = ""
## 显示条件（设计 20 §四 的「前置」那一列，例：看过车辙观察点）；留空 = 一直可选
@export var condition: String = ""
## 选完跳到哪个节点；留空 = 结束这次对话
@export var next_node_id: String = ""
@export var set_flag: String = ""
@export var favor_delta: int = 0
@export var grant_item_id: String = ""
@export var note: String = ""
