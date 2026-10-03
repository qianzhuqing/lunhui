## 端到端：加载表 → 组队 → 打一场明雷 → 结算奖励与掉落。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const DropResolverScript := preload("res://src/core/drop_resolver.gd")
const PityTrackerScript := preload("res://src/core/pity_tracker.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const ShopServiceScript := preload("res://src/core/shop_service.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const LevelServiceScript := preload("res://src/core/level_service.gd")
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")

const SEED := 2026


func suite_name() -> String:
	return "端到端：明雷战斗与掉落"


func run() -> void:
	var db = get_db()
	check_eq(TableValidatorScript.validate(db).size(), 0, "开局校验通过")

	var factory = EnemyFactoryScript.new(db)
	var allies := _party(db, 5)
	var enemies: Array = factory.create_team("team_bandit_patrol", "normal")
	check_eq(enemies.size(), 3, "山贼巡山队 2 刀 1 弓")

	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var result: Dictionary = simulator.simulate(allies, enemies)
	check_eq(result["winner"], BattleSimulatorScript.WINNER_ALLY, "5 级两人组能清掉巡山队")
	check_gt(float(result["rewards"]["exp"]), 0.0, "打完有经验入账")
	check_gt(float(result["rewards"]["money"]), 0.0, "打完有铜钱入账")

	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED + 1), PityTrackerScript.new())
	var bonus: float = DropResolverScript.party_drop_bonus(allies)
	check_in_range(bonus, 0.0, 0.25, "全队掉落机缘不超过保守方案上限")

	var collected: Dictionary = {}
	for enemy in enemies:
		for entry: Dictionary in resolver.roll_group(enemy.drop_group, "normal", {"drop_rate_bonus": bonus}):
			var item_id: String = str(entry["item_id"])
			collected[item_id] = int(collected.get(item_id, 0)) + int(entry["qty"])
	check_gt(float(collected.size()), 0.0, "打完后至少掉落一种物品")
	check_gt(float(int(collected.get("item_money", 0))), 0.0, "铜钱必掉")

	print("      端到端：%d 回合，奖励 经验 %d / 铜钱 %d，掉落 %d 种" % [
		result["rounds"], result["rewards"]["exp"], result["rewards"]["money"], collected.size(),
	])
	_check_rewards_and_persistence(db)
	_check_wounded_persistence(db)
	_check_multi_character_state(db)


## 战斗外气血（v11）：带伤进下一场 → 伤随存档往返 → 医馆按缺失收费并真的回满。
func _check_wounded_persistence(db) -> void:
	var state = GameStateScript.new_game(db, "normal")
	var char_id := str(state.char_ids[0])
	var sheet = CharacterSheetScript.new(db, state, char_id)
	var cap: int = sheet.max_hp()
	check_eq(int(sheet.current_hp()), cap, "新档是满血")
	check_false(state.is_wounded(char_id), "新档没有伤记录")

	# 模拟「上一场没打满」：打伤 40 点
	state.set_current_hp(char_id, maxi(1, cap - 40))
	var built: Array = PartyBuilderScript.build_actors(db, state)
	check_eq(int(built[0].hp), cap - 40, "带着伤进下一场（%d / %d）" % [int(built[0].hp), cap])
	check_eq(int(sheet.current_hp()), cap - 40, "面板读同一个数")

	# 伤随存档往返
	state.inventory.money = 500
	var dir_path := "res://.logs/test_end_to_end_hp"
	if DirAccess.dir_exists_absolute(dir_path):
		var dir := DirAccess.open(dir_path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	var store = SaveStoreScript.new(dir_path, 2)
	store.ensure_dir()
	check_true(bool(store.save_slot(1, state)["ok"]), "带伤的存档写入成功")
	var loaded: Dictionary = store.load_slot(1, db)
	check_true(loaded["ok"], "带伤的存档读出成功")
	if not loaded["ok"]:
		return
	var back = loaded["state"]
	check_eq(back.migrated_from, 0, "v11 存档不需要迁移")
	check_eq(int(back.current_hp_of(char_id)), cap - 40, "伤随存档往返")

	# 医馆治疗：按缺失收费并回满
	var shop = ShopServiceScript.new(db, back)
	var healed: Dictionary = shop.heal_party("bld_clinic")
	check_true(bool(healed["ok"]), "医馆能治：%s" % healed["error"])
	check_eq(int(healed["healed"]), 40, "回 40 点")
	check_eq(int(healed["cost"]), 80, "40 点 × 2 文 = 80")
	check_eq(int(back.inventory.money), 420, "钱扣掉了")
	var after: Array = PartyBuilderScript.build_actors(db, back)
	check_eq(int(after[0].hp), cap, "治完满血进战斗")


## 战斗奖励落账 → 存档往返 → 装备加成确实进了战斗单位。
func _check_rewards_and_persistence(db) -> void:
	var state = GameStateScript.new_game(db, "normal")
	var char_id := str(state.char_ids[0])
	var factory = EnemyFactoryScript.new(db)
	var enemies: Array = factory.create_team("team_wolf_pack", "normal")

	# 带装备的战斗单位：外功比脱掉武器后更高
	var armed: Array = PartyBuilderScript.build_actors(db, state)
	# 队伍人数来自存档（`character_base` 的行数会随设计推进变化）——按**存档里的**人数核，不写死 1
	check_eq(armed.size(), state.char_ids.size(), "队伍按存档构造出战斗单位（%d 人）" % state.char_ids.size())
	var atk_armed := float(armed[0].stat("atk_phys"))
	var unequipped: Dictionary = state.inventory.unequip(char_id, "weapon", 0)
	check_true(unequipped["ok"], "卸下初始武器")
	var bare: Array = PartyBuilderScript.build_actors(db, state)
	var atk_bare := float(bare[0].stat("atk_phys"))
	check_gt(atk_armed, atk_bare, "装备加成进了战斗单位（外功更高）")
	state.inventory.equip(db, char_id, state.level_of(char_id), "sword", str(unequipped["instance_id"]))

	# 内功「装备即生效」也要穿过 PartyBuilder 到**战斗单位**（不只是面板）：
	# CharacterSheet 那层由 test_skill_loadout 钉着，但「装配 → BattleActor」这一段以前没人验——
	# 内功是 0.6.x 的主角系统（24 部、占格制），它要是在这一层断了，全系统就只剩面板数字。
	var loadout = SkillLoadoutScript.new(db, state, char_id)
	loadout.learn("pf_xuanwei_01")
	check_true(bool(loadout.equip("pf_xuanwei_01")["ok"]), "装上★1 内功")
	var qi_with := float(PartyBuilderScript.build_actors(db, state)[0].stat("qi_max"))
	loadout.unequip("pf_xuanwei_01")
	var qi_without := float(PartyBuilderScript.build_actors(db, state)[0].stat("qi_max"))
	check_eq(qi_with - qi_without, 30.0, "内功加成进了战斗单位（内力上限 +30）")
	loadout.equip("pf_xuanwei_01")

	# 打一场并落账
	var result: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		PartyBuilderScript.build_actors(db, state), enemies
	)
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED + 7), PityTrackerScript.new())
	var drops: Array = resolver.roll_group("drop_wolf", "normal", {"drop_rate_bonus": 1.0})
	var applied: Dictionary = BattleRewardScript.apply(db, state, result, drops)
	check_eq(state.party_exp, int(result["rewards"]["exp"]), "经验记进队伍池")
	check_gt(float(state.inventory.money), float(result["rewards"]["money"]) - 0.5, "铜钱入账")
	check_gt(float(state.inventory.item_ids().size() + state.inventory.equipment_count()), 0.0, "掉落进了背包")

	# 存档往返后奖励仍在
	var dir_path := "res://.logs/test_end_to_end"
	var store = SaveStoreScript.new(dir_path, 2)
	if DirAccess.dir_exists_absolute(dir_path):
		var dir := DirAccess.open(dir_path)
		if dir != null:
			for file_name: String in dir.get_files():
				dir.remove(file_name)
	store.ensure_dir()
	check_true(bool(store.save_slot(1, state)["ok"]), "带奖励的存档写入成功")
	var loaded: Dictionary = store.load_slot(1, db)
	check_true(loaded["ok"], "带奖励的存档读取成功")
	if loaded["ok"]:
		var back = loaded["state"]
		check_eq(back.party_exp, state.party_exp, "经验往返一致")
		check_eq(back.inventory.money, state.inventory.money, "铜钱往返一致")
		check_eq(back.inventory.equipment_count(), state.inventory.equipment_count(), "装备往返一致")
		check_eq(str(back.inventory.item_ids()), str(state.inventory.item_ids()), "材料往返一致")


## 多人队伍：`character_base` 现在只有 1 行，所以**所有「多人」分支从来没被跑过**——
## 而设计说的是 3–4 人队伍（00_总览／04）。这条不往策划的表里加东西，而是**复制模板行**
## 造出 N 个角色（同属性、只改 id/名字，其中一个智更高），把多人分支逐个跑一遍：
## 队伍上限 4 / 每人等级与加点互不串 / 判定「谁行谁上」 / 升级先补等级最低的 /
## 医馆按全队缺失气血求和 / 多角色存档往返。
func _check_multi_character_state(db) -> void:
	var wide = _table_with_n_chars(5)
	# ① 多人队伍：0.10.0 起 `new_game()` 只给 `recruit_def.is_initial` 的成员（设计 09 §3.2），
	# 所以这里**显式点名 4 人**来跑多人分支；默认组队按 recruit_def 核（不再按 character_base 前 4 行）
	var initial_ids := PackedStringArray()
	for row: Resource in get_db().rows("recruit_def"):
		if int(row.is_initial) == 1:
			initial_ids.append(str(row.char_id))
	var default_state = GameStateScript.new_game(wide, "normal")
	check_eq(default_state.party_size(), initial_ids.size(), "默认队伍 = recruit_def 的初始成员（%d 人）" % initial_ids.size())
	var state = party_state(wide, 4)
	check_eq(state.char_ids.size(), 4, "显式点名的多人队伍真的带 4 个角色")
	var ids := PackedStringArray(state.char_ids)

	# ② 等级、加点都是**按人**记的，互不串
	state.append_level(ids[2], 3)
	check_eq(state.level_of(ids[2]), 4, "给第 3 个角色升 3 级")
	check_eq(state.level_of(ids[1]), 1, "第 2 个角色的等级没被连带")
	state.spend_point(wide, ids[1], "str")
	check_eq(int(state.allocations_of(ids[1]).get("str", 0)), 1, "第 2 个角色加了一点力")
	check_eq(int(state.allocations_of(ids[2]).get("str", 0)), 0, "第 3 个角色的加点没被动过")

	# ③ 判定「队里谁行谁上」：第 3 个角色的智被我调高，文学判定就该他上
	var judge_state = party_state(wide, 4)
	var judge_ids := PackedStringArray(judge_state.char_ids)
	var checks = EventCheckServiceScript.new(wide, judge_state)
	var best: Dictionary = checks.best_check_value("skill:wenxue")
	check_eq(str(best["char_id"]), judge_ids[2], "判定取全队最高的那个（智最高的第 3 人）")
	var sheet_third = CharacterSheetScript.new(wide, judge_state, judge_ids[2])
	var sheet_first = CharacterSheetScript.new(wide, judge_state, judge_ids[0])
	check_eq(
		int(best["value"]), int(sheet_third.event_check_value("skill:wenxue")["value"]),
		"取的就是他本人的判定值",
	)
	check_gt(
		float(best["value"]), float(sheet_first.event_check_value("skill:wenxue")["value"]),
		"比第 1 个角色的判定值高",
	)

	# ④ 经验池先补给**等级最低**的那个（开发侧口径，多人时才有意义）
	var gap_state = party_state(wide, 4)
	var gap_ids := PackedStringArray(gap_state.char_ids)
	for index in range(gap_ids.size()):
		gap_state.char_levels[gap_ids[index]] = 3 if index == 1 else 5
	# 经验池故意给到**够升 5 级那一档**（900）：这样「等级 3 的」和「等级 5 的」都付得起，
	# 才是真的在比较「先补谁」。第一版只给到 3 级那一档（420）→ 只有一个人付得起，
	# 把「挑最低」写成「挑最高」也照样绿（反向验证当场抓到我这条断言太弱，才改的）。
	var row5: Resource = wide.get_row("level_growth", 5)
	gap_state.party_exp = int(row5.exp_to_next)
	var levels = LevelServiceScript.new(wide, gap_state)
	var applied: Dictionary = levels.apply_available_levels()
	check_eq(gap_state.level_of(gap_ids[1]), 4, "先补等级最低的那个（3 → 4）")
	check_eq(gap_state.level_of(gap_ids[0]), 5, "高等级的不动")
	check_eq(int(gap_state.party_exp), int(row5.exp_to_next) - 420, "经验池扣的正是 3→4 那一份（420）")
	check_eq(Array(applied["changes"]).size(), 1, "只升了 1 级")

	# ⑤ 医馆按**全队**缺失气血求和（不是只算一个人）
	var heal_state = party_state(wide, 4)
	var heal_ids := PackedStringArray(heal_state.char_ids)
	heal_state.inventory.money = 1000
	var actors: Array = PartyBuilderScript.build_actors(wide, heal_state)
	heal_state.set_current_hp(heal_ids[0], maxi(1, int(actors[0].max_hp()) - 10))
	heal_state.set_current_hp(heal_ids[1], maxi(1, int(actors[1].max_hp()) - 20))
	var shop = ShopServiceScript.new(wide, heal_state)
	var preview: Dictionary = shop.heal_preview("bld_clinic")
	check_eq(int(preview["missing"]), 30, "全队缺失 10 + 20 = 30")
	check_eq(int(preview["cost"]), 60, "每点 2 文 → 60 文")
	var healed: Dictionary = shop.heal_party("bld_clinic")
	check_true(bool(healed["ok"]), "全队治疗成功")
	check_eq(int(heal_state.inventory.money), 940, "钱扣掉 60")
	for char_id: String in heal_ids:
		check_false(heal_state.is_wounded(char_id), "治完全队都不带伤")

	# ⑥ 多角色存档往返：等级与加点一个都不能丢
	var back = GameStateScript.from_dict(state.to_dict(), wide)
	check_not_null(back, "多角色存档能读回来")
	if back != null:
		check_eq(back.char_ids.size(), 4, "4 个角色都在")
		check_eq(back.level_of(ids[2]), 4, "第 3 个角色的等级往返一致")
		check_eq(int(back.allocations_of(ids[1]).get("str", 0)), 1, "加点往返一致")


## 造一张有 N 个角色模板的表库：复制唯一那行、改 id 与名字，第 3 个角色把智调高
## （用来验「判定谁高谁上」）。**只改内存里的副本**，不碰策划的 CSV。
## 实现收在 `test_case.gd`（多人界面版式那条用例也要用同一份夹具，别让它漂成两份）。
func _table_with_n_chars(count: int):
	return table_with_n_chars(count)


func _party(db, level: int) -> Array:
	# 0.4.0 起只有一个角色模板，同模板复制成两人
	var out: Array = []
	var row: Resource = db.get_row("character_base", "scholar_fallen")
	for index in range(2):
		var actor = BattleActorScript.from_character(db, row, level)
		actor.actor_id = "scholar_%d" % (index + 1)
		actor.display_name = "书生 %d" % (index + 1)
		out.append(actor)
	return out
