## 角色面板数据：五维合成、派生数值、判定值、加点。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")


func suite_name() -> String:
	return "角色面板数据"


func run() -> void:
	# 01 的判定值公式 `等级 + floor(裸属性 / 5)`——除数写死在代码里，而下面那几条断言
	# 都是用「实际值 vs 手算期望」比的，把除数从 5 改成 4 时**期望值也得跟着改才红**，
	# 于是变异探针（2026-10-03：`EVENT_SKILL_ATTR_DIVISOR 5→4`）没抓住。这里把除数本身钉住。
	check_eq(CharacterSheetScript.EVENT_SKILL_ATTR_DIVISOR, 5, "判定值公式的除数是 5（01 原文）")
	var db = get_db()
	var state = GameStateScript.new_game(db, "normal")
	var char_id := str(state.char_ids[0])
	var sheet = CharacterSheetScript.new(db, state, char_id)
	_check_basics(db, state, sheet)
	_check_five_attributes(db, state, sheet)
	_check_stats_match_calculator(db, state, sheet)
	_check_event_check(db, state, sheet)
	_check_allocation(db, state)
	_check_views(sheet)
	_check_loadout(db, state)
	_check_codex_reward(db)
	_check_unusable_skill_note(db, state)


## 图鉴奖励（设计 01「收集本身也是成长」）：每收集 5 部武学、七项各 +1、上限 10 次，
## 而且**面板与门槛必须看到同一套属性**（否则会出现「面板显示够门槛、实际学不会」）。
## 招式的两种「能不能用」，面板都要如实说清楚：
##   · **增益招式**（`sk_drunk_zuibu` 醉里乾坤·醉步）：整行 0（无伤害／倍率 0／段数 0、`target_type=self`），
##     但表里配了 `on_cast` 发放（醉步 → 忘忧）→ **战斗里能用**，施放一次给自己上 buff，
##     面板写「增益招式：施放时给自己上 buff」（2026-10-03 起；在那之前它被标成「用不出来」，见决策 221）。
##   · **真的没效果**的招式（既不打伤害、也没有 `on_cast` 发放）：写「战斗里用不出来（效果未配）」。
func _check_unusable_skill_note(db, state) -> void:
	var char_id := str(state.char_ids[0])
	state.learn_skill(char_id, "sk_drunk_zuibu")
	var rows: Array = CharacterSheetScript.new(db, state, char_id).skill_rows()
	var found: Dictionary = {}
	for row: Dictionary in rows:
		found[str(row["skill_id"])] = row
	check_true(found.has("sk_drunk_zuibu"), "醉步已学会、会出现在武学列表里")
	if found.has("sk_drunk_zuibu"):
		var broken: Dictionary = found["sk_drunk_zuibu"]
		check_true(bool(broken["battle_usable"]), "醉步是**增益招式**：战斗里能用")
		check_true(
			str(broken["battle_note"]).contains("增益"),
			"面板会写清它是增益招式：%s" % str(broken["battle_note"]),
		)
	if found.has("sk_xuanwei_01"):
		check_true(bool(found["sk_xuanwei_01"]["battle_usable"]), "对照：玄微剑法·起手能用")
		check_eq(str(found["sk_xuanwei_01"]["battle_note"]), "", "对照：能用的招式不带这条说明")

	# 内功的「特殊效果 id」：表里填了、但没有定义表也没有代码落点（4 部高星内功都是这样）。
	# 面板要如实标出来（且**不能显示 id**），别让玩家以为装了没反应是 bug。
	state.learn_skill(char_id, "pf_drunk_02")     # 醉里乾坤·忘忧 ★5：passive_effect = effect_drunk_mastery
	state.learn_skill(char_id, "pf_xuanwei_01")   # 玄微心法·引气：没有 passive_effect
	var passive_rows: Dictionary = {}
	for row: Dictionary in CharacterSheetScript.new(db, state, char_id).skill_rows():
		passive_rows[str(row["skill_id"])] = row
	check_true(passive_rows.has("pf_drunk_02") and passive_rows.has("pf_xuanwei_01"), "两部内功都在列表里")
	if passive_rows.has("pf_drunk_02"):
		check_true(
			bool(passive_rows["pf_drunk_02"]["special_effect_pending"]),
			"忘忧标了「特殊效果暂未生效」",
		)
	if passive_rows.has("pf_xuanwei_01"):
		check_false(
			bool(passive_rows["pf_xuanwei_01"]["special_effect_pending"]),
			"对照：没填特殊效果的引气不标",
		)


func _check_codex_reward(db) -> void:
	# 图鉴是**全队共有**的收集进度，而这个用例的剧本是「起始 1 部 → 再学 4 部到 5 部」。
	# 队伍人数会随 `character_base` 的行数变化（1 → 3~4），所以这里**钉住单人文书生**，
	# 才能验到「刚好 5 部」那条边界（框架说明决策 161）。
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	state.char_levels[char_id] = 12
	var sheet = CharacterSheetScript.new(db, state, char_id)
	check_eq(sheet.collected_skill_count(), 1, "开局只有起始武学（图鉴 1 部）")
	check_eq(int(sheet.codex_bonus()["times"]), 0, "不到 5 部没有奖励")
	var before_naked := int(sheet.naked_attrs().get("int", 0))
	var before_total := int(sheet.total_attrs().get("int", 0))
	var before_atk := float(sheet.stats().get("atk_phys", 0.0))

	# 再学 4 部凑够 5 部（用表里真实存在的武学 id，挑没有门槛的）
	for skill_id: String in ["sk_wudu_02", "sk_bandit_slash", "sk_bandit_scout", "sk_chensha_02"]:
		state.learn_skill(char_id, skill_id)
	check_eq(sheet.collected_skill_count(), 5, "凑够 5 部")
	check_eq(int(sheet.codex_bonus()["times"]), 1, "5 部给一次奖励")
	check_eq(int(sheet.codex_bonus()["bonus"]), 1, "一次奖励七项各 +1")
	check_eq(int(sheet.naked_attrs().get("int", 0)), before_naked + 1, "裸属性也涨（判定值跟着涨）")
	check_eq(int(sheet.total_attrs().get("int", 0)), before_total + 1, "五维合计涨 1")
	check_gt(float(sheet.stats().get("atk_phys", 0.0)), before_atk, "派生数值跟着涨（内功取智）")

	# 门槛与面板必须一致：把智抬到刚好够「五毒掌·蚀骨（智 12）」这条线
	var need: int = 12 - int(sheet.loadout_attrs()["int"])
	for index in range(maxi(0, need)):
		state.spend_point(db, char_id, "int")
	var loadout = SkillLoadoutScript.new(db, state, char_id)
	check_eq(
		int(loadout.attrs()["int"]), int(sheet.loadout_attrs()["int"]),
		"门槛看到的智与面板 loadout 一致（图鉴奖励两边都算）",
	)
	check_true(
		bool(loadout.requirement_of("sk_wudu_03")["ok"]),
		"智到线了，门槛放行：%s" % str(loadout.requirement_of("sk_wudu_03")["reason"]),
	)

	# 全收集：把表里所有武学都记下（`learn_skill` 是低层记录器，不过门槛）
	for row: Resource in db.rows("skill_base"):
		state.learn_skill(char_id, str(row.skill_id))
	var total_skills: int = db.rows("skill_base").size()
	check_eq(sheet.collected_skill_count(), total_skills, "表里 %d 部全部计入图鉴" % total_skills)
	var full: Dictionary = sheet.codex_bonus()
	# 全收集的奖励次数 = min(部数 ÷ 步长, 上限)：表里现在 52 部 → 52/5 = 10，正好压在上限上
	var raw_times := int(floor(float(total_skills) / 5.0))
	var expect_times := mini(raw_times, int(full["cap"]))
	check_eq(
		int(full["times"]), expect_times,
		"全收集 %d 部 → %d 次奖励（÷5 与上限 %d 取小）" % [total_skills, expect_times, int(full["cap"])],
	)
	check_eq(int(full["bonus"]), expect_times, "每次 +1，全收集共 +%d" % expect_times)


## 面板上的武学：槽位与公式一致、每行带「装了没／为什么装不上」，并占用同一套装配数据
func _check_loadout(db, state) -> void:
	var char_id := str(state.char_ids[0])
	var sheet = CharacterSheetScript.new(db, state, char_id)
	var summary: Dictionary = sheet.slot_summary()
	# 这一步之前用例把角色抬到了 10 级（见 _check_five_attributes）：
	# 招式槽 2 + 10/5 + 悟10/8 = 5，内功容量 2 + 10/6 + 根5/10 = 3
	check_eq(int(summary["active_slots"]), 5, "10 级书生招式槽 5")
	check_eq(int(summary["passive_capacity"]), 3, "10 级书生内功容量 3")
	check_eq(int(summary["active_used"]), 1, "起始武学占着一个招式槽")
	check_eq(int(summary["passive_used"]), 0, "内功容量还空着")

	var rows: Array = sheet.skill_rows()
	check_eq(rows.size(), 1, "开局只有一部已学武学")
	if rows.is_empty():
		return
	var row: Dictionary = rows[0]
	check_eq(str(row["skill_id"]), "sk_xuanwei_01", "列出的是起始武学")
	check_true(bool(row["equipped"]), "标记为已装配")
	check_eq(str(row["equip_error"]), "", "已装配的行没有错误原因")

	# 卸下之后：行变成未装配，且给出「已经装配」以外的原因（槽位空着所以能再装）
	sheet.loadout().unequip("sk_xuanwei_01")
	var after = CharacterSheetScript.new(db, state, char_id)
	check_eq(int(after.slot_summary()["active_used"]), 0, "卸下后面板槽位空出")
	var after_row: Dictionary = after.skill_rows()[0]
	check_false(bool(after_row["equipped"]), "卸下后不再是已装配")
	check_eq(str(after_row["equip_error"]), "", "能装回来时不给错误原因")
	after.loadout().equip("sk_xuanwei_01")


func _check_basics(db, state, sheet) -> void:
	check_true(sheet.valid(), "角色模板有效")
	check_eq(sheet.display_name(), "家道失落的书生", "显示名取自模板")
	check_eq(sheet.weapon_type_id(), "sword", "模板武器类型")
	check_eq(sheet.weapon_type_name(), "剑", "武器类型显示名")
	check_eq(sheet.level, state.level_of(str(state.char_ids[0])), "等级取自存档")
	check_eq(sheet.role_tag(), "探索型", "出身标记")


func _check_five_attributes(db, state, sheet) -> void:
	var base: Dictionary = sheet.base_attrs()
	var naked: Dictionary = sheet.naked_attrs()
	var total: Dictionary = sheet.total_attrs()
	check_eq(int(naked["str"]), int(base["str"]), "没加点时裸值等于模板值")
	check_eq(int(total["agi"]), int(naked["agi"]), "初始武器没有属性点加成")

	# 加一点力：裸值与合计都涨
	state.spend_point(db, str(state.char_ids[0]), "str")
	sheet = CharacterSheetScript.new(db, state, str(state.char_ids[0]))
	check_eq(int(sheet.naked_attrs()["str"]), int(base["str"]) + 1, "加点进入裸值")

	# 换上一件带属性的装备：合计涨、裸值不变
	var char_id := str(state.char_ids[0])
	state.char_levels[char_id] = 10  # 青锋剑要 5 级，抬到 10 级一次说清
	sheet = CharacterSheetScript.new(db, state, char_id)
	var sword: String = state.inventory.add_equipment(db, "eq_sword_02")
	state.inventory.equip(db, char_id, sheet.level, "sword", sword)
	sheet = CharacterSheetScript.new(db, state, char_id)
	check_eq(int(sheet.total_attrs()["agi"]), int(sheet.naked_attrs()["agi"]) + 2, "装备的敏 +2 只进合计")


func _check_stats_match_calculator(db, state, sheet) -> void:
	var calculator = AttributeCalculatorScript.new(db)
	var expected: Dictionary = calculator.compute(
		sheet.level, sheet.base_attrs(), sheet.allocations(), sheet.contributions()
	)
	var actual: Dictionary = sheet.stats()
	for stat_id in ["atk_phys", "atk_qi", "hp_max", "def_phys", "speed"]:
		check_eq(int(actual[stat_id]), int(expected[stat_id]), "%s 与属性计算器一致" % stat_id)


func _check_event_check(db, state, sheet) -> void:
	# 文学 3 + floor(智裸值 10 / 5) = 5
	var wenxue: Dictionary = sheet.event_check_value("skill:wenxue")
	check_eq(int(wenxue["value"]), 5, "文学判定 = 等级 3 + 智裸值/5")
	check_true(bool(wenxue["naked"]), "判定值标记为裸属性")

	var qimen: Dictionary = sheet.event_check_value("skill:qimen")
	check_eq(int(qimen["value"]), 3, "奇门判定 = 等级 1 + 智裸值/5 = 3")

	var strength: Dictionary = sheet.event_check_value("attr:str")
	check_eq(int(strength["value"]), int(sheet.naked_attrs()["str"]), "属性判定取裸值")

	# 判定只用裸属性：换装不改变判定值
	var char_id := str(state.char_ids[0])
	state.char_levels[char_id] = 10  # 药王玉佩要 6 级
	sheet = CharacterSheetScript.new(db, state, char_id)
	var before := int(sheet.event_check_value("skill:wenxue")["value"])
	var int_ring: String = state.inventory.add_equipment(db, "eq_acc_02")
	state.inventory.equip(db, char_id, sheet.level, sheet.weapon_type_id(), int_ring)
	var after_sheet = CharacterSheetScript.new(db, state, char_id)
	check_eq(int(after_sheet.event_check_value("skill:wenxue")["value"]), before, "换装不改变判定值（判定只用裸属性）")
	check_gt(float(after_sheet.total_attrs()["int"]), float(after_sheet.naked_attrs()["int"]), "但装备确实加了智的合计")

	var rows: Array = after_sheet.event_skill_rows()
	check_eq(rows.size(), 5, "五项目非战斗技能都在面板上")
	var wenxue_row: Dictionary = {}
	for row: Dictionary in rows:
		if row["skill_id"] == "wenxue":
			wenxue_row = row
	check_eq(int(wenxue_row.get("level", -1)), 3, "文学初始等级 3")
	check_eq(str(wenxue_row.get("related_attr_name", "")), "智", "文学关联属性是智")


func _check_allocation(db, state) -> void:
	var fresh = GameStateScript.new_game(db, "normal")
	var char_id := str(fresh.char_ids[0])
	# 1 级给 5 点（level_growth.upgrade_points；设计 0.13.0 由 3 提到 5）
	check_eq(fresh.available_points(db, char_id), 5, "1 级有 5 点可分配")
	check_true(bool(fresh.spend_point(db, char_id, "agi")["ok"]), "加点成功")
	check_true(bool(fresh.spend_point(db, char_id, "agi")["ok"]), "再加一点")
	check_true(bool(fresh.spend_point(db, char_id, "con")["ok"]), "第三点也能加")
	check_true(bool(fresh.spend_point(db, char_id, "int")["ok"]), "第四点也能加")
	check_true(bool(fresh.spend_point(db, char_id, "luk")["ok"]), "第五点也能加")
	check_eq(fresh.available_points(db, char_id), 0, "用完就没有了")
	var overflow: Dictionary = fresh.spend_point(db, char_id, "agi")
	check_false(overflow["ok"], "没有点数时加点失败")
	check_eq(int(fresh.allocations_of(char_id)["agi"]), 2, "加点记在存档里")
	var bad: Dictionary = fresh.spend_point(db, char_id, "luck")
	check_false(bad["ok"], "不存在的属性不能加")
	# 设计 0.13.0：悟性／根骨是「资质」——创建时定死，只能靠图鉴奖励与特定内功提升
	var talent: Dictionary = fresh.spend_point(db, char_id, "wu")
	check_false(bool(talent["ok"]), "悟性是不可加点的资质")
	check_true(str(talent["error"]).contains("资质"), "原因写的是资质而不是「没有点数」：%s" % talent["error"])
	check_eq(int(fresh.allocations_of(char_id).get("wu", 0)), 0, "被拒之后没写进存档")
	check_false(str(fresh.allocation_block_reason(db, "gen")).is_empty(), "根骨同样算资质")
	check_eq(str(fresh.allocation_block_reason(db, "str")), "", "五维可以加点")

	# 升级后点数变多：1~3 级每级 5 点 → 累计 15，前面已经用掉 5 点
	fresh.char_levels[char_id] = 3
	check_eq(fresh.available_points(db, char_id), 10, "3 级累计 15 点，已用 5 点，剩 10 点")


func _check_views(sheet) -> void:
	var slots: Array = sheet.equipped_rows()
	check_eq(slots.size(), 8, "7 类槽位＋戒指多一格 = 8 行")
	var weapon_rows := 0
	var empty_rows := 0
	for row: Dictionary in slots:
		if row["slot_id"] == "weapon":
			weapon_rows += 1
			check_false(bool(row["empty"]), "武器槽装了初始武器")
			check_true(str(row["summary"]).contains("外功攻击"), "槽位摘要显示关键属性")
		if bool(row["empty"]):
			empty_rows += 1
	check_eq(weapon_rows, 1, "武器槽只有一格")
	check_gt(float(empty_rows), 0.0, "没装备的槽位标记为空")

	var skills: Array = sheet.skill_rows()
	check_eq(skills.size(), 1, "模板带一门起始武学")
	check_eq(str(skills[0]["skill_id"]), "sk_xuanwei_01", "起始武学是玄微剑法·起手")
	check_true(bool(skills[0]["battle_usable"]), "起始武学在战斗里能用（有伤害类型且倍率 > 0）")
	check_eq(str(skills[0]["battle_note"]), "", "能用的招式不挂「用不出来」的说明")

	var hit_label: String = sheet.stat_label("hit_rate")
	check_true(hit_label.contains("%"), "百分比类派生数值按百分比显示")
	check_eq(sheet.stat_name("atk_phys"), "外功攻击", "派生数值显示名")
