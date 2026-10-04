## 文档计数与引用（含美术需求对账）——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "文档计数与引用（含美术需求对账）"


func run() -> void:
	_check_design_docs_paths_exist()
	_check_asset_questions_match_status()
	_check_completion_rows_symbols_exist()
	_check_design_doc_table_count()
	_check_combat_feel_matches_design()
	_check_art_icon_counts()
	_check_design_doc_row_counts()
	_check_design_doc_refs()
	_check_missing_table_claims()
	_check_struck_entries_are_closed()




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
