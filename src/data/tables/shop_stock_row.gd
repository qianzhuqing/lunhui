## shop_stock.csv 行：货架（某店卖什么、什么价、限不限量）。
##
## 价格规则：卖出价一律低于买入价，差价即交易税（校验器会查，别让玩家刷钱）。
extends "res://src/data/table_row.gd"

@export var shop_id: String = ""
@export var sort_order: int = 0
## 物品或装备 id，按是否存在于 equip_base 分流
@export var item_id: String = ""
@export var buy_price: int = 0
@export var sell_price: int = 0
## 0 表示无限量（第一章所有货架都是无限量）
@export var stock_limit: int = 0
@export var note: String = ""


func is_unlimited() -> bool:
	return stock_limit <= 0


func margin() -> int:
	return buy_price - sell_price
