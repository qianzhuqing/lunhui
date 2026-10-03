## 商店与经济（05_装备与掉落.md「商店与经济」）。
##
## 规则全在表里：卖什么／什么价／限不限量看 `shop_stock.csv`，治疗单价看 `building_def.service_price`。
## 交易状态（累计买入次数、回购列表）按设计**写进存档**，所以这里只做校验与记账，
## 存档字段的读写走 GameState，不在这里碰 JSON。
##
## 三条自己定的口径（设计没写，写在这里免得以后猜）：
## - **只收店里有 `sell_price` 的东西**：掉落装备如果这家店没上架，它就不收。
##   设计没给「估价回收」的公式，不自己编一个数值。
## - **回购＝撤销卖出**：价格就是当初的卖出价，上限 20 条，满了挤掉最早的。
## - **医馆治疗已接**：战斗外气血 v11 起进存档（`GameState.char_hp`），
##   `heal_preview()` 算「全队缺失气血 × service_price」，`heal_party()` 扣钱回满。
class_name ShopService
extends RefCounted

## 回购列表上限（05 文档建议 20 条）
const BUYBACK_LIMIT := 20
const PartyBuilderScript := preload("res://src/core/party_builder.gd")

var db
var state


func _init(table_db, game_state) -> void:
	db = table_db
	state = game_state


# ------------------------------------------------------------------ 建筑与货架

func building(building_id: String) -> Resource:
	return db.get_row("building_def", building_id)


func building_name(building_id: String) -> String:
	var row: Resource = building(building_id)
	return str(row.name_cn) if row != null else building_id


## 货架（按 sort_order）
func stock_rows(building_id: String) -> Array:
	var row: Resource = building(building_id)
	if row == null or str(row.stock_group).is_empty():
		return []
	var out: Array = db.rows_where("shop_stock", "shop_id", str(row.stock_group))
	out.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.sort_order) < int(b.sort_order))
	return out


func stock_row(building_id: String, item_id: String) -> Resource:
	var row: Resource = building(building_id)
	if row == null or str(row.stock_group).is_empty():
		return null
	return db.get_row("shop_stock", "%s|%s" % [str(row.stock_group), item_id])


## 买入价 / 卖出价（0 表示不卖 / 不收）
func buy_price(building_id: String, item_id: String) -> int:
	var row: Resource = stock_row(building_id, item_id)
	return maxi(0, int(row.buy_price)) if row != null else 0


func sell_price(building_id: String, item_id: String) -> int:
	var row: Resource = stock_row(building_id, item_id)
	return maxi(0, int(row.sell_price)) if row != null else 0


## 还能买几个（stock_limit = 0 表示无限量）
func remaining(building_id: String, item_id: String) -> int:
	var row: Resource = stock_row(building_id, item_id)
	if row == null:
		return 0
	var limit := int(row.stock_limit)
	if limit <= 0:
		return -1
	return maxi(0, limit - purchased(building_id, item_id))


func purchased(building_id: String, item_id: String) -> int:
	return state.purchased_count(building_id, item_id) if state != null else 0


func money() -> int:
	return int(state.inventory.money) if state != null and state.inventory != null else 0


func item_name(item_id: String) -> String:
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null:
		return str(equip.name_cn)
	var item: Resource = db.get_row("item_base", item_id)
	return str(item.name_cn) if item != null else item_id


# ------------------------------------------------------------------ 买入

## 能不能买：{ok, error, unit_price, total, remaining}
func can_buy(building_id: String, item_id: String, qty: int = 1) -> Dictionary:
	var unit := buy_price(building_id, item_id)
	var count := maxi(1, qty)
	if state == null or state.inventory == null:
		return _deny("没有会话状态", unit, count)
	var row: Resource = stock_row(building_id, item_id)
	if row == null or unit <= 0:
		return _deny("这家店不卖这个", unit, count)
	var equip: Resource = db.get_row("equip_base", item_id)
	if equip != null and bool(equip.drop_only):
		return _deny("%s 只能掉落获得，店里不卖" % str(equip.name_cn), unit, count)
	if db.get_row("item_base", item_id) == null and equip == null:
		return _deny("表里没有这个商品：%s" % item_id, unit, count)
	var left := remaining(building_id, item_id)
	if left >= 0 and count > left:
		return _deny("库存不够（还能买 %d 个）" % left, unit, count)
	var total := unit * count
	if money() < total:
		return _deny("铜钱不够（需要 %d，现有 %d）" % [total, money()], unit, count)
	return {"ok": true, "error": "", "unit_price": unit, "total": total, "remaining": left}


func _deny(reason: String, unit: int, qty: int) -> Dictionary:
	return {"ok": false, "error": reason, "unit_price": unit, "total": unit * qty, "remaining": 0}


## 买入。返回 {ok, error, spent, got: [{item_id, qty}], equipment: [instance_id]}
func buy(building_id: String, item_id: String, qty: int = 1) -> Dictionary:
	var count := maxi(1, qty)
	var check := can_buy(building_id, item_id, count)
	if not check["ok"]:
		return {"ok": false, "error": check["error"], "spent": 0, "got": [], "equipment": []}
	var equipment: Array = []
	var got: Array = []
	if db.get_row("equip_base", item_id) != null:
		for index in range(count):
			# 店里卖的是白板装备：不带随机词条
			var instance_id: String = state.inventory.add_equipment(db, item_id)
			if instance_id.is_empty():
				return {"ok": false, "error": "生成装备失败", "spent": 0, "got": [], "equipment": []}
			equipment.append(instance_id)
		got.append({"item_id": item_id, "qty": count})
	else:
		var added: Dictionary = state.inventory.add_item(db, item_id, count)
		if not added["ok"]:
			return {"ok": false, "error": str(added["error"]), "spent": 0, "got": [], "equipment": []}
		got.append({"item_id": item_id, "qty": int(added["added"]), "overflow": int(added["overflow"])})
		if int(added["overflow"]) > 0:
			# 背包满了：只按真正放进去的数量收钱，别把玩家的钱扣在空气上
			count = int(added["added"])
	var spent := int(check["unit_price"]) * count
	state.inventory.money -= spent
	state.record_purchase(building_id, item_id, count)
	return {"ok": true, "error": "", "spent": spent, "got": got, "equipment": equipment}


# ------------------------------------------------------------------ 卖出

## 能不能卖：{ok, error, unit_price, total}
## sell_id 传物品 id 或装备实例 id（`eq_sword_01#3`）都行。
func can_sell(building_id: String, sell_id: String, qty: int = 1) -> Dictionary:
	var count := maxi(1, qty)
	if state == null or state.inventory == null:
		return {"ok": false, "error": "没有会话状态", "unit_price": 0, "total": 0}
	var base_id := sell_id
	var is_equipment: bool = state.inventory.has_equipment(sell_id)
	if is_equipment:
		base_id = state.inventory.base_of(sell_id)
		if count > 1:
			return {"ok": false, "error": "装备一次只卖一件", "unit_price": 0, "total": 0}
		if state.inventory.is_equipped(sell_id):
			return {"ok": false, "error": "先卸下装备再卖", "unit_price": 0, "total": 0}
	else:
		var item: Resource = db.get_row("item_base", sell_id)
		if item == null:
			return {"ok": false, "error": "背包里没有这个", "unit_price": 0, "total": 0}
		if bool(item.is_key_item):
			return {"ok": false, "error": "%s 是钥匙道具，不能卖" % str(item.name_cn), "unit_price": 0, "total": 0}
		if state.inventory.count(sell_id) < count:
			return {"ok": false, "error": "数量不足（有 %d）" % state.inventory.count(sell_id), "unit_price": 0, "total": 0}
	var unit := sell_price(building_id, base_id)
	if unit <= 0:
		return {
			"ok": false,
			"error": "%s 这家店不收" % item_name(base_id),
			"unit_price": 0, "total": 0,
		}
	return {"ok": true, "error": "", "unit_price": unit, "total": unit * count, "base_id": base_id}


## 卖出。返回 {ok, error, earned, entry}
func sell(building_id: String, sell_id: String, qty: int = 1) -> Dictionary:
	var count := maxi(1, qty)
	var check := can_sell(building_id, sell_id, count)
	if not check["ok"]:
		return {"ok": false, "error": check["error"], "earned": 0, "entry": {}}
	var base_id := str(check["base_id"])
	var is_equipment: bool = state.inventory.has_equipment(sell_id)
	var entry := {"item_id": base_id, "qty": count, "price": int(check["unit_price"])}
	if is_equipment:
		var affixes: Array = state.inventory.affixes_of(sell_id)
		entry["instance_id"] = sell_id
		entry["affixes"] = affixes
		var removed: Dictionary = state.inventory.remove_equipment(sell_id)
		if not removed["ok"]:
			return {"ok": false, "error": str(removed["error"]), "earned": 0, "entry": {}}
	else:
		var removed_item: Dictionary = state.inventory.remove_item(db, sell_id, count)
		if not removed_item["ok"]:
			return {"ok": false, "error": str(removed_item["error"]), "earned": 0, "entry": {}}
	var earned := int(check["total"])
	state.inventory.money += earned
	state.push_buyback(building_id, entry, BUYBACK_LIMIT)
	return {"ok": true, "error": "", "earned": earned, "entry": entry}


# ------------------------------------------------------------------ 回购

func buyback_rows(building_id: String) -> Array:
	return state.buyback_of(building_id) if state != null else []


## 从回购列表买回来（价格就是当初的卖出价）。
##
## **背包放不下时只买放得下的那些**：与买入路径同一口径（只按真正放进去的数量收钱），
## 剩下的数量留在回购列表里。以前这里按全价扣钱、整条出列，多出来的部分就被吞了——
## 用例 `test_shop._check_buyback_partial` 钉住这条（2 格空位、回购 5 个：只该收 2 个的钱）。
func buy_back(building_id: String, index: int) -> Dictionary:
	if state == null or state.inventory == null:
		return {"ok": false, "error": "没有会话状态", "cost": 0}
	var rows: Array = buyback_rows(building_id)
	if index < 0 or index >= rows.size():
		return {"ok": false, "error": "回购列表里没有这一条", "cost": 0}
	var entry: Dictionary = rows[index]
	var price := int(entry.get("price", 0))
	var wanted := maxi(1, int(entry.get("qty", 1)))
	var base_id := str(entry.get("item_id", ""))
	var is_equipment: bool = db.get_row("equip_base", base_id) != null
	var fits := 1 if is_equipment else mini(wanted, state.inventory.room_for(db, base_id))
	if fits <= 0:
		return {
			"ok": false,
			"error": "背包放不下（%s 已经堆满 %d 个）" % [item_name(base_id), state.inventory.count(base_id)],
			"cost": 0,
		}
	var cost := price * fits
	if money() < cost:
		return {"ok": false, "error": "铜钱不够（需要 %d）" % cost, "cost": cost}
	if is_equipment:
		var affixes: Array = []
		if not str(entry.get("instance_id", "")).is_empty():
			affixes = Array(entry.get("affixes", []))
		var persisted: String = state.inventory.add_equipment(db, base_id, affixes)
		if persisted.is_empty():
			return {"ok": false, "error": "回购失败（造不出装备）", "cost": cost}
	else:
		var added: Dictionary = state.inventory.add_item(db, base_id, fits)
		if not added["ok"] or int(added["added"]) != fits:
			return {"ok": false, "error": str(added["error"]), "cost": cost}
	state.inventory.money -= cost
	if fits < wanted:
		state.set_buyback_qty(building_id, index, wanted - fits)
	else:
		state.pop_buyback(building_id, index)
	return {
		"ok": true, "error": "", "cost": cost, "item_id": base_id,
		"received": fits, "partial": fits < wanted,
	}


# ------------------------------------------------------------------ 治疗服务

func has_heal(building_id: String) -> bool:
	var row: Resource = building(building_id)
	return row != null and str(row.service_id) == "heal"


## 治疗费用 = 缺失气血 × service_price（05 文档）
func heal_cost(building_id: String, missing_hp: int) -> int:
	var row: Resource = building(building_id)
	if row == null or str(row.service_id) != "heal":
		return 0
	return maxi(0, int(ceil(float(maxi(0, missing_hp)) * float(row.service_price))))


func heal_price(building_id: String) -> float:
	var row: Resource = building(building_id)
	return float(row.service_price) if row != null else 0.0


## 治疗预览：全队缺多少点气血、要花多少钱（不改任何状态，界面用它决定按钮亮不亮）
## 返回 {ok, error, missing, cost, rows: [{char_id, name, hp, max_hp, missing}]}
func heal_preview(building_id: String) -> Dictionary:
	var out := {"ok": false, "error": "", "missing": 0, "cost": 0, "rows": []}
	if state == null or state.inventory == null:
		out["error"] = "没有会话状态"
		return out
	if not has_heal(building_id):
		out["error"] = "这家店没有治疗服务"
		return out
	var rows: Array = []
	var missing := 0
	# 用 PartyBuilder 走同一套「等级 + 加点 + 装备 + 战斗外气血」，
	# 免得医馆按另一套算法算上限（两套算法迟早对不上）
	for char_id: String in state.char_ids:
		var actor = PartyBuilderScript.build_actor(db, state, char_id)
		if actor == null:
			continue
		var max_hp: int = actor.max_hp()
		var hp: int = clampi(actor.hp, 1, max_hp)
		var gap := maxi(0, max_hp - hp)
		missing += gap
		rows.append({
			"char_id": char_id, "name": actor.display_name,
			"hp": hp, "max_hp": max_hp, "missing": gap,
		})
	out["ok"] = true
	out["missing"] = missing
	out["cost"] = heal_cost(building_id, missing)
	out["rows"] = rows
	return out


## 治疗：扣钱、全队回满。返回 {ok, error, cost, healed}
func heal_party(building_id: String) -> Dictionary:
	var preview := heal_preview(building_id)
	if not bool(preview["ok"]):
		return {"ok": false, "error": str(preview["error"]), "cost": 0, "healed": 0}
	var missing := int(preview["missing"])
	if missing <= 0:
		return {"ok": false, "error": "气血是满的", "cost": 0, "healed": 0}
	var cost := int(preview["cost"])
	if money() < cost:
		return {"ok": false, "error": "铜钱不够（需要 %d）" % cost, "cost": cost, "healed": 0}
	state.inventory.money -= cost
	state.heal_all()
	return {"ok": true, "error": "", "cost": cost, "healed": missing}
