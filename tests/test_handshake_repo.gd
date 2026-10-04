## 仓库与文件约定——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "仓库与文件约定"


func run() -> void:
	_check_no_orphan_code()
	_check_ps1_mentions_enum_columns()
	_check_doc_tool_references()
	_check_no_dangling_uids()
	_check_no_orphan_import_files()
	_check_git_repo_contract()
	_check_tables_dir_is_not_imported()
	_check_engine_scripts_scan_logs()
	_check_no_stray_control_bytes()
	_check_no_escape_eaten_tabs()
	_check_file_size()




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






func _check_file_size() -> void:
	var doc_text := FileAccess.get_file_as_string(FILE_SIZE_LIST_PATH)
	check_false(doc_text.is_empty(), "读得到 %s" % FILE_SIZE_LIST_PATH)
	var listed := _file_size_listed_paths(doc_text)
	check_gt(
		float(listed.size()), 0.0,
		"%s 的「现状」表应能解析出文件名（表格式改了就要连着改这条门限）" % FILE_SIZE_LIST_PATH.get_file()
	)
	_check_file_size_parsing()
	for path: String in listed:
		check_true(
			FileAccess.file_exists("res://" + path),
			"%s 在拆分清单里，但文件不存在——路径写错等于开了一格假白名单" % path
		)
	var over_limit := PackedStringArray()
	for path: String in _collect_files(["src", "tests", "tools"]):
		if not path.ends_with(".gd"):
			continue
		var lines := _count_gd_lines("res://" + path)
		if lines <= FILE_SIZE_LIMIT:
			continue
		over_limit.append(path)
		check_true(
			listed.has(path),
			"%s 有 %d 行、超过 %d 行的硬上限——按职责拆细（一批只拆一个、只挪位置不改行为），"
				% [path, lines, FILE_SIZE_LIMIT]
				+ "存量登记与建议拆分轴见 docs/dev/代码拆分清单.md；新增代码不许再加剧"
		)
	for path: String in listed:
		if not FileAccess.file_exists("res://" + path):
			continue
		var lines := _count_gd_lines("res://" + path)
		if lines > FILE_SIZE_LIMIT:
			continue
		check_true(
			false,
			"%s 已经拆到 %d 行（≤ %d），把 `docs/dev/代码拆分清单.md` 里那一行划掉"
				% [path, lines, FILE_SIZE_LIMIT]
				+ "——留着的白名单越攒越大，这条门限就慢慢成了摆设"
		)
	# 清单抬头「量：N 个超标」是最容易过期的一句话（同 185「看板写死的动态数字」一个家族）：
	# 拆完一个，那个 N 也得跟着减，否则读的人会以为还有 N 个没拆。
	var headline_re := RegEx.new()
	headline_re.compile("量：(\\d+) 个超标")
	var headline := headline_re.search(doc_text)
	check_not_null(headline, "拆分清单抬头应写明「量：N 个超标」（措辞改了要连着改这条门限）")
	if headline != null:
		check_eq(
			int(headline.get_string(1)), over_limit.size(),
			"拆分清单抬头的「%s」与实测的超标文件数（%d 个）不符" % [headline.get_string(0), over_limit.size()]
		)




## 从「现状」表里取**第 2 列**（文件列，反引号包起来的路径）。
##
## 只认第 2 列：第 3 列（建议拆分轴）里也写着 `.gd` 路径（如 `tests/handshake/*.gd`），
## 那是**目标**不是现状，一起扫进来会变成一条"文件不存在"的假红。
func _file_size_listed_paths(doc_text: String) -> PackedStringArray:
	var out := PackedStringArray()
	for raw: String in doc_text.split("\n"):
		var line := raw.strip_edges()
		if not line.begins_with("|"):
			continue
		var cells := line.split("|")
		if cells.size() < 4:
			continue
		var name := _first_backticked(cells[2])
		if name.ends_with(".gd"):
			out.append(name)
	return out




## 内存负向用例：这条门限的两种"看着是绿的、其实没在管"的坏法都是解析写错造成的
## （列取错、末尾换行多数一行），所以先钉住解析本身，再去扫真文件。
func _check_file_size_parsing() -> void:
	var sample := "\n".join(PackedStringArray([
		"| 行数 | 文件 | 建议拆分轴 |",
		"|---|---|---|",
		"| 999 | `src/a.gd` | 按检查族拆成 `src/family/*.gd` |",
		"| 900 | `tools/b.gd` | 几何与绘制分开 |",
	]))
	var parsed := _file_size_listed_paths(sample)
	check_eq(parsed.size(), 2, "「现状」表解析出的行数（应只数文件列带 `.gd` 的两行）")
	check_true(parsed.has("src/a.gd"), "解析出第 2 列的反引号路径")
	check_false(parsed.has("src/family/*.gd"), "第 3 列（建议拆分轴）里的 `.gd` 路径不算现状")
	check_eq(_first_backticked("   `src/a.gd`  "), "src/a.gd", "取反引号之间的内容并去空白")
	check_eq(_first_backticked("没有反引号"), "", "没有反引号就取不到（宁可空，也不能猜）")
	# 末尾换行那一行的口径：两种写法都必须数成 2 行，否则台账与门限会互相打脸。
	check_eq(_count_lines_in_text("a\nb\n"), 2, "末尾换行不算一行")
	check_eq(_count_lines_in_text("a\nb"), 2, "没有末尾换行同样是 2 行")
	check_eq(_count_lines_in_text(""), 0, "空文件 0 行")




func _first_backticked(cell: String) -> String:
	var open_at := cell.find("`")
	if open_at < 0:
		return ""
	var close_at := cell.find("`", open_at + 1)
	if close_at < 0:
		return ""
	return cell.substr(open_at + 1, close_at - open_at - 1).strip_edges()




## 数一个脚本的行数。`split` 会把**末尾那个换行**也切成一个空串，直接数就成了 N+1——
## 台账与门限差一行，两边会互相打脸，所以这里显式扣掉。
func _count_gd_lines(path: String) -> int:
	return _count_lines_in_text(FileAccess.get_file_as_string(path))




func _count_lines_in_text(text: String) -> int:
	if text.is_empty():
		return 0
	var lines := text.split("\n")
	if lines.size() > 0 and lines[lines.size() - 1].is_empty():
		lines.remove_at(lines.size() - 1)
	return lines.size()
