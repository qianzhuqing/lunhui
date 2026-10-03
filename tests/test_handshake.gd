## 策划对接守卫。
##
## 把「策划增加或修改需求后，计划与验证必须跟着动」这条规则变成硬门槛：
##   1. `docs/design/CHANGELOG.md` 的当前设计版本，必须与已动工模块的 design_version 一致
##   2. 已动工（进行中／已完成／需返工）的模块必须在 `docs/dev/验证清单.csv` 登记验证用例
##   3. 验证用例文件必须真实存在，且 verified_design_version 必须等于当前设计版本
##   4. `tests/` 下的用例文件（基础设施除外）必须被某个模块引用，不许有孤儿验证
##
## 红了怎么办：先读 CHANGELOG 看改了什么，评估要不要返工；然后更新代码、补或改用例，
## 最后同步 `docs/dev/模块对接表.csv`（design_version／status）与 `docs/dev/验证清单.csv`。
extends "res://tests/test_case.gd"

const CHANGELOG_PATH := "res://docs/design/CHANGELOG.md"
const TableValidatorScript := preload("res://src/core/table_validator.gd")
const TableRegistryScript := preload("res://src/data/table_registry.gd")
const CONTRACT_PATH := "res://docs/dev/模块对接表.csv"
const VERIFY_PATH := "res://docs/dev/验证清单.csv"
const TESTS_DIR := "res://tests"
const STATUS_PATH := "res://docs/dev/当前状态.md"
const QUESTIONS_PATH := "res://docs/dev/待策划确认.md"

## 需要「计划和验证跟上」的状态。
## 「需返工」故意不在其中：它本来就被标成落后于当前设计版本，是待处理状态，
## 不能因为版本对不上就再报一次；但它的验证用例仍然要登记（返工要有验收目标）。
const STARTED_STATUSES := ["进行中", "已完成"]
const MENTIONED_STATUSES := ["进行中", "已完成", "需返工"]

## 基础设施用例：不属于某个策划模块，不参与孤儿检查。
## `test_layout_budget.gd` 是"门限的裁判自己也要有反例"（见该文件头与框架说明决策 189）——
## 它盯的是 `LayoutBudget` 本身，不对应任何一条策划需求。
const INFRASTRUCTURE_TESTS := ["test_case.gd", "test_handshake.gd", "test_layout_budget.gd"]

## 只把文本文件读进语料。`docs/dev/images/` 下有 PNG 截图，当文本读会刷一屏
## 「Unicode parsing error」并把二进制垃圾塞进语料（踩过：9 条告警，全是截图）。
const TEXT_EXTENSIONS := ["gd", "tscn", "tres", "godot", "csv", "md", "json", "bat", "ps1", "txt"]

## 采样表那条门限要拿 growth_const 的公式重算，所以直接读 GrowthCalculator（不手抄公式）
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")


func suite_name() -> String:
	return "策划对接与验证覆盖"


func run() -> void:
	var version := _current_design_version()
	check_ne(version, "", "CHANGELOG.md 里应能找到「当前设计版本」")

	var contract := _read_csv(CONTRACT_PATH)
	var verify_rows := _read_csv(VERIFY_PATH)
	check_gt(float(contract.size()), 0.0, "对接表应能解析出模块行")
	check_gt(float(verify_rows.size()), 0.0, "验证清单应能解析出模块行")
	if version.is_empty() or contract.is_empty() or verify_rows.is_empty():
		return

	var verify_by_module: Dictionary = {}
	for row: Dictionary in verify_rows:
		verify_by_module[str(row.get("module_id", ""))] = row

	_check_design_version(version, contract)
	_check_verify_coverage(version, contract, verify_by_module)
	_check_contract_links(contract, verify_rows)
	_check_no_duplicate_modules(contract, verify_rows)
	_check_no_orphan_tests(verify_rows)
	_check_contract_assets(contract)
	_check_no_orphan_design_docs(contract)
	_check_no_orphan_code()
	_check_scene_node_contracts()
	_check_verify_tests_are_executed(verify_rows)
	_check_scene_selftests_are_wired()
	_check_doc_tool_references()
	_check_no_dangling_uids()
	_check_no_orphan_import_files()
	_check_every_scene_is_reachable()
	_check_no_hardcoded_step_counts()
	_check_git_repo_contract()
	_check_tables_dir_is_not_imported()
	_check_tests_use_seeded_rng()
	_check_design_docs_paths_exist()
	_check_asset_questions_match_status()
	_check_completion_rows_symbols_exist()
	_check_framework_doc_toc()
	_check_loop_segment_count()
	_check_design_doc_table_count()
	_check_project_entry_point()
	_check_repo_conventions()
	_check_doc_enums_match_code()
	_check_single_source_of_truth()
	_check_doc_tables_match_data()
	_check_shop_shelves_match_design()
	_check_combat_feel_matches_design()
	_check_no_new_test_only_api()
	_check_battle_tests_are_seeded()
	_check_designer_questions_cover_gaps()
	_check_blockers_are_tracked()
	_check_code_constants_exist_in_tables()
	_check_code_row_literals_exist()
	_check_data_dictionary_covers_columns()
	_check_save_fields_are_serialized()
	_check_engine_scripts_scan_logs()
	_check_slot_sample_table_matches_formula()
	_check_hidden_content_table_matches_data()
	_check_shop_table_matches_buildings()
	_check_shop_tabs_have_buttons()
	_check_question_numbering()
	_check_dashboard_numbers()
	_check_verification_check_floors()


## 33. 商店的页签列表与场景里的按钮必须一一对应。
##
## 由来（2026-10-03）：`shop_screen.gd` 的页签是**代码里的常量** `TABS`（buy／sell／buyback／service），
## 绑定时用 `_tabs_box.get_node_or_null("Tab%s" % 后缀)` —— **取不到就静默跳过**，
## 于是"TABS 里加了第五个页签、场景却没加按钮"的结果是：**玩家点不到那个页签，而自检全绿**
## （自检走的是 `select_tab(key)` 直调，不需要按钮存在）。反过来，场景里多一个 `Tab*` 按钮
## 也没人管（点下去什么都不做）。
##
## 所以这里把两侧对齐：TABS 的每个 key（经 `_tab_node_suffix` 的同名映射）都要在场景里找到
## `Tab<后缀>` 按钮；场景里每个 `Tab*` 按钮也都必须对应一个 key。
func _check_shop_tabs_have_buttons() -> void:
	var source := FileAccess.get_file_as_string("res://src/ui/shop_screen.gd")
	var scene := FileAccess.get_file_as_string("res://scenes/shop_screen.tscn")
	check_false(source.is_empty(), "读得到 shop_screen.gd")
	check_false(scene.is_empty(), "读得到 shop_screen.tscn")
	# ① TABS 的 key
	var tabs_start := source.find("const TABS")
	check_gt(float(tabs_start), 0.0, "shop_screen.gd 里有 TABS 常量")
	if tabs_start < 0:
		return
	var tabs_end := source.find("]", tabs_start)
	var tabs_body := source.substr(tabs_start, tabs_end - tabs_start)
	var key_regex := RegEx.new()
	key_regex.compile("\"key\"\\s*:\\s*\"([a-z_]+)\"")
	var keys := PackedStringArray()
	for hit: RegExMatch in key_regex.search_all(tabs_body):
		keys.append(hit.get_string(1))
	check_gt(float(keys.size()), 3.0, "TABS 解析出足够多的页签（%d 个）" % keys.size())
	# ② 后缀映射（`_tab_node_suffix` 那一行里的 "key": "Suffix" 对）
	var suffix_line := ""
	for raw: String in source.split("\n"):
		if raw.contains("func _tab_node_suffix"):
			suffix_line = raw + source.substr(source.find(raw) + raw.length(), 200)
			break
	var suffix_regex := RegEx.new()
	suffix_regex.compile("\"([a-z_]+)\"\\s*:\\s*\"([A-Za-z]+)\"")
	var suffixes := {}
	for hit: RegExMatch in suffix_regex.search_all(suffix_line):
		suffixes[hit.get_string(1)] = hit.get_string(2)
	# ③ 场景里的 Tab* 按钮
	var button_regex := RegEx.new()
	button_regex.compile("\\[node name=\"(Tab[A-Za-z]*)\" type=\"Button\"")
	var scene_buttons := {}
	for hit: RegExMatch in button_regex.search_all(scene):
		scene_buttons[hit.get_string(1)] = true
	for key: String in keys:
		var suffix := str(suffixes.get(key, ""))
		check_false(suffix.is_empty(), "TABS 的页签 %s 在 _tab_node_suffix 里没有后缀映射（按钮名拼不出来）" % key)
		if suffix.is_empty():
			continue
		check_true(
			scene_buttons.has("Tab" + suffix),
			"TABS 里的页签 %s 需要场景里有 Tab%s 按钮（没有按钮＝玩家点不到，而自检直调 select_tab 照样绿）"
				% [key, suffix]
		)
		scene_buttons.erase("Tab" + suffix)
	for left: String in scene_buttons:
		check_true(false, "场景里的 %s 按钮在 TABS 里没有对应页签（点了什么都不做）" % left)


## 32. 05 的「四家店」表要与 `building_def` 对齐（店名 + 附加服务 + 行数）。
##
## 由来（2026-10-03）：这张表此前**只覆盖了一半**——`_check_shop_shelves_match_design`
## 按建筑 id 硬编码了"这家店只准卖什么"（材料／白板饰品、非战斗回血…），但**店的集合本身没人核**：
## 设计加第五家店、改店名、或给某家店加一项服务，数据不跟上也不会有任何提示。
##
## 判定：按 05 那张表的每一行取店名 → 到 `building_def` 里按 `name_cn` 找 →
## 核「附加服务」那一格（`—` 表示没有服务，其余词要求 `service_id` 非空）；最后核行数一致。
##
## 2026-10-03 收口：`building_def` 里现在还有**不是店**的建筑（0.10.0 的 `bld_dummy` 木桩，
## `building_type=service`）——它既不进 05 那张店表，也不该算进"店的行数"，否则加一个设施
## 就会把这张表判成缺一行。两边的口径统一成「`building_type=shop` 的建筑」。
func _check_shop_table_matches_buildings() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/05_装备与掉落.md")
	check_false(doc.is_empty(), "读得到 05_装备与掉落.md")
	var db = get_db()
	var by_name := {}
	var shop_count := 0
	for row: Resource in db.rows("building_def"):
		if str(row.building_type) != "shop":
			continue
		shop_count += 1
		by_name[str(row.name_cn)] = row
	var rows := 0
	# 只认「隐藏内容式」的 3 列行：| 建筑 | 卖什么 | 附加服务 |，避免把别的表也算进来
	var start := doc.find("| 建筑 | 卖什么 | 附加服务 |")
	if start < 0:
		fail("05 里找不到「四家店」那张表（表头变了？）")
		return
	var body := doc.substr(start)
	for raw: String in body.split("\n"):
		var line := raw.strip_edges()
		if line.is_empty():
			break          # 表格结束
		if not line.begins_with("|"):
			break
		var cells := line.split("|")
		if cells.size() < 5:
			continue
		var building_name := cells[1].strip_edges()
		if building_name == "建筑" or building_name.begins_with("---"):
			continue
		var service_text := cells[3].strip_edges()
		rows += 1
		if not by_name.has(building_name):
			fail("05 的商店表里有「%s」，但 building_def 里没有同名的店（building_type=shop）" % building_name)
			continue
		var row: Resource = by_name[building_name]
		var doc_says_service := service_text != "" and service_text != "—"
		var data_has_service := not str(row.service_id).is_empty()
		check_true(
			doc_says_service == data_has_service,
			"05 说「%s」的附加服务是「%s」（%s），building_def 里 service_id='%s'（%s）" % [
				building_name, service_text, "有服务" if doc_says_service else "无服务",
				str(row.service_id), "有服务" if data_has_service else "无服务",
			]
		)
	check_eq(rows, shop_count, "05 的商店行数与 building_def 里 building_type=shop 的行数一致")


## 31. 03 的「隐藏内容」7 行表要与 `hidden_trigger` 对齐（名称与类型都对得上）。
##
## 由来（2026-10-03）：这张表列的是"第一章七种钥匙"，而**七类里有一类（顺序／三火盆）还没做**——
## 也就是说这张表天然容易和代码/数据脱节。此前只有 `validate_tables` 的**警告**在报"线索条数"
## 那条（5.20），**名称与类型没人核**：doc 把某条的名称写错、或把类型从"击杀方式"改成"行为"，谁都不会发现。
##
## 类型词是中文（道具／空间／顺序／行为／击杀方式／完整度／携带物），枚举在代码里，
## 所以这里手写一张 7 条的映射（每一对的枚举都取自 `table_validator` 的 `trigger_type` 允许值）。
## 数量也核：03 说七个层次，数据里就该有 7 行（多一个少一个都点出来）。
const HIDDEN_TYPE_WORDS := {
	"道具": "item", "空间": "space", "顺序": "sequence", "行为": "behavior",
	"击杀方式": "kill_style", "完整度": "completion", "携带物": "carry",
}


func _check_hidden_content_table_matches_data() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/03_副本_黑风寨.md")
	check_false(doc.is_empty(), "读得到 03_副本_黑风寨.md")
	var db = get_db()
	var by_name := {}
	for row: Resource in db.rows("hidden_trigger"):
		by_name[str(row.name_cn)] = row
	var rows := 0
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if not line.begins_with("|"):
			continue
		var cells := line.split("|")
		if cells.size() < 6:
			continue
		var type_word := cells[3].strip_edges()
		if not HIDDEN_TYPE_WORDS.has(type_word):
			continue     # 不是「隐藏内容」那张表（类型列不在 7 个词里）
		var name := cells[2].strip_edges()
		rows += 1
		if not by_name.has(name):
			fail("03 隐藏内容表的「%s」在 hidden_trigger 里找不到（name_cn 对不上）" % name)
			continue
		var row: Resource = by_name[name]
		check_eq(
			str(row.trigger_type), HIDDEN_TYPE_WORDS[type_word],
			"03 说「%s」的类型是%s，hidden_trigger 里 trigger_type=%s" % [name, type_word, str(row.trigger_type)]
		)
	check_eq(rows, db.rows("hidden_trigger").size(), "03 的隐藏内容行数与 hidden_trigger 行数一致")


## 30. 01 的「槽位采样点」表必须与 `growth_const` 的公式算出来的一致。
##
## 由来（2026-10-03）：那张表是**手算**的，而它就在公式下方当例子用——实测两格是错的：
## 「20 级专注悟性 30」的内功容量写 4（公式 2+floor(20/6)+floor(5/10)=5）、
## 「20 级专注根骨 25」的招式槽写 6（悟性仍是基础 10 → 2+4+floor(10/8)=7）。
## `test_growth` 早就写过一句「文档采样值待订正」，但那只钉住了"公式算出来是 5"，没有钉住表本身。
## 现在按表里的每一行（等级 + 写明的悟／根，没写就用首个模板的基础资质）重算一遍，
## 与表里那两个数逐个比——**设计改公式（growth_const）或改采样点都会立刻反映出来**。
func _check_slot_sample_table_matches_formula() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/01_角色系统.md")
	check_false(doc.is_empty(), "读得到 01_角色系统.md")
	var db = get_db()
	var tables: Array = db.rows("character_base")
	if tables.is_empty():
		check_true(false, "character_base 至少要有一个模板（采样表的默认资质取自它）")
		return
	var base_wu := int(tables[0].initial_wu)
	var base_gen := int(tables[0].initial_gen)
	var growth = GrowthCalculatorScript.new(db)
	var level_regex := RegEx.new()
	level_regex.compile("(\\d+)\\s*级")
	var wu_regex := RegEx.new()
	wu_regex.compile("悟(?:性)?\\s*(\\d+)")
	var gen_regex := RegEx.new()
	gen_regex.compile("根(?:骨)?\\s*(\\d+)")
	var rows := 0
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if not line.begins_with("|") or not line.contains("级"):
			continue
		var cells := line.split("|")
		if cells.size() < 5:
			continue
		var label := cells[1]
		var level_hit := level_regex.search(label)
		if level_hit == null:
			continue
		var digits := RegEx.new()
		digits.compile("(\\d+)")
		var slot_hit := digits.search(cells[2].replace("*", ""))
		var cap_hit := digits.search(cells[3].replace("*", ""))
		if slot_hit == null or cap_hit == null:
			continue     # 不是「场景 | 招式槽 | 内功容量」那种行
		rows += 1
		var level := int(level_hit.get_string(1))
		var wu_hit := wu_regex.search(label)
		var gen_hit := gen_regex.search(label)
		var wu := int(wu_hit.get_string(1)) if wu_hit != null else base_wu
		var gen := int(gen_hit.get_string(1)) if gen_hit != null else base_gen
		var attrs := {"wu": wu, "gen": gen}
		check_eq(
			int(slot_hit.get_string(1)), growth.active_slots(level, attrs),
			"01 采样表「%s」的招式槽与公式一致（悟 %d／根 %d）" % [label.strip_edges(), wu, gen]
		)
		check_eq(
			int(cap_hit.get_string(1)), growth.passive_capacity(level, attrs),
			"01 采样表「%s」的内功容量与公式一致（悟 %d／根 %d）" % [label.strip_edges(), wu, gen]
		)
	check_gt(float(rows), 3.0, "01 的槽位采样表至少解析出 4 行（%d 行）" % rows)


## 29. `GameState` 里**声明的每个字段**都必须进 `to_dict()`（或写进白名单并说明理由）。
##
## 由来（2026-10-03）：存档字段是"加了就得存"的东西，而**忘了加进 to_dict() 不会报任何错**——
## 那个字段每次读档都静默回到默认值（玩家表现是"我明明买了/学了，怎么没了"）。
## 升版本的纪律（`ADDED_IN` 那张表）靠人记，这条门限是机器兜底：
## 扫 `src/core/game_state.gd` 的 `var x` 声明与 `to_dict()` 里的 `"x":` 键，缺一个就红。
## 白名单是**不持久化**的三个"读档整理结果"（只报给菜单看，不回写）。
## `Inventory` 同样列出（它有自己的一份 to_dict/from_dict，同一个坑）。
##
## 2026-10-03 补两处：
##   ① **反向**：`to_dict()` 写进去的每个键，都必须在文件里被读过——"存了却回不来"是同一个
##      玩家症状的另一半（实测两个文件现在都是 0 个孤儿键）。
##   ② 取键的范围从"文件后半段"收成 **`to_dict()` 的函数体**：以前是从 `func to_dict` 一直截到
##      文件结尾再到里面找 `"键":`，于是"某个字段其实没写、但它的名字恰好在后面某个字面量里出现过"
##      就能蒙混过去。现在按花括号配平取出函数体，两个方向都只认这一段。
const SAVE_SERIALIZATION_TARGETS := [
	{
		"path": "res://src/core/game_state.gd",
		"exempt": ["migrated_from", "pruned_characters", "reclaimed_equipped"],
	},
	{"path": "res://src/core/inventory.gd", "exempt": []},
]


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


## 37. 凡是自己起引擎的 `tools/*.bat`，都必须扫引擎日志错误。
##
## 由来（2026-10-03）：`check_log_errors.bat` 是专治"运行期错误掐断函数、标记与退出码照样绿"那条的
## （决策 147／148），几个**步骤包装**都接了它；但两个**观测工具**没接——
## `run_balance_analysis.bat`／`run_perf_analysis.bat` 自己起引擎跑分析脚本，而脚本只打一张表、
## 既没有收尾标记也不判通过：**一次运行期错误就能让"半截表"看起来像完整结果**
## （决策 167／168 那条"量到错数据 → 结论写进文档"的路，就是这么走出来的）。
## 本轮把两个工具补上（收尾标记 + 扫日志），再把"起引擎就得扫日志"写成硬约定。
##
## 判定：`tools/*.bat` 里出现**行首就是 `"%GODOT_BIN%"` 的调用**（真的起引擎）才收；
## 只定义变量、只做存在性检查的不算。
func _check_engine_scripts_scan_logs() -> void:
	var invoke_re := RegEx.new()
	invoke_re.compile("(?m)^\\s*\"%GODOT_BIN%\"")
	var checked := 0
	for file_name: String in DirAccess.get_files_at("res://tools"):
		if not file_name.ends_with(".bat") or file_name == "check_log_errors.bat":
			continue
		var text := FileAccess.get_file_as_string("res://tools/" + file_name)
		if invoke_re.search(text) == null:
			continue
		checked += 1
		check_true(
			text.contains("check_log_errors.bat"),
			"%s 自己起引擎却不扫引擎日志——运行期错误会让它把半截结果当完整结果（补一条 check_log_errors.bat）"
				% file_name
		)
	check_gt(float(checked), 5.0, "扫到足够多自己起引擎的 tools/*.bat（%d 个）" % checked)


## 取出 `func to_dict()` 的**函数体**：从它后面的第一个 `{` 起，按花括号配平到归零。
##
## 为什么要精确到函数体（而不是"从 to_dict 截到文件结尾"）：那条门限是双向量键的，
## 用"后半段"当范围会让"没写进去的字段"被后面某个同名字面量蒙混过关；
## 也因为要问"写进去的键有没有人读"，而**读档代码就在同一个文件的后半段**，
## 拿"文件后半段"当范围等于把答案也算进问题里。
func _to_dict_body(source: String) -> String:
	var start := source.find("func to_dict")
	if start < 0:
		return ""
	var brace := source.find("{", start)
	if brace < 0:
		return ""
	var depth := 0
	for index in range(brace, source.length()):
		var ch := source[index]
		if ch == "{":
			depth += 1
		elif ch == "}":
			depth -= 1
			if depth == 0:
				return source.substr(brace, index - brace + 1)
	return ""


## 28. 数据字典（06）必须**提到**每张表的每个列。
##
## 由来（2026-10-03）：06 的「表清单行数」与「枚举」都有门限，但**列**一直没有——
## 而它正是策划查"这个字段叫什么、什么意思"的地方。实测确实漏了三处**功能性**列：
## `character_base` 的 `weapon_type`／`initial_wu`／`initial_gen`（0.6.0 加的两维）／`attr_total`／`start_equip_ids`、
## `equip_base` 的 `weapon_type`、`level_growth` 的 `base_def_qi`（见框架说明决策 172）。
##
## 判定**从宽**：只要求列名以 `` `列名` `` 的形式出现在该表那一节里（不查类型、不查说明文字对不对）——
## 这一节本来就有组合行（`` | `qty_min` / `qty_max` | int | … | ``）和分组行（`` | `bonus_…` … | ``）两种写法，
## 这条门限拦的是"字典里根本没提这个列"。
## 白名单只放**给人看的说明列**（不驱动逻辑、字典不逐列登记）；白名单外的新列必须登记。
const DOC_DICTIONARY_PROSE_COLUMNS := ["desc", "note", "name_cn", "icon", "display_color"]


func _check_data_dictionary_covers_columns() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/06_配置表说明.md")
	check_false(doc.is_empty(), "读得到 06_配置表说明.md")
	# 按 "### <表>.csv" 切成小节（只在本节内找列名，避免别处的同名词蒙混）
	var sections: Dictionary = {}
	var current := ""
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("### ") and line.ends_with(".csv"):
			current = line.substr(4).strip_edges().trim_suffix(".csv")
			sections[current] = ""
			continue
		if current.is_empty():
			continue
		sections[current] = str(sections[current]) + "\n" + line
	var checked := 0
	for table_name: String in TableRegistryScript.TABLES:
		if not sections.has(table_name):
			continue     # 没写数据字典的表由「表清单」那条门限管
		var body := str(sections[table_name])
		for column: String in _csv_header_columns(table_name):
			if DOC_DICTIONARY_PROSE_COLUMNS.has(column):
				continue
			checked += 1
			check_true(
				body.contains("`%s`" % column),
				"06 的数据字典里没提 %s.%s（列名改了或新加了列，就要在字典里登记）" % [table_name, column]
			)
	# 下限贴着现状（179 列）：主要防"小节没切开／表名对不上"导致这条门限**空转**。
	check_gt(float(checked), 150.0, "字典覆盖检查扫到了足够多的列（%d 列）" % checked)


func _csv_header_columns(table_name: String) -> PackedStringArray:
	var out := PackedStringArray()
	var path := "%s/%s.csv" % [TableRegistryScript.TABLES_DIR, table_name]
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return out
	var header := file.get_csv_line()
	file.close()
	for i in range(header.size()):
		var name := str(header[i]).strip_edges()
		if i == 0:
			name = name.trim_prefix("\uFEFF")     # 第一格可能带 BOM（构建期也剥它）
		if not name.is_empty():
			out.append(name)
	return out


## 27. 代码里点了名的**战斗常数**，表里必须都有。
##
## 与 `table_validator._check_growth_constants_present` 是一对：成长那边代码里有集中清单
## （`GrowthCalculator.DEFAULTS` 的键），所以放在构建期校验器里；战斗这边**没有**集中清单——
## 名字散在 `constant("<id>", 默认值)` 的调用点上，所以只能在测试里**扫源码**把实参抓出来。
## 放测试里还有一个理由：导出后的包里不一定有 `.gd` 源码，运行期不该依赖读源码。
##
## 由来（2026-10-03）：`combat_const` 缺行同样只会静默用兜底值（`DamageResolver.constant()`），
## 而 PS1 那份 5 个常数的清单是手抄的——代码新加一个常数、清单忘了加，缺行就又没人管。
func _check_code_constants_exist_in_tables() -> void:
	var source := FileAccess.get_file_as_string("res://src/core/damage_resolver.gd")
	check_false(source.is_empty(), "读得到 damage_resolver.gd（要扫它的常数调用点）")
	var regex := RegEx.new()
	regex.compile("constant\\(\"([a-z_0-9]+)\"")
	var lookup := {}
	for row: Resource in get_db().rows("combat_const"):
		lookup[str(row.const_id)] = true
	var scanned := 0
	for hit: RegExMatch in regex.search_all(source):
		scanned += 1
		var key := hit.get_string(1)
		check_true(
			lookup.has(key),
			"combat_const 缺少代码依赖的常数 '%s'（缺行会静默用兜底值）" % key
		)
	check_gt(float(scanned), 3.0, "扫到足够多的战斗常数调用点（%d 处）" % scanned)


## 14. 代码里**按字面量 id 查表**的地方（`get_row("表", "某行")`），那行必须真的在表里。
##
## 由来：`LOCKED_SCENES`（决策 88）、`enemy_base.ai_template`（决策 203）、`difficulty_config.unlock_condition`
## 都是「代码写死的 id 清单必须对得上表」的同类问题，但一处一处补永远补不完。
## 这条做成**通用门限**：扫 `src/` 里的 `get_row("表", "字面量")` 与 `require_row(...)`，逐条断言那一行存在——
## 改表时改掉了代码依赖的那一行，会在自检里当场点名（以前是运行时静默走兜底值，比如
## `sneak_detect_reduce()` 缺行就默默用 0.4）。
## 动态拼的键（`"%s|%s" % [...]`）跳过：它们由 `RELATIONS` 那套引用校验管。
func _check_code_row_literals_exist() -> void:
	var db = get_db()
	var regex := RegEx.new()
	regex.compile("(?:get_row|require_row)\\(\\s*\"([a-z_0-9]+)\"\\s*,\\s*\"([^\"]+)\"\\s*\\)")
	var files: PackedStringArray = _collect_files(["src"])
	var found := 0
	for path: String in files:
		if not path.ends_with(".gd"):
			continue
		var source := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in regex.search_all(source):
			var table_name := hit.get_string(1)
			var row_id := hit.get_string(2)
			if row_id.contains("%"):
				continue     # 动态拼接的主键（"%s|%s"），交给 RELATIONS 校验
			found += 1
			check_true(
				db.get_row(table_name, row_id) != null,
				"%s 里写死的 %s[%s] 在表里不存在（改表时改掉了代码依赖的那一行）" % [path, table_name, row_id],
			)
	check_gt(float(found), 3.0, "扫到足够多的「字面量查表」调用点（%d 处）" % found)


## 26. 「设计实现对照」里每条「部分／未实现」的行，都必须写清**卡在哪里**。
##
## 由来（2026-10-03）：这份对照表是**第二份独立维护的缺口清单**，和「待策划确认」各写各的。
## 交叉核对时发现有 **5 条缺口只活在这份表里**（可聊 NPC／消耗品使用／装备强化打造／非战斗技能成长／
## 战斗时长目标）——刚修完「Q 清单漏了大半」（决策 158），这是同一个家族第二次撞上。
## 所以写成硬约定：⛔／🟡 的行里必须**指向一个可查的落点**——允许的写法是
## `Q数字`（待策划确认）／`决策 数字`（框架说明）／`当前状态.md`／`待策划确认.md`／`07 待补`
## （地编清单）／「可选」（设计明确说不需要别人回的）。
## 第一版只认「`当前状态.md` 紧跟 #N」这一种，结果 13 行假红——真实写法是
## 「已登记 `当前状态.md` 缺口 #22」这种中间夹字的。**门限要照着现实写，不是照着理想写。**
## 图例那三行（`| 🟡 部分 |` 等）按前缀跳过，不算数据行。
func _check_blockers_are_tracked() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/dev/设计实现对照.md")
	check_false(doc.is_empty(), "读得到 设计实现对照.md")
	var trace := RegEx.new()
	# 注意 `待策划确认` 后面**必须**跟一个 Q 编号才算数——只写文件名等于没指出是哪一条。
	trace.compile("Q\\d+|决策\\s*\\d+|当前状态\\.md|待策划确认[^\\n]{0,8}Q\\d+|07 待补|缺口\\s*#\\d+|可选")
	var legend := ["| 🟡 部分 |", "| ⛔ 未实现·等设计 |", "| ⛔ 未实现·等地编 |", "| ✅ 已完成 |"]
	var checked := 0
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if not (line.contains("⛔") or line.contains("🟡")):
			continue
		var is_legend := false
		for mark: String in legend:
			if line.begins_with(mark):
				is_legend = true
		if is_legend:
			continue
		checked += 1
		check_true(
			trace.search(line) != null,
			"设计实现对照的 ⛔／🟡 行没写清卡在哪：%s（补 Q 编号／决策编号／当前状态.md／07 待补，或写「可选」）"
				% line.substr(0, 36)
		)
	check_gt(float(checked), 15.0, "数到了足够多的 ⛔／🟡 行（%d 行）" % checked)


## 36. 「只打印一个 OK 标记」的验证脚本也要有检查条数下限（删掉几条检查没人会红）。
##
## 由来（2026-10-03）：单元自检有 `MIN_ASSERTIONS` 这条棘轮（删断言当场红），
## 而**场景自检与两个验收脚本一条都没有**——偏偏它们是「版式预算（`LAYOUT`／`CONTENT`）、
## 真实节点接线、装配真的进战斗、跨场景交接、地图位点可达」这类**只有把真场景跑起来才量得到**
## 的东西的唯一覆盖。把某行 `ok = ok and …`（或某个 `_problems.append(…)`）删掉，
## `SELF-TEST: OK`／`LOOPCHECK: OK`／`MAPCHECK: OK` 照样打印，验收照样 16/16。
##
## 记的是**当前条数**（棘轮，不是目标）：删一条就红，加了新检查就往上调——
## 和 `MIN_ASSERTIONS` 一样，它证明不了"检查有效"，只保证**没人能悄悄把检查删掉**。
## 运行期错误导致的假绿不归它管：那条由 `tools\check_log_errors.bat` 扫 `SCRIPT ERROR` 兜着。
## 每条都写明"这个 pattern 代表什么"；`SELF-TEST: %s` 那句会被扫到，
## **新加一个带自检的场景必须在表里登记一行**（数量对账会拦）。
const VERIFICATION_CHECK_FLOORS := {
	"src/ui/battle_screen.gd": {"pattern": "ok = ok and", "floor": 17, "why": "场景自检的聚合断言"},
	"src/ui/shop_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/waypoint_screen.gd": {"pattern": "ok = ok and", "floor": 10, "why": "场景自检的聚合断言"},
	"src/ui/dungeon_screen.gd": {"pattern": "ok = ok and", "floor": 10, "why": "场景自检的聚合断言"},
	"src/ui/character_screen.gd": {"pattern": "ok = ok and", "floor": 9, "why": "场景自检的聚合断言"},
	"src/ui/cultivate_screen.gd": {"pattern": "ok = ok and", "floor": 9, "why": "场景自检的聚合断言"},
	"src/ui/clue_screen.gd": {"pattern": "ok = ok and", "floor": 7, "why": "场景自检的聚合断言"},
	"src/ui/main_menu.gd": {"pattern": "ok = ok and", "floor": 6, "why": "场景自检的聚合断言"},
	"src/world/overworld_controller.gd": {"pattern": "ok = ok and", "floor": 5, "why": "场景自检的聚合断言"},
	"src/world/local_map_controller.gd": {"pattern": "ok = ok and", "floor": 3, "why": "场景自检的聚合断言"},
	"src/bootstrap.gd": {"pattern": "ok = ok and", "floor": 5, "why": "配置表诊断场景自检的聚合断言"},
	"tools/check_loop.gd": {"pattern": "ok = ok and", "floor": 12, "why": "跨场景闭环每段一个聚合断言（③ 现在有胜／败两次）"},
	"tools/mapgen/verify_maps.gd": {"pattern": "_problems.append(", "floor": 27, "why": "每个地图检查点都必须能报错"},
}


func _check_verification_check_floors() -> void:
	var selftest_scenes := PackedStringArray()
	for path: String in _collect_files(["src"]):
		if not path.ends_with(".gd"):
			continue
		if FileAccess.get_file_as_string("res://" + path).contains("SELF-TEST: %s"):
			selftest_scenes.append(path)
	check_eq(
		selftest_scenes.size(), 11,
		"带场景自检的文件数没变（新加一个就要在棘轮表里登记一行）：%s" % ", ".join(selftest_scenes)
	)
	for path: String in selftest_scenes:
		check_true(
			VERIFICATION_CHECK_FLOORS.has(path),
			"%s 有场景自检，但棘轮表里没有它的检查条数下限" % path
		)
	for path: String in VERIFICATION_CHECK_FLOORS.keys():
		var spec: Dictionary = VERIFICATION_CHECK_FLOORS[path]
		var text := FileAccess.get_file_as_string("res://" + str(path))
		check_false(text.is_empty(), "读得到 %s" % path)
		# 用 `String.count()` 数**字面量**，不走正则：pattern 里有 `(` 这类字符时，
		# 正则写错（少个反斜杠）会静默数出 0 条，反而把门限变成假红/假绿（第一次就踩了）。
		var count := text.count(str(spec["pattern"]))
		var floor_count := int(spec["floor"])
		check_true(
			count >= floor_count,
			"%s 的检查只剩 %d 条（下限 %d，数的是「%s」= %s）——少的哪条？删检查要连着改棘轮表并写明理由"
				% [path, count, floor_count, str(spec["pattern"]), str(spec["why"])]
		)
	# 文档里写的"N 个真实场景自检"也得对得上——同 183 的 Q 范围、185 的看板数字一个家族：
	# 场景自检加一个，那两句就变成假话，而它们写在**交接文档**和**一页看板**上，最容易被当真。
	var count_re := RegEx.new()
	count_re.compile("(\\d+)\\s*个真实场景自检")
	var docs_checked := 0
	for path: String in ["AGENTS.md", "docs/dev/当前状态.md"]:
		var text := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in count_re.search_all(text):
			docs_checked += 1
			check_eq(
				int(hit.get_string(1)), selftest_scenes.size(),
				"%s 里的「%s」与实际自检场景数（%d 个）不符——加了自检场景就要同步这句"
					% [path, hit.get_string(0), selftest_scenes.size()]
			)
	check_gt(float(docs_checked), 0.0, "至少在一份文档里写明了真实场景自检的个数（扫到 %d 处）" % docs_checked)


## 35. 「当前状态」一页看板上写死的表数／行数，必须与真表一致。
##
## 由来（2026-10-03）：那一行写着「37 张表 / 566 行」——**同一行里还写着"条数以运行输出为准，
## 别在这里写死"，而它自己就是写死的**。表一加、行一改，这句立刻变成假话，而且没人会红。
## 183 治的是 Q 编号、182 治的是代码里的 id 清单，这是同一家族的第三处：**看板上的动态数字**。
##
## 数字从哪来：`TableDb` 载 `data/generated`（验收前刚由构建期重建过），
## 与 build_tables 打印的「N 张表 / M 行」**同源同算法**，不手抄。
func _check_dashboard_numbers() -> void:
	var status := FileAccess.get_file_as_string(STATUS_PATH)
	check_false(status.is_empty(), "读得到 当前状态.md")
	var db := TableDbScript.new()
	db.load_all()
	check_eq(db.errors.size(), 0, "TableDb 能载全（%s）" % "; ".join(db.errors))
	var table_count := db.tables.size()
	check_gt(float(table_count), 20.0, "载到足够多的表（%d 张）" % table_count)
	var row_count := 0
	for table_name: String in db.tables.keys():
		var rows: Array = db.tables[table_name].get("rows")
		row_count += rows.size()
	var claim_re := RegEx.new()
	claim_re.compile("(\\d+)\\s*张表\\s*/\\s*(\\d+)\\s*行")
	var hits := claim_re.search_all(status)
	check_gt(float(hits.size()), 0.0, "当前状态.md 里写明了「N 张表 / M 行」（扫到 %d 处）" % hits.size())
	for hit: RegExMatch in hits:
		check_eq(
			int(hit.get_string(1)), table_count,
			"看板上写的表数 %s 与实载 %d 张不符" % [hit.get_string(1), table_count]
		)
		check_eq(
			int(hit.get_string(2)), row_count,
			"看板上写的行数 %s 与实载 %d 行不符（加／删表行后要同步这句）" % [hit.get_string(2), row_count]
		)


## 34. 「待策划确认」的 Q 编号必须自洽，而且两份「现状文档」里写的范围要对得上。
##
## 由来（2026-10-03）：Q 清单从 16 条长到 36 条，而 `当前状态.md` 开头那句「（**Q1–Q31**）」
## 一直没人改——**清单越补越长，那句范围就越是假话**，而且没有任何检查会红。
## 这跟 `LOCKED_SCENES`（决策 182）是同一个家族：**文档里写死的编号/清单，必须对得上真实来源**。
##
## 三条一起钉：
##   ① 编号不许重（两条问题共用一个编号 = 引用它必然指错一边）；
##   ② 1..最大编号里缺的那些，必须与「跳号登记」那行**一一对应**——`Q28` 是开发侧结案移除、
##      编号空着不挪；以后谁把一条问题**悄悄删掉**，这里当场红（一份看起来完整的待办清单
##      最容易烂在这儿）；
##   ③ 两份现状文档里写的 `Q<起>–<止>`，止必须等于真实最大编号。
##      `框架说明.md` **不在其中**：那里是带日期的叙事（"那天补到哪"），拿现状口径去量它会假红。
func _check_question_numbering() -> void:
	var questions := FileAccess.get_file_as_string(QUESTIONS_PATH)
	check_false(questions.is_empty(), "读得到 待策划确认.md")
	if questions.is_empty():
		return

	var row_re := RegEx.new()
	row_re.compile("(?m)^\\|\\s*Q(\\d+)\\s*\\|")
	var seen: Dictionary = {}
	var dupes := PackedStringArray()
	var max_q := 0
	var count := 0
	for hit: RegExMatch in row_re.search_all(questions):
		var number := int(hit.get_string(1))
		count += 1
		max_q = maxi(max_q, number)
		if seen.has(number):
			dupes.append("Q%d" % number)
		seen[number] = true
	check_gt(float(max_q), 20.0, "Q 清单数到了足够多的编号（%d 条，最大 Q%d）" % [count, max_q])
	check_eq(dupes.size(), 0, "同一编号被两条问题共用（引用它会指错）：%s" % ", ".join(dupes))

	# ② 跳号必须登记：登记行写成「**跳号登记**（…）：Q28。」（机器只认这行里的 Q 编号）
	var declared: Dictionary = {}
	var marker_re := RegEx.new()
	marker_re.compile("跳号登记[^\\n]*")
	var number_re := RegEx.new()
	number_re.compile("Q(\\d+)")
	for raw: String in questions.split("\n"):
		var marker := marker_re.search(raw)
		if marker == null:
			continue
		for hit: RegExMatch in number_re.search_all(marker.get_string(0)):
			declared[int(hit.get_string(1))] = true
	var missing := PackedStringArray()
	for number in range(1, max_q + 1):
		if not seen.has(number) and not declared.has(number):
			missing.append("Q%d" % number)
	check_eq(
		missing.size(), 0,
		"这些编号既没有对应的问题、也不在「跳号登记」里（被悄悄删了？）：%s" % ", ".join(missing)
	)
	for number: int in declared.keys():
		check_true(not seen.has(number), "跳号登记里的 Q%d 又回来了——把那条登记删掉" % number)

	# ③ 现状文档里的范围必须止于真实最大编号
	var range_re := RegEx.new()
	range_re.compile("Q(\\d+)\\s*[–—-]\\s*Q?(\\d+)")
	var ranged := 0
	for path: String in [STATUS_PATH, QUESTIONS_PATH]:
		var text := FileAccess.get_file_as_string(path)
		for hit: RegExMatch in range_re.search_all(text):
			ranged += 1
			check_eq(
				int(hit.get_string(2)), max_q,
				"%s 里写的 Q 范围「%s」止于 Q%s，而 Q 清单最大是 Q%d——清单长了，这句没跟上"
					% [path.get_file(), hit.get_string(0), hit.get_string(2), max_q]
			)
	check_gt(float(ranged), 0.0, "至少在一份现状文档里写明了当前 Q 范围（扫到 %d 处）" % ranged)


## 25. 「待策划确认」那份 Q 清单必须真的覆盖「当前状态」里的每一条缺口。
##
## 由来（2026-10-03）：Q 清单是第一版手工压缩出来的，交叉核对时发现**漏了 15 条**——
## 包括最严重的那条（`character_base` 只有 1 行 → 第一章红色精英线全档 0 胜）。
## **一份漏了大半的待办清单比没有清单更糟**：策划照着它逐条回完，以为清空了，其实还差一半，
## 而这类"清单看起来完整"的问题，光靠人读是发现不了的。所以把覆盖做成门限：
## `当前状态.md` 3.1／3.2 里每一行的编号，都必须在 Q 清单里被引用到
## （3.1 引用成 `#N`，3.2 引用成 `3.2 #N`——两节的编号会重合，所以分开认）。
## 3.3（地编/美术）与 3.4（表侧一致）不是表格行，不在这条里。
## 新加一条缺口时同时补一条 Q，是有意识的行为，不是门限找麻烦。
func _check_designer_questions_cover_gaps() -> void:
	var status := FileAccess.get_file_as_string("res://docs/dev/当前状态.md")
	var questions := FileAccess.get_file_as_string("res://docs/dev/待策划确认.md")
	check_false(status.is_empty(), "读得到 当前状态.md")
	check_false(questions.is_empty(), "读得到 待策划确认.md")
	var row_regex := RegEx.new()
	row_regex.compile("^\\|\\s*(\\d+)\\s*\\|\\s*\\*\\*")
	var section := ""
	var data_rows := 0
	var rule_rows := 0
	for raw: String in status.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("### 3.1"):
			section = "3.1"
			continue
		if line.begins_with("### 3.2"):
			section = "3.2"
			continue
		if line.begins_with("### "):
			section = ""
			continue
		if section.is_empty():
			continue
		var hit := row_regex.search(line)
		if hit == null:
			continue
		var number := int(hit.get_string(1))
		# `#1` 不能拿 `contains` 判——它会被 `#17` 蒙混过去，所以用带边界的正则。
		var needle := ("3\\.2\\s*#%d(?!\\d)" % number) if section == "3.2" else ("(^|[^\\d.])#%d(?!\\d)" % number)
		var regex := RegEx.new()
		regex.compile(needle)
		if section == "3.1":
			data_rows += 1
		else:
			rule_rows += 1
		check_not_null(
			regex.search(questions),
			"当前状态 %s #%d 没有进「待策划确认」——补一条 Q，或把那条缺口从表里移走（编号引用见该文件）"
				% [section, number]
		)
	check_gt(float(data_rows), 15.0, "3.1 数到了足够多的缺口（%d 条）" % data_rows)
	check_gt(float(rule_rows), 3.0, "3.2 数到了足够多的缺口（%d 条）" % rule_rows)


## 8. `src/` 下的脚本与 `scenes/` 下的场景都必须有人引用（和「孤儿用例」「孤儿设计文档」同一类）。
##
## 判定「被引用」的三种方式：路径字符串出现在别处（`preload`／`load`／场景的 ext_resource／
## autoload 配置）、或者它的 `class_name` 在别的文件里被用到。
## 现在 91 个脚本 + `scenes/` 下 18 个场景全部有引用（外加根目录那 1 个白名单空场景，见 8b）；
## 以后谁写了脚本忘了接上，这里会当场红。
## 真要留一个「暂时没人用」的东西，把它加进 INFRASTRUCTURE_SCRIPTS 并写明理由。
const INFRASTRUCTURE_SCRIPTS := []


func _check_no_orphan_code() -> void:
	var corpus := ""
	var files: PackedStringArray = _collect_files(["src", "scenes", "tests", "docs", "tools"])
	files.append("project.godot")
	for path: String in files:
		var text := FileAccess.get_file_as_string("res://" + path)
		if not text.is_empty():
			corpus += text

	for path: String in _collect_files(["src"]):
		if not path.ends_with(".gd") or INFRASTRUCTURE_SCRIPTS.has(path):
			continue
		check_true(
			_has_reference(corpus, path),
			"孤儿脚本：%s 没有被任何地方引用（写完忘了接上？还是该删了？）" % path,
		)
	for path: String in _collect_files(["scenes"]):
		if not path.ends_with(".tscn"):
			continue
		check_true(
			_has_reference(corpus, path),
			"孤儿场景：%s 没有被任何地方引用（写完忘了接上？还是该删了？）" % path,
		)
	_check_no_orphan_root_scenes(corpus)


## 8b. 项目**根目录**下的 `.tscn` 也要有主。
##
## 这条是**用 MCP 列场景时撞出来的**：MCP 的 `list_scenes` 报 19 个场景，而 `scenes/` 下只有 18 个——
## 多出来的是根目录的 `node_2d.tscn`（一个空 `Node2D`，103 字节，谁都不引用）。
## 原因是上面的孤儿扫描只收 `scenes/`：**只要有人把场景存到项目根，它就能永远躲过检查**。
## 而用 MCP 的 `add_node` + `save_scene` 联调时，默认落点恰好就是 `res://node_2d.tscn`。
##
## 继承来的那个空场景不是本轮写的，删它要动 Git 里已有的文件，所以先登记在白名单里等确认；
## 白名单自身也会烂（文件被删了名单还留着），所以顺手断言它仍然存在。
const ROOT_SCENE_WHITELIST := ["node_2d.tscn"]


func _check_no_orphan_root_scenes(corpus: String) -> void:
	for file_name: String in DirAccess.get_files_at("res://"):
		if not file_name.ends_with(".tscn") or ROOT_SCENE_WHITELIST.has(file_name):
			continue
		check_true(
			_has_reference(corpus, file_name),
			"根目录下的场景 %s 没有被任何地方引用（编辑器草稿？还是该删了？）" % file_name,
		)
	for file_name: String in ROOT_SCENE_WHITELIST:
		check_true(
			FileAccess.file_exists("res://" + file_name),
			"根场景白名单里的 %s 已经不存在了，把这条白名单删掉" % file_name,
		)
		# 反方向（2026-10-03 补）：白名单是"暂时没人引用的编辑器草稿"——
		# 一旦它被引用了，这条豁免就该退场，否则"以后再变成孤儿"也没人发现。
		# **判定要用纯代码语料**：上面那份 corpus 故意把 docs/tools 也算作"引用"（宽松），
		# 而 `node_2d.tscn` 恰恰在框架说明与 README_MCP 里被反复提到——拿宽语料判会当场假红。
		# 这里现算一份只含 src／scenes／project.godot 的语料（第一版踩过，见框架说明决策 178）。
		var code_corpus := FileAccess.get_file_as_string("res://project.godot")
		for path: String in _collect_files(["src", "scenes"]):
			code_corpus += FileAccess.get_file_as_string("res://" + path)
		check_false(
			_has_reference(code_corpus, file_name),
			"根场景白名单里的 %s 已经被引用了，把这条白名单删掉" % file_name,
		)


## 8c. `.uid` 文件不许是孤儿。
##
## Godot 4.4+ 给每个脚本生成 `<名字>.gd.uid`，而**删掉或改名源文件时它不会跟着消失**：
## 上一轮那对孤儿探针（`src/core/__orphan_probe.gd` ＋ `scenes/__orphan_probe.tscn`）删干净了，
## 但它的 `.uid` 留了下来——142 个 `.uid` 里就这一个对不上，而且**没有任何检查会红**。
## 残骸会骗后来的人「这个脚本还在」，所以钉住：每个 `.uid` 都必须有同名源文件。
## 注意 `_collect_files()` 按扩展名过滤、收不到 `.uid`，这里自己递归（跳过 `.godot/` 与隐藏目录）。
func _check_no_dangling_uids() -> void:
	var uids := PackedStringArray()
	_collect_files_with_suffix("", ".uid", uids)
	check_gt(float(uids.size()), 100.0, "扫到足够多的 .uid 文件（%d 个）" % uids.size())
	for path: String in uids:
		var target := path.substr(0, path.length() - 4)
		check_true(
			FileAccess.file_exists("res://" + target),
			"%s 没有对应的源文件（改名或删文件时留下的残骸？）" % path,
		)


## 8d. `.import` 残骸：导入元数据还在，源文件没了。
##
## 由来（2026-10-03）：`data/tables/__probe_unregistered.csv.import` 是**我自己**留下的——
## 反向验证时建了个临时 CSV、跑了一遍验收（Godot 顺手写了 `<csv>.import`），删 CSV 时把这一半忘了。
## Godot 会给每个被导入的文件写 `<源文件>.import`，而**源文件删掉它不会跟着消失**
## （和上面 `.uid` 残骸同一个坑：那条有门限、这条没有）。残骸会骗后来的人"这个文件还在"，
## 所以钉住：每个 `.import` 都必须找得到源文件。
func _check_no_orphan_import_files() -> void:
	var metas := PackedStringArray()
	_collect_files_with_suffix("", ".import", metas)
	check_gt(float(metas.size()), 20.0, "扫到足够多的 .import 文件（%d 个）" % metas.size())
	for path: String in metas:
		var target := path.substr(0, path.length() - 7)
		check_true(
			FileAccess.file_exists("res://" + target),
			"%s 没有对应的源文件（删文件／改名时留下的残骸——把它删掉，或把源文件找回来）" % path
		)


## 42. `data/tables/` 只放设计师要看的表：不许再长出 Godot 的导入产物。
##
## 由来（2026-10-03）：`*.csv` 的默认导入器是「CSV 翻译表」，编辑器给每张表按列生成
## `*.translation`、按表生成 `*.import`（实测 254 ＋ 37 个），每次扫描还刷 10 条 `UID duplicate`。
## 治法是在表目录放 `.gdignore` 让编辑器**不扫描**它（构建期用 `FileAccess` 读原始 CSV，
## 不受影响——同一轮验收里"37 张表 / 566 行"照常构建即为证）。
## 这条门限把"治好了"钉住：`.gdignore` 在、且目录里没有 `*.translation`/`*.import`。
## 谁要是把它删了（或用别的方式让导入回来），这里当场红——附回滚/复核口径见决策 196。
func _check_tables_dir_is_not_imported() -> void:
	var dir := "res://data/tables"
	check_true(
		FileAccess.file_exists(dir + "/.gdignore"),
		"data/tables/.gdignore 不见了：编辑器会重新把 37 张 CSV 当翻译表导入（254 个 *.translation ＋ 37 个 *.import 会回来）"
	)
	var leftovers := PackedStringArray()
	for file_name: String in DirAccess.get_files_at(dir):
		if file_name.ends_with(".translation") or file_name.ends_with(".import"):
			leftovers.append(file_name)
	# 名单可能长到几百个（真回归时就是那样），只举前 5 个 + 总数，别把失败信息刷爆。
	var sample := PackedStringArray()
	for i in range(mini(5, leftovers.size())):
		sample.append(leftovers[i])
	check_eq(
		leftovers.size(), 0,
		"data/tables 里又有 Godot 的导入产物了（%d 个，例如 %s）：表目录只该有 CSV（见决策 196）"
			% [leftovers.size(), ", ".join(sample)]
	)


## 41. 版本库契约：`.gitignore` 的忽略面 + `.gitattributes` 的批处理行尾。
##
## 由来（2026-10-03）：这个仓库的入库流程是根目录的 `sync_to_git.bat`——`git add .` 全量入库再 push
## （HEAD 目前只跟踪 16 个骨架文件，`src/ tests/ tools/ docs/ data/ scenes/ assets/` 都还没入册）。
## `git add .` 是"什么都收"的，于是两件事没人盯就会出事：
##
##   ① `.gitignore` 一旦少了 `.godot/`／`data/generated/`／`.logs/`，生成物和日志会被扫进仓库；
##      `data/tables/` 下那 291 个 Godot 导入产物（每列一个 `*.translation`、每表一个 `*.import`）
##      同理——它们是导入产物，不是设计资产（见决策 191）。
##   ② `.gitattributes` 原本只有 `* text=auto eol=lf`，而仓库自己的约定是"`tools/*.bat` 必须 CRLF"
##      （`_audit_bat` 按字节验）。两者冲突时**新克隆拿到的是 LF 的 .bat，克隆完验收直接红**——
##      本地看不出来，因为工作区的文件是 CRLF、只是没被重新 checkout 过。
##
## 这里两条都钉住（`git check-attr` 那种"真要问 Git"的验证写在注释与文档里：自检跑在引擎里，没有 git）。
func _check_git_repo_contract() -> void:
	var ignore_text := FileAccess.get_file_as_string("res://.gitignore")
	check_false(ignore_text.is_empty(), "读得到 .gitignore")
	var ignored: Dictionary = {}
	for raw: String in ignore_text.split("\n"):
		ignored[raw.strip_edges()] = true
	var required := [
		".godot/", "data/generated/", ".logs/",
		"data/tables/*.translation", "data/tables/*.import",
	]
	for pattern: String in required:
		check_true(
			ignored.has(pattern),
			".gitignore 少了 `%s`：入库脚本用 `git add .` 全量收，这一条丢了它就会被提交" % pattern
		)

	var attrs := FileAccess.get_file_as_string("res://.gitattributes")
	check_false(attrs.is_empty(), "读得到 .gitattributes")
	# gitattributes 是"后面的规则覆盖前面的"，所以只认**最后一条**命中 *.bat 的规则。
	var bat_rule := "(没有匹配 *.bat 的规则)"
	for raw: String in attrs.split("\n"):
		var line := raw.strip_edges()
		if line.is_empty() or line.begins_with("#"):
			continue
		var cells := line.split(" ", false)
		if cells.size() < 2:
			continue
		var pattern := cells[0]
		if pattern == "*.bat" or pattern.ends_with("/*.bat"):
			bat_rule = line
	check_true(
		bat_rule.contains("eol=crlf"),
		".gitattributes 的 `*.bat` 行尾不是 CRLF（最后一条规则：'%s'）：`eol=lf` 会让新克隆拿到 LF 的 .bat，而仓库约定要求 CRLF——克隆完验收就红" % bat_rule
	)


## 43. 用例里不许出现「没给种子的随机源」。
##
## `_check_battle_tests_are_seeded` 只管"造战斗界面"那 12 行窗口；别处（掉落、事件判定、词条、
## 明雷刷新……）要是写了 `RngServiceScript.new()`（无参 = 随机种子）或 `RandomNumberGenerator.new()`，
## 断言就会**时红时绿**——红起来查不出原因，绿起来也不算数（这类"噪声当门限"的坑，
## 项目在别处已经写过：`PERF` 为什么不做门限）。
## 检查口径：`tests/` 下这三个无参构造**一个都不许有**；要用固定种子（`RngServiceScript.new(SEED)`）。
## 真需要引擎自带 RNG 的极少数情况，就在下一行显式 `seed = …`，并把理由写进这里的白名单。
const UNSAFE_RNG_PATTERNS := ["RngServiceScript.new()", "RngService.new()", "RandomNumberGenerator.new()"]


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


## 44. 策划文档里带目录前缀的路径，必须指向**现在真的存在**的东西。
##
## 由来（2026-10-03）：`docs/dev` 那侧早有一条"开发文档里引到的工具必须存在"（182／183 反复提过），
## 但**只认 `docs/dev` 和 `tools/`**。而策划文档里也在写"去哪找"：
## `scenes/maps/overworld.tscn`、`tests/test_map_assets.gd`、`tools/mapgen/verify_maps.gd`、
## `data/tables/character_base.csv`……这些指针**没有任何门限**——文件一改名/一挪，设计侧照着找不到，
## 而验收照样全绿。今天实测这 10 处全对，所以把"全对"钉住。
##
## **为什么只收 `docs/design`**：`docs/dev/框架说明.md` 是**带日期的叙事**，里面有意引用一堆
## "不存在的路径"当反面例子（`drop_resolver_TYPO.gd`、`__probe_unregistered.csv`、
## `src/core/__orphan_probe.gd`……实测 10 处，全是故意的）——拿现状口径去量它会假红。
## 策划文档要写"将来才建的东西"时，**别带目录前缀**（写 `dialogue_tree.csv` 而不是
## `data/tables/dialogue_tree.csv`）：这条守的是"带前缀的指向现在存在的东西"。
func _check_design_docs_paths_exist() -> void:
	var path_re := RegEx.new()
	path_re.compile("(?:src|scenes|tools|data|assets|tests)/[A-Za-z0-9_\\-/\\.]+\\.(?:gd|tscn|tres|csv|bat|ps1|py|png|json)")
	var checked := 0
	for path: String in _collect_files(["docs/design"]):
		if not path.ends_with(".md"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in path_re.search_all(text):
			checked += 1
			var target := hit.get_string(0)
			check_true(
				FileAccess.file_exists("res://" + target),
				"%s 里写的 %s 不存在——带目录前缀的应当指向**现在**存在的东西（要写「将来才建」的，别带目录前缀）"
					% [path.get_file(), target]
			)
	check_gt(float(checked), 5.0, "策划文档里扫到足够多的路径引用（%d 处）" % checked)


## 45. 「地编／美术」那两份清单必须对得上：A 清单的行数 = `当前状态` 3.3 的行数，
## 抬头写明的"N 条（A1–Am）"也要与真实条数、最大编号一致。
##
## 由来（2026-10-03）：`当前状态.md` 3.3 节有 **5** 条给地编／美术的缺口，而 `待策划确认.md` 的
## A 清单只有 **4** 条、两份文档的抬头还都写着"4 条（A1–A4）"——第 5 条（
## `kenney_tiny_town` 那套 tileset 留不留）**只活在详细表里**，从没进过"一句话就能回"的清单。
## 这与 Q 清单漏项（决策 158／159）、Q 范围写旧（183）是同一个家族：**清单漏一条、编号写旧，
## 光靠人读发现不了**。所以把两边对上：条数、编号、抬头三处一起核。
func _check_asset_questions_match_status() -> void:
	var questions := FileAccess.get_file_as_string(QUESTIONS_PATH)
	var status := FileAccess.get_file_as_string(STATUS_PATH)
	check_false(questions.is_empty() and status.is_empty(), "读得到两份现状文档")

	# ① A 清单：行数与最大编号
	var a_re := RegEx.new()
	a_re.compile("(?m)^\\|\\s*A(\\d+)\\s*\\|")
	var a_numbers: Array[int] = []
	for hit: RegExMatch in a_re.search_all(questions):
		a_numbers.append(int(hit.get_string(1)))
	check_gt(float(a_numbers.size()), 2.0, "A 清单扫到足够多的条目（%d 条）" % a_numbers.size())
	var a_max := 0
	for number: int in a_numbers:
		a_max = maxi(a_max, number)

	# ② `当前状态` 3.3 节的行数（从 `### 3.3` 数到下一个标题）
	var section_rows := 0
	var in_section := false
	var row_re := RegEx.new()
	row_re.compile("^\\|\\s*(\\d+)\\s*\\|")
	for raw: String in status.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("### 3.3"):
			in_section = true
			continue
		if in_section and (line.begins_with("### ") or line.begins_with("## ")):
			in_section = false
			continue
		if in_section and row_re.search(line) != null:
			section_rows += 1
	check_eq(
		section_rows, a_numbers.size(),
		"「当前状态」3.3 有 %d 条给地编／美术的缺口，而「待策划确认」的 A 清单只有 %d 条——漏的那条要补进去"
			% [section_rows, a_numbers.size()]
	)

	# ③ 抬头写的"N 条（A1–Am）"：条数与最大编号都要对
	var claim_re := RegEx.new()
	claim_re.compile("地编/美术\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*条（A1[–-]A?(\\d+)）")
	var claims := 0
	for path: String in [QUESTIONS_PATH, STATUS_PATH]:
		var text := FileAccess.get_file_as_string(path)
		for hit: RegExMatch in claim_re.search_all(text):
			claims += 1
			check_eq(
				int(hit.get_string(1)), a_numbers.size(),
				"%s 抬头写「%s」，而 A 清单实有 %d 条" % [path.get_file(), hit.get_string(0), a_numbers.size()]
			)
			check_eq(
				int(hit.get_string(2)), a_max,
				"%s 抬头写「%s」，而 A 清单最大编号是 A%d" % [path.get_file(), hit.get_string(0), a_max]
			)
	check_gt(float(claims), 0.0, "至少在一份文档里写明了 A 清单的条数（扫到 %d 处）" % claims)


## 46. `设计实现对照.md` 里 ✅ 行点名的代码符号必须**在代码里真的找得到**。
##
## 由来（2026-10-03）：那张表是给设计侧看的"✅ = 代码里有实现、且有断言钉着"，54 行 ✅ 里点了
## 94 个反引号符号（`DamageResolver`／`flee_block_reason`／`roaming_spawn.behavior`……）。
## 符号一旦被改名或删掉，这一行就变成**没人能核**的假话——而验收不会红（文档不在任何门限里）。
## 这跟"开发文档里引到的工具必须存在"（182／183）、"策划文档里的路径必须存在"（201）是一家。
##
## 判定口径（照实测量来的，不是照理想写的）：
##   ① 语料**只收代码**（`src/scenes/tests/tools` + `project.godot`）——把文档也收进来等于自证；
##   ② 反引号里带点的（`DungeonService.progress`、`skill_base.weapon_type`）按 `.` 拆开**逐段**找：
##      实测这样 94 个全过，而"整串找"会有 5 个假红（文档习惯写 `类.成员`，代码里是
##      `DungeonServiceScript.progress` 这种别名）——**门限要照着现实写**；
##   ③ **只看"状态列整个是 ✅"的行**：第一版按"这一行里出现 ✅ 就算"筛，结果抓到 2 个假红——
##      有一行是**半 ✅ 半 ⛔**（正门／后山密道 ✅，伪装混入 ⛔），而 ⛔ 那半句点的正是
##      "只在 data 里、src 一行都不认"的两件钥匙道具。**门限第一次跑就纠正了我的过滤条件。**
func _check_completion_rows_symbols_exist() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/dev/设计实现对照.md")
	check_false(doc.is_empty(), "读得到 设计实现对照.md")
	var corpus := FileAccess.get_file_as_string("res://project.godot")
	for path: String in _collect_files(["src", "scenes", "tests", "tools"]):
		corpus += FileAccess.get_file_as_string("res://" + path)
	check_gt(float(corpus.length()), 100000.0, "代码语料收得够多（%d 字符）" % corpus.length())

	var token_re := RegEx.new()
	token_re.compile("`([A-Za-z_][A-Za-z0-9_\\.]*)`")
	var missing := PackedStringArray()
	var scanned := 0
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if not line.begins_with("|") or not line.contains("✅") or line.contains("✅ 已完成"):
			continue
		# 状态列是这一行的**最后一格**：只有它整个以 ✅ 开头，才算"这行声称已完成"。
		var cells := line.split("|")
		var status := ""
		for index in range(cells.size() - 1, -1, -1):
			if not cells[index].strip_edges().is_empty():
				status = cells[index].strip_edges()
				break
		if not status.begins_with("✅"):
			continue
		for hit: RegExMatch in token_re.search_all(line):
			var token := hit.get_string(1)
			scanned += 1
			for part: String in token.split("."):
				if not corpus.contains(part):
					missing.append(token)
					break
	check_gt(float(scanned), 50.0, "✅ 行里扫到足够多的代码符号（%d 个）" % scanned)
	var sample := PackedStringArray()
	for index in range(mini(6, missing.size())):
		sample.append(missing[index])
	check_eq(
		missing.size(), 0,
		"设计实现对照的 ✅ 行点了代码里找不到的符号（%d 个，例如 %s）：那行要么改符号名，要么如实降级"
			% [missing.size(), ", ".join(sample)]
	)


## 47. `框架说明.md` 的目录必须与实际的 `##` 小节**一一对应（含顺序）**。
##
## 由来（2026-10-03）：这个文件已经 3600+ 行、13 个小节、200+ 条编号决策，**却没有目录**——
## 接手的人（包括我这个 agent）每次都靠 grep 找。补了目录之后，缺的是"别让它烂"：
## 新增/改名一个小节而忘了改目录，读的人就会照着目录找不到那一节，而**没有任何东西会红**。
## 判定口径：目录节（`## 目录` 到下一个 `##` 之间）里的 `N. 标题` 行，必须与全文 `## 标题`
## （去掉目录自己）**逐条逐序相等**；数量不同、标题不同、顺序不同都点名。
func _check_framework_doc_toc() -> void:
	const PATH := "res://docs/dev/框架说明.md"
	var doc := FileAccess.get_file_as_string(PATH)
	check_false(doc.is_empty(), "读得到 框架说明.md")

	var headings := PackedStringArray()
	var toc: Array[String] = []
	var toc_re := RegEx.new()
	toc_re.compile("^(\\d+)\\.\\s+(.+)$")
	var in_toc := false
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("## "):
			var title := line.substr(3).strip_edges()
			if title == "目录":
				in_toc = true
				continue
			in_toc = false
			headings.append(title)
			continue
		if not in_toc:
			continue
		var hit := toc_re.search(line)
		if hit != null:
			var entry := hit.get_string(2).strip_edges()
			# 目录里允许写"← 备注"这类尾巴，比对时只取标题本体
			var arrow := entry.find("←")
			if arrow >= 0:
				entry = entry.substr(0, arrow).strip_edges()
			toc.append(entry)
	check_gt(float(headings.size()), 8.0, "扫到足够多的小节（%d 个）" % headings.size())
	check_gt(float(toc.size()), 8.0, "目录里扫到足够多条目（%d 条）" % toc.size())

	var problems := PackedStringArray()
	if toc.size() != headings.size():
		problems.append("目录有 %d 条、实际有 %d 个小节" % [toc.size(), headings.size()])
	for index in range(mini(toc.size(), headings.size())):
		if toc[index] != headings[index]:
			problems.append("第 %d 条：目录写「%s」，实际是「%s」" % [index + 1, toc[index], headings[index]])
	for index in range(mini(toc.size(), headings.size()), headings.size()):
		problems.append("缺登记：%s" % headings[index])
	var sample := PackedStringArray()
	for index in range(mini(4, problems.size())):
		sample.append(problems[index])
	check_eq(
		problems.size(), 0,
		"框架说明的目录与真实小节对不上（%d 处：%s）——新增/改名小节要顺手改目录"
			% [problems.size(), "；".join(sample)]
	)


## 48. `check_loop` 的「段数」：代码里的段标记是唯一出处，两份文档不许各写一个数。
##
## 由来（2026-10-03）：看板与工具表都写着"**10 段**真引擎"，而 `check_loop.gd` 里只有 **9 个**
## 段标记（`# ---------- ① …`）；更糟的是 **⑥ 那行注释是复制粘贴错的**——它写着"存档 → 读档"，
## 下面调的却是 `_dungeon_round`（副本巡游）。数字对不上、注释说错，两处都没有东西会红。
## 现在：段标记是唯一出处，两份文档里 `**N 段**` 的那个 N 必须等于标记数；注释写错靠人读，
## 但"数量"这一层钉住。
##
## **叙述里引用旧值时别用粗体**：这条门限把粗体的 `**N 段**` 当成"声称当前段数"，
## 所以决策正文里回顾"当时写着 10 段"要写成普通『10 段』——第一次跑就是被自己的正文判红的
## （同 197 的"门限命中自己"）。
func _check_loop_segment_count() -> void:
	var loop := FileAccess.get_file_as_string("res://tools/check_loop.gd")
	check_false(loop.is_empty(), "读得到 check_loop.gd")
	var marker_re := RegEx.new()
	marker_re.compile("(?m)^\\s*# -{5,} ")
	var segments := marker_re.search_all(loop).size()
	check_gt(float(segments), 5.0, "check_loop 扫到足够多的段标记（%d 段）" % segments)

	var claim_re := RegEx.new()
	claim_re.compile("\\*\\*(\\d+)\\s*段")
	var claims := 0
	for path: String in [STATUS_PATH, "res://docs/dev/框架说明.md"]:
		var text := FileAccess.get_file_as_string(path)
		for hit: RegExMatch in claim_re.search_all(text):
			claims += 1
			check_eq(
				int(hit.get_string(1)), segments,
				"%s 写「%s」，而 check_loop 里有 %d 个段标记" % [path.get_file(), hit.get_string(0), segments]
			)
	check_gt(float(claims), 0.0, "至少在一份文档里写明了闭环段数（扫到 %d 处）" % claims)


## 49. 策划文档里写的「共 N 张 CSV」必须等于真实的表数。
##
## 由来（2026-10-03）：`00_总览.md` 结尾写着「全部数值配置在 `data/tables/`，共 **22** 张 CSV」，
## 而 `06_配置表说明.md` 写的是 37、现实也是 **37**——**同一套策划文档自己就对不上**。
## 这跟 183 的 Q 范围、185 的看板数字、194 的步数、205 的段数是同一家族：写下来的数字没人核。
##
## 口径：只扫策划文档里 `N 张 CSV` 这种**关于表数**的声称（`07` 里那些"地图 1 张/5 张"不算），
## 数从 `table_registry.TABLES` 现算（不写死）；`CHANGELOG.md` 不看——它是带日期的叙事
## （"23 张 / 301 行"记的是当时）。
func _check_design_doc_table_count() -> void:
	var expected := TableRegistryScript.TABLES.size()
	check_gt(float(expected), 10.0, "注册表里扫到足够多的表（%d 张）" % expected)
	var claim_re := RegEx.new()
	claim_re.compile("(\\d+)\\s*张\\s*CSV")
	var claims := 0
	for path: String in _collect_files(["docs/design"]):
		if not path.ends_with(".md") or path.ends_with("CHANGELOG.md"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in claim_re.search_all(text):
			claims += 1
			check_eq(
				int(hit.get_string(1)), expected,
				"%s 写「%s」，而 data/tables 里实有 %d 张（以注册表为准）"
					% [path.get_file(), hit.get_string(0), expected]
			)
	check_gt(float(claims), 0.0, "至少在一份策划文档里写明了表数（扫到 %d 处）" % claims)


## 40. 「现状类文档」里不许写死总命令的步数。
##
## 由来（2026-10-03）：`框架说明` 的工具清单里一边写着"docs 里不再写死步数，免得加一步就漏改一处"
## （还举了真漏过的例子），**同一段的上方却写着「当前 16」**；`地图搭建说明.md` 里也还留着
## 「15 步里的「map acceptance」」——而实际已经是 17 步。这两处都是"写下来那天是真的、之后没人改"。
## 政策已经写在文档里了，缺的是**有人盯着**：这条门限扫"现状类文档"（见下面的清单）里的
## `数字+步`，发现就红，并告诉怎么改（写成"看末尾 `passed steps`"）。
##
## 为什么 `框架说明.md` 不在清单里：它是**带日期的叙事**（"总命令 14 步 → 15 步""占一步（16 步）"），
## 那些句子记的是"那天发生了什么"，拿现状口径去量会假红（同 183 的 Q 范围、185 的看板数字）。
## 同理 `平衡观测.md`／`性能观测.md` 是快照报告，也不收。
const CURRENT_STATE_DOCS := [
	"AGENTS.md",
	"README.md",
	"docs/dev/当前状态.md",
	"docs/dev/地图搭建说明.md",
]


func _check_no_hardcoded_step_counts() -> void:
	var step_re := RegEx.new()
	step_re.compile("\\d+\\s*步")
	var scanned := 0
	for path: String in CURRENT_STATE_DOCS:
		var text := FileAccess.get_file_as_string("res://" + path)
		check_false(text.is_empty(), "读得到 %s" % path)
		scanned += 1
		for hit: RegExMatch in step_re.search_all(text):
			fail(
				"%s 里写死了总命令的步数（「%s」）：加一步它就变成假话——改成「看末尾 `passed steps`」"
					% [path, hit.get_string(0)]
			)
	check_eq(scanned, CURRENT_STATE_DOCS.size(), "现状类文档都读到了（%d 份）" % scanned)


## 39. `scenes/` 下每个场景都必须有一步"真的跑得到它"。
##
## 由来（2026-10-03）：`bootstrap.tscn` 被 `AGENTS.md` 当成"配置表诊断场景"，却**没有任何一步跑过它**
## （决策 192 才补上）。而现有的孤儿门限只问"有没有人**提到**这个路径"——`AGENTS.md` 里写一句也算提到，
## 所以"提到过"和"跑得到"是两件事。这里的判据换成可执行的：
## 每个场景要么出现在 `run_all_checks.bat` 的真场景自检里，要么被某个用例 `load()`／`instantiate()`。
## 两者都没有 → 红（新场景写完忘了接上？还是本来就该删？）。
func _check_every_scene_is_reachable() -> void:
	var runner := FileAccess.get_file_as_string("res://tools/run_all_checks.bat")
	check_false(runner.is_empty(), "读得到总命令脚本")
	var corpus := ""
	for path: String in _collect_files(["tests"]):
		corpus += FileAccess.get_file_as_string("res://" + path)
	var checked := 0
	for path: String in _collect_files(["scenes"]):
		if not path.ends_with(".tscn"):
			continue
		checked += 1
		var res_path := "res://" + path
		if runner.contains(res_path):
			continue     # 有真场景自检（自带 `SELF-TEST` 标记与退出码）
		check_true(
			corpus.contains(res_path),
			"%s 既不在总命令的真场景自检里、也没有任何用例加载它——新场景得接上（决策 192 的由来）" % res_path
		)
	check_gt(float(checked), 15.0, "扫到足够多的场景（%d 个）" % checked)


func _collect_files_with_suffix(dir: String, suffix: String, out: PackedStringArray) -> void:
	var handle := DirAccess.open("res://" + dir)
	if handle == null:
		return
	for file_name: String in handle.get_files():
		if file_name.ends_with(suffix):
			out.append(("%s/%s" % [dir, file_name]).trim_prefix("/"))
	for sub: String in handle.get_directories():
		if sub.begins_with("."):
			continue
		_collect_files_with_suffix(("%s/%s" % [dir, sub]).trim_prefix("/"), suffix, out)


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


## 14. AGENTS.md 里那几条**仓库级硬约定**也要有门限，不然「约定」只是愿望。
##
## ① `src/` 不许直接读 `res://data/tables/`：CSV 是**构建期输入**，运行期只读 `data/generated/*.tres`
##    （直接读 CSV 会绕过校验、导出后也可能根本不在包里）。只认 `res://data/tables` 这个前缀，
##    `res://src/data/tables/...`（行类路径）不算——两者只差一个 `src/`。
## ② `tools/*.bat` 必须纯 ASCII + CRLF：cmd 用本地代码页解析，非 ASCII 变乱码；
##    LF-only 时 `call :label` 之类的标签查找会失效（这条真踩过）。
## ③ `tools/*.ps1` 只要含非 ASCII（中文注释）就必须带 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读，
##    中文被拆坏后连语法都报错（`audit_table_usage.ps1` 踩过）。
##
## 白名单只有一处：`src/data/table_registry.gd` 里的 `TABLES_DIR := "res://data/tables"` ——
## **构建管线**（`tools/build_tables.gd`）就是靠它去读 CSV 的，那是它该干的事。
const CSV_DIR_WHITELIST := ["src/data/table_registry.gd"]


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


## `.bat`：纯 ASCII + 全是 CRLF（有一处裸 LF 就点出来，附字节位置方便定位）
func _audit_bat(path: String) -> void:
	var bytes := _read_raw(path)
	if bytes.is_empty():
		fail("%s 读不到内容" % path)
		return
	var bare_lf := -1
	var non_ascii := -1
	for i in range(bytes.size()):
		if bytes[i] == 10 and (i == 0 or bytes[i - 1] != 13):
			if bare_lf < 0:
				bare_lf = i
		if bytes[i] > 127 and non_ascii < 0:
			non_ascii = i
	check_eq(bare_lf, -1, "%s 第 %d 字节是裸 LF：cmd 下标签查找会失效，请转成 CRLF" % [path, bare_lf])
	check_eq(non_ascii, -1, "%s 第 %d 字节不是 ASCII：cmd 按本地代码页解析会变乱码" % [path, non_ascii])


## `.ps1`：含非 ASCII 就必须有 UTF-8 BOM
func _audit_ps1(path: String) -> void:
	var bytes := _read_raw(path)
	if bytes.is_empty():
		fail("%s 读不到内容" % path)
		return
	var has_bom := bytes.size() >= 3 and bytes[0] == 0xEF and bytes[1] == 0xBB and bytes[2] == 0xBF
	var has_non_ascii := false
	for byte in bytes:
		if byte > 127:
			has_non_ascii = true
			break
	check_true(
		not has_non_ascii or has_bom,
		"%s 含非 ASCII（中文）却没有 UTF-8 BOM：PowerShell 5.1 会按 ANSI 读坏它" % path,
	)


## 15. 06_配置表说明.md 里写的枚举，必须与代码/数据对得上。
##
## 为什么值得一条门限：06 是**策划配置时的依据**——照文档填一个代码不接受的值，构建期会直接报
## 「不是合法取值」，来回一趟才知道该填什么。这条门限诞生当天就抓到一个真坑：
## `event_check.reward_type` 文档写 7 种，代码 `ENUMS` 只剩 4 种（none／item／room／event），
## 于是「给判定配一条 equip 奖励」——06 允许、PS1 允许、`EventCheckService` 也实现了的写法——
## 会被自己的构建期校验器拒掉（见框架说明决策 87）。
##
## 解析两种写法（06 里都用）：① 表格行 `| \`列\` | 类型 | \`a\` / \`b\` … |`（值全是反引号字面量、
## 至少 2 个，单值多是「引用某张表」）；② 说明行 `… \`列\` 取值：\`a\` / \`b\` …`。
## 比三处：代码 `TableValidator.ENUMS`；主键列比表里的实际取值；`equip_base.slot` 因为**故意不写死**，
## 比 `equip_slot_def.slot_id` 的实际取值。
## 已知「文档过期」的几处写在 `DOC_ENUM_KNOWN_STALE` 里并写明理由，改一处删一行。
## **2026-10-03：四处都改完了（06 按代码与数据订正），白名单已清空**——现在文档与代码必须逐项一致，
## 再出现不一致会当场红（见框架说明决策 171）。这个空字典保留是因为下面的门限要读它的结构。
## 2026-10-03：这四处**都改完了**（06 按代码与数据订正），白名单清空——现在文档与代码必须逐项一致，
## 再出现不一致会当场红。留着这个空字典是因为门限的代码要读它（结构保留，条目归零）。
const DOC_ENUM_KNOWN_STALE := {}


func _check_doc_enums_match_code() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/06_配置表说明.md")
	check_false(doc.is_empty(), "读得到 06_配置表说明.md")
	var db = get_db()
	var enums: Dictionary = TableValidatorScript.ENUMS
	var primary_keys: Dictionary = TableRegistryScript.PRIMARY_KEYS
	var row_regex := RegEx.new()
	row_regex.compile("^\\|\\s*`([a-z_0-9]+)`\\s*\\|[^|]*\\|(.+)\\|\\s*$")
	var prose_regex := RegEx.new()
	prose_regex.compile("`([a-z_0-9]+)`\\s*取值：(.+)$")
	var value_regex := RegEx.new()
	value_regex.compile("`([A-Za-z_0-9:]+)`")
	var table := ""
	var doc_enums := {}
	for raw: String in doc.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("### ") and line.ends_with(".csv"):
			table = line.substr(4).strip_edges().trim_suffix(".csv")
			continue
		if table.is_empty():
			continue
		var key_column := ""
		var body := ""
		var row_hit := row_regex.search(line)
		if row_hit != null:
			key_column = row_hit.get_string(1)
			body = row_hit.get_string(2)
		else:
			var prose_hit := prose_regex.search(line)
			if prose_hit == null:
				continue
			key_column = prose_hit.get_string(1)
			body = prose_hit.get_string(2)
		var values := PackedStringArray()
		var looks_like_prefix := false
		for found: RegExMatch in value_regex.search_all(body):
			var value := found.get_string(1)
			# `sk_`／`pf_`／`attr:`／`stat:` 这类是**前缀**，不是枚举值（文档里用来说明命名或引用形式）
			if value.ends_with("_") or value.ends_with(":"):
				looks_like_prefix = true
			if not value.is_empty() and not values.has(value):
				values.append(value)
		if values.size() < 2 or looks_like_prefix:
			continue     # 单值多是「引用某张表／可以留空」这类说明，不是枚举
		doc_enums["%s.%s" % [table, key_column]] = values
	check_gt(float(doc_enums.size()), 5.0, "从 06 里解析出足够多的枚举（%d 处）" % doc_enums.size())

	var checked := 0
	for key: String in doc_enums:
		if DOC_ENUM_KNOWN_STALE.has(key):
			continue
		var parts: PackedStringArray = key.split(".")
		var table_name := parts[0]
		var column := parts[1]
		var doc_values: PackedStringArray = doc_enums[key]
		if enums.has(table_name) and Dictionary(enums[table_name]).has(column):
			checked += 1
			_check_same_enum(key, doc_values, Array(Dictionary(enums[table_name])[column]), "代码 ENUMS")
			continue
		# 只比**单主键**表的主键列：复合主键的每一半都是 join 键，文档写的是语义不是取值集合
		var key_columns: Array = Array(primary_keys.get(table_name, []))
		if key_columns.size() == 1 and key_columns[0] == column:
			var actual := PackedStringArray()
			for row: Resource in db.rows(table_name):
				actual.append(str(row.get(column)))
			checked += 1
			_check_same_enum(key, doc_values, Array(actual), "表里的实际取值")
	check_gt(float(checked), 3.0, "真正比对过的枚举列（%d 处）" % checked)
	# 白名单自己也会烂：文档里那行不在了就把白名单一起删
	for key: String in DOC_ENUM_KNOWN_STALE:
		check_true(doc_enums.has(key), "06 枚举白名单里的 %s 已经不在文档里了，把这条删掉" % key)


## 两个取值集合必须一样（空串先滤掉：`ENUMS` 用 "" 表示「允许留空」，文档不会写出来）
func _check_same_enum(key: String, doc_values: PackedStringArray, actual: Array, side: String) -> void:
	var actual_set := {}
	for value: Variant in actual:
		actual_set[str(value)] = true
	var doc_set := {}
	for value: String in doc_values:
		doc_set[value] = true
	var doc_only := PackedStringArray()
	for value: String in doc_values:
		if value != "" and not actual_set.has(value):
			doc_only.append(value)
	var actual_only := PackedStringArray()
	for value: Variant in actual:
		var text := str(value)
		if text != "" and not doc_set.has(text):
			actual_only.append(text)
	check_true(
		doc_only.is_empty() and actual_only.is_empty(),
		"06 的 %s 与%s 对不上：文档多 [%s]，那边多 [%s]" % [
			key, side, ", ".join(doc_only), ", ".join(actual_only),
		],
	)


## 16. 「同一事实只许有一处定义」——只收那些**漂移了不会报错、只会静默丢东西**的常量。
##
## 由头：`LOCKED_SCENES` 曾在两个文件里各写一份（注释还写着「同一口径」），谁改一边都不会红；
## 贡献 kind 字符串（attr_point／stat_flat）原先在 4 个文件里写死，而**写错一个字母就会被
## `AttributeCalculator` 的 `!=` 判断静默跳过**——面板上少一截加成，没有任何报错。
##
## 故意**不**收场景路径常量（`PLACEHOLDER_SCENE` 等 9 处）：路径写错会当场加载失败，是响的，
## 不值得为它加门限、更不值得为了门限去打包一堆 const。
const SINGLE_SOURCE_RULES := [
	{
		"label": "const LOCKED_SCENES",
		"owner": "src/core/world_map_service.gd",
		"also": [],
		"why": "哪些小地图本章不开放：大地图控制器必须引用它，不能各写一份（漂移了不会报错）",
	},
	{
		"label": "\"attr_point\"",
		"owner": "src/core/attribute_calculator.gd",
		"also": ["src/core/table_validator.gd"],
		"why": "贡献 kind 写错会被计算器静默跳过；table_validator 那处是 affix_pool.value_kind 的枚举（另一个轴）",
	},
	{
		"label": "\"stat_flat\"",
		"owner": "src/core/attribute_calculator.gd",
		"also": [],
		"why": "同上：贡献 kind 写错会被静默跳过",
	},
]


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


## 17. 设计文档里「写死的表」与配置表逐格对（**硬门限**：今天全一致）。
##
## 覆盖：01 的**星级表**（熟练度成长／修习门槛／打坐费用）、05 的**槽位表**（7 类 + 戒指 2 枚）、
## 05 的**稀有度表**（词条数量区间）、05 的**武器类型表**（4 类与中文名）。
## 04 的伤害类型表**故意不在这里**：它今天就有 1 格不一致（`dot_internal.use_def_qi`，缺口 #21），
## 改哪边要设计拍板，所以那条走 `validate_tables.ps1` 5.26 的**警告**；拍完板再挪进来当硬门限。
## 为什么值得一条门限：文档是策划改需求的第一落点，改了文档忘了改 CSV（或反过来）今天只有人眼能发现。
func _check_doc_tables_match_data() -> void:
	var db = get_db()
	var checked := 0
	var mastery_max := 10     # growth_const.mastery_max；01 的「熟练度成长 ×1.50」是指**练满**的倍率

	# ① 01 星级表：| ★N | 定位 | ×1.50 | 属性 8（或「无」） | 50 |
	var doc01 := FileAccess.get_file_as_string("res://docs/design/01_角色系统.md")
	var star_regex := RegEx.new()
	star_regex.compile("(?m)^\\|\\s*★(\\d)\\s*\\|[^|]*\\|\\s*×([0-9.]+)\\s*\\|\\s*([^|]*)\\|\\s*([0-9]+)\\s*\\|")
	var star_rows := {}
	for found: RegExMatch in star_regex.search_all(doc01):
		var req_text := found.get_string(3).strip_edges()
		star_rows[int(found.get_string(1))] = {
			"multiplier": float(found.get_string(2)),
			"req": 0 if req_text.contains("无") else int(req_text.replace("属性", ""). strip_edges()),
			"cost": int(found.get_string(4)),
		}
	check_eq(star_rows.size(), 5, "从 01 解析出 5 行星级表")
	for row: Resource in db.rows("skill_star_def"):
		var star := int(row.star)
		if not star_rows.has(star):
			continue
		checked += 1
		var doc: Dictionary = star_rows[star]
		check_float(
			1.0 + float(row.mastery_gain) * float(mastery_max), float(doc["multiplier"]),
			"★%d 练满的熟练度倍率与 01 一致" % star, 0.0001,
		)
		check_eq(int(row.learn_req_value), int(doc["req"]), "★%d 的修习门槛与 01 一致" % star)
		check_eq(int(row.cultivate_cost_base), int(doc["cost"]), "★%d 的打坐基础费用与 01 一致" % star)

	# ② 05 槽位表：| `slot` | 名 | 数量 | 说明 |
	var doc05 := FileAccess.get_file_as_string("res://docs/design/05_装备与掉落.md")
	var slot_regex := RegEx.new()
	slot_regex.compile("(?m)^\\|\\s*`([a-z_]+)`\\s*\\|\\s*([^|]+)\\|\\s*(\\d+)\\s*\\|")
	var slot_rows := {}
	for found: RegExMatch in slot_regex.search_all(doc05):
		slot_rows[found.get_string(1)] = {
			"name": found.get_string(2).strip_edges(),
			"max": int(found.get_string(3)),
		}
	check_eq(slot_rows.size(), 7, "从 05 解析出 7 类槽位")
	for row: Resource in db.rows("equip_slot_def"):
		var slot_id := str(row.slot_id)
		check_true(slot_rows.has(slot_id), "05 的槽位表里有 %s" % slot_id)
		if not slot_rows.has(slot_id):
			continue
		checked += 1
		var doc_slot: Dictionary = slot_rows[slot_id]
		check_eq(str(row.name_cn), str(doc_slot["name"]), "%s 的中文名与 05 一致" % slot_id)
		check_eq(int(row.max_equip), int(doc_slot["max"]), "%s 的可佩戴数量与 05 一致" % slot_id)

	# ③ 05 稀有度表：| common | 凡品 | 灰 | 0–1 |（词条数量）
	var rarity_regex := RegEx.new()
	rarity_regex.compile("(?m)^\\|\\s*(common|fine|rare|epic|legend)\\s*\\|[^|]*\\|[^|]*\\|\\s*([0-9]+)(?:–([0-9]+))?\\s*\\|")
	var rarity_rows := {}
	for found: RegExMatch in rarity_regex.search_all(doc05):
		var low := int(found.get_string(2))
		var high_text := found.get_string(3)
		rarity_rows[found.get_string(1)] = {
			"min": low, "max": int(high_text) if not high_text.is_empty() else low,
		}
	check_eq(rarity_rows.size(), 5, "从 05 解析出 5 档稀有度")
	for row: Resource in db.rows("rarity_def"):
		var rarity_id := str(row.rarity_id)
		if not rarity_rows.has(rarity_id):
			continue
		checked += 1
		var doc_rarity: Dictionary = rarity_rows[rarity_id]
		check_eq(int(row.affix_min), int(doc_rarity["min"]), "%s 的词条数下限与 05 一致" % rarity_id)
		check_eq(int(row.affix_max), int(doc_rarity["max"]), "%s 的词条数上限与 05 一致" % rarity_id)

	# ④ 05 武器类型表：| `sword` | 剑 | 敏・智 | 招式均衡 |
	var weapon_regex := RegEx.new()
	weapon_regex.compile("(?m)^\\|\\s*`(sword|fist|blade|spear)`\\s*\\|\\s*([^|]+)\\|")
	var weapon_rows := {}
	for found: RegExMatch in weapon_regex.search_all(doc05):
		weapon_rows[found.get_string(1)] = found.get_string(2).strip_edges()
	check_eq(weapon_rows.size(), 4, "从 05 解析出 4 种武器类型")
	for row: Resource in db.rows("weapon_type_def"):
		var weapon_id := str(row.weapon_type)
		check_true(weapon_rows.has(weapon_id), "05 的武器表里有 %s" % weapon_id)
		if not weapon_rows.has(weapon_id):
			continue
		checked += 1
		check_eq(str(row.name_cn), str(weapon_rows[weapon_id]), "%s 的中文名与 05 一致" % weapon_id)

	# ⑤ 01 的七维属性表：| 力 str | 主职能 | 次要 |
	var attr_regex := RegEx.new()
	attr_regex.compile("(?m)^\\|\\s*([^|\\s]+)\\s+([a-z]{2,4})\\s*\\|[^|]*\\|")
	var attr_rows := {}
	for found: RegExMatch in attr_regex.search_all(doc01):
		attr_rows[found.get_string(2)] = found.get_string(1)
	check_eq(attr_rows.size(), 7, "从 01 解析出 7 维属性")
	for row: Resource in db.rows("attribute_def"):
		var attr_id := str(row.attr_id)
		check_true(attr_rows.has(attr_id), "01 的属性表里有 %s" % attr_id)
		if not attr_rows.has(attr_id):
			continue
		checked += 1
		check_eq(str(row.name_cn), str(attr_rows[attr_id]), "%s 的中文名与 01 一致" % attr_id)

	# ⑥ 01 的非战斗技能表：| `yishu` | 医术 | 智 | 说明 |
	var skill_regex := RegEx.new()
	skill_regex.compile("(?m)^\\|\\s*`(qimen|yishu|dusu|wenxue|shengcun)`\\s*\\|\\s*([^|]+)\\|\\s*([^|]+)\\|")
	var attr_name_to_id := {
		"力": "str", "体": "con", "敏": "agi", "智": "int", "运": "luk", "悟性": "wu", "根骨": "gen",
	}
	var skill_rows := {}
	for found: RegExMatch in skill_regex.search_all(doc01):
		skill_rows[found.get_string(1)] = {
			"name": found.get_string(2).strip_edges(),
			"attr": attr_name_to_id.get(found.get_string(3).strip_edges(), ""),
		}
	check_eq(skill_rows.size(), 5, "从 01 解析出 5 项非战斗技能")
	for row: Resource in db.rows("event_skill_def"):
		var skill_id := str(row.skill_id)
		check_true(skill_rows.has(skill_id), "01 的非战斗技能表里有 %s" % skill_id)
		if not skill_rows.has(skill_id):
			continue
		checked += 1
		var doc_skill: Dictionary = skill_rows[skill_id]
		check_eq(str(row.name_cn), str(doc_skill["name"]), "%s 的中文名与 01 一致" % skill_id)
		check_eq(str(row.related_attr), str(doc_skill["attr"]), "%s 的关联属性与 01 一致" % skill_id)
	check_gt(float(checked), 15.0, "真正比过的「文档表 vs 数据」行（%d 处）" % checked)
	_check_doc_spawn_table(db)


## 17b. 07 的**逐位点明雷表**（`spawn_id`／队伍／行为／警戒格数／刷新秒数／精英标记）与 `roaming_spawn` 对。
##
## 为什么它值得硬门限：这张表是**地编的施工图**，而且它比 02 的分布表更细——位点 id、警戒半径、
## 刷新秒数、精英标记一个不漏，还列了 02 漏掉的野猪群。位点集合也严格比（多一个少一个都红）。
## `spawn_id` 那列支持简写（`sp_lp_wolf_01/02/03`），按前缀展开。
func _check_doc_spawn_table(db) -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/07_地图资源需求.md")
	var row_regex := RegEx.new()
	row_regex.compile("(?m)^\\|\\s*([^|]+?)\\s*\\|\\s*`([^`]+)`\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|\\s*([^|]+?)\\s*\\|")
	var tail_regex := RegEx.new()
	tail_regex.compile("(\\d+)$")
	var behavior_word := {
		"游荡": "wander", "巡逻": "patrol", "驻守": "idle", "追击": "chase", "沉睡": "sleep",
	}
	var team_name_to_id := {}
	for row: Resource in db.rows("enemy_team"):
		team_name_to_id[str(row.name_cn)] = str(row.team_id)
	var region_name_to_id := {}
	for row: Resource in db.rows("map_region"):
		region_name_to_id[str(row.name_cn)] = str(row.node_id)
	region_name_to_id["黑风寨外围"] = "n_heifengzhai"     # 07 与 02 都用这个口语叫法
	var doc_spawns := {}
	var rows_checked := 0
	for found: RegExMatch in row_regex.search_all(doc):
		var spawn_cell := found.get_string(2)
		if not spawn_cell.begins_with("sp_"):
			continue
		var region_name := found.get_string(1).strip_edges()
		var team_name := found.get_string(3).strip_edges()
		var behavior_text := found.get_string(4).strip_edges()
		var alert_text := found.get_string(5)
		var respawn_text := found.get_string(6)
		var elite_text := found.get_string(7)
		if not behavior_word.has(behavior_text):
			continue     # 不是明雷表那几行（07 里还有别的表格也带 `sp_` 引用）
		rows_checked += 1
		var ids := PackedStringArray()
		var parts := spawn_cell.split("/", false)
		var base := parts[0].strip_edges()
		var prefix := base
		var tail := tail_regex.search(base)
		if tail != null:
			prefix = base.substr(0, base.length() - tail.get_string(1).length())
		for part: String in parts:
			var piece := part.strip_edges()
			ids.append(piece if piece.length() > 2 else prefix + piece)
		for spawn_id: String in ids:
			doc_spawns[spawn_id] = {
				"region": region_name_to_id.get(region_name, ""),
				"team": team_name_to_id.get(team_name, ""),
				"behavior": behavior_word[behavior_text],
				"alert": float(alert_text.replace("格", "").strip_edges()),
				"respawn": 0 if respawn_text.contains("不刷新") else int(respawn_text.replace("s", "").strip_edges()),
				"elite": 1 if elite_text.contains("✔") else 0,
			}
	check_eq(rows_checked, 13, "从 07 解析出 13 行逐位点明雷表")
	check_eq(doc_spawns.size(), 17, "07 那 13 行展开成 17 个位点")
	var actual_ids := PackedStringArray()
	for row: Resource in db.rows("roaming_spawn"):
		actual_ids.append(str(row.spawn_id))
	check_eq(doc_spawns.size(), actual_ids.size(), "07 列出的位点数与 roaming_spawn 一致")
	for row: Resource in db.rows("roaming_spawn"):
		var spawn_id := str(row.spawn_id)
		check_true(doc_spawns.has(spawn_id), "07 的逐位点表里有 %s" % spawn_id)
		if not doc_spawns.has(spawn_id):
			continue
		var doc_row: Dictionary = doc_spawns[spawn_id]
		check_eq(str(row.region_id), str(doc_row["region"]), "%s 的区域与 07 一致" % spawn_id)
		check_eq(str(row.team_id), str(doc_row["team"]), "%s 的队伍与 07 一致" % spawn_id)
		check_eq(str(row.behavior), str(doc_row["behavior"]), "%s 的行为与 07 一致" % spawn_id)
		check_float(float(row.alert_radius), float(doc_row["alert"]), "%s 的警戒半径与 07 一致" % spawn_id, 0.0001)
		check_eq(int(row.respawn_sec), int(doc_row["respawn"]), "%s 的刷新秒数与 07 一致" % spawn_id)
		check_eq(int(row.is_elite), int(doc_row["elite"]), "%s 的精英标记与 07 一致" % spawn_id)


## 18. 05 的「商店」表：**每类店该卖什么**与货架上的实物要对得上（硬门限，今天全一致）。
##
## 05_装备与掉落.md 的表：杂货铺＝材料、白板饰品／酒楼＝**非战斗**回血道具／
## 医馆＝**战斗中**回血道具（附加服务：花钱治疗）／铁匠铺＝白板装备。
## 两条回血道具不能混（05 专门写了「这是酒楼和医馆的职能分界，不能混」），所以这条门限卡的是
## `use_context` 与货架的对应——以前只测「买了能用」，没人管「这家店卖的东西对不对」。
func _check_shop_shelves_match_design() -> void:
	var db = get_db()
	var stock_by_group := {}
	for row: Resource in db.rows("shop_stock"):
		var shop_id := str(row.shop_id)
		if not stock_by_group.has(shop_id):
			stock_by_group[shop_id] = []
		stock_by_group[shop_id].append(row)
	var checked := 0
	for building: Resource in db.rows("building_def"):
		var group := str(building.stock_group)
		if group.is_empty():
			continue
		var building_id := str(building.building_id)
		check_true(stock_by_group.has(group), "%s 的货架 %s 在 shop_stock 里有货" % [building_id, group])
		if not stock_by_group.has(group):
			continue
		for row: Resource in stock_by_group[group]:
			var item_id := str(row.item_id)
			var equip: Resource = db.get_row("equip_base", item_id)
			var item: Resource = db.get_row("item_base", item_id)
			checked += 1
			match building_id:
				"bld_grocery":
					var ok := false
					if item != null and str(item.item_type) == "material":
						ok = true
					if equip != null and str(equip.rarity) == "common" \
						and (str(equip.slot) == "ring" or str(equip.slot) == "necklace"):
						ok = true
					check_true(ok, "杂货铺只卖材料与白板饰品（%s 不符）" % item_id)
				"bld_tavern":
					check_true(
						item != null and str(item.item_type) == "consumable" \
							and str(item.use_context) == "field",
						"酒楼只卖非战斗回血道具（%s 不符）" % item_id,
					)
				"bld_clinic":
					check_true(
						item != null and str(item.item_type) == "consumable" \
							and str(item.use_context) == "battle",
						"医馆只卖战斗中回血道具（%s 不符）" % item_id,
					)
				"bld_smith":
					check_true(
						equip != null and str(equip.rarity) == "common",
						"铁匠铺只卖白板装备（%s 不符）" % item_id,
					)
		# 附加服务：05 那张表里只有医馆带「花钱治疗」
		if building_id == "bld_clinic":
			check_eq(str(building.service_id), "heal", "医馆带治疗服务")
		else:
			check_eq(str(building.service_id), "", "%s 没有附加服务（05 的表里只有医馆有）" % building_id)
	check_gt(float(checked), 10.0, "真正比过的货架条目（%d 条）" % checked)


## 19. 02 里的**接触手感数值**：文档正文写了三个百分比，代码/表里各有一份，别让它们悄悄分家。
##
## 02_地图与明雷.md 的「不同方向接触」表与「潜行」一节写着：
##   背袭「敌方架势初始值**降低 30%**」 → `combat_const.backstab_poise_reduce`
##   奇袭「**首回合伤害 +50%**」        → `combat_const.surprise_damage_bonus`
##   潜行「**移速降至 60%**」           → `player_controller.SNEAK_RATIO`
## 这三个数直接决定手感（背袭划不划算、潜行绕不绕得开），而它们分住在文档、表、代码三处。
## 数字**从文档里现读**（不在代码里再抄一遍），所以策划改文档而忘了改表/代码时这里会红。
## 潜行的「被发现判定降低」02 没给数值（`combat_const.sneak_detect_reduce = 0.4` 是开发侧定的），不在这条里。
func _check_combat_feel_matches_design() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/02_地图与明雷.md")
	var db = get_db()
	var want := {}
	var patterns := {
		"降低\\s*(\\d+)%": "backstab",
		"首回合伤害\\s*\\+(\\d+)%": "surprise",
		"移速降至\\s*(\\d+)%": "sneak",
	}
	for pattern: String in patterns:
		var regex := RegEx.new()
		regex.compile(pattern)
		var found := regex.search(doc)
		check_not_null(found, "02 里找得到「%s」这个数值" % pattern)
		if found != null:
			want[str(patterns[pattern])] = float(found.get_string(1))
	var backstab: Resource = db.get_row("combat_const", "backstab_poise_reduce")
	check_not_null(backstab, "combat_const 里有背袭架势削减")
	if backstab != null:
		check_float(
			float(backstab.value) * 100.0, float(want.get("backstab", -1.0)),
			"背袭架势削减与 02 一致（%）", 0.0001,
		)
	var surprise: Resource = db.get_row("combat_const", "surprise_damage_bonus")
	check_not_null(surprise, "combat_const 里有奇袭增伤")
	if surprise != null:
		check_float(
			float(surprise.value) * 100.0, float(want.get("surprise", -1.0)),
			"奇袭首回合增伤与 02 一致（%）", 0.0001,
		)
	var controller := preload("res://src/world/player_controller.gd")
	check_float(
		float(controller.SNEAK_RATIO) * 100.0, float(want.get("sneak", -1.0)),
		"潜行移速比与 02 一致（%）", 0.0001,
	)


## 20. 「写了但游戏路径从不调用」的接口清单：钉成门限，只许少不许悄悄多。
##
## 由来：框架说明「API 使用审计」那一节记过一次人工审计——图鉴奖励就是栽在这里的
## （`codex_bonus()` 早写好了，全项目只有用例在调，游戏里收集再多也不涨属性）。
## 那次全项目扫出 22 个「只被测试调用」的接口并逐个看过；后来又加了不少代码，
## 所以这里把它做成**常驻门限**：每个 `func` 的名字在 `src/`＋`scenes/`＋`project.godot` 里
## 出现 ≤1 次（只有定义那行）＝游戏路径从不调用；出现在下面的白名单里才算通过。
## `_` 开头的一律跳过（引擎回调与私有 helper，与那次审计同一口径）。
## **新加的这种函数**会当场红：要么接进游戏路径，要么把名字与理由写进白名单（有意识的行为）。
const TEST_ONLY_API_ALLOWED := {
	# —— 0.7.0 数据层：buff／套装的服务类先落地并有用例钉着，**结算与六指令随后接** ——
	# （等 BattleActor/BattleSimulator/battle_screen 真正读它们时，把这些条目逐条删掉。）
	"polarity_of": "0.7.0 buff 数据层：结算未接（见 08 与框架说明决策 210）",
	"is_field_buff": "0.7.0 buff 数据层：战斗外增益的 HUD 显示未接",
	"grants_of_buff": "0.7.0 buff 数据层：发放时机未接",
	# —— 查询接口：游戏逻辑不需要，测试/界面按需读 ——
	"has_state": "查询", "has_status": "查询", "hp_ratio": "查询", "kill_style_of": "查询",
	"checks_for_region": "查询", "shop_state_of": "查询", "has_first_kill": "查询",
	"first_kill_count": "查询", "get_seed": "查询", "has_table": "查询",
	"has_value": "查询", "column_of": "查询", "get_id": "查询", "has_cap": "查询", "has_service": "查询",
	"buff_stacks": "查询", "buff_remaining": "查询",
	"exits": "查询", "is_unlimited": "查询", "focus_attrs": "查询", "round_text": "查询",
	"auto_running": "查询", "slot_button": "查询", "tier_id": "查询", "body_texture_path": "查询",
	"brazier_texture_path": "查询：用例验火盆用的是地编交付的那两张「灭／燃」贴图（07 §8.3）",
	"is_defined": "查询", "node_icon": "查询", "highlight_visible": "查询", "map_progress_text": "查询",
	"fog_cells": "查询", "facing_quadrant": "查询", "is_sneaking": "查询", "has_elite_glow": "查询",
	"elite_marker_id": "查询", "weapon_type_name_of": "查询", "team_drop_groups": "查询",
	"threat_tags": "查询", "skills_from_enemy": "查询", "resolve_db": "查询",
	# —— 音效：播放记录只给用例看（游戏路径不需要知道"刚才放过什么"）——
	"played_events": "音效播放记录：用例读",
	"has_played": "同上",
	"clear_log": "同上（用例之间清记录）",
	"shutdown": "音效播放层收尾：--script 模式下没人回收挂在 root 上的节点（由 tools/run_tests.gd 调）",
	# —— 等设计补效果列/流程：接口留着，游戏里还没有调用方 ——
	"is_field_use": "等 item_base 效果列（05：两种回血道具要分开，使用未做）",
	"is_battle_use": "同上",
	"clear_dispellable": "等「驱散」来源（设计只标了可驱散，没给驱散技能）",
	"set_pity": "保底计数由存档读写，游戏路径不需要外部塞 —— 留给测试",
	"roll_group_many": "多组连掉的便利接口，游戏路径逐组调 roll_group",
	"set_strategy": "界面只用轮换（cycle_strategy）；直接设值留给测试",
	# —— 存档/工具接口：设计或流程上还没有调用方 ——
	"delete_slot": "设计文档里没有「删除存档」这一条",
	"has_any_save": "菜单逐个槽位检查，没用到这个汇总查询",
}


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


## 按字节读文件（编码检查要用原始字节，`get_file_as_string` 会先把编码理顺）
func _read_raw(path: String) -> PackedByteArray:
	var file := FileAccess.open("res://" + path, FileAccess.READ)
	if file == null:
		return PackedByteArray()
	return file.get_buffer(file.get_length())


## 10. 验证清单登记的用例必须真的在**执行列表**里（`tools/run_tests.gd` 的 `TEST_SCRIPTS`）。
## 「文件存在」与「有人引用」前两条都查过了，但**登记了却没进执行列表**的用例会永远不跑——
## 文件在、也被引用了，那两个门限都抓不到它。这条把第三个方向也焊上。
func _check_verify_tests_are_executed(verify_rows: Array) -> void:
	var runner := FileAccess.get_file_as_string("res://tools/run_tests.gd")
	var regex := RegEx.new()
	regex.compile("res://(tests/[^\"]+\\.gd)")
	var executed := PackedStringArray()
	for found: RegExMatch in regex.search_all(runner):
		var path := found.get_string(1)
		if not executed.has(path):
			executed.append(path)
	check_gt(float(executed.size()), 20.0, "能读出执行列表（%d 个用例）" % executed.size())
	for row: Dictionary in verify_rows:
		var module_id := str(row.get("module_id", ""))
		for path: String in _split(str(row.get("verify_paths", ""))):
			if not path.begins_with("tests/"):
				continue   # 只查 tests/ 下的用例；引擎级脚本（check_loop 等）另有入口与判定
			check_true(
				executed.has(path),
				"%s 登记的用例 %s 不在 tools/run_tests.gd 的执行列表里（登记了却从不运行）" % [module_id, path],
			)


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


func _check_doc_tool_references() -> void:
	var regex := RegEx.new()
	regex.compile("tools[\\\\/]([A-Za-z0-9_\\.\\\\/]+\\.(?:bat|ps1|gd|py))")
	var checked := 0
	for path: String in _collect_files(["docs/dev"]):
		if not path.ends_with(".md"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		for found: RegExMatch in regex.search_all(text):
			var rel := found.get_string(1).replace("\\", "/")
			checked += 1
			check_true(
				FileAccess.file_exists("res://tools/" + rel),
				"%s 引用了不存在的工具：tools/%s" % [path.get_file(), rel],
			)
	check_gt(float(checked), 5.0, "开发文档里引用到足够多的工具（%d 处）" % checked)


##
## 代码里支持 `--xxx-selftest` 的每个脚本，都要在 `tools/run_all_checks.bat` 里被调用一次；
## 反过来 runner 里写的开关也必须真的有实现——写错开关名时场景会照常启动、不退出，
## 只因为没有 `SELF-TEST: OK` 而判红，从 harness 层面看不出是「开关写错了」还是「场景真坏了」。
func _check_scene_selftests_are_wired() -> void:
	var code_flags := _collect_flags(["src"])
	var runner_flags := _collect_flags(["tools/run_all_checks.bat"])
	check_gt(float(code_flags.size()), 5.0, "扫到足够多的场景自检开关（%d 个）" % code_flags.size())
	check_gt(float(runner_flags.size()), 5.0, "总命令里调用了足够多的自检（%d 个）" % runner_flags.size())
	for flag: String in code_flags:
		check_true(
			runner_flags.has(flag),
			"%s 场景支持 %s，但 tools/run_all_checks.bat 没调用它（写了自检却没人跑）" % [flag, flag],
		)
	for flag: String in runner_flags:
		check_true(
			code_flags.has(flag),
			"tools/run_all_checks.bat 调用了 %s，但没有哪个脚本实现它（开关名写错了？）" % flag,
		)


## 从若干路径里扫出 `--xxx-selftest` 开关名（去重排序）。
## 路径可以是目录（递归收 .gd）也可以是单个文件——注意 `_collect_files()` 只认目录，
## 传文件进去会静默返回空（第一版就这么错，导致「runner 里 0 个开关」的假红）。
func _collect_flags(paths: Array) -> PackedStringArray:
	var regex := RegEx.new()
	regex.compile("--([a-z-]*selftest)")
	var files := PackedStringArray()
	for entry: String in paths:
		if not entry.get_extension().is_empty():
			files.append(entry)
		else:
			files.append_array(_collect_files([entry]))
	var found := PackedStringArray()
	for path: String in files:
		var text := FileAccess.get_file_as_string("res://" + path)
		for match: RegExMatch in regex.search_all(text):
			var flag := "--" + match.get_string(1)
			if not found.has(flag):
				found.append(flag)
	found.sort()
	return found


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


## 路径本身出现在别处，或它的 class_name 在别的文件里被用到
func _has_reference(corpus: String, path: String) -> bool:
	var text := FileAccess.get_file_as_string("res://" + path)
	if text.is_empty():
		return true   # 读不到就不在这里判（别的检查会报），免得误红
	var class_regex := RegEx.new()
	class_regex.compile("(?m)^class_name\\s+([A-Za-z_][A-Za-z0-9_]*)")
	var found := class_regex.search(text)
	if found != null:
		var class_name_id := found.get_string(1)
		var uses := 0
		var use_regex := RegEx.new()
		use_regex.compile("\\b%s\\b" % class_name_id)
		for match: RegExMatch in use_regex.search_all(corpus):
			uses += 1
		# 自己的定义算一次；别的文件里再用到就 > 1
		if uses > 1:
			return true
	return corpus.contains(path)


## 递归收集某几个目录下的文件（相对 res:// 的路径）
func _collect_files(dirs: Array) -> PackedStringArray:
	var out := PackedStringArray()
	for dir: String in dirs:
		_collect_dir(dir, out)
	return out


func _collect_dir(dir: String, out: PackedStringArray) -> void:
	var handle := DirAccess.open("res://" + dir)
	if handle == null:
		return
	for file_name: String in handle.get_files():
		if not TEXT_EXTENSIONS.has(file_name.get_extension().to_lower()):
			continue
		out.append("%s/%s" % [dir, file_name])
	for sub: String in handle.get_directories():
		if sub.begins_with("."):
			continue
		_collect_dir("%s/%s" % [dir, sub], out)


## 6. 对接表登记的 `code_paths` 与 `data_tables` 必须真实存在。
##
## 这两列是写给设计侧「去哪找」的——写错了比不写更糟（他们会照着找不到的东西来问，
## 或者以为某个模块早就动工了）。以前只校验了验证用例存在，这两列从来没查过。
func _check_contract_assets(contract: Array) -> void:
	for row: Dictionary in contract:
		var module_id := str(row.get("module_id", ""))
		for path: String in _split(str(row.get("code_paths", ""))):
			check_true(
				FileAccess.file_exists("res://" + path),
				"%s 的 code_paths 引用了不存在的文件：%s" % [module_id, path],
			)
		for table: String in _split(str(row.get("data_tables", ""))):
			var file_name := table if table.ends_with(".csv") else table + ".csv"
			check_true(
				FileAccess.file_exists("res://data/tables/" + file_name),
				"%s 的 data_tables 引用了不存在的表：%s" % [module_id, table],
			)
		for doc: String in _split(str(row.get("design_docs", ""))):
			check_true(
				FileAccess.file_exists("res://docs/design/" + doc),
				"%s 的 design_docs 引用了不存在的文档：%s" % [module_id, doc],
			)


## 7. 反向：`docs/design/` 下的每篇设计文档（README／CHANGELOG 除外）都应被某个模块引用。
## 一篇没人引用的设计文档，等于「没人负责的设计」——要么漏登记模块，要么文档该并进别处。
func _check_no_orphan_design_docs(contract: Array) -> void:
	var referenced := {}
	for row: Dictionary in contract:
		for doc: String in _split(str(row.get("design_docs", ""))):
			referenced[doc] = true
	var skip := ["README.md", "CHANGELOG.md"]
	for file_name: String in DirAccess.get_files_at("res://docs/design"):
		if not file_name.ends_with(".md") or skip.has(file_name):
			continue
		check_true(
			referenced.has(file_name),
			"设计文档 %s 没有被任何模块的 design_docs 引用（漏登记模块？）" % file_name,
		)


## 同一张表里 module_id 只能出现一次。
## 重复行会被后面那行**悄悄盖掉**（两边都按 module_id 建索引），
## 结果就是一边写着「已做」、一边写着「未做」而自检完全看不出来——这坑踩过一次。
func _check_no_duplicate_modules(contract: Array, verify_rows: Array) -> void:
	for pair: Array in [
		["模块对接表", contract], ["验证清单", verify_rows],
	]:
		var table_name: String = pair[0]
		var seen := {}
		for row: Dictionary in Array(pair[1]):
			var module_id := str(row.get("module_id", ""))
			if module_id.is_empty():
				continue
			check_false(seen.has(module_id), "%s 里 %s 重复出现（后者会盖掉前者）" % [table_name, module_id])
			seen[module_id] = true


## 1. 已动工的模块必须对齐当前设计版本。
func _check_design_version(version: String, contract: Array) -> void:
	for row: Dictionary in contract:
		var status := str(row.get("status", ""))
		if not STARTED_STATUSES.has(status):
			continue
		var module_id := str(row.get("module_id", ""))
		var aligned := str(row.get("design_version", ""))
		if aligned != version:
			fail("%s（%s）对齐的设计版本是 %s，当前是 %s：需求变过了，先读 CHANGELOG 评估返工，再更新代码与验证" % [
				module_id, status, aligned, version,
			])


## 2/3. 已动工的模块必须有验证用例，用例存在且对齐当前设计版本。
func _check_verify_coverage(version: String, contract: Array, verify_by_module: Dictionary) -> void:
	for row: Dictionary in contract:
		var status := str(row.get("status", ""))
		if not MENTIONED_STATUSES.has(status):
			continue
		var module_id := str(row.get("module_id", ""))
		if not verify_by_module.has(module_id):
			fail("%s 已动工，但没在 docs/dev/验证清单.csv 登记验证用例" % module_id)
			continue
		var verify_row: Dictionary = verify_by_module[module_id]
		var paths := _split(str(verify_row.get("verify_paths", "")))
		check_gt(float(paths.size()), 0.0, "%s 的 verify_paths 不能为空" % module_id)
		for path: String in paths:
			check_true(FileAccess.file_exists("res://" + path), "%s 引用的用例不存在：%s" % [module_id, path])
		var verified := str(verify_row.get("verified_design_version", ""))
		# 需返工的模块本来就要补验证，不要求它已经对齐
		if status != "需返工" and verified != version:
			fail("%s 的验证对齐版本是 %s，当前是 %s：需求变过，必须补或改用例（%s）后更新 verified_design_version" % [
				module_id, verified, version, ", ".join(paths),
			])


## 4. 验证清单不能引用对接表里不存在的模块。
func _check_contract_links(contract: Array, verify_rows: Array) -> void:
	var known: Dictionary = {}
	for row: Dictionary in contract:
		known[str(row.get("module_id", ""))] = true
	for row: Dictionary in verify_rows:
		var module_id := str(row.get("module_id", ""))
		check_true(known.has(module_id), "验证清单里的 module_id '%s' 不在对接表中" % module_id)


## 5. tests/ 下不许有没人引用的用例文件。
func _check_no_orphan_tests(verify_rows: Array) -> void:
	var referenced: Dictionary = {}
	for row: Dictionary in verify_rows:
		for path: String in _split(str(row.get("verify_paths", ""))):
			referenced[path] = true
	for file_name: String in DirAccess.get_files_at(TESTS_DIR):
		if not file_name.ends_with(".gd") or INFRASTRUCTURE_TESTS.has(file_name):
			continue
		var path := "tests/" + file_name
		check_true(referenced.has(path), "用例 %s 没有被任何模块的 verify_paths 引用（孤儿验证）" % path)
	# 基础设施白名单也要对得上文件（2026-10-03 补）：名字写错／文件删了都不该悄悄留着
	for file_name: String in INFRASTRUCTURE_TESTS:
		check_true(
			FileAccess.file_exists("%s/%s" % [TESTS_DIR, file_name]),
			"INFRASTRUCTURE_TESTS 里的 %s 不存在（名字写错或文件删了？）" % file_name,
		)


func _split(raw: String) -> PackedStringArray:
	var out := PackedStringArray()
	for part: String in raw.split("|", false):
		var trimmed := part.strip_edges()
		if not trimmed.is_empty():
			out.append(trimmed)
	return out


## 读 CHANGELOG 顶部的「当前设计版本」。
func _current_design_version() -> String:
	var file := FileAccess.open(CHANGELOG_PATH, FileAccess.READ)
	if file == null:
		fail("读不到 %s" % CHANGELOG_PATH)
		return ""
	var regex := RegEx.new()
	regex.compile("当前设计版本[:：]\\s*\\*{0,2}([0-9]+\\.[0-9]+\\.[0-9]+)")
	var version := ""
	while not file.eof_reached():
		var line := file.get_line()
		var found := regex.search(line)
		if found != null:
			version = found.get_string(1)
			break
	file.close()
	return version


## 读 CSV（自动剥 BOM），返回 [{列名: 值}, ...]。
func _read_csv(path: String) -> Array:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		fail("读不到 %s" % path)
		return []
	var header := file.get_csv_line()
	if header.size() > 0 and header[0].begins_with("\ufeff"):
		header[0] = header[0].substr(1)
	var rows: Array = []
	while not file.eof_reached():
		var line := file.get_csv_line()
		if line.size() == 1 and line[0].strip_edges().is_empty():
			continue
		var row: Dictionary = {}
		for index in range(header.size()):
			row[header[index].strip_edges()] = line[index] if index < line.size() else ""
		rows.append(row)
	file.close()
	return rows
