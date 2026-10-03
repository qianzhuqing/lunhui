## building_def.csv 行：建筑（店铺与附加服务）。
extends "res://src/data/table_row.gd"

@export var building_id: String = ""
@export var name_cn: String = ""
## shop 等，目前只有店铺
@export var building_type: String = "shop"
## 货架 id，引用 shop_stock.shop_id
@export var stock_group: String = ""
## 附加服务 id（如 heal 治疗），留空表示只有买卖
@export var service_id: String = ""
## 附加服务单价（每点气血的文数）
@export var service_price: int = 0
@export var unlock_condition: String = ""
@export var desc: String = ""


func has_service() -> bool:
	return not service_id.strip_edges().is_empty()
