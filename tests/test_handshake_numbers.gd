## 文档里写死的数字与计数——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "文档里写死的数字与计数"


func run() -> void:
	_check_no_hardcoded_step_counts()
	_check_loop_segment_count()
	_check_slot_sample_table_matches_formula()
	_check_special_effect_count_claim()
	_check_dashboard_numbers()
	_check_handover_sheet_numbers()
	_check_contrast_audit_numbers()
	_check_verification_check_floors()




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
