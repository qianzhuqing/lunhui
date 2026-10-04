## 旗标与可达性——"策划对接守卫"的一族（拆自 tests/test_handshake.gd，2026-10-04）。
##
## 拆分的规矩：**只挪位置、不改行为**。常量与共用夹具（_read_csv／_collect_files…）
## 留在基类 tests/test_handshake.gd，本族只放自己这一类的检查，逐条搬过来、一字未改。
## 加检查就写在对应族里；跨族要用某个函数先看它是不是该挪进基类。
extends "res://tests/test_handshake.gd"


func suite_name() -> String:
	return "旗标与可达性"


func run() -> void:
	_check_flag_reachability()
	_check_sidequest_favors()
	_check_set_tier_reachability()




func _check_flag_reachability() -> void:
	var db = get_db()
	var required := PackedStringArray()
	for rule: Array in [
		["recruit_def", "join_condition"], ["guide_step", "condition"],
		["story_node", "trigger_condition"], ["chapter_def", "complete_condition"],
		["dialogue_node", "condition"], ["dialogue_option", "condition"],
		["npc_quest", "requirement"],
	]:
		_collect_required_flags(db, str(rule[0]), str(rule[1]), required)
	check_gt(float(required.size()), 3.0, "扫到足够多「被要求的旗标」（%d 枚）" % required.size())

	var produced := PackedStringArray()
	for row: Resource in db.rows("dialogue_option"):
		_note_flag(produced, str(row.set_flag))
	for table_name: String in ["event_check", "hidden_trigger"]:
		for row: Resource in db.rows(table_name):
			if str(row.reward_type) == "event":
				_note_flag(produced, str(row.reward_id))
	for row: Resource in db.rows("story_node"):
		if str(row.kind) != "chapter_end":
			continue
		var chapter: Resource = db.get_row("chapter_def", str(row.chapter_id))
		if chapter != null:
			_note_flag(produced, str(chapter.complete_condition))
	_collect_code_producers(produced)

	var missing := PackedStringArray()
	for flag_id: String in required:
		if _flag_produced(produced, flag_id) or KNOWN_FLAG_GAPS.has(flag_id):
			continue
		missing.append(flag_id)
	check_eq(
		missing.size(), 0,
		"每一枚被要求的旗标都有人能点亮（没人管的：%s）——要么补来源、要么写进 KNOWN_FLAG_GAPS 并写明等谁"
			% "、".join(missing)
	)
	# 缺口清单**双向**维护：补上了就要把那一行删掉（过期清单 = 下一个人照着它白找）
	var stale := PackedStringArray()
	for flag_id: String in KNOWN_FLAG_GAPS:
		if _flag_produced(produced, flag_id):
			stale.append(flag_id)
	check_eq(stale.size(), 0, "已知缺口清单里这些已经有来源了，请删掉对应行：%s" % "、".join(stale))




## 条件语言里被**要求**的旗标（按 & 拆；只看 flag_*，origin: / item: 那些不是旗标）
func _collect_required_flags(db, table_name: String, column: String, out: PackedStringArray) -> void:
	if not db.tables.has(table_name):
		return
	for row: Resource in db.rows(table_name):
		# 条件语言的三种连接符都要拆：`&` 全部满足、`|` 任一满足、`!` 取反。
		# `!flag_x` 里被**要求**的仍然是 `flag_x`（要求它"没被点亮"也是要求），照收。
		for group: String in str(row.get(column)).replace("|", "&").split("&", false):
			var token := group.strip_edges()
			while token.begins_with("!"):
				token = token.substr(1).strip_edges()
			if token.begins_with("flag_"):
				_note_flag(out, token)




func _note_flag(out: PackedStringArray, flag_id: String) -> void:
	var token := flag_id.strip_edges()
	if token.begins_with("flag_") and not out.has(token):
		out.append(token)




## 代码里的生产者。**故意算「提到过」而不是做数据流**：GDScript 侧读不了 AST，
## 能做的是「这个字面量在代码里被 set 过」；只被读、从没被 set 的旗标因此会漏网
## ——那种情况由「谁要求它」那一侧自己的用例管。
## **口径与限度**：GDScript 侧读不了 AST，做不了「到底谁 set」的数据流，所以这里用
## 「这个 flag 字面量在 src/ 里出现过」当门限——`TEAM_WIN_FLAGS` 这种**装在常量表里**的写法
## （`set_flag(str(TEAM_WIN_FLAGS[team_id]))`）只有这样才认得出来。
## 代价是「只被读、从没被 set」的旗标会漏网；那类只能由「谁要求它」那一侧自己的用例管。
## 真正要抓的是**全项目没人提**的那种（`flag_luoyanpo_met`／`flag_poison_hall`／
## `flag_huangcun_done`／`flag_qiutu_saved` 四条当初都是这样）。
##
## 另有一条**已知的假阴性**：`src/` 里把旗标写在**自检函数**里也算数
## （例：`npc_panel` 的自检手动置过 `flag_qiutu_saved`）。所以真要把「这条链有没有来源」
## 钉死，还得在**那条链自己的用例**里断言（`test_event_check` 就钉了「掷中＝救出来了」
## ——反向验证过：把来源改字，那条会红）。
func _collect_code_producers(out: PackedStringArray) -> void:
	var literal_re := RegEx.new()
	literal_re.compile("[\"'](flag_[a-zA-Z0-9_%]+)[\"']")
	for path: String in _collect_files(["src"]):
		var text := FileAccess.get_file_as_string("res://" + path)
		if text.is_empty():
			continue
		for hit: RegExMatch in literal_re.search_all(text):
			_note_literal(out, hit.get_string(1))




## 带 % 的字面量（如 flag_%s_joined）当模式存下来，比对时按前后缀匹配
func _note_literal(out: PackedStringArray, literal: String) -> void:
	var token := literal.strip_edges()
	if token.is_empty() or out.has(token):
		return
	if token.contains("%"):
		out.append(token)
		return
	_note_flag(out, token)




func _flag_produced(produced: PackedStringArray, flag_id: String) -> bool:
	if produced.has(flag_id):
		return true
	for entry: String in produced:
		if not entry.contains("%"):
			continue
		var parts: PackedStringArray = entry.split("%s")
		if parts.size() != 2:
			continue
		if flag_id.begins_with(parts[0]) and flag_id.ends_with(parts[1]):
			return true
	return false






func _check_sidequest_favors() -> void:
	var doc := FileAccess.get_file_as_string("res://docs/design/20_第一章剧情.md")
	check_false(doc.is_empty(), "读得到 20 号剧情文档")
	var db = get_db()
	# 只扫 §九 那张表：表头是「| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |」
	var start := doc.find("| # | 名称 | 归属 | 类型 | 地点 | 奖励 | 好感 |")
	check_true(start >= 0, "找得到 §九 的十条支线表")
	if start < 0:
		return
	var body := doc.substr(start)
	var rows := 0
	for raw_line: String in body.split("\n"):
		var line := raw_line.strip_edges()
		if line.is_empty() or not line.begins_with("|"):
			break
		var cells := line.split("|")
		if cells.size() < 8:
			continue
		var owner := cells[3].strip_edges()
		if owner == "归属" or owner.begins_with("---"):
			continue
		rows += 1
		if SIDEQUEST_FORCE_SKIP.has(owner):
			continue
		var wanted := PackedInt32Array()
		for piece: String in cells[7].strip_edges().replace("＋", "").split("／", false):
			var digits := ""
			for ch: String in piece:
				if ch.is_valid_int():
					digits += ch
			if not digits.is_empty():
				wanted.append(int(digits))
		check_gt(float(wanted.size()), 0.0, "§九 里「%s」那条写了好感" % owner)
		var npc_id := _npc_id_for_display_name(db, owner)
		check_true(not npc_id.is_empty(), "「%s」按中文名在 npc_def 里找得到" % owner)
		if npc_id.is_empty():
			continue
		var got := PackedInt32Array()
		for quest: Resource in db.rows_where("npc_quest", "npc_id", npc_id):
			got.append(int(quest.reward_favor))
		for value: int in wanted:
			check_true(
				got.has(value),
				"§九 说「%s」这条给 +%d 好感，他的委托好感却是 %s——两边对不上就有一边写错了"
					% [owner, value, str(got)],
			)
	check_gt(float(rows), 5.0, "§九 表里解析出足够多的支线（%d 条）" % rows)




func _npc_id_for_display_name(db, name_cn: String) -> String:
	for row: Resource in db.rows("npc_def"):
		if str(row.name_cn) == name_cn:
			return str(row.npc_id)
	return ""






func _check_set_tier_reachability() -> void:
	var db = get_db()
	var obtainable := _obtainable_ids(db)
	var reachable_skills := _reachable_skill_ids(db)
	var dead := PackedStringArray()
	var tiers := 0
	for def: Resource in db.rows("set_def"):
		var set_id := str(def.set_id)
		var kind := str(def.set_kind)
		var members := PackedStringArray()
		for member_row: Resource in db.rows("set_member"):
			if str(member_row.set_id) == set_id:
				members.append(str(member_row.member_id))
		if members.is_empty():
			continue
		var reach_cap := 0
		if kind == "equip":
			var per_slot: Dictionary = {}
			for member: String in members:
				if not obtainable.has(member):
					continue
				var equip: Resource = db.get_row("equip_base", member)
				var slot := str(equip.slot) if equip != null else ""
				per_slot[slot] = int(per_slot.get(slot, 0)) + 1
			for slot: String in per_slot:
				var limit := 1
				var slot_row: Resource = db.get_row("equip_slot_def", slot)
				if slot_row != null:
					limit = int(slot_row.max_equip)
				reach_cap += mini(int(per_slot[slot]), limit)
		elif kind == "skill_active":
			for member: String in members:
				if reachable_skills.has(member):
					reach_cap += 1
		elif kind == "skill_passive":
			for member: String in members:
				if not reachable_skills.has(member):
					continue
				var passive: Resource = db.get_row("skill_passive", member)
				if passive != null:
					reach_cap += int(passive.slot_cost)
		for tier: Resource in db.rows("set_bonus"):
			if str(tier.set_id) != set_id:
				continue
			tiers += 1
			if reach_cap >= int(tier.required_count):
				continue
			dead.append("%s|%d" % [set_id, int(tier.required_count)])
	check_gt(float(tiers), 4.0, "扫到足够多的套装档位（%d 档）" % tiers)
	var unexpected := PackedStringArray()
	for key: String in dead:
		if not KNOWN_UNREACHABLE_SET_TIERS.has(key):
			unexpected.append(key)
	check_eq(
		unexpected.size(), 0,
		"这些套装档位按已实现的来源凑不出来（要么补来源、要么写进 KNOWN_UNREACHABLE_SET_TIERS 并写明等谁）：%s"
			% "、".join(unexpected)
	)
	# 缺口清单**双向**维护：设计把来源补上之后，这里会红，提示删掉那一行
	var stale := PackedStringArray()
	for key: String in KNOWN_UNREACHABLE_SET_TIERS:
		if not dead.has(key):
			stale.append(key)
	check_eq(stale.size(), 0, "已知缺口清单里这些档位已经凑得出来了，请删掉对应行：%s" % "、".join(stale))




## 「已经发得出来」的 id 集合——与 `validate_tables.ps1` 5.19 同一套通道，再加**代码里点名的 id**
## （剧情物由代码发，如 `battle_screen.TEAM_WIN_ITEMS` 那本账册）。
func _obtainable_ids(db) -> Dictionary:
	var out: Dictionary = {}
	for table_name: String in ["drop_table", "shop_stock", "npc_offer"]:
		for row: Resource in db.rows(table_name):
			var key := str(row.item_id)
			if key != "":
				out[key] = true
	for row: Resource in db.rows("enemy_equip"):
		out[str(row.equip_id)] = true
	for row: Resource in db.rows("world_event"):
		# `gift` 的 effect_id 才是物品（`trade` 是货架组、`spar` 是队伍）
		if str(row.effect_kind) == "gift" and str(row.effect_id) != "":
			out[str(row.effect_id)] = true
	for table_name: String in ["event_check", "hidden_trigger"]:
		for row: Resource in db.rows(table_name):
			var key := str(row.reward_id)
			if key != "":
				out[key] = true
	for row: Resource in db.rows("character_base"):
		for equip_id in str(row.start_equip_ids).split("|", false):
			out[equip_id.strip_edges()] = true
	var equip_ids := PackedStringArray()
	for row: Resource in db.rows("equip_base"):
		equip_ids.append(str(row.equip_id))
	for path: String in _collect_files(["src"]):
		var text := FileAccess.get_file_as_string("res://" + path)
		if text.is_empty():
			continue
		for equip_id: String in equip_ids:
			if text.contains(equip_id):
				out[equip_id] = true
	return out




## 来源已实现的武学 id。`SkillGrant.IMPLEMENTED_SOURCES` 是唯一真相（别在这里再列一遍），
## 外加 `start`——起手武学从角色模板或创建界面的「起始武学」来，**都是玩家能拿到的**
## （`pf_xuanwei_01`／`sk_common_01` 没进任何模板，但自定义路线可以选，所以算拿得到）。
func _reachable_skill_ids(db) -> Dictionary:
	var out: Dictionary = {}
	for row: Resource in db.rows("skill_base"):
		var source := str(row.source_type)
		if source == "start" or SkillGrantScript.IMPLEMENTED_SOURCES.has(source):
			out[str(row.skill_id)] = true
	return out
