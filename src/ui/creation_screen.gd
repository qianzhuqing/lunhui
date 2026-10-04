## 创建角色（设计 13，0.17.0）：两条路——「使用模板」与「不使用模板」。
##
##   使用模板：选出身（5 张卡）→ 选天赋 → 起名 → 确认
##   不使用模板：分七维（总和 49、单项 3~15）→ 选武器 → 选本系 ★1 武学 → 选天赋 → 起名 → 确认
##
## 界面用代码搭（占位美术阶段的既有约定）。所有规则与数据都在 `CreationService`／
## `TalentService` 里，这里只做「显示 + 收集选择 + 把 spec 交给 MenuController」。
##
## 两条纪律（设计 13）：**创建没走完不写存档**（确认那一刻才落盘）；
## 选了谁当主角，谁就不作为同伴出现（`RecruitService` 只收不在队里的人，天然满足）。
##
## 自检：`-- --creation-selftest` 走「模板路 → 自建路 → 非法方案被拒」并返回退出码。
extends Control

const CreationServiceScript := preload("res://src/core/creation_service.gd")
const TalentServiceScript := preload("res://src/core/talent_service.gd")
const MenuControllerScript := preload("res://src/core/menu_controller.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

const MENU_SCENE := "res://scenes/main_menu.tscn"
const HUB_SCENE := "res://scenes/placeholder_game.tscn"
## 七维的展示顺序（与面板一致：五维在前，资质在后）
const ATTR_ORDER := ["str", "con", "agi", "int", "luk", "wu", "gen"]
const ROUTE_ORIGIN := "origin"
const ROUTE_CUSTOM := "custom"

## 自检注入点
var store_override = null
var scene_switch_handler := Callable()
var db

var route := ROUTE_ORIGIN
var spec: Dictionary = {}

var _content: VBoxContainer
var _status: Label
var _confirm_button: Button
var _route_box: HBoxContainer
var _body: VBoxContainer
## 右侧立绘（设计 0.19.1：创建界面放右侧 320×480；占位是 80×120 剪影 4 倍显示）
var _portrait: TextureRect = null

## 立绘目录：**按 char_id** 命名（`assets/sprites/portraits/portrait_<char_id>.png`）。
## 美术出图后同名替换；缺图时不留空白框——退回「（立绘待出）」的浅色底，玩家不会以为是 bug。
const PORTRAIT_DIR := "res://assets/sprites/portraits/"
const PORTRAIT_PLACEHOLDER := "（立绘待出）"


func _ready() -> void:
	setup()
	if _has_user_arg("--creation-selftest"):
		call_deferred("_run_creation_selftest")


func setup() -> void:
	if db != null:
		return
	db = _resolve_db()
	spec = {"route": ROUTE_ORIGIN, "talents": [], "name_cn": ""}
	_build_ui()
	_refresh()


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String) -> Node:
	var tree := _tree()
	if tree == null:
		return null
	return tree.root.get_node_or_null(node_name)


func _tree() -> SceneTree:
	if is_inside_tree():
		return get_tree()
	return Engine.get_main_loop() as SceneTree


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 界面

## 界面配件（卡片、色值）走共享构件——`const` 就近放在用它的这一段上面
const UiKitScript := preload("res://src/ui/ui_kit.gd")


func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var bg := ColorRect.new()
	bg.name = "Backdrop"
	# 背板色值只在主题里一处（`UiKit/colors/backdrop`）
	bg.color = UiKitScript.color("backdrop")
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var margin := MarginContainer.new()
	margin.name = "Margin"
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 24)
	margin.add_theme_constant_override("margin_right", 24)
	# 立绘位（320×480）进来之后纵向只剩 6px 余量：上下边距压到 8
	# ——设计 0.19.1 提醒过「这两处版面会很紧，加任何一行都要先想清楚从哪儿腾」。
	margin.add_theme_constant_override("margin_top", 8)
	margin.add_theme_constant_override("margin_bottom", 8)
	add_child(margin)

	var column := VBoxContainer.new()
	column.name = "Column"
	margin.add_child(column)

	var title := Label.new()
	title.name = "Title"
	title.text = "创建角色"
	# 界面标题档（设计 15 §4.4：标题 24／正文 12，**不再有 16 档**——16 ＝ 12×1.33，像素字发虚）
	title.add_theme_font_size_override("font_size", 24)
	column.add_child(title)

	_route_box = HBoxContainer.new()
	_route_box.name = "Routes"
	for entry: Array in [[ROUTE_ORIGIN, "使用模板"], [ROUTE_CUSTOM, "不使用模板"]]:
		var button := Button.new()
		button.name = "Route%s" % str(entry[0]).capitalize()
		button.text = str(entry[1])
		button.custom_minimum_size = Vector2(160, 40)
		button.pressed.connect(func() -> void: select_route(str(entry[0])))
		_route_box.add_child(button)
	column.add_child(_route_box)

	var scroll := ScrollContainer.new()
	scroll.name = "Scroll"
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	# 左内容 / 右立绘（设计 0.19.1 的版式：创建界面立绘放右侧）
	var body := HBoxContainer.new()
	body.name = "Body"
	body.add_theme_constant_override("separation", 12)
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(body)
	body.add_child(scroll)
	_portrait = TextureRect.new()
	_portrait.name = "Portrait"
	_portrait.custom_minimum_size = Vector2(320, 480)
	_portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_portrait.stretch_mode = TextureRect.STRETCH_SCALE
	_portrait.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	body.add_child(_portrait)
	_content = VBoxContainer.new()
	_content.name = "Content"
	_content.custom_minimum_size = Vector2(760, 0)
	scroll.add_child(_content)
	_body = _content

	_status = Label.new()
	_status.name = "Status"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.custom_minimum_size = Vector2(760, 0)
	column.add_child(_status)

	var buttons := HBoxContainer.new()
	buttons.name = "Buttons"
	_confirm_button = Button.new()
	_confirm_button.name = "ConfirmButton"
	_confirm_button.text = "确认创建"
	_confirm_button.custom_minimum_size = Vector2(200, 40)
	_confirm_button.pressed.connect(confirm)
	buttons.add_child(_confirm_button)
	var back := Button.new()
	back.name = "BackButton"
	back.text = "返回（Esc）"
	back.custom_minimum_size = Vector2(160, 40)
	back.pressed.connect(go_back)
	buttons.add_child(back)
	column.add_child(buttons)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		go_back()


func select_route(next: String) -> void:
	route = next
	spec["route"] = next
	_refresh()


func _rebuild_body() -> void:
	for child in _body.get_children():
		# **先从树上摘掉再 queue_free**：只 queue_free 的话旧节点要到帧末才真走，
		# 而下面马上又用**同一个名字**建新节点 —— Godot 见名字撞了就把新的自动改名
		# （`@Label@718` 这种），于是"按名字找节点"的人（用例／自检／面板）拿到的
		# 可能是上一帧那个（2026-10-04 用例第一次跑就踩到）。其他面板（角色／商店／打坐／
		# 副本／驿站）本来就是 `remove_child` ＋ `queue_free` 的写法，这里对齐。
		_body.remove_child(child)
		child.queue_free()
	_body = _content


func _refresh() -> void:
	_rebuild_body()
	_refresh_portrait()
	if route == ROUTE_ORIGIN:
		_build_origin_section()
	else:
		_build_custom_section()
	_build_talent_section()
	_build_name_section()
	_build_summary_section()
	var errors: PackedStringArray = CreationServiceScript.validate(db, _current_spec())
	_confirm_button.disabled = not errors.is_empty()
	_status.text = "可以创建了：按「确认创建」" if errors.is_empty() else "；".join(errors)


## 确认前的汇总（设计 13 §六「确认页汇总全部选择——让玩家在进游戏前再核对一次」）。
##
## 一次把所有选择写成几行：出身／七维／武器／起始武学／天赋／名字。**与面板同一份取名口径**
## （武器名与武学名都走 `CreationService` 那两个 helper），不在这里另查一遍表。
func _build_summary_section() -> void:
	_body.add_child(_section_label("确认一遍"))
	var label := Label.new()
	label.name = "SummaryLabel"
	label.text = summary_text()
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.custom_minimum_size = Vector2(740, 0)
	label.add_theme_font_size_override("font_size", 12)
	_body.add_child(label)


## 汇总文案（自检与用例读它：名字固定叫 `SummaryLabel`）
func summary_text() -> String:
	var lines := PackedStringArray()
	var spec_now := _current_spec()
	if str(spec_now.get("route", ROUTE_ORIGIN)) == ROUTE_ORIGIN:
		var origin_id := str(spec_now.get("origin_id", ""))
		var card: Resource = db.get_row("origin_def", origin_id)
		var char_row: Resource = db.get_row("character_base", str(card.char_id)) if card != null else null
		lines.append("出身：%s（%s）" % [
			str(card.name_cn) if card != null else "还没选",
			str(card.playstyle) if card != null else "—",
		])
		if char_row != null:
			var attrs: Dictionary = char_row.initial_attrs()
			var parts := PackedStringArray()
			for attr_id: String in ATTR_ORDER:
				parts.append("%s %d" % [_attr_name(attr_id), int(attrs.get(attr_id, 0))])
			lines.append("七维（模板给的）：%s" % "　".join(parts))
			var skill_names := PackedStringArray()
			for skill_id: String in char_row.skill_ids():
				skill_names.append(CreationServiceScript.skill_name_of(db, skill_id))
			lines.append("武器：%s　起始武学：%s" % [
				CreationServiceScript.weapon_name_of(db, str(char_row.weapon_type)),
				"、".join(skill_names),
			])
	else:
		var attrs: Dictionary = Dictionary(spec_now.get("attrs", {}))
		var parts := PackedStringArray()
		for attr_id: String in ATTR_ORDER:
			parts.append("%s %d" % [_attr_name(attr_id), int(attrs.get(attr_id, 0))])
		lines.append("七维（自己分的）：%s" % "　".join(parts))
		var weapon_type := str(spec_now.get("weapon_type", ""))
		var skill_id := str(spec_now.get("start_skill_id", ""))
		lines.append("武器：%s　起始武学：%s" % [
			CreationServiceScript.weapon_name_of(db, weapon_type) if not weapon_type.is_empty() else "还没选",
			CreationServiceScript.skill_name_of(db, skill_id) if not skill_id.is_empty() else "还没选",
		])
	var talent_names := PackedStringArray()
	for talent_id: Variant in Array(spec_now.get("talents", [])):
		var row: Resource = db.get_row("talent_def", str(talent_id))
		talent_names.append(str(row.name_cn) if row != null else str(talent_id))
	lines.append("天赋：%s（%d/%d 点）" % [
		"、".join(talent_names) if not talent_names.is_empty() else "还没选",
		TalentServiceScript.cost_of(db, PackedStringArray(spec_now.get("talents", []))),
		TalentServiceScript.budget(db),
	])
	lines.append("名字：%s" % (
		str(spec_now.get("name_cn", "")) if not str(spec_now.get("name_cn", "")).strip_edges().is_empty() else "还没填"
	))
	return "\n".join(lines)


## 收集当前选择 → 交给校验／创建的 spec（**每次刷新都从控件与 spec 现算**）
func _current_spec() -> Dictionary:
	var out := spec.duplicate(true)
	# 名字**以控件为准**：这一格是玩家直接改的，程序里改 `text` 不会发 `text_changed`，
	# 只信 spec 的话就会「界面上写着一个名字、建出来的角色是另一个」（自检里就踩到过）。
	var name_edit: LineEdit = find_child("NameEdit", true, false)
	if name_edit != null:
		out["name_cn"] = name_edit.text
	if str(out.get("route", ROUTE_ORIGIN)) == ROUTE_ORIGIN:
		out.erase("attrs")
		out.erase("weapon_type")
		out.erase("start_skill_id")
	else:
		out["attrs"] = Dictionary(out.get("attrs", {}))
	return out


func _build_origin_section() -> void:
	var cards: Array = CreationServiceScript.origins(db)
	_body.add_child(_section_label("选出身（模板）"))
	for card: Dictionary in cards:
		var picked: bool = str(spec.get("origin_id", "")) == str(card["origin_id"])
		# 一张卡 = 按钮（点它选中）＋ 下面一行信息（七维条形图／武器／起始武学）。
		# 设计 13 §二／§六 要的就是这两行：**只给一句文案没法做数值选择**，
		# 而条形图比读七个数字快（五个出身一眼可比）。
		var box := VBoxContainer.new()
		box.name = "OriginCard_%s" % str(card["origin_id"])
		box.add_theme_constant_override("separation", 2)
		var button := Button.new()
		button.name = "Origin_%s" % str(card["origin_id"])
		button.text = "%s%s（%s）——%s" % [
			"● " if picked else "○ ", str(card["name_cn"]), str(card["playstyle"]), str(card["tagline"]),
		]
		button.custom_minimum_size = Vector2(740, 40)
		button.pressed.connect(func() -> void: pick_origin(str(card["origin_id"])))
		box.add_child(button)
		var detail := Label.new()
		detail.name = "OriginDetail_%s" % str(card["origin_id"])
		detail.text = _origin_card_detail(card)
		detail.add_theme_font_size_override("font_size", 12)
		box.add_child(detail)
		_body.add_child(box)


## 出身卡的第二行：**七维条形图 ＋ 武器 ＋ 起始武学**（设计 13 §二／§六）。
## 条形图与 NPC 面板的好感条同一套写法（`█`／`░` 各五格，按 15 分满刻度）——
## 字形来自已经落地的像素字体，不额外要图。
func _origin_card_detail(card: Dictionary) -> String:
	var attrs: Dictionary = card.get("attrs", {})
	var parts := PackedStringArray()
	for attr_id: String in ATTR_ORDER:
		var value := int(attrs.get(attr_id, 0))
		parts.append("%s %s%d" % [_attr_name(attr_id), _attr_bar(value), value])
	var skills := PackedStringArray()
	for name_cn: Variant in Array(card.get("skill_names", [])):
		skills.append(str(name_cn))
	return "%s　｜　%s：%s" % [
		"　".join(parts),
		str(card.get("weapon_name", "")),
		"、".join(skills),
	]


## 七维条形图：五格满格 = `create_attr_max`（15）
func _attr_bar(value: int) -> String:
	var filled := clampi(int(round(float(value) / 15.0 * 5.0)), 0, 5)
	return "█".repeat(filled) + "░".repeat(5 - filled)


func pick_origin(origin_id: String) -> void:
	spec["origin_id"] = origin_id
	# 预填**真名**而不是卡的标题（设计 21 §七 第 5 条）：书生的卡叫「家道失落的书生」，
	# 但主角默认名该是「陆文昭」。空值退回卡标题，见 `CreationService.default_name_of`。
	if str(spec.get("name_cn", "")).strip_edges().is_empty():
		spec["name_cn"] = CreationServiceScript.default_name_of(db, origin_id)
	_refresh()


func _build_custom_section() -> void:
	var rules: Dictionary = CreationServiceScript.attr_rules(db)
	_body.add_child(_section_label("分七维（总和 %d，单项 %d~%d）" % [int(rules["total"]), int(rules["min"]), int(rules["max"])]))
	var attrs: Dictionary = Dictionary(spec.get("attrs", {}))
	for attr_id: String in ATTR_ORDER:
		if not attrs.has(attr_id):
			attrs[attr_id] = int(rules["min"])
	spec["attrs"] = attrs
	for attr_id: String in ATTR_ORDER:
		var row := HBoxContainer.new()
		row.name = "Attr_%s" % attr_id
		var label := Label.new()
		label.text = "%s：%d" % [_attr_name(attr_id), int(attrs[attr_id])]
		label.custom_minimum_size = Vector2(200, 0)
		row.add_child(label)
		for delta: int in [-1, 1]:
			var button := Button.new()
			button.name = "%s%s" % ["Minus" if delta < 0 else "Plus", attr_id]
			button.text = "-1" if delta < 0 else "+1"
			button.custom_minimum_size = Vector2(64, 36)
			button.pressed.connect(func() -> void: nudge_attr(attr_id, delta))
			row.add_child(button)
		_body.add_child(row)
	var sum := 0
	for attr_id: String in ATTR_ORDER:
		sum += int(attrs[attr_id])
	_body.add_child(_section_label("已分配 %d / %d" % [sum, int(rules["total"])]))

	_body.add_child(_section_label("选武器"))
	for weapon: Dictionary in CreationServiceScript.weapons(db):
		var picked: bool = str(spec.get("weapon_type", "")) == str(weapon["weapon_type"])
		var button := Button.new()
		button.name = "Weapon_%s" % str(weapon["weapon_type"])
		button.text = ("● " if picked else "○ ") + str(weapon["name_cn"])
		button.custom_minimum_size = Vector2(180, 36)
		button.pressed.connect(func() -> void: pick_weapon(str(weapon["weapon_type"])))
		_body.add_child(button)

	var weapon_type := str(spec.get("weapon_type", ""))
	if not weapon_type.is_empty():
		_body.add_child(_section_label("选起始武学（★1 本系）"))
		for option: Dictionary in CreationServiceScript.start_skill_options(db, weapon_type):
			var picked: bool = str(spec.get("start_skill_id", "")) == str(option["skill_id"])
			var button := Button.new()
			button.name = "StartSkill_%s" % str(option["skill_id"])
			button.text = ("● " if picked else "○ ") + str(option["name_cn"])
			button.custom_minimum_size = Vector2(300, 36)
			button.pressed.connect(func() -> void: pick_start_skill(str(option["skill_id"])))
			_body.add_child(button)


func nudge_attr(attr_id: String, delta: int) -> void:
	var attrs: Dictionary = Dictionary(spec.get("attrs", {}))
	attrs[attr_id] = int(attrs.get(attr_id, 0)) + delta
	spec["attrs"] = attrs
	_refresh()


func pick_weapon(weapon_type: String) -> void:
	spec["weapon_type"] = weapon_type
	# 换武器就把起始武学清掉：上一把的武学多半不在新武器的选项里
	spec.erase("start_skill_id")
	_refresh()


func pick_start_skill(skill_id: String) -> void:
	spec["start_skill_id"] = skill_id
	_refresh()


func _build_talent_section() -> void:
	var picks := PackedStringArray()
	for talent_id: Variant in Array(spec.get("talents", [])):
		picks.append(str(talent_id))
	_body.add_child(_section_label("选天赋（还剩 %d / %d 点）"
		% [TalentServiceScript.remaining_points(db, picks), TalentServiceScript.budget(db)]))
	for card: Dictionary in TalentServiceScript.cards(db):
		var talent_id := str(card["talent_id"])
		var picked: bool = picks.has(talent_id)
		var button := Button.new()
		button.name = "Talent_%s" % talent_id
		button.text = "%s%s（%d 点）——%s" % [
			"● " if picked else "○ ", str(card["name_cn"]), int(card["cost"]), str(card["desc"]),
		]
		button.custom_minimum_size = Vector2(740, 36)
		button.pressed.connect(func() -> void: toggle_talent(talent_id))
		_body.add_child(button)


func toggle_talent(talent_id: String) -> void:
	var picks := PackedStringArray()
	for item: Variant in Array(spec.get("talents", [])):
		picks.append(str(item))
	var result: Dictionary = TalentServiceScript.toggle(db, picks, talent_id)
	if bool(result["ok"]):
		spec["talents"] = Array(result["picks"])
	else:
		_status.text = str(result["error"])
	_refresh()


func _build_name_section() -> void:
	_body.add_child(_section_label("起名"))
	var edit := LineEdit.new()
	edit.name = "NameEdit"
	edit.text = str(spec.get("name_cn", ""))
	edit.custom_minimum_size = Vector2(320, 36)
	edit.text_changed.connect(func(text: String) -> void: spec["name_cn"] = text)
	_body.add_child(edit)


func _section_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", 12)
	return label


func _attr_name(attr_id: String) -> String:
	var row: Resource = db.get_row("attribute_def", attr_id)
	return str(row.name_cn) if row != null else attr_id


## 右侧立绘：跟着**当前选择**走——模板路看出身卡指向的 char_id，
## 自建路还没有立绘（角色还没生成），显示占位底。
func _refresh_portrait() -> void:
	if _portrait == null:
		return
	var char_id := ""
	if route == ROUTE_ORIGIN:
		var card: Resource = db.get_row("origin_def", str(spec.get("origin_id", "")))
		if card != null:
			char_id = str(card.char_id)
	var path := "%sportrait_%s.png" % [PORTRAIT_DIR, char_id]
	if char_id.is_empty() or not ResourceLoader.exists(path):
		_portrait.texture = null
		_portrait.tooltip_text = PORTRAIT_PLACEHOLDER
		return
	_portrait.tooltip_text = ""
	_portrait.texture = load(path)


func portrait_path() -> String:
	var card: Resource = db.get_row("origin_def", str(spec.get("origin_id", "")))
	return "%sportrait_%s.png" % [PORTRAIT_DIR, str(card.char_id) if card != null else ""]


# ------------------------------------------------------------------ 提交

## 确认创建：走 `MenuController.new_game(spec)`（落盘也由它做），成功就进枢纽页。
func confirm() -> Dictionary:
	var errors: PackedStringArray = CreationServiceScript.validate(db, _current_spec())
	if not errors.is_empty():
		_status.text = "；".join(errors)
		return {"ok": false, "error": "; ".join(errors)}
	var controller = MenuControllerScript.new(
		db, _make_store(), PackedStringArray(), "normal"
	)
	var result: Dictionary = controller.new_game(_current_spec())
	if not bool(result["ok"]):
		_status.text = str(result["message"])
		return {"ok": false, "error": str(result["message"])}
	# 会话状态要写进 GameSession（与菜单 `_handle()` 同一处口径）：枢纽页与大地图都读它
	var session := _session_node("GameSession")
	if session != null and result.get("state") != null:
		session.set_state(result["state"])
	_status.text = "创建完成，进游戏"
	_switch_to(HUB_SCENE)
	return {"ok": true, "slot": int(result["slot"])}


func _make_store():
	if store_override != null:
		return store_override
	return SaveStoreScript.new(SaveStoreScript.default_dir())


func go_back() -> void:
	_switch_to(MENU_SCENE)


func _switch_to(path: String) -> void:
	if scene_switch_handler.is_valid():
		scene_switch_handler.call(path)
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(path)


# ------------------------------------------------------------------ 自检

func _run_creation_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()
	var store = SaveStoreScript.new("res://.logs/creation_selftest")
	# 上一轮留下的存档会让「创建成功」这条断言变成时红时绿——先清干净
	for slot in range(1, store.slot_count + 1):
		DirAccess.remove_absolute(store.slot_path(slot))
	store_override = store
	var switched: Array = []
	scene_switch_handler = func(path: String) -> void: switched.append(path)

	# ① 模板路：选出身 → 确认
	select_route(ROUTE_ORIGIN)
	var cards: Array = CreationServiceScript.origins(db)
	ok = ok and not cards.is_empty()
	lines.append("出身卡 %d 张（%s…）" % [cards.size(), str(cards[0]["name_cn"])])
	pick_origin(str(cards[0]["origin_id"]))
	# 立绘位（设计 0.19.1）：右侧 320×480，选了出身就有占位图（美术出图后同名替换）
	var portrait: TextureRect = find_child("Portrait", true, false)
	var portrait_ok: bool = portrait != null \
		and portrait.custom_minimum_size == Vector2(320, 480) \
		and portrait.texture != null
	ok = ok and portrait_ok
	lines.append("立绘位 320×480 且跟着出身走=%s（%s）" % [portrait_ok, portrait_path()])
	# 默认名（设计 21 §七 第 5 条）：名字那一格预填的是**真名**（不是卡的标题）
	var name_edit: LineEdit = find_child("NameEdit", true, false)
	var default_name := CreationServiceScript.default_name_of(db, str(cards[0]["origin_id"]))
	var prefill_ok: bool = name_edit != null and not default_name.is_empty() \
		and name_edit.text == default_name
	ok = ok and prefill_ok
	lines.append("名字格预填默认名「%s」=%s" % [default_name, prefill_ok])
	# 「主角可改」：改成别的名字 → 创建后**从存档里读回来的是改过的那个**
	var typed_name := "王二"
	if name_edit != null:
		name_edit.text = typed_name
	var created: Dictionary = confirm()
	ok = ok and bool(created["ok"])
	lines.append("模板路创建 ok=%s（槽 %s）" % [created["ok"], str(created.get("slot", 0))])
	ok = ok and switched.size() == 1 and switched[0] == HUB_SCENE
	var loaded: Dictionary = store.load_slot(int(created.get("slot", 1)), db)
	var saved_name := ""
	if bool(loaded.get("ok", false)) and loaded["state"] != null \
			and not loaded["state"].char_ids.is_empty():
		saved_name = str(loaded["state"].char_name(db, str(loaded["state"].char_ids[0])))
	var name_saved: bool = saved_name == typed_name
	ok = ok and name_saved
	lines.append("改名「%s」写进存档=%s（读回来：%s）" % [typed_name, name_saved, saved_name])

	# ② 自建路：分满七维 → 选武器 → 选武学 → 确认
	switched.clear()
	select_route(ROUTE_CUSTOM)
	var rules: Dictionary = CreationServiceScript.attr_rules(db)
	var attrs: Dictionary = {}
	var left := int(rules["total"]) - int(rules["min"]) * ATTR_ORDER.size()
	for attr_id: String in ATTR_ORDER:
		attrs[attr_id] = int(rules["min"])
	# 按「每个属性最多加到 create_attr_max」把余量分完（一次加满 str 会越过单项上限）
	var headroom := int(rules["max"]) - int(rules["min"])
	for attr_id: String in ATTR_ORDER:
		if left <= 0:
			break
		var add := mini(left, headroom)
		attrs[attr_id] = int(attrs[attr_id]) + add
		left -= add
	spec["attrs"] = attrs
	var weapons_list: Array = CreationServiceScript.weapons(db)
	ok = ok and not weapons_list.is_empty()
	pick_weapon(str(weapons_list[0]["weapon_type"]))
	var options: Array = CreationServiceScript.start_skill_options(db, str(spec["weapon_type"]))
	ok = ok and not options.is_empty()
	if not options.is_empty():
		pick_start_skill(str(options[0]["skill_id"]))
	var custom: Dictionary = confirm()
	ok = ok and bool(custom["ok"])
	lines.append("自建路创建 ok=%s（%s）" % [custom["ok"], str(custom.get("error", "七维总和 %d" % int(rules["total"])))])
	ok = ok and switched.size() == 1 and switched[0] == HUB_SCENE

	# ③ 非法方案必须被挡住（七维总和不对）
	switched.clear()
	var bad := attrs.duplicate(true)
	bad["str"] = int(bad["str"]) + 1
	spec["attrs"] = bad
	var refused: Dictionary = confirm()
	ok = ok and not bool(refused["ok"]) and switched.is_empty()
	lines.append("七维总和不对时被拒=%s" % (not bool(refused["ok"])))

	# ④ 天赋点数上限：把 24 个天赋全选一遍，花完点之后必须拒绝
	select_route(ROUTE_ORIGIN)
	pick_origin(str(cards[0]["origin_id"]))
	var spent := 0
	for card: Dictionary in TalentServiceScript.cards(db):
		var before := int(TalentServiceScript.remaining_points(db, _picks()))
		toggle_talent(str(card["talent_id"]))
		var after := int(TalentServiceScript.remaining_points(db, _picks()))
		if after < before:
			spent += before - after
	ok = ok and spent <= TalentServiceScript.budget(db)
	lines.append("天赋共花 %d 点（上限 %d），没超" % [spent, TalentServiceScript.budget(db)])
	ok = ok and int(TalentServiceScript.remaining_points(db, _picks())) >= 0

	# 文案守卫：整页不许出现表内 id 形态
	var copy_hits: PackedStringArray = CopyGuardScript.id_tokens(self)
	ok = ok and copy_hits.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	var budget_ok: bool = LayoutBudgetScript.fits(self)
	ok = ok and budget_ok
	lines.append(LayoutBudgetScript.ascii_line(self))

	for line: String in lines:
		print("  " + line)
	print("CREATION SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)


func _picks() -> PackedStringArray:
	var out := PackedStringArray()
	for item: Variant in Array(spec.get("talents", [])):
		out.append(str(item))
	return out
