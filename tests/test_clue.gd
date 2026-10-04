## 线索本：隐藏内容与事件判定的线索来源、完成状态（03_副本_黑风寨.md「线索必须能被找到」）。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const ClueServiceScript := preload("res://src/core/clue_service.gd")
const CLUE_SCENE := "res://scenes/clue_screen.tscn"


func suite_name() -> String:
	return "线索本"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var clues = ClueServiceScript.new(db, state)
	_check_scene_clues(db, state, clues)
	_check_region_clues(db, clues)
	_check_status_follows_save(state, clues)
	_check_clue_source_parsing(db)
	_check_long_list_ui(db, state)
	_check_panel_scene(db, state)


## 线索来源的解析口径：分号分隔、**空段丢掉**、每段去首尾空白。
## 这条是「每条隐藏至少两条线索」那道设计门限的底座——要是空段被当成一条，
## 「只有一条;」这种写法就会被机器数成两条而**假绿**。行类的 `clues()` 用 `split(";", false)` + `strip_edges()`。
func _check_clue_source_parsing(db) -> void:
	var base: Resource = db.get_row("hidden_trigger", "trig_wine")
	check_not_null(base, "拿一条真实隐藏内容当模板")
	if base == null:
		return
	var row: Resource = base.duplicate(true)
	row.clue_source = "  甲说的一句话  ;;;乙说的一句话;"
	var parsed: PackedStringArray = row.clues()
	check_eq(parsed.size(), 2, "空段丢掉：只剩两条（%s）" % str(parsed))
	if parsed.size() == 2:
		check_eq(str(parsed[0]), "甲说的一句话", "第 1 条去掉首尾空白")
		check_eq(str(parsed[1]), "乙说的一句话", "第 2 条去掉首尾空白")
	row.clue_source = ""
	check_eq(Array(row.clues()).size(), 0, "空字段就是没有线索来源")
	row.clue_source = "只有一条"
	check_eq(Array(row.clues()).size(), 1, "单条写法就是一条（门限会点它名）")

	var event_base: Resource = db.get_row("event_check", "ev_gamble")
	check_not_null(event_base, "事件判定表也拿一行当模板")
	if event_base != null:
		var event_row: Resource = event_base.duplicate(true)
		event_row.clue_source = "甲;乙;;"
		check_eq(Array(event_row.clues()).size(), 2, "事件判定的线索来源走同一套解析")


## 长列表：把黑风寨的隐藏内容复制成 6 倍（36 条隐藏 + 5 条事件 = 41 条），
## 面板要**逐条建行**、不许悄悄截断。真实数据只有 8 条左右，这种规模靠当前内容到不了——
## 而「列表被截断」在界面上看起来只是「少了几条」，很难被发现。
func _check_long_list_ui(db, state) -> void:
	if scene_tree == null:
		fail("没有注入场景树，线索本长列表用例无法进行")
		return
	var wide = table_with_duplicated_triggers(db, "scene_heifengzhai", 6)
	var clues = ClueServiceScript.new(wide, state)
	var expected: int = clues.clues_for_scene("scene_heifengzhai").size()
	check_eq(expected, 41, "6 倍复制后是 36 条隐藏 + 5 条事件")
	var screen = load(CLUE_SCENE).instantiate()
	screen.db_override = wide
	screen.state_override = state
	screen.scope = "scene"
	screen.scene_id = "scene_heifengzhai"
	scene_tree.root.add_child(screen)
	screen.setup()
	screen.select_section("hidden")
	check_eq(screen.section_row_count("hidden"), expected, "每一行都建出来了（%d 行）" % screen.section_row_count("hidden"))
	var subtitle: Label = screen.find_child("Subtitle", true, false)
	check_not_null(subtitle, "有「共 N 条」的小标题")
	if subtitle != null:
		check_true(subtitle.text.contains("共 41 处"), "小标题写清总数：%s" % subtitle.text)
	check_eq(screen.section_row_count("hidden"), expected, "带名字的行数也对得上")
	scene_tree.root.remove_child(screen)
	screen.free()


## 一张图的线索：隐藏内容 + 事件判定，每条都要有线索来源
func _check_scene_clues(db, state, clues) -> void:
	var entries: Array = clues.clues_for_scene("scene_heifengzhai")
	check_eq(entries.size(), 11, "黑风寨 6 条隐藏 + 5 条事件判定")
	var hidden := 0
	var events := 0
	for entry: Dictionary in entries:
		if str(entry["kind"]) == "hidden":
			hidden += 1
		else:
			events += 1
		check_false(Array(entry["clues"]).is_empty(), "%s 有用得上的线索" % str(entry["name"]))
		var text: String = clues.describe(entry)
		check_true(text.contains("线索："), "%s 的文案写清线索：%s" % [str(entry["id"]), text])
		check_true(text.contains("未完成"), "新档都还没完成：%s" % str(entry["id"]))
	check_eq(hidden, 6, "黑风寨本图 6 条隐藏（后山密道那条属于塌陷山洞）")
	check_eq(events, 5, "黑风寨 5 条事件判定（0.29.0 加了 `ev_shen_rescue`：地牢里救沈雁回）")

	# 后山密道的线索在塌陷山洞那张图里
	var cave: Array = clues.clues_for_scene("scene_cave")
	# （石碑那条事件的 scene_id 是空的、按 region_id 归到大地图，所以在 region 视角里）
	check_eq(cave.size(), 1, "塌陷山洞小地图只有 1 条隐藏（后山密道）")
	var dig_found := false
	for entry: Dictionary in cave:
		if str(entry["id"]) == "trig_dig":
			dig_found = true
			check_true(str(entry["note"]).contains("后山"), "线索本用表里的 note 说明这是什么：%s" % str(entry["note"]))
			check_eq(Array(entry["clues"]).size(), 2, "后山密道有两条线索")
	check_true(dig_found, "后山密道归在塌陷山洞")

	# 三火盆那条明说「缺位点做不了」，不是静悄悄消失
	var unsupported := 0
	for entry: Dictionary in entries:
		if bool(entry.get("unsupported", false)):
			unsupported += 1
			check_true(clues.describe(entry).contains("缺地图位点"), "做不了的条目要说明：%s" % clues.describe(entry))
	check_eq(unsupported, 1, "七类里只剩三火盆（sequence）缺位点")


## 大地图线索按地标归组
func _check_region_clues(db, clues) -> void:
	var all_entries: Array = clues.region_clues()
	check_eq(all_entries.size(), 9, "野外共 9 条事件判定（落雁坡 3／荒村 1／山洞 1／黑风寨 2／清风驿 1／驿站 1）")
	check_eq(clues.clues_for_region("n_luoyanpo").size(), 3, "落雁坡 3 条")
	check_eq(clues.clues_for_region("n_huangcun").size(), 1, "荒村 1 条")
	check_eq(clues.clues_for_region("n_qingfengyi").size(), 1, "清风驿 1 条（赌局）")
	for entry: Dictionary in clues.clues_for_region("n_luoyanpo"):
		check_eq(str(entry["region_name"]), "落雁坡", "带上的地标名对")
		check_true(not str(entry["source_label"]).is_empty(), "判定来源有名字：%s" % str(entry["source_label"]))


## 完成状态跟着存档走：隐藏看 dungeon_records，事件看 event_checks
func _check_status_follows_save(state, clues) -> void:
	for entry: Dictionary in clues.clues_for_scene("scene_heifengzhai"):
		check_false(bool(entry["done"]), "%s 一开始都是未完成" % str(entry["id"]))
	state.record_dungeon("scene_heifengzhai", "triggers", "trig_wine")
	state.record_event_check("ev_shed_trap", "done")
	# 用本图（scene 作用域）的事件记录做对照；ev_force_gate 是大地图 region 作用域的
	state.record_event_check("ev_poison_identify", "failed")
	var seen := {}
	for entry: Dictionary in clues.clues_for_scene("scene_heifengzhai"):
		seen[str(entry["id"])] = entry
	check_true(bool(seen["trig_wine"]["done"]), "触发过的隐藏内容标完成")
	check_true(bool(seen["ev_shed_trap"]["done"]), "判定成功的事件标完成")
	check_false(bool(seen["ev_poison_identify"]["done"]), "判定失败的不算完成")
	check_true(bool(seen["ev_poison_identify"]["failed"]), "但记着失败过")
	check_true(clues.describe(seen["ev_poison_identify"]).contains("失败过"), "文案也写「失败过」")


## 面板场景：能实例化、按 scope 渲染、能关
func _check_panel_scene(db, state) -> void:
	if scene_tree == null:
		fail("没有注入场景树")
		return
	var panel = load("res://scenes/clue_screen.tscn").instantiate()
	panel.state_override = state
	panel.scope = "scene"
	panel.scene_id = "scene_heifengzhai"
	var closes: Array = []
	panel.return_handler = func() -> void: closes.append(true)
	scene_tree.root.add_child(panel)
	panel.setup()
	# 五类分页（设计 0.32.0）：默认落在「主线」，切到「隐藏」才是本图的线索清单
	var titles: Array = []
	for index in range(panel._tabs.get_tab_count()):
		titles.append(panel._tabs.get_tab_title(index))
	check_eq(titles, ["主线", "支线", "传闻", "角色", "隐藏"], "五类页签的名字与顺序")
	panel.select_section("hidden")
	check_eq(panel.section_id(), "hidden", "切页签后当前页跟着换")
	# 高亮本身看面板自己记的那一份：`--script` 模式下 `TabContainer.current_tab` 不会真正生效
	# （`character_screen` 也踩过这个坑，那边同样是自己记 `_tab_index`）
	check_eq(panel._section_index, 4, "页签高亮（当前页索引）也跟着换")
	check_eq(panel.entry_count(), 11, "面板按条目数渲染：%d" % panel.entry_count())
	check_true(str(panel._subtitle.text).contains("共 11 处"), "小标题写清总数：%s" % str(panel._subtitle.text))
	panel.scope = "region"
	panel.refresh()
	check_eq(panel.entry_count(), 9, "切成野外视角后按地标列 9 条")
	check_true(str(panel._subtitle.text).contains("野外"), "小标题跟着作用域变：%s" % str(panel._subtitle.text))
	panel.press_return()
	check_eq(closes.size(), 1, "离开按钮触发回退")
	scene_tree.root.remove_child(panel)
	panel.free()
