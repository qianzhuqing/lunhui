## 战斗结算入账：把经验、铜钱与掉落写进存档。
##
## 战斗界面（`battle_screen.gd`）打完就调 `apply()`；扫荡、宝箱、隐藏内容也走这里；
## 掉落条目可以来自 DropResolver，也可以来自宝箱表。
class_name BattleReward
extends RefCounted

const InventoryScript := preload("res://src/core/inventory.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")
const LevelServiceScript := preload("res://src/core/level_service.gd")


## result 传 BattleSimulator.simulate() 的返回值。
## roller 传 AffixRoller 时，掉落的装备会滚随机词条（战斗界面与小地图都会传；不传就现开一个随机源）。
static func apply(db, state, result: Dictionary, drops: Array = [], roller = null) -> Dictionary:
	var rewards: Dictionary = result.get("rewards", {})
	return grant(db, state, int(rewards.get("exp", 0)), int(rewards.get("money", 0)), drops, roller)


## 直接入账。返回 {ok, exp, money, items, equipment, errors}
static func grant(db, state, exp: int, money: int, drops: Array, roller = null) -> Dictionary:
	var out := {
		"ok": true,
		"exp": maxi(exp, 0),
		"money": maxi(money, 0),
		"items": [],
		"equipment": [],
		"errors": [],
		# 背包放不下的掉落：算「结算成功但没全捡起来」，不把整场判成失败
		"overflow": [],
		"level_ups": [],
		"level_up_text": "",
	}
	if state == null:
		out["ok"] = false
		out["errors"].append("没有会话状态")
		return out
	if state.inventory == null:
		state.inventory = InventoryScript.new()
	var affix_roller = roller if roller != null else AffixRollerScript.new(db, RngServiceScript.new())

	# 经验进队伍池，然后立刻把付得起的等级换出来（level_growth.exp_to_next）
	state.party_exp += int(out["exp"])
	var levels := LevelServiceScript.new(db, state)
	var level_result: Dictionary = levels.apply_available_levels()
	out["level_ups"] = level_result["changes"]
	out["level_up_text"] = levels.describe(level_result["changes"])
	state.inventory.money += int(out["money"])

	for entry: Dictionary in drops:
		var item_id := str(entry.get("item_id", ""))
		var qty := maxi(1, int(entry.get("qty", 1)))
		if item_id.is_empty():
			continue
		var one: Dictionary = grant_item(db, state, item_id, qty, affix_roller)
		match str(one["kind"]):
			"money":
				continue    # 钱在 grant_item 里已经加到 inventory.money 了
			"equip":
				if bool(one["ok"]):
					out["equipment"].append_array(one["instance_ids"])
				else:
					out["errors"].append("掉落的 %s 没能生成装备实例" % item_id)
				continue
			_:
				if not bool(one["ok"]):
					out["overflow"].append({"item_id": item_id, "qty": qty, "reason": str(one["error"])})
					continue
				out["items"].append({
					"item_id": item_id, "qty": int(one["qty"]),
					"overflow": int(one.get("overflow", 0)),
				})
				if int(one.get("overflow", 0)) > 0:
					out["overflow"].append({"item_id": item_id, "qty": int(one["overflow"]), "reason": "背包已满"})
	out["ok"] = out["errors"].is_empty()
	return out


## 把**一件奖励物品**入账：货币直接进钱、装备建实例、其余堆叠。
## 返回 {ok, kind, item_id, qty, money, instance_ids, overflow, error}
##
## 这是「掉落条目怎么变成玩家资产」的**唯一口径**——别在别处再写一遍 `if item_id == "item_money"`。
## 起因：`ev_gamble`（赌局赢钱）走的是 `EventCheckService` 自己的 `add_item`，
## 于是「赢钱」把 `item_money` 当成一件可堆叠物品塞进了背包，`inventory.money` 一文没动——
## 玩家在客栈赌赢一把，得到的是背包里一行「铜钱」，买不了任何东西。
static func grant_item(db, state, item_id: String, qty: int = 1, affix_roller = null) -> Dictionary:
	var out := {
		"ok": true, "kind": "", "item_id": item_id, "qty": 0,
		"money": 0, "instance_ids": [], "overflow": 0, "error": "",
	}
	if state == null or state.inventory == null:
		out["ok"] = false
		out["error"] = "没有会话状态"
		return out
	if item_id.is_empty():
		out["ok"] = false
		out["error"] = "没有奖励物品 id"
		return out
	var amount := maxi(1, qty)
	if item_id == "item_money":
		state.inventory.money += amount
		out["kind"] = "money"
		out["money"] = amount
		out["qty"] = amount
		return out
	if db.get_row("equip_base", item_id) != null:
		var roller = affix_roller if affix_roller != null else AffixRollerScript.new(db, RngServiceScript.new())
		for index in range(amount):
			var instance_id: String = state.inventory.add_equipment(db, item_id, roller.roll_for(item_id))
			if instance_id.is_empty():
				out["ok"] = false
				out["error"] = "装备 %s 没能生成实例" % item_id
				return out
			out["instance_ids"].append(instance_id)
		out["kind"] = "equip"
		out["qty"] = amount
		return out
	var added: Dictionary = state.inventory.add_item(db, item_id, amount)
	out["kind"] = "item"
	out["qty"] = int(added["added"])
	out["overflow"] = int(added["overflow"])
	out["ok"] = bool(added["ok"])
	if not bool(added["ok"]):
		out["error"] = str(added["error"])
	return out
