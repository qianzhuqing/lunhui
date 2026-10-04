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
const CONTRAST_PATH := "res://docs/dev/设计实现对照.md"
const HANDOVER_PATH := "res://docs/design/交接单.md"
const ART_SPEC_PATH := "res://docs/design/15_美术风格需求.md"
const ART_HANDOVER_PATH := "res://docs/design/交接单_美术.md"

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
## 「框架说明」概述里那两个存档数字（当前版本 vN／`to_dict()` 字段数）要拿代码现算
const GameStateScript := preload("res://src/core/game_state.gd")
## 套装档位的可达性是「有没有人能拿到」，与 SkillGrant 认的来源是同一个真相
const SkillGrantScript := preload("res://src/core/skill_grant.gd")


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
	_check_windowed_smoke_matches_runner()
	_check_framework_save_claims()
	_check_no_absolute_autoload_paths()
	_check_preload_for_global_class_calls()
	_check_ps1_mentions_enum_columns()
	_check_doc_tool_references()
	_check_no_dangling_uids()
	_check_no_orphan_import_files()
	_check_every_scene_is_reachable()
	_check_no_hardcoded_step_counts()
	_check_flag_reachability()
	_check_sidequest_favors()


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
	_check_blocker_citations_are_live()
	_check_special_effect_count_claim()
	_check_dashboard_numbers()
	_check_handover_sheet_numbers()
	_check_art_icon_counts()
	_check_design_doc_row_counts()
	_check_design_doc_refs()
	_check_missing_table_claims()
	_check_set_tier_reachability()
	_check_contrast_audit_numbers()
	_check_struck_entries_are_closed()
	_check_no_stray_control_bytes()
	_check_no_escape_eaten_tabs()
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
	# 03 是**黑风寨**那一章的副本文档；它正文里也写了「后山密道」（那条捷径在塌陷山洞）。
	# 0.26.0 起隐藏内容多了一个**别的副本**（石隙迷窟的 `trig_shixi_reward`）——
	# 那不归 03 管，所以下面比的是「03 点名的那几张图里的隐藏内容」，
	# 而不是 hidden_trigger 全表（全表一加副本就假红，见决策 284）。
	var scenes_in_doc := {}
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
		scenes_in_doc[str(row.scene_id)] = true
		scenes_in_doc["scene_cave"] = true      # 03 正文里的「后山密道」
		check_eq(
			str(row.trigger_type), HIDDEN_TYPE_WORDS[type_word],
			"03 说「%s」的类型是%s，hidden_trigger 里 trigger_type=%s" % [name, type_word, str(row.trigger_type)]
		)
	var expected := 0
	for row: Resource in db.rows("hidden_trigger"):
		if scenes_in_doc.has(str(row.scene_id)):
			expected += 1
	check_eq(rows, expected,
		"03 的隐藏内容行数与「03 提到的那些图」里的 hidden_trigger 行数一致（%s）" % str(scenes_in_doc.keys()))


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
## 余量上限（同 `MIN_ASSERTIONS` 的 `SLACK_LIMIT`）：实际条数比棘轮表高出这个数就红，
## 提示"加了检查就把 floor 贴上去"——不然那张表会慢慢退化成摆设（2026-10-04 实测漂了 1～11 条）。
const VERIFICATION_FLOOR_SLACK := 2
const VERIFICATION_CHECK_FLOORS := {
	"src/ui/battle_screen.gd": {"pattern": "ok = ok and", "floor": 22, "why": "场景自检的聚合断言"},
	"src/ui/shop_screen.gd": {"pattern": "ok = ok and", "floor": 12, "why": "场景自检的聚合断言"},
	"src/ui/waypoint_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/dungeon_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/character_screen.gd": {"pattern": "ok = ok and", "floor": 13, "why": "场景自检的聚合断言"},
	"src/ui/cultivate_screen.gd": {"pattern": "ok = ok and", "floor": 11, "why": "场景自检的聚合断言"},
	"src/ui/clue_screen.gd": {"pattern": "ok = ok and", "floor": 8, "why": "场景自检的聚合断言"},
	"src/ui/main_menu.gd": {"pattern": "ok = ok and", "floor": 7, "why": "场景自检的聚合断言"},
	"src/world/overworld_controller.gd": {"pattern": "ok = ok and", "floor": 17, "why": "场景自检的聚合断言（2026-10-04 决策 337 加了「观察点可见标记」一条）"},
	"src/world/local_map_controller.gd": {"pattern": "ok = ok and", "floor": 14, "why": "场景自检的聚合断言（2026-10-04 决策 337 加了「观察点可见标记」一条）"},
	"src/bootstrap.gd": {"pattern": "ok = ok and", "floor": 6, "why": "配置表诊断场景自检的聚合断言"},
	"src/ui/creation_screen.gd": {"pattern": "ok = ok and", "floor": 15, "why": "创建角色场景自检的聚合断言"},
	"src/ui/npc_panel.gd": {"pattern": "ok = ok and", "floor": 20, "why": "NPC 面板自检的聚合断言"},
	"tools/check_loop.gd": {"pattern": "ok = ok and", "floor": 15, "why": "跨场景闭环每段一个聚合断言（③ 现在有胜／败两次）"},
	"tools/mapgen/verify_maps.gd": {"pattern": "_problems.append(", "floor": 39, "why": "每个地图检查点都必须能报错（2026-10-04 决策 332 给「按 id 绑的 NPC 位点」加了 2 条：id 不存在／人不在本图）"},
}


func _check_verification_check_floors() -> void:
	var selftest_scenes := PackedStringArray()
	for path: String in _collect_files(["src"]):
		if not path.ends_with(".gd"):
			continue
		if FileAccess.get_file_as_string("res://" + path).contains("SELF-TEST: %s"):
			selftest_scenes.append(path)
	check_eq(
		selftest_scenes.size(), 13,
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
		# **余量也不能太大**（2026-10-04 补）：原先只查「不低于下限」，于是"加了检查却忘了
		# 更新棘轮表"会让下限慢慢变成摆设——实测 15 个文件全部漂了 1～11 条，
		# 其中 `overworld_controller` 差 11 条（等于白送 11 条检查的删除额度）。
		# 口径同 `MIN_ASSERTIONS` 的 `SLACK_LIMIT`：**加了检查就把它贴上去**，
		# 而不是把上限抬高。余量给 2 条，免得"改一行注释顺手多写一句"就红。
		check_true(
			count <= floor_count + VERIFICATION_FLOOR_SLACK,
			"%s 的检查涨到 %d 条、棘轮表还写着 %d——把 floor 贴到 %d（余量上限 %d）"
				% [path, count, floor_count, count, VERIFICATION_FLOOR_SLACK]
		)
	# 文档里写的"N 个真实场景自检"也得对得上——同 183 的 Q 范围、185 的看板数字一个家族：
	# 场景自检加一个，那两句就变成假话，而它们写在**交接文档**和**一页看板**上，最容易被当真。
	var count_re := RegEx.new()
	count_re.compile("(\\d+)\\s*个真实场景自检")
	var docs_checked := 0
	# README 也收进来（2026-10-04）：它是**仓库的门面**，而这一轮给它补验收说明时
	# 顺手写下的是「11 个」——那数字早就长到 13 了，却没有任何门限看得见。
	for path: String in ["AGENTS.md", "docs/dev/当前状态.md", "README.md"]:
		var text := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in count_re.search_all(text):
			docs_checked += 1
			check_eq(
				int(hit.get_string(1)), selftest_scenes.size(),
				"%s 里的「%s」与实际自检场景数（%d 个）不符——加了自检场景就要同步这句"
					% [path, hit.get_string(0), selftest_scenes.size()]
			)
	check_gt(float(docs_checked), 0.0, "至少在一份文档里写明了真实场景自检的个数（扫到 %d 处）" % docs_checked)


## 35. 「当前状态」看板与「设计实现对照」里写死的表数／行数，必须与真表一致。
##
## 由来（2026-10-03）：那一行写着「37 张表 / 566 行」——**同一行里还写着"条数以运行输出为准，
## 别在这里写死"，而它自己就是写死的**。表一加、行一改，这句立刻变成假话，而且没人会红。
## 183 治的是 Q 编号、182 治的是代码里的 id 清单，这是同一家族的第三处：**文档里的动态数字**。
##
## 2026-10-04 扩到第二份文档：`设计实现对照.md` 那句一直停在「37 张表」——**它偏偏是接手的人
## 拿来问"还缺什么"的那份清单**，头一句就写着过期的规模，比不写更糟（同一份文件里
## 「`character_base` 1 行」也停在 0.9.0 之前）。两份文档用同一个正则量，而且**各至少要出现一次**
## ——措辞被删掉也要红，别让声称悄悄消失。
##
## 数字从哪来：`TableDb` 载 `data/generated`（验收前刚由构建期重建过），
## 与 build_tables 打印的「N 张表 / M 行」**同源同算法**，不手抄。
func _check_dashboard_numbers() -> void:
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
	for path: String in [STATUS_PATH, CONTRAST_PATH]:
		_check_table_count_claim(path, claim_re, table_count, row_count)
	_check_character_base_row_claim(db)


## 「N 张表 / M 行」这句在两份文档里各要出现一次，且数字都要等于实载。
func _check_table_count_claim(path: String, claim_re: RegEx, table_count: int, row_count: int) -> void:
	var text := FileAccess.get_file_as_string(path)
	check_false(text.is_empty(), "读得到 %s" % path)
	var hits := claim_re.search_all(text)
	check_gt(
		float(hits.size()), 0.0,
		"%s 里写明了「N 张表 / M 行」（扫到 %d 处）" % [path, hits.size()]
	)
	for hit: RegExMatch in hits:
		check_eq(
			int(hit.get_string(1)), table_count,
			"%s 写的表数 %s 与实载 %d 张不符" % [path, hit.get_string(1), table_count]
		)
		check_eq(
			int(hit.get_string(2)), row_count,
			"%s 写的行数 %s 与实载 %d 行不符（加／删表行后要同步这句）"
				% [path, hit.get_string(2), row_count]
		)


## 「设计实现对照」里还有一处写死表行数的地方（01 那格的 `character_base` N 行）。
## 它一直写着 1，而 0.9.0 起那张表就是 5 行——同一家族、同一手法量。
func _check_character_base_row_claim(db) -> void:
	var contrast := FileAccess.get_file_as_string(CONTRAST_PATH)
	check_false(contrast.is_empty(), "读得到 %s" % CONTRAST_PATH)
	var claim_re := RegEx.new()
	claim_re.compile("`character_base`\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*行")
	var hit := claim_re.search(contrast)
	check_not_null(hit, "对照表里写了「`character_base` N 行」（措辞改了也要连着改这条门限）")
	if hit == null:
		return
	var actual: int = db.rows("character_base").size()
	check_eq(
		int(hit.get_string(1)), actual,
		"对照表写 character_base %s 行，数据里是 %d 行" % [hit.get_string(1), actual]
	)


## 56. 设计侧「交接单」里的表数与逐表行数，必须与真表一致。
##
## 由来（2026-10-04）：`docs/design/交接单.md` 是设计交付的**汇总页**，开头就写着
## 「程序从这里看就够了」——可它 §一 那句一直停在 **0.23.0 的「54 张表 / 848 行」**，
## 现实是 **64 张 / 1023 行**；正文里 `enemy_skill`（28 行）／`story_node`（3 个）／
## 「`dialogue_node`／`dialogue_option` 已建骨架（1／3 行）」也全是 0.29.0 之前的旧数，
## §〇 那句「Q1–Q65」停在 Q78 之前、账册旗标还写着改名前的 `flag_ledger_to_pei` 一族。
## **读它的人照 §一 去核对会白找**——与 185（看板数字）、194（步数）、205（段数）同一家族，
## 只是这一份在 `docs/design/` 下，以前**没有任何门限**盖着。
##
## 口径：只扫**计数**写法——「N 张表 / M 行」与 `` `表名`（N 行… ``；数字从 `TableDb` 现算，
## 不手抄。「＋1 行」「3 处」这类**改动量**不在此列（那是当时的 diff，不是现状）。
## **一段都扫不到也要红**：别让声称悄悄消失，留下一份看起来干净、其实再没人核对的页。
##
## 为什么不直接并进 185（`_check_dashboard_numbers`）：那两份是**开发侧**文档，
## 措辞删掉就该红；这一份**归设计侧**，设计重写本页时可以换措辞——
## 但**只要写了，就必须是真的**。
func _check_handover_sheet_numbers() -> void:
	var text := FileAccess.get_file_as_string(HANDOVER_PATH)
	check_false(text.is_empty(), "读得到 %s" % HANDOVER_PATH)
	if text.is_empty():
		return
	var db := TableDbScript.new()
	db.load_all()
	check_eq(db.errors.size(), 0, "TableDb 能载全（%s）" % "; ".join(db.errors))
	var table_count := db.tables.size()
	var row_count := 0
	for table_name: String in db.tables.keys():
		var rows: Array = db.tables[table_name].get("rows")
		row_count += rows.size()
	var scanned := 0
	var total_re := RegEx.new()
	total_re.compile("(\\d+)\\s*张表\\s*/\\s*(\\d+)\\s*行")
	for hit: RegExMatch in total_re.search_all(text):
		scanned += 1
		check_eq(
			int(hit.get_string(1)), table_count,
			"交接单写的表数 %s 与实载 %d 张不符（加／删表后要同步这一句）"
				% [hit.get_string(1), table_count]
		)
		check_eq(
			int(hit.get_string(2)), row_count,
			"交接单写的行数 %s 与实载 %d 行不符（加／删表行后要同步这一句）"
				% [hit.get_string(2), row_count]
		)
	var row_re := RegEx.new()
	row_re.compile("`([a-z][a-z0-9_]*)`\\s*（\\s*(\\d+)\\s*行")
	for hit: RegExMatch in row_re.search_all(text):
		var name := hit.get_string(1)
		if not db.tables.has(name):
			fail("交接单把 `%s` 当表名写着行数，但 data/tables 里没有这张表" % name)
			continue
		var actual: int = (db.tables[name].get("rows") as Array).size()
		scanned += 1
		check_eq(
			int(hit.get_string(2)), actual,
			"交接单写 `%s` 有 %s 行，数据里是 %d 行" % [name, hit.get_string(2), actual]
		)
	check_gt(float(scanned), 0.0, "交接单里扫到了计数声称（一句都没有的话，这条门限就白设了）")


## 57. 美术清单里的图标数（15 §4.3 与《交接单（美术）》）必须与表行数一致。
##
## 由来（2026-10-04）：美术交接单第 9 行写着「**图标 160 个**（武学 65／装备 27／物品 19／
## 增益 19／异常 4／派生 28／地图标记 8）」——**这两组数自己就对不上**（列出来的七个类目合计 **170**），
## 15 §4.3 的标题也还停在「约 160 个」。图标数是**排期量**：每加一部武学／一件装备它就变，
## 而**没有任何门限会红**——185 量表数行数、194 量步数、205 量段数、350 量交接单；
## 这是同一家族的第五处，也是第一次量**类目内部的和**。
##
## 口径两条：
##   ① 15 §4.3 那张表逐行读「数量」与「来源表」（表名写在反引号里）——数量必须等于那张表的行数，
##      标题括号里的总数必须等于各行之和；
##   ② 美术交接单里「N 个（…）」的括号内成分之和必须等于它写出来的总数（括号**外**的解释文字不算，
##      所以「不含第 10 行那 8 张」这种注解可以放心写在后面）。
## 两条都不许「一处都没扫到」就算过。
func _check_art_icon_counts() -> void:
	var db := TableDbScript.new()
	db.load_all()
	check_eq(db.errors.size(), 0, "TableDb 能载全（%s）" % "; ".join(db.errors))
	var spec := FileAccess.get_file_as_string(ART_SPEC_PATH)
	check_false(spec.is_empty(), "读得到 %s" % ART_SPEC_PATH)
	var head_re := RegEx.new()
	head_re.compile("###\\s*4\\.3\\s*图标（(\\d+)\\s*个）")
	var head := head_re.search(spec)
	check_not_null(head, "15 §4.3 的标题里写了图标总数（措辞改了要连着改这条门限）")
	var body := spec
	if head != null:
		body = spec.substr(head.get_end())
		var next_head := body.find("\n### ")
		if next_head > 0:
			body = body.substr(0, next_head)
	# ① 逐类目：数量 == 来源表的行数，总数 == 各行之和
	var row_re := RegEx.new()
	row_re.compile(
		"^\\|\\s*([^|]+?)\\s*\\|\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*\\|\\s*`([a-z][a-z0-9_]*)`"
	)
	var counted := 0
	var total := 0
	for raw: String in body.split("\n"):
		var hit := row_re.search(raw)
		if hit == null:
			continue
		var label := hit.get_string(1).replace("*", "").strip_edges()
		var claimed := int(hit.get_string(2))
		var table_name := hit.get_string(3)
		counted += 1
		total += claimed
		if not db.tables.has(table_name):
			fail("15 §4.3 拿 `%s` 当来源表，但 data/tables 里没有这张表" % table_name)
			continue
		var actual: int = (db.tables[table_name].get("rows") as Array).size()
		check_eq(
			claimed, actual,
			"15 §4.3 写「%s」这一档有 %d 个图标，而 `%s` 有 %d 行——表长了就要连着算一遍图标量"
				% [label, claimed, table_name, actual]
		)
	check_gt(float(counted), 5.0, "15 §4.3 扫到了足够多的类目（%d 行）" % counted)
	if head != null:
		check_eq(
			int(head.get_string(1)), total,
			"15 §4.3 标题写 %s 个图标，而表里各档合计 %d 个" % [head.get_string(1), total]
		)
	# ② 美术交接单：「N 个（各档…）」的总数 == 括号内之和
	var sheet := FileAccess.get_file_as_string(ART_HANDOVER_PATH)
	check_false(sheet.is_empty(), "读得到 %s" % ART_HANDOVER_PATH)
	var bundle_re := RegEx.new()
	bundle_re.compile("(\\d+)\\s*个\\*{0,2}\\s*（([^）]*)）")
	var num_re := RegEx.new()
	num_re.compile("\\d+")
	var bundles := 0
	for hit: RegExMatch in bundle_re.search_all(sheet):
		var parts := num_re.search_all(hit.get_string(2))
		if parts.size() < 3:
			continue     # 「（原生 32×32，头肩）」这种不是成分清单
		bundles += 1
		var inner_total := 0
		for part: RegExMatch in parts:
			inner_total += int(part.get_string(0))
		check_eq(
			int(hit.get_string(1)), inner_total,
			"美术交接单写「%s」，括号里各成分合计 %d——两组数自己就打起来了"
				% [hit.get_string(0).replace("*", ""), inner_total]
		)
	check_gt(float(bundles), 0.0, "美术交接单里扫到了「N 个（成分…）」的计数声称")


## 58. 设计文档里「某张表 N 行／N 件／N 个」的声称，抽成一张小表逐条对账。
##
## 由来（2026-10-04）：350／351 各修一处之后，把**同一手法**（能算的数就现算，绝不手抄）
## 扩大到别的设计文档，一次又抓到**四处**——而且全在地编／美术照着排期的那两份上：
##
##   · 15 §4.2 补「NPC 头像 **数量 7**（`npc_def`）」——0.29.0 加了沈雁回，`npc_def` 已经 **8** 行
##     （15 §4.3 与美术交接单早就是 8，只有这一格没改）；
##   · 07 §8.5「`equip_base` 目前没有 `icon` 列，**24 件**装备」——实际 **27** 行；
##   · 07 §8.2 标题「按 `enemy_base.csv` **14 行**」——练功木桩／石隙猎犬／游方弟子 加进来后是 **17** 行；
##   · 20 号 §十二「`dialogue_node`／`dialogue_option`（各 **1／3** 行）」——已经长到 **8／16**。
##
## 老问题一模一样：**表在长，数字留在原地**；而这四处写的是"要画多少张""有几个角色"，
## 是排期量。**没有一条门限看得见**（185 量看板、350 量交接单、351 量图标类目）。
##
## 口径：每条写死「文档 + 正则（只抓数，表名写在 pairs 里）+ 说明」；正则**抓不到也红**
## （措辞改了要连着改这条门限，别让它悄悄失效）。**带日期的叙事行不算**——行里出现
## 「当时／以前／当年」就跳过（同 `_looks_historical`，20 号那句「当时刚建骨架（各 1／3 行）」
## 记的是 0.29.0 的状态，不该拿现状口径去量）。
const ROW_COUNT_CLAIMS := [
	{
		"path": "res://docs/design/15_美术风格需求.md",
		"re": "\\|\\s*数量\\s*\\|\\s*(\\d+)\\s*（\\s*`npc_def`",
		"pairs": [[1, "npc_def"]],
		"why": "15 §4.2 补「NPC 头像 数量 N（`npc_def`）」",
	},
	{
		"path": "res://docs/design/07_地图资源需求.md",
		"re": "`equip_base`[^\\n]{0,40}?，\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*件装备",
		"pairs": [[1, "equip_base"]],
		"why": "07 §8.5「`equip_base` …N 件装备」",
	},
	{
		"path": "res://docs/design/07_地图资源需求.md",
		"re": "按\\s*`enemy_base\\.csv`\\s*(\\d+)\\s*行",
		"pairs": [[1, "enemy_base"]],
		"why": "07 §8.2 标题「按 `enemy_base.csv` N 行」",
	},
	{
		"path": "res://docs/design/20_第一章剧情.md",
		"re": "各\\s*\\*{0,2}(\\d+)／(\\d+)\\*{0,2}\\s*行",
		"pairs": [[1, "dialogue_node"], [2, "dialogue_option"]],
		"why": "20 号 §十二「`dialogue_node`／`dialogue_option` 各 N／M 行」",
	},
	{
		"path": "res://docs/design/交接单_美术.md",
		"re": "NPC 头像\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*张",
		"pairs": [[1, "npc_def"]],
		"why": "美术交接单第 10 行「NPC 头像 N 张」",
	},
]


func _check_design_doc_row_counts() -> void:
	var db := TableDbScript.new()
	db.load_all()
	check_eq(db.errors.size(), 0, "TableDb 能载全（%s）" % "; ".join(db.errors))
	for spec: Dictionary in ROW_COUNT_CLAIMS:
		var path := str(spec["path"])
		var text := FileAccess.get_file_as_string(path)
		check_false(text.is_empty(), "读得到 %s" % path)
		if text.is_empty():
			continue
		var re := RegEx.new()
		re.compile(str(spec["re"]))
		var found := 0
		for raw: String in text.split("\n"):
			if _looks_historical(raw):
				continue
			for hit: RegExMatch in re.search_all(raw):
				found += 1
				for pair: Array in spec["pairs"]:
					var claimed := int(hit.get_string(int(pair[0])))
					var table_name := str(pair[1])
					var actual: int = (db.tables[table_name].get("rows") as Array).size()
					check_eq(
						claimed, actual,
						"%s：%s 写了 %d，而 `%s` 有 %d 行——表长到哪儿，这句就要跟着到哪儿"
							% [path.get_file(), str(spec["why"]), claimed, table_name, actual]
					)
		check_gt(float(found), 0.0, "%s 抓不到「%s」——改措辞要连着改这条门限" % [path.get_file(), str(spec["why"])])


## 61. 设计文档里点名的「表.列／表.行」必须真在表里——**改列名最容易把这种指针写死**。
##
## 由来（2026-10-04）：0.14.0 把 `npc_guard.peace_condition` 拆成三列，那时设计文档里
## 那些**带点号**的引用就成了指向不存在的东西的指针（读的人会去找一个没有的列）。
## 这一轮做「同名列互盖」扫查时顺手把它量了：`docs/design/*.md`（CHANGELOG 除外——那是带日期的
## 叙事）里带点号的引用共 **135 处**，**134 处**是真列名或真行值，只剩一处是**计划中还没落的行**
## （20 号 §九 写的 `npc_quest.nq_yan_01`）。
##
## 口径：只认 `\b<表名>.<标识符>` 这种**带点号**的写法。设计写"将来才有的列"本来就不该带点
## （写「给 `event_check` 加一列 `hp_cost`」，别写 `event_check.hp_cost`），所以这条不会误伤计划。
## `csv` 后缀（`item_base.csv`）不算引用；计划中的行写进 `PENDING_DOC_REFS`，**双向维护**。
const PENDING_DOC_REFS := [
	## 20 号 §九 那张支线表要新增的行（表还没落，见 `待策划确认.md` Q72／Q73）
	"npc_quest.nq_yan_01",
]


func _check_design_doc_refs() -> void:
	var db := TableDbScript.new()
	db.load_all()
	check_eq(db.errors.size(), 0, "TableDb 能载全（%s）" % "; ".join(db.errors))
	var ref_re := RegEx.new()
	ref_re.compile("\\b([a-z][a-z0-9_]*)\\.([A-Za-z][A-Za-z0-9_]*)")
	var checked := 0
	var missing := PackedStringArray()
	var pending_seen := PackedStringArray()
	for path: String in _collect_files(["docs/design"]):
		if not path.ends_with(".md") or path.ends_with("CHANGELOG.md"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		for hit: RegExMatch in ref_re.search_all(text):
			var table_name := hit.get_string(1)
			var token := hit.get_string(2)
			if token == "csv" or not db.tables.has(table_name):
				continue
			checked += 1
			var key := "%s.%s" % [table_name, token]
			if PENDING_DOC_REFS.has(key):
				pending_seen.append(key)
				continue
			if _table_has_token(db, table_name, token):
				continue
			missing.append("%s 里的 %s" % [path.get_file(), key])
	check_gt(float(checked), 50.0, "设计文档里扫到足够多的「表.列／表.行」引用（%d 处）" % checked)
	check_eq(
		missing.size(), 0,
		"这些引用既不是列名也不是任何一行的值（列被改名了？还是写错了？）：%s" % "；".join(missing)
			+ "——计划中才有的行列进 PENDING_DOC_REFS 并写明等谁；将来才有的列**不要带点号**写"
	)
	# 计划清单双向维护：行真的落了之后，这里要红，提示删掉那一条
	for key: String in PENDING_DOC_REFS:
		if not pending_seen.has(key):
			check_true(false, "PENDING_DOC_REFS 里的 %s 已经没有引用了，把这一条删掉" % key)
			continue
		var parts := key.split(".")
		check_false(
			parts.size() == 2 and _table_has_token(db, parts[0], parts[1]),
			"PENDING_DOC_REFS 里的 %s 已经落表了，把这一条删掉（清单过期 = 下一个人照着它白找）" % key,
		)


## `token` 是不是这张表的**列名**（CSV 表头）或**任何一行的值**（宽松口径：宁可漏一个笔误，
## 也不要因为设计文档里写了某个非主键的取值而假红——例：`drop_table.drop_chest_silver` 是"掉落组"的值，
## 不是那一行的主键）。
func _table_has_token(db, table_name: String, token: String) -> bool:
	if db.get_row(table_name, token) != null:
		return true
	var header := FileAccess.get_file_as_string("res://data/tables/%s.csv" % table_name).split("\n")
	if not header.is_empty():
		for column: String in header[0].split(","):
			if column.strip_edges().trim_prefix("\uFEFF") == token:
				return true
	for row: Resource in db.rows(table_name):
		for prop: Dictionary in row.get_property_list():
			if int(prop.get("usage", 0)) & PROPERTY_USAGE_SCRIPT_VARIABLE == 0:
				continue
			if str(row.get(str(prop.get("name")))) == token:
				return true
	return false


## 「设计实现对照」那张「待补充的表（…）」列在括号里的表名，**一个都不许已经在 `data/tables` 里**。
##
## 由来（2026-10-04）：那一格写着「`equip_upgrade` / `dialogue_node`／`dialogue_option` /
## `craft_recipe` / `quest_board` / `ng_plus_config`）……五张表都不存在」——**六个名字、五个数**，
## 而且 `dialogue_node`／`dialogue_option` 0.31.0 就落表了（对话容器那条链已经在跑）。
## 这是「缺口清单自己过期」的又一例：**读它的人会去找一张已经存在的表**。
##
## 口径：只认那一格**括号里**的表名（说明里可以自由提别的表），所以不会误伤"提到某张现有的表"的行。
func _check_missing_table_claims() -> void:
	var contrast := FileAccess.get_file_as_string(CONTRAST_PATH)
	check_false(contrast.is_empty(), "读得到 %s" % CONTRAST_PATH)
	var slot := contrast.find("待补充的表（")
	check_gt(float(slot), 0.0, "对照表里那张「待补充的表（…）」还在（改标题要连着改这条门限）")
	if slot < 0:
		return
	var tail := contrast.substr(slot + len("待补充的表（"))
	var close := tail.find("）")
	check_gt(float(close), 0.0, "「待补充的表（…）」后面有右括号收口")
	if close < 0:
		return
	var listed := tail.substr(0, close)
	var db := TableDbScript.new()
	db.load_all()
	var token_re := RegEx.new()
	token_re.compile("`([a-z][a-z0-9_]*)(?:\\.csv)?`")
	var stale := PackedStringArray()
	for hit: RegExMatch in token_re.search_all(listed):
		var name := hit.get_string(1)
		if db.tables.has(name):
			stale.append("%s（表已存在）" % name)
	check_eq(
		stale.size(), 0,
		"对照表把已经建好的表列进「待补充的表」了（读的人会去找一张已经存在的表）：%s"
			% ", ".join(stale)
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
##   ③ 三份现状文档里写的 `Q<起>–<止>`，止必须等于真实最大编号
##      （2026-10-04 补第三份：设计侧 `交接单.md` 那句「Q1–Q65」也一直没人量）。
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
	for path: String in [STATUS_PATH, QUESTIONS_PATH, HANDOVER_PATH]:
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
					# 0.23.0 把毒酒放进杂货铺（隐藏线「毒杀毒手」的钥匙，300 文限量 1 件）——
					# 设计 05 的「材料＋白板饰品」是常规商品口径，钥匙道具是**故意**摆在这里的。
					if item != null and str(item.item_type) == "key":
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
	"weapon_type_name_of": "查询", "team_drop_groups": "查询",
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


##
## 「真窗口冒烟」那一份清单必须与总命令**逐条一致**——两份 .bat 各抄了一份场景表，
## 而它们谁也不看谁：新加一个场景自检只补了 `run_all_checks.bat`，冒烟就会**静默少跑一个**，
## 而两边都还是"全 OK"（文档里还写着"同一份清单"）。这正是决策 200 想防的事，却一直靠人抄两遍。
func _check_windowed_smoke_matches_runner() -> void:
	var runner := _collect_scene_calls("tools/run_all_checks.bat")
	var smoke := _collect_scene_calls("tools/run_windowed_smoke.bat")
	check_gt(float(runner.size()), 5.0, "总命令里扫到足够多的场景自检（%d 条）" % runner.size())
	check_gt(float(smoke.size()), 5.0, "真窗口冒烟里扫到足够多的场景自检（%d 条）" % smoke.size())
	var missing := PackedStringArray()
	for entry: String in runner:
		if not smoke.has(entry):
			missing.append(entry)
	check_eq(
		missing.size(), 0,
		"这些场景自检在 tools/run_all_checks.bat 里有、但 tools/run_windowed_smoke.bat 里没有"
			+ "（新加自检要两份都补，否则冒烟静默少跑一个、两边还都是绿的）：%s" % "、".join(missing)
	)
	var extra := PackedStringArray()
	for entry: String in smoke:
		if not runner.has(entry):
			extra.append(entry)
	check_eq(extra.size(), 0, "这些场景自检只在真窗口冒烟里（总命令漏了？）：%s" % "、".join(extra))
	# 两个清单里的场景路径都必须真的存在（改名只改了一边时，这条会先响）
	var bad_paths := PackedStringArray()
	for entry: String in Array(runner) + Array(smoke):
		var scene := str(entry).split("|")[1]
		if not FileAccess.file_exists(scene) and not bad_paths.has(scene):
			bad_paths.append(scene)
	check_eq(bad_paths.size(), 0, "两份清单里指向不存在的场景：%s" % "、".join(bad_paths))


## 从一份 .bat 里扫出 `check_scene.bat "名字" "场景" "开关"` 的三元组（拼成 `名字|场景|开关`）。
## **按行扫并跳过 `rem`／`::` 注释行**——第一版是全文本正则，把注释掉的那一行也数了进去，
## 于是"注释掉一个场景自检"这条最容易被忽略的改法**照样是绿的**（反向验证时当场发现）。
func _collect_scene_calls(path: String) -> PackedStringArray:
	var regex := RegEx.new()
	regex.compile("check_scene\\.bat\"\\s+\"([^\"]+)\"\\s+\"([^\"]+)\"\\s+\"([^\"]+)\"")
	var found := PackedStringArray()
	var text := FileAccess.get_file_as_string("res://" + path)
	for raw: String in text.split("\n"):
		var line := raw.strip_edges()
		if line.begins_with("rem ") or line.begins_with("rem\t") or line.begins_with("::"):
			continue
		var hit := regex.search(line)
		if hit == null:
			continue
		found.append("%s|%s|%s" % [hit.get_string(1), hit.get_string(2), hit.get_string(3)])
	return found


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


## 65. 「枚举三处同改」的第三处（`tools/validate_tables.ps1`）也得能看见——`ENUMS` 里每个枚举列，
## PS1 至少要提到那一列。
##
## 由来（2026-10-04）：`data/AGENTS.md` 写着「改枚举要三处一起改：06 数据字典／`table_validator.ENUMS`／
## PS1」，而 `ENUMS` 的 41 个枚举列里**有 7 列的列名在整个 PS1 里一次都没出现过**——也就是说
## **设计侧那道网根本不查它们**：`building_def.building_type`／`drop_table.roll_type`／
## `item_base.use_context`／`map_local.scene_type`／`map_region.node_type`／`talent_def.category`／
## `weapon_type_def.default_element`。后果不静默（构建期那道网会红），但**设计侧自己跑的时候看不见**，
## 而他们交接口径恰恰是「这条每次都是绿的」。这一轮把那 7 条补成了真检查。
##
## 口径：**只要求列名在 PS1 里出现**。这是条**名字级棘轮**，不是证明——所以规矩写在这里：
## 新加枚举列时，**要么给 PS1 补一条检查，要么写进 `PS1_ENUM_EXEMPT` 并说明为什么这边不查**。
const PS1_ENUM_EXEMPT := {
	# 今天没有豁免。示例：某列只有引擎 API 能算（PS1 是纯文本校验、没有引擎），就写进这里并说明。
}


func _check_ps1_mentions_enum_columns() -> void:
	var ps1 := FileAccess.get_file_as_string("res://tools/validate_tables.ps1")
	check_false(ps1.is_empty(), "读得到 tools/validate_tables.ps1")
	# **去掉注释**再找：第一版是全文匹配，于是"把列名写在注释里"就能把这条门限糊过去——
	# 反向验证（删掉 `weapon_type_def.default_element` 那条真检查）时当场发现：只剩我自己那句
	# "这一轮补了哪 7 列" 的注释还留着，于是它照样绿。注释里提一嘴不算查过。
	var code := ""
	for raw: String in ps1.split("\n"):
		code += str(raw.split("#")[0]) + "\n"
	var enums: Dictionary = TableValidatorScript.ENUMS
	check_gt(float(enums.size()), 10.0, "ENUMS 里读到足够多的表（%d 张）" % enums.size())
	var checked := 0
	var missing := PackedStringArray()
	for table_name: String in enums.keys():
		var columns: Dictionary = enums[table_name]
		for column: String in columns.keys():
			checked += 1
			var key := "%s.%s" % [table_name, column]
			if PS1_ENUM_EXEMPT.has(key):
				continue
			if not code.contains(column):
				missing.append(key)
	check_gt(float(checked), 30.0, "扫到足够多的枚举列（%d 个）" % checked)
	check_eq(
		missing.size(), 0,
		"这些枚举列在 `TableValidator.ENUMS` 里有、但 `tools/validate_tables.ps1` 里一个字都没提"
			+ "（「枚举三处同改」的第三处漏了，设计侧跑网时看不见）：%s" % "、".join(missing)
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
## 「被要求的旗标」必须有人能点亮——把 Q51 那一族断链变成机器门限。
##
## 由来（三次都是人工踩出来的）：落雁坡那枚 flag_luoyanpo_met／毒堂那枚 flag_poison_hall／
## 荒村那枚 flag_huangcun_done 一度**一个来源都没有**，于是三位同伴永远入不了队；
## flag_qiutu_saved 也一样（钱大夫的委托永远交不了）。这类断链**表里看不出来**：
## 「要求」写在一处、「来源」在另一处或根本不存在，两边单独看都是合法的。
##
## 要求来自七处：recruit_def.join_condition／guide_step.condition／
## story_node.trigger_condition／chapter_def.complete_condition／
## dialogue_node.condition／dialogue_option.condition／npc_quest.requirement，
## 条件语言按 & 拆开后取 flag_*。
##
## 来源三类：
##   ① 表里——对话选项的 set_flag、event_check／hidden_trigger 的 reward_type=event 的
##      reward_id、以及 story_node(kind=chapter_end) 会置**本章**的 complete_condition；
##   ② 代码里——set_flag("flag…") 那种字面量，以及「const NAME := "flag…" 且 set_flag(NAME)
##      真的被调用过」的常量形式（GuideService 那几枚就是）；
##   ③ 格式化写法——set_flag("flag_%s_joined" % …) 这种当**模式**匹配（招募链）。
##
## **已知缺口**写在 KNOWN_FLAG_GAPS：补上就要把那行删掉，否则门限会红
## （「缺口清单过期」和「新断链」一样要有人管，与 PENDING_COST_CHECKS 同一套做法）。
const KNOWN_FLAG_GAPS := [
	## 落雁坡旧镖车位点还没摆（地编的活，见 20 号 §十二）
	"flag_luoyanpo_met",
	## 伪装混入（号衣＋腰牌）整套还没做——设计侧的 Q7 还开着
	"flag_bd_uniform",
]


func _check_flag_reachability() -> void:
	var db = get_db()
	var required := PackedStringArray()
	for rule: Array in [
		["recruit_def", "join_condition"], ["guide_step", "condition"],
		["story_node", "trigger_condition"], ["chapter_def", "complete_condition"],
		["dialogue_node", "condition"], ["dialogue_option", "condition"],
		["npc_quest", "requirement"],
	]:
		_collect_required_flags(db, str(rule[0]), str(rule[1]), required)
	check_gt(float(required.size()), 3.0, "扫到足够多「被要求的旗标」（%d 枚）" % required.size())

	var produced := PackedStringArray()
	for row: Resource in db.rows("dialogue_option"):
		_note_flag(produced, str(row.set_flag))
	for table_name: String in ["event_check", "hidden_trigger"]:
		for row: Resource in db.rows(table_name):
			if str(row.reward_type) == "event":
				_note_flag(produced, str(row.reward_id))
	for row: Resource in db.rows("story_node"):
		if str(row.kind) != "chapter_end":
			continue
		var chapter: Resource = db.get_row("chapter_def", str(row.chapter_id))
		if chapter != null:
			_note_flag(produced, str(chapter.complete_condition))
	_collect_code_producers(produced)

	var missing := PackedStringArray()
	for flag_id: String in required:
		if _flag_produced(produced, flag_id) or KNOWN_FLAG_GAPS.has(flag_id):
			continue
		missing.append(flag_id)
	check_eq(
		missing.size(), 0,
		"每一枚被要求的旗标都有人能点亮（没人管的：%s）——要么补来源、要么写进 KNOWN_FLAG_GAPS 并写明等谁"
			% "、".join(missing)
	)
	# 缺口清单**双向**维护：补上了就要把那一行删掉（过期清单 = 下一个人照着它白找）
	var stale := PackedStringArray()
	for flag_id: String in KNOWN_FLAG_GAPS:
		if _flag_produced(produced, flag_id):
			stale.append(flag_id)
	check_eq(stale.size(), 0, "已知缺口清单里这些已经有来源了，请删掉对应行：%s" % "、".join(stale))


## 条件语言里被**要求**的旗标（按 & 拆；只看 flag_*，origin: / item: 那些不是旗标）
func _collect_required_flags(db, table_name: String, column: String, out: PackedStringArray) -> void:
	if not db.tables.has(table_name):
		return
	for row: Resource in db.rows(table_name):
		# 条件语言的三种连接符都要拆：`&` 全部满足、`|` 任一满足、`!` 取反。
		# `!flag_x` 里被**要求**的仍然是 `flag_x`（要求它"没被点亮"也是要求），照收。
		for group: String in str(row.get(column)).replace("|", "&").split("&", false):
			var token := group.strip_edges()
			while token.begins_with("!"):
				token = token.substr(1).strip_edges()
			if token.begins_with("flag_"):
				_note_flag(out, token)


func _note_flag(out: PackedStringArray, flag_id: String) -> void:
	var token := flag_id.strip_edges()
	if token.begins_with("flag_") and not out.has(token):
		out.append(token)


## 代码里的生产者。**故意算「提到过」而不是做数据流**：GDScript 侧读不了 AST，
## 能做的是「这个字面量在代码里被 set 过」；只被读、从没被 set 的旗标因此会漏网
## ——那种情况由「谁要求它」那一侧自己的用例管。
## **口径与限度**：GDScript 侧读不了 AST，做不了「到底谁 set」的数据流，所以这里用
## 「这个 flag 字面量在 src/ 里出现过」当门限——`TEAM_WIN_FLAGS` 这种**装在常量表里**的写法
## （`set_flag(str(TEAM_WIN_FLAGS[team_id]))`）只有这样才认得出来。
## 代价是「只被读、从没被 set」的旗标会漏网；那类只能由「谁要求它」那一侧自己的用例管。
## 真正要抓的是**全项目没人提**的那种（`flag_luoyanpo_met`／`flag_poison_hall`／
## `flag_huangcun_done`／`flag_qiutu_saved` 四条当初都是这样）。
##
## 另有一条**已知的假阴性**：`src/` 里把旗标写在**自检函数**里也算数
## （例：`npc_panel` 的自检手动置过 `flag_qiutu_saved`）。所以真要把「这条链有没有来源」
## 钉死，还得在**那条链自己的用例**里断言（`test_event_check` 就钉了「掷中＝救出来了」
## ——反向验证过：把来源改字，那条会红）。
func _collect_code_producers(out: PackedStringArray) -> void:
	var literal_re := RegEx.new()
	literal_re.compile("[\"'](flag_[a-zA-Z0-9_%]+)[\"']")
	for path: String in _collect_files(["src"]):
		var text := FileAccess.get_file_as_string("res://" + path)
		if text.is_empty():
			continue
		for hit: RegExMatch in literal_re.search_all(text):
			_note_literal(out, hit.get_string(1))


## 带 % 的字面量（如 flag_%s_joined）当模式存下来，比对时按前后缀匹配
func _note_literal(out: PackedStringArray, literal: String) -> void:
	var token := literal.strip_edges()
	if token.is_empty() or out.has(token):
		return
	if token.contains("%"):
		out.append(token)
		return
	_note_flag(out, token)


func _flag_produced(produced: PackedStringArray, flag_id: String) -> bool:
	if produced.has(flag_id):
		return true
	for entry: String in produced:
		if not entry.contains("%"):
			continue
		var parts: PackedStringArray = entry.split("%s")
		if parts.size() != 2:
			continue
		if flag_id.begins_with(parts[0]) and flag_id.ends_with(parts[1]):
			return true
	return false


## 20 号 §九 的十条支线表 ↔ npc_quest：**好感那一列必须对得上**。
##
## 为什么单挑这一列：它是那张表里**唯一能被机器读的数值**（＋20 那种）；
## 「奖励」那一列是散文（「草药汤 ＋ 旧信（燕小七线）」），机器比不了——散文那半由设计侧自己看。
##
## 2026-10-04 第一次跑就抓到两处对不上：陈氏 20→**25**、孙掌柜 10→**20**（且漏了草药汤），
## 都按**设计那张表**改了数据——设计文档是较新的口径，数据是早先落的。
##
## 例外写在 SIDEQUEST_FORCE_SKIP：**张贵**那条按设计是「图只可偷」（走 of_zhang_treasure），
## 本来就没有 npc_quest 行；招募那四条归 recruit_def，也不在这张表里。
const SIDEQUEST_FORCE_SKIP := ["张贵", "燕小七", "林铁山", "白清和", "苏九娘"]


func _check_sidequest_favors() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/20_第一章剧情.md")
	check_false(doc.is_empty(), "读得到 20 号剧情文档")
	var db = get_db()
	# 只扫 §九 那张表：表头是「| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |」
	var start := doc.find("| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |")
	check_true(start >= 0, "找得到 §九 的十条支线表")
	if start < 0:
		return
	var body := doc.substr(start)
	var rows := 0
	for raw_line: String in body.split("\n"):
		var line := raw_line.strip_edges()
		if line.is_empty() or not line.begins_with("|"):
			break
		var cells := line.split("|")
		if cells.size() < 8:
			continue
		var owner := cells[3].strip_edges()
		if owner == "归属" or owner.begins_with("---"):
			continue
		rows += 1
		if SIDEQUEST_FORCE_SKIP.has(owner):
			continue
		var wanted := PackedInt32Array()
		for piece: String in cells[7].strip_edges().replace("＋", "").split("／", false):
			var digits := ""
			for ch: String in piece:
				if ch.is_valid_int():
					digits += ch
			if not digits.is_empty():
				wanted.append(int(digits))
		check_gt(float(wanted.size()), 0.0, "§九 里「%s」那条写了好感" % owner)
		var npc_id := _npc_id_for_display_name(db, owner)
		check_true(not npc_id.is_empty(), "「%s」按中文名在 npc_def 里找得到" % owner)
		if npc_id.is_empty():
			continue
		var got := PackedInt32Array()
		for quest: Resource in db.rows_where("npc_quest", "npc_id", npc_id):
			got.append(int(quest.reward_favor))
		for value: int in wanted:
			check_true(
				got.has(value),
				"§九 说「%s」这条给 +%d 好感，他的委托好感却是 %s——两边对不上就有一边写错了"
					% [owner, value, str(got)],
			)
	check_gt(float(rows), 5.0, "§九 表里解析出足够多的支线（%d 条）" % rows)


func _npc_id_for_display_name(db, name_cn: String) -> String:
	for row: Resource in db.rows("npc_def"):
		if str(row.name_cn) == name_cn:
			return str(row.npc_id)
	return ""


## 47. 对照表里当卡点引用的 **Q 编号必须是"还活着的问题"**。
##
## 由来（2026-10-04）：Q1（毒酒从哪来）与 Q2（6 件良品装从哪来）都被设计后续补上了来源，
## 我把它们划掉、登记成跳号；可 `设计实现对照.md` 里**当时还有行拿它们当卡点**——
## 那意味着接手的人会照着一条已经解决的问题白找一遍（和 185 的"过期的数字"、`KNOWN_FLAG_GAPS`
## 的"过期缺口清单"是同一家族：**清单本身也会过期，而过期清单比不写更糟**）。
##
## 口径：只看 ⛔／🟡 的行（图例那四行不算），扫里面的 `Q\d+`；被划掉的 Q 写成 `| ~~Q1~~ |`，
## 行首正则 `^\| Q1(?!\d)` 自然匹配不到 → 就算"不是活问题"。跳号登记那行（`Q1、Q2、Q28`）
## 不是行首 `| Q1`，也不会被误判成活问题。
func _check_blocker_citations_are_live() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/dev/设计实现对照.md")
	var questions := FileAccess.get_file_as_string(QUESTIONS_PATH)
	check_false(doc.is_empty() and questions.is_empty(), "读得到对照表与问题清单")
	var legend := [
		"| 🟡 部分 |", "| ⛔ 未实现·等设计 |", "| ⛔ 未实现·等地编 |", "| ✅ 已完成 |",
	]
	var q_re := RegEx.new()
	q_re.compile("Q(\\d+)")
	var cited := 0
	var stale := PackedStringArray()
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
		for hit: RegExMatch in q_re.search_all(line):
			var qid := int(hit.get_string(1))
			cited += 1
			var live_re := RegEx.new()
			live_re.compile("(?m)^\\| Q%d(?!\\d)" % qid)
			if live_re.search(questions) == null and not stale.has("Q%d" % qid):
				stale.append("Q%d" % qid)
	check_gt(float(cited), 10.0, "对照表的 ⛔／🟡 行里扫到足够多的 Q 引用（%d 处）" % cited)
	check_eq(
		stale.size(), 0,
		"对照表里这些 Q 已经不是活问题了（被回答／划掉就更新那一行）：%s" % "、".join(stale)
	)


## 48. 看板里写死的「特殊效果 id 共 N 条」必须等于数据里的真实条数。
##
## 由来（2026-10-04）：这条一直写「共 8 条：装备 4 件＋内功 4 部」，而验收里的 `[效果]` 警告
## **每次都现数**——实际是装备 6 ＋ 内功 5 ＝ **11**（多出来的是铁匠印记／药王符与 0.26 新增的
## 「遗篇·归元」）。**表格在长，填数字的地方不长**：这类"文档引用一个能算出来的数"最容易被漏，
## 而它偏偏写在给策划看的那一行上（策划会按 8 条去估工作量）。
##
## 口径：数字从**数据**现算（`equip_base.special_effect` ＋ `skill_passive.passive_effect` 非空行数），
## 与看板那行的 `共 N 条` 比。措辞被改掉也要红（"这一行没写条数"）——**别让声称悄悄消失**。
func _check_special_effect_count_claim() -> void:
	var db = TableDbScript.new()
	db.load_all()
	var equip_rows := 0
	for row: Resource in db.rows("equip_base"):
		if not str(row.special_effect).strip_edges().is_empty():
			equip_rows += 1
	var passive_rows := 0
	for row: Resource in db.rows("skill_passive"):
		if not str(row.passive_effect).strip_edges().is_empty():
			passive_rows += 1
	var actual := equip_rows + passive_rows
	check_gt(float(actual), 5.0, "数据里填了效果 id 的条数（装备 %d ＋ 内功 %d ＝ %d）" % [
		equip_rows, passive_rows, actual,
	])
	var status := FileAccess.get_file_as_string(STATUS_PATH)
	var claim_re := RegEx.new()
	claim_re.compile("「特殊效果 id」全是悬空的\\*{0,2}（\\*{0,2}共 (\\d+) 条")
	var hit := claim_re.search(status)
	check_not_null(hit, "看板里那行写了「特殊效果 id 全是悬空的（共 N 条）」（措辞改了也要连着改这条门限）")
	if hit != null:
		check_eq(
			int(hit.get_string(1)), actual,
			"看板写「共 %s 条」，数据里是 %d 条（装备 %d ＋ 内功 %d）——行数变了就同步这一句"
				% [hit.get_string(1), actual, equip_rows, passive_rows]
		)
	# 同一句话在「设计实现对照」里还有一份（`B2` 那张「等设计补」表写着「共 N 条悬空 id」）——
	# 它一直停在 8，复核订正到 11 之后两边要一起走，否则接手的人会照旧数错（决策 318／319 的续）。
	var contrast := FileAccess.get_file_as_string(CONTRAST_PATH)
	check_false(contrast.is_empty(), "读得到 %s" % CONTRAST_PATH)
	var contrast_re := RegEx.new()
	contrast_re.compile("共\\s*\\*{0,2}(\\d+)\\*{0,2}\\s*条悬空 id")
	var contrast_hit := contrast_re.search(contrast)
	check_not_null(contrast_hit, "对照表里写了「共 N 条悬空 id」（措辞改了也要连着改这条门限）")
	if contrast_hit != null:
		check_eq(
			int(contrast_hit.get_string(1)), actual,
			"对照表写「共 %s 条悬空 id」，数据里是 %d 条（装备 %d ＋ 内功 %d）"
				% [contrast_hit.get_string(1), actual, equip_rows, passive_rows]
		)


## 51. 套装的**每一档**必须真的凑得出来——按「已经发得出来的来源」算。
##
## 由来（2026-10-04）：5.19（PS1 那条可达性审计）只查「单件／单部有没有来源」，查不出**组合**。
## `set_xuanwei_sword` 的「三招／五招」要 3／5 部玄微剑法，而 `sk_xuanwei_02／03／04` 三部全是
## `source_type=npc`（门派对话没实现）——**拿得到的只有起手与点星＝2 部**；
## `set_xuanwei_qi` 的「七格」要 7 点占格，而**拿得到的**三部只占 6 格（1＋2＋3，另两部也是 npc）。
## 表里配了、`buff_def` 也配了、代码也认，**玩家把能拿的全拿了也亮不起来**——
## 这类「这一档是死内容」以前没有任何地方会报：单件都是"有来源"的，只是凑不齐。
##
## 口径（与 `tools/validate_tables.ps1` 5.19b **同一条规则**，改一处要改两处）：
##   equip         来源按 5.19 那几路（掉落／货架／起始／事件与隐藏奖励／敌人身上／NPC 兑换／代码点名），
##                 并且**按槽位**算上限——同槽位最多 `equip_slot_def.max_equip` 件（戒指两枚）
##   skill_active   招式数 = 来源已实现的成员数（`SkillGrant.IMPLEMENTED_SOURCES` 是唯一真相）
##   skill_passive  格数 = 来源已实现的成员 `slot_cost` 之和
##
## 已知缺口写在 KNOWN_UNREACHABLE_SET_TIERS（**双向**维护：补上了就把那行删掉，
## 否则门限会红——「缺口清单过期」和「新断链」一样要有人管，与 KNOWN_FLAG_GAPS 同一套做法）。
const KNOWN_UNREACHABLE_SET_TIERS := [
	## 三招／五招：02／03／04 三部都是 source_type=npc（门派对话没实现，`待策划确认.md` Q3）
	"set_xuanwei_sword|3",
	"set_xuanwei_sword|5",
	## 七格：拿得到的只有引气（1 格，开局）＋周天（2 格，剧情）＋太清（3 格，剧情）＝6 格
	"set_xuanwei_qi|7",
]


func _check_set_tier_reachability() -> void:
	var db = get_db()
	var obtainable := _obtainable_ids(db)
	var reachable_skills := _reachable_skill_ids(db)
	var dead := PackedStringArray()
	var tiers := 0
	for def: Resource in db.rows("set_def"):
		var set_id := str(def.set_id)
		var kind := str(def.set_kind)
		var members := PackedStringArray()
		for member_row: Resource in db.rows("set_member"):
			if str(member_row.set_id) == set_id:
				members.append(str(member_row.member_id))
		if members.is_empty():
			continue
		var reach_cap := 0
		if kind == "equip":
			var per_slot: Dictionary = {}
			for member: String in members:
				if not obtainable.has(member):
					continue
				var equip: Resource = db.get_row("equip_base", member)
				var slot := str(equip.slot) if equip != null else ""
				per_slot[slot] = int(per_slot.get(slot, 0)) + 1
			for slot: String in per_slot:
				var limit := 1
				var slot_row: Resource = db.get_row("equip_slot_def", slot)
				if slot_row != null:
					limit = int(slot_row.max_equip)
				reach_cap += mini(int(per_slot[slot]), limit)
		elif kind == "skill_active":
			for member: String in members:
				if reachable_skills.has(member):
					reach_cap += 1
		elif kind == "skill_passive":
			for member: String in members:
				if not reachable_skills.has(member):
					continue
				var passive: Resource = db.get_row("skill_passive", member)
				if passive != null:
					reach_cap += int(passive.slot_cost)
		for tier: Resource in db.rows("set_bonus"):
			if str(tier.set_id) != set_id:
				continue
			tiers += 1
			if reach_cap >= int(tier.required_count):
				continue
			dead.append("%s|%d" % [set_id, int(tier.required_count)])
	check_gt(float(tiers), 4.0, "扫到足够多的套装档位（%d 档）" % tiers)
	var unexpected := PackedStringArray()
	for key: String in dead:
		if not KNOWN_UNREACHABLE_SET_TIERS.has(key):
			unexpected.append(key)
	check_eq(
		unexpected.size(), 0,
		"这些套装档位按已实现的来源凑不出来（要么补来源、要么写进 KNOWN_UNREACHABLE_SET_TIERS 并写明等谁）：%s"
			% "、".join(unexpected)
	)
	# 缺口清单**双向**维护：设计把来源补上之后，这里会红，提示删掉那一行
	var stale := PackedStringArray()
	for key: String in KNOWN_UNREACHABLE_SET_TIERS:
		if not dead.has(key):
			stale.append(key)
	check_eq(stale.size(), 0, "已知缺口清单里这些档位已经凑得出来了，请删掉对应行：%s" % "、".join(stale))


## 「已经发得出来」的 id 集合——与 `validate_tables.ps1` 5.19 同一套通道，再加**代码里点名的 id**
## （剧情物由代码发，如 `battle_screen.TEAM_WIN_ITEMS` 那本账册）。
func _obtainable_ids(db) -> Dictionary:
	var out: Dictionary = {}
	for table_name: String in ["drop_table", "shop_stock", "npc_offer"]:
		for row: Resource in db.rows(table_name):
			var key := str(row.item_id)
			if key != "":
				out[key] = true
	for row: Resource in db.rows("enemy_equip"):
		out[str(row.equip_id)] = true
	for row: Resource in db.rows("world_event"):
		# `gift` 的 effect_id 才是物品（`trade` 是货架组、`spar` 是队伍）
		if str(row.effect_kind) == "gift" and str(row.effect_id) != "":
			out[str(row.effect_id)] = true
	for table_name: String in ["event_check", "hidden_trigger"]:
		for row: Resource in db.rows(table_name):
			var key := str(row.reward_id)
			if key != "":
				out[key] = true
	for row: Resource in db.rows("character_base"):
		for equip_id in str(row.start_equip_ids).split("|", false):
			out[equip_id.strip_edges()] = true
	var equip_ids := PackedStringArray()
	for row: Resource in db.rows("equip_base"):
		equip_ids.append(str(row.equip_id))
	for path: String in _collect_files(["src"]):
		var text := FileAccess.get_file_as_string("res://" + path)
		if text.is_empty():
			continue
		for equip_id: String in equip_ids:
			if text.contains(equip_id):
				out[equip_id] = true
	return out


## 来源已实现的武学 id。`SkillGrant.IMPLEMENTED_SOURCES` 是唯一真相（别在这里再列一遍），
## 外加 `start`——起手武学从角色模板或创建界面的「起始武学」来，**都是玩家能拿到的**
## （`pf_xuanwei_01`／`sk_common_01` 没进任何模板，但自定义路线可以选，所以算拿得到）。
func _reachable_skill_ids(db) -> Dictionary:
	var out: Dictionary = {}
	for row: Resource in db.rows("skill_base"):
		var source := str(row.source_type)
		if source == "start" or SkillGrantScript.IMPLEMENTED_SOURCES.has(source):
			out[str(row.skill_id)] = true
	return out


## 52. 「设计实现对照」里两处**能从数据算出来的**数字：判定条数、武学来源未实现的比例。
##
## 由来（2026-10-04，决策 336）：B0 那节写着「16 / **52** 部武学」——`skill_base` 早就是 65 行；
## 「判定不要卡在主线必经路上」那行写着「**12** 条判定」，实际有 14 条（0.29.0 加了 `ev_shen_rescue`）。
## 两个数都能算：判定数 = `event_check` 行数；武学分母 = `skill_base` 行数、分子 = `source_type`
## 还是 npc／shop（没有来源）的条数。**以前只靠人记，漂了没人知道**（与决策 185／319 同一家族）。
##
## 口径：只扫**现状文档**（`设计实现对照.md`），并跳过**历史叙事**的行（含「以前／当时／当年」）——
## 那些是回放，拿现状口径去量它会假红（同决策 183 对 `框架说明.md` 的处理）。
func _check_contrast_audit_numbers() -> void:
	var db = get_db()
	var check_rows: int = db.rows("event_check").size()
	var skill_total: int = db.rows("skill_base").size()
	var skill_pending := 0
	for row: Resource in db.rows("skill_base"):
		if str(row.source_type) in ["npc", "shop"]:
			skill_pending += 1
	check_gt(float(check_rows), 10.0, "载到足够多的判定（%d 条）" % check_rows)
	check_gt(float(skill_total), 40.0, "载到足够多的武学（%d 部）" % skill_total)
	var text := FileAccess.get_file_as_string(CONTRAST_PATH)
	check_false(text.is_empty(), "读得到 %s" % CONTRAST_PATH)
	var claim_re := RegEx.new()
	claim_re.compile("(\\d+)\\s*条判定")
	var ratio_re := RegEx.new()
	ratio_re.compile("(\\d+)\\s*/\\s*(\\d+)\\s*部武学")
	var check_claims := 0
	var ratio_claims := 0
	for raw: String in text.split("\n"):
		var line := raw.strip_edges()
		for hit: RegExMatch in claim_re.search_all(line):
			# **历史叙事不拿现状口径去量**：同一行里这个数**前面**已经写了「以前／当时／当年」就跳过
			# （表格行常常前半句是现状、后半句是回放，整行跳过会把现状那半也漏掉——第一版就这么写的）。
			if _looks_historical(line.substr(0, hit.get_start())):
				continue
			check_claims += 1
			check_eq(
				int(hit.get_string(1)), check_rows,
				"对照表写「%s 条判定」，event_check 里是 %d 条——数变了就同步这一句"
					% [hit.get_string(1), check_rows]
			)
		for hit: RegExMatch in ratio_re.search_all(line):
			if _looks_historical(line.substr(0, hit.get_start())):
				continue
			ratio_claims += 1
			check_eq(
				int(hit.get_string(1)), skill_pending,
				"对照表写「%s / … 部武学」，而 `source_type` 还是 npc／shop 的是 %d 部（分子变了要跟着改）"
					% [hit.get_string(1), skill_pending]
			)
			check_eq(
				int(hit.get_string(2)), skill_total,
				"对照表写「… / %s 部武学」，而 `skill_base` 有 %d 行（分母变了要跟着改）"
					% [hit.get_string(2), skill_total]
			)
	check_gt(float(check_claims), 0.0, "对照表里写了「N 条判定」（措辞改了也要连着改这条门限）")
	check_gt(float(ratio_claims), 0.0, "对照表里写了「N / M 部武学」（措辞改了也要连着改这条门限）")


func _looks_historical(prefix: String) -> bool:
	return prefix.contains("以前") or prefix.contains("当时") or prefix.contains("当年")


## 53. **划掉的条目（`~~…~~`）正文里不许再留下「还没做」的字样。**
##
## 由来（2026-10-04，决策 338）：A3（精英 `marker_elite_red` 贴图）**图在 0.14.0 就交付、代码在决策 304
## 也接了线**（`roaming_enemy._make_elite_glow()` 按 `elite_marker` 的 id 拼路径加载），可两份清单里
## A3 那行**标题划掉了、正文还写着「代码侧仍是程序化光晕，换图属开发侧接线——需要时提一句就换」**——
## 读它的人会把一件做完的事当成待办。**「划掉」与「正文说没做」自相矛盾，而没有任何门限会红。**
##
## 口径：四份现状／清单文档里凡是**行内有划线**的，先把划线片段（`~~…~~`）摘掉再查「待办字样」——
## 旧措辞**划掉就放过**（那是明确撤回），留在正文里才算矛盾。词表写死在这里，加词要连着改这条注释。
const STRUCK_PENDING_WORDS := ["开发侧接线", "要开发侧", "需要时提一句", "还没接", "未接线", "仍是程序化"]


func _check_struck_entries_are_closed() -> void:
	var struck_re := RegEx.new()
	struck_re.compile("~~[^~]*~~")
	var checked := 0
	var hits := PackedStringArray()
	for file_name: String in ["待策划确认.md", "地图搭建说明.md", "设计实现对照.md", "当前状态.md"]:
		var path := "res://docs/dev/%s" % file_name
		var text := FileAccess.get_file_as_string(path)
		check_false(text.is_empty(), "读得到 %s" % path)
		if text.is_empty():
			continue
		var line_no := 0
		for raw: String in text.split("\n"):
			line_no += 1
			if not raw.contains("~~"):
				continue
			checked += 1
			var body := struck_re.sub(raw, "", true)
			for word: String in STRUCK_PENDING_WORDS:
				if body.contains(word):
					hits.append("%s:%d「%s」…%s" % [file_name, line_no, word, body.strip_edges().substr(0, 50)])
					break
	check_gt(float(checked), 5.0, "扫到足够多的划线条目（%d 行）" % checked)
	check_eq(
		hits.size(), 0,
		"这些「划掉了」的条目正文还在说要开发侧做（自相矛盾：要么把正文改成已完成，要么把旧措辞也划掉）：%s"
			% "；".join(hits)
	)


## 54. **文本文件里不许有控制字符**——`反引号 + 字母` 被当成转义吃掉，留下的就是这些字节。
##
## 由来（2026-10-04，决策 339）：`待策划确认.md` 里躺着 **17 个控制字节**，全是这个坑：
## PowerShell 的双引号串里 `` `e ``＝ESC（0x1B）、`` `f ``＝换页（0x0C）、`` `a ``＝响铃（0x07），
## 而它**连前一个反引号一起**吃掉了：`` `event_check` `` 变成 `<ESC>vent_check`、`` `fail_note` `` 变成
## `<FF>ail_note`、`` `assets `` 变成 `<BEL>ssets`。读出来就是「**`ote 写的是…**」「**`ffect_id**」
## 这种残句，markdown 的代码片段还会**跨词配对**、整段渲染错位；`性能观测.md` 1 处、`验证清单.csv` 2 处，
## 而**任何门限都不看字节**——它就这么躺了好几轮。
##
## 口径：`docs/`／`src/`／`data/`／`tools/`／`tests/`／`scenes/` 下的文本文件（按扩展名）逐字节扫，
## 除 `\t`／`\n`／`\r` 外不许出现任何 `< 0x20` 的字节。`addons/` 是第三方、`.logs/` 是运行产物，都不扫。
func _check_no_stray_control_bytes() -> void:
	var files: PackedStringArray = _collect_files(["docs", "src", "data", "tools", "tests", "scenes"])
	check_gt(float(files.size()), 200.0, "扫到足够多的文本文件（%d 个）" % files.size())
	var hits := PackedStringArray()
	for path: String in files:
		var bytes := FileAccess.get_file_as_bytes("res://" + path)
		var found: Dictionary = {}
		for index in bytes.size():
			var value := int(bytes[index])
			if value < 0x20 and value != 0x09 and value != 0x0A and value != 0x0D:
				found[value] = int(found.get(value, 0)) + 1
		if found.is_empty():
			continue
		var parts := PackedStringArray()
		for value: int in found.keys():
			parts.append("0x%02X×%d" % [value, int(found[value])])
		hits.append("%s（%s）" % [path, "、".join(parts)])
	check_eq(
		hits.size(), 0,
		"这些文本文件里有控制字符——多半是「反引号 ＋ 字母」被当成转义吃掉了"
			+ "（PowerShell 里 `` ` ``e＝ESC／`` ` ``f＝换页／`` ` ``a＝响铃；连反引号一起没了，读出来就是「`ote」这种残句）：%s"
			% "；".join(hits)
	)


## 59. 文档里**TAB 后面紧跟一个 ASCII 字母**＝`` `t `` 被吃掉留下的残渣（339 的视野外）。
##
## 由来（2026-10-04）：339 把控制字节扫了一遍，但白名单里放行了 `\t`／`\n`／`\r`——缩进要用。
## 结果**同一个坑的另一种样子**一直躺在四份开发文档里：PowerShell 双引号串里 `` `t ``＝TAB，
## 于是 `` `tools\check_maps.bat` `` 被吃成 `<TAB>ools\check_maps.bat`、`` `trigger()` `` 变成 `<TAB>rigger()`、
## `` `table_with_n_chars(4)` `` 变成 `<TAB>able_with_n_chars(4)`（`docs/dev/待策划确认.md` ×2、
## `当前状态.md`、`框架说明.md`、`性能观测.md` ×2，共 6 处）。反引号连同那个字母一起没了，
## **读出来就是「tools\check_maps.bat」这种残句**，而任何门限都不看它。
##
## 口径：`docs/**/*.md` 与根目录 `AGENTS.md`／`README.md` 里，TAB 后面紧跟 ASCII 字母就算错。
## 为什么这条判据不漏：文档里合法的 TAB 只有两种用法——整行缩进（后面跟的是中文、
## ASCII-art 的 `│`／`└`／`▲`，或另一个 TAB）与代码块里的对齐，**没有一种会让 TAB 紧跟一个词的首字母**。
## 真写了「tab 缩进的代码块」也会被拦下来——那改成空格就好（文档里的代码块一律空格缩进）。
## `tools/*.bat` 里那种行首 TAB 缩进不受影响：这条只扫 `.md`。
func _check_no_escape_eaten_tabs() -> void:
	var files: PackedStringArray = _collect_files(["docs"])
	files.append("AGENTS.md")
	files.append("README.md")
	var tab_re := RegEx.new()
	tab_re.compile("\\t[A-Za-z]")
	var hits := PackedStringArray()
	var scanned := 0
	for path: String in files:
		if not path.ends_with(".md"):
			continue
		var text := FileAccess.get_file_as_string("res://" + path)
		if text.is_empty():
			continue
		scanned += 1
		var line_no := 0
		for raw: String in text.split("\n"):
			line_no += 1
			var hit := tab_re.search(raw)
			if hit == null:
				continue
			var at := hit.get_start()
			var head := raw.substr(maxi(0, at - 16), 16)
			var tail := raw.substr(at + 1, 18)
			hits.append("%s:%d「…%s<TAB>%s…」" % [path.get_file(), line_no, head, tail])
	check_gt(float(scanned), 10.0, "扫到了足够多的文档（%d 份）" % scanned)
	check_eq(
		hits.size(), 0,
		"这些行里 TAB 紧跟字母——是「反引号 ＋ 字母」被当成转义吃掉的残渣（339 的 TAB 版，"
			+ "反引号与那个字母都没了，读出来是残句）：%s" % "；".join(hits)
	)

