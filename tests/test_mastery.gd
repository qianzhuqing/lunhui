## 熟练度成长：打坐费用曲线、上限与拒绝、战斗施放 +1、伤害倍率联动。
##
## 规则出处 docs/design/01_角色系统.md「熟练度（修炼）」与 growth_const.csv：
## 战斗中施放每次 +mastery_combat_gain、打坐费用 = cultivate_cost_base × cultivate_cost_growth^(熟练度-1)。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const MasteryServiceScript := preload("res://src/core/mastery_service.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const CHAR_ID := "scholar_fallen"
const SKILL := "sk_xuanwei_01"
const CULTIVATE_SCENE := "res://scenes/cultivate_screen.tscn"


func suite_name() -> String:
	return "熟练度与打坐"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var mastery = MasteryServiceScript.new(db, state)
	_check_cost_curve(db, state, mastery)
	_check_train(db, state, mastery)
	_check_combat_usage(db, state, mastery)
	_check_multiplier(db, state)
	_check_multi_member_ui(db)
	_check_full_train_budget(db, state, mastery)


## 打坐费用按「当前等级 → 下一级」逐级变贵
func _check_cost_curve(db, state, mastery) -> void:
	var growth = GrowthCalculatorScript.new(db)
	var star: Resource = db.get_row("skill_star_def", "1")
	var growth_rate: float = growth.constant("cultivate_cost_growth")
	check_float(float(mastery.train_cost(CHAR_ID, SKILL)), float(star.cultivate_cost_base), "0 → 1 用基础价")
	var previous := 0
	for level in range(1, 5):
		state.set_mastery(CHAR_ID, SKILL, level)
		var expected: int = int(round(float(star.cultivate_cost_base) * pow(growth_rate, float(level))))
		check_eq(int(mastery.train_cost(CHAR_ID, SKILL)), expected, "%d → %d 的费用" % [level, level + 1])
		check_gt(float(mastery.train_cost(CHAR_ID, SKILL)), float(previous), "费用逐级上涨")
		previous = int(mastery.train_cost(CHAR_ID, SKILL))
	state.set_mastery(CHAR_ID, SKILL, 0)


## 打坐：未学／满级／缺钱都要被拒；成功就扣钱 +1 并写进存档
func _check_train(db, state, mastery) -> void:
	state.inventory.money = 0
	var poor: Dictionary = mastery.can_train(CHAR_ID, SKILL)
	check_false(bool(poor["ok"]), "没钱不能打坐")
	check_true(str(poor["error"]).contains("铜钱"), "说明是铜钱不够：%s" % poor["error"])

	var unknown: Dictionary = mastery.can_train(CHAR_ID, "sk_chensha_01")
	check_false(bool(unknown["ok"]), "没学过的武学不能打坐")
	check_true(str(unknown["error"]).contains("还没学会"), "说明没学会：%s" % unknown["error"])

	state.inventory.money = 1000
	var cost: int = mastery.train_cost(CHAR_ID, SKILL)
	var trained: Dictionary = mastery.train(CHAR_ID, SKILL)
	check_true(bool(trained["ok"]), "打坐成功：%s" % trained["error"])
	check_eq(int(trained["cost"]), cost, "按表里的费用扣钱")
	check_eq(state.inventory.money, 1000 - cost, "铜钱扣掉")
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), 1, "熟练度 +1")
	check_eq(state.mastery_of(CHAR_ID, SKILL), 1, "写进存档")

	state.set_mastery(CHAR_ID, SKILL, mastery.mastery_max(), mastery.mastery_max())
	var maxed: Dictionary = mastery.can_train(CHAR_ID, SKILL)
	check_false(bool(maxed["ok"]), "练满不能再打坐")
	check_true(str(maxed["error"]).contains("练满"), "说明已经练满：%s" % maxed["error"])
	state.set_mastery(CHAR_ID, SKILL, 0)

	# 「差一文」边界：刚好够钱能打坐、少一文就拒（这类 off-by-one 最容易被后来的改动踩坏）
	state.set_mastery(CHAR_ID, SKILL, 0)
	var exact_cost: int = mastery.train_cost(CHAR_ID, SKILL)
	state.inventory.money = exact_cost - 1
	var short: Dictionary = mastery.train(CHAR_ID, SKILL)
	check_false(bool(short["ok"]), "差一文打不了坐")
	check_eq(state.inventory.money, exact_cost - 1, "被拒时一文不扣")
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), 0, "被拒时熟练度不动")
	state.inventory.money = exact_cost
	var exact: Dictionary = mastery.train(CHAR_ID, SKILL)
	check_true(bool(exact["ok"]), "刚好够钱能打坐：%s" % exact["error"])
	check_eq(state.inventory.money, 0, "刚好花光")
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), 1, "熟练度 +1")
	state.set_mastery(CHAR_ID, SKILL, 0)


## 打坐界面的多人分支：4 人队伍时的成员按钮、切人后的列表、以及「给谁练谁涨」。
## 自检只跑过 1 个角色（`character_base` 现在也只有 1 行），多人分支以前没人验——
## 切人串号（给 B 打坐却涨到 A 头上）在单角色自检里永远看不出来。
func _check_multi_member_ui(db) -> void:
	if scene_tree == null:
		fail("没有注入场景树，打坐界面用例无法进行")
		return
	var wide = table_with_n_chars(4)
	var state = party_state(wide, 4)
	var ids := PackedStringArray(state.char_ids)
	check_eq(ids.size(), 4, "造了 4 人队伍")
	var a := ids[0]
	var b := ids[1]
	state.inventory.money = 1000
	# 只给 B 一点熟练度：切过去应当看得到 3/10，A 还是 0
	state.set_mastery(b, SKILL, 3)
	var screen = load(CULTIVATE_SCENE).instantiate()
	screen.db_override = wide
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()
	var members := 0
	for node: Node in screen.find_children("CultivateMemberButton*", "Button", true, false):
		members += 1
	check_eq(members, 4, "4 人队伍在打坐界面各有成员按钮")
	screen.select_char(a)
	check_eq(str(screen.selected_char), a, "切到第 1 个角色")
	check_eq(int(screen.mastery.mastery_of(a, SKILL)), 0, "A 的熟练度是 0")
	screen.select_char(b)
	check_eq(str(screen.selected_char), b, "切到第 2 个角色")
	check_eq(int(screen.mastery.mastery_of(b, SKILL)), 3, "B 的熟练度是 3")
	var money_before := int(state.inventory.money)
	var cost: int = screen.mastery.train_cost(b, SKILL)
	var trained: Dictionary = screen.press_train(SKILL)
	check_true(bool(trained["ok"]), "给 B 打坐：%s" % trained["error"])
	check_eq(int(screen.mastery.mastery_of(b, SKILL)), 4, "B 涨到 4")
	check_eq(int(screen.mastery.mastery_of(a, SKILL)), 0, "A 一点没涨（没串号）")
	check_eq(int(state.inventory.money), money_before - cost, "只扣一份钱")
	scene_tree.root.remove_child(screen)
	screen.free()


## 打坐的「一整套」累积边界：钱**刚好够**从 0 连练到上限时，最后一次正好花光；
## 少一文则最后一次被拒、那一文留着。单价曲线是逐级上涨的（`base × growth^level`），
## 所以这里验的是「逐级费用之和」这个合计数与扣钱口径对不对——单次边界在 `_check_train` 里另有对照。
func _check_full_train_budget(db, state, mastery) -> void:
	var top: int = mastery.mastery_max()
	var total := 0
	for level in range(top):
		state.set_mastery(CHAR_ID, SKILL, level)
		total += int(mastery.train_cost(CHAR_ID, SKILL))
	check_gt(float(total), 0.0, "从 0 练到上限一共要花钱（%d 文）" % total)
	check_gt(float(total), float(mastery.train_cost(CHAR_ID, SKILL)), "整套比第一级贵（曲线确实在涨）")

	state.set_mastery(CHAR_ID, SKILL, 0)
	state.inventory.money = total
	var done := 0
	while mastery.mastery_of(CHAR_ID, SKILL) < top and done < top + 5:
		var result: Dictionary = mastery.train(CHAR_ID, SKILL)
		check_true(bool(result["ok"]), "第 %d 次打坐成功：%s" % [done + 1, result["error"]])
		done += 1
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), top, "练到上限")
	check_eq(int(state.inventory.money), 0, "刚好花光（合计 %d 文）" % total)
	check_eq(done, top, "正好练了上限那么多次")

	# 少一文：只能练到上限前一级，最后一次被拒且那一文留着
	state.set_mastery(CHAR_ID, SKILL, 0)
	state.inventory.money = total - 1
	var done_short := 0
	while mastery.mastery_of(CHAR_ID, SKILL) < top - 1 and done_short < top + 5:
		mastery.train(CHAR_ID, SKILL)
		done_short += 1
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), top - 1, "差一文只能练到上限前一级")
	# 此刻余额应当正好是「最后一级的费用 − 1」——差的那一文就是它
	var last_cost: int = mastery.train_cost(CHAR_ID, SKILL)
	check_eq(int(state.inventory.money), last_cost - 1, "余额 = 最后一级费用 − 1（差一文）")
	var last: Dictionary = mastery.train(CHAR_ID, SKILL)
	check_false(bool(last["ok"]), "最后一练被拒（钱不够）")
	check_eq(int(state.inventory.money), last_cost - 1, "被拒时余额一分没动")
	state.set_mastery(CHAR_ID, SKILL, 0)


## 战斗施放：每次施放 +1，封顶；只统计真正施放过的招式
func _check_combat_usage(db, state, mastery) -> void:
	state.set_mastery(CHAR_ID, SKILL, 0)
	var gained: Array = mastery.apply_combat_usage(CHAR_ID, {SKILL: 3})
	check_eq(gained.size(), 1, "只有施放过的招式进结算")
	if not gained.is_empty():
		check_eq(int(gained[0]["gained"]), 3, "施放 3 次 +3")
		check_eq(mastery.mastery_of(CHAR_ID, SKILL), 3, "熟练度到 3")

	# 封顶：再施放 20 次也只到上限
	mastery.apply_combat_usage(CHAR_ID, {SKILL: 20})
	check_eq(mastery.mastery_of(CHAR_ID, SKILL), mastery.mastery_max(), "熟练度封顶在 mastery_max")
	var no_gain: Array = mastery.apply_combat_usage(CHAR_ID, {SKILL: 5})
	check_true(no_gain.is_empty(), "练满之后不再增长")
	state.set_mastery(CHAR_ID, SKILL, 0)

	# 真打一场：simulator 会记下谁施放了什么
	var actor = BattleActorScript.from_character(
		db, db.get_row("character_base", CHAR_ID), 12, {}, []
	)
	actor.skills = PackedStringArray([SKILL])
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(4242))
	sim.setup([actor], enemies, {})
	var guard := 0
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
	var uses: Dictionary = sim.skill_uses(str(actor.actor_id))
	check_gt(float(int(uses.get(SKILL, 0))), 0.0, "战斗记录下了施放次数：%s" % str(uses))
	var settled: Array = mastery.apply_combat_usage(CHAR_ID, uses)
	check_gt(float(settled.size()), 0.0, "打完能结算出熟练度")
	check_eq(
		mastery.mastery_of(CHAR_ID, SKILL), mini(int(uses.get(SKILL, 0)), mastery.mastery_max()),
		"熟练度增量 = 施放次数（封顶 %d，本场施放 %d 次）" % [mastery.mastery_max(), int(uses.get(SKILL, 0))],
	)


## 熟练度直接决定招式倍率（伤害管线第 1 步用的就是这个乘数）
func _check_multiplier(db, state) -> void:
	var growth = GrowthCalculatorScript.new(db)
	var base: float = growth.mastery_multiplier(1, 0)
	var trained: float = growth.mastery_multiplier(1, 5)
	var maxed: float = growth.mastery_multiplier(1, growth.mastery_max())
	check_float(base, 1.0, "0 熟练度不加成")
	check_gt(trained, base, "练了就更强")
	check_gt(maxed, trained, "练满更强")
	# ★1 练满 ×1.5（skill_star_def.mastery_gain = 0.05）
	check_float(maxed, 1.5, "★1 练满 ×1.5", 0.001)

	# 存档往返带上熟练度
	state.set_mastery(CHAR_ID, SKILL, 4)
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_eq(back.mastery_of(CHAR_ID, SKILL), 4, "熟练度往返一致")
