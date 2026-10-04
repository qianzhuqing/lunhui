## 剧情节点与章节推进（设计 18 §3.1／§3.2，0.22.0）：
## 把「7 个断点」里代码侧能接的那几条钉住。
##
## 这一段的价值在于：**这 6 部武学以前一部都拿不到**（`source_type=story` 没有落地处），
## 而「第一章结束」原来是个硬编码常量。现在条件与效果都在表里，这里逐条验证。
extends "res://tests/test_case.gd"

const GuideServiceScript := preload("res://src/core/guide_service.gd")
const StoryServiceScript := preload("res://src/core/story_service.gd")
const ChapterServiceScript := preload("res://src/core/chapter_service.gd")
const SkillLoadoutScript := preload("res://src/core/skill_loadout.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const GameStateScript := preload("res://src/core/game_state.gd")

const HEIFENGZHAI := "scene_heifengzhai"
const QINGFENGYI := "scene_qingfengyi"


func suite_name() -> String:
	return "剧情节点与章节推进"


func run() -> void:
	var db = get_db()
	_check_supplies_flag(db)
	_check_arrived_flag(db)
	_check_story_claim(db)
	_check_chapter_advance(db)
	_check_choice_bonus(db)
	_check_origin_gift(db)


## 本命机遇（设计 21 §九／0.31.0）：**按出身给**的那部 ★4 内功。
##
## 三条口径各钉一条：① 表里 5 条 `origin_gift`，每条发一部内功；② 条件是
## `origin:<char_id>`（＋该条需要的旗标），所以**只有那个出身的主角**领得到；
## ③ 领到手之后**武学真的学会**（不是空头支票），而且领过就记账、不会重复发。
func _check_origin_gift(db) -> void:
	var nodes: Array = []
	for row: Resource in db.rows("story_node"):
		if str(row.kind) == "origin_gift":
			nodes.append(row)
	check_eq(nodes.size(), 5, "五条本命机遇（五个出身各一条）")
	for row: Resource in nodes:
		var grants := str(row.grant_skill_ids)
		check_false(grants.is_empty(), "%s 发了东西" % row.node_id)
		check_true(
			str(row.trigger_condition).contains("origin:"), "%s 的条件里写出身" % row.node_id
		)
		var skill: Resource = db.get_row("skill_base", grants.split(";")[0])
		check_not_null(skill, "%s 发的内功在表里（%s）" % [row.node_id, grants])
		if skill == null:
			continue
		check_eq(str(skill.source_type), "origin", "%s 的来源类型是 origin" % str(skill.skill_id))
		check_eq(int(skill.star), 4, "%s 是 ★4" % str(skill.skill_id))
		check_eq(int(skill.learn_req_value), 18, "%s 的门槛值 18" % str(skill.skill_id))
		check_false(
			str(skill.learn_req_attr).is_empty(), "%s 写了门槛属性" % str(skill.skill_id)
		)

	# 书生当主角：看过告示后，在清风驿领得到自己那条
	var scholar = GameStateScript.new_game(db, "normal", PackedStringArray(["scholar_fallen"]))
	scholar.set_flag("flag_board_read")
	var scholar_here := _node_ids(StoryServiceScript.pending_for(db, scholar, "scene_qingfengyi"))
	check_true(
		scholar_here.has("opp_scholar"), "书生在清风驿领得到自己的那条（%s）" % str(scholar_here)
	)
	check_false(scholar_here.has("opp_qi"), "白清和那条轮不到他（复命后、且出身不对）")

	# 林铁山当主角：他那条在**落雁坡**（大地图节点，没有小地图）
	var gang = GameStateScript.new_game(db, "normal", PackedStringArray(["ch_gang"]))
	var gang_town := _node_ids(StoryServiceScript.pending_for(db, gang, "scene_qingfengyi"))
	check_false(gang_town.has("opp_gang"), "林铁山在清风驿领不到（他的在落雁坡）")
	var gang_slope := _node_ids(StoryServiceScript.pending_for(db, gang, "n_luoyanpo"))
	check_true(gang_slope.has("opp_gang"), "走到落雁坡就领得到（%s）" % str(gang_slope))

	# **这一步只验「发的是哪一部」**：`granted` 里连「被修习门槛挡下」的条目也一起返回，
	# 所以「真的学会了」要看 `learned`——而 1 级林铁山力只有 12、够不到 18（21 §9.6 ① 的门槛），
	# 这里本来就学不会。**「力加到 18 之后真发到手」那条口径在 `test_overworld` 的真场景用例里钉**
	# （`_check_region_origin_gift`，决策 345）——这一行以前写着「领到手 = 真的学会」，是句空话。
	var claimed: Array = StoryServiceScript.claim_for(db, gang, "n_luoyanpo")
	var granted_ids := PackedStringArray()
	for entry: Dictionary in claimed:
		for item: Dictionary in Array(entry.get("granted", [])):
			granted_ids.append(str(item.get("skill_id", "")))
	check_true(
		granted_ids.has("pf_chensha_buhuan"),
		"领到的是《沉沙心法·不还》（%s）" % str(granted_ids),
	)
	check_true(gang.has_flag(StoryServiceScript.done_flag("opp_gang")), "领过就记账（不会重复发）")
	check_true(
		StoryServiceScript.pending_for(db, gang, "n_luoyanpo").is_empty(),
		"同一条不会第二次进待领名单",
	)


func _node_ids(rows: Array) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in rows:
		out.append(str(row.node_id))
	return out


## 抉择的永久增益（设计 20 §八，0.29.1）：`story_node(kind=choice)` 的增益
## **跟旗标走**，走 `AttributeCalculator` 的贡献通道，**只发给主角**。
##
## 三条口径各自钉一条：① 表里三条抉择都真的带增益（配成空节点会被构建期挡，
## 但"三行都在"是设计状态，值得钉）；② 旗标没点亮时一点加成都没有；
## ③ 点亮后属性点层（悟性／根骨／运）与固定值层（内力／气血上限、掉落机缘）都进面板与战斗。
func _check_choice_bonus(db) -> void:
	var choices: Array = []
	for row: Resource in db.rows("story_node"):
		if str(row.kind) == "choice":
			choices.append(row)
			check_false(row.bonus_contributions().is_empty(), "%s 带着永久增益" % row.node_id)
	check_eq(choices.size(), 3, "账册三选一 = 3 条 kind=choice 节点")

	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	var row: Resource = null
	for candidate: Resource in choices:
		if str(candidate.node_id) == "ledger_public":
			row = candidate
	check_not_null(row, "找得到「清名」那条（呈官）")
	if row == null:
		return
	var before = CharacterSheetScript.new(db, state, char_id)
	var wu_before := int(before.naked_attrs().get("wu", 0))
	var qi_before := float(before.stats().get("qi_max", 0.0))
	check_false(state.has_flag(str(row.trigger_condition)), "开局还没做过这个抉择")

	state.set_flag(str(row.trigger_condition))
	var after = CharacterSheetScript.new(db, state, char_id)
	check_eq(
		int(after.naked_attrs().get("wu", 0)), wu_before + 2,
		"「清名」给悟性 +2（属性点层，进判定与门槛）",
	)
	check_float(
		float(after.stats().get("qi_max", 0.0)), qi_before + 20.0,
		"「清名」给内力上限 +20（固定值层）", 0.01,
	)
	# 面板文案（设计 20 §八 第 1 条：界面要能看见）——名字从表里取，不写死中文
	var labels: PackedStringArray = StoryServiceScript.bonus_labels(db, state, char_id)
	check_true(labels.has("悟性 +2"), "面板文案里写出「悟性 +2」（%s）" % str(labels))
	check_true(labels.has("内力上限 +20"), "面板文案里写出「内力上限 +20」（%s）" % str(labels))
	check_true(
		StoryServiceScript.bonus_labels(db, state, "ch_gang").is_empty(),
		"增益文案同样只给主角",
	)

	# 只给主角：别的 char_id 问同一份服务，拿到的是空
	var other_id := ""
	for candidate: Resource in db.rows("character_base"):
		var candidate_id := str(candidate.char_id)
		if candidate_id != char_id:
			other_id = candidate_id
			break
	check_false(other_id.is_empty(), "表里还有别的角色模板（这条才验得动）")
	if not other_id.is_empty():
		check_true(
			StoryServiceScript.permanent_contributions(db, state, other_id).is_empty(),
			"同一个抉择，问别人（%s）是空的——增益只发给主角" % other_id,
		)


## 备货旗标（设计 18 断点 1）：`flag_supplies_ready` 原来只在表里出现、没人置。
## 判定口径写在 `condition_spec` 里：「等级 ≥ 5 且背包里有回血道具」。
func _check_supplies_flag(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	check_false(state.has_flag(GuideServiceScript.SUPPLIES_FLAG), "开局没备货")
	check_false(GuideServiceScript.supplies_ready(db, state), "1 级 + 空背包 = 没备货")

	# 只有等级、没有消耗品 → 还不算
	state.char_levels[char_id] = 5
	GuideServiceScript.refresh_derived_flags(db, state)
	check_false(state.has_flag(GuideServiceScript.SUPPLIES_FLAG), "光到 5 级、背包空的还是没备货")

	# 有消耗品了 → 旗帜点亮
	state.inventory.add_item(db, "item_med_01", 1)
	var set_now: PackedStringArray = GuideServiceScript.refresh_derived_flags(db, state)
	check_true(state.has_flag(GuideServiceScript.SUPPLIES_FLAG), "5 级 + 带药 → 备货完成")
	check_true(set_now.has(GuideServiceScript.SUPPLIES_FLAG), "返回值告诉调用方刚点亮了哪一枚")
	# 幂等：再算一次不该重复播报
	check_true(GuideServiceScript.refresh_derived_flags(db, state).is_empty(), "已经点亮的不再重复报")
	# 引导 HUD 跟着走到第 4 步（分母按表算，别写死）
	state.set_flag("flag_board_read")
	state.set_flag("flag_ch_ci_joined")
	var want := "4/%d" % GuideServiceScript.steps(db).size()
	check_true(GuideServiceScript.hud_text(db, state).contains(want),
		"备货完成后引导走到第 4 步：%s" % GuideServiceScript.hud_text(db, state))


## 进寨旗标（设计 18 断点 2）：判据是**入口房间**，不是"进了这张图"
## （从后山密道直接下到三层地牢不算"进寨"）。
func _check_arrived_flag(db) -> void:
	var state = solo_state(db)
	var entrance := GuideServiceScript.entrance_room_of(db, HEIFENGZHAI)
	check_true(not entrance.is_empty(), "黑风寨配了入口房间（room_type=entrance）")
	# 后山地牢（隐藏层的房间）不算进寨
	GuideServiceScript.note_room_entered(db, state, HEIFENGZHAI, "hf3_dungeon")
	check_false(state.has_flag(GuideServiceScript.ARRIVED_HEIFENGZHAI_FLAG), "从后山地牢进来不算进寨")
	# 别的图的入口房间也不算
	GuideServiceScript.note_room_entered(db, state, QINGFENGYI, entrance)
	check_false(state.has_flag(GuideServiceScript.ARRIVED_HEIFENGZHAI_FLAG), "别张图的房间不点亮进寨旗标")
	# 真正的入口房间才算
	var set_now: PackedStringArray = GuideServiceScript.note_room_entered(db, state, HEIFENGZHAI, entrance)
	check_true(state.has_flag(GuideServiceScript.ARRIVED_HEIFENGZHAI_FLAG), "走进寨门 → 进寨")
	check_true(set_now.has(GuideServiceScript.ARRIVED_HEIFENGZHAI_FLAG), "返回值告诉调用方点亮了它")


## 剧情节点发武学（设计 18 断点 4／6）：6 部 `source_type=story` 的武学要有落地处，
## `chapter_end` 节点还要把章节完成条件置上。
func _check_story_claim(db) -> void:
	var state = solo_state(db)
	var char_id := str(state.char_ids[0])
	# 条件没点亮 → 领不到
	check_true(StoryServiceScript.pending_for(db, state, QINGFENGYI).is_empty(), "救出沈小姐之前没有剧情节点可领")

	# 点亮「救出沈小姐」→ 回清风驿复命，拿 3 部武学
	state.set_flag("flag_shen_rescued")
	var claimed: Array = StoryServiceScript.claim_for(db, state, QINGFENGYI)
	check_eq(claimed.size(), 1, "清风驿上能领的剧情节点 1 个（chapter1_end）")
	var granted: Array = Array(claimed[0]["granted"]) if claimed.size() > 0 else []
	check_eq(granted.size(), 3, "chapter1_end 发 3 部武学（长歌／破军式／太清）")
	check_true(bool(claimed[0].get("chapter_done", false)), "章节结束节点会置章节完成条件")
	# 1 级书生的资质不够那 3 部的修习门槛（长歌要悟性 25、太清要根骨 25、破军式要力 12）——
	# 这时**必须在返回里写明为什么学不会**（不静默），而不是假装发了
	var blocked_reasons := 0
	for entry: Dictionary in granted:
		var learned: Array = Array(entry.get("learned", []))
		var blocked: Array = Array(entry.get("blocked", []))
		check_true(learned.size() + blocked.size() >= 1,
			"%s：要么学会、要么写明门槛不够" % str(entry.get("skill_id", "")))
		if not blocked.is_empty():
			blocked_reasons += 1
			check_false(str(Dictionary(blocked[0]).get("reason", "")).is_empty(),
				"被门槛挡住时带原因：%s" % str(Dictionary(blocked[0]).get("reason", "")))
	check_gt(float(blocked_reasons), 0.0, "1 级书生确实够不上这几部的门槛（门槛真的在判）")

	# 资质够了就该真的学会（换一份"资质到位"的存档，验证 happy path）
	var ready = solo_state(db)
	ready.char_allocations[char_id] = {"wu": 20, "gen": 25, "str": 12}
	ready.set_flag("flag_shen_rescued")
	var claimed2: Array = StoryServiceScript.claim_for(db, ready, QINGFENGYI)
	var loadout = SkillLoadoutScript.new(db, ready, char_id)
	var all_learned := claimed2.size() == 1
	for skill_id: String in ["sk_xuanwei_08", "sk_common_02", "pf_xuanwei_05"]:
		if not loadout.is_learned(skill_id):
			all_learned = false
	check_true(all_learned, "资质到位后这 3 部真的学会（章节奖励不是空头支票）")
	# 一次性：再领一次什么都没有
	check_true(StoryServiceScript.claim_for(db, state, QINGFENGYI).is_empty(), "剧情节点只能领一次")

	# 「不限地点」的门派节点（药王谷那条）：站在哪儿都能领
	var other = solo_state(db)
	other.set_flag("flag_poison_hall")
	var anywhere: Array = StoryServiceScript.claim_for(db, other, "")
	check_eq(anywhere.size(), 1, "place_id 留空的节点在任何地方都能领（药王谷）")
	check_eq(Array(anywhere[0]["granted"]).size(), 1, "药王谷发 1 部（续命诀）")


## 章节推进（设计 18 断点 5）：`chapter_01` 完成条件满足 → 推进到 `chapter_02`，
## **一次只推一格**，且最后一章停在原地。
func _check_chapter_advance(db) -> void:
	var state = solo_state(db)
	check_eq(state.chapter_id, "chapter_01", "开局是第一章")
	check_eq(ChapterServiceScript.label_of(db, state), "第一章·黑风寨", "章节名从 chapter_def.name_cn 取（不给玩家看 id）")
	check_false(bool(ChapterServiceScript.try_advance(db, state).get("advanced", false)), "还没完成 → 不推进")

	state.set_flag("flag_chapter1_done")
	var advanced: Dictionary = ChapterServiceScript.try_advance(db, state)
	check_true(bool(advanced.get("advanced", false)), "完成条件满足 → 推进")
	check_eq(state.chapter_id, "chapter_02", "推进到第二章")
	check_eq(str(advanced.get("name", "")), "第二章（待定）", "播报用表里的名字")
	check_eq(str(advanced.get("text", "")), "第一章告一段落", "完成文案取自 chapter_def.complete_text_cn")
	# 第二章是占位（没有下一章）：再调用不推进
	check_false(bool(ChapterServiceScript.try_advance(db, state).get("advanced", false)), "最后一章停在原地")
