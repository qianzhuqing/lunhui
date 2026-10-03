## 存档槽：枚举、读写、损坏兜底、选槽策略。
##
## 用 res://.logs/ 下的临时目录，不依赖 user:// 的写权限（沙箱里 user:// 不可写）。
extends "res://tests/test_case.gd"

const SaveStoreScript := preload("res://src/core/save_store.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

const TEST_DIR := "res://.logs/test_save_store"
const SLOTS := 3


func suite_name() -> String:
	return "存档槽读写与选槽"


func run() -> void:
	var db = get_db()
	_clean_dir(TEST_DIR)
	var store = SaveStoreScript.new(TEST_DIR, SLOTS)
	check_true(store.ensure_dir(), "存档目录应能创建")
	check_eq(store.default_dir(), SaveStoreScript.DEFAULT_DIR, "没有覆盖时默认写 user://saves")
	_check_empty(store)
	_check_round_trip(db, store)
	_check_every_field_round_trip(db, store)
	_check_corrupt(store)
	_check_slot_picking(db, store)
	_check_delete(store)
	_check_save_size(db)


func _check_empty(store) -> void:
	check_false(store.has_any_save(), "空目录里没有存档")
	check_eq(store.first_free_slot(), 1, "第一个空槽是 1")
	check_eq(store.oldest_slot(), -1, "没有存档时没有最旧槽")
	var slots: Array = store.list_slots()
	check_eq(slots.size(), SLOTS, "槽位数量与配置一致")
	for entry: Dictionary in slots:
		check_false(bool(entry["exists"]), "第 %d 格应为空" % entry["slot"])
		check_eq(str(entry["label"]), "空存档位", "空槽的显示文案")


func _check_round_trip(db, store) -> void:
	var state = GameStateScript.new_game(db, "normal")
	# 0.10.0 起开局队伍 = `recruit_def.is_initial=1` 的成员（设计 09 §3.2：开局只有书生一人，
	# 同伴在剧情里加入），不再取 character_base 的前 4 行。按**表**核，不写死人数。
	var want_party := GameStateScript.default_party_ids_from_recruit(db)
	check_eq(
		state.party_size(), want_party.size(),
		"开局队伍取 recruit_def 的初始成员（%d 人）" % want_party.size()
	)
	check_eq(str(state.char_ids), str(want_party), "开局队伍逐人一致（顺序也照 recruit_def）")
	check_eq(state.difficulty_id, "normal", "新游戏默认普通难度")

	var saved: Dictionary = store.save_slot(1, state)
	check_true(saved["ok"], "存档应写入成功：%s" % saved["error"])
	check_true(store.slot_exists(1), "槽位 1 有文件")
	check_true(FileAccess.file_exists(store.slot_path(1)), "存档路径与预期一致")
	check_true(store.has_any_save(), "写入后 has_any_save 为真")

	var loaded: Dictionary = store.load_slot(1)
	check_true(loaded["ok"], "存档应能读回：%s" % loaded["error"])
	if loaded["ok"]:
		var restored = loaded["state"]
		check_eq(restored.party_size(), state.party_size(), "队伍人数读回一致")
		check_eq(str(restored.char_ids), str(state.char_ids), "队伍成员读回一致")
		check_eq(restored.difficulty_id, "normal", "难度读回一致")
		check_eq(restored.slot, 1, "读回后槽位号会补上")
		check_gt(float(restored.saved_unix), 0.0, "保存时间被记录")
		check_true(restored.short_label(db).contains("Lv"), "存档条目显示队伍与等级")
		check_true(restored.short_label(db).contains("-"), "存档条目显示保存时间")

	var file := FileAccess.open(store.slot_path(1), FileAccess.READ)
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	file.close()
	check_true(GameStateScript.is_valid_dict(parsed), "落盘的是合法 JSON 结构")
	check_eq(int(Dictionary(parsed).get("version", 0)), GameStateScript.VERSION, "存档带结构版本号")

	var slots: Array = store.list_slots(db)
	check_true(bool(slots[0]["exists"]), "枚举能看到槽位 1")
	check_false(bool(slots[0]["corrupt"]), "刚写的存档不应判定为损坏")
	check_eq(store.first_free_slot(), 2, "写入槽 1 后第一个空槽是 2")


## 全字段往返：把**每一个**存档字段都设成非默认值，落盘再读回，两份 to_dict 必须零差异。
##
## 这条兜的是「字段级遗漏」：写了却没人读、读了却没写、或者新字段只加了一半——
## 逐个字段写断言容易漏，比字典更省事也更严。
func _check_every_field_round_trip(db, store) -> void:
	var state = GameStateScript.new_game(db, "normal")
	var char_id := str(state.char_ids[0])
	state.difficulty_id = "nightmare"
	state.chapter_id = "chapter_99"
	state.created_unix = 111
	state.char_levels[char_id] = 12

	# 背包：材料 + 带词条的装备 + 穿戴映射
	state.inventory.money = 321
	state.inventory.add_item(db, "item_herb", 5)
	var ring: String = state.inventory.add_equipment(
		db, "eq_ring_01",
		[{"affix_id": "af_str_01", "target": "attr:str", "value_kind": "attr_point", "value": 2.0}],
	)
	var equipped: Dictionary = state.inventory.equip(db, char_id, state.level_of(char_id), "", ring)
	check_true(bool(equipped["ok"]), "测试用的戒指能戴上：%s" % equipped.get("error", ""))

	state.char_allocations[char_id] = {"str": 2, "con": 1}
	state.party_exp = 456
	state.skill_mastery[char_id] = {"sk_xuanwei_01": 7}
	state.learn_skill(char_id, "sk_wudu_01")
	state.set_loadout(char_id, PackedStringArray(["sk_xuanwei_01"]), PackedStringArray())
	state.record_purchase("bld_smith", "eq_sword_01", 2)
	state.push_buyback("bld_smith", {"item_id": "item_herb", "qty": 1, "price": 5})
	state.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper")
	state.record_dungeon("scene_heifengzhai", "triggers", "trig_wine")
	state.record_dungeon("scene_heifengzhai", "rooms", "hf1_yard")
	state.record_dungeon("scene_heifengzhai", "rooms_entered", "hf1_shed")
	state.record_dungeon("scene_heifengzhai", "bosses", "hf3_dungeon")
	var pity = state.pity_tracker()
	pity.register_attempt("drop_bd_elite|dr_elite_03")
	state.store_pity(pity)
	state.reveal_node("n_cave_collapse")
	state.record_event_check("ev_force_gate", "failed")
	state.set_flag("flag_demo")
	state.mark_first_kill("drop_bd_boss")
	state.set_current_hp(char_id, 33)
	state.set_world_pos(Vector2(1234.5, -678.25))

	var saved: Dictionary = store.save_slot(2, state)
	check_true(bool(saved["ok"]), "全字段存档写入成功：%s" % saved["error"])
	var loaded: Dictionary = store.load_slot(2, db)
	check_true(bool(loaded["ok"]), "全字段存档读出成功：%s" % loaded["error"])
	if not loaded["ok"]:
		return
	var loaded_state = loaded["state"]
	check_eq(loaded_state.migrated_from, 0, "当前版本的档不需要迁移")
	var diff := _diff_path(state.to_dict(), loaded_state.to_dict())
	check_eq(diff, "", "全字段往返零差异（第一处差异：%s）" % diff)
	# 24 = 22 + v12 的 field_buffs（战斗外增益）+ v13 的 world_pos（大地图坐标）；新增字段要一起登记
	check_eq(int(Dictionary(state.to_dict()).size()), 24, "to_dict 的字段数没变（新增字段要一起登记）")
	check_eq(loaded_state.world_position(), Vector2(1234.5, -678.25), "大地图坐标（v13）往返一致")
	# 自查：比较器本身真的能报差异，否则上面那条断言就是空的
	check_ne(_diff_path({"a": 1, "b": 2}, {"a": 1}), "", "比较器能发现丢字段")
	check_ne(_diff_path({"a": 1}, {"a": 2}), "", "比较器能发现值不同")
	check_ne(_diff_path({"a": [1, 2]}, {"a": [1, 2, 3]}), "", "比较器能发现数组长度不同")
	check_eq(_diff_path({"a": [1, {"b": true}]}, {"a": [1, {"b": true}]}), "", "比较器对相同结构返回空")


## 递归比较两份 JSON 结构，返回第一处不同的路径；完全一致返回空串
func _diff_path(a: Variant, b: Variant, path: String = "root") -> String:
	if a is Dictionary and b is Dictionary:
		var keys := {}
		for key: Variant in a:
			keys[key] = true
		for key: Variant in b:
			keys[key] = true
		for key: Variant in keys:
			if not a.has(key):
				return "%s.%s（读回时多了）" % [path, key]
			if not b.has(key):
				return "%s.%s（读回时丢了）" % [path, key]
			var sub := _diff_path(a[key], b[key], "%s.%s" % [path, key])
			if not sub.is_empty():
				return sub
		return ""
	if a is Array and b is Array:
		if a.size() != b.size():
			return "%s（长度 %d vs %d）" % [path, a.size(), b.size()]
		for index in range(a.size()):
			var sub := _diff_path(a[index], b[index], "%s[%d]" % [path, index])
			if not sub.is_empty():
				return sub
		return ""
	if a is float or b is float:
		if not is_equal_approx(float(a), float(b)):
			return "%s（%s vs %s）" % [path, a, b]
		return ""
	if str(a) != str(b):
		return "%s（%s vs %s）" % [path, a, b]
	return ""


func _check_corrupt(store) -> void:
	var file := FileAccess.open(store.slot_path(2), FileAccess.WRITE)
	file.store_string("{ 这不是合法 JSON")
	file.close()
	var entry: Dictionary = store.describe_slot(2)
	check_true(bool(entry["exists"]), "损坏文件仍算「存在」")
	check_true(bool(entry["corrupt"]), "非 JSON 应判定为损坏")
	check_eq(str(entry["label"]), "存档损坏", "损坏槽的显示文案")
	check_false(store.load_slot(2)["ok"], "损坏存档读不出来而不是崩溃")
	check_true(store.has_any_save(), "有损坏存档时仍算有存档")

	# 结构对但字段缺失，同样按损坏处理
	var half := FileAccess.open(store.slot_path(3), FileAccess.WRITE)
	half.store_string(JSON.stringify({"version": 1, "char_ids": []}))
	half.close()
	check_true(bool(store.describe_slot(3)["corrupt"]), "字段缺失的存档判定为损坏")


func _check_slot_picking(db, store) -> void:
	# 槽 1 正常、2/3 损坏 → 没有空槽，先回收损坏槽
	var placement: Dictionary = store.slot_for_new_game()
	check_true(placement["ok"], "满了也要给出可用槽")
	check_true(bool(placement["overwrite"]), "没有空槽时是覆盖")
	check_eq(int(placement["slot"]), 2, "优先回收损坏的槽位")
	check_eq(store.oldest_slot(), 1, "最旧可用存档跳过损坏槽")

	# 人为写三个**可用**且时间不同的存档，验证「最旧」是按 saved_unix 判定
	_clean_dir(TEST_DIR)
	for slot in range(1, SLOTS + 1):
		var state = GameStateScript.new_game(db, "normal", PackedStringArray(), slot)
		var data: Dictionary = state.to_dict()
		data["saved_unix"] = 1000 * slot
		var file := FileAccess.open(store.slot_path(slot), FileAccess.WRITE)
		file.store_string(JSON.stringify(data))
		file.close()
	check_eq(store.oldest_slot(), 1, "saved_unix 最小的槽最旧")
	check_eq(int(store.slot_for_new_game()["slot"]), 1, "无空槽时覆盖最旧的那个")
	check_eq(store.first_free_slot(), -1, "三个槽都占满")
	check_eq(store.first_corrupt_slot(), -1, "没有损坏槽可回收")


func _check_delete(store) -> void:
	check_true(store.delete_slot(1), "删档成功")
	check_false(store.slot_exists(1), "删档后文件不在了")
	check_false(store.delete_slot(1), "删不存在的档返回 false")
	check_eq(store.first_free_slot(), 1, "删档后槽位重新可用")


## 存档体积：把「4 人 + 大背包（道具堆满 + 30 件装备）+ 熟练度/加点/装配/副本记录/首杀/揭开」
## 都塞进去，量一份 JSON 有多大，并验证**读回来再写回去不会膨胀**。
## 不追求小，只防「数量级」的意外：比如不小心把整张配置表、每帧日志或重复实例写进存档
## （这类错不会报错，只会让存档悄悄涨到几百 KB～几 MB）。实际值会打进日志，给后续做参考。
func _check_save_size(db) -> void:
	var wide = table_with_n_chars(4)
	var state = party_state(wide, 4)
	var ids := PackedStringArray(state.char_ids)
	state.inventory.money = 999999
	state.party_exp = 123456
	for row: Resource in db.rows("item_base"):
		if str(row.item_type) == "currency":
			continue
		state.inventory.add_item(db, str(row.item_id), maxi(1, int(row.stack_max)))
	for base_id: String in ["eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01", "eq_acc_01"]:
		for _n in range(6):
			var instance_id: String = state.inventory.add_equipment(db, base_id)
			state.inventory.equip(
				wide, ids[0], state.level_of(ids[0]),
				str(wide.get_row("character_base", ids[0]).weapon_type), instance_id,
			)
	for char_id: String in ids:
		state.set_mastery(char_id, "sk_xuanwei_01", 5)
		state.spend_point(wide, char_id, "str")
	state.record_dungeon("scene_heifengzhai", "chests", "hf1_yard|chest_01")
	state.mark_first_kill("drop_bd_boss")

	var text := JSON.stringify(state.to_dict(), "  ")
	var kb := float(text.length()) / 1024.0
	print("  · 存档体积观测：4 人 + 大背包 = %.1f KB（%d 字符）" % [kb, text.length()])
	check_gt(kb, 1.0, "真写进去了东西（实际 %.1f KB）" % kb)
	check_lt(kb, 64.0, "存档体积在合理量级（实际 %.1f KB，上限 64 KB）" % kb)

	var back = GameStateScript.from_dict(JSON.parse_string(text), wide)
	check_not_null(back, "这份大档能读回来")
	if back != null:
		var again := JSON.stringify(back.to_dict(), "  ")
		var growth := float(again.length() - text.length()) / float(maxi(1, text.length()))
		check_lt(absf(growth), 0.02, "读回来再写回去体积基本不变（变化 %.2f%%）" % (growth * 100.0))


func _clean_dir(path: String) -> void:
	if DirAccess.dir_exists_absolute(path):
		var dir := DirAccess.open(path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	DirAccess.make_dir_recursive_absolute(path)
