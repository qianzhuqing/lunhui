## 存档时机（02_地图与明雷.md「存档规则」）：城镇手动存、大地图切图自动存、副本内自动存。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const SaveServiceScript := preload("res://src/core/save_service.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")

const TEST_DIR := "res://.logs/test_save_timing"
const CHAR_ID := "scholar_fallen"


func suite_name() -> String:
	return "存档时机"


func run() -> void:
	var db = get_db()
	_clean_dir(TEST_DIR)
	_check_service(db)
	_check_round_trip(db)


## SaveService：没槽位不让存；有槽位落盘并记下原因
func _check_service(db) -> void:
	var store = SaveStoreScript.new(TEST_DIR, 3)
	var bare = solo_state(db)
	var service = SaveServiceScript.new(store, bare)
	check_false(bool(service.can_save()["ok"]), "没有存档槽时不能存")
	check_true(str(service.can_save()["error"]).contains("存档槽"), "说明是缺槽位：%s" % service.can_save()["error"])
	var refused: Dictionary = service.save("测试")
	check_false(bool(refused["ok"]), "没槽位时保存被拒")
	check_false(store.slot_exists(2), "被拒时不会写文件")

	# 给个槽位：正常落盘
	bare.slot = 2
	var saved: Dictionary = service.save("城镇存档点")
	check_true(bool(saved["ok"]), "有槽位就能存：%s" % saved["error"])
	check_eq(int(saved["slot"]), 2, "写在当前槽位")
	check_true(store.slot_exists(2), "文件真的落盘了")
	check_gt(float(bare.saved_unix), 0.0, "更新时间戳")
	check_eq(service.last_reason, "城镇存档点", "记下存档原因")
	check_false(service.last_auto, "手动存档标记对")
	check_true(service.describe().contains("保存成功"), "提示文案：%s" % service.describe())
	# 自动存档：文案不同，仍是同一个槽
	var auto: Dictionary = service.save("清房间", true)
	check_true(bool(auto["ok"]) and service.last_auto, "自动存档也走同一条路")
	check_true(service.describe().contains("已自动保存"), "自动存档文案：%s" % service.describe())
	check_eq(int(bare.slot), 2, "自动存档不会换槽")


## 存档内容真的是当前进度：等级、加点、背包、完成度都要在
func _check_round_trip(db) -> void:
	var store = SaveStoreScript.new(TEST_DIR, 3)
	var state = solo_state(db)
	state.slot = 1
	state.append_level(CHAR_ID)
	state.spend_point(db, CHAR_ID, "str")
	state.inventory.add_item(db, "item_herb", 3)
	state.inventory.add_equipment(db, "eq_sword_02")
	state.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper")
	state.record_event_check("ev_shed_trap", "done")
	state.party_exp = 77
	var level_before := state.level_of(CHAR_ID)
	var str_before := int(CharacterSheetScript.new(db, state, CHAR_ID).total_attrs()["str"])

	var service = SaveServiceScript.new(store, state)
	check_true(bool(service.save("测试落盘")["ok"]), "保存成功")
	var loaded: Dictionary = store.load_slot(1, db)
	check_true(bool(loaded["ok"]), "读回来成功：%s" % loaded.get("error", ""))
	if not bool(loaded["ok"]):
		return
	var back = loaded["state"]
	check_eq(back.level_of(CHAR_ID), level_before, "等级在存档里")
	check_eq(int(CharacterSheetScript.new(db, back, CHAR_ID).total_attrs()["str"]), str_before, "加点效果在存档里")
	check_eq(back.inventory.count("item_herb"), 3, "背包在存档里")
	check_eq(back.inventory.equipment_count(), 3, "装备实例在存档里（0.10.2 起初始装备两件）")
	check_eq(back.party_exp, 77, "队伍经验在存档里")
	check_true(Array(back.dungeon_record("scene_heifengzhai")["chests"]).has("hf1_shed|drop_chest_copper"), "完成度在存档里")
	check_eq(back.event_check_result("ev_shed_trap"), "done", "事件判定在存档里")


func _clean_dir(path: String) -> void:
	if DirAccess.dir_exists_absolute(path):
		var dir := DirAccess.open(path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	DirAccess.make_dir_recursive_absolute(path)
