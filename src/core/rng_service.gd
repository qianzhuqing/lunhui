## 可复现的随机源。
##
## 战斗、掉落的所有随机都必须走这里，测试靠固定种子拿到确定结果。
class_name RngService
extends RefCounted

var _rng := RandomNumberGenerator.new()


## seed_value 传 -1 表示随机；传其它值则固定种子（可复现）。
func _init(seed_value: int = -1) -> void:
	set_seed(seed_value)


func set_seed(seed_value: int) -> void:
	if seed_value < 0:
		_rng.randomize()
	else:
		_rng.seed = seed_value


func get_seed() -> int:
	return int(_rng.seed)


func randf() -> float:
	return _rng.randf()


func randf_range(from: float, to: float) -> float:
	return _rng.randf_range(from, to)


func randi_range(from: int, to: int) -> int:
	return _rng.randi_range(from, to)


## 以概率 p 判定：p <= 0 恒 false，p >= 1 恒 true。
func chance(p: float) -> bool:
	if p <= 0.0:
		return false
	if p >= 1.0:
		return true
	return _rng.randf() < p


## 档位抽取：按权重从 entries（[{"key": Variant, "weight": float}, ...]）里取一个。
## 权重全为 0 或列表为空时返回 null。
func pick_weighted(entries: Array) -> Variant:
	var total := 0.0
	for entry: Dictionary in entries:
		total += maxf(0.0, float(entry.get("weight", 0.0)))
	if total <= 0.0:
		return null
	var roll := _rng.randf() * total
	var cursor := 0.0
	for entry: Dictionary in entries:
		cursor += maxf(0.0, float(entry.get("weight", 0.0)))
		if roll < cursor:
			return entry.get("key")
	return entries[entries.size() - 1].get("key")
