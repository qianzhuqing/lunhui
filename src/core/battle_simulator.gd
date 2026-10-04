## 无界面战斗模拟器：把「出手顺序 → 选招 → 结算 → 回合末回复 → 胜负」跑完。
##
## 它的存在是为了让伤害管线、属性、掉落能在 headless 下端到端验证；
## 加速、自动战斗策略、架势与破绽、异常状态留到后续里程碑。
class_name BattleSimulator
extends RefCounted

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const DamageResolverScript := preload("res://src/core/damage_resolver.gd")
## buff 的发放规则（buff_grant 的读法）只写在 BuffService 里，模拟器不自己扫表
const BuffServiceScript := preload("res://src/core/buff_service.gd")

const MAX_ROUNDS := 50
const WINNER_ALLY := "ally"
const WINNER_ENEMY := "enemy"
const WINNER_DRAW := "draw"
## 主动撤退：不是胜负，是「离开战斗」——不结算经验铜钱掉落，明雷也不清
const WINNER_FLEE := "flee"

## 逃跑判定（设计 02 只写了「被追上第一回合不能逃」，成功率公式没给，以下是开发侧定的）：
## 以双方「最高身法」对比，同速 50%，快的一边接近上限、慢的一边接近下限，
## 用 (mine-theirs)/(mine+theirs) 自归一化，不需要额外的量纲常数。数值待设计确认。
const FLEE_BASE_CHANCE := 0.5
const FLEE_MIN_CHANCE := 0.25
const FLEE_MAX_CHANCE := 0.95

var _db
var _rng
var _resolver
## BuffService 只留一份（它内部有按来源/buff 的缓存；`_is_support_skill()` 会被循环调用）
var _buff_service_cache = null


func _init(db, rng) -> void:
	_db = db
	_rng = rng
	_resolver = DamageResolverScript.new(db, rng)


## 战斗状态（分步执行用）：setup → step_round × N → result
var _allies: Array = []
var _enemies: Array = []
var _options: Dictionary = {}
var _log: Array = []
var _rounds: int = 0
var _damage_dealt: Dictionary = {}
## 结构化事件（给表现层用：跳字、受击反馈）。UI 用 take_events() 取走，不走日志解析。
var _events: Array = []
var _encounter = null
## 破绽窗口里的增伤（设计：架势打空后的窗口「受绝招伤害大幅提高」）
const POISE_BREAK_DAMAGE_BONUS := 0.5

## 自动战斗策略（设计 04 只给了三个名字：保守／均衡／全力，语义是开发侧定的，等设计确认）：
##   balanced     均衡（默认）：期望总伤害 = 倍率 × 段数 × 能打到的目标数 —— 多人时偏爱群攻
##   all_out      全力：只挑单发倍率最高的招式，不管几段、不管能打几个 —— 「把伤害压在一下上」
##   conservative 保守：最省内力（同消耗时挑倍率最低的）—— 能用起手式就用起手式
## 只影响**我方自动战斗**；敌人 AI 永远走均衡，免得换策略就改了敌人的行为。
const STRATEGY_BALANCED := "balanced"
const STRATEGY_ALL_OUT := "all_out"
const STRATEGY_CONSERVATIVE := "conservative"
const STRATEGY_ORDER := [STRATEGY_CONSERVATIVE, STRATEGY_BALANCED, STRATEGY_ALL_OUT]
const STRATEGY_LABELS := {
	STRATEGY_CONSERVATIVE: "保守",
	STRATEGY_BALANCED: "均衡",
	STRATEGY_ALL_OUT: "全力",
}
## 拆招（设计 04 核心循环）：敌方出招前有一回合预兆，玩家花一次行动拆招，
## 读到的那一招**伤害减半**，拆招者**反涨自身架势**。
## 减半是设计原文写死的；反涨的比例设计没给数值，先定「自身架势上限的 15%」，
## 和 poise_regen 一样写成代码常量并记进交接表，等设计补表列。
const PARRY_DAMAGE_MULT := 0.5
const PARRY_POISE_GAIN := 0.15
## 当前回合的行动顺序与游标（逐行动推进用）
var _order: Array = []
var _order_index: int = 0
## 本回合每个敌人的预兆招式（enemy.actor_id → skill_id）
var _intents: Dictionary = {}
## 本回合已经被拆招的敌人（enemy.actor_id → true）
var _parried: Dictionary = {}
## 已经成功撤退（战斗以「离开」收场）
var _escaped := false
## 我方自动战斗策略（见 STRATEGY_* 常量；敌人不受影响）
var _strategy: String = STRATEGY_BALANCED


## 摆好一场战斗。options: {"max_rounds": int, "modifiers": Dictionary, "encounter": Encounter}
func setup(allies: Array, enemies: Array, options: Dictionary = {}) -> void:
	_allies = allies
	_enemies = enemies
	_options = options
	_log = []
	_rounds = 0
	_damage_dealt = {}
	_events = []
	_encounter = options.get("encounter", null)
	_order = []
	_order_index = 0
	_intents = {}
	_parried = {}
	_escaped = false
	_strategy = str(options.get("strategy", STRATEGY_BALANCED))
	if not STRATEGY_ORDER.has(_strategy):
		_strategy = STRATEGY_BALANCED
	_apply_encounter_opening()
	_apply_pending_grants()


## 开打时把「进场 buff」逐条发下去（装备的 on_equip／on_battle_start、战斗外增益）。
## 常驻 buff 不走这里——它们在构造角色时就进了贡献列表（见 BattleActor 的 buff 一节）。
func _apply_pending_grants() -> void:
	for actor in _allies + _enemies:
		var grants: Array = actor.pending_grants
		if grants.is_empty():
			continue
		for grant: Dictionary in grants:
			var result: Dictionary = actor.add_buff(
				str(grant["buff_id"]), str(grant.get("source_type", "")),
				str(grant.get("source_id", "")), int(grant.get("stacks", 1))
			)
			if bool(result.get("applied", false)):
				_log.append("%s 带着 %s 入场（%s）" % [
					actor.display_name, str(result["name"]), _duration_text(result),
				])
		actor.pending_grants = []


func _duration_text(buff_result: Dictionary) -> String:
	var remaining := int(buff_result.get("remaining", 0))
	if remaining > 0:
		return "%d 回合" % remaining
	return "整场" if remaining < 0 else "常驻"


## 开局修正：背袭时敌方架势初始值降低（比例来自 combat_const）
func _apply_encounter_opening() -> void:
	if _encounter == null:
		return
	var ratio := float(_encounter.enemy_poise_reduce)
	if ratio <= 0.0:
		return
	for enemy in _enemies:
		enemy.reduce_poise_ratio(ratio)


## 开一个新回合（算好出手顺序，游标归零），之后可以逐行动推进
func begin_round() -> void:
	if finished():
		return
	var max_rounds: int = int(_options.get("max_rounds", MAX_ROUNDS))
	if _rounds >= max_rounds:
		return
	_rounds += 1
	_log.append("—— 第 %d 回合 ——" % _rounds)
	_order = turn_order(_allies, _enemies)
	if _rounds == 1 and _encounter != null and int(_encounter.first_side) >= 0:
		_order = _prioritize(_order, int(_encounter.first_side))
	# 天赋「先发制人」（设计 12 §六：首回合必定先手）：第 1 回合把带着这条规则的人提到最前
	_order = _apply_talent_first_strike(_order, _rounds)
	# 主动防御的「下回合先手」：把上回合用过防御姿态的人提到本回合最前
	_order = _apply_first_strike(_order)
	_order_index = 0
	_refresh_intents()


## 把手上有「先手」标记的单位提到出手序最前（互相之间保持原来的身法顺序），用完即清
func _apply_first_strike(order: Array) -> Array:
	if _pending_first_next_round.is_empty():
		return order
	var head: Array = []
	var rest: Array = []
	for actor in order:
		if _pending_first_next_round.has(str(actor.actor_id)):
			head.append(actor)
		else:
			rest.append(actor)
	_pending_first_next_round.clear()
	return head + rest


## 天赋「先发制人」（设计 12 §六：**首回合必定先手**）：
## 只在第 1 回合生效，把带着 `rule:first_round_priority` 的单位提到出手序最前
## （互相之间仍是原来的身法顺序）。与上面那条分开：那条是「主动防御的下回合先手」，
## 跨回合、且要清标记；这条是**开局一次**的规则，读战斗单位上的天赋快照。
func _apply_talent_first_strike(order: Array, round_no: int) -> Array:
	if round_no != 1:
		return order
	var head: Array = []
	var rest: Array = []
	for actor in order:
		var rules: Dictionary = actor.talent_rules if actor.talent_rules != null else {}
		if float(rules.get("first_round_priority", 0.0)) > 0.0:
			head.append(actor)
		else:
			rest.append(actor)
	if head.is_empty():
		return order
	return head + rest


## 当前该行动的单位；本回合跑完或该单位无法行动时返回 null
func current_actor():
	while _order_index < _order.size():
		var actor = _order[_order_index]
		if actor.is_alive() and _has_living(_allies) and _has_living(_enemies):
			return actor
		_order_index += 1
	return null


func in_round() -> bool:
	return current_actor() != null


## 当前单位能用的招式（内力够、武器匹配、伤害类型支持）
func available_skills(actor) -> Array:
	var out: Array = []
	for skill_id: String in actor.skills:
		var active: Resource = _db.get_row("skill_active", skill_id)
		if active == null:
			continue
		# 增益招式（没有伤害、但表里配了 on_cast 发放）也算「能用」——施放一次给自己上 buff
		if not active.is_attack() and not _is_support_skill(skill_id):
			continue
		# 冷却中不算「可用」（界面上会灰着写清楚还差几回合）
		if cooldown_left(str(actor.actor_id), skill_id) > 0:
			continue
		if actor.max_qi() > 0 and int(active.qi_cost) > actor.qi:
			continue
		# 增益招式没有伤害类型，跳过「伤害类型支持」这一条（它本来就不走伤害管线）
		if active.is_attack() and not _is_supported_damage(str(active.damage_type)):
			continue
		var base: Resource = _db.get_row("skill_base", skill_id)
		if base != null and not _weapon_allows(actor, base):
			continue
		out.append(active)
	return out


## 这个单位身上「这一招能不能放、为什么不能」的完整体检表（给界面用）：
## [{skill_id, name, qi_cost, power_ratio, ok, reason, cooldown_left}]
func skill_options(actor) -> Array:
	var out: Array = []
	if actor == null:
		return out
	for skill_id: String in actor.skills:
		var active: Resource = _db.get_row("skill_active", skill_id)
		if active == null:
			continue
		if not active.is_attack() and not _is_support_skill(skill_id):
			continue
		var is_support: bool = not active.is_attack()
		var base: Resource = _db.get_row("skill_base", skill_id)
		var reason := ""
		var left := cooldown_left(str(actor.actor_id), skill_id)
		if left > 0:
			reason = "冷却中（第 %d 回合起可用）" % cooldown_ready_round(str(actor.actor_id), skill_id)
		elif actor.max_qi() > 0 and int(active.qi_cost) > actor.qi:
			reason = "内力不足（要 %d）" % int(active.qi_cost)
		elif not is_support and not _is_supported_damage(str(active.damage_type)):
			reason = _unsupported_damage_reason(str(active.damage_type))
		elif base != null and not _weapon_allows(actor, base):
			reason = "武器不符"
		out.append({
			"skill_id": skill_id,
			"name": str(base.name_cn) if base != null else skill_id,
			"qi_cost": int(active.qi_cost),
			"power_ratio": float(active.power_ratio),
			"hit_count": int(active.hit_count),
			"target_type": str(active.target_type),
			"support": is_support,
			"ok": reason.is_empty(),
			"reason": reason,
			"cooldown_left": left,
		})
	return out


## 用指定招式打指定目标（手选招式走这里），返回本行动的日志行
## 取走本批事件（取完即清空）
func take_events() -> Array:
	var out := _events
	_events = []
	return out


## 用指定招式打指定目标（手选招式走这里），返回本行动的日志行
func act(actor, skill: Resource, target) -> Array:
	if actor == null or skill == null or target == null:
		return []
	var from_index := _log.size()
	# 增益招式：没有伤害、不走伤害管线——施放一次给自己上 buff（表里那条 on_cast 发放）
	if not skill.is_attack():
		if not _is_support_skill(str(skill.skill_id)):
			return []
		_record_use(actor, skill)
		_start_cooldown(actor, skill)
		_log.append("R%d %s 施放 %s（增益）" % [_rounds, actor.display_name, _skill_name(skill)])
		_apply_cast_grants(actor, skill)
		actor.spend_qi(int(skill.qi_cost))
		_order_index += 1
		if not _order.is_empty() and current_actor() == null:
			_finish_round()
		return _log.slice(from_index)
	_record_use(actor, skill)
	_start_cooldown(actor, skill)
	# 手选招式也要遵守 target_type：全体招式点谁都一样，打的是全场
	for hit_target in _targets_for(actor, skill, target):
		_act_once(actor, skill, hit_target)
	actor.spend_qi(int(skill.qi_cost))
	_apply_cast_grants(actor, skill)
	_order_index += 1
	# 只有「本来就在回合里」才结算回合末：没有 begin_round 就调 act（用例／脚本直接出招）
	# 不该顺手把刚打出来的破绽窗口和架势回复跑掉
	if not _order.is_empty() and current_actor() == null:
		_finish_round()
	return _log.slice(from_index)


## 手选招式时的目标集合：all_enemy 展开成全部活着的对手，其余就是选中的那一个
func _targets_for(actor, skill: Resource, chosen) -> Array:
	# `target_type=self`：打的是**自己**（反噬／自伤这类招式），玩家点谁都不改变这一点。
	# 以前它掉进下面的「就是选中的那一个」分支——于是自伤招式会打在敌人身上（06 允许 self，代码却没认）。
	if str(skill.target_type) == "self":
		return [actor]
	if str(skill.target_type) != "all_enemy":
		return [chosen]
	var opposing: Array = _enemies if actor.side == BattleActorScript.SIDE_ALLY else _allies
	var out: Array = []
	for candidate in opposing:
		if candidate.is_alive():
			out.append(candidate)
	return out if not out.is_empty() else [chosen]


## 让当前单位按自动策略行动
func auto_act() -> Array:
	var actor = current_actor()
	if actor == null:
		return []
	var from_index := _log.size()
	var skill: Resource = _skill_for(actor)
	if skill == null:
		_log.append("R%d %s 无可用招式，跳过" % [_rounds, actor.display_name])
		_order_index += 1
		if current_actor() == null:
			_finish_round()
		return _log.slice(from_index)
	_record_use(actor, skill)
	_start_cooldown(actor, skill)
	for target in _pick_targets(actor, _allies, _enemies):
		_act_once(actor, skill, target)
	actor.spend_qi(int(skill.qi_cost))
	_apply_cast_grants(actor, skill)
	_order_index += 1
	if current_actor() == null:
		_finish_round()
	return _log.slice(from_index)


## 一次结算（群攻招式对每个目标各调一次，但整段只算一次行动）
func _act_once(actor, skill: Resource, target) -> void:
	var modifiers := _modifiers_for(actor)
	if target.is_broken():
		# 破绽窗口：受到伤害提高（窗口由上一击打空架势产生）
		modifiers["dmg_up"] = float(modifiers.get("dmg_up", 0.0)) + POISE_BREAK_DAMAGE_BONUS
	# 拆招：被读破的那一招整招伤害减半（设计 04）。乘在现有增伤之后再减半，
	# 免得「破绽增伤 +50%」和「拆招 −50%」在同一层互相抵消。
	if actor.side == BattleActorScript.SIDE_ENEMY and _parried.has(str(actor.actor_id)):
		var up := 1.0 + float(modifiers.get("dmg_up", 0.0))
		modifiers["dmg_up"] = up * PARRY_DAMAGE_MULT - 1.0
	# 攻击段数：多段招式每段各掷一次命中/暴击/格挡，伤害与破架势逐段结算
	# （skill_active.hit_count；目标死了就不再打剩下的段）
	var hits := maxi(1, int(skill.hit_count))
	var total_dealt := 0
	var landed := 0
	var any_crit := false
	for hit_index in range(hits):
		if not target.is_alive():
			break
		var landed_this := _resolve_one_hit(actor, target, skill, modifiers, hit_index, hits)
		if landed_this < 0:
			break
		total_dealt += landed_this
		landed += 1
		any_crit = any_crit or bool(_last_hit_crit)
	if landed > 0:
		var suffix := ""
		if any_crit:
			suffix += "（暴击）"
		if hits > 1:
			suffix += "（%d 段）" % hits
		_log.append("R%d %s 用 %s 打 %s：%d%s" % [
			_rounds, actor.display_name, _skill_name(skill), target.display_name, total_dealt, suffix,
		])
	if target.is_broken():
		_log.append("R%d %s 的架势被打破，进入破绽！" % [_rounds, target.display_name])


## 多段循环里记录上一段是否暴击（只为了日志里能写「暴击」）
var _last_hit_crit := false


## 命中后发「命中触发」的 buff：**每段各算一次**（与逐段结算同一口径）。
##
## 两个来源：
##   · 装备特效（`actor.on_hit_grants`，由 `PartyBuilder` 从 `buff_grant(source_type=equip, trigger=on_hit)` 收集，
##     例：淬毒指环命中叠一层淬毒）；
##   · **招式自带**（`buff_grant(source_type=skill_active, trigger=on_hit)`，2026-10-03 补上——
##     `on_cast` 那次只接了「施放」，命中这一半漏了：设计写着「招式的增益效果＝命中/施放时发的 buff」）。
## 两者都上不去时**不静默**：把原因写进战报。
func _apply_hit_grants(actor, skill: Resource = null) -> void:
	if actor == null:
		return
	var grants: Array = actor.on_hit_grants.duplicate()
	var skill_id := str(skill.skill_id) if skill != null else ""
	if not skill_id.is_empty() and _db != null:
		for row: Resource in _buff_service().grants_for("skill_active", skill_id, "on_hit"):
			grants.append({
				"buff_id": str(row.buff_id), "source_type": "skill_active",
				"source_id": skill_id, "stacks": maxi(1, int(row.stacks)),
			})
	if grants.is_empty():
		return
	for grant: Dictionary in grants:
		var result: Dictionary = actor.add_buff(
			str(grant["buff_id"]), str(grant.get("source_type", "")),
			str(grant.get("source_id", "")), int(grant.get("stacks", 1))
		)
		if not bool(result.get("applied", false)):
			if str(result.get("reason", "")) != "unique":
				_log.append("%s 的 %s 命中附带 %s 没上上去：%s" % [
					actor.display_name, _skill_name(skill) if skill != null else "这一击",
					str(grant["buff_id"]), str(result.get("reason", "")),
				])
			continue
		if int(result["stacks"]) > 1:
			_log.append("%s 的 %s 叠到 %d 层" % [actor.display_name, str(result["name"]), int(result["stacks"])])
		_events.append({
			"kind": "buff_gain", "source": str(actor.actor_id), "target": str(actor.actor_id),
			"buff_id": str(result["buff_id"]), "damage": 0,
		})


## 结算一段。返回这一段造成的伤害；未命中返回 0；结算失败返回 -1（调用方停手）
func _resolve_one_hit(actor, target, skill: Resource, modifiers: Dictionary, hit_index: int, hit_count: int) -> int:
	var result: Dictionary = _resolver.resolve(actor, target, skill, modifiers)
	if not result.get("ok", false):
		_log.append("R%d %s 招式 %s 结算失败：%s" % [_rounds, actor.display_name, _skill_name(skill), result.get("error", "")])
		_events.append({"kind": "failed", "source": actor.actor_id, "target": target.actor_id, "damage": 0})
		return -1
	_last_hit_crit = bool(result.get("is_crit", false))
	if not result.get("is_hit", false):
		_log.append("R%d %s 用 %s 打 %s：未命中" % [_rounds, actor.display_name, _skill_name(skill), target.display_name])
		_events.append({
			"kind": "miss", "source": actor.actor_id, "target": target.actor_id, "damage": 0,
			"hit_index": hit_index, "hit_count": hit_count,
		})
		return 0
	var dealt: int = target.take_damage(int(result["damage"]))
	_damage_dealt[actor.actor_id] = int(_damage_dealt.get(actor.actor_id, 0)) + dealt
	if not target.is_alive() and not _kill_styles.has(target.actor_id):
		_kill_styles[target.actor_id] = "normal"
	# 架势伤害 = 招式破架势值 + 攻击者的破架势派生数值
	var poise_hit := int(skill.poise_damage) + int(actor.stat("poise_break"))
	target.take_poise_damage(poise_hit)
	# 命中触发（buff_grant.trigger=on_hit）：装备特效（淬毒指环）与招式自带的都在这发，逐段各一次
	_apply_hit_grants(actor, skill)
	var detail: Dictionary = result.get("detail", {})
	# 异常状态：命中之后掷一次（伤害类型是 dot 的招式本身没有直伤，全靠这里挂层）
	var status_event := _apply_status(actor, target, skill, modifiers)
	# 斩杀规则：血量掉到阈值以下，剩余 DoT 立刻结算（combat_const.dot_execute_threshold）
	var executed := _settle_execute(target)
	_events.append({
		"kind": "hit",
		"source": actor.actor_id,
		"target": target.actor_id,
		"damage": dealt,
		"is_crit": bool(result.get("is_crit", false)),
		# 系别：招式自己的，或被武器覆盖后的（见 _modifiers_for），界面与用例都能读到
		"element": str(detail.get("element", "")),
		"poise_damage": poise_hit,
		"was_broken": bool(target.is_broken()),
		"killed": not target.is_alive(),
		"hit_index": hit_index,
		"hit_count": hit_count,
	})
	if not status_event.is_empty():
		_events.append(status_event)
	for entry: Dictionary in executed:
		_events.append({"kind": "dot", "source": str(entry["source_id"]), "target": target.actor_id,
			"damage": int(entry["damage"]), "status_id": str(entry["status_id"]), "execute": true})
	return dealt


func _finish_round() -> void:
	for actor in _allies + _enemies:
		actor.end_of_round_regen()
		actor.end_of_round_poise()
	_tick_buffs()
	_tick_statuses()


## 回合末：限时 buff 各 −1，归零播报（常驻与整场不动，见 BattleActor.tick_buffs）
func _tick_buffs() -> void:
	for actor in _allies + _enemies:
		for entry: Dictionary in actor.tick_buffs():
			_log.append("R%d %s 的%s结束" % [_rounds, actor.display_name, _buff_name(str(entry["buff_id"]))])
			_events.append({
				"kind": "buff_expired", "source": str(entry.get("source_id", "")),
				"target": str(actor.actor_id), "buff_id": str(entry["buff_id"]), "damage": 0,
			})


func _buff_name(buff_id: String) -> String:
	var row: Resource = _db.get_row("buff_def", buff_id)
	return str(row.name_cn) if row != null else buff_id


## 回合末统一结算所有 DoT（设计：不按行动条各自计时，全部在回合末跳字）
func _tick_statuses() -> void:
	for actor in _allies + _enemies:
		if not actor.is_alive() or actor.statuses.is_empty():
			continue
		for entry: Dictionary in actor.tick_statuses():
			var damage := int(entry["damage"])
			if damage > 0:
				actor.take_damage(damage)
			if not actor.is_alive() and not _kill_styles.has(actor.actor_id):
				# 死于持续伤害：记下是哪一类（毒杀判定读这个）
				_kill_styles[actor.actor_id] = str(entry["status_id"])
			_log.append("R%d %s 受%s ×%d：%d%s" % [
				_rounds, actor.display_name, _status_name_by_id(str(entry["status_id"])),
				int(entry["stacks"]), damage,
				"（结束）" if bool(entry["expired"]) else "",
			])
			_events.append({
				"kind": "dot", "source": str(entry["source_id"]), "target": str(actor.actor_id),
				"damage": damage, "status_id": str(entry["status_id"]),
				"killed": not actor.is_alive(), "execute": false,
			})
		# 回合末的 DoT 也可能把血打下去：斩杀规则同样生效
		for entry: Dictionary in _settle_execute(actor):
			_events.append({
				"kind": "dot", "source": str(entry["source_id"]), "target": str(actor.actor_id),
				"damage": int(entry["damage"]), "status_id": str(entry["status_id"]), "execute": true,
			})


func _status_name_by_id(status_id: String) -> String:
	var row: Resource = _db.get_row("status_effect", status_id)
	return str(row.name_cn) if row != null else status_id


## 上状态的几率（04 文档：运决定触发几率）= base_chance × status_chance_mul + 异常触发率，
## 再乘 (1 − 目标抗性)，夹在 0~95%。
##
## `status_effect.trigger_stat` 记的是**负责的属性**（运），战斗单位身上只有算好的派生数值，
## 所以读 `debuff_chance`（attr_to_stat 1016：运 → 异常触发率）。强度同理读 `debuff_power`。
func status_chance(actor, target, skill: Resource) -> float:
	var status_id := str(skill.get("status_id"))
	if status_id.is_empty():
		return 0.0
	var row: Resource = _db.get_row("status_effect", status_id)
	if row == null:
		return 0.0
	var chance := float(row.base_chance) * float(skill.get("status_chance_mul"))
	chance += _resolver.stat_of(actor, "debuff_chance")
	# 抗性走 `BattleActor.resistance_of()`：**角色与敌人同一条派生数值**（stat_def.res_<status_id>），
	# 玩家练五毒心法也有毒抗（设计 0.12.0）。敌人行那份快照只是缺派生数值时的兜底。
	chance *= 1.0 - clampf(target.resistance_of(status_id), 0.0, 1.0)
	return clampf(chance, 0.0, 0.95)


## 跑完一整场（setup + 逐回合推进）。
func simulate(allies: Array, enemies: Array, options: Dictionary = {}) -> Dictionary:
	setup(allies, enemies, options)
	var max_rounds: int = int(_options.get("max_rounds", MAX_ROUNDS))
	while not finished() and _rounds < max_rounds:
		step_round()
	return result()


## 打完一回合，返回这一回合新增的日志行（战斗界面按回合播放）。
func step_round() -> Array:
	var from_index := _log.size()
	if finished():
		return []
	var max_rounds: int = int(_options.get("max_rounds", MAX_ROUNDS))
	if _rounds >= max_rounds:
		return []
	_rounds += 1
	_log.append("—— 第 %d 回合 ——" % _rounds)
	_refresh_intents()
	var order := turn_order(_allies, _enemies)
	if _rounds == 1 and _encounter != null and int(_encounter.first_side) >= 0:
		order = _prioritize(order, int(_encounter.first_side))
	order = _apply_talent_first_strike(order, _rounds)
	for actor in order:
		if not actor.is_alive():
			continue
		if not _has_living(_allies) or not _has_living(_enemies):
			break
		var targets := _pick_targets(actor, _allies, _enemies)
		if targets.is_empty():
			continue
		var skill: Resource = _skill_for(actor)
		if skill == null:
			_log.append("R%d %s 无可用招式，跳过" % [_rounds, actor.display_name])
			continue
		_record_use(actor, skill)
		_start_cooldown(actor, skill)
		var modifiers := _modifiers_for(actor)
		for target in targets:
			# 和界面那条路（begin_round/act）共用同一个「一次行动」：
			# 架势、异常状态、斩杀规则都只写一份，免得两条路行为不一致
			_act_once(actor, skill, target)
		actor.spend_qi(int(skill.qi_cost))
		_apply_cast_grants(actor, skill)
	_finish_round()
	return _log.slice(from_index)


func finished() -> bool:
	if _escaped:
		return true
	var max_rounds: int = int(_options.get("max_rounds", MAX_ROUNDS))
	if not _has_living(_allies) or not _has_living(_enemies):
		return true
	return _rounds >= max_rounds


func winner() -> String:
	if _escaped:
		return WINNER_FLEE
	if _has_living(_allies) and not _has_living(_enemies):
		return WINNER_ALLY
	if _has_living(_enemies) and not _has_living(_allies):
		return WINNER_ENEMY
	return WINNER_DRAW


func rounds_played() -> int:
	return _rounds


func result() -> Dictionary:
	var out := {
		"winner": winner(),
		"escaped": _escaped,
		"rounds": _rounds,
		"log": _log,
		"rewards": _rewards(_enemies),
		"damage_dealt": _damage_dealt,
		"allies": _allies,
		"enemies": _enemies,
	}
	if _encounter != null:
		out["encounter"] = _encounter.summary()
		out["pending_rules"] = Array(_encounter.pending_rules)
	# 「表里配了、代码还不认」的状态规则也要报出去（界面会播成「暂缓规则」）：
	# 静默忽略会让玩家（和下一个人）以为它生效了。见 battle_actor.KNOWN_STATUS_RULES。
	var pending: Array = Array(out.get("pending_rules", []))
	for actor in _allies + _enemies:
		for rule: String in actor.unhandled_rules:
			# 说中文状态名，别把 `rule_bleed_move` 这种表内 id 甩给玩家（文案纪律）
			var note := "%s 的特殊规则还没接（设计未定规则，暂不触发）" % _status_name_for_rule(actor, rule)
			if not pending.has(note):
				pending.append(note)
		# 同理：`effect_kind=special` 的 buff（例：淬毒）表里/代码里都还没有效果实现，
		# 玩家花容量装上却只拿到层数——必须如实播出来，不能静默。
		for entry: Dictionary in actor.unhandled_buffs:
			var buff_note := "%s 的「%s」还没实现（%s）" % [
				actor.display_name, str(entry["name"]), str(entry["reason"]),
			]
			if not pending.has(buff_note):
				pending.append(buff_note)
	if not pending.is_empty():
		out["pending_rules"] = pending
	return out


## 状态规则 id → 玩家看得懂的名字（查 `status_effect.name_cn`）；查不到才回 id
func _status_name_for_rule(actor, rule: String) -> String:
	for entry: Dictionary in actor.statuses:
		if str(entry.get("rule", "")) != rule:
			continue
		var row: Resource = _db.get_row("status_effect", str(entry.get("status_id", "")))
		if row != null:
			return str(row.name_cn)
	return rule


## 首回合的先手方排到前面（同侧内部保持原有顺序）
func _prioritize(order: Array, side: int) -> Array:
	var first: Array = []
	var rest: Array = []
	for actor in order:
		if actor.side == side:
			first.append(actor)
		else:
			rest.append(actor)
	first.append_array(rest)
	return first


## 基础修正 + 遭遇带来的首回合修正（奇袭增伤）
## 谁在战斗里施放过哪些招式（actor_id → {skill_id: 次数}）。
## 打完用来结算熟练度（growth_const.mastery_combat_gain 每次施放 +1），
## 一次行动只记一次——多目标招式会在 _act_once 里循环，不能按次记。
var _skill_uses: Dictionary = {}

## actor_id → 死因（"normal" 直伤 / "poison" 中毒 / "burn" 灼伤 …）。
## 隐藏内容要按击杀方式判定（`hidden_trigger.required_condition = kill_with=poison`）。
var _kill_styles: Dictionary = {}
## actor_id → {skill_id: 可以再用的回合数}（skill_active.cooldown：放完要等 N 个回合）
var _cooldowns: Dictionary = {}


## 这个招式还要等几回合才能再用（0 = 现在就能用）
func cooldown_left(actor_id: String, skill_id: String) -> int:
	var entries: Dictionary = _cooldowns.get(actor_id, {})
	var ready_round := int(entries.get(skill_id, 0))
	return maxi(0, ready_round - _rounds)


## 从第几回合起又能用了（0 = 现在就能用）
func cooldown_ready_round(actor_id: String, skill_id: String) -> int:
	var entries: Dictionary = _cooldowns.get(actor_id, {})
	return int(entries.get(skill_id, 0))


func _start_cooldown(actor, skill: Resource) -> void:
	var rounds := int(skill.get("cooldown"))
	if rounds <= 0 or actor == null:
		return
	var entries: Dictionary = _cooldowns.get(str(actor.actor_id), {})
	# 冷却 N 回合 = 接下来 N 个回合不能再放（第 N+1 个回合起可用）
	entries[str(skill.skill_id)] = _rounds + rounds + 1
	_cooldowns[str(actor.actor_id)] = entries


func kill_style_of(actor_id: String) -> String:
	return str(_kill_styles.get(actor_id, ""))


func kill_styles() -> Dictionary:
	return _kill_styles.duplicate()


## 某个单位这一场施放过的招式次数（没有就空字典）
func skill_uses(actor_id: String) -> Dictionary:
	return Dictionary(_skill_uses.get(actor_id, {})).duplicate()


func _record_use(actor, skill: Resource) -> void:
	if actor == null or skill == null:
		return
	# 现造的「普通攻击」不是武学：不进招式使用记录（隐藏内容的 skill_use 判定不该被它污染）
	if str(skill.skill_id) == BASIC_ATTACK_ID:
		return
	var actor_id := str(actor.actor_id)
	var uses: Dictionary = _skill_uses.get(actor_id, {})
	var skill_id := str(skill.skill_id)
	uses[skill_id] = int(uses.get(skill_id, 0)) + 1
	_skill_uses[actor_id] = uses


## 命中之后掷异常状态：几率 = 表里 base_chance × 招式的 status_chance_mul + 异常触发率（运），
## 再乘 (1 − 目标抗性)；强度走 resolve_dot 的快照伤害。返回一个事件（没上上去就是空字典）。
func _apply_status(actor, target, skill: Resource, modifiers: Dictionary) -> Dictionary:
	var status_id := str(skill.get("status_id"))
	if status_id.is_empty() or not target.is_alive():
		return {}
	var status_row: Resource = _db.get_row("status_effect", status_id)
	if status_row == null:
		push_error("[BattleSimulator] status_effect 缺少 %s" % status_id)
		return {}
	var chance := status_chance(actor, target, skill)
	var type_row: Resource = _db.get_row("damage_type", str(status_row.damage_type))
	var duration := int(status_row.duration)
	var max_stack := int(status_row.max_stack)
	if type_row != null:
		duration = maxi(duration, int(type_row.base_duration))
		max_stack = maxi(max_stack, int(type_row.max_stack))
	if not _rng.chance(chance):
		_log.append("R%d %s 的 %s 没上到 %s（几率 %.0f%%）" % [
			_rounds, actor.display_name, _status_name(status_row), target.display_name, chance * 100.0,
		])
		return {}
	# 用状态自己的伤害类型算每层伤害（中毒无视外防、流血吃外防、内伤吃内防 + 内伤抗性）
	var dot: Dictionary = _resolver.resolve_dot(
		actor, target, skill, modifiers, str(status_row.damage_type)
	)
	if not bool(dot.get("ok", false)):
		return {}
	var applied: Dictionary = target.add_status(
		status_id, str(actor.actor_id), float(dot["damage"]),
		max_stack, duration, str(status_row.extra_rule), bool(status_row.dispellable)
	)
	_log.append("R%d %s 让 %s 中了%s ×%d（每层 %d，持续 %d 回合）%s" % [
		_rounds, actor.display_name, target.display_name, _status_name(status_row),
		int(applied["stacks"]), int(dot["damage"]), duration,
		"（已满层，只刷新时长）" if bool(applied["refreshed"]) else "",
	])
	return {
		"kind": "status",
		"source": str(actor.actor_id),
		"target": str(target.actor_id),
		"status_id": status_id,
		"stacks": int(applied["stacks"]),
		"per_stack": int(dot["damage"]),
		"damage": 0,
	}


func _status_name(status_row: Resource) -> String:
	return str(status_row.name_cn) if status_row != null else "异常"


## 斩杀：目标血量比例低于 combat_const.dot_execute_threshold 时，剩余层数立刻结算并清空
func _settle_execute(target) -> Array:
	if not target.is_alive() or target.statuses.is_empty():
		return []
	var ratio := float(target.hp) / maxf(1.0, float(target.max_hp()))
	if ratio >= _execute_threshold():
		return []
	var settled: Array = target.consume_all_statuses()
	var total := 0
	for entry: Dictionary in settled:
		total += int(entry["damage"])
	if total > 0:
		target.take_damage(total)
		if not target.is_alive() and not _kill_styles.has(target.actor_id):
			_kill_styles[target.actor_id] = str(settled[0]["status_id"])
		_log.append("R%d 斩杀：%s 气血低于 %.0f%%，剩余持续伤害立即结算 %d" % [
			_rounds, target.display_name, _execute_threshold() * 100.0, total,
		])
	return settled


func _execute_threshold() -> float:
	var value := 0.15
	var row: Resource = _db.get_row("combat_const", "dot_execute_threshold")
	if row != null:
		value = float(row.value)
	return value


func _modifiers_for(actor) -> Dictionary:
	var modifiers: Dictionary = (_options.get("modifiers", {}) as Dictionary).duplicate()
	if _encounter != null:
		var extra: Dictionary = _encounter.round_modifiers(_rounds, actor.side)
		for key: String in extra:
			modifiers[key] = float(modifiers.get(key, 0.0)) + float(extra[key])
	# 武器系别覆盖招式系别（05 文档：具体系别以每件装备自己的 element 为准）
	# 敌人没有装备，tags 里没有这一项，自然还是按招式自己的系别
	var weapon_element := str(actor.tags.get("weapon_element", ""))
	if not weapon_element.is_empty() and not modifiers.has("element_override"):
		modifiers["element_override"] = weapon_element
	return modifiers


## 出手顺序：身法高的先手，同速按传入次序稳定排列。
func turn_order(allies: Array, enemies: Array) -> Array:
	var living: Array = []
	for actor in allies + enemies:
		if actor.is_alive():
			living.append(actor)
	var indexed: Array = []
	for index in range(living.size()):
		indexed.append({"index": index, "actor": living[index]})
	indexed.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var speed_a: float = float(a["actor"].stat("speed"))
		var speed_b: float = float(b["actor"].stat("speed"))
		if is_equal_approx(speed_a, speed_b):
			return int(a["index"]) < int(b["index"])
		return speed_a > speed_b
	)
	var out: Array = []
	for entry: Dictionary in indexed:
		out.append(entry["actor"])
	return out


## 选招：取倍率最高的可用招式。
##
## 0.6.0 起招式数值在 skill_active（skill_base 只放身份与门槛），所以这里返回的是 skill_active 行。
## 过滤条件：能支付内力、武器类型匹配、伤害类型被管线支持（只有**反伤**还没实现，跳过而不是报错；
## DoT 类是支持的——见 `_is_supported_damage()` 的注释）。
func pick_skill(actor) -> Resource:
	var best: Resource = null
	# 从 -INF 起：保守策略的分数是负的（省内力），用 -1 当初值会一个都挑不出来
	var best_power := -INF
	for skill_id: String in actor.skills:
		var active: Resource = _db.get_row("skill_active", skill_id)
		if not _is_usable(actor, active):
			continue
		# 自动策略按「期望总伤害」挑：倍率 × 段数 × 这一下能打到几个人。
		# 只看倍率的话，横扫（1.3 倍 × 3 段 × 全体）永远输给斩风（2.1 倍单体），那张表就白配了。
		var power := _skill_score(actor, active)
		if power > best_power:
			best = active
			best_power = power
	return best


## 这一招现在能不能选（冷却、内力、伤害类型、武器四条，与 available_skills 同一口径）
func _is_usable(actor, active: Resource) -> bool:
	if active == null or not active.is_attack():
		return false
	if cooldown_left(str(actor.actor_id), str(active.skill_id)) > 0:
		return false
	# 敌人没有内力池（enemy_base 没有 qi 列，EnemyFactory 给 qi_max=0），
	# 所以内力消耗只对**有内力池的单位**生效——这条缺口记在交接表的 enemy_data 备注里。
	if actor.max_qi() > 0 and int(active.qi_cost) > actor.qi:
		return false
	if not _is_supported_damage(str(active.damage_type)):
		return false
	var base: Resource = _db.get_row("skill_base", str(active.skill_id))
	if base != null and not _weapon_allows(actor, base):
		return false
	return true


## 本回合该用什么招：敌人**照预兆里定好的那一招**打（预兆了却临时变招，玩家就白拆了）；
## 预兆的那招这一回合变得不可用（例如被拆到没内力）才退回重新选。
func _skill_for(actor) -> Resource:
	if actor.side == BattleActorScript.SIDE_ENEMY:
		var planned := intent_of(str(actor.actor_id))
		if not planned.is_empty():
			var active: Resource = _db.get_row("skill_active", planned)
			if _is_usable(actor, active):
				return active
	return pick_skill(actor)


# ------------------------------------------------------------------ 逃跑（设计 02：被追上第一回合不能逃）

## 现在能不能撤；能撤返回空串，否则返回给玩家看的原因
func flee_block_reason(actor) -> String:
	if actor == null:
		return "现在不是我方行动"
	if actor.side != BattleActorScript.SIDE_ALLY:
		return "只能我方决定撤不撤"
	if _escaped or finished():
		return "战斗已经结束了"
	# 设计 02 的接触结果表：被追击抓到 → 敌方先手，且第一回合无法逃跑
	if _rounds <= 1 and _encounter != null and str(_encounter.contact) == EncounterScript.CONTACT_CAUGHT:
		return "被追上来的第一回合撤不掉"
	if _order.is_empty() or current_actor() != actor:
		return "现在不是我方行动"
	return ""


## 逃跑成功率：双方最高身法对比，同速 50%，快的一方更高（夹在 25%~95%）
func flee_chance(_actor = null) -> float:
	var mine := _best_speed(_allies)
	var theirs := _best_speed(_enemies)
	var total := mine + theirs
	if total <= 0.0:
		return FLEE_BASE_CHANCE
	var chance := FLEE_BASE_CHANCE + (1.0 - FLEE_BASE_CHANCE) * ((mine - theirs) / total)
	return clampf(chance, FLEE_MIN_CHANCE, FLEE_MAX_CHANCE)


func _best_speed(actors: Array) -> float:
	var best := 0.0
	for actor in actors:
		if actor.is_alive():
			best = maxf(best, float(actor.stat("speed")))
	return best


## 逃跑：花掉这次行动掷一次判定。成功 → 战斗以「撤退」收场（无经验无掉落、明雷不清）；
## 失败 → 白费一次行动。
## 返回 {ok, escaped, error, lines, chance, roll}
func flee(actor) -> Dictionary:
	var reason := flee_block_reason(actor)
	if not reason.is_empty():
		return {"ok": false, "escaped": false, "error": reason, "lines": [], "chance": 0.0, "roll": 0.0}
	var chance := flee_chance(actor)
	var roll: float = _rng.randf()
	var modifiers: Dictionary = _options.get("modifiers", {})
	var escaped: bool = roll < chance
	# 测试开关：force_flee / no_flee（和 force_hit／force_crit 同一条路子）
	if bool(modifiers.get("force_flee", false)):
		escaped = true
	elif bool(modifiers.get("no_flee", false)):
		escaped = false
	var lines := PackedStringArray()
	if escaped:
		_escaped = true
		lines.append("R%d %s 带队撤退成功（掷 %.2f < %.2f）" % [_rounds, actor.display_name, roll, chance])
	else:
		lines.append("R%d %s 想撤，被缠住了（掷 %.2f ≥ %.2f）" % [_rounds, actor.display_name, roll, chance])
		_order_index += 1
		if not _order.is_empty() and current_actor() == null:
			_finish_round()
	return {
		"ok": true, "escaped": escaped, "error": "", "lines": Array(lines),
		"chance": chance, "roll": roll,
	}


# ------------------------------------------------------------------ 预兆与拆招（设计 04 核心循环）

## 每回合开始给每个活着的敌人定好这一回合的招式，玩家在轮到自己之前就能看见预兆
func _refresh_intents() -> void:
	_intents = {}
	_parried = {}
	for enemy in _enemies:
		if not enemy.is_alive():
			continue
		var skill: Resource = pick_skill(enemy)
		if skill != null:
			_intents[str(enemy.actor_id)] = str(skill.skill_id)


## 某个敌人这一回合预兆的招式 id（没有返回空串）
func intent_of(actor_id: String) -> String:
	return str(_intents.get(actor_id, ""))


func is_parried(actor_id: String) -> bool:
	return _parried.has(actor_id)


## 预兆文案（界面与战报用）：["大寨主 将用 黑风刀法·斩风", ...]
func intent_lines() -> PackedStringArray:
	var out := PackedStringArray()
	for enemy in _enemies:
		if not enemy.is_alive():
			continue
		var skill_id := intent_of(str(enemy.actor_id))
		if skill_id.is_empty():
			continue
		out.append("%s 将用 %s" % [enemy.display_name, skill_display_name(skill_id)])
	return out


## 拆招能不能用；能用返回空串，否则返回给玩家看的原因
func parry_block_reason(actor, target) -> String:
	if actor == null:
		return "现在不是我方行动"
	if target == null:
		return "没有可拆的目标"
	if actor.side != BattleActorScript.SIDE_ALLY or target.side != BattleActorScript.SIDE_ENEMY:
		return "只能拆敌人的招"
	if not target.is_alive():
		return "%s 已经倒下" % target.display_name
	if not _has_living(_allies) or not _has_living(_enemies):
		return "战斗已结束"
	var target_id := str(target.actor_id)
	if not _intents.has(target_id):
		return "%s 这一回合没有出招预兆" % target.display_name
	if _parried.has(target_id):
		return "%s 的招这一回合已经拆过了" % target.display_name
	return ""


## 拆招：花掉这次行动，读破目标的预兆招式（那一招伤害减半），并反涨自身架势。
## 返回 {ok, error, lines, poise_gain}
func parry(actor, target) -> Dictionary:
	var reason := parry_block_reason(actor, target)
	if not reason.is_empty():
		return {"ok": false, "error": reason, "lines": [], "poise_gain": 0}
	var target_id := str(target.actor_id)
	_parried[target_id] = true
	var before: int = actor.poise
	# 架势池为 0 的单位（没有架势机制的临时夹具）没有「反涨」可言，跳过而不是加 0 条假日志
	var pool: int = actor.max_poise()
	if pool > 0:
		actor.poise = mini(pool, actor.poise + maxi(1, int(round(float(pool) * PARRY_POISE_GAIN))))
	var lines := PackedStringArray()
	lines.append("R%d %s 拆招，读破 %s 的 %s（这一招伤害减半）" % [
		_rounds, actor.display_name, target.display_name, skill_display_name(_intents[target_id]),
	])
	lines.append("R%d %s 架势 +%d（%d/%d）" % [
		_rounds, actor.display_name, int(actor.poise) - before, int(actor.poise), actor.max_poise(),
	])
	_order_index += 1
	# 拆招也是一次行动：拆完这回合的行动就用掉了，回合末结算照旧
	if not _order.is_empty() and current_actor() == null:
		_finish_round()
	return {
		"ok": true, "error": "", "lines": Array(lines),
		"poise_gain": int(actor.poise) - before, "target": target_id,
	}


# ------------------------------------------------------------------ 六指令（08：唯一的行动入口）
#
# 六条：普通攻击／招式／内功／道具／主动防御／逃跑。招式（`act`）与逃跑（`flee`）早早就有，
# 这里补齐其余四条，并统一「花掉一次行动」的收尾（本回合跑完就结算回合末，与 `act` 同一条路）。

## 普通攻击：不耗内力、不看武器与内力门槛（谁都能打一下），固定倍率与破架势。
## 这两条数值在表里**没有落点**（没有任何「普通攻击」行，`combat_const` 也没有），
## 用开发侧常数并在交接表登记请设计补列——别让它变成第二份没人管的数值。
const BASIC_ATTACK_ID := "__basic_attack__"
const BASIC_ATTACK_POWER := 1.0
const BASIC_ATTACK_POISE := 8

var _basic_attack_row: Resource = null


## 现造一条招式行（不落表）：走与招式完全同一条伤害管线，只是没有 skill_base 身份
## （星级按缺省 1 星、无熟练度，见 DamageResolver 的 null 兜底）。
func basic_attack_skill() -> Resource:
	if _basic_attack_row == null:
		var row = load("res://src/data/tables/skill_active_row.gd").new()
		row.skill_id = BASIC_ATTACK_ID
		row.damage_type = "dmg_normal"
		row.power_ratio = BASIC_ATTACK_POWER
		row.qi_cost = 0
		row.poise_damage = BASIC_ATTACK_POISE
		row.hit_count = 1
		row.target_type = "single"
		_basic_attack_row = row
	return _basic_attack_row


func basic_attack(actor, target) -> Array:
	if actor == null or target == null or not target.is_alive():
		return []
	var from_index := _log.size()
	var skill := basic_attack_skill()
	for hit_target in _targets_for(actor, skill, target):
		_act_once(actor, skill, hit_target)
	_spend_action(actor)
	return _log.slice(from_index)


## 主动防御：「花一次行动换 buff_def 里的防御姿态」。
## `buff_stat` 给的是减伤与外防；**架势回复与下回合先手表里都没有字段**——
## 架势回复先按开发侧常数做（与拆招同一档 15%，已记交接表），下回合先手在这条代码里实现
## （`_pending_first_next_round`），因为它是这套 buff 唯一无法用数值列表达的语义。
const DEFEND_BUFF_ID := "buff_guard"
const DEFEND_POISE_GAIN := 0.15

var _pending_first_next_round: Dictionary = {}


func defend_block_reason(actor) -> String:
	if actor == null or not actor.is_alive():
		return "这个单位不能行动"
	var service = actor.buff_service()
	if service == null or not service.exists(DEFEND_BUFF_ID):
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[BattleSimulator] buff_def 里没有防御姿态（%s）" % DEFEND_BUFF_ID)
		return "防御姿态的配置对不上（数据错，已记进日志）"
	if _escaped or finished():
		return "战斗已经结束了"
	if actor.has_buff(DEFEND_BUFF_ID):
		return "已经在这个姿态里了"
	return ""


## 返回 {ok, error, lines, poise_gain}
func defend(actor) -> Dictionary:
	var reason := defend_block_reason(actor)
	if not reason.is_empty():
		return {"ok": false, "error": reason, "lines": [], "poise_gain": 0}
	var from_index := _log.size()
	var result: Dictionary = actor.add_buff(DEFEND_BUFF_ID, "action", "defend")
	if not bool(result.get("applied", false)):
		var why := str(result.get("reason", ""))
		return {
			"ok": false, "poise_gain": 0, "lines": [],
			"error": "防御姿态上不去：%s" % ("这个姿态是唯一的，已经有一层了" if why == "unique" else why),
		}
	var before_poise := int(actor.poise)
	var gain := maxi(1, int(ceil(float(actor.max_poise()) * DEFEND_POISE_GAIN)))
	actor.poise = mini(actor.max_poise(), actor.poise + gain)
	_pending_first_next_round[str(actor.actor_id)] = true
	_log.append("R%d %s 进入防御姿态（本回合减伤、下回合先手，架势 +%d）" % [
		_rounds, actor.display_name, int(actor.poise) - before_poise,
	])
	_events.append({
		"kind": "buff_gain", "source": str(actor.actor_id), "target": str(actor.actor_id),
		"buff_id": DEFEND_BUFF_ID, "damage": 0,
	})
	_spend_action(actor)
	return {
		"ok": true, "error": "", "lines": _log.slice(from_index),
		"poise_gain": int(actor.poise) - before_poise,
	}


## 已装内功 → 它能催动出的运功 buff（界面按这个列表排「内功」指令的按钮）。
## 返回 [{skill_id, name, slot_cost, buff_id, buff_name, qi_cost, ok, reason}]
func passive_cast_options(actor) -> Array:
	var out: Array = []
	if actor == null:
		return out
	var service = BuffServiceScript.new(_db)
	for skill_id: String in Array(actor.passives):
		var base: Resource = _db.get_row("skill_base", skill_id)
		var passive: Resource = _db.get_row("skill_passive", skill_id)
		var grants: Array = service.grants_for("skill_passive", skill_id, "on_cast")
		var buff_id := str(grants[0].buff_id) if grants.size() > 0 else ""
		out.append({
			"skill_id": skill_id,
			"name": str(base.name_cn) if base != null else skill_id,
			"slot_cost": int(passive.slot_cost) if passive != null else 0,
			"buff_id": buff_id,
			"buff_name": _buff_name(buff_id) if not buff_id.is_empty() else "",
			"qi_cost": 0,
			"ok": passive_cast_block_reason(actor, skill_id).is_empty(),
			"reason": passive_cast_block_reason(actor, skill_id),
		})
	return out


## 内功催动：现在能不能催动；能用返回空串。
## 内力消耗：`skill_passive` **没有 qi_cost 列**（设计原文只写「一次行动＋内力」），
## 所以现在按 0 收，并把这条缺口写进交接表；界面按钮上如实标注「内力消耗列未配」。
func passive_cast_block_reason(actor, skill_id: String) -> String:
	if actor == null or not actor.is_alive():
		return "这个单位不能行动"
	if not Array(actor.passives).has(skill_id):
		return "这部内功没有装配"
	var service = BuffServiceScript.new(_db)
	if service.grants_for("skill_passive", skill_id, "on_cast").is_empty():
		return "这部内功没有运功 buff（表里没有 on_cast 的发放）"
	if _escaped or finished():
		return "战斗已经结束了"
	return ""


## 返回 {ok, error, lines, buff_id}
func cast_passive(actor, skill_id: String) -> Dictionary:
	var reason := passive_cast_block_reason(actor, skill_id)
	if not reason.is_empty():
		return {"ok": false, "error": reason, "lines": [], "buff_id": ""}
	var from_index := _log.size()
	var service = BuffServiceScript.new(_db)
	var applied := PackedStringArray()
	for grant: Resource in service.grants_for("skill_passive", skill_id, "on_cast"):
		var result: Dictionary = actor.add_buff(
			str(grant.buff_id), "skill_passive", skill_id, maxi(1, int(grant.stacks))
		)
		if bool(result.get("applied", false)) or str(result.get("reason", "")) == "unique":
			applied.append(str(result["name"]))
	_log.append("R%d %s 催动 %s：%s" % [
		_rounds, actor.display_name, skill_display_name(skill_id), "、".join(applied),
	])
	_events.append({
		"kind": "buff_gain", "source": str(actor.actor_id), "target": str(actor.actor_id),
		"buff_id": skill_id, "damage": 0,
	})
	_spend_action(actor)
	return {
		"ok": true, "error": "", "lines": _log.slice(from_index),
		"buff_id": str(service.grants_for("skill_passive", skill_id, "on_cast")[0].buff_id),
	}


## 道具（六指令之一）：`item_base` 里 `use_context=battle` 的道具。
## **效果数值列还没有**——设计把效果写在 `desc` 文字里（例：「战斗中恢复气血60」）。
## 所以这里只把指令与「为什么现在用不了」如实列出来：不解析中文、也不自己编数值。
func battle_item_options(inventory) -> Array:
	var out: Array = []
	for row: Resource in _db.rows("item_base"):
		if str(row.use_context) != "battle":
			continue
		out.append({
			"item_id": str(row.item_id),
			"name": str(row.name_cn),
			"qty": int(inventory.count(str(row.item_id))) if inventory != null else 0,
			"desc": str(row.desc),
			"usable": false,
			# 玩家可见：**不许出现表名／列名**（AGENTS 硬规矩）。设计还没给效果数值列，如实说一句人话。
			"reason": "效果数值还没配（等设计补）",
		})
	return out


## 增益招式（「醉里乾坤·醉步」就是这一档）：没有伤害类型与倍率，靠 `on_cast` 给自己发 buff。
## **能不能用由表决定**——判定收敛到 `BuffService.has_cast_grant()`，不在界面或模拟器里另写一套。
func _is_support_skill(skill_id: String) -> bool:
	if _db == null:
		return false
	return _buff_service().has_cast_grant(skill_id)


## 模拟器自己留一份 BuffService（内部按来源分桶 + 按 buff 缓存，别在循环里反复 new）
func _buff_service():
	if _buff_service_cache == null:
		_buff_service_cache = BuffServiceScript.new(_db)
	return _buff_service_cache


## 招式自带的增益（08：`skill_active` 的 `on_cast` 发放——「招式的增益效果＝施放时发的 buff」）。
##
## 与「命中触发」（`on_hit`，见 `_apply_hit_grants`）分开：**施放**一次只发一次（不管中没中、
## 也不管打到几个人），命中触发按命中段数各发一次。
## buff 上不去时**不静默**：把原因写进战报（表里给招式配了一条不存在的 buff 时，这条就是唯一的线索）。
func _apply_cast_grants(actor, skill: Resource) -> void:
	if actor == null or skill == null or _db == null:
		return
	var skill_id := str(skill.skill_id)
	if skill_id.is_empty():
		return
	var service = BuffServiceScript.new(_db)
	for grant: Resource in service.grants_for("skill_active", skill_id, "on_cast"):
		var result: Dictionary = actor.add_buff(
			str(grant.buff_id), "skill_active", skill_id, maxi(1, int(grant.stacks))
		)
		if bool(result.get("applied", false)):
			_log.append("%s 的 %s 附带 %s（%s）" % [
				actor.display_name, _skill_name(skill), str(result["name"]), _duration_text(result),
			])
			_events.append({
				"kind": "buff_gain", "source": str(actor.actor_id), "target": str(actor.actor_id),
				"buff_id": str(result["buff_id"]), "damage": 0,
			})
			continue
		if str(result.get("reason", "")) == "unique":
			continue      # 已经有了（unique 不重复获得）——这是正常情况，不刷屏
		_log.append("%s 的 %s 附带 %s 没上上去：%s" % [
			actor.display_name, _skill_name(skill), str(grant.buff_id), str(result.get("reason", "")),
		])
		_events.append({
			"kind": "buff_failed", "source": str(actor.actor_id), "target": str(actor.actor_id),
			"buff_id": str(grant.buff_id), "damage": 0,
		})


## 一次行动的收尾：轮到下一位；本回合跑完就结算回合末
func _spend_action(_actor) -> void:
	_order_index += 1
	if not _order.is_empty() and current_actor() == null:
		_finish_round()


## 一次行动的期望总伤害权重（不含随机与实际防御，只用来排序）
func _skill_score(actor, active: Resource) -> float:
	# 策略只改我方的选招；敌人永远走均衡，免得玩家换个策略就把敌人行为也改了
	if actor.side == BattleActorScript.SIDE_ALLY:
		match _strategy:
			STRATEGY_ALL_OUT:
				return float(active.power_ratio)
			STRATEGY_CONSERVATIVE:
				# 主序：内力消耗越低越好；同消耗再看倍率（越低越省）
				return -float(active.qi_cost) * 1000.0 - float(active.power_ratio)
	var targets := 1
	if str(active.target_type) == "all_enemy":
		targets = 0
		var opposing: Array = _enemies if actor.side == BattleActorScript.SIDE_ALLY else _allies
		for candidate in opposing:
			if candidate.is_alive():
				targets += 1
		targets = maxi(1, targets)
	return float(active.power_ratio) * float(maxi(1, int(active.hit_count))) * float(targets)


## 当前自动战斗策略（只影响我方）
func strategy() -> String:
	return _strategy


func strategy_label() -> String:
	return str(STRATEGY_LABELS.get(_strategy, _strategy))


## 换策略；不认识的 id 返回 false 且不改现状
func set_strategy(strategy_id: String) -> bool:
	if not STRATEGY_ORDER.has(strategy_id):
		return false
	_strategy = strategy_id
	return true


## 按「保守 → 均衡 → 全力 → 保守」轮换（界面按钮用），返回新策略 id
func cycle_strategy() -> String:
	var index := STRATEGY_ORDER.find(_strategy)
	_strategy = str(STRATEGY_ORDER[(index + 1) % STRATEGY_ORDER.size()])
	return _strategy


## 伤害类型是否被当前管线支持。**只有反伤（`dmg_reflect`）不支持**：`resolve()` 会显式拒绝它；
## DoT 类（`category=dot`）走的是同一份 resolve 的 dot 分支 + `resolve_dot()` 快照，是支持的。
##
## **这份判断的唯一出处是 `is_supported_damage_type()`（静态）**：构建期校验器也用它，
## 所以「表里给招式配了一个代码不认的伤害类型」会在构建期就报出来（那招会永远没有按钮，见决策 229）。
func _is_supported_damage(type_id: String) -> bool:
	return is_supported_damage_type(_db, type_id)


static func is_supported_damage_type(db, type_id: String) -> bool:
	var row: Resource = db.get_row("damage_type", type_id) if db != null else null
	if row == null:
		return false
	# **DoT 是支持的**：`DamageResolver.resolve()` 对 `category=dot` 走「这一下只做命中判定 + 挂层」
	# 那条分支（每层伤害由 `resolve_dot()` 在施加那一刻算好锁死），回合末按 status_effect 结算。
	# 以前这里把 dot 一律当「没接」挡掉，于是**五毒掌／烈火掌那 6 招永远没有按钮**、自动战斗也不选它——
	# 而用例是直接 `sim.act()` 放招的，绕过了这道过滤，所以一直没人发现（见框架说明决策 228）。
	# 真正没实现的只有反伤（`dmg_reflect`，`resolve()` 会显式拒绝）。
	return type_id != "dmg_reflect"


## 招式因为伤害类型而用不了时给玩家看的那句话。
## 说中文名，别把表里的英文 id（dot_poison）甩到战斗界面上——玩家看不懂那是什么。
func _unsupported_damage_reason(type_id: String) -> String:
	var row: Resource = _db.get_row("damage_type", type_id)
	if row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[BattleSimulator] damage_type 缺少 %s" % type_id)
		return "这一招的伤害类型没配（数据错，已记进日志）"
	var name_cn := str(row.name_cn)
	return "%s 还没接结算" % name_cn


## 招式绑定的武器类型要跟身上的武器对上。
## 只有带 weapon_type 标记的单位（玩家队伍）才受这条限制，敌人沿用队伍配置的招式。
func _weapon_allows(actor, base: Resource) -> bool:
	if base.accepts_any_weapon():
		return true
	if not actor.tags.has("weapon_type"):
		return true
	var weapon := str(actor.tags.get("weapon_type", ""))
	if weapon.is_empty():
		return false
	return str(base.weapon_type) == weapon


func _skill_name(active_row: Resource) -> String:
	if str(active_row.skill_id) == BASIC_ATTACK_ID:
		return "普通攻击"
	var base: Resource = _db.get_row("skill_base", str(active_row.skill_id))
	return str(base.name_cn) if base != null else str(active_row.skill_id)


## 按 skill_id 取中文名（预兆与拆招播报用；界面也读这个）
func skill_display_name(skill_id: String) -> String:
	var base: Resource = _db.get_row("skill_base", skill_id)
	return str(base.name_cn) if base != null else skill_id


## 目标选择：对面血量最低的活人；群攻招式返回全部活人。
func _pick_targets(actor, allies: Array, enemies: Array) -> Array:
	var opposing: Array = enemies if actor.side == BattleActorScript.SIDE_ALLY else allies
	var living: Array = []
	for target in opposing:
		if target.is_alive():
			living.append(target)
	if living.is_empty():
		return []
	var skill := _skill_for(actor)
	if skill != null:
		# `self`：打自己（反噬类）——自动战斗也必须遵守，否则它会去打敌人（06 的三种 target_type 之一）
		if str(skill.target_type) == "self":
			return [actor]
		if str(skill.target_type) == "all_enemy":
			return living
	var best = living[0]
	for target in living:
		if target.hp < best.hp:
			best = target
	return [best]


func _rewards(enemies: Array) -> Dictionary:
	var exp := 0
	var money := 0
	for enemy in enemies:
		if enemy.is_alive():
			# 没打死的敌人不算奖励
			continue
		exp += int(enemy.reward_exp)
		money += int(enemy.reward_money)
	return {"exp": exp, "money": money}


func _has_living(actors: Array) -> bool:
	for actor in actors:
		if actor.is_alive():
			return true
	return false
