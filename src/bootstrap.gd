## 引导场景：加载配置表 → 交叉校验 → 打印摘要。
##
## 它不做玩法，只是让「F5 就能看到框架是否健康」这件事成立，
## 后续所有场景都接在它后面（见 docs/dev/框架说明.md 的扩展点）。
extends Node

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const TableValidatorScript := preload("res://src/core/table_validator.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")

const TITLE := "《轮回》基础框架 · 引导场景"


func _ready() -> void:
	print("=== %s ===" % TITLE)
	var selftest := _has_user_arg("--bootstrap-selftest")
	var db = _resolve_db()
	if db == null:
		push_error("[Bootstrap] 配置表未生成，先运行 tools/build_tables.gd")
		if selftest:
			_finish_selftest(false, PackedStringArray(["配置表没加载起来"]))
		return
	_print_tables(db)
	_print_characters(db)
	var errors := _check(db)
	if selftest:
		_run_bootstrap_selftest(db, errors)
	else:
		print("=== 引导结束 ===")


## 优先复用 GameData 单例，避免重复加载；拿不到就自己加载一份。
##
## **不要写 `get_node("/root/GameData")`**（`AGENTS.md` 的硬规矩）：`--script` 模式下 autoload
## 还没进活动场景树，绝对路径取不到。**2026-10-04 实测这里就是全项目唯一漏网的一处**——
## 它没被发现，是因为本文件的自检是在**真场景**里跑的（那时绝对路径能用），
## 而工具链（`run_tests`／变异探针／平衡分析）全是 `--script` 起的。现在按 `main_menu.gd` 的写法取：
## `Engine.get_main_loop() as SceneTree` 再相对查找。
func _resolve_db():
	var tree := Engine.get_main_loop() as SceneTree
	var game_data: Node = tree.root.get_node_or_null("GameData") if tree != null else null
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var db = TableDbScript.new()
	db.load_all()
	return db


func _print_tables(db) -> void:
	var counts: Dictionary = db.summary()
	print("[表] 共 %d 张：" % counts.size())
	for table_name: String in counts:
		print("  - %-22s %4d 行" % [table_name, counts[table_name]])


func _print_characters(db) -> void:
	var calculator = AttributeCalculatorScript.new(db)
	print("[角色] 可操控角色：")
	for row: Resource in db.rows("character_base"):
		var actor = BattleActorScript.from_character(db, row)
		print("  - %s（%s）Lv%d 气血 %d 内力 %d 外功 %d 内功 %d 身法 %d 招式 %s" % [
			actor.display_name, row.role_tag, actor.level,
			actor.max_hp(), actor.max_qi(),
			int(actor.stat("atk_phys")), int(actor.stat("atk_qi")), int(actor.stat("speed")),
			", ".join(actor.skills),
		])
		var attrs: Dictionary = calculator.attr_totals_of(row.initial_attrs())
		print("     五维 力%s 体%s 敏%s 智%s 运%s" % [
			int(attrs.get("str", 0)), int(attrs.get("con", 0)), int(attrs.get("agi", 0)),
			int(attrs.get("int", 0)), int(attrs.get("luk", 0)),
		])
	print("[敌人] 小队 %d 支，明雷刷新点 %d 个" % [
		db.rows("enemy_team").size(), db.rows("roaming_spawn").size(),
	])
	print("[掉落] 掉落组 %d 个，难度 %s" % [
		db.column_values("drop_table", "drop_group").size(),
		", ".join(_column_names(db, "difficulty_config", "difficulty_id")),
	])


func _check(db) -> PackedStringArray:
	var errors: PackedStringArray = TableValidatorScript.validate(db)
	if errors.is_empty():
		print("[校验] 通过：主键、枚举、引用、数值区间全部合法")
		return errors
	push_error("[校验] 发现 %d 条问题：" % errors.size())
	for message: String in errors:
		push_error("  - " + message)
	return errors


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


## 真实场景自检（`-- --bootstrap-selftest`）：把"配置表诊断场景"也变成验收里的一步。
##
## 为什么值得单独跑：`TableDb.summary()` 与「模板 → 战斗单位 → 五维合计」这条链**只有这个场景在用**
## （`from_character` 别处也用，但 `summary()` 只此一家）。而在此之前，**没有任何一步跑过
## `scenes/bootstrap.tscn`**——`AGENTS.md` 却把它当作"配置表诊断场景"介绍给后来的人：
## 它坏了，没人会知道（真场景自检那十条里没有它，用例也没有加载它）。
## 所以按其余场景的同一套口径补上：走一遍真路径 → 打印可核对的证据行 → 报 `BOOTSTRAP SELF-TEST` → 退出码。
func _run_bootstrap_selftest(db, errors: PackedStringArray) -> void:
	var ok := true
	var lines := PackedStringArray()

	var counts: Dictionary = db.summary()
	var populated := 0
	for table_name: String in counts:
		if int(counts[table_name]) > 0:
			populated += 1
	var summary_ok := counts.size() > 20 and populated == counts.size()
	ok = ok and summary_ok
	lines.append("摘要：%d 张表、%d 张有行=%s" % [counts.size(), populated, summary_ok])

	var templates: Array = db.rows("character_base")
	var actors_ok := not templates.is_empty()
	for row: Resource in templates:
		var actor = BattleActorScript.from_character(db, row)
		if actor == null or str(actor.display_name).is_empty() \
				or actor.max_hp() <= 0.0 or actor.skills.is_empty():
			actors_ok = false
	ok = ok and actors_ok
	lines.append("角色模板 %d 条都能建出战斗单位（名字／气血／招式齐全）=%s" % [templates.size(), actors_ok])

	var calculator = AttributeCalculatorScript.new(db)
	var attrs_ok := true
	for row: Resource in templates:
		var attrs: Dictionary = calculator.attr_totals_of(row.initial_attrs())
		for key: String in ["str", "con", "agi", "int", "luk"]:
			if not attrs.has(key):
				attrs_ok = false
	ok = ok and attrs_ok
	lines.append("五维合计拿得到（str/con/agi/int/luk）=%s" % attrs_ok)

	var difficulties := _column_names(db, "difficulty_config", "difficulty_id")
	var diff_ok := not difficulties.is_empty()
	ok = ok and diff_ok
	lines.append("难度列 %d 个=%s（%s）" % [difficulties.size(), diff_ok, "／".join(difficulties)])

	var valid_ok := errors.is_empty()
	ok = ok and valid_ok
	lines.append("交叉校验零错误=%s（%d 条）" % [valid_ok, errors.size()])

	_finish_selftest(ok, lines)


func _finish_selftest(ok: bool, lines: PackedStringArray) -> void:
	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	for line: String in lines:
		print("  " + line)
	print("BOOTSTRAP SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	get_tree().quit(0 if ok else 1)


func _column_names(db, table_name: String, column: String) -> PackedStringArray:
	var out := PackedStringArray()
	for row: Resource in db.rows(table_name):
		out.append(str(row.get(column)))
	return out
