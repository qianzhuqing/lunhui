## talent_def.csv 行：创建角色的天赋池（24 行，五个点内自选）。
##
## 0.16.0 新增（设计 12）：出身定底子、天赋定偏好。数值／规则效果在 `talent_effect`
## （`attr:`／`stat:`／`rule:` 三种前缀），这里只有展示与花费。
extends "res://src/data/table_row.gd"

@export var talent_id: String = ""
@export var name_cn: String = ""
## 六个方向：combat（武学）／body（筋骨）／agile（身法）／mind（悟道）／social（江湖）／fortune（财货）
@export var category: String = ""
## 花几个天赋点（1~3）；总点数在 growth_const.talent_points
@export var cost: int = 1
## 玩家在创建界面看到的就是这句话——**带代价的天赋必须把代价写在这里**（设计 12 第三条纪律）
@export var desc: String = ""
