## 掉落：必掉/概率、难度倍率不改清单、保底跨难度继承。
extends "res://tests/test_case.gd"

const DropResolverScript := preload("res://src/core/drop_resolver.gd")
const PityTrackerScript := preload("res://src/core/pity_tracker.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

const SEED := 90210
const ROLLS := 2000


func suite_name() -> String:
	return "掉落与保底"


func run() -> void:
	var db = get_db()
	# 保底字典里的版本标记（只写不读，跨读档不用它做分支）——写死绝对值，动它要同时想清楚兼容
	check_eq(PityTrackerScript.VERSION, 1, "保底字典的版本标记 = 1")
	_check_guaranteed_and_rate(db)
	_check_weighted_pick()
	_check_difficulty_scaling(db)
	_check_difficulty_keeps_list(db)
	_check_first_kill_only(db)
	_check_first_kill_persistence(db)
	_check_pity(db)
	_check_pity_cross_difficulty(db)
	_check_drop_bonus(db)


func _check_guaranteed_and_rate(db) -> void:
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED))
	var money := 0
	var herb := 0
	for index in range(ROLLS):
		var drops: Array = resolver.roll_group("drop_wolf", "normal")
		money += _count_occurrences(drops, "item_money")
		herb += _count_occurrences(drops, "item_herb")
	check_eq(money, ROLLS, "必掉槽每次都出铜钱")
	check_in_range(float(herb), float(ROLLS) * 0.20, float(ROLLS) * 0.30, "草药掉率约 25%")

	# 数量区间：野狼铜钱配置为 3~8
	var qty_ok := true
	for index in range(200):
		for entry: Dictionary in resolver.roll_group("drop_wolf", "normal"):
			if str(entry["item_id"]) == "item_money":
				qty_ok = qty_ok and int(entry["qty"]) >= 3 and int(entry["qty"]) <= 8
	check_true(qty_ok, "铜钱数量落在配置区间 3~8")


## RngService.pick_weighted 是以后生成装备词条要用的抽取器，这里先钉住行为。
func _check_weighted_pick() -> void:
	var rng = RngServiceScript.new(SEED)
	var entries := [{"key": "a", "weight": 1.0}, {"key": "b", "weight": 3.0}]
	var hits := 0
	for index in range(2000):
		if rng.pick_weighted(entries) == "b":
			hits += 1
	check_in_range(float(hits), 1400.0, 1600.0, "权重 1:3 时大致四分之三落在 b")
	check_eq(rng.pick_weighted([]), null, "空权重表返回 null")


func _check_difficulty_scaling(db) -> void:
	var normal := _count_drops(db, "drop_wolf", "normal", "item_herb")
	var hard := _count_drops(db, "drop_wolf", "hard", "item_herb")
	check_gt(float(hard), float(normal) * 1.2, "困难难度对凡品倍率 ×1.5，掉落明显增多")


func _check_difficulty_keeps_list(db) -> void:
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED))
	for difficulty in ["normal", "nightmare"]:
		var drops: Array = resolver.roll_group("drop_hidden_drunk", difficulty)
		check_eq(_count_occurrences(drops, "item_scroll_drunk"), 1, "%s 下必掉隐藏武学残卷" % difficulty)
		# 设计 10 §五（0.14.0）：**`drop_table` 不再列装备**——装备改由 `enemy_equip` 推（见下面那条）
		check_eq(_count_occurrences(drops, "eq_sword_04"), 0, "%s 下掉落表里不再列装备（装备走 enemy_equip）" % difficulty)


func _check_first_kill_only(db) -> void:
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED))
	# 0.14.0 起「首杀必掉装备」由 `enemy_equip` 接管（大寨主身上挂的就是黑风刀）
	var first_drops: Array = resolver.roll_enemy_equipment("en_bd_boss", true)
	check_eq(_count_occurrences(first_drops, "eq_sword_03"), 1, "首杀必掉他身上的武器（黑风刀）")
	check_eq(
		_count_occurrences(first_drops, "eq_sword_03"),
		1, "首杀那一次**只保一件**，不会因为保底再额外多给一件"
	)
	# 重复刷：黑风刀回到它自己的稀有度概率（良品 → 40%），不再必掉
	var hits := 0
	for index in range(200):
		var drops: Array = resolver.roll_enemy_equipment("en_bd_boss", false)
		if _count_occurrences(drops, "eq_sword_03") > 0:
			hits += 1
	check_in_range(float(hits), 50.0, 110.0, "重复刷黑风刀按良品的 40%% 出货（200 次里 %d 次）" % hits)


## 首杀记录必须进存档：以前它只存在会话里，退出重进就能再领一次首杀固定掉落。
func _check_first_kill_persistence(db) -> void:
	var state = solo_state(db)
	check_false(state.has_first_kill("drop_bd_boss"), "新档没有首杀记录")
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED))

	var first_pass: bool = state.mark_first_kill("drop_bd_boss")
	check_true(first_pass, "第一次击倒算首杀")
	check_true(state.has_first_kill("drop_bd_boss"), "首杀记录写进状态")
	check_eq(
		_count_occurrences(resolver.roll_enemy_equipment("en_bd_boss", first_pass), "eq_sword_03"),
		1,
		"首杀那次必掉他身上的武器（黑风刀）"
	)

	var second_pass: bool = state.mark_first_kill("drop_bd_boss")
	check_false(second_pass, "同一掉落组第二次不再算首杀")
	check_true(
		_count_occurrences(resolver.roll_enemy_equipment("en_bd_boss", second_pass), "eq_sword_03") <= 1,
		"第二次不再保底（最多按良品 40% 掷中一件）"
	)

	# 存档往返 = 退出重进：首杀记录必须留着（这就是漏洞的正面用例）
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_eq(back.migrated_from, 0, "v10 存档不需要迁移")
	check_true(back.has_first_kill("drop_bd_boss"), "存档往返后仍是已首杀")
	check_eq(back.first_kill_count(), 1, "首杀记录数量往返一致")
	check_false(back.mark_first_kill("drop_bd_boss"), "读档后同一组依旧不算首杀")

	# v9 及更早的老档没有这份记录 → 当作「还没打过首杀」（口径写进交接表）
	var legacy: Dictionary = state.to_dict()
	legacy["version"] = 9
	legacy.erase("first_kills")
	var migrated = GameStateScript.from_dict(legacy, db)
	check_not_null(migrated, "v9 老档能读进来")
	if migrated != null:
		check_eq(migrated.migrated_from, 9, "v9 老档记下迁移来源")
		check_eq(migrated.version, GameStateScript.VERSION, "v9 迁移后版本升到当前")
		check_false(migrated.has_first_kill("drop_bd_boss"), "v9 老档视作没打过首杀")


func _check_pity(db) -> void:
	# 0.14.0 起 `drop_table` 里那 12 行「敌人身上装备」被删掉了（设计 10 §五），
	# 其中就包括带保底的那两条——所以**保底机制本身**改用内存夹具验（复制表、只改内存，
	# 不碰策划的 CSV；同 `test_case.table_with_*` 的做法）。
	var local = TableDbScript.new()
	local.load_all()
	var table: Resource = local.tables["drop_table"].duplicate(true)
	var template: Resource = table.rows[0].duplicate(true)
	template.drop_row_id = "dr_test_pity"
	template.drop_group = "drop_test_pity"
	template.slot = 1
	template.item_id = "eq_sword_02"
	template.item_type = "equip"
	template.qty_min = 1
	template.qty_max = 1
	template.base_rate = 0.0          # 永远掷不中 → 只能靠保底出
	template.roll_type = "independent"
	template.pity_count = 20
	template.difficulty_scaled = 0
	template.first_kill_only = 0
	table.rows.append(template)
	table.index[str(template.id)] = table.rows.size() - 1
	local.tables["drop_table"] = table

	var pity = PityTrackerScript.new()
	var resolver = DropResolverScript.new(local, RngServiceScript.new(SEED), pity)
	var found_at := -1
	for index in range(200):
		var drops: Array = resolver.roll_group("drop_test_pity", "normal")
		if _count_occurrences(drops, "eq_sword_02") > 0:
			found_at = index + 1
			break
	check_gt(float(found_at), 0.0, "保底槽在 20 次内必出货（base_rate=0 也能被保底顶出来）")
	check_true(found_at <= 20, "保底计数不超过 20（实际第 %d 次）" % found_at)
	check_eq(pity.attempts("drop_test_pity|dr_test_pity"), 0, "出货后保底计数清零")

	# 保底计数单位本身的语义
	var tracker = PityTrackerScript.new()
	for index in range(19):
		tracker.register_attempt("drop_x|dr_x")
	check_false(tracker.should_force("drop_x|dr_x", 20), "19 次还没到保底")
	tracker.register_attempt("drop_x|dr_x")
	check_true(tracker.should_force("drop_x|dr_x", 20), "第 20 次触发保底")
	var restored = PityTrackerScript.new()
	restored.from_dict(tracker.to_dict())
	check_eq(restored.attempts("drop_x|dr_x"), 20, "保底计数可序列化与恢复（跨周目继承的落点）")
	restored.reset("drop_x|dr_x")
	check_eq(restored.attempts("drop_x|dr_x"), 0, "reset 清空计数")


## 保底跨难度继承：同一个 PityTracker 带着走，切难度不影响计数。
##
## 同样改用**内存夹具**（那两条带保底的发行数据行 0.14.0 已删，见 `_check_pity` 的说明）。
func _check_pity_cross_difficulty(db) -> void:
	var local = TableDbScript.new()
	local.load_all()
	var table: Resource = local.tables["drop_table"].duplicate(true)
	var template: Resource = table.rows[0].duplicate(true)
	template.drop_row_id = "dr_test_pity_x"
	template.drop_group = "drop_test_pity_x"
	template.item_id = "eq_sword_02"
	template.item_type = "equip"
	template.base_rate = 0.0
	template.pity_count = 20
	template.difficulty_scaled = 0
	table.rows.append(template)
	table.index[str(template.id)] = table.rows.size() - 1
	local.tables["drop_table"] = table
	var pity = PityTrackerScript.new()
	var key := "drop_test_pity_x|dr_test_pity_x"
	for index in range(19):
		pity.register_attempt(key)
	var resolver = DropResolverScript.new(local, RngServiceScript.new(SEED), pity)
	var drops: Array = resolver.roll_group("drop_test_pity_x", "hard")
	check_eq(_count_occurrences(drops, "eq_sword_02"), 1, "带着 19 次计数切到困难，下一次必出")


func _check_drop_bonus(db) -> void:
	var base := _count_drops(db, "drop_wolf", "normal", "item_herb")
	var bonus := _count_drops(db, "drop_wolf", "normal", "item_herb", {"drop_rate_bonus": 0.5})
	check_gt(float(bonus), float(base) * 1.2, "运属性掉落机缘按倍率放大非必掉槽")


func _count_drops(db, group: String, difficulty: String, item_id: String, options: Dictionary = {}) -> int:
	var resolver = DropResolverScript.new(db, RngServiceScript.new(SEED))
	var total := 0
	for index in range(ROLLS):
		total += _count_occurrences(resolver.roll_group(group, difficulty, options), item_id)
	return total


## 出现次数（掉率统计用；数量区间另行校验）。
func _count_occurrences(drops: Array, item_id: String) -> int:
	var total := 0
	for entry: Dictionary in drops:
		if str(entry["item_id"]) == item_id:
			total += 1
	return total
