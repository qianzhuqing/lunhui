## 界面约定（两条）：版式预算 + 背板。
##
## 1. **版式预算**：整页最小尺寸要**两个方向**都塞得进设计分辨率（1152×648）。界面「塞不下」肉眼很难发现——
##    内容只会被屏幕底部/右侧裁掉，代码一声不吭。战斗界面最早就是这么坏的（标题被裁掉半行，711 > 648）。
##    2026-10-03 补：以前只量**高度**——`DESIGN_WIDTH` 声明了却没人用过，横向膨胀（多一列、按钮变长、
##    敌人卡片加宽）不会被任何用例发现。现在 `fits()` 两个方向一起量，`LAYOUT` 行也把宽度打出来。
## 2. **背板**：这些面板是**盖在游戏画面上**的（Tab／按 E 打开），没有不透明背板就会让地图、
##    NPC 和地图自己的 HUD 透上来，把面板文字糊成一团（视觉自查抓到的真问题）。
## 3. **页签内容宽度**（2026-10-03 补）：面板里的滚动区都是**只滚竖向**（`horizontal_scroll_mode` 关着），
##    所以内容一宽就被裁掉，而外层量不出来——`get_combined_minimum_size()` 到 `ScrollContainer` 就断了，
##    量到的是面板外壳（角色面板那个 760 其实来自 `_tabs.custom_minimum_size` 这个写死的常量）。
##    这是「量不出来 ≠ 塞得下」的又一例：**外面全绿，里面照样被切掉半行**。
##    这条量的是滚动区里那个内容节点的最小宽度，调用方要当门限用。
##
## 这三条现在都是**绊线**：量过、全过，但以后谁改坏就会立刻红。
class_name LayoutBudget
extends RefCounted

const DESIGN_WIDTH := 1152
const DESIGN_HEIGHT := 648
## 背板至少要这么不透明，玩家的字才不会被场景糊掉
const BACKDROP_MIN_ALPHA := 0.9


## 量「整页最小高度」：优先 MarginContainer（面板的外边距容器），退而找第一个 Column。
## 找不到可量的节点返回 0——调用方要把它当成失败（量不出来 ≠ 塞得下）。
static func min_height(screen: Node) -> float:
	var target := measure_target(screen)
	if target == null:
		return 0.0
	return target.get_combined_minimum_size().y


## 量「整页最小宽度」——同一个可量节点的 x（见上：宽度以前从来没量过）
static func min_width(screen: Node) -> float:
	var target := measure_target(screen)
	if target == null:
		return 0.0
	return target.get_combined_minimum_size().x


static func measure_target(screen: Node) -> Control:
	if screen == null:
		return null
	for node: Node in screen.find_children("Margin", "MarginContainer", true, false):
		return node as Control
	for node: Node in screen.find_children("Column", "VBoxContainer", true, false):
		return node as Control
	return null


## 塞得下吗：**两个方向**都量得出来（>0）且不超设计分辨率才算过
static func fits(screen: Node) -> bool:
	var target := measure_target(screen)
	if target == null:
		return false
	var size: Vector2 = target.get_combined_minimum_size()
	return size.x > 0.0 and size.y > 0.0 \
		and size.x <= float(DESIGN_WIDTH) and size.y <= float(DESIGN_HEIGHT)


## 机器可读的一行：验收脚本用 `findstr LAYOUT` 抓它。
## 故意用 ASCII 而不用中文——cmd 的 findstr 按控制台代码页匹配，中文匹配不到（踩过）。
static func ascii_line(screen: Node, tag: String = "") -> String:
	var target := measure_target(screen)
	var size := Vector2.ZERO if target == null else target.get_combined_minimum_size()
	var label := "LAYOUT" if tag.is_empty() else "LAYOUT[%s]" % tag
	return "%s min_width=%.0f min_height=%.0f limit=%dx%d ok=%s" % [
		label, size.x, size.y, DESIGN_WIDTH, DESIGN_HEIGHT,
		size.x > 0.0 and size.y > 0.0 \
			and size.x <= float(DESIGN_WIDTH) and size.y <= float(DESIGN_HEIGHT),
	]


## 盖在游戏画面上的面板有没有够不透明的背板（`ColorRect` 名为 `Backdrop`）
static func has_opaque_backdrop(screen: Node, min_alpha: float = BACKDROP_MIN_ALPHA) -> bool:
	var backdrop := backdrop_of(screen)
	return backdrop != null and backdrop.color.a >= min_alpha


static func backdrop_of(screen: Node) -> ColorRect:
	if screen == null:
		return null
	var node: Node = screen.find_child("Backdrop", true, false)
	return node as ColorRect


## 同上：`findstr BACKDROP` 抓这一行
static func ascii_backdrop_line(screen: Node) -> String:
	var backdrop := backdrop_of(screen)
	var alpha := backdrop.color.a if backdrop != null else -1.0
	return "BACKDROP alpha=%.2f min=%.2f ok=%s" % [
		alpha, BACKDROP_MIN_ALPHA, backdrop != null and alpha >= BACKDROP_MIN_ALPHA,
	]


## 页签内容的最小宽度：滚动区是「只滚竖向」的，内容比面板宽就会被裁掉。
##
## 量法：取每个 `ScrollContainer` 里那个内容 Control 的 `get_combined_minimum_size().x`，取最大值。
## 一个滚动区都没有（或里面没有 Control）返回 0——**调用方要把它当成失败**
## （同 `min_height` 的口径：量不出来 ≠ 塞得下）。
##
## **小值不代表没量到**：带 `autowrap` 的 Label 最小宽度只有 1 个字形（实测 224px 的句子 → 最小宽度 1px），
## 因为长文本会换行、不会被横向裁掉——那是安全的。这条量的是**不换行的内容**：
## 按钮、固定宽卡片、显式设过 `custom_minimum_size` 的行（这几类宽了才是真会被裁）。
static func content_min_width(screen: Node) -> float:
	if screen == null:
		return 0.0
	var widest := 0.0
	for node: Node in screen.find_children("*", "ScrollContainer", true, false):
		var container := node as ScrollContainer
		if container == null:
			continue
		for child: Node in container.get_children():
			var content := child as Control
			if content == null:
				continue
			widest = maxf(widest, content.get_combined_minimum_size().x)
			break
	return widest


## 内容塞得进吗：量得出来（> 0）且不超过面板可用宽度。
##
## 全屏面板的可用宽度是 `DESIGN_WIDTH` 减掉左右边距，这里取设计宽度这个**上限**（宁松勿假绿）：
## 连 1152 都超的内容一定是被裁掉的，先让这一层红，再按面板实际边距收紧。
static func content_fits(screen: Node, limit: float = float(DESIGN_WIDTH)) -> bool:
	var width := content_min_width(screen)
	return width > 0.0 and width <= limit


## 机器可读的一行：验收脚本用 `findstr CONTENT` 抓它（ASCII 的理由同上）
static func ascii_content_line(screen: Node, limit: float = float(DESIGN_WIDTH)) -> String:
	var width := content_min_width(screen)
	return "CONTENT min_width=%.0f limit=%.0f ok=%s" % [
		width, limit, width > 0.0 and width <= limit,
	]


## 把一块内容（左上角 `point`、尺寸 `item`）夹进 `bounds` 画布里，返回夹好之后的左上角。
##
## 为什么要有它：跳字（伤害数字）是按「卡片位置 + 固定偏移」摆的，而卡片靠右时那个偏移会把数字
## **推出屏幕**——满招式那档的版式预算实测内容宽已经到 1134，离设计宽度 1152 只剩 18px。
## 夹一下的代价是"数字可能压在卡片上"，但比"数字看不见"好。
## `bounds` 非正（还没排版，headless 里常见）时原样返回，别把位置夹成 0。
static func clamp_into(bounds: Vector2, point: Vector2, item: Vector2) -> Vector2:
	if bounds.x <= 0.0 or bounds.y <= 0.0:
		return point
	return Vector2(
		clampf(point.x, 0.0, maxf(0.0, bounds.x - item.x)),
		clampf(point.y, 0.0, maxf(0.0, bounds.y - item.y)),
	)
