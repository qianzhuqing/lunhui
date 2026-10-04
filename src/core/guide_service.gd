## 开局引导（设计 09 §3.1）：HUD 上常驻一行「当前目标」，条件满足就自动推进。
##
## 数据源 `guide_step.csv`（4 步）。每行的 `condition` 就是**「这一步成为当前目标」的判据**：
## 取值是剧情旗标（`GameState.flags`），第一步用哨兵 `start`（开局即满足，表里没有对应的旗标）。
##
## 口径：**当前步 = 条件已满足的最后一行**——旗标一个个点亮，目标就一行行往前走。
## 最后一步（「出城上山」）的条件满足后停在最后一步：设计表只有 4 步，没有再往后的目标。
## 条件本身由谁点亮是各系统的事（悬赏板发 `flag_board_read`、招募发 `flag_ch_ci_joined`…），
## 这里只读旗标，不自己发明触发点。
class_name GuideService
extends RefCounted

## 第一步的哨兵：设计表里写的是「开局」，没有旗标可查。
const START_CONDITION := "start"
## 条件语言的前缀（设计 21 §9.3）：`origin:<char_id>` = 主角正是这个出身。
## 前缀只在这里写一次——`story_node` 等表里都按它拼。
const ORIGIN_PREFIX := "origin:"
## `item:<id>`／`item:<id>:<数量>` = 背包里有这件东西（数量省略＝1）。
## 与 `npc_quest.requirement` 用的是同一套写法（那边由 `NpcService` 解析），
## 这里把它接进条件语言，好让「必须拿着某件东西才谈这件事」也能写在表里
## ——例：终局难题只在**账册到手**之后才问（20 §七）。
const ITEM_PREFIX := "item:"
## 备货旗标（设计 18 §3.2 的 `gs_04`）：**判定口径写在 `condition_spec` 里**——
## 「等级 ≥ 5 且背包里有回血道具」。这个旗标以前没人置（断点 1），现在由
## `refresh_derived_flags()` 按那条口径算出来；名字只在这里写一次。
const SUPPLIES_FLAG := "flag_supplies_ready"
## 备货要求的等级（设计 18 的 `condition_spec` 写明 5 级）
const SUPPLIES_LEVEL := 5
## 进寨旗标（设计 18 的断点 2）：`flag_arrived_heifengzhai` 原来也没有来源。
const ARRIVED_HEIFENGZHAI_FLAG := "flag_arrived_heifengzhai"
## 「前寨」那张图（`map_local.scene_id`）——**别在别处再写一遍这个字符串**。
const HEIFENGZHAI_SCENE := "scene_heifengzhai"


## 按 `sort_order` 排好的引导步骤（表里的顺序就是玩家看到的顺序）。
static func steps(db) -> Array:
	var rows: Array = db.rows("guide_step").duplicate()
	rows.sort_custom(func(a, b) -> bool: return int(a.sort_order) < int(b.sort_order))
	return rows


static func condition_met(state, condition: String) -> bool:
	var text := condition.strip_edges()
	if text == START_CONDITION:
		return true
	if state == null:
		return false
	# 条件可以用 `&` 串起来（**全部满足**才算满足）：本命机遇的时机就是
	# 「看过告示**且**主角是书生」这种两段式。空串没人用，`&` 两侧也不许有空段——
	# 写坏了宁可当场不满足（返回 false），不要静默当成"没条件"。
	if text.contains("&"):
		for part: String in text.split("&", false):
			if part.strip_edges().is_empty():
				return false
			if not condition_met(state, part.strip_edges()):
				return false
		return true
	# `|` = **任一满足**（2026-10-04 加，供「三选一做过没有」这类判定用：结算之后再说话，
	# 而三个旗标只能写一条条件）。拆分顺序是 `&` 先、`|` 后，所以 `a|b&c` = (a 或 b) 且 c。
	if text.contains("|"):
		for part: String in text.split("|", false):
			if part.strip_edges().is_empty():
				return false
			if condition_met(state, part.strip_edges()):
				return true
		return false
	# `!` = 取反（写在整条前面）：三选一的「还没选过」就靠它——
	# 否则玩家能反复进去选，三个永久增益会叠在一起（设计 20 §八 第 6 条：三选一、不叠加）。
	if text.begins_with("!"):
		var inner := text.substr(1).strip_edges()
		if inner.is_empty():
			return false
		return not condition_met(state, inner)
	# 条件语言多一种写法（设计 21 §9.3，0.31.0）：`origin:<char_id>` = **主角正是这个出身**。
	#
	# 为什么走条件语言而不是"触发点置一把旗标"：出身是**创建时就定死**的事实，
	# 它不是一个"发生过的事件"；而能置旗的两个通道（`event_check`／`hidden_trigger`）
	# **都会污染计数**（前者进事件判定＋线索本＋副本完成度，后者进隐藏内容）——
	# 和观察点是同一个坑。主角就是开局第一个人（`char_ids[0]`，`CreationService` 把
	# 玩家选的 `origin_id` 传给 `new_game()`）。
	if text.begins_with(ORIGIN_PREFIX):
		var origin_id := text.substr(ORIGIN_PREFIX.length())
		return not origin_id.is_empty() and not state.char_ids.is_empty() \
			and str(state.char_ids[0]) == origin_id
	# `item:<id>`／`item:<id>:<数量>`：背包里有没有这件东西（与 `npc_quest.requirement` 同一种写法）。
	# 判错时宁可**不满足**：条件写坏了，玩家看不到那段内容（有日志/用例兜着），
	# 而不是把「没账册也能做终局抉择」这种漏洞放进去。
	if text.begins_with(ITEM_PREFIX):
		if state.inventory == null:
			return false
		var parts: PackedStringArray = text.substr(ITEM_PREFIX.length()).split(":")
		if parts.is_empty() or parts[0].strip_edges().is_empty():
			return false
		var need := 1
		if parts.size() > 1:
			need = maxi(1, int(parts[1]))
		return state.inventory.count(parts[0].strip_edges()) >= need
	return state.has_flag(text)


## 把「判定口径在表里、但没人去置」的旗标算出来（设计 18 的断点 1）。
##
## 目前只有一条：`gs_04` 的 `flag_supplies_ready`。返回这次真正点亮的旗标名，
## 调用方可以据此播报（场景层拿它给一句提示）。
static func refresh_derived_flags(db, state) -> PackedStringArray:
	var set_now := PackedStringArray()
	if db == null or state == null:
		return set_now
	if not state.has_flag(SUPPLIES_FLAG) and supplies_ready(db, state):
		state.set_flag(SUPPLIES_FLAG)
		set_now.append(SUPPLIES_FLAG)
	return set_now


## 备货够了没有：**队里至少一人到 5 级**（木桩练级是练"当前这个人"的，要求全员到 5 太苛刻）
## 且背包里有消耗品。
##
## 为什么是"任意消耗品"：设计 18 写的是「回血道具」，可 `item_base` **没有一列说这件东西回什么**
## （只有 `use_context`），开发侧不解析 `desc` 里的散文取数。这条宽口径已记
## `待策划确认.md` Q59，设计给列之后这里只改一处。
static func supplies_ready(db, state) -> bool:
	if state == null:
		return false
	var level_ok := false
	for char_id: String in state.char_ids:
		if state.level_of(char_id) >= SUPPLIES_LEVEL:
			level_ok = true
			break
	if not level_ok:
		return false
	for item_id: String in state.inventory.item_ids():
		var row: Resource = db.get_row("item_base", item_id)
		if row == null:
			continue
		if str(row.item_type) == "consumable" and not str(row.use_context).is_empty():
			return true
	return false


## 某张图的「入口房间」（`dungeon_room.room_type=entrance`）。
static func entrance_room_of(db, scene_id: String) -> String:
	if db == null or scene_id.is_empty():
		return ""
	for row: Resource in db.rows("dungeon_room"):
		if str(row.scene_id) == scene_id and str(row.room_type) == "entrance":
			return str(row.room_id)
	return ""


## 玩家走进某个房间时调一次：点亮**只能在场景里判**的那几条旗标。
##
## 目前只有一条——「首次进入黑风寨前寨」（设计 18 的断点 2）。判据是**入口房间**
## （`room_type=entrance`）而不是"进了这张图"：从后山密道直接下到三层地牢不算"进寨"。
static func note_room_entered(db, state, scene_id: String, room_id: String) -> PackedStringArray:
	var set_now := PackedStringArray()
	if db == null or state == null or room_id.is_empty():
		return set_now
	if state.has_flag(ARRIVED_HEIFENGZHAI_FLAG):
		return set_now
	if scene_id != HEIFENGZHAI_SCENE:
		return set_now
	if room_id != entrance_room_of(db, scene_id):
		return set_now
	state.set_flag(ARRIVED_HEIFENGZHAI_FLAG)
	set_now.append(ARRIVED_HEIFENGZHAI_FLAG)
	return set_now


## 当前目标行；表为空（或没配 `start`）时返回 `{}`。
static func current(db, state) -> Dictionary:
	var result: Dictionary = {}
	for row: Resource in steps(db):
		if condition_met(state, str(row.condition)):
			result = {
				"step_id": str(row.step_id),
				"index": int(row.sort_order),
				"text_cn": str(row.text_cn),
				"condition": str(row.condition),
			}
	return result


## HUD 那一行的文案：`当前目标：…（2/4）`；没配引导表时返回空串（HUD 不占位）。
static func hud_text(db, state) -> String:
	var row: Dictionary = current(db, state)
	if row.is_empty():
		return ""
	return "当前目标：%s（%d/%d）" % [
		str(row["text_cn"]), int(row["index"]), steps(db).size(),
	]
