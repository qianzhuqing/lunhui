## 统一 buff 系统（docs/design/08_增益与套装.md）。
##
## 边界：这个类只管「表怎么读 + 一个 buff 的静态规则」——极性、作用域、持续、叠加、
## 它带哪些数值修正、谁会发它。**结算不在这里**：上 buff / 每回合递减 / 把修正并进属性，
## 都要动已经跑通的血气／内力／架势管线，落在战斗侧（`BattleActor` / `BattleSimulator`）。
##
## 与 `status_effect` 的分工（设计原文）：**会逐回合掉血的走 status_effect，其余走 buff_def**。
class_name BuffService
extends RefCounted

## 贡献通道的 kind 字符串归 AttributeCalculator（这里只读它，不抄一份）
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")

## buff_def.is_debuff：0 增益 / 1 减益（同一套机制，UI 上分红蓝）
const POLARITY_BUFF := 0
const POLARITY_DEBUFF := 1

## buff_def.scope
const SCOPE_BATTLE := "battle"      ## 进战斗清空、战斗结束清除
const SCOPE_FIELD := "field"        ## 跨场景保留，带进战斗后按回合递减

## buff_def.stack_rule
const STACK_REFRESH := "refresh"    ## 只刷新剩余回合，不叠层
const STACK_STACK := "stack"        ## 叠层，上限 max_stack
const STACK_UNIQUE := "unique"      ## 全局唯一，重复获得无效

## buff_def.duration：>0 剩余回合；0 常驻（随来源存在而存在）；-1 持续到本场结束
const DURATION_PERMANENT := 0
const DURATION_UNTIL_BATTLE_END := -1

## buff_def.effect_kind
const EFFECT_KIND_STAT := "stat"          ## 数值型：加成写在 buff_stat
const EFFECT_KIND_COMPUTED := "computed"  ## 算出来（通用运功＝该内功常驻加成 ×2）
const EFFECT_KIND_SPECIAL := "special"    ## 特判

var db

## 表在运行期是**不可变**的（构建期生成、只读），所以这里可以放心缓存两样经常被反复问的东西：
##   · `buff_grant` 按「来源」分桶——`grants_for` 以前每次调用都全表扫一遍（4 人满装时逐件装备
##     各扫一次，把「构造」从 1.6ms 抬到 5.3ms，性能观测工具量出来的）；
##   · `buff_stat` 按 buff_id 的结果——同一 buff 会被反复问。
## 要改表请新建一个 TableDb 实例（夹具就是这么做的），别在这个实例上就地改。
var _grants_by_source: Dictionary = {}
var _grants_indexed := false
var _stat_mods_cache: Dictionary = {}


func _init(table_db) -> void:
	db = table_db


func all_defs() -> Array:
	return db.rows("buff_def")


func def_of(buff_id: String) -> Resource:
	return db.get_row("buff_def", buff_id)


func exists(buff_id: String) -> bool:
	return def_of(buff_id) != null


func is_debuff(buff_id: String) -> bool:
	var row: Resource = def_of(buff_id)
	return row != null and bool(row.is_debuff)


func polarity_of(buff_id: String) -> int:
	return POLARITY_DEBUFF if is_debuff(buff_id) else POLARITY_BUFF


func scope_of(buff_id: String) -> String:
	var row: Resource = def_of(buff_id)
	return str(row.scope) if row != null else ""


func is_field_buff(buff_id: String) -> bool:
	return scope_of(buff_id) == SCOPE_FIELD


func duration_of(buff_id: String) -> int:
	var row: Resource = def_of(buff_id)
	return int(row.duration) if row != null else 0


## 战斗外增益的有效分钟数（0.8.0 起：`scope=field` 用**现实时间**计时，
## 进战斗后转为整场有效、不按回合递减）。`battle` 的 buff 这里读出来是 0。
func field_minutes_of(buff_id: String) -> int:
	var row: Resource = def_of(buff_id)
	return int(row.field_minutes) if row != null else 0


func is_permanent(buff_id: String) -> bool:
	return duration_of(buff_id) == DURATION_PERMANENT


func stack_rule_of(buff_id: String) -> String:
	var row: Resource = def_of(buff_id)
	return str(row.stack_rule) if row != null else ""


func max_stack_of(buff_id: String) -> int:
	var row: Resource = def_of(buff_id)
	return int(row.max_stack) if row != null else 1


## `stat` 数值型（走 buff_stat）／`computed` 算出来（如通用运功＝常驻加成 ×2）／`special` 特判。
func effect_kind_of(buff_id: String) -> String:
	var row: Resource = def_of(buff_id)
	return str(row.effect_kind) if row != null else ""


## 一个 buff 带的数值修正：[{"kind": "attr"|"stat", "target_id": String, "value": float}]
## `attr:*` 是属性点（和加点一样先经 attr_to_stat），`stat:*` 是派生数值（固定值层）。
func stat_mods_of(buff_id: String) -> Array:
	if _stat_mods_cache.has(buff_id):
		return _stat_mods_cache[buff_id]
	var out: Array = []
	for row: Resource in db.rows("buff_stat"):
		if str(row.buff_id) != buff_id:
			continue
		var parsed: Dictionary = row.parsed_target()
		out.append({
			"kind": str(parsed["kind"]),
			"target_id": str(parsed["target_id"]),
			"value": float(row.value),
		})
	_stat_mods_cache[buff_id] = out
	return out


## 把一个 buff 的数值修正转成 `AttributeCalculator` 的贡献列表。
##
## 常驻 buff（装备 on_equip、套装档位）走**这条通道**并在属性计算里生效——
## 这样「装备即生效」在角色面板与战斗里读到的是同一个数，不必再加第二条结算路径。
## 临时 buff 由 `BattleActor` 在战斗里调用同一个函数重新算一遍属性。
func contributions_of(buff_id: String, source: String = "", stacks: int = 1) -> Array:
	var out: Array = []
	var label := source if not source.is_empty() else "buff:%s" % buff_id
	for mod: Dictionary in stat_mods_of(buff_id):
		var kind: String = (
			AttributeCalculatorScript.CONTRIB_ATTR_POINT
			if str(mod["kind"]) == "attr"
			else AttributeCalculatorScript.CONTRIB_STAT_FLAT
		)
		for _i in range(maxi(1, stacks)):
			out.append({
				"kind": kind, "target": str(mod["target_id"]),
				"value": float(mod["value"]), "source": label,
			})
	return out


## 谁会发这个 buff（`buff_grant` 里 source_id == 它）；`trigger` 留空表示不限时机。
func grants_of_buff(buff_id: String, trigger: String = "") -> Array:
	var out: Array = []
	for row: Resource in db.rows("buff_grant"):
		if str(row.buff_id) != buff_id:
			continue
		if not trigger.is_empty() and str(row.trigger) != trigger:
			continue
		out.append(row)
	return out


## 某个来源（装备／内功／招式／套装）在指定时机发的 buff 行。
func grants_for(source_type: String, source_id: String, trigger: String = "") -> Array:
	var out: Array = []
	for row: Resource in _grants_of_source(source_type, source_id):
		if not trigger.is_empty() and str(row.trigger) != trigger:
			continue
		out.append(row)
	return out


## 按「来源」取发放行（走一次建好的分桶索引，不再每次全表扫）
func _grants_of_source(source_type: String, source_id: String) -> Array:
	if not _grants_indexed:
		for row: Resource in db.rows("buff_grant"):
			var key := "%s|%s" % [str(row.source_type), str(row.source_id)]
			var bucket: Array = _grants_by_source.get(key, [])
			bucket.append(row)
			_grants_by_source[key] = bucket
		_grants_indexed = true
	return _grants_by_source.get("%s|%s" % [source_type, source_id], [])


## 这个招式有没有「施放时发 buff」的配置（`skill_active` 的 `on_cast`）。
##
## **增益招式**（例：醉里乾坤·醉步 → 忘忧）就是靠这个判定「能不能用」：它没有伤害类型与倍率
## （`is_attack()` 为假），但表里给它配了 on_cast 发放——施放一次给自己上 buff，不走伤害管线。
## 判定放这里，是因为「谁的 on_cast 发什么」的唯一出处就是这张表（见框架说明决策 221）。
func has_cast_grant(skill_id: String) -> bool:
	if skill_id.is_empty():
		return false
	return not grants_for("skill_active", skill_id, "on_cast").is_empty()


## 常驻类（`duration == 0`）：随来源存在而存在——装备与内功被动的数值加成走这条。
func persistent_grants_for(source_type: String, source_id: String) -> Array:
	var out: Array = []
	for row: Resource in grants_for(source_type, source_id):
		if is_permanent(str(row.buff_id)):
			out.append(row)
	return out
