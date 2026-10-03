## 角色与行囊界面：真实场景、真实按钮。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const LayoutBudgetScript := preload("res://src/ui/layout_budget.gd")

const UI_SCENE := "res://scenes/character_screen.tscn"
const CHAR_ID := "scholar_fallen"


func suite_name() -> String:
	return "角色与行囊界面"


func run() -> void:
	if scene_tree == null:
		fail("没有注入场景树，界面用例无法进行")
		return
	var db = get_db()
	var state = solo_state(db)
	var screen = load(UI_SCENE).instantiate()
	screen.state_override = state
	var back_calls: Array = []
	screen.back_handler = func() -> void: back_calls.append(true)
	scene_tree.root.add_child(screen)
	screen.setup()

	_check_tabs(screen)
	_check_character_tab(db, screen, state)
	_check_skill_loadout(screen, state)
	_check_equipment_tab(db, screen, state)
	_check_bag_tab(db, screen, state)
	_check_skillbook(db, screen, state)
	_check_back(screen, back_calls)

	scene_tree.root.remove_child(screen)
	screen.free()

	_check_party_stress(db)
	_check_big_bag(db)
	_check_no_points_ui(db)


## 五维点加完之后：加号按钮要**置灰**（不是「能点、点了才报错」），标题写明剩 0。
func _check_no_points_ui(db) -> void:
	var state = solo_state(db)
	var screen = load(UI_SCENE).instantiate()
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()
	var guard := 0
	while screen.sheet().available_points() > 0 and guard < 200:
		screen.press_plus("str")
		guard += 1
	check_eq(screen.sheet().available_points(), 0, "把五维点加完（用了 %d 次）" % guard)
	var plus: Button = screen.find_child("PlusButtonstr", true, false)
	check_not_null(plus, "加号按钮还在")
	if plus != null:
		check_true(plus.disabled, "没点数了，加号按钮置灰（不是点了才报错）")
	var header: Label = screen.find_child("AttrHeader", true, false)
	check_not_null(header, "五维标题还在")
	if header != null:
		check_true(header.text.contains("未分配点数：0"), "标题写明剩 0：%s" % header.text)
	var refused: Dictionary = screen.press_plus("str")
	check_false(bool(refused["ok"]), "就算绕过置灰硬调也拒绝")
	scene_tree.root.remove_child(screen)
	screen.free()


## 大背包：15 种道具各堆满 + 30 件装备实例时，背包页还列得出来、版式还塞得进、存档还往返得回来。
## 发行数据里 `item_base` 只有 16 行，这种局面靠正常游玩很难自然到——列表是按存档动态生成的，
## 东西一多就可能出岔子（重名节点、排序、版式）。
func _check_big_bag(db) -> void:
	var state = solo_state(db)
	var inventory = state.inventory
	inventory.money = 123456
	var equip_before := int(inventory.equipment_count())
	var filled := 0
	for row: Resource in db.rows("item_base"):
		if str(row.item_type) == "currency":
			continue
		if bool(inventory.add_item(db, str(row.item_id), maxi(1, int(row.stack_max)))["ok"]):
			filled += 1
	check_eq(filled, 15, "16 行道具里除「铜钱」外都放进背包（各堆满）")
	var instances: Array = []
	for base_id: String in ["eq_sword_01", "eq_fist_01", "eq_ring_01", "eq_head_01", "eq_armor_01"]:
		for _n in range(6):
			instances.append(inventory.add_equipment(db, base_id))
	check_eq(instances.size(), 30, "造了 30 件装备实例")
	check_false(instances.has(""), "30 件实例都造出来了（装备 id 写错时 add_equipment 会返回空串）")
	check_eq(int(inventory.equipment_count()), equip_before + 30, "装备实例数对得上")
	var unique: Dictionary = {}
	for instance_id: String in inventory.equipment_ids():
		unique[instance_id] = true
	check_eq(unique.size(), int(inventory.equipment_count()), "30 件实例的 id 没有重号")

	# 存档往返：几十件东西一个都不能丢
	var back = GameStateScript.from_dict(state.to_dict(), db)
	check_not_null(back, "大背包能读回来")
	if back != null:
		check_eq(int(back.inventory.equipment_count()), int(inventory.equipment_count()), "装备数量往返一致")
		check_eq(back.inventory.count("item_iron"), int(inventory.count("item_iron")), "堆叠数量往返一致")
		check_eq(str(back.inventory.item_ids()), str(inventory.item_ids()), "道具清单往返一致")
		check_eq(back.inventory.money, 123456, "铜钱往返一致")

	# 界面：背包页真的把每一行都建出来了，且整页仍塞得进设计分辨率
	var screen = load(UI_SCENE).instantiate()
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()
	screen.select_tab(2)
	var item_rows := 0
	for node: Node in screen.find_children("ItemRow*", "HBoxContainer", true, false):
		item_rows += 1
	check_eq(item_rows, 15, "背包页列出 15 种道具")
	var equip_rows := 0
	for node: Node in screen.find_children("EquipRow*", "HBoxContainer", true, false):
		equip_rows += 1
	check_eq(equip_rows, int(inventory.equipment_count()), "装备区列出每一件实例")
	check_true(LayoutBudgetScript.fits(screen), "大背包下整页仍塞进设计分辨率")
	scene_tree.root.remove_child(screen)
	screen.free()


func _check_tabs(screen) -> void:
	var tabs: TabContainer = screen.find_child("Tabs", true, false)
	check_not_null(tabs, "有页签容器")
	if tabs == null:
		return
	check_eq(tabs.get_tab_count(), 3, "三个页签")
	check_eq(tabs.get_tab_title(0), "角色", "第一个页签是角色")
	check_eq(tabs.get_tab_title(1), "装备", "第二个页签是装备")
	check_eq(tabs.get_tab_title(2), "背包", "第三个页签是背包")
	check_eq(screen.current_tab(), 0, "默认停在角色页")
	screen.select_tab(2)
	check_eq(screen.current_tab(), 2, "能切到背包页")
	screen.select_tab(0)


func _check_character_tab(db, screen, state) -> void:
	var header: Label = screen.find_child("Header", true, false)
	check_not_null(header, "有头部信息")
	if header != null:
		check_true(header.text.contains("铜钱"), "头部显示铜钱")
		check_true(header.text.contains("经验"), "头部显示队伍经验")

	var member: Button = screen.find_child("MemberButton%s" % CHAR_ID, true, false)
	check_not_null(member, "成员列表里有当前角色")
	for attr_id in ["str", "con", "agi", "int", "luk"]:
		check_not_null(screen.find_child("PlusButton%s" % attr_id, true, false), "%s 有加点按钮" % attr_id)
	# 设计 0.13.0：悟性／根骨是资质——按钮仍在（玩家看得到这一项），但置灰并写明原因
	for talent_id in ["wu", "gen"]:
		var talent_plus: Button = screen.find_child("PlusButton%s" % talent_id, true, false)
		check_not_null(talent_plus, "%s 的加号还在（置灰而不是藏起来）" % talent_id)
		if talent_plus != null:
			check_true(talent_plus.disabled, "%s 是资质，加号置灰" % talent_id)
			check_true(talent_plus.tooltip_text.contains("资质"), "%s 的悬浮说明写明是资质：%s" % [talent_id, talent_plus.tooltip_text])
		var talent_label: Label = screen.find_child("AttrLabel%s" % talent_id, true, false)
		check_true(talent_label != null and talent_label.text.contains("不可加点"), "%s 那一行写明不可加点" % talent_id)

	var sheet = screen.sheet()
	var before: int = sheet.available_points()
	var plus: Button = screen.find_child("PlusButtonstr", true, false)
	if plus != null:
		plus.emit_signal("pressed")
	check_eq(int(state.allocations_of(CHAR_ID).get("str", 0)), 1, "点加号会写进存档")
	check_eq(screen.sheet().available_points(), before - 1, "未分配点数减少")
	check_true(screen.status_text().contains("加点"), "状态栏提示加点成功")

	var event_label: Label = screen.find_child("EventSkillwenxue", true, false)
	check_not_null(event_label, "非战斗技能行存在")
	if event_label != null:
		check_true(event_label.text.contains("判定"), "显示判定值")
	check_true(str(screen.find_child("EventSkillHeader", true, false).text).contains("不含装备"), "标注判定不含装备")

	# 战斗外气血（v11）：面板要看得到当前气血；五维行不能被自动换行压成竖排
	var hp_line: Label = screen.find_child("CurrentHp", true, false)
	check_not_null(hp_line, "战斗属性上面有当前气血行")
	if hp_line != null:
		check_true(hp_line.text.contains("当前气血"), "写清是当前气血：%s" % hp_line.text)
	var attr_label: Label = screen.find_child("AttrLabelstr", true, false)
	check_not_null(attr_label, "五维行存在")
	if attr_label != null:
		check_eq(attr_label.get_line_count(), 1, "五维一行显示，没被压成竖排：%s" % attr_label.text)
	# 图鉴奖励：面板要写清「已收集几部 → 现在 +几、再收几部再 +1」
	var codex_line: Label = screen.find_child("CodexReward", true, false)
	check_not_null(codex_line, "五维下面有图鉴奖励行")
	if codex_line != null:
		check_true(codex_line.text.contains("图鉴奖励"), "写清是图鉴奖励：%s" % codex_line.text)
		check_true(codex_line.text.contains("已收集 1 部"), "开局已收集 1 部（起始武学）：%s" % codex_line.text)


## 武学装配：面板上的「装配／卸下」按钮写回存档，卸下后战斗就没这一招了
func _check_skill_loadout(screen, state) -> void:
	screen.select_tab(0)
	var header: Label = screen.find_child("SkillHeader", true, false)
	check_not_null(header, "有武学标题")
	if header != null:
		check_true(header.text.contains("招式"), "标题写清招式槽：%s" % header.text)
		check_true(header.text.contains("内功"), "标题写清内功容量：%s" % header.text)

	var skill_id := "sk_xuanwei_01"
	var button: Button = screen.find_child("LoadoutButton%s" % skill_id, true, false)
	check_not_null(button, "起始武学有装配按钮")
	if button == null:
		return
	check_true(button.text == "卸下", "已装配的显示「卸下」：%s" % button.text)
	button.emit_signal("pressed")
	check_eq(PackedStringArray(state.loadout_of(CHAR_ID)["active"]).size(), 0, "卸下后招式槽空了")
	check_true(screen.status_text().contains("卸下"), "状态栏提示卸下：%s" % screen.status_text())

	var again: Button = screen.find_child("LoadoutButton%s" % skill_id, true, false)
	check_not_null(again, "卸下后按钮还在")
	if again != null:
		check_true(again.text == "装配", "卸下后按钮变成「装配」：%s" % again.text)
		again.emit_signal("pressed")
	check_eq(PackedStringArray(state.loadout_of(CHAR_ID)["active"]).size(), 1, "再点一下就装回去")
	check_true(screen.status_text().contains("装配"), "状态栏提示装配：%s" % screen.status_text())

	# 装不上的武学（武器不符／门槛不足）按钮要置灰
	if not state.is_learned(CHAR_ID, "sk_wudu_01"):
		state.learn_skill(CHAR_ID, "sk_wudu_01")
	screen.refresh()
	var fist: Button = screen.find_child("LoadoutButtonsk_wudu_01", true, false)
	check_not_null(fist, "拳法出现在武学列表里")
	if fist != null:
		check_true(fist.disabled, "剑客装不了拳法，按钮置灰")
	# 醉里乾坤·醉步（整行 0、target_type=self，但表里配了 on_cast 发放）：它是**增益招式**，
	# 战斗里能用；面板要写清这一点（2026-10-03 起；在那之前写的是「用不出来」，见决策 221）。
	if not state.is_learned(CHAR_ID, "sk_drunk_zuibu"):
		state.learn_skill(CHAR_ID, "sk_drunk_zuibu")
	screen.refresh()
	var empty_label: Label = screen.find_child("Skillsk_drunk_zuibu", true, false)
	check_not_null(empty_label, "醉步出现在武学列表里")
	if empty_label != null:
		check_true(
			empty_label.text.contains("增益招式"),
			"面板写清它是增益招式（施放给自己上 buff）：%s" % empty_label.text,
		)
	# 内功的「特殊效果」设计还没给效果表：面板要如实标出来，而且**不能把 id 原样显示**出来
	if not state.is_learned(CHAR_ID, "pf_drunk_02"):
		state.learn_skill(CHAR_ID, "pf_drunk_02")
	screen.refresh()
	var effect_label: Label = screen.find_child("Skillpf_drunk_02", true, false)
	check_not_null(effect_label, "忘忧出现在武学列表里")
	if effect_label != null:
		check_true(
			effect_label.text.contains("暂未生效"),
			"面板写清那条特殊效果暂未生效：%s" % effect_label.text,
		)
		check_false(
			effect_label.text.contains("effect_"),
			"不把表里的效果 id 原样甩给玩家：%s" % effect_label.text,
		)


func _check_equipment_tab(db, screen, state) -> void:
	screen.select_tab(1)
	var slot_button: Button = screen.find_child("SlotButtonweapon_0", true, false)
	check_not_null(slot_button, "武器槽有按钮")
	check_true(screen.find_child("SlotButtonring_1", true, false) != null, "戒指槽有两格")
	var unequip: Button = screen.find_child("UnequipButtonweapon_0", true, false)
	check_not_null(unequip, "武器槽有卸下按钮")
	if unequip == null:
		return
	var instance_id: String = state.inventory.equipped_instances(db, CHAR_ID)[0]
	unequip.emit_signal("pressed")
	# 0.10.2 起初始装备是**两件**（武器＋布衣）：卸掉武器后身上还剩那件布衣
	check_eq(state.inventory.equipped_instances(db, CHAR_ID).size(), 1, "卸下武器后身上还剩上衣")
	check_false(state.inventory.is_equipped(instance_id), "卸下的那把武器不再算穿着")
	check_true(screen.status_text().contains("卸下"), "状态栏提示卸下")
	# 状态栏说装备名，不是 eq_sword_01#1 这种实例 id（2026-10-03 文案审计）
	check_false(screen.status_text().contains("#"), "卸下提示不漏实例序号：%s" % screen.status_text())
	check_false(screen.status_text().contains("eq_"), "卸下提示不漏装备 id：%s" % screen.status_text())

	# 选槽位后候选里出现刚卸下的武器，点一下就装回去
	screen.press_slot("weapon", 0)
	var candidate_header: Label = screen.find_child("CandidateHeader", true, false)
	check_not_null(candidate_header, "选槽后出现候选列表标题")
	if candidate_header != null:
		check_true(
			candidate_header.text.contains("武器") and not candidate_header.text.contains("weapon"),
			"候选标题写中文槽位名而不是内部 id：%s" % candidate_header.text,
		)
	var equip_button: Button = screen.find_child("EquipButton%s" % instance_id, true, false)
	check_not_null(equip_button, "候选列表里出现刚卸下的武器")
	if equip_button != null:
		check_false(equip_button.disabled, "候选按钮可点")
		equip_button.emit_signal("pressed")
		check_eq(state.inventory.equipped_instances(db, CHAR_ID).size(), 2, "装回后身上两件装备")
		check_true(screen.status_text().contains("装上"), "状态栏提示装上：%s" % screen.status_text())
		check_false(screen.status_text().contains("eq_"), "装上提示不漏装备 id：%s" % screen.status_text())

	# 武器类型不匹配的装备要被置灰：塞一把拳套进背包
	var fist: String = state.inventory.add_equipment(screen._db, "eq_fist_01")
	screen.refresh()
	screen.press_slot("weapon", 0)
	var fist_button: Button = screen.find_child("EquipButton%s" % fist, true, false)
	check_not_null(fist_button, "拳套出现在武器的候选里")
	if fist_button != null:
		check_true(fist_button.disabled, "武器类型不匹配的候选被置灰")


func _check_bag_tab(db, screen, state) -> void:
	screen.select_tab(2)
	state.inventory.add_item(db, "item_herb", 2)
	state.inventory.add_item(db, "item_wine_gourd", 1)
	screen.refresh()

	var herb_label: Label = screen.find_child("ItemLabelitem_herb", true, false)
	check_not_null(herb_label, "背包里有草药")
	if herb_label != null:
		check_true(herb_label.text.contains("×2"), "显示堆叠数量")
	var drop_herb: Button = screen.find_child("DropButtonitem_herb", true, false)
	check_not_null(drop_herb, "材料可以丢弃")
	var drop_key: Button = screen.find_child("DropButtonitem_wine_gourd", true, false)
	check_not_null(drop_key, "钥匙道具也在列表里")
	if drop_key != null:
		check_true(drop_key.disabled, "钥匙道具的丢弃按钮被禁用")

	var refused: Dictionary = screen.drop_item("item_wine_gourd")
	check_false(refused["ok"], "钥匙道具丢弃被拒")
	check_true(screen.status_text().contains("钥匙"), "状态栏说明拒绝原因")
	if drop_herb != null:
		drop_herb.emit_signal("pressed")
		check_eq(state.inventory.count("item_herb"), 1, "丢弃材料数量减一")

	# 分类筛选
	screen.set_filter("material")
	check_not_null(screen.find_child("DropButtonitem_herb", true, false), "材料分类下能看到草药")
	check_true(screen.find_child("DropButtonitem_wine_gourd", true, false) == null, "钥匙被筛掉")
	# 「钥匙」按**设计语义**筛（is_key_item），不是按 item_type=key：
	# 05 把铁镐列进钥匙道具，而它的 item_type 是 tool——以前它哪个分类都进不去
	state.inventory.add_item(db, "item_pickaxe", 1)
	screen.refresh()
	screen.set_filter("key")
	check_not_null(screen.find_child("DropButtonitem_wine_gourd", true, false), "钥匙分类下有酒葫芦")
	check_not_null(
		screen.find_child("DropButtonitem_pickaxe", true, false),
		"钥匙分类下有铁镐（item_type=tool，但它是钥匙道具）",
	)
	check_true(screen.find_child("DropButtonitem_herb", true, false) == null, "材料不在钥匙分类里")
	screen.set_filter("all")


## 秘籍研读：门槛不够按钮置灰并写原因；够门槛时点一下就学会并消耗一本
func _check_skillbook(db, screen, state) -> void:
	screen.select_tab(2)
	screen.set_filter("skillbook")
	state.inventory.add_item(db, "item_scroll_wudu", 1)
	screen.refresh()
	var button: Button = screen.find_child("StudyButtonitem_scroll_wudu", true, false)
	check_not_null(button, "残页有研读按钮")
	if button == null:
		return
	# 1 级书生智 10 < 12：按钮置灰，原因写在按钮上
	check_true(button.disabled, "智不够时研读按钮置灰")
	var label: Label = screen.find_child("ItemLabelitem_scroll_wudu", true, false)
	check_not_null(label, "残页有文字行")
	if label != null:
		check_true(label.text.contains("门槛"), "行里写明研读不行的原因：%s" % label.text)
	# 点数全塞智，把它抬到 12
	var sheet = screen.sheet()
	while int(screen.sheet().total_attrs()["int"]) < 12 and screen.sheet().available_points() > 0:
		screen.press_plus("int")
	screen.refresh()
	button = screen.find_child("StudyButtonitem_scroll_wudu", true, false)
	check_false(button.disabled, "智 12 后研读按钮可点")
	button.emit_signal("pressed")
	check_true(state.is_learned(CHAR_ID, "sk_wudu_03"), "研读后学会五毒掌·蚀骨")
	check_eq(state.inventory.count("item_scroll_wudu"), 0, "研读消耗一本")
	check_true(screen.status_text().contains("研读"), "状态栏提示研读：%s" % screen.status_text())
	screen.set_filter("all")


func _check_back(screen, back_calls: Array) -> void:
	var back: Button = screen.find_child("BackButton", true, false)
	check_not_null(back, "有返回按钮")
	if back != null:
		back.emit_signal("pressed")
		check_eq(back_calls.size(), 1, "返回按钮触发回退")


## 4 人满员 + 长名字 + 满级 + 六位数铜钱的版式绊线。
##
## 现表只有 1 个角色模板，而设计要 3–4 人（`docs/dev/当前状态.md` 缺口 #1）——也就是说
## **多人下的界面从来没被量过**：成员列表每多一人就多一行按钮、长名字会撑宽左侧栏，
## 而「塞不下」在界面代码里一声不吭（只会被屏幕裁掉）。这里复制模板行造 4 人，
## 把「整页仍塞进 1152×648」钉死，等策划补行时不会第一次就踩雷。
## （版式余量最紧的是角色页，加一行就要先想清楚——见 AGENTS.md。）
func _check_party_stress(db) -> void:
	var wide = table_with_n_chars(4, 2, true)
	var state = party_state(wide, 4)
	for char_id: String in state.char_ids:
		state.char_levels[char_id] = 20
	state.inventory.money = 999999
	state.party_exp = 9876543

	var screen = load(UI_SCENE).instantiate()
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()

	var members := 0
	for node: Node in screen.find_children("MemberButton*", "Button", true, false):
		members += 1
	check_eq(members, 4, "4 人队伍在成员列表里各有按钮（多人界面分支以前没跑过）")
	var header: Label = screen.find_child("Header", true, false)
	check_not_null(header, "满员时头部信息还在")
	if header != null:
		check_true(header.text.contains("999999"), "六位数铜钱照常显示：%s" % header.text)
	var laid_out := LayoutBudgetScript.fits(screen)
	if not laid_out:
		fail("4 人满员 + 长名字下版式超预算：" + LayoutBudgetScript.ascii_line(screen))
	check_true(laid_out, "4 人满员 + 长名字仍塞进设计分辨率")
	# 成员行是 HBox（只会横向长），而滚动区的横向滚动是关着的：行宽超过设计宽度，后面的成员就被裁掉。
	# 这里量成员行自己的最小宽度——**不用等帧就准**（探针验过：4 个 300px 按钮的行宽是 1212）。
	# 外层那条 `fits()` 量不到它：`get_combined_minimum_size()` 到 ScrollContainer 就断了。
	var members_node: Control = screen.find_child("Members", true, false)
	check_not_null(members_node, "有成员行（HBox）")
	var members_width: float = members_node.get_combined_minimum_size().x if members_node != null else -1.0
	check_lt(members_width, float(LayoutBudgetScript.DESIGN_WIDTH), "4 人 + 长名字的成员行不超设计宽度（%.0f px）" % members_width)
	screen.select_tab(2)
	check_eq(screen.current_tab(), 2, "满员下也能切到背包页")
	check_not_null(screen.sheet(), "满员下选中的角色仍有数据面板")

	scene_tree.root.remove_child(screen)
	screen.free()
