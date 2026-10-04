## 战斗的**不变量模糊测试**：随机组队 × 随机敌人队伍 × 随机难度 × 固定种子，跑 120 场，
## 只问「有没有哪种组合会破坏不变量」——逐条判据都是**静默腐坏**那一类：
##   · 气血／内力跑出 `[0, 上限]` 之外（负血、过量治疗、消耗没扣干净）；
##   · 伤害值出现 `NaN` 或负数（除零、上限公式写错时最先在这里露头）；
##   · 一场架跑不完 或 回合数超过 `BattleSimulator.MAX_ROUNDS`（死循环／不收敛）；
##   · `winner` 落到三个合法值之外。
##
## 为什么值得单独有一条：现有的战斗用例都是**定点**的（指定队伍、指定等级、指定种子），
## 覆盖的是"设计写下来的那些组合"；而**组合爆炸**（1~4 人 × 1~15 级 × 三种难度 × 18 支队伍）
## 里任何一处边缘都可能只在某个没写过的组合上炸。固定种子保证可复现——
## 失败信息里带 `seed=/队伍/难度`，照着那三个数就能重跑出同一场。
##
## 顺带一条覆盖保证：跑完必须**两种胜负都出现过**（否则说明种子挑得不对／战斗提前退化成一种结果）。
extends "res://tests/test_case.gd"

const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

## 固定种子表：1..120（数小、可读、可复现）。改这里等于换一批对局，不是"调松门限"。
const SEEDS := 120
## 注意：这里必须用普通数组字面量——`PackedStringArray([...])` 在 GDScript 里**不是常量表达式**，
## 写成 `const` 会直接解析失败（第一版就是这么红的）。
const PARTY_POOL := ["scholar_fallen", "ch_gang", "ch_du", "ch_ci", "ch_qi"]
const DIFFICULTIES := ["normal", "hard", "nightmare"]


func suite_name() -> String:
	return "战斗不变量模糊测试（120 场随机对局）"


func run() -> void:
	var db = get_db()
	var teams := PackedStringArray()
	for row: Resource in db.rows("enemy_team"):
		teams.append(str(row.team_id))
	check_gt(float(teams.size()), 5.0, "读到足够多的敌人队伍（%d 支）" % teams.size())

	var problems := PackedStringArray()
	var checked := 0
	var winners := {}
	for seed_value in range(1, SEEDS + 1):
		var team_id: String = teams[seed_value % teams.size()]
		var difficulty: String = str(DIFFICULTIES[seed_value % DIFFICULTIES.size()])
		var party_size := 1 + (seed_value % 4)
		var level := 1 + (seed_value % 15)
		var ids := PackedStringArray()
		for index in range(party_size):
			var char_id: String = str(PARTY_POOL[(seed_value + index) % PARTY_POOL.size()])
			if ids.has(char_id):
				continue
			ids.append(char_id)
		var state = GameStateScript.new_game(db, difficulty, ids)
		for char_id: String in ids:
			state.char_levels[char_id] = level     # 等级来自存档，直接点名（同 `analysis_balance` 的口径）
		var allies: Array = PartyBuilderScript.build_actors(db, state)
		var enemies: Array = EnemyFactoryScript.new(db).create_team(team_id, difficulty)
		var sim = BattleSimulatorScript.new(db, RngServiceScript.new(seed_value * 7919))
		var result: Dictionary = sim.simulate(allies, enemies, {})
		checked += 1
		var tag := "seed=%d %s/%s 队伍%d人/%d级" % [seed_value, team_id, difficulty, ids.size(), level]
		winners[str(result.get("winner", ""))] = int(winners.get(str(result.get("winner", "")), 0)) + 1
		if not sim.finished():
			problems.append("%s：simulate() 跑完却没结束" % tag)
		var rounds := int(result.get("rounds", -1))
		if rounds < 0 or rounds > BattleSimulatorScript.MAX_ROUNDS:
			problems.append("%s：回合数越界 %d（上限 %d）" % [tag, rounds, BattleSimulatorScript.MAX_ROUNDS])
		if Array(result.get("log", [])).is_empty():
			problems.append("%s：战报是空的" % tag)
		for unit in allies + enemies:
			if unit.hp < 0 or unit.hp > unit.max_hp():
				problems.append("%s：%s 气血越界 %d／%d" % [tag, unit.display_name, unit.hp, unit.max_hp()])
			if unit.qi < 0 or unit.qi > unit.max_qi():
				problems.append("%s：%s 内力越界 %d／%d" % [tag, unit.display_name, unit.qi, unit.max_qi()])
		var dealt: Dictionary = result.get("damage_dealt", {})
		for key: String in dealt.keys():
			var value := float(dealt[key])
			if is_nan(value) or is_inf(value) or value < 0.0:
				problems.append("%s：伤害值不合法 %s=%s" % [tag, key, str(value)])
		var winner := str(result.get("winner", ""))
		if winner != "ally" and winner != "enemy" and winner != "draw":
			problems.append("%s：winner 取值意外 '%s'" % [tag, winner])

	check_eq(checked, SEEDS, "每一场都真的跑了（跑了 %d 场）" % checked)
	check_eq(problems.size(), 0, "120 场随机对局里没有一场破坏不变量：%s" % "；".join(problems))
	# 覆盖保证：两边都得赢过——只有一边赢说明这批种子退化成了"同一种架"
	check_gt(float(int(winners.get("ally", 0))), 0.0, "有打赢的对局（%d 场）" % int(winners.get("ally", 0)))
	check_gt(float(int(winners.get("enemy", 0))), 0.0, "有打输的对局（%d 场）" % int(winners.get("enemy", 0)))
