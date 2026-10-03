## 异常状态与持续伤害（04_战斗与伤害.md「持续伤害三条规则」与「异常状态」）。
##
## 逐条钉住：几率（运）与强度（智）、同源同类叠层刷新时长、快照制、回合末统一结算、
## 不吃暴击与闪避、防御口径（中毒/灼伤无视内外防、流血吃外功防御、内伤吃内功防御——由 `damage_type.use_def_*` 决定）、
## 灼伤削防、内伤内力回复减半、可驱散、斩杀阈值。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const DamageResolverScript := preload("res://src/core/damage_resolver.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")

const SEED := 20261003


func suite_name() -> String:
	return "异常状态与持续伤害"


func run() -> void:
	var db = get_db()
	_check_chance(db)
	_check_stacking(db)
	_check_snapshot(db)
	_check_defense_rules(db)
	_check_burn_and_internal(db)
	_check_unhandled_rules(db)
	_check_dispellable(db)
	_check_battle_flow(db)
	_check_enemy_resistances(db)
	_check_player_resistances(db)
	_check_regen_caps(db)


## 触发几率 = base_chance × status_chance_mul + 异常触发率（运换算），再乘 (1 − 抗性)
func _check_chance(db) -> void:
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var attacker = _actor("atk", 10.0, BattleActorScript.SIDE_ALLY)
	attacker.stats["debuff_chance"] = 0.04
	var target = _actor("def", 10.0, BattleActorScript.SIDE_ENEMY)
	var poison_skill: Resource = db.get_row("skill_active", "sk_wudu_01")
	# 中毒 base_chance 0.35 × 招式 1.5 + 运给的 4% = 56.5%
	check_float(sim.status_chance(attacker, target, poison_skill), 0.565, "伤害型招式上异常的几率", 0.0001)
	# 目标有 50% 抗性 → 减半
	target.resistances["poison"] = 0.5
	check_float(sim.status_chance(attacker, target, poison_skill), 0.2825, "抗性把几率减半", 0.0001)
	target.resistances.erase("poison")
	# 招式没有 status_id 时不上状态
	var plain: Resource = db.get_row("skill_active", "sk_xuanwei_01")
	check_float(sim.status_chance(attacker, target, plain), 0.0, "不带异常的招式几率为 0")


## 同源同类叠层、刷新时长；到上限只刷新不加层
func _check_stacking(db) -> void:
	var actor = _actor("victim", 10.0, BattleActorScript.SIDE_ENEMY)
	for index in range(6):
		actor.add_status("poison", "atk", 10.0, 5, 3, "", true)
	check_eq(actor.status_stacks("poison"), 5, "中毒最多 5 层")
	var entry: Dictionary = actor.statuses[0]
	check_eq(int(entry["remaining"]), 3, "刷新时长（不会叠到 6 回合）")
	# 不同来源独立计算
	actor.add_status("poison", "other", 8.0, 5, 3, "", true)
	check_eq(actor.statuses.size(), 2, "不同来源的中毒是两条")
	check_eq(actor.status_stacks("poison"), 6, "总层数按来源相加")


## 快照制：每层按施加那一刻的伤害记，之后重新施放不会改老层的值
func _check_snapshot(db) -> void:
	var actor = _actor("victim", 10.0, BattleActorScript.SIDE_ENEMY)
	actor.add_status("poison", "atk", 10.0, 5, 3, "", true)
	actor.add_status("poison", "atk", 25.0, 5, 3, "", true)
	var ticks: Array = actor.tick_statuses()
	check_eq(ticks.size(), 1, "一条状态跳一次")
	if not ticks.is_empty():
		check_eq(int(ticks[0]["damage"]), 35, "10 + 25：每层按各自锁定的值跳")


## 防御口径（按当前数据）：中毒/灼伤无视内外防御，流血吃外功防御，**内伤吃内功防御**。
## 注：04_战斗与伤害.md 的伤害类型表把内伤写成「吃内防 否」，三处口径不一致已记缺口 #21 请设计确认。
func _check_defense_rules(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var attacker = _actor("atk", 10.0, BattleActorScript.SIDE_ALLY)
	attacker.stats["atk_phys"] = 60.0
	attacker.stats["atk_qi"] = 60.0
	var tanky = _actor("def", 10.0, BattleActorScript.SIDE_ENEMY)
	tanky.stats["def_phys"] = 120.0
	tanky.stats["def_qi"] = 120.0

	var poison: Resource = db.get_row("skill_active", "sk_wudu_02")
	var bleed: Resource = db.get_row("skill_active", "sk_xuanwei_07")
	var internal: Resource = db.get_row("skill_active", "sk_drunk_01")
	# **必须把状态自己的伤害类型传进去**：`resolve_dot` 的 `dot_type_id` 缺省时退回招式自己的
	# `damage_type`——`sk_drunk_01`（醉里乾坤·颠倒）是 `dmg_normal`，于是「内伤吃内功防御」那条
	# 以前其实在验**外功防御**（def_phys 120 → 1200 当然更低），**内伤这个格子从来没被验过**。
	# 是「把 dot_internal.use_def_qi 改成 0」的反向探针没红，才把这条弱断言暴露出来（决策 91）。
	var poison_damage := int(_dot(resolver, attacker, tanky, poison, "dot_poison")["damage"])
	var bleed_damage := int(_dot(resolver, attacker, tanky, bleed, "dot_bleed")["damage"])
	var internal_damage := int(_dot(resolver, attacker, tanky, internal, "dot_internal")["damage"])

	# 把防御堆到极高：吃防御的那两条要明显变低，无视防御的不受影响
	var wall = _actor("def2", 10.0, BattleActorScript.SIDE_ENEMY)
	wall.stats["def_phys"] = 1200.0
	wall.stats["def_qi"] = 1200.0
	check_eq(
		int(_dot(resolver, attacker, wall, poison, "dot_poison")["damage"]), poison_damage,
		"中毒无视内外防御",
	)
	check_lt(
		float(_dot(resolver, attacker, wall, bleed, "dot_bleed")["damage"]), float(bleed_damage),
		"流血吃外功防御",
	)
	check_lt(
		float(_dot(resolver, attacker, wall, internal, "dot_internal")["damage"]), float(internal_damage),
		"内伤吃内功防御（按当前数据；04 的伤害类型表写的是「不吃」，见缺口 #21）",
	)


## dot_type_id 传状态自己的伤害类型（模拟器也是这么调的）
func _dot(resolver, attacker, defender, skill: Resource, dot_type_id: String = "") -> Dictionary:
	return resolver.resolve_dot(attacker, defender, skill, {"no_variance": true}, dot_type_id)


## 灼伤每层削 5% 外功防御；内伤让内力回复减半
func _check_burn_and_internal(db) -> void:
	var victim = _actor("victim", 10.0, BattleActorScript.SIDE_ENEMY)
	victim.stats["def_phys"] = 100.0
	check_float(victim.defense_multiplier("def_phys"), 1.0, "没状态时防御不变")
	victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true)
	victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true)
	check_float(victim.defense_multiplier("def_phys"), 0.9, "两层灼伤削 10% 外功防御", 0.0001)
	check_float(victim.defense_multiplier("def_qi"), 1.0, "灼伤只削外功防御")

	# 削防真的让直伤变高（同一发攻击，带火伤的目标更疼）
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var attacker = _actor("atk", 10.0, BattleActorScript.SIDE_ALLY)
	var skill: Resource = db.get_row("skill_active", "sk_xuanwei_01")
	var plain = _actor("plain", 10.0, BattleActorScript.SIDE_ENEMY)
	plain.stats["def_phys"] = 100.0
	var before := int(resolver.resolve(attacker, plain, skill, {"no_variance": true})["damage"])
	var after := int(resolver.resolve(attacker, victim, skill, {"no_variance": true})["damage"])
	check_gt(float(after), float(before), "灼伤削防后同一招打得更疼（%d → %d）" % [before, after])

	# 内伤：内力回复减半
	var hurt = _actor("hurt", 10.0, BattleActorScript.SIDE_ALLY)
	hurt.stats["qi_regen"] = 10.0
	hurt.qi = 0
	hurt.end_of_round_regen()
	check_eq(hurt.qi, 10, "正常每回合回 10 内力")
	hurt.qi = 0
	hurt.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", false)
	check_float(hurt.qi_regen_factor(), 0.5, "内伤内力回复减半")
	hurt.end_of_round_regen()
	check_eq(hurt.qi, 5, "内伤下半量回复")


## 「表里配了、代码还不认」的状态规则必须报出来，不许静默。
##
## 表里 `bleed.extra_rule = rule_bleed_move`（流血换位），而它依赖站位、设计还没定规则——
## 以前代码把它当空气：既不做效果，也不出声，`actor.statuses` 里那个 `rule` 字段就这么躺着。
## 现在会记进 `unhandled_rules` 并由战斗结算播成「暂缓规则」（和奇袭的「无防备」同一套口径）。
func _check_unhandled_rules(db) -> void:
	var victim = _actor("victim", 10.0, BattleActorScript.SIDE_ENEMY)
	# 表里流血那条规则 id 就是它（不是代码里编的）
	check_eq(str(db.get_row("status_effect", "bleed").extra_rule), "rule_bleed_move", "流血规则取自表")
	victim.add_status("bleed", "atk", 5.0, 3, 4, "rule_bleed_move", true)
	check_true(victim.unhandled_rules.has("rule_bleed_move"), "不认识的规则被记下来")
	# 已经实现的规则不该被误报
	victim.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true)
	victim.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", true)
	check_eq(victim.unhandled_rules.size(), 1, "只记不认识的（灼伤／内伤都实现了）：%s" % str(victim.unhandled_rules))
	# 无规则（空串）也不该被当成「不认识」
	victim.add_status("poison", "atk", 5.0, 5, 3, "", true)
	check_eq(victim.unhandled_rules.size(), 1, "空规则不算未实现")
	# 结算要把它播出来：界面读 result()["pending_rules"] 写「暂缓规则：…」
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var ally = _actor("ally", 10.0, BattleActorScript.SIDE_ALLY)
	sim.setup([ally], [victim], {})
	var pending: Array = Array(sim.result().get("pending_rules", []))
	var mentioned := false
	var leaks_id := false
	for line: String in pending:
		if line.contains("流血"):
			mentioned = true
		if line.contains("rule_"):
			leaks_id = true
	check_true(mentioned, "结算的暂缓规则里点名了它（说中文状态名）：%s" % str(pending))
	check_false(leaks_id, "暂缓规则文案不漏表内 id：%s" % str(pending))


## 可驱散：毒/灼伤/流血能被清，内伤清不掉
func _check_dispellable(db) -> void:
	var actor = _actor("victim", 10.0, BattleActorScript.SIDE_ENEMY)
	actor.add_status("poison", "atk", 5.0, 5, 3, "", true)
	actor.add_status("burn", "atk", 5.0, 3, 2, "rule_burn_def_down", true)
	actor.add_status("internal", "atk", 5.0, 1, 5, "rule_internal_qi", false)
	var cleared: PackedStringArray = actor.clear_dispellable()
	check_eq(cleared.size(), 2, "清掉两条可驱散的")
	check_true(actor.has_status("internal"), "内伤不可驱散")
	check_false(actor.has_status("poison"), "中毒被清掉")


## 整场战斗：命中挂状态 → 回合末统一跳字 → 血量过低时斩杀结算
func _check_battle_flow(db) -> void:
	var attacker = _actor("atk", 20.0, BattleActorScript.SIDE_ALLY)
	attacker.skills = PackedStringArray(["sk_wudu_02"])
	attacker.stats["debuff_chance"] = 0.4          # 抬几率，保证上得去
	var victim = _actor("victim", 5.0, BattleActorScript.SIDE_ENEMY)
	victim.hp = victim.max_hp()
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [victim], {})
	sim.begin_round()
	var skill: Resource = db.get_row("skill_active", "sk_wudu_02")
	sim.act(attacker, skill, victim)
	check_true(victim.has_status("poison"), "命中后上了中毒")
	var stacks: int = victim.status_stacks("poison")
	check_eq(stacks, 1, "第一次施放 1 层")
	check_eq(sim.kill_style_of(victim.actor_id), "", "还没死人，没有死因")

	# 敌方随后行动 → 本回合结束 → 回合末统一结算跳一次毒（层数留着继续跳）
	sim.auto_act()
	var dot_events := 0
	for event: Dictionary in sim.take_events():
		if str(event.get("kind", "")) == "dot":
			dot_events += 1
	check_gt(float(dot_events), 0.0, "回合末跳出持续伤害事件")

	# 斩杀：血量压到阈值以下，剩余层数立即结算并清空
	var execute_target = _actor("exec", 5.0, BattleActorScript.SIDE_ENEMY)
	execute_target.hp = maxi(1, int(float(execute_target.max_hp()) * 0.1))
	execute_target.add_status("poison", "atk", 9.0, 5, 3, "", true)
	execute_target.add_status("poison", "atk", 9.0, 5, 3, "", true)
	var sim2 = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim2.setup([attacker], [execute_target], {})
	sim2.begin_round()
	sim2.act(attacker, skill, execute_target)
	var executed := false
	for event: Dictionary in sim2.take_events():
		if str(event.get("kind", "")) == "dot" and bool(event.get("execute", false)):
			executed = true
	check_true(executed or not execute_target.is_alive(), "血量过低时剩余持续伤害立即结算")
	check_true(execute_target.statuses.is_empty() or not execute_target.is_alive(), "斩杀后清空层数")
	if not execute_target.is_alive():
		check_eq(sim2.kill_style_of(execute_target.actor_id), "poison", "死于毒伤 → 死因记成中毒（毒杀判定用）")


## 0.12.0：抗性是**角色与敌人共用的派生数值**——玩家练五毒心法也能拿到毒抗。
##
## 由来（2026-10-03）：`skill_passive_stat` 早就配了 `stat:res_poison`（五毒心法系 0.10~0.25），
## 而状态生效那条路只读 `BattleActor.resistances` 快照——那是敌人行专用的一份，
## 玩家这份抗性算进了派生数值却**没人读**，等于「练了以毒攻毒、照样中毒」。
func _check_player_resistances(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	var before: Array = PartyBuilderScript.build_actors(db, state)
	check_eq(before.size(), 1, "单人队伍")
	var bare = before[0]
	check_float(bare.resistance_of("poison"), 0.0, "没练内功时没有毒抗")

	# 学会并装上五毒心法·引气（skill_passive_stat 给 stat:res_poison 0.10）
	state.learn_skill(char_id, "pf_wudu_01")
	state.set_loadout(char_id, PackedStringArray(), PackedStringArray(["pf_wudu_01"]))
	var armed: Array = PartyBuilderScript.build_actors(db, state)
	var actor = armed[0]
	check_float(actor.stat("res_poison"), 0.10, "内功的毒抗进了派生数值")
	check_float(actor.resistance_of("poison"), 0.10, "抗性读取走同一条派生数值（角色与敌人共用）")

	# 真实效果：同一个人打同一招，目标有这条抗性时上状态的几率按 (1−抗性) 打折
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var attacker = _actor("atk_resist", 10.0, BattleActorScript.SIDE_ALLY)
	attacker.stats["debuff_chance"] = 0.0
	var poison_skill: Resource = db.get_row("skill_active", "sk_wudu_01")
	var chance_bare: float = sim.status_chance(attacker, bare, poison_skill)
	var chance_armed: float = sim.status_chance(attacker, actor, poison_skill)
	check_float(chance_armed, chance_bare * 0.9, "0.10 毒抗把上状态几率乘 0.9", 0.0001)

	# 敌人那一侧没变：毒手 50%（现在也走同一条 `resistance_of`）
	var factory = load("res://src/core/enemy_factory.gd").new(db)
	var poison_hand = factory.create("en_bd_poison_hand", "normal")
	check_float(poison_hand.resistance_of("poison"), 0.15, "毒手那 15% 毒抗（内功给的）走同一条读取口")


## 敌人的异常抗性（enemy_base 的 res_* 列）：几率要减半，内伤抗性还减伤害
func _check_enemy_resistances(db) -> void:
	var factory = load("res://src/core/enemy_factory.gd").new(db)
	var wolf = factory.create("en_wolf", "normal")
	# 0.14.0（设计 10 §二）：毒/火/流血三条**特权列取消**——敌人的抗性改为「装内功才有」，
	# 值来自 `enemy_passive` → `skill_passive_stat` 的 `stat:res_*`；读的入口也统一成
	# `resistance_of()`（角色与敌人共用派生数值）。**数值比旧列低**，见下面的断言。
	check_float(wolf.resistance_of("poison"), 0.0, "野狼没装五毒内功 → 没有毒抗")
	var poison_hand = factory.create("en_bd_poison_hand", "normal")
	check_float(poison_hand.resistance_of("poison"), 0.15, "毒手装了五毒心法（pf_wudu_03 给 0.15 毒抗）")
	var boss = factory.create("en_bd_boss", "normal")
	check_float(boss.resistance_of("poison"), 0.10, "大寨主装了五毒心法·腐骨（pf_wudu_02 给 0.10 毒抗）")
	check_float(boss.resistance_of("burn"), 0.0, "大寨主没有火抗（旧特权列 0.2 已随设计取消）")
	check_float(boss.resistance_of("bleed"), 0.0, "大寨主没有流血抗（旧特权列 0.2 已随设计取消）")
	var drunk = factory.create("en_hidden_drunk", "normal")
	check_float(drunk.resistance_of("bleed"), 0.15, "醉刀客装了罗汉功（pf_luohan_01 给 0.15 流血抗）")

	# 几率：同一个人打野狼 vs 打毒手，抗性把几率砍半
	var attacker = _actor("clever", 10.0, BattleActorScript.SIDE_ALLY)
	attacker.stats["debuff_chance"] = 0.0
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([attacker], [wolf], {})
	var skill: Resource = db.get_row("skill_active", "sk_wudu_03")   # 中毒 0.9 × 2.2
	var base_chance := sim.status_chance(attacker, wolf, skill)
	var resisted := sim.status_chance(attacker, poison_hand, skill)
	check_float(
		resisted, base_chance * (1.0 - 0.15),
		"毒抗按内功给的 0.15 压低上毒几率（%.3f → %.3f）" % [base_chance, resisted], 0.0001
	)

	# res_internal 额外减免内伤类持续伤害
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var tall = _actor("tall", 10.0, BattleActorScript.SIDE_ENEMY)
	tall.stats["res_internal"] = 0.0
	var tough = _actor("tough", 10.0, BattleActorScript.SIDE_ENEMY)
	tough.stats["res_internal"] = 0.5
	var internal: Resource = db.get_row("skill_active", "sk_drunk_01")
	var plain_damage := int(_dot(resolver, attacker, tall, internal, "dot_internal")["damage"])
	var resisted_damage := int(_dot(resolver, attacker, tough, internal, "dot_internal")["damage"])
	check_lt(float(resisted_damage), float(plain_damage), "内伤抗性减伤（%d → %d）" % [plain_damage, resisted_damage])
	# 上限**来自表**（stat_def.res_internal.max_value），不是代码里写死的常数：
	# 把表里的上限改成 0.5，同一个超上限的抗性（0.9）就只按 0.5 减伤 → 伤害比默认 0.75 时更高。
	# 这条是 2026-10-03 把 `clampf(…, 0.75)` 改成 `_stat_cap("res_internal", …)` 的回归锚
	# （见框架说明决策 180）。
	var lowered: Resource = db.tables["stat_def"].duplicate(true)
	for row: Resource in lowered.rows:
		if str(row.stat_id) == "res_internal":
			row.max_value = 0.5
	var lowered_db = TableDbScript.new()
	lowered_db.tables = db.tables.duplicate()
	lowered_db.tables["stat_def"] = lowered
	var capped = _actor("capped", 10.0, BattleActorScript.SIDE_ENEMY)
	capped.stats["res_internal"] = 0.9
	var default_cap := int(_dot(resolver, attacker, capped, internal, "dot_internal")["damage"])
	var lowered_resolver = DamageResolverScript.new(lowered_db, RngServiceScript.new(SEED))
	var lowered_cap := int(_dot(lowered_resolver, attacker, capped, internal, "dot_internal")["damage"])
	check_gt(float(lowered_cap), float(default_cap),
		"内伤抗性上限读表：表里改成 0.5 后同一击伤害更高（%d > %d）" % [lowered_cap, default_cap])
	# 别的 DoT 不受内伤抗性影响
	var poison_skill: Resource = db.get_row("skill_active", "sk_wudu_02")
	check_eq(
		int(_dot(resolver, attacker, tough, poison_skill, "dot_poison")["damage"]),
		int(_dot(resolver, attacker, tall, poison_skill, "dot_poison")["damage"]),
		"中毒不吃内伤抗性",
	)
	# 直的伤害型招式挂的中毒层，也要按「中毒」的规则算（无视外功防御）
	var fist: Resource = db.get_row("skill_active", "sk_wudu_01")   # 直伤 + 中毒
	var wall = _actor("wall", 10.0, BattleActorScript.SIDE_ENEMY)
	wall.stats["def_phys"] = 900.0
	check_eq(
		int(_dot(resolver, attacker, wall, fist, "dot_poison")["damage"]),
		int(_dot(resolver, attacker, tall, fist, "dot_poison")["damage"]),
		"毒层无视外功防御（哪怕挂毒的那招是直伤）",
	)


## 回合末回复的边界：回满就停（不溢出）、内伤减半后再封顶、倒下的单位不回复（气血与内力都不动）。
## 「内伤内力回复减半」本身在 `_check_burn_and_internal` 里验过，这条补的是**封顶**与**死人**两种边界——
## 溢出（内力超过上限）不会报错，只会在界面上显示成一条满不起来的条，很难被发现。
func _check_regen_caps(db) -> void:
	var actor = _actor("regen", 10.0, BattleActorScript.SIDE_ALLY)
	actor.stats["hp_regen"] = 10.0
	actor.stats["qi_regen"] = 10.0
	# ① 满血满内力：回复不动
	actor.hp = actor.max_hp()
	actor.qi = actor.max_qi()
	actor.end_of_round_regen()
	check_eq(int(actor.hp), int(actor.max_hp()), "满血时回合末回复不溢出")
	check_eq(int(actor.qi), int(actor.max_qi()), "满内力时不溢出")
	# ② 差 2 点：回 10 也只补到上限
	actor.hp = actor.max_hp() - 2
	actor.qi = actor.max_qi() - 2
	actor.end_of_round_regen()
	check_eq(int(actor.hp), int(actor.max_hp()), "气血补到上限就停")
	check_eq(int(actor.qi), int(actor.max_qi()), "内力补到上限就停")
	# ③ 内伤减半之后再封顶：剩 2 点、回复 10 → 减半 5，仍然只补到上限
	actor.add_status("internal", "foe", 4.0, 3, 3, "rule_internal_qi", true)
	check_float(actor.qi_regen_factor(), 0.5, "内伤下内力回复减半")
	actor.hp = actor.max_hp() - 2
	actor.qi = actor.max_qi() - 2
	actor.end_of_round_regen()
	check_eq(int(actor.hp), int(actor.max_hp()), "内伤不影响气血回复的口径（这里仍是满）")
	check_eq(int(actor.qi), int(actor.max_qi()), "减半之后也只补到上限")
	check_true(int(actor.qi) <= int(actor.max_qi()), "内力不会超过上限（%d/%d）" % [int(actor.qi), int(actor.max_qi())])
	# ④ 倒下的单位：气血与内力都不回复（回合末结算会跳过死人）
	actor.hp = 0
	actor.qi = 5
	actor.end_of_round_regen()
	check_eq(int(actor.hp), 0, "倒下不回复气血")
	check_eq(int(actor.qi), 5, "倒下也不回复内力")


func _actor(id: String, speed: float, side: int):
	var actor = BattleActorScript.new()
	actor.actor_id = id
	actor.display_name = id
	actor.side = side
	actor.level = 10
	actor.base_accuracy = 1.0
	actor.stats = {"hp_max": 300.0, "atk_phys": 40.0, "atk_qi": 40.0, "def_phys": 0.0, "def_qi": 0.0,
		"speed": speed, "hit_rate": 0.5, "dodge_rate": 0.0, "crit_rate": 0.0, "crit_dmg": 0.0,
		"qi_max": 100.0, "qi_regen": 1.0, "hp_regen": 0.0, "pen_rate": 0.0, "block_rate": 0.0,
		"block_reduction": 0.0, "dmg_reduction": 0.0, "debuff_chance": 0.0, "debuff_power": 0.0}
	actor.poise_max = 100
	actor.refill()
	return actor
