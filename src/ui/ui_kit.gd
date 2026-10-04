## 界面共享构件：**卡片**（标题条 ＋ 副字 ＋ 内容列）。
##
## 为什么单开一份：策划的 12 屏示意图（`docs/dev/UI示意落地清单.md`）里，每一屏都是
## 「几张带标题条的卡片」（网页版的 `.lu-panel` ＋ `.lu-panel-h` ＋ `.lu-dim`），
## 而现在的浮层是裸 `Label` 堆成一列——没有卡片就没有信息层级，看上去就"不像示意图"。
## 卡片收在一处，**换皮肤只改 `assets/ui/theme_lunhui.tres` 里的 `UiKit/colors/*`**
## （15 §4.4 的色值只写在那儿，决策 375），代码里不再抄第二份色值。
##
## 跨文件调用走 `preload`（根 `AGENTS.md`）：
##     const UiKitScript := preload("res://src/ui/ui_kit.gd")
##     var body := UiKitScript.add_card(box, "战斗属性", "28 行")
##     body.add_child(...)
class_name UiKit
extends RefCounted

const THEME_PATH := "res://assets/ui/theme_lunhui.tres"

## 主题资源只读一次（每建一张卡都 `load()` 一次没必要；`ResourceLoader` 本来也缓存）
static var _theme_cache: Theme = null


## 卡片／标题条／小节的色值**统一从这里取**——色值表在主题资源里，不在代码里抄第二份
static func color(name: String) -> Color:
	var theme := _theme()
	if theme != null and theme.has_color(name, "UiKit"):
		return theme.get_color(name, "UiKit")
	push_error("[ui_kit] 主题 %s 里没有 UiKit/colors/%s" % [THEME_PATH, name])
	return Color.MAGENTA


## 一块卡片：外面是面板九宫格背板（`assets/ui/common/panel_bg.png`），
## 顶上一条**标题条**，返回**内容列**给调用方往里加行。
## `hint` 是标题后面那截次要字（示意图里的 `.lu-dim`，例：「心法占格　已占 13 / 14」）。
static func add_card(parent: Node, title: String, hint: String = "", node_name: String = "") -> VBoxContainer:
	var card := PanelContainer.new()
	card.name = node_name if not node_name.is_empty() else "Card"
	card.add_theme_stylebox_override("panel", _panel_style())
	var column := VBoxContainer.new()
	column.name = "CardColumn"
	column.add_theme_constant_override("separation", 4)
	card.add_child(column)
	if not title.is_empty():
		column.add_child(_header(title, hint))
	var body := VBoxContainer.new()
	body.name = "Body"
	body.add_theme_constant_override("separation", 4)
	column.add_child(body)
	parent.add_child(card)
	return body


## 把浮层的背板刷成宣纸（色值在主题 `UiKit/colors/backdrop` **一处**）。
##
## 面板过去是「比地图更沉」的近黑，8 个浮层各在 `.tscn`／代码里写一份 `Color(0.07,0.08,0.1,0.97)`
## ——皮肤一翻成宣纸，那 8 份色值就是散在八个文件里的旧口径。现在统一由这里刷：
## 浮层挂上屏幕时（`OverlayStack.mount()`）按名字找到 `Backdrop` 调一次。
## **刷不上不报错**：面板没有背板这件事 `LayoutBudget.has_opaque_backdrop()` 已经在盯着。
static func paint_backdrop(screen: Node) -> void:
	if screen == null:
		return
	var backdrop := screen.find_child("Backdrop", true, false) as ColorRect
	if backdrop == null:
		return
	backdrop.color = color("backdrop")


## 面板背板：主题里那件九宫格 `StyleBoxTexture`（15 §4.4 的 `panel_bg.png`）。
## 主题缺了它也不要红——退回一块同色底的方块，界面还能看。
static func _panel_style() -> StyleBox:
	var theme := _theme()
	if theme != null and theme.has_stylebox("panel", "PanelContainer"):
		return theme.get_stylebox("panel", "PanelContainer")
	var flat := StyleBoxFlat.new()
	flat.bg_color = Color(0.929412, 0.890196, 0.796078, 1)
	return flat


## 标题条：照示意图的 `.lu-panel-h`——**不是一条深色带**（深色带是旧皮肤的事），
## 而是「墨色标题 ＋ 右边一截次要字 ＋ 下面一道 1px 细线」。
## 标题走 `ink` 而不是 `gold`：15 §4.4 的枯黄在宣纸底上对比度只有 1.7：1，读不清。
static func _header(title: String, hint: String) -> VBoxContainer:
	var box := VBoxContainer.new()
	box.name = "Header"
	box.add_theme_constant_override("separation", 2)
	var row := HBoxContainer.new()
	row.name = "HeaderRow"
	row.add_theme_constant_override("separation", 8)
	var title_label := Label.new()
	title_label.name = "HeaderTitle"
	title_label.text = title
	title_label.add_theme_color_override("font_color", color("ink"))
	row.add_child(title_label)
	if not hint.is_empty():
		var hint_label := Label.new()
		hint_label.name = "HeaderHint"
		hint_label.text = hint
		hint_label.add_theme_color_override("font_color", color("ink_dim"))
		hint_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hint_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(hint_label)
	box.add_child(row)
	var rule := ColorRect.new()
	rule.name = "HeaderRule"
	rule.color = color("line")
	rule.custom_minimum_size = Vector2(0, 1)
	box.add_child(rule)
	return box


static func _theme() -> Theme:
	if _theme_cache == null:
		_theme_cache = load(THEME_PATH) as Theme
	return _theme_cache
