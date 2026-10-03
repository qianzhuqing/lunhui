## 掉落保底计数器。
##
## 计数单位是「掉落组 + 掉落槽」（pity_count 配在槽上，所以按槽计数最不容易含糊）。
## to_dict / from_dict 是跨难度继承的落点：切换难度时不要清空，直接带着走。
## 跨周目继承随多周目一起暂缓（docs/design/CHANGELOG.md 0.4.1）：接口留着，暂不启用。
class_name PityTracker
extends RefCounted

const VERSION := 1

var _counters: Dictionary = {}


## 记一次未出货，返回累计次数。
func register_attempt(key: String) -> int:
	var count: int = int(_counters.get(key, 0)) + 1
	_counters[key] = count
	return count


func attempts(key: String) -> int:
	return int(_counters.get(key, 0))


## 是否已达保底（达到即本次强制掉落）。
func should_force(key: String, threshold: int) -> bool:
	if threshold <= 0:
		return false
	return attempts(key) >= threshold


## 出货后清零。
func reset(key: String) -> void:
	_counters.erase(key)


func clear() -> void:
	_counters.clear()


func size() -> int:
	return _counters.size()


func to_dict() -> Dictionary:
	return {"version": VERSION, "counters": _counters.duplicate(true)}


func from_dict(data: Dictionary) -> void:
	_counters = {}
	var raw: Dictionary = data.get("counters", {})
	for key: Variant in raw:
		_counters[str(key)] = int(raw[key])
