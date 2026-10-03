## 明雷遭遇规则：接触方式决定战斗开局。
extends "res://tests/test_case.gd"

const EncounterScript := preload("res://src/core/encounter.gd")
const RoamingEnemyScript := preload("res://src/world/roaming_enemy.gd")


func suite_name() -> String:
	return "明雷遭遇规则"


func run() -> void:
	var db = get_db()
	# 阵营常量的**绝对值**：Encounter 这份现在直接取自 `BattleActor.SIDE_*`（单一出处），
	# 这里与 `test_battle._check_absolute_constants` 一起把 0/1 钉死——改哪一处都会红。
	check_eq(EncounterScript.SIDE_ALLY, 0, "Encounter 的我方常量 = 0")
	check_eq(EncounterScript.SIDE_ENEMY, 1, "Encounter 的敌方常量 = 1")
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", "team_wolf_pack")
	_check_front(db, spawn, team)
	_check_back(db, spawn, team)
	_check_sleep(db, spawn, team)
	_check_spotted(db, spawn, team)
	_check_neutral_team(db)
	_check_pinned_contact_distance()


## 明雷的**接触距离**是开发侧定的手感值（`enemy_team` 没有这一列，已记交接表等设计补）。
## 变异探针实测：把它从 22 改成 60，一整套自检照样绿——而它决定「离多远就被拖进战斗」，
## 悄悄调大等于玩家在视野外被伏击。按 `test_poise` 里敌人那两行的同一条纪律：
## 把**数值本身**钉住，改它就得改这条断言（有意识的行为）。
func _check_pinned_contact_distance() -> void:
	check_float(RoamingEnemyScript.CONTACT_DISTANCE, 22.0, "明雷接触距离 22px（改它＝改被拖进战斗的远近）")


func _check_front(db, spawn, team) -> void:
	var encounter = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_FRONT, "normal")
	check_eq(encounter.first_side, -1, "正面遭遇不强制先手，按身法排序")
	check_float(encounter.surprise_bonus, 0.0, "正面没有奇袭加成")
	check_eq(encounter.pending_rules.size(), 0, "正面没有暂缓规则")
	check_eq(encounter.contact_label(), "正面遭遇", "接触方式文案")
	check_eq(encounter.team_name, "野狼群", "队伍名取自 enemy_team")
	check_eq(encounter.headline(), "正面遭遇　野狼群", "开场文案")


func _check_back(db, spawn, team) -> void:
	var encounter = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_BACK, "normal")
	check_eq(encounter.first_side, EncounterScript.SIDE_ALLY, "背后接触我方先手")
	check_float(encounter.enemy_poise_reduce, 0.3, "背袭让敌方架势初始值降 30%（数值取 combat_const.backstab_poise_reduce）")
	check_eq(encounter.pending_rules.size(), 0, "架势削减已经实现，不再记暂缓")
	check_eq(encounter.round_modifiers(1, EncounterScript.SIDE_ALLY), {}, "背袭没有额外增伤（那是奇袭）")


func _check_sleep(db, spawn, team) -> void:
	var encounter = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_SLEEP, "normal")
	check_eq(encounter.first_side, EncounterScript.SIDE_ALLY, "奇袭我方先手")
	check_float(encounter.surprise_bonus, 0.5, "奇袭首回合增伤取 combat_const.surprise_damage_bonus")
	var first: Dictionary = encounter.round_modifiers(1, EncounterScript.SIDE_ALLY)
	check_float(float(first.get("dmg_up", 0.0)), 0.5, "首回合我方增伤 50%")
	check_eq(encounter.round_modifiers(2, EncounterScript.SIDE_ALLY), {}, "增伤只在首回合")
	check_eq(encounter.round_modifiers(1, EncounterScript.SIDE_ENEMY), {}, "增伤不给敌方")


func _check_spotted(db, spawn, team) -> void:
	var spotted = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_SPOTTED, "normal")
	check_eq(spotted.first_side, EncounterScript.SIDE_ENEMY, "被发现时敌方先手")
	var caught = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_CAUGHT, "normal")
	check_eq(caught.first_side, EncounterScript.SIDE_ENEMY, "被追上敌方先手")
	check_eq(
		caught.pending_rules.size(), 0,
		"被追上的「首回合不能逃」已经由 BattleSimulator 真判了，不再记暂缓（见 test_battle 的逃跑用例）",
	)


func _check_neutral_team(db) -> void:
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_herbalist")
	var team: Resource = db.get_row("enemy_team", "team_herbalist")
	var encounter = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_FRONT, "normal")
	check_eq(encounter.is_elite, false, "采药人不是精英")
	check_eq(encounter.summary()["team_id"], "team_herbalist", "中立队伍照样能构造遭遇（是否开战由明雷决定）")
