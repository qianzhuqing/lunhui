## 武学：修习门槛、招式槽、内功占格、装配与卸下、存档往返。
##
## 规则出处 docs/design/01_角色系统.md：招式槽 2+等级/5+悟性/8（上限 9）、
## 内功容量 2+等级/6+根骨/10（上限 7）、内功按星级占 1~3 格、高星武学要属性门槛。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")

const CHAR_ID := "scholar_fallen"


func suite_name() -> String:
	return "武学装配"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)

	_check_starting(db, state, loadout)
	_check_learn_requirement(db, state)
	_check_weapon_requirement(db, state)
	_check_active_slots(db, state)
	_check_passive_capacity(db, state)
	_check_passive_bonus(db, state)
	_check_round_trip(db, state)
	_check_no_self_loop(db, state)


## 开局：已学 = 模板的起始武学，装配已经铺好，槽位按公式算
func _check_starting(db, state, loadout) -> void:
	check_true(loadout.valid(), "装配对象有效")
	check_true(state.is_learned(CHAR_ID, "sk_xuanwei_01"), "模板起始武学算已学")
	check_false(state.is_learned(CHAR_ID, "sk_xuanwei_05"), "没学过的武学不算已学")
	check_eq(loadout.learned_ids().size(), 1, "开局只会一部（角色卡的起始武学）")
	check_true(loadout.active_ids().has("sk_xuanwei_01"), "起始武学默认装在招式槽里")
	check_true(loadout.passive_ids().is_empty(), "开局没有内功")
	check_eq(loadout.active_slots(), 3, "1 级书生招式槽 3（2 + 0 + 悟10/8）")
	check_eq(loadout.passive_capacity(), 2, "1 级书生内功容量 2（2 + 0 + 根5/10）")
	check_eq(state.collected_skill_count(), 1, "图鉴计数按已学去重")


## 修习门槛：★3 招式要悟性 12，1 级书生只有 10 → 学不了；加到 12 就能学
func _check_learn_requirement(db, state) -> void:
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	var requirement: Dictionary = loadout.requirement_of("sk_xuanwei_05")
	check_false(bool(requirement["ok"]), "悟性不足时门槛不过")
	check_eq(str(requirement["attr"]), "wu", "门槛属性是悟性")
	check_eq(int(requirement["value"]), 12, "门槛值取表里的 12")
	check_true(str(requirement["reason"]).contains("悟性"), "原因里写清要什么属性：%s" % requirement["reason"])

	var refused: Dictionary = loadout.learn("sk_xuanwei_05")
	check_false(bool(refused["ok"]), "门槛不足时学不会")
	check_true(str(refused["error"]).contains("修习门槛"), "给出修习门槛的提示：%s" % refused["error"])
	check_false(state.is_learned(CHAR_ID, "sk_xuanwei_05"), "被拒之后没写进已学")

	# 悟性是**资质**（设计 0.13.0：不能加点），第一条能把它抬到 12 的现成途径是图鉴奖励。
	# 这里用「复制模板行、直接给资质」的夹具，而不是往这份**共用存档**里塞 10 部武学——
	# 那会把图鉴奖励（七项 +2）一起带给后面的用例，把「书生根骨 5 不够 ★2 门槛」那条挤掉。
	var raised_db = table_with_talents(12, 5)
	var raised_state = solo_state(raised_db)
	var raised = SkillLoadoutScript.new(raised_db, raised_state, CHAR_ID)
	check_eq(int(raised.attrs()["wu"]), 12, "夹具把悟性给到 12（对应图鉴奖励那条通道）")
	check_true(bool(raised.requirement_of("sk_xuanwei_05")["ok"]), "悟性 12 后门槛过了")
	var learned: Dictionary = raised.learn("sk_xuanwei_05")
	check_true(bool(learned["ok"]) and bool(learned["new"]), "过了门槛就能学会")
	check_true(raised_state.is_learned(CHAR_ID, "sk_xuanwei_05"), "学会后写进已学")
	var again: Dictionary = raised.learn("sk_xuanwei_05")
	check_true(bool(again["ok"]) and not bool(again["new"]), "重复学不算失败也不算新学")


## 武器类型：拿剑时装不了拳法；卸下剑连剑招也装不上，但通用招式任何时候都能装
func _check_weapon_requirement(db, state) -> void:
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	loadout.learn("sk_chensha_01")
	loadout.learn("sk_common_01")

	var fist: Dictionary = loadout.can_equip("sk_chensha_01")
	check_false(bool(fist["ok"]), "拿剑时拳法装不上")
	check_true(str(fist["error"]).contains("武器不符"), "说明是武器不符：%s" % fist["error"])

	var common: Dictionary = loadout.can_equip("sk_common_01")
	check_true(bool(common["ok"]), "通用招式任何武器都能装")

	# 卸下武器：剑招装不上，通用招式照装
	var instance_id: String = state.inventory.equipped_instances(db, CHAR_ID)[0]
	state.inventory.unequip(CHAR_ID, "weapon", 0)
	var bare = SkillLoadoutScript.new(db, state, CHAR_ID)
	check_true(str(bare.equipped_weapon_type()).is_empty(), "卸下后身上没武器")
	check_false(bool(bare.can_equip("sk_xuanwei_02")["ok"]), "空手装不了剑招")
	check_true(bool(bare.can_equip("sk_common_01")["ok"]), "空手也能装通用招式")
	# 装回去，后面的用例还要用剑
	state.inventory.equip(db, CHAR_ID, 1, "sword", instance_id)


## 招式槽上限：装满 3 个之后第 4 个被拒；卸一个又能装
func _check_active_slots(db, state) -> void:
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	for skill_id: String in ["sk_xuanwei_02", "sk_xuanwei_03", "sk_xuanwei_04", "sk_common_02"]:
		loadout.learn(skill_id)
	# 先清空装配，再按槽位一个个装
	for skill_id: String in loadout.active_ids():
		loadout.unequip(skill_id)
	check_eq(loadout.active_used(), 0, "清空后招式槽为空")

	var filled := 0
	for skill_id: String in ["sk_xuanwei_01", "sk_xuanwei_02", "sk_xuanwei_03"]:
		var result: Dictionary = loadout.equip(skill_id)
		if bool(result["ok"]):
			filled += 1
	check_eq(filled, 3, "前 3 个都装上了")
	check_eq(loadout.active_used(), loadout.active_slots(), "招式槽正好装满")

	# 第 4 个（通用、武器不限）应该被槽位拦住
	var fourth := ""
	for skill_id: String in loadout.learned_ids():
		if loadout.active_ids().has(skill_id):
			continue
		if str(db.get_row("skill_base", skill_id).skill_kind) != "active":
			continue
		if bool(loadout.can_equip(skill_id)["ok"]) or "招式槽已满" in str(loadout.can_equip(skill_id)["error"]):
			fourth = skill_id
			break
	check_ne(fourth, "", "找到一个可比较的第 4 个招式")
	if fourth.is_empty():
		return
	var overflow: Dictionary = loadout.can_equip(fourth)
	check_false(bool(overflow["ok"]), "招式槽满了装不上第 4 个")
	check_true(str(overflow["error"]).contains("招式槽已满"), "说明是槽位满：%s" % overflow["error"])

	var removed: Dictionary = loadout.unequip("sk_xuanwei_01")
	check_true(bool(removed["ok"]), "能卸下招式")
	check_eq(loadout.active_used(), 2, "卸下后少一个")
	check_true(bool(loadout.equip(fourth)["ok"]), "腾出槽位后第 4 个能装上")
	check_true(bool(loadout.unequip(fourth)["ok"]), "收尾：把它卸掉")


## 内功占格：容量 2 格，装 2 部★1 就满；★2 内功要根骨 8
func _check_passive_capacity(db, state) -> void:
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	loadout.learn("pf_xuanwei_01")
	loadout.learn("pf_wudu_01")
	check_eq(loadout.passive_slot_cost("pf_xuanwei_01"), 1, "★1 内功占 1 格")
	check_eq(loadout.passive_capacity(), 2, "容量 2 格")

	check_true(bool(loadout.equip("pf_xuanwei_01")["ok"]), "第 1 部内功装得上")
	check_eq(loadout.passive_used(), 1, "占 1 格")
	check_true(bool(loadout.equip("pf_wudu_01")["ok"]), "第 2 部内功装得上")
	check_eq(loadout.passive_used(), 2, "占满 2 格")

	loadout.learn("pf_yaowang_01")
	var overflow: Dictionary = loadout.can_equip("pf_yaowang_01")
	check_false(bool(overflow["ok"]), "容量满了装不上第 3 部")
	check_true(str(overflow["error"]).contains("内功容量不足"), "说明是容量不足：%s" % overflow["error"])

	var summary: Dictionary = loadout.slot_summary()
	check_eq(int(summary["passive_used"]), 2, "面板上的内功占格数正确")
	check_eq(int(summary["passive_capacity"]), 2, "面板上的内功容量正确")

	# ★2 内功要根骨 8：书生只有 5，门槛不过
	check_true(state.is_learned(CHAR_ID, "pf_xuanwei_01"), "内功也能学会")
	var gated: Dictionary = loadout.requirement_of("pf_xuanwei_03")
	check_false(bool(gated["ok"]), "根骨不够时 ★2 内功门槛不过")
	check_eq(str(gated["attr"]), "gen", "★2 内功门槛属性是根骨")
	check_eq(int(gated["value"]), 8, "门槛值取星级表默认值 8")


## 内功「装备即生效」：加了内力上限，卸下就还原；判定值不受装配影响
func _check_passive_bonus(db, state) -> void:
	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	var before: float = sheet.stat_value("qi_max")
	sheet.loadout().unequip("pf_xuanwei_01")
	var without: float = CharacterSheetScript.new(db, state, CHAR_ID).stat_value("qi_max")
	sheet.loadout().equip("pf_xuanwei_01")
	var with_passive: float = CharacterSheetScript.new(db, state, CHAR_ID).stat_value("qi_max")
	check_float(without, before - 30.0, "卸下内功后内力上限掉 30")
	check_float(with_passive, before, "装回内功后内力上限还原")

	# 内功给的是派生数值不是五维：判定值（只用裸属性）不该变
	var naked_before: Dictionary = sheet.naked_attrs()
	sheet.loadout().unequip("pf_xuanwei_01")
	var naked_after: Dictionary = CharacterSheetScript.new(db, state, CHAR_ID).naked_attrs()
	check_eq(naked_after, naked_before, "内功加成不进裸属性（判定值不受装配影响）")
	sheet.loadout().equip("pf_xuanwei_01")


## 存档往返：已学与装配都要留下；玩家自己卸空的槽不许被迁移逻辑悄悄填上
func _check_round_trip(db, state) -> void:
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	for skill_id: String in loadout.active_ids():
		loadout.unequip(skill_id)
	check_eq(sheet_active(state), 0, "把招式槽清空（模拟玩家主动卸下）")

	var data: Dictionary = state.to_dict()
	var back = GameStateScript.from_dict(data, db)
	check_not_null(back, "装配状态能读回来")
	if back == null:
		return
	check_eq(back.migrated_from, 0, "同版本往返不需要迁移")
	check_true(back.is_learned(CHAR_ID, "sk_xuanwei_01"), "已学列表往返一致")
	check_true(back.is_learned(CHAR_ID, "pf_xuanwei_01"), "内功的已学也往返一致")
	check_eq(back.collected_skill_count(), state.collected_skill_count(), "图鉴计数往返一致")
	check_eq(sheet_active(back), 0, "玩家卸空的招式槽不会被迁移重新填上")
	check_true(
		PackedStringArray(back.loadout_of(CHAR_ID)["passive"]).has("pf_xuanwei_01"),
		"内功装配往返一致",
	)
	check_eq(back.version, GameStateScript.VERSION, "写回的版本是当前版本")


func sheet_active(state) -> int:
	return PackedStringArray(state.loadout_of(CHAR_ID)["active"]).size()


## 内功加成不许反过来影响槽位与门槛：
## ★5 内功（玄微心法·太清）自己给悟性 +5，而悟性正是招式槽的入参。
## 要是槽位按「含内功」的属性算，装哪部内功就变成了顺序游戏（决策 22 明确禁止）。
##
## 0.13.0 起资质（悟性／根骨）不能加点，而第一章也到不了 ★5 内功的门槛（根骨 25），
## 所以这里换成「复制模板行、直接给资质」的内存夹具（`table_with_talents`）。
func _check_no_self_loop(_db, _state) -> void:
	var db = table_with_talents(19, 25)
	var state = solo_state(db)
	state.char_levels[CHAR_ID] = 20
	# 夹具给的是根骨 25（够 ★5 内功门槛与容量）与悟性 19（卡在 /8 的边界上，+5 就会多一格）

	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	check_eq(loadout.passive_capacity(), 7, "20 级根骨 25：内功容量满格 7")
	var slots_before: int = loadout.active_slots()
	var wu_before := int(CharacterSheetScript.new(db, state, CHAR_ID).total_attrs()["wu"])
	check_eq(slots_before, 2 + 4 + wu_before / 8, "招式槽 = 2 + 20/5 + 悟性/8（按当前悟性 %d）" % wu_before)

	var learned: Dictionary = loadout.learn("pf_xuanwei_05")
	check_true(bool(learned["ok"]), "根骨 25 够 ★5 内功的修习门槛：%s" % learned["error"])
	var cost: int = loadout.passive_slot_cost("pf_xuanwei_05")
	var used_before: int = loadout.passive_used()
	var equipped: Dictionary = loadout.equip("pf_xuanwei_05")
	check_true(bool(equipped["ok"]), "★5 内功（占 %d 格）装得上：%s" % [cost, equipped["error"]])
	check_eq(loadout.passive_used(), used_before + cost, "占格数按 slot_cost 增加")

	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	var wu_after := int(sheet.total_attrs()["wu"])
	check_eq(wu_after, wu_before + 5, "内功的悟性 +5 确实进了五维")
	check_eq(
		wu_after / 8, wu_before / 8 + 1,
		"这个 +5 正好跨过 /8 的整数边界（悟性 %d → %d），所以下一条断言是有意义的" % [wu_before, wu_after],
	)
	check_eq(sheet.loadout().active_slots(), slots_before, "但招式槽不跟着涨（不含内功）")
	check_eq(
		int(sheet.stats()["slot_active"]), slots_before,
		"面板显示的招式槽与判定口径一致（不是 per-面板一个数 per-判定另一个数）",
	)
	check_eq(int(sheet.stats()["passive_capacity"]), 7, "面板显示的内功容量也是 7")
