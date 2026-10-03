## 设备设置（音量）与它在枢纽页上的入口。
##
## 由来：音效接上以后玩家**关不掉**——设计没有设置界面／暂停菜单，而音量是玩家随时会想改的东西。
## 这条用例钉三件事：① 设置文件读写（读不到／读坏／写不进去都要有说法，**绝不悄悄静音**）；
## ② 音量真的写到那条总线上（不是只改了个数字）；③ 枢纽页那个按钮按一下轮换一档、当场生效。
extends "res://tests/test_case.gd"

const SettingsStoreScript := preload("res://src/core/settings_store.gd")
const SfxScript := preload("res://src/audio/sfx.gd")

const HUB_SCENE := "res://scenes/placeholder_game.tscn"
const TEST_DIR := "res://.logs/test_settings"


func suite_name() -> String:
	return "设置：音量与落盘"


func run() -> void:
	_check_store_round_trip()
	_check_store_tolerates_broken()
	_check_volume_hits_the_bus()
	_check_hub_button()


func _check_store_round_trip() -> void:
	DirAccess.make_dir_recursive_absolute(TEST_DIR)
	var store = SettingsStoreScript.new(TEST_DIR)
	# 上一轮跑留下的文件会让「没有文件时用默认值」这条断言变成时红时绿——先清干净再验
	DirAccess.remove_absolute(store.file_path())
	var missing: Dictionary = store.load_volume()
	check_true(
		bool(missing["ok"]) and is_equal_approx(float(missing["volume"]), 1.0),
		"还没有设置文件时用默认音量 1.0（实际 %s）" % str(missing["volume"]),
	)
	var saved: Dictionary = store.save_volume(0.25)
	check_true(bool(saved["ok"]), "写设置：%s" % str(saved.get("error", "")))
	var loaded: Dictionary = store.load_volume()
	check_true(bool(loaded["ok"]), "读设置：%s" % str(loaded.get("error", "")))
	check_float(float(loaded["volume"]), 0.25, "写进去 0.25，读回来还是 0.25")
	check_true(
		FileAccess.file_exists(store.file_path()),
		"设置落在存档目录旁边的 settings.cfg：%s" % store.file_path(),
	)


func _check_store_tolerates_broken() -> void:
	# 读坏：文件内容不是合法 cfg → 报错 + 退回默认音量（**不是 0＝静音**）
	var broken_dir := TEST_DIR + "/broken"
	DirAccess.make_dir_recursive_absolute(broken_dir)
	var broken = SettingsStoreScript.new(broken_dir)
	var handle := FileAccess.open(broken.file_path(), FileAccess.WRITE)
	check_not_null(handle, "能造一个坏设置文件")
	if handle != null:
		handle.store_string("[audio\nvolume = 这不是数字")
		handle.close()
	var loaded: Dictionary = broken.load_volume()
	check_false(bool(loaded["ok"]), "坏掉的设置文件要报错（不静默吞掉）")
	check_float(float(loaded["volume"]), 1.0, "坏掉时退回默认音量（不是 0＝静音）")

	# 写不进去：目录被一个同名文件挡着 → 如实返回失败，不抛错、不挡游戏
	var blocker_dir := TEST_DIR + "/blocker"
	var blocker := FileAccess.open(blocker_dir, FileAccess.WRITE)
	check_not_null(blocker, "造一个挡路的同名文件")
	if blocker != null:
		blocker.store_string("x")
		blocker.close()
	var blocked = SettingsStoreScript.new(blocker_dir + "/sub")
	var failed: Dictionary = blocked.save_volume(0.5)
	check_false(bool(failed["ok"]), "写不进去时如实返回失败：%s" % str(failed.get("error", "")))


func _check_volume_hits_the_bus() -> void:
	var bus := AudioServer.get_bus_index(SfxScript.SOUND_BUS)
	check_gt(float(bus), 0.0, "音效总线在（%s）" % SfxScript.SOUND_BUS)
	# `persist=false`：用例不写玩家的设置文件
	SfxScript.set_volume(0.25, false)
	check_float(SfxScript.volume(), 0.25, "音量记在 Sfx 里")
	check_float(
		db_to_linear(AudioServer.get_bus_volume_db(bus)), 0.25,
		"0.25 真的写到了总线上（不是只改了个数字）", 0.01,
	)
	check_eq(SfxScript.volume_text(), "音量：25%", "文案写百分比")
	check_float(SfxScript.cycle_volume(false), 0.5, "轮换到下一档")
	SfxScript.set_volume(1.0, false)
	check_float(SfxScript.cycle_volume(false), 0.0, "到顶回到静音")
	check_eq(SfxScript.volume_text(), "音量：静音", "0 说「静音」不说 0%")
	check_lt(
		db_to_linear(AudioServer.get_bus_volume_db(bus)), 0.001,
		"静音时总线确实听不见（%f）" % db_to_linear(AudioServer.get_bus_volume_db(bus)),
	)
	SfxScript.set_volume(1.0, false)
	check_float(
		db_to_linear(AudioServer.get_bus_volume_db(bus)), 1.0,
		"恢复 100%：总线回到原音量", 0.01,
	)


func _check_hub_button() -> void:
	if scene_tree == null:
		fail("没有注入场景树")
		return
	var session_node = scene_tree.root.get_node_or_null("GameSession")
	if session_node != null:
		session_node.set_state(solo_state(get_db()))
	var hub = load(HUB_SCENE).instantiate()
	scene_tree.root.add_child(hub)
	hub.setup()
	var button: Button = hub.find_child("VolumeButton", true, false)
	check_not_null(button, "枢纽页有音量按钮（设计还没有设置界面，这里是游戏内唯一随时到得了的菜单）")
	if button != null:
		SfxScript.set_volume(1.0, false)
		button.text = SfxScript.volume_text()
		button.emit_signal("pressed")
		check_float(SfxScript.volume(), 0.0, "按一下从 100% 轮到静音")
		check_true(button.text.contains("静音"), "按钮文案跟着更新：%s" % button.text)
		button.emit_signal("pressed")
		check_float(SfxScript.volume(), 0.25, "再按一下回到 25%")
		check_true(button.text.contains("25%"), "按钮文案继续跟着走：%s" % button.text)
		var notice: Label = hub.find_child("Notice", true, false)
		check_not_null(notice, "枢纽页有反馈行")
		if notice != null:
			check_true(notice.text.contains("音量"), "按完给一句反馈：%s" % notice.text)
		# 写不进去（沙箱里的 user://）**不许挡住使用**：音量已经在本次运行内生效，
		# 只记一条警告——与「存档写不进去不挡开局」同一条纪律
		check_float(
			db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index(SfxScript.SOUND_BUS))),
			0.25, "按钮按下后总线就变（落盘失败也不影响）", 0.01,
	)
	scene_tree.root.remove_child(hub)
	hub.free()
	SfxScript.set_volume(1.0, false)
