## 代码契约（preload／单一定义／存档字段）——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "代码契约（preload／单一定义／存档字段）"


func run() -> void:
	_check_scene_node_contracts()
	_check_framework_save_claims()
	_check_no_absolute_autoload_paths()
	_check_preload_for_global_class_calls()
	_check_tests_use_seeded_rng()
	_check_project_entry_point()
	_check_repo_conventions()
	_check_single_source_of_truth()
	_check_no_new_test_only_api()
	_check_battle_tests_are_seeded()
	_check_save_fields_are_serialized()






func _check_save_fields_are_serialized() -> void:
	var var_regex := RegEx.new()
	var_regex.compile("(?m)^var\\s+([a-z_0-9]+)\\s*[:=]")
	var key_regex := RegEx.new()
	key_regex.compile("\"([a-z_0-9]+)\"\\s*:")
	for target: Dictionary in SAVE_SERIALIZATION_TARGETS:
		var path := str(target["path"])
		var exempt: Array = target["exempt"]
		var source := FileAccess.get_file_as_string(path)
		check_false(source.is_empty(), "读得到 %s" % path)
		if source.is_empty():
			continue
		var to_dict_start := source.find("func to_dict")
		check_gt(float(to_dict_start), 0.0, "%s 里有 to_dict()" % path)
		if to_dict_start < 0:
			continue
		var to_dict_body := _to_dict_body(source)
		check_false(to_dict_body.is_empty(), "%s 的 to_dict() 函数体能取出来（花括号配平）" % path.get_file())
		if to_dict_body.is_empty():
			continue
		var declared := PackedStringArray()
		for hit: RegExMatch in var_regex.search_all(source):
			declared.append(hit.get_string(1))
		var written := {}
		for hit: RegExMatch in key_regex.search_all(to_dict_body):
			written[hit.get_string(1)] = true
		check_gt(float(declared.size()), 3.0, "%s 扫到足够多的字段（%d 个）" % [path.get_file(), declared.size()])
		check_gt(float(written.size()), 3.0, "%s 的 to_dict() 里扫到足够多的键（%d 个）" % [path.get_file(), written.size()])
		for name: String in declared:
			if exempt.has(name):
				continue
			check_true(
				written.has(name),
				"%s 的字段 '%s' 没写进 to_dict()：读档会静默丢（要么补上，要么加进白名单说明理由）"
					% [path.get_file(), name]
			)
		# 反向：写进去的键，读档时得有人认——否则存了也回不来，而玩家看到的只是"东西没了"。
		var body_index := source.find(to_dict_body)
		var outside := source.substr(0, body_index) + source.substr(body_index + to_dict_body.length())
		var orphans := PackedStringArray()
		for key: String in written.keys():
			if not outside.contains("\"%s\"" % key):
				orphans.append(str(key))
		check_eq(
			orphans.size(), 0,
			"%s 的 to_dict() 写了 %s，但文件里再没人提这个键——读档时没人认它（存了却回不来）：补读档分支，或把键删掉"
				% [path.get_file(), ", ".join(orphans)]
		)






func _check_tests_use_seeded_rng() -> void:
	var dir := DirAccess.open("res://tests")
	if dir == null:
		fail("打不开 tests/ 目录，无法检查随机源")
		return
	var scanned := 0
	for file_name: String in dir.get_files():
		if not file_name.ends_with(".gd"):
			continue
		var source := FileAccess.get_file_as_string("res://tests/%s" % file_name)
		scanned += 1
		for raw: String in source.split("\n"):
			var line := raw.strip_edges()
			if line.is_empty() or line.begins_with("#"):
				continue
			# **先抹掉字符串字面量再搜**：否则上面那三个模式常量会把门限自己判红
			# （第一次跑就是那样，三条红全指着自己）。调用不可能写在字符串里。
			var code := _strip_string_literals(raw)
			for pattern: String in UNSAFE_RNG_PATTERNS:
				if code.contains(pattern):
					fail(
						"%s 里有没给种子的随机源 `%s`：断言会时红时绿。改成 `RngServiceScript.new(SEED)`"
							% [file_name, pattern]
					)
	check_gt(float(scanned), 30.0, "扫到足够多的用例文件（%d 个）" % scanned)




## 去掉一行里的字符串字面量（含简单的 `\"` 转义），只留代码部分。
func _strip_string_literals(line: String) -> String:
	var out := ""
	var in_string := false
	var index := 0
	while index < line.length():
		var ch := line[index]
		if in_string:
			if ch == "\\" and index + 1 < line.length():
				index += 2
				continue
			if ch == "\"":
				in_string = false
		else:
			if ch == "\"":
				in_string = true
			else:
				out += ch
		index += 1
	return out




## 13. 工程入口与 autoload：`project.godot` 里这两处是**文档写明的不变量**，以前却没有任何门限在盯。
##
## AGENTS.md 写着「主场景是 `scenes/main_menu.tscn`（启动菜单）；`scenes/bootstrap.tscn` 是配置表诊断场景，
## 不是入口」，也写着 `GameData` 与 `GameSession` 是 autoload。
## 危险性在于：把 `run/main_scene` 改成 bootstrap／占位场景，**所有用例照样绿**——场景自检都是按路径
## 显式启动的，没人去读工程入口；那时候玩家双击游戏会进到诊断场景。
## （autoload 这两条相对安全些：场景自检跑起来会崩，但那已经是运行期的事了，这里静态钉住更早。）
func _check_project_entry_point() -> void:
	var text := FileAccess.get_file_as_string("res://project.godot")
	check_true(
		text.contains('run/main_scene="res://scenes/main_menu.tscn"'),
		"主场景是启动菜单（bootstrap 是诊断场景，不是入口）",
	)
	check_true(text.contains('GameData="*res://src/autoload/game_data.gd"'), "GameData 是 autoload")
	check_true(text.contains('GameSession="*res://src/autoload/game_session.gd"'), "GameSession 是 autoload")
	check_true(FileAccess.file_exists("res://src/autoload/game_data.gd"), "GameData 脚本存在")
	check_true(FileAccess.file_exists("res://src/autoload/game_session.gd"), "GameSession 脚本存在")
	check_true(
		text.contains('renderer/rendering_method="mobile"'),
		"渲染器仍是 mobile（换渲染器是正式变更：改这里要同时更新 AGENTS 与本条门限）",
	)






func _check_repo_conventions() -> void:
	var src_files: PackedStringArray = _collect_files(["src"])
	check_gt(float(src_files.size()), 20.0, "能扫到 src 下的文件（%d 个）" % src_files.size())
	for path: String in src_files:
		var text := FileAccess.get_file_as_string("res://" + path)
		if not CSV_DIR_WHITELIST.has(path):
			check_false(
				text.contains("res://data/tables"),
				"%s 提到了 res://data/tables（运行期只读 data/generated；构建期白名单见 CSV_DIR_WHITELIST）" % path,
			)
	# 白名单自己也会烂：文件被删/改名了就把这条一起清掉
	for path: String in CSV_DIR_WHITELIST:
		check_true(FileAccess.file_exists("res://" + path), "CSV 目录白名单里的 %s 不存在了" % path)

	var bat_count := 0
	var ps1_count := 0
	for file_name: String in DirAccess.get_files_at("res://tools"):
		if file_name.ends_with(".bat"):
			bat_count += 1
			_audit_bat("tools/%s" % file_name)
		elif file_name.ends_with(".ps1"):
			ps1_count += 1
			_audit_ps1("tools/%s" % file_name)
	check_gt(float(bat_count), 3.0, "扫到足够多的 .bat（%d 个）" % bat_count)
	check_gt(float(ps1_count), 0.0, "扫到 .ps1（%d 个）" % ps1_count)






func _check_single_source_of_truth() -> void:
	var files: PackedStringArray = _collect_files(["src"])
	check_gt(float(files.size()), 20.0, "能扫到 src 下的文件（%d 个）" % files.size())
	for rule: Dictionary in SINGLE_SOURCE_RULES:
		var label := str(rule["label"])
		var owner := str(rule["owner"])
		var allowed := PackedStringArray([owner])
		allowed.append_array(PackedStringArray(rule["also"]))
		var hits := PackedStringArray()
		for path: String in files:
			if not path.ends_with(".gd"):
				continue
			if FileAccess.get_file_as_string("res://" + path).contains(label):
				hits.append(path)
		check_gt(float(hits.size()), 0.0, "「%s」至少出现在一处（规则不是空的）" % label)
		check_true(hits.has(owner), "「%s」仍在定义处 %s" % [label, owner])
		for path: String in hits:
			check_true(
				allowed.has(path),
				"「%s」在 %s 里又写了一份（定义处是 %s）：%s" % [label, path, owner, str(rule["why"])],
			)
		check_true(FileAccess.file_exists("res://" + owner), "规则里的定义处 %s 不存在了" % owner)
		for path: String in rule["also"]:
			check_true(FileAccess.file_exists("res://" + path), "白名单里的 %s 不存在了，把这条删掉" % path)






func _check_no_new_test_only_api() -> void:
	var src: PackedStringArray = _collect_files(["src"])
	check_gt(float(src.size()), 20.0, "能扫到 src 下的文件（%d 个）" % src.size())
	var corpus := ""
	var files: PackedStringArray = _collect_files(["src"])
	files.append_array(_collect_files(["scenes"]))
	files.append("project.godot")
	for path: String in files:
		corpus += FileAccess.get_file_as_string("res://" + path)
	var def_regex := RegEx.new()
	def_regex.compile("(?m)^(?:static\\s+)?func\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(")
	var found_names := PackedStringArray()
	var unexpected := PackedStringArray()
	for path: String in src:
		if not path.ends_with(".gd"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		for found: RegExMatch in def_regex.search_all(text):
			var name := found.get_string(1)
			if name.begins_with("_"):
				continue
			var use_regex := RegEx.new()
			use_regex.compile("\\b%s\\b" % name)
			if use_regex.search_all(corpus).size() > 1:
				continue     # 游戏路径里有调用，正常
			found_names.append(name)
			if not TEST_ONLY_API_ALLOWED.has(name):
				unexpected.append("%s（%s）" % [name, path])
	check_gt(float(found_names.size()), 20.0, "扫到足够多的「只被测试调用」接口（%d 个）" % found_names.size())
	check_eq(
		unexpected.size(), 0,
		"新增了「游戏路径从不调用」的接口：%s —— 接进游戏路径，或把名字与理由写进 TEST_ONLY_API_ALLOWED"
			% ", ".join(unexpected),
	)
	# 白名单自己也会烂：接口删了/改名了就把那行一起删
	var all_src_text := ""
	for path: String in src:
		if path.ends_with(".gd"):
			all_src_text += FileAccess.get_file_as_string("res://" + path)
	for name: String in TEST_ONLY_API_ALLOWED:
		if not all_src_text.contains("func %s(" % name) and not all_src_text.contains("func %s (" % name):
			fail("白名单里的 %s 在 src/ 里已经找不到定义了，把这条删掉（理由：%s）" % [name, str(TEST_ONLY_API_ALLOWED[name])])
		elif not found_names.has(name):
			# 反方向（2026-10-03 补）：白名单里记的是"游戏路径从不调用"的接口，
			# 一旦它**被接进游戏**了，这条就不再需要——留着会让"以后再断掉调用"没人发现。
			fail("TEST_ONLY_API_ALLOWED 里的 %s 已经接进游戏路径了（现在数得到调用方），把这条删掉" % name)
	print("  · 只被测试调用的接口：%d 个（都在白名单里）" % found_names.size())




## 11. 场景自检开关与总命令必须双向对齐（和上一条同一类洞：「写了自检却没人跑」）。
##
## 12. 开发文档里引用的 `tools/…` 文件必须真的存在。文档里的命令过期，后来的人会照着敲一条
## 不存在的命令——`check_maps` 接进总命令那次就差点留下「手动跑一次」这种尸体说法。
## 只查 `docs/dev/`（开发侧自己的文档）；设计文档是策划写的，可能提到将来的工具，不该拿这条误伤。
## 注意这条门限**会读它自己的说明**：文档里要举例（哪怕是反例）请写成 `tools/<假名字>.bat`
## 这种不构成真实路径的形态，否则门限自己就红了（真踩过，见框架说明决策 57）。
## 13. 用例里造的**战斗界面**必须固定随机种子。
##
## 起因（2026-10-03 实打实抓到的一次 flaky）：`test_skill_grant` 的真实战斗没设 `rng_seed`，
## 而 `BattleScreen.rng_seed` 默认是 -1（`RngService` 见负数就 `randomize()`）——
## 掉落件数是掷出来的，于是「结算卡片里有没有那一行」时红时绿；`test_overworld` 的闭环战斗同样没设，
## 而那场还会写经验与「明雷已清」，随机种子会让**后面一串断言**跟着飘。
## 项目对自检的要求里写着「必须确定性」，所以把它变成门限：造战斗界面的地方，
## 往下 12 行内必须出现 `rng_seed`。
func _check_battle_tests_are_seeded() -> void:
	var dir := DirAccess.open("res://tests")
	if dir == null:
		fail("打不开 tests/ 目录，无法检查战斗用例的随机种子")
		return
	var scanned := 0
	for file_name: String in dir.get_files():
		if not file_name.ends_with(".gd"):
			continue
		var source := FileAccess.get_file_as_string("res://tests/%s" % file_name)
		var lines := source.split("\n")
		for index in range(lines.size()):
			if not lines[index].contains("BATTLE_SCENE).instantiate()"):
				continue
			scanned += 1
			var window := "\n".join(lines.slice(index, mini(index + 12, lines.size())))
			check_true(
				window.contains("rng_seed"),
				"%s 第 %d 行造了战斗界面但没固定 rng_seed（掉落/暴击随机 → 断言时红时绿）" % [file_name, index + 1],
			)
	check_gt(float(scanned), 5.0, "确实扫到了战斗界面用例（%d 处）" % scanned)




## 62. `框架说明` 概述里那两个**存档数字**必须与代码一致：当前版本 vN 与 `to_dict()` 字段数。
##
## 由来（2026-10-04）：那一节写着「载入时做损坏兜底与 **v1 → v10** 迁移」「当前版本 **v11**」
## 「字段数（现在是 **22**）」，而代码里是 **v15**、字段数 **27**、迁移闸门到 v15——
## 同一段还照着 v11 列了一遍「每版加了什么」，于是 v12（战斗外增益）／v13（大地图坐标）／
## v14（天赋与自建模板）／v15（NPC 好感）**四版在入口文档里根本不存在**。
## 那一段不是"带日期的叙事"，是**入口概述**：新人照着它理解存档结构，所以按现状量。
##
## 口径：`框架说明.md` 里那两句各扫一处，分别与 `GameState.VERSION` 和一个全新 state 的
## `to_dict()` 字段数对账；**两句都必须扫到**（措辞改了要连着改这条门限，别让声称悄悄消失）。
func _check_framework_save_claims() -> void:
	var db := TableDbScript.new()
	db.load_all()
	var doc := FileAccess.get_file_as_string("res://docs/dev/框架说明.md")
	check_false(doc.is_empty(), "读得到 docs/dev/框架说明.md")
	var version_re := RegEx.new()
	version_re.compile("当前版本 \\*\\*v(\\d+)\\*\\*")
	var version_hit := version_re.search(doc)
	check_not_null(version_hit, "框架说明概述里写了「当前版本 **vN**」（措辞改了要连着改这条门限）")
	if version_hit != null:
		check_eq(
			int(version_hit.get_string(1)), int(GameStateScript.VERSION),
			"框架说明写过期了：它说当前存档版本是 v%s，代码里是 v%d" % [version_hit.get_string(1), int(GameStateScript.VERSION)]
		)
	var size_re := RegEx.new()
	size_re.compile("字段数（现在是 \\*{0,2}(\\d+)\\*{0,2}）")
	var size_hit := size_re.search(doc)
	check_not_null(size_hit, "框架说明概述里写了「字段数（现在是 N）」（措辞改了要连着改这条门限）")
	if size_hit != null:
		var fresh = GameStateScript.new_game(db, "normal")
		var actual := Dictionary(fresh.to_dict()).size()
		check_eq(
			int(size_hit.get_string(1)), actual,
			"框架说明写过期了：它说 `to_dict()` 有 %s 个字段，实际是 %d（也在 `test_save_store` 里钉着）"
				% [size_hit.get_string(1), actual]
		)




## 63. 取 autoload 不许走 `get_node("/root/X")` 绝对路径——`--script` 模式下那样会失败。
##
## 由来（2026-10-04）：这是 `AGENTS.md` 的硬性约定（`--script` 模式里 autoload 节点还没进活动场景树，
## 绝对路径取不到），可**全靠人记**。而这一条一旦写错，**受影响的是工具链本身**：
## `run_tests.gd`、变异探针、平衡／性能分析全是用 `--script` 起的——写错的路径在真场景自检里照样绿，
## 到工具里才炸；更糟的情况是"静默取不到 → 那块逻辑没生效"，而验收还是绿的。
##
## 口径：`src/**/*.gd` 里**去掉行内注释后**不许出现 `get_node("/root` 或 `get_node_or_null("/root`
## （`main_menu.gd` 的注释里写着"不能用 `get_node(\"/root/X\")`"，正是这条规矩的出处，别误伤它）。
## 同时要求 `src/` 里正规写法 `Engine.get_main_loop()` 至少还有 4 处——否则说明这段代码整体换了写法，
## 这条门限量的对象已经不是它了。
func _check_no_absolute_autoload_paths() -> void:
	var hits := PackedStringArray()
	var sanctioned := 0
	var scanned := 0
	for path: String in _collect_files(["src"]):
		if not path.ends_with(".gd"):
			continue
		scanned += 1
		var line_no := 0
		for raw: String in FileAccess.get_file_as_string("res://" + path).split("\n"):
			line_no += 1
			var code := raw.split("#")[0]
			if code.contains("Engine.get_main_loop"):
				sanctioned += 1
			for needle: String in ["get_node(\"/root", "get_node_or_null(\"/root"]:
				if code.contains(needle):
					hits.append("%s:%d「%s」" % [path.get_file(), line_no, raw.strip_edges()])
	check_gt(float(scanned), 50.0, "扫到足够多的脚本（%d 个）" % scanned)
	check_gt(float(sanctioned), 4.0, "`src/` 里还在用正规写法 Engine.get_main_loop（%d 处）" % sanctioned)
	check_eq(
		hits.size(), 0,
		"这些地方用绝对路径取 autoload（`--script` 模式会失败，写成 Engine.get_main_loop() 再相对取）：%s"
			% "；".join(hits)
	)




## 64. 调用全局类名（`X.new()`／`X.静态方法()`）的那个文件必须 `preload` 到它——
## **类型标注允许写全局名**（这条界线是这一轮量出来的，见下）。
##
## 由来（2026-10-04）：`AGENTS.md` 那条「跨文件类型引用用 `preload` 常量，不要依赖 `.dotdot` 的全局类名缓存」
## 从来没被量过。一量，现实是：**类型标注里写全局类名有 106 处**（`var x: TableDb`、`-> Inventory`），
## 而**真·实例化／静态调用**只有 **1 处漏网**——`save_store.gd` 一个 `preload` 都没有，
## 却全靠 `GameState.from_dict()/is_valid_dict()/format_time()` 干活（**能不能读档压在类缓存上**）。
## 于是把规矩收成能执行的那半句：**调用必须 `preload`**；类型标注跟随现状（工具链每轮
## 都会 `--editor --quit` 刷新缓存，标注那部分靠它）。AGENTS.md 的措辞已按这条界线写清。
##
## 口径：对 `src/**/*.gd` 里每个 `class_name X`，任何**别的**文件若在代码（**去掉行内注释**）里
## 出现 `X.new(` 或 `X.标识符(`，就必须包含那个文件路径的 `preload`。注释里提一嘴不算——
## 这个仓库的注释里有大量"反面例子"（`main_menu.gd` 就写着 `get_node("/root/X")` 之类）。
func _check_preload_for_global_class_calls() -> void:
	var files: PackedStringArray = _collect_files(["src"])
	var class_re := RegEx.new()
	class_re.compile("(?m)^class_name\\s+([A-Za-z_][A-Za-z0-9_]*)")
	var owners := {}
	for path: String in files:
		if not path.ends_with(".gd"):
			continue
		var found := class_re.search(FileAccess.get_file_as_string("res://" + path))
		if found != null:
			owners[found.get_string(1)] = path
	check_gt(float(owners.size()), 20.0, "扫到足够多的全局类（%d 个）" % owners.size())
	var misses := PackedStringArray()
	var preloaded_calls := 0
	var preload_re := RegEx.new()
	preload_re.compile("\\b[A-Za-z_][A-Za-z0-9_]*Script\\s*\\.\\s*[A-Za-z_]\\w*\\s*\\(")
	for path: String in files:
		if not path.ends_with(".gd"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		var code := ""
		for raw: String in text.split("\n"):
			code += str(raw.split("#")[0]) + "\n"
		preloaded_calls += preload_re.search_all(code).size()
		for class_id: String in owners.keys():
			if str(owners[class_id]) == path:
				continue
			var call_re := RegEx.new()
			call_re.compile("\\b%s\\s*\\.\\s*[A-Za-z_]\\w*\\s*\\(" % class_id)
			if call_re.search(code) == null:
				continue
			if not text.contains("res://" + str(owners[class_id])):
				misses.append("%s 调用 %s.*（没 preload res://%s）" % [path.get_file(), class_id, str(owners[class_id])])
	# 非空校验：跨文件调用**确实存在**，只是全走 `XxxScript.` 这套写法（今天 0 处裸调用）；
	# 万一哪天大家都改用别的写法，这条门限量的对象就变了，那时要连着改这里。
	check_gt(float(preloaded_calls), 5.0, "跨文件调用走的是 `XxxScript.` 这套 preload 写法（%d 处）" % preloaded_calls)
	check_eq(
		misses.size(), 0,
		"这些调用没走 preload（调用别压在 .godot 类缓存上）：%s" % "；".join(misses)
	)




## 9. 界面骨架的节点契约：代码里 `_require_node("A/B/C")` 要求的节点，在对应 `.tscn` 里必须真的存在。
##
## 运行期场景自检也会发现少节点，但那是「跑起来才知道」；改节点名时最容易只改一边
## （代码改了场景没改，或反过来），这里静态先查一遍，代价几乎为零。
func _check_scene_node_contracts() -> void:
	var regex := RegEx.new()
	regex.compile("_require_node\\(\"([^\"]+)\"\\)")
	var checked := 0
	for script_path: String in _collect_files(["src/ui"]):
		if not script_path.ends_with(".gd"):
			continue
		var scene_path := "scenes/%s.tscn" % script_path.get_file().get_basename()
		if not FileAccess.file_exists("res://" + scene_path):
			continue
		var source := FileAccess.get_file_as_string("res://" + script_path)
		var scene := FileAccess.get_file_as_string("res://" + scene_path)
		for found: RegExMatch in regex.search_all(source):
			var node_path := found.get_string(1)
			var leaf := node_path.get_file()
			var parent := node_path.get_base_dir()
			if parent.is_empty():
				parent = "."
			checked += 1
			var hit := false
			for line: String in scene.split("\n"):
				if line.begins_with("[node name=\"%s\"" % leaf) and line.contains("parent=\"%s\"" % parent):
					hit = true
					break
			check_true(hit, "%s 的 _require_node(\"%s\") 在 %s 里找不到对应节点（期望 parent=%s）" % [
				script_path.get_file(), node_path, scene_path, parent,
			])
	check_gt(float(checked), 30.0, "界面节点契约覆盖到足够多的节点（%d 个）" % checked)
