## 成长曲线求值。
##
## 公式写在代码里、系数写在表里（docs/design/06_配置表说明.md 的边界约定）：
##   linear       eff = rate * attr
##   diminishing  eff = cap * attr / (attr + param)   趋近 cap 但永不到达
##   piecewise    按传入的分段点线性插值
class_name CurveEvaluator
extends RefCounted

const CURVE_LINEAR := "linear"
const CURVE_DIMINISHING := "diminishing"
const CURVE_PIECEWISE := "piecewise"


## 求值。cap <= 0 视为无上限（表里留空即无上限）。
static func evaluate(
	curve: String,
	rate: float,
	attr_value: float,
	cap: float = 0.0,
	param: float = 0.0,
	points: PackedVector2Array = PackedVector2Array()
) -> float:
	var x := maxf(0.0, attr_value)
	var result := 0.0
	match curve:
		CURVE_DIMINISHING:
			if cap <= 0.0 or param <= 0.0:
				push_error("[CurveEvaluator] diminishing 需要 cap > 0 且 param > 0，退回线性：cap=%s param=%s" % [cap, param])
				result = rate * x
			else:
				result = cap * x / (x + param)
		CURVE_PIECEWISE:
			if points.size() < 2:
				push_error("[CurveEvaluator] piecewise 需要至少两个分段点，退回线性")
				result = rate * x
			else:
				result = _interpolate(points, x)
		_:
			result = rate * x
	if cap > 0.0 and result > cap:
		result = cap
	return result


static func _interpolate(points: PackedVector2Array, x: float) -> float:
	# PackedVector2Array 没有 sort_custom，先转成普通数组再按 x 排序
	var sorted: Array = []
	for point: Vector2 in points:
		sorted.append(point)
	sorted.sort_custom(func(a: Vector2, b: Vector2) -> bool: return a.x < b.x)
	if x <= sorted[0].x:
		return sorted[0].y
	var last: Vector2 = sorted[sorted.size() - 1]
	if x >= last.x:
		return last.y
	for i in range(sorted.size() - 1):
		var a: Vector2 = sorted[i]
		var b: Vector2 = sorted[i + 1]
		if x <= b.x:
			var span := b.x - a.x
			if span <= 0.0:
				return b.y
			var t := (x - a.x) / span
			return a.y + (b.y - a.y) * t
	return last.y
