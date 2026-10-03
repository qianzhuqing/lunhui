## 打坐修炼界面（客栈／门派）。
##
## 骨架在 `scenes/cultivate_screen.tscn` 里（节点名即接口），规则与记账在 `src/core/mastery_service.gd`：
## 一次打坐 = 熟练度 +1，费用 = `skill_star_def.cultivate_cost_base × growth_const.cultivate_cost_growth^(熟练度-1)`。
##
## 入口：清风驿的 `Markers/Facilities/facility_inn` 旁边按 E。
## 自检：`-- --cultivate-selftest`（打坐一次 → 扣钱 → 熟练度 +1 → 倍率上升，再看满级与缺钱的拒绝）。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const MasteryServiceScript := preload("res://src/core/mastery_service.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
## 打坐的余韵是 08 的**战斗外增益**（`buff_def` 里 `scope=field`、`field_minutes=10`）
const BuffServiceScript := preload("res://src/core/buff_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

var state_override = null
## 用例注入的表库（要在真场景里跑「4 人队伍」这种发行数据用不到的配置）
var db_override = null
var return_handler := Callable()
## 当前在给谁打坐（多人队伍时切换）
var selected_char: String = ""

var db
var state
var mastery
var _growth

var _title: Label
var _money: Label
var _members: HBoxContainer
var _list: VBoxContainer
var _status: Label
var _return_button: Button
var _bound := false


func _ready() -> void:
	setup()
	if _has_user_arg("--cultivate-selftest"):
		call_deferred("_run_cultivate_selftest")


func setup() -> void:
	if mastery != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	mastery = MasteryServiceScript.new(db, state)
	_growth = GrowthCalculatorScript.new(db)
	if selected_char.is_empty() and not state.char_ids.is_empty():
		selected_char = str(state.char_ids[0])
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


func status_text() -> String:
	return _status.text if _status != null else ""


func money_text() -> String:
	return _money.text if _money != null else ""


func char_name() -> String:
	return state.char_name(db, selected_char) if state != null else ""


# ------------------------------------------------------------------ 界面

func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_money = _require_node("Panel/Margin/Column/Money") as Label
	_members = _require_node("Panel/Margin/Column/Members") as HBoxContainer
	_list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_return_button.pressed.connect(press_return)
	_status.text = ""


func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("cultivate_screen.tscn 缺少节点：%s" % path)
		assert(false, "cultivate_screen.tscn 缺少节点：%s" % path)
	return node


func select_char(char_id: String) -> void:
	selected_char = char_id
	refresh()


func press_return() -> void:
	if return_handler.is_valid():
		return_handler.call()
		return
	queue_free()


func show_message(text: String) -> void:
	_set_status(text)


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


func refresh() -> void:
	if mastery == null or _list == null:
		return
	_title.text = "打坐修炼　（%s）" % char_name()
	_money.text = "铜钱 %d　｜　一次打坐 = 熟练度 +1（上限 %d）" % [
		int(state.inventory.money), mastery.mastery_max(),
	]
	_rebuild_members()
	_clear(_list)
	var learned: PackedStringArray = state.learned_of(selected_char)
	if learned.is_empty():
		_list.add_child(_make_label("Empty", "还没学会任何武学"))
		return
	var shown := 0
	for skill_id: String in learned:
		var row: Resource = db.get_row("skill_base", skill_id)
		if row == null:
			continue
		shown += 1
		var check: Dictionary = mastery.can_train(selected_char, skill_id)
		var star: Resource = _growth.star_row(int(row.star))
		var line := HBoxContainer.new()
		line.name = "TrainRow%s" % skill_id
		line.add_theme_constant_override("separation", 8)
		var text := "%s（%s★ %s）　熟练 %d/%d　倍率 ×%.2f　打坐 %d 文" % [
			str(row.name_cn), str(star.name_cn) if star != null else str(row.star),
			"招式" if row.is_active() else "内功",
			int(check["mastery"]), int(check["max"]),
			_growth.mastery_multiplier(int(row.star), int(check["mastery"])),
			int(check["cost"]),
		]
		if not bool(check["ok"]):
			text += "　— %s" % str(check["error"])
		var label := _make_label("TrainLabel%s" % skill_id, text, 0, str(star.color) if star != null else "")
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		line.add_child(_make_button(
			"TrainButton%s" % skill_id,
			"打坐",
			func() -> void: press_train(skill_id),
			not bool(check["ok"]),
		))
		_list.add_child(line)
	if shown == 0:
		_list.add_child(_make_label("Empty", "已学武学都查不到表数据"))


func _rebuild_members() -> void:
	_clear(_members)
	for char_id: String in state.char_ids:
		var name: String = state.char_name(db, char_id)
		var prefix := "▶ " if char_id == selected_char else ""
		_members.add_child(_make_button(
			"CultivateMemberButton%s" % char_id,
			"%s%s" % [prefix, name],
			func() -> void: select_char(char_id),
		))


func _clear(box: Node) -> void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()


func _make_label(node_name: String, text: String, font_size: int = 0, color: String = "") -> Label:
	var label := Label.new()
	label.name = node_name
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if font_size > 0:
		label.add_theme_font_size_override("font_size", font_size)
	if not color.is_empty():
		label.add_theme_color_override("font_color", Color(color))
	return label


func _make_button(node_name: String, text: String, pressed: Callable, disabled: bool = false) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.disabled = disabled
	button.pressed.connect(pressed)
	return button


# ------------------------------------------------------------------ 操作

func press_train(skill_id: String) -> Dictionary:
	var result: Dictionary = mastery.train(selected_char, skill_id)
	if bool(result["ok"]):
		_set_status("打坐：%s 熟练度 %d → %d（花 %d 文）　倍率 ×%.2f%s" % [
			mastery.skill_name(skill_id), int(result["before"]), int(result["mastery"]),
			int(result["cost"]), _multiplier(skill_id, int(result["mastery"])),
			_grant_meditation_buff(),
		])
	else:
		_set_status("打坐失败：%s" % str(result["error"]))
	refresh()
	return result


## 打坐的余韵（08：`buff_meditated` 是**战斗外增益**，按现实分钟计时；进战斗后维持整场）。
## 时长从 `buff_def.field_minutes` 读（不写死在这里），返回给玩家看的一段文案；
## 没开会话（用例／独立场景）就跳过——那是环境问题，不该让打坐失败。
func _grant_meditation_buff() -> String:
	var session_node := _session_node()
	if session_node == null or not session_node.has_method("add_field_buff"):
		return ""
	var service = BuffServiceScript.new(db)
	var minutes: int = service.field_minutes_of("buff_meditated")
	if minutes <= 0:
		return ""
	var applied: Dictionary = session_node.add_field_buff("buff_meditated", minutes)
	if not bool(applied.get("ok", false)):
		return ""
	var row: Resource = db.get_row("buff_def", "buff_meditated")
	var name_cn := str(row.name_cn) if row != null else "打坐余韵"
	return "　余韵：%s %d 分钟（战斗外）" % [name_cn, minutes]


func _multiplier(skill_id: String, level: int) -> float:
	return _growth.mastery_multiplier(mastery.star_of(skill_id), level)


# ------------------------------------------------------------------ 环境

func _resolve_db():
	if db_override != null:
		return db_override
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String = "GameSession") -> Node:
	var tree := _tree()
	if tree == null:
		return null
	return tree.root.get_node_or_null(node_name)


func _tree() -> SceneTree:
	# 不在场景树里时 get_tree() 会打一条 ERROR（--script 模式用例直接 new 节点就会踩到）
	if is_inside_tree():
		var tree := get_tree()
		if tree != null:
			return tree
	return Engine.get_main_loop() as SceneTree


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 自检

func _run_cultivate_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	state.inventory.money = 1000
	refresh()
	var skill_id := ""
	for candidate: String in state.learned_of(selected_char):
		if db.get_row("skill_base", candidate) != null:
			skill_id = candidate
			break
	ok = ok and not skill_id.is_empty()
	if skill_id.is_empty():
		lines.append("没有可打坐的武学")
	var before: int = mastery.mastery_of(selected_char, skill_id)
	var cost: int = mastery.train_cost(selected_char, skill_id)
	var money_before := int(state.inventory.money)

	# 1. 打坐一次：扣钱、熟练度 +1、倍率上升
	var result: Dictionary = press_train(skill_id)
	var after: int = mastery.mastery_of(selected_char, skill_id)
	var trained_ok: bool = (
		bool(result["ok"]) and after == before + 1
		and int(state.inventory.money) == money_before - cost
	)
	ok = ok and trained_ok
	lines.append("打坐 ok=%s（%s 熟练 %d → %d，花 %d 文，倍率 ×%.2f）" % [
		trained_ok, mastery.skill_name(skill_id), before, after, cost,
		_multiplier(skill_id, after),
	])
	# 打坐余韵（08）：打坐成功要真的挂上战斗外增益，时长来自 buff_def.field_minutes
	var session_node := _session_node()
	var field_rows: Array = session_node.active_field_buffs() if session_node != null else []
	var buff_row = BuffServiceScript.new(db).def_of("buff_meditated")
	var expect_minutes: int = int(buff_row.field_minutes) if buff_row != null else 0
	var field_ok: bool = (
		field_rows.size() == 1
		and str(field_rows[0]["buff_id"]) == "buff_meditated"
		and int(field_rows[0]["remaining_sec"]) > (expect_minutes - 1) * 60
	)
	ok = ok and field_ok
	lines.append("打坐余韵挂上=%s（%s，剩 %d 分钟）" % [
		field_ok, str(buff_row.name_cn) if buff_row != null else "-",
		int(field_rows[0]["remaining_sec"] / 60) if field_rows.size() > 0 else 0,
	])

	# 2. 缺钱要拒绝
	state.inventory.money = 0
	var poor: Dictionary = press_train(skill_id)
	ok = ok and not bool(poor["ok"]) and str(poor["error"]).contains("铜钱")
	lines.append("缺钱拒绝 ok=%s（%s）" % [not bool(poor["ok"]), str(poor["error"])])
	# 按钮态：不能打坐的时候要**置灰**，而不是「能点、点了才报错」
	var poor_button: Button = find_child("TrainButton%s" % skill_id, true, false)
	ok = ok and poor_button != null and poor_button.disabled
	lines.append("缺钱时按钮置灰=%s" % (poor_button != null and poor_button.disabled))
	state.inventory.money = 1000

	# 3. 练满之后拒绝
	state.set_mastery(selected_char, skill_id, mastery.mastery_max(), mastery.mastery_max())
	refresh()
	var maxed: Dictionary = press_train(skill_id)
	ok = ok and not bool(maxed["ok"]) and str(maxed["error"]).contains("练满")
	lines.append("练满拒绝 ok=%s（%s）" % [not bool(maxed["ok"]), str(maxed["error"])])
	var maxed_button: Button = find_child("TrainButton%s" % skill_id, true, false)
	var maxed_label: Label = find_child("TrainLabel%s" % skill_id, true, false)
	var maxed_ui: bool = (
		maxed_button != null and maxed_button.disabled
		and maxed_label != null and maxed_label.text.contains("练满")
	)
	ok = ok and maxed_ui
	lines.append("练满时按钮置灰且行里写「练满」=%s" % maxed_ui)

	# 版式预算：整页最小高度要塞得进设计分辨率
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 滚动区内容宽度：横滚是关着的，一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
	ok = ok and LayoutBudgetScript.content_fits(self)
	lines.append(LayoutBudgetScript.ascii_content_line(self))
	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	for line: String in lines:
		print("  " + line)
	print("CULTIVATE SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
