## 字体试排：把「中文像素字体在真实字号下长什么样」渲成一张 PNG（15 §3 的落地验收物）。
##
## 用法（**必须带窗口**，headless 抓不到字形渲染）：
##   "%GODOT_BIN%" --path . --log-file .logs\font_preview.log --quit-after 600 \
##       --script res://tools/artgen/font_preview.gd
##
## 为什么要有这一条：字体是整套界面的观感前提，而「12px 的汉字糊不糊」只能看，不能推。
## 这一页同时把 15 §4.4 的两档字号并排、与 UI 实际配色一起摆出来，
## 好让设计直接判「这版字形能不能用」——顺便把「字号是 12 的整数倍」这条纪律摆在眼前。

extends SceneTree

const FONT_ZH := "res://assets/fonts/ark-pixel-12px-proportional-zh_hans.ttf.woff2"
const FONT_LATIN := "res://assets/fonts/ark-pixel-12px-proportional-latin.ttf.woff2"
const OUT_PATH := "res://docs/dev/images/字体落地_试排.png"

## 15 §4.4 的配色，照样摆一遍——字形要跟真背景一起看才判得准。
const COLOR_PANEL := Color("1e2224")
const COLOR_BORDER := Color("6e6a63")
const COLOR_TITLE_BG := Color("2c3236")
const COLOR_BODY := Color("d8d2c4")
const COLOR_MINOR := Color("8a8578")
const COLOR_DISABLED := Color("5a5a55")
const COLOR_GOLD := Color("c8a24a")
const COLOR_RED := Color("a83a2e")

var _frames := 0
var _shot := false


func _initialize() -> void:
	var root := get_root()
	root.add_child(_build_page())


func _process(_delta: float) -> bool:
	# 等两帧：第一帧建完 UI，第二帧才拿得到排好版的视口
	_frames += 1
	if _frames < 3:
		return false
	if _shot:
		return true
	_shot = true
	var image := get_root().get_texture().get_image()
	var error := image.save_png(ProjectSettings.globalize_path(OUT_PATH))
	print("字体试排 → %s（%s，%dx%d）" % [
		OUT_PATH, "OK" if error == OK else "失败 %d" % error, image.get_width(), image.get_height(),
	])
	return true


func _build_page() -> Control:
	var page := Control.new()
	page.set_anchors_preset(Control.PRESET_FULL_RECT)
	var bg := ColorRect.new()
	bg.color = Color("14181a")
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	page.add_child(bg)

	var panel := PanelContainer.new()
	panel.position = Vector2(48, 36)
	panel.custom_minimum_size = Vector2(1056, 576)
	panel.add_theme_stylebox_override("panel", _panel_style())
	page.add_child(panel)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 8)
	panel.add_child(column)
	column.add_child(_title_bar("字体落地 · ark-pixel-font 12px（15 §3 / §4.4）"))

	# 正文 12px（原生）——这是 UI 里出现次数最多的一档
	column.add_child(_row("正文 12px（原生）",
		"清风驿有铁匠铺、药铺、客栈与校场；打坐可得气血与内力。"))
	column.add_child(_row("正文 12px ×2＝24px（显示档）",
		"黑风寨三层：前寨杂乱、聚义厅规整、后寨压抑；火把越少，位置越深。"))
	# 标题那一档：16px 原生在 2026.09 已被上游废弃，这里把两种落地方式都摆出来
	column.add_child(_row("标题方案 A：12px 字体 ×3＝36px",
		"石隙迷窟 · 一线天光", 36, COLOR_GOLD))
	column.add_child(_row("标题方案 B：12px 字体 ×2＝24px（只靠颜色区分）",
		"石隙迷窟 · 一线天光", 24, COLOR_GOLD))
	column.add_child(_row("非整数字号（禁止）：13px",
		"这条会糊：12px 原生的字形按 13px 画，笔画会宽窄不一。", 13, COLOR_MINOR))
	column.add_child(_row("拉丁与数字（同一套字体）",
		"Lv.12  气血 148/148  内力 32/32  A1 B2 C3  0.85x"))
	column.add_child(_row("三级文字层级（只能靠颜色）",
		"正文 · 次要文字 · 禁用文字", 12, COLOR_BODY))

	var note := Label.new()
	note.text = "说明：本页由 tools/artgen/font_preview.gd 在真窗口里渲染，字号全部为 12 的整数倍。"
	note.add_theme_font_size_override("font_size", 12)
	note.add_theme_color_override("font_color", COLOR_MINOR)
	column.add_child(note)
	return page


func _row(label_text: String, sample: String, size: int = 12, sample_color: Color = COLOR_BODY) -> VBoxContainer:
	var row := VBoxContainer.new()
	row.add_theme_constant_override("separation", 2)
	var caption := Label.new()
	caption.text = label_text
	caption.add_theme_font_size_override("font_size", 12)
	caption.add_theme_color_override("font_color", COLOR_MINOR)
	row.add_child(caption)
	var sample_label := Label.new()
	sample_label.text = sample
	sample_label.add_theme_font_size_override("font_size", size)
	sample_label.add_theme_color_override("font_color", sample_color)
	# 两档字体都显式指向那一份，免得「看到的是回退字体」还以为字体没生效
	var font: FontFile = load(FONT_ZH)
	if font != null:
		sample_label.add_theme_font_override("font", font)
	row.add_child(sample_label)
	return row


func _title_bar(text: String) -> Control:
	var bar := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = COLOR_TITLE_BG
	style.border_color = COLOR_BORDER
	style.set_border_width_all(1)
	style.content_margin_left = 8
	style.content_margin_right = 8
	style.content_margin_top = 6
	style.content_margin_bottom = 6
	bar.add_theme_stylebox_override("panel", style)
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", 24)
	label.add_theme_color_override("font_color", COLOR_BODY)
	var font: FontFile = load(FONT_ZH)
	if font != null:
		label.add_theme_font_override("font", font)
	bar.add_child(label)
	return bar


func _panel_style() -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = COLOR_PANEL
	style.border_color = COLOR_BORDER
	style.set_border_width_all(1)
	style.content_margin_left = 16
	style.content_margin_right = 16
	style.content_margin_top = 12
	style.content_margin_bottom = 12
	return style
