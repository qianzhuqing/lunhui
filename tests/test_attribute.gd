## 属性四层计算与成长曲线。
extends "res://tests/test_case.gd"

const CurveEvaluatorScript := preload("res://src/core/curve_evaluator.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

const BASE_ATTRS := {"str": 5, "con": 5, "agi": 5, "int": 5, "luk": 5}


func suite_name() -> String:
	return "属性四层计算与曲线"


func run() -> void:
	var db = get_db()
	var calculator = AttributeCalculatorScript.new(db)
	_check_curves()
	_check_four_layers(calculator)
	_check_caps(calculator)
	_check_level_growth(calculator)


func _check_curves() -> void:
	check_float(CurveEvaluatorScript.evaluate("linear", 2.0, 5.0), 10.0, "线性：2.0/点 × 5 点")
	check_float(CurveEvaluatorScript.evaluate("linear", 2.0, 100.0, 50.0), 50.0, "线性 + 上限：夹到 cap")
	check_float(CurveEvaluatorScript.evaluate("diminishing", 0.01, 80.0, 0.95, 80.0), 0.475, "递减：attr = param 时取到一半 cap")
	var low: float = CurveEvaluatorScript.evaluate("diminishing", 0.01, 10.0, 0.95, 80.0)
	var high: float = CurveEvaluatorScript.evaluate("diminishing", 0.01, 400.0, 0.95, 80.0)
	check_gt(high, low, "递减曲线单调递增")
	check_lt(high, 0.95, "递减曲线永远到不了 cap")
	check_lt(CurveEvaluatorScript.evaluate("diminishing", 0.01, 1000000.0, 0.95, 80.0), 0.950001, "递减曲线趋近 cap 但不越界")
	var points := PackedVector2Array([Vector2(0, 0), Vector2(10, 5), Vector2(20, 20)])
	check_float(CurveEvaluatorScript.evaluate("piecewise", 0.0, 5.0, 0.0, 0.0, points), 2.5, "分段插值：区间中点")
	check_float(CurveEvaluatorScript.evaluate("piecewise", 0.0, 100.0, 0.0, 0.0, points), 20.0, "分段插值：超出上界取端点")


## 四层公式：final = (基础 + 等级成长 + 加点 + 固定值) × (1 + 百分比)
func _check_four_layers(calculator) -> void:
	var level := 1
	var base_stats: Dictionary = calculator.compute(level, BASE_ATTRS)
	# level_growth[1]：hp 100 / qi 40 / atk 10 / def 5 / speed 10
	check_eq(int(base_stats["hp_max"]), 100 + 12 * 5, "第 1+3 层：气血 = 基础 100 + 体 5 × 12")
	check_eq(int(base_stats["atk_phys"]), 10 + 2 * 5, "第 1+3 层：外功 = 基础 10 + 力 5 × 2")
	check_eq(int(base_stats["atk_qi"]), int(floor(10.0 + 1.8 * 5)), "第 1+3 层：内功 = 基础 10 + 智 5 × 1.8")
	check_eq(int(base_stats["def_phys"]), int(floor(5.0 + 1.2 * 5)), "第 1+3 层：外防 = 基础 5 + 体 5 × 1.2")
	check_eq(int(base_stats["speed"]), int(floor(10.0 + 1.5 * 5)), "第 1+3 层：身法向下取整")
	check_eq(int(base_stats["qi_max"]), 40 + 6 * 5, "第 1+3 层：内力 = 基础 40 + 智 5 × 6")

	# 第 2 层加点
	var allocated: Dictionary = calculator.compute(level, BASE_ATTRS, {"str": 5})
	check_eq(int(allocated["atk_phys"]), 10 + 2 * 10, "第 2 层：加点走属性→派生换算")

	# 第 2 层装备属性点（会连锁影响多个派生数值）
	var equip_attr := [{"kind": "attr_point", "target": "str", "value": 4, "source": "夹具"}]
	var with_equip: Dictionary = calculator.compute(level, BASE_ATTRS, {"str": 5}, equip_attr)
	check_eq(int(with_equip["atk_phys"]), 10 + 2 * 14, "第 2 层：装备属性点连锁换算")
	check_eq(int(with_equip["poise_break"]), int(floor(0.8 * 14)), "第 2 层：同一属性点同时驱动破架势")

	# 第 4 层固定值 + 百分比层
	var contributions := [
		{"kind": "attr_point", "target": "str", "value": 4},
		{"kind": "stat_flat", "target": "atk_phys", "value": 10},
		{"kind": "stat_percent", "target": "atk_phys", "value": 0.5},
	]
	var full: Dictionary = calculator.compute(level, BASE_ATTRS, {"str": 5}, contributions)
	# (10 + 2×14 + 10) × 1.5 = 72；若把百分比提前加，会得到 67，说明顺序是对的
	check_eq(int(full["atk_phys"]), 72, "百分比最后相乘，不参与加法层")


func _check_caps(calculator) -> void:
	var huge := {"str": 0, "con": 200, "agi": 1000000, "int": 0, "luk": 1000000}
	var stats: Dictionary = calculator.compute(20, huge)
	check_in_range(float(stats["hit_rate"]), 0.94, 0.95, "命中率被夹在 cap 0.95 以内")
	check_in_range(float(stats["dodge_rate"]), 0.0, 0.6, "闪避率不超过 cap 0.6")
	check_in_range(float(stats["crit_rate"]), 0.0, 0.75, "暴击率不超过 cap 0.75")
	check_in_range(float(stats["crit_dmg"]), 0.0, 3.0, "暴击伤害不超过 cap 3.0")
	check_in_range(float(stats["drop_rate"]), 0.0, 0.25, "掉落机缘不超过保守方案上限 0.25")
	check_in_range(float(stats["res_internal"]), 0.0, 0.75, "内伤抗性不超过上限 0.75")

	var zero_attr := {"str": 0, "con": 0, "agi": 0, "int": 0, "luk": 0}
	var bare: Dictionary = calculator.compute(1, zero_attr)
	check_eq(int(bare["atk_phys"]), 10, "属性为 0 时只剩等级基础值")
	check_eq(float(bare["hit_rate"]), 0.0, "属性为 0 时递减类派生数值为 0")
	check_eq(float(bare["res_internal"]), 0.0, "内伤抗性下限为 0")


func _check_level_growth(calculator) -> void:
	var level_20: Dictionary = calculator.compute(20, BASE_ATTRS)
	check_eq(int(level_20["hp_max"]), 252 + 12 * 5, "20 级基础气血 252 + 体 5 × 12")
	var row = get_db().get_row("level_growth", 20)
	check_not_null(row, "level_growth 有 20 级行")
	if row != null:
		check_eq(row.upgrade_points, 0, "满级不再给属性点")
