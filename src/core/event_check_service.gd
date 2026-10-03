## 事件判定（01_角色系统.md 的「统一的判定接口」＋ `event_check.csv`）。
##
## 判定值来自 `CharacterSheet.event_check_value()`：`attr:x` 取裸属性、`skill:y` = 技能等级 + floor(裸属性/5)。
## 队伍里**谁行谁上**（取最高的那个判定值），当前只有一名角色时等价于他本人。
##
## 两种强度（照 01 原文实现）：
## - `hard`：判定值 ≥ 难度必定成功，**不足则无法进行**——确定性，不掷骰、也不扣任何东西；
## - `soft`：达标必定成功；不足可掷骰，成功率 `0.5 + (判定值 − 难度) × 0.1`，夹在 5%~95%，
##   而且**失败不惩罚**，只是拿不到好处（01：「否则玩家会读档重来，判定就失去意义了」）。
##
## 这里记一笔曾经的错：12 条判定以前全按「达标才过」跑，等于把 5 条 `soft` 也当成了硬门槛，
## 比设计严得多（低技能玩家在 `ev_gamble`／`ev_night_watch` 这类点位上永远没机会）。
## 公式与数值设计都写在 01 文档里，所以直接照抄成 `SOFT_*` 常量；等设计把常数挪进表再改读表。
##
## `fail_note` 只是失败时的提示文案：设计**没有**给「失败扣血」这类代价数值，所以只如实转述，
## 不假装扣过血、也不自己编一个数（以前的注释把它说成「hard 失败要付代价」，那是读错了原文）。
##
## 奖励（设计 06 允许的 7 种，**全部有落点**，2026-10-03 补齐最后两种）：
##   `item`（进背包）、`skillbook`（同一本书，进背包后走研读流程——与 `hidden_trigger` 同口径）、
##   `equip`（建装备实例进背包）、`room`（去某个房间／揭开某个地标）、`event`（记剧情旗标）、
##   `none`（判定本身就是收益）、`boss`（Boss 现身开战）。
## `boss` 只把「开战请求」（`start_battle` + 敌人 id）交出去：本服务是纯逻辑、不碰场景，
## 由场景层 `resolve_event()` 用各自既有的开战通道发起（小地图与大地图各接一处）。
## 认不出的 `reward_type` 仍然**显式报错**，不静默当成「没有奖励」。
class_name EventCheckService
extends RefCounted

const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
## 奖励物品入账的口径与战斗结算**共用一处**（货币进钱、装备建实例、其余堆叠）
const BattleRewardScript := preload("res://src/core/battle_reward.gd")

## 软判定掷骰（公式出自 01_角色系统.md「两种判定强度」）
const SOFT_BASE := 0.5
const SOFT_PER_POINT := 0.1
const SOFT_MIN := 0.05
const SOFT_MAX := 0.95

## `fail_note` 里写的是「会付出代价」、但设计还没给数值的判定。
##
## 现在这一类只有落石陷阱一条（`ev_shed_trap`：原文「触发落石陷阱损失气血」）。
## 设计没给「扣多少血」，所以**不自己编**；但也不能让玩家以为真掉了血——
## 按 04 文档「暂缓规则」的同一口径在界面上如实说明。设计补了代价列（如 `hp_cost`）之后，
## 把这条清单和说明一起删掉。
const PENDING_COST_CHECKS := ["ev_shed_trap"]

var db
var state
var _rng


## rng 传 null 就用随机种子（正常游戏）；测试传固定种子的 RngService 拿确定结果。
func _init(table_db, game_state, rng = null) -> void:
	db = table_db
	state = game_state
	_rng = rng if rng != null else RngServiceScript.new()


func check_row(check_id: String) -> Resource:
	return db.get_row("event_check", check_id)


## 这张小地图上的事件位点（给界面／自检用）
func checks_for_scene(scene_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("event_check"):
		if str(row.scene_id) == scene_id:
			out.append(row)
	return out


## 这张小地图在配置表里挂在哪个大地图节点（`map_local.parent_node`）
func parent_node_of(scene_id: String) -> String:
	var local: Resource = db.get_row("map_local", scene_id)
	return str(local.parent_node) if local != null else ""


## 这条判定**允许**出现在这张小地图上吗。
##
## 两种情况：
##   ① 表里直接写了 `scene_id`（黑风寨那 4 条）；
##   ② 表里只写了 `region_id`，而它正好是这张图的父节点——
##      07 文档把赌局摆在清风驿·客栈，而表里 `ev_gamble` 的 `scene_id` 是空的，
##      以前只认 ①，于是镇上那个位点**永远没人接**（地图验收还专门给它开了个
##      `check_id != "ev_gamble"` 的例外，等于两边都知道它特殊，却没人把它接上）。
##
## **注意这只是「允许」，不是「存在」**：`ev_climb_wall` 的 region 也是 n_heifengzhai（允许出现在黑风寨图里），
## 但它的位点摆在寨墙外的大地图上，黑风寨图里没有 `Event_ev_climb_wall`。
## 真正接不接得上由控制器决定——**图上必须有 `Event_<check_id>` 位点**，两个条件同时成立才接。
func check_allowed_in_scene(check_id: String, scene_id: String) -> bool:
	var row: Resource = check_row(check_id)
	if row == null:
		return false
	if str(row.scene_id) == scene_id:
		return true
	if not str(row.scene_id).is_empty():
		return false
	var parent := parent_node_of(scene_id)
	return not parent.is_empty() and str(row.region_id) == parent


## 大地图上的事件位点（按 region_id 归属）
func checks_for_region(region_id: String) -> Array:
	var out: Array = []
	for row: Resource in db.rows("event_check"):
		if str(row.region_id) == region_id:
			out.append(row)
	return out


## 全队最高的判定值：{value, char_id, name, source, naked}
func best_check_value(source: String) -> Dictionary:
	var best := {"value": -999, "char_id": "", "name": "", "source": source, "naked": false}
	if state == null:
		return best
	# CharacterSheet.event_check_value 对未知来源会返回 0，这里先自己校验来源有效
	# （表里写错一个 check_source 时，应该报「来源无效」而不是静悄悄按 0 判失败）
	var parts := source.split(":", false)
	if parts.size() != 2 or not _source_valid(parts[0], parts[1]):
		return best
	for char_id: String in state.char_ids:
		var sheet = CharacterSheetScript.new(db, state, char_id)
		var result: Dictionary = sheet.event_check_value(source)
		if int(result["value"]) > int(best["value"]):
			best = {
				"value": int(result["value"]),
				"char_id": char_id,
				"name": state.char_name(db, char_id),
				"source": source,
				"naked": bool(result.get("naked", false)),
			}
	return best


func _source_valid(kind: String, target_id: String) -> bool:
	if kind == "skill":
		return db.get_row("event_skill_def", target_id) != null
	if kind == "attr":
		return db.get_row("attribute_def", target_id) != null
	return false


## 软判定不足时的成功率：`0.5 + (判定值 − 难度) × 0.1`，夹在 5%~95%。
## 达标（差值 ≥ 0）不给 100% 以外的花样，由调用方直接当成功处理。
func soft_chance(value: int, difficulty: int) -> float:
	return clampf(
		SOFT_BASE + float(value - difficulty) * SOFT_PER_POINT,
		SOFT_MIN,
		SOFT_MAX,
	)


## 判定：{ok(能不能判，比如已经做过), success(确定性结果), roll_needed, chance,
##        value, difficulty, source_label, who, text}
##
## **这里不掷骰**：`judge()` 是可以重复调用的纯查询（界面预览、用例、线索本都调它），
## 掷骰只在 `resolve()` 里发生一次。软判定不足时 `success=false` 但 `roll_needed=true`，
## `chance` 是那一掷的概率；硬判定不足则 `roll_needed=false`、`chance=0`（不足就是做不到）。
func judge(check_id: String) -> Dictionary:
	var row: Resource = check_row(check_id)
	if row == null:
		# 位点/表对不上是**数据错**：id 只进日志，玩家看一句人话（AGENTS：玩家可见文案不许出现表内 id）
		push_error("[EventCheckService] 没有这条事件判定：%s" % check_id)
		return {"ok": false, "success": false, "text": "这里没有可做的判定（数据错，已记进日志）"}
	if bool(row.once_only) and state != null and state.event_check_result(check_id) == "done":
		return {
			"ok": false, "success": false,
			"text": "%s：已经做过了" % str(row.note),
			"done": true,
		}
	var source := str(row.check_source)
	var best: Dictionary = best_check_value(source)
	if int(best["value"]) < -900:
		return {"ok": false, "success": false, "text": "%s：判定来源 %s 无效" % [str(row.note), source]}
	var difficulty := int(row.difficulty)
	var value := int(best["value"])
	var check_type := str(row.check_type)
	var success: bool = value >= difficulty
	var roll_needed: bool = check_type == "soft" and not success
	var chance := 0.0
	if success:
		chance = 1.0
	elif roll_needed:
		chance = soft_chance(value, difficulty)
	var verdict := "成功" if success else "失败"
	if roll_needed:
		verdict = "软判定，掷骰 %d%%" % roundi(chance * 100.0)
	return {
		"ok": true,
		"success": success,
		"roll_needed": roll_needed,
		"chance": chance,
		"value": value,
		"difficulty": difficulty,
		"source": source,
		"source_label": source_label(source),
		"who": str(best["name"]),
		"check_type": check_type,
		"naked": bool(best["naked"]),
		"clue": str(row.clue_source),
		"note": str(row.note),
		"text": "%s：%s %d %s %d，%s" % [
			str(row.note), source_label(source), value,
			"≥" if success else "<", difficulty, verdict,
		],
	}


## 走一次事件：判定 → 成功发奖励 / 失败转述代价，结果写存档
## 返回 {ok, success, text, reward, detail}
func resolve(check_id: String) -> Dictionary:
	var judged: Dictionary = judge(check_id)
	if not bool(judged.get("ok", false)):
		return {
			"ok": false, "success": false, "reward": {}, "text": str(judged.get("text", "")),
			"already": bool(judged.get("done", false)),
		}
	var row: Resource = check_row(check_id)
	var success := bool(judged["success"])
	var reward: Dictionary = {"type": "none", "id": ""}
	var text := str(judged["text"])
	var rolled := false
	if bool(judged.get("roll_needed", false)):
		# 软判定不足：掷一次骰。失败不惩罚，只是拿不到好处（01 原文）。
		success = _rng.chance(float(judged["chance"]))
		rolled = true
		text += "　掷骰 → %s" % ("成功" if success else "失败")
	if success:
		reward = _grant(row)
		if not str(reward.get("error", "")).is_empty():
			text += "　但%s" % str(reward["error"])
		else:
			var grant_text := str(reward.get("text", ""))
			if not grant_text.is_empty():
				text += "　%s" % grant_text
		if state != null:
			state.record_event_check(check_id, "done")
	else:
		text += "　%s" % str(row.fail_note)
		if PENDING_COST_CHECKS.has(check_id):
			text += "（这条代价还没接：设计未给数值，暂不结算）"
		if state != null:
			state.record_event_check(check_id, "failed")
	return {
		"ok": true, "success": success, "reward": reward, "text": text,
		"already": false, "check_id": check_id,
		"rolled": rolled,
		"chance": float(judged.get("chance", 0.0)),
	}


## 发奖励：item / equip / room / event / none
##
## `skillbook` 与 `boss` 是设计 06 允许、但这里做不了的两种：秘籍要背包的研读流程、Boss 要开战流程，
## 都不是这个纯逻辑服务能干的。以前它们会**静默**落进「没有奖励」的兜底，等于策划配了不发东西；
## 现在显式报错并把原因带回给玩家文案（校验器那侧同时补上 id 检查，配错 id 在加载期就会红）。
func _grant(row: Resource) -> Dictionary:
	var reward_type := str(row.reward_type)
	var reward_id := str(row.reward_id)
	if state == null:
		return {"type": reward_type, "id": reward_id, "error": "没有会话状态"}
	match reward_type:
		"item", "skillbook":
			# 走与战斗结算同一套入账口径：货币进钱、装备建实例、其余堆叠。
			# （以前这里直接 add_item，「赌局赢钱」就把铜钱当成一件背包物品发了，钱一分没涨。）
			# `skillbook` 与 `item` **同一条路**：它就是一本书（`item_base.item_type=skillbook` 的道具），
			# 拿到之后走背包的研读流程学会武学——`hidden_trigger` 那边早就是这个口径（2026-10-03 对齐）。
			var granted: Dictionary = BattleRewardScript.grant_item(db, state, reward_id, 1)
			if not bool(granted["ok"]):
				return {"type": reward_type, "id": reward_id, "error": str(granted["error"])}
			if str(granted["kind"]) == "money":
				return {
					"type": "money", "id": reward_id, "qty": int(granted["qty"]),
					"text": "得到 %d 文钱" % int(granted["qty"]),
				}
			return {
				"type": reward_type, "id": reward_id, "qty": int(granted["qty"]),
				"text": "得到 %s" % item_name(reward_id),
			}
		"equip":
			var instance_id: String = state.inventory.add_equipment(db, reward_id)
			if instance_id.is_empty():
				push_error("[EventCheckService] event_check 的 reward_type=equip 指向的装备不存在：%s" % reward_id)
				return {"type": reward_type, "id": reward_id, "error": "奖励的装备不存在（数据错，已记进日志）"}
			return {
				"type": reward_type, "id": reward_id, "instance_id": instance_id,
				"text": "得到 %s" % item_name(reward_id),
			}
		"room":
			# 房间奖励有两种落点：小地图里换房间、大地图上揭开地标
			var node: Resource = db.get_row("map_region", reward_id)
			if node != null:
				state.reveal_node(reward_id)
				return {
					"type": "region", "id": reward_id, "text": "在地图上标出了 %s" % str(node.name_cn),
				}
			var room: Resource = db.get_row("dungeon_room", reward_id)
			if room != null:
				# 房间奖励顺带把这张图对应的地标也标出来（例：追足迹 → 塌陷山洞出现在地图上）
				var landmark := _node_for_scene(str(room.scene_id))
				if not landmark.is_empty():
					state.reveal_node(landmark)
				return {
					"type": "room", "id": reward_id,
					"landmark": landmark,
					"text": "找到通往「%s」的路%s" % [
						str(room.room_name),
						"" if landmark.is_empty() else "，地图上也标出了 %s" % _node_name(landmark),
					],
				}
			push_error("[EventCheckService] event_check 的 reward_type=room 指向的房间／地标不存在：%s" % reward_id)
			return {"type": reward_type, "id": reward_id, "error": "奖励的房间或地标不存在（数据错，已记进日志）"}
		"event":
			state.set_flag(reward_id)
			# 旗标名（event_xxx）是内部记账，玩家看判定那句就够
			return {"type": "event", "id": reward_id, "text": "记下了这段经过"}
		"boss":
			# 判定通过 → Boss 现身开战。语义与 `hidden_trigger.reward_type=boss` **完全一致**
			# （那边早就这么做：`local_map_controller._trigger_item` 拿 enemy_base 的一行组一支单人队）。
			# 这个服务是纯逻辑、不碰场景，所以只把「开战请求」交出去，由场景层 `resolve_event` 发起；
			# 场景层漏接的话玩家会看到这句文案、而不会有战斗——把请求字段留着，方便测试与排错。
			var enemy: Resource = db.get_row("enemy_base", reward_id)
			if enemy == null:
				push_error("[EventCheckService] event_check 的 reward_type=boss 指向的敌人不存在：%s" % reward_id)
				return {"type": "boss", "id": reward_id, "error": "奖励指向的敌人不存在（数据错，已记进日志）"}
			return {
				"type": "boss", "id": reward_id, "start_battle": true,
				"enemy_name": str(enemy.name_cn),
				"text": "%s 现身！" % str(enemy.name_cn),
			}
		"none":
			# 设计 06：`none` 表示「判定本身就是收益」（例如避开了陷阱）。
			# 这里**不留提示文案**：原来那半句「（这一步的效果由场景自己处理）」是写给开发看的，
			# 却被 `resolve()` 拼进状态栏直接给玩家看了（真出现过）。
			return {"type": "none", "id": "", "text": ""}
		_:
			# 走到这儿说明 reward_type 不在设计允许的取值里：校验器会红，运行时也不能装没事
			push_error("[EventCheckService] event_check 的 reward_type=%s（%s）不是设计允许的取值" % [reward_type, reward_id])
			return {"type": reward_type, "id": reward_id, "error": "奖励类型不是设计允许的取值（数据错，已记进日志）"}


## 哪张大地图节点通往这个小地图（`map_region.enter_scene`）
func _node_for_scene(scene_id: String) -> String:
	if scene_id.is_empty():
		return ""
	for row: Resource in db.rows("map_region"):
		if str(row.enter_scene) == scene_id:
			return str(row.node_id)
	return ""


func _node_name(node_id: String) -> String:
	var row: Resource = db.get_row("map_region", node_id)
	return str(row.name_cn) if row != null else node_id


func item_name(item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		return str(equip.name_cn)
	var item: Resource = db.get_row("item_base", item_id)
	if item != null:
		return str(item.name_cn)
	# 两张表都查不到 = 数据错：id 只进日志（上面那句 push_error），玩家看一句人话
	push_error("[EventCheckService] 奖励 id 在 item_base／equip_base 里都查不到：%s" % item_id)
	return "那件东西（数据错，已记进日志）"


## `skill:qimen` → 「奇门 3」；`attr:agi` → 「敏 9」
func source_label(source: String) -> String:
	var parts := source.split(":", false)
	if parts.size() != 2:
		return source
	var kind := parts[0]
	var target_id := parts[1]
	if kind == "skill":
		var row: Resource = db.get_row("event_skill_def", target_id)
		return str(row.name_cn) if row != null else target_id
	var attr: Resource = db.get_row("attribute_def", target_id)
	return str(attr.name_cn) if attr != null else target_id
