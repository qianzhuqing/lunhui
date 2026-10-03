## 战斗序列：出手顺序、胜负、奖励、跑飞保护。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const EncounterScript := preload("res://src/core/encounter.gd")

const SEED := 424242
## 固定命中／无浮动／无格挡：拆招「伤害减半」的对照要精确到数值
const FIXED_MODS := {"force_hit": true, "no_variance": true, "no_block": true}


func suite_name() -> String:
	return "战斗序列与胜负"


func run() -> void:
	var db = get_db()
	_check_absolute_constants()
	_check_turn_order(db)
	_check_party_vs_team(db)
	_check_rewards(db)
	_check_difficulty_scaling(db)
	_check_determinism(db)
	_check_round_cap(db)
	_check_telegraph_and_parry(db)
	_check_flee(db)
	_check_exact_kill(db)
	_check_auto_strategies(db)
	_check_self_target_skill(db)


## 手感常量的**绝对值**断言（框架说明决策 153：期望值不许由被测常量自己算出来——
## 本文件里那些「按倍数关系」的断言挡不住整条公式被同倍缩放）。
## 这一组是 2026-10-03 变异探针第三块点名「没人钉」的几条。
func _check_absolute_constants() -> void:
	check_eq(BattleActorScript.SIDE_ALLY, 0, "我方阵营常量 = 0（Encounter 与模拟器共用这一处）")
	check_eq(BattleActorScript.SIDE_ENEMY, 1, "敌方阵营常量 = 1")
	check_float(BattleSimulatorScript.BASIC_ATTACK_POWER, 1.0, "普通攻击倍率 1.0", 0.0001)
	check_eq(BattleSimulatorScript.BASIC_ATTACK_POISE, 8, "普通攻击破架势 8")
	check_float(BattleSimulatorScript.DEFEND_POISE_GAIN, 0.15, "主动防御回架势上限的 15%", 0.0001)


func _check_turn_order(db) -> void:
	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var slow = _actor("slow", 10.0, BattleActorScript.SIDE_ALLY)
	var fast = _actor("fast", 30.0, BattleActorScript.SIDE_ALLY)
	var middle = _actor("middle", 20.0, BattleActorScript.SIDE_ALLY)
	var order: Array = simulator.turn_order([slow, fast, middle], [])
	check_eq(_names(order), "fast,middle,slow", "身法高的先手")

	var tie_a = _actor("tie_a", 15.0, BattleActorScript.SIDE_ALLY)
	var tie_b = _actor("tie_b", 15.0, BattleActorScript.SIDE_ENEMY)
	var tie_order: Array = simulator.turn_order([tie_a], [tie_b])
	check_eq(_names(tie_order), "tie_a,tie_b", "同速按稳定次序排列")


func _check_party_vs_team(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var allies := _party(db, 5)
	var enemies: Array = factory.create_team("team_wolf_pack", "normal")
	check_eq(enemies.size(), 3, "野狼群是 3 只")
	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var result: Dictionary = simulator.simulate(allies, enemies)
	check_eq(result["winner"], BattleSimulatorScript.WINNER_ALLY, "5 级两人组应打赢 2 级野狼群")
	check_in_range(float(result["rounds"]), 1.0, 20.0, "普通战回合数落在合理区间")
	check_gt(float(result["log"].size()), 0.0, "战斗日志不为空")
	for enemy in enemies:
		check_false(enemy.is_alive(), "我方获胜后敌人应全部倒下")


func _check_rewards(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var normal: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		_party(db, 5), factory.create_team("team_wolf_pack", "normal")
	)
	check_eq(int(normal["rewards"]["exp"]), 12 * 3, "普通难度经验 = 野狼 12 × 3")
	check_eq(int(normal["rewards"]["money"]), 5 * 3, "普通难度铜钱 = 野狼 5 × 3")

	# 绝境：exp ×1.7 → round(20.4)=20，money ×1.5 → round(7.5)=8
	var nightmare: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		_party(db, 8), factory.create_team("team_wolf_pack", "nightmare")
	)
	check_eq(nightmare["winner"], BattleSimulatorScript.WINNER_ALLY, "8 级队伍能扛住绝境野狼群")
	check_eq(int(nightmare["rewards"]["exp"]), 20 * 3, "绝境经验倍率 ×1.7")
	check_eq(int(nightmare["rewards"]["money"]), 8 * 3, "绝境铜钱倍率 ×1.5")


## 难度倍率（设计 05：难度只改数值，不改清单）——`enemy_base × difficulty_config` 的**战斗数值**三档。
## exp/money 的倍率由 `_check_rewards` 钉住；这里补 hp/atk/def：以前它们**没有任何断言**，
## 漏乘或写错就会出现「困难与普通一模一样」而用例全绿。期望值直接由表算，不写死数字，
## 另外补一条**单调性**断言：越难越厚越疼（表值被改成反向也要红）。
func _check_difficulty_scaling(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var base: Resource = db.get_row("enemy_base", "en_bd_thug")
	# 0.14.0（设计 10 §五）：敌人属性现在是「七维/派生列 **+ 装备加成**」——
	# 山寨喽啰身上挂着柴刀与布衣，布衣给 20 点气血，所以期望值要把装备那一份算进来
	# （装备加成是**乘倍率之前**的基础值：难度倍率照旧在最后乘）。
	var equip_hp := 0
	var equip_atk := 0.0
	var equip_def := 0.0
	for equip_row: Resource in db.rows_where("enemy_equip", "enemy_id", "en_bd_thug"):
		var equip: Resource = db.get_row("equip_base", str(equip_row.equip_id))
		if equip == null:
			continue
		equip_hp += int(equip.bonus_hp_max)
		equip_atk += float(equip.bonus_atk_phys)
		equip_def += float(equip.bonus_def_phys)
	check_gt(float(equip_hp), 0.0, "夹具前提：山寨喽啰的装备里有加血的那件（布衣 +20）")
	for difficulty_id: String in ["normal", "hard", "nightmare"]:
		var row: Resource = db.get_row("difficulty_config", difficulty_id)
		var actor = factory.create("en_bd_thug", difficulty_id)
		check_eq(
			actor.max_hp(), roundi(float(base.hp_base + equip_hp) * float(row.enemy_hp_mul)),
			"%s：血量 = (hp_base + 装备加成) × enemy_hp_mul" % difficulty_id,
		)
		check_float(
			actor.stat("atk_phys"), float(base.atk_phys + equip_atk) * float(row.enemy_atk_mul),
			"%s：外功 = (atk_phys + 装备加成) × enemy_atk_mul" % difficulty_id,
		)
		check_float(
			actor.stat("def_phys"), float(base.def_phys + equip_def) * float(row.enemy_def_mul),
			"%s：外防 = (def_phys + 装备加成) × enemy_def_mul" % difficulty_id,
		)
	var normal = factory.create("en_bd_thug", "normal")
	var hard = factory.create("en_bd_thug", "hard")
	var nightmare = factory.create("en_bd_thug", "nightmare")
	check_gt(float(hard.max_hp()), float(normal.max_hp()), "困难比普通血厚")
	check_gt(float(nightmare.max_hp()), float(hard.max_hp()), "绝境比困难血厚")
	check_gt(float(nightmare.stat("atk_phys")), float(hard.stat("atk_phys")), "绝境比困难更疼")
	check_gt(float(nightmare.stat("def_phys")), float(normal.stat("def_phys")), "绝境比普通更硬")


func _check_determinism(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var first: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		_party(db, 6), factory.create_team("team_bandit_patrol", "normal")
	)
	var second: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		_party(db, 6), factory.create_team("team_bandit_patrol", "normal")
	)
	check_eq(first["rounds"], second["rounds"], "同种子回合数一致")
	check_eq(str(first["log"]), str(second["log"]), "同种子战斗日志完全一致")


func _check_round_cap(db) -> void:
	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var tank = _actor("tank", 10.0, BattleActorScript.SIDE_ALLY, 100000)
	var wall = _actor("wall", 9.0, BattleActorScript.SIDE_ENEMY, 100000)
	var result: Dictionary = simulator.simulate([tank], [wall], {"max_rounds": 3})
	check_eq(int(result["rounds"]), 3, "达到回合上限就收手")
	check_eq(result["winner"], BattleSimulatorScript.WINNER_DRAW, "打不完判平局")

	# 上面那条**把上限压到 3**，所以默认值（50）从来没被走过：变异探针实测，把常数改成 10
	# 一整套自检照样绿——而它决定「打到多少回合算平局」，真打起来会直接把普通仗判平（决策 154）。
	# 这里不传 max_rounds，让默认值自己跑满。
	var default_tank = _actor("tank2", 10.0, BattleActorScript.SIDE_ALLY, 100000)
	var default_wall = _actor("wall2", 9.0, BattleActorScript.SIDE_ENEMY, 100000)
	var default_result: Dictionary = BattleSimulatorScript.new(db, RngServiceScript.new(SEED)).simulate(
		[default_tank], [default_wall]
	)
	check_eq(
		int(default_result["rounds"]), 50,
		"不指定上限时打满默认 50 回合（实得 %d）" % int(default_result["rounds"])
	)
	check_eq(default_result["winner"], BattleSimulatorScript.WINNER_DRAW, "默认上限下打不完也是平局")


## 预兆与拆招（设计 04 核心循环）：敌方出招前有预兆；我方花一次行动拆招，
## 读到的那一招伤害减半，拆招者反涨自身架势。
func _check_telegraph_and_parry(db) -> void:
	var enemies: Array = EnemyFactoryScript.new(db).create_team("team_wolf_pack", "normal")
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup(_party(db, 5), enemies, {"modifiers": FIXED_MODS})
	sim.begin_round()
	check_eq(sim.intent_lines().size(), 3, "每只野狼都有预兆")
	for enemy in enemies:
		check_ne(sim.intent_of(str(enemy.actor_id)), "", "%s 预兆了招式" % enemy.display_name)
	check_true(
		str(sim.intent_lines()[0]).contains("将用"),
		"预兆文案写清出什么招：%s" % str(sim.intent_lines()[0]),
	)

	# 伤害减半：同种子同对手，一条路拆招、一条路不拆
	var plain := _enemy_round_damage(db, false)
	var parried := _enemy_round_damage(db, true)
	check_gt(float(plain), 0.0, "不拆招时敌人打出伤害：%d" % plain)
	check_eq(parried, int(floor(float(plain) * 0.5)), "拆招后那一招伤害减半（%d → %d）" % [plain, parried])

	# 反涨自身架势 + 花掉这次行动 + 同一个敌人一回合只能拆一次
	var hero = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY, 400)
	var foe = _actor("foe", 9.0, BattleActorScript.SIDE_ENEMY, 400)
	var duel = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	duel.setup([hero], [foe], {"modifiers": FIXED_MODS})
	duel.begin_round()
	check_eq(str(duel.current_actor().actor_id), "hero", "身法高的我方先手")
	hero.poise = 0
	hero.poise_max = 40
	var result: Dictionary = duel.parry(hero, foe)
	check_true(bool(result["ok"]), "拆招成功：%s" % str(result.get("error", "")))
	# 反涨 = 自身架势上限的 15%（PARRY_POISE_GAIN 是开发侧定的，交接表待设计补列）。
	# 这里写死 40 × 0.15 = 6：只写 `> 0` 时把 0.15 改成 0.25 也不会红（变异探针实测）。
	# 注意 `15%%`：GDScript 的 `%` 格式化里，字面百分号要写成 `%%`。写成 `15%（…` 会报运行期错误
	# （"String formatting error: unsupported format character"）。**实测它不掐断函数**：断言照跑，
	# 只是日志里留一行 ERROR、消息里的 `%d` 没被替换——正因如此它藏了很久：日志扫描当时只认
	# `SCRIPT ERROR`，而这行不带那个前缀（见框架说明决策 190）。
	check_eq(int(result["poise_gain"]), 6, "拆招反涨架势 = 上限 15%%（40 × 0.15 = 6，实得 %d）" % int(result["poise_gain"]))
	check_eq(int(hero.poise), int(result["poise_gain"]), "架势真的加上去了")
	check_true(duel.is_parried("foe"), "目标被标记为已拆招")
	check_eq(str(duel.current_actor().actor_id), "foe", "拆招花掉了这次行动（轮到敌人）")
	check_eq(
		duel.parry_block_reason(duel.current_actor(), foe), "只能拆敌人的招",
		"不是我方行动时拆不了",
	)
	# 同一个人再来一次也不行（行动已经用完，直接给原因）
	check_false(bool(duel.parry(hero, foe)["ok"]), "同一个敌人这一回合不能重复拆")


## 自动战斗三档策略（设计 04：保守／均衡／全力，语义是开发侧定的，等设计确认）：
## 同一局面三种策略要挑出**不同**的招式，否则这个功能就是摆设。
func _check_auto_strategies(db) -> void:
	# 一个能同时装下单发高倍率与群攻的对手组：两只活着的敌人，群攻才有价值
	var hero = _actor("hero", 20.0, BattleActorScript.SIDE_ALLY, 400)
	hero.stats["qi_max"] = 60.0
	hero.refill()
	# 起手（0 内力 1.0 倍）、斩风（16 内力、2.1 倍单体）、横扫（14 内力、1.3 倍 ×3 段全体）
	hero.skills = PackedStringArray(["sk_xuanwei_01", "sk_boss_zhangfeng", "sk_boss_hengsao"])
	var foe_a = _actor("foe_a", 10.0, BattleActorScript.SIDE_ENEMY, 400)
	var foe_b = _actor("foe_b", 10.0, BattleActorScript.SIDE_ENEMY, 400)

	var picks := {}
	for strategy_id: String in BattleSimulatorScript.STRATEGY_ORDER:
		var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
		sim.setup([hero], [foe_a, foe_b], {"strategy": strategy_id})
		var picked: Resource = sim.pick_skill(hero)
		picks[strategy_id] = str(picked.skill_id) if picked != null else ""
	check_eq(picks[BattleSimulatorScript.STRATEGY_CONSERVATIVE], "sk_xuanwei_01", "保守挑最省内力的起手式")
	check_eq(picks[BattleSimulatorScript.STRATEGY_BALANCED], "sk_boss_hengsao", "均衡挑期望总伤害最高的群攻")
	check_eq(picks[BattleSimulatorScript.STRATEGY_ALL_OUT], "sk_boss_zhangfeng", "全力挑单发倍率最高的斩风")
	check_eq(
		[picks[BattleSimulatorScript.STRATEGY_CONSERVATIVE], picks[BattleSimulatorScript.STRATEGY_BALANCED],
			picks[BattleSimulatorScript.STRATEGY_ALL_OUT]].size(),
		3, "三种策略各挑各的（不是同一个答案换三个名字）",
	)

	# 默认是均衡；不认识的 id 拒绝且不改现状
	var default_sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	default_sim.setup([hero], [foe_a, foe_b], {})
	check_eq(default_sim.strategy(), BattleSimulatorScript.STRATEGY_BALANCED, "默认策略是均衡")
	check_false(default_sim.set_strategy("nonsense"), "不认识的策略被拒绝")
	check_eq(default_sim.strategy(), BattleSimulatorScript.STRATEGY_BALANCED, "拒绝后策略不变")
	check_eq(default_sim.cycle_strategy(), BattleSimulatorScript.STRATEGY_ALL_OUT, "轮换顺序：均衡 → 全力")
	check_eq(default_sim.cycle_strategy(), BattleSimulatorScript.STRATEGY_CONSERVATIVE, "轮换顺序：全力 → 保守")
	check_eq(default_sim.cycle_strategy(), BattleSimulatorScript.STRATEGY_BALANCED, "轮换回到均衡")

	# 策略只改我方：同一只敌人换三档策略都应该挑同一招
	var enemy_picks := {}
	for strategy_id: String in BattleSimulatorScript.STRATEGY_ORDER:
		var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
		sim.setup([hero], [foe_a, foe_b], {"strategy": strategy_id})
		var picked: Resource = sim.pick_skill(foe_a)
		enemy_picks[strategy_id] = str(picked.skill_id) if picked != null else ""
	check_eq(
		enemy_picks[BattleSimulatorScript.STRATEGY_BALANCED],
		enemy_picks[BattleSimulatorScript.STRATEGY_ALL_OUT],
		"敌人不受玩家策略影响",
	)
	check_eq(
		enemy_picks[BattleSimulatorScript.STRATEGY_BALANCED],
		enemy_picks[BattleSimulatorScript.STRATEGY_CONSERVATIVE],
		"敌人不受玩家策略影响（保守）",
	)


## 一个回合里敌人打出的总伤害（拆招减半的对照：两条路只有「拆不拆」不同）
func _enemy_round_damage(db, with_parry: bool) -> int:
	var hero = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY, 400)
	var foe = _actor("foe", 9.0, BattleActorScript.SIDE_ENEMY, 400)
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [foe], {"modifiers": FIXED_MODS})
	sim.begin_round()
	var actor = sim.current_actor()
	if with_parry:
		sim.parry(actor, foe)
	else:
		sim.act(actor, sim.available_skills(actor)[0], foe)
	sim.auto_act()
	var total := 0
	for event: Dictionary in sim.take_events():
		if str(event.get("kind", "")) == "hit" and str(event.get("source", "")) == "foe":
			total += int(event.get("damage", 0))
	return total


## 逃跑（设计 02：被追上第一回合不能逃）。成功率公式是开发侧定的，这里钉住口径与两个分支。
func _check_flee(db) -> void:
	var even := _flee_chance_with(db, 10.0, 10.0)
	check_float(even, 0.5, "同身法时成功率 50%")
	check_gt(_flee_chance_with(db, 30.0, 10.0), even, "身法高的一边更容易逃掉")
	check_lt(_flee_chance_with(db, 10.0, 30.0), even, "身法低的一边更难得手")
	# 上下限要**顶到**才算钉住：写成 `<= 0.95` 时，把上限调到 0.85 也照样绿（变异探针实测）。
	check_float(_flee_chance_with(db, 1000.0, 1.0), 0.95, "身法碾压时成功率顶到上限 95%")
	check_float(_flee_chance_with(db, 1.0, 1000.0), 0.25, "身法被碾压时成功率顶到下限 25%")

	# 成功：以「撤退」收场，没有奖励
	var win: Dictionary = _flee_fight(db, {"force_flee": true})
	check_true(bool(win["escaped"]), "force_flee 下必定撤成功")
	check_eq(str(win["winner"]), BattleSimulatorScript.WINNER_FLEE, "撤退是 flee 结局，不是胜负")
	check_eq(int(win["rewards"]["exp"]), 0, "撤退不结算经验")
	check_eq(int(win["rewards"]["money"]), 0, "撤退不结算铜钱")

	# 失败：白费一次行动，战斗继续
	var fail: Dictionary = _flee_fight(db, {"no_flee": true})
	check_false(bool(fail["escaped"]), "no_flee 下撤不掉")
	check_ne(str(fail["winner"]), BattleSimulatorScript.WINNER_FLEE, "撤不掉就不是撤退结局")

	# 被追上：第一回合撤不了，过了第一回合就能撤
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var hero = _actor("hero", 30.0, BattleActorScript.SIDE_ALLY, 400)
	var foe = _actor("foe", 5.0, BattleActorScript.SIDE_ENEMY, 400)
	sim.setup([hero], [foe], {"encounter": _caught_encounter(db), "modifiers": FIXED_MODS})
	sim.begin_round()
	var blocked: String = sim.flee_block_reason(hero)
	check_true(blocked.contains("第一回合"), "被追上的第一回合撤不掉：%s" % blocked)
	sim.step_round()
	sim.begin_round()
	check_eq(sim.flee_block_reason(hero), "", "过了第一回合就能撤")


## 气血「正好被打死」：`take_damage(hp)` 必须让它倒下（归 0、is_alive 假），差 1 点不能；
## 伤害只减不加——0／负数不许当治疗用，倒下的单位也不掉负血。
func _check_exact_kill(db) -> void:
	var hero = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY, 40)
	check_eq(int(hero.take_damage(39)), 39, "差 1 点：伤害全吃下")
	check_true(hero.is_alive(), "还剩 1 血，站着")
	check_eq(int(hero.hp), 1, "气血正好剩 1")
	check_eq(int(hero.take_damage(5)), 1, "超出剩余气血的伤害只按剩下的扣")
	check_eq(int(hero.hp), 0, "正好打空：气血归 0")
	check_false(hero.is_alive(), "气血 0 就是倒下")
	check_eq(int(hero.take_damage(10)), 0, "倒下的单位再挨打不掉负血")
	check_eq(int(hero.hp), 0, "气血不会变负")
	check_eq(int(hero.take_damage(-5)), 0, "负伤害不是治疗")
	check_eq(int(hero.hp), 0, "负伤害不改气血")


func _flee_chance_with(db, my_speed: float, their_speed: float) -> float:
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var hero = _actor("hero", my_speed, BattleActorScript.SIDE_ALLY, 400)
	var foe = _actor("foe", their_speed, BattleActorScript.SIDE_ENEMY, 400)
	sim.setup([hero], [foe], {})
	return sim.flee_chance(hero)


func _flee_fight(db, modifiers: Dictionary) -> Dictionary:
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var hero = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY, 400)
	var foe = _actor("foe", 10.0, BattleActorScript.SIDE_ENEMY, 400)
	sim.setup([hero], [foe], {"modifiers": modifiers})
	sim.begin_round()
	var result: Dictionary = sim.flee(hero)
	check_true(bool(result["ok"]), "逃跑判定能跑：%s" % str(result.get("error", "")))
	var out: Dictionary = sim.result()
	out["escaped"] = bool(result["escaped"])
	return out


func _caught_encounter(db):
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", str(spawn.team_id))
	return EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_CAUGHT, "normal")


func _party(db, level: int) -> Array:
	# 0.4.0 起 character_base 只有一个角色模板，队伍由同一个模板复制出多人
	var out: Array = []
	var row: Resource = db.get_row("character_base", "scholar_fallen")
	for index in range(2):
		var actor = BattleActorScript.from_character(db, row, level)
		actor.actor_id = "scholar_%d" % (index + 1)
		actor.display_name = "书生 %d" % (index + 1)
		out.append(actor)
	return out


## `target_type=self` 的招式必须打**自己**（反噬／自伤那一类）。
##
## 06 允许 `single / self / all_enemy`，代码却只在手选路径认了 `all_enemy`、在自动路径也认了它——
## `self` 两处都掉进「打对面的目标」分支：**自伤招式会打到敌人身上**（2026-10-03 修，见决策 224）。
## 发行数据里还没有这类招式，所以用夹具（复制表 + 加一条 self 招式）把两条行动路径都走一遍。
func _check_self_target_skill(db) -> void:
	var stub = load("res://src/core/table_db.gd").new()
	stub.load_all()
	var rows: Resource = stub.tables["skill_active"].duplicate(true)
	var self_skill = load("res://src/data/tables/skill_active_row.gd").new()
	self_skill.skill_id = "sk_test_self"
	self_skill.element = "external"
	self_skill.damage_type = "dmg_normal"
	self_skill.power_ratio = 1.0
	self_skill.hit_count = 1
	self_skill.target_type = "self"
	# 手工建的行要**自己写主键与索引**：`TableResource.get_row` 查的是 `index` 字典，
	# 加载器平时会写这两样，夹具不补就会出现「表里有这行、get_row 却找不到」（这个坑踩过一次）。
	self_skill.id = "sk_test_self"
	rows.rows.append(self_skill)
	rows.index[self_skill.id] = rows.rows.size() - 1
	stub.tables["skill_active"] = rows

	# ① 手选路径：点着敌人放，也得打自己
	var sim = BattleSimulatorScript.new(stub, RngServiceScript.new(SEED))
	var hero = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY)
	var foe = _actor("foe", 10.0, BattleActorScript.SIDE_ENEMY)
	hero.skills = PackedStringArray(["sk_test_self"])
	sim.setup([hero], [foe], FIXED_MODS)
	var hero_hp := int(hero.hp)
	var foe_hp := int(foe.hp)
	sim.act(hero, self_skill, foe)
	check_lt(float(hero.hp), float(hero_hp), "self 招式打自己（手选：点着敌人也一样）")
	check_eq(int(foe.hp), foe_hp, "敌人一点血都没掉（以前这里会误伤敌人）")

	# ② 自动战斗路径：同一个口径
	var sim2 = BattleSimulatorScript.new(stub, RngServiceScript.new(SEED))
	var hero2 = _actor("hero", 10.0, BattleActorScript.SIDE_ALLY)
	var foe2 = _actor("foe", 10.0, BattleActorScript.SIDE_ENEMY)
	hero2.skills = PackedStringArray(["sk_test_self"])
	sim2.setup([hero2], [foe2], FIXED_MODS)
	sim2.begin_round()
	var hero2_hp := int(hero2.hp)
	var foe2_hp := int(foe2.hp)
	var lines: Array = sim2.auto_act()
	check_lt(float(hero2.hp), float(hero2_hp), "self 招式打自己（自动战斗同样遵守 target_type）")
	check_eq(int(foe2.hp), foe2_hp, "自动战斗下敌人也不掉血")
	check_true(str(lines).contains("hero"), "战报里点名的是自己：%s" % str(lines))


func _actor(actor_id: String, speed: float, side: int, hp: int = 400) -> Variant:
	var actor = BattleActorScript.new()
	actor.actor_id = actor_id
	actor.display_name = actor_id
	actor.side = side
	actor.level = 5
	actor.stats = {
		"hp_max": float(hp), "qi_max": 20.0,
		"atk_phys": 10.0, "atk_qi": 0.0,
		"def_phys": 0.0, "def_qi": 0.0,
		"speed": speed, "hit_rate": 1.0, "dodge_rate": 0.0,
		"crit_rate": 0.0, "crit_dmg": 0.0,
	}
	actor.skills = PackedStringArray(["sk_xuanwei_01"])
	actor.refill()
	return actor


func _names(actors: Array) -> String:
	var out := PackedStringArray()
	for actor in actors:
		out.append(str(actor.actor_id))
	return ",".join(out)
