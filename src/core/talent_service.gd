## 天赋（设计 12，0.16.0）：出身定底子、天赋定偏好，5 个点内自选。
##
## 数据全在两张表里：`talent_def`（展示与花费）＋ `talent_effect`（效果）。
## 效果目标三种前缀：`attr:`（属性点）／`stat:`（派生数值）／`rule:`（规则性效果）。
##
## 三条设计纪律在代码里的落点：
##   ① 「动七维的天赋必须总和为零」——**构建期**由 `table_validator._check_creation_tables`
##      报警告（0.16.0 的发行数据本身是净 +2／+3，已记 Q58 等设计裁决）；
##   ② 「纯增益只加资源与规则、不加数值」——`rule:` 那几种（起始铜钱、图鉴翻倍…）
##      由**各自的系统**读 `rule_value()`，不混进属性贡献；
##   ③ 「高代价天赋的代价必须写进 desc」——那是文案纪律，代码只保证 `desc` 有值（构建期查）。
##
## 与前缀的唯一出处对齐：`attr:`／`stat:` 的换算复用 `AttributeCalculator` 的贡献 kind 常量，
## 不在这里另写字面量。
class_name TalentService
extends RefCounted

const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")


## 已选天赋（`state.talent_picks[char_id]`）。
static func picked_of(state, char_id: String) -> PackedStringArray:
	if state == null:
		return PackedStringArray()
	var raw = state.talent_picks.get(char_id, [])
	var out := PackedStringArray()
	for item: Variant in Array(raw):
		out.append(str(item))
	return out


static func cost_of(db, picks: PackedStringArray) -> int:
	var total := 0
	if db == null:
		return total
	for talent_id: String in picks:
		var row: Resource = db.get_row("talent_def", talent_id)
		if row != null:
			total += int(row.cost)
	return total


static func budget(db) -> int:
	if db == null:
		return 0
	for row: Resource in db.rows("growth_const"):
		if str(row.const_id) == "talent_points":
			return int(row.value)
	return 0


static func remaining_points(db, picks: PackedStringArray) -> int:
	return budget(db) - cost_of(db, picks)


## 能不能再选一个（已选过、点数不够、天赋不存在都被拒，并给原因）。
static func can_pick(db, picks: PackedStringArray, talent_id: String) -> Dictionary:
	var row: Resource = db.get_row("talent_def", talent_id)
	if row == null:
		return {"ok": false, "error": "没有这个天赋：%s" % talent_id}
	if picks.has(talent_id):
		return {"ok": false, "error": "已经选了「%s」" % str(row.name_cn)}
	if int(row.cost) > remaining_points(db, picks):
		return {"ok": false, "error": "点数不够：「%s」要 %d 点，只剩 %d 点"
			% [str(row.name_cn), int(row.cost), remaining_points(db, picks)]}
	return {"ok": true, "error": ""}


## 选／取消一个天赋（创建界面按一下就走这里）。返回 {ok, picks, error}
static func toggle(db, picks: PackedStringArray, talent_id: String) -> Dictionary:
	var next := PackedStringArray()
	if picks.has(talent_id):
		for item: String in picks:
			if item != talent_id:
				next.append(item)
		return {"ok": true, "picks": next, "error": ""}
	var check := can_pick(db, picks, talent_id)
	if not bool(check["ok"]):
		return {"ok": false, "picks": picks, "error": str(check["error"])}
	next = picks.duplicate()
	next.append(talent_id)
	return {"ok": true, "picks": next, "error": ""}


## 天赋的 `attr:`／`stat:` 效果 → `AttributeCalculator` 的贡献列表。
##
## 与图鉴奖励、装备、内功走同一条通道（`CharacterSheet.contributions()` 汇总），
## 所以「选了什么天赋」会真的进面板与战斗，而不是只写在存档里。
static func contributions(db, state, char_id: String) -> Array:
	var out: Array = []
	if db == null or state == null:
		return out
	for talent_id: String in picked_of(state, char_id):
		for row: Resource in db.rows_where("talent_effect", "talent_id", talent_id):
			var target := str(row.target)
			var parts := target.split(":", true, 1)
			if parts.size() != 2:
				continue
			var value := float(row.value)
			match parts[0]:
				"attr":
					out.append({
						"kind": AttributeCalculatorScript.CONTRIB_ATTR_POINT,
						"target": parts[1], "value": value, "source": "talent:%s" % talent_id,
					})
				"stat":
					out.append({
						"kind": AttributeCalculatorScript.CONTRIB_STAT_FLAT,
						"target": parts[1], "value": value, "source": "talent:%s" % talent_id,
					})
	return out


## 规则性效果（`rule:`）的取值表：`{rule_id: 合计值}`。
##
## 谁消费谁读——例如起始铜钱由创建流程读 `start_money`，图鉴翻倍由 `codex_bonus()` 读
## `codex_bonus_multiplier`。**不在这里替它们决定语义**，这一层只把表里的数字合并起来。
static func rules(db, state, char_id: String) -> Dictionary:
	var out: Dictionary = {}
	if db == null or state == null:
		return out
	for talent_id: String in picked_of(state, char_id):
		for row: Resource in db.rows_where("talent_effect", "talent_id", talent_id):
			var target := str(row.target)
			if not target.begins_with("rule:"):
				continue
			var rule_id := target.substr("rule:".length())
			out[rule_id] = float(out.get(rule_id, 0.0)) + float(row.value)
	return out


static func rule_value(db, state, char_id: String, rule_id: String, fallback: float = 0.0) -> float:
	return float(rules(db, state, char_id).get(rule_id, fallback))


## 队里这条规则的**最大值**：判定门槛、买价这类效果是「队伍级」的——
## 谁带着它就算数（设计 12 §六：江湖百晓生「判定门槛 −2」，让不想打的人也能推内容）。
## 也用来问「队里有没有这条规则」（返回值 > 0）。
static func party_rule_value(db, state, rule_id: String, fallback: float = 0.0) -> float:
	var out := fallback
	if db == null or state == null:
		return out
	for char_id: String in state.char_ids:
		out = maxf(out, rule_value(db, state, str(char_id), rule_id, fallback))
	return out


## 判定值上的天赋加成（设计 12 §六 的三条）：
##   `rule:event_check_bonus`        = **所有**判定 +N（见多识广 +1）
##   `rule:event_check_bonus_<技能>` = 只加这一门（三寸不烂之舌：文学 +2／悬壶：医术 +2）
## 与 `CharacterSheet.event_check_value()` 的公式**同一条路**：先加进判定值，再由
## `EventCheckService` 与门槛比——不减门槛（那是另一条规则 `event_check_difficulty`）。
static func check_bonus(db, state, char_id: String, skill_id: String) -> int:
	var out := int(rule_value(db, state, char_id, "event_check_bonus", 0.0))
	if not skill_id.is_empty():
		out += int(rule_value(db, state, char_id, "event_check_bonus_%s" % skill_id, 0.0))
	return out


## 展示用的天赋卡（创建界面直接渲染这个）：按 category 分组、按花费排序。
static func cards(db) -> Array:
	var out: Array = []
	if db == null:
		return out
	for row: Resource in db.rows("talent_def"):
		out.append({
			"talent_id": str(row.talent_id),
			"name_cn": str(row.name_cn),
			"category": str(row.category),
			"cost": int(row.cost),
			"desc": str(row.desc),
		})
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if a["category"] == b["category"]:
			if a["cost"] == b["cost"]:
				return a["talent_id"] < b["talent_id"]
			return int(a["cost"]) < int(b["cost"])
		return str(a["category"]) < str(b["category"])
	)
	return out
