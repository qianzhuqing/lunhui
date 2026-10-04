## story_node.csv 行：剧情节点（0.22.0 新增，设计 18 §3.1）。
##
## 它解决的是「`source_type=story` 的武学没有落地处」：`skill_base.source_id`
## 指向的就是本表的 `node_id`，条件满足时由 `StoryService` 发放 `grant_skill_ids`。
## `kind`：`chapter_end`（章节收束，同时置章节完成旗标）／`faction`（门派支线）／
##         `choice`（抉择的永久增益，设计 20 §八）／`origin_gift`（出身本命机遇，设计 21 §九）。
##
## `kind=choice` 的四种增益列（设计 20 §八，0.29.1）：**不新建表**——`story_node` 本来就是
## 「按旗标触发、发东西」的节点表，抉择也是一个节点。增益走 `AttributeCalculator` 的**贡献通道**
## （`attr_point`／`stat_flat`，与图鉴奖励同一条路），**不新增字段、不动存档版本**：
## 「选过没选过」就是那枚抉择旗标（`trigger_condition`）。
extends "res://src/data/table_row.gd"

## 贡献 kind 字符串的**唯一定义处**在 `AttributeCalculator`——这里不重写字面量
## （写错会被计算器静默跳过，门限 `_check_single_source_of_truth` 当场会红）。
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

@export var node_id: String = ""
@export var kind: String = ""
@export var chapter_id: String = ""
## 触发地点（引用 map_local.scene_id）；留空 = 地点不限（例如药王谷那条）
@export var place_id: String = ""
## 触发条件（剧情旗标）
@export var trigger_condition: String = ""
## 发放的武学（引用 skill_base.skill_id，分号分隔）
@export var grant_skill_ids: String = ""
## 永久增益（`kind=choice` 用）：属性点层与固定值层各一对，分别引用 attribute_def／stat_def
@export var bonus_attr_id: String = ""
@export var bonus_attr_value: int = 0
@export var bonus_stat_id: String = ""
@export var bonus_stat_value: float = 0.0
@export var text_cn: String = ""
@export var note: String = ""


## 这条节点的增益贡献（`AttributeCalculator` 的贡献形状）。
## 两对各填一条；都不填就返回空（那不是增益节点）。
func bonus_contributions() -> Array:
	var out: Array = []
	if not bonus_attr_id.is_empty() and bonus_attr_value != 0:
		out.append({
			"kind": AttributeCalculatorScript.CONTRIB_ATTR_POINT, "target": bonus_attr_id,
			"value": bonus_attr_value, "source": "story_node:%s" % node_id,
		})
	if not bonus_stat_id.is_empty() and bonus_stat_value != 0.0:
		out.append({
			"kind": AttributeCalculatorScript.CONTRIB_STAT_FLAT, "target": bonus_stat_id,
			"value": bonus_stat_value, "source": "story_node:%s" % node_id,
		})
	return out
