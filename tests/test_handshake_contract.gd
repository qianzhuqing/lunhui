## 策划对接（对接表／验证清单／版本对齐）——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "策划对接（对接表／验证清单／版本对齐）"


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
	_check_verify_tests_are_executed(verify_rows)




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
