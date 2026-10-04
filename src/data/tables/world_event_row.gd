## world_event.csv 行：大地图随机事件（设计 19 §四）。
##
## 明雷默认关之后，大地图除了地形什么都没有——这一张表给它补上**非战斗**的内容：
## 按区域权重抽，`kind` 决定玩法（货商／门派弟子／江湖人／官府／天象／野物）。
##
## 三条纪律（19 §四）：以非战斗为主（战斗只留玩家主动选的切磋）、权重按区域给、事件要给东西或信息。
extends "res://src/data/table_row.gd"

@export var event_id: String = ""
@export var name_cn: String = ""
@export var kind: String = ""
## 适用区域（`map_region.node_id`，多个用 ";" 分隔）——每个区域的池子不同
@export var region_tags: String = ""
## 抽取权重（越大越常见）
@export var weight: int = 0
## 额外条件（旗标；空 = 无条件）
@export var condition: String = ""
@export var repeatable: int = 1
## 效果类型：trade／spar／hint／gift／check
@export var effect_kind: String = ""
## 效果指向的东西（掉落组／队伍／线索 id／武学 id……由 `effect_kind` 决定怎么读）
@export var effect_id: String = ""
## 事件发生时的提示（HUD／弹窗上那句话）
@export var prompt_text_cn: String = ""
@export var text_cn: String = ""
@export var note: String = ""
