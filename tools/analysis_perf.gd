## 体量观测（只读分析，不是门限）：把「人最多 / 列表最长 / 存档最满」的几组极端数据跑一遍，
## 打印构造与查询耗时，以及静态内存占用。
##
## 用法：
##   tools\run_perf_analysis.bat
##
## 与 `analysis_balance.gd` 同类：**不改数据、不写存档、不判通过／不通过**——耗时随机器与当时负载波动，
## 做成门限只会让正常波动变成假红。它给的是一条「现在这些极端规模要花多少」的量级基线，
## 人工整理进 `docs/dev/性能观测.md`。
##
## 口径（改了这里也要改那份报告）：
##   ① 4 人满装（每人 6 件实例）+ 大背包（15 种道具各堆满）：构造 → 造战斗单位 → 打一场 4v4
##   ② 线索本 40 条（隐藏内容复制 6 倍）：查询 → 打开面板并建行
##   ③ 副本完成度 30 层（第 1 层复制成 30 层）：取完成度与分层 → 开面板建行
##   ④ **每项在同一次会话里连测 3 次，报「中位数（最小–最大）」**：2026-10-03 实测，同一份代码
##      在不同会话里能差 2–3 倍（构造 1.6ms vs 5.5ms，见框架说明决策 247），**单次数字没法跨会话比**；
##      而同一会话里的三次很稳（±0.2ms）——所以中位＋波动范围才是能看的东西。
##
## 注意：helper 的参数**故意不写类型**（`TableDb` 是 `RefCounted`，写 `db: Resource` 会当场报错，
## `analysis_balance.gd` 踩过这个坑）。
extends SceneTree

## 同一项在一趟里连测几次（取中位；见文件头的口径 ④）
const REPEATS := 3


func _initialize() -> void:
	var db = load("res://src/core/table_db.gd").new()
	db.load_all()
	# 复用测试夹具（复制行只改内存副本，不碰策划的 CSV）
	var helper = load("res://tests/test_case.gd").new()
	print("---- 体量观测（口径见 docs/dev/性能观测.md；同会话连测 %d 次取中位）----" % REPEATS)
	var party_runs: Array = []
	for index in range(REPEATS):
		party_runs.append(_measure_rich_party(db, helper))
	_print_rich_party(party_runs)
	var clue_runs: Array = []
	for index in range(REPEATS):
		clue_runs.append(_measure_long_clue_list(db, helper))
	_print_long_clue_list(clue_runs)
	var floor_runs: Array = []
	for index in range(REPEATS):
		floor_runs.append(_measure_many_floors(db, helper))
	_print_many_floors(floor_runs)
	print("· 静态内存 %.1f MB（进程级，含引擎本身，只作量级参考）" % _memory_mb())
	# 同 `analysis_balance.gd`：给"跑完了"一个可 findstr 的凭证。
	# 计时本来就是量级参考，但**半截的表**（运行期错误掐断）连量级都不成立。
	print("PERF SNAPSHOT: OK")
	quit(0)


func _ms_since(start_usec: int) -> float:
	return float(Time.get_ticks_usec() - start_usec) / 1000.0


func _memory_mb() -> float:
	return float(Performance.get_monitor(Performance.MEMORY_STATIC)) / 1048576.0


## 一组数字的中位与范围，如 `5.6ms（5.5–5.7）`
static func _median_ms(values: Array) -> String:
	var sorted := values.duplicate()
	sorted.sort()
	var count := sorted.size()
	var median := float(sorted[count / 2]) if count % 2 == 1 \
		 else (float(sorted[count / 2 - 1]) + float(sorted[count / 2])) / 2.0
	return "%.1fms（%.1f–%.1f）" % [median, float(sorted[0]), float(sorted[count - 1])]


static func _pick(runs: Array, key: String) -> Array:
	var out: Array = []
	for run: Dictionary in runs:
		out.append(float(run[key]))
	return out


## 4 人满装 + 大背包：从造数据到打完一场 4v4，再到存档往返（只测不打印）
func _measure_rich_party(db, helper) -> Dictionary:
	var GameStateScript := load("res://src/core/game_state.gd")
	var PartyBuilderScript := load("res://src/core/party_builder.gd")
	var EnemyFactoryScript := load("res://src/core/enemy_factory.gd")
	var SimScript := load("res://src/core/battle_simulator.gd")
	var RngScript := load("res://src/core/rng_service.gd")

	var t0 := Time.get_ticks_usec()
	var wide = helper.table_with_n_chars(4)
	# 队伍必须**显式点名 4 人**：0.10.0 起 `new_game()` 只给 `recruit_def.is_initial` 的成员
	# （开局书生一人），不点名的话这一段会静默退化成「1 人满装」——2026-10-03 实测过一次
	# （打印里出现「（1 人）」、模拟 1 回合就结束），量到的不再是本页要测的最重夹具。
	var state = helper.party_state(wide, 4)
	# 夹具自检：这一段的名字就叫「4 人满装」，队伍少于 4 人说明夹具又跟着设计状态漂了
	# （2026-10-03 真的退化过一次：new_game 只给初始成员，于是量成了「1 人满装」）。
	if state.char_ids.size() != 4:
		push_error("[PerfAnalysis] 夹具队伍只有 %d 人（应为 4）——量到的不是最重的夹具" % state.char_ids.size())
	for char_id: String in state.char_ids:
		var weapon_type := str(wide.get_row("character_base", char_id).weapon_type)
		for base_id: String in ["eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01", "eq_acc_01", "eq_belt_01"]:
			var instance_id: String = state.inventory.add_equipment(wide, base_id)
			state.inventory.equip(wide, char_id, state.level_of(char_id), weapon_type, instance_id)
	for row: Resource in db.rows("item_base"):
		if str(row.item_type) == "currency":
			continue
		state.inventory.add_item(wide, str(row.item_id), maxi(1, int(row.stack_max)))
	var build_ms := _ms_since(t0)

	var t1 := Time.get_ticks_usec()
	var actors: Array = PartyBuilderScript.build_actors(wide, state)
	var party_ms := _ms_since(t1)

	var enemies: Array = EnemyFactoryScript.new(wide).create_team("team_heifeng_elite", "nightmare")
	var t2 := Time.get_ticks_usec()
	var sim = SimScript.new(wide, RngScript.new(20261003))
	var result: Dictionary = sim.simulate(actors, enemies, {"modifiers": {}})
	var sim_ms := _ms_since(t2)

	var t3 := Time.get_ticks_usec()
	var text := JSON.stringify(state.to_dict(), "  ")
	var save_ms := _ms_since(t3)
	var t4 := Time.get_ticks_usec()
	var back = GameStateScript.from_dict(JSON.parse_string(text), wide)
	var load_ms := _ms_since(t4)
	return {
		"构造": build_ms, "造战斗单位": party_ms, "模拟": sim_ms, "存档": save_ms, "读档": load_ms,
		"actors": actors.size(), "winner": str(result.get("winner", "?")),
		"rounds": int(result.get("rounds", 0)), "kb": float(text.length()) / 1024.0,
		"read_back": back != null,
	}


func _print_rich_party(runs: Array) -> void:
	var last: Dictionary = runs[runs.size() - 1]
	print("· 4 人满装 + 大背包：构造 %s｜造战斗单位 %s（%d 人）｜4v4 模拟 %s（%s，%d 回合）｜存档 %s（%.1f KB）｜读档 %s（读回=%s）"
		% [
			_median_ms(_pick(runs, "构造")), _median_ms(_pick(runs, "造战斗单位")), int(last["actors"]),
			_median_ms(_pick(runs, "模拟")), str(last["winner"]), int(last["rounds"]),
			_median_ms(_pick(runs, "存档")), float(last["kb"]),
			_median_ms(_pick(runs, "读档")), str(last["read_back"]),
		])


## 线索本 40 条：查询 + 打开真面板并建行（只测不打印）
func _measure_long_clue_list(db, helper) -> Dictionary:
	var GameStateScript := load("res://src/core/game_state.gd")
	var wide = helper.table_with_duplicated_triggers(db, "scene_heifengzhai", 6)
	var state = GameStateScript.new_game(wide, "normal")

	var t0 := Time.get_ticks_usec()
	var service = load("res://src/core/clue_service.gd").new(wide, state)
	var entries: Array = service.clues_for_scene("scene_heifengzhai")
	var query_ms := _ms_since(t0)

	var screen = load("res://scenes/clue_screen.tscn").instantiate()
	screen.db_override = wide
	screen.state_override = state
	screen.scope = "scene"
	screen.scene_id = "scene_heifengzhai"
	var t1 := Time.get_ticks_usec()
	# **`--script` 模式下 `add_child` 不会触发 `_ready`**（本项目的用例都显式调 `setup()`），
	# 所以建 UI 的耗时要连 `setup()` 一起量（第一版只量了 add_child，量出来 0 行）。
	root.add_child(screen)
	screen.setup()
	var panel_ms := _ms_since(t1)
	var rows: int = screen.entry_count()
	root.remove_child(screen)
	screen.free()
	return {"查询": query_ms, "面板": panel_ms, "entries": entries.size(), "rows": rows}


func _print_long_clue_list(runs: Array) -> void:
	var last: Dictionary = runs[runs.size() - 1]
	print("· 线索本 %d 条：查询 %s｜开面板并建 %d 行 %s"
		% [int(last["entries"]), _median_ms(_pick(runs, "查询")), int(last["rows"]), _median_ms(_pick(runs, "面板"))])


## 副本完成度 30 层：取完成度+分层 + 打开真面板建行（只测不打印）
func _measure_many_floors(db, helper) -> Dictionary:
	var GameStateScript := load("res://src/core/game_state.gd")
	var wide = helper.table_with_many_floors(db, "scene_heifengzhai", 30)
	var state = GameStateScript.new_game(wide, "normal")
	var service = load("res://src/core/dungeon_service.gd").new(wide, state)

	var t0 := Time.get_ticks_usec()
	var snapshot: Dictionary = service.progress("scene_heifengzhai")
	var floors: Array = service.floors("scene_heifengzhai")
	var data_ms := _ms_since(t0)

	var screen = load("res://scenes/dungeon_screen.tscn").instantiate()
	screen.db_override = wide
	screen.state_override = state
	screen.scene_id = "scene_heifengzhai"
	var t1 := Time.get_ticks_usec()
	root.add_child(screen)
	screen.setup()
	var panel_ms := _ms_since(t1)
	var rows := 0
	for node: Node in screen.find_children("FloorRow*", "HBoxContainer", true, false):
		rows += 1
	root.remove_child(screen)
	screen.free()
	return {
		"数据": data_ms, "面板": panel_ms, "floors": floors.size(),
		"percent": int(snapshot["percent"]), "rows": rows,
	}


func _print_many_floors(runs: Array) -> void:
	var last: Dictionary = runs[runs.size() - 1]
	print("· 完成度 %d 层：取完成度+分层 %s（总计 %d%%）｜开面板并建 %d 行 %s"
		% [
			int(last["floors"]), _median_ms(_pick(runs, "数据")), int(last["percent"]),
			int(last["rows"]), _median_ms(_pick(runs, "面板")),
		])
