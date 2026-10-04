## 接线（自检开关／闭环／冒烟／问题追踪）——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "接线（自检开关／闭环／冒烟／问题追踪）"


func run() -> void:
	_check_scene_selftests_are_wired()
	_check_windowed_smoke_matches_runner()
	_check_every_scene_is_reachable()
	_check_framework_doc_toc()
	_check_designer_questions_cover_gaps()
	_check_blockers_are_tracked()
	_check_question_numbering()
	_check_blocker_citations_are_live()




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
