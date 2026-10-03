## 城镇木桩练习战（设计 09 §3.3）：把玩家从「刚出门」送到「能上山」的练级场。
##
## 规则全部读表（09 §3.3 的收益上限表）：
##   角色等级    只给到 `growth_const.dummy_xp_cap_level`（5 级），到顶后木桩战给 0 经验
##   招式熟练度  参与木桩战的招式只涨到 `growth_const.dummy_mastery_cap`（3 级）
##   掉落／铜钱／首杀／击败领悟  一律没有（否则它就成了刷级永动机，也就不需要上山了）
## 到顶时给一句提示（`ui_text.dummy_cap_reached`）：静默不给经验会让玩家以为卡住。
##
## 入口：城镇里 `building_def.service_id == SERVICE_ID` 的建筑（现在只有清风驿校场的木桩）。
##
## **队伍 id 是代码侧约定的**：`building_def` 没有指向队伍的列，而设计 09 §3.3 也只写「按 E 进入
## 战斗」、没给木桩的数值。设计侧补一行 `enemy_team`（`TEAM_ID`）与一行 `enemy_base`
## （`ENEMY_ID`：中性模板、**不给招式**＝不还手）之后这里就能跑；在那之前 `data_ready()`
## 会如实说出缺什么（`待策划确认.md` Q52），界面照实说「还没准备好」。
class_name PracticeService
extends RefCounted

const EncounterScript := preload("res://src/core/encounter.gd")

## 木桩这类建筑的 `building_def.service_id`
const SERVICE_ID := "dummy_training"
## 练习战用的队伍 id（设计侧补表时照这个名字建）
const TEAM_ID := "team_dummy_training"
## 木桩自己的敌人 id（中性模板、不给招式）
const ENEMY_ID := "en_dummy_training"


static func is_practice_building(db, building_id: String) -> bool:
	var row: Resource = db.get_row("building_def", building_id) if db != null else null
	return row != null and str(row.service_id) == SERVICE_ID


## 木桩能练到几级（`growth_const.dummy_xp_cap_level`；缺行时返回 0 = 不限制，并出声）
static func cap_level(db) -> int:
	var row: Resource = db.get_row("growth_const", "dummy_xp_cap_level") if db != null else null
	if row == null:
		push_error("[PracticeService] growth_const 缺 dummy_xp_cap_level——练习战的经验上限没有出处")
		return 0
	return int(row.value)


## 木桩战里熟练度能涨到几级（`growth_const.dummy_mastery_cap`）
static func mastery_cap(db) -> int:
	var row: Resource = db.get_row("growth_const", "dummy_mastery_cap") if db != null else null
	if row == null:
		push_error("[PracticeService] growth_const 缺 dummy_mastery_cap——练习战的熟练度上限没有出处")
		return 0
	return int(row.value)


## 还能练吗：队伍里**还有人**没过上限。经验是队伍池，所以按「最低的那个人」判——
## 全员到顶之后木桩战就不再给经验（设计：到顶后给 0 经验 + 一句提示）。
static func xp_allowed(db, state) -> bool:
	if state == null or state.char_ids.is_empty():
		return false
	var cap := cap_level(db)
	if cap <= 0:
		return true
	for char_id: String in state.char_ids:
		if state.level_of(char_id) < cap:
			return true
	return false


## 练习战该给多少经验。设计 0.14.0（Q54）定了口径：**单场最多补到上限门槛，超出部分丢弃**
## （「溢出会催生『多打一场提前拿满』的无意义重复」）。
##
## 经验进的是**队伍池**（`party_exp`），`LevelService` 先补等级最低的人，所以：
##   · 全员到顶 → 0
##   · 否则 → `min(原值, 还差的量)`，其中「还差的量」= 把每个成员都送到上限所需经验之和 − 池里已有的
## 0.13.0 那版是「还有人没过上限就给原值」，一次给多了会让有人冲过上限——Q54 就是为这个问的。
static func capped_exp(db, state, raw_exp: int) -> int:
	var raw := maxi(0, raw_exp)
	if not xp_allowed(db, state):
		return 0
	if cap_level(db) <= 0:
		# 表里没给上限：不自己编一个，照原值给（`cap_level` 已经 push_error 报缺行）
		return raw
	return mini(raw, exp_room(db, state))


## 把全队送到上限**还差多少经验**（扣掉池里已经攒着的）：
## `Σ_成员(从当前等级到上限的 exp_to_next 之和) − party_exp`，下限 0。
static func exp_room(db, state) -> int:
	if state == null:
		return 0
	var cap := cap_level(db)
	if cap <= 0:
		return 0
	var need := 0
	for char_id: String in state.char_ids:
		need += _exp_to_cap(db, state.level_of(char_id), cap)
	return maxi(0, need - int(state.party_exp))


## 从 `level` 升到 `cap` 需要的累计经验（逐级 `level_growth.exp_to_next` 求和；已到顶就是 0）
static func _exp_to_cap(db, level: int, cap: int) -> int:
	var total := 0
	for current in range(maxi(1, level), cap):
		var row: Resource = db.get_row("level_growth", current)
		if row == null:
			continue
		total += int(row.exp_to_next)
	return total


## 到顶时给玩家的一句提示（`ui_text.dummy_cap_reached`）
static func cap_notice(db) -> String:
	var row: Resource = db.get_row("ui_text", "dummy_cap_reached") if db != null else null
	if row == null:
		push_error("[PracticeService] ui_text 缺 dummy_cap_reached——到顶提示没有出处")
		return "木桩已经练不出东西了。"
	return str(row.text_cn)


## 练习战的数据齐了没有。返回 {ok, error}；缺什么就说什么（不静默、不自己编数值）。
static func data_ready(db) -> Dictionary:
	if db == null:
		return {"ok": false, "error": "没有配置表"}
	var team: Resource = db.get_row("enemy_team", TEAM_ID)
	if team == null:
		return {"ok": false, "error": "缺 enemy_team 行 %s（设计侧补）" % TEAM_ID}
	var missing := PackedStringArray()
	for member: String in str(team.members).split(";"):
		var enemy_id := member.split(":")[0].strip_edges()
		if enemy_id.is_empty():
			continue
		if db.get_row("enemy_base", enemy_id) == null:
			missing.append(enemy_id)
	if not missing.is_empty():
		return {"ok": false, "error": "缺 enemy_base 行 %s（设计侧补）" % "、".join(missing)}
	return {"ok": true, "error": ""}


## 组一场练习战。调用方负责把它交给战斗场景（`local_map_controller.start_practice`）。
## `encounter.practice = true` 是**后面所有封顶规则的开关**（结算面板按它跳过掉落/铜钱/领悟/首杀）。
static func build_encounter(db, scene_id: String, difficulty_id: String = "normal"):
	var team: Resource = db.get_row("enemy_team", TEAM_ID)
	var encounter = EncounterScript.build(db, {
		"spawn_id": "practice",
		"source_scene": scene_id,
		"source_key": "practice",     # 不写房间名：练习战不进副本完成度
		"team_id": TEAM_ID,
		"is_elite": false,
	}, team, EncounterScript.CONTACT_FRONT, difficulty_id)
	encounter.practice = true
	return encounter
