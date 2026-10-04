## equip_base.csv 行：装备基础数值。
##
## attr_* 是直接加属性点，会再经过 attr_to_stat 换算成派生数值；
## bonus_* 是直接加派生数值，去掉前缀即为 stat_def.stat_id（见 docs/design/06_配置表说明.md）。
extends "res://src/data/table_row.gd"

@export var equip_id: String = ""
@export var name_cn: String = ""
## weapon / armor / head / accessory / hidden
@export var slot: String = ""
## 引用 rarity_def.rarity_id
@export var rarity: String = ""
@export var level_req: int = 1
## 武器系别 external / internal / odd
@export var element: String = ""
## 武器类型，引用 weapon_type_def.weapon_type（防具／饰品留空）
@export var weapon_type: String = ""
@export var attr_str: int = 0
@export var attr_con: int = 0
@export var attr_agi: int = 0
@export var attr_int: int = 0
@export var attr_luk: int = 0
@export var bonus_atk_phys: float = 0.0
@export var bonus_atk_qi: float = 0.0
@export var bonus_def_phys: float = 0.0
@export var bonus_hp_max: float = 0.0
@export var bonus_speed: float = 0.0
@export var bonus_crit_rate: float = 0.0
## 穿透率：抵消目标一部分减伤
@export var bonus_pen_rate: float = 0.0
## 格挡率：与闪避互斥的另一种免伤手段
@export var bonus_block_rate: float = 0.0
@export var bonus_block_reduction: float = 0.0
## 减伤率：终局乘区
@export var bonus_dmg_reduction: float = 0.0
@export var special_effect: String = ""
@export var drop_only: bool = false
@export var desc: String = ""
## 图标 id：**值＝`equip_id`**（设计 15 §六＋A14，0.31.1）。与 `item_base.icon` 同形——
## 界面按 `assets/icons/equip/<icon>.png` 取图、空则退回 `equip_id`，所以"还没出图"不等于数据错。
@export var icon: String = ""


## 直接加派生数值的部分，键为 stat_def.stat_id。
func stat_bonuses() -> Dictionary:
	return {
		"atk_phys": bonus_atk_phys,
		"atk_qi": bonus_atk_qi,
		"def_phys": bonus_def_phys,
		"hp_max": bonus_hp_max,
		"speed": bonus_speed,
		"crit_rate": bonus_crit_rate,
		"pen_rate": bonus_pen_rate,
		"block_rate": bonus_block_rate,
		"block_reduction": bonus_block_reduction,
		"dmg_reduction": bonus_dmg_reduction,
	}


## 直接加属性点的部分，键为 attribute_def.attr_id。
func attr_bonuses() -> Dictionary:
	return {
		"str": attr_str,
		"con": attr_con,
		"agi": attr_agi,
		"int": attr_int,
		"luk": attr_luk,
	}
