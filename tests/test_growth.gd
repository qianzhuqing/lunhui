## 成长系统：槽位上限、熟练度、打坐费用、图鉴奖励。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")


func suite_name() -> String:
	return "成长系统：槽位与熟练度"


func run() -> void:
	var db = get_db()
	var growth = GrowthCalculatorScript.new(db)
	_check_slots(growth)
	_check_mastery(growth)
	_check_cultivate(growth)
	_check_codex(growth)
	_check_passive_fit(db, growth)
	_check_panel(db)


## 公式：招式槽 = 2 + floor(等级/5) + floor(悟性/8)（上限 9）
##       内功容量 = 2 + floor(等级/6) + floor(根骨/10)（上限 7）
func _check_slots(growth) -> void:
	# 模板初始资质是 悟 10 / 根 5
	check_eq(growth.active_slots(1, {"wu": 10, "gen": 5}), 3, "1 级书生：招式槽 3")
	check_eq(growth.passive_capacity(1, {"wu": 10, "gen": 5}), 2, "1 级书生：内功容量 2（连一部 ★5 都装不下）")
	check_eq(growth.active_slots(20, {"wu": 10, "gen": 5}), 7, "20 级不投资资质：招式槽 7")
	check_eq(growth.passive_capacity(20, {"wu": 10, "gen": 5}), 5, "20 级不投资资质：容量 5")
	check_eq(growth.active_slots(20, {"wu": 30, "gen": 5}), 9, "20 级专注悟性：招式槽满 9")
	check_eq(growth.passive_capacity(20, {"wu": 5, "gen": 25}), 7, "20 级专注根骨：容量满 7")
	check_eq(growth.active_slots(20, {"wu": 5, "gen": 25}), 6, "20 级专注根骨：招式槽只有 6")
	# 硬上限
	check_eq(growth.active_slots(99, {"wu": 999}), 9, "招式槽不会超过硬上限 9")
	check_eq(growth.passive_capacity(99, {"gen": 999}), 7, "内功容量不会超过硬上限 7")
	# 注意：01_角色系统.md 的采样表里「20 级、专注悟性 30」那行把内功容量写成 4，
	# 但容量公式与悟性无关、20 级最低也是 5，所以按公式取 5，已在交接表里回报订正。
	check_eq(growth.passive_capacity(20, {"wu": 30, "gen": 5}), 5, "内功容量与悟性无关（01 的采样表已按公式订正，并由 test_handshake 逐行核）")


## 招式实际倍率 = 基础倍率 × (1 + 熟练度 × mastery_gain)
func _check_mastery(growth) -> void:
	check_eq(growth.mastery_max(), 10, "熟练度上限 10")
	check_float(growth.mastery_multiplier(1, 0), 1.0, "没练过时系数是 1.0")
	check_float(growth.mastery_multiplier(1, 10), 1.5, "★1 练满 ×1.50")
	check_float(growth.mastery_multiplier(2, 10), 1.45, "★2 练满 ×1.45")
	check_float(growth.mastery_multiplier(3, 10), 1.4, "★3 练满 ×1.40")
	check_float(growth.mastery_multiplier(4, 10), 1.3, "★4 练满 ×1.30")
	check_float(growth.mastery_multiplier(5, 10), 1.25, "★5 练满 ×1.25")
	check_float(growth.mastery_multiplier(1, 99), 1.5, "熟练度超过上限按上限算")
	check_float(growth.mastery_multiplier(1, -5), 1.0, "负熟练度按 0 算")
	var star5 = growth.star_row(5)
	check_not_null(star5, "5 星定义存在")
	if star5 != null:
		check_eq(str(star5.name_cn), "绝学", "★5 叫绝学")
		check_eq(int(star5.learn_req_value), 25, "★5 修习门槛 25")


func _check_cultivate(growth) -> void:
	check_eq(growth.cultivate_cost(1, 1), 50, "★1 的第一级打坐费 50")
	check_eq(growth.cultivate_cost(1, 2), 80, "第二级 50 × 1.6 = 80")
	check_eq(growth.cultivate_cost(5, 1), 450, "★5 的第一级 450")
	check_gt(float(growth.cultivate_cost(5, 10)), float(growth.cultivate_cost(5, 1)), "练得越高越贵")


func _check_codex(growth) -> void:
	check_eq(int(growth.codex_bonus(0)["times"]), 0, "一部都没有时没有奖励")
	check_eq(int(growth.codex_bonus(4)["times"]), 0, "收集 4 部还没到 5 部")
	check_eq(int(growth.codex_bonus(5)["times"]), 1, "5 部给一次")
	check_eq(int(growth.codex_bonus(5)["bonus"]), 1, "一次奖励七项各 +1")
	check_eq(int(growth.codex_bonus(50)["times"]), 10, "50 部给满 10 次")
	check_eq(int(growth.codex_bonus(500)["times"]), 10, "超过上限仍是 10 次")
	var attrs: Dictionary = growth.codex_bonus(10)["attrs"]
	check_eq(attrs.size(), 7, "图鉴奖励覆盖七项属性")
	for attr_id in ["str", "con", "agi", "int", "luk", "wu", "gen"]:
		check_eq(int(attrs[attr_id]), 2, "%s 也吃到图鉴奖励" % attr_id)


func _check_passive_fit(db, growth) -> void:
	var one_small := PackedStringArray(["pf_xuanwei_01"])            # ★1 占 1 格
	var one_top := PackedStringArray(["pf_xuanwei_05"])              # ★5 占 3 格
	var low := {"wu": 10, "gen": 5}
	check_true(bool(growth.passive_fit(one_small, 1, low)["fits"]), "1 级装得下一部低级内功")
	check_false(bool(growth.passive_fit(one_top, 1, low)["fits"]), "1 级只有 2 格，装不下 ★5（占 3 格）")
	check_true(bool(growth.passive_fit(one_top, 20, {"wu": 10, "gen": 25})["fits"]), "容量 7 时装得下 ★5")
	check_false(
		bool(growth.passive_fit(PackedStringArray(["pf_xuanwei_05", "pf_xuanwei_01"]), 1, low)["fits"]),
		"3 + 1 = 4 格超过 1 级的 2 格"
	)
	var contributions: Array = growth.passive_contributions(one_small)
	check_gt(float(contributions.size()), 0.0, "内功加成能转成属性贡献")
	_check_mastery_scales_passive(db, growth)


## 熟练度放大内功加成（0.15.0 口径确认第 3 条）：**只放大 `stat:` 类，不放大 `attr:` 类**。
##
## 用真实数据挑一部**同时有 attr: 与 stat: 两种加成**的内功，这样一条用例能同时钉两边：
## 放大错边（把 attr 也乘了）或者系数公式换了都会红。
func _check_mastery_scales_passive(db, growth) -> void:
	var picked := ""
	var attr_sum := 0.0
	var stat_sum := 0.0
	# 候选列表来自 `skill_passive_stat` 里**两种前缀都有**的那几部（一个都不能少，
	# 少了这条用例会退化成"夹具找不到"的假绿）
	for skill_id: String in ["pf_xuanwei_05", "pf_heifeng_02", "pf_heifeng_03", "pf_tieqiang_01", "pf_wudu_04"]:
		var row: Resource = db.get_row("skill_base", skill_id)
		if row == null:
			continue
		var has_attr := false
		var has_stat := false
		for stat_row: Resource in db.rows_where("skill_passive_stat", "skill_id", skill_id):
			var parsed: Dictionary = stat_row.parsed_target()
			if str(parsed["kind"]) == "attr":
				has_attr = true
			elif str(parsed["kind"]) == "stat":
				has_stat = true
		if has_attr and has_stat:
			picked = skill_id
			break
	check_true(not picked.is_empty(), "找得到一部同时有 attr: 与 stat: 加成的内功（熟练度用例的夹具）")
	if picked.is_empty():
		return
	var ids := PackedStringArray([picked])
	var star := int(db.get_row("skill_base", picked).star)
	var base: Array = growth.passive_contributions(ids)
	var scaled_5: Array = growth.passive_contributions(ids, {picked: 5})
	check_eq(scaled_5.size(), base.size(), "每条加成都在（熟练度不改变条数）")
	var factor: float = growth.mastery_multiplier(star, 5)
	check_gt(factor, 1.0, "5 级熟练度系数 > 1（实际 %.2f）" % factor)
	for i in range(base.size()):
		var before: Dictionary = base[i]
		var after: Dictionary = scaled_5[i]
		var is_attr: bool = str(before["kind"]) == "attr_point"
		var expected: float = float(before["value"]) * (1.0 if is_attr else factor)
		check_float(float(after["value"]), expected,
			"%s 的 %s 加成熟练度 5 级后应为 %.3f（attr 不放大、stat 放大）"
				% [picked, str(before["target"]), expected], 0.001)


func _check_panel(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	var sheet = CharacterSheetScript.new(db, state, char_id)
	check_eq(sheet.active_slots(), 3, "面板显示招式槽 3")
	check_eq(sheet.passive_capacity(), 2, "面板显示内功容量 2")
	check_eq(int(sheet.stats()["slot_active"]), 3, "槽位并入派生数值供面板显示")
	check_eq(int(sheet.total_attrs().size()), 7, "七维属性都在面板上")
	check_eq(int(sheet.naked_attrs().get("wu", 0)), 10, "悟性初始 10")
	check_eq(int(sheet.naked_attrs().get("gen", 0)), 5, "根骨初始 5")
	var str_before := int(sheet.total_attrs()["str"])
	# 设计 0.13.0：悟性与根骨是「资质」，不能靠加点涨（只能靠图鉴奖励与特定内功）
	check_false(bool(state.spend_point(db, char_id, "wu")["ok"]), "悟性不可加点（资质）")
	check_false(bool(state.spend_point(db, char_id, "gen")["ok"]), "根骨不可加点（资质）")
	check_true(bool(state.spend_point(db, char_id, "str")["ok"]), "五维照样能加")
	var after = CharacterSheetScript.new(db, state, char_id)
	check_eq(int(after.total_attrs()["wu"]), 10, "被拒之后悟性没变")
	check_eq(int(after.total_attrs()["str"]), str_before + 1, "加了力的那一点面板跟着变")
