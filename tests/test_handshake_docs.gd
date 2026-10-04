## 文档 ↔ 配置表逐格对账（含商店三件）——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "文档 ↔ 配置表逐格对账（含商店三件）"


func run() -> void:
	_check_doc_enums_match_code()
	_check_doc_tables_match_data()
	_check_shop_shelves_match_design()
	_check_code_constants_exist_in_tables()
	_check_code_row_literals_exist()
	_check_data_dictionary_covers_columns()
	_check_hidden_content_table_matches_data()
	_check_shop_table_matches_buildings()
	_check_shop_tabs_have_buttons()




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
					# 2026-10-04（Q3）：`shop` 那 5 部武学改走秘籍道具，四家店各上架一本——
					# 秘籍是这几家店**新增的例外品类**，07 §4.1 那张建筑表已写明。
					if item != null and str(item.item_type) == "skillbook":
						ok = true
					check_true(ok, "杂货铺只卖材料与白板饰品（%s 不符）" % item_id)
				"bld_tavern":
					check_true(
						(item != null and str(item.item_type) == "consumable" \
							and str(item.use_context) == "field") \
							or (item != null and str(item.item_type) == "skillbook"),
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
						(equip != null and str(equip.rarity) == "common") \
							or (item != null and str(item.item_type) == "skillbook"),
						"铁匠铺只卖白板装备（%s 不符）" % item_id,
					)
		# 附加服务：05 那张表里只有医馆带「花钱治疗」
		if building_id == "bld_clinic":
			check_eq(str(building.service_id), "heal", "医馆带治疗服务")
		else:
			check_eq(str(building.service_id), "", "%s 没有附加服务（05 的表里只有医馆有）" % building_id)
	check_gt(float(checked), 10.0, "真正比过的货架条目（%d 条）" % checked)
