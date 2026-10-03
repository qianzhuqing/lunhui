## 敌人实例化：enemy_base × difficulty_config → BattleActor。
##
## 难度只改数值倍率，不改掉落清单（掉落倍率在 DropResolver 里处理）。
##
## 招式来自 `enemy_skill.csv`（0.10.0 由设计交付，见 `docs/design/06_配置表说明.md`）。
## 原来这里是两张写死的映射（`AI_SKILL_MAP` / `ENEMY_SKILL_OVERRIDE`）——Q48 问过设计后已作废：
## 「谁用哪几招」现在设计侧改表就行，不用改代码。
## 构建期 `table_validator._check_ai_templates` 仍然守着两件事：`ai_template` 必须是代码认的取值、
## 除 `ai_neutral`（不还手，如采药人）外每个敌人在 `enemy_skill` 里至少要有一条招式。
class_name EnemyFactory
extends RefCounted

const BattleActorScript := preload("res://src/core/battle_actor.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
const InventoryScript := preload("res://src/core/inventory.gd")
const BuffServiceScript := preload("res://src/core/buff_service.gd")

## 还在用旧派生列的敌人：**每个进程只报一次**（构建/自检日志里看得见，不刷屏）。
## 设计 10 §三的顺序就是「先改表结构与工厂、**跑通空数据**，再一起配数值」——
## 七维那一列空着时走这条路，配上了自动切到共用管线。
static var _derived_path_warned: bool = false

## `enemy_base.ai_template` 的合法取值：**只决定行为**（`ai_boss` → 架势回复更高、`ai_neutral` → 不还手、
## `ai_sleeping` → 可奇袭……），不再决定招式（招式在 `enemy_skill.csv`）。
## 构建期校验器拿这份清单查表；06 目前没有登记这列，已记进 `待策划确认.md`（请设计补进数据字典）。
const AI_TEMPLATES := [
	"ai_aggressive", "ai_basic", "ai_boss", "ai_chaser", "ai_debuffer",
	"ai_elite", "ai_neutral", "ai_ranged", "ai_sleeping",
]

var _db


func _init(db) -> void:
	_db = db


## 生成一个敌人；enemy_id 不存在时报错并返回 null。
func create(enemy_id: String, difficulty_id: String = "normal"):
	var row: Resource = _db.get_row("enemy_base", enemy_id)
	if row == null:
		push_error("[EnemyFactory] enemy_base 缺少 %s" % enemy_id)
		return null
	var difficulty: Resource = _db.get_row("difficulty_config", difficulty_id)
	if difficulty == null:
		push_error("[EnemyFactory] 未知难度 %s，退回 normal" % difficulty_id)
		difficulty = _db.require_row("difficulty_config", "normal")

	var attrs: Dictionary = row.attr_map()
	var stats: Dictionary = (
		_stats_from_attrs(row, attrs, difficulty) if not attrs.is_empty()
		else _stats_from_derived(row, difficulty)
	)
	var skills := skills_of(str(row.enemy_id))

	var actor = BattleActorScript.from_enemy(row, stats, skills, int(row.level), _db)
	actor.actor_id = row.enemy_id
	actor.reward_exp = int(round(float(row.exp_reward) * float(difficulty.exp_mul)))
	actor.reward_money = int(round(float(row.money) * float(difficulty.money_mul)))
	actor.tags["difficulty"] = difficulty_id
	# 异常抗性要真的挂上：毒手 50% 毒抗、大寨主／醉刀客 20~30% 三抗（enemy_base 的 res_* 列）
	actor.resistances = row.resistances()
	return actor


## 旧派生列那条路（过渡期回退）：`enemy_base` 那七列 `attr_*` 还空着时用。
## **内功／装备的 `stat:` 类加成照样叠上去**（毒抗、内伤抗性这些靠内功来的就是这一类），
## 而 `attr:` 类加成（例如某部内功给悟性 +5）要等七维配齐、走共用管线才生效——
## 这条差异会在 `_warn_derived_path` 里点名，不让它静默。
func _stats_from_derived(row, difficulty) -> Dictionary:
	# ① 旧派生列（**未乘难度**的基础值）
	var stats := {
		"hp_max": float(row.hp_base),
		"qi_max": 0.0,
		"atk_phys": float(row.atk_phys),
		"atk_qi": float(row.atk_qi),
		"def_phys": float(row.def_phys),
		"def_qi": float(row.def_qi),
		"speed": float(row.speed),
		"hit_rate": float(row.hit_rate),
		"dodge_rate": float(row.dodge_rate),
		"crit_rate": float(row.crit_rate),
		"poise_break": 0.0,
		"hp_regen": 0.0,
		"qi_regen": 0.0,
		"res_internal": float(row.res_internal),
		# 敌方不穿透也不格挡（`enemy_base` 没有这两列；玩家的穿透与格挡先在玩家侧生效）
		"pen_rate": 0.0,
		"block_rate": 0.0,
		"block_reduction": 0.0,
		"dmg_reduction": 0.0,
	}
	# ② 装备/内功的 `stat:` 类加成 → **加在基础值上**（设计 10 §二：难度倍率照旧在最后乘）
	for contrib: Dictionary in _enemy_contributions(str(row.enemy_id)):
		if str(contrib.get("kind", "")) != AttributeCalculatorScript.CONTRIB_STAT_FLAT:
			continue
		var target := str(contrib.get("target", ""))
		if target.is_empty():
			continue
		stats[target] = float(stats.get(target, 0.0)) + float(contrib.get("value", 0.0))
	# ③ 难度倍率最后乘（只作用于 hp／内外功攻击／内外防，与旧口径一致）
	stats["hp_max"] = float(roundi(float(stats["hp_max"]) * float(difficulty.enemy_hp_mul)))
	stats["atk_phys"] = float(stats["atk_phys"]) * float(difficulty.enemy_atk_mul)
	stats["atk_qi"] = float(stats["atk_qi"]) * float(difficulty.enemy_atk_mul)
	stats["def_phys"] = float(stats["def_phys"]) * float(difficulty.enemy_def_mul)
	stats["def_qi"] = float(stats["def_qi"]) * float(difficulty.enemy_def_mul)
	_warn_derived_path(str(row.enemy_id))
	return stats


## 共用管线（设计 10 §二／§五）：**七维 + 等级 + 装备加成 + 内功加成** → `attr_to_stat`／`level_growth`
## → 派生数值；难度倍率照旧在最后乘。与角色走的是同一个 `AttributeCalculator`。
func _stats_from_attrs(row, attrs: Dictionary, difficulty) -> Dictionary:
	var contributions: Array = _enemy_contributions(str(row.enemy_id))
	# 内伤抗性这一列**保留**（设计只取消了毒/火/流血三条特权列），走固定值层进管线
	if float(row.res_internal) != 0.0:
		contributions.append({
			"kind": AttributeCalculatorScript.CONTRIB_STAT_FLAT, "target": "res_internal",
			"value": float(row.res_internal), "source": "enemy_base.res_internal",
		})
	var calculator = AttributeCalculatorScript.new(_db)
	var derived: Dictionary = calculator.compute(int(row.level), attrs, {}, contributions)
	var stats := {
		"hp_max": float(roundi(float(derived.get("hp_max", 0.0)) * float(difficulty.enemy_hp_mul))),
		"qi_max": float(derived.get("qi_max", 0.0)),
		"atk_phys": float(derived.get("atk_phys", 0.0)) * float(difficulty.enemy_atk_mul),
		"atk_qi": float(derived.get("atk_qi", 0.0)) * float(difficulty.enemy_atk_mul),
		"def_phys": float(derived.get("def_phys", 0.0)) * float(difficulty.enemy_def_mul),
		"def_qi": float(derived.get("def_qi", 0.0)) * float(difficulty.enemy_def_mul),
	}
	# 其余派生数值（身法／命中／闪避／暴击／回气／三种抗性…）照抄：它们不吃难度倍率
	for stat_id: String in derived:
		if stats.has(stat_id):
			continue
		stats[stat_id] = float(derived[stat_id])
	for extra: String in ["pen_rate", "block_rate", "block_reduction", "dmg_reduction"]:
		if not stats.has(extra):
			stats[extra] = 0.0
	return stats


## 敌人的**装备**与**内功**加成，都走与角色相同的贡献通道：
##   · `enemy_equip` → `Inventory.equipment_contributions`（含装备自带的常驻 buff）
##   · `enemy_passive` → `GrowthCalculator.passive_contributions`（内功的 `stat:`／`attr:` 加成，
##     **三种元素抗性也从这儿来**——设计 10 §二取消了那三条特权列，改为「装内功才有」）
func _enemy_contributions(enemy_id: String) -> Array:
	if enemy_id.is_empty():
		return []
	var out: Array = []
	var buff_service = BuffServiceScript.new(_db)
	for row: Resource in _db.rows_where("enemy_equip", "enemy_id", enemy_id):
		var equip_id := str(row.equip_id)
		if equip_id.is_empty():
			continue
		out.append_array(InventoryScript.equipment_contributions(
			_db, equip_id, "enemy_equip:%s" % equip_id, buff_service
		))
	var passives := PackedStringArray()
	for row: Resource in _db.rows_where("enemy_passive", "enemy_id", enemy_id):
		var skill_id := str(row.skill_id)
		if not skill_id.is_empty():
			passives.append(skill_id)
	if not passives.is_empty():
		out.append_array(GrowthCalculatorScript.new(_db).passive_contributions(passives))
	return out


## 「这几行还在用旧派生列」——每个进程只报一次（自检/构建日志里看得见）。
func _warn_derived_path(enemy_id: String) -> void:
	if _derived_path_warned:
		return
	_derived_path_warned = true
	var pending := PackedStringArray()
	for row: Resource in _db.rows("enemy_base"):
		if row.attr_map().is_empty():
			pending.append(str(row.enemy_id))
	push_warning(
		"[EnemyFactory] %s 还在用旧派生列（enemy_base 的 attr_* 七维还没配）——" % enemy_id
		+ "这些行按 10 §三的过渡口径跑；配齐七维后自动切到与角色同一条管线。待配：%s" % "、".join(pending)
	)


## 一个敌人的招式清单（`enemy_skill.csv`，按 `sort_order` 排）。
##
## 表里没有这个敌人（正常不该发生，构建期会点名「一招都出不了」）时返回空——
## 空手敌人不会静默消失：战斗里它会走「无可用招式，跳过」并写进战报（`BattleSimulator.auto_act`）。
func skills_of(enemy_id: String) -> PackedStringArray:
	var rows: Array = []
	for row: Resource in _db.rows("enemy_skill"):
		if str(row.enemy_id) == enemy_id:
			rows.append(row)
	rows.sort_custom(func(a, b): return int(a.sort_order) < int(b.sort_order))
	var out := PackedStringArray()
	for row: Resource in rows:
		out.append(str(row.skill_id))
	return out


## 按 enemy_team 生成整队，返回 BattleActor 数组。
func create_team(team_id: String, difficulty_id: String = "normal") -> Array:
	var team: Resource = _db.get_row("enemy_team", team_id)
	if team == null:
		push_error("[EnemyFactory] enemy_team 缺少 %s" % team_id)
		return []
	var out: Array = []
	for member: Dictionary in team.parsed_members():
		var enemy_id: String = member["enemy_id"]
		for index in range(int(member["count"])):
			var actor = create(enemy_id, difficulty_id)
			if actor == null:
				continue
			actor.actor_id = "%s#%d" % [enemy_id, index + 1]
			out.append(actor)
	return out


## 小队涉及的全部掉落组（去重），打完后交给 DropResolver 结算。
func team_drop_groups(team_id: String) -> PackedStringArray:
	var team: Resource = _db.get_row("enemy_team", team_id)
	if team == null:
		push_error("[EnemyFactory] enemy_team 缺少 %s" % team_id)
		return PackedStringArray()
	var out := PackedStringArray()
	for member: Dictionary in team.parsed_members():
		var row: Resource = _db.get_row("enemy_base", member["enemy_id"])
		if row == null:
			continue
		if row.drop_group.is_empty() or out.has(row.drop_group):
			continue
		out.append(row.drop_group)
	return out


## 威胁标示统计，用于明雷颜色环与调试输出。
func threat_tags(actors: Array) -> PackedStringArray:
	var out := PackedStringArray()
	for actor in actors:
		var tag: String = str(actor.tags.get("threat_tag", ""))
		if not tag.is_empty() and not out.has(tag):
			out.append(tag)
	return out
