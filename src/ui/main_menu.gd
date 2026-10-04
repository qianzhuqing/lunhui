## 启动菜单：新建游戏 / 读取存档 / 退出游戏。
##
## 界面用代码搭（占位美术阶段的取舍），决策都在 MenuController 里，
## 所以菜单流程既被 tests/test_menu.gd 覆盖，也能用 `--menu-selftest`
## 在真实场景里整条跑一遍（见 docs/dev/框架说明.md）。
extends Control

const MenuControllerScript := preload("res://src/core/menu_controller.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

const PLACEHOLDER_SCENE := "res://scenes/placeholder_game.tscn"
const OVERWORLD_SCENE := "res://scenes/world_run.tscn"
## 创建角色（设计 13）：**新建游戏先过创建界面**——「创建没走完不写存档」，
## 所以这一步只切场景，落盘由创建界面确认时调 `MenuController.new_game(spec)` 做。
const CREATION_SCENE := "res://scenes/creation_screen.tscn"

const BUTTON_NEW := "新建游戏"
const BUTTON_LOAD := "读取存档"
const BUTTON_QUIT := "退出游戏"

## 自检注入点。都不注入时走真实实现：写 user://saves、切场景、退出进程。
var store_override = null
var quit_handler := Callable()
var scene_switch_handler := Callable()
## 进游戏要切到哪个场景（`_enter_game` 写；用例注入场景切换回调时靠它验证落点）。
## 设计 09 §二：新建走占位枢纽页，读档一律回大地图。
var pending_scene: String = PLACEHOLDER_SCENE

var controller

var _status: Label
var _slot_box: VBoxContainer
var _new_button: Button
var _load_button: Button
var _quit_button: Button


func _ready() -> void:
	setup()
	if _has_user_arg("--menu-selftest"):
		call_deferred("_run_menu_selftest")


## 搭界面并接上控制器。幂等，用例可以直接调（不依赖引擎是否已发 _ready）。
func setup() -> void:
	if controller != null:
		return
	var db = _resolve_db()
	controller = MenuControllerScript.new(db, _make_store(), GameStateScript.default_party_ids_from_recruit(db))
	_build_ui()
	_refresh()


## 场景与逻辑的连接点，方便用例直接调。
func resolve_db():
	return _resolve_db()


## 新建游戏：造状态 → 落在存档槽里 → 进入游戏。
func press_new_game() -> Dictionary:
	# 设计 13：新建游戏走创建角色流程（选出身／分七维 → 天赋 → 起名 → 确认）。
	# 这一步**不建号、不落盘**——玩家中途退出就什么都不留。
	if scene_switch_handler.is_valid():
		scene_switch_handler.call(CREATION_SCENE)
	else:
		var tree := get_tree()
		if tree != null:
			tree.change_scene_to_file(CREATION_SCENE)
	return {"ok": true, "action": MenuControllerScript.ACTION_NONE, "slot": 0,
		"state": null, "message": "去创建角色"}


## 读取存档：展开存档列表；没有存档时给出提示。
func press_load_game() -> Dictionary:
	_slot_box.visible = not _slot_box.visible
	if not controller.can_load():
		_set_status("暂无存档可以读取，先「新建游戏」")
		return {"ok": false, "action": MenuControllerScript.ACTION_NONE, "message": _status.text}
	_set_status("选择要读取的存档（共 %d 份）" % controller.save_count())
	return {"ok": true, "action": "list", "message": _status.text}


## 读取指定槽位。
func press_slot(slot: int) -> Dictionary:
	var result: Dictionary = controller.load_game(slot)
	_handle(result)
	return result


## 退出游戏：真实实现会结束进程。
func press_quit() -> void:
	if quit_handler.is_valid():
		quit_handler.call()
		return
	var tree := _tree()
	if tree == null:
		push_error("[MainMenu] 没有场景树，无法退出")
		return
	tree.quit()


func status_text() -> String:
	return _status.text if _status != null else ""


func slot_button(slot: int) -> Button:
	return _slot_box.get_node_or_null("SlotButton%d" % slot)


func _handle(result: Dictionary) -> void:
	var message := str(result.get("message", ""))
	if not message.is_empty():
		_set_status(message)
	if str(result.get("action", "")) != MenuControllerScript.ACTION_PLAY:
		return
	# 设计 09 §二：读档一律回大地图（`load_game` 在结果里写 `start_scene`），
	# 新建游戏照旧进占位枢纽页。
	var start_scene := OVERWORLD_SCENE if str(result.get("start_scene", "")) == MenuControllerScript.START_OVERWORLD else PLACEHOLDER_SCENE
	_enter_game(result["state"], start_scene)


func _enter_game(state, start_scene: String = PLACEHOLDER_SCENE) -> void:
	var session := _session()
	if session != null:
		session.set_state(state)
	# 落点交给视图：用例注入 scene_switch_handler 时不切场景，但可以读这个字段验证落点
	pending_scene = start_scene
	if scene_switch_handler.is_valid():
		scene_switch_handler.call(state)
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(start_scene)


func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var center := CenterContainer.new()
	center.name = "Center"
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var column := VBoxContainer.new()
	column.name = "Column"
	column.custom_minimum_size = Vector2(440, 0)
	column.add_theme_constant_override("separation", 10)
	center.add_child(column)

	var title := Label.new()
	title.name = "Title"
	title.text = "《轮回》"
	# 标题档 24（设计 15 §4.4：A11 原先的「标题 36px」按 Q82 改成 24——1× 体系里 36 会顶到布局，
	# 也超出一套 12px 字集的可用倍率）。以前这里停在 36，是没跟上那条修订。
	title.add_theme_font_size_override("font_size", 24)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(title)

	var subtitle := Label.new()
	subtitle.name = "Subtitle"
	# 副标题只说「这一版能做什么」。别写成自我否定的一句话——大地图、明雷、战斗、
	# 行囊都已经接进游戏了，玩家的第一句话不能是谎话（tests/test_copy_audit.gd 会盯着）。
	subtitle.text = "第一章「黑风寨」　大地图・明雷・战斗・行囊"
	subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(subtitle)

	_new_button = _make_button("NewGameButton", BUTTON_NEW, "_on_new_game_pressed")
	column.add_child(_new_button)
	_load_button = _make_button("LoadGameButton", BUTTON_LOAD, "_on_load_pressed")
	column.add_child(_load_button)

	_slot_box = VBoxContainer.new()
	_slot_box.name = "SlotList"
	_slot_box.add_theme_constant_override("separation", 4)
	column.add_child(_slot_box)

	_quit_button = _make_button("QuitButton", BUTTON_QUIT, "_on_quit_pressed")
	column.add_child(_quit_button)

	_status = Label.new()
	_status.name = "Status"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.custom_minimum_size = Vector2(440, 48)
	_status.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	column.add_child(_status)


func _make_button(node_name: String, text: String, handler: String) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.custom_minimum_size = Vector2(440, 44)
	button.pressed.connect(Callable(self, handler))
	return button


func _refresh() -> void:
	# 先让控制器重读存档目录，再刷界面，避免按钮状态停留在上一次
	controller.refresh()
	_refresh_slots()
	_new_button.disabled = not controller.can_start_new_game()
	_load_button.disabled = not controller.can_load()
	if not controller.can_start_new_game():
		_set_status("配置表未就绪：先运行 tools\\run_tests.bat 生成 data/generated")
	elif not controller.can_load():
		_set_status("暂无存档，先「新建游戏」")
	else:
		_set_status("已有 %d 份存档，点「读取存档」选择" % controller.save_count())


func _refresh_slots() -> void:
	for child in _slot_box.get_children():
		# 先摘再 queue_free（同 creation_screen._rebuild_body 的注释）：名字要立刻可复用
		_slot_box.remove_child(child)
		child.queue_free()
	var labels: PackedStringArray = controller.slot_labels()
	for index in range(controller.slots.size()):
		var entry: Dictionary = controller.slots[index]
		var slot := int(entry["slot"])
		var button := Button.new()
		button.name = "SlotButton%d" % slot
		button.text = labels[index]
		button.custom_minimum_size = Vector2(440, 32)
		button.disabled = not bool(entry["exists"]) or bool(entry["corrupt"])
		button.pressed.connect(_on_slot_pressed.bind(slot))
		_slot_box.add_child(button)


func _on_new_game_pressed() -> void:
	press_new_game()


func _on_load_pressed() -> void:
	press_load_game()


func _on_slot_pressed(slot: int) -> void:
	press_slot(slot)


func _on_quit_pressed() -> void:
	press_quit()


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


func _make_store():
	if store_override != null:
		return store_override
	return SaveStoreScript.new(SaveStoreScript.default_dir())


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var db = TableDbScript.new()
	db.load_all()
	return db


## 取 autoload 节点。
##
## 不能用 `get_node("/root/X")`：`--script` 模式下的节点还没进入活动场景树，
## 绝对路径与 get_tree() 都会失效，所以统一从主循环的 root 相对查找。
func _session_node(node_name: String) -> Node:
	var root := _root_node()
	if root == null:
		return null
	return root.get_node_or_null(node_name)


func _session() -> Node:
	return _session_node("GameSession")


func _root_node() -> Node:
	var tree := _tree()
	return tree.root if tree != null else null


func _tree() -> SceneTree:
	# 不在场景树里时 get_tree() 会打一条 ERROR（--script 模式用例直接 new 节点就会踩到）
	if is_inside_tree():
		var tree := get_tree()
		if tree != null:
			return tree
	return Engine.get_main_loop() as SceneTree


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


## 真实场景自检：不注入任何东西，走一遍新建 → 落盘 → 枚举 → 读取 → 退出动作。
func _run_menu_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()

	var created: Dictionary = controller.new_game()
	var new_ok := bool(created["ok"]) and str(created["action"]) == MenuControllerScript.ACTION_PLAY
	ok = ok and new_ok
	var slot := int(created["slot"])
	lines.append("新建游戏 ok=%s 槽=%d %s" % [new_ok, slot, str(created["message"])])
	controller.refresh()

	var store = controller.store()
	var saved: bool = slot > 0 and store.slot_exists(slot)
	ok = ok and saved
	lines.append("落盘 ok=%s 路径=%s" % [saved, store.slot_path(slot)])

	var listed := false
	for entry: Dictionary in store.list_slots(_resolve_db()):
		if int(entry["slot"]) == slot and bool(entry["exists"]) and not bool(entry["corrupt"]):
			listed = true
	ok = ok and listed
	lines.append("枚举 ok=%s 存档数=%d" % [listed, controller.save_count()])

	var loaded: Dictionary = controller.load_game(slot)
	var load_ok := bool(loaded["ok"]) and str(loaded["action"]) == MenuControllerScript.ACTION_PLAY
	var same := false
	if load_ok:
		var state = loaded["state"]
		same = state != null and state.party_size() > 0 and int(state.slot) == slot
	ok = ok and load_ok and same
	lines.append("读取 ok=%s 队伍与槽位一致=%s" % [load_ok, same])

	var quit_result: Dictionary = controller.quit_game()
	var quit_ok := str(quit_result["action"]) == MenuControllerScript.ACTION_QUIT
	ok = ok and quit_ok
	lines.append("退出动作 ok=%s" % quit_ok)

	# 版式预算：整页最小高度要塞得进设计分辨率
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	for line: String in lines:
		print("  " + line)
	print("MENU SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	get_tree().quit(0 if ok else 1)
