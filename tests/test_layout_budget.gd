## 版式预算这个门限**自己**也要有反例。
##
## 由来（2026-10-03）：`LayoutBudget.fits()` 是十个场景自检 + 角色面板那群"版式塞得下"断言的
## **唯一裁判**——但它自己**一条反例都没有**（全项目只在 test_character_ui 里被正面调用过）。
## 把 `fits()` 改成 `return true`、或让 `measure_target()` 永远返回一个尺寸很小的节点，
## **所有"塞得下"的断言照样全绿**，而那意味着整页被裁掉也没人知道。
## 这跟决策 181 那句"自检直调 API 会绕过……"是同一家族：**门限的裁判要自己有条反例**。
##
## 这里用**合成的面板**（不依赖任何真实场景）把四个口径的正反面都钉住：
##   `fits`：太大 → false；两个方向各量一次；量不出来（没有 Margin/Column） → false（"量不出来 ≠ 塞得下"）；
##   `content_fits`：滚动内容超限 → false；没有滚动区 → false；
##   `has_opaque_backdrop`：背板太透 → false；没有背板 → false；
##   最后把 `ascii_line` 的判定与 `fits()` 对齐（那几行是给验收脚本 findstr 抓的，别自己说另一套）。
extends "res://tests/test_case.gd"

const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")


func suite_name() -> String:
	return "版式预算门限自身"


func run() -> void:
	_check_fits()
	_check_content()
	_check_backdrop()
	_check_clamp()


## 造一个"有可量节点"的合成面板：Control 里放一个叫 Margin 的 MarginContainer。
func _panel(size: Vector2) -> Control:
	var screen := Control.new()
	var margin := MarginContainer.new()
	margin.name = "Margin"
	margin.custom_minimum_size = size
	screen.add_child(margin)
	return screen


func _check_fits() -> void:
	# 基准分辨率是**设计文档写死的**（07 §1：1152×648）——上面那两条用 `DESIGN_WIDTH + 48` 造夹具，
	# 所以把常量本身改掉时它们会跟着放宽、**谁都不会红**（2026-10-03 的变异探针实测：
	# `DESIGN_WIDTH 1152→1280` 没被抓住）。这里钉绝对值，改基准分辨率必须是有意为之。
	check_eq(int(LayoutBudgetScript.DESIGN_WIDTH), 1152, "基准分辨率宽 = 1152（07 §1）")
	check_eq(int(LayoutBudgetScript.DESIGN_HEIGHT), 648, "基准分辨率高 = 648（07 §1）")
	var small := _panel(Vector2(400, 300))
	check_true(LayoutBudgetScript.fits(small), "400×300 的合成面板：塞得下")
	check_true(LayoutBudgetScript.ascii_line(small).contains("ok=true"), "LAYOUT 行与 fits() 同口径：%s" % LayoutBudgetScript.ascii_line(small))
	check_true(abs(LayoutBudgetScript.min_width(small) - 400.0) < 0.5, "量到的是可量节点的宽度（%.0f）" % LayoutBudgetScript.min_width(small))
	small.free()

	var too_wide := _panel(Vector2(float(LayoutBudgetScript.DESIGN_WIDTH) + 48.0, 300.0))
	check_false(LayoutBudgetScript.fits(too_wide), "宽出设计分辨率 48px：判不通过（只量高度的那种旧口径会漏掉）")
	check_true(LayoutBudgetScript.ascii_line(too_wide).contains("ok=false"), "LAYOUT 行如实写 ok=false：%s" % LayoutBudgetScript.ascii_line(too_wide))
	too_wide.free()

	var too_tall := _panel(Vector2(400.0, float(LayoutBudgetScript.DESIGN_HEIGHT) + 15.0))
	check_false(LayoutBudgetScript.fits(too_tall), "高出设计分辨率 15px：判不通过")
	too_tall.free()

	# 「量不出来 ≠ 塞得下」：没有任何 Margin/Column 的面板要判**失败**，不能默认通过
	var unmeasurable := Control.new()
	check_false(LayoutBudgetScript.fits(unmeasurable), "量不出来的面板判失败（量不出来 ≠ 塞得下）")
	check_false(LayoutBudgetScript.ascii_line(unmeasurable).contains("ok=true"), "量不出来时 LAYOUT 行也不许写 ok=true")
	unmeasurable.free()


func _check_content() -> void:
	var screen := Control.new()
	var scroll := ScrollContainer.new()
	screen.add_child(scroll)
	var content := Control.new()
	content.custom_minimum_size = Vector2(2000, 10)
	scroll.add_child(content)
	check_false(LayoutBudgetScript.content_fits(screen), "滚动内容 2000 > 上限 1152：判被裁")
	check_true(LayoutBudgetScript.content_fits(screen, 2500.0), "把上限放宽到 2500 就通过（上限是参数，不是写死的）")
	check_true(LayoutBudgetScript.ascii_content_line(screen).contains("ok=false"), "CONTENT 行与 content_fits() 同口径：%s" % LayoutBudgetScript.ascii_content_line(screen))
	screen.free()

	var no_scroll := Control.new()
	check_false(LayoutBudgetScript.content_fits(no_scroll), "连滚动区都没有：判失败（量不出来 ≠ 塞得下）")
	no_scroll.free()


func _check_backdrop() -> void:
	var thin := Control.new()
	var faint := ColorRect.new()
	faint.name = "Backdrop"
	faint.color = Color(0.0, 0.0, 0.0, 0.5)
	thin.add_child(faint)
	check_false(LayoutBudgetScript.has_opaque_backdrop(thin), "背板只有 0.5 不透明：判太透（地图会透上来把字糊掉）")
	thin.free()

	var ok_screen := Control.new()
	var solid := ColorRect.new()
	solid.name = "Backdrop"
	solid.color = Color(0.0, 0.0, 0.0, 0.97)
	ok_screen.add_child(solid)
	check_true(LayoutBudgetScript.has_opaque_backdrop(ok_screen), "背板 0.97：通过")
	ok_screen.free()

	var no_backdrop := Control.new()
	check_false(LayoutBudgetScript.has_opaque_backdrop(no_backdrop), "连背板都没有：判失败")
	no_backdrop.free()


## `clamp_into`（跳字落点用）：夹进画布、边界情况都不许把位置搞成 0。
func _check_clamp() -> void:
	var bounds := Vector2(1152, 648)
	var item := Vector2(60, 24)
	var inside := LayoutBudgetScript.clamp_into(bounds, Vector2(300, 200), item)
	check_eq(inside, Vector2(300, 200), "本来就在画布里的位置不动它")
	var pushed := LayoutBudgetScript.clamp_into(bounds, Vector2(1400, 700), item)
	check_eq(pushed, Vector2(1152 - 60, 648 - 24), "推出去的位置夹回画布内（含内容自身尺寸）")
	var negative := LayoutBudgetScript.clamp_into(bounds, Vector2(-40, -40), item)
	check_eq(negative, Vector2.ZERO, "跑到左上角外面的位置夹回 0")
	var unsized := LayoutBudgetScript.clamp_into(Vector2.ZERO, Vector2(300, 200), item)
	check_eq(unsized, Vector2(300, 200), "画布还没排版（尺寸为 0）时原样返回，不许夹成 0")
	var huge := LayoutBudgetScript.clamp_into(Vector2(100, 100), Vector2(50, 50), Vector2(400, 400))
	check_eq(huge, Vector2.ZERO, "内容比画布还大时退到左上角，不出现负数")
