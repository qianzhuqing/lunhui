## 剧情招募（设计 09 §3.2）：开局只有 `recruit_def.is_initial=1` 的人，其余同伴按剧情加入。
##
## 数据源 `recruit_def.csv`：`join_condition` 是旗标 id，`join_scene` 既可能是
## `map_local.scene_id`（小地图／副本，如清风驿、黑风寨），也可能是 `map_region.node_id`
## （大地图上的区域，如落雁坡——它没有自己的小地图）。两条都在这里认。
##
## 口径：**「条件旗标已点亮 ∧ 人在那张图（或站到那个区域地标前）∧ 还没入队」→ 入队**。
## 谁来点亮条件旗标是各系统的事（燕小七要的 `flag_board_read` 由清风驿的悬赏板发出；
## 另外三位的 `flag_luoyanpo_met`／`flag_poison_hall`／`flag_huangcun_done` 目前**没有任何来源**——
## 那是设计侧的剧情内容，没到之前他们不会入队，代码这边不自己编触发点）。
##
## 入队动作本身只有一处：`GameState.add_character()`（发等级／起始装备／起始武学并铺装配）。
class_name RecruitService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")
## 入队时发的奖励走**与掉落／事件奖励同一条入账口径**（货币进钱、装备建实例、其余堆叠）
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const NpcServiceScript := preload("res://src/core/npc_service.gd")


## 入队时发的「支线奖励」（设计 20 号 §九 那张表：四条招募支线各写「入队 ＋ 某物」＋ 好感 ＋20）。
##
## **暂时写在这里**：`recruit_def` 没有奖励列（**Q72** 给了三个选项：加列／改用招募对话的选项／不发）。
## 两条**不用**写进来——林铁山的柴刀就是他的起始武器（`start_equip_ids`）、苏九娘的五毒散手
## 就是她的起始武学（`start_skill_ids`），那两样 `GameState.add_character()` 已经发了 ✅。
## 所以这里只补「起始装备／起始武学之外」的两件：燕小七的回气散、白清和的药酒。
##
## 好感那一列四条都是 ＋20（也是 §九 表的数）；加的是**这位同伴自己**的好感
## （同伴有 `npc_favor` 行，见 `框架说明.md` 决策 296）。
const JOIN_REWARDS := {
	"ch_ci": {"items": ["item_potion_qi"], "favor": 20},
	"ch_gang": {"items": [], "favor": 20},
	"ch_du": {"items": [], "favor": 20},
	"ch_qi": {"items": ["item_med_02"], "favor": 20},
}


## 某一行「是不是在这张图里等着」：`join_scene` 命中 scene_id，或命中这张图的父区域节点。
static func scene_matches(db, row: Resource, scene_id: String) -> bool:
	var want := str(row.join_scene)
	if want.is_empty() or scene_id.is_empty():
		return false
	if want == scene_id:
		return true
	var local: Resource = db.get_row("map_local", scene_id)
	return local != null and str(local.parent_node) == want


## 还没入队、条件也满足的招募行。
##
## **`is_initial=1` 不再被跳过**（设计 0.30.0）：主角是**玩家选出来的**（`CreationService.build()`
## 把 `origin_id` 传给 `new_game()`），所以"选谁就是谁"，`is_initial` 只作**兜底**。
## 以前这里跳过初始行，而 `scholar_fallen` 是**唯一**一行 `is_initial=1`——于是主角选别人时，
## **书生永远无法入队**（他是 Q51 四位同伴之一）。下面那句 `state.char_ids.has(...)` 已经管住重复：
## 主角是书生时他本来就在队里、照样跳过；是别人时他不在队里、可以按剧情入队。
static func _ready_rows(db, state, match_call: Callable) -> Array:
	var out: Array = []
	if state == null:
		return out
	for row: Resource in db.rows("recruit_def"):
		if state.char_ids.has(str(row.char_id)):
			continue
		if not match_call.call(row):
			continue
		if not GuideServiceScript.condition_met(state, str(row.join_condition)):
			continue
		out.append(row)
	return out


## 小地图／副本里等着加入的人（按 `map_local.scene_id` 或它的父区域节点匹配）。
static func pending_for_scene(db, state, scene_id: String) -> Array:
	return _ready_rows(db, state, func(row: Resource) -> bool:
		return scene_matches(db, row, scene_id)
	)


## 大地图上等着加入的人（按 `map_region.node_id` 匹配——落雁坡这类没有小地图的区域）。
static func pending_for_region(db, state, node_id: String) -> Array:
	return _ready_rows(db, state, func(row: Resource) -> bool:
		return not node_id.is_empty() and str(row.join_scene) == node_id
	)


## 把一位同伴加进队伍。返回 {ok, char_id, name_cn, note, rewards, error}。
static func join(db, state, char_id: String) -> Dictionary:
	var row: Resource = db.get_row("character_base", char_id)
	var name_cn := str(row.name_cn) if row != null else char_id
	var result: Dictionary = state.add_character(db, char_id) if state != null else {"ok": false, "error": "没有会话状态"}
	if not bool(result.get("ok", false)):
		return {
			"ok": false, "char_id": char_id, "name_cn": name_cn,
			"note": "", "rewards": [], "error": str(result.get("error", "入队失败")),
		}
	var recruit_row: Resource = db.get_row("recruit_def", char_id)
	# 入队点亮一枚旗标 `flag_<char_id>_joined`：设计侧的 `guide_step` 就是拿它当下一步的条件的
	# （现例：`flag_ch_ci_joined`），别在别处再手写一遍这个字符串。
	state.set_flag("flag_%s_joined" % char_id)
	var rewards := _apply_join_rewards(db, state, char_id)
	return {
		"ok": true, "char_id": char_id, "name_cn": name_cn,
		"note": str(recruit_row.join_note) if recruit_row != null else "",
		"rewards": rewards, "error": "",
	}


## 发入队奖励（`JOIN_REWARDS`）：东西走统一入账口径，好感加到这位同伴头上。
## 返回**给人看的**奖励短句（例 `["回气散 ×1", "好感 +20"]`），调用方拼进入队提示。
static func _apply_join_rewards(db, state, char_id: String) -> Array:
	var out: Array = []
	if state == null or not JOIN_REWARDS.has(char_id):
		return out
	var spec: Dictionary = JOIN_REWARDS[char_id]
	for item_id: String in Array(spec.get("items", [])):
		var granted: Dictionary = BattleRewardScript.grant_item(db, state, item_id, 1)
		if not bool(granted.get("ok", false)):
			# 发不出去**出声**（入库口径出问题时，宁可在自检里红，也别静默少一件）
			push_error("[RecruitService] %s 的入队奖励 %s 没发出去：%s" % [
				char_id, item_id, str(granted.get("error", "")),
			])
			continue
		out.append("%s ×1" % _item_name(db, item_id))
	var favor := int(spec.get("favor", 0))
	if favor != 0:
		var added: Dictionary = NpcServiceScript.add_favor(db, state, char_id, favor)
		if bool(added.get("ok", false)):
			out.append("好感 +%d" % favor)
	return out


static func _item_name(db, item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		return str(equip.name_cn)
	var item: Resource = db.get_row("item_base", item_id)
	return str(item.name_cn) if item != null else item_id


## 一次处理完某个场景里所有待加入的人（按表里的顺序），返回结果数组。
static func join_all_for_scene(db, state, scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in pending_for_scene(db, state, scene_id):
		out.append(join(db, state, str(row.char_id)))
	return out


## 大地图上的版本（按区域节点 id）。
static func join_all_for_region(db, state, node_id: String) -> Array:
	var out: Array = []
	for row: Resource in pending_for_region(db, state, node_id):
		out.append(join(db, state, str(row.char_id)))
	return out
