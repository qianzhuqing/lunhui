## 架势与破绽：打空架势 → 破绽窗口（受伤提高）→ 窗口结束重置；背袭降低初始架势。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const EncounterScript := preload("res://src/core/encounter.gd")

const SEED := 8080


func suite_name() -> String:
	return "架势与破绽"


func run() -> void:
	var db = get_db()
	_check_pool(db)
	_check_break_and_bonus(db)
	_check_break_damage_bonus(db)
	_check_round_reset(db)
	_check_battle_regen(db)
	_check_backstab_opening(db)
	_check_exact_break(db)


func _check_pool(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var wolf = factory.create("en_wolf", "normal")
	check_eq(wolf.max_poise(), 20, "野狼架势取自 enemy_base.poise")
	check_eq(wolf.poise, 20, "开局架势是满的")
	var boss = factory.create("en_bd_boss", "normal")
	check_eq(boss.max_poise(), 160, "大寨主架势 160")
	# 「回复极快」的数值是开发侧定的代码常量（enemy_base 没有这一列，已记进对接表等设计补），
	# 所以这里把**数值本身**钉住：改常量就得改这条断言（有意识的行为）。
	check_float(wolf.poise_regen_ratio, 0.1, "普通敌人每回合回 10% 架势上限")
	check_float(boss.poise_regen_ratio, 0.3, "大寨主每回合回 30%（设计：回复极快）")
	var hero = BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 5)
	# 玩家架势上限的两个常数同样是开发侧定的（`character_base` 还没有架势列，已记交接表），
	# 按上面同一条纪律把**数值本身**钉住。变异探针实测：只写 `> 0` 时，把 40 改成 60 也不会红。
	check_eq(hero.max_poise(), 80, "5 级玩家架势上限 = 40 + 8 × 5 = 80（实得 %d）" % hero.max_poise())
	var rookie = BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 1)
	check_eq(rookie.max_poise(), 48, "1 级玩家架势上限 = 40 + 8 × 1 = 48（实得 %d）" % rookie.max_poise())


func _check_break_and_bonus(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var allies: Array = [BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 10)]
	var wolf = factory.create("en_wolf", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup(allies, [wolf], {"modifiers": {"force_hit": true, "no_variance": true}})
	var skill: Resource = db.get_row("skill_active", "sk_chensha_01")   # 破架势高
	check_not_null(skill, "沉沙掌可用")
	var before: int = wolf.poise
	sim.act(allies[0], skill, wolf)
	check_lt(float(wolf.poise), float(before), "命中会扣架势（招式破架势 + 攻击者破架势）")
	var guard := 0
	while not wolf.is_broken() and guard < 10:
		sim.act(allies[0], skill, wolf)
		guard += 1
	check_true(wolf.is_broken(), "架势打空后进入破绽")
	check_eq(wolf.poise, 0, "破绽时架势为 0")
	var log: Array = sim.result()["log"]
	check_true("；".join(PackedStringArray(log)).contains("破绽"), "日志会提示架势被打破")


func _check_round_reset(db) -> void:
	var wolf = EnemyFactoryScript.new(db).create("en_wolf", "normal")
	wolf.take_poise_damage(wolf.max_poise())
	check_true(wolf.is_broken(), "打空即破绽")
	wolf.end_of_round_poise()
	check_false(wolf.is_broken(), "破绽窗口只有 1 回合")
	check_eq(wolf.poise, wolf.max_poise(), "窗口结束架势重置")
	wolf.take_poise_damage(5)
	var regen_before: int = wolf.poise
	wolf.end_of_round_poise()
	check_gt(float(wolf.poise), float(regen_before), "没破绽时每回合回复架势")
	check_eq(wolf.poise - regen_before, int(20 * 0.1), "回的量 = 上限的 10%（20×0.1=2）")
	# 回复要封顶在上限（不然打两回合就超上限了）
	wolf.poise = wolf.max_poise() - 1
	wolf.end_of_round_poise()
	check_eq(wolf.poise, wolf.max_poise(), "回复封顶在架势上限")


## 回复要在**真战斗的回合末**发生（不是只有一个能被调用的方法）。
## 破绽窗口的伤害加成：设计 04「打空架势 → 破绽窗口 → 绝招爆发」，
## 加成值 `POISE_BREAK_DAMAGE_BONUS = 0.5`（开发侧代码常量，`combat_const` 还没这一行，已记进对接表）。
## 以前用例只断言了「进破绽」与日志文案，**挨打更疼这件事没人验**——把加成调成 0 也不会红。
func _check_break_damage_bonus(db) -> void:
	var mods := {"force_hit": true, "no_variance": true, "no_block": true}
	var normal := _damage_once(db, mods, false)
	var broken := _damage_once(db, mods, true)
	check_gt(float(broken), float(normal), "破绽中挨同一招更疼")
	# 期望值**不许写成 `1.0 + 常量`**：那样常量一改、期望值跟着改，断言照样绿——这正是
	# 变异探针抓出来的假绿（框架说明决策 153）。这里写死 1.5 倍，常量被动过就红。
	check_float(
		float(broken), float(normal) * 1.5,
		"破绽窗口伤害 = 普通 × 1.5：普通 %d → 破绽 %d" % [normal, broken], 1.5,
	)


## 用同一招打同一个目标一次，返回打掉的血量。target_broken=true 时先把架势打空。
func _damage_once(db, mods: Dictionary, target_broken: bool) -> int:
	var hero = BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 10)
	hero.stats["crit_rate"] = 0.0        # 没有「关暴击」的开关，直接把这个变量去掉
	var wolf = EnemyFactoryScript.new(db).create("en_wolf", "normal")
	if target_broken:
		wolf.take_poise_damage(wolf.max_poise())
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [wolf], {"modifiers": mods})
	var before: int = wolf.hp
	sim.act(hero, db.get_row("skill_active", "sk_xuanwei_01"), wolf)
	return before - int(wolf.hp)


## 回复要在**真战斗的回合末**发生（不是只有一个能被调用的方法）。
## 让我方没招可出，这样这一回合里没人碰大寨主的架势，能单独观察回复量。
func _check_battle_regen(db) -> void:
	var hero = BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 10)
	hero.skills = PackedStringArray()
	var boss = EnemyFactoryScript.new(db).create("en_bd_boss", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [boss], {"modifiers": {"force_hit": true, "no_variance": true}})
	boss.take_poise_damage(80)                 # 160 → 80
	check_eq(boss.poise, 80, "先把大寨主架势打到一半")
	sim.step_round()
	check_eq(boss.poise, 80 + int(160 * 0.3), "打完一回合，大寨主按 30% 上限回复（+48）")


func _check_backstab_opening(db) -> void:
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", "team_wolf_pack")
	var encounter = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_BACK, "normal")
	var allies: Array = [BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 5)]
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup(allies, enemies, {"encounter": encounter})
	for enemy in enemies:
		check_lt(float(enemy.poise), float(enemy.max_poise()), "背袭开局敌方架势被压低：%s" % enemy.display_name)
		check_float(float(enemy.poise), float(enemy.max_poise()) * 0.7, "压到 70%（-30%）", 1.0)


## 「架势正好被打空」：`take_poise_damage(poise)` 必须刚好进破绽（扣到 0、不出负数），
## 差 1 点则不能；背袭比例拉满也只压到 1——`reduce_poise_ratio` 有下限，
## 不然一进战斗就破绽（设计只写「压低初始架势」，没写能压到 0）。
func _check_exact_break(db) -> void:
	var wolf = EnemyFactoryScript.new(db).create("en_wolf", "normal")
	var poise0 := int(wolf.poise)
	check_gt(float(poise0), 1.0, "野狼开局架势够做边界样本")
	check_eq(int(wolf.take_poise_damage(poise0 - 1)), poise0 - 1, "差 1 点的伤害全吃下")
	check_false(wolf.is_broken(), "差 1 点不进破绽")
	check_eq(int(wolf.poise), 1, "架势正好剩 1")
	check_eq(int(wolf.take_poise_damage(1)), 1, "再打 1 点正好打空")
	check_true(wolf.is_broken(), "正好打空 → 进破绽")
	check_eq(int(wolf.poise), 0, "打空后架势是 0（不出现负数）")

	var hero = BattleActorScript.from_character(db, db.get_row("character_base", "scholar_fallen"), 5)
	hero.reduce_poise_ratio(1.0)
	check_eq(int(hero.poise), 1, "背袭比例拉满也只压到 1")
	check_false(hero.is_broken(), "背袭压低架势不会一上来就破绽")
