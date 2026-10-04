## 启动菜单：控制器决策 + 真实场景冒烟。
##
## 菜单的「实际功能」在真实场景里走一遍：点新建 → 落盘 → 列表出现 → 点读取 → 会话状态就位。
extends "res://tests/test_case.gd"

const MenuControllerScript := preload("res://src/core/menu_controller.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const EmptyDbScript := preload("res://src/core/table_db.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

const MENU_SCENE := "res://scenes/main_menu.tscn"
const PLACEHOLDER_SCENE := "res://scenes/placeholder_game.tscn"
const CREATION_SCENE := "res://scenes/creation_screen.tscn"
const TEST_DIR := "res://.logs/test_menu"
const SLOTS := 3


## 存档目录不可写时，新建游戏仍应能进游戏，只是提示「不会保留」。
class ReadOnlyStore:
	extends "res://src/core/save_store.gd"

	func save_slot(_slot: int, _state) -> Dictionary:
		return {"ok": false, "error": "模拟：存档目录只读"}


func suite_name() -> String:
	return "启动菜单：新建 / 读取 / 退出"


func run() -> void:
	# 游戏里默认 6 个存档槽；用例夹具自己只开 3 格（`SLOTS`），所以常量本身要单独钉住
	# （2026-10-03 变异探针：`DEFAULT_SLOT_COUNT 6→3` 没被抓住）。
	check_eq(SaveStoreScript.DEFAULT_SLOT_COUNT, 6, "默认存档槽位数 = 6（用例夹具另有 SLOTS）")
	_clean_dir(TEST_DIR)
	_check_controller_flow()
	_check_controller_without_tables()
	_check_unwritable_store()
	_check_corrupt_slot_load()
	_check_overwrite_oldest_slot()
	_check_stale_equipped_writeback()
	_check_scene_flow()


## 决策层：不碰场景树也要能覆盖全部分支。
func _check_controller_flow() -> void:
	var db = get_db()
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	store.ensure_dir()
	var controller = MenuControllerScript.new(db, store)
	check_true(controller.can_start_new_game(), "配置表就绪时可以新建游戏")
	check_false(controller.can_load(), "没有存档时读取不可用")
	check_false(controller.has_saves(), "没有存档时 has_saves 为假")

	var created: Dictionary = controller.new_game()
	check_true(created["ok"], "新建游戏应成功")
	check_eq(created["action"], MenuControllerScript.ACTION_PLAY, "新建后动作是进入游戏")
	check_eq(int(created["slot"]), 1, "新游戏落在第一个空槽")
	# 0.10.0 起开局队伍由 `recruit_def.is_initial` 决定（设计 09 §3.2：开局只有书生一人，
	# 同伴按 join_condition／join_scene 在剧情里加入），不再取 character_base 的前 4 行。
	var initial_ids := PackedStringArray()
	for row: Resource in get_db().rows("recruit_def"):
		if int(row.is_initial) == 1:
			initial_ids.append(str(row.char_id))
	check_gt(float(initial_ids.size()), 0.0, "recruit_def 里有初始成员（%d 人）" % initial_ids.size())
	check_eq(created["state"].party_size(), initial_ids.size(), "新游戏按 recruit_def 的初始成员组队")
	check_eq(str(created["state"].char_ids), str(initial_ids), "开局队伍与 recruit_def 的初始成员逐人一致（顺序也照表）")
	check_eq(str(created.get("start_scene", "")), MenuControllerScript.START_HUB, "新建游戏进占位枢纽页")
	check_true(store.slot_exists(1), "新建游戏会立刻自动存档")
	# 空方案走 `_with_defaults`：默认用第一张出身卡，名字预填**真名**而不是卡标题
	# （设计 21 §七 第 5 条：书生那张卡叫「家道失落的书生」，真名是陆文昭）
	check_eq(
		str(created["state"].char_name(get_db(), str(created["state"].char_ids[0]))), "陆文昭",
		"没给名字时默认用出身卡的真名",
	)

	controller.refresh()
	check_true(controller.can_load(), "有了存档后读取可用")
	check_eq(controller.save_count(), 1, "存档计数为 1")
	check_true(controller.slot_labels()[0].contains("第 1 格"), "槽位文案带格号")
	check_true(controller.slot_labels()[0].contains("Lv"), "槽位文案带队伍等级")

	var second: Dictionary = controller.new_game()
	check_eq(int(second["slot"]), 2, "第二次新建落在下一个空槽")
	controller.refresh()
	check_eq(controller.save_count(), 2, "两份存档")

	var loaded: Dictionary = controller.load_game(1)
	check_true(loaded["ok"], "读取得回来")
	check_eq(loaded["action"], MenuControllerScript.ACTION_PLAY, "读取后动作是进入游戏")
	check_eq(int(loaded["state"].slot), 1, "读回的是第 1 格")
	check_eq(str(loaded["state"].char_ids), str(created["state"].char_ids), "读回的队伍与建档时一致")
	# 设计 09 §二：读档一律回大地图，落点用存档里的 world_pos（v13）
	check_eq(str(loaded.get("start_scene", "")), MenuControllerScript.START_OVERWORLD, "读档一律回大地图")
	created["state"].set_world_pos(Vector2(300.0, 400.0))
	store.save_slot(1, created["state"])
	var reloaded: Dictionary = controller.load_game(1)
	check_eq(reloaded["state"].world_position(), Vector2(300.0, 400.0), "读档把大地图坐标一起带回来")

	var missing: Dictionary = controller.load_game(99)
	check_false(missing["ok"], "读不存在的槽位应失败")
	check_true(str(missing["message"]).contains("没有存档"), "失败文案说清原因")

	var quit_result: Dictionary = controller.quit_game()
	check_eq(quit_result["action"], MenuControllerScript.ACTION_QUIT, "退出动作")


func _check_controller_without_tables() -> void:
	var empty_db = EmptyDbScript.new()
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	var controller = MenuControllerScript.new(empty_db, store)
	check_false(controller.can_start_new_game(), "没有配置表时不能新建游戏")
	var result: Dictionary = controller.new_game()
	check_false(result["ok"], "没有配置表时新建应失败")
	check_true(str(result["message"]).contains("配置表"), "失败文案指向配置表")


## 存档写不进去也不能挡住玩：进游戏照走，只是提示本次进度不保留。
func _check_unwritable_store() -> void:
	var db = get_db()
	var store = ReadOnlyStore.new(TEST_DIR, SLOTS)
	var controller = MenuControllerScript.new(db, store)
	var result: Dictionary = controller.new_game()
	check_true(result["ok"], "存档目录不可写时仍能开局")
	check_eq(result["action"], MenuControllerScript.ACTION_PLAY, "照样进入游戏")
	check_true(str(result["message"]).contains("自动存档失败"), "提示自动存档失败")
	check_true(result["state"] != null, "内存里的会话状态有效")


## 表现层：实例化真实场景，点真实按钮。
func _check_scene_flow() -> void:
	if scene_tree == null:
		fail("没有注入场景树，UI 冒烟无法进行")
		return
	_clean_dir(TEST_DIR)
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	store.ensure_dir()

	var menu = load(MENU_SCENE).instantiate()
	menu.store_override = store
	var switched: Array = []
	menu.scene_switch_handler = func(state) -> void: switched.append(state)
	var quit_called: Array = []
	menu.quit_handler = func() -> void: quit_called.append(true)
	scene_tree.root.add_child(menu)
	menu.setup()

	check_true(menu.status_text().contains("暂无存档"), "空存档时状态栏给提示")
	var new_button: Button = menu.find_child("NewGameButton", true, false)
	var load_button: Button = menu.find_child("LoadGameButton", true, false)
	var quit_button: Button = menu.find_child("QuitButton", true, false)
	check_not_null(new_button, "有「新建游戏」按钮")
	check_not_null(load_button, "有「读取存档」按钮")
	check_not_null(quit_button, "有「退出游戏」按钮")
	if new_button == null or load_button == null or quit_button == null:
		menu.free()
		return
	check_eq(new_button.text, "新建游戏", "新建按钮文案")
	check_eq(load_button.text, "读取存档", "读取按钮文案")
	check_eq(quit_button.text, "退出游戏", "退出按钮文案")
	check_true(load_button.disabled, "没有存档时读取按钮置灰")
	check_true(bool(menu.slot_button(1).disabled), "空槽按钮不可点")

	# 点「新建游戏」：**只切到创建界面**（设计 13：创建没走完不写存档）——
	# 以前这里直接建号落盘，现在中间多了创建角色那一步。
	new_button.emit_signal("pressed")
	check_eq(switched.size(), 1, "点新建游戏切到创建界面")
	check_eq(str(switched[0]), menu.CREATION_SCENE, "切的是创建角色场景")
	check_false(store.slot_exists(1), "创建没走完**不写存档**")

	# 走创建界面：真实场景、真实按钮 → 确认 → 落盘 + 进枢纽页
	var creation = load(CREATION_SCENE).instantiate()
	creation.store_override = store
	var creation_switched: Array = []
	creation.scene_switch_handler = func(path: String) -> void: creation_switched.append(path)
	scene_tree.root.add_child(creation)
	creation.setup()
	var origin_button: Button = creation.find_child("Origin_ori_scholar", true, false)
	check_not_null(origin_button, "创建界面有出身卡按钮（ori_scholar）")
	if origin_button != null:
		origin_button.emit_signal("pressed")
	var confirm_button: Button = creation.find_child("ConfirmButton", true, false)
	check_not_null(confirm_button, "创建界面有「确认创建」按钮")
	check_true(confirm_button != null and not confirm_button.disabled, "选了出身就能确认（不是禁用态）")
	if confirm_button != null:
		confirm_button.emit_signal("pressed")
	check_true(store.slot_exists(1), "确认创建后落盘")
	check_eq(creation_switched.size(), 1, "确认后进游戏")
	check_eq(str(creation_switched[0]), creation.HUB_SCENE, "新建游戏落在占位枢纽页")
	scene_tree.root.remove_child(creation)
	creation.free()

	var session = scene_tree.root.get_node_or_null("GameSession")
	check_not_null(session, "GameSession 单例在场（/root 现有：%s）" % _describe_root())
	check_true(session != null and session.state != null, "会话状态被写入 GameSession")

	# 回菜单重来一次，验证「读取存档」真的能载入
	menu._refresh()
	menu.press_load_game()
	check_true(load_button.disabled == false, "有存档后读取按钮可用")
	var slot_button: Button = menu.slot_button(1)
	check_not_null(slot_button, "槽位按钮已生成")
	if slot_button != null:
		check_false(slot_button.disabled, "有档的槽位按钮可点")
		slot_button.emit_signal("pressed")
		check_eq(switched.size(), 2, "读取后再次进入游戏")
		check_eq(menu.pending_scene, menu.OVERWORLD_SCENE, "读档落在世界地图（设计 09 §二）")
		if switched.size() >= 2:
			check_eq(int(switched[1].slot), 1, "载入的是第 1 格")

	quit_button.emit_signal("pressed")
	check_eq(quit_called.size(), 1, "退出按钮触发退出回调")

	# 占位游戏场景：显示当前会话状态，并能退回菜单
	var placeholder = load(PLACEHOLDER_SCENE).instantiate()
	scene_tree.root.add_child(placeholder)
	placeholder.setup()
	check_gt(float(placeholder.state_lines().size()), 0.0, "占位场景能取到会话状态")
	check_true(placeholder.state_lines()[2].contains("队伍"), "占位场景展示队伍")
	var back: Button = placeholder.find_child("BackButton", true, false)
	check_not_null(back, "占位场景有返回按钮")
	var back_calls: Array = []
	placeholder.back_handler = func() -> void: back_calls.append(true)
	if back != null:
		back.emit_signal("pressed")
		check_eq(back_calls.size(), 1, "返回按钮触发回退")

	scene_tree.root.remove_child(placeholder)
	placeholder.free()
	scene_tree.root.remove_child(menu)
	menu.free()
	if session != null:
		session.clear()


## 三个槽全满时「新建游戏」：覆盖**最旧**那一格，并且提示玩家（不问就覆盖，必须说清楚）。
## 以前只验过「有空槽就放空槽」；「全满 → 覆盖最旧」这条分支没人跑过，
## 而它正是老玩家常常撞上的那条（三个档位都用完了）。
func _check_overwrite_oldest_slot() -> void:
	var db = get_db()
	var dir_path := TEST_DIR + "/full"
	_clean_dir(dir_path)      # 上一轮跑剩的文件会让「空槽」变成「覆盖」，先把目录清干净
	var store = SaveStoreScript.new(dir_path, SLOTS)
	store.ensure_dir()
	var controller = MenuControllerScript.new(db, store, GameStateScript.default_party_ids(db))
	for index in range(SLOTS):
		var made: Dictionary = controller.new_game()
		check_true(bool(made["ok"]), "第 %d 次新建成功" % (index + 1))
		check_false(bool(made["overwrite"]), "新档落在空槽（没有覆盖）")
	controller.refresh()
	check_eq(controller.save_count(), SLOTS, "三格都满了")

	# 把第 2 格改成「最旧」：直接改文件里的 saved_unix（save_slot 会用当前时间覆盖它）
	var path := store.slot_path(2)
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	data["saved_unix"] = 100
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(JSON.stringify(data))
	file.close()
	var slot1_before := int(store.describe_slot(1)["saved_unix"])
	var slot3_before := int(store.describe_slot(3)["saved_unix"])

	var again: Dictionary = controller.new_game()
	check_true(bool(again["ok"]), "全满时新建仍然成功")
	check_eq(int(again["slot"]), 2, "覆盖的是最旧那一格（第 2 格）")
	check_true(bool(again["overwrite"]), "结果里标着 overwrite=true")
	check_true(str(again["message"]).contains("已覆盖"), "提示写明覆盖了最旧存档：%s" % str(again["message"]))
	check_true(int(store.describe_slot(2)["saved_unix"]) > 100, "第 2 格已经换成了新档")
	check_eq(int(store.describe_slot(1)["saved_unix"]), slot1_before, "第 1 格没被动过")
	check_eq(int(store.describe_slot(3)["saved_unix"]), slot3_before, "第 3 格没被动过")


## 选到一个**损坏的存档**：菜单不许崩，要给得出原因、不给出半成品状态，并且仍然停在菜单（不进游戏）。
## 写失败那一侧有 `ReadOnlyStore` 用例；读失败这一侧以前没人验——「读取」点下去没反应或直接崩，
## 是玩家第一时间会碰到的（存档文件被杀毒软件截断、U 盘拔了之类）。
func _check_corrupt_slot_load() -> void:
	var db = get_db()
	var store = SaveStoreScript.new(TEST_DIR + "/corrupt", SLOTS)
	store.ensure_dir()
	var file := FileAccess.open(store.slot_path(1), FileAccess.WRITE)
	file.store_string("{ 这不是合法 JSON")
	file.close()
	var controller = MenuControllerScript.new(db, store)
	check_true(controller.has_saves(), "损坏的档也算「有存档」（列表里看得到）")
	check_true(controller.can_load(), "损坏的档也能点「读取」（点了要给原因，不是灰掉）")
	var labels := PackedStringArray(controller.slot_labels())
	check_true(labels[0].contains("损坏"), "槽位文案照实写「损坏」：%s" % labels[0])

	var loaded: Dictionary = controller.load_game(1)
	check_false(bool(loaded["ok"]), "损坏档读不进来")
	check_eq(str(loaded["action"]), MenuControllerScript.ACTION_NONE, "动作是「什么都不做」（不切场景）")
	check_null(loaded["state"], "不给出半成品状态")
	check_true(str(loaded["message"]).contains("损坏"), "给得出原因：%s" % str(loaded["message"]))
	check_true(controller.can_start_new_game(), "读档失败后仍能新建游戏（菜单没被卡住）")


## 读档时按当前配表收口：档里穿着「配表装不下」的装备时，读进来退回背包**并且整理完写回存档**。
## 起因：槽位是配表驱动的（设计可以删槽位或把 max_equip 改小），而穿戴记录留在存档里——
## 不写回的话每次读档都要重收一遍，档里一直留着旧记录（见框架说明决策 101）。
func _check_stale_equipped_writeback() -> void:
	var db = get_db()
	var store = SaveStoreScript.new(TEST_DIR + "/stale_equip", SLOTS)
	store.ensure_dir()
	var controller = MenuControllerScript.new(db, store)
	var created: Dictionary = controller.new_game()
	check_true(bool(created["ok"]), "先把一份新档写进去")
	var state = created["state"]
	var char_id := str(state.char_ids[0])
	var r1: String = state.inventory.add_equipment(db, "eq_ring_01")
	var r2: String = state.inventory.add_equipment(db, "eq_ring_02")
	var r3: String = state.inventory.add_equipment(db, "eq_ring_03")
	# 手工造一份「旧配表允许 3 枚戒指」的档：戒指槽塞 3 格（当前 max_equip=2）
	var slots: Dictionary = state.inventory.equipped.get(char_id, {})
	slots["ring"] = [r1, r2, r3]
	state.inventory.equipped[char_id] = slots
	check_true(bool(store.save_slot(1, state)["ok"]), "把这档「旧配表」写进去")

	var loaded: Dictionary = controller.load_game(1)
	check_true(bool(loaded["ok"]), "读档成功")
	check_eq(int(loaded["state"].reclaimed_equipped), 1, "读档时发现 1 件穿不上的装备")
	check_true(str(loaded["message"]).contains("退回"), "提示写明按配表退回了装备：%s" % str(loaded["message"]))
	check_false(loaded["state"].inventory.is_equipped(r3), "第 3 枚不再算穿着")
	check_true(loaded["state"].inventory.has_equipment(r3), "第 3 枚回到背包（实例没丢）")

	# 整理完要写回：再读一次不该还有可退的（不然每次读档都在重收，档里一直留着旧记录）
	var again: Dictionary = controller.load_game(1)
	check_eq(int(again["state"].reclaimed_equipped), 0, "写回后再读没有可退的（整理真的落盘了）")


func _clean_dir(path: String) -> void:
	if DirAccess.dir_exists_absolute(path):
		var dir := DirAccess.open(path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	DirAccess.make_dir_recursive_absolute(path)


func _describe_root() -> String:
	if scene_tree == null:
		return "无场景树"
	var names := PackedStringArray()
	for child in scene_tree.root.get_children():
		names.append(str(child.name))
	return ", ".join(names)
