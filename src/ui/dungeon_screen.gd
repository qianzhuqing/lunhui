## 副本完成度界面（03_副本_黑风寨.md「完成度」+「已通关层可扫荡」）。
##
## 骨架在 `scenes/dungeon_screen.tscn`，数据来自 `src/core/dungeon_service.gd`：
##   四项完成度（宝箱／隐藏房间／隐藏 Boss／事件）—— 设计明说「不影响奖励，只作追求目标与炫耀项」
##   楼层列表：每层几个房间、清了几个、能不能扫荡；扫荡按钮直接调 `DungeonService.sweep()`
##
## 入口：副本里按 M（`show_progress`）。自检：`-- --dungeon-selftest`。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const DungeonServiceScript := preload("res://src/core/dungeon_service.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

var state_override = null
## 用例注入的表库（要在真场景里跑「几十层」这种发行数据用不到的配置）
var db_override = null
var return_handler := Callable()
## 扫荡落点：由调用方（小地图控制器）接过去，界面不直接碰场景
var sweep_handler := Callable()
var scene_id: String = ""

var db
var state
var dungeon

var _title: Label
var _summary: Label
var _detail: Label
var _list: VBoxContainer
var _status: Label
var _return_button: Button
var _bound := false

## 扫荡动画：掉落**在按下时就结算**（逻辑不能等动画），动画只负责「看得见」——
## 进度条推进 + 逐间打勾，走完再把结果与完成度摆到界面上。
## 用「自己数时间」而不是 Tween：用例可以 step_sweep_animation() 精确推帧，不靠真实帧率。
const SWEEP_ANIM_SECONDS := 0.6

var _sweeping := false
var _sweep_floor := -1
var _sweep_elapsed := 0.0
var _sweep_result: Dictionary = {}
var _sweep_rooms: PackedStringArray = PackedStringArray()
var _sweep_row_base_text := ""
var _sweep_bar: ProgressBar = null


func _ready() -> void:
	setup()
	if _has_user_arg("--dungeon-selftest"):
		call_deferred("_run_dungeon_selftest")


func setup() -> void:
	if dungeon != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	dungeon = DungeonServiceScript.new(db, state, RngServiceScript.new())
	if scene_id.is_empty():
		scene_id = "scene_heifengzhai"
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


func status_text() -> String:
	return _status.text if _status != null else ""


func summary_text() -> String:
	return _summary.text if _summary != null else ""


func detail_text() -> String:
	return _detail.text if _detail != null else ""


func floor_rows() -> Array:
	return _list.get_children() if _list != null else []


func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_summary = _require_node("Panel/Margin/Column/Summary") as Label
	_detail = _require_node("Panel/Margin/Column/Detail") as Label
	_list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_return_button.pressed.connect(press_return)
	_status.text = ""


func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("dungeon_screen.tscn 缺少节点：%s" % path)
		assert(false, "dungeon_screen.tscn 缺少节点：%s" % path)
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
	if dungeon == null or _list == null:
		return
	var room: Resource = db.get_row("map_local", scene_id)
	_title.text = "%s　完成度" % (str(room.name_cn) if room != null else scene_id)
	var snapshot: Dictionary = dungeon.progress(scene_id)
	_summary.text = "总计 %d%%　（%d / %d 项）%s" % [
		int(snapshot["percent"]), int(snapshot["done"]), int(snapshot["total"]),
		"　—— 全部完成！" if int(snapshot["percent"]) >= 100 else "",
	]
	_detail.text = "宝箱 %d/%d　隐藏房间 %d/%d　隐藏 Boss %d/%d　事件 %d/%d" % [
		int(snapshot["chests"]["done"]), int(snapshot["chests"]["total"]),
		int(snapshot["hidden_rooms"]["done"]), int(snapshot["hidden_rooms"]["total"]),
		int(snapshot["hidden_boss"]["done"]), int(snapshot["hidden_boss"]["total"]),
		int(snapshot["events"]["done"]), int(snapshot["events"]["total"]),
	]
	_clear(_list)
	for entry: Dictionary in dungeon.floors(scene_id):
		_list.add_child(_make_floor_row(entry))


func _make_floor_row(entry: Dictionary) -> Control:
	var floor_number := int(entry["floor"])
	var line := HBoxContainer.new()
	line.name = "FloorRow%d" % floor_number
	line.add_theme_constant_override("separation", 8)
	var enemy_rooms: Array = entry["enemy_rooms"]
	var cleared: Array = entry["cleared"]
	var sweepable := bool(entry["sweepable"])
	var text := "第 %d 层　房间 %d（有敌人 %d，已清 %d）" % [
		floor_number, Array(entry["rooms"]).size(), enemy_rooms.size(), cleared.size(),
	]
	if enemy_rooms.is_empty():
		text += "　— 这层没有战斗房间"
	elif sweepable:
		text += "　▶ 已通关，可扫荡"
	else:
		var left := PackedStringArray()
		for room_id: String in enemy_rooms:
			if not cleared.has(room_id):
				left.append(_room_name(room_id))
		text += "　— 还差：%s" % "、".join(left)
	var label := _make_label("FloorLabel%d" % floor_number, text)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	line.add_child(label)
	line.add_child(_make_button(
		"SweepButton%d" % floor_number, "扫荡（仅掉落）",
		func() -> void: press_sweep(floor_number),
		not sweepable,
	))
	return line


## 房间 id → 中文名（给玩家看的不是 hf2_hall 这种 id）
func _room_name(room_id: String) -> String:
	var row: Resource = db.get_row("dungeon_room", room_id)
	return str(row.room_name) if row != null else room_id


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


## 扫荡：交给调用方（小地图控制器）执行，界面只负责显示结果
func press_sweep(floor_number: int) -> Dictionary:
	if _sweeping:
		return {"ok": false, "error": "扫荡动画还没播完"}
	if not sweep_handler.is_valid():
		show_message("这里不能扫荡（要在大地图的副本里）")
		return {"ok": false, "error": "no_handler"}
	var result: Dictionary = sweep_handler.call(floor_number)
	if not bool(result.get("ok", false)):
		show_message("扫荡第 %d 层：%s" % [floor_number, str(result.get("error", ""))])
		return result
	_begin_sweep_animation(floor_number, result)
	return result


# ------------------------------------------------------------------ 扫荡动画

func is_sweeping() -> bool:
	return _sweeping


## 0.0 ~ 1.0
func sweep_progress() -> float:
	return clampf(_sweep_elapsed / SWEEP_ANIM_SECONDS, 0.0, 1.0)


## 已经打勾的房间名（动画里逐间冒出来）
func sweep_ticked_rooms() -> PackedStringArray:
	var out := PackedStringArray()
	if _sweep_rooms.is_empty():
		return out
	# 四舍五入而不是向下取整：只有一间敌人房的层，打勾要出现在动画**中间**，
	# 不然它会跟「动画结束→刷新列表」撞在同一帧，玩家根本看不见那一勾。
	var count := int(floor(sweep_progress() * float(_sweep_rooms.size()) + 0.5))
	for index in range(mini(count, _sweep_rooms.size())):
		out.append(_room_name(_sweep_rooms[index]))
	return out


## 推进动画（真实运行由 _process 调；用例直接调这个，不依赖帧率）
func step_sweep_animation(delta: float) -> void:
	if not _sweeping:
		return
	_sweep_elapsed += delta
	_refresh_sweep_visuals()
	if _sweep_elapsed >= SWEEP_ANIM_SECONDS:
		_finish_sweep_animation()


func _begin_sweep_animation(floor_number: int, result: Dictionary) -> void:
	_sweeping = true
	_sweep_floor = floor_number
	_sweep_elapsed = 0.0
	_sweep_result = result
	_sweep_rooms = _floor_enemy_rooms(floor_number)
	# 行文本先存下来：打勾是在它后面追加，刷新视觉时重新拼，免得越拼越长
	var label: Label = find_child("FloorLabel%d" % floor_number, true, false)
	_sweep_row_base_text = str(label.text) if label != null else ""
	_build_sweep_bar()
	_set_sweep_buttons_disabled(true)
	show_message("扫荡第 %d 层…（逐间清过去，只结算掉落）" % floor_number)
	_refresh_sweep_visuals()


func _refresh_sweep_visuals() -> void:
	if _sweep_bar != null and is_instance_valid(_sweep_bar):
		_sweep_bar.value = sweep_progress() * 100.0
	var label: Label = find_child("FloorLabel%d" % _sweep_floor, true, false)
	if label == null:
		return
	var ticked: PackedStringArray = sweep_ticked_rooms()
	label.text = _sweep_row_base_text
	if not ticked.is_empty():
		label.text = "%s　✓ %s" % [_sweep_row_base_text, "、".join(ticked)]


func _finish_sweep_animation() -> void:
	_sweeping = false
	_sweep_elapsed = SWEEP_ANIM_SECONDS
	_sweep_bar = null
	show_message("扫荡第 %d 层：%s" % [
		_sweep_floor, str(_sweep_result.get("summary", _sweep_result.get("error", ""))),
	])
	# refresh() 会重建列表，进度条跟着被清掉（它是列表的第一个孩子）
	# 按钮的可用状态也由 refresh() 按各层自己的进度重算——别在这里一律打开，
	# 否则没通关的层会跟着变成可扫（踩过）。
	refresh()


## 第 N 层有敌人的房间（动画里一间一间打勾用的）
func _floor_enemy_rooms(floor_number: int) -> PackedStringArray:
	var out := PackedStringArray()
	if dungeon == null:
		return out
	for entry: Dictionary in dungeon.floors(scene_id):
		if int(entry["floor"]) != floor_number:
			continue
		for room_id: String in Array(entry["enemy_rooms"]):
			out.append(room_id)
	return out


func _build_sweep_bar() -> void:
	if _sweep_bar != null and is_instance_valid(_sweep_bar):
		return
	if _list == null:
		return
	_sweep_bar = ProgressBar.new()
	_sweep_bar.name = "SweepProgress"
	_sweep_bar.max_value = 100.0
	_sweep_bar.value = 0.0
	_sweep_bar.show_percentage = false
	_sweep_bar.custom_minimum_size = Vector2(0, 12)
	# 放进滚动列表里而不是整个面板的底部：面板竖向已经排满，塞在 Column 末尾会被挤出屏幕
	_list.add_child(_sweep_bar)
	_list.move_child(_sweep_bar, 0)


func _set_sweep_buttons_disabled(disabled: bool) -> void:
	if _list == null:
		return
	for row in _list.get_children():
		for child in row.get_children():
			if child is Button and str(child.name).begins_with("SweepButton"):
				child.disabled = disabled


func _process(delta: float) -> void:
	if _sweeping:
		step_sweep_animation(delta)


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

func _run_dungeon_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	scene_id = "scene_heifengzhai"
	refresh()
	lines.append("摘要：%s" % summary_text())
	lines.append("明细：%s" % detail_text())
	var snapshot: Dictionary = dungeon.progress(scene_id)
	ok = ok and int(snapshot["total"]) == 12 and int(snapshot["percent"]) == 0
	ok = ok and floor_rows().size() == 3
	lines.append("楼层行数=%d（可扫荡 %d 层）" % [floor_rows().size(), dungeon.sweepable_floors(scene_id).size()])
	# 打通第一层之后：那一层变成可扫荡，按钮可点
	state.record_dungeon(scene_id, "rooms", "hf1_yard")
	refresh()
	var sweep_button: Button = find_child("SweepButton1", true, false)
	ok = ok and sweep_button != null and not sweep_button.disabled
	lines.append("第 1 层清完 → 扫荡按钮可点=%s" % (sweep_button != null and not sweep_button.disabled))
	# 扫荡走调用方：这里挂一个假 handler，确认界面把结果播出来
	var calls: Array = []
	sweep_handler = func(floor_number: int) -> Dictionary:
		calls.append(floor_number)
		return {"ok": true, "summary": "铜钱 +12　物品 铁矿石×1", "floor": floor_number}
	press_sweep(1)
	# 动画：按下就结算，进度与打勾走完才落结果
	ok = ok and calls.size() == 1 and is_sweeping()
	var mid_progress := sweep_progress()
	step_sweep_animation(SWEEP_ANIM_SECONDS * 0.5)
	ok = ok and sweep_progress() > mid_progress and sweep_ticked_rooms().size() > 0
	lines.append("扫荡动画：进度 %.2f → 打勾 %s" % [sweep_progress(), str(sweep_ticked_rooms())])
	step_sweep_animation(SWEEP_ANIM_SECONDS)
	ok = ok and not is_sweeping() and str(status_text()).contains("铜钱 +12")
	lines.append("扫荡回调=%s　状态栏=%s" % [str(calls), status_text()])
	# 没通关的层按钮灰着并写清还差哪些房间
	var floor2_label: Label = find_child("FloorLabel2", true, false)
	ok = ok and floor2_label != null and str(floor2_label.text).contains("还差")
	lines.append("第 2 层提示=%s" % (str(floor2_label.text) if floor2_label != null else "（没有）"))
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
	print("DUNGEON SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
