## 商店：货架、买入、卖出、回购、限量、治疗费用与存档交易状态。
##
## 规则出处 docs/design/05_装备与掉落.md「商店与经济」：
## 所有店铺都买也卖；卖出价低于买入价；卖出的装备只进回购列表；治疗费 = 缺失气血 × service_price。
extends "res://tests/test_case.gd"

const GameStateScript := preload("res://src/core/game_state.gd")
const ShopServiceScript := preload("res://src/core/shop_service.gd")
const CharacterSheetScript := preload("res://src/core/character_sheet.gd")
const AffixRollerScript := preload("res://src/core/affix_roller.gd")
const RngServiceScript := preload("res://src/core/rng_service.gd")

const CHAR_ID := "scholar_fallen"
const SMITH := "bld_smith"
const GROCERY := "bld_grocery"
const CLINIC := "bld_clinic"
const SHOP_SCENE := "res://scenes/shop_screen.tscn"


func suite_name() -> String:
	return "商店与交易"


func run() -> void:
	var db = get_db()
	var state = solo_state(db)
	var shop = ShopServiceScript.new(db, state)
	_check_stock(db, shop)
	_check_buy(db, state, shop)
	_check_sell(db, state, shop)
	_check_buyback(db, state, shop)
	_check_buyback_partial(db)
	_check_limited_stock(db)
	_check_limited_stock_ui(db)
	_check_bulk_buy(db)
	_check_sell_all(db)
	_check_clinic_multi_member(db)
	_check_heal(db, state, shop)
	_check_save(db, state)


## 五家店的货架按表来，且都同时买也卖
## （0.28.0 加了第五家：**行商** `bld_caravan`——它不是地图上的建筑，
##   是大地图随机事件 `we_caravan` 开出来的货架，所以这里也要能查到它的行）
func _check_stock(db, shop) -> void:
	# 杂货铺 7 行 = 5 行材料／白板饰品 ＋ 0.23.0 加进来的毒酒（隐藏线「毒杀毒手」的钥匙）
	# ＋ 2026-10-04 的秘籍（Q3：shop 那 5 部武学改走 `item` 来源，四家店各上架一本）
	var counts := {
		GROCERY: 7, "bld_tavern": 3, CLINIC: 2, SMITH: 10, "bld_caravan": 4,
	}
	for building_id: String in counts:
		var rows: Array = shop.stock_rows(building_id)
		check_eq(rows.size(), int(counts[building_id]), "%s 的货架行数" % building_id)
		var last_order := -1
		for row: Resource in rows:
			check_gt(float(row.sort_order), float(last_order), "货架按 sort_order 升序")
			last_order = int(row.sort_order)
			check_gt(float(row.buy_price), 0.0, "%s 的 %s 有买入价" % [building_id, row.item_id])
			check_gt(float(row.buy_price), float(row.sell_price), "%s 的 %s 买入价高于卖出价" % [building_id, row.item_id])
	check_true(shop.has_heal(CLINIC), "医馆有治疗服务")
	check_false(shop.has_heal(SMITH), "铁匠铺没有治疗服务")


## 买入：扣钱、进背包、记累计次数；钱不够、不卖的东西、只掉落的东西都要被拒
func _check_buy(db, state, shop) -> void:
	state.inventory.money = 1000
	var bought: Dictionary = shop.buy(GROCERY, "item_iron", 5)
	check_true(bool(bought["ok"]), "买 5 块铁矿石：%s" % bought["error"])
	check_eq(int(bought["spent"]), 100, "花掉 5 × 20 = 100 文")
	check_eq(state.inventory.money, 900, "铜钱扣掉")
	check_eq(state.inventory.count("item_iron"), 5, "材料进背包")
	check_eq(state.purchased_count(GROCERY, "item_iron"), 5, "累计买入次数写进存档")

	var poor: Dictionary = shop.can_buy(SMITH, "eq_sword_01", 1)
	check_true(bool(poor["ok"]), "900 文买得起 120 文的剑")
	state.inventory.money = 10
	var no_money: Dictionary = shop.buy(SMITH, "eq_sword_01", 1)
	check_false(bool(no_money["ok"]), "铜钱不够买不了")
	check_true(str(no_money["error"]).contains("铜钱不够"), "说明是铜钱不够：%s" % no_money["error"])
	state.inventory.money = 1000

	var not_sold: Dictionary = shop.can_buy(SMITH, "item_herb", 1)
	check_false(bool(not_sold["ok"]), "铁匠铺不卖草药")
	check_true(str(not_sold["error"]).contains("不卖"), "说明这家店不卖：%s" % not_sold["error"])

	# 装备买入走实例，且是白板（没有随机词条——词条是刷出来的，不是买来的）
	var weapon: Dictionary = shop.buy(SMITH, "eq_sword_01", 1)
	check_true(bool(weapon["ok"]), "买白板剑：%s" % weapon["error"])
	check_eq(Array(weapon["equipment"]).size(), 1, "生成了一件装备实例")
	if not Array(weapon["equipment"]).is_empty():
		var instance_id := str(Array(weapon["equipment"])[0])
		check_eq(state.inventory.base_of(instance_id), "eq_sword_01", "本体是铁剑")
		check_true(state.inventory.affixes_of(instance_id).is_empty(), "店里卖的是白板，没有词条")

	# stock_limit = 0 表示无限量（第一章全是无限量）
	check_eq(shop.remaining(GROCERY, "item_iron"), -1, "无限量货架返回 -1")
	check_eq(shop.buy_price(GROCERY, "item_iron"), 20, "买入价取自货架")

	# 「差一文」边界：刚好够钱能买、少一文就拒（与治疗那条同口径）
	var count_before := int(state.inventory.count("item_iron"))
	state.inventory.money = 19
	var short: Dictionary = shop.buy(GROCERY, "item_iron", 1)
	check_false(bool(short["ok"]), "差一文买不了")
	check_eq(state.inventory.money, 19, "被拒时一文不扣")
	check_eq(int(state.inventory.count("item_iron")), count_before, "被拒时背包不变")
	state.inventory.money = 20
	var exact: Dictionary = shop.buy(GROCERY, "item_iron", 1)
	check_true(bool(exact["ok"]), "刚好够钱能买：%s" % exact["error"])
	check_eq(state.inventory.money, 0, "刚好花光")
	check_eq(int(state.inventory.count("item_iron")), count_before + 1, "货进了背包")


## 卖出：价格用卖出价、扣物品、进回购列表；钥匙、身上穿着的、店里不上架的都要被拒
func _check_sell(db, state, shop) -> void:
	var iron_before := int(state.inventory.count("item_iron"))
	var got: Dictionary = shop.sell(GROCERY, "item_iron", 2)
	check_true(bool(got["ok"]), "卖 2 块铁矿石：%s" % got["error"])
	check_eq(int(got["earned"]), 12, "卖价 6 × 2 = 12 文")
	check_eq(int(state.inventory.count("item_iron")), iron_before - 2, "背包里少 2 块")
	var entries: Array = shop.buyback_rows(GROCERY)
	check_eq(entries.size(), 1, "卖出的东西进回购列表")
	if not entries.is_empty():
		check_eq(str(entries[0]["item_id"]), "item_iron", "回购条目记的是铁矿石")
		check_eq(int(entries[0]["price"]), 6, "回购价就是卖出价")
		check_eq(int(entries[0]["qty"]), 2, "回购条目记数量")

	# 钥匙道具不能卖
	state.inventory.add_item(db, "item_wine_gourd", 1)
	var key_sell: Dictionary = shop.sell(GROCERY, "item_wine_gourd", 1)
	check_false(bool(key_sell["ok"]), "钥匙道具不能卖")
	check_true(str(key_sell["error"]).contains("钥匙"), "说明是钥匙道具：%s" % key_sell["error"])

	# 掉落的装备（店里没上架）不收
	var dropped: String = state.inventory.add_equipment(db, "eq_sword_03")
	var no_shelf: Dictionary = shop.sell(SMITH, dropped, 1)
	check_false(bool(no_shelf["ok"]), "店里没上架的装备不收")
	check_true(str(no_shelf["error"]).contains("不收"), "说明这家店不收：%s" % no_shelf["error"])

	# 上架的装备：身上穿着的不能卖，卸下后能卖，且词条一起进回购列表
	# （白板戒指 eq_ring_01 要 2 级：抬到 3 级；词条手写，免得 0~1 条的凡品掷出空词条）
	state.char_levels[CHAR_ID] = 3
	var ring: String = state.inventory.add_equipment(
		db, "eq_ring_01",
		[{"affix_id": "af_luk_01", "target": "attr:luk", "value_kind": "attr_point", "value": 2.0}]
	)
	var worn: Dictionary = state.inventory.equip(db, CHAR_ID, 3, "sword", ring)
	check_true(bool(worn["ok"]), "先把戒指戴上")
	var worn_sell: Dictionary = shop.sell(GROCERY, ring, 1)
	check_false(bool(worn_sell["ok"]), "身上穿着的不能卖")
	check_true(str(worn_sell["error"]).contains("先卸下"), "提示先卸下：%s" % worn_sell["error"])
	state.inventory.unequip(CHAR_ID, "ring", 0)
	var sold: Dictionary = shop.sell(GROCERY, ring, 1)
	check_true(bool(sold["ok"]), "卸下后能卖：%s" % sold["error"])
	check_false(state.inventory.has_equipment(ring), "卖掉的实例从背包里消失")
	var last: Array = shop.buyback_rows(GROCERY)
	check_eq(str(last[last.size() - 1].get("instance_id", "")), ring, "回购条目记着实例 id")
	check_gt(float(Array(last[last.size() - 1].get("affixes", [])).size()), 0.0, "词条跟着进回购列表")


## 回购：价格就是当初的卖出价；带词条的装备原样回来；条目按上限挤掉最早的
func _check_buyback(db, state, shop) -> void:
	# 回购上限是**开发侧口径**（模块备注写着「回购＝按卖出价撤销卖出（上限20条）」）：
	# 用 `push_buyback(..., 20)` 造夹具的地方会跟着常量走，所以常量本身得单独钉住
	# （2026-10-03 变异探针：`BUYBACK_LIMIT 20→5` 没被抓住）。
	check_eq(ShopServiceScript.BUYBACK_LIMIT, 20, "回购列表上限 20 条")
	var before: Array = shop.buyback_rows(GROCERY)
	var index := before.size() - 1
	var entry: Dictionary = before[index]
	var cost := int(entry["price"]) * int(entry["qty"])
	var money_before: int = state.inventory.money
	var back: Dictionary = shop.buy_back(GROCERY, index)
	check_true(bool(back["ok"]), "能从回购列表买回来：%s" % back["error"])
	check_eq(int(back["cost"]), cost, "回购价 = 当初的卖出价")
	check_eq(state.inventory.money, money_before - cost, "回购扣钱")
	check_eq(shop.buyback_rows(GROCERY).size(), before.size() - 1, "回购后条目出列")
	var restored := ""
	for instance_id: String in state.inventory.equipment_ids():
		if state.inventory.base_of(instance_id) == "eq_ring_01":
			restored = instance_id
	check_ne(restored, "", "戒指回到背包")
	if not restored.is_empty():
		check_gt(
			float(state.inventory.affixes_of(restored).size()), 0.0,
			"回购回来的戒指词条没丢：%s" % AffixRollerScript.summarize(db, state.inventory.affixes_of(restored)),
		)

	# 回购列表上限 20：塞 25 条，最早的 5 条被挤掉
	for n in range(25):
		state.push_buyback(SMITH, {"item_id": "item_iron", "qty": 1, "price": 6, "tag": n}, 20)
	var capped: Array = state.buyback_of(SMITH)
	check_eq(capped.size(), 20, "回购列表封顶 20 条")
	check_eq(int(capped[0].get("tag", -1)), 5, "最早的那几条被挤掉")


## 回购时的「部分入包」：同一堆只剩 2 格空位、回购条目却有 5 个时，
## 以前按**全价**扣钱、整条出列 —— 玩家付了 5 个的钱只拿到 2 个，另外 3 个凭空消失。
## （买入路径早就写了「只按真正放进去的数量收钱，别把钱扣在空气上」，回购漏了这一条。）
func _check_buyback_partial(db) -> void:
	var state = solo_state(db)
	var shop = ShopServiceScript.new(db, state)
	state.inventory.money = 1000
	var row: Resource = db.get_row("item_base", "item_iron")
	var cap := maxi(1, int(row.stack_max))
	state.inventory.add_item(db, "item_iron", cap)
	check_true(bool(shop.sell(GROCERY, "item_iron", 5)["ok"]), "先卖掉 5 块铁矿石（进回购列表）")
	var unit := shop.sell_price(GROCERY, "item_iron")
	check_gt(float(unit), 0.0, "杂货铺收铁矿石且单价大于 0")
	state.inventory.add_item(db, "item_iron", 3) # 卖完剩 94，再捡 3 块 → 只剩 2 格空位
	check_eq(state.inventory.count("item_iron"), cap - 2, "背包里这一堆只剩 2 格空位")

	var money_before := int(state.inventory.money)
	var back: Dictionary = shop.buy_back(GROCERY, 0)
	check_true(bool(back["ok"]), "回购能部分成交：%s" % back["error"])
	check_eq(int(back["cost"]), unit * 2, "只按真正放进去的 2 个收钱")
	check_eq(state.inventory.count("item_iron"), cap, "背包这一堆补满")
	check_eq(int(state.inventory.money), money_before - unit * 2, "钱也只扣 2 个的")
	var left: Array = shop.buyback_rows(GROCERY)
	check_eq(left.size(), 1, "没买完的还留在回购列表")
	if not left.is_empty():
		check_eq(int(left[0]["qty"]), 3, "剩下的 3 个还在列表里（没被吞掉）")

	# 腾出地方再买回剩下的：条目清掉
	state.inventory.remove_item(db, "item_iron", 4)
	var again: Dictionary = shop.buy_back(GROCERY, 0)
	check_true(bool(again["ok"]), "腾出地方后能把剩下的买回来：%s" % again["error"])
	check_eq(shop.buyback_rows(GROCERY).size(), 0, "买完条目出列")

	# 一格都放不下时：不许扣钱，条目原样保留
	check_true(bool(shop.sell(GROCERY, "item_iron", 5)["ok"]), "再卖 5 块（新条目）")
	# 用 room_for 补齐（别手算格数——上一版就是手算错了，断言才红的）
	state.inventory.add_item(db, "item_iron", state.inventory.room_for(db, "item_iron"))
	check_eq(state.inventory.room_for(db, "item_iron"), 0, "这一堆现在是满的")
	var full_money := int(state.inventory.money)
	var refused: Dictionary = shop.buy_back(GROCERY, shop.buyback_rows(GROCERY).size() - 1)
	check_false(bool(refused["ok"]), "背包放不下时不许回购")
	check_true(str(refused["error"]).contains("放不下"), "说明是背包放不下：%s" % refused["error"])
	check_eq(int(state.inventory.money), full_money, "被拒时一文不扣")


## 医馆治疗：费用 = 缺失气血 × service_price。战斗外气血 v11 起进存档，所以这里是**真治**。
func _check_heal(db, state, shop) -> void:
	check_eq(shop.heal_price(CLINIC), 2.0, "每点气血 2 文")
	check_eq(shop.heal_cost(CLINIC, 30), 60, "缺 30 点气血要 60 文")
	check_eq(shop.heal_cost(SMITH, 30), 0, "没有治疗服务的店费用为 0")
	check_false(bool(shop.heal_preview(SMITH)["ok"]), "铁匠铺没有治疗服务")

	# 满血：预览缺 0、治疗被拒
	var full: Dictionary = shop.heal_preview(CLINIC)
	check_true(bool(full["ok"]), "医馆能出治疗预览")
	check_eq(int(full["missing"]), 0, "没受伤时缺失为 0")
	var full_heal: Dictionary = shop.heal_party(CLINIC)
	check_false(bool(full_heal["ok"]), "满血不用治")
	check_true(str(full_heal["error"]).contains("满"), "说明气血是满的：%s" % full_heal["error"])

	# 把主角打伤 30 点：预览按缺失算钱，治疗后回满
	var sheet = CharacterSheetScript.new(db, state, CHAR_ID)
	var cap: int = sheet.max_hp()
	state.set_current_hp(CHAR_ID, cap - 30)
	var preview: Dictionary = shop.heal_preview(CLINIC)
	check_eq(int(preview["missing"]), 30, "缺 30 点")
	check_eq(int(preview["cost"]), 60, "缺 30 点要 60 文")
	check_eq(int(Array(preview["rows"])[0]["hp"]), cap - 30, "预览里写着当前气血")
	state.inventory.money = 59
	var poor: Dictionary = shop.heal_party(CLINIC)
	check_false(bool(poor["ok"]), "钱不够治不了")
	check_true(str(poor["error"]).contains("铜钱不够"), "写清缺钱：%s" % poor["error"])
	state.inventory.money = 60
	var healed: Dictionary = shop.heal_party(CLINIC)
	check_true(bool(healed["ok"]), "钱够就能治：%s" % healed["error"])
	check_eq(int(healed["cost"]), 60, "花 60 文")
	check_eq(int(healed["healed"]), 30, "回复点数 = 缺失气血")
	check_eq(int(state.inventory.money), 0, "钱扣掉了")
	check_false(state.is_wounded(CHAR_ID), "治完全队不带伤")
	check_eq(int(shop.heal_preview(CLINIC)["missing"]), 0, "治完预览归零")


## 交易状态进存档：累计买入次数与回购列表都要能读回来
func _check_save(db, state) -> void:
	var data: Dictionary = state.to_dict()
	check_true(data.has("shop_state"), "存档里有 shop_state")
	var back = GameStateScript.from_dict(data, db)
	check_not_null(back, "带交易状态的存档能读回来")
	if back == null:
		return
	check_eq(back.migrated_from, 0, "同版本往返不需要迁移")
	check_eq(back.purchased_count(GROCERY, "item_iron"), state.purchased_count(GROCERY, "item_iron"), "累计买入次数往返一致")
	check_eq(back.buyback_of(SMITH).size(), state.buyback_of(SMITH).size(), "回购列表往返一致")
	check_eq(shop_state_keys(back), shop_state_keys(state), "参与过交易的店铺一致")

	# v5 老档（没有 shop_state）读进来是空的，版本升到 v6
	var legacy: Dictionary = data.duplicate(true)
	legacy["version"] = 5
	legacy.erase("shop_state")
	var migrated = GameStateScript.from_dict(legacy, db)
	check_eq(migrated.migrated_from, 5, "记下从 v5 迁移")
	check_eq(migrated.version, GameStateScript.VERSION, "版本升到当前")
	check_eq(migrated.buyback_of(SMITH).size(), 0, "老档没有回购列表")
	check_eq(migrated.purchased_count(GROCERY, "item_iron"), 0, "老档没有累计买入次数")


## 限量货架：第一章的货架全是无限量（`stock_limit=0`），所以「限量」这条路从来没被真数据跑过——
## 这里复制一份 `shop_stock` 只改内存，把杂货铺的铁矿石改成限 3 个，服务层与界面层各走一遍。
func _check_limited_stock(db) -> void:
	var limited = _stock_table_with(db, "shop_grocery", "item_iron", 3)
	var state = solo_state(db)
	state.inventory.money = 1000
	var shop = ShopServiceScript.new(limited, state)
	check_eq(shop.remaining(GROCERY, "item_iron"), 3, "限量货架初始还剩 3")
	var two: Dictionary = shop.buy(GROCERY, "item_iron", 2)
	check_true(bool(two["ok"]), "买 2 个可以：%s" % two["error"])
	check_eq(shop.remaining(GROCERY, "item_iron"), 1, "买完剩 1")
	check_eq(state.purchased_count(GROCERY, "item_iron"), 2, "累计买入次数跟着涨")

	var over: Dictionary = shop.buy(GROCERY, "item_iron", 2)
	check_false(bool(over["ok"]), "只剩 1 个时买 2 个被拒")
	check_true(str(over["error"]).contains("库存不够"), "说明是库存不够：%s" % over["error"])
	check_eq(int(state.inventory.money), 960, "被拒时一文不扣")
	check_eq(state.purchased_count(GROCERY, "item_iron"), 2, "被拒时累计次数不变")

	check_true(bool(shop.buy(GROCERY, "item_iron", 1)["ok"]), "把最后 1 个买走")
	check_eq(shop.remaining(GROCERY, "item_iron"), 0, "卖光后的余量是 0（限量和无限量的 -1 要分得开）")
	var sold_out: Dictionary = shop.can_buy(GROCERY, "item_iron", 1)
	check_false(bool(sold_out["ok"]), "卖光后再买被拒")
	check_true(str(sold_out["error"]).contains("还能买 0 个"), "写清还能买 0 个：%s" % sold_out["error"])
	check_eq(shop.remaining(GROCERY, "item_herb"), -1, "改的是那一行：同店其它货架还是无限量")

	# 累计买入次数进存档 → 读回来仍然「卖光了」
	var back = GameStateScript.from_dict(state.to_dict(), limited)
	check_not_null(back, "限量进度随存档走")
	if back != null:
		var shop2 = ShopServiceScript.new(limited, back)
		check_eq(shop2.remaining(GROCERY, "item_iron"), 0, "读档后仍记得这家店卖光了")


## 「一次买多件」两条路：装备买 5 把要生成 5 个**独立实例**（用得上、卖得出、各带各的词条）；
## 材料堆到上限时**只按真正放进去的数量收钱**（买入侧的 overflow 以前没人验——回购侧补过同类）。
func _check_bulk_buy(db) -> void:
	var state = solo_state(db)
	var shop = ShopServiceScript.new(db, state)
	state.inventory.money = 1000
	var swords: Dictionary = shop.buy(SMITH, "eq_sword_01", 5)
	check_true(bool(swords["ok"]), "一次买 5 把剑：%s" % swords["error"])
	check_eq(Array(swords["equipment"]).size(), 5, "生成 5 个装备实例")
	var unique: Dictionary = {}
	for instance_id: String in Array(swords["equipment"]):
		unique[str(instance_id)] = true
	check_eq(unique.size(), 5, "5 个实例 id 各不相同")
	check_true(not unique.has(""), "没有造空的实例 id")
	check_eq(int(swords["spent"]), 120 * 5, "按 5 件收钱")
	check_eq(int(state.inventory.money), 1000 - 600, "钱扣对")

	# 材料：先堆到只剩 2 格空位，再买 5 个 → 只收 2 个的钱，其余在 overflow 里报出来
	state.inventory.add_item(db, "item_iron", 97)
	state.inventory.money = 1000
	var partial: Dictionary = shop.buy(GROCERY, "item_iron", 5)
	check_true(bool(partial["ok"]), "堆快满时也能买（部分入包）：%s" % partial["error"])
	check_eq(int(partial["spent"]), 20 * 2, "只按真正放进去的 2 个收钱")
	check_eq(int(state.inventory.count("item_iron")), 99, "这一堆补满了")
	check_eq(int(state.inventory.money), 1000 - 40, "钱只扣 2 个的")
	check_eq(int(Array(partial["got"])[0].get("overflow", 0)), 3, "没放进去的 3 个在 overflow 里报出来")
	check_eq(state.purchased_count(GROCERY, "item_iron"), 2, "累计买入次数按真正入包的数量记")


## 卖出侧的另一半：**一次卖光**（数量正好等于持有量）→ 背包那一行**消失**（不留 0 个的空行，
## 否则背包里会出现「铁矿石 ×0」），回购条目记下整批的数量与单价；卖得比手上多要被拒。
func _check_sell_all(db) -> void:
	var state = solo_state(db)
	var shop = ShopServiceScript.new(db, state)
	state.inventory.add_item(db, "item_iron", 7)
	var too_many: Dictionary = shop.sell(GROCERY, "item_iron", 8)
	check_false(bool(too_many["ok"]), "卖得比手上多要被拒")
	check_true(str(too_many["error"]).contains("数量不足"), "写明数量不足：%s" % too_many["error"])
	check_eq(int(state.inventory.count("item_iron")), 7, "被拒后数量不变")

	var sold: Dictionary = shop.sell(GROCERY, "item_iron", 7)
	check_true(bool(sold["ok"]), "一次卖光 7 块：%s" % sold["error"])
	check_eq(int(sold["earned"]), 6 * 7, "按卖出价 ×7 收钱")
	check_eq(int(state.inventory.count("item_iron")), 0, "数量归 0")
	check_false(state.inventory.has("item_iron"), "背包里不再算「有」它")
	check_false(state.inventory.item_ids().has("item_iron"), "背包清单里也不再有它（不留 ×0 的空行）")
	# `item_ids()` 会过滤 0，所以「留不留 0 键」从外面看不出来——但它**会被写进存档**（多一条垃圾，
	# 而且堆叠表会随着买卖慢慢涨），所以直接看内部表
	check_false(state.inventory.stacks.has("item_iron"), "内部堆叠表也不留 0 键（存档里不该有垃圾条目）")
	var rows: Array = shop.buyback_rows(GROCERY)
	check_eq(rows.size(), 1, "整批进回购列表一条")
	if not rows.is_empty():
		check_eq(int(rows[0]["qty"]), 7, "条目记 7 个")
		check_eq(int(rows[0]["price"]), 6, "单价还是卖出价")


## 商店服务页的多人分支：4 人队伍时预览要**逐人列一行**、总价按全队缺失求和，治疗一次全队回满。
## 服务层的「全队求和」在 test_end_to_end 里验过，这一条补的是**界面行数与文案**——
## 自检只跑过 1 人，4 行预览的排布与「谁缺多少」没人看过。
func _check_clinic_multi_member(db) -> void:
	if scene_tree == null:
		fail("没有注入场景树，医馆多人用例无法进行")
		return
	var wide = table_with_n_chars(4)
	var state = party_state(wide, 4)
	var ids := PackedStringArray(state.char_ids)
	check_eq(ids.size(), 4, "造了 4 人队伍")
	state.inventory.money = 1000
	var cap_1: int = CharacterSheetScript.new(wide, state, ids[1]).max_hp()
	var cap_2: int = CharacterSheetScript.new(wide, state, ids[2]).max_hp()
	state.set_current_hp(ids[1], cap_1 - 10)
	state.set_current_hp(ids[2], cap_2 - 20)
	var screen = load(SHOP_SCENE).instantiate()
	screen.db_override = wide
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()
	screen.open_building(CLINIC)
	screen.select_tab("service")
	var heal_label: Label = screen.find_child("HealLabel", true, false)
	check_not_null(heal_label, "有治疗预览行")
	if heal_label != null:
		check_true(heal_label.text.contains("全队缺 30 点"), "总缺失 = 10 + 20：%s" % heal_label.text)
		check_true(heal_label.text.contains("要 60 文"), "总价 = 30 点 × 2 文：%s" % heal_label.text)
	var rows := 0
	for node: Node in screen.find_children("Heal_*", "Label", true, false):
		rows += 1
	check_eq(rows, 4, "四个队员各有一行")
	var second_row: Label = screen.find_child("Heal_%s" % ids[1], true, false)
	check_not_null(second_row, "带伤队员有自己的行")
	if second_row != null:
		check_true(second_row.text.contains("缺 10"), "写清他缺多少：%s" % second_row.text)
	var heal_button: Button = screen.find_child("HealButton", true, false)
	if heal_button != null:
		check_false(heal_button.disabled, "钱够就能按")
	var healed: Dictionary = screen.press_heal()
	check_true(bool(healed["ok"]), "全队治疗成功：%s" % healed["error"])
	check_eq(int(healed["cost"]), 60, "花 60 文")
	check_eq(int(state.inventory.money), 940, "钱扣掉 60")
	for char_id: String in ids:
		check_false(state.is_wounded(char_id), "治完都不带伤（%s）" % char_id)
	scene_tree.root.remove_child(screen)
	screen.free()


## 限量在界面上要看得见：行里写「限量：还剩 N」；要买的数量超过剩余时按钮置灰并写明原因。
func _check_limited_stock_ui(db) -> void:
	if scene_tree == null:
		fail("没有注入场景树，限量界面用例无法进行")
		return
	var limited = _stock_table_with(db, "shop_grocery", "item_iron", 3)
	var state = solo_state(db)
	state.inventory.money = 1000
	var screen = load(SHOP_SCENE).instantiate()
	screen.db_override = limited
	screen.state_override = state
	scene_tree.root.add_child(screen)
	screen.setup()
	screen.open_building(GROCERY)
	screen.select_tab("buy")

	var label: Label = screen.find_child("BuyLabelitem_iron", true, false)
	check_not_null(label, "限量商品行在")
	if label != null:
		check_true(label.text.contains("限量：还剩 3"), "行里写明还剩几个：%s" % label.text)
	var buy: Button = screen.find_child("BuyButtonitem_iron", true, false)
	check_not_null(buy, "限量商品有买入按钮")
	if buy != null:
		check_false(buy.disabled, "数量 1 时能买")

	# 数量轮到 5（> 剩余 3）：按钮置灰、行里写明库存不够（QTY_STEPS = 1 → 5 → 10 → 1）
	check_eq(screen.quantity(), 1, "默认数量 1")
	screen.press_qty()
	check_eq(screen.quantity(), 5, "点一下轮到 5 个")
	var buy5: Button = screen.find_child("BuyButtonitem_iron", true, false)
	check_not_null(buy5, "刷新后的行里还有买入按钮")
	if buy5 != null:
		check_true(buy5.disabled, "要买 5 个但只剩 3 个 → 按钮置灰")
	var label5: Label = screen.find_child("BuyLabelitem_iron", true, false)
	if label5 != null:
		check_true(label5.text.contains("库存不够"), "行里写明库存不够：%s" % label5.text)

	# 轮回到 1：买三次就把这 3 个卖光 → 行还在、写「还剩 0」、按钮仍置灰
	screen.press_qty()
	check_eq(screen.quantity(), 10, "再点一下轮到 10 个")
	screen.press_qty()
	check_eq(screen.quantity(), 1, "数量轮回到 1")
	check_true(bool(screen.press_buy("item_iron")["ok"]), "买第 1 个")
	check_true(bool(screen.press_buy("item_iron")["ok"]), "买第 2 个")
	check_true(bool(screen.press_buy("item_iron")["ok"]), "买第 3 个（卖光）")
	check_eq(state.inventory.count("item_iron"), 3, "背包里进了 3 个")
	var sold_label: Label = screen.find_child("BuyLabelitem_iron", true, false)
	check_not_null(sold_label, "卖光后商品行还在（不是整行消失）")
	if sold_label != null:
		check_true(sold_label.text.contains("还剩 0"), "写清还剩 0：%s" % sold_label.text)
	var sold_buy: Button = screen.find_child("BuyButtonitem_iron", true, false)
	if sold_buy != null:
		check_true(sold_buy.disabled, "卖光后按钮置灰")
	scene_tree.root.remove_child(screen)
	screen.free()


## 复制一份表库，把某一行 `shop_stock` 的 `stock_limit` 改掉（**只改内存副本，不碰策划的 CSV**）。
func _stock_table_with(db, shop_id: String, item_id: String, limit: int):
	var custom = TableDbScript.new()
	custom.load_all()
	var table: Resource = custom.tables["shop_stock"].duplicate(true)
	for row: Resource in table.rows:
		if str(row.shop_id) == shop_id and str(row.item_id) == item_id:
			row.stock_limit = limit
	custom.tables["shop_stock"] = table
	return custom


func shop_state_keys(state) -> PackedStringArray:
	var out := PackedStringArray()
	for key: String in state.shop_state:
		out.append(key)
	out.sort()
	return out
