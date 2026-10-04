## 开局引导（设计 09 §3.1 → 0.22.0 设计 18 §3.2）：`guide_step` 的 6 步按剧情旗标自动推进。
##
## 这里**不写死**每一步的旗标名：直接从表里读 `condition` 再逐个点亮，
## 这样设计改旗标名／加一步都不会让用例变成"永远真"的空壳。
extends "res://tests/test_case.gd"

const GuideServiceScript := preload("res://src/core/guide_service.gd")


func suite_name() -> String:
	return "开局引导"


func run() -> void:
	var db = get_db()
	_check_steps_table(db)
	_check_progression(db)
	_check_hud_text(db)


## 表本身：6 步（0.22.0 由 4 步扩到 6 步，补上「进寨」与「救人复命」）、
## sort_order 从 1 连续、每步有文案与 condition_spec、第一步是 `start` 哨兵
func _check_steps_table(db) -> void:
	var rows: Array = GuideServiceScript.steps(db)
	check_eq(rows.size(), 6, "引导 6 步（guide_step 的行数；0.22.0 由 4 步扩到 6 步）")
	for index in range(rows.size()):
		var row: Resource = rows[index]
		check_eq(int(row.sort_order), index + 1, "第 %d 步的 sort_order 从 1 连续" % (index + 1))
		check_false(str(row.text_cn).is_empty(), "第 %d 步有给玩家看的文案" % (index + 1))
		check_false(str(row.condition).is_empty(), "第 %d 步写清了触发条件" % (index + 1))
	check_eq(str(rows[0].condition), GuideServiceScript.START_CONDITION, "第一步用 start 哨兵（开局就显示）")
	check_ne(str(rows[rows.size() - 1].condition), GuideServiceScript.START_CONDITION, "最后一步等一个真实旗标")


## 推进：新档停在第一步；按表把条件一个个点亮，当前目标就一步步往前走
func _check_progression(db) -> void:
	var state = solo_state(db)
	var rows: Array = GuideServiceScript.steps(db)
	var first: Dictionary = GuideServiceScript.current(db, state)
	check_eq(str(first["step_id"]), str(rows[0].step_id), "新档停在第一步")
	check_eq(int(first["index"]), 1, "第一步的序号是 1")

	# 无关旗标不推进（免得"点了个什么都往前跳"）
	state.set_flag("flag_not_in_guide")
	check_eq(str(GuideServiceScript.current(db, state)["step_id"]), str(rows[0].step_id), "无关旗标不推进")

	for index in range(1, rows.size()):
		var condition := str(rows[index].condition)
		state.set_flag(condition)
		var row: Dictionary = GuideServiceScript.current(db, state)
		check_eq(str(row["step_id"]), str(rows[index].step_id), "点亮 %s 后推进到第 %d 步" % [condition, index + 1])
		check_eq(int(row["index"]), index + 1, "第 %d 步的序号对得上" % (index + 1))

	# 反向：把最后一步的条件清掉，目标要退回去（旗标是唯一判据）
	state.set_flag(str(rows[rows.size() - 1].condition), false)
	check_eq(
		str(GuideServiceScript.current(db, state)["step_id"]), str(rows[rows.size() - 2].step_id),
		"清掉最后一步的旗标后退回上一步"
	)


## HUD 文案：带「当前目标」与进度分母；每一步的文案都来自表
func _check_hud_text(db) -> void:
	var state = solo_state(db)
	var text := GuideServiceScript.hud_text(db, state)
	var rows: Array = GuideServiceScript.steps(db)
	check_true(text.begins_with("当前目标："), "HUD 那一行以「当前目标：」开头：%s" % text)
	check_true(text.contains(str(rows[0].text_cn)), "文案取表里的第一步原文")
	check_true(text.contains("1/%d" % rows.size()), "带上进度（1/%d）：%s" % [rows.size(), text])
	state.set_flag(str(rows[rows.size() - 1].condition))
	var last_text := GuideServiceScript.hud_text(db, state)
	check_true(last_text.contains(str(rows[rows.size() - 1].text_cn)), "走到最后一步显示最后一步的文案")
	check_true(last_text.contains("%d/%d" % [rows.size(), rows.size()]), "进度显示满格：%s" % last_text)
	# 玩家可见的文案里不许出现 flag_ 形态的旗标 id（决策 242 的同一条纪律）
	check_false(last_text.contains("flag_"), "HUD 文案不出现旗标 id：%s" % last_text)
