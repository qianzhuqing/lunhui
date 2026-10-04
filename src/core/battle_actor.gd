## 战斗单位：玩家角色与敌人都用同一套快照结构。
##
## 它只保存「已算好的属性快照 + 当前资源」，不含 AI、不含场景节点，
## 这样战斗能在 headless 下整场跑完，也方便以后接 UI。
class_name BattleActor
extends RefCounted

const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const BuffServiceScript := preload("res://src/core/buff_service.gd")

const SIDE_ALLY := 0
const SIDE_ENEMY := 1

## 只能由装备或临时效果提供的派生数值：没有来源时补 0，
## 保证伤害管线取得到（来源在 equip_base 的 bonus_* 列）。
const EQUIPMENT_ONLY_STATS := ["pen_rate", "block_rate", "block_reduction", "dmg_reduction"]

## 战斗内的唯一标识（同名敌人加序号）。
var actor_id: String = ""
## 来源 id：角色为 character_base.char_id，敌人为 enemy_base.enemy_id。
var source_id: String = ""
var display_name: String = ""
var side: int = SIDE_ALLY
var level: int = 1

## stat_def.stat_id → 最终数值。
var stats: Dictionary = {}
## 异常状态抗性（敌人专属，键为 poison/burn/bleed/internal）。
var resistances: Dictionary = {}
## 附带标记：faction / threat_tag / ai_template 等，供上层筛选与表现用。
var tags: Dictionary = {}

var skills: PackedStringArray = PackedStringArray()
## 已装配的内功（08 的「内功」指令按这个列表逐个催动）。
var passives: PackedStringArray = PackedStringArray()
## 进场时要发的常驻／整场 buff（由 PartyBuilder 按存档算好：装备 on_battle_start、战斗外增益…）。
## 每条 {buff_id, source_type, source_id, stacks}；`BattleSimulator.setup()` 开打时逐条施加。
var pending_grants: Array = []
## 命中时触发的装备特效（`buff_grant.trigger=on_hit`），由模拟器在命中后施加。
var on_hit_grants: Array = []
var hp: int = 0
var qi: int = 0

## 命中基准。
## 文档没给命中公式，这里按两张表的语义各自落地：
##   玩家角色：敏换算出的 hit_rate 是「相对基准的加成」，基准 1.0
##   敌人：enemy_base.hit_rate 直接就是命中率，基准 0.0
## 命中率 = clamp(base_accuracy + hit_rate - 目标 dodge_rate, 5%, 99%)。
var base_accuracy: float = 1.0

## 击杀奖励与掉落组（敌人用；角色为 0 / 空）。
var reward_exp: int = 0
var reward_money: int = 0
var drop_group: String = ""

## 武学熟练度：skill_id → 等级（0 表示没练过，倍率系数按 0 算）
var skill_mastery: Dictionary = {}

## 天赋里 `rule:` 那几条（设计 12 §五）：`{rule_id: 合计值}`，由 `PartyBuilder` 从存档算好。
## **谁消费谁读**（先手在回合顺序里读、招式伤害在伤害管线里读），这里只当一条快照。
## 敌人没有天赋 → 空字典，读不到就是 0。
var talent_rules: Dictionary = {}

## 异常状态（DoT）。按 04_战斗与伤害.md 的持续伤害规则：
##   同源同类叠层、刷新时长（键 = status_id + source_id，不同来源独立计算）；
##   每层伤害在**施加那一刻锁定**（快照制，增益掉光也照原值跳）。
## 结构：[{status_id, source_id, rule, stacks: [每层伤害], remaining}]
var statuses: Array = []

## 战斗内 buff（08）：常驻／整场／限时都在这里，和异常状态共用一套回合末结算。
## 结构：[{buff_id, source_type, source_id, remaining, stacks}]
##   remaining > 0 剩余回合 / 0 常驻（随来源存在）/ -1 整场（战斗结束才清）
var buffs: Array = []
## -- 属性重算输入：只有玩家角色有（敌人是 EnemyFactory 直接给的快照） --
var _db = null
var _calculator = null
var _buff_service_cache = null
var _base_attrs: Dictionary = {}
var _allocations: Dictionary = {}
var _base_contributions: Array = []
## 敌人没有属性链：buff 的 `stat:` 修正直接叠在这份快照上（`attr:` 类如实记进 unhandled_buffs）
var _stats_baseline: Dictionary = {}

## 表里没给数值／规则、代码也没实现的特判 buff：**不静默**，界面按「暂缓规则」播报。
## 每条 {buff_id, name, reason}
var unhandled_buffs: Array = []

## 灼伤每层降低的外功防御比例。表里只有 desc 文字（「每层降低目标5%外功防御」），
## 没有独立数值列，先按 desc 写成常量并回报设计补列。
const BURN_DEF_DOWN_PER_STACK := 0.05
## 内伤「内力回复减半」
const INTERNAL_QI_REGEN_FACTOR := 0.5

## 架势（破防资源）。
## 敌人取自 enemy_base.poise；玩家没有对应列，先按「40 + 8×等级」给一条基线
## （记在交接表里请设计补一列，比如 character_base.poise 或 stat_def.poise_max）。
const PLAYER_POISE_BASE := 40.0
const PLAYER_POISE_PER_LEVEL := 8.0
## 回合末回复比例；Boss「架势回复极快」，按 30% 处理（同样等表里补 poise_regen）
const POISE_REGEN_DEFAULT := 0.1
const POISE_REGEN_BOSS := 0.3

var poise: int = 0
var poise_max: int = 0
## 破绽窗口：架势被打空后进入，持续 1 回合
var broken: bool = false
var broken_rounds_left: int = 0
var poise_regen_ratio: float = POISE_REGEN_DEFAULT


func max_poise() -> int:
	return poise_max


func is_broken() -> bool:
	return broken


## 扣架势，返回实际扣掉的量；扣到 0 就进入破绽
func take_poise_damage(amount: int) -> int:
	var lost := mini(maxi(amount, 0), poise)
	poise -= lost
	if poise <= 0 and not broken:
		poise = 0
		broken = true
		broken_rounds_left = 1
	return lost


## 回合末：破绽窗口递减（结束时架势重置），否则按比例回复
func end_of_round_poise() -> void:
	if not is_alive():
		return
	if broken:
		broken_rounds_left -= 1
		if broken_rounds_left <= 0:
			broken = false
			poise = poise_max      # 设计：破绽结束后架势重置
		return
	poise = mini(poise_max, poise + int(float(poise_max) * poise_regen_ratio))


## 背袭等效果：降低初始架势（比例来自 combat_const.backstab_poise_reduce）
func reduce_poise_ratio(ratio: float) -> void:
	poise = maxi(1, int(round(float(poise_max) * (1.0 - clampf(ratio, 0.0, 1.0)))))


func mastery_of(skill_id: String) -> int:
	return int(skill_mastery.get(skill_id, 0))


# ------------------------------------------------------------------ 异常状态

## 代码目前认得的 `status_effect.extra_rule`（其余值一律记进 `unhandled_rules` 报出去）。
## 静默忽略等于「配了但没生效」：`bleed` 表里写着 `rule_bleed_move`（流血换位），
## 而它依赖站位、设计还没定规则——那就必须像奇袭的「无防备」一样明说，不能当没看见。
const KNOWN_STATUS_RULES := ["", "rule_burn_def_down", "rule_internal_qi"]

## 本次战斗里遇到过的「不认识的状态规则」（界面会把它们播成「暂缓规则」）
var unhandled_rules: PackedStringArray = PackedStringArray()

## 设计已经声明、但代码还没实现的规则：**允许留在表里**（构建期不报错），
## 运行期会走 `unhandled_rules` → 战斗结算的「暂缓规则」播出来。
## 加进来必须写清为什么暂时不做；实现之后要从这里删掉，并把它挪进 KNOWN_STATUS_RULES。
const DECLARED_STATUS_RULES := {
	"rule_bleed_move": "设计 04 的「流血换位额外触发」依赖前/后排站位，站位规则还没定（见对照表 C 段）",
}


## 上一层状态（同源同类叠层，超出上限只刷新时长）。返回 {stacks, refreshed, damage}
func add_status(
	status_id: String, source_id: String, damage_per_stack: float,
	max_stack: int, duration: int, rule: String = "", dispellable: bool = true
) -> Dictionary:
	if not KNOWN_STATUS_RULES.has(rule) and not unhandled_rules.has(rule):
		unhandled_rules.append(rule)
	var entry := _status_entry(status_id, source_id)
	if entry.is_empty():
		entry = {
			"status_id": status_id, "source_id": source_id, "rule": rule,
			"dispellable": dispellable,
			"stacks": [], "remaining": 0,
		}
		statuses.append(entry)
	var stacks: Array = entry["stacks"]
	var refreshed := stacks.size() >= maxi(1, max_stack)
	if not refreshed:
		stacks.append(damage_per_stack)
	entry["stacks"] = stacks
	entry["remaining"] = maxi(int(entry.get("remaining", 0)), maxi(1, duration))
	return {"stacks": stacks.size(), "refreshed": refreshed, "damage": damage_per_stack}


func _status_entry(status_id: String, source_id: String) -> Dictionary:
	for entry: Dictionary in statuses:
		if str(entry["status_id"]) == status_id and str(entry["source_id"]) == source_id:
			return entry
	return {}


func status_stacks(status_id: String) -> int:
	var total := 0
	for entry: Dictionary in statuses:
		if str(entry["status_id"]) == status_id:
			total += Array(entry["stacks"]).size()
	return total


func has_status(status_id: String) -> bool:
	return status_stacks(status_id) > 0


## 回合末结算：每层按锁定的伤害跳一次，持续回合 -1，到 0 的整条清掉。
## 返回 [{status_id, source_id, stacks, damage, expired}]
func tick_statuses() -> Array:
	var out: Array = []
	var alive: Array = []
	for entry: Dictionary in statuses:
		entry["remaining"] = int(entry["remaining"]) - 1
		var stacks: Array = entry["stacks"]
		var damage := 0
		for value: Variant in stacks:
			damage += int(floor(float(value)))
		out.append({
			"status_id": str(entry["status_id"]),
			"source_id": str(entry["source_id"]),
			"stacks": stacks.size(),
			"damage": damage,
			"expired": int(entry["remaining"]) <= 0,
		})
		if int(entry["remaining"]) > 0:
			alive.append(entry)
	statuses = alive
	return out


## 斩杀：把剩下的层数立刻各结算一次并清空（combat_const.dot_execute_threshold 触发）
func consume_all_statuses() -> Array:
	var out: Array = []
	for entry: Dictionary in statuses:
		var stacks: Array = entry["stacks"]
		var damage := 0
		for value: Variant in stacks:
			damage += int(floor(float(value)))
		out.append({
			"status_id": str(entry["status_id"]),
			"source_id": str(entry["source_id"]),
			"stacks": stacks.size(),
			"damage": damage,
			"expired": true,
		})
	statuses = []
	return out


## 可驱散的清掉（内伤 dispellable=0，驱散不掉）
func clear_dispellable() -> Array:
	var cleared := PackedStringArray()
	var alive: Array = []
	for entry: Dictionary in statuses:
		if not bool(entry.get("dispellable", true)):
			alive.append(entry)
		else:
			cleared.append(str(entry["status_id"]))
	statuses = alive
	return cleared


## 面板／战斗卡片用：[{status_id, name, stacks, remaining, color}]
func status_rows(db) -> Array:
	var out: Array = []
	for entry: Dictionary in statuses:
		var row: Resource = db.get_row("status_effect", str(entry["status_id"]))
		var type_row: Resource = db.get_row("damage_type", str(row.damage_type)) if row != null else null
		out.append({
			"status_id": str(entry["status_id"]),
			"name": str(row.name_cn) if row != null else str(entry["status_id"]),
			"stacks": Array(entry["stacks"]).size(),
			"remaining": int(entry["remaining"]),
			"color": str(type_row.display_color) if type_row != null else "#FFFFFF",
			# 图标**交给界面按表拼路径**（这里只递数据，不碰 UI 路径——core 不依赖界面）
			"icon": str(row.icon) if row != null else "",
		})
	return out


## 防御乘数：灼伤每层 -5% 外功防御（只影响 def_phys）
func defense_multiplier(stat_id: String) -> float:
	if stat_id != "def_phys":
		return 1.0
	var stacks := 0
	for entry: Dictionary in statuses:
		if str(entry.get("rule", "")) == "rule_burn_def_down":
			stacks += Array(entry["stacks"]).size()
	if stacks <= 0:
		return 1.0
	return maxf(0.0, 1.0 - BURN_DEF_DOWN_PER_STACK * float(stacks))


## 内力回复乘数：内伤减半
func qi_regen_factor() -> float:
	for entry: Dictionary in statuses:
		if str(entry.get("rule", "")) == "rule_internal_qi":
			return INTERNAL_QI_REGEN_FACTOR
	return 1.0


# ------------------------------------------------------------------ buff（08 增益与套装）
#
# 结算分工（单一实现，别在别处再写一套）：
#   · **常驻** buff（装备 `on_equip`、套装档位，`duration=0`）在构造角色时就进了贡献列表
#     （`BuffService.contributions_of` ← `Inventory` / `CharacterSheet`），这里不需要再叠一次。
#   · **临时** buff（正回合数 / 整场 / 触发类）走这个列表，属性在变化时整份重算。

func buff_service():
	if _db == null:
		return null
	if _buff_service_cache == null:
		_buff_service_cache = BuffServiceScript.new(_db)
	return _buff_service_cache


## 构造时交给它「重算属性所需的输入」：等级、基础五维、加点、贡献列表。
## 没有调过这个函数的单位（敌人）走快照叠加那条兜底路径。
func setup_recompute(
	db, calculator, level_value: int, base_attrs: Dictionary,
	allocations: Dictionary, contributions: Array
) -> void:
	_db = db
	_calculator = calculator
	level = level_value
	_base_attrs = base_attrs.duplicate(true)
	_allocations = allocations.duplicate(true)
	_base_contributions = contributions.duplicate(true)


func has_buff(buff_id: String) -> bool:
	for entry: Dictionary in buffs:
		if str(entry["buff_id"]) == buff_id:
			return true
	return false


func buff_stacks(buff_id: String) -> int:
	var total := 0
	for entry: Dictionary in buffs:
		if str(entry["buff_id"]) == buff_id:
			total += int(entry["stacks"])
	return total


## 剩余回合：>0 回合数 / 0 常驻 / -1 整场
func buff_remaining(buff_id: String) -> int:
	for entry: Dictionary in buffs:
		if str(entry["buff_id"]) == buff_id:
			return int(entry["remaining"])
	return 0


func _buff_entry(buff_id: String, source_type: String, source_id: String) -> Dictionary:
	for entry: Dictionary in buffs:
		if str(entry["buff_id"]) == buff_id \
				and str(entry["source_type"]) == source_type \
				and str(entry["source_id"]) == source_id:
			return entry
	return {}


func _note_unhandled_buff(buff_id: String, reason: String) -> void:
	for entry: Dictionary in unhandled_buffs:
		if str(entry["buff_id"]) == buff_id:
			return
	var row: Resource = _db.get_row("buff_def", buff_id) if _db != null else null
	unhandled_buffs.append({
		"buff_id": buff_id,
		"name": str(row.name_cn) if row != null else buff_id,
		"reason": reason,
	})


## 施加一个 buff。叠加规则按 `buff_def.stack_rule`：
##   unique 全局唯一（重复无效）／refresh 只刷新剩余回合／stack 叠层到 max_stack。
## 返回 {applied, reason, buff_id, name, stacks, refreshed, remaining}
func add_buff(
	buff_id: String, source_type: String = "", source_id: String = "", grant_stacks: int = 1
) -> Dictionary:
	var service = buff_service()
	if service == null:
		return {"applied": false, "reason": "这个单位没有接配置表（buff 需要 db）", "buff_id": buff_id}
	if not service.exists(buff_id):
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[BattleActor] buff_def 里没有 %s" % buff_id)
		return {"applied": false, "reason": "这条增益的配置对不上（数据错，已记进日志）", "buff_id": buff_id}
	var rule: String = service.stack_rule_of(buff_id)
	var cap := maxi(1, service.max_stack_of(buff_id))
	var add := maxi(1, grant_stacks)
	if rule == BuffServiceScript.STACK_UNIQUE:
		for existing: Dictionary in buffs:
			if str(existing["buff_id"]) == buff_id:
				return {
					"applied": false, "reason": "unique", "buff_id": buff_id,
					"name": _buff_name(buff_id), "stacks": int(existing["stacks"]),
				}
	var entry := _buff_entry(buff_id, source_type, source_id)
	var is_new := entry.is_empty()
	if is_new:
		entry = {
			"buff_id": buff_id, "source_type": source_type, "source_id": source_id,
			# 从 0 起：下面按叠加规则统一加（**不能写 1**——叠层类会变成「第一次就给 2 层」，
			# 这个 bug 是 2026-10-03 写「招式附带增益」用例时抓到的，见框架说明决策 220）
			"remaining": service.duration_of(buff_id), "stacks": 0,
		}
		buffs.append(entry)
	var before := int(entry["stacks"])
	if rule == BuffServiceScript.STACK_STACK:
		entry["stacks"] = mini(cap, before + add)
	else:
		entry["stacks"] = mini(cap, maxi(1, add if is_new else before))
	var duration: int = service.duration_of(buff_id)
	if duration > 0:
		entry["remaining"] = duration     # refresh：刷新剩余回合
	elif is_new:
		entry["remaining"] = duration     # 0 常驻 / -1 整场
	if service.effect_kind_of(buff_id) == BuffServiceScript.EFFECT_KIND_SPECIAL:
		_note_unhandled_buff(buff_id, "特判效果（effect_kind=special）还没有代码实现，只记层数")
	_rebuild_stats()
	return {
		"applied": true, "reason": "", "buff_id": buff_id, "name": _buff_name(buff_id),
		"stacks": int(entry["stacks"]), "refreshed": not is_new, "remaining": int(entry["remaining"]),
	}


## 回合末：`battle` 作用域的限时 buff 各 −1，归零即失效；常驻与整场不动。
## 返回失效的条目（给战报播报）。
func tick_buffs() -> Array:
	var expired: Array = []
	var alive: Array = []
	var changed := false
	for entry: Dictionary in buffs:
		var remaining := int(entry["remaining"])
		if remaining > 0:
			entry["remaining"] = remaining - 1
			changed = true
			if int(entry["remaining"]) <= 0:
				expired.append(entry.duplicate(true))
				continue
		alive.append(entry)
	buffs = alive
	if changed:
		_rebuild_stats()
	return expired


## 战斗结束：清掉所有战斗内 buff（`field` 作用域的**留在存档里**，由会话层管）。
## 返回被清掉的 buff_id 列表。
func clear_battle_buffs() -> PackedStringArray:
	var cleared := PackedStringArray()
	var alive: Array = []
	for entry: Dictionary in buffs:
		if str(entry["source_type"]) == "field":
			alive.append(entry)
			continue
		cleared.append(str(entry["buff_id"]))
	buffs = alive
	if cleared.size() > 0:
		_rebuild_stats()
	return cleared


## 面板用：[{buff_id, name, stacks, remaining, permanent, is_debuff, kind}]，
## 与 `status_rows()` 同形状（`kind` 区分两套表）——**合并展示**由 `effect_rows()` 负责。
func buff_rows(db) -> Array:
	var service = buff_service()
	var out: Array = []
	for entry: Dictionary in buffs:
		var buff_id := str(entry["buff_id"])
		var row: Resource = db.get_row("buff_def", buff_id)
		out.append({
			"buff_id": buff_id,
			"entry_id": buff_id,
			"name": str(row.name_cn) if row != null else buff_id,
			"stacks": int(entry["stacks"]),
			"remaining": int(entry["remaining"]),
			"permanent": int(entry["remaining"]) == 0,
			"is_debuff": bool(row.is_debuff) if row != null else false,
			"desc": str(row.desc) if row != null else "",
			"icon": str(row.icon) if row != null else "",
			"kind": "buff",
		})
	# 悬浮说明里带上数值修正的中文名，玩家才知道这条 buff 到底给了什么
	for item: Dictionary in out:
		var mods: Array = service.stat_mods_of(str(item["buff_id"])) if service != null else []
		if mods.size() > 0:
			item["desc"] = "%s（%s）" % [str(item["desc"]), _describe_stat_mods(db, mods)]
	return sort_effect_rows(out)


## 08 的 UI 要求：`status_effect` 与 `buff_def` **合并成一条列表**，玩家看不出这是两套表。
## 排序固定：减益在前，其余按剩余回合升序（快过期的在前），常驻最后。
func effect_rows(db) -> Array:
	var out: Array = []
	for row: Dictionary in status_rows(db):
		row["kind"] = "status"
		row["entry_id"] = str(row["status_id"])     # 与 buff 行统一一个键，界面不用分两套取
		row["is_debuff"] = true          # 异常状态全是减益
		row["permanent"] = false
		row["desc"] = row.get("desc", "")
		out.append(row)
	out.append_array(buff_rows(db))
	return sort_effect_rows(out)


## 固定排序（08）：**减益在前**，同极性按剩余回合升序（快过期的在前），常驻／整场排末尾。
## 公开给界面用：战斗面板要把**所有单位**的条目合成一条列表再排一次，不能各排各的
## （各排各的 = 敌方的减益会被我方增益压在下面）。
static func sort_effect_rows(rows: Array) -> Array:
	var indexed: Array = []
	for index in range(rows.size()):
		indexed.append({"index": index, "row": rows[index]})
	indexed.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var ra: Dictionary = a["row"]
		var rb: Dictionary = b["row"]
		var da := 0 if bool(ra.get("is_debuff", false)) else 1
		var db_ := 0 if bool(rb.get("is_debuff", false)) else 1
		if da != db_:
			return da < db_
		var ka := _remaining_sort_key(ra)
		var kb := _remaining_sort_key(rb)
		if ka != kb:
			return ka < kb
		return int(a["index"]) < int(b["index"])
	)
	var out: Array = []
	for entry: Dictionary in indexed:
		out.append(entry["row"])
	return out


static func _remaining_sort_key(row: Dictionary) -> int:
	var remaining := int(row.get("remaining", 0))
	return remaining if remaining > 0 else 9999     # 常驻／整场排在同极性末尾


static func _describe_stat_mods(db, mods: Array) -> String:
	var parts := PackedStringArray()
	for mod: Dictionary in mods:
		var target_id := str(mod["target_id"])
		var name := target_id
		if str(mod["kind"]) == "attr":
			var attr: Resource = db.get_row("attribute_def", target_id)
			if attr != null:
				name = str(attr.name_cn)
		else:
			var stat: Resource = db.get_row("stat_def", target_id)
			if stat != null:
				name = str(stat.name_cn)
		var value := float(mod["value"])
		var sign_text := "+" if value >= 0.0 else ""
		var value_text := str(int(value)) if is_equal_approx(value, round(value)) else str(value)
		parts.append("%s %s%s" % [name, sign_text, value_text])
	return "，".join(parts)


## 属性重算：把常驻贡献 + 当前 buff 的临时贡献合起来重算一份快照。
func _rebuild_stats() -> void:
	var hp_before := hp
	var qi_before := qi
	if _calculator != null:
		stats = _calculator.compute(level, _base_attrs, _allocations, _runtime_contributions())
	elif not _stats_baseline.is_empty():
		# 敌人：没有基础五维，`attr:` 类修正没法走属性链——直接在快照上叠 `stat:`，
		# 遇到属性点类修正如实记进 `unhandled_buffs`（不静默丢）
		stats = _stats_baseline.duplicate(true)
		var service = buff_service()
		if service != null:
			for entry: Dictionary in buffs:
				var buff_id := str(entry["buff_id"])
				var stacks := int(entry["stacks"])
				for mod: Dictionary in service.stat_mods_of(buff_id):
					if str(mod["kind"]) == "attr":
						_note_unhandled_buff(buff_id, "属性点类修正需要属性链，敌人没有基础五维，暂不结算")
						continue
					var stat_id := str(mod["target_id"])
					stats[stat_id] = float(stats.get(stat_id, 0.0)) + float(mod["value"]) * float(stacks)
	for stat_id: String in EQUIPMENT_ONLY_STATS:
		if not stats.has(stat_id):
			stats[stat_id] = 0.0
	# 上限变了不补血／不补内力（临时上限不该当治疗），只把超出的部分夹掉
	hp = clampi(hp_before, 0, max_hp())
	qi = clampi(qi_before, 0, max_qi())


## 常驻贡献 + buffer 修正（computed 类＝把来源那份贡献再来一遍）
func _runtime_contributions() -> Array:
	var out: Array = _base_contributions.duplicate(true)
	var service = buff_service()
	if service == null:
		return out
	for entry: Dictionary in buffs:
		var buff_id := str(entry["buff_id"])
		var stacks := maxi(1, int(entry["stacks"]))
		if service.effect_kind_of(buff_id) == BuffServiceScript.EFFECT_KIND_COMPUTED:
			# 通用运功（06 原文）：该内功的常驻加成临时翻倍——值不写在表里，算出来
			for contribution: Dictionary in _base_contributions:
				if str(contribution.get("source", "")) == str(entry["source_id"]):
					out.append(contribution.duplicate(true))
			continue
		out.append_array(service.contributions_of(buff_id, "buff:%s" % buff_id, stacks))
	return out


func _buff_name(buff_id: String) -> String:
	var row: Resource = _db.get_row("buff_def", buff_id) if _db != null else null
	return str(row.name_cn) if row != null else buff_id


## 用玩家角色表造一个战斗单位。
static func from_character(
	db,
	char_row: Resource,
	level: int = 0,
	allocations: Dictionary = {},
	contributions: Array = []
) -> BattleActor:
	var calculator = AttributeCalculatorScript.new(db)
	var effective_level: int = level if level > 0 else maxi(1, int(char_row.start_level))
	var computed: Dictionary = calculator.compute(
		effective_level,
		char_row.initial_attrs(),
		allocations,
		contributions
	)
	var actor := BattleActor.new()
	# 常驻 buff 的贡献在贡献列表里（见 buff 一节），这里存一份 db 与快照基线：
	# 让「直接造一个 BattleActor」的调用方（用例／工具）也能加临时 buff
	actor._db = db
	actor._stats_baseline = computed.duplicate(true)
	actor.actor_id = char_row.char_id
	actor.source_id = char_row.char_id
	actor.display_name = char_row.name_cn
	actor.side = SIDE_ALLY
	actor.level = effective_level
	actor.stats = computed
	for stat_id: String in EQUIPMENT_ONLY_STATS:
		if not actor.stats.has(stat_id):
			actor.stats[stat_id] = 0.0
	actor.skills = char_row.skill_ids()
	actor.tags = {"role_tag": char_row.role_tag, "faction": "party"}
	actor.base_accuracy = 1.0
	actor.poise_max = int(PLAYER_POISE_BASE + PLAYER_POISE_PER_LEVEL * float(effective_level))
	actor.refill()
	return actor


## 用算好的数值造一个敌人（缩放由 EnemyFactory 负责）。
static func from_enemy(
	enemy_row: Resource,
	stats: Dictionary,
	skills: PackedStringArray,
	level: int = 0,
	db = null
) -> BattleActor:
	var actor := BattleActor.new()
	# 敌人也要能接 buff（08：招式的 on_cast／命中触发对双方一视同仁）。
	# 以前这里没有 db，`add_buff` 会直接返回「这个单位没有接配置表」——那是一条**静默的**失败路径：
	# 设计一旦给某个敌人招式配 on_cast，效果会凭空消失（见框架说明决策 220）。
	if db != null:
		actor._db = db
	actor.actor_id = enemy_row.enemy_id
	actor.source_id = enemy_row.enemy_id
	actor.display_name = enemy_row.name_cn
	actor.side = SIDE_ENEMY
	actor.level = level if level > 0 else int(enemy_row.level)
	actor.stats = stats
	actor._stats_baseline = stats.duplicate(true)
	actor.skills = skills
	actor.resistances = enemy_row.resistances()
	# 命中基准敌我统一成 1.0（设计 10 §七第四条，0.28.0 答 Q57）。
	#
	# 以前敌人是 0.0、把 `enemy_base.hit_rate` 当**绝对**命中率用；七维改造之后
	# `hit_rate` 是 `attr_to_stat` 用 diminishing 曲线从**敏**算出来的加成
	# （cap 0.95／param 80）——敏 24 只给 0.22，敌人若还留着 0 基准就成了「十刀九空」，
	# 而且这条在数值表里完全看不出来。真正决定打不打得中的是**对方的闪避**
	# （10 §五：「想让敌人血厚防薄，用敏表达」）。
	actor.base_accuracy = 1.0
	actor.poise_max = maxi(1, int(enemy_row.poise))
	actor.poise_regen_ratio = POISE_REGEN_BOSS if str(enemy_row.ai_template) == "ai_boss" else POISE_REGEN_DEFAULT
	actor.tags = {
		"faction": enemy_row.faction,
		"threat_tag": enemy_row.threat_tag,
		"ai_template": enemy_row.ai_template,
	}
	actor.drop_group = enemy_row.drop_group
	actor.refill()
	return actor


## 取派生数值，取不到返回 fallback。
func stat(stat_id: String, fallback: float = 0.0) -> float:
	return float(stats.get(stat_id, fallback))


## 异常／持续伤害抗性：**角色与敌人共用的派生数值** `stat_def.res_<status_id>`
## （`poison`／`burn`／`bleed`／`internal`，见 status_effect.csv 的四个 id）。
##
## 设计 0.12.0：抗性不再是敌人模板的特权列——玩家练了五毒心法，`skill_passive_stat` 的
## `stat:res_poison` 就会经贡献通道算进派生数值，这里读到的和敌人读的是同一条。
## `resistances`（敌人行的快照）只作为**派生数值缺失时的兜底**（用例里手搓的战斗单位会走它）。
func resistance_of(status_id: String) -> float:
	var stat_id := "res_%s" % status_id
	if stats.has(stat_id):
		return float(stats[stat_id])
	return float(resistances.get(status_id, 0.0))


func max_hp() -> int:
	return maxi(0, int(stat("hp_max")))


func max_qi() -> int:
	return maxi(0, int(stat("qi_max")))


## 按当前数值把气血内力补满。
func refill() -> void:
	hp = max_hp()
	qi = max_qi()
	poise = poise_max
	broken = false
	broken_rounds_left = 0


func is_alive() -> bool:
	return hp > 0


func hp_ratio() -> float:
	var total := max_hp()
	if total <= 0:
		return 0.0
	return clampf(float(hp) / float(total), 0.0, 1.0)


## 扣血，返回实际扣掉的量。
func take_damage(amount: int) -> int:
	var lost := mini(maxi(amount, 0), hp)
	hp -= lost
	return lost


func heal(amount: int) -> int:
	var before := hp
	hp = mini(hp + maxi(amount, 0), max_hp())
	return hp - before


## 支付内力，不足则返回 false 且不扣。
func spend_qi(cost: int) -> bool:
	if cost <= 0:
		return true
	if qi < cost:
		return false
	qi -= cost
	return true


## 回合末回复（气血回复 / 内力回复）。
func end_of_round_regen() -> void:
	if not is_alive():
		return
	heal(int(floor(stat("hp_regen"))))
	# 内伤：内力回复减半（status_effect.extra_rule = rule_internal_qi）
	var regen := float(stat("qi_regen")) * qi_regen_factor()
	qi = mini(qi + int(floor(regen)), max_qi())


func summary() -> Dictionary:
	return {
		"actor_id": actor_id,
		"name": display_name,
		"side": side,
		"level": level,
		"hp": hp,
		"hp_max": max_hp(),
		"qi": qi,
		"poise": poise,
		"poise_max": poise_max,
		"broken": broken,
		"base_accuracy": base_accuracy,
		"speed": stat("speed"),
		"atk_phys": stat("atk_phys"),
		"atk_qi": stat("atk_qi"),
		"def_phys": stat("def_phys"),
		"def_qi": stat("def_qi"),
	}
