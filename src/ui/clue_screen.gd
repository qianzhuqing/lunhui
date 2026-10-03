## 线索本：把隐藏内容与其线索来源摆给玩家看（03_副本_黑风寨.md：「线索必须能被找到」）。
##
## 骨架在 `scenes/clue_screen.tscn`，内容来自 `src/core/clue_service.gd`。
## 入口：小地图里按 K（看本图隐藏内容与事件判定）；大地图按 K（按地标看野外事件）。
## 自检：`-- --clue-selftest`。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const ClueServiceScript := preload("res://src/core/clue_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

var state_override = null
## 用例注入的表库（要在真场景里跑「几十条线索」这种发行数据用不到的配置）
var db_override = null
var return_handler := Callable()
## "scene"（小地图）或 "region"（大地图按地标）
var scope: String = "scene"
var scene_id: String = ""

var db
var state
var clues

var _title: Label
var _subtitle: Label
var _list: VBoxContainer
var _status: Label
var _return_button: Button
var _bound := false


func _ready() -> void:
	setup()
	if _has_user_arg("--clue-selftest"):
		call_deferred("_run_clue_selftest")


func setup() -> void:
	if clues != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	clues = ClueServiceScript.new(db, state)
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


func status_text() -> String:
	return _status.text if _status != null else ""


func entry_count() -> int:
	return _list.get_child_count() if _list != null else 0


func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_subtitle = _require_node("Panel/Margin/Column/Subtitle") as Label
	_list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_return_button.pressed.connect(press_return)
	_status.text = ""


func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("clue_screen.tscn 缺少节点：%s" % path)
		assert(false, "clue_screen.tscn 缺少节点：%s" % path)
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
	if clues == null or _list == null:
		return
	_clear(_list)
	var entries: Array = []
	if scope == "region":
		_title.text = "线索本　（野外按地标）"
		entries = clues.region_clues()
	else:
		var row: Resource = db.get_row("map_local", scene_id)
		_title.text = "线索本　（%s）" % (str(row.name_cn) if row != null else scene_id)
		entries = clues.clues_for_scene(scene_id)
	var done := 0
	for entry: Dictionary in entries:
		if bool(entry["done"]):
			done += 1
	_subtitle.text = "共 %d 条　已完成 %d 条　（线索来自 NPC 闲聊与场景描述，找到就记在这里）" % [entries.size(), done]
	if entries.is_empty():
		_list.add_child(_make_label("Empty", "这里暂时没有可查的线索"))
		return
	for entry: Dictionary in entries:
		var label := _make_label(
			"Clue_%s" % str(entry["id"]), clues.describe(entry), 0,
			"" if not bool(entry["done"]) else "#6fcf97",
		)
		_list.add_child(label)


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

func _run_clue_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	# 1. 小地图线索：黑风寨的隐藏内容与事件判定
	scope = "scene"
	scene_id = "scene_heifengzhai"
	refresh()
	var scene_entries: Array = clues.clues_for_scene(scene_id)
	var hidden := 0
	var events := 0
	var with_clues := 0
	var unsupported := 0
	for entry: Dictionary in scene_entries:
		if str(entry["kind"]) == "hidden":
			hidden += 1
		else:
			events += 1
		if not Array(entry["clues"]).is_empty():
			with_clues += 1
		if bool(entry.get("unsupported", false)):
			unsupported += 1
	# 黑风寨本图 6 条隐藏（后山密道那条的 scene_id 是 scene_cave）
	ok = ok and hidden == 6 and events == 4 and with_clues == scene_entries.size()
	lines.append("黑风寨：隐藏 %d 条／事件 %d 条／都有线索=%s（缺位点未做 %d 条）" % [
		hidden, events, with_clues == scene_entries.size(), unsupported,
	])
	lines.append("面板行数=%d" % entry_count())
	ok = ok and entry_count() == scene_entries.size()
	# 2. 完成状态跟存档走
	state.record_dungeon(scene_id, "triggers", "trig_wine")
	refresh()
	var wine_done := false
	for entry: Dictionary in clues.clues_for_scene(scene_id):
		if str(entry["id"]) == "trig_wine":
			wine_done = bool(entry["done"])
	ok = ok and wine_done
	lines.append("完成状态跟存档：酒葫芦已触发=%s" % wine_done)
	# 3. 大地图线索（按地标）
	scope = "region"
	refresh()
	var region_entries: Array = clues.region_clues()
	# 野外按地标：落雁坡 3、荒村 1、塌陷山洞 1、黑风寨 2、清风驿 1 = 8
	ok = ok and region_entries.size() == 8
	lines.append("大地图线索=%d 条（落雁坡 %d／荒村 %d）" % [
		region_entries.size(),
		clues.clues_for_region("n_luoyanpo").size(),
		clues.clues_for_region("n_huangcun").size(),
	])
	# 版式预算：整页最小高度要塞得进设计分辨率
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 滚动区内容宽度：横滚是关着的，线索一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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
	print("CLUE SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
