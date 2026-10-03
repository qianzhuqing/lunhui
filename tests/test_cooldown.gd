## 招式冷却（`skill_active.cooldown`）。
##
## 口径：冷却 N = **放完之后接下来 N 个回合不能再用**（第 N+1 个回合起可用）。
## 界面上会灰着写清「第几回合起可用」，自动策略与手选都遵守。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const SEED := 20261003
const FIXED := {"force_hit": true, "no_variance": true, "no_block": true}


func suite_name() -> String:
	return "招式冷却"


func run() -> void:
	var db = get_db()
	_check_table(db)
	_check_cooldown_cycle(db)
	_check_options_and_ai(db)
	_check_independence(db)


## 表里 20 个招式有冷却（2~5 回合）
func _check_table(db) -> void:
	var with_cooldown := 0
	for row: Resource in db.rows("skill_active"):
		if int(row.cooldown) > 0:
			with_cooldown += 1
	check_eq(with_cooldown, 20, "20 个招式配了冷却")
	check_eq(int(db.get_row("skill_active", "sk_xuanwei_05").cooldown), 2, "玄微剑法·点星冷却 2")
	check_eq(int(db.get_row("skill_active", "sk_drunk_01").cooldown), 5, "醉里乾坤·颠倒冷却 5")
	check_eq(int(db.get_row("skill_active", "sk_xuanwei_01").cooldown), 0, "起手式没有冷却")


## 放完进冷却、第 N+1 回合又能用
func _check_cooldown_cycle(db) -> void:
	var hero = _actor("hero", BattleActorScript.SIDE_ALLY)
	hero.skills = PackedStringArray(["sk_xuanwei_05", "sk_xuanwei_01"])
	var foe = _actor("foe", BattleActorScript.SIDE_ENEMY, {"hp_max": 9999.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [foe], {"modifiers": FIXED})
	var skill: Resource = db.get_row("skill_active", "sk_xuanwei_05")
	check_eq(sim.cooldown_left("hero", "sk_xuanwei_05"), 0, "开打前没有冷却")

	sim.begin_round()   # 第 1 回合
	sim.act(hero, skill, foe)
	check_gt(float(sim.cooldown_left("hero", "sk_xuanwei_05")), 0.0, "放完就进冷却")
	check_eq(sim.cooldown_ready_round("hero", "sk_xuanwei_05"), 4, "冷却 2 → 第 4 回合起可用")
	check_false(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 1 回合里已经不可用")

	sim.begin_round()   # 第 2 回合
	check_false(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 2 回合还在冷却")
	sim.begin_round()   # 第 3 回合
	check_false(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 3 回合还在冷却")
	sim.begin_round()   # 第 4 回合
	check_true(_has(sim.available_skills(hero), "sk_xuanwei_05"), "第 4 回合冷却结束")
	check_eq(sim.cooldown_left("hero", "sk_xuanwei_05"), 0, "冷却归零")
	# 再放一次又进冷却
	sim.act(hero, skill, foe)
	check_eq(sim.cooldown_ready_round("hero", "sk_xuanwei_05"), 7, "第 4 回合再放 → 第 7 回合起可用")


## 体检表把冷却写成原因；自动策略也不会挑冷却中的招式
func _check_options_and_ai(db) -> void:
	var hero = _actor("hero", BattleActorScript.SIDE_ALLY)
	hero.skills = PackedStringArray(["sk_xuanwei_05", "sk_xuanwei_01"])
	var foe = _actor("foe", BattleActorScript.SIDE_ENEMY, {"hp_max": 9999.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [foe], {"modifiers": FIXED})
	sim.begin_round()
	sim.act(hero, db.get_row("skill_active", "sk_xuanwei_05"), foe)

	var options: Dictionary = {}
	for option: Dictionary in sim.skill_options(hero):
		options[str(option["skill_id"])] = option
	check_true(options.has("sk_xuanwei_05"), "冷却中的招式仍然列在体检表里")
	if options.has("sk_xuanwei_05"):
		check_false(bool(options["sk_xuanwei_05"]["ok"]), "冷却中的招式标记为不可用")
		check_true(str(options["sk_xuanwei_05"]["reason"]).contains("第 4 回合起可用"), "写清第几回合起可用：%s" % options["sk_xuanwei_05"]["reason"])
	check_true(bool(options["sk_xuanwei_01"]["ok"]), "没冷却的招式照常可用")

	# 自动策略：点星冷却时只能选起手式
	var picked: Resource = sim.pick_skill(hero)
	check_eq(str(picked.skill_id), "sk_xuanwei_01", "AI 不挑冷却中的招式，退而用起手式")


## 冷却按「单位 + 招式」记：别人的同一招、自己的别招都不受影响
func _check_independence(db) -> void:
	var a = _actor("a", BattleActorScript.SIDE_ALLY)
	var b = _actor("b", BattleActorScript.SIDE_ALLY)
	a.skills = PackedStringArray(["sk_xuanwei_05", "sk_xuanwei_01"])
	b.skills = PackedStringArray(["sk_xuanwei_05", "sk_xuanwei_01"])
	var foe = _actor("foe", BattleActorScript.SIDE_ENEMY, {"hp_max": 9999.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([a, b], [foe], {"modifiers": FIXED})
	sim.begin_round()
	sim.act(a, db.get_row("skill_active", "sk_xuanwei_05"), foe)
	check_gt(float(sim.cooldown_left("a", "sk_xuanwei_05")), 0.0, "a 用过的招在冷却")
	check_eq(sim.cooldown_left("b", "sk_xuanwei_05"), 0, "b 的同一招不受影响")
	check_eq(sim.cooldown_left("a", "sk_xuanwei_01"), 0, "a 的另一招不受影响")


func _has(skills: Array, skill_id: String) -> bool:
	for skill: Resource in skills:
		if str(skill.skill_id) == skill_id:
			return true
	return false


func _actor(id: String, side: int, overrides: Dictionary = {}):
	var actor = BattleActorScript.new()
	actor.actor_id = id
	actor.display_name = id
	actor.side = side
	actor.level = 10
	actor.base_accuracy = 1.0
	actor.stats = {
		"hp_max": 300.0, "atk_phys": 40.0, "atk_qi": 40.0, "def_phys": 0.0, "def_qi": 0.0,
		"speed": 10.0, "hit_rate": 0.5, "dodge_rate": 0.0, "crit_rate": 0.0, "crit_dmg": 0.0,
		"qi_max": 100.0, "qi_regen": 1.0, "hp_regen": 0.0, "pen_rate": 0.0, "block_rate": 0.0,
		"block_reduction": 0.0, "dmg_reduction": 0.0, "debuff_chance": 0.0, "debuff_power": 0.0,
		"poise_break": 0.0,
	}
	for key: String in overrides:
		actor.stats[key] = overrides[key]
	actor.poise_max = 100
	actor.refill()
	return actor
