## 经验与升级（01_角色系统.md「等级与经验」 + level_growth.csv）。
##
## 表里 `exp_to_next` 是「升下一级所需经验」，满级（20）那一行是 0。
## 经验进队伍池 `party_exp`，付得起就把等级换出来（加点与等级成长由 AttributeCalculator 自动跟上）。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const LevelServiceScript := preload("res://src/core/level_service.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")

const CHAR_ID := "scholar_fallen"


func suite_name() -> String:
	return "经验与升级"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var levels = LevelServiceScript.new(db, state)
	_check_table(db, levels)
	_check_level_up(db, state, levels)
	_check_points_and_stats(db, state)
	_check_cap(db, state, levels)
	_check_battle_reward(db)


## 经验表本身：1→2 要 100，之后每级递增，20 级封顶
func _check_table(db, levels) -> void:
	check_eq(levels.exp_to_next(1), 100, "1 级升 2 级要 100 经验")
	check_eq(levels.exp_to_next(2), 240, "2 级升 3 级要 240 经验")
	check_eq(levels.exp_to_next(19), 9650, "19 级升 20 级要 9650 经验")
	check_eq(levels.exp_to_next(20), 0, "满级不再需要经验")
	check_eq(levels.max_level(), 20, "等级上限 20")
	check_eq(levels.upgrade_points(1), 5, "每级给 5 点加点（设计 0.13.0 由 3 提到 5）")
	var previous := 0
	for level in range(1, 20):
		check_gt(float(levels.exp_to_next(level)), float(previous), "%d 级的需求比上一级高" % level)
		previous = levels.exp_to_next(level)


## 付得起才升：99 不升、100 升一级、跨级一次到位
func _check_level_up(db, state, levels) -> void:
	state.party_exp = 99
	var none: Dictionary = levels.apply_available_levels()
	check_true(Array(none["changes"]).is_empty(), "99 经验不够升一级")
	check_eq(state.level_of(CHAR_ID), 1, "等级没变")
	check_eq(state.party_exp, 99, "池子里的经验留着")

	state.party_exp = 100
	var once: Dictionary = levels.apply_available_levels()
	check_eq(Array(once["changes"]).size(), 1, "100 经验升一级")
	check_eq(state.level_of(CHAR_ID), 2, "等级到 2")
	check_eq(state.party_exp, 0, "扣掉 100 经验")
	if not Array(once["changes"]).is_empty():
		var change: Dictionary = Array(once["changes"])[0]
		check_eq(int(change["from"]), 1, "从 1 级")
		check_eq(int(change["to"]), 2, "升到 2 级")
		check_eq(int(change["points"]), 5, "这次给了 5 点")
	check_true(levels.describe(once["changes"]).contains("Lv1 → Lv2"), "文案写清级别变化：%s" % levels.describe(once["changes"]))

	# 一路升到 4 级：240 + 420
	state.party_exp = 240 + 420
	var multi: Dictionary = levels.apply_available_levels()
	check_eq(state.level_of(CHAR_ID), 4, "经验够就连续升（到 4 级）")
	check_eq(state.party_exp, 0, "跨级把经验全花掉")
	check_eq(Array(multi["changes"]).size(), 2, "这次升了两级")

	# 经验不够下一级时留着
	state.party_exp = 300
	levels.apply_available_levels()
	check_eq(state.level_of(CHAR_ID), 4, "300 不够 4→5（要 640）")
	check_eq(state.party_exp, 300, "不够的经验不扣")


## 升级带动加点上限与等级成长（这几条本来就接在 level_growth 上）
func _check_points_and_stats(db, state) -> void:
	state.char_levels[CHAR_ID] = 1
	state.char_allocations = {}
	check_eq(state.available_points(db, CHAR_ID), 5, "1 级有 5 点")
	state.append_level(CHAR_ID)  # 直接升一级，绕过经验池
	check_eq(state.level_of(CHAR_ID), 2, "升到 2 级")
	check_eq(state.available_points(db, CHAR_ID), 10, "2 级累计 10 点")
	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	var hp_at_2: float = sheet.stat_value("hp_max")
	check_true(bool(state.spend_point(db, CHAR_ID, "str")["ok"]), "升级得来的点能加")
	state.append_level(CHAR_ID)
	check_eq(state.available_points(db, CHAR_ID), 14, "3 级累计 15 点，加了 1 点还剩 14")
	var hp_at_3: float = CharacterSheetScript.new(db, state, CHAR_ID).stat_value("hp_max")
	check_gt(hp_at_3, hp_at_2, "等级成长进了基础气血（%.0f → %.0f）" % [hp_at_2, hp_at_3])


## 满级之后经验只记账不消耗
func _check_cap(db, state, levels) -> void:
	state.char_levels[CHAR_ID] = 20
	state.party_exp = 99999
	var capped: Dictionary = levels.apply_available_levels()
	check_true(Array(capped["changes"]).is_empty(), "满级不再升级")
	check_true(bool(capped["capped"]), "结果里标着已满级")
	check_eq(state.party_exp, 99999, "满级后经验留在池子里")
	var progress: Dictionary = levels.exp_progress(CHAR_ID)
	check_true(bool(progress["maxed"]), "面板能看出已经满级")


## 战斗入账顺手升级：经验够的当场升，结算里能看到
func _check_battle_reward(db) -> void:
	var state = solo_state(db)
	var applied: Dictionary = BattleRewardScript.grant(db, state, 100, 0, [])
	check_eq(state.level_of(CHAR_ID), 2, "打完拿到 100 经验就升到 2 级")
	check_eq(state.party_exp, 0, "经验扣进等级里")
	check_true(str(applied["level_up_text"]).contains("Lv1 → Lv2"), "入账结果里带升级文案：%s" % applied["level_up_text"])
	check_eq(Array(applied["level_ups"]).size(), 1, "升级记录也能读出来")

	# 经验不够就不升，但仍然记账
	var state2 = solo_state(db)
	var small: Dictionary = BattleRewardScript.grant(db, state2, 36, 0, [])
	check_eq(state2.level_of(CHAR_ID), 1, "36 经验不够升级")
	check_eq(state2.party_exp, 36, "经验先记在队伍池里")
	check_true(str(small["level_up_text"]).is_empty(), "没升级就没有升级文案")
