## 多段攻击（`skill_active.hit_count`）与目标类型（`target_type=all_enemy`）。
##
## 设计 04：招式记录里有「攻击段数」；横扫是 3 段打全体、夜雨是 2 段单体、定军是 2 段全体。
## 每段各掷一次命中/暴击/格挡，伤害与破架势逐段结算（打到死就不再补刀）。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const SEED := 20261003
## 确定性修饰：必命中、不格挡、不浮动，方便按倍率比数
const FIXED := {"force_hit": true, "no_variance": true, "no_block": true}


func suite_name() -> String:
	return "多段攻击与目标类型"


func run() -> void:
	var db = get_db()
	_check_hit_count(db)
	_check_all_enemy(db)
	_check_poise_per_hit(db)
	_check_events(db)
	_check_single_hit_regression(db)
	_check_auto_pick_counts_area(db)


## 夜雨 2 段 × 倍率 2.5 = 起手的 5 倍（同一个人、同一个靶子）
func _check_hit_count(db) -> void:
	var night_rain: Resource = db.get_row("skill_active", "sk_heifeng_01")
	check_eq(int(night_rain.hit_count), 2, "黑风刀法·夜雨是 2 段")
	var baseline := _damage_of(db, "sk_xuanwei_01", 1)
	var doubled := _damage_of(db, "sk_heifeng_01", 1)
	check_eq(doubled, int(baseline * 2.5 * 2), "2 段 × 2.5 倍率 = 单段 1.0 的 5 倍（%d vs %d）" % [doubled, baseline])


## 横扫 3 段打全体：手选一个目标也要波及全场
func _check_all_enemy(db) -> void:
	var sweep: Resource = db.get_row("skill_active", "sk_boss_hengsao")
	check_eq(int(sweep.hit_count), 3, "黑风刀法·横扫是 3 段")
	check_eq(str(sweep.target_type), "all_enemy", "横扫打全体")

	var attacker = _actor("hero", BattleActorScript.SIDE_ALLY, {"atk_phys": 50.0})
	var enemies: Array = []
	for index in range(3):
		enemies.append(_actor("foe%d" % index, BattleActorScript.SIDE_ENEMY, {"hp_max": 400.0}))
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], enemies, {"modifiers": FIXED})
	sim.begin_round()
	sim.act(attacker, sweep, enemies[0])
	for enemy in enemies:
		check_lt(float(enemy.hp), 400.0, "%s 也被横扫打到（气血 %d）" % [enemy.actor_id, enemy.hp])


## 破架势逐段结算：3 段招式扣三倍的架势
func _check_poise_per_hit(db) -> void:
	var sweep: Resource = db.get_row("skill_active", "sk_boss_hengsao")
	var single: Resource = db.get_row("skill_active", "sk_xuanwei_01")
	var attacker = _actor("hero", BattleActorScript.SIDE_ALLY, {"poise_break": 6.0})
	var target = _actor("dummy", BattleActorScript.SIDE_ENEMY, {"hp_max": 5000.0})
	target.poise_max = 999
	target.poise = 999
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [target], {"modifiers": FIXED})
	var before_single: int = target.poise
	sim.act(attacker, single, target)
	var single_loss: int = before_single - target.poise
	var before_sweep: int = target.poise
	sim.act(attacker, sweep, target)
	var sweep_loss: int = before_sweep - target.poise
	# 每段的破架势 = 招式破架势 + 攻击者破架势（横扫自己的 poise_damage 与起手不同，按表算）
	var per_hit := int(sweep.poise_damage) + int(attacker.stat("poise_break"))
	check_eq(sweep_loss, per_hit * 3, "3 段招式按段结算破架势（%d = 3 × %d）" % [sweep_loss, per_hit])
	check_eq(single_loss, int(single.poise_damage) + int(attacker.stat("poise_break")), "单段招式一段结算")


## 事件带段序号：界面靠它错开跳字
func _check_events(db) -> void:
	var attacker = _actor("hero", BattleActorScript.SIDE_ALLY, {"atk_phys": 40.0})
	var target = _actor("dummy", BattleActorScript.SIDE_ENEMY, {"hp_max": 900.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [target], {"modifiers": FIXED})
	sim.begin_round()
	sim.act(attacker, db.get_row("skill_active", "sk_heifeng_01"), target)
	var hits := {}
	for event: Dictionary in sim.take_events():
		if str(event.get("kind", "")) == "hit":
			hits[int(event.get("hit_index", -1))] = int(event.get("hit_count", 0))
	check_eq(hits.size(), 2, "两段各产生一个事件：%s" % str(hits))
	check_true(hits.has(0) and hits.has(1), "段序号是 0 与 1")
	check_eq(int(hits.get(0, 0)), 2, "事件里记着总段数")


## 回归：单段招式仍然只打一段、只出一个事件
func _check_single_hit_regression(db) -> void:
	var attacker = _actor("hero", BattleActorScript.SIDE_ALLY, {"atk_phys": 40.0})
	var target = _actor("dummy", BattleActorScript.SIDE_ENEMY, {"hp_max": 900.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [target], {"modifiers": FIXED})
	sim.begin_round()
	var lines: Array = sim.act(attacker, db.get_row("skill_active", "sk_xuanwei_01"), target)
	var hit_events := 0
	for event: Dictionary in sim.take_events():
		if str(event.get("kind", "")) == "hit":
			hit_events += 1
	check_eq(hit_events, 1, "单段招式只出一个命中事件")
	check_eq(lines.size(), 1, "单段招式只有一行日志")
	check_false(str(lines[0]).contains("段"), "单段日志不写段数：%s" % str(lines[0]))


## 打一发并返回造成的伤害（同一个攻击者/靶子，保证可比）
## 自动策略按「倍率 × 段数 × 人数」排序：
## 横扫 1.3×3 段 = 3.9（单体也一样比斩风的 2.1 高，因为它是 3 段），三个人时更是 11.7。
func _check_auto_pick_counts_area(db) -> void:
	var boss = _actor("boss", BattleActorScript.SIDE_ENEMY, {"atk_phys": 40.0})
	boss.skills = PackedStringArray(["sk_boss_zhangfeng", "sk_boss_hengsao"])
	var single_foe = _actor("solo", BattleActorScript.SIDE_ALLY, {})
	var sim_one = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim_one.setup([single_foe], [boss], {"modifiers": FIXED})
	var sweep_row: Resource = db.get_row("skill_active", "sk_boss_hengsao")
	var single_row: Resource = db.get_row("skill_active", "sk_boss_zhangfeng")
	check_float(sim_one._skill_score(boss, sweep_row), 3.9, "横扫 1 人时权重 1.3×3 段", 0.001)
	check_float(sim_one._skill_score(boss, single_row), 2.1, "斩风权重就是 2.1", 0.001)
	check_eq(
		str(sim_one.pick_skill(boss).skill_id), "sk_boss_hengsao",
		"段数也算进来：3 段横扫（3.9）压过单段斩风（2.1）",
	)

	var crowd: Array = []
	for index in range(3):
		crowd.append(_actor("ally%d" % index, BattleActorScript.SIDE_ALLY, {}))
	var sim_many = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim_many.setup(crowd, [boss], {"modifiers": FIXED})
	check_float(sim_many._skill_score(boss, sweep_row), 11.7, "横扫打 3 人权重 1.3×3 段×3 人", 0.001)
	check_eq(
		str(sim_many.pick_skill(boss).skill_id), "sk_boss_hengsao",
		"三个人时还是横扫（11.7 ≫ 2.1）",
	)


func _damage_of(db, skill_id: String, target_count: int) -> int:
	var attacker = _actor("hero", BattleActorScript.SIDE_ALLY, {"atk_phys": 50.0})
	var target = _actor("dummy", BattleActorScript.SIDE_ENEMY, {"hp_max": 100000.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [target], {"modifiers": FIXED})
	sim.begin_round()
	sim.act(attacker, db.get_row("skill_active", skill_id), target)
	return 100000 - target.hp


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
