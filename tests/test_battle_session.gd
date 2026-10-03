## 分步战斗：逐回合推进、与一次性模拟一致、遭遇修正生效。
extends "res://tests/test_case.gd"

const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

const SEED := 5150


func suite_name() -> String:
	return "分步战斗与遭遇修正"


func run() -> void:
	var db = get_db()
	_check_stepwise_matches_simulate(db)
	_check_first_side(db)
	_check_surprise_bonus(db)


func _new_fight(db, encounter = null, options: Dictionary = {}) -> Dictionary:
	var state = solo_state(db)
	var allies: Array = PartyBuilderScript.build_actors(db, state)
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var opts := options.duplicate()
	if encounter != null:
		opts["encounter"] = encounter
	sim.setup(allies, enemies, opts)
	return {"sim": sim, "allies": allies, "enemies": enemies}


func _check_stepwise_matches_simulate(db) -> void:
	var stepwise: Dictionary = _new_fight(db, null, {"modifiers": {"force_hit": true, "no_variance": true}})
	var sim = stepwise["sim"]
	check_false(sim.finished(), "刚摆好没结束")
	check_eq(sim.rounds_played(), 0, "还没打任何一回合")
	var guard := 0
	while not sim.finished() and guard < 60:
		var lines: Array = sim.step_round()
		check_gt(float(lines.size()), 0.0, "每回合至少有一行日志（含回合头）")
		guard += 1
	var stepped: Dictionary = sim.result()

	var once = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var direct: Dictionary = once.simulate(
		PartyBuilderScript.build_actors(db, solo_state(db)),
		EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal"),
		{"modifiers": {"force_hit": true, "no_variance": true}}
	)
	check_eq(stepped["winner"], direct["winner"], "分步与一次跑完的胜负一致")
	check_eq(int(stepped["rounds"]), int(direct["rounds"]), "回合数一致")
	check_eq(str(stepped["log"]), str(direct["log"]), "日志完全一致（同种子确定）")


## 先手方：被发现的遭遇里，敌方应该抢在第一回合最前面
func _check_first_side(db) -> void:
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", "team_wolf_pack")
	var spotted = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_SPOTTED, "normal")
	var fight: Dictionary = _new_fight(db, spotted, {"modifiers": {"force_hit": true, "no_variance": true}})
	var sim = fight["sim"]
	var lines: Array = sim.step_round()
	var first_action := ""
	for line: String in lines:
		if str(line).begins_with("R"):
			first_action = str(line)
			break
	check_true(first_action.contains("野狼"), "被发现时第一手是敌方：%s" % first_action)

	var front = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_FRONT, "normal")
	var calm: Dictionary = _new_fight(db, front, {"modifiers": {"force_hit": true, "no_variance": true}})
	var calm_lines: Array = calm["sim"].step_round()
	var calm_first := ""
	for line: String in calm_lines:
		if str(line).begins_with("R"):
			calm_first = str(line)
			break
	check_true(calm_first.contains("书生"), "正面遭遇时身法高的我方先手：%s" % calm_first)


## 奇袭：首回合我方总伤害更高，第二回合回到常态
func _check_surprise_bonus(db) -> void:
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", "team_wolf_pack")
	var mods := {"modifiers": {"force_hit": true, "no_variance": true}}
	var front = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_FRONT, "normal")
	var sleep = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_SLEEP, "normal")

	var plain: Dictionary = _new_fight(db, front, mods)
	plain["sim"].step_round()
	var surprise: Dictionary = _new_fight(db, sleep, mods)
	surprise["sim"].step_round()
	var plain_damage := _ally_damage(plain["sim"], plain["allies"])
	var surprise_damage := _ally_damage(surprise["sim"], surprise["allies"])
	check_gt(surprise_damage, plain_damage, "奇袭首回合伤害更高（%d > %d）" % [surprise_damage, plain_damage])

	# 但奇袭也会让敌方抢先行动完，所以先手顺序不同——这不是矛盾，是奇袭=我方先手+增伤
	var pending: Array = surprise["sim"].result()["pending_rules"]
	check_true(
		"；".join(PackedStringArray(pending)).contains("无防备"),
		"奇袭同样把「敌方首回合无防备」记成暂缓规则（设计还没定它的效果，见 test_encounter）"
	)


func _ally_damage(sim, allies: Array) -> float:
	var dealt: Dictionary = sim.result()["damage_dealt"]
	var total := 0.0
	for actor in allies:
		total += float(dealt.get(actor.actor_id, 0))
	return total
