## 属性四层计算。
##
## 文档定死的结构（docs/design/01_角色系统.md），顺序不可改：
##   最终属性 = ( 基础值 + 等级成长 + 加点 + 装备固定值 + 心法固定值 ) × ( 1 + 百分比加成总和 )
##
## 本实现的分层落点：
##   第 1 层「基础值」      角色初始五维（character_base.initial_*）
##   第 2 层「加点」        玩家分配的属性点 + 装备/心法的属性点加成（都先过 attr_to_stat 换算成派生数值）
##   第 3 层「等级成长」    level_growth 的基础派生数值
##   第 4 层「固定值」      bonus_ 类派生数值加成（装备、心法、临时增益）
##   第 5 步「百分比」      最后统一相乘
## 最后按 stat_def 的 min/max 夹取，并按 value_kind 决定是否取整。
class_name AttributeCalculator
extends RefCounted

const CurveEvaluatorScript := preload("res://src/core/curve_evaluator.gd")
const TableDbScript := preload("res://src/core/table_db.gd")

## 贡献类型。
const CONTRIB_ATTR_POINT := "attr_point"
const CONTRIB_STAT_FLAT := "stat_flat"
const CONTRIB_STAT_PERCENT := "stat_percent"

## level_growth 的基础列 → stat_def.stat_id。
const LEVEL_BASE_STATS := {
	"base_hp": "hp_max",
	"base_qi": "qi_max",
	"base_atk_phys": "atk_phys",
	"base_atk_qi": "atk_qi",
	"base_def_phys": "def_phys",
	"base_def_qi": "def_qi",
	"base_speed": "speed",
}

var _db: TableDbScript


func _init(db: TableDbScript) -> void:
	_db = db


## 计算最终派生数值，返回 {stat_id: float|int}。
##
## base_attrs   角色初始五维 {attr_id: int}
## allocations  玩家加点 {attr_id: int}
## contributions 外部贡献列表，元素形如
##   {"kind": "attr_point", "target": "str", "value": 2, "source": "eq_sword_02"}
##   {"kind": "stat_flat", "target": "atk_phys", "value": 6, "source": "eq_sword_02"}
##   {"kind": "stat_percent", "target": "atk_phys", "value": 0.1, "source": "buff_x"}
##
## include_level_base  **敌人不吃 `level_growth` 的基础列**（设计 10 §七，0.28.0 答 Q57）：
## `base_hp`／`base_atk_phys`… 是**英雄基线**（「一个 N 级角色长什么样」），给敌人共用
## 等于让每只 2 级野狼都变成「2 级英雄」，而杂兵本来就该低于同级英雄。所以敌人传 `false`：
## 强度**全部由七维 ＋ 装备／内功固定值给**，`level` 对敌人只剩三件事——
## 经验与掉落档次、招式槽与内功容量的门槛、难度标签（那三件都不在这条管线里）。
func compute(
	level: int,
	base_attrs: Dictionary = {},
	allocations: Dictionary = {},
	contributions: Array = [],
	include_level_base: bool = true
) -> Dictionary:
	var attr_totals := attr_totals_of(base_attrs, allocations, contributions)
	var subtotal := level_base_stats(level) if include_level_base else {}

	# 属性 → 派生数值
	for row: Resource in _db.rows("attr_to_stat"):
		var value: float = CurveEvaluatorScript.evaluate(
			row.curve,
			row.rate,
			float(attr_totals.get(row.attr_id, 0.0)),
			row.cap,
			row.param
		)
		subtotal[row.stat_id] = float(subtotal.get(row.stat_id, 0.0)) + value

	# 派生数值固定值层
	for contribution: Dictionary in contributions:
		if contribution.get("kind", "") != CONTRIB_STAT_FLAT:
			continue
		var stat_id := _contribution_target(contribution)
		subtotal[stat_id] = float(subtotal.get(stat_id, 0.0)) + float(contribution.get("value", 0.0))

	# 百分比层
	var percent_totals := percent_totals_of(contributions)

	var out: Dictionary = {}
	for stat_id: String in subtotal:
		var value := float(subtotal[stat_id]) * (1.0 + float(percent_totals.get(stat_id, 0.0)))
		out[stat_id] = apply_stat_def(stat_id, value)
	return out


## 只算五维合计（基础 + 加点 + 属性点类贡献）。
func attr_totals_of(base_attrs: Dictionary, allocations: Dictionary = {}, contributions: Array = []) -> Dictionary:
	var totals: Dictionary = {}
	for attr_id: Variant in base_attrs:
		totals[str(attr_id)] = float(base_attrs[attr_id])
	for attr_id: Variant in allocations:
		totals[str(attr_id)] = float(totals.get(str(attr_id), 0.0)) + float(allocations[attr_id])
	for contribution: Dictionary in contributions:
		if contribution.get("kind", "") != CONTRIB_ATTR_POINT:
			continue
		var attr_id := _contribution_target(contribution)
		totals[attr_id] = float(totals.get(attr_id, 0.0)) + float(contribution.get("value", 0.0))
	return totals


## 等级成长带的固定值层。
func level_base_stats(level: int) -> Dictionary:
	var row := _db.get_row("level_growth", level)
	if row == null:
		push_error("[AttributeCalculator] level_growth 缺少等级 %s" % level)
		return {}
	var out: Dictionary = {}
	for column: String in LEVEL_BASE_STATS:
		out[LEVEL_BASE_STATS[column]] = float(row.get(column))
	return out


func percent_totals_of(contributions: Array) -> Dictionary:
	var totals: Dictionary = {}
	for contribution: Dictionary in contributions:
		if contribution.get("kind", "") != CONTRIB_STAT_PERCENT:
			continue
		var stat_id := _contribution_target(contribution)
		totals[stat_id] = float(totals.get(stat_id, 0.0)) + float(contribution.get("value", 0.0))
	return totals


## 按 stat_def 夹取上下限并按 value_kind 取整。
func apply_stat_def(stat_id: String, value: float) -> Variant:
	var stat_row := _db.get_row("stat_def", stat_id)
	if stat_row == null:
		push_error("[AttributeCalculator] stat_def 缺少派生数值 %s" % stat_id)
		return value
	var result := value
	result = maxf(result, stat_row.min_value)
	if stat_row.has_max():
		result = minf(result, stat_row.max_value)
	if stat_row.value_kind == "int":
		return int(floor(result))
	return result


func _contribution_target(contribution: Dictionary) -> String:
	if contribution.has("target"):
		return str(contribution["target"])
	if contribution.has("attr_id"):
		return str(contribution["attr_id"])
	if contribution.has("stat_id"):
		return str(contribution["stat_id"])
	return ""
