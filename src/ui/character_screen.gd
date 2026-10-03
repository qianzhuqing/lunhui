## 角色与行囊：角色面板 / 装备 / 背包三个页签。
##
## 界面用代码搭（占位美术阶段的既有约定），数据全部来自 CharacterSheet 与 Inventory，
## 面板不自己算数值。写操作（加点、穿戴、卸下、丢弃）后立刻重建页签。
##
## 自检：`-- --character-selftest` 会走一遍「切页签 → 加点 → 穿脱装备 → 丢钥匙道具」并返回退出码。
extends Control

const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const SkillGrantScript := preload("res://src/core/skill_grant.gd")
const LevelServiceScript := preload("res://src/core/level_service.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

const PLACEHOLDER_SCENE := "res://scenes/placeholder_game.tscn"

## 背包分类筛选
const FILTERS := [
	{"key": "all", "name": "全部"},
	{"key": "material", "name": "材料"},
	{"key": "consumable", "name": "消耗品"},
	{"key": "key", "name": "钥匙"},
	{"key": "skillbook", "name": "残页"},
	{"key": "equip", "name": "装备"},
]

## 自检注入点
var state_override = null
var back_handler := Callable()

var _db
var _selected_char: String = ""
var _selected_slot: String = ""
var _filter: String = "all"
## 页签索引自己记一份：TabContainer.current_tab 在 --script 自检环境里不会真正生效
## （节点还没进活动场景树），所以这里作为唯一真相；用户点击页签时由信号同步回来。
var _tab_index: int = 0

var _header: Label
var _tabs: TabContainer
var _char_box: VBoxContainer
var _equip_box: VBoxContainer
var _bag_box: VBoxContainer
var _status: Label


func _ready() -> void:
	setup()
	if _has_user_arg("--character-selftest"):
		call_deferred("_run_character_selftest")


## 幂等：用例可以直接调，不依赖引擎是否已经发过 _ready。
func setup() -> void:
	if _tabs != null:
		return
	_db = _resolve_db()
	var state = current_state()
	if state != null and state.char_ids.size() > 0:
		_selected_char = str(state.char_ids[0])
	_build_ui()
	refresh()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		press_back()


# ------------------------------------------------------------------ 供用例与入口调用

func current_state():
	if state_override != null:
		return state_override
	var session := _session_node("GameSession")
	return session.state if session != null else null


func sheet():
	var state = current_state()
	if state == null or _selected_char.is_empty():
		return null
	return CharacterSheetScript.new(_db, state, _selected_char)


func status_text() -> String:
	return _status.text if _status != null else ""


func current_tab() -> int:
	return _tab_index if _tabs != null else -1


func select_tab(index: int) -> void:
	if _tabs == null:
		return
	var clamped := clampi(index, 0, maxi(0, _tabs.get_tab_count() - 1))
	_tab_index = clamped
	_tabs.current_tab = clamped


func _on_tab_changed(index: int) -> void:
	_tab_index = index


func press_back() -> void:
	if back_handler.is_valid():
		back_handler.call()
		return
	var tree := _tree()
	if tree != null:
		tree.change_scene_to_file(PLACEHOLDER_SCENE)


func select_char(char_id: String) -> void:
	_selected_char = char_id
	_selected_slot = ""
	refresh()


## 加一点，返回 {ok, error, remaining}
func press_plus(attr_id: String) -> Dictionary:
	var panel = sheet()
	if panel == null:
		_set_status("没有可操作的角色")
		return {"ok": false, "error": "没有可操作的角色", "remaining": 0}
	var result: Dictionary = panel.spend_point(attr_id)
	_set_status("加点成功：%s +1" % panel.attribute_name(attr_id) if result["ok"] else str(result["error"]))
	refresh()
	return result


## 选中某个槽位，列出可换的装备
func press_slot(slot_id: String, index: int) -> void:
	_selected_slot = "%s|%d" % [slot_id, index]
	_set_status("选择要装上的装备")
	refresh()


## 把背包里的装备穿上
func equip_instance(instance_id: String) -> Dictionary:
	var state = current_state()
	var panel = sheet()
	if state == null or panel == null:
		return {"ok": false, "error": "没有可操作的角色"}
	var result: Dictionary = state.inventory.equip(
		_db, panel.char_id, panel.level, panel.weapon_type_id(), instance_id
	)
	_set_status("%s 已装上" % _item_name(instance_id) if result["ok"] else str(result["error"]))
	refresh()
	return result


## 卸下某格装备
func unequip(slot_id: String, index: int) -> Dictionary:
	var state = current_state()
	var panel = sheet()
	if state == null or panel == null:
		return {"ok": false, "error": "没有可操作的角色"}
	var result: Dictionary = state.inventory.unequip(panel.char_id, slot_id, index)
	_set_status("%s 已卸下" % _item_name(str(result["instance_id"])) if result["ok"] else str(result["error"]))
	refresh()
	return result


func set_filter(kind: String) -> void:
	_filter = kind
	refresh()


## 这一行物品属于当前筛选吗。
##
## 「钥匙」这一类按设计 05 的**语义**判：钥匙道具是隐藏内容的载体，也就是 `is_key_item`——
## 不是 `item_type == "key"`。05 的钥匙道具清单里就有铁镐，而它的 `item_type` 是 `tool`，
## 以前它哪个分类都进不去，玩家只能在「全部」里翻（数据侧要不要把 item_type 并进 key 已记在缺口表）。
func _row_matches_filter(row: Resource) -> bool:
	if _filter == "all":
		return true
	if _filter == "key":
		return bool(row.is_key_item)
	return str(row.item_type) == _filter


## 装／卸一部武学（招式进招式槽，内功占容量格）
func toggle_skill(skill_id: String) -> Dictionary:
	var panel = sheet()
	if panel == null:
		_set_status("没有可操作的角色")
		return {"ok": false, "error": "没有可操作的角色", "equipped": false}
	var result: Dictionary = panel.loadout().toggle(skill_id)
	var name := _skill_name(skill_id)
	if result["ok"]:
		_set_status("已%s「%s」" % ["装配" if bool(result["equipped"]) else "卸下", name])
	else:
		_set_status("「%s」%s" % [name, str(result["error"])])
	refresh()
	return result


func _skill_name(skill_id: String) -> String:
	var row: Resource = _db.get_row("skill_base", skill_id)
	return str(row.name_cn) if row != null else skill_id


## 研读秘籍（item 来源的一次性武学）。门槛不够或已经学过都不消耗。
func study_item(item_id: String) -> Dictionary:
	var state = current_state()
	if state == null:
		return {"ok": false, "error": "没有会话状态"}
	var result: Dictionary = SkillGrantScript.study(_db, state, item_id)
	if bool(result["ok"]):
		_set_status("研读《%s》：%s" % [_item_name(item_id), _learned_names(result)])
	else:
		_set_status("《%s》%s" % [_item_name(item_id), str(result["error"])])
	refresh()
	return result


func _item_name(item_id: String) -> String:
	# 装备实例 id（`eq_sword_01#1`）也走这里：TableDb.display_name 会剥掉 #序号
	return _db.display_name(item_id)


func _learned_names(result: Dictionary) -> String:
	var names := PackedStringArray()
	for entry: Dictionary in Array(result.get("entries", [])):
		if not PackedStringArray(entry["learned"]).is_empty():
			names.append("学会「%s」" % str(entry["name"]))
	if names.is_empty():
		return "没有新学会的武学"
	var text := "、".join(names)
	if not str(result["error"]).is_empty():
		text += "（%s）" % str(result["error"])
	return text


## 丢弃物品（钥匙道具会被拒绝）
func drop_item(item_id: String) -> Dictionary:
	var state = current_state()
	if state == null:
		return {"ok": false, "error": "没有会话状态"}
	var result: Dictionary = state.inventory.remove_item(_db, item_id, 1)
	_set_status("已丢弃 1 个" if result["ok"] else str(result["error"]))
	refresh()
	return result


func refresh() -> void:
	if _tabs == null:
		return
	_rebuild_header()
	_rebuild_character()
	_rebuild_equipment()
	_rebuild_bag()


# ------------------------------------------------------------------ 界面构建

func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	# 背板：这界面是盖在游戏画面上的（Tab 打开），没有背板就会让地图与 NPC 透上来把字糊掉
	var backdrop := ColorRect.new()
	backdrop.name = "Backdrop"
	backdrop.color = Color(0.07, 0.08, 0.1, 0.97)
	backdrop.set_anchors_preset(Control.PRESET_FULL_RECT)
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(backdrop)
	var margin := MarginContainer.new()
	margin.name = "Margin"
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	add_child(margin)

	var column := VBoxContainer.new()
	column.name = "Column"
	column.add_theme_constant_override("separation", 8)
	margin.add_child(column)

	var top := HBoxContainer.new()
	top.name = "Top"
	top.add_theme_constant_override("separation", 12)
	column.add_child(top)
	_header = Label.new()
	_header.name = "Header"
	_header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(_header)
	var back := Button.new()
	back.name = "BackButton"
	back.text = "返回游戏"
	back.pressed.connect(press_back)
	top.add_child(back)

	_tabs = TabContainer.new()
	_tabs.name = "Tabs"
	_tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_tabs.custom_minimum_size = Vector2(760, 520)
	_tabs.tab_changed.connect(_on_tab_changed)
	column.add_child(_tabs)
	_char_box = _make_tab("角色")
	_equip_box = _make_tab("装备")
	_bag_box = _make_tab("背包")

	_status = Label.new()
	_status.name = "Status"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.custom_minimum_size = Vector2(0, 32)
	column.add_child(_status)


func _make_tab(tab_name: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = "Scroll%s" % tab_name
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var box := VBoxContainer.new()
	box.name = "Box%s" % tab_name
	box.add_theme_constant_override("separation", 4)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(box)
	_tabs.add_child(scroll)
	_tabs.set_tab_title(_tabs.get_tab_count() - 1, tab_name)
	return box


func _make_button(node_name: String, text: String, pressed: Callable, disabled: bool = false) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.disabled = disabled
	button.alignment = HORIZONTAL_ALIGNMENT_LEFT
	button.pressed.connect(pressed)
	return button


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


func _clear(box: Node) -> void:
	for child in box.get_children():
		box.remove_child(child)
		child.queue_free()


# ------------------------------------------------------------------ 角色页

func _rebuild_header() -> void:
	var state = current_state()
	if state == null or state.inventory == null:
		_header.text = "没有会话状态"
		return
	# 队伍经验池 + 离下一级还差多少（升级规则见 src/core/level_service.gd）
	var levels = LevelServiceScript.new(_db, state)
	var exp_text := "队伍经验 %d" % int(state.party_exp)
	if not state.char_ids.is_empty():
		var progress: Dictionary = levels.exp_progress(str(state.char_ids[0]))
		exp_text += "（已满级）" if bool(progress["maxed"]) else "（下一级还需 %d）" % int(progress["need"])
	_header.text = "铜钱 %d　%s　难度 %s" % [
		int(state.inventory.money), exp_text, state.difficulty_name(_db),
	]


func _rebuild_character() -> void:
	_clear(_char_box)
	var state = current_state()
	if state == null:
		_char_box.add_child(_make_label("Empty", "没有会话状态：请回主菜单新建或读取存档"))
		return

	var member_row := HBoxContainer.new()
	member_row.name = "Members"
	member_row.add_theme_constant_override("separation", 6)
	_char_box.add_child(member_row)
	for char_id: String in state.char_ids:
		var selected := char_id == _selected_char
		var button := _make_button(
			"MemberButton%s" % char_id,
			"%s%s" % ["▶ " if selected else "", state.char_name(_db, char_id)],
			func() -> void: select_char(char_id)
		)
		member_row.add_child(button)

	var panel = sheet()
	if panel == null or not panel.valid():
		_char_box.add_child(_make_label("InvalidChar", "角色模板缺失：%s" % _selected_char))
		return

	_char_box.add_child(_make_label(
		"Template",
		"%s　Lv%d　%s　%s" % [panel.display_name(), panel.level, panel.role_tag(), panel.weapon_type_name()],
		20
	))
	if not panel.template_desc().is_empty():
		_char_box.add_child(_make_label("TemplateDesc", panel.template_desc()))

	# 五维：显示「合计（裸值 + 装备）」，可加点
	_char_box.add_child(_make_label("AttrHeader", "五维（未分配点数：%d）" % panel.available_points(), 18))
	var total: Dictionary = panel.total_attrs()
	var naked: Dictionary = panel.naked_attrs()
	for attr_row: Resource in _db.rows("attribute_def"):
		var attr_id := str(attr_row.attr_id)
		var total_value := int(total.get(attr_id, 0))
		var naked_value := int(naked.get(attr_id, 0))
		# 悟性／根骨是资质（设计 0.13.0）：加号置灰，并在行里写清为什么不能加
		var blocked_reason: String = panel.allocation_block_reason(attr_id)
		var is_talent := not blocked_reason.is_empty()
		var row := HBoxContainer.new()
		row.name = "AttrRow%s" % attr_id
		row.add_theme_constant_override("separation", 8)
		var attr_label := _make_label(
			"AttrLabel%s" % attr_id,
			"%s %d（裸 %d ＋ 装备 %d）%s" % [
				str(attr_row.name_cn), total_value, naked_value, total_value - naked_value,
				"　资质，不可加点" if is_talent else "",
			]
		)
		# 自动换行会把标签的最小宽度压到一个字，HBox 于是把它挤成竖排——
		# 五维行必须让它撑满（这一条踩过：面板上「力 5（裸 5 ＋ 装备 0）」被竖着排下来）
		attr_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(attr_label)
		var plus := _make_button(
			"PlusButton%s" % attr_id, "+", func() -> void: press_plus(attr_id),
			panel.available_points() <= 0 or is_talent
		)
		if is_talent:
			plus.tooltip_text = blocked_reason
		plus.custom_minimum_size = Vector2(36, 0)
		row.add_child(plus)
		_char_box.add_child(row)

	# 图鉴奖励（设计 01「收集本身也是成长」）：进度与当前加成摆出来，玩家才知道集图鉴有用
	var codex: Dictionary = panel.codex_bonus()
	var collected := panel.collected_skill_count()
	var step := maxi(1, int(codex.get("step", 5)))
	var remaining := (step - collected % step) % step
	_char_box.add_child(_make_label(
		"CodexReward",
		"图鉴奖励：已收集 %d 部武学 → 七项各 +%d（每 %d 部 +1，上限 %d 次%s）" % [
			collected, int(codex["bonus"]), step, int(codex.get("cap", 10)),
			"" if remaining == 0 else "，再收 %d 部再 +1" % remaining,
		],
	))

	# 派生数值：按攻击 / 防御 / 资源与行动分组
	_char_box.add_child(_make_label("StatHeader", "战斗属性", 18))
	# 战斗外气血（v11 起进存档）：面板上要看得见，不然玩家不知道为什么要去医馆
	var current_hp := int(panel.current_hp())
	var max_hp := int(panel.max_hp())
	_char_box.add_child(_make_label(
		"CurrentHp",
		"当前气血 %d / %d%s" % [current_hp, max_hp, "" if current_hp >= max_hp else "　（可到医馆花钱治疗）"],
	))
	for group_name: String in CharacterSheetScript.STAT_GROUPS:
		var parts := PackedStringArray()
		for stat_id: String in CharacterSheetScript.STAT_GROUPS[group_name]:
			parts.append("%s %s" % [panel.stat_name(stat_id), panel.stat_label(stat_id)])
		_char_box.add_child(_make_label("Stat%s" % group_name, "%s：%s" % [group_name, "　".join(parts)]))

	# 非战斗技能与判定值（判定不含装备）
	_char_box.add_child(_make_label("EventSkillHeader", "非战斗技能（判定值不含装备加成）", 18))
	for row: Dictionary in panel.event_skill_rows():
		_char_box.add_child(_make_label(
			"EventSkill%s" % row["skill_id"],
			"%s %d/%d　判定 %d（%s）" % [
				row["name"], int(row["level"]), int(row["max_level"]),
				int(row["check_value"]), row["related_attr_name"],
			]
		))

	# 武学：学到的都能装，招式进招式槽、内功按星级占容量格
	var summary: Dictionary = panel.slot_summary()
	_char_box.add_child(_make_label(
		"SkillHeader",
		"武学（招式 %d/%d　内功 %d/%d 格）" % [
			int(summary["active_used"]), int(summary["active_slots"]),
			int(summary["passive_used"]), int(summary["passive_capacity"]),
		],
		18,
	))
	for row: Dictionary in panel.skill_rows():
		_char_box.add_child(_make_skill_row(row))


## 一行武学：名字／星级色 + 数值摘要 + 装配（卸下）按钮。
## 装不下的按钮置灰，原因写在旁边——玩家不用猜为什么点不动。
func _make_skill_row(row: Dictionary) -> Control:
	var skill_id: String = row["skill_id"]
	var equipped: bool = bool(row["equipped"])
	var kind_text := "招式" if row["kind"] == "active" else "内功 %d 格" % int(row.get("slot_cost", 0))
	var detail := ""
	if row["kind"] == "active":
		detail = "倍率 %.2f　内力 %d" % [float(row["power_ratio"]), int(row["qi_cost"])]
		# 效果没配的招式（战斗里没有按钮、用不出来）如实写一句：玩家能装它占一个招式槽，
		# 不写清楚他就只会觉得「点了没反应」。
		var battle_note := str(row.get("battle_note", ""))
		if not battle_note.is_empty():
			detail += "　—　%s" % battle_note
	else:
		detail = str(row.get("bonus_summary", ""))
		# 内功填了「特殊效果 id」但设计还没给效果表（4 部高星内功都是这样）——如实写一句，
		# 不显示 id（界面不许出现表内英文 id，文案审计盯着）。
		if bool(row.get("special_effect_pending", false)):
			detail += "　—　带一条特殊效果，暂未生效（设计未给效果表）"
	# 修习门槛（属性不够时写出来，玩家知道该练哪一项）
	if not str(row["learn_req_reason"]).is_empty():
		detail += "　门槛 %s" % str(row["learn_req_reason"])
	var suffix := "　▶ 已装配" if equipped else "　%s" % str(row["equip_error"])
	var line := HBoxContainer.new()
	line.name = "SkillRow%s" % skill_id
	line.add_theme_constant_override("separation", 6)
	var label := _make_label(
		"Skill%s" % skill_id,
		"%s★　%s　%s　熟练 %d/%d（×%.2f）　%s%s" % [
			str(row["star_name"]), str(row["name"]), kind_text,
			int(row["mastery"]), int(row["mastery_max"]), float(row["mastery_multiplier"]),
			detail, suffix,
		],
		0,
		str(row["star_color"]),
	)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	line.add_child(label)
	line.add_child(_make_button(
		"LoadoutButton%s" % skill_id,
		"卸下" if equipped else "装配",
		func() -> void: toggle_skill(skill_id),
		not equipped and not str(row["equip_error"]).is_empty(),
	))
	return line


# ------------------------------------------------------------------ 装备页

func _rebuild_equipment() -> void:
	_clear(_equip_box)
	var state = current_state()
	var panel = sheet()
	if state == null or panel == null:
		_equip_box.add_child(_make_label("Empty", "没有会话状态"))
		return

	_equip_box.add_child(_make_label("SlotHeader", "%s 的装备（点槽位选要换的装备）" % panel.display_name(), 18))
	for row: Dictionary in panel.equipped_rows():
		var slot_id: String = row["slot_id"]
		var index: int = int(row["index"])
		var slot_key := "%s|%d" % [slot_id, index]
		var capacity_row: Resource = _db.get_row("equip_slot_def", slot_id)
		var capacity := int(capacity_row.max_equip) if capacity_row != null else 1
		var slot_label: String = str(row["slot_name"]) if capacity <= 1 else "%s%d" % [row["slot_name"], index + 1]
		var line := HBoxContainer.new()
		line.name = "SlotRow%s_%d" % [slot_id, index]
		line.add_theme_constant_override("separation", 8)
		var suffix := "▶ " if _selected_slot == slot_key else ""
		var text := "%s%s：%s" % [suffix, slot_label, row["name"]]
		if not bool(row["empty"]):
			text += "（%s）" % row["summary"]
			if not str(row["affix_summary"]).is_empty():
				text += "　词条：%s" % str(row["affix_summary"])
		var button := _make_button(
			"SlotButton%s_%d" % [slot_id, index], text,
			func() -> void: press_slot(slot_id, index)
		)
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		if not bool(row["empty"]):
			button.add_theme_color_override("font_color", Color(str(row["rarity_color"])))
		line.add_child(button)
		if not bool(row["empty"]):
			var slot_index := index
			line.add_child(_make_button(
				"UnequipButton%s_%d" % [slot_id, slot_index], "卸下",
				func() -> void: unequip(slot_id, slot_index)
			))
		_equip_box.add_child(line)

	if _selected_slot.is_empty():
		_equip_box.add_child(_make_label("Hint", "选一个槽位后，这里会列出可以装上的装备。"))
		return

	var parts := _selected_slot.split("|")
	var target_slot := parts[0]
	# 写中文槽位名，别把内部 id 甩给玩家看（equip_slot_def.name_cn）
	var slot_row: Resource = _db.get_row("equip_slot_def", target_slot)
	var slot_name := str(slot_row.name_cn) if slot_row != null else target_slot
	_equip_box.add_child(_make_label("CandidateHeader", "可以装到「%s」的装备：" % slot_name, 18))
	var candidates := _candidate_rows(target_slot)
	if candidates.is_empty():
		_equip_box.add_child(_make_label("NoCandidate", "背包里没有适合这个槽位的装备"))
		return
	for candidate: Dictionary in candidates:
		var instance_id: String = candidate["instance_id"]
		var text := "%s（%s）%s" % [candidate["name"], candidate["rarity"], candidate["summary"]]
		if not str(candidate["affix_summary"]).is_empty():
			text += "　词条：%s" % str(candidate["affix_summary"])
		if not bool(candidate["ok"]):
			text += "　— %s" % candidate["error"]
		var button := _make_button(
			"EquipButton%s" % instance_id, text,
			func() -> void: equip_instance(instance_id), not bool(candidate["ok"])
		)
		button.add_theme_color_override("font_color", Color(str(candidate["color"])))
		_equip_box.add_child(button)


## 某个槽位可用的背包装备：{instance_id, name, rarity, color, summary, ok, error}
func _candidate_rows(slot_id: String) -> Array:
	var out: Array = []
	var state = current_state()
	var panel = sheet()
	if state == null or panel == null:
		return out
	for instance_id: String in state.inventory.equipment_ids():
		var base: Resource = _db.get_row("equip_base", state.inventory.base_of(instance_id))
		if base == null or str(base.slot) != slot_id:
			continue
		if state.inventory.is_equipped(instance_id):
			continue
		var check: Dictionary = state.inventory.can_equip(
			_db, panel.char_id, panel.level, panel.weapon_type_id(), instance_id
		)
		out.append({
			"instance_id": instance_id,
			"name": str(base.name_cn),
			"rarity": panel.rarity_name(str(base.rarity)),
			"color": panel.rarity_color(str(base.rarity)),
			"summary": panel.equipment_summary(base),
			"affix_summary": panel.affix_summary_of(instance_id),
			"ok": bool(check["ok"]),
			"error": str(check["error"]),
		})
	return out


# ------------------------------------------------------------------ 背包页

func _rebuild_bag() -> void:
	_clear(_bag_box)
	var state = current_state()
	if state == null or state.inventory == null:
		_bag_box.add_child(_make_label("Empty", "没有会话状态"))
		return
	var inventory = state.inventory

	var filters := HBoxContainer.new()
	filters.name = "Filters"
	filters.add_theme_constant_override("separation", 6)
	_bag_box.add_child(filters)
	for entry: Dictionary in FILTERS:
		var key: String = entry["key"]
		var button := _make_button(
			"FilterButton%s" % key,
			"%s%s" % ["▶ " if _filter == key else "", entry["name"]],
			func() -> void: set_filter(key)
		)
		filters.add_child(button)

	_bag_box.add_child(_make_label("BagHeader", "背包（铜钱 %d）" % int(inventory.money), 18))
	var shown := 0
	for item_id: String in inventory.item_ids():
		var row: Resource = _db.get_row("item_base", item_id)
		if row == null:
			continue
		if not _row_matches_filter(row):
			continue
		shown += 1
		var line := HBoxContainer.new()
		line.name = "ItemRow%s" % item_id
		line.add_theme_constant_override("separation", 8)
		var label := _make_label(
			"ItemLabel%s" % item_id,
			"%s ×%d（%s）%s" % [
				str(row.name_cn), inventory.count(item_id), str(row.rarity), str(row.desc),
			],
			0, _rarity_color(str(row.rarity))
		)
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		var is_key := bool(row.is_key_item)
		# 秘籍（skillbook）能研读；研读才消耗，丢弃照旧被钥匙规则挡住
		if str(row.item_type) == "skillbook":
			var study_check: Dictionary = SkillGrantScript.study_gate(_db, state, item_id)
			# 原因写在标签上、按钮只写「研读」：按钮文字太长会把这行撑变形
			if not bool(study_check["ok"]):
				label.text += "　— 研读：%s" % str(study_check["error"])
			line.add_child(_make_button(
				"StudyButton%s" % item_id,
				"研读",
				func() -> void: study_item(item_id),
				not bool(study_check["ok"]),
			))
		line.add_child(_make_button(
			"DropButton%s" % item_id, "丢弃", func() -> void: drop_item(item_id), is_key
		))
		_bag_box.add_child(line)

	# 装备实例（未穿戴的可以在这里直接装）
	if _filter == "all" or _filter == "equip":
		_bag_box.add_child(_make_label("BagEquipHeader", "装备", 18))
		var panel = sheet()
		for instance_id: String in inventory.equipment_ids():
			var base: Resource = _db.get_row("equip_base", inventory.base_of(instance_id))
			if base == null:
				continue
			shown += 1
			var equipped_by: Dictionary = inventory.equipped_by(instance_id)
			var line := HBoxContainer.new()
			line.name = "EquipRow%s" % instance_id
			line.add_theme_constant_override("separation", 8)
			var suffix := "（已装备）" if not equipped_by.is_empty() else ""
			var affix_text := "" if panel == null else panel.affix_summary_of(instance_id)
			var label := _make_label(
				"EquipLabel%s" % instance_id,
				"%s（%s）%s%s%s" % [
					str(base.name_cn), str(base.rarity), str(base.desc), suffix,
					"" if affix_text.is_empty() else "　词条：%s" % affix_text,
				],
				0, panel.rarity_color(str(base.rarity)) if panel != null else ""
			)
			label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			line.add_child(label)
			if equipped_by.is_empty() and panel != null:
				var check: Dictionary = inventory.can_equip(
					_db, panel.char_id, panel.level, panel.weapon_type_id(), instance_id
				)
				line.add_child(_make_button(
					"BagEquipButton%s" % instance_id, "装备",
					func() -> void: equip_instance(instance_id), not bool(check["ok"])
				))
			_bag_box.add_child(line)

	if shown == 0:
		_bag_box.add_child(_make_label("BagEmpty", "这一分类下没有东西"))


func _rarity_color(rarity_id: String) -> String:
	var row: Resource = _db.get_row("rarity_def", rarity_id)
	return str(row.color) if row != null else "#FFFFFF"


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


# ------------------------------------------------------------------ 环境

func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var db = TableDbScript.new()
	db.load_all()
	return db


func _session_node(node_name: String) -> Node:
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


# ------------------------------------------------------------------ 真实场景自检

func _run_character_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()
	var state = current_state()
	if state == null:
		# 自检**钉成单人队**：这套剧本是"选中的人不够门槛 → 拒绝且不消耗，够门槛才学会并消耗一本"。
		# 而研读是**全队**行为（`SkillGrant` 让队里每个够门槛的人都学会，够的人≥1 就消耗残页）——
		# 用 4 人队跑时，苏九娘（智 14）会先满足条件，于是"应被拒绝"的那一次直接成功、残页被吃掉，
		# 剧本不再成立（2026-10-03 接 4 人队探针时踩到，见框架说明决策 163）。
		state = GameStateScript.new_game(_db, "normal", PackedStringArray(["scholar_fallen"]))
		state_override = state
		_selected_char = str(state.char_ids[0])
		refresh()

	# 1. 切页签
	select_tab(1)
	var equip_tab := current_tab() == 1
	select_tab(2)
	var bag_tab := current_tab() == 2
	select_tab(0)
	ok = ok and equip_tab and bag_tab
	lines.append("切页签 ok=%s" % (equip_tab and bag_tab))

	# 2. 加点
	var panel = sheet()
	var points_before := panel.available_points()
	var str_before := int(panel.total_attrs().get("str", 0))
	var allocated: Dictionary = press_plus("str")
	panel = sheet()
	var str_after := int(panel.total_attrs().get("str", 0))
	var allocate_ok := (
		bool(allocated["ok"])
		and str_after == str_before + 1
		and panel.available_points() == points_before - 1
	)
	ok = ok and allocate_ok
	lines.append("加点 ok=%s（力 %d → %d，剩余 %d）" % [allocate_ok, str_before, str_after, panel.available_points()])

	# 3. 穿脱装备：先卸下武器，再装回去（外功会掉下去又回来）
	var weapon_row: Dictionary = {}
	for row: Dictionary in panel.equipped_rows():
		if row["slot_id"] == "weapon" and not bool(row["empty"]):
			weapon_row = row
			break
	var equip_ok := false
	if not weapon_row.is_empty():
		var instance_id: String = weapon_row["instance_id"]
		var atk_equipped := panel.stat_value("atk_phys")
		var removed: Dictionary = unequip("weapon", int(weapon_row["index"]))
		var atk_removed := sheet().stat_value("atk_phys")
		var equipped: Dictionary = equip_instance(instance_id)
		var atk_again := sheet().stat_value("atk_phys")
		equip_ok = (
			bool(removed["ok"])
			and bool(equipped["ok"])
			and atk_removed < atk_equipped
			and is_equal_approx(atk_again, atk_equipped)
		)
		lines.append("穿脱装备 ok=%s（外功 %.0f → %.0f → %.0f）" % [equip_ok, atk_equipped, atk_removed, atk_again])
	else:
		lines.append("穿脱装备 ok=false（武器槽是空的）")
	ok = ok and equip_ok

	# 4. 武学装配：卸下招式 → 槽位腾出来 → 再装回去（战斗用的是装配，不是模板）
	var loadout = sheet().loadout()
	var first_active := ""
	for skill_id: String in loadout.active_ids():
		first_active = skill_id
		break
	var loadout_ok := false
	if first_active.is_empty():
		lines.append("武学装配 ok=false（招式槽是空的）")
	else:
		var used_before: int = loadout.active_used()
		var removed: Dictionary = toggle_skill(first_active)
		var used_after: int = sheet().loadout().active_used()
		var back: Dictionary = toggle_skill(first_active)
		var used_back: int = sheet().loadout().active_used()
		loadout_ok = (
			bool(removed["ok"]) and not bool(removed["equipped"])
			and used_after == used_before - 1
			and bool(back["ok"]) and bool(back["equipped"])
			and used_back == used_before
		)
		lines.append("武学装配 ok=%s（%s：招式槽 %d → %d → %d）" % [
			loadout_ok, first_active, used_before, used_after, used_back,
		])
	ok = ok and loadout_ok

	# 5. 秘籍研读：门槛不够不消耗，学会才消耗一本
	var char_id := str(state.char_ids[0])
	state.inventory.add_item(_db, "item_scroll_wudu", 1)
	var scrolls_before: int = state.inventory.count("item_scroll_wudu")
	var refused_study: Dictionary = study_item("item_scroll_wudu")
	var study_ok: bool = not bool(refused_study["ok"]) and state.inventory.count("item_scroll_wudu") == scrolls_before
	while int(sheet().total_attrs()["int"]) < 12 and sheet().available_points() > 0:
		press_plus("int")
	var studied: Dictionary = study_item("item_scroll_wudu")
	study_ok = (
		study_ok and bool(studied["ok"])
		and state.is_learned(char_id, "sk_wudu_03")
		and state.inventory.count("item_scroll_wudu") == 0
	)
	ok = ok and study_ok
	lines.append("秘籍研读 ok=%s（智 %d：门槛不足不消耗，学会后消耗一本）" % [
		study_ok, int(sheet().total_attrs()["int"]),
	])

	# 6. 丢钥匙道具必须被拒
	state.inventory.add_item(_db, "item_wine_gourd", 1)
	refresh()
	var dropped: Dictionary = drop_item("item_wine_gourd")
	var drop_ok := not bool(dropped["ok"]) and str(dropped["error"]).contains("钥匙")
	ok = ok and drop_ok
	lines.append("钥匙不可丢弃 ok=%s（%s）" % [drop_ok, str(dropped["error"])])

	# 大背包：把 15 种道具各堆满、再塞一批装备实例，然后才量版式——
	# 背包列表是按存档动态生成的，东西一多就可能顶破版式（发行数据里 item_base 只有 16 行，
	# 「几十件东西」这种局面靠正常游玩很难自然到，专门造出来量一次）。
	var filled := 0
	for item_row: Resource in _db.rows("item_base"):
		if str(item_row.item_type) == "currency":
			continue
		if bool(state.inventory.add_item(_db, str(item_row.item_id), maxi(1, int(item_row.stack_max)))["ok"]):
			filled += 1
	for base_id: String in ["eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01"]:
		for _n in range(5):
			state.inventory.add_equipment(_db, base_id)
	refresh()
	lines.append("大背包：道具 %d 种堆满 + 装备 %d 件" % [filled, state.inventory.equipment_count()])

	# 版式预算：整页最小高度要塞得进设计分辨率（塞不下时内容被屏幕底部裁掉，代码一声不吭）
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 页签内容宽度：**外层版式量不到滚动区里的内容**（`get_combined_minimum_size()` 到 ScrollContainer
	# 就断了，量到的其实是 `_tabs.custom_minimum_size` 那个写死的常量），而横向滚动是关着的——
	# 内容一宽就被裁掉、代码一声不吭。
	# 量法有个坑：TabContainer 只让**当前页**可见，容器又会跳过不可见的子树，所以必须
	# **逐页切过去 + 等一帧**再量；否则量到的是 0，那是「量不出来」，不是「塞得下」。
	for index in range(_tabs.get_tab_count()):
		select_tab(index)
		await _tree().process_frame
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
	print("CHARACTER SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)
