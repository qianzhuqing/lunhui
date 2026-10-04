## 游戏内的枢纽场景（占位美术）。
##
## 它是「新建／读取存档之后落地的第一站」：在这里能查看当前会话状态、手动存档、
## 进大地图探索（明雷／副本／战斗都从那里开始）、开角色与行囊，也能退回主菜单。
## 真正的城镇系统（NPC、任务、商店街）还没做，所以这里只是一张精简的中转页。
extends Control

const TableDbScript := preload("res://src/core/table_db.gd")

const MENU_SCENE := "res://scenes/main_menu.tscn"
const CHARACTER_SCENE := "res://scenes/character_screen.tscn"
const SaveServiceScript := preload("res://src/core/save_service.gd")
const WORLD_SCENE := "res://scenes/world_run.tscn"
const SfxScript := preload("res://src/audio/sfx.gd")
const SettingsStoreScript := preload("res://src/core/settings_store.gd")

var back_handler := Callable()
## 存档设施（用例可注入临时目录）
var save_store_override = null

var _lines: VBoxContainer
var _notice: Label
var _volume_button: Button
## 自动战斗开关（设计 11 §四）：持久设置，落 settings.cfg，与音量同一处入口。
var _auto_battle_button: Button
var _settings_override = null


func _ready() -> void:
	setup()


## 幂等，用例可以直接调。
func setup() -> void:
	if _lines != null:
		return
	_build_ui()
	_fill_state()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		press_back()
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_TAB:
		press_character()
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_ENTER:
		press_world()
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_F5:
		press_save()


func _state():
	var session := _session_node("GameSession")
	return session.state if session != null else null


## 一句话反馈（存了没存上、存到哪一格）
func _set_headline(text: String) -> void:
	if _notice != null:
		_notice.text = text


## 打开角色与行囊（三个页签）
func press_character() -> void:
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(CHARACTER_SCENE)


## 进入大地图开始探索（最短闭环的入口）
func press_world() -> void:
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(WORLD_SCENE)


## 手动存档（城镇/枢纽是存档点）。读档仍在主菜单，游戏内只做保存。
func press_save() -> Dictionary:
	var state = _state()
	if state == null:
		_set_headline("没有会话状态，存不了")
		return {"ok": false, "error": "没有会话状态"}
	var store = save_store_override
	if store == null:
		store = SaveServiceScript.make_default()
	var service = SaveServiceScript.new(store, state)
	var result: Dictionary = service.save("枢纽存档点")
	_set_headline(service.describe())
	return result


func press_back() -> void:
	if back_handler.is_valid():
		back_handler.call()
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(MENU_SCENE)


## 音量：按一下轮换一档（静音／25%／50%／75%／100%），**当场生效**并写进设置文件。
##
## 为什么放在枢纽页：设计还没有设置界面／暂停菜单，而这里是**游戏内唯一随时到得了的菜单**
## （大地图按 Esc 就回这儿）。等设计给了正式设置界面，把这一行搬过去即可——`Sfx` 那边的接口不用动。
func press_volume() -> float:
	var value := SfxScript.cycle_volume()
	if _volume_button != null:
		_volume_button.text = SfxScript.volume_text()
	_set_headline("音量：%s（已写入设置）" % SfxScript.volume_text().replace("音量：", ""))
	return value


## 自动战斗开关：按一下开／关，写进设置文件；**下次进战斗就按它自动打**。
## 「玩家点任意指令立刻接管」这条在战斗界面里实现（点一下指令就退出自动），
## 这里只管持久设置本身——省事，不夺权。
func press_auto_battle() -> bool:
	var store = settings()
	var current: bool = bool(store.load_auto_battle().get("enabled", false))
	var wanted := not current
	var result: Dictionary = store.save_auto_battle(wanted)
	if _auto_battle_button != null:
		_auto_battle_button.text = auto_battle_text(wanted)
	if bool(result.get("ok", false)):
		_set_headline("自动战斗：%s（已写入设置）" % ("开" if wanted else "关"))
	else:
		_set_headline("自动战斗：%s（设置写不进去：%s）" % ["开" if wanted else "关", str(result.get("error", ""))])
	return wanted


static func auto_battle_text(enabled: bool) -> String:
	return "自动战斗：%s（点指令即接管）" % ("开" if enabled else "关")


## 设置存储：自检用 `_settings_override` 指到临时目录，正常运行落默认存档目录旁。
func settings():
	if _settings_override != null:
		return _settings_override
	_settings_override = SettingsStoreScript.new()
	return _settings_override


func state_lines() -> PackedStringArray:
	var session := _session_node("GameSession")
	if session == null or session.state == null:
		return PackedStringArray(["没有会话状态：请回主菜单新建或读取存档"])
	return session.state.summary_lines(_resolve_db())


func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var center := CenterContainer.new()
	center.name = "Center"
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var column := VBoxContainer.new()
	column.name = "Column"
	column.custom_minimum_size = Vector2(460, 0)
	column.add_theme_constant_override("separation", 8)
	center.add_child(column)

	var title := Label.new()
	title.name = "Title"
	title.text = "游戏内（占位场景）"
	title.add_theme_font_size_override("font_size", 24)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(title)

	var hint := Label.new()
	hint.name = "Hint"
	hint.text = "这里是游戏内枢纽：进大地图可探索明雷、副本与战斗（Enter）"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(hint)

	_lines = VBoxContainer.new()
	_lines.name = "StateLines"
	_lines.add_theme_constant_override("separation", 4)
	column.add_child(_lines)

	var back := Button.new()
	back.name = "BackButton"
	back.text = "返回主菜单"
	back.custom_minimum_size = Vector2(460, 40)
	back.pressed.connect(press_back)
	column.add_child(back)

	var character := Button.new()
	character.name = "CharacterButton"
	character.text = "角色与行囊（Tab）"
	character.custom_minimum_size = Vector2(460, 40)
	character.pressed.connect(press_character)
	column.add_child(character)

	var world_button := Button.new()
	world_button.name = "WorldButton"
	world_button.text = "进入大地图（Enter）"
	world_button.custom_minimum_size = Vector2(460, 40)
	world_button.pressed.connect(press_world)
	column.add_child(world_button)

	# 存档点：设计 02「城镇：存档点，可自由存读」——占位场景就是城镇外的枢纽，给它一个手动存档
	var save_button := Button.new()
	save_button.name = "SaveButton"
	save_button.text = "保存游戏（F5）"
	save_button.custom_minimum_size = Vector2(460, 40)
	save_button.pressed.connect(press_save)
	column.add_child(save_button)

	# 音量：游戏有音效之后玩家得关得掉（音效层是复用的 MIT 播放层，音量走它的那条总线）
	_volume_button = Button.new()
	_volume_button.name = "VolumeButton"
	_volume_button.text = SfxScript.volume_text()
	_volume_button.custom_minimum_size = Vector2(460, 40)
	_volume_button.pressed.connect(press_volume)
	column.add_child(_volume_button)

	# 自动战斗开关（设计 11 §四）：与音量同处，持久设置
	_auto_battle_button = Button.new()
	_auto_battle_button.name = "AutoBattleButton"
	_auto_battle_button.text = auto_battle_text(bool(settings().load_auto_battle().get("enabled", false)))
	_auto_battle_button.custom_minimum_size = Vector2(460, 40)
	_auto_battle_button.pressed.connect(press_auto_battle)
	column.add_child(_auto_battle_button)

	# 上一场战斗的结果（从战斗回来时给一句交代）
	var session := _tree().root.get_node_or_null("GameSession") if _tree() != null else null
	if session != null and not session.last_battle.is_empty():
		var summary := Label.new()
		summary.name = "LastBattle"
		summary.text = "上一场：%s　%s" % [str(session.last_battle.get("team", "")), str(session.last_battle.get("summary", ""))]
		summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		summary.custom_minimum_size = Vector2(460, 0)
		column.add_child(summary)

	# 存档反馈（手动存档后写在这里）
	_notice = Label.new()
	_notice.name = "Notice"
	_notice.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_notice.custom_minimum_size = Vector2(460, 0)
	column.add_child(_notice)


func _fill_state() -> void:
	for line: String in state_lines():
		var label := Label.new()
		label.text = "· " + line
		_lines.add_child(label)


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var db = TableDbScript.new()
	db.load_all()
	return db


## 同 main_menu：--script 模式下节点不在活动场景树里，绝对路径不可用。
func _session_node(node_name: String) -> Node:
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
