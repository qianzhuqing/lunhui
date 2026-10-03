## 掉落结算。
##
## 规则来自 docs/design/05_装备与掉落.md：
##   难度不改变掉落清单，只改变掉落几率 —— 唯一实现处就是 difficulty_drop_rate；
##   保底按掉落槽计数、跨难度继承（把 PityTracker 存进存档即可）；跨周目继承随多周目暂缓。
## 本里程碑只按槽位掷点，装备的词条生成留到后续（掉落只产出「哪件装备、几件」）。
class_name DropResolver
extends RefCounted

var _db
var _rng
var _pity


func _init(db, rng, pity = null) -> void:
	_db = db
	_rng = rng
	_pity = pity


func set_pity(pity) -> void:
	_pity = pity


## 全队掉落机缘：取队伍里最高的运属性 drop_rate（保守方案，上限 25% 写在表里）。
static func party_drop_bonus(allies: Array) -> float:
	var best := 0.0
	for actor in allies:
		best = maxf(best, float(actor.stat("drop_rate")))
	return best


## 结算一个掉落组。
##
## options: {"first_kill": bool, "drop_rate_bonus": float}
## 返回 [{"drop_row_id", "item_id", "item_type", "qty", "rarity", "from_pity"}, ...]
func roll_group(drop_group: String, difficulty_id: String = "normal", options: Dictionary = {}) -> Array:
	var rows: Array = _db.rows_where("drop_table", "drop_group", drop_group)
	if rows.is_empty():
		push_error("[DropResolver] drop_table 里没有掉落组 %s" % drop_group)
		return []
	rows.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.slot) < int(b.slot))

	var first_kill := bool(options.get("first_kill", false))
	var drop_bonus := maxf(0.0, float(options.get("drop_rate_bonus", 0.0)))
	var out: Array = []
	var exclusive_taken := false

	for row: Resource in rows:
		if bool(row.first_kill_only) and not first_kill:
			continue
		if str(row.roll_type) == "exclusive" and exclusive_taken:
			continue

		var rarity := _item_rarity(row)
		var rate := float(row.base_rate)
		if bool(row.difficulty_scaled):
			rate *= _difficulty_multiplier(difficulty_id, rarity)
		if not row.is_guaranteed():
			# 运属性只对非必掉槽生效，避免把保底槽也放大
			rate *= 1.0 + drop_bonus
		rate = clampf(rate, 0.0, 1.0)

		var key := "%s|%s" % [row.drop_group, row.drop_row_id]
		var success := true
		if rate < 1.0:
			success = _rng.chance(rate)
		var from_pity := false
		if not success and row.has_pity() and _pity != null:
			_pity.register_attempt(key)
			if _pity.should_force(key, int(row.pity_count)):
				success = true
				from_pity = true
		if success and row.has_pity() and _pity != null:
			_pity.reset(key)
		if not success:
			continue
		if str(row.roll_type) == "exclusive":
			exclusive_taken = true

		var qty_min: int = int(row.qty_min)
		var qty_max: int = maxi(qty_min, int(row.qty_max))
		out.append({
			"drop_row_id": row.drop_row_id,
			"item_id": row.item_id,
			"item_type": row.item_type,
			"qty": _rng.randi_range(qty_min, qty_max),
			"rarity": rarity,
			"from_pity": from_pity,
		})
	return out


## 组内非必掉槽全部掷一遍（用于「重复刷」的期望值统计与测试）。
func roll_group_many(drop_group: String, times: int, difficulty_id: String = "normal", options: Dictionary = {}) -> Array:
	var out: Array = []
	for index in range(maxi(0, times)):
		out.append(roll_group(drop_group, difficulty_id, options))
	return out


func _difficulty_multiplier(difficulty_id: String, rarity_id: String) -> float:
	var row: Resource = _db.get_row("difficulty_drop_rate", "%s|%s" % [difficulty_id, rarity_id])
	if row == null:
		return 1.0
	return float(row.rate_multiplier)


## 敌人的**装备掉落**（设计 10 §五，0.14.0 起）：不再在 `drop_table` 里重复列装备，
## 而是**从 `enemy_equip` 推**——身上穿的，就是能掉的。
##
## 口径（表里只写了「概率复用 `rarity_def.drop_weight`」，其余是开发侧按那句定的，
## 已登记 `待策划确认.md` Q56）：
##   · 每件装备各自掷一次，概率 = `drop_weight / 100`（凡品 100 → 必掉；传世 0.5 → 0.5%）
##   · **首杀必掉一件**：优先**武器槽**（设计说「大寨主身上挂的就是黑风刀，效果不变」，
##     醉刀客那把锈月也在武器槽上），没有武器就退而取稀有度最高的那件
##   · 掷出来的条目与 `drop_table` 的条目同形（`{item_id, qty}`），直接交给 `BattleReward`
##     的掉落分支（装备会建实例并滚词条）
func roll_enemy_equipment(enemy_id: String, first_kill: bool = false) -> Array:
	var rows: Array = _db.rows_where("enemy_equip", "enemy_id", enemy_id)
	if rows.is_empty():
		return []
	var out: Array = []
	var guaranteed_done := false
	# 首杀那一件先定下来（武器优先），免得「随机掷出来的」把保底名额占掉之后还要补一件
	var guaranteed_id := _first_kill_pick(rows) if first_kill else ""
	for row: Resource in rows:
		var equip_id := str(row.equip_id)
		if equip_id.is_empty():
			continue
		if not guaranteed_id.is_empty() and equip_id == guaranteed_id and not guaranteed_done:
			guaranteed_done = true
			out.append({"item_id": equip_id, "qty": 1, "source": "enemy_equip_guaranteed"})
			continue
		if _rng.chance(_equipment_rate(equip_id)):
			out.append({"item_id": equip_id, "qty": 1, "source": "enemy_equip"})
	if first_kill and not guaranteed_done and not guaranteed_id.is_empty():
		out.append({"item_id": guaranteed_id, "qty": 1, "source": "enemy_equip_guaranteed"})
	return out


## 一件装备的掉落概率：稀有度的 `drop_weight` ÷ 100，夹在 0~1（表里 100 表示「必掉」）
func _equipment_rate(equip_id: String) -> float:
	var equip: Resource = _db.get_row("equip_base", equip_id)
	if equip == null:
		return 0.0
	var rarity: Resource = _db.get_row("rarity_def", str(equip.rarity))
	if rarity == null:
		return 0.0
	return clampf(float(rarity.drop_weight) / 100.0, 0.0, 1.0)


## 首杀保底给哪一件：优先武器槽，没有武器就取稀有度最高的（同稀有度取列表里靠前的）
func _first_kill_pick(rows: Array) -> String:
	var weapon_id := ""
	var best_id := ""
	var best_weight := -1.0
	for row: Resource in rows:
		var equip_id := str(row.equip_id)
		var equip: Resource = _db.get_row("equip_base", equip_id)
		if equip == null:
			continue
		var weight := 0.0
		var rarity: Resource = _db.get_row("rarity_def", str(equip.rarity))
		if rarity != null:
			weight = float(rarity.drop_weight)
		if str(equip.slot) == "weapon" and weapon_id.is_empty():
			weapon_id = equip_id
		if weight > best_weight:
			best_weight = weight
			best_id = equip_id
	return weapon_id if not weapon_id.is_empty() else best_id


func _item_rarity(drop_row: Resource) -> String:
	if str(drop_row.item_type) == "equip":
		var equip: Resource = _db.get_row("equip_base", drop_row.item_id)
		if equip == null:
			push_error("[DropResolver] equip_base 缺少 %s" % drop_row.item_id)
			return "common"
		return str(equip.rarity)
	var item: Resource = _db.get_row("item_base", drop_row.item_id)
	if item == null:
		push_error("[DropResolver] item_base 缺少 %s" % drop_row.item_id)
		return "common"
	return str(item.rarity)
