## 城镇木桩练习战（设计 09 §3.3）：收益封顶、到顶给提示、数据没到就说清楚。
##
## 木桩的 `enemy_team`／`enemy_base` 两行**设计侧还没给**（Q52），所以这里分两层验：
##   ① 服务层：封顶规则与提示全部读表（不依赖那两行）；`data_ready()` 如实报告缺什么
##   ② 夹具层：临时往**内存副本**里补两行，验证「组出来的遭遇是练习战 + 木桩不还手」——不碰策划的 CSV
extends "res://tests/test_case.gd"

const PracticeServiceScript := preload("res://src/core/practice_service.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
const PartyBuilderScript := preload("res://src/core/party_builder.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const MasteryServiceScript := preload("res://src/core/mastery_service.gd")


func suite_name() -> String:
	return "木桩练习战"


func run() -> void:
	var db = get_db()
	_check_entry_detection(db)
	_check_caps(db)
	_check_notice_and_readiness(db)
	_check_mastery_cap(db)
	_check_fixture_encounter(db)
	_check_dummy_never_attacks(db)


## 入口识别：`building_def.service_id == dummy_training` 的房子就是木桩，别的不是
func _check_entry_detection(db) -> void:
	check_eq(PracticeServiceScript.SERVICE_ID, "dummy_training", "木桩的 service_id 写死为 dummy_training")
	var dummy_id := ""
	for row: Resource in db.rows("building_def"):
		if str(row.service_id) == PracticeServiceScript.SERVICE_ID:
			dummy_id = str(row.building_id)
	check_eq(dummy_id, "bld_dummy", "表里的木桩建筑是 bld_dummy")
	check_true(PracticeServiceScript.is_practice_building(db, "bld_dummy"), "认得木桩")
	check_false(PracticeServiceScript.is_practice_building(db, "bld_grocery"), "杂货铺不是木桩")
	check_false(PracticeServiceScript.is_practice_building(db, "不存在的建筑"), "不存在的建筑不冒充木桩")
	check_eq(float(db.get_row("building_def", "bld_dummy").service_price), 0.0, "木桩不收钱（表里 service_price=0）")


## 封顶规则：等级上限读表、到顶就不给经验、熟练度上限读表
func _check_caps(db) -> void:
	check_eq(PracticeServiceScript.cap_level(db), 5, "经验上限读 growth_const.dummy_xp_cap_level（5 级）")
	check_eq(PracticeServiceScript.mastery_cap(db), 3, "熟练度上限读 growth_const.dummy_mastery_cap（3 级）")
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	check_eq(state.level_of(char_id), 1, "新档 1 级")
	check_true(PracticeServiceScript.xp_allowed(db, state), "1 级还能练")
	check_eq(PracticeServiceScript.capped_exp(db, state, 30), 30, "没到顶：经验原样给")
	state.char_levels[char_id] = 4
	check_true(PracticeServiceScript.xp_allowed(db, state), "4 级还能练（上限是 5）")
	state.char_levels[char_id] = 5
	check_false(PracticeServiceScript.xp_allowed(db, state), "到 5 级就练不出东西了")
	check_eq(PracticeServiceScript.capped_exp(db, state, 30), 0, "到顶后经验给 0")
	check_eq(PracticeServiceScript.capped_exp(db, state, -5), 0, "负数当 0（不给负经验）")
	# Q54（0.14.0）：**单场最多补到上限门槛，超出丢弃**——1 级到 5 级要 100+240+420+640 = 1400
	state.char_levels[char_id] = 1
	state.party_exp = 0
	check_eq(PracticeServiceScript.exp_room(db, state), 1400, "1 级到 5 级还差 1400 经验")
	check_eq(PracticeServiceScript.capped_exp(db, state, 9999), 1400, "一次给再多也只补到门槛（超出丢弃）")
	state.party_exp = 1300
	check_eq(PracticeServiceScript.capped_exp(db, state, 9999), 100, "池里已有 1300 → 这一场最多再给 100")
	state.party_exp = 1400
	check_eq(PracticeServiceScript.capped_exp(db, state, 9999), 0, "池子已经够送到门槛 → 这一场给 0")
	state.party_exp = 0
	# 队伍里只要还有一个人没过上限，就还能练
	var wide = table_with_n_chars(3)
	var party = party_state(wide, 3)
	var ids := PackedStringArray(party.char_ids)
	check_eq(PracticeServiceScript.exp_room(wide, party), 4200, "三个人都要送到上限 → 3 × 1400")
	party.char_levels[ids[0]] = 5
	party.char_levels[ids[1]] = 5
	party.char_levels[ids[2]] = 3
	# 第三个人 3 级 → 5 级：exp_to_next(3)=420 + exp_to_next(4)=640 = 1060
	check_eq(PracticeServiceScript.exp_room(wide, party), 1060, "只剩第三个人（3 级）要补到 5 级 → 420+640")
	check_true(PracticeServiceScript.xp_allowed(wide, party), "队里还有人没过上限 → 还能练")
	party.char_levels[ids[2]] = 5
	check_false(PracticeServiceScript.xp_allowed(wide, party), "全员到顶 → 不再给经验")


## 到顶提示与「数据齐了没有」：都要读表／如实报告，不能自己编
func _check_notice_and_readiness(db) -> void:
	var notice := PracticeServiceScript.cap_notice(db)
	var row: Resource = db.get_row("ui_text", "dummy_cap_reached")
	check_not_null(row, "ui_text 里有 dummy_cap_reached")
	check_eq(notice, str(row.text_cn), "到顶提示取表里那句原文：%s" % notice)
	check_true(notice.contains("上山"), "提示里指了下一步（上山）：%s" % notice)
	var ready: Dictionary = PracticeServiceScript.data_ready(db)
	# 这一条**不写死「现在缺」**：设计补上那两行之后它自然变成 ok=true，
	# 这里只钉「报告自洽」——缺就说缺什么，齐了就 error 为空。
	check_true(bool(ready["ok"]) == str(ready["error"]).is_empty(), "data_ready 的 ok 与 error 自洽")


## 熟练度封顶：练习战里只涨到 `dummy_mastery_cap`，而且**只压涨、不压低**
## （已经练到 5 级的招式打木桩不该掉回 3 级——那等于打木桩惩罚玩家）。
func _check_mastery_cap(db) -> void:
	var state = solo_state(db)
	var char_id: String = state.char_ids[0]
	var mastery = MasteryServiceScript.new(db, state)
	state.set_mastery(char_id, "sk_xuanwei_01", 0)
	var gained: Array = mastery.apply_combat_usage(char_id, {"sk_xuanwei_01": 5}, PracticeServiceScript.mastery_cap(db))
	check_eq(state.mastery_of(char_id, "sk_xuanwei_01"), 3, "5 次施放只涨到上限 3（不是 5）")
	check_false(gained.is_empty(), "涨了就要有回执（结算面板要写出来）")
	state.set_mastery(char_id, "sk_xuanwei_02", 5)
	mastery.apply_combat_usage(char_id, {"sk_xuanwei_02": 5}, PracticeServiceScript.mastery_cap(db))
	check_eq(state.mastery_of(char_id, "sk_xuanwei_02"), 5, "已经 5 级的招式打木桩不掉级")
	# 不传上限时照旧按 mastery_max（10）——别的战斗不受影响
	state.set_mastery(char_id, "sk_xuanwei_03", 0)
	mastery.apply_combat_usage(char_id, {"sk_xuanwei_03": 2})
	check_eq(state.mastery_of(char_id, "sk_xuanwei_03"), 2, "不传上限时按普通战斗的规则涨（每次 +1）")


## 夹具层：往内存副本里补两支行（木桩队伍 + 木桩敌人）→ 组出来的遭遇是练习战
func _check_fixture_encounter(db) -> void:
	var local = _with_dummy_rows(db)
	check_not_null(local.get_row("enemy_team", PracticeServiceScript.TEAM_ID), "夹具里补上了木桩队伍")
	var ready: Dictionary = PracticeServiceScript.data_ready(local)
	check_true(bool(ready["ok"]), "补完两行之后数据就齐了：%s" % str(ready["error"]))
	var encounter = PracticeServiceScript.build_encounter(local, "scene_qingfengyi", "normal")
	check_not_null(encounter, "能组出练习战遭遇")
	if encounter == null:
		return
	check_true(bool(encounter.practice), "打成练习战标记（结算按它封顶）")
	check_eq(str(encounter.team_id), PracticeServiceScript.TEAM_ID, "用的是木桩队伍")
	check_eq(str(encounter.source_key), "practice", "source_key 不占房间名（不进副本完成度）")
	check_eq(str(encounter.source_scene), "scene_qingfengyi", "source_scene 保留（打完回这张图）")


## 木桩**不还手**：它没有招式（`ai_neutral` 且 `enemy_skill` 里一条都没有）→ 模拟器跳过它的回合
func _check_dummy_never_attacks(db) -> void:
	var local = _with_dummy_rows(db)
	var state = solo_state(local)
	var allies: Array = PartyBuilderScript.build_actors(local, state)
	var enemies: Array = EnemyFactoryScript.new(local).create_team(PracticeServiceScript.TEAM_ID, "normal")
	check_eq(enemies.size(), 1, "木桩就一个目标")
	if enemies.is_empty():
		return
	var dummy = enemies[0]
	check_true(dummy.skills.is_empty(), "木桩没有招式（不还手）")
	var hp_before: int = allies[0].hp
	var sim = BattleSimulatorScript.new(local, RngServiceScript.new(20261003))
	sim.setup(allies, enemies, {"modifiers": {"force_hit": true, "no_variance": true}})
	var lines: Array = sim.step_round()
	check_eq(int(allies[0].hp), hp_before, "打过一回合，我方一点血都没掉（木桩不还手）")
	check_true(str(lines).contains("无可用招式"), "战报里写明木桩没有可用招式：%s" % str(lines))


# ------------------------------------------------------------------ 夹具

## 往**内存副本**里补木桩那两行（enemy_team + enemy_base）。只改内存，绝不碰 CSV。
## 数值只是夹具用（真值等设计给，Q52）：木桩 40 血、经验 10、不给招式。
func _with_dummy_rows(db):
	var local = TableDbScript.new()
	local.load_all()
	var team_table: Resource = local.tables["enemy_team"].duplicate(true)
	# **必须一起改 `id`**：表容器按 `row.id`（= 主键值）建索引，只改 `team_id` 会查不到这一行
	# （而且会顺手把原来那行的索引覆盖掉——夹具踩过一次）。
	var team_row: Resource = _blank_row(team_table, PracticeServiceScript.TEAM_ID, {
		"team_id": PracticeServiceScript.TEAM_ID,
		"name_cn": "木桩（夹具）",
		"members": "%s:1" % PracticeServiceScript.ENEMY_ID,
		"threat_tag": "green",
		"team_buff": "",
		"note": "夹具：只存在于内存里",
	})
	team_table.rows.append(team_row)
	team_table.index[str(team_row.id)] = team_table.rows.size() - 1
	local.tables["enemy_team"] = team_table

	var enemy_table: Resource = local.tables["enemy_base"].duplicate(true)
	var enemy_row: Resource = _blank_row(enemy_table, PracticeServiceScript.ENEMY_ID, {
		"enemy_id": PracticeServiceScript.ENEMY_ID,
		"name_cn": "木桩（夹具）",
		"faction": "neutral",
		"level": 1,
		"hp_base": 40,
		"atk_phys": 0,
		"atk_qi": 0,
		"def_phys": 0,
		"def_qi": 0,
		"speed": 1,
		"hit_rate": 0.0,
		"dodge_rate": 0.0,
		"crit_rate": 0.0,
		"poise": 10,
		"res_internal": 0.0,
		"drop_group": "",
		"exp_reward": 10,
		"money": 0,
		"threat_tag": "green",
		"ai_template": "ai_neutral",
		"desc": "夹具：不还手的木桩",
	})
	enemy_table.rows.append(enemy_row)
	enemy_table.index[str(enemy_row.id)] = enemy_table.rows.size() - 1
	local.tables["enemy_base"] = enemy_table
	return local


## 造一行「和表里同类型但字段清干净」的行对象：先复制同表第一行拿到脚本，
## 再把不在 `fields` 里的脚本变量按类型清空——否则夹具会继承上一行的名字与数值。
func _blank_row(table: Resource, row_id: String, fields: Dictionary) -> Resource:
	var row: Resource = table.rows[0].duplicate(true)
	for property in row.get_property_list():
		var name := str(property["name"])
		if fields.has(name) or not (int(property["usage"]) & PROPERTY_USAGE_SCRIPT_VARIABLE):
			continue
		match typeof(row.get(name)):
			TYPE_STRING:
				row.set(name, "")
			TYPE_FLOAT:
				row.set(name, 0.0)
			TYPE_INT:
				row.set(name, 0)
	for key: String in fields:
		row.set(key, fields[key])
	# `id` 必须在**清空之后再设**：它本身也是脚本变量，先设会被上面那一轮清成空串
	# （踩过：索引写进去之后 get_row 查不到，报「缺 enemy_team 行」）。
	row.id = row_id
	return row
