## 副本完成度界面：四项完成度、楼层列表、扫荡按钮（真实场景）。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")

const UI_SCENE := "res://scenes/dungeon_screen.tscn"
const SCENE := "scene_heifengzhai"


func suite_name() -> String:
	return "副本完成度界面"


func run() -> void:
	if scene_tree == null:
		fail("没有注入场景树，界面用例无法进行")
		return
	var db = get_db()
	var state = solo_state(db)
	var panel = load(UI_SCENE).instantiate()
	panel.state_override = state
	panel.scene_id = SCENE
	var closes: Array = []
	panel.return_handler = func() -> void: closes.append(true)
	scene_tree.root.add_child(panel)
	panel.setup()

	_check_summary(panel)
	_check_floors(panel, state)
	_check_sweep(panel, state)
	_check_close(panel, closes)

	scene_tree.root.remove_child(panel)
	panel.free()
	_check_long_floor_list(db, state)


## 几十层的长列表：完成度面板是「一层一行」，夹具把第 1 层复制成 30 层，
## 面板要**逐层建行**（30 个 FloorRow），不许截断或只画前几层——真实数据只有 3 层，
## 「列表被截断」在界面上看起来只是「少了几层」，很难被发现。
func _check_long_floor_list(db, state) -> void:
	var wide = table_with_many_floors(db, SCENE, 30)
	var panel = load(UI_SCENE).instantiate()
	panel.db_override = wide
	panel.state_override = state
	panel.scene_id = SCENE
	scene_tree.root.add_child(panel)
	panel.setup()
	var rows := 0
	for node: Node in panel.find_children("FloorRow*", "HBoxContainer", true, false):
		rows += 1
	check_eq(rows, 30, "30 层都建了行（实际 %d）" % rows)
	check_not_null(panel.find_child("FloorRow30", true, false), "第 30 层的行在（没被截断）")
	check_not_null(panel.find_child("FloorRow3", true, false), "第 3 层的行也在")
	scene_tree.root.remove_child(panel)
	panel.free()




## 四项完成度与百分比
func _check_summary(panel) -> void:
	check_true(panel.summary_text().contains("总计 0%"), "新档完成度 0%%：%s" % panel.summary_text())
	var detail: String = panel.detail_text()
	check_true(detail.contains("宝箱 0/4"), "宝箱分母来自表：%s" % detail)
	check_true(detail.contains("隐藏房间 0/5"), "隐藏房间分母：%s" % detail)
	check_true(detail.contains("隐藏 Boss 0/1"), "隐藏 Boss 分母（只算醉刀客）：%s" % detail)
	check_true(detail.contains("事件 0/2"), "事件分母：%s" % detail)


## 楼层列表：每层的房间数、清怪进度、能不能扫荡
func _check_floors(panel, state) -> void:
	check_eq(panel.floor_rows().size(), 3, "黑风寨三层都在列表里")
	var floor1: Label = panel.find_child("FloorLabel1", true, false)
	check_not_null(floor1, "第 1 层有一行")
	if floor1 != null:
		check_true(floor1.text.contains("已清 0"), "还没打就写着已清 0：%s" % floor1.text)
		check_true(floor1.text.contains("还差"), "没通关的层写清还差哪些房间：%s" % floor1.text)
	var sweep1: Button = panel.find_child("SweepButton1", true, false)
	check_not_null(sweep1, "第 1 层有扫荡按钮")
	if sweep1 != null:
		check_true(sweep1.disabled, "没通关时扫荡按钮置灰")

	# 打通第 1 层（前院）后：可扫荡，完成度不变（房间清怪不算完成度四项）
	state.record_dungeon(SCENE, "rooms", "hf1_yard")
	panel.refresh()
	var after: Button = panel.find_child("SweepButton1", true, false)
	check_false(after.disabled, "通关后扫荡按钮可点")
	var label: Label = panel.find_child("FloorLabel1", true, false)
	check_true(label.text.contains("可扫荡"), "行里标出可扫荡：%s" % label.text)


## 扫荡按钮：走调用方的 handler，并把结果播在状态栏
func _check_sweep(panel, state) -> void:
	var calls: Array = []
	panel.sweep_handler = func(floor_number: int) -> Dictionary:
		calls.append(floor_number)
		return {"ok": true, "summary": "铜钱 +9　物品 兽皮×1", "floor": floor_number}
	var button: Button = panel.find_child("SweepButton1", true, false)
	if button != null:
		button.emit_signal("pressed")
	check_eq(calls.size(), 1, "点扫荡会通知调用方一次")
	check_eq(int(calls[0]) if not calls.is_empty() else -1, 1, "扫的是第 1 层")
	# 扫荡动画：进度条推进、逐间打勾、按钮防连点；掉落其实按下时就结算了
	check_true(panel.is_sweeping(), "按下扫荡进入动画状态")
	check_not_null(panel.find_child("SweepProgress", true, false), "有扫荡进度条")
	check_lt(panel.sweep_progress(), 1.0, "刚按下进度还没满")
	check_true(button != null and button.disabled, "动画期间扫荡按钮置灰（防连点）")
	panel.step_sweep_animation(0.15)
	check_gt(panel.sweep_progress(), 0.0, "推进后进度在涨（%.2f）" % panel.sweep_progress())
	panel.step_sweep_animation(0.2)
	check_gt(float(panel.sweep_ticked_rooms().size()), 0.0, "过半时至少打勾一间（进度 %.2f）" % panel.sweep_progress())
	var sweeping_row: Label = panel.find_child("FloorLabel1", true, false)
	check_true(
		sweeping_row != null and sweeping_row.text.contains("✓"),
		"扫荡时逐间打勾：%s" % (sweeping_row.text if sweeping_row != null else "(没有行)"),
	)
	panel.step_sweep_animation(1.0)
	check_false(panel.is_sweeping(), "动画走完")
	check_true(panel.status_text().contains("铜钱 +9"), "状态栏播报扫荡结果：%s" % panel.status_text())
	check_true(panel.find_child("SweepProgress", true, false) == null, "动画结束后进度条收走")
	var after_row: Button = panel.find_child("SweepButton1", true, false)
	check_true(after_row != null and not after_row.disabled, "动画结束后按钮恢复可点")
	var other_row: Button = panel.find_child("SweepButton2", true, false)
	check_true(
		other_row != null and other_row.disabled,
		"动画结束后没通关的层仍然是灰的（别一律打开）",
	)
	# 没有 handler（比如误开在城镇）时给明确提示
	panel.sweep_handler = Callable()
	panel.press_sweep(2)
	check_true(panel.status_text().contains("不能扫荡"), "没有落点时说明原因：%s" % panel.status_text())


func _check_close(panel, closes: Array) -> void:
	panel.press_return()
	check_eq(closes.size(), 1, "离开按钮触发回退")
