## dialogue_option.csv 行：玩家在某个对话节点上能选的**一句话**，以及选完世界改了什么。
##
## 效果三样，都可留空：**置旗标**（心性与立场就记在这儿——设计 20 §四 的「义／谋／利 +1」）、
## **好感增减**（`npc_favor` 那一套）、**给一件东西**（走 `BattleReward.grant_item`，
## 与掉落／事件奖励同一条入账口径）。
##
## **没有数值列**：设计 20 §十一 写死「选项改立场与旁白、**不改数值**」——
## 战力成长走武学／装备／加点三条既有通道，不从对话里发属性。
extends "res://src/data/table_row.gd"

## `set_flag` 的分隔符：**一列可以写多个旗标**（2026-10-04，Q83）。
##
## 由来：幕二「拔剑」那条选项要一次置两个旗标（`flag_lin_silent` ＋ `flag_luoyanpo_met`），
## 而这一列原本只装得下一个。与其给表加一列（列一动，行类／两端校验／数据字典都要跟），
## 不如让这一列能写多个（`flag_a;flag_b`）——**旧数据是单值，行为一字不变**。
##
## **拆法只有这一处**：运行期 `DialogueService` 与构建期 `TableValidator` 都调它；
## `tools/validate_tables.ps1` 那份是镜像（PS1 读不到 GDScript），改这里要连着改那边。
const SET_FLAG_SEPARATOR := ";"


## 拆 `set_flag`：空串 → 空；单值 → 一个元素。空白段（`flag_a;;flag_b`）跳过，不当错误。
static func parse_set_flags(text: String) -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in text.split(SET_FLAG_SEPARATOR, false):
		var flag := part.strip_edges()
		if not flag.is_empty():
			out.append(flag)
	return out


@export var option_id: String = ""
@export var node_id: String = ""
@export var sort_order: int = 1
@export var text_cn: String = ""
## 显示条件（设计 20 §四 的「前置」那一列，例：看过车辙观察点）；留空 = 一直可选
@export var condition: String = ""
## 选完跳到哪个节点；留空 = 结束这次对话
@export var next_node_id: String = ""
## 选完置的旗标；**多个用分号隔开**（Q83）。心性写 `heart_*`（义／谋／利）
@export var set_flag: String = ""
@export var favor_delta: int = 0
@export var grant_item_id: String = ""
@export var note: String = ""
