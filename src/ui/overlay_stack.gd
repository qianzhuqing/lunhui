## 浮层栈（设计 18.1，0.18.1）：浮层改「栈」管理，不再是「同时只开一个」。
##
## 规则（14 号文档「已定口径」一节）：
##   · 打开某浮层 → 压栈
##   · 目标**已在栈里** → **弹回到它**，不重复压（A→B→A 就是回到 A，不会越点越深）
##   · `Esc` → 弹一层；栈空则回场景
##   · 栈深上限 3，达上限再压则**挤掉栈底**
##
## 这里只做**栈本身**（纯逻辑、可单测）：谁被挤掉、谁被弹掉由返回值告诉调用方，
## 由调用方去关对应的界面。**压栈时重新读状态**那条纪律不在这一层——
## 它属于各浮层的 `refresh()`，调用方在压栈后必须调一次。
class_name OverlayStack
extends RefCounted

## 栈深上限（设计 18.1 定死 3）
const LIMIT := 3

var _ids: Array[String] = []


func ids() -> Array[String]:
	return _ids.duplicate()


func depth() -> int:
	return _ids.size()


func is_empty() -> bool:
	return _ids.is_empty()


func has(id: String) -> bool:
	return _ids.has(id)


func top() -> String:
	return _ids[_ids.size() - 1] if not _ids.is_empty() else ""


## 压栈。返回**需要被关掉的浮层**（可能不止一个）：
##   · 目标已在栈里 → 它上面的全部（「弹回到它」）
##   · 栈已满 → 栈底那一个（「挤掉栈底」）
## 目标自己不在返回里（它要留着当栈顶）。
func push(id: String) -> Array:
	if id.is_empty():
		return []
	if _ids.has(id):
		return pop_to(id)
	_ids.append(id)
	var closed: Array = []
	while _ids.size() > LIMIT:
		closed.append(_ids.pop_front())
	return closed


## 弹一层。返回被弹掉的 id（栈空返回空串）。
func pop() -> String:
	if _ids.is_empty():
		return ""
	return _ids.pop_back()


## 弹到指定浮层：把它**上面**的全部弹掉（自己留在栈顶）。返回被弹掉的。
func pop_to(id: String) -> Array:
	var closed: Array = []
	if not _ids.has(id):
		return closed
	while not _ids.is_empty() and _ids[_ids.size() - 1] != id:
		closed.append(_ids.pop_back())
	return closed


## 程序化关闭（例：界面自己的「返回」按钮）：从栈里摘掉。
## 返回它**上面**被一起关掉的浮层（与 `pop_to` 同一口径：关掉一个浮层时，
## 压在它上面的也得跟着走，否则栈里会留下已经不在屏幕上的幽灵）。
func remove(id: String) -> Array:
	var closed: Array = pop_to(id)
	if not _ids.is_empty() and _ids[_ids.size() - 1] == id:
		_ids.pop_back()
	return closed


## 清空（切场景/回图时用）。返回原本栈里的全部。
func clear() -> Array:
	var out: Array = _ids.duplicate()
	_ids.clear()
	return out
