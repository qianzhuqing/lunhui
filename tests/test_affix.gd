## 装备随机词条：数量按稀有度、按槽位限定、按 min_rarity 门槛、数值在区间内，
## 并且真的走进属性计算、进存档、进掉落。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")

const CHAR_ID := "scholar_fallen"
const SEED := 20261003


func suite_name() -> String:
	return "装备词条"


func run() -> void:
	var db = get_db()
	_check_count_by_rarity(db)
	_check_slot_and_rarity_pool(db)
	_check_value_range_and_determinism(db)
	_check_contributions(db)
	_check_save_round_trip(db)
	_check_drop_integration(db)
	_check_weapon_element(db)
	_check_new_rarity_supported(db)


## 词条数量落在 rarity_def 的区间里；凡品可能 0 条，也不可能出现 2 条
func _check_count_by_rarity(db) -> void:
	var counts := {}
	# 稀有度顺序的**唯一出处是表**（`rarity_def` 从低到高的行顺序）——设计加档时这里自动跟上，
	# 不再抄一份常量名单（决策 230：抄了那份，新档的 min_rarity 门槛会静默失效）。
	for rarity_row: Resource in db.rows("rarity_def"):
		var rarity_id := str(rarity_row.rarity_id)
		var row: Resource = db.get_row("rarity_def", rarity_id)
		var roller = AffixRollerScript.new(db, RngServiceScript.new(SEED))
		var seen := {}
		for index in range(40):
			var n := roller.affix_count(rarity_id)
			seen[n] = true
			check_in_range(float(n), float(row.affix_min), float(row.affix_max),
				"%s 词条数 %d 落在 [%d, %d]" % [rarity_id, n, row.affix_min, row.affix_max])
		counts[rarity_id] = seen
	check_true(counts["common"].has(0) or counts["common"].has(1), "凡品最多 1 条")
	for n: int in counts["common"]:
		check_lt(float(n), 2.0, "凡品不会出 2 条（掷到 %d 条）" % n)
	for n: int in counts["legend"]:
		check_eq(n, 4, "传世固定 4 条")


## 词条按槽位限定：武器刷不出「内力回复」这种护甲词条；同一条词条不会在一件装备上出现两次
func _check_slot_and_rarity_pool(db) -> void:
	var roller = AffixRollerScript.new(db, RngServiceScript.new(SEED))
	var weapon_rows := 0
	for equip_id: String in ["eq_sword_02", "eq_sword_03", "eq_sword_04", "eq_fist_02", "eq_spear_02"]:
		var base: Resource = db.get_row("equip_base", equip_id)
		var records: Array = roller.roll_for(equip_id)
		var seen := {}
		for record: Dictionary in records:
			var pool_row: Resource = db.get_row("affix_pool", str(record["affix_id"]))
			weapon_rows += 1
			check_true(pool_row.slots().has(str(base.slot)), "%s 的词条 %s 允许出现在 %s" % [
				equip_id, record["affix_id"], base.slot,
			])
			check_false(seen.has(str(record["affix_id"])), "同一件装备不重复出同一条词条")
			seen[str(record["affix_id"])] = true
			check_true(
				roller.rarity_rank(str(pool_row.min_rarity)) <= roller.rarity_rank(str(base.rarity)),
				"%s（%s）达到词条 %s 的最低稀有度" % [equip_id, base.rarity, record["affix_id"]],
			)
	check_gt(float(weapon_rows), 0.0, "武器确实掷出了词条")

	# 武器槽的候选里不该有护甲专属词条（例：af_hp_max_01 只允许 body|belt|shoulder|necklace）
	var weapon_pool: Array = roller.candidates_for("weapon", "legend")
	for row: Resource in weapon_pool:
		check_false(weapon_pool.is_empty(), "武器词条池非空")
		check_true(row.slots().has("weapon"), "词条池里的 %s 允许武器" % row.affix_id)


## 数值在 value_min/max 之间；同种子掷两次结果完全一样
func _check_value_range_and_determinism(db) -> void:
	var first = AffixRollerScript.new(db, RngServiceScript.new(SEED))
	var second = AffixRollerScript.new(db, RngServiceScript.new(SEED))
	var a: Array = first.roll_for("eq_sword_04")
	var b: Array = second.roll_for("eq_sword_04")
	check_eq(a, b, "同种子掷出的词条完全一致")
	check_gt(float(a.size()), 0.0, "传世武器有四条词条")
	for record: Dictionary in a:
		var row: Resource = db.get_row("affix_pool", str(record["affix_id"]))
		var value := float(record["value"])
		var slack := 1.0 if str(record["value_kind"]) == "attr_point" else 0.001
		check_in_range(
			value, float(row.value_min) - slack, float(row.value_max) + slack,
			"%s 的数值 %.3f 落在 [%.2f, %.2f]" % [record["affix_id"], value, row.value_min, row.value_max],
		)


## 词条真的进属性：属性点词条走属性点层，派生数值词条走固定值层
func _check_contributions(db) -> void:
	var state = solo_state(db)
	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	var str_before := int(sheet.total_attrs()["str"])
	var atk_before := sheet.stat_value("atk_phys")

	# 词条记录是存档里存的样子：affix_id + target + value_kind + value
	var rolled: Array = [
		{"affix_id": "af_str_01", "target": "attr:str", "value_kind": "attr_point", "value": 3.0},
		{"affix_id": "af_atk_phys_01", "target": "stat:atk_phys", "value_kind": "flat", "value": 7.0},
	]
	var instance_id: String = state.inventory.add_equipment(db, "eq_sword_01", rolled)
	check_eq(state.inventory.affixes_of(instance_id).size(), 2, "词条随实例存下来")
	var before_sources := _sources(state.inventory.contributions_for(db, CHAR_ID))
	check_false(before_sources.has(instance_id), "没穿上时不算贡献")

	var equipped: Dictionary = state.inventory.equip(
		db, CHAR_ID, state.level_of(CHAR_ID), "sword", instance_id
	)
	check_true(bool(equipped["ok"]), "带词条的武器能穿上")
	var after = CharacterSheetScript.new(db, state, CHAR_ID)
	check_eq(int(after.total_attrs()["str"]), str_before + 3, "属性点词条进了五维")
	# 属性点还会经 attr_to_stat 换算：力 3 点带来的外功攻击要一起算上（所以只断言「涨了且不少于 7」）
	check_gt(after.stat_value("atk_phys"), atk_before + 7.0, "派生数值词条 + 属性点换算都进了外功攻击")

	var summary := after.affix_summary_of(instance_id)
	check_true(summary.contains("力 +3"), "词条文案给出属性：%s" % summary)
	check_true(summary.contains("外功攻击 +7"), "词条文案给出派生数值：%s" % summary)


## 存档往返：词条跟着实例走，读回来贡献一致
func _check_save_round_trip(db) -> void:
	var state = solo_state(db)
	var rolled: Array = AffixRollerScript.new(db, RngServiceScript.new(SEED)).roll_for("eq_ring_02")
	check_gt(float(rolled.size()), 0.0, "良品戒指掷出了词条")
	var instance_id: String = state.inventory.add_equipment(db, "eq_ring_02", rolled)
	var before: Array = state.inventory.contributions_for(db, CHAR_ID)

	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_not_null(back, "带词条的存档能读回来")
	if back == null:
		return
	check_eq(back.inventory.affixes_of(instance_id), rolled, "词条原样保留")
	check_eq(back.inventory.contributions_for(db, CHAR_ID), before, "贡献列表往返一致")


## 掉落接词条：BattleReward 造装备实例时会滚词条
func _check_drop_integration(db) -> void:
	var state = solo_state(db)
	var roller = AffixRollerScript.new(db, RngServiceScript.new(SEED))
	var applied: Dictionary = BattleRewardScript.grant(
		db, state, 0, 0, [{"item_id": "eq_sword_03", "qty": 1}], roller
	)
	check_eq(Array(applied["equipment"]).size(), 1, "掉了一件装备")
	if Array(applied["equipment"]).is_empty():
		return
	var instance_id := str(Array(applied["equipment"])[0])
	var affixes: Array = state.inventory.affixes_of(instance_id)
	var base: Resource = db.get_row("equip_base", "eq_sword_03")
	var rarity_row: Resource = db.get_row("rarity_def", str(base.rarity))
	check_in_range(
		float(affixes.size()), float(rarity_row.affix_min), float(rarity_row.affix_max),
		"掉落的 %s（%s）带 %d 条词条" % [base.name_cn, base.rarity, affixes.size()],
	)
	for record: Dictionary in affixes:
		check_true(
			db.get_row("affix_pool", str(record["affix_id"])) != null,
			"掉落的词条 %s 在词条池里" % record["affix_id"],
		)


## 武器系别覆盖招式系别（05 文档：乌木拳套是内功拳那条规则）
func _check_weapon_element(db) -> void:
	var state = solo_state(db)
	state.char_levels[CHAR_ID] = 12
	var odd_sword: String = state.inventory.add_equipment(db, "eq_sword_04")
	var equipped: Dictionary = state.inventory.equip(db, CHAR_ID, 12, "sword", odd_sword)
	check_true(bool(equipped["ok"]), "奇诡剑（10 级）能装上")

	var actor = PartyBuilderScript.build_actor(db, state, CHAR_ID)
	check_not_null(actor, "造出战斗单位")
	check_eq(str(actor.tags.get("weapon_element", "")), "odd", "武器系别带进战斗单位")

	# 打一场，看事件里的系别是不是被武器覆盖过的
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([actor], enemies, {})
	var guard := 0
	var elements := {}
	while not sim.finished() and guard < 40:
		guard += 1
		if not sim.in_round():
			sim.begin_round()
		if sim.current_actor() == actor:
			var skills: Array = sim.available_skills(actor)
			if skills.is_empty():
				sim.auto_act()
			else:
				sim.act(actor, skills[0], enemies[0])
		else:
			sim.auto_act()
		for event: Dictionary in sim.take_events():
			if str(event.get("kind", "")) == "hit" and str(event.get("source", "")) == actor.actor_id:
				elements[str(event.get("element", ""))] = true
	check_true(elements.has("odd"), "招式本身是外功，但事件里的系别是武器给的奇诡：%s" % str(elements))

	# 卸下武器后回到招式自己的系别
	state.inventory.unequip(CHAR_ID, "weapon", 0)
	var bare = PartyBuilderScript.build_actor(db, state, CHAR_ID)
	check_eq(str(bare.tags.get("weapon_element", "")), "", "卸下武器后没有武器系别")


## 设计**加一档稀有度**时，代码要自动跟上（排序的唯一出处是 `rarity_def` 的行顺序）。
##
## 由来（2026-10-03，决策 230）：`AffixRoller` 以前抄了一份常量名单 `RARITY_ORDER`，
## 而设计侧校验器（PS1）是按表顺序算的——两边一旦分家，新档的 `rarity_rank()` 会返回 **0（＝凡品）**，
## 于是「要求高稀有度才出的词条」会落到新档上（或者反过来）：**门槛静默失效**。
## 这里用夹具加一档 `myth`（排在传世之后）+ 一条只给 myth 的词条，验排序与门槛都按表走。
func _check_new_rarity_supported(db) -> void:
	var stub = load("res://src/core/table_db.gd").new()
	stub.load_all()
	var rarities: Resource = stub.tables["rarity_def"].duplicate(true)
	var myth: Resource = rarities.rows[0].duplicate(true)     # 复制凡品行再改
	myth.rarity_id = "myth"
	myth.id = "myth"
	myth.name_cn = "神话"
	myth.affix_min = 4
	myth.affix_max = 4
	rarities.rows.append(myth)
	rarities.index["myth"] = rarities.rows.size() - 1
	stub.tables["rarity_def"] = rarities
	var pool: Resource = stub.tables["affix_pool"].duplicate(true)
	var top: Resource = pool.rows[0].duplicate(true)
	top.affix_id = "af_test_myth"
	top.id = "af_test_myth"
	top.min_rarity = "myth"
	top.allow_slots = "weapon"
	pool.rows.append(top)
	pool.index["af_test_myth"] = pool.rows.size() - 1
	stub.tables["affix_pool"] = pool

	var roller = AffixRollerScript.new(stub, RngServiceScript.new(SEED))
	check_eq(roller.rarity_rank("myth"), 5, "新档按表顺序排到最后（不再静默当凡品）")
	check_eq(roller.rarity_rank("legend"), 4, "对照：传世还是第 4 档")
	var myth_pool: Array = roller.candidates_for("weapon", "myth")
	var legend_pool: Array = roller.candidates_for("weapon", "legend")
	check_true(_pool_has(myth_pool, "af_test_myth"), "新档装备能刷到「要求 myth」的词条")
	check_false(_pool_has(legend_pool, "af_test_myth"), "低一档（传世）刷不到它——门槛没有失效")


func _pool_has(pool: Array, affix_id: String) -> bool:
	for row: Resource in pool:
		if str(row.affix_id) == affix_id:
			return true
	return false


func _sources(contributions: Array) -> Array:
	var out: Array = []
	for entry: Dictionary in contributions:
		out.append(str(entry.get("source", "")))
	return out
