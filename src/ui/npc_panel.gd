## NPC 交往面板（设计 19 §三）：基本信息 ＋ 交互菜单（赠送／切磋／偷窃／兑换／任务）。
##
## 19 号要求「信息面板与交互菜单分开：按一个键看信息，按另一个键进入交互」——
## 两个键各是哪个设计没写（`待策划确认.md` Q62），所以这里按「一个面板、信息在最上面」实现：
## **看信息不需要点任何一次有风险的操作**（打开就能看见），交互项在下面分开列。
## 等设计点完键位，把信息那一段抽成独立浮层即可（一份数据两处渲染）。
##
## 界面用代码搭（占位美术阶段的既有约定）。所有规则在 `NpcService` 里，这里只显示与转发。
extends Control

const NpcServiceScript := preload("res://src/core/npc_service.gd")
## 对话容器（设计 20 §十一）：台词与选项都在 `dialogue_node`／`dialogue_option` 里
const DialogueServiceScript := preload("res://src/core/dialogue_service.gd")
const TableDbScript := preload("res://src/core/table_db.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

## 自检与用例的注入点
var state_override = null
var npc_id: String = ""
## 两种模式（设计 0.28.0 的 Q62：**E 进交互、Q 看信息**）：
##   `info`     = **只读**信息面板：名字／称号／等级／好感／介绍，Esc 返回，**不带任何副作用**；
##   `interact` = 交互菜单：赠送／切磋／偷窃／兑换／任务。
const MODE_INFO := "info"
const MODE_INTERACT := "interact"
## 第三种模式（2026-10-04）：「旁白／自白」——**只有台词与选项**，没有「这个人」。
##
## 为什么需要它：设计 20 号 §3.1 的**序幕·择念**写的是主角自己心里那一句
## （原话：「这一句，是主角第一次**替自己**开口，而不是接 NPC 的话」），
## 而交往面板的框里全是「这个人是谁、好感多少、能不能偷」——把主角的自白套进去，
## 等于给他安了一张好感条与一条偷窃难度。所以这里按数据同一套（`dialogue_node`／
## `dialogue_option`）渲染，只把「人」那一段整个收起来。
const MODE_STORY := "story"
var mode: String = MODE_INTERACT
var return_handler := Callable()
## 切磋要开一场仗——由场景控制器接（它管遭遇与切场景）
var spar_handler := Callable()
var rng_override = null
## 击杀方式（`npc_quest` 的 `kill_style:` 条件要它；调用方从会话的上一场战斗里取）
var kill_styles: Dictionary = {}
## 当前停在哪个对话节点（空 = 从头开始；面板每次 `refresh()` 都按它取那一句）
var dialogue_node_id: String = ""

var db
var _name_label: Label
var _info_label: Label
var _favor_label: Label
## 头像：**原生 32×32、显示 2 倍到 64×64**，放左上角，与名字／称号／等级／好感同排
## （设计 14 §「NPC 信息面板的结构」——这几项是「这人是谁」的一组信息，不要拆到两处）。
var _avatar: TextureRect = null
var _status: Label
var _actions: VBoxContainer
var _rng = null


func _ready() -> void:
	setup()
	if _has_user_arg("--npc-selftest"):
		call_deferred("_run_npc_selftest")


## 幂等：用例可以直接调
func setup() -> void:
	if db != null:
		return
	db = _resolve_db()
	# 没有会话状态时**自己开一局**（与 `clue_screen`／`character_screen` 同一套）：
	# 面板要读好感、委托、事件判定，全都挂在 `GameState` 上；state 为 null 时
	# 那些调用会报 `Nil` 的运行期错误，而面板看起来还是开着的（自检会红在日志门限上）。
	if current_state() == null:
		var session := _session_node("GameSession")
		if session != null:
			session.set_state(load("res://src/core/game_state.gd").new_game(db, "normal"))
		else:
			state_override = load("res://src/core/game_state.gd").new_game(db, "normal")
	_build_ui()
	refresh()


func _resolve_db():
	var game_data := _session_node("GameData")
	if game_data != null and game_data.db != null and not game_data.db.tables.is_empty():
		return game_data.db
	var table_db = TableDbScript.new()
	table_db.load_all()
	return table_db


func _session_node(node_name: String) -> Node:
	var tree := _tree()
	return tree.root.get_node_or_null(node_name) if tree != null else null


func _tree() -> SceneTree:
	if is_inside_tree():
		return get_tree()
	return Engine.get_main_loop() as SceneTree


func current_state():
	if state_override != null:
		return state_override
	var session := _session_node("GameSession")
	return session.state if session != null else null


func rng():
	if rng_override != null:
		return rng_override
	if _rng == null:
		_rng = RngServiceScript.new()
	return _rng


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		leave()


func leave() -> void:
	if return_handler.is_valid():
		return_handler.call()


# ------------------------------------------------------------------ 界面

func _build_ui() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var bg := ColorRect.new()
	bg.name = "Backdrop"
	bg.color = Color(0.06, 0.07, 0.08, 0.97)   # 盖在场景上的面板要够不透明（版式预算也会查）
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var margin := MarginContainer.new()
	margin.name = "Margin"
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side: String in ["left", "right"]:
		margin.add_theme_constant_override("margin_%s" % side, 32)
	for side: String in ["top", "bottom"]:
		margin.add_theme_constant_override("margin_%s" % side, 24)
	add_child(margin)
	var column := VBoxContainer.new()
	column.name = "Column"
	margin.add_child(column)

	var head := HBoxContainer.new()
	head.name = "Head"
	head.add_theme_constant_override("separation", 10)
	column.add_child(head)
	_avatar = TextureRect.new()
	_avatar.name = "Avatar"
	_avatar.custom_minimum_size = Vector2(64, 64)
	_avatar.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_avatar.stretch_mode = TextureRect.STRETCH_SCALE
	_avatar.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	head.add_child(_avatar)
	var who := VBoxContainer.new()
	who.name = "Who"
	who.add_theme_constant_override("separation", 2)
	head.add_child(who)
	_name_label = Label.new()
	_name_label.name = "NameLabel"
	_name_label.add_theme_font_size_override("font_size", 16)
	who.add_child(_name_label)
	_favor_label = Label.new()
	_favor_label.name = "FavorLabel"
	who.add_child(_favor_label)
	_info_label = Label.new()
	_info_label.name = "InfoLabel"
	_info_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_info_label.custom_minimum_size = Vector2(720, 0)
	column.add_child(_info_label)

	var scroll := ScrollContainer.new()
	scroll.name = "Scroll"
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(scroll)
	_actions = VBoxContainer.new()
	_actions.name = "Actions"
	_actions.custom_minimum_size = Vector2(720, 0)
	scroll.add_child(_actions)

	_status = Label.new()
	_status.name = "Status"
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.custom_minimum_size = Vector2(720, 0)
	column.add_child(_status)
	var back := Button.new()
	back.name = "BackButton"
	back.text = "离开（Esc）"
	back.custom_minimum_size = Vector2(200, 40)
	back.pressed.connect(leave)
	column.add_child(back)


func refresh() -> void:
	# 旁白／自白模式（序幕择念）：**没有「这个人」**——不查身份、不摆好感条与交往段
	if mode == MODE_STORY:
		_refresh_story()
		return
	# 「人」的统一取法：NPC 或**同伴**（同伴没有 npc_def 行，服务层按同一形状合成一行）
	var def: Resource = NpcServiceScript.person_of(db, current_state(), npc_id)
	if def == null:
		_name_label.text = "（不认识这个人）"
		_info_label.text = ""
		_favor_label.text = ""
		return
	var favor := NpcServiceScript.favor_of(db, current_state(), npc_id)
	# 名字/称号/等级/好感是**一组**信息（14 §「NPC 信息面板的结构」）
	_name_label.text = "%s　%s　Lv.%d" % [str(def.name_cn), str(def.title_cn), int(def.level)]
	_favor_label.text = "好感 %d　%s" % [favor, _favor_bar(favor)]
	_info_label.text = str(def.info_text_cn)
	_refresh_avatar()
	# 先摘再 queue_free：队列里的旧条目要到帧末才走，名字会撞（见 creation_screen._rebuild_body 的注释）
	for child in _actions.get_children():
		_actions.remove_child(child)
		child.queue_free()
	# 只读模式（Q 看信息）：只有上面那一组信息 ＋ 介绍，**不给任何交互项**
	if mode == MODE_INFO:
		_actions.add_child(_section("（只读信息面板：按 E 才是与他打交道）"))
		return
	_add_talk_section()
	_add_gift_section()
	_add_spar_section()
	_add_steal_section()
	_add_offer_section()
	_add_quest_section()


## 「聊聊」（设计 20 §十一 的对话容器）：**台词与选项都从表里来**，面板只呈现与转发。
##
## 旁白／自白模式（`MODE_STORY`，序幕择念用它）：数据同一套，但把「人」那一段整个收起来。
## 台词取 `dialogue_node_id` 指的那一条，选项仍走 `DialogueService.options_for`（条件照判）。
func _refresh_story() -> void:
	var state = current_state()
	_name_label.text = ""
	_info_label.text = ""
	_favor_label.text = ""
	if _avatar != null:
		_avatar.visible = false
	# 先摘再 queue_free：队列里的旧条目要到帧末才走，名字会撞（见 creation_screen._rebuild_body 的注释）
	for child in _actions.get_children():
		_actions.remove_child(child)
		child.queue_free()
	var node: Resource = DialogueServiceScript.node_of(db, dialogue_node_id)
	if node == null:
		dialogue_node_id = ""
		if _status != null:
			_status.text = "这一段现在不说了（条件没到）"
		return
	_actions.add_child(_section(str(node.text_cn)))
	var options: Array = DialogueServiceScript.options_for(db, state, str(node.node_id))
	if options.is_empty():
		_actions.add_child(_section("（没有可选的）"))
		return
	for option: Resource in options:
		var button := Button.new()
		button.name = "TalkOption%s" % str(option.option_id)
		button.text = str(option.text_cn)
		button.custom_minimum_size = Vector2(420, 36)
		button.pressed.connect(func() -> void: choose_dialogue(str(option.option_id)))
		_actions.add_child(button)


## 「聊聊」（设计 20 §十一 的对话容器）：**台词与选项都从表里来**，面板只呈现与转发。
##
## 形状：先说这个人的那一句（`dialogue_node.text_cn`），下面是他现在能选的选项
## （`dialogue_option`，按 `condition` 过滤——设计 20 §四 的「前置」就写在那儿）。
## 选完 `dialogue_node_id` 跟着 `next_node_id` 走；空 = 这次谈完了。
func _add_talk_section() -> void:
	var state = current_state()
	if not DialogueServiceScript.has_dialogue(db, npc_id):
		return
	var node: Resource = DialogueServiceScript.node_of(db, dialogue_node_id)
	if node == null or str(node.speaker_id) != npc_id:
		node = DialogueServiceScript.entry_node(db, state, npc_id)
		dialogue_node_id = str(node.node_id) if node != null else ""
	if node == null:
		# 有行、但这句现在不该说（条件是旗标）→ 如实说，不装成"没什么可说"
		_actions.add_child(_section("他现在不想谈这件事（条件没到）"))
		return
	# 说话人名字也走 `person_of`：同伴没有 npc_def 行，用旧取法会把 `ch_ci` 这种 id 印给玩家
	# （CopyGuard 当场抓到过——文案里出现表内 id 就是 bug）。
	var def: Resource = NpcServiceScript.person_of(db, current_state(), npc_id)
	var speaker_name := str(def.name_cn) if def != null else npc_id
	_actions.add_child(_section("%s：%s" % [speaker_name, str(node.text_cn)]))
	var options: Array = DialogueServiceScript.options_for(db, state, str(node.node_id))
	if options.is_empty():
		_actions.add_child(_section("（他没别的话了）"))
		return
	for option: Resource in options:
		var button := Button.new()
		button.name = "TalkOption%s" % str(option.option_id)
		button.text = str(option.text_cn)
		button.custom_minimum_size = Vector2(420, 36)
		button.pressed.connect(func() -> void: choose_dialogue(str(option.option_id)))
		_actions.add_child(button)


## 选一句话：转发给 `DialogueService`（效果全在那边结算），然后刷新到下一句。
func choose_dialogue(option_id: String) -> Dictionary:
	var result: Dictionary = DialogueServiceScript.choose(db, current_state(), option_id)
	if bool(result.get("ok", false)):
		dialogue_node_id = str(result.get("next_node_id", ""))
		_status.text = "「%s」" % str(result.get("text", ""))
	else:
		_status.text = str(result.get("error", "这句话现在不能说"))
	refresh()
	return result


## 好感条：10 格（19 §三要求「必须显示好感度与条形」——玩家要判断还差多少能换东西）
func _favor_bar(favor: int) -> String:
	var row: Resource = NpcServiceScript.favor_row_of(db, npc_id)
	var cap := maxi(1, int(row.favor_max) if row != null else 100)
	var filled := int(round(float(favor) / float(cap) * 10.0))
	var out := ""
	for i in range(10):
		out += "█" if i < filled else "░"
	return out


## 头像：按 `npc_id` 取 32×32 的 NPC 头像（美术出图后同名替换）。
##
## **同伴没有那张图**（美术只出过七个 NPC 的），退到**立绘剪影**——同一张脸，不留空白框；
## 两者长宽比不同，所以那种情况按比例居中而不是拉伸。两样都没有时才如实写「（头像待出）」。
func _refresh_avatar() -> void:
	if _avatar == null:
		return
	var avatar_file := "res://assets/sprites/avatars/avatar_%s.png" % npc_id
	if ResourceLoader.exists(avatar_file):
		_avatar.stretch_mode = TextureRect.STRETCH_SCALE
		_avatar.texture = load(avatar_file)
		_avatar.tooltip_text = ""
		return
	var portrait_file := "res://assets/sprites/portraits/portrait_%s.png" % npc_id
	if ResourceLoader.exists(portrait_file):
		_avatar.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		_avatar.texture = load(portrait_file)
		_avatar.tooltip_text = ""
		return
	_avatar.stretch_mode = TextureRect.STRETCH_SCALE
	_avatar.texture = null
	_avatar.tooltip_text = "（头像待出）"


func avatar_path() -> String:
	return "res://assets/sprites/avatars/avatar_%s.png" % npc_id


func _section(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", 12)
	return label


func _add_gift_section() -> void:
	var state = current_state()
	if state == null:
		return
	_actions.add_child(_section("赠送（送对喜好加得多）"))
	var shown := 0
	for item_id: String in state.inventory.item_ids():
		var row: Resource = db.get_row("item_base", item_id)
		if row == null or str(row.item_type) == "currency" or str(row.item_type) == "key":
			continue        # 钱不能送；钥匙道具是任务物（19 §2.3 的口径）
		if shown >= 12:
			break
		shown += 1
		var button := Button.new()
		button.name = "Gift_%s" % item_id
		button.text = "赠送 %s（持有 %d）" % [str(row.name_cn), state.inventory.count(item_id)]
		button.custom_minimum_size = Vector2(420, 36)
		button.pressed.connect(func() -> void: gift(item_id))
		_actions.add_child(button)
	if shown == 0:
		_actions.add_child(_section("（背包里没有能送的东西）"))


func _add_spar_section() -> void:
	var team := NpcServiceScript.spar_team(db, npc_id)
	if team.is_empty():
		return
	var row: Resource = NpcServiceScript.favor_row_of(db, npc_id)
	var gain := int(row.spar_favor) if row != null else 0
	var button := Button.new()
	button.name = "SparButton"
	button.text = "切磋（赢了好感 +%d）" % gain
	button.custom_minimum_size = Vector2(420, 36)
	button.pressed.connect(func() -> void: spar())
	_actions.add_child(button)


func _add_steal_section() -> void:
	var steals: Array = NpcServiceScript.offers_of(db, npc_id, "steal")
	if steals.is_empty():
		return
	_actions.add_child(_section("偷窃（走敏／运判定；失败扣好感）"))
	for offer: Resource in steals:
		var button := Button.new()
		button.name = "Steal_%s" % str(offer.offer_id)
		button.text = "偷 %s" % _item_name(str(offer.item_id))
		button.custom_minimum_size = Vector2(420, 36)
		button.pressed.connect(func() -> void: steal(str(offer.offer_id)))
		_actions.add_child(button)


func _add_offer_section() -> void:
	var offers: Array = NpcServiceScript.offers_of(db, npc_id, "offer")
	if offers.is_empty():
		return
	_actions.add_child(_section("兑换（需要好感，有的还要钱）"))
	for offer: Resource in offers:
		var price := int(offer.price)
		var button := Button.new()
		button.name = "Offer_%s" % str(offer.offer_id)
		button.text = "兑换 %s（好感 %d%s）" % [
			_item_name(str(offer.item_id)), int(offer.favor_required),
			"" if price <= 0 else "，%d 文" % price,
		]
		button.custom_minimum_size = Vector2(420, 36)
		button.pressed.connect(func() -> void: redeem(str(offer.offer_id)))
		_actions.add_child(button)


func _add_quest_section() -> void:
	var quest: Resource = NpcServiceScript.available_quest(
		db, current_state(), npc_id, {"kill_styles": kill_styles}
	)
	if quest == null:
		return
	_actions.add_child(_section("委托：%s" % str(quest.text_cn)))
	var button := Button.new()
	button.name = "QuestButton"
	button.text = "交委托（好感 +%d）" % int(quest.reward_favor)
	button.custom_minimum_size = Vector2(420, 36)
	button.pressed.connect(func() -> void: complete_quest(str(quest.quest_id)))
	_actions.add_child(button)


func _item_name(item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		return str(equip.name_cn)
	var item: Resource = db.get_row("item_base", item_id)
	return str(item.name_cn) if item != null else item_id


# ------------------------------------------------------------------ 动作（全部转发给 NpcService）

func gift(item_id: String) -> Dictionary:
	var result: Dictionary = NpcServiceScript.gift(db, current_state(), npc_id, item_id)
	_status.text = str(result["text"]) if bool(result["ok"]) else str(result["error"])
	# 「还给谁加了好感」要说出来（设计 20 号 §九 #8：孙掌柜那封信是给燕小七的）——
	# 不然玩家只看见自己这条好感涨了，不知道别人那边也动了。
	var extra: Array = Array(result.get("extra_favor", []))
	if bool(result.get("ok", false)) and not extra.is_empty():
		_status.text += "　（%s）" % "、".join(extra)
	refresh()
	return result


func spar() -> Dictionary:
	var team := NpcServiceScript.spar_team(db, npc_id)
	if team.is_empty():
		_status.text = "这个人不切磋"
		return {"ok": false, "error": "no_spar"}
	if not spar_handler.is_valid():
		_status.text = "这里不能切磋（没有战斗入口）"
		return {"ok": false, "error": "no_handler"}
	spar_handler.call(npc_id, team)
	return {"ok": true, "team_id": team}


func steal(offer_id: String) -> Dictionary:
	var offer: Resource = db.get_row("npc_offer", offer_id)
	if offer == null:
		return {"ok": false, "error": "没有这个条目"}
	var result: Dictionary = NpcServiceScript.steal(
		db, current_state(), npc_id, str(offer.item_id), float(rng().randf())
	)
	_status.text = str(result["text"]) if bool(result["ok"]) else str(result["error"])
	refresh()
	return result


func redeem(offer_id: String) -> Dictionary:
	var result: Dictionary = NpcServiceScript.redeem(db, current_state(), offer_id)
	_status.text = str(result["text"]) if bool(result["ok"]) else str(result["error"])
	refresh()
	return result


func complete_quest(quest_id: String) -> Dictionary:
	var result: Dictionary = NpcServiceScript.complete_quest(
		db, current_state(), quest_id, {"kill_styles": kill_styles}
	)
	_status.text = str(result["text"]) if bool(result["ok"]) else str(result["error"])
	refresh()
	return result


func status_text() -> String:
	return _status.text if _status != null else ""


## 面板自己的自检钩子（真实场景那一条在 `--local-selftest` 的「NPC 面板」一段）
func backdrop_ok() -> bool:
	return LayoutBudgetScript.has_opaque_backdrop(self)


func copy_hits() -> PackedStringArray:
	return CopyGuardScript.id_tokens(self)


func _has_user_arg(flag: String) -> bool:
	return OS.get_cmdline_user_args().has(flag)


# ------------------------------------------------------------------ 自检

## 真实场景自检：开一个可交往的 NPC，走一遍「看信息 → 赠送 → 偷窃 → 兑换 → 委托」，
## 每一步都要有说法（成功或「条件不够」），不许静默。
func _run_npc_selftest() -> void:
	var ok := true
	var lines := PackedStringArray()
	var state = current_state()
	if state == null:
		var session := _session_node("GameSession")
		if session != null:
			session.set_state(load("res://src/core/game_state.gd").new_game(db, "normal"))
			state = session.state
	ok = ok and state != null
	# 自检挑一个**能送、能偷、能换、有委托**的人：钱大夫（医馆）
	npc_id = "npc_qian_dafu"
	refresh()
	lines.append("信息面板：%s（好感 %d）" % [_name_label.text, NpcServiceScript.favor_of(db, state, npc_id)])
	ok = ok and not _info_label.text.is_empty()
	ok = ok and _favor_label.text.contains("█") or _favor_label.text.contains("░")
	# 头像（设计 14：原生 32×32、显示 2 倍到 64×64，与名字／称号／等级同排）
	var avatar_ok: bool = _avatar != null \
		and _avatar.custom_minimum_size == Vector2(64, 64) \
		and _avatar.texture != null \
		and _name_label.text.contains("Lv.")
	ok = ok and avatar_ok
	lines.append("头像 64×64 且与名字/称号/等级同排=%s（%s）" % [avatar_ok, avatar_path()])

	# 赠送：给一件他喜欢的东西（`like_item_ids` 里有草药）
	state.inventory.add_item(db, "item_herb", 1)
	var before := NpcServiceScript.favor_of(db, state, npc_id)
	var gifted: Dictionary = gift("item_herb")
	ok = ok and bool(gifted["ok"]) and NpcServiceScript.favor_of(db, state, npc_id) > before
	lines.append("赠送 ok=%s（好感 %d → %d）：%s" % [
		gifted["ok"], before, NpcServiceScript.favor_of(db, state, npc_id), status_text(),
	])
	# 送空背包里的东西：要被拒且给说法
	var refused: Dictionary = gift("item_iron")
	ok = ok and not bool(refused["ok"]) and not status_text().is_empty()
	lines.append("送没有的东西被拒=%s（%s）" % [not bool(refused["ok"]), status_text()])

	# 偷窃：判定是 `掷点 ≤ 成功率`——掷 0 必成功、掷 1 必失败（固定掷点，用例可复现）
	var steal_win: Dictionary = NpcServiceScript.steal(db, state, npc_id, "item_potion_small", 0.0)
	ok = ok and bool(steal_win["ok"]) and bool(steal_win["success"])
	ok = ok and state.inventory.count("item_potion_small") >= 1
	lines.append("偷窃得手：%s" % str(steal_win["text"]))
	var steal_fail: Dictionary = NpcServiceScript.steal(db, state, npc_id, "item_potion_small", 1.0)
	ok = ok and bool(steal_fail["ok"]) and not bool(steal_fail["success"])
	lines.append("偷窃失败有说法（扣好感）：%s" % str(steal_fail["text"]))

	# 兑换：好感不够时被拒（药王符要 80），够了才换得动
	var poor: Dictionary = redeem("of_qian_acc")
	ok = ok and not bool(poor["ok"]) and status_text().contains("好感")
	lines.append("好感不够时兑换被拒=%s（%s）" % [not bool(poor["ok"]), status_text()])

	# 委托：条件没达成时不显示可交的委托（钱大夫那条要 `flag_qiutu_saved`）
	var quest: Resource = NpcServiceScript.available_quest(db, state, npc_id)
	ok = ok and quest == null
	lines.append("条件没达成时没有可交的委托=%s" % (quest == null))
	state.set_flag("flag_qiutu_saved")
	refresh()
	quest = NpcServiceScript.available_quest(db, state, npc_id)
	ok = ok and quest != null
	if quest != null:
		var done: Dictionary = complete_quest(str(quest.quest_id))
		ok = ok and bool(done["ok"]) and state.has_flag("npc_quest_done_%s" % str(quest.quest_id))
		lines.append("交委托 ok=%s（好感 %d）：%s" % [done["ok"], int(done["favor"]), status_text()])

	# 版式与背板（面板自检的既有两条）+ 文案守卫
	# 对话容器（设计 20 §十一）：陈氏那条是照 20 号 §四 幕三 落的（她说的话 ＋ 三个选项）
	npc_id = "npc_huangcun"
	dialogue_node_id = ""
	refresh()
	var crouch: Button = _actions.find_child("TalkOptionopt_chen_crouch", true, false)
	var talk_ok: bool = DialogueServiceScript.has_dialogue(db, npc_id) and crouch != null
	ok = ok and talk_ok
	lines.append("对话：台词与选项按表渲染=%s（按钮：%s）" % [
		talk_ok, crouch.text if crouch != null else "缺",
	])
	var favor_before := NpcServiceScript.favor_of(db, state, npc_id)
	# 陈氏初始好感是 −10（表里就这么写的）：先把好感抬到正数，才验得动「−2」这一步；
	# 顺带钉住**负数不会被吃掉**（以前 `favor_of` 把好感夹在 [0, cap]，−10 一读就是 0）。
	ok = ok and favor_before == NpcServiceScript.FAVOR_MIN
	lines.append("负好感读得出来（陈氏初始 %d，地板 %d）=%s" % [
		favor_before, NpcServiceScript.FAVOR_MIN, favor_before == NpcServiceScript.FAVOR_MIN,
	])
	NpcServiceScript.add_favor(db, state, npc_id, 12)
	favor_before = NpcServiceScript.favor_of(db, state, npc_id)
	var chosen: Dictionary = choose_dialogue("opt_chen_press")
	var effect_ok: bool = bool(chosen["ok"]) and state.has_flag("heart_mou") \
		and NpcServiceScript.favor_of(db, state, npc_id) == favor_before - 2
	ok = ok and effect_ok
	lines.append("对话选项效果（置 heart_mou ＋ 好感 %d→%d）=%s" % [
		favor_before, NpcServiceScript.favor_of(db, state, npc_id), effect_ok,
	])

	# 同伴也是「人」（设计 20 §十）：他们没有 npc_def 行，`person_of()` 按同一形状合成一行；
	# 面板照常显示名字／称号／立绘，并且能谈（幕一 燕小七那四个选项）。
	npc_id = "ch_ci"
	dialogue_node_id = ""
	refresh()
	var companion_name: String = str(db.get_row("character_base", npc_id).name_cn)
	var pay: Button = _actions.find_child("TalkOptionopt_ci_pay", true, false)
	var companion_ok: bool = _name_label.text.contains(companion_name) \
		and _avatar.texture != null and pay != null
	ok = ok and companion_ok
	lines.append("同伴可交往（%s：名字／立绘／聊聊）=%s（%s）" % [
		npc_id, companion_ok, _name_label.text,
	])
	if pay != null:
		var favor0 := NpcServiceScript.favor_of(db, state, npc_id)
		var talked: Dictionary = choose_dialogue("opt_ci_pay")
		var companion_effect: bool = bool(talked["ok"]) \
			and NpcServiceScript.favor_of(db, state, npc_id) == favor0 + 5
		ok = ok and companion_effect
		lines.append("同伴对话效果（好感 %d→%d）=%s" % [
			favor0, NpcServiceScript.favor_of(db, state, npc_id), companion_effect,
		])

	# 版式与背板（面板自检的既有两条）+ 文案守卫
	ok = ok and backdrop_ok()
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	var copy: PackedStringArray = copy_hits()
	ok = ok and copy.is_empty()
	lines.append(CopyGuardScript.ascii_line(self))
	if not copy.is_empty():
		# 命中时把 token 打出来（`overworld_controller` 的自检也是这么干的）——
		# 光报一个数字，排查要重跑一遍才知道是哪个控件。
		lines.append("COPY 命中：%s" % "；".join(copy))
	for line: String in lines:
		print("  " + line)
	print("NPC SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	var tree := _tree()
	if tree != null:
		tree.quit(0 if ok else 1)
