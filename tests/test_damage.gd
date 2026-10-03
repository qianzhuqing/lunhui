## 伤害结算管线：顺序、系数、边界。
extends "res://tests/test_case.gd"

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const DamageResolverScript := preload("res://src/core/damage_resolver.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const SEED := 20261002


func suite_name() -> String:
	return "伤害结算管线"


func run() -> void:
	var db = get_db()
	# 绝对值断言：变异探针点名「没人钉」——crit_dmg 的兜底值（combat_const 缺行时才用）
	check_float(DamageResolverScript.DEFAULT_CRIT_DMG, 0.5, "暴击伤害兜底值 0.5（决策 155 的口径）", 0.0001)
	_check_golden_pipeline(db)
	_check_element_counter(db)
	_check_crit_before_defense(db)
	_check_mastery(db)
	_check_penetration(db)
	_check_block(db)
	_check_damage_reduction(db)
	_check_modifiers(db)
	_check_variance(db)
	_check_hit_and_dodge(db)
	_check_unsupported_types(db)
	_check_defense_curve(db)


## 固定输入 + 关闭浮动 → 可手算的黄金数值。
func _check_golden_pipeline(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var attacker = _attacker()
	var defender = _defender()
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	var result: Dictionary = resolver.resolve(attacker, defender, skill, {"force_hit": true, "no_variance": true})
	check_true(result["ok"], "外功直伤应结算成功")
	check_true(result["is_hit"], "force_hit 时必定命中")
	# 0.6.0 起传 skill_active 行：D = 100 × 1.6 × 1.0（熟练度 0）= 160
	check_float(result["detail"]["step_d"], 160.0, "第 1 步：攻击力 × 倍率 × 熟练度系数")
	check_float(result["detail"]["mastery_multiplier"], 1.0, "没练过时熟练度系数是 1.0")
	check_float(result["detail"]["defense_reduction"], 0.1875, "防御减免公式")
	check_float(result["detail"]["step_r"], 130.0, "第 5 步：防御减免后数值")
	check_float(result["detail"]["pen_rate"], 0.0, "没有穿透时穿透率为 0")
	check_float(result["detail"]["effective_defense"], 60.0, "没有穿透时有效防御等于原始防御")
	check_false(result["is_blocked"], "没有格挡时不触发格挡")
	check_eq(int(result["raw_damage"]), 130, "第 9 步：floor 取整")
	check_eq(int(result["damage"]), 130, "最终伤害")


func _check_element_counter(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var attacker = _attacker()
	var defender = _defender()
	var mods := {"force_hit": true, "no_variance": true}

	var internal_skill = db.get_row("skill_active", "sk_xuanwei_qi_01")
	var internal_result: Dictionary = resolver.resolve(attacker, defender, internal_skill, mods)
	check_float(internal_result["detail"]["element_multiplier"], 1.3, "内功克外功 +30%")
	check_eq(int(internal_result["damage"]), 79, "内功黄金数值")

	var odd_skill = db.get_row("skill_active", "sk_wudu_01")
	var odd_result: Dictionary = resolver.resolve(attacker, defender, odd_skill, mods)
	check_float(odd_result["detail"]["element_multiplier"], 0.8, "奇诡被外功克 −20%")
	check_eq(int(odd_result["damage"]), 39, "奇诡黄金数值（力智均值）")

	defender.tags["element"] = "internal"
	var countered: Dictionary = resolver.resolve(attacker, defender, db.get_row("skill_active", "sk_xuanwei_03"), mods)
	check_float(countered["detail"]["element_multiplier"], 0.8, "外功打内功被克")
	check_eq(int(countered["damage"]), 104, "被克时的黄金数值")


func _check_crit_before_defense(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var crit_skill = db.get_row("skill_active", "sk_xuanwei_03").duplicate(true)
	crit_skill.damage_type = "dmg_crit"
	var result: Dictionary = resolver.resolve(
		_attacker(), _defender(), crit_skill,
		{"force_hit": true, "force_crit": true, "no_variance": true}
	)
	check_true(result["is_crit"], "force_crit 应判定为暴击")
	# 0.4.0 起暴击倍率改为 crit_base_mult(1.5) + 暴击伤害(0.5) = 2.0
	check_float(result["detail"]["crit_multiplier"], 2.0, "暴击倍率 = crit_base_mult + 暴击伤害")
	# (160 × 2.0) × 0.8125 = 260
	check_eq(int(result["damage"]), 260, "九步管线下的暴击黄金数值")

	var non_crit: Dictionary = resolver.resolve(
		_attacker(), _defender(), db.get_row("skill_active", "sk_xuanwei_03"),
		{"force_hit": true, "no_variance": true}
	)
	check_false(non_crit["is_crit"], "dmg_normal 不参与暴击判定")


## 熟练度：招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)。
func _check_mastery(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	var mods := {"force_hit": true, "no_variance": true}

	# 玄微剑法·穿云式是 ★2，mastery_gain 0.045；练满 10 级 → ×1.45
	var veteran = _attacker()
	veteran.skill_mastery["sk_xuanwei_03"] = 10
	var result: Dictionary = resolver.resolve(veteran, _defender(), skill, mods)
	check_eq(int(result["detail"]["star"]), 2, "星级取自 skill_base")
	check_eq(int(result["detail"]["mastery"]), 10, "熟练度取自战斗单位")
	check_float(result["detail"]["mastery_multiplier"], 1.45, "★2 练满 = 1 + 10 × 0.045")
	check_float(result["detail"]["step_d"], 232.0, "第 1 步乘上熟练度系数（160 × 1.45）")
	check_eq(int(result["damage"]), 188, "练满后的黄金数值")

	# ★1 成长更快、★5 更慢：同一熟练度下低星倍率更高
	var rookie_skill = db.get_row("skill_active", "sk_common_01")   # ★1
	var master_skill = db.get_row("skill_active", "sk_drunk_01")    # ★5
	veteran.skill_mastery["sk_common_01"] = 10
	veteran.skill_mastery["sk_drunk_01"] = 10
	var rookie: Dictionary = resolver.resolve(veteran, _defender(), rookie_skill, mods)
	var master: Dictionary = resolver.resolve(veteran, _defender(), master_skill, mods)
	check_float(rookie["detail"]["mastery_multiplier"], 1.5, "★1 练满 = 1.50")
	check_float(master["detail"]["mastery_multiplier"], 1.25, "★5 练满 = 1.25")


## 穿透：压的是「有效防御」，不是减免结果——这两个写法算出来的数不一样。
func _check_penetration(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	var mods := {"force_hit": true, "no_variance": true}

	var piercing = _attacker()
	piercing.stats["pen_rate"] = 0.5
	var result: Dictionary = resolver.resolve(piercing, _defender(), skill, mods)
	check_float(result["detail"]["pen_rate"], 0.5, "穿透率取自攻击者属性")
	check_float(result["detail"]["effective_defense"], 30.0, "有效防御 = 60 × (1 − 0.5)")
	check_float(result["detail"]["defense_reduction"], 30.0 / 290.0, "减免按有效防御计算")
	check_eq(int(result["damage"]), 143, "穿透后的黄金数值")
	# 对照：若把穿透错用在减免结果上，会得到 160 × (1 − 0.1875×0.5) = 145
	var wrong_shape := 160.0 * (1.0 - 0.1875 * 0.5)
	check_ne(int(floor(wrong_shape)), int(result["damage"]), "穿透必须作用在防御上，而非减免结果上")

	# 穿透上限取 stat_def 的 cap 0.75
	var over = _attacker()
	over.stats["pen_rate"] = 0.95
	var capped: Dictionary = resolver.resolve(over, _defender(), skill, mods)
	check_float(capped["detail"]["pen_rate"], 0.75, "穿透率夹在 0.75 以内")
	check_eq(int(capped["damage"]), 151, "穿透上限下的黄金数值")

	# 穿透对高防御目标收益更大：同一点穿透，防御越高省得越多
	var low_def = _defender()
	low_def.stats["def_phys"] = 20.0
	var high_def = _defender()
	high_def.stats["def_phys"] = 120.0
	var low_gain := _step_r(resolver, piercing, low_def, skill, mods) - _step_r(resolver, _attacker(), low_def, skill, mods)
	var high_gain := _step_r(resolver, piercing, high_def, skill, mods) - _step_r(resolver, _attacker(), high_def, skill, mods)
	check_gt(high_gain, low_gain, "同样的穿透，对高防御目标的绝对收益更大")


## 格挡：防御之后的独立乘区；闪避与格挡互斥（先闪避，未闪避才判格挡）。
func _check_block(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var skill = db.get_row("skill_active", "sk_xuanwei_03")

	var blocker = _defender()
	blocker.stats["block_rate"] = 0.5
	blocker.stats["block_reduction"] = 0.3
	var result: Dictionary = resolver.resolve(
		_attacker(), blocker, skill,
		{"force_hit": true, "force_block": true, "no_variance": true}
	)
	check_true(result["is_blocked"], "force_block 应触发格挡")
	check_float(result["detail"]["step_r"], 130.0, "先算防御减免（格挡在防御之后）")
	check_float(result["detail"]["step_b"], 91.0, "再乘格挡减伤：130 × (1 − 0.3)")
	check_eq(int(result["damage"]), 91, "格挡后的黄金数值")

	var capped_blocker = _defender()
	capped_blocker.stats["block_rate"] = 0.5
	capped_blocker.stats["block_reduction"] = 0.95
	var capped: Dictionary = resolver.resolve(
		_attacker(), capped_blocker, skill,
		{"force_hit": true, "force_block": true, "no_variance": true}
	)
	check_true(capped["is_blocked"], "force_block 必定触发格挡")
	check_float(capped["detail"]["block_reduction"], 0.8, "格挡减伤率夹在 stat_def 的 0.8 以内")
	check_float(capped["detail"]["step_b"], 26.0, "格挡上限后的中间值")
	# 130 × (1 − 0.8) 在浮点里是 25.999…，floor 后是 25——顺手钉住这个边界
	check_eq(int(capped["damage"]), 25, "格挡上限下的黄金数值（浮点边界取 floor）")

	# 格挡率本身有上限（stat_def 0.75）：写 1.0 也只会按 0.75 掷
	var over_cap = _defender()
	over_cap.stats["block_rate"] = 1.0
	var blocked_count := 0
	for index in range(400):
		if resolver.resolve(_attacker(), over_cap, skill, {"force_hit": true, "no_variance": true})["is_blocked"]:
			blocked_count += 1
	check_in_range(float(blocked_count), 240.0, 360.0, "格挡率被夹到 0.75 后大致四分之三概率格挡")

	# 闪避与格挡互斥：闪避成功时直接免伤，不再判格挡
	var blind = _attacker()
	blind.stats["hit_rate"] = 0.0
	var evasive = _defender()
	evasive.stats["dodge_rate"] = 0.9
	evasive.stats["block_rate"] = 1.0
	var dodges := 0
	for index in range(200):
		var miss_result: Dictionary = resolver.resolve(blind, evasive, skill, {"no_variance": true})
		if not miss_result["is_hit"]:
			dodges += 1
			check_false(miss_result["is_blocked"], "被闪避时不判格挡")
			check_false(bool(miss_result["detail"].get("blocked", false)), "被闪避时不记录格挡")
	check_gt(float(dodges), 0.0, "低命中下应出现闪避")


## 减伤：终局乘区，装备属性 + 临时效果叠加，并夹到上限。
func _check_damage_reduction(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	var mods := {"force_hit": true, "no_variance": true}

	var armored = _defender()
	armored.stats["dmg_reduction"] = 0.25
	var result: Dictionary = resolver.resolve(_attacker(), armored, skill, mods)
	check_float(result["detail"]["dmg_reduction"], 0.25, "减伤取自防守方属性")
	check_float(result["detail"]["step_m"], 97.5, "130 × (1 − 0.25)")
	check_eq(int(result["damage"]), 97, "减伤后的黄金数值")

	var stacked: Dictionary = resolver.resolve(
		_attacker(), armored, skill,
		{"force_hit": true, "no_variance": true, "dmg_down": 0.1}
	)
	check_float(stacked["detail"]["dmg_reduction"], 0.35, "临时减伤叠加在装备减伤之上")
	check_eq(int(stacked["damage"]), 84, "叠加后的黄金数值")

	var capped_defender = _defender()
	capped_defender.stats["dmg_reduction"] = 0.9
	var capped: Dictionary = resolver.resolve(_attacker(), capped_defender, skill, mods)
	check_float(capped["detail"]["dmg_reduction"], 0.6, "减伤率夹在 stat_def 的 0.6 以内")
	check_eq(int(capped["damage"]), 52, "减伤上限下的黄金数值")

	var boosted: Dictionary = resolver.resolve(
		_attacker(), _defender(), skill,
		{"force_hit": true, "no_variance": true, "dmg_up": 0.2}
	)
	check_eq(int(boosted["damage"]), 156, "第 7 步的增伤")


## 取第 5 步的中间值，用来比较穿透收益（避免取整带来的噪声）。
func _step_r(resolver, attacker, defender, skill, modifiers: Dictionary) -> float:
	return float(resolver.resolve(attacker, defender, skill, modifiers)["detail"]["step_r"])


func _check_modifiers(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	var boosted: Dictionary = resolver.resolve(
		_attacker(), _defender(), skill,
		{"force_hit": true, "no_variance": true, "attack_stat_bonus": 0.1}
	)
	check_float(boosted["detail"]["step_a"], 176.0, "第 2 步：攻击属性系数 +10%")
	check_eq(int(boosted["damage"]), 143, "攻击属性系数影响黄金数值")

	var mitigated: Dictionary = resolver.resolve(
		_attacker(), _defender(), skill,
		{"force_hit": true, "no_variance": true, "dmg_up": 0.2, "dmg_down": 0.1}
	)
	check_eq(int(mitigated["damage"]), 140, "第 7 步：增伤与临时减伤相乘")


func _check_variance(db) -> void:
	var rng = RngServiceScript.new(SEED)
	var resolver = DamageResolverScript.new(db, rng)
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	# 无浮动基准值 130 → 浮动区间 [123.5, 136.5]
	for index in range(400):
		var result: Dictionary = resolver.resolve(_attacker(), _defender(), skill, {"force_hit": true})
		var variance := float(result["detail"]["variance"])
		check_true(variance >= 0.95 and variance <= 1.05, "浮动系数落在 0.95~1.05（第 %d 次：%f）" % [index, variance])
		if index < 40:
			check_in_range(float(result["raw_damage"]), 123.0, 137.0, "浮动后的伤害落在理论区间")


func _check_hit_and_dodge(db) -> void:
	var rng = RngServiceScript.new(SEED)
	var resolver = DamageResolverScript.new(db, rng)
	var skill = db.get_row("skill_active", "sk_xuanwei_03")
	check_float(resolver.hit_chance(_attacker(), _defender()), 0.7, "命中率 = 命中 − 闪避")

	var blind = _attacker()
	blind.stats["hit_rate"] = 0.0
	var dodgy = _defender()
	dodgy.stats["dodge_rate"] = 0.9
	check_float(resolver.hit_chance(blind, dodgy), 0.05, "命中率下限 5%")

	var sharp = _attacker()
	sharp.stats["hit_rate"] = 1.0
	var slow = _defender()
	slow.stats["dodge_rate"] = 0.0
	check_float(resolver.hit_chance(sharp, slow), 0.99, "命中率上限 99%")

	# 玩家角色走「基准 1.0 + 命中 − 闪避」：敏给的是加成，不是绝对命中
	var character_like = _attacker()
	character_like.base_accuracy = 1.0
	character_like.stats["hit_rate"] = 0.05
	var wolf_like = _defender()
	wolf_like.stats["dodge_rate"] = 0.10
	check_float(resolver.hit_chance(character_like, wolf_like), 0.95, "玩家命中基准 1.0，敏的命中是加成")

	var hits := 0
	for index in range(400):
		var result: Dictionary = resolver.resolve(blind, dodgy, skill, {"no_variance": true})
		if result["is_hit"]:
			hits += 1
		else:
			check_eq(int(result["damage"]), 0, "未命中时伤害为 0")
	check_in_range(float(hits), 1.0, 60.0, "5% 命中率下 400 次应只有少量命中")


func _check_unsupported_types(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	var dot_skill = db.get_row("skill_active", "sk_wudu_02")
	var dot_result: Dictionary = resolver.resolve(_attacker(), _defender(), dot_skill, {})
	# 持续伤害不再被拒：这一下没有直伤，只负责命中判定 + 挂层（每层伤害走 resolve_dot 的快照）
	check_true(bool(dot_result["ok"]), "持续伤害类型能走通管线")
	check_eq(int(dot_result["damage"]), 0, "持续伤害招式本身不结算直伤")
	check_true(bool(dot_result["detail"].get("dot", false)), "detail 标记这是持续伤害")
	var dot_snapshot: Dictionary = resolver.resolve_dot(_attacker(), _defender(), dot_skill, {})
	check_true(bool(dot_snapshot["ok"]), "每层快照伤害算得出来")
	check_gt(float(dot_snapshot["damage"]), 0.0, "快照伤害大于 0：%d" % int(dot_snapshot["damage"]))

	var reflect_skill = db.get_row("skill_active", "sk_xuanwei_03").duplicate(true)
	reflect_skill.damage_type = "dmg_reflect"
	var reflect_result: Dictionary = resolver.resolve(_attacker(), _defender(), reflect_skill, {})
	check_false(reflect_result["ok"], "反伤类型应显式拒绝")


func _check_defense_curve(db) -> void:
	var resolver = DamageResolverScript.new(db, RngServiceScript.new(SEED))
	check_eq(resolver.defense_reduction(0.0, 10), 0.0, "没有防御就没有减免")
	check_lt(resolver.defense_reduction(10.0, 10), resolver.defense_reduction(100.0, 10), "防御越高减免越高")
	check_lt(resolver.defense_reduction(100000.0, 10), 1.0, "减免恒小于 100%")
	check_gt(resolver.defense_reduction(100.0, 1), resolver.defense_reduction(100.0, 20), "攻击者等级越高，分母越大、减免越低")


func _attacker():
	var actor = BattleActorScript.new()
	actor.actor_id = "test_attacker"
	actor.display_name = "测试攻击者"
	actor.side = BattleActorScript.SIDE_ALLY
	actor.level = 10
	# 手搓单位走「绝对命中率」语义：命中 − 闪避 就是命中率
	actor.base_accuracy = 0.0
	actor.stats = {
		"hp_max": 500.0, "qi_max": 100.0,
		"atk_phys": 100.0, "atk_qi": 50.0,
		"def_phys": 20.0, "def_qi": 20.0,
		"speed": 20.0, "hit_rate": 0.9, "dodge_rate": 0.1,
		"crit_rate": 0.5, "crit_dmg": 0.5,
		"pen_rate": 0.0, "block_rate": 0.0, "block_reduction": 0.0, "dmg_reduction": 0.0,
	}
	actor.refill()
	return actor


func _defender():
	var actor = BattleActorScript.new()
	actor.actor_id = "test_defender"
	actor.display_name = "测试防守者"
	actor.side = BattleActorScript.SIDE_ENEMY
	actor.level = 5
	actor.base_accuracy = 0.0
	actor.stats = {
		"hp_max": 300.0, "qi_max": 0.0,
		"atk_phys": 40.0, "atk_qi": 0.0,
		"def_phys": 60.0, "def_qi": 30.0,
		"speed": 10.0, "hit_rate": 0.8, "dodge_rate": 0.2,
		"crit_rate": 0.0, "crit_dmg": 0.0,
		"pen_rate": 0.0, "block_rate": 0.0, "block_reduction": 0.0, "dmg_reduction": 0.0,
	}
	actor.refill()
	return actor
