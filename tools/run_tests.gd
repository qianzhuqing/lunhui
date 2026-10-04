## 自检入口：逐条跑 tests/ 下的用例并汇总退出码。
##
## 用法：
##   godot --headless --path . --log-file .logs/tests.log --script res://tools/run_tests.gd
## 退出码 0 = 全部通过，1 = 有失败（CI 与本地都靠退出码判断）。
extends SceneTree

const SfxScript := preload("res://src/audio/sfx.gd")
const TEST_SCRIPTS := [
	"res://tests/test_table_pipeline.gd",
	"res://tests/test_attribute.gd",
	"res://tests/test_damage.gd",
	"res://tests/test_battle.gd",
	"res://tests/test_battle_fuzz.gd",
	"res://tests/test_drop.gd",
	"res://tests/test_end_to_end.gd",
	"res://tests/test_save_store.gd",
	"res://tests/test_menu.gd",
	"res://tests/test_inventory.gd",
	"res://tests/test_character_sheet.gd",
	"res://tests/test_save_migration.gd",
	"res://tests/test_character_ui.gd",
	"res://tests/test_growth.gd",
	"res://tests/test_skill.gd",
	"res://tests/test_map_assets.gd",
	"res://tests/test_encounter.gd",
	"res://tests/test_battle_session.gd",
	"res://tests/test_overworld.gd",
	"res://tests/test_world_event.gd",
	"res://tests/test_battle_ui.gd",
	"res://tests/test_local_map.gd",
	"res://tests/test_poise.gd",
	"res://tests/test_skill_loadout.gd",
	"res://tests/test_skill_grant.gd",
	"res://tests/test_affix.gd",
	"res://tests/test_shop.gd",
	"res://tests/test_mastery.gd",
	"res://tests/test_status.gd",
	"res://tests/test_dungeon.gd",
	"res://tests/test_multihit.gd",
	"res://tests/test_world_map.gd",
	"res://tests/test_level.gd",
	"res://tests/test_event_check.gd",
	"res://tests/test_cooldown.gd",
	"res://tests/test_clue.gd",
	"res://tests/test_dungeon_ui.gd",
	"res://tests/test_save_timing.gd",
	# 「策划对接守卫」2026-10-04 按检查族拆成八份（每份 ≤ 800 行，见
	# `docs/dev/代码拆分清单.md`）；公共常量与夹具留在 `test_handshake.gd`（它本身不再是 suite）。
	"res://tests/test_handshake_contract.gd",
	"res://tests/test_handshake_repo.gd",
	"res://tests/test_handshake_code.gd",
	"res://tests/test_handshake_docs.gd",
	"res://tests/test_handshake_numbers.gd",
	"res://tests/test_handshake_doccounts.gd",
	"res://tests/test_handshake_wiring.gd",
	"res://tests/test_handshake_flags.gd",
	"res://tests/test_copy_audit.gd",
	"res://tests/test_layout_budget.gd",
	"res://tests/test_buff.gd",
	"res://tests/test_audio.gd",
	"res://tests/test_settings.gd",
	"res://tests/test_guide.gd",
	"res://tests/test_story.gd",
	"res://tests/test_talent.gd",
	"res://tests/test_dialogue.gd",
	"res://tests/test_creation.gd",
	"res://tests/test_overlay_stack.gd",
	"res://tests/test_recruit.gd",
	"res://tests/test_guard.gd",
	"res://tests/test_practice.gd",
	"res://tests/test_enemy_template.gd",
]

## 断言数下限：用例里出现「运行时报错」会让后面的断言静悄悄地不跑，退出码却仍是 0。
## 这个下限把那类假绿变成红。
##
## **基线要贴着现状**：留 10 条余量。
## 2026-10-03 之前这里停在 4366 而实际已经 5318，空了 952 条的窟窿——那次是「有下限」和「没下限」的区别。
## 同一天晚些时候又量了一次：停在 6059、实际 6213，**又空了 154 条**——
## 这次不是"没下限"，而是**下限松到拦不住"删掉一整块断言"**（最小的用例文件也就 15 条断言）。
## 现在贴到 9714−10＝9704（2026-10-04「策划对接守卫」拆成八族之后：有几条门限按
## `tests/*.gd` 逐个文件断言，多出八份 suite 就多出十几条——本条是"余量上限"逼着贴的，
## 见决策 211）。并加了余量上限（见 `SLACK_LIMIT`）：
## 基线再漂就会被自己点名。
## 注意「整个用例文件被摘掉」**不归这条管**：那件事由 `test_handshake._check_verify_tests_are_executed`
## （登记的用例必须在 `TEST_SCRIPTS` 里、否则红）兜着。这条守的是**文件内部的整块断言被删**。
## 数据表行数改动也会带来断言增减（逐行断言的循环）：确认无误后把这一行改到新值——
## 那是**有意识的行为**，不是把下限调松。
const MIN_ASSERTIONS := 9704

## 基线最大余量：实际断言数 − `MIN_ASSERTIONS` 超过它，说明基线松了（这条网在退化成摆设）。
## 有它是因为真发生过两次：`4366 vs 5318`（952 条窟窿）、`6059 vs 6213`（154 条）。
## 触发时**把 `MIN_ASSERTIONS` 贴到 total−10**，而不是把上限调高——上限只是防漂，不是目标。
const SLACK_LIMIT := 100

## 「文件里写了几处断言」vs「实际跑了几条」。
##
## 光有总数下限还不够：某个 `if x != null:` 或空循环里的整段断言从来没执行，总条数照样在涨。
## 所以按**文件**比一次：实际执行数 < 静态断言点，就是有条件分支／空循环没走到（假绿的典型来源）。
## 真有正当例外，写进白名单并说明理由——和技术侧白名单（配表审计、资产审计）同一个思路。
## 注意：实际数可能**大于**静态数（断言写在被循环调用的 helper 里），那种情况不算异常。
const SKIPPED_CHECK_WHITELIST := {}

## 数「断言调用点」：check_xxx( 的出现次数（要求紧跟左括号，避免把说明文字算进去）。
## **故意不算 `fail(`**：那是失败路径的回报口（比如「event_check 在所有地图里都没有落点」），
## 它不执行才说明数据是对的；把它算进来会天天报假绿。
const CHECK_CALL_PATTERN := "check_(?:true|false|eq|ne|float|in_range|gt|lt|not_null|null)\\("


func _initialize() -> void:
	quit(_run())


func _run() -> int:
	print("=== 自检开始 ===")
	var total_checks := 0
	var failures: Array[String] = []
	var skipped_used: Dictionary = {}
	for path: String in TEST_SCRIPTS:
		var script: Script = load(path)
		if script == null or not script.can_instantiate():
			failures.append("%s 无法加载或解析失败" % path)
			continue
		var test_case = script.new()
		test_case.scene_tree = self
		test_case.run()
		total_checks += test_case.checks()
		var static_sites := _count_check_sites(FileAccess.get_file_as_string(path))
		print("--- %s ---（断言 %d／静态 %d）" % [test_case.suite_name(), test_case.checks(), static_sites])
		if test_case.checks() < static_sites and not SKIPPED_CHECK_WHITELIST.has(path):
			failures.append(
				"[假绿] %s 写了 %d 处断言，只跑了 %d 条：有条件分支或空循环没走到。补断言，或把它写进 SKIPPED_CHECK_WHITELIST 并说明理由"
				% [path, static_sites, test_case.checks()]
			)
		elif test_case.checks() < static_sites:
			skipped_used[path] = true
		for message: String in test_case.failures():
			failures.append("[%s] %s" % [test_case.suite_name(), message])

	# 用例都跑完了：把按需挂上去的音效播放层摘掉。`--script` 模式没人回收挂在 root 上的节点，
	# 不退就是一串 `ObjectDB instances were leaked at exit`（真玩时它跟进程同寿，不需要这一步）。
	SfxScript.shutdown()

	# 白名单也要**双向**维护（2026-10-03 补）：条目对应的文件现在不再需要跳过（实际执行数追平了
	# 静态断言点），就说明那条豁免已经过期——留着会让"以后再漏跑断言"没人发现。
	for path: String in SKIPPED_CHECK_WHITELIST:
		if not FileAccess.file_exists(path):
			failures.append("[白名单过期] SKIPPED_CHECK_WHITELIST 里的 %s 不存在了，把这条删掉" % path)
		elif not skipped_used.has(path):
			failures.append(
				"[白名单过期] SKIPPED_CHECK_WHITELIST 里的 %s 已经不需要豁免了（实际执行数不再少于静态断言点），把这条删掉"
				% path
			)

	if failures.is_empty():
		if total_checks < MIN_ASSERTIONS:
			failures.append(
				"断言数 %d 少于基线 %d：有用例中途报错退出了，或用例／数据表行数被删减？"
				% [total_checks, MIN_ASSERTIONS]
				+ "（确认无误就把 tools/run_tests.gd 的 MIN_ASSERTIONS 改到新值——别把基线调松）"
			)
		elif total_checks - MIN_ASSERTIONS >= SLACK_LIMIT:
			failures.append(
				"断言基线松了：实际 %d、基线 %d，余量 %d 已到上限 %d——把它贴到 %d（total−10）。"
					% [total_checks, MIN_ASSERTIONS, total_checks - MIN_ASSERTIONS, SLACK_LIMIT, total_checks - 10]
				+ "余量太大就等于没有下限：删掉一大块断言也不会红（2026-10-03 出现过 952 条的窟窿）"
			)
	if failures.is_empty():
		print("=== 自检通过：%d 条断言 ===" % total_checks)
		# run_tests.bat 以这行标记为准（中途报错就不会打印它，自然判失败）
		print("SELFCHECK: OK (%d assertions)" % total_checks)
		return 0
	printerr("=== 自检失败：%d 条断言中有 %d 条不通过 ===" % [total_checks, failures.size()])
	printerr("SELFCHECK: FAILED (%d failures)" % failures.size())
	for message: String in failures:
		printerr("[FAIL] " + message)
	return 1


## 数一个用例文件里写了多少处断言调用
func _count_check_sites(source: String) -> int:
	var regex := RegEx.new()
	regex.compile(CHECK_CALL_PATTERN)
	var total := 0
	for line: String in source.split("\n"):
		# 注释里举的例子不算断言点（例如「别写 check_true(false, …) 这种」）
		if line.strip_edges().begins_with("#"):
			continue
		total += regex.search_all(line).size()
	return total
