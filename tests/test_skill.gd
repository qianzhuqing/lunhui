## 武学：总表与明细对应、武器类型限制、选招与熟练度接线。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const DamageResolverScript := preload("res://src/core/damage_resolver.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const SEED := 31337


func suite_name() -> String:
	return "武学：总表／明细／选招"


func run() -> void:
	var db = get_db()
	_check_tables(db)
	_check_weapon_gate(db)
	_check_qi_boundary(db)
	_check_enemy_skills(db)
	_check_dot_skills_usable(db)


func _check_tables(db) -> void:
	check_eq(db.rows("skill_base").size(), 59, "第一章 59 部武学")
	check_eq(db.rows("skill_active").size(), 35, "35 部招式")
	check_eq(db.rows("skill_passive").size(), 24, "24 部内功")
	check_eq(db.rows("skill_star_def").size(), 5, "五档星级")
	check_eq(db.rows("growth_const").size(), 16, "16 条成长常数（0.10.0 加了木桩的两条上限）")

	var actives := 0
	var passives := 0
	for row: Resource in db.rows("skill_base"):
		if row.is_active():
			actives += 1
			check_not_null(db.get_row("skill_active", row.skill_id), "%s 有招式明细" % row.skill_id)
		else:
			passives += 1
			check_not_null(db.get_row("skill_passive", row.skill_id), "%s 有内功明细" % row.skill_id)
	check_eq(actives, 35, "skill_kind 与明细表一致：35 部招式")
	check_eq(passives, 24, "skill_kind 与明细表一致：24 部内功")

	# 通用招式（any）与绑定招式都在
	check_true(bool(db.get_row("skill_base", "sk_common_01").accepts_any_weapon()), "连环腿是通用招式")
	check_false(bool(db.get_row("skill_base", "sk_xuanwei_01").accepts_any_weapon()), "玄微剑法绑定武器")
	check_eq(str(db.get_row("skill_base", "sk_xuanwei_01").weapon_type), "sword", "玄微剑法绑剑")
	# 内功占格 1~3
	check_eq(int(db.get_row("skill_passive", "pf_xuanwei_05").slot_cost), 3, "★5 内功占 3 格")
	check_eq(int(db.get_row("skill_passive", "pf_xuanwei_01").slot_cost), 1, "★1 内功占 1 格")


## 招式绑定武器类型：拿刀就不能用剑法，通用招式随时能用。
func _check_weapon_gate(db) -> void:
	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	var actor = _actor()
	actor.skills = PackedStringArray(["sk_xuanwei_01", "sk_chensha_01", "sk_common_01"])
	actor.tags["weapon_type"] = "sword"
	var with_sword: Resource = simulator.pick_skill(actor)
	check_eq(str(with_sword.skill_id), "sk_xuanwei_01", "剑在手时选到剑法")
	check_ne(str(with_sword.skill_id), "sk_chensha_01", "拳法被武器类型挡住")

	actor.tags["weapon_type"] = "fist"
	var with_fist: Resource = simulator.pick_skill(actor)
	check_eq(str(with_fist.skill_id), "sk_chensha_01", "换拳套后选到拳法")

	actor.tags["weapon_type"] = ""
	var unarmed: Resource = simulator.pick_skill(actor)
	check_eq(str(unarmed.skill_id), "sk_common_01", "没武器只能用通用招式")

	# 熟练度更高时，同倍率的招式优先度不变（倍率才是排序依据），但伤害更高
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var target = _actor()
	target.side = BattleActorScript.SIDE_ENEMY
	var plain: Dictionary = resolver.resolve(actor, target, with_sword, {"force_hit": true, "no_variance": true})
	actor.skill_mastery[str(with_sword.skill_id)] = 10
	var trained: Dictionary = resolver.resolve(actor, target, with_sword, {"force_hit": true, "no_variance": true})
	check_gt(float(trained["damage"]), float(plain["damage"]), "练过的招式打得更疼")


## 「内力刚好够一招」：判定写的是 `qi_cost > qi` 才拒，所以**刚好相等必须能用**。
## 有人把它写成 `>=` 就会变成「内力明明够却说不够」，而玩家在耗到见底时最容易正好卡在这个数上。
func _check_qi_boundary(db) -> void:
	var actor = _actor()
	# 拿一部**要耗内力**的招式当样本（sk_xuanwei_01 是起手式，qi_cost=0 验不出边界）
	var skill: Resource = db.get_row("skill_active", "sk_xuanwei_02")
	check_not_null(skill, "玄微剑法·进步有招式明细")
	var cost := int(skill.qi_cost)
	check_gt(float(cost), 0.0, "这一招要耗内力（拿它当边界样本）")

	# 支付本身：刚好够 → true 且归零；差一点 → false 且不扣
	actor.qi = cost
	check_true(actor.spend_qi(cost), "内力刚好够：支付成功")
	check_eq(int(actor.qi), 0, "刚好够时余额归零")
	actor.qi = cost - 1
	check_false(actor.spend_qi(cost), "差一点：支付失败")
	check_eq(int(actor.qi), cost - 1, "支付失败不扣内力")

	# 选招与体检表：刚好够要算「可用」，差一点要写明「内力不足」
	var simulator = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	actor.skills = PackedStringArray(["sk_xuanwei_02"])
	actor.tags["weapon_type"] = "sword"
	actor.qi = cost
	check_true(_available(simulator, actor, "sk_xuanwei_02"), "刚好够内力：这一招在可用列表里")
	actor.qi = cost - 1
	check_false(_available(simulator, actor, "sk_xuanwei_02"), "差一点内力：不在可用列表里")
	var option := _option_of(simulator, actor, "sk_xuanwei_02")
	check_false(bool(option.get("ok", true)), "体检表也标成不可用")
	check_true(str(option.get("reason", "")).contains("内力不足"), "体检表写明内力不足：%s" % str(option.get("reason", "")))


func _available(simulator, actor, skill_id: String) -> bool:
	for active: Resource in simulator.available_skills(actor):
		if str(active.skill_id) == skill_id:
			return true
	return false


func _option_of(simulator, actor, skill_id: String) -> Dictionary:
	for option: Dictionary in simulator.skill_options(actor):
		if str(option.get("skill_id", "")) == skill_id:
			return option
	return {}


## 敌人招式映射：毒手用五毒，醉刀客用自己的路数（不再错用黑风刀法）。
func _check_enemy_skills(db) -> void:
	var factory = EnemyFactoryScript.new(db)
	var poison_hand = factory.create("en_bd_poison_hand", "normal")
	check_not_null(poison_hand, "毒手能造出来")
	if poison_hand != null:
		# 0.11.0：招式改由 `enemy_skill.csv` 配（设计 10 §一：毒手＝上毒 + 毒发，主动施加异常）
		check_true(
			poison_hand.skills.has("sk_wudu_02") and poison_hand.skills.has("sk_wudu_03"),
			"毒手带上毒＋毒发两招（enemy_skill.csv 的 sk_wudu_02／03）：%s" % str(poison_hand.skills),
		)
		check_eq(poison_hand.skills.size(), 2, "毒手两招（杂兵 2 招是设计 10 的配表原则）")
	var drunk = factory.create("en_hidden_drunk", "normal")
	check_not_null(drunk, "醉刀客能造出来")
	if drunk != null:
		check_true(drunk.skills.has("sk_drunk_jiuzhongdao"), "醉刀客用醉里乾坤")
		check_false(drunk.skills.has("sk_boss_zhangfeng"), "醉刀客不再错用大寨主的招式")
	var boss = factory.create("en_bd_boss", "normal")
	check_not_null(boss, "大寨主能造出来")
	if boss != null:
		check_true(boss.skills.has("sk_boss_zhangfeng"), "大寨主用黑风刀法")
	# 所有敌人的招式都必须在 skill_active 里有明细，否则选招会空转
	for enemy_id in ["en_wolf", "en_boar", "en_bd_thug", "en_bd_scout", "en_bd_archer", "en_bd_lone_wolf",
			"en_bd_patrol_leader", "en_bd_elite_blade", "en_bd_poison_hand", "en_bd_butcher", "en_bd_boss"]:
		var actor = factory.create(enemy_id, "normal")
		if actor == null:
			continue
		for skill_id: String in actor.skills:
			check_not_null(db.get_row("skill_active", skill_id), "%s 的招式 %s 有明细" % [enemy_id, skill_id])


## DoT 类招式（`damage_type = dot_*`）必须是**能点出来**的招式。
##
## 由来（2026-10-03）：`BattleSimulator._is_supported_damage()` 以前把 `category=dot` 一律当「没接结算」
## 挡掉，于是**五毒掌（瘴／蚀骨／化血）与烈火掌（引火／燎原／焚心）这 6 招永远没有按钮**，
## 自动战斗也不选——而 DoT 的结算其实早就完整（`resolve()` 的 dot 分支负责命中与挂层，
## `resolve_dot()` 快照每层伤害，回合末按 `status_effect` 跳字，`tests/test_status.gd` 一直在验）。
## **为什么以前没人发现**：那些用例是直接 `sim.act()` 放招的，**绕过了 `available_skills` 这道过滤**。
func _check_dot_skills_usable(db) -> void:
	var dot_skills := PackedStringArray([
		"sk_wudu_02", "sk_wudu_03", "sk_wudu_04",   # 五毒掌：中毒
		"sk_liehuo_01", "sk_liehuo_02", "sk_liehuo_03",   # 烈火掌：灼伤
	])
	for skill_id: String in dot_skills:
		var row: Resource = db.get_row("skill_active", skill_id)
		check_not_null(row, "表里有 %s" % skill_id)
		if row != null:
			check_true(
				BattleSimulatorScript.new(db, RngServiceScript.new(SEED))._is_supported_damage(str(row.damage_type)),
				"%s 的伤害类型（%s）算「支持」" % [skill_id, str(row.damage_type)],
			)
	var actor = _actor()          # 不带 weapon_type 标记 → 不受武器类型限制（这几招是拳法）
	actor.skills = dot_skills
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([actor], [], {})
	var usable := PackedStringArray()
	for option: Dictionary in sim.skill_options(actor):
		if bool(option["ok"]):
			usable.append(str(option["skill_id"]))
	check_eq(usable.size(), dot_skills.size(), "六招 DoT 全部可点（%s）" % ", ".join(usable))
	for skill_id: String in dot_skills:
		check_true(usable.has(skill_id), "%s 在可点列表里" % skill_id)


func _actor():
	var actor = BattleActorScript.new()
	actor.actor_id = "test_skill_actor"
	actor.display_name = "测试角色"
	actor.side = BattleActorScript.SIDE_ALLY
	actor.level = 10
	actor.stats = {
		"hp_max": 500.0, "qi_max": 100.0,
		"atk_phys": 100.0, "atk_qi": 50.0,
		"def_phys": 20.0, "def_qi": 20.0,
		"speed": 20.0, "hit_rate": 0.9, "dodge_rate": 0.1,
		"crit_rate": 0.0, "crit_dmg": 0.0,
		"pen_rate": 0.0, "block_rate": 0.0, "block_reduction": 0.0, "dmg_reduction": 0.0,
	}
	actor.base_accuracy = 0.0
	actor.refill()
	return actor
