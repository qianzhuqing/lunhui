## 驿站界面：快速传送 + 切换难度（03_副本_黑风寨.md：难度可在大地图任意驿站切换）。
##
## 骨架在 `scenes/waypoint_screen.tscn`（节点名即接口），规则在 `src/core/world_map_service.gd`。
## 入口：大地图上走到 `Markers/Node_n_post_station`（驿站）旁边按 E。
## 自检：`-- --waypoint-selftest`（推进到驿站 → 传送目标 → 切难度 → 显示条件未达成的原因）。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

var state_override = null
var return_handler := Callable()
## 传送落点：调用方（大地图控制器）接过去切场景；没接就只写会话里的待进入场景
var travel_handler := Callable()
## 当前所在小地图（用于把「你在这里」那一项灰掉）
var current_scene_id: String = ""

var db
var state
var world_map

var _title: Label
var _progress: Label
var _list: VBoxContainer
var _status: Label
var _return_button: Button
var _bound := false


func _ready() -> void:
	setup()
	if _has_user_arg("--waypoint-selftest"):
		call_deferred("_run_waypoint_selftest")


func setup() -> void:
	if world_map != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	world_map = WorldMapServiceScript.new(db, state)
	world_map.apply_initial_reveals()
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


func status_text() -> String:
	return _status.text if _status != null else ""


func progress_text() -> String:
	return _progress.text if _progress != null else ""


func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_progress = _require_node("Panel/Margin/Column/Progress") as Label
	_list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_return_button.pressed.connect(press_return)
	_status.text = ""


func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("waypoint_screen.tscn 缺少节点：%s" % path)
		assert(false, "waypoint_screen.tscn 缺少节点：%s" % path)
	return node


func press_return() -> void:
	if return_handler.is_valid():
		return_handler.call()
		return
	queue_free()


func show_message(text: String) -> void:
	if _status != null:
		_status.text = text


func refresh() -> void:
	if world_map == null or _list == null:
		return
	_title.text = "驿站　（传送与难度切换）"
	_progress.text = world_map.progress_text()
	_clear(_list)
	# 传送目标
	_list.add_child(_make_label("TravelHeader", "去哪里", 18))
	var targets: Array = world_map.travel_targets(current_scene_id)
	if targets.is_empty():
		_list.add_child(_make_label("NoTravel", "还没有探索到别的地方（走远一点，地图是自己走出来的）"))
	for entry: Dictionary in targets:
		var node_id := str(entry["node_id"])
		var line := HBoxContainer.new()
		line.name = "TravelRow%s" % node_id
		line.add_theme_constant_override("separation", 8)
		var label := _make_label(
			"TravelLabel%s" % node_id,
			"%s%s" % [str(entry["name"]), "（你在这里）" if bool(entry["current"]) else ""],
		)
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		line.add_child(_make_button(
			"TravelButton%s" % node_id, "传送", func() -> void: press_travel(node_id), bool(entry["current"])
		))
		_list.add_child(line)
	# 难度
	_list.add_child(_make_label("DifficultyHeader", "难度（驿站才能改）", 18))
	for row: Dictionary in world_map.difficulty_rows():
		var difficulty_id := str(row["difficulty_id"])
		var line := HBoxContainer.new()
		line.name = "DifficultyRow%s" % difficulty_id
		line.add_theme_constant_override("separation", 8)
		var text := "%s（%s）" % [str(row["name"]), str(row["condition"])]
		if bool(row["current"]):
			text += "　▶ 当前"
		elif not bool(row["ok"]):
			text += "　— %s" % str(row["error"])
		var label := _make_label("DifficultyLabel%s" % difficulty_id, text)
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		line.add_child(_make_button(
			"DifficultyButton%s" % difficulty_id, "切换",
			func() -> void: press_difficulty(difficulty_id),
			bool(row["current"]) or not bool(row["ok"]),
		))
		_list.add_child(line)


func _clear(box: Node) -> void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()


func _make_label(node_name: String, text: String, font_size: int = 0) -> Label:
	var label := Label.new()
	label.name = node_name
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if font_size > 0:
		label.add_theme_font_size_override("font_size", font_size)
	return label


func _make_button(node_name: String, text: String, pressed: Callable, disabled: bool = false) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.disabled = disabled
	button.pressed.connect(pressed)
	return button


# ------------------------------------------------------------------ 操作

## 传送：界面不自己切场景，交给调用方（大地图控制器）——它还要把玩家位置写回会话
func press_travel(node_id: String) -> Dictionary:
	var result: Dictionary = world_map.travel(node_id, current_scene_id)
	if not bool(result["ok"]):
		show_message(str(result["error"]))
		return result
	var session_node := _session_node()
	if travel_handler.is_valid():
		travel_handler.call(str(result["scene_id"]), str(result["name"]))
	elif session_node != null:
		session_node.pending_local_scene = str(result["scene_id"])
	show_message("出发去 %s" % str(result["name"]))
	return result


func press_difficulty(difficulty_id: String) -> Dictionary:
	var result: Dictionary = world_map.switch_difficulty(difficulty_id)
	show_message(
		"难度已切到 %s" % str(result.get("name", difficulty_id))
		if bool(result["ok"]) else str(result["error"])
	)
	refresh()
	return result


# ------------------------------------------------------------------ 环境

func _resolve_db():
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

func _run_waypoint_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	state.reveal_node("n_qingfengyi")
	state.reveal_node("n_luoyanpo")
	refresh()
	lines.append("地图：%s" % progress_text())

	# 1. 传送目标：已揭开且有可进入小地图的才列出来
	# （落雁坡是大地图上的野外区域，enter_scene 是空的，不算「传送目的地」）
	var targets: Array = world_map.travel_targets("")
	var names := PackedStringArray()
	for entry: Dictionary in targets:
		names.append(str(entry["name"]))
	var travel_ok := names.has("清风驿") and not names.has("落雁坡") and not names.has("黑风寨")
	ok = ok and travel_ok
	lines.append("传送目标 ok=%s（%s；未探索的与野外区域都不列）" % [travel_ok, "、".join(names)])

	# 2. 没揭开的不能传
	var blocked: Dictionary = world_map.travel("n_huangcun", "")
	ok = ok and not bool(blocked["ok"]) and str(blocked["error"]).contains("还没探索")
	lines.append("未探索拦截 ok=%s（%s）" % [not bool(blocked["ok"]), str(blocked["error"])])

	# 3. 难度：普通随便切；困难／绝境要打过 Boss
	var easy: Dictionary = press_difficulty("normal")
	ok = ok and bool(easy["ok"])
	var hard: Dictionary = press_difficulty("hard")
	ok = ok and not bool(hard["ok"]) and str(hard["error"]).contains("大寨主")
	lines.append("难度门槛 ok=%s（困难：%s）" % [not bool(hard["ok"]), str(hard["error"])])
	state.record_dungeon("scene_heifengzhai", "bosses", "en_bd_boss")
	var hard_now: Dictionary = press_difficulty("hard")
	ok = ok and bool(hard_now["ok"])
	var nightmare: Dictionary = press_difficulty("nightmare")
	ok = ok and not bool(nightmare["ok"]) and str(nightmare["error"]).contains("醉刀客")
	lines.append("击败大寨主后 ok=%s（困难可切=%s；绝境还差：%s）" % [
		bool(hard_now["ok"]), bool(hard_now["ok"]), str(nightmare["error"]),
	])
	state.record_dungeon("scene_heifengzhai", "bosses", "en_hidden_drunk")
	var nightmare_now: Dictionary = press_difficulty("nightmare")
	ok = ok and bool(nightmare_now["ok"])
	lines.append("击败醉刀客后绝境可切=%s（当前难度 %s）" % [bool(nightmare_now["ok"]), state.difficulty_id])

	# 版式预算：整页最小高度要塞得进设计分辨率
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 滚动区内容宽度：横滚是关着的，目的地一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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
	print("WAYPOINT SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
