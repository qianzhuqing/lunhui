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

const UiKitScript := preload("res://src/ui/ui_kit.gd")

## 栈深上限（设计 18.1 定死 3）
const LIMIT := 3

## 浮层画布层（与 `shop_screen.tscn` 的 `layer = 5` 对齐）：压在世界与 HUD 之上。
const OVERLAY_LAYER := 5

var _ids: Array[String] = []


## 把一张浮层场景挂到**画布层**上，返回它自己。
##
## **为什么必须有这一步**（2026-10-04 实测的“面板打不开”）：`npc_panel.tscn` 与
## `character_screen.tscn` 的根是 `Control`，直接 `add_child` 到 `Node2D` 场景根下会
## **跟着相机平移**——面板明明开了，玩家在屏幕上看不到（`local_map_controller` 里
## `_hud_layer()` 那句注释就是同一个坑的记录：HUD 当年也是这么跑出屏幕的）。
## `shop／cultivate／clue／dungeon／waypoint` 几张场景的根本来就是 `CanvasLayer`，
## 所以它们一直没露；这里统一收口：是 `CanvasLayer` 就照旧挂，是 `Control`（或别的
## CanvasItem）就包一层再挂——**新加浮层不用再记这条**。
##
## 包出来的那层跟着面板一起回收（面板离开场景树时收掉空画布），
## 所以调用方的 `panel.queue_free()` 不用改。
static func mount(host: Node, panel: Node, layer: int = OVERLAY_LAYER) -> Node:
	if panel is CanvasLayer:
		host.add_child(panel)
		UiKitScript.paint_backdrop(panel)
		return panel
	var canvas := CanvasLayer.new()
	canvas.name = "%sLayer" % panel.name
	canvas.layer = layer
	host.add_child(canvas)
	canvas.add_child(panel)
	panel.tree_exited.connect(func() -> void:
		if is_instance_valid(canvas):
			canvas.queue_free()
	)
	# 背板统一刷成宣纸（色值在主题里一处，见 `UiKit.paint_backdrop`）。
	# 放在 `add_child` 之后：`_ready` 已经跑完，**代码里建背板的面板**这时也建好了。
	UiKitScript.paint_backdrop(panel)
	return panel


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
