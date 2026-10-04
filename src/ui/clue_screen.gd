## 线索本：把隐藏内容与其线索来源摆给玩家看（03_副本_黑风寨.md：「线索必须能被找到」）。
##
## 骨架在 `scenes/clue_screen.tscn`，内容来自 `src/core/clue_service.gd`。
## 入口：小地图里按 K（看本图隐藏内容与事件判定）；大地图按 K（按地标看野外事件）。
## 自检：`-- --clue-selftest`。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const ClueServiceScript := preload("res://src/core/clue_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")
const WorldEventServiceScript := preload("res://src/core/world_event_service.gd")
const UiKitScript := preload("res://src/ui/ui_kit.gd")

var state_override = null
## 用例注入的表库（要在真场景里跑「几十条线索」这种发行数据用不到的配置）
var db_override = null
var return_handler := Callable()
## "scene"（小地图）或 "region"（大地图按地标）
var scope: String = "scene"
var scene_id: String = ""

var db
var state
var clues

var _title: Label
var _subtitle: Label
var _tabs: TabContainer
## 页签 id → 那一页装条目的 VBoxContainer
var _lists := {}
## 页签顺序（取自服务那一处常量，别在这里抄第二份）
var _sections: Array = []
## 页签索引自己记一份：`TabContainer.current_tab` 在 `--script` 自检环境里不会真正生效
## （与 `character_screen` 同一个坑，那边也是这么记的）
var _section_index: int = 0
var _status: Label
var _return_button: Button
var _bound := false


func _ready() -> void:
	setup()
	if _has_user_arg("--clue-selftest"):
		call_deferred("_run_clue_selftest")


func setup() -> void:
	if clues != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	clues = ClueServiceScript.new(db, state)
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


func status_text() -> String:
	return _status.text if _status != null else ""


func entry_count() -> int:
	return section_row_count(section_id())


## 当前页签的 id（"main"／"side"／"rumor"／"cast"／"hidden"）
func section_id() -> String:
	if _sections.is_empty():
		return "hidden"
	return str(_sections[clampi(_section_index, 0, _sections.size() - 1)])


## 某一页建了几行**条目**——用例按页数行，免得全局找 `Clue_*` 把别的页也算进来；
## 只数 `Clue_*`（空页摆的那个提示行不算一行线索）。
## 条目现在挂在卡片的内容列里（0.32.0 照示意图补的卡片结构），所以**按后代递归数**。
func section_row_count(id: String) -> int:
	var box: Node = _lists.get(id)
	if box == null:
		return 0
	return _count_clue_rows(box)


func _count_clue_rows(node: Node) -> int:
	var rows := 0
	for child in node.get_children():
		if str(child.name).begins_with("Clue_"):
			rows += 1
		else:
			rows += _count_clue_rows(child)
	return rows


## 切页签：内容与标题都跟着换（当前页签的高亮由 `TabContainer` 自己画）
func select_section(id: String) -> void:
	var index := _sections.find(id)
	if index < 0:
		return
	_section_index = index
	if _tabs != null and _tabs.current_tab != index:
		_tabs.current_tab = index
	refresh()


func _on_tab_changed(index: int) -> void:
	if index < 0 or index >= _sections.size() or index == _section_index:
		return
	_section_index = index
	refresh()


func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_subtitle = _require_node("Panel/Margin/Column/Subtitle") as Label
	_tabs = _require_node("Panel/Margin/Column/Tabs") as TabContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_return_button.pressed.connect(press_return)
	# 五类分页（设计 0.32.0）：主线／支线／传闻／角色／隐藏
	_sections = Array(ClueServiceScript.SECTION_ORDER)
	for id: String in _sections:
		_lists[id] = _make_tab(str(ClueServiceScript.SECTION_TITLES.get(id, id)))
	_tabs.tab_changed.connect(_on_tab_changed)
	_status.text = ""


## 一页＝滚动区里一个 VBox：横向滚动关掉（宽了要被 `CONTENT` 那道门限量出来）
func _make_tab(title: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = "Scroll%s" % title
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var box := VBoxContainer.new()
	box.name = "List%s" % title
	box.add_theme_constant_override("separation", 6)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(box)
	_tabs.add_child(scroll)
	_tabs.set_tab_title(_tabs.get_tab_count() - 1, title)
	return box


func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("clue_screen.tscn 缺少节点：%s" % path)
		assert(false, "clue_screen.tscn 缺少节点：%s" % path)
	return node


func press_return() -> void:
	if return_handler.is_valid():
		return_handler.call()
		return
	queue_free()


func show_message(text: String) -> void:
	if _status != null:
		_status.text = text


func refresh() -> void:
	if clues == null or _tabs == null:
		return
	if scope != "scene" and scope != "region":
		scope = "scene"
	# `scene_id` 还没被调用方赋上时（自检环境里 `_ready` 先跑一遍）不算数据错，别刷假 ERROR
	if scope == "scene" and not scene_id.is_empty() and db.get_row("map_local", scene_id) == null:
		# 数据错：scene id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见决策 329）
		push_error("[clue] map_local 里没有这张图：%s" % scene_id)
	# 五页一次性都建出来：没显示的那几页也得有内容，`CONTENT` 那道门限才量得到宽度
	for id: String in _sections:
		var box: VBoxContainer = _lists[id]
		_clear(box)
		var page: Dictionary = clues.section(id, scope, scene_id)
		var entries: Array = page["entries"]
		# 一页＝一块卡片（标题条写这一类叫什么，右边那截写几条——照示意图的信息层级）
		var body: VBoxContainer = UiKitScript.add_card(
			box,
			str(ClueServiceScript.SECTION_TITLES.get(id, id)),
			"%d 条" % entries.size(),
			"Card%s" % id,
		)
		if entries.is_empty():
			body.add_child(_make_label("Empty", _empty_text(id), 0, "#8A8578"))
			continue
		for entry: Dictionary in entries:
			body.add_child(_make_label(
				"Clue_%s" % str(entry["id"]), str(entry["text"]), 0,
				"" if not bool(entry["done"]) else "#6fcf97",
			))
	_apply_page_header()


## 标题与小标题永远取自**当前那页**（文案在服务里拼，面板不自己编）
func _apply_page_header() -> void:
	var page: Dictionary = clues.section(section_id(), scope, scene_id)
	_title.text = str(page["title"])
	_subtitle.text = str(page["subtitle"])


func _empty_text(id: String) -> String:
	match id:
		"main":
			return "还没有可跟的线索"
		"side":
			return "还没有人托你办事"
		"rumor":
			return "还没听到什么风声——多跟人聊聊，多看看墙角"
		"cast":
			return "还没遇上同行的人"
		_:
			return "这里暂时没有可查的线索"


func _clear(box: Node) -> void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()


func _make_label(node_name: String, text: String, font_size: int = 0, color: String = "") -> Label:
	var label := Label.new()
	label.name = node_name
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	if font_size > 0:
		label.add_theme_font_size_override("font_size", font_size)
	if not color.is_empty():
		label.add_theme_color_override("font_color", Color(color))
	return label


# ------------------------------------------------------------------ 环境

func _resolve_db():
	if db_override != null:
		return db_override
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String = "GameSession") -> Node:
	var tree := _tree()
	if tree == null:
		return null
	return tree.root.get_node_or_null(node_name)


func _tree() -> SceneTree:
	# 不在场景树里时 get_tree() 会打一条 ERROR（--script 模式用例直接 new 节点就会踩到）
	if is_inside_tree():
		var tree := get_tree()
		if tree != null:
			return tree
	return Engine.get_main_loop() as SceneTree


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 自检

## 自检取某页某一条的文字（找不到返回空串——`.contains()` 于是当场假）
func _section_text(service, section_id: String, entry_id: String) -> String:
	for entry: Dictionary in service.section(section_id)["entries"]:
		if str(entry["id"]) == entry_id:
			return str(entry["text"])
	return ""


func _run_clue_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	# 1. 小地图线索：黑风寨的隐藏内容与事件判定
	scope = "scene"
	scene_id = "scene_heifengzhai"
	refresh()
	var scene_entries: Array = clues.clues_for_scene(scene_id)
	var hidden := 0
	var events := 0
	var with_clues := 0
	var unsupported := 0
	for entry: Dictionary in scene_entries:
		if str(entry["kind"]) == "hidden":
			hidden += 1
		else:
			events += 1
		if not Array(entry["clues"]).is_empty():
			with_clues += 1
		if bool(entry.get("unsupported", false)):
			unsupported += 1
	# 黑风寨本图 6 条隐藏（后山密道那条的 scene_id 是 scene_cave）＋ 5 条事件判定
	# （0.29.0 加了 `ev_shen_rescue`：地牢里救沈雁回）
	ok = ok and hidden == 6 and events == 5 and with_clues == scene_entries.size()
	lines.append("黑风寨：隐藏 %d 条／事件 %d 条／都有线索=%s（缺位点未做 %d 条）" % [
		hidden, events, with_clues == scene_entries.size(), unsupported,
	])
	select_section("hidden")
	lines.append("隐藏页行数=%d" % entry_count())
	ok = ok and entry_count() == scene_entries.size()
	# 2. 完成状态跟存档走
	state.record_dungeon(scene_id, "triggers", "trig_wine")
	refresh()
	var wine_done := false
	for entry: Dictionary in clues.clues_for_scene(scene_id):
		if str(entry["id"]) == "trig_wine":
			wine_done = bool(entry["done"])
	ok = ok and wine_done
	lines.append("完成状态跟存档：酒葫芦已触发=%s" % wine_done)
	# 3. 大地图线索（按地标）
	scope = "region"
	refresh()
	var region_entries: Array = clues.region_clues()
	# 野外按地标：落雁坡 3、荒村 1、塌陷山洞 1、黑风寨 2、清风驿 1、官道 1 = 9
	# （官道那条是 0.28.0 的 `ev_patrol_check`，此前这条期望值一直少算了驿站这一格）
	ok = ok and region_entries.size() == 9
	lines.append("大地图线索=%d 条（落雁坡 %d／荒村 %d）" % [
		region_entries.size(),
		clues.clues_for_region("n_luoyanpo").size(),
		clues.clues_for_region("n_huangcun").size(),
	])
	# 隐藏那一页跟着视线走：野外视角摆 9 条、小标题写「野外」，并且明写「未揭」几处
	var hidden_region: Dictionary = clues.section("hidden", "region", "")
	var hidden_scope_ok: bool = section_row_count("hidden") == region_entries.size() \
		and str(hidden_region["subtitle"]).contains("野外") \
		and str(hidden_region["subtitle"]).contains("未揭")
	ok = ok and hidden_scope_ok
	lines.append("隐藏页跟视角走（野外）=%s（%s）" % [hidden_scope_ok, str(hidden_region["subtitle"])])

	# —— 3.5 五类分页（设计 0.32.0／UI 落地清单 §二-10）——
	var seq := PackedStringArray(_sections)
	var tabs_ok: bool = _sections.size() == 5 and _tabs.get_tab_count() == 5
	ok = ok and tabs_ok
	lines.append("五类页签=%s（%s）" % [tabs_ok, "／".join(seq)])
	# 当前页高亮跟着走：切到「传闻」那页，索引、TabContainer、标题三处都要换
	select_section("rumor")
	var switched: bool = _section_index == 2 and _tabs.current_tab == 2 \
		and str(_title.text).contains("传闻")
	ok = ok and switched
	lines.append("切页签＝标题与高亮都跟着换=%s（%s）" % [switched, str(_title.text)])

	# 主线：开局 4 步、第一步「进行中」、后面的「未接」、小标题写百分比
	var main_page: Dictionary = clues.section("main")
	var main_text := ""
	for entry: Dictionary in main_page["entries"]:
		main_text += str(entry["text"])
	var main_ok: bool = main_page["entries"].size() == db.rows("guide_step").size() \
		and str(main_page["subtitle"]).contains("进度") \
		and main_text.contains("进行中") and main_text.contains("未接")
	ok = ok and main_ok
	lines.append("主线页=%s（%s）" % [main_ok, str(main_page["subtitle"])])

	# 支线：条件没到＝待接；凑齐条件＝待交；交过＝已了（三步各改一处存档，状态词跟着动）
	var side_text_before := _section_text(clues, "side", "nq_wang_01")
	state.inventory.add_item(db, "item_iron", 10)
	var side_text_ready := _section_text(clues, "side", "nq_wang_01")
	state.set_flag(NpcServiceScript.quest_done_flag("nq_wang_01"))
	var side_text_done := _section_text(clues, "side", "nq_wang_01")
	var side_ok: bool = side_text_before.contains("［待接］") \
		and side_text_ready.contains("［待交］") and side_text_done.contains("［已了］") \
		and side_text_before.contains("好感 +15")
	ok = ok and side_ok
	lines.append("支线页：待接→待交→已了=%s（委托人与奖励也写了）" % side_ok)

	# 传闻：两处来源各接一遍，且「验」跟着指向的东西走（用一份新档做，免得被上面的旗标污染）
	var fresh = GameStateScript.new_game(db, "normal")
	var fresh_clues = ClueServiceScript.new(db, fresh)
	var rumor_empty: bool = fresh_clues.section("rumor")["entries"].is_empty()
	fresh.set_flag("flag_obs_ob_dukou_pile_04")
	var rumor_text := _section_text(fresh_clues, "rumor", "ob_dukou_pile_04")
	var rumor_ok: bool = rumor_empty and rumor_text.contains("未验") \
		and rumor_text.contains("醉刀客")
	ok = ok and rumor_ok
	lines.append("传闻页：读过的碎句认得出「指向谁」＝%s（%s）" % [
		rumor_ok, "指向醉刀客" if rumor_text.contains("醉刀客") else "没认出指向",
	])
	fresh.set_flag(WorldEventServiceScript.hint_flag("trig_wine"))
	var rumor_heard: int = fresh_clues.section("rumor")["entries"].size()
	fresh.record_dungeon("scene_heifengzhai", "triggers", "trig_wine")
	var rumor_verified: bool = rumor_heard == 2 \
		and _section_text(fresh_clues, "rumor", "ob_dukou_pile_04").contains("已验") \
		and _section_text(fresh_clues, "rumor", "we_rumor").contains("已验")
	ok = ok and rumor_verified
	lines.append("传闻页：触发了它指向的东西才翻「已验」＝%s（听来 %d 条）" % [rumor_verified, rumor_heard])

	# 角色：主角自己不算「同伴」；同伴状态跟着队伍与加入条件走
	var cast_page: Dictionary = fresh_clues.section("cast")
	var protagonist_id := str(fresh.char_ids[0]) if not fresh.char_ids.is_empty() else ""
	var protagonist_listed := false
	for entry: Dictionary in cast_page["entries"]:
		if str(entry["id"]) == protagonist_id:
			protagonist_listed = true
	var ci_before := _section_text(fresh_clues, "cast", "ch_ci")
	fresh.set_flag("flag_board_read")
	var ci_ready := _section_text(fresh_clues, "cast", "ch_ci")
	fresh.char_ids.append("ch_ci")
	var ci_joined := _section_text(fresh_clues, "cast", "ch_ci")
	var cast_ok: bool = not protagonist_listed \
		and ci_before.contains("［未遇］") and ci_ready.contains("［待入队］") \
		and ci_joined.contains("［已入队］")
	ok = ok and cast_ok
	lines.append("角色页：主角不摆＋同伴未遇→待入队→已入队＝%s（共 %d 行）" % [
		cast_ok, cast_page["entries"].size(),
	])
	# 版式预算：整页最小高度要塞得进设计分辨率
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 滚动区内容宽度：横滚是关着的，线索一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
	ok = ok and LayoutBudgetScript.content_fits(self)
	lines.append(LayoutBudgetScript.ascii_content_line(self))
	# 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy_hits.is_empty():
		lines.append("COPY 命中：%s" % "；".join(copy_hits))
	for line: String in lines:
		print("  " + line)
	print("CLUE SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
