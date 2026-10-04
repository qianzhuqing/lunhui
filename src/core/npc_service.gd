## NPC 交往（设计 19 §二，0.25 交付）：好感度、赠礼、切磋、偷窃、兑换、专属任务。
##
## 五张表各管一摊：`npc_def`（身份／位置／切磋队伍／基本信息）、`npc_favor`（好感规则）、
## `npc_offer`（可兑换与可偷）、`npc_quest`（专属任务线）、`world_event`（大地图随机事件，
## 在 `WorldEventService` 里，不在这张表）。
##
## 三条设计纪律在代码里的落点：
##   · **喜好分两级**：`like_item_ids`（最爱）与 `like_categories`（类别）——
##     「送对具体物品是惊喜，送对类别只是礼貌」，所以两个加成不一样大；
##   · **偷窃走事件判定**（不是战斗），失败**扣好感**；`steal_difficulty=0` = **不可偷**
##     （给纯善意的 NPC 留的口子）；
##   · **任务条件复用既有语言**：`item:<id>:<数>`／`flag_xxx`／`event:<check_id>`／`kill_style:<x>`，
##     不新造一套——以后加 NPC 不用改代码。
##
## 四个数按设计 0.28.0 的答复（Q63）：送中最爱 **＋8**、送对类别 **＋2**、送错 **−3**、
## 偷窃失败 **−8**——比例是关键（「偷一下试试、失手只是回到原点」就不是最优解了，
## **偷窃的紧张感来自失手真的疼**）。切磋输**不罚**（输了不扣好感，否则玩家不敢切磋）。
class_name NpcService
extends RefCounted

const BattleRewardScript := preload("res://src/core/battle_reward.gd")
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")
const GuideServiceScript := preload("res://src/core/guide_service.gd")

## 送中最爱：在 `gift_favor` 之上再加这么多（「惊喜」，设计 0.28.0 定 ＋8）
const LIKE_ITEM_BONUS := 8
## 送对类别：只加一点（「礼貌」）
const LIKE_CATEGORY_BONUS := 2
## 送了对方讨厌的东西（设计 0.28.0 定 −3——比「偷窃失手」轻，但仍要疼）
const DISLIKE_PENALTY := -3

## 位点命名的两个前缀（07 §九 第 12 条／§十二）：`npc_` 开头的才算 NPC 位点，
## 其中 `npc_slot_` 是**占位命名**（按行序绑人），`npc_<npc_id>` 是**按 id 绑**。
const SLOT_PREFIX := "npc_"
const SLOT_ID_PREFIX := "npc_slot_"
## 偷窃失败被当场发现（设计 0.28.0 定 −8：**失手真的疼**，这才是偷窃的紧张感来源）
const STEAL_FAIL_PENALTY := -8
## 好感的地板。**好感可以是负的**：`npc_favor.initial_favor` 里陈氏就是 **−10**
## （设计 20 号 §九 也写着「陈氏好感由 −10 转正」），而设计 19 §2.2 只给了「初始值与上限」，
## 没有下限列——所以这里**按数据里最深的那一个**（−10）当地板，不自己发明更狠的数：
## 有地板才谈得上"偷窃失手真的疼"（Q63 的比例）又不会一路扣到救不回来。
## 设计若要更狠或更浅，给 `npc_favor` 加一列 `favor_min`，改这一处即可。
const FAVOR_MIN := -10


# ------------------------------------------------------------------ 静态查表

static func def_of(db, npc_id: String) -> Resource:
	return db.get_row("npc_def", npc_id) if db != null else null


## 对话容器里的**特殊说话人**记号：`player` = 主角自己。
##
## 为什么要有它：设计 20 号 §3.1「序幕·择念」那一句是主角自己心里的话
## （原话：「主角第一次**替自己**开口，而不是接 NPC 的话」），而主角是哪一号人物
## 要等玩家选完出身才知道——表里没法写死一个 `char_id`。所以说话人写这个记号，
## 运行期按 `GameState.char_ids[0]`（`CreationService` 把玩家选的出身放在第一位）解析。
##
## 它**不是一个人**（没有 `npc_def`／`npc_favor` 行）：好感、赠礼、切磋、偷窃都不适用——
## 所以这种对话一律用 `NpcPanel.MODE_STORY` 渲染（只有台词与选项）。
const PLAYER_ID := "player"


## 同伴的所在地：`recruit_def.join_scene`（小地图或大地图节点，与招募链同一处口径）。
static func join_scene_of(db, char_id: String) -> String:
	var row: Resource = db.get_row("recruit_def", char_id) if db != null else null
	return str(row.join_scene) if row != null else ""


## 「人」的统一取法：`npc_def` 里的人，**或者同伴**（`character_base`）。
##
## 同伴没有 `npc_def` 行（设计 20 §十 只写了「同伴与城镇 NPC 共用一套好感规则」，
## 没有再给四行），所以这里把同伴**按 `npc_def` 的形状合成一行**——
## 名字／称号／等级／介绍／所在地从 `character_base` ＋ `recruit_def` ＋ 存档里取，
## 面板与各处调用方就不用为「两种人」各写一套。
##
## 合成行**不进表**（每次调用新建一个 Resource），所以谁也改不动它。
static func person_of(db, state, person_id: String) -> Resource:
	if db == null or person_id.is_empty():
		return null
	# 特殊记号 `player`：主角自己——按运行期第一位（玩家选的那个出身）解析
	if person_id == PLAYER_ID:
		if state == null or state.char_ids.is_empty():
			return null
		return person_of(db, state, str(state.char_ids[0]))
	var def: Resource = def_of(db, person_id)
	if def != null:
		return def
	var char_row: Resource = db.get_row("character_base", person_id)
	if char_row == null:
		return null
	var synth = load("res://src/data/tables/npc_def_row.gd").new()
	synth.npc_id = person_id
	synth.name_cn = str(char_row.name_cn)
	synth.title_cn = str(char_row.role_tag)
	synth.level = int(state.level_of(person_id)) if state != null else int(char_row.start_level)
	synth.place_id = join_scene_of(db, person_id)
	synth.faction = "companion"
	synth.spar_team_id = ""
	synth.greet_text_cn = ""
	synth.info_text_cn = str(char_row.desc)
	return synth


static func favor_row_of(db, npc_id: String) -> Resource:
	return db.get_row("npc_favor", npc_id) if db != null else null


## 某个地点上的人：`place_id` 命中这张图（或它的父区域节点）。
## 与 `RecruitService`／`StoryService` 同一套地点口径。
static func npcs_at(db, place_id: String) -> Array:
	var out: Array = []
	if db == null or place_id.is_empty():
		return out
	var local: Resource = db.get_row("map_local", place_id)
	for row: Resource in db.rows("npc_def"):
		var want := str(row.place_id)
		if want == place_id:
			out.append(row)
		elif local != null and want == str(local.parent_node):
			out.append(row)
	return out


## 位点名 → 人（07 §九 第 12 条／§十二）。**两种命名都认**：
##   · **按 id 绑**：位点名写成 `npc_<npc_id>`——**只认这个地点上的人**，认不出来就返回空串
##     （**绝不退回「按行序」**：那会把旁边的人认成他，比认不出来更糟）；
##   · **占位命名**：`npc_slot_0N` → 这个地点的第 N 个人（`npc_def` 行序，老口径）。
##
## 07 那句「等 NPC／对话表出来后再按 id 绑定」的前置条件已经满足（`dialogue_node`／`dialogue_option`
## 0.31.0 落表）——地编把位点改名成 `npc_<npc_id>` 的当天，这里不用再改代码（见 `框架说明.md` 决策 332）。
static func npc_for_slot(db, slot_name: String, place_id: String) -> String:
	if not slot_name.begins_with(SLOT_PREFIX):
		return ""
	var rows: Array = npcs_at(db, place_id)
	if not slot_name.begins_with(SLOT_ID_PREFIX):
		for row: Resource in rows:
			if str(row.npc_id) == slot_name:
				return slot_name
		return ""
	var digits := ""
	for ch: String in slot_name:
		if ch.is_valid_int():
			digits += ch
	if digits.is_empty():
		return ""
	var index := maxi(0, int(digits) - 1)
	if index >= rows.size():
		return ""
	return str(rows[index].npc_id)


# ------------------------------------------------------------------ 好感度

## 当前好感（没记录就取表里的 `initial_favor`），并夹到 `[0, favor_max]`。
static func favor_of(db, state, npc_id: String) -> int:
	var row := favor_row_of(db, npc_id)
	var cap := int(row.favor_max) if row != null else 100
	var value := int(state.npc_favor.get(npc_id, int(row.initial_favor) if row != null else 0))
	return clampi(value, FAVOR_MIN, maxi(1, cap))


## 改好感。返回 {ok, before, after, delta, capped, error}
static func add_favor(db, state, npc_id: String, delta: int) -> Dictionary:
	if state == null:
		return {"ok": false, "before": 0, "after": 0, "delta": 0, "capped": false, "error": "没有会话状态"}
	var row := favor_row_of(db, npc_id)
	if row == null:
		# 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
		push_error("[NpcService] npc_favor 里没有 %s" % npc_id)
		return {"ok": false, "before": 0, "after": 0, "delta": 0, "capped": false,
			"error": "这个人的交情没记（数据错，已记进日志）"}
	var before := favor_of(db, state, npc_id)
	var cap := maxi(1, int(row.favor_max))
	var after := clampi(before + delta, FAVOR_MIN, cap)
	state.npc_favor[npc_id] = after
	return {
		"ok": true, "before": before, "after": after, "delta": after - before,
		"capped": after != before + delta, "error": "",
	}


# ------------------------------------------------------------------ 赠礼

## 送礼：从背包扣一件，按喜好给好感。返回 {ok, delta, favor, text, error}
static func gift(db, state, npc_id: String, item_id: String) -> Dictionary:
	var row := favor_row_of(db, npc_id)
	var def := def_of(db, npc_id)
	var name_cn := str(def.name_cn) if def != null else npc_id
	if row == null:
		return {"ok": false, "delta": 0, "favor": 0, "text": "", "error": "这个人还不能交往"}
	if not state.inventory.has(item_id, 1):
		return {"ok": false, "delta": 0, "favor": favor_of(db, state, npc_id),
			"text": "", "error": "背包里没有这件东西"}
	var delta := int(row.gift_favor)
	var reaction := "收下了"
	if _in_list(str(row.like_item_ids), item_id):
		delta += LIKE_ITEM_BONUS
		reaction = "眼前一亮——正是他喜欢的东西"
	elif _in_list(str(row.dislike_item_ids), item_id):
		delta = DISLIKE_PENALTY
		reaction = "皱了皱眉，还是收下了"
	else:
		var item: Resource = db.get_row("item_base", item_id)
		if item != null and _in_list(str(row.like_categories), str(item.item_type)):
			delta += LIKE_CATEGORY_BONUS
			reaction = "道了声谢"
	var taken: Dictionary = state.inventory.remove_item(db, item_id, 1)
	if not bool(taken.get("ok", false)):
		return {"ok": false, "delta": 0, "favor": favor_of(db, state, npc_id),
			"text": "", "error": str(taken.get("error", "拿不出来"))}
	var changed: Dictionary = add_favor(db, state, npc_id, delta)
	return {
		"ok": true, "delta": int(changed["delta"]), "favor": int(changed["after"]),
		"text": "%s %s（好感 %+d → %d）" % [name_cn, reaction, int(changed["delta"]), int(changed["after"])],
		"error": "",
	}


# ------------------------------------------------------------------ 切磋

## 能不能切磋：表里给了 `spar_team_id` 才能（19 §2.2 的「切磋赢了加好感」）。
static func spar_team(db, npc_id: String) -> String:
	var def := def_of(db, npc_id)
	return str(def.spar_team_id) if def != null else ""


## 切磋赢了：加好感。返回与 `add_favor` 同形，另附一句文案。
static func win_spar(db, state, npc_id: String) -> Dictionary:
	var row := favor_row_of(db, npc_id)
	var def := def_of(db, npc_id)
	var name_cn := str(def.name_cn) if def != null else npc_id
	if row == null or int(row.spar_favor) <= 0:
		return {"ok": false, "delta": 0, "after": favor_of(db, state, npc_id),
			"text": "", "error": "这个人不切磋"}
	var changed: Dictionary = add_favor(db, state, npc_id, int(row.spar_favor))
	changed["text"] = "%s 抱拳：「承让。」（好感 %+d → %d）" % [name_cn, int(changed["delta"]), int(changed["after"])]
	return changed


# ------------------------------------------------------------------ 偷窃

## 偷窃判定值：取全队「敏／运」判定值里高的那个（19 §2.3：偷窃走事件判定，敏或运）。
## 复用**事件判定那一套**（`EventCheckService.best_check_value`），不另算一套。
static func steal_value(db, state) -> int:
	var service = EventCheckServiceScript.new(db, state)
	return maxi(
		int(service.best_check_value("attr:agi")["value"]),
		int(service.best_check_value("attr:luk")["value"]),
	)


## 偷窃成功率：与事件判定的 soft 口径同一条公式（`0.5 + 差值 × 0.1`，夹 5%~95%）。
static func steal_chance(db, state, difficulty: int) -> float:
	var service = EventCheckServiceScript.new(db, state)
	return service.soft_chance(steal_value(db, state), difficulty)


## 偷一件东西。`roll` 传 0~1 之间的随机数（用例固定种子）。
## 返回 {ok, success, text, favor, error}——**失败扣好感**（19 §2.3），但不进战斗。
static func steal(db, state, npc_id: String, item_id: String, roll: float) -> Dictionary:
	var row := favor_row_of(db, npc_id)
	var def := def_of(db, npc_id)
	var name_cn := str(def.name_cn) if def != null else npc_id
	if row == null:
		return {"ok": false, "success": false, "text": "", "favor": 0, "error": "这个人还不能交往"}
	var difficulty := int(row.steal_difficulty)
	if difficulty <= 0:
		# `steal_difficulty=0` = 不可偷：纯善意的 NPC 留的口子（例：救出来的阿福）
		return {"ok": false, "success": false, "text": "", "favor": favor_of(db, state, npc_id),
			"error": "%s 身上没有可偷的东西" % name_cn}
	var chance := steal_chance(db, state, difficulty)
	if roll <= chance:
		var granted: Dictionary = BattleRewardScript.grant_item(db, state, item_id, 1)
		return {
			"ok": true, "success": true, "favor": favor_of(db, state, npc_id),
			"text": "得手了：%s（成功率 %d%%）" % [_item_name(db, item_id), int(round(chance * 100.0))],
			"error": "" if bool(granted.get("ok", false)) else str(granted.get("error", "")),
		}
	var changed: Dictionary = add_favor(db, state, npc_id, STEAL_FAIL_PENALTY)
	return {
		"ok": true, "success": false, "favor": int(changed["after"]),
		"text": "被发现了（成功率 %d%%）：好感 %+d → %d" % [
			int(round(chance * 100.0)), int(changed["delta"]), int(changed["after"]),
		],
		"error": "",
	}

# ------------------------------------------------------------------ 兑换

static func offers_of(db, npc_id: String, kind: String = "offer") -> Array:
	var out: Array = []
	if db == null:
		return out
	for row: Resource in db.rows_where("npc_offer", "npc_id", npc_id):
		if str(row.kind) == kind:
			out.append(row)
	return out


## 能不能换：好感够 + 钱够（`price` 为空＝只靠好感）。
static func can_redeem(db, state, offer) -> Dictionary:
	var need := int(offer.favor_required)
	var have := favor_of(db, state, str(offer.npc_id))
	if have < need:
		return {"ok": false, "error": "好感不够（%d／%d）" % [have, need]}
	var price := int(offer.price)
	if price > 0 and int(state.inventory.money) < price:
		return {"ok": false, "error": "钱不够（%d／%d）" % [int(state.inventory.money), price]}
	return {"ok": true, "error": ""}


## 兑换一件：扣钱（如果有价）＋ 进背包。返回 {ok, item_id, text, error}
static func redeem(db, state, offer_id: String) -> Dictionary:
	var offer: Resource = db.get_row("npc_offer", offer_id) if db != null else null
	if offer == null:
		return {"ok": false, "item_id": "", "text": "", "error": "没有这个条目"}
	if str(offer.kind) != "offer":
		return {"ok": false, "item_id": "", "text": "", "error": "这件东西不是用来换的"}
	var check := can_redeem(db, state, offer)
	if not bool(check["ok"]):
		return {"ok": false, "item_id": "", "text": "", "error": str(check["error"])}
	var price := int(offer.price)
	if price > 0:
		state.inventory.money -= price
	var granted: Dictionary = BattleRewardScript.grant_item(db, state, str(offer.item_id), 1)
	if not bool(granted.get("ok", false)):
		if price > 0:
			state.inventory.money += price   # 发不出去就把钱退回去
		return {"ok": false, "item_id": str(offer.item_id), "text": "", "error": str(granted.get("error", "拿不到"))}
	var def := def_of(db, str(offer.npc_id))
	var name_cn := str(def.name_cn) if def != null else str(offer.npc_id)
	return {
		"ok": true, "item_id": str(offer.item_id),
		"text": "%s 把 %s 交给了你%s" % [
			name_cn, _item_name(db, str(offer.item_id)),
			"" if price <= 0 else "（%d 文）" % price,
		],
		"error": "",
	}


# ------------------------------------------------------------------ 专属任务

static func quests_of(db, npc_id: String) -> Array:
	var rows: Array = db.rows_where("npc_quest", "npc_id", npc_id) if db != null else []
	rows.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.sort_order) < int(b.sort_order))
	return rows


## 任务条件是否达成——**复用既有条件语言**（19 §2.5）。
## `context` 可带 `kill_styles`（击杀方式记在会话的 `last_battle` 里，状态里没有）。
static func requirement_met(db, state, requirement: String, context: Dictionary = {}) -> bool:
	var text := requirement.strip_edges()
	if text.is_empty():
		return true
	var parts := text.split(":", true)
	match parts[0]:
		"item":
			if parts.size() < 3:
				return false
			return state.inventory.count(parts[1]) >= int(parts[2])
		"event":
			if parts.size() < 2:
				return false
			# 事件判定的结果记在存档里（`record_event_check` 的取值：done／failed）
			return state.event_check_result(parts[1]) == "done"
		"kill_style":
			if parts.size() < 2:
				return false
			var styles: Dictionary = context.get("kill_styles", {})
			return int(styles.get(parts[1], 0)) > 0
		_:
			# `flag_xxx` 这类旗标条件
			return GuideServiceScript.condition_met(state, text)


static func quest_done(state, quest_id: String) -> bool:
	return state.has_flag(quest_done_flag(quest_id)) if state != null else false


static func quest_done_flag(quest_id: String) -> String:
	return "npc_quest_done_%s" % quest_id


## 这个 NPC 当前能接的那一条（按 sort_order 第一条「没做过且条件达成」）。
static func available_quest(db, state, npc_id: String, context: Dictionary = {}) -> Resource:
	for row: Resource in quests_of(db, npc_id):
		if quest_done(state, str(row.quest_id)):
			continue
		if requirement_met(db, state, str(row.requirement), context):
			return row
	return null


## 交任务：发奖励 + 加好感 + 记账（只能交一次）。返回 {ok, text, rewards, favor, error}
static func complete_quest(db, state, quest_id: String, context: Dictionary = {}) -> Dictionary:
	var row: Resource = db.get_row("npc_quest", quest_id) if db != null else null
	if row == null:
		return {"ok": false, "text": "", "rewards": [], "favor": 0, "error": "没有这条任务"}
	if quest_done(state, quest_id):
		return {"ok": false, "text": "", "rewards": [], "favor": 0, "error": "这条已经交过了"}
	if not requirement_met(db, state, str(row.requirement), context):
		return {"ok": false, "text": "", "rewards": [], "favor": 0, "error": "条件还没达成"}
	var rewards: Array = []
	for item_id: String in str(row.reward_item_ids).split(";", false):
		var granted: Dictionary = BattleRewardScript.grant_item(db, state, item_id.strip_edges(), 1)
		rewards.append({"item_id": item_id.strip_edges(), "ok": bool(granted.get("ok", false))})
	var favor_result: Dictionary = add_favor(db, state, str(row.npc_id), int(row.reward_favor))
	# 「这条支线还会给**别人**加好感」（设计 20 号 §九 #8：孙掌柜那条给的是**燕小七** ＋10）。
	# 表里没有「给谁」这一列（Q72 给了三个选项），所以先写在这里；设计给列就挪进表。
	var extra: Array = []
	if EXTRA_FAVOR.has(quest_id):
		for entry: Dictionary in EXTRA_FAVOR[quest_id]:
			var who := str(entry.get("npc_id", ""))
			var added: Dictionary = add_favor(db, state, who, int(entry.get("value", 0)))
			if bool(added.get("ok", false)):
				extra.append("%s 好感 +%d" % [_display_name(db, who), int(entry.get("value", 0))])
	state.set_flag(quest_done_flag(quest_id))
	return {
		"ok": true, "text": str(row.text_cn), "rewards": rewards,
		"favor": int(favor_result.get("after", 0)), "extra_favor": extra, "error": "",
	}


## 「这条支线还给谁加好感」：设计 20 号 §九 #8（孙掌柜《带一句话》）——信是给燕小七的，
## 所以加的是**她**的好感 ＋10。**暂时写在这里**：`npc_quest.reward_favor` 只能加委托人自己
## （Q72 ③ 给了三个选项：加一列 `favor_target_id`／改口径／挪到对话里）。
const EXTRA_FAVOR := {
	"nq_sun_01": [{"npc_id": "ch_ci", "value": 10}],
}


static func _display_name(db, npc_id: String) -> String:
	var def: Resource = db.get_row("npc_def", npc_id) if db != null else null
	if def != null:
		return str(def.name_cn)
	var char_row: Resource = db.get_row("character_base", npc_id) if db != null else null
	return str(char_row.name_cn) if char_row != null else npc_id


# ------------------------------------------------------------------ 小工具

static func _in_list(joined: String, value: String) -> bool:
	if joined.strip_edges().is_empty() or value.is_empty():
		return false
	for part: String in joined.split(";", false):
		if part.strip_edges() == value:
			return true
	return false


static func _item_name(db, item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		return str(equip.name_cn)
	var item: Resource = db.get_row("item_base", item_id)
	return str(item.name_cn) if item != null else item_id
