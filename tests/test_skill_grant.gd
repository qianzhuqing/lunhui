## 武学来源：击败指名敌人时「领悟」，以及门槛不足时的拒绝。
##
## 本轮只实现 skill_base.source_type=drop（source_id = enemy_base.enemy_id），
## 其余来源（npc／story／hidden／item／shop）要 NPC、剧情、秘籍与商店，未做。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const SkillGrantScript := preload("res://src/core/skill_grant.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const EncounterScript := preload("res://src/core/encounter.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")

const BATTLE_SCENE := "res://scenes/battle_screen.tscn"
const CHAR_ID := "scholar_fallen"


func suite_name() -> String:
	return "武学来源与领悟"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	_check_source_lookup(db)
	_check_grant(db, state)
	_check_requirement_blocks(db, state)
	_check_hidden_source(db)
	_check_skillbook(db, state)
	_check_battle_flow(db)


func _check_source_lookup(db) -> void:
	var scout: PackedStringArray = SkillGrantScript.skills_from_enemy(db, "en_bd_scout")
	check_true(scout.has("sk_bandit_scout"), "巡山兵身上带黑风刀法·劈山")
	check_true(SkillGrantScript.skills_from_enemy(db, "en_bd_missing").is_empty(), "不存在的敌人不掉武学")
	# npc 还没接（要对话表）：即便 source_id 撞上了也不该被当成掉落
	check_true(SkillGrantScript.skills_from_enemy(db, "xuanwei").is_empty(), "未实现的来源不发放")
	check_true(SkillGrantScript.skills_from_source(db, "npc", "xuanwei").is_empty(), "npc 来源未接入")
	# 0.22.0 起 story 接上了（设计 18 §3.1）：`source_id` 指向 story_node 的节点，
	# 6 部原来悬空的武学由此有落地处（这条断言以前写的是"未接入"，随实现翻转）
	check_eq(SkillGrantScript.skills_from_source(db, "story", "chapter1_end").size(), 3,
		"story 来源已接入：chapter1_end 带 3 部武学")
	check_eq(SkillGrantScript.skills_from_source(db, "hidden", "trig_wine").size(), 3, "醉刀客点位带 3 部武学")
	check_eq(SkillGrantScript.skills_from_source(db, "item", "item_scroll_wudu").size(), 2, "五毒残页带招式与内功各一")


## 隐藏点位（source_type=hidden）：触发成功就按门槛发放
func _check_hidden_source(db) -> void:
	var state = solo_state(db)
	# 1 级书生：悟 10／根 5，三部都要 12~18 → 全被门槛拦住，但要说清为什么
	var blocked: Array = SkillGrantScript.grant_from_source(db, state, "hidden", "trig_wine")
	check_eq(blocked.size(), 3, "三部武学都进了结果（好告诉玩家差多少）")
	for entry: Dictionary in blocked:
		check_true(PackedStringArray(entry["learned"]).is_empty(), "%s 没学会" % str(entry["name"]))
		check_gt(float(Array(entry["blocked"]).size()), 0.0, "%s 写明被门槛挡住" % str(entry["name"]))
	check_true(str(SkillGrantScript.summarize(blocked)).contains("未习得"), "汇总里写「未习得」")
	check_false(state.is_learned(CHAR_ID, "sk_drunk_zuibu"), "被挡住的不写进已学")

	# 悟性是**资质**（设计 0.13.0：不能加点），第一章能把它抬到 12 的现成途径是图鉴奖励。
	# 这条验的是「门槛放行之后学不学得会」，所以用复制模板行的夹具直接把资质给到位。
	var raised_db = table_with_talents(12, 5)
	var raised_state = solo_state(raised_db)
	var mixed: Array = SkillGrantScript.grant_from_source(raised_db, raised_state, "hidden", "trig_wine")
	var learned_names := PackedStringArray()
	for entry: Dictionary in mixed:
		for char_id: String in PackedStringArray(entry["learned"]):
			learned_names.append(str(entry["name"]))
	check_true(learned_names.has("醉里乾坤·醉步"), "悟性 12 后学会醉步：%s" % str(learned_names))
	check_true(raised_state.is_learned(CHAR_ID, "sk_drunk_zuibu"), "醉步写进已学")
	check_false(raised_state.is_learned(CHAR_ID, "sk_drunk_jiuzhongdao"), "悟性还够不到 ★4 的酒中刀")


## 秘籍（source_type=item）：研读学会并消耗一本；门槛不够就不消耗
func _check_skillbook(db, state) -> void:
	# state 是前面几步用过的存档：先看清包里有没有残页
	state.inventory.add_item(db, "item_scroll_wudu", 1)
	var before: int = state.inventory.count("item_scroll_wudu")
	check_eq(before, 1, "背包里有 1 张五毒残页")

	# 智 10 < 12：研读被门槛挡住，而且**不消耗**（别浪费稀有残页）
	var refused: Dictionary = SkillGrantScript.study(db, state, "item_scroll_wudu")
	check_false(bool(refused["ok"]), "智不够时研读不了")
	check_true(str(refused["error"]).contains("修习门槛"), "说明是修习门槛：%s" % refused["error"])
	check_eq(state.inventory.count("item_scroll_wudu"), 1, "门槛不够不消耗秘籍")

	# 智抬到 12：学会蚀骨（招式），蚀脉要根骨 12 还差一点
	var need: int = 12 - int(CharacterSheetScript.new(db, state, CHAR_ID).total_attrs()["int"])
	for index in range(maxi(0, need)):
		state.spend_point(db, CHAR_ID, "int")
	var studied: Dictionary = SkillGrantScript.study(db, state, "item_scroll_wudu")
	check_true(bool(studied["ok"]), "智 12 后研读成功：%s" % studied["error"])
	check_true(state.is_learned(CHAR_ID, "sk_wudu_03"), "学会五毒掌·蚀骨")
	check_eq(state.inventory.count("item_scroll_wudu"), 0, "研读成功消耗一本")
	check_true(str(studied["error"]).contains("没学会") or str(studied["error"]).is_empty(), "部分没学会时也说明：%s" % studied["error"])

	# 没有残页时给明确提示
	var missing: Dictionary = SkillGrantScript.study(db, state, "item_scroll_wudu")
	check_false(bool(missing["ok"]), "没残页研读不了")
	check_true(str(missing["error"]).contains("没有"), "说明背包里没有：%s" % missing["error"])

	# 钥匙类的残卷（醉里乾坤）现在没有对应武学：集齐机制未做，提示要说清
	state.inventory.add_item(db, "item_scroll_drunk", 1)
	var no_skill: Dictionary = SkillGrantScript.can_study(db, state, "item_scroll_drunk")
	check_false(bool(no_skill["ok"]), "没有对应武学的残卷不能研读")
	check_true(str(no_skill["error"]).contains("集齐"), "说明要集齐：%s" % no_skill["error"])


func _check_grant(db, state) -> void:
	var granted: Array = SkillGrantScript.grant_from_defeated(db, state, PackedStringArray(["en_bd_scout"]))
	check_eq(granted.size(), 1, "击败巡山兵只领悟一部武学")
	if granted.is_empty():
		return
	var entry: Dictionary = granted[0]
	check_eq(str(entry["skill_id"]), "sk_bandit_scout", "领悟的是黑风刀法·劈山")
	check_true(PackedStringArray(entry["learned"]).has(CHAR_ID), "写进已学")
	check_true(state.is_learned(CHAR_ID, "sk_bandit_scout"), "存档里能查到")
	check_true(str(SkillGrantScript.describe(entry)).contains("领悟"), "结算文案说明领悟：%s" % SkillGrantScript.describe(entry))

	# 同一支队伍再打一遍：已经学过就不再刷屏
	var again: Array = SkillGrantScript.grant_from_defeated(db, state, PackedStringArray(["en_bd_scout"]))
	check_true(again.is_empty(), "已学过的武学不再重复播报")

	# 一部敌人带多部武学（喽啰：黑风刀法·乱劈 + 沉沙掌·裂石 + 黑风心法·蛮力）
	var thug: Array = SkillGrantScript.grant_from_defeated(db, state, PackedStringArray(["en_bd_thug"]))
	check_eq(thug.size(), 3, "喽啰同时掉两部招式与一部内功")

	# 武器绑定：领会来的黑风刀法是刀招，书生拿的是剑 → 装配被拒（与战斗里的选招规则一致）
	var loadout = SkillLoadoutScript.new(db, state, CHAR_ID)
	var blade: Dictionary = loadout.can_equip("sk_bandit_scout")
	check_false(bool(blade["ok"]), "刀招装不到剑客身上")
	check_true(str(blade["error"]).contains("武器不符"), "拒绝理由是武器不符：%s" % blade["error"])

	# 通用招式（弓手的冷箭是 any）才装得上
	SkillGrantScript.grant_from_defeated(db, state, PackedStringArray(["en_bd_archer"]))
	var equip: Dictionary = loadout.equip("sk_bandit_arrow")
	check_true(bool(equip["ok"]), "通用招式能马上装配：%s" % equip["error"])
	check_true(loadout.active_ids().has("sk_bandit_arrow"), "装配表里有它")


func _check_requirement_blocks(db, state) -> void:
	# 黑风刀法·夜雨（drop en_bd_boss）★4 要力 18；巡山队长带的 ★3 招式要悟 12
	# 巡山队长带 3 部★3 武学（悟 12 / 力 12 / 根骨 12），1 级书生一个都够不着
	var blocked: Array = SkillGrantScript.grant_from_defeated(db, state, PackedStringArray(["en_bd_patrol_leader"]))
	check_eq(blocked.size(), 3, "门槛不够的武学也要进结算（好告诉玩家为什么没学到）")
	if blocked.is_empty():
		return
	var entry: Dictionary = blocked[0]
	for each: Dictionary in blocked:
		check_true(PackedStringArray(each["learned"]).is_empty(), "门槛不够时没学会")
	check_gt(float(Array(entry["blocked"]).size()), 0.0, "列出被门槛挡住的角色")
	var reason := str(Array(entry["blocked"])[0]["reason"])
	check_true(reason.contains("修习门槛"), "原因写明修习门槛：%s" % reason)
	check_true(str(SkillGrantScript.describe(entry)).contains("没能领悟"), "结算文案也说明没学会")
	check_false(state.is_learned(CHAR_ID, "sk_xuanwei_05"), "被挡住就不写进已学")


## 真实战斗闭环：打山贼巡山队 → 胜利结算里出现「领悟」
func _check_battle_flow(db) -> void:
	if scene_tree == null:
		fail("没有注入场景树，战斗闭环无法进行")
		return
	var state = solo_state(db)
	# 造一个打得赢的队伍：升到 12 级，点数全塞力量
	state.char_levels[CHAR_ID] = 12
	for index in range(33):
		state.spend_point(db, CHAR_ID, "str")

	var spawn_row: Resource = db.get_row("roaming_spawn", "sp_lp_patrol_01")
	check_not_null(spawn_row, "有巡山队的明雷")
	if spawn_row == null:
		return
	var team_row: Resource = db.get_row("enemy_team", str(spawn_row.team_id))
	var encounter = EncounterScript.build(db, spawn_row, team_row, EncounterScript.CONTACT_FRONT, "normal")
	var session_node := scene_tree.root.get_node_or_null("GameSession")
	check_not_null(session_node, "GameSession 在场")
	if session_node == null:
		return
	session_node.pending_encounter = encounter

	var battle = load(BATTLE_SCENE).instantiate()
	battle.state_override = state
	# 随机种子固定：掉落件数是掷出来的，不固定的话「结算卡片里有没有这一行」会时红时绿
	battle.rng_seed = 20261003
	scene_tree.root.add_child(battle)
	battle.setup()
	check_eq(battle.enemies.size(), 3, "巡山队 3 人")
	var guard := 0
	while not battle.finished() and guard < 200:
		var actor = battle.sim.current_actor() if battle.sim.in_round() else null
		if actor != null and battle.allies.has(actor):
			var skills: Array = battle.sim.available_skills(actor)
			if skills.is_empty():
				battle.press_next_round()
			else:
				battle.press_skill(str(skills[0].skill_id))
		else:
			battle.press_next_round()
		guard += 1
	check_true(battle.finished(), "12 级的队伍能打完巡山队")
	var result_text := str(battle.result_label_text())
	var battle_log := str(battle._log.get_parsed_text())
	check_true(result_text.contains("胜利"), "打赢了：%s" % result_text)
	# 卡片只有三行，放不下的行进战报——所以「播报」要卡片或战报里有一处
	check_true(
		result_text.contains("领悟") or battle_log.contains("领悟"),
		"结算里播报领悟（卡片或战报）：%s" % result_text,
	)
	check_true(
		state.is_learned(CHAR_ID, "sk_chensha_02") or state.is_learned(CHAR_ID, "sk_bandit_arrow"),
		"喽啰或弓手的武学进了已学",
	)
	scene_tree.root.remove_child(battle)
	battle.free()
	session_node.pending_encounter = null
