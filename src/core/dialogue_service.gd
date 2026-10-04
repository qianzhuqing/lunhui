## 对话容器（设计 20 §十一）：**台词与选项都在表里**，这里只做三件事——
## 「这个人现在说哪句」「这句有哪些选项可选」「选了之后世界改了什么」。
##
## 为什么单独立一张表而不是塞进 `npc_def`／`npc_quest`：
## 剧情的落点是「对话」（一个人说的话 ＋ 玩家的选择），而 `npc_def` 那一行是**身份卡**、
## `npc_quest` 是**委托**——它们都装不下"一句话 ＋ 三四个带前置的选项"。
##
## 纯逻辑、不碰场景：面板与用例共用同一份判定，谁也别自己再解析一遍条件。
class_name DialogueService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")
## 选项给东西时走**与掉落／事件奖励同一条入账口径**（货币进钱、装备建实例、其余堆叠）
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const DialogueOptionRowScript := preload("res://src/data/tables/dialogue_option_row.gd")

## 序幕·择念那个节点（设计 20 §3.1）：读清风驿的悬赏板时摆出来，问过一次就不再问。
## **唯一出处**——「什么时候摆」（`local_map_controller`）与「选项要顺带置 `flag_open_*`」
## （本文件的 `choose`）都读它，谁都不许再抄一份节点 id。
const OPENING_NODE := "dl_opening_choice"


## 这个人有没有配对话（面板用它决定要不要摆「聊聊」那一段）
static func has_dialogue(db, speaker_id: String) -> bool:
	if db == null or speaker_id.is_empty():
		return false
	for row: Resource in db.rows("dialogue_node"):
		if str(row.speaker_id) == speaker_id:
			return true
	return false


static func node_of(db, node_id: String) -> Resource:
	return db.get_row("dialogue_node", node_id) if db != null and not node_id.is_empty() else null


## 这个人**现在**该说的那一句：按 `sort_order` 取第一条条件满足的节点；没有就 null。
##
## **「选完之后的接续节点」不算入口**：被某个 `dialogue_option.next_node_id` 指到的那些
## （序幕三条心理独白 `dl_open_*`、终局三选一的幕结 `dl_ledger_reply_*`）是**点选项跳过去**的，
## 不是「再找这个人说话时他该说的第一句」。不排掉的话，选完终局难题再点她一次，
## 她会把幕结那句（「你选了我想要的……」）当问候重念一遍——2026-10-04 文案批次落表时踩到。
##
## 「条件满足」用与引导／招募／剧情节点**同一份**条件语言（`GuideService.condition_met`）——
## 不另造一套，免得"同一句话在两处判定不一样"。
static func entry_node(db, state, speaker_id: String) -> Resource:
	if db == null or speaker_id.is_empty():
		return null
	var continuations := _option_targets(db)
	var rows: Array = []
	for row: Resource in db.rows("dialogue_node"):
		if str(row.speaker_id) == speaker_id:
			rows.append(row)
	rows.sort_custom(
		func(a: Resource, b: Resource) -> bool: return int(a.sort_order) < int(b.sort_order)
	)
	for row: Resource in rows:
		if continuations.has(str(row.node_id)):
			continue
		if condition_ok(state, str(row.condition)):
			return row
	return null


## 被选项指到的节点（`dialogue_option.next_node_id`）：它们是**接续**，不是入口
static func _option_targets(db) -> Dictionary:
	var out := {}
	for row: Resource in db.rows("dialogue_option"):
		var next_id := str(row.next_node_id).strip_edges()
		if not next_id.is_empty():
			out[next_id] = true
	return out


## 某个节点上**现在可选**的选项（按 `sort_order`；`condition` 是设计 20 §四 的「前置」）。
static func options_for(db, state, node_id: String) -> Array:
	var out: Array = []
	if db == null or node_id.is_empty():
		return out
	for row: Resource in db.rows("dialogue_option"):
		if str(row.node_id) != node_id:
			continue
		if not condition_ok(state, str(row.condition)):
			continue
		out.append(row)
	out.sort_custom(
		func(a: Resource, b: Resource) -> bool: return int(a.sort_order) < int(b.sort_order)
	)
	return out


## 条件语言：留空 = 无条件；其余交给 `GuideService.condition_met`（含 `&` 串接与 `origin:`）
static func condition_ok(state, condition: String) -> bool:
	if condition.strip_edges().is_empty():
		return true
	return GuideServiceScript.condition_met(state, condition.strip_edges())


## 选一个选项：先**落地效果**（置旗标 → 好感 → 给东西），再告诉调用方下一句是哪句。
##
## 返回 {ok, option_id, text, next_node_id, flag, favor, item, error}——
## 效果按这个顺序结算，任何一步失败都**如实写进 error**，不静默吞掉（配错 id 的代价是
## 玩家选了没反应，那比报错难查得多）。
static func choose(db, state, option_id: String) -> Dictionary:
	var out := {
		"ok": false, "option_id": option_id, "text": "", "next_node_id": "",
		"flag": "", "favor": 0, "item": "", "error": "",
	}
	if db == null or state == null:
		out["error"] = "没有会话状态"
		return out
	var row: Resource = db.get_row("dialogue_option", option_id)
	if row == null:
		out["error"] = "没有这个选项"
		return out
	if not condition_ok(state, str(row.condition)):
		out["error"] = "这个选项现在不该出现（前置没满足）"
		return out
	out["text"] = str(row.text_cn)
	out["next_node_id"] = str(row.next_node_id)

	var flag_text := str(row.set_flag)
	if not flag_text.is_empty():
		# 一列可以写多个旗标（分号隔开，Q83）：幕二「拔剑」那条要同时置
		# `flag_lin_silent` 与 `flag_luoyanpo_met`。**拆法只有一处**（行类的 `parse_set_flags`），
		# 与构建期校验、PS1 那份校验同一口径；单值（旧数据）行为一字不变。
		var flags: PackedStringArray = DialogueOptionRowScript.parse_set_flags(flag_text)
		for flag: String in flags:
			state.set_flag(flag)
		out["flag"] = flag_text
		# 序幕·择念（设计 20 §3.1，0.32.0 补）：**心性由序幕一次定死**——称号与幕结旁白只看
		# `flag_open_*`，而后面各幕的选项照旧只置当场反应的 `heart_*`。
		# 对应关系是同一个后缀（`heart_yi` → `flag_open_yi`），所以既不逐条抄选项 id、
		# 也不用给 `dialogue_option` 加列（表侧不加列是设计侧这一版的拍板）。
		# 多值时按"这一份里带 `heart_*` 的每一个"走。
		if str(row.node_id) == OPENING_NODE:
			for flag: String in flags:
				if flag.begins_with("heart_"):
					state.set_flag("flag_open_%s" % flag.substr("heart_".length()))

	var delta := int(row.favor_delta)
	if delta != 0:
		var speaker := _speaker_of(db, str(row.node_id))
		if speaker.is_empty():
			out["error"] = "这个选项要改好感，但节点没写说话人"
			return out
		var favor: Dictionary = NpcServiceScript.add_favor(db, state, speaker, delta)
		if not bool(favor.get("ok", false)):
			out["error"] = str(favor.get("error", "好感没改上"))
			return out
		out["favor"] = delta

	var item_id := str(row.grant_item_id)
	if not item_id.is_empty():
		var granted: Dictionary = BattleRewardScript.grant_item(db, state, item_id, 1)
		if not bool(granted.get("ok", false)):
			out["error"] = str(granted.get("error", "东西没发出去"))
			return out
		out["item"] = item_id

	out["ok"] = true
	return out


## 选项挂在哪个人身上（`favor_delta` 要改谁的好感）
static func _speaker_of(db, node_id: String) -> String:
	var node: Resource = node_of(db, node_id)
	return str(node.speaker_id) if node != null else ""
