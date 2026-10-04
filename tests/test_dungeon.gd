## 副本完成度与扫荡（03_副本_黑风寨.md「商业与重复性」「完成度」）。
##
## 完成度四项：宝箱 / 隐藏房间 / 隐藏 Boss / 事件，只作追求目标，不影响奖励。
## 扫荡：已通关层（该层带敌人的房间全清过）可扫荡，**只结算掉落、不给经验**。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const DungeonServiceScript := preload("res://src/core/dungeon_service.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")

const SCENE := "scene_heifengzhai"
const SEED := 20261003


func suite_name() -> String:
	return "副本完成度与扫荡"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var service = DungeonServiceScript.new(db, state, RngServiceScript.new(SEED))
	_check_progress_items(db, state, service)
	_check_progress_math(db, state, service)
	_check_floors(db, state, service)
	_check_sweep(db, state, service)
	_check_save(db, state)
	_check_overflow(db)
	_check_exit_symmetry(db)


## 完成度的分母来自表：宝箱 4、隐藏房间 5、隐藏 Boss 1（醉刀客）、事件 2
func _check_progress_items(db, state, service) -> void:
	var snapshot: Dictionary = service.progress(SCENE)
	check_eq(int(snapshot["chests"]["total"]), 4, "黑风寨 4 个宝箱")
	check_eq(int(snapshot["hidden_rooms"]["total"]), 5, "5 个隐藏房间")
	check_eq(int(snapshot["hidden_boss"]["total"]), 1, "隐藏 Boss 只有醉刀客（大寨主算章节 Boss）")
	check_eq(int(snapshot["events"]["total"]), 2, "2 个事件触发（潜行入寨／锈剑共鸣）")
	check_eq(int(snapshot["percent"]), 0, "新档完成度 0%")

	# 另一张图的分母也跟着表走
	var cave: Dictionary = service.progress("scene_cave")
	check_eq(int(cave["chests"]["total"]), 0, "塌陷山洞没有宝箱")
	check_eq(int(cave["hidden_rooms"]["total"]), 1, "山洞 1 个隐藏房间（塌方深处）")


## 记录齐了就是 100%，只记一半按比例算
func _check_progress_math(db, state, service) -> void:
	state.record_dungeon(SCENE, "chests", "hf1_shed|drop_chest_copper")
	state.record_dungeon(SCENE, "chests", "hf1_vault|drop_chest_silver")
	state.record_dungeon(SCENE, "rooms_entered", "hf1_deep")
	state.record_dungeon(SCENE, "bosses", "en_hidden_drunk")
	state.record_dungeon(SCENE, "triggers", "trig_rusty_sword")
	var half: Dictionary = service.progress(SCENE)
	check_eq(int(half["chests"]["done"]), 2, "开了 2 个宝箱")
	check_eq(int(half["hidden_rooms"]["done"]), 1, "进过 1 个隐藏房间")
	check_eq(int(half["hidden_boss"]["done"]), 1, "醉刀客已击败")
	check_eq(int(half["events"]["done"]), 1, "触发过 1 个事件")
	check_eq(int(half["done"]), 5, "合计 5 项")
	check_eq(int(half["total"]), 12, "分母 4 + 5 + 1 + 2 = 12")
	check_eq(int(half["percent"]), 42, "5/12 → 42%（四舍五入）")
	check_true(service.progress_text(SCENE).contains("完成度 42%"), "HUD 文案带上完成度：%s" % service.progress_text(SCENE))

	# 重复记录不叠加
	check_false(state.record_dungeon(SCENE, "chests", "hf1_shed|drop_chest_copper"), "同一项重复记录会被拒")
	# 补齐剩下的
	for chest_id: String in ["hf1_secret|drop_chest_silver", "hf2_vault|drop_chest_gold"]:
		state.record_dungeon(SCENE, "chests", chest_id)
	for room_id: String in ["hf1_vault", "hf1_secret", "hf2_tunnel", "hf3_dungeon"]:
		state.record_dungeon(SCENE, "rooms_entered", room_id)
	state.record_dungeon(SCENE, "triggers", "trig_stealth_clear")
	check_eq(int(service.progress(SCENE)["percent"]), 100, "全清之后 100%")


## 层视图：每层哪些房间带敌人、清过没、能不能扫荡
func _check_floors(db, state, service) -> void:
	var fresh = solo_state(db)
	var plan = DungeonServiceScript.new(db, fresh, RngServiceScript.new(SEED))
	var listed: Array = plan.floors(SCENE)
	check_eq(listed.size(), 3, "黑风寨三层")
	var first: Dictionary = listed[0]
	check_eq(int(first["floor"]), 1, "第一项是第 1 层")
	check_eq(Array(first["enemy_rooms"]).size(), 1, "第 1 层只有前院有敌人")
	check_false(bool(first["sweepable"]), "没打过就不能扫荡")
	check_true(plan.sweepable_floors(SCENE).is_empty(), "新档没有可扫荡的层")
	fresh.record_dungeon(SCENE, "rooms", "hf1_yard")
	check_true(bool(plan.floors(SCENE)[0]["sweepable"]), "清过前院后第 1 层可扫荡")
	check_eq(plan.sweepable_floors(SCENE), [1], "可扫荡层列出第 1 层")


## 扫荡：只能扫已通关的层、只结算掉落（不给经验）
func _check_sweep(db, state, service) -> void:
	var fresh = solo_state(db)
	var runner = DungeonServiceScript.new(db, fresh, RngServiceScript.new(SEED))
	var early: Dictionary = runner.sweep(SCENE, 1)
	check_false(bool(early["ok"]), "没通关的层不能扫荡")
	check_true(str(early["error"]).contains("还没通关"), "说明差哪些房间：%s" % early["error"])

	fresh.record_dungeon(SCENE, "rooms", "hf1_yard")
	var exp_before := int(fresh.party_exp)
	var money_before := int(fresh.inventory.money)
	var equipment_before: int = fresh.inventory.equipment_count()
	var swept: Dictionary = runner.sweep(SCENE, 1)
	check_true(bool(swept["ok"]), "通关的层能扫荡：%s" % swept["error"])
	check_eq(int(fresh.party_exp), exp_before, "扫荡不给经验（设计：仅结算掉落）")
	check_gt(float(fresh.inventory.money - money_before), 0.0, "扫荡结算了掉落里的铜钱")
	check_gt(float(Array(swept["drops"]).size()), 0.0, "掷出了掉落条目")
	check_true(str(swept["summary"]).contains("铜钱"), "摘要里有铜钱：%s" % swept["summary"])
	check_true(
		fresh.inventory.equipment_count() >= equipment_before,
		"掉落装备会进背包（%d → %d）" % [equipment_before, fresh.inventory.equipment_count()],
	)
	# 可以反复扫——这是刷子循环的效率口
	var again: Dictionary = runner.sweep(SCENE, 1)
	check_true(bool(again["ok"]), "同一层可以重复扫荡")


## 背包满：掉落记成「没捡起来」，不把整场结算判成失败（扫荡与战斗共用 BattleReward）
func _check_overflow(db) -> void:
	var state = solo_state(db)
	state.inventory.add_item(db, "item_herb", 99)     # 草药堆叠上限就是 99
	var applied: Dictionary = BattleRewardScript.grant(
		db, state, 10, 5, [{"item_id": "item_herb", "qty": 5}]
	)
	check_true(bool(applied["ok"]), "背包满不算结算失败：%s" % str(applied["errors"]))
	check_eq(Array(applied["overflow"]).size(), 1, "溢出单独记一条")
	if not Array(applied["overflow"]).is_empty():
		var entry: Dictionary = Array(applied["overflow"])[0]
		check_eq(str(entry["item_id"]), "item_herb", "溢出的是草药")
		check_eq(int(entry["qty"]), 5, "溢出数量写清")
		check_true(str(entry["reason"]).contains("满"), "写上原因：%s" % str(entry["reason"]))
	check_eq(state.party_exp, 10, "经验照常入账")
	check_eq(state.inventory.money, 5, "铜钱照常入账")
	check_eq(state.inventory.count("item_herb"), 99, "背包没被塞爆")

	# 部分入包：这一堆还剩 2 格、奖励 5 个 → 进去 2、**溢出 3**（写的是没捡起来的数量，不是 5）
	var partial_state = solo_state(db)
	partial_state.inventory.add_item(db, "item_herb", 97)
	var partial: Dictionary = BattleRewardScript.grant(
		db, partial_state, 0, 0, [{"item_id": "item_herb", "qty": 5}]
	)
	check_true(bool(partial["ok"]), "部分入包也不算结算失败")
	check_eq(Array(partial["items"]).size(), 1, "入账清单里记了这一条")
	if not Array(partial["items"]).is_empty():
		var item_entry: Dictionary = Array(partial["items"])[0]
		check_eq(int(item_entry["qty"]), 2, "真正进去 2 个")
		check_eq(int(item_entry["overflow"]), 3, "这一条自己记着溢出 3")
	check_eq(Array(partial["overflow"]).size(), 1, "溢出清单单独一条")
	if not Array(partial["overflow"]).is_empty():
		check_eq(int(Array(partial["overflow"])[0]["qty"]), 3, "溢出数量是 3（不是 5）")
	check_eq(int(partial_state.inventory.count("item_herb")), 99, "这一堆补满")


## 完成度与保底都进存档
func _check_save(db, state) -> void:
	state.record_dungeon(SCENE, "rooms", "hf2_poison")
	state.pity = {"version": 1, "counters": {"drop_bd_elite|slot_1": 3}}
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_not_null(back, "带完成度的存档能读回来")
	if back == null:
		return
	check_eq(back.migrated_from, 0, "同版本往返不需要迁移")
	check_true(Array(back.dungeon_record(SCENE)["rooms"]).has("hf2_poison"), "打过的房间往返一致")
	check_eq(int(Dictionary(back.pity.get("counters", {})).get("drop_bd_elite|slot_1", 0)), 3, "保底计数往返一致")
	check_eq(back.pity_tracker().attempts("drop_bd_elite|slot_1"), 3, "保底计数器读得到")


## 07 §九 第 3 条：地图上走廊是双向的，所以 `dungeon_room.exit_rooms` 必须成对声明——
## a→b 写了、b→a 没写就是漏了一格。以前只由 validate_tables 5.28 与 verify_maps 各报一次
## 建议级提示，漏一条谁也不会红；2026-10-04 把表补成对称后升成断言，防止再漂回去（决策 326）。
func _check_exit_symmetry(db) -> void:
	var one_way: Array[String] = []
	for row: Resource in db.rows("dungeon_room"):
		var room_id := str(row.room_id)
		for exit_id in str(row.exit_rooms).split("|", false):
			var target := exit_id.strip_edges()
			if target == "":
				continue
			var back: Resource = db.get_row("dungeon_room", target)
			if back == null:
				continue     # 指向不存在的房间由构建期引用校验报，这里不重复
			if not str(back.exit_rooms).split("|", false).has(room_id):
				one_way.append("%s→%s" % [room_id, target])
	var detail := "无" if one_way.is_empty() else ", ".join(PackedStringArray(one_way))
	check_eq(one_way.size(), 0, "出口成对声明（单向的：%s）" % detail)
