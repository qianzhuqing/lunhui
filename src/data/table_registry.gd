## 配置表注册表：表清单、行类脚本、主键、列别名的唯一来源。
##
## tools/build_tables.gd（构建期）与 src/core/table_db.gd（运行期）都读这里，
## 新增一张表时只需在 data/tables/ 放 CSV、在 src/data/tables/ 放行类，再登记到这里。
extends RefCounted

## 表清单，顺序与 docs/design/06_配置表说明.md 的分组一致。
const TABLES := [
	# 角色与属性
	"attribute_def",
	"stat_def",
	"curve_def",
	"attr_to_stat",
	"level_growth",
	"character_base",
	"character_base_skill",
	"event_skill_def",
	"event_check",
	# 引导与招募（0.10.0 新增，见 docs/design/09_试玩修正.md）
	"guide_step",
	"recruit_def",
	"ui_text",
	# 战斗
	"damage_type",
	"element_counter",
	"status_effect",
	"skill_base",
	"skill_active",
	"skill_passive",
	"skill_passive_stat",
	"skill_star_def",
	"combat_const",
	# 增益与套装（0.6.2 新增，见 docs/design/08_增益与套装.md）
	"buff_def",
	"buff_stat",
	"buff_grant",
	"set_def",
	"set_member",
	"set_bonus",
	"feature_toggle",
	# 成长
	"growth_const",
	# 装备与物品
	"rarity_def",
	"equip_base",
	"equip_slot_def",
	"weapon_type_def",
	"affix_pool",
	"item_base",
	# 商店与经济
	"building_def",
	"shop_stock",
	# 敌人与掉落
	"enemy_base",
	"enemy_team",
	"enemy_skill",
	"enemy_equip",
	"enemy_passive",
	"drop_table",
	"difficulty_config",
	"difficulty_drop_rate",
	# 地图与副本
	"map_region",
	"map_local",
	"roaming_spawn",
	"dungeon_room",
	"hidden_trigger",
	"npc_guard",
]

## 表名 → 行类脚本路径。
const ROW_SCRIPTS := {
	"attribute_def": "res://src/data/tables/attribute_def_row.gd",
	"stat_def": "res://src/data/tables/stat_def_row.gd",
	"curve_def": "res://src/data/tables/curve_def_row.gd",
	"attr_to_stat": "res://src/data/tables/attr_to_stat_row.gd",
	"level_growth": "res://src/data/tables/level_growth_row.gd",
	"character_base": "res://src/data/tables/character_base_row.gd",
	"character_base_skill": "res://src/data/tables/character_base_skill_row.gd",
	"event_skill_def": "res://src/data/tables/event_skill_def_row.gd",
	"event_check": "res://src/data/tables/event_check_row.gd",
	"guide_step": "res://src/data/tables/guide_step_row.gd",
	"recruit_def": "res://src/data/tables/recruit_def_row.gd",
	"ui_text": "res://src/data/tables/ui_text_row.gd",
	"damage_type": "res://src/data/tables/damage_type_row.gd",
	"element_counter": "res://src/data/tables/element_counter_row.gd",
	"status_effect": "res://src/data/tables/status_effect_row.gd",
	"skill_base": "res://src/data/tables/skill_base_row.gd",
	"skill_active": "res://src/data/tables/skill_active_row.gd",
	"skill_passive": "res://src/data/tables/skill_passive_row.gd",
	"skill_passive_stat": "res://src/data/tables/skill_passive_stat_row.gd",
	"skill_star_def": "res://src/data/tables/skill_star_def_row.gd",
	"combat_const": "res://src/data/tables/combat_const_row.gd",
	"buff_def": "res://src/data/tables/buff_def_row.gd",
	"buff_stat": "res://src/data/tables/buff_stat_row.gd",
	"buff_grant": "res://src/data/tables/buff_grant_row.gd",
	"set_def": "res://src/data/tables/set_def_row.gd",
	"set_member": "res://src/data/tables/set_member_row.gd",
	"set_bonus": "res://src/data/tables/set_bonus_row.gd",
	"feature_toggle": "res://src/data/tables/feature_toggle_row.gd",
	"growth_const": "res://src/data/tables/growth_const_row.gd",
	"rarity_def": "res://src/data/tables/rarity_def_row.gd",
	"equip_base": "res://src/data/tables/equip_base_row.gd",
	"equip_slot_def": "res://src/data/tables/equip_slot_def_row.gd",
	"weapon_type_def": "res://src/data/tables/weapon_type_def_row.gd",
	"affix_pool": "res://src/data/tables/affix_pool_row.gd",
	"item_base": "res://src/data/tables/item_base_row.gd",
	"building_def": "res://src/data/tables/building_def_row.gd",
	"shop_stock": "res://src/data/tables/shop_stock_row.gd",
	"enemy_base": "res://src/data/tables/enemy_base_row.gd",
	"enemy_team": "res://src/data/tables/enemy_team_row.gd",
	"enemy_skill": "res://src/data/tables/enemy_skill_row.gd",
	"enemy_equip": "res://src/data/tables/enemy_equip_row.gd",
	"enemy_passive": "res://src/data/tables/enemy_passive_row.gd",
	"drop_table": "res://src/data/tables/drop_table_row.gd",
	"difficulty_config": "res://src/data/tables/difficulty_config_row.gd",
	"difficulty_drop_rate": "res://src/data/tables/difficulty_drop_rate_row.gd",
	"map_region": "res://src/data/tables/map_region_row.gd",
	"map_local": "res://src/data/tables/map_local_row.gd",
	"roaming_spawn": "res://src/data/tables/roaming_spawn_row.gd",
	"dungeon_room": "res://src/data/tables/dungeon_room_row.gd",
	"hidden_trigger": "res://src/data/tables/hidden_trigger_row.gd",
	"npc_guard": "res://src/data/tables/npc_guard_row.gd",
}

## 表名 → 主键列名（顺序即 id 拼接顺序）。
const PRIMARY_KEYS := {
	"attribute_def": ["attr_id"],
	"stat_def": ["stat_id"],
	"curve_def": ["curve_id"],
	"attr_to_stat": ["id"],
	"level_growth": ["level"],
	"character_base": ["char_id"],
	"character_base_skill": ["char_id", "skill_id"],
	"event_skill_def": ["skill_id"],
	"event_check": ["check_id"],
	"guide_step": ["step_id"],
	"recruit_def": ["char_id"],
	"ui_text": ["text_id"],
	"damage_type": ["type_id"],
	"element_counter": ["element_atk", "element_def"],
	"status_effect": ["status_id"],
	"skill_base": ["skill_id"],
	"skill_active": ["skill_id"],
	"skill_passive": ["skill_id"],
	"skill_passive_stat": ["skill_id", "target"],
	"skill_star_def": ["star"],
	"combat_const": ["const_id"],
	"buff_def": ["buff_id"],
	"buff_stat": ["buff_id", "target"],
	"buff_grant": ["grant_id"],
	"set_def": ["set_id"],
	"set_member": ["set_id", "member_id"],
	"set_bonus": ["set_id", "required_count"],
	"feature_toggle": ["toggle_id"],
	"growth_const": ["const_id"],
	"rarity_def": ["rarity_id"],
	"equip_base": ["equip_id"],
	"equip_slot_def": ["slot_id"],
	"weapon_type_def": ["weapon_type"],
	"affix_pool": ["affix_id"],
	"item_base": ["item_id"],
	"building_def": ["building_id"],
	"shop_stock": ["shop_id", "item_id"],
	"enemy_base": ["enemy_id"],
	"enemy_team": ["team_id"],
	"enemy_skill": ["enemy_id", "skill_id"],
	"enemy_equip": ["enemy_id", "slot_id"],
	"enemy_passive": ["enemy_id", "skill_id"],
	"drop_table": ["drop_row_id"],
	"difficulty_config": ["difficulty_id"],
	"difficulty_drop_rate": ["difficulty_id", "rarity_id"],
	"map_region": ["node_id"],
	"map_local": ["scene_id"],
	"roaming_spawn": ["spawn_id"],
	"dungeon_room": ["room_id"],
	"hidden_trigger": ["trigger_id"],
	"npc_guard": ["guard_id"],
}

## 表名 → { CSV 列名: 行类属性名 }，仅当两者不一致时登记。
## id 与 exp 在 GDScript 里分别是基类字段名与全局函数名，需要改名避让。
const COLUMN_ALIASES := {
	"attr_to_stat": {"id": "row_id"},
	"enemy_base": {"exp": "exp_reward"},
}

const TABLES_DIR := "res://data/tables"
const OUTPUT_DIR := "res://data/generated"

## 行类属性名 → CSV 列名。
static func column_of(table_name: String, property_name: String) -> String:
	for column: String in COLUMN_ALIASES.get(table_name, {}):
		if COLUMN_ALIASES[table_name][column] == property_name:
			return column
	return property_name

## CSV 列名 → 行类属性名。
static func property_of(table_name: String, column: String) -> String:
	return COLUMN_ALIASES.get(table_name, {}).get(column, column)

## 复合主键拼成 id。
static func build_id(table_name: String, values: Dictionary) -> String:
	var parts := PackedStringArray()
	for key: String in PRIMARY_KEYS.get(table_name, []):
		parts.append(str(values.get(key, "")))
	return "|".join(parts)
