## 商店界面。
##
## 骨架在 `scenes/shop_screen.tscn` 里（节点名即接口，改版式改场景，代码只做绑定），
## 规则与记账全在 `src/core/shop_service.gd`，这里只负责显示与转发。
##
## 入口：清风驿（`scene_qingfengyi`）里走到 `Markers/Buildings/<building_id>` 旁边按 E。
## 自检：`-- --shop-selftest`，买 → 卖 → 回购 → 切页签走一遍并按退出码报告。
extends CanvasLayer

const TableDbScript := preload("res://src/core/table_db.gd")
const CopyGuardScript := preload("res://src/ui/copy_guard.gd")
const GameStateScript := preload("res://src/core/game_state.gd")
const ShopServiceScript := preload("res://src/core/shop_service.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

const QTY_STEPS := [1, 5, 10]
const TABS := [
	{"key": "buy", "name": "买入"},
	{"key": "sell", "name": "卖出"},
	{"key": "buyback", "name": "回购"},
	{"key": "service", "name": "服务"},
]

var state_override = null
## 用例注入的表库（要在真场景里跑「限定货架」这种发行数据用不到的配置）
var db_override = null
var return_handler := Callable()
## 当前店（building_def.building_id）
var building_id: String = "bld_smith"
## 打开界面时停在哪个页签
var tab: String = "buy"
## 买卖数量（材料一次买 10 个很常见）
var qty_index := 0

var db
var state
var shop

var _title: Label
var _money: Label
var _list: VBoxContainer
var _status: Label
var _qty_button: Button
var _return_button: Button
var _tabs_box: HBoxContainer
var _bound := false


func _ready() -> void:
	setup()
	if _has_user_arg("--shop-selftest"):
		call_deferred("_run_shop_selftest")


func setup() -> void:
	if shop != null:
		return
	db = _resolve_db()
	state = current_state()
	var session_node := _session_node()
	if state == null:
		state = GameStateScript.new_game(db, "normal")
		if session_node != null:
			session_node.set_state(state)
	shop = ShopServiceScript.new(db, state)
	_bind_ui()
	refresh()


func current_state():
	if state_override != null:
		return state_override
	var session_node := _session_node()
	return session_node.state if session_node != null else null


## 打开某家店（入口在 local_map_controller 里按 Marker 名传过来）
func open_building(id: String) -> void:
	building_id = id
	tab = "buy"
	if shop != null:
		refresh()


func building_name() -> String:
	return shop.building_name(building_id) if shop != null else building_id


func status_text() -> String:
	return _status.text if _status != null else ""


## 给界面塞一句话（开店时用：「进店：铁匠铺」这类提示）
func show_message(text: String) -> void:
	_set_status(text)


func money_text() -> String:
	return _money.text if _money != null else ""


func quantity() -> int:
	return int(QTY_STEPS[qty_index])


# ------------------------------------------------------------------ 界面

## 骨架在场景里，这里只查找与接线；商品行是按存档状态动态生成的
func _bind_ui() -> void:
	if _bound:
		return
	_bound = true
	_title = _require_node("Panel/Margin/Column/Title") as Label
	_money = _require_node("Panel/Margin/Column/Money") as Label
	_tabs_box = _require_node("Panel/Margin/Column/Tabs") as HBoxContainer
	_list = _require_node("Panel/Margin/Column/Body/List") as VBoxContainer
	_status = _require_node("Panel/Margin/Column/Status") as Label
	_qty_button = _require_node("Panel/Margin/Column/Buttons/QtyButton") as Button
	_return_button = _require_node("Panel/Margin/Column/Buttons/ReturnButton") as Button
	_qty_button.pressed.connect(press_qty)
	_return_button.pressed.connect(press_return)
	for entry: Dictionary in TABS:
		var button := _tabs_box.get_node_or_null("Tab%s" % _tab_node_suffix(str(entry["key"]))) as Button
		if button != null:
			var key := str(entry["key"])
			button.pressed.connect(func() -> void: select_tab(key))
	_status.text = ""


func _tab_node_suffix(key: String) -> String:
	return {"buy": "Buy", "sell": "Sell", "buyback": "Buyback", "service": "Service"}.get(key, key)


## 场景里少一个节点就报出来（别让界面静悄悄缺一块）
func _require_node(path: String) -> Node:
	var node := get_node_or_null(path)
	if node == null:
		push_error("shop_screen.tscn 缺少节点：%s" % path)
		assert(false, "shop_screen.tscn 缺少节点：%s" % path)
	return node


func select_tab(key: String) -> void:
	tab = key
	refresh()


func press_qty() -> int:
	qty_index = (qty_index + 1) % QTY_STEPS.size()
	refresh()
	return quantity()


func press_return() -> void:
	if return_handler.is_valid():
		return_handler.call()
		return
	queue_free()


func _set_status(text: String) -> void:
	if _status != null:
		_status.text = text


func refresh() -> void:
	if shop == null or _list == null:
		return
	_title.text = "%s（%s）" % [building_name(), _building_desc()]
	_money.text = "铜钱 %d　｜　买卖数量 ×%d" % [shop.money(), quantity()]
	_qty_button.text = "数量 ×%d" % quantity()
	_refresh_tabs()
	_clear(_list)
	match tab:
		"sell":
			_rebuild_sell()
		"buyback":
			_rebuild_buyback()
		"service":
			_rebuild_service()
		_:
			_rebuild_buy()


func _building_desc() -> String:
	var row: Resource = shop.building(building_id)
	return str(row.desc) if row != null else ""


func _refresh_tabs() -> void:
	for entry: Dictionary in TABS:
		var button := _tabs_box.get_node_or_null("Tab%s" % _tab_node_suffix(str(entry["key"]))) as Button
		if button != null:
			button.text = "%s%s" % ["▶ " if tab == str(entry["key"]) else "", entry["name"]]


func _rebuild_buy() -> void:
	var rows: Array = shop.stock_rows(building_id)
	if rows.is_empty():
		_list.add_child(_make_label("Empty", "这家店没有货架"))
		return
	for row: Resource in rows:
		var item_id := str(row.item_id)
		if int(row.buy_price) <= 0:
			continue
		var check: Dictionary = shop.can_buy(building_id, item_id, quantity())
		var line := HBoxContainer.new()
		line.name = "BuyRow%s" % item_id
		line.add_theme_constant_override("separation", 8)
		var text := "%s　%d 文/个" % [shop.item_name(item_id), int(row.buy_price)]
		var left: int = shop.remaining(building_id, item_id)
		if left >= 0:
			text += "　（限量：还剩 %d）" % left
		if not bool(check["ok"]):
			text += "　— %s" % str(check["error"])
		var label := _make_label("BuyLabel%s" % item_id, text, 0, _item_color(item_id))
		# 不设 EXPAND：中文在 HBox 里的最小宽度只有一个字，会被挤成一列（踩过）
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		line.add_child(_make_button(
			"BuyButton%s" % item_id,
			"买 %d 个（%d 文）" % [quantity(), int(check["total"])],
			func() -> void: press_buy(item_id),
			not bool(check["ok"]),
		))
		_list.add_child(line)


func _rebuild_sell() -> void:
	var shown := 0
	# 材料与道具
	for item_id: String in state.inventory.item_ids():
		var row: Resource = db.get_row("item_base", item_id)
		if row == null:
			continue
		var unit: int = shop.sell_price(building_id, item_id)
		if unit <= 0:
			continue
		shown += 1
		var count: int = state.inventory.count(item_id)
		var sell_qty := mini(quantity(), count)
		_list.add_child(_make_sell_row(
			item_id, "%s ×%d　卖 %d 文/个" % [str(row.name_cn), count, unit],
			"卖 %d 个（+%d 文）" % [sell_qty, unit * sell_qty],
			bool(row.is_key_item), item_id, sell_qty,
		))
	# 未穿戴的装备实例
	for instance_id: String in state.inventory.equipment_ids():
		if state.inventory.is_equipped(instance_id):
			continue
		var base_id: String = state.inventory.base_of(instance_id)
		var unit: int = shop.sell_price(building_id, base_id)
		if unit <= 0:
			continue
		shown += 1
		var base: Resource = db.get_row("equip_base", base_id)
		var affix := _affix_summary(instance_id)
		_list.add_child(_make_sell_row(
			instance_id,
			"%s（%s）%s　卖 %d 文" % [
				str(base.name_cn), str(base.rarity), affix, unit,
			],
			"卖出（+%d 文）" % unit, false, instance_id, 1,
		))
	if shown == 0:
		_list.add_child(_make_label("Empty", "背包里没有这家店收的东西"))


func _make_sell_row(
	sell_id: String, text: String, button_text: String,
	disabled: bool, node_key: String, _qty: int
) -> Control:
	var line := HBoxContainer.new()
	line.name = "SellRow%s" % node_key
	line.add_theme_constant_override("separation", 8)
	var label := _make_label("SellLabel%s" % node_key, text)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	line.add_child(label)
	line.add_child(_make_button(
		"SellButton%s" % node_key, button_text,
		func() -> void: press_sell(sell_id), disabled,
	))
	return line


func _rebuild_buyback() -> void:
	var rows: Array = shop.buyback_rows(building_id)
	if rows.is_empty():
		_list.add_child(_make_label("Empty", "还没有卖过东西给这家店"))
		return
	for index in range(rows.size()):
		var entry: Dictionary = rows[index]
		var line := HBoxContainer.new()
		line.name = "BuybackRow%d" % index
		line.add_theme_constant_override("separation", 8)
		var cost := int(entry.get("price", 0)) * maxi(1, int(entry.get("qty", 1)))
		var text := "%s ×%d　买回 %d 文" % [
			shop.item_name(str(entry.get("item_id", ""))), int(entry.get("qty", 1)), cost,
		]
		var affixes: Array = Array(entry.get("affixes", []))
		if not affixes.is_empty():
			text += "　词条：%s" % AffixRollerScript.summarize(db, affixes)
		var label := _make_label("BuybackLabel%d" % index, text)
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line.add_child(label)
		line.add_child(_make_button(
			"BuybackButton%d" % index, "买回（%d 文）" % cost,
			func() -> void: press_buyback(index),
		))
		_list.add_child(line)


func _rebuild_service() -> void:
	if not shop.has_heal(building_id):
		_list.add_child(_make_label("Empty", "这家店没有附加服务"))
		return
	var line := HBoxContainer.new()
	line.name = "HealRow"
	line.add_theme_constant_override("separation", 8)
	var preview: Dictionary = shop.heal_preview(building_id)
	var missing := int(preview.get("missing", 0))
	var cost := int(preview.get("cost", 0))
	var heal_label := _make_label(
		"HealLabel",
		"治疗：每点气血 %s 文｜全队缺 %d 点，要 %d 文（费用 = 缺失气血 × 单价）" % [
			str(shop.heal_price(building_id)), missing, cost,
		],
	)
	heal_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	line.add_child(heal_label)
	# 战斗外气血 v11 起进存档：缺多少、要多少钱都算得出来，钱够就能按
	line.add_child(_make_button(
		"HealButton", "治疗", func() -> void: press_heal(), missing <= 0 or shop.money() < cost
	))
	_list.add_child(line)
	for row: Dictionary in Array(preview.get("rows", [])):
		_list.add_child(_make_label(
			"Heal_%s" % str(row["char_id"]),
			"%s　气血 %d / %d%s" % [
				str(row["name"]), int(row["hp"]), int(row["max_hp"]),
				"" if int(row["missing"]) <= 0 else "　缺 %d" % int(row["missing"]),
			],
		))
	if missing <= 0:
		_list.add_child(_make_label("HealNote", "全队气血是满的。"))
	elif shop.money() < cost:
		_list.add_child(_make_label("HealNote", "铜钱不够：要 %d 文。" % cost))


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


func _make_button(node_name: String, text: String, pressed: Callable, disabled: bool = false) -> Button:
	var button := Button.new()
	button.name = node_name
	button.text = text
	button.disabled = disabled
	button.pressed.connect(pressed)
	return button


func _item_color(item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		var rarity: Resource = db.get_row("rarity_def", str(equip.rarity))
		return str(rarity.color) if rarity != null else ""
	var item: Resource = db.get_row("item_base", item_id)
	if item != null:
		var item_rarity: Resource = db.get_row("rarity_def", str(item.rarity))
		return str(item_rarity.color) if item_rarity != null else ""
	return ""


func _affix_summary(instance_id: String) -> String:
	return AffixRollerScript.summarize(db, state.inventory.affixes_of(instance_id))


# ------------------------------------------------------------------ 操作

func press_buy(item_id: String) -> Dictionary:
	var result: Dictionary = shop.buy(building_id, item_id, quantity())
	_set_status(
		"买入 %s ×%d，花掉 %d 文" % [shop.item_name(item_id), quantity(), int(result["spent"])]
		if result["ok"] else str(result["error"])
	)
	refresh()
	return result


func press_sell(sell_id: String) -> Dictionary:
	var qty := 1 if state.inventory.has_equipment(sell_id) else mini(quantity(), state.inventory.count(sell_id))
	var result: Dictionary = shop.sell(building_id, sell_id, maxi(1, qty))
	_set_status(
		"卖出，收入 %d 文" % int(result["earned"]) if result["ok"] else str(result["error"])
	)
	refresh()
	return result


func press_buyback(index: int) -> Dictionary:
	var result: Dictionary = shop.buy_back(building_id, index)
	if not bool(result["ok"]):
		_set_status(str(result["error"]))
	else:
		# 背包快满时只买回一部分：写清买到几个、剩下的还在列表里（别让玩家以为全买回来了）
		var text := "买回 %s ×%d，花掉 %d 文" % [
			shop.item_name(str(result.get("item_id", ""))), int(result.get("received", 1)), int(result["cost"]),
		]
		if bool(result.get("partial", false)):
			text += "（背包放不下，剩下的还留在回购列表）"
		_set_status(text)
	refresh()
	return result


func press_heal() -> Dictionary:
	var result: Dictionary = shop.heal_party(building_id)
	if bool(result["ok"]):
		_set_status("治疗完成：回 %d 点气血，花 %d 文" % [int(result["healed"]), int(result["cost"])])
	else:
		_set_status(str(result["error"]))
	refresh()
	return result


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

## 真实场景自检：换页签 → 买 → 卖 → 回购，账单要对得上
func _run_shop_selftest() -> void:
	var tree := _tree()
	if tree != null:
		await tree.process_frame
		await tree.process_frame
	var ok := true
	var lines := PackedStringArray()
	open_building("bld_smith")
	state.inventory.money = 1000
	refresh()
	lines.append("店：%s　铜钱 %d" % [building_name(), shop.money()])

	# 1. 四个页签都切一遍
	for entry: Dictionary in TABS:
		select_tab(str(entry["key"]))
	select_tab("buy")
	ok = ok and tab == "buy"
	lines.append("页签切换 ok=%s" % (tab == "buy"))

	# 2. 买入白板剑
	var money_before: int = shop.money()
	var bought: Dictionary = press_buy("eq_sword_01")
	ok = ok and bool(bought["ok"]) and shop.money() == money_before - int(bought["spent"])
	var bought_instance := ""
	for instance_id: String in state.inventory.equipment_ids():
		if state.inventory.base_of(instance_id) == "eq_sword_01":
			bought_instance = instance_id
	lines.append("买入 ok=%s（花 %d 文，余 %d）" % [bool(bought["ok"]), int(bought["spent"]), shop.money()])

	# 3. 卖回去（白板剑在铁匠铺的货架上）
	var earned := 0
	if not bought_instance.is_empty():
		var sold: Dictionary = press_sell(bought_instance)
		ok = ok and bool(sold["ok"])
		earned = int(sold["earned"])
		lines.append("卖出 ok=%s（收 %d 文）" % [bool(sold["ok"]), earned])
	else:
		ok = false
		lines.append("卖出 ok=false（没买到剑）")

	# 4. 回购列表买回来
	select_tab("buyback")
	var back: Dictionary = press_buyback(shop.buyback_rows(building_id).size() - 1)
	ok = ok and bool(back["ok"]) and int(back["cost"]) == earned
	lines.append("回购 ok=%s（花 %d 文，应等于卖出价 %d）" % [bool(back["ok"]), int(back["cost"]), earned])

	# 5. 治疗页：先打伤一名队员，再按「治疗」看它真的回血（战斗外气血 v11 起进存档）
	open_building("bld_clinic")
	select_tab("service")
	var char_id := str(state.char_ids[0])
	var cap: int = shop.heal_preview(building_id).get("rows", [])[0]["max_hp"]
	state.set_current_hp(char_id, cap - 30)
	state.inventory.money = maxi(state.inventory.money, 200)
	select_tab("service")
	var heal_button: Button = find_child("HealButton", true, false)
	ok = ok and heal_button != null and not heal_button.disabled
	var healed: Dictionary = press_heal()
	ok = ok and bool(healed["ok"]) and int(healed["healed"]) == 30 and int(healed["cost"]) == 60
	ok = ok and not state.is_wounded(char_id)
	lines.append("治疗页 ok=%s（缺 30 点 → 花 %d 文，回 %d 点，治完满血=%s）" % [
		bool(healed["ok"]), int(healed["cost"]), int(healed["healed"]),
		not state.is_wounded(char_id),
	])
	# 钱不够时「治疗」按钮要置灰、行下写明还差多少（不是「能点、点了才报错」）
	state.set_current_hp(char_id, cap - 30)
	state.inventory.money = 59
	select_tab("service")
	var poor_heal: Button = find_child("HealButton", true, false)
	var poor_note: Label = find_child("HealNote", true, false)
	var poor_ui: bool = (
		poor_heal != null and poor_heal.disabled
		and poor_note != null and poor_note.text.contains("铜钱不够")
	)
	ok = ok and poor_ui
	lines.append("缺钱时治疗按钮置灰且写明原因=%s（%s）" % [
		poor_ui, str(poor_note.text) if poor_note != null else "-",
	])
	state.inventory.money = maxi(state.inventory.money, 200)
	select_tab("service")
	# 版式预算：整页最小高度要塞得进设计分辨率，塞不下时内容会被屏幕底部裁掉而代码一声不吭
	ok = ok and LayoutBudgetScript.fits(self)
	lines.append(LayoutBudgetScript.ascii_line(self))
	ok = ok and LayoutBudgetScript.has_opaque_backdrop(self)
	lines.append(LayoutBudgetScript.ascii_backdrop_line(self))
	# 滚动区内容宽度：横滚是关着的，清单一行宽了就被裁掉——而上面那行 LAYOUT 只量到面板外壳
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
	print("SHOP SELF-TEST: %s" % ("OK" if ok else "FAILED"))
	if tree != null:
		tree.quit(0 if ok else 1)
