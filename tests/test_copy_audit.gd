## 玩家可见文案审计（换对象审计的产物）。
##
## 起因：功能一路往前走，文案却留在原地。启动菜单副标题写着「玩法未接入，当前为框架骨架」，
## 而那时大地图、明雷、战斗、行囊、商店、副本、图鉴全都接进游戏了——玩家看到的第一句话是假话。
## 同类问题在游戏内枢纽页、奇袭的暂缓规则说明上都出现过。
##
## 这个用例把这类错误变成**绊线**：
##   1) src/ 下任何 .gd 都不许再出现那几句过时断言（含注释）；
##   2) 真实场景里的关键文案必须真的说到点上（菜单说清是哪一章、能玩什么）；
##   3) 招式用不了的原因必须是中文名，不能把表里的英文 id 甩到战斗界面上。
extends "res://tests/test_case.gd"

const EncounterScript := preload("res://src/core/encounter.gd")
const BattleActorScript := preload("res://src/core/battle_actor.gd")
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const SaveStoreScript := preload("res://src/core/save_store.gd")
const InputSetupScript := preload("res://src/world/input_setup.gd")

const MENU_SCENE := "res://scenes/main_menu.tscn"
const PLACEHOLDER_SCENE := "res://scenes/placeholder_game.tscn"
const TEST_DIR := "res://.logs/test_copy_audit"
const SEED := 20261003

## 已经查实是谎话的句子。加进来之前先确认代码里真的没有这个功能缺口。
const BANNED_COPY := [
	"玩法未接入",     # 大地图／明雷／战斗／行囊都已接入
	"玩法还没接",
	"尚未接入",       # 游戏内枢纽页曾这么写
	"架势系统未实现",  # 架势与拆招在 0.6.x 已经实现
]

## 表名静态扫描的**白名单**：这里出现的表名**只进日志或只做内部判断**，玩家看不到。
## 每条都要写清「为什么玩家看不到」；**补上了就要删掉那一行**（双向维护，见 `_check_no_table_names_in_player_text`）。
const TABLE_NAME_IN_TEXT_ALLOWED := {
	"res://src/core/practice_service.gd":
		"data_ready() 的 error 串只进 push_error 与返回值；界面上玩家看到的是调用方自己那句"
		+ "「木桩还没准备好（缺一条配置，已记进日志）」（local_map_controller.start_practice）",
}


func suite_name() -> String:
	return "玩家可见文案审计"


func run() -> void:
	_check_no_banned_copy_in_source()
	_check_display_name_helper()
	_check_hud_mentions_every_key()
	_check_menu_copy()
	_check_hub_copy()
	_check_stale_save_labels()
	_check_unsupported_damage_reason()
	_check_surprise_pending_rule()
	_check_no_table_names_in_player_text()


## 1.2) HUD 必须把**可用的按键**说清楚：控制器源码里 `is_action_pressed("xxx")` 用到的动作，
## 它的按键标签要出现在同一个文件的 HUD 提示行里（键位来自 `InputSetup.ACTIONS`，标签用
## `OS.get_keycode_string` 反推；一个动作有多个键时只要广告其中一个，比如 E／Space 写 E 就够）。
##
## 起因：线索本（K）在**两个** HUD 提示里都没写——玩家只能靠猜，而设计 03 要求
## 「线索必须能被找到」。这类「功能做了但没人知道你按哪个键」的缺口，用这条机械钉住。
func _check_hud_mentions_every_key() -> void:
	var controllers := [
		"res://src/world/overworld_controller.gd",
		"res://src/world/local_map_controller.gd",
	]
	var checked_actions := 0
	for path: String in controllers:
		var source := FileAccess.get_file_as_string(path)
		var hint := _hud_hint_text(source)
		check_false(hint.is_empty(), "%s 能定位到 HUD 提示行" % path)
		var actions := _handled_actions(source)
		check_gt(float(actions.size()), 0.0, "%s 能认出它处理了哪些按键动作" % path)
		for action: String in actions:
			var labels := _key_labels(action)
			if labels.is_empty():
				continue  # ui_cancel 这类不在 InputSetup.ACTIONS 里的动作，不管
			checked_actions += 1
			var found := false
			for label: String in labels:
				if hint.contains(label):
					found = true
			check_true(found, "%s 的 HUD 提示里写了「%s」的按键（%s）：%s" % [
				path.get_file(), action, "／".join(labels), hint.replace("\n", " ⏎ "),
			])
	check_gt(float(checked_actions), 6.0, "覆盖到多个按键动作（%d 个）" % checked_actions)


## 源码里 `is_action_pressed("xxx")` / `is_action_just_pressed("xxx")` 用到的动作名
func _handled_actions(source: String) -> PackedStringArray:
	var regex := RegEx.new()
	regex.compile("is_action_(?:just_)?pressed\\(\"([a-z_]+)\"\\)")
	var out := PackedStringArray()
	for found: RegExMatch in regex.search_all(source):
		var action := found.get_string(1)
		if not out.has(action):
			out.append(action)
	return out


## 动作对应的按键标签（一个动作可能绑了多个键）
func _key_labels(action: String) -> PackedStringArray:
	var out := PackedStringArray()
	if not InputSetupScript.ACTIONS.has(action):
		return out
	for keycode: int in InputSetupScript.ACTIONS[action]:
		out.append(OS.get_keycode_string(keycode))
	return out


## HUD 提示那几行（`_status.text = …` 与 `hint := …` / `hint += …`）
func _hud_hint_text(source: String) -> String:
	var parts := PackedStringArray()
	for line: String in source.split("\n"):
		if line.contains("_status.text =") or line.contains("hint :=") or line.contains("hint +="):
			parts.append(line.strip_edges())
	return "\n".join(parts)


## 1.5) 「id → 中文名」的唯一入口：界面文案都靠它，所以它自己也要钉住
func _check_display_name_helper() -> void:
	var db = get_db()
	check_eq(db.display_name("item_pickaxe"), "铁镐", "道具说中文名")
	check_eq(db.display_name("eq_sword_01"), "铁剑", "装备说中文名")
	check_eq(db.display_name("eq_sword_01#1"), "铁剑", "装备实例 id 会剥掉 #序号")
	check_eq(db.display_name("en_bd_boss"), db.get_row("enemy_base", "en_bd_boss").name_cn, "敌人说中文名")
	check_eq(db.display_name("hf3_dungeon"), "后山地牢", "房间取 room_name")
	check_eq(db.display_name("dmg_normal"), "非暴击直伤", "伤害类型说中文名")
	check_eq(db.display_name("查无此人"), "查无此人", "查不到就原样返回（不许编假名字）")
	check_eq(db.display_name(""), "", "空 id 返回空串")


## 1) 源码里不许再留着那几句过时断言（注释也算，注释是下一个接手的人唯一的地图）
func _check_no_banned_copy_in_source() -> void:
	var files := _gd_files("res://src")
	check_gt(float(files.size()), 20.0, "能递归扫到 src/ 下的 .gd（扫到 %d 个）" % files.size())
	var hits := PackedStringArray()
	for path: String in files:
		var text := FileAccess.get_file_as_string(path)
		for phrase: String in BANNED_COPY:
			if text.contains(phrase):
				hits.append("%s ← 「%s」" % [path, phrase])
	check_eq(hits.size(), 0, "src/ 下没有过时文案：%s" % ", ".join(hits))


## 2) 启动菜单：副标题得说清是哪一章、这一版能玩什么
func _check_menu_copy() -> void:
	if scene_tree == null:
		fail("没有注入场景树，菜单文案检查无法进行")
		return
	var store = SaveStoreScript.new(TEST_DIR, 3)
	store.ensure_dir()
	var menu = load(MENU_SCENE).instantiate()
	menu.store_override = store
	scene_tree.root.add_child(menu)
	menu.setup()

	var subtitle: Label = menu.find_child("Subtitle", true, false)
	check_not_null(subtitle, "菜单有副标题标签")
	if subtitle != null:
		var text := subtitle.text
		check_true(text.contains("黑风寨"), "副标题说清是第一章哪一段：%s" % text)
		check_true(text.contains("大地图"), "副标题点名大地图这个已接入的玩法：%s" % text)
		check_true(text.contains("战斗"), "副标题点名战斗这个已接入的玩法：%s" % text)

	# 菜单的三个按钮是本轮的硬功能，文案一起钉住（改名就得有意为之）
	var new_button: Button = menu.find_child("NewGameButton", true, false)
	var load_button: Button = menu.find_child("LoadGameButton", true, false)
	var quit_button: Button = menu.find_child("QuitButton", true, false)
	check_eq(new_button.text if new_button != null else "", "新建游戏", "新建按钮文案")
	check_eq(load_button.text if load_button != null else "", "读取存档", "读取按钮文案")
	check_eq(quit_button.text if quit_button != null else "", "退出游戏", "退出按钮文案")

	_check_no_id_tokens(menu, "启动菜单")
	scene_tree.root.remove_child(menu)
	menu.free()


## 3) 游戏内枢纽：提示条要指路，不能再写「尚未接入」
func _check_hub_copy() -> void:
	if scene_tree == null:
		fail("没有注入场景树，枢纽文案检查无法进行")
		return
	var hub = load(PLACEHOLDER_SCENE).instantiate()
	scene_tree.root.add_child(hub)
	hub.setup()
	var hint: Label = hub.find_child("Hint", true, false)
	check_not_null(hint, "枢纽页有提示标签")
	if hint != null:
		check_true(hint.text.contains("大地图"), "提示条指向大地图：%s" % hint.text)
		check_true(hint.text.contains("战斗"), "提示条提到战斗：%s" % hint.text)
	_check_no_id_tokens(hub, "游戏内枢纽")
	scene_tree.root.remove_child(hub)
	hub.free()


## 5) 玩家可见的每一段文案里**不许出现「表内英文 id」那种形态**
## （`chapter_01`／`eq_sword_01`／`facility_pawnshop`…）。机械判据：小写字母打头、中间带下划线的 token。
##
## 这一条是**活的**：2026-10-03 枢纽页那行「章节：第 1 章（chapter_01）」就把章节 id 甩给了玩家——
## 当时的审计只钉了「不许说假话」，没钉「不许漏 id」（见框架说明决策 242）。
## 中文标点、`Lv1`、`F5`、`Tab`、`Esc` 这些都不带下划线，不会被误伤。
func _check_no_id_tokens(node: Node, where: String) -> void:
	var token_re := RegEx.new()
	token_re.compile("[a-z][a-z0-9]*_[a-z0-9_]+")
	var offenders := PackedStringArray()
	for text: String in _visible_texts(node):
		if token_re.search(text) != null:
			offenders.append(text.strip_edges())
	check_eq(
		offenders.size(), 0,
		"%s 的可见文案里不许出现表内 id 形态（往表里要中文名，id 只留给日志）：%s"
			% [where, "；".join(offenders)],
	)


## 某个界面下所有 Label／Button／RichTextLabel 的文字（递归）
func _visible_texts(node: Node) -> PackedStringArray:
	var out := PackedStringArray()
	for child in node.get_children():
		if child is Label:
			out.append((child as Label).text)
		elif child is Button:
			out.append((child as Button).text)
		elif child is RichTextLabel:
			out.append((child as RichTextLabel).text)
		out.append_array(_visible_texts(child))
	return out


## 6) 存档里出现**配表里没有的** id 时，标签同样不许把 id 摆给玩家。
##
## 现实里发生过：设计从 `character_base` 里删过角色（读档要剔掉「已下架角色」）；
## 难度改名同理。这类标签出现在**启动菜单的存档列表**与**枢纽页状态行**上，都是玩家可见的。
func _check_stale_save_labels() -> void:
	var db = get_db()
	var state = solo_state(db)
	state.char_ids = PackedStringArray(["scholar_gone"])
	state.char_levels = {"scholar_gone": 3}
	state.difficulty_id = "difficulty_gone"
	var label := state.short_label(db)
	check_false(label.contains("scholar_gone"), "下架角色不进存档列表标签：%s" % label)
	check_false(label.contains("difficulty_gone"), "未知难度不进存档列表标签：%s" % label)
	check_true(label.contains("已下架"), "如实说明这个角色已下架：%s" % label)
	check_true(label.contains("未知难度"), "如实说明难度不认识：%s" % label)
	var joined := "\n".join(state.summary_lines(db))
	check_false(joined.contains("scholar_gone"), "枢纽页状态行也不漏角色 id：%s" % joined)
	check_false(joined.contains("difficulty_gone"), "枢纽页状态行也不漏难度 id：%s" % joined)


## 4) 招式用不了的原因说中文名，别让玩家看表里的 id；**DoT 类招式是能用的**（不是"未接结算"）
##
## 由来（2026-10-03）：这里原来断言 `_is_supported_damage("dot_poison") == false`（"DoT 仍标为未接结算"）——
## 那在 DoT 里程碑落地**之前**是对的，之后就成了**替一个过期的过滤条件站台**：五毒掌／烈火掌那 6 招
## 明明有完整结算（`resolve()` 的 dot 分支 + `resolve_dot()` 快照 + 回合末跳字），却永远没有按钮。
## 现在这条改成：DoT 可用、只有反伤（`dmg_reflect`）不可用；真正不可用时那条中文原因也照样验。
func _check_unsupported_damage_reason() -> void:
	var db = get_db()
	var hero = _actor("hero", BattleActorScript.SIDE_ALLY)
	hero.skills = PackedStringArray(["sk_wudu_02"])   # dot_poison，中毒
	var foe = _actor("foe", BattleActorScript.SIDE_ENEMY, {"hp_max": 9999.0})
	var sim = BattleSimulatorScript.new(db, RngServiceScript.new(SEED))
	sim.setup([hero], [foe], {"modifiers": {"force_hit": true, "no_variance": true, "no_block": true}})

	var options: Array = sim.skill_options(hero)
	check_eq(options.size(), 1, "中毒招式列出来了")
	if not options.is_empty():
		var option: Dictionary = options[0]
		var reason := str(option["reason"])
		check_true(bool(option["ok"]), "DoT 类招式**可用**（五毒掌那一路要能点出来）：%s" % reason)
		check_eq(reason, "", "可用时不给原因")
	check_true(sim._is_supported_damage("dmg_normal"), "直伤仍然可用（对照，证明不是把所有招都判死了）")
	check_true(sim._is_supported_damage("dot_poison"), "中毒类可用（结算早就有了）")
	check_true(sim._is_supported_damage("dot_burn"), "灼伤类可用")
	check_false(sim._is_supported_damage("dmg_reflect"), "反伤仍然不可用（只有它真的没接）")
	# 真正不可用时，那句原因必须用中文名、不许漏表内 id
	var reflect_reason: String = sim._unsupported_damage_reason("dmg_reflect")
	check_true(reflect_reason.contains("反伤"), "不可用的原因用中文名：%s" % reflect_reason)
	check_false(reflect_reason.contains("dmg_"), "原因不泄露表里的英文 id：%s" % reflect_reason)


## 5) 奇袭的暂缓规则说明：说「等设计定效果」，不甩锅给已经做完的架势系统
func _check_surprise_pending_rule() -> void:
	var db = get_db()
	var spawn: Resource = db.get_row("roaming_spawn", "sp_lp_wolf_01")
	var team: Resource = db.get_row("enemy_team", "team_wolf_pack")
	var surprise = EncounterScript.build(db, spawn, team, EncounterScript.CONTACT_SLEEP, "normal")
	check_eq(surprise.pending_rules.size(), 1, "奇袭只挂一条待定规则（敌方无防备）")
	if surprise.pending_rules.is_empty():
		return
	var rule := str(surprise.pending_rules[0])
	check_true(rule.contains("无防备"), "规则文案点名「无防备」：%s" % rule)
	check_true(rule.contains("设计"), "规则文案说明是设计侧没定效果：%s" % rule)
	check_false(rule.contains("架势"), "不再把责任推给已经实现的架势系统：%s" % rule)


## 6) **玩家可见文案里不许出现表名**——场景自检量不到的那一半，用静态扫描兜住。
##
## 由来（2026-10-04，决策 330）：上一轮抓到 NPC 兜底那句把表名 `npc_def` 甩给玩家，靠的是**逐个交互路径**
## （五个站位逐个按一遍）。可这类泄漏散在**没人走到的分支**里——数据错路径、道具列表的说明、世界事件的提示……
## 场景自检是「跑到哪儿扫哪儿」：跑不到的分支它永远报 0。所以这里换成**按对象扫**：
## 把 `data/tables/*.csv` 的表名当词表（唯一真相是数据目录本身，不手抄一份），扫 `src/` 里的**字符串字面量**，
## 只要一句**中文**里夹着表名就报出来。
##
## 三条排除（都写死在判据里，不靠人记）：
##   · `table_validator.gd`：构建期校验器的消息**天生是给开发看的**（跑在编辑器／命令行里，不进游戏界面）；
##   · 同一行有 `push_error／push_warning／printerr／print(` 的（单行日志）；
##   · 以 `"[` 开头的句子：这个工程的日志一律带 `[模块]` 前缀，多行 `push_warning(` 也能被这条抓住。
## 剩下的若确实是内部用途，写进 `TABLE_NAME_IN_TEXT_ALLOWED` 并写明理由；**补上就要删行**（双向维护）。
func _check_no_table_names_in_player_text() -> void:
	var table_names := _table_names()
	check_gt(float(table_names.size()), 30.0, "从 data/tables 读到足够多的表名（%d 个）" % table_names.size())
	var alt := ""
	for name: String in table_names:
		alt += ("|" if alt != "" else "") + name
	var tok_re := RegEx.new()
	tok_re.compile("\\b(%s)\\b" % alt)
	var lit_re := RegEx.new()
	lit_re.compile("\"([^\"]*)\"|'([^']*)'")
	var hits: Dictionary = {}     # 文件名 → 命中行
	for path: String in _gd_files("res://src"):
		if path.ends_with("table_validator.gd"):
			continue
		var text := FileAccess.get_file_as_string(path)
		if text.is_empty():
			continue
		var line_no := 0
		for raw: String in text.split("\n"):
			line_no += 1
			if raw.contains("push_error") or raw.contains("push_warning") \
					or raw.contains("printerr") or raw.contains("print("):
				continue
			var code := raw
			var hash := code.find("#")
			if hash >= 0:
				code = code.substr(0, hash)
			for m: RegExMatch in lit_re.search_all(code):
				var lit := m.get_string(0)
				if not _has_cjk(lit) or tok_re.search(lit) == null:
					continue
				if lit.begins_with("\"["):
					continue     # `"[模块] …"` 是日志句
				var key := path.get_file()
				if not hits.has(key):
					hits[key] = PackedStringArray()
				# PackedStringArray 是**值类型**：`Array(hits[key])` 只是拷一份，改了不会回到字典里
				# （第一版就是这么写的，报出来的命中明细是空的）。取出来、追加、再放回去。
				var bucket: PackedStringArray = hits[key]
				bucket.append("%d ← %s" % [line_no, lit])
				hits[key] = bucket
	var bad := PackedStringArray()
	for file_name: String in hits.keys():
		if _allowed_file(file_name):
			continue
		bad.append("%s:%s" % [file_name, ", ".join(Array(hits[file_name]))])
	check_eq(
		bad.size(), 0,
		"这些中文文案里夹着表名（玩家看得到吗？真看得到就改人话＋表名进日志；内部用途就写进 TABLE_NAME_IN_TEXT_ALLOWED 并写明理由）：%s"
			% "；".join(bad)
	)
	# 白名单**双向**维护：修好了／删了那段代码就要把那行删掉（过期白名单 = 下一个人照着它白找）
	var stale := PackedStringArray()
	for allowed_path: String in TABLE_NAME_IN_TEXT_ALLOWED.keys():
		if not hits.has(allowed_path.get_file()):
			stale.append(allowed_path.get_file())
	check_eq(stale.size(), 0, "白名单里这些文件已经没有夹表名的文案了，请删掉对应行：%s" % "、".join(stale))


func _has_cjk(text: String) -> bool:
	for index in text.length():
		var code := text.unicode_at(index)
		if code >= 0x4E00 and code <= 0x9FFF:
			return true
	return false


## 从数据目录本身读表名——**别在用例里手抄一份**（加一张表就漏一个词）。
func _table_names() -> PackedStringArray:
	var out := PackedStringArray()
	var dir := DirAccess.open("res://data/tables")
	if dir == null:
		return out
	dir.list_dir_begin()
	var entry := dir.get_next()
	while entry != "":
		if not dir.current_is_dir() and entry.ends_with(".csv"):
			out.append(entry.get_basename())
		entry = dir.get_next()
	dir.list_dir_end()
	return out


## 白名单里记的是完整路径；命中表里只留文件名，这里把两边对上。
func _allowed_file(file_name: String) -> bool:
	for allowed_path: String in TABLE_NAME_IN_TEXT_ALLOWED.keys():
		if allowed_path.get_file() == file_name:
			return true
	return false


## 递归收集 res:// 下的 .gd（不靠 .godot 缓存，纯文件系统遍历）
func _gd_files(root: String) -> PackedStringArray:
	var out := PackedStringArray()
	var stack: Array[String] = [root]
	while not stack.is_empty():
		var dir_path: String = stack.pop_back()
		var dir := DirAccess.open(dir_path)
		if dir == null:
			continue
		dir.list_dir_begin()
		var entry := dir.get_next()
		while entry != "":
			var full := dir_path.path_join(entry)
			if dir.current_is_dir():
				stack.append(full)
			elif entry.ends_with(".gd"):
				out.append(full)
			entry = dir.get_next()
		dir.list_dir_end()
	return out


func _actor(id: String, side: int, overrides: Dictionary = {}):
	var actor = BattleActorScript.new()
	actor.actor_id = id
	actor.display_name = id
	actor.side = side
	actor.level = 10
	actor.base_accuracy = 1.0
	actor.stats = {
		"hp_max": 300.0, "atk_phys": 40.0, "atk_qi": 40.0, "def_phys": 0.0, "def_qi": 0.0,
		"speed": 10.0, "hit_rate": 0.5, "dodge_rate": 0.0, "crit_rate": 0.0, "crit_dmg": 0.0,
		"qi_max": 100.0, "qi_regen": 1.0, "hp_regen": 0.0, "pen_rate": 0.0, "block_rate": 0.0,
		"block_reduction": 0.0, "dmg_reduction": 0.0, "debuff_chance": 0.0, "debuff_power": 0.0,
		"poise_break": 0.0,
	}
	for key: String in overrides:
		actor.stats[key] = overrides[key]
	actor.poise_max = 100
	actor.refill()
	return actor
