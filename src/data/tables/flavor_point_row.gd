## flavor_point.csv 行：观察点（设计 20 §3.2，0.29.1）。
##
## 这是**最薄的一张表**：一个位点、一句话。它**不参与任何数值、不发奖励、不锁任何路**
## ——玩家不看也能通关，看了就先一步猜到真相（探索的回报）。
##
## 为什么不复用既有通道（设计原文的口径）：`event_check` 会被算进「事件判定」并进线索本
## 与副本完成度；`hidden_trigger` 会被当成「隐藏内容」计数——观察点两样都不是，
## 占那两个数会把副本完成度的分母改错。
##
## 位点名 `Observe_<point_id>`：**前缀的唯一出处是本文件的 `MARKER_PREFIX`／
## `marker_name_of()`**（地编与小地图／大地图两边都按它认位点）。
extends "res://src/data/table_row.gd"

const MARKER_PREFIX := "Observe_"

@export var point_id: String = ""
## 小地图（scene_id）与大地图节点（region_id）**二选一**
@export var scene_id: String = ""
## 房间级位点：挂在 `Markers/<room_id>/` 下（与宝箱／触发同一套找法）
@export var room_id: String = ""
@export var region_id: String = ""
@export var text_cn: String = ""


static func marker_name_of(point_id: String) -> String:
	return "%s%s" % [MARKER_PREFIX, point_id]


func marker_name() -> String:
	return marker_name_of(point_id)
