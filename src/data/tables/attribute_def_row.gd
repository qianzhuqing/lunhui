## attribute_def.csv 行：五维属性定义。
extends "res://src/data/table_row.gd"

@export var attr_id: String = ""
@export var name_cn: String = ""
## 1 = 玩家可自由加点（力体敏智运）；0 = 资质，创建时定死（悟性／根骨）。
## 设计 0.13.0：资质决定天花板、等级决定成长速度，所以资质不许靠刷级买。
@export var allocatable: int = 1
@export var desc: String = ""
