## 伤害结算管线。
##
## 严格按 docs/design/04_战斗与伤害.md 的九步走，顺序不可改：
##   0. 闪避判定：未命中 → 伤害为 0，后续全部跳过
##   1. D = 技能基础值 × 技能倍率    「技能基础值」= 攻击力（按系别取力/智/均值）
##   2. A = D × (1 + 攻击属性系数)    系数来自 buff 与被动
##   3. C = 直伤且暴击 ? A × (crit_base_mult + 暴击伤害) : A
##   4. K = C × 系别相克系数
##   5. 有效防御 = DEF × (1 - 穿透率)
##      防御减免 = 有效防御 / (有效防御 + def_const + def_level_coeff × 攻击者等级)
##      R = K × (1 - 防御减免)
##   6. B = 格挡判定成功 ? R × (1 - 格挡减伤率) : R
##   7. M = B × (1 + 增伤总和) × (1 - 减伤率)
##   8. F = M × 随机浮动(variance_min ~ variance_max)
##   9. 最终伤害 = floor(F)
##
## 位置纪律：暴击与相克在防御之前、穿透在防御之前、格挡在防御之后、减伤在最后。
## 闪避与格挡互斥：先判闪避（完全免伤），未闪避才判格挡（部分免伤）。
## 全局常数（crit_base_mult / def_const / def_level_coeff / variance_*）来自 combat_const.csv，
## 公式仍在代码里——参数在表、算法在代码。
## 本里程碑只做直伤：dot_* 与 dmg_reflect 会被显式拒绝，不做持续结算（见 docs/dev/框架说明.md）。
class_name DamageResolver
extends RefCounted

## combat_const 缺行时的兜底默认值（正常运行时这些值都在表里）。
const RNG_MIN_VARIANCE_DEFAULT := 0.95
const RNG_MAX_VARIANCE_DEFAULT := 1.05
const DEF_BASE_DEFAULT := 60.0
const DEF_LEVEL_FACTOR_DEFAULT := 20.0
const CRIT_BASE_MULT_DEFAULT := 1.5
## 比率上限的兜底；正常运行时取自 stat_def.max_value。
## 注意：这四个值**必须**在 `stat_def` 里写上上限——表里留空（= 0 = 无上限）时会静默用这里的兜底，
## 表与实际行为就分家了。`table_validator.REQUIRED_CAPPED_STATS` 每次都盯着这件事（决策 155）。
const PEN_RATE_CAP_DEFAULT := 0.75
const BLOCK_RATE_CAP_DEFAULT := 0.75
const BLOCK_REDUCTION_CAP_DEFAULT := 0.8
const DMG_REDUCTION_CAP_DEFAULT := 0.6
## 内伤抗性的上限兜底（同样是 stat_def.max_value 说了算；2026-10-03 从硬编码 0.75 改成读表，
## 见框架说明决策 180——它是管线里最后一个"表里有数、代码却写死"的上限）。
const RES_INTERNAL_CAP_DEFAULT := 0.75
const HIT_CHANCE_MIN := 0.05
const HIT_CHANCE_MAX := 0.99
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
## enemy_base 没有暴击伤害列，敌人先统一用这个加成（0.5 = 暴击 +50%）。
const DEFAULT_CRIT_DMG := 0.5

const DEFAULT_ELEMENT := "external"
const ELEMENTS := ["external", "internal", "odd"]

var _db
var _rng
## "攻系|守系" → 倍率
var _element_table: Dictionary = {}
## combat_const.const_id → value
var _consts: Dictionary = {}
var _growth


func _init(db, rng) -> void:
	_db = db
	_rng = rng
	_build_element_table()
	_build_constants()
	_growth = GrowthCalculatorScript.new(db)


func _build_element_table() -> void:
	_element_table.clear()
	for row: Resource in _db.rows("element_counter"):
		_element_table["%s|%s" % [row.element_atk, row.element_def]] = float(row.multiplier)


func _build_constants() -> void:
	_consts.clear()
	for row: Resource in _db.rows("combat_const"):
		_consts[str(row.const_id)] = float(row.value)


## 取全局常数；表里缺行就报错并退回默认值。
func constant(const_id: String, fallback: float) -> float:
	if _consts.has(const_id):
		return float(_consts[const_id])
	push_error("[DamageResolver] combat_const 缺少 %s，退回默认值 %s" % [const_id, fallback])
	return fallback


func crit_base_mult() -> float:
	return constant("crit_base_mult", CRIT_BASE_MULT_DEFAULT)


func def_const() -> float:
	return constant("def_const", DEF_BASE_DEFAULT)


func def_level_coeff() -> float:
	return constant("def_level_coeff", DEF_LEVEL_FACTOR_DEFAULT)


func variance_min() -> float:
	return constant("variance_min", RNG_MIN_VARIANCE_DEFAULT)


func variance_max() -> float:
	return constant("variance_max", RNG_MAX_VARIANCE_DEFAULT)


## 比率上限从 stat_def.max_value 读，表里没配才用兜底值。
func _stat_cap(stat_id: String, fallback: float) -> float:
	var row: Resource = _db.get_row("stat_def", stat_id)
	if row != null and row.has_max():
		return float(row.max_value)
	return fallback


func element_multiplier(atk_element: String, def_element: String) -> float:
	return float(_element_table.get("%s|%s" % [atk_element, def_element], 1.0))


## 按系别取攻击力：外功取力、内功取智、奇诡取两者均值。
## 每层持续伤害（快照）：同一套 1~5 步骨架，但**不吃暴击与格挡**；
## 闪避按 damage_type.can_dodge（由调用方判定），防御按 `damage_type.use_def_*` 逐类型决定——
## 按当前数据：**中毒与灼伤无视内外防御、流血吃外功防御、内伤吃内功防御**。
## （注：04_战斗与伤害.md 的伤害类型表把内伤写成「吃内防 否」，与数据/用例不一致，
##  已记当前状态缺口 #21 请设计确认；校验器 5.26 每次验收都会把这条点出来。）
## 强度乘 (1 + 异常强度)，最后照吃增伤/减伤与浮动——算完就锁进层里，之后不再重算。
## `dot_type_id` 传状态自己的伤害类型（`status_effect.damage_type`）：
## 中毒/灼伤/内伤该不该吃防御由**状态**决定，不是由挂它的那招决定
## （例：五毒散手是直伤招式，但它挂的中毒层照样「无视外功防御」）。
func resolve_dot(
	attacker, defender, skill_row: Resource, modifiers: Dictionary = {}, dot_type_id: String = ""
) -> Dictionary:
	var type_id := dot_type_id if not dot_type_id.is_empty() else str(skill_row.damage_type)
	var damage_type_row: Resource = _db.get_row("damage_type", type_id)
	if damage_type_row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[DamageResolver] damage_type 不存在：%s" % type_id)
		return {"ok": false, "damage": 0, "detail": {}, "error": "这一招的伤害类型没配（数据错，已记进日志）"}
	var skill_id := str(skill_row.skill_id)
	var skill_base: Resource = _db.get_row("skill_base", skill_id)
	var star := int(skill_base.star) if skill_base != null else 1
	var mastery := int(attacker.mastery_of(skill_id)) if attacker.has_method("mastery_of") else 0
	var element: String = str(modifiers.get("element_override", skill_row.element))
	if element.is_empty():
		element = DEFAULT_ELEMENT
	# 1~4 步：攻击力 × 倍率 × 熟练度系数 × 攻击属性系数 × 系别相克
	var step: float = attack_value(attacker, element) * float(skill_row.power_ratio) * _growth.mastery_multiplier(star, mastery)
	step *= 1.0 + float(modifiers.get("attack_stat_bonus", 0.0))
	var counter := element_multiplier(element, str(defender.tags.get("element", DEFAULT_ELEMENT)))
	step *= counter
	# 5 步：穿透压低有效防御，再按 damage_type 决定吃不吃防御
	var pen_rate := clampf(_stat(attacker, "pen_rate"), 0.0, _stat_cap("pen_rate", PEN_RATE_CAP_DEFAULT))
	var raw_defense := defense_value(defender, damage_type_row)
	var reduction := defense_reduction(raw_defense * (1.0 - pen_rate), attacker.level)
	step *= 1.0 - reduction
	# 异常强度：智换算出来的 debuff_power
	step *= 1.0 + _stat(attacker, "debuff_power")
	# 内伤抗性（stat_def.res_internal，01 文档写「抵抗内伤类效果」）：
	# 只作用在内伤类持续伤害上；04 没写它落在第几步，这里按「伤害减免乘区」处理
	if type_id == "dot_internal":
		step *= 1.0 - clampf(_stat(defender, "res_internal"), 0.0, _stat_cap("res_internal", RES_INTERNAL_CAP_DEFAULT))
	# 7 步：增伤 / 减伤（DoT 只免暴击与格挡，减伤照吃）
	step *= 1.0 + float(modifiers.get("dmg_up", 0.0))
	var total_reduction := clampf(
		_stat(defender, "dmg_reduction") + float(modifiers.get("dmg_down", 0.0)),
		0.0,
		_stat_cap("dmg_reduction", DMG_REDUCTION_CAP_DEFAULT)
	)
	step *= 1.0 - total_reduction
	# 8 步：随机浮动（在施加那一刻掷一次，之后锁死）
	if not bool(modifiers.get("no_variance", false)):
		step *= _rng.randf_range(variance_min(), variance_max())
	return {
		"ok": true,
		"damage": maxi(1, int(floor(step))),
		"element": element,
		"counter": counter,
		"reduction": reduction,
		"detail": {"raw_defense": raw_defense, "effective_defense": raw_defense * (1.0 - pen_rate)},
	}


func attack_value(actor, element: String) -> float:
	match element:
		"internal":
			return _stat(actor, "atk_qi")
		"odd":
			return (_stat(actor, "atk_phys") + _stat(actor, "atk_qi")) * 0.5
		_:
			return _stat(actor, "atk_phys")


## 按伤害类型决定吃哪些防御。
func defense_value(actor, damage_type_row: Resource) -> float:
	var total := 0.0
	if damage_type_row.use_def_phys:
		total += _stat(actor, "def_phys")
	if damage_type_row.use_def_qi:
		total += _stat(actor, "def_qi")
	# 灼伤每层削 5% 外功防御（status_effect.extra_rule = rule_burn_def_down）
	if actor.has_method("defense_multiplier"):
		total *= float(actor.defense_multiplier("def_phys")) if damage_type_row.use_def_phys else 1.0
	return total


## 攻击者的某个派生数值（战斗模拟器算异常触发率时要用同一份快照）
func stat_of(actor, stat_id: String, fallback: float = 0.0) -> float:
	return _stat(actor, stat_id, fallback)


## 防御减免公式：防滚雪球的关键，分母随攻击者等级成长。
func defense_reduction(defense: float, attacker_level: int) -> float:
	if defense <= 0.0:
		return 0.0
	return defense / (defense + def_const() + def_level_coeff() * float(maxi(attacker_level, 1)))


## 命中率 = 攻击者命中基准 + 命中 - 防守者闪避，夹在 [5%, 99%]。
##
## **敌我命中基准现在都是 1.0**（设计 10 §七第四条，0.28.0 答 Q57）：`hit_rate` 一律是
## 敏经 diminishing 曲线给的**加成**，真正决定打不打得中的是对方的 `dodge_rate`。
## 手搓的战斗单位（用例夹具）仍可以传 0.0 走「命中 − 闪避＝绝对命中率」的老语义，
## 细节见 `battle_actor.base_accuracy`。
func hit_chance(attacker, defender) -> float:
	return clampf(
		_accuracy_base(attacker) + _stat(attacker, "hit_rate") - _stat(defender, "dodge_rate"),
		HIT_CHANCE_MIN,
		HIT_CHANCE_MAX
	)


func _accuracy_base(actor) -> float:
	if actor.get("base_accuracy") != null:
		return float(actor.base_accuracy)
	return 1.0


## 结算一次伤害。
##
## 0.6.0 起 `skill_row` 传的是 **skill_active 行**（招式数值），身份与星级去 skill_base 查；
## 第 1 步会乘上熟练度系数（`1 + 熟练度 × mastery_gain`）。
##
## modifiers 支持：
##   attack_stat_bonus  float  第 2 步的攻击属性系数
##   dmg_up             float  第 7 步的增伤
##   dmg_down           float  第 7 步的临时减伤（叠加在防守方 dmg_reduction 之上）
##   element_override   String 覆盖招式系别
##   force_hit / force_crit / force_block / no_block / no_variance  bool  测试与固定演出用
func resolve(attacker, defender, skill_row: Resource, modifiers: Dictionary = {}) -> Dictionary:
	var type_id: String = str(skill_row.damage_type)
	var damage_type_row: Resource = _db.get_row("damage_type", type_id)
	if damage_type_row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[DamageResolver] 招式 %s 的 damage_type '%s' 不存在" % [skill_row.skill_id, type_id])
		return _fail("这一招的伤害类型没配（数据错，已记进日志）")
	if damage_type_row.is_dot():
		# 持续伤害不算直伤：这一下只负责「命中判定 + 把层数挂上去」，
		# 每层伤害由 resolve_dot() 在施加那一刻算好并锁死（快照制）。
		var dot_element: String = str(modifiers.get("element_override", skill_row.element))
		if dot_element.is_empty():
			dot_element = DEFAULT_ELEMENT
		var dot_hit := true
		var dot_chance := hit_chance(attacker, defender)
		if damage_type_row.can_dodge and not bool(modifiers.get("force_hit", false)):
			dot_hit = _rng.chance(dot_chance)
		return {
			"ok": true,
			"is_hit": dot_hit,
			"is_crit": false,
			"is_blocked": false,
			"damage": 0,
			"raw_damage": 0,
			"detail": {
				"hit_chance": dot_chance, "element": dot_element, "blocked": false, "dot": true,
			},
		}
	if type_id == "dmg_reflect":
		return _fail("反伤未实现（本里程碑只做直伤）：%s" % type_id)

	var skill_id := str(skill_row.skill_id)
	var skill_base: Resource = _db.get_row("skill_base", skill_id)
	var star := int(skill_base.star) if skill_base != null else 1
	var mastery := 0
	if attacker.has_method("mastery_of"):
		mastery = int(attacker.mastery_of(skill_id))
	var mastery_multiplier: float = _growth.mastery_multiplier(star, mastery)
	# 天赋「天生武胆」（设计 12 §六：招式伤害 +15%）：**只对真招式**算——
	# 普通攻击没有 skill_base 行，不吃这一条（设计写的是"招式伤害"）。乘区放在最后一步之后
	# 统一乘，见 step 8 之前那一行，免得插进九步顺序里。
	var talent_skill_factor := 1.0
	if skill_base != null:
		var rules: Dictionary = attacker.talent_rules if attacker.talent_rules != null else {}
		talent_skill_factor += float(rules.get("skill_damage", 0.0))

	var element: String = str(modifiers.get("element_override", skill_row.element))
	if element.is_empty():
		element = DEFAULT_ELEMENT
	var defender_element: String = str(defender.tags.get("element", DEFAULT_ELEMENT))

	# 0. 闪避判定：未命中就完全免伤，后续全部跳过（也就不再判格挡）
	var chance := hit_chance(attacker, defender)
	var hit := true
	if damage_type_row.can_dodge and not bool(modifiers.get("force_hit", false)):
		hit = _rng.chance(chance)
	if not hit:
		return {
			"ok": true,
			"is_hit": false,
			"is_crit": false,
			"is_blocked": false,
			"damage": 0,
			"raw_damage": 0,
			"detail": {"hit_chance": chance, "element": element, "blocked": false},
		}

	# 1. 技能基础值 × 倍率 × 熟练度系数
	var step_d := attack_value(attacker, element) * float(skill_row.power_ratio) * mastery_multiplier
	# 2. 攻击属性系数
	var step_a := step_d * (1.0 + float(modifiers.get("attack_stat_bonus", 0.0)))
	# 3. 暴击（防御之前）：倍率 = crit_base_mult + 暴击伤害
	var crit := false
	if damage_type_row.can_crit:
		crit = bool(modifiers.get("force_crit", false)) or _rng.chance(_stat(attacker, "crit_rate"))
	var crit_multiplier := 1.0
	if crit:
		crit_multiplier = crit_base_mult() + _crit_damage(attacker)
	var step_c := step_a * crit_multiplier
	# 4. 系别相克（同样在防御之前）
	var counter := element_multiplier(element, defender_element)
	var step_k := step_c * counter
	# 5. 穿透压低有效防御，再算减免
	var pen_rate := clampf(_stat(attacker, "pen_rate"), 0.0, _stat_cap("pen_rate", PEN_RATE_CAP_DEFAULT))
	var raw_defense := defense_value(defender, damage_type_row)
	var effective_defense := raw_defense * (1.0 - pen_rate)
	var reduction := defense_reduction(effective_defense, attacker.level)
	var step_r := step_k * (1.0 - reduction)
	# 6. 格挡：防御之后的独立乘区（能走到这里说明没被闪避）
	var blocked := false
	var block_reduction := 0.0
	if not bool(modifiers.get("no_block", false)):
		var block_rate := clampf(_stat(defender, "block_rate"), 0.0, _stat_cap("block_rate", BLOCK_RATE_CAP_DEFAULT))
		blocked = block_rate > 0.0 and (bool(modifiers.get("force_block", false)) or _rng.chance(block_rate))
		if blocked:
			block_reduction = clampf(
				_stat(defender, "block_reduction"),
				0.0,
				_stat_cap("block_reduction", BLOCK_REDUCTION_CAP_DEFAULT)
			)
	var step_b := step_r * (1.0 - block_reduction)
	# 7. 增伤 / 减伤（减伤是终局乘区：装备属性 + 临时效果）
	# 天赋「天生武胆」的招式伤害加成也落在**增伤**这一区（不是另插一步，九步顺序不动）
	var dmg_up := (1.0 + float(modifiers.get("dmg_up", 0.0))) * talent_skill_factor
	var total_reduction := clampf(
		_stat(defender, "dmg_reduction") + float(modifiers.get("dmg_down", 0.0)),
		0.0,
		_stat_cap("dmg_reduction", DMG_REDUCTION_CAP_DEFAULT)
	)
	var step_m := step_b * dmg_up * (1.0 - total_reduction)
	# 8. 随机浮动
	var variance := 1.0
	if not bool(modifiers.get("no_variance", false)):
		variance = _rng.randf_range(variance_min(), variance_max())
	var step_f := step_m * variance
	# 9. 取整
	var raw := int(floor(step_f))

	return {
		"ok": true,
		"is_hit": true,
		"is_crit": crit,
		"is_blocked": blocked,
		"damage": maxi(raw, 1),
		"raw_damage": raw,
		"detail": {
			"damage_type": type_id,
			"element": element,
			"defender_element": defender_element,
			"attack_value": attack_value(attacker, element),
			"skill_id": skill_id,
			"star": star,
			"mastery": mastery,
			"mastery_multiplier": mastery_multiplier,
			"talent_skill_factor": talent_skill_factor,
			"step_d": step_d,
			"step_a": step_a,
			"step_c": step_c,
			"step_k": step_k,
			"step_r": step_r,
			"step_b": step_b,
			"step_m": step_m,
			"step_f": step_f,
			"crit_multiplier": crit_multiplier,
			"element_multiplier": counter,
			"pen_rate": pen_rate,
			"defense": raw_defense,
			"effective_defense": effective_defense,
			"defense_reduction": reduction,
			"blocked": blocked,
			"block_reduction": block_reduction,
			"dmg_reduction": total_reduction,
			"variance": variance,
		},
	}


func _crit_damage(actor) -> float:
	var value := _stat(actor, "crit_dmg", -1.0)
	if value < 0.0:
		return DEFAULT_CRIT_DMG
	return value


func _stat(actor, stat_id: String, fallback: float = 0.0) -> float:
	if actor.has_method("stat"):
		return float(actor.stat(stat_id, fallback))
	return float(actor.stats.get(stat_id, fallback))


func _fail(message: String) -> Dictionary:
	push_error("[DamageResolver] " + message)
	return {"ok": false, "is_hit": false, "is_crit": false, "damage": 0, "raw_damage": 0, "error": message}
