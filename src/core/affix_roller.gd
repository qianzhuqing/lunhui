## 装备词条生成（05_装备与掉落.md 的「稀有度 → 词条数量」与「词条按槽位限定」）。
##
## 规则与数值全在表里：
##   `rarity_def.affix_min/affix_max` 决定这件装备能带几条词条；
##   `affix_pool.allow_slots` 决定词条能出现在哪些槽位（武器不该刷出「内力回复」）；
##   `affix_pool.min_rarity` 决定词条最低出在哪一档稀有度上；
##   `affix_pool.weight` 是抽取权重，`value_min/value_max` 是数值区间。
##
## 存档里存 `{affix_id, target, value_kind, value}`：滚好的数值与目标都冻结在存档里，
## 设计后来改了词条池也不会把已刷出来的装备改坏（只剩 affix_id 的老档仍按表反查）。
class_name AffixRoller
extends RefCounted

## 贡献 kind 字符串的**唯一定义处**是 `AttributeCalculator`（写错会被它的 `!=` 判断静默跳过：
## 面板少一截加成、没有任何报错）。这里只借名字用，不再写字面量。
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
## value_kind 的两种语义：加属性点 / 加派生数值（rate 也是派生数值，只是它本身是百分比）
const ATTR_KIND := AttributeCalculatorScript.CONTRIB_ATTR_POINT
const STAT_KIND := AttributeCalculatorScript.CONTRIB_STAT_FLAT

var db
var rng


func _init(table_db, rng_service) -> void:
	db = table_db
	rng = rng_service


## 稀有度的高低顺序**唯一出处是表**（`rarity_def` 从低到高的行顺序）——设计侧校验器
## （`validate_tables.ps1` 的 `$rarityRank`）就是这么算的。以前这里抄了一份常量名单，
## 于是设计**加一档稀有度**时：PS1 跟着表走、代码却对新档 `return 0`（＝凡品），
## `min_rarity` 的门槛静默失效（新档装备能刷出本该更高档才有的词条）。见框架说明决策 230。
func rarity_rank(rarity_id: String) -> int:
	var rows: Array = db.rows("rarity_def")
	for index in range(rows.size()):
		if str(rows[index].rarity_id) == rarity_id:
			return index
	return 0


## 这件装备能带几条词条（取 rarity_def.affix_min~affix_max 之间的随机数）
func affix_count(rarity_id: String) -> int:
	var row: Resource = db.get_row("rarity_def", rarity_id)
	if row == null:
		push_error("[AffixRoller] rarity_def 缺少 %s" % rarity_id)
		return 0
	var low := maxi(0, int(row.affix_min))
	var high := maxi(low, int(row.affix_max))
	if rng == null:
		return low
	return rng.randi_range(low, high)


## 候选词条：槽位命中且稀有度够高
func candidates_for(slot_id: String, rarity_id: String) -> Array:
	var out: Array = []
	var rarity_rank := rarity_rank(rarity_id)
	for row: Resource in db.rows("affix_pool"):
		if not row.slots().has(slot_id):
			continue
		if rarity_rank(str(row.min_rarity)) > rarity_rank:
			continue
		if int(row.weight) <= 0:
			continue
		out.append(row)
	return out


## 掷一件装备的词条。返回 [{affix_id, target, value_kind, value}]，按抽取顺序排列。
## 候选不够（比如低稀有度 + 冷门槽位）时能出几条出几条，不报错也不补假词条。
func roll_for(equip_id: String) -> Array:
	var base: Resource = db.get_row("equip_base", equip_id)
	if base == null:
		push_error("[AffixRoller] equip_base 缺少 %s" % equip_id)
		return []
	var slot_id := str(base.slot)
	var pool := candidates_for(slot_id, str(base.rarity))
	var count := affix_count(str(base.rarity))
	var out: Array = []
	for index in range(mini(count, pool.size())):
		var picked: Resource = _take_weighted(pool)
		if picked == null:
			break
		out.append({
			"affix_id": str(picked.affix_id),
			"target": str(picked.target),
			"value_kind": str(picked.value_kind),
			"value": _roll_value(picked),
		})
	return out


## 按权重抽一条并从池子里拿掉（同一件装备不重复出同一条词条）
func _take_weighted(pool: Array) -> Resource:
	var entries: Array = []
	for row: Resource in pool:
		entries.append({"key": row, "weight": float(row.weight)})
	var picked: Variant = rng.pick_weighted(entries) if rng != null else entries[0]["key"]
	if picked == null:
		return null
	pool.erase(picked)
	return picked


func _roll_value(row: Resource) -> float:
	var low := float(row.value_min)
	var high := maxf(low, float(row.value_max))
	var value: float = rng.randf_range(low, high) if rng != null else low
	if str(row.value_kind) == ATTR_KIND:
		return float(int(round(value)))
	# 派生数值保留 3 位：暴击率这类本身就是小数（0.02 = 2%）
	return roundf(value * 1000.0) / 1000.0


# ------------------------------------------------------------------ 读档后的查询（静态：Inventory 直接用）

## 记录里的目标串（"attr:str" / "stat:atk_phys"），没有就回表里查
static func target_of(db, record: Dictionary) -> String:
	var target := str(record.get("target", ""))
	if not target.is_empty():
		return target
	var row: Resource = db.get_row("affix_pool", str(record.get("affix_id", "")))
	if row != null:
		return str(row.target)
	push_error("[AffixRoller] affix_pool 里没有词条 %s" % str(record.get("affix_id", "")))
	return ""


## 存档里的词条记录 → AttributeCalculator 的贡献列表
static func contributions_of(db, records: Array) -> Array:
	var out: Array = []
	for record: Dictionary in records:
		var target := target_of(db, record)
		if target.is_empty():
			continue
		var separator := target.find(":")
		var prefix := target.substr(0, separator) if separator >= 0 else ""
		var target_id := target.substr(separator + 1) if separator >= 0 else target
		var value := float(record.get("value", 0.0))
		if is_equal_approx(value, 0.0):
			continue
		var kind := ATTR_KIND if prefix == "attr" else STAT_KIND
		out.append({
			"kind": kind,
			"target": target_id,
			"value": float(int(value)) if kind == ATTR_KIND else value,
			"source": str(record.get("affix_id", "")),
		})
	return out


## 单条词条的显示文案：「外功攻击 +7」「暴击率 +2.0%」
static func describe(db, record: Dictionary) -> String:
	var target := target_of(db, record)
	if target.is_empty():
		return "未知词条"
	var separator := target.find(":")
	var prefix := target.substr(0, separator) if separator >= 0 else ""
	var target_id := target.substr(separator + 1) if separator >= 0 else target
	var value := float(record.get("value", 0.0))
	if prefix == "attr":
		var attr_row: Resource = db.get_row("attribute_def", target_id)
		var attr_name := str(attr_row.name_cn) if attr_row != null else target_id
		return "%s +%d" % [attr_name, int(value)]
	var stat_row: Resource = db.get_row("stat_def", target_id)
	var stat_name := str(stat_row.name_cn) if stat_row != null else target_id
	if stat_row != null and bool(stat_row.is_percent):
		return "%s +%.1f%%" % [stat_name, value * 100.0]
	if stat_row != null and int(stat_row.show_decimals) > 0:
		return "%s +%.1f" % [stat_name, value]
	return "%s +%d" % [stat_name, int(round(value))]


## 一件装备的整段词条文案
static func summarize(db, records: Array) -> String:
	var parts := PackedStringArray()
	for record: Dictionary in records:
		parts.append(describe(db, record))
	return "、".join(parts)
