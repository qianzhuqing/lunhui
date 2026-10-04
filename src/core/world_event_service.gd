## 大地图随机事件（设计 19 §四，0.25.0／0.25.1）。
##
## 明雷默认关之后大地图太空，所以按**区域权重**抽非战斗事件：
## 货商／门派弟子／江湖人／官府巡查／天象奇遇／野物。三条纪律写在 19 号里：
##   ① **以非战斗为主**（战斗只保留「切磋」，而且玩家主动选）；
##   ② **权重按区域给**（每个区域的池子不同）；
##   ③ **事件要给东西或信息**——只是看一眼就走的随机事件等于空气。
##
## 0.25.1 又加了两处提示：HUD 一行**模糊**的（`prompt_text_cn`，例「远处有车马声」）
## 与地图上的**临时淡色光点**。三条规则：提示只在事件存活期间出现、同一时刻最多一条、
## 走开或超时标记消失——**随机事件是「路过遇上」，不是「待领的任务」**。
##
## 效果这一半：0.28.0（Q64）把八行的 `effect_id` 填齐之后接通，五类各接一处——
##   `gift`／`hint`／`check` 在**这里**就能结算（纯逻辑：发物品、记线索、走判定）；
##   `trade`／`spar` 要挂在**当前场景**上（开货架界面、切战斗场景），服务层只交一个
##   `scene_action` 请求出去，由场景层动手——服务层碰不到节点，这是分层的既有口径。
## `effect_id` 空着时仍然**如实说「没配」**，不假装发了什么（19 §四第 3 条）。
class_name WorldEventService
extends RefCounted

const GuideServiceScript := preload("res://src/core/guide_service.gd")
const BattleRewardScript := preload("res://src/core/battle_reward.gd")
## 判定类效果直接在这里结算：`event_check` 是纯逻辑，**不需要地图位点**——
## Q64 的 `ev_patrol_check` 就是「官道固定关卡（地图位点）」与「随机事件 we_patrol（直接结算）」
## 两个入口共用一行判定里的第二条路。
const EventCheckServiceScript := preload("res://src/core/event_check_service.gd")

## 事件存活时间（秒）：超时就消失（19 §四：随机事件是「路过遇上」）
const LIFETIME := 45.0
## 玩家走开多远就消失（像素；一格 32）
const DESPAWN_DISTANCE := 320.0
## 触发半径（走到光点上）
const TRIGGER_DISTANCE := 24.0
## 生成距离：在玩家附近这个半径内挑一点（不要贴脸出现）
const SPAWN_MIN := 96.0
const SPAWN_MAX := 224.0
## 两次事件之间的最短间隔（秒）——刚遇过一场别马上再来一场
const COOLDOWN := 20.0


# ------------------------------------------------------------------ 池子

## 这个区域能出哪些事件（`region_tags` 用 ";" 分隔）。
static func rows_for_region(db, region_id: String) -> Array:
	var out: Array = []
	if db == null or region_id.is_empty():
		return out
	for row: Resource in db.rows("world_event"):
		for tag: String in str(row.region_tags).split(";", false):
			if tag.strip_edges() == region_id:
				out.append(row)
				break
	return out


## 一次性事件的记账旗标（写在旗标里，不用新开存档字段）。
static func done_flag(event_id: String) -> String:
	return "world_event_done_%s" % event_id


## 「给线索」类效果记的旗标（`effect_id` 指向的是 `hidden_trigger.trigger_id`）。
## 线索本（`ClueService`）认得它，会在那一条后面标「已从传闻中听说」——
## 线索本身没藏着（03 要求线索必须找得到），所以这里不是「解锁」，是「你确实听人说过」的凭据。
static func hint_flag(trigger_id: String) -> String:
	return "clue_hint_%s" % trigger_id


static func is_done(state, event_id: String) -> bool:
	return state.has_flag(done_flag(event_id)) if state != null else false


## 现在能出的事件：条件旗标满足 ＋（可重复 或 还没出过）。
static func eligible(db, state, region_id: String) -> Array:
	var out: Array = []
	for row: Resource in rows_for_region(db, region_id):
		if int(row.repeatable) == 0 and is_done(state, str(row.event_id)):
			continue
		if not GuideServiceScript.condition_met(state, str(row.condition)):
			continue
		out.append(row)
	return out


## 按权重抽一个；池子空返回 null。`roll` 是 0~1 的随机数（用例传固定值好复现）。
static func pick(db, state, region_id: String, roll: float) -> Resource:
	var pool: Array = eligible(db, state, region_id)
	if pool.is_empty():
		return null
	var total := 0
	for row: Resource in pool:
		total += maxi(0, int(row.weight))
	if total <= 0:
		return pool[0]
	var target := clampf(roll, 0.0, 0.999999) * float(total)
	var acc := 0.0
	for row: Resource in pool:
		acc += float(maxi(0, int(row.weight)))
		if target < acc:
			return row
	return pool[pool.size() - 1]


# ------------------------------------------------------------------ 触发

## 触发一个事件：按 `effect_kind` 分发效果，一次性事件记账。
##
## 返回 {ok, event_id, text, prompt, effect, error}——`effect` 里带
## `{kind, id, applied, note, scene_action, text}`：
## - **`effect_id` 空着时 `applied=false` 且 note 写明原因**；
## - `scene_action` 非空 = 这一半要场景层动手（`open_shop`／`start_battle`）；
## - `text` 是**玩家可见**的结算文案（`check` 类才有）。
##
## `rng` 传 null 就用随机种子（正常游戏）；用例传固定种子的 `RngService` 拿确定结果。
static func trigger(db, state, event_id: String, rng = null) -> Dictionary:
	var row: Resource = db.get_row("world_event", event_id) if db != null else null
	if row == null:
		return {"ok": false, "event_id": event_id, "text": "", "prompt": "", "effect": {}, "error": "没有这个事件"}
	var effect := _apply_effect(db, state, row, rng)
	if int(row.repeatable) == 0:
		state.set_flag(done_flag(event_id))
	return {
		"ok": true, "event_id": event_id,
		"text": str(row.text_cn), "prompt": str(row.prompt_text_cn),
		"effect": effect, "error": "",
	}


## 效果分发。**`effect_id` 没配时如实说"没配"，不假装发了什么**（19 §四第 3 条）。
static func _apply_effect(db, state, row: Resource, rng = null) -> Dictionary:
	var kind := str(row.effect_kind)
	var target := str(row.effect_id)
	if target.is_empty():
		return {
			"kind": kind, "id": "", "applied": false, "scene_action": "", "text": "",
			# 玩家可见（大地图状态栏）：**不许出现表名／列名，也不许出现 Q 编号**（AGENTS 硬规矩）
			"note": "这条事件的效果还没配（等设计给指向）",
		}
	match kind:
		"gift":
			var granted: Dictionary = BattleRewardScript.grant_item(db, state, target, 1)
			return {
				"kind": kind, "id": target, "applied": bool(granted.get("ok", false)),
				"scene_action": "", "text": "", "note": "",
				"granted": granted,
			}
		"hint":
			# 「给信息」也算给东西（19 §四第 3 条）：把这条线索记进线索本
			if state != null and not target.is_empty():
				state.set_flag(hint_flag(target))
			return {
				"kind": kind, "id": target, "applied": true, "scene_action": "", "text": "",
				"note": "线索已记进线索本",
			}
		"check":
			# 不要求玩家走到地图位点：官道关卡与随机事件共用同一行判定（Q64）
			var result: Dictionary = EventCheckServiceScript.new(db, state, rng).resolve(target)
			var reward: Dictionary = result.get("reward", {})
			var boss_id := ""
			var scene_action := ""
			if bool(reward.get("start_battle", false)):
				boss_id = str(reward.get("id", ""))
				scene_action = "start_battle"
			return {
				"kind": kind, "id": target, "applied": bool(result.get("ok", false)),
				"scene_action": scene_action, "text": str(result.get("text", "")), "note": "",
				"success": bool(result.get("success", false)), "battle_enemy_id": boss_id,
			}
		"trade":
			# 开货架要挂在当前场景上（界面是节点），服务层只交请求
			return {
				"kind": kind, "id": target, "applied": false, "scene_action": "open_shop",
				"text": "", "note": "",
			}
		"spar":
			return {
				"kind": kind, "id": target, "applied": false, "scene_action": "start_battle",
				"text": "", "note": "",
			}
		_:
			return {
				"kind": kind, "id": target, "applied": false, "scene_action": "", "text": "",
				"note": "没见过的效果类型：%s" % kind,
			}
