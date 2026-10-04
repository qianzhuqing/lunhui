## 轻量断言基类。
##
## 不引入第三方插件（GUT 需要联网），断言自行累计失败，
## 由 tools/run_tests.gd 汇总后决定退出码。
class_name TestCase
extends RefCounted

const TableDbScript := preload("res://src/core/table_db.gd")
## 名字**不能**叫 GameStateScript：子类大多自己 `const GameStateScript := preload(...)`，
## 基类同名成员会让所有子类解析失败（踩过：一瞬间 26 个用例文件"无法加载"）。
const SoloStateScript := preload("res://src/core/game_state.gd")

## 用例默认用「一个人」的队伍。
##
## 为什么要有这个：`character_base.csv` 的行数是**设计状态**——0.4.0 起收敛成 1 行，
## 而设计要 3–4 人队伍（`当前状态.md` 缺口 #1，01 的「备选模板数据」里已有 4 行现成数据）。
## 绝大多数用例的场景其实是"**某一个角色**做某事"（谁去判定、谁学招式、谁打这一场），
## 它们不该随表里的行数漂移——2026-10-03 实测：把 4 行接上去，**198 条断言变红**，
## 其中 132 条来自这一条根因（见框架说明决策 161）。
## 所以要求：场景型用例显式 `solo_state(db)`（或自己造队）；只有**专门验证默认组队**的用例
## 才直接调 `GameState.new_game(db, …)` 并**按表算期望值**，不要写死 1。
const SOLO_CHAR_ID := "scholar_fallen"


## **必须声明返回类型**：不声明时调用处拿到的是无类型值，`var x := solo_state(db)` 会
## "Cannot infer the type"（GDScript 解析错误，整个文件加载失败——踩过）。
func solo_state(db, difficulty: String = "normal") -> SoloStateScript:
	return SoloStateScript.new_game(db, difficulty, PackedStringArray([SOLO_CHAR_ID]))


## 造一支 N 人队伍：按 `db` 里 `character_base` 的行顺序取前 N 人（不够就取到多少算多少）。
##
## 为什么要有这个：**0.10.0 起 `new_game()` 的队伍来自 `recruit_def.is_initial`**
## （设计 09 §3.2：开局只有书生一人，同伴按 `join_condition`／`join_scene` 在剧情里加入），
## 不再取 `character_base` 的前 4 行。所以「多人分支」的用例必须**显式点名队伍**——
## 想要 4 个人就传 4 个 id，别再指望 `new_game` 自动给 4 人。
func party_state(db, count: int, difficulty: String = "normal") -> SoloStateScript:
	var ids := PackedStringArray()
	for row: Resource in db.rows("character_base"):
		if ids.size() >= count:
			break
		ids.append(str(row.char_id))
	return SoloStateScript.new_game(db, difficulty, ids)


## 把一条**条件**点亮（夹具用）：`a&b` 两段都置、`a|b` 两段都置（"任一满足"自然成立）、
## `!x` 段跳过（要求的是"它没被点亮"，夹具不该去点亮它）。
## `origin:`／`item:` 这些不是旗标的条件也跳过——那些得靠造出对应状态来满足。
##
## 为什么要有它：夹具以前直接 `state.set_flag(join_condition)`，条件里一出现 `|`／`&`，
## 置进去的就是个**假旗标**（名字叫 `flag_a|flag_b`），条件仍然不成立，
## 「走到地标旁入队」直接掉进"没点亮"那条分支（2026-10-04 Q83 实测踩到）。
func satisfy_condition(state, condition: String) -> void:
	for group: String in condition.replace("|", "&").split("&", false):
		var token := group.strip_edges()
		if token.is_empty() or token.begins_with("!"):
			continue
		if token.begins_with("flag_"):
			state.set_flag(token)


## 把**所有角色模板的资质**（悟性／根骨）直接改成指定值的内存夹具。
##
## 设计 0.13.0 把悟性／根骨锁成「资质」（不能加点），而第一章也没有别的来源能把根骨抬到
## ★5 内功那类门槛（图鉴奖励最多 +10 → 根骨 15；玄微心法·太清本身就要根骨 25）。
## 所以「槽位／容量公式」「内功加成不吃自环」这类用例改成**复制模板行、直接给资质**——
## 它们验的是公式与顺序，不是「资质怎么获得」（那条由 test_skill_loadout 的图鉴用例盯）。
## **只改内存副本，绝不碰策划的 CSV。**
func table_with_talents(wu: int, gen: int):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["character_base"].duplicate(true)
	for row: Resource in table.rows:
		row.initial_wu = wu
		row.initial_gen = gen
	custom.tables["character_base"] = table
	return custom

var _checks := 0
var _failures: Array[String] = []
var _db

## 需要真实场景树的用例（UI 冒烟）由 tools/run_tests.gd 注入。
var scene_tree: SceneTree = null


## 用例名，打印用。
func suite_name() -> String:
	return "未命名用例"


## 用例主体，子类覆写。
func run() -> void:
	pass


## 共享一份表库（资源是只读的，多个用例共用一个实例没问题）。
func get_db():
	if _db == null:
		_db = TableDbScript.new()
		_db.load_all()
	return _db


## 造一张「有 N 个角色模板」的表库：复制唯一那行、改 id 与名字。
##
## 起因：`character_base` 只有 1 行，而设计要 3–4 人队伍（`docs/dev/当前状态.md` 缺口 #1），
## 所以所有**多人分支**（多人界面版式、判定谁行谁上、经验先补等级最低的、医馆全队求和…）
## 在真实数据到位前都没人跑过。`smartest_index` 那个角色把智调高，用来验「判定谁行谁上」；
## `long_names` 用长名字把界面宽度压一压（成员列表、头部信息）。
##
## **契约：结果恰好 count 个角色。** 以前是「往现有行后面追加 N-1 个」，于是
## `character_base.csv` 有 1 行时得到 4 个、有 5 行时得到 8 个——**夹具自己跟着设计状态漂移**
## （2026-10-03 把 01 的 4 行接上去探针时踩到）。现在先把表重置成只剩基准行，再补副本。
##
## **只改内存里的副本，绝不碰策划的 CSV。**
func table_with_n_chars(count: int, smartest_index: int = 2, long_names: bool = false):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["character_base"].duplicate(true)
	var base: Resource = table.rows[0]
	# 非战斗技能也要跟着复制：`skill:wenxue` 的判定值 = 文学等级 + floor(智/5)，
	# 只复制模板不复制技能表的话，新角色文学是 0 级 —— 曾经就因此在「谁高谁上」上判错了。
	var skill_table: Resource = custom.tables["character_base_skill"].duplicate(true)
	var base_skills: Array = []
	for skill_row: Resource in skill_table.rows:
		if str(skill_row.char_id) == str(base.char_id):
			base_skills.append(skill_row)
	var rows: Array = [base]
	var skill_rows: Array = base_skills.duplicate()
	for index in range(1, count):
		var row: Resource = base.duplicate(true)
		row.id = "%s_%d" % [str(base.char_id), index]
		row.char_id = row.id
		row.name_cn = (
			"玄微剑派掌门人独孤求败·第%d席" % index
			if long_names
			else "%s %d" % [str(base.name_cn), index]
		)
		if index == smartest_index:
			row.initial_int = int(base.initial_int) + 15
		rows.append(row)
		for skill_row: Resource in base_skills:
			var copy: Resource = skill_row.duplicate(true)
			copy.char_id = row.char_id
			copy.id = "%s|%s" % [row.char_id, str(skill_row.skill_id)]
			skill_rows.append(copy)
	table.rows = rows
	table.index = {}
	for i in range(rows.size()):
		table.index[str(rows[i].id)] = i
	skill_table.rows = skill_rows
	skill_table.index = {}
	for i in range(skill_rows.size()):
		skill_table.index[str(skill_rows[i].id)] = i
	custom.tables["character_base"] = table
	custom.tables["character_base_skill"] = skill_table
	return custom


func checks() -> int:
	return _checks


func failures() -> Array[String]:
	return _failures


func fail(message: String) -> void:
	_failures.append(message)


func check_true(condition: bool, message: String) -> void:
	_checks += 1
	if not condition:
		_failures.append(message)


func check_false(condition: bool, message: String) -> void:
	check_true(not condition, message)


func check_eq(actual: Variant, expected: Variant, message: String) -> void:
	_checks += 1
	if actual != expected:
		_failures.append("%s（实际 %s，期望 %s）" % [message, actual, expected])


func check_ne(actual: Variant, unexpected: Variant, message: String) -> void:
	_checks += 1
	if actual == unexpected:
		_failures.append("%s（不该等于 %s）" % [message, unexpected])


func check_float(actual: float, expected: float, message: String, tolerance: float = 0.0001) -> void:
	_checks += 1
	if absf(actual - expected) > tolerance:
		_failures.append("%s（实际 %f，期望 %f，容差 %f）" % [message, actual, expected, tolerance])


func check_in_range(actual: float, low: float, high: float, message: String) -> void:
	_checks += 1
	if actual < low or actual > high:
		_failures.append("%s（实际 %f，期望落在 [%f, %f]）" % [message, actual, low, high])


func check_gt(actual: float, threshold: float, message: String) -> void:
	_checks += 1
	if actual <= threshold:
		_failures.append("%s（实际 %f，应大于 %f）" % [message, actual, threshold])


func check_lt(actual: float, threshold: float, message: String) -> void:
	_checks += 1
	if actual >= threshold:
		_failures.append("%s（实际 %f，应小于 %f）" % [message, actual, threshold])


func check_not_null(value: Variant, message: String) -> void:
	_checks += 1
	if value == null:
		_failures.append(message)


## 断言应该是 null（`check_not_null` 的补集；少了它，用例只能写 check_true(x == null)）
func check_null(value: Variant, message: String) -> void:
	_checks += 1
	if value != null:
		_failures.append("%s（实际 %s）" % [message, value])


## 造一张表库，把某张图里的**隐藏内容整行复制 `times` 倍**（只改内存副本）。
## 用途：线索本的「长列表」用例（`tests/test_clue.gd`）与体量观测工具（`tools/analysis_perf.gd`）——
## 真实数据只有 8 条线索，到不了会长到出问题的规模，而「列表被截断」在界面上只是「少了几条」。
func table_with_duplicated_triggers(db, scene_id: String, times: int):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["hidden_trigger"].duplicate(true)
	var originals: Array = []
	for row: Resource in table.rows:
		if str(row.scene_id) == scene_id:
			originals.append(row)
	for index in range(1, times):
		for base: Resource in originals:
			var copy: Resource = base.duplicate(true)
			copy.trigger_id = "%s_dup%d" % [str(base.trigger_id), index]
			copy.name_cn = "%s（副本%d）" % [str(base.name_cn), index]
			table.rows.append(copy)
			table.index[copy.trigger_id] = table.rows.size() - 1
	custom.tables["hidden_trigger"] = table
	return custom


## 造一张表库，把某张图**第 1 层的房间复制成 `floors` 层**（只改内存副本；出口/宝箱/隐藏触发清空，
## 免得把别的机制牵扯进来）。用途：完成度面板的「长列表」用例（`tests/test_dungeon_ui.gd`）
## 与体量观测工具（`tools/analysis_perf.gd`）——真实数据只有 3 层。
func table_with_many_floors(db, scene_id: String, floors: int):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["dungeon_room"].duplicate(true)
	var base_rooms: Array = []
	for row: Resource in table.rows:
		if str(row.scene_id) == scene_id and int(row.floor) == 1:
			base_rooms.append(row)
	for floor in range(2, floors + 1):
		for base: Resource in base_rooms:
			var copy: Resource = base.duplicate(true)
			copy.room_id = "%s_f%d" % [str(base.room_id), floor]
			copy.floor = floor
			copy.room_name = "%s（第%d层）" % [str(base.room_name), floor]
			copy.exit_rooms = ""
			copy.hidden_trigger = ""
			copy.chest_id = ""
			table.rows.append(copy)
			table.index[copy.room_id] = table.rows.size() - 1
	custom.tables["dungeon_room"] = table
	return custom
