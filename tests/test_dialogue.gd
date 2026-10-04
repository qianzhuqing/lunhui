## 对话容器（设计 20 §十一，0.31.0）：台词与选项的出口。
##
## 这一层以前不存在，所以设计侧十条支线、六幕台词全都**没有容器可落**（`当前状态.md` #4 里
## 「npc 要对话表」那条就是它）。这里钉四件事：
##   ① 发行数据里陈氏那条**与设计 20 §四 幕三 逐字一致**（选项文案、心性旗标、好感增减）；
##   ② 条件语言决定「他现在说不说」与「这条选项现在能不能选」（留空 = 无条件）；
##   ③ 选项效果三样都真的落地（置旗标、好感、给东西）；条件不满足时**选不动且报错**；
##   ④ 好感**可以是负的**——`npc_favor.initial_favor` 里陈氏就是 −10，
##      以前 `favor_of()` 把好感夹在 [0, cap]，负数被吃掉，设计写的「由 −10 转正」表达不出来。
extends "res://tests/test_case.gd"

const DialogueServiceScript := preload("res://src/core/dialogue_service.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")
const GuideServiceScript := preload("res://src/core/guide_service.gd")
const StoryServiceScript := preload("res://src/core/story_service.gd")
## 名字不能叫 `TableDbScript`：基类 `TestCase` 已经有一个同名的（GDScript 会当场拒绝解析）
const LocalDbScript := preload("res://src/core/table_db.gd")

const CHEN := "npc_huangcun"
const CHEN_NODE := "dl_huangcun_chen"
## 终局难题（20 号 §七）：打完大寨主之后沈雁回问你账册怎么办
const SHEN := "npc_shen_yanhui"
const LEDGER_NODE := "dl_ledger_choice"
## 幕四·寨门与地牢（20 号 §四／§五）：铁栏后那句 ＋ 三个选项
const CELL_NODE := "dl_shen_cell"
## 序幕·择念（20 号 §3.1）：主角自己那一问 ＋ 三选一
const OPENING_NODE := "dl_opening_choice"


func suite_name() -> String:
	return "对话容器"


func run() -> void:
	var db = get_db()
	_check_release_rows(db)
	_check_conditions(db)
	_check_effects(db)
	_check_region_npcs(db)
	_check_companion_person(db)
	_check_companion_dialogue(db)
	_check_quest_extra_favor(db)
	_check_condition_ops(db)
	_check_shen_cell(db)
	_check_opening_choice(db)
	_check_ledger_choice(db)


## 幕四·寨门与地牢（20 号 §四 幕四 ＋ §五 的选项表）：她一开口就是「你是来领那三十两的？」，
## 三个选项的文案与效果（义／谋／利 ＋ 好感 −5）也是照表落。
##
## 这一段以前**根本没有容器**：地牢那次判定只置一个旗标，铁栏后的人一句话都没有。
## 落地的入口是 `local_map_controller.EVENT_DIALOGUES`（判定过了接这段）。
## **没落的那一格**：§五 那行「她反问『你为什么帮我』→ 三答」——三句回答设计没写，不自己编（Q73）。
func _check_shen_cell(db) -> void:
	var node: Resource = db.get_row("dialogue_node", CELL_NODE)
	check_not_null(node, "地牢那一句在表里")
	if node == null:
		return
	check_eq(str(node.speaker_id), SHEN, "说话人是沈雁回")
	check_eq(str(node.text_cn), "你是来领那三十两的？", "台词逐字照 20 号 §四 幕四")
	var options: Array = DialogueServiceScript.options_for(db, solo_state(db), CELL_NODE)
	check_eq(options.size(), 3, "三个选项（§五 幕四 的选项表里有文案的那三条）")
	var texts := PackedStringArray()
	var flags := PackedStringArray()
	var favors := PackedStringArray()
	for option: Resource in options:
		texts.append(str(option.text_cn))
		flags.append("%s=%s" % [str(option.option_id), str(option.set_flag)])
		favors.append("%s=%d" % [str(option.option_id), int(option.favor_delta)])
	check_true(texts.has("「我带你走。」"), "「我带你走。」在（%s）" % str(texts))
	check_true(texts.has("「账册在哪？」"), "「账册在哪？」在")
	check_true(texts.has("「三十两是我的。」"), "「三十两是我的。」在")
	check_true(str(flags).contains("opt_shen_take=heart_yi"), "带走那条给义（%s）" % str(flags))
	check_true(str(flags).contains("opt_shen_ledger=heart_mou"), "问账册那条给谋")
	check_true(str(flags).contains("opt_shen_thirty=heart_li"), "「三十两」那条给利")
	check_true(str(favors).contains("opt_shen_thirty=-5"), "「三十两」那条好感 −5（表里写明，%s）" % str(favors))
	# 效果真的落地：旗标 ＋ 好感（沈雁回的初始好感是 0）
	var state = solo_state(db)
	var before := NpcServiceScript.favor_of(db, state, SHEN)
	var chosen: Dictionary = DialogueServiceScript.choose(db, state, "opt_shen_thirty")
	check_true(bool(chosen.get("ok", false)), "选得动：%s" % str(chosen.get("error", "")))
	check_true(state.has_flag("heart_li"), "置上心性旗标 heart_li")
	check_eq(NpcServiceScript.favor_of(db, state, SHEN), before - 5, "好感 0 −5 就是 −5（负好感允许）")


## 序幕·择念（设计 20 号 §3.1）：读告示板那一下的**第一个决定**，三选一各给一档心性。
##
## 说话人是记号 `player`（主角自己）——主角是哪一号人物要等玩家选完出身才知道，
## 表里没法写死一个 `char_id`。这也是「这一句是主角替自己开口，而不是接 NPC 的话」的落法，
## 所以面板按 `MODE_STORY` 渲染（没有好感条与交往段）。
##
## 条件 `!heart_yi&!heart_mou&!heart_li` 把「只问一次」钉在表里：设计 §十三 v2 第 2 条说
## 心性由序幕**一次定死**、后面只改当场反应。
func _check_opening_choice(db) -> void:
	var node: Resource = db.get_row("dialogue_node", OPENING_NODE)
	check_not_null(node, "序幕择念那一句在表里")
	if node == null:
		return
	check_eq(str(node.speaker_id), NpcServiceScript.PLAYER_ID, "说话人是主角自己（记号 player）")
	check_true(str(node.text_cn).contains("你为什么要接这桩事"), "那一问在（%s）" % str(node.text_cn))
	check_true(str(node.text_cn).contains("谢银三十两"), "告示原文也在（逐字）")
	# 三个选项与三句心理独白（都逐字照 §3.1）
	var state = solo_state(db)
	var options: Array = DialogueServiceScript.options_for(db, state, OPENING_NODE)
	check_eq(options.size(), 3, "三选一")
	var texts := PackedStringArray()
	var flags := PackedStringArray()
	var nexts := PackedStringArray()
	for option: Resource in options:
		texts.append(str(option.text_cn))
		flags.append("%s=%s" % [str(option.option_id), str(option.set_flag)])
		nexts.append(str(option.next_node_id))
	check_true(texts.has("「三十两。」"), "「三十两。」在（%s）" % str(texts))
	check_true(texts.has("「告示上的字，是账房体。」"), "「账房体」那条在")
	check_true(texts.has("「十年前也有一桩事，也在这条河上。」"), "「十年前也有一桩事」那条在")
	check_true(str(flags).contains("opt_open_li=heart_li"), "「三十两」给利（%s）" % str(flags))
	check_true(str(flags).contains("opt_open_mou=heart_mou"), "「账房体」给谋")
	check_true(str(flags).contains("opt_open_yi=heart_yi"), "「十年前」给义")
	check_true(str(nexts).contains("dl_open_li"), "选完接的是那句心理独白（%s）" % str(nexts))
	# 选一条：旗标落地，并且面板会走到那句独白（`next_node_id` 指向它）
	var chosen: Dictionary = DialogueServiceScript.choose(db, state, "opt_open_yi")
	check_true(bool(chosen.get("ok", false)), "选得动：%s" % str(chosen.get("error", "")))
	check_true(state.has_flag("heart_yi"), "选「十年前」→ 置 heart_yi")
	check_false(state.has_flag("heart_li"), "另外两档不受影响（一次只定一档）")
	# 0.32.0：同一处再置一枚「序幕定死」的 `flag_open_*`——称号与幕结旁白只看它
	# （心性由序幕一次定死；后面各幕的选项照旧只置当场反应的 `heart_*`）。
	check_true(state.has_flag("flag_open_yi"), "同一处顺带置 flag_open_yi（序幕定死的那一档）")
	check_false(state.has_flag("flag_open_mou"), "其余两枚 flag_open_* 不跟着置")
	check_false(state.has_flag("flag_open_li"), "三条选项一一对应，不串档")
	check_eq(str(chosen.get("next_node_id", "")), "dl_open_yi", "接上那句独白")
	check_eq(
		str(db.get_row("dialogue_node", "dl_open_yi").text_cn),
		"有些账，别人早忘了。你不敢忘。",
		"独白逐字照 §3.1",
	)
	# 心性定过之后，这一问就不再出现（条件在表里）
	check_false(
		DialogueServiceScript.condition_ok(state, str(node.condition)),
		"选过之后那条条件不成立（不会反复问）",
	)
	var fresh = solo_state(db)
	check_true(
		DialogueServiceScript.condition_ok(fresh, str(node.condition)),
		"没选过的档里它才出现",
	)


## 条件语言的两个新连接符（2026-10-04 加）：`|`＝任一满足、`!`＝取反。
##
## 为什么非加不可：终局难题要表达「三个旗标一个都没点亮」——`&` 只能写「全部满足」、
## `|` 只能写「任一满足」，三选一的「还没选过」两种都写不出来，于是玩家能反复进去选，
## 三个永久增益会叠在一起（设计 20 §八 第 6 条：三选一、不叠加）。
func _check_condition_ops(db) -> void:
	var state = solo_state(db)
	check_false(GuideServiceScript.condition_met(state, "flag_a"), "没点亮的旗标＝不满足")
	check_true(GuideServiceScript.condition_met(state, "!flag_a"), "取反：没点亮时 `!flag_a` 成立")
	state.set_flag("flag_a")
	check_false(GuideServiceScript.condition_met(state, "!flag_a"), "点亮之后 `!flag_a` 不成立")
	check_true(GuideServiceScript.condition_met(state, "flag_a|flag_b"), "任一满足：左边亮就算过")
	check_false(GuideServiceScript.condition_met(state, "flag_b|flag_c"), "两个都没亮＝不满足")
	check_true(
		GuideServiceScript.condition_met(state, "flag_a&!flag_b"),
		"`&` 先拆、`!` 在后：亮 a 且没亮 b 成立",
	)
	check_false(
		GuideServiceScript.condition_met(state, "!flag_a&flag_a"),
		"同一条里自相矛盾＝不成立（写坏了不会静默当成没条件）",
	)
	check_false(GuideServiceScript.condition_met(state, "!flag_a|"), "尾部空段＝不成立（与 `&` 同一套纪律）")
	# `item:<id>`：背包里有没有这件东西（与 `npc_quest.requirement` 同一种写法）。
	# 终局难题靠它——**没拿到账册就不会问这件事**。
	check_false(
		GuideServiceScript.condition_met(state, "item:item_bd_ledger"),
		"背包里没有账册时 `item:item_bd_ledger` 不成立",
	)
	state.inventory.add_item(db, "item_bd_ledger", 1)
	check_true(GuideServiceScript.condition_met(state, "item:item_bd_ledger"), "拿到账册之后成立")
	check_true(
		GuideServiceScript.condition_met(state, "item:item_bd_ledger:1"),
		"带数量的写法（与 npc_quest.requirement 一致）也认",
	)
	check_false(
		GuideServiceScript.condition_met(state, "item:item_bd_ledger:2"),
		"只有一本时 `:2` 不成立",
	)


## 终局难题（20 号 §七）：一册账，三条路——三条路各置一个旗标，
## 而 story_node 那三行 `kind=choice` 的永久增益**照着旗标发**（设计 20 §八）。
##
## 这条链以前是**断的**：`flag_ledger_public/buried/scattered` 全项目没有来源，
## 三行增益谁也拿不到（`test_handshake.KNOWN_FLAG_GAPS` 里挂着它们）。
func _check_ledger_choice(db) -> void:
	var node: Resource = db.get_row("dialogue_node", LEDGER_NODE)
	check_not_null(node, "终局难题那条对话在表里")
	if node == null:
		return
	check_eq(str(node.speaker_id), SHEN, "说话人是沈雁回（§七：她要素真相）")
	var shen_def: Resource = db.get_row("npc_def", SHEN)
	check_not_null(shen_def, "沈雁回有 npc_def 行（美术的头像文件就是照这个 id 出的）")
	if shen_def != null:
		check_eq(str(shen_def.place_id), "scene_heifengzhai", "她挂在地牢那张图上")
	# ① 没打过对质 → 她不问这件事
	var state = solo_state(db)
	var cell_entry: Resource = DialogueServiceScript.entry_node(db, state, SHEN)
	check_not_null(cell_entry, "没打赢大寨主时她有话说——是地牢那一句")
	if cell_entry != null:
		check_eq(str(cell_entry.node_id), CELL_NODE, "取到的是幕四那一句（不是账册那道题）")
	# ② 打过对质 → 摆出难题，三条路都在
	state.set_flag("flag_heifeng_confront")
	# 账册是条件的一部分（`item:item_bd_ledger`）：先验「对质打过但账册没到手」也不问
	check_null(
		DialogueServiceScript.entry_node(db, state, SHEN),
		"打赢了但账册还没到手 → 她也不问（`item:` 那一段挡着；幕四那句也被 `!flag_heifeng_confront` 挡了）",
	)
	state.inventory.add_item(db, "item_bd_ledger", 1)
	var entry: Resource = DialogueServiceScript.entry_node(db, state, SHEN)
	check_not_null(entry, "打赢大寨主之后她会问")
	if entry != null:
		check_eq(str(entry.node_id), LEDGER_NODE, "取到的就是终局难题那一条")
	var options: Array = DialogueServiceScript.options_for(db, state, LEDGER_NODE)
	check_eq(options.size(), 3, "三条路（呈官／沉底／散页）")
	var flags := PackedStringArray()
	for option: Resource in options:
		flags.append(str(option.set_flag))
	check_true(flags.has("flag_ledger_public"), "呈官那条置 flag_ledger_public（%s）" % str(flags))
	check_true(flags.has("flag_ledger_buried"), "沉底那条置 flag_ledger_buried")
	check_true(flags.has("flag_ledger_scattered"), "散页那条置 flag_ledger_scattered")
	# ③ 选一条 → 旗标落地；再问她就不问了（**三选一不叠加**靠的就是这一步）
	var chosen: Dictionary = DialogueServiceScript.choose(db, state, "opt_ledger_public")
	check_true(bool(chosen.get("ok", false)), "选得动：%s" % str(chosen.get("error", "")))
	check_true(state.has_flag("flag_ledger_public"), "选了呈官 → 旗标真的进存档")
	check_true(state.has_flag("flag_heifeng_confront"), "对质那条旗标不受影响")
	check_null(
		DialogueServiceScript.entry_node(db, state, SHEN),
		"选过之后她不再问（三个 `!flag_ledger_*` 一起把它挡住）",
	)
	# 永久增益照旗标发（设计 20 §八 第 3 条：走 AttributeCalculator 的贡献通道）
	var char_id := str(state.char_ids[0])
	var labels: PackedStringArray = StoryServiceScript.bonus_labels(db, state, char_id)
	check_true(
		str(labels).contains("悟性 +2") and str(labels).contains("内力上限 +20"),
		"选完之后面板写得出「清名」那两条增益（%s）" % str(labels),
	)


## 20 号 §九 #8：孙掌柜那条支线给的是**燕小七**的好感 ＋10（信是给她的）。
##
## `npc_quest.reward_favor` 只能加**委托人自己**，所以这条额外的好感写在
## `NpcService.EXTRA_FAVOR`（Q72 ③ 还在等设计给列）。
func _check_quest_extra_favor(db) -> void:
	var state = solo_state(db)
	state.set_flag("flag_ch_ci_joined")
	var ci_before := NpcServiceScript.favor_of(db, state, "ch_ci")
	var sun_before := NpcServiceScript.favor_of(db, state, "npc_sun_zhanggui")
	var done: Dictionary = NpcServiceScript.complete_quest(db, state, "nq_sun_01")
	check_true(bool(done.get("ok", false)), "孙掌柜那条交得了：%s" % str(done.get("error", "")))
	check_eq(
		NpcServiceScript.favor_of(db, state, "ch_ci"), ci_before + 10,
		"燕小七好感 ＋10（信是给她的）",
	)
	check_eq(
		NpcServiceScript.favor_of(db, state, "npc_sun_zhanggui"), sun_before + 20,
		"孙掌柜自己那条 ＋20（表里的 reward_favor）",
	)
	check_true(
		Array(done.get("extra_favor", [])).has("燕小七 好感 +10"),
		"结果里带上了给人看的短句：%s" % str(done.get("extra_favor", [])),
	)


## 同伴也是「人」（设计 20 §十：同伴与城镇 NPC 共用一套好感规则）：
## 他们没有 `npc_def` 行，所以 `person_of()` 按同一形状**合成一行**——
## 名字／称号从 `character_base`、所在地从 `recruit_def`、等级从存档实时取。
func _check_companion_person(db) -> void:
	var state = solo_state(db)
	var char_id := "ch_ci"
	check_true(NpcServiceScript.def_of(db, char_id) == null, "同伴确实没有 npc_def 行")
	var person: Resource = NpcServiceScript.person_of(db, state, char_id)
	check_not_null(person, "但 person_of 认得出他")
	if person == null:
		return
	var char_row: Resource = db.get_row("character_base", char_id)
	check_eq(str(person.name_cn), str(char_row.name_cn), "名字来自 character_base")
	check_eq(str(person.title_cn), str(char_row.role_tag), "称号用他的路线标签")
	check_eq(
		str(person.place_id), str(db.get_row("recruit_def", char_id).join_scene),
		"所在地来自 recruit_def.join_scene",
	)
	# NPC 那一侧不受影响：真表行还是原样返回
	var npc: Resource = NpcServiceScript.person_of(db, state, "npc_wang_tie")
	check_eq(str(npc.npc_id), "npc_wang_tie", "NPC 走的是真表行（不是合成）")
	# 好感行（Q68 的推荐值：赠送／切磋 5、不可偷）
	var favor_row: Resource = NpcServiceScript.favor_row_of(db, char_id)
	check_not_null(favor_row, "同伴有好感行（设计写的「＋4 四位同伴」）")
	if favor_row != null:
		check_eq(int(favor_row.gift_favor), 5, "赠送 +5")
		check_eq(int(favor_row.spar_favor), 5, "切磋 +5")
		check_eq(int(favor_row.steal_difficulty), 0, "同伴不可偷（设计明写）")


## 幕一·客栈里的绿林客（20 号 §四）：台词与四个选项逐字一致，效果真的改到同伴身上
func _check_companion_dialogue(db) -> void:
	var node: Resource = db.get_row("dialogue_node", "dl_qingfengyi_ci")
	check_not_null(node, "燕小七那条对话在表里")
	if node == null:
		return
	check_eq(str(node.speaker_id), "ch_ci", "说话人是同伴（不是 npc_def 行）")
	var state = solo_state(db)
	var options: Array = DialogueServiceScript.options_for(db, state, "dl_qingfengyi_ci")
	check_eq(options.size(), 4, "四个选项（20 号 §四 幕一）")
	var texts := PackedStringArray()
	for option: Resource in options:
		texts.append(str(option.text_cn))
	check_true(texts.has("「房钱，我替你付。」"), "「房钱，我替你付。」在（%s）" % str(texts))
	check_true(texts.has("「你怎么知道，要人不要钱？」"), "反问那句在")
	check_true(texts.has("「三十两，够我半年。」"), "「三十两，够我半年。」在")
	check_true(texts.has("（不接话，起身就走）"), "「不接话」在")
	# 效果：义 +1 ＋ 好感 +5（表里都是 0 起步，加完是 5）
	var before := NpcServiceScript.favor_of(db, state, "ch_ci")
	var chosen: Dictionary = DialogueServiceScript.choose(db, state, "opt_ci_pay")
	check_true(bool(chosen.get("ok", false)), "选得动：%s" % str(chosen.get("error", "")))
	check_true(state.has_flag("heart_yi"), "置上心性旗标 heart_yi")
	check_eq(NpcServiceScript.favor_of(db, state, "ch_ci"), before + 5, "好感 +5（同伴的好感也进了 npc_favor）")
	# 利那条：好感 −1、心性给利
	var state2 = solo_state(db)
	DialogueServiceScript.choose(db, state2, "opt_ci_money")
	check_true(state2.has_flag("heart_li"), "「三十两」给利")
	# 0 −1 = **−1**：好感可以是负的（地板 `FAVOR_MIN` 是 −10）——
	# 正是这轮把 `[0, cap]` 改成 `[FAVOR_MIN, cap]` 的用意，不然「失手真的疼」根本表达不出来。
	check_eq(NpcServiceScript.favor_of(db, state2, "ch_ci"), -1, "好感 0 −1 就是 −1（负好感允许）")


## NPC 的地点口径（设计 19）：`npc_def.place_id` **既可以写小地图，也可以写大地图节点**——
## 落雁坡没有自己的小地图，采药人老周就挂在大地图的区域节点上。
## 这条以前只在小地图那一侧接（大地图上按 E 什么也不发生），现在两边都认。
func _check_region_npcs(db) -> void:
	var slope: Array = NpcServiceScript.npcs_at(db, "n_luoyanpo")
	var slope_ids := PackedStringArray()
	for row: Resource in slope:
		slope_ids.append(str(row.npc_id))
	check_true(slope_ids.has("npc_caiyao"), "大地图区域节点上有老周（%s）" % str(slope_ids))

	var town: Array = NpcServiceScript.npcs_at(db, "scene_qingfengyi")
	var town_ids := PackedStringArray()
	for row: Resource in town:
		town_ids.append(str(row.npc_id))
	check_true(town_ids.has("npc_wang_tie"), "清风驿上有王铁（%s）" % str(town_ids))
	check_false(town_ids.has("npc_caiyao"), "老周不在清风驿（地点口径不串台）")


## 发行数据：陈氏那条是照 20 号 §四 幕三 落的（文案与效果逐字核对）
func _check_release_rows(db) -> void:
	var node: Resource = db.get_row("dialogue_node", CHEN_NODE)
	check_not_null(node, "陈氏那条对话节点在表里")
	if node == null:
		return
	check_eq(str(node.speaker_id), CHEN, "说话人是陈氏")
	check_false(str(node.text_cn).is_empty(), "她有一句话（%s）" % str(node.text_cn))
	var options: Array = DialogueServiceScript.options_for(db, solo_state(db), CHEN_NODE)
	check_eq(options.size(), 3, "三个选项（20 号 §四 幕三）")
	var texts := PackedStringArray()
	var flags := PackedStringArray()
	var favors := PackedStringArray()
	for option: Resource in options:
		texts.append(str(option.text_cn))
		flags.append("%s=%s" % [str(option.option_id), str(option.set_flag)])
		favors.append("%s=%d" % [str(option.option_id), int(option.favor_delta)])
	check_true(texts.has("蹲下来，与她对视"), "「蹲下来，与她对视」在（%s）" % str(texts))
	check_true(texts.has("「那晚你看见了什么？」（逼问）"), "逼问那句在（%s）" % str(texts))
	check_true(texts.has("什么都不问"), "「什么都不问」在（%s）" % str(texts))
	check_true(str(flags).contains("opt_chen_crouch=heart_yi"), "对视给义（%s）" % str(flags))
	check_true(str(flags).contains("opt_chen_press=heart_mou"), "逼问给谋（%s）" % str(flags))
	check_true(str(flags).contains("opt_chen_silent="), "「什么都不问」不改心性（%s）" % str(flags))
	check_true(str(favors).contains("opt_chen_press=-2"), "逼问扣好感 −2（%s）" % str(favors))


## 条件语言：说话人「现在说不说」与选项「现在能不能选」都走同一份判定
func _check_conditions(db) -> void:
	var state = solo_state(db)
	# 说话人的入口节点：条件留空 = 一直可谈
	var entry: Resource = DialogueServiceScript.entry_node(db, state, CHEN)
	check_not_null(entry, "陈氏现在有话说")
	if entry != null:
		check_eq(str(entry.node_id), CHEN_NODE, "取到的就是那条")
	check_true(DialogueServiceScript.has_dialogue(db, CHEN), "面板问得出「这个人有没有对话」")
	check_false(DialogueServiceScript.has_dialogue(db, "npc_wang_tie"), "没配的人如实说没有")

	# 给一个「只在看过告示板之后才出现」的选项（夹具：只改内存）
	var local = _with_extra_option(db, {
		"option_id": "opt_chen_extra", "node_id": CHEN_NODE, "sort_order": 9,
		"text_cn": "想问问告示板上那张纸", "condition": "flag_board_read",
		"next_node_id": "", "set_flag": "", "favor_delta": 0,
		"grant_item_id": "item_herb", "note": "夹具：验前置与给东西",
	})
	var before: Array = DialogueServiceScript.options_for(local, state, CHEN_NODE)
	check_eq(before.size(), 3, "前置没满足时那条选项**不出现**（%d 条）" % before.size())
	var blocked: Dictionary = DialogueServiceScript.choose(local, state, "opt_chen_extra")
	check_false(bool(blocked.get("ok", false)), "硬选也选不动")
	check_true(
		str(blocked.get("error", "")).contains("前置"), "并且说明原因：%s" % str(blocked.get("error", "")),
	)

	state.set_flag("flag_board_read")
	var after: Array = DialogueServiceScript.options_for(local, state, CHEN_NODE)
	check_eq(after.size(), 4, "前置满足后它出现了")
	var item_state = solo_state(local)
	item_state.set_flag("flag_board_read")
	var herb_before: int = item_state.inventory.count("item_herb")
	var chosen: Dictionary = DialogueServiceScript.choose(local, item_state, "opt_chen_extra")
	check_true(bool(chosen.get("ok", false)), "选得动：%s" % str(chosen.get("error", "")))
	check_eq(
		item_state.inventory.count("item_herb"), herb_before + 1,
		"选项给的东西真的进背包（走与掉落同一条入账口径）",
	)


## 效果三样：置旗标／好感／给东西；以及"好感可以是负的"这条回归
func _check_effects(db) -> void:
	var state = solo_state(db)
	# 负好感（陈氏初始 −10）——回归：以前被夹在 0，读出来也是 0
	# 地板本身钉**绝对值**（不是符号）：它决定"偷窃失手真的疼"能疼到哪一步，
	# 放松或收紧都会静默改手感（变异探针 `code` 那一块有对应 case）。
	check_eq(NpcServiceScript.FAVOR_MIN, -10, "好感地板是 −10（与 npc_favor 里最深的初始值一致）")
	check_eq(NpcServiceScript.favor_of(db, state, CHEN), -10, "陈氏的初始好感是 −10（表里就这么写）")
	check_eq(
		NpcServiceScript.add_favor(db, state, CHEN, -5)["after"], NpcServiceScript.FAVOR_MIN,
		"再扣只会扣到地板（%d），不会无限往下" % NpcServiceScript.FAVOR_MIN,
	)
	# 置旗标 + 好感：逼问那条
	state = solo_state(db)
	var press: Dictionary = DialogueServiceScript.choose(db, state, "opt_chen_press")
	check_true(bool(press.get("ok", false)), "逼问选得动")
	check_true(state.has_flag("heart_mou"), "置上心性旗标 heart_mou")
	check_eq(int(press.get("favor", 0)), -2, "返回里带上好感变化")
	# 对视那条：只置旗标、不动好感
	state = solo_state(db)
	var crouch: Dictionary = DialogueServiceScript.choose(db, state, "opt_chen_crouch")
	check_true(state.has_flag("heart_yi"), "对视置 heart_yi")
	check_eq(int(crouch.get("favor", 0)), 0, "它不改好感")
	# 认不出的选项：显式报错，不静默
	var missing: Dictionary = DialogueServiceScript.choose(db, state, "opt_not_exist")
	check_false(bool(missing.get("ok", false)), "没有这个选项时选不动")
	check_false(str(missing.get("error", "")).is_empty(), "并且给原因：%s" % str(missing.get("error", "")))


## 夹具：往 `dialogue_option` 追加一行（只改内存副本）
func _with_extra_option(db, spec: Dictionary):
	var local = LocalDbScript.new()
	local.load_all()
	var table: Resource = local.tables["dialogue_option"].duplicate(true)
	var template: Resource = table.rows[0].duplicate(true)
	for key: String in spec:
		template.set(key, spec[key])
	template.id = str(spec["option_id"])
	table.rows.append(template)
	table.index[str(template.id)] = table.rows.size() - 1
	local.tables["dialogue_option"] = table
	return local
