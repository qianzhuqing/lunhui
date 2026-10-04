## 创建角色（设计 13，0.17.0）＋ 天赋（设计 12，0.16.0）。
##
## 这里验的是**纯逻辑层**（`CreationService`／`TalentService`）；界面在
## `--creation-selftest`（真实场景、真实按钮）与 `test_menu` 的场景流里。
##
## 两条纪律各有一条断言：创建没走完不落盘（由 test_menu 的场景流验）、
## 选了谁当主角谁就不作为同伴出现（本文件验）。
extends "res://tests/test_case.gd"

const CreationServiceScript := preload("res://src/core/creation_service.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const TalentServiceScript := preload("res://src/core/talent_service.gd")
const RecruitServiceScript := preload("res://src/core/recruit_service.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")

const ORIGIN_SCHOLAR := "ori_scholar"
const ORIGIN_GANG := "ori_gang"


func suite_name() -> String:
	return "创建角色与天赋"


func run() -> void:
	var db = get_db()
	_check_cards(db)
	_check_validation(db)
	_check_origin_route(db)
	_check_custom_route(db)
	_check_talents(db)
	_check_screen_cards(db)


## 出身卡与确认汇总在**真界面**上长什么样（设计 13 §二／§六）：这两条是"给玩家做数值选择用"的，
## 只在纯逻辑层验"数据备齐了"不够——所以这里实例化真场景，读它真建出来的那两个 Label。
func _check_screen_cards(db) -> void:
	var screen = load("res://scenes/creation_screen.tscn").instantiate()
	scene_tree.root.add_child(screen)
	screen.setup()
	var detail: Label = screen.find_child("OriginDetail_ori_scholar", true, false)
	check_not_null(detail, "出身卡有第二行（七维 ＋ 武器 ＋ 起始武学）")
	if detail != null:
		check_true(detail.text.contains("█"), "七维用条形图（设计 13 §六）：%s" % detail.text)
		check_true(detail.text.contains("剑"), "卡片写着武器名")
		check_true(detail.text.contains("玄微剑法·起手"), "卡片写着起始武学名")
	# 选书生那张卡 → 「名字」那一格预填的是**真名**，不是卡标题（设计 21 §七 第 5 条）
	screen.pick_origin(ORIGIN_SCHOLAR)
	var name_edit: LineEdit = screen.find_child("NameEdit", true, false)
	check_not_null(name_edit, "起名那一格在")
	if name_edit != null:
		check_eq(name_edit.text, "陆文昭", "预填的是真名（不是「家道失落的书生」）")
	# 选一张卡之后，确认段的汇总要跟着变（设计 13 §六「确认页汇总全部选择」）
	screen.pick_origin(ORIGIN_GANG)
	var summary: Label = screen.find_child("SummaryLabel", true, false)
	check_not_null(summary, "确认段有一块汇总")
	if summary != null:
		for want: String in ["出身", "七维", "武器", "起始武学", "天赋", "名字"]:
			check_true(summary.text.contains(want), "汇总里有「%s」：%s" % [want, summary.text])
		check_true(summary.text.contains("林铁山"), "选完出身，汇总里就是那个人")
		# 起始武学名从表里现取（不写死"应该是哪一招"——角色卡换了招式这条不该跟着红）
		var gang: Resource = db.get_row("character_base", "ch_gang")
		var first_skill := str(gang.skill_ids()[0]) if gang != null and gang.skill_ids().size() > 0 else ""
		var expect_name := str(db.get_row("skill_base", first_skill).name_cn) if not first_skill.is_empty() else ""
		check_true(
			not expect_name.is_empty() and summary.text.contains(expect_name),
			"汇总里带着他的起始武学（%s）：%s" % [expect_name, summary.text],
		)
	scene_tree.root.remove_child(screen)
	screen.free()


## 出身卡与七维规则都从表里来（创建界面直接渲染这两份数据）
func _check_cards(db) -> void:
	var cards: Array = CreationServiceScript.origins(db)
	check_eq(cards.size(), 5, "5 张出身卡（origin_def 5 行）")
	check_eq(str(cards[0]["origin_id"]), ORIGIN_SCHOLAR, "按 sort_order 排，第一张是书生")
	check_false(str(cards[0]["tagline"]).is_empty(), "出身卡带一句话定位（tagline）")
	check_false(str(cards[0]["playstyle"]).is_empty(), "出身卡带玩法标签（playstyle）")
	# 设计 13 §二：**卡片必须显示七维与起始武学**（"不能只给一句文案——玩家是在做数值选择"）
	var attrs: Dictionary = cards[0].get("attrs", {})
	check_eq(attrs.size(), 7, "出身卡带上七维（%s）" % str(attrs))
	var attr_sum := 0
	for attr_id: String in ["str", "con", "agi", "int", "luk", "wu", "gen"]:
		attr_sum += int(attrs.get(attr_id, 0))
	check_eq(attr_sum, 49, "卡片上的七维合计 49（与 character_base 一致）")
	check_eq(str(cards[0].get("weapon_name", "")), "剑", "卡片带武器中文名（书生的家传剑）")
	var skill_names: PackedStringArray = cards[0].get("skill_names", PackedStringArray())
	check_eq(skill_names.size(), 1, "卡片带起始武学（书生的玄微剑法·起手）")
	check_eq(str(skill_names[0]), "玄微剑法·起手", "起始武学写中文名，不写 id")
	# 默认名（设计 21 §七 第 5 条）：卡的标题是类名「家道失落的书生」，真名才是陆文昭——
	# 预填进"名字"那一格的必须是真名
	check_eq(
		CreationServiceScript.default_name_of(db, ORIGIN_SCHOLAR), "陆文昭",
		"书生的默认名是真名（不是卡标题）",
	)
	check_eq(
		str(cards[0].get("default_name_cn", "")), "陆文昭",
		"出身卡数据里带着默认名（界面直接用它）",
	)
	for card: Dictionary in cards:
		check_false(
			str(card.get("default_name_cn", "")).is_empty(),
			"每张卡都有默认名（%s）" % str(card["origin_id"]),
		)
	var rules: Dictionary = CreationServiceScript.attr_rules(db)
	check_eq(int(rules["total"]), 49, "七维总和 49（growth_const.create_attr_total）")
	check_eq(int(rules["min"]), 3, "单项下限 3")
	check_eq(int(rules["max"]), 15, "单项上限 15")
	check_eq(CreationServiceScript.weapons(db).size(), 4, "四种武器可选")


func _check_validation(db) -> void:
	var ok_spec := {"route": "origin", "origin_id": ORIGIN_SCHOLAR, "name_cn": "书生", "talents": []}
	check_true(CreationServiceScript.validate(db, ok_spec).is_empty(), "模板路的最小方案就合法")

	var bad_origin := {"route": "origin", "origin_id": "ori_missing", "name_cn": "谁", "talents": []}
	check_false(CreationServiceScript.validate(db, bad_origin).is_empty(), "出身不存在要报错")

	var no_name := {"route": "origin", "origin_id": ORIGIN_SCHOLAR, "name_cn": "  ", "talents": []}
	check_false(CreationServiceScript.validate(db, no_name).is_empty(), "没起名要报错")
	# 刚进创建界面（什么都没选）时说**人话**，别把「出身不存在：」这种空 id 摆给玩家
	var fresh: PackedStringArray = CreationServiceScript.validate(db, {"route": "origin", "talents": []})
	check_true(str(fresh).contains("还没选出身"), "没选出身时提示是人话：%s" % str(fresh))
	check_false(str(fresh).contains("出身不存在"), "没选出身时不说「出身不存在」（那是数据错才说的话）")

	# 自建路：总和不对 / 单项越界 / 武器不存在 / 起始武学不是本系 ★1
	var custom := _custom_spec(db)
	check_true(CreationServiceScript.validate(db, custom).is_empty(), "自建路的最小合法方案")
	var wrong_sum := custom.duplicate(true)
	wrong_sum["attrs"] = custom["attrs"].duplicate()
	wrong_sum["attrs"]["str"] = int(wrong_sum["attrs"]["str"]) + 1
	check_false(CreationServiceScript.validate(db, wrong_sum).is_empty(), "七维总和不对要报错")
	var over := custom.duplicate(true)
	over["attrs"] = custom["attrs"].duplicate()
	over["attrs"]["str"] = int(CreationServiceScript.attr_rules(db)["max"]) + 1
	over["attrs"]["con"] = int(over["attrs"]["con"]) - 1
	check_false(CreationServiceScript.validate(db, over).is_empty(), "单项超过上限要报错")
	var bad_weapon := custom.duplicate(true)
	bad_weapon["weapon_type"] = "staff"
	check_false(CreationServiceScript.validate(db, bad_weapon).is_empty(), "武器类型不存在要报错")
	var bad_skill := custom.duplicate(true)
	bad_skill["start_skill_id"] = "sk_xuanwei_08"
	check_false(CreationServiceScript.validate(db, bad_skill).is_empty(), "起始武学必须★1本系（★5 不行）")
	var too_many_talents := ok_spec.duplicate(true)
	too_many_talents["talents"] = ["tal_dan_shi", "tal_jiu_ming", "tal_yi_tong"]
	check_false(CreationServiceScript.validate(db, too_many_talents).is_empty(), "天赋点超过 5 要报错")


## 模板路：主角就是那张卡指向的角色；**他不会再作为同伴出现**（设计 13 的准话）
func _check_origin_route(db) -> void:
	var built: Dictionary = CreationServiceScript.build(db, {
		"route": "origin", "origin_id": ORIGIN_GANG, "name_cn": "林铁山", "talents": [],
	})
	check_true(bool(built["ok"]), "模板路能建出存档：%s" % str(built.get("error", "")))
	var state = built["state"]
	if state == null:
		return
	check_eq(str(built["char_id"]), "ch_gang", "选「林铁山」这张卡 = 用 ch_gang 的模板")
	check_eq(state.party_size(), 1, "开局只有主角一人")
	# 主角已经在队里 → 招募链不会把他再收一遍（同一张卡的同伴入口自然跳过）
	var pending: Array = RecruitServiceScript.pending_for_scene(db, state, "scene_qingfengyi")
	var pending_ids := PackedStringArray()
	for row: Resource in pending:
		pending_ids.append(str(row.char_id))
	check_false(pending_ids.has("ch_gang"), "主角不会再作为同伴出现")
	# 换了主角，其它同伴照样能来（燕小七那条链的条件是告示板旗标）
	state.set_flag("flag_board_read")
	var after: Array = RecruitServiceScript.pending_for_scene(db, state, "scene_qingfengyi")
	var after_ids := PackedStringArray()
	for row: Resource in after:
		after_ids.append(str(row.char_id))
	check_true(after_ids.has("ch_ci"), "主角换成别人之后，燕小七仍然按剧情入队")
	check_false(after_ids.has("ch_gang"), "主角（这次是林铁山）仍然不在同伴名单里")

	# 「主角可改」＋默认名（设计 21 §七 第 5 条）：名字要**真的进存档**——
	# `char_name()` 读的是表，所以改过的名字按自建角色那套注入一行（v14 的字段，不动版本）
	var renamed: Dictionary = CreationServiceScript.build(db, {
		"route": "origin", "origin_id": ORIGIN_SCHOLAR, "name_cn": "王二", "talents": [],
	})
	check_true(bool(renamed["ok"]), "改名的模板角色建得出来：%s" % str(renamed.get("error", "")))
	var named_state = renamed["state"]
	if named_state != null:
		check_eq(str(named_state.char_name(db, "scholar_fallen")), "王二", "面板读到的是玩家改的名字")
		check_eq(int(named_state.char_ids.size()), 1, "队伍人数不受影响")
		# 存档往返：改名跟着走（不然读档又变回卡标题）
		var back = GameStateScript.from_dict(named_state.to_dict(), db)
		check_not_null(back, "改名后的档读得回来")
		if back != null:
			check_eq(str(back.char_name(db, "scholar_fallen")), "王二", "读档后名字仍是玩家改的")


## 自建路：模板注入 db、起始装备按武器给、面板拿得到这份模板
func _check_custom_route(db) -> void:
	var spec := _custom_spec(db)
	spec["name_cn"] = "自建剑客"
	var built: Dictionary = CreationServiceScript.build(db, spec)
	check_true(bool(built["ok"]), "自建路能建出存档：%s" % str(built.get("error", "")))
	var state = built["state"]
	if state == null:
		return
	var char_id := str(built["char_id"])
	check_eq(char_id, CreationServiceScript.CUSTOM_CHAR_ID, "自建角色用固定 id")
	var row: Resource = db.get_row("character_base", char_id)
	var want_attrs: Dictionary = spec["attrs"]
	check_not_null(row, "自建模板注入进了 character_base（后面按 char_id 取模板的地方都要它）")
	if row != null:
		check_eq(int(row.initial_str), int(want_attrs["str"]), "七维按玩家分的存进模板")
		check_eq(str(row.weapon_type), str(spec["weapon_type"]), "武器类型进模板")
	check_true(state.custom_templates.has(char_id), "自建模板也进存档（读档要重新注入）")
	# 起始装备：该武器的凡品 ＋ 布衣
	var equipped: Array = []
	for instance_id: String in state.inventory.equipped_instances(db, char_id):
		equipped.append(state.inventory.base_of(instance_id))
	check_true(equipped.size() >= 2, "起始装备发下来了（武器 + 布衣）：%s" % str(equipped))
	# 面板真的能按这份模板算
	var sheet = CharacterSheetScript.new(db, state, char_id)
	check_true(sheet.valid(), "面板认得这个自建角色")
	check_eq(int(sheet.total_attrs()["str"]), int(want_attrs["str"]), "面板五维 = 自建模板的七维")


## 天赋：花费上限、`attr:`／`stat:` 进属性贡献、`rule:` 由各自系统读
func _check_talents(db) -> void:
	var budget := TalentServiceScript.budget(db)
	check_eq(budget, 5, "天赋总点数 5（growth_const.talent_points）")
	var cards: Array = TalentServiceScript.cards(db)
	check_eq(cards.size(), 24, "天赋池 24 条")
	check_true(TalentServiceScript.remaining_points(db, PackedStringArray(["tal_tie_shen"])) == 4,
		"选了 1 点的「铁打身板」还剩 4 点")
	check_false(bool(TalentServiceScript.can_pick(db, PackedStringArray(["tal_dan_shi"]), "tal_jiu_ming")["ok"]),
		"3 + 3 超过 5 点：第二个选不上")

	var built: Dictionary = CreationServiceScript.build(db, {
		"route": "origin", "origin_id": ORIGIN_SCHOLAR, "name_cn": "书生",
		"talents": ["tal_tie_shen", "tal_wu_chi", "tal_jia_di"],
	})
	check_true(bool(built["ok"]), "带天赋的创建方案合法：%s" % str(built.get("error", "")))
	var state = built["state"]
	if state == null:
		return
	var char_id := str(built["char_id"])
	# `rule:start_money` = 500：纯增益走资源，不进属性
	check_eq(state.inventory.money, 500, "「家底殷实」的起始铜钱进钱包（rule 类不进属性）")
	# `stat:res_internal` +0.10 与 `rule:mastery_gain` 0.30 各自归位
	var sheet = CharacterSheetScript.new(db, state, char_id)
	var res_total := 0.0
	for contribution: Dictionary in sheet.contributions():
		if str(contribution.get("target", "")) == "res_internal":
			res_total += float(contribution.get("value", 0.0))
	check_float(res_total, 0.10, "「铁打身板」的内伤抗性走属性贡献通道", 0.0001)
	check_float(TalentServiceScript.rule_value(db, state, char_id, "mastery_gain", 0.0), 0.30,
		"「武痴」的熟练度成长是 rule 类，由成长系统读")
	# 存档往返：天赋列表跟着走
	var back = _round_trip(db, state)
	check_eq(Array(back.talent_picks.get(char_id, [])).size(), 3, "天赋进存档、读回来还是 3 个")


func _custom_spec(db) -> Dictionary:
	var rules: Dictionary = CreationServiceScript.attr_rules(db)
	var attrs: Dictionary = {}
	var left := int(rules["total"]) - int(rules["min"]) * 7
	for attr_id: String in ["str", "con", "agi", "int", "luk", "wu", "gen"]:
		var add := mini(left, int(rules["max"]) - int(rules["min"]))
		attrs[attr_id] = int(rules["min"]) + add
		left -= add
	var weapon := str(CreationServiceScript.weapons(db)[0]["weapon_type"])
	var options: Array = CreationServiceScript.start_skill_options(db, weapon)
	return {
		"route": "custom", "name_cn": "自建", "attrs": attrs,
		"weapon_type": weapon,
		"start_skill_id": str(options[0]["skill_id"]) if not options.is_empty() else "",
		"talents": [],
	}


## 走一遍真存档往返（与 SaveStore 同一套 to_dict／from_dict）
func _round_trip(db, state):
	var game_state_script := preload("res://src/core/game_state.gd")
	return game_state_script.from_dict(state.to_dict(), db)
