## 配置表交叉校验：构建期（build_tables）与运行期（GameData）共用同一套规则。
##
## 校验分四类：主键、枚举、引用、数值区间。
## 任何一条不过都返回可定位的报错文本，构建脚本据此 fail-fast。
class_name TableValidator
extends RefCounted

const Registry := preload("res://src/data/table_registry.gd")
## 异常状态的 `extra_rule` 取值必须落在**代码真的认得的规则**里——那份名单的唯一真相是
## `battle_actor.KNOWN_STATUS_RULES`（不在这里手抄一份，抄了就会漂移）。
const BattleActorScript := preload("res://src/core/battle_actor.gd")
## AI 模板映射与逐敌人招式覆盖的唯一真相都在 EnemyFactory（这里只读，不手抄一份）
const EnemyFactoryScript := preload("res://src/core/enemy_factory.gd")
## 难度解锁短语的唯一真相在 WorldMapService（这里只读它那份映射表，不手抄中文短语）
const WorldMapServiceScript := preload("res://src/core/world_map_service.gd")
## 成长常数的名字在代码里有一份集中清单（`GrowthCalculator.DEFAULTS` 的键）——校验器只读它
const GrowthCalculatorScript := preload("res://src/core/growth_calculator.gd")
const AttributeCalculatorScript := preload("res://src/core/attribute_calculator.gd")
## 「伤害类型代码支不支持」的唯一出处是 BattleSimulator 的静态判断（这里**不另写一份名单**，
## 抄一份就迟早分家——2026-10-03 的 DoT 事故正是「代码过滤」与「数据」悄悄不一致，见决策 229）。
const BattleSimulatorScript := preload("res://src/core/battle_simulator.gd")

const ELEMENTS := ["external", "internal", "odd"]
const ITEM_TYPES := ["currency", "material", "consumable", "key", "tool", "skillbook"]
const DROP_ITEM_TYPES := ["money", "material", "consumable", "equip", "key", "tool", "skillbook"]
const THREAT_TAGS := ["green", "yellow", "red", "purple"]

## 表名 → { 列名: 允许取值 }，含 "" 表示允许留空。
const ENUMS := {
	"attr_to_stat": {"curve": ["linear", "diminishing", "piecewise"]},
	"stat_def": {"value_kind": ["int", "float"]},
	"damage_type": {
		"category": ["instant", "dot"],
		"tick_timing": ["immediate", "turn_end"],
	},
	"element_counter": {"element_atk": ELEMENTS, "element_def": ELEMENTS},
	"weapon_type_def": {"default_element": ELEMENTS},
	"event_check": {
		"check_type": ["hard", "soft"],
		# 设计 06 允许 7 种：item／equip／skillbook／room／boss／event／none。
		# 这里以前只留了 4 种（none／item／room／event），于是「给判定配一条 equip 奖励」
		# 这种 06 允许、PS1 也允许、EventCheckService 也实现的写法，**会被构建期校验器拒掉**——
		# 2026-10-03 才发现（见框架说明决策 87），跑这条用例的就是「文档枚举 vs 代码枚举」那条门限。
		"reward_type": ["none", "item", "equip", "skillbook", "room", "boss", "event"],
	},
	"skill_base": {
		"skill_kind": ["active", "passive"],
		"source_type": ["start", "npc", "drop", "story", "hidden", "item", "shop"],
	},
	"skill_active": {
		"element": ["", "external", "internal", "odd"],
		"target_type": ["single", "self", "all_enemy"],
	},
	# 槽位不写死：equip_base.slot 与 affix_pool.allow_slots 走 equip_slot_def 的动态引用
	"equip_base": {"element": ["", "external", "internal", "odd"]},
	"affix_pool": {"value_kind": ["attr_point", "flat", "rate"]},
	"item_base": {"item_type": ITEM_TYPES, "use_context": ["", "field", "battle"]},
	# `service` 是 0.10.0 加的（`bld_dummy` 木桩：按 E 进练习战）——设计 09 的正文写作
	# `interactive`，而数据里是 `service`（沿用医馆那套 service_id／service_price 列）。
	# 这里**以数据为准**接受 `service`，两者的叫法差异记进 `待策划确认.md`。
	"building_def": {"building_type": ["shop", "service"]},
	# `training`：设计 0.14.0 为「练功木桩」加的新阵营（中性模板、不还手、无掉落）。
	# 06 没有列过 faction 的枚举，所以这里就是唯一出处（改数据要同时改这一行）。
	"enemy_base": {"faction": ["beast", "neutral", "bandit", "rogue", "hidden", "training"], "threat_tag": THREAT_TAGS},
	"enemy_team": {"threat_tag": THREAT_TAGS},
	"drop_table": {"item_type": DROP_ITEM_TYPES, "roll_type": ["independent", "exclusive"]},
	"map_region": {"node_type": ["town", "fast_travel", "wild", "dungeon", "poi"]},
	"map_local": {"scene_type": ["town", "dungeon", "poi"]},
	"roaming_spawn": {"behavior": ["idle", "patrol", "wander", "chase", "sleep"]},
	"dungeon_room": {
		"room_type": ["entrance", "battle", "treasure", "trap", "secret", "elite", "story", "boss"],
		"branch_group": ["", "main", "side", "hidden"],
	},
	"hidden_trigger": {
		"trigger_type": ["item", "space", "sequence", "behavior", "kill_style", "completion", "carry"],
		"reward_type": ["boss", "equip", "skillbook", "room", "event"],
	},
	# 增益与套装（0.7.0 的表；2026-10-03 补进**构建期**这一侧——之前只有 PS1 那一半在查，
	# 于是表写错要等到跑验收才报，见框架说明决策 218）。
	# 注意 `buff_def.is_debuff` 是 **bool 列**（`str(row.get())` 得到 true/false，与 CSV 里的 0/1 不同名），
	# 所以它不在 ENUMS 里——那一条由 PS1 按原始 CSV 文本查，理由写在 `_check_buff_tables()` 的注释里。
	"buff_def": {
		"scope": ["battle", "field"],
		"stack_rule": ["refresh", "stack", "unique"],
		"effect_kind": ["stat", "computed", "special"],
	},
	"buff_grant": {
		"source_type": ["skill_active", "skill_passive", "equip", "set", "event", "item"],
		"trigger": ["on_equip", "on_battle_start", "on_cast", "on_hit", "on_consume"],
	},
	"set_def": {"set_kind": ["equip", "skill_active", "skill_passive"]},
	"feature_toggle": {"value": ["0", "1"]},
}

## 单值 / 多值引用规则：{table, column, target, target_column?, split?, allow_empty?}
const REFERENCES := [
	# 角色与属性
	{"table": "attr_to_stat", "column": "attr_id", "target": "attribute_def"},
	{"table": "attr_to_stat", "column": "stat_id", "target": "stat_def"},
	{"table": "attr_to_stat", "column": "curve", "target": "curve_def"},
	{"table": "character_base", "column": "start_skill_ids", "target": "skill_base", "split": ";", "allow_empty": true},
	{"table": "character_base", "column": "weapon_type", "target": "weapon_type_def"},
	{"table": "character_base", "column": "start_equip_ids", "target": "equip_base", "split": ";", "allow_empty": true},
	{"table": "character_base_skill", "column": "char_id", "target": "character_base"},
	{"table": "character_base_skill", "column": "skill_id", "target": "event_skill_def"},
	{"table": "event_skill_def", "column": "related_attr", "target": "attribute_def"},
	{"table": "event_check", "column": "region_id", "target": "map_region", "allow_empty": true},
	{"table": "event_check", "column": "scene_id", "target": "map_local", "allow_empty": true},
	{"table": "event_check", "column": "room_id", "target": "dungeon_room", "allow_empty": true},
	# 战斗
	{"table": "status_effect", "column": "damage_type", "target": "damage_type"},
	{"table": "status_effect", "column": "trigger_stat", "target": "attribute_def"},
	{"table": "status_effect", "column": "power_stat", "target": "attribute_def"},
	# skill_base 0.6.0 起只放身份与门槛：伤害/消耗在 skill_active，内功在 skill_passive
	{"table": "skill_base", "column": "learn_req_attr", "target": "attribute_def", "allow_empty": true},
	{"table": "skill_base", "column": "star", "target": "skill_star_def", "target_column": "star"},
	{"table": "skill_active", "column": "skill_id", "target": "skill_base"},
	{"table": "skill_active", "column": "damage_type", "target": "damage_type", "allow_empty": true},
	{"table": "skill_active", "column": "status_id", "target": "status_effect", "allow_empty": true},
	{"table": "skill_passive", "column": "skill_id", "target": "skill_base"},
	{"table": "skill_passive_stat", "column": "skill_id", "target": "skill_passive"},
	# 装备与物品
	{"table": "equip_base", "column": "rarity", "target": "rarity_def"},
	{"table": "equip_base", "column": "slot", "target": "equip_slot_def"},
	{"table": "equip_base", "column": "weapon_type", "target": "weapon_type_def", "allow_empty": true},
	{"table": "affix_pool", "column": "allow_slots", "target": "equip_slot_def", "split": "|"},
	{"table": "equip_slot_def", "column": "allow_weapon_types", "target": "weapon_type_def", "split": "|", "allow_empty": true},
	{"table": "weapon_type_def", "column": "attr_focus", "target": "attribute_def", "split": "|", "allow_empty": true},
	{"table": "weapon_type_def", "column": "skill_school", "target": "skill_base", "target_column": "school_id", "allow_empty": true},
	# 商店与经济
	# 0.10.0 起 building_def 里有非店铺行（木桩 `service`），所以这里允许空——
	# 「店铺必须有货架」改由 `_check_building_rows` 单独管（原来的必填规则会把木桩当场拒掉）
	{"table": "building_def", "column": "stock_group", "target": "shop_stock", "target_column": "shop_id", "allow_empty": true},
	{"table": "shop_stock", "column": "shop_id", "target": "building_def", "target_column": "stock_group"},
	{"table": "affix_pool", "column": "min_rarity", "target": "rarity_def"},
	{"table": "item_base", "column": "rarity", "target": "rarity_def"},
	# 敌人与掉落
	# `drop_group` 允许为空：设计 0.14.0 的练功木桩按「无掉落」配（`faction=training`）。
	# 空值这条口子由下面 `_check_training_faction` 收口——**非训练敌人仍然必须有掉落组**。
	{"table": "enemy_base", "column": "drop_group", "target": "drop_table", "target_column": "drop_group", "allow_empty": true},
	{"table": "enemy_skill", "column": "enemy_id", "target": "enemy_base"},
	# 设计 10 §一：`skill_id` 引用 `skill_base`（数值走 `skill_active`）
	{"table": "enemy_skill", "column": "skill_id", "target": "skill_base"},
	# 0.11.1：敌人装备——槽位与装备都跟角色同一套表
	{"table": "enemy_equip", "column": "enemy_id", "target": "enemy_base"},
	{"table": "enemy_equip", "column": "slot_id", "target": "equip_slot_def"},
	{"table": "enemy_equip", "column": "equip_id", "target": "equip_base"},
	{"table": "enemy_passive", "column": "enemy_id", "target": "enemy_base"},
	{"table": "enemy_passive", "column": "skill_id", "target": "skill_passive"},
	{"table": "difficulty_drop_rate", "column": "difficulty_id", "target": "difficulty_config"},
	{"table": "difficulty_drop_rate", "column": "rarity_id", "target": "rarity_def"},
	# 地图与副本
	{"table": "map_region", "column": "enter_scene", "target": "map_local", "allow_empty": true},
	{"table": "map_local", "column": "parent_node", "target": "map_region", "allow_empty": true},
	{"table": "roaming_spawn", "column": "region_id", "target": "map_region"},
	{"table": "roaming_spawn", "column": "team_id", "target": "enemy_team"},
	{"table": "dungeon_room", "column": "scene_id", "target": "map_local"},
	{"table": "dungeon_room", "column": "enemy_team", "target": "enemy_team", "allow_empty": true},
	{"table": "dungeon_room", "column": "chest_id", "target": "drop_table", "target_column": "drop_group", "allow_empty": true},
	{"table": "dungeon_room", "column": "hidden_trigger", "target": "hidden_trigger", "allow_empty": true},
	{"table": "dungeon_room", "column": "exit_rooms", "target": "dungeon_room", "split": "|", "allow_empty": true},
	{"table": "hidden_trigger", "column": "scene_id", "target": "map_local"},
	{"table": "hidden_trigger", "column": "room_id", "target": "dungeon_room", "allow_empty": true},
	{"table": "hidden_trigger", "column": "required_item", "target": "item_base", "allow_empty": true},
	# 引导／招募／宝箱守护者（0.10.0 新增，见 docs/design/09_试玩修正.md）
	{"table": "recruit_def", "column": "char_id", "target": "character_base"},
	{"table": "npc_guard", "column": "room_id", "target": "dungeon_room"},
	{"table": "npc_guard", "column": "fight_team", "target": "enemy_team", "allow_empty": true},
]

## 数值区间规则。
const RANGES := [
	{"table": "attr_to_stat", "column": "rate", "min": 0.0},
	{"table": "event_skill_def", "column": "max_level", "min": 1.0},
	{"table": "event_check", "column": "difficulty", "min": 0.0},
	{"table": "character_base", "column": "attr_total", "min": 0.0},
	{"table": "character_base_skill", "column": "level", "min": 0.0},
	{"table": "equip_slot_def", "column": "max_equip", "min": 1.0},
	{"table": "equip_slot_def", "column": "sort_order", "min": 1.0},
	{"table": "building_def", "column": "service_price", "min": 0.0},
	{"table": "shop_stock", "column": "sort_order", "min": 1.0},
	{"table": "shop_stock", "column": "buy_price", "min": 1.0},
	{"table": "shop_stock", "column": "sell_price", "min": 0.0},
	{"table": "shop_stock", "column": "stock_limit", "min": 0.0},
	{"table": "skill_active", "column": "power_ratio", "min": 0.0},
	{"table": "skill_active", "column": "qi_cost", "min": 0.0},
	{"table": "skill_active", "column": "hit_count", "min": 0.0},
	{"table": "skill_active", "column": "cooldown", "min": 0.0},
	# 负的 poise_damage 会被 `take_poise_damage` 的 `maxi(amount, 0)` 静默吃掉——
	# 招式看着「破架势 20」，实际一点都破不了，还不报错。
	{"table": "skill_active", "column": "poise_damage", "min": 0.0},
	# stack_max = 0 会被 `maxi(1, …)` 当成 1（设计写 0 想要「不可堆叠」也是 1），负值更没意义。
	{"table": "item_base", "column": "stack_max", "min": 1.0},
	# 负的 status_chance_mul：异常几率会算成负数，掷点永远掷不中——招式看着能上毒、实际一次都上不了。
	{"table": "skill_active", "column": "status_chance_mul", "min": 0.0},
	# 负的经验门槛：等级会一路跳到上限（LevelService 有守卫不会死循环，但结果同样荒谬）。
	{"table": "level_growth", "column": "exp_to_next", "min": 0.0},
	# 负的加点：`available_points` 直接变负，面板上「未分配点数：-3」。
	{"table": "level_growth", "column": "upgrade_points", "min": 0.0},
	{"table": "skill_passive", "column": "slot_cost", "min": 1.0, "max": 3.0},
	{"table": "skill_star_def", "column": "mastery_gain", "min": 0.0001},
	{"table": "skill_star_def", "column": "cultivate_cost_base", "min": 1.0},
	{"table": "character_base", "column": "initial_str", "min": 0.0},
	{"table": "character_base", "column": "initial_con", "min": 0.0},
	{"table": "character_base", "column": "initial_agi", "min": 0.0},
	{"table": "character_base", "column": "initial_int", "min": 0.0},
	{"table": "character_base", "column": "initial_luk", "min": 0.0},
	{"table": "affix_pool", "column": "weight", "min": 1.0},
	{"table": "drop_table", "column": "base_rate", "min": 0.0, "max": 1.0},
	{"table": "drop_table", "column": "slot", "min": 1.0},
	{"table": "drop_table", "column": "qty_min", "min": 0.0},
	{"table": "difficulty_drop_rate", "column": "rate_multiplier", "min": 0.0},
	# 难度倍率必须**严格为正**：`enemy_hp_mul = 0` 会让敌人一出生就是 0 血（直接判负、白拿奖励），
	# `exp_mul = 0` 会让该难度一点经验都不给，负值更是反向。0.0001 是「> 0」的既有写法。
	{"table": "difficulty_config", "column": "enemy_hp_mul", "min": 0.0001},
	{"table": "difficulty_config", "column": "enemy_atk_mul", "min": 0.0001},
	{"table": "difficulty_config", "column": "enemy_def_mul", "min": 0.0001},
	{"table": "difficulty_config", "column": "exp_mul", "min": 0.0001},
	{"table": "difficulty_config", "column": "money_mul", "min": 0.0001},
	{"table": "enemy_base", "column": "level", "min": 1.0},
	{"table": "enemy_base", "column": "hp_base", "min": 1.0},
	# 敌人战斗数值：负的攻击/防御会让伤害公式反向（`def/(def+常数)` 为负 → 减免变加伤），
	# 比率类超过 1 也没有意义（命中 1.5、闪避 2.0、抗性 1.5 都是数据写错）。
	{"table": "enemy_base", "column": "atk_phys", "min": 0.0},
	{"table": "enemy_base", "column": "atk_qi", "min": 0.0},
	{"table": "enemy_base", "column": "def_phys", "min": 0.0},
	{"table": "enemy_base", "column": "def_qi", "min": 0.0},
	{"table": "enemy_base", "column": "speed", "min": 0.0},
	{"table": "enemy_base", "column": "poise", "min": 0.0},
	{"table": "enemy_base", "column": "hit_rate", "min": 0.0, "max": 1.0},
	{"table": "enemy_base", "column": "dodge_rate", "min": 0.0, "max": 1.0},
	{"table": "enemy_base", "column": "crit_rate", "min": 0.0, "max": 1.0},
	# 0.14.0：毒/火/流血三条特权列取消了（设计 10 §二）——敌人要抗性就装内功；
	# 内伤抗性这一列**保留**（角色侧也有 stat_def.res_internal）。
	{"table": "enemy_base", "column": "res_internal", "min": 0.0, "max": 1.0},
	# 七维（与 character_base 同名同义）：非负；总和不做硬性要求（敌人不像角色卡那样定死 49）
	{"table": "enemy_base", "column": "attr_str", "min": 0.0},
	{"table": "enemy_base", "column": "attr_con", "min": 0.0},
	{"table": "enemy_base", "column": "attr_agi", "min": 0.0},
	{"table": "enemy_base", "column": "attr_int", "min": 0.0},
	{"table": "enemy_base", "column": "attr_luk", "min": 0.0},
	{"table": "enemy_base", "column": "attr_wu", "min": 0.0},
	{"table": "enemy_base", "column": "attr_gen", "min": 0.0},
	{"table": "roaming_spawn", "column": "alert_radius", "min": 0.0},
	{"table": "roaming_spawn", "column": "chase_speed", "min": 0.0},
	{"table": "roaming_spawn", "column": "respawn_sec", "min": 0.0},
	{"table": "map_region", "column": "pos_x", "min": 0.0},
	{"table": "map_region", "column": "pos_y", "min": 0.0},
	# 楼层号从 1 起：0 或负数会让完成度面板显示「第 0 层」，而分层/扫荡都按它分组。
	{"table": "dungeon_room", "column": "floor", "min": 1.0},
	# 2026-10-03 用变异探针扫出来的五条（两边的网都没盖住，见框架说明决策 157）：
	# 层数/持续为 0 的状态**永远不生效**（`mini(max_stack, …)` 直接压成 0 层、0 回合直接过期），
	# 触发率超过 1 是无意义的比率，负的售价与铜钱等于买东西倒贴/打赢反而亏钱。
	{"table": "status_effect", "column": "max_stack", "min": 1.0},
	{"table": "status_effect", "column": "duration", "min": 1.0},
	{"table": "status_effect", "column": "base_chance", "min": 0.0, "max": 1.0},
	# 0 有意义（＝不回收，店里不收），但不能为负。
	{"table": "item_base", "column": "sell_price", "min": 0.0},
	{"table": "enemy_base", "column": "money", "min": 0.0},
]

## `level_growth` 的基础数值列：升级不该让人变弱，这些列随等级**不许下降**（允许持平）。
const LEVEL_BASE_COLUMNS := [
	"base_hp", "base_qi", "base_atk_phys", "base_atk_qi",
	"base_def_phys", "base_def_qi", "base_speed",
]

## 关系规则：left <= right。
const RELATIONS := [
	{"table": "affix_pool", "left": "value_min", "right": "value_max", "label": "词条区间"},
	{"table": "drop_table", "left": "qty_min", "right": "qty_max", "label": "掉落数量区间"},
	{"table": "stat_def", "left": "min_value", "right": "max_value", "label": "派生数值上下限"},
	# 稀有度 → 词条条数区间：05 写的是「0–1／1–2／2–3…」，写成 min>max 不会炸——
	# `AffixRoller.affix_count()` 里有一句 `maxi(low, high)` 兜着，结果是**每件都只出 min 条**
	# （看着正常、分布却没了）。所以在这里按区间比一次。
	{"table": "rarity_def", "left": "affix_min", "right": "affix_max", "label": "词条数量区间"},
]

## 九步管线里有四个比率**必须**有上限，代码取的是 `stat_def.max_value`
## （`DamageResolver._stat_cap`）。而这张表的约定是「max_value 留空 = 0 = 无上限」——
## 那时代码会**静默换用兜底常数**（穿透/格挡 0.75、格挡减伤 0.8、减伤 0.6），
## 表里写着「无上限」、跑起来却是 0.75，**没有任何地方会红**（2026-10-03 查兜底常数时发现，决策 155）。
## 所以把这四个「必须写上上限」做成门限：真要改成无上限，得同时改管线，不能在表里留空。
## `res_internal` 是 2026-10-03 补进来的：它是管线里最后一个"表里有上限、代码却写死 0.75"的比率，
## 现在代码也读表了，于是它同样必须写上上限（见框架说明决策 180）。
const REQUIRED_CAPPED_STATS := ["pen_rate", "block_rate", "block_reduction", "dmg_reduction", "res_internal"]


static func validate(db) -> PackedStringArray:
	var errors := PackedStringArray()
	for message: String in db.errors:
		errors.append(message)
	if not errors.is_empty():
		# 缺表时引用校验会刷屏，直接返回加载错误
		return errors
	_check_primary_keys(db, errors)
	_check_enums(db, errors)
	_check_status_rules(db, errors)
	_check_trigger_conditions(db, errors)
	_check_ai_templates(db, errors)
	_check_references(db, errors)
	_check_drop_item_refs(db, errors)
	_check_hidden_reward_refs(db, errors)
	_check_enemy_team_members(db, errors)
	_check_training_faction(db, errors)
	_check_enemy_skill_slots(db, errors)
	_check_character_attr_total(db, errors)
	_check_attribute_allocatable(db, errors)
	_check_level_growth_rows(db, errors)
	_check_equip_level_reqs(db, errors)
	_check_dungeon_floors(db, errors)
	_check_event_sources(db, errors)
	_check_event_rewards(db, errors)
	_check_event_skill_levels(db, errors)
	_check_shop_item_refs(db, errors)
	_check_shop_prices(db, errors)
	_check_building_rows(db, errors)
	_check_guide_and_recruit(db, errors)
	_check_npc_guards(db, errors)
	_check_weapon_slot_consistency(db, errors)
	_check_skill_details(db, errors)
	_check_skill_weapon_types(db, errors)
	_check_passive_stats(db, errors)
	_check_affix_targets(db, errors)
	_check_equip_bonuses(db, errors)
	_check_growth_constants(db, errors)
	_check_star_def(db, errors)
	_check_ranges(db, errors)
	_check_relations(db, errors)
	_check_pipeline_caps(db, errors)
	_check_diminishing(db, errors)
	_check_start_skills_usable(db, errors)
	_check_start_equipment_usable(db, errors)
	_check_difficulty_ladder(db, errors)
	_check_difficulty_unlock_conditions(db, errors)
	_check_growth_constants_present(db, errors)
	_check_growth_const_ranges(db, errors)
	_check_locked_scenes_exist(db, errors)
	_check_buff_tables(db, errors)
	_check_character_event_skills(db, errors)
	_check_skill_damage_types_supported(db, errors)
	return errors


## 非战斗技能（01 文档的「每个模板给出全部非战斗技能的初始等级」）：
## 漏一行**不会报错**——`CharacterSheet.event_skill_level()` 找不到就返回 0，于是
## 「忘了配」和「故意配 0 级」长得一模一样，判定值静默变低。PS1 的 §5.10 一直是硬错误，
## 构建期这一侧到 2026-10-03 才补上（见框架说明决策 219）。
##
## 上限**不写死 10**：按 `event_skill_def.max_level` 逐条查（那份才是这一列的唯一出处，
## PS1 那边写的是 0~10 字面量——两处数据一致，但这里不抄第二份）。
static func _check_character_event_skills(db, errors: PackedStringArray) -> void:
	var max_level_of: Dictionary = {}
	for row: Resource in db.rows("event_skill_def"):
		max_level_of[str(row.skill_id)] = int(row.max_level)
	var covered_by_char: Dictionary = {}
	for row: Resource in db.rows("character_base_skill"):
		var char_id := str(row.char_id)
		var skill_id := str(row.skill_id)
		if not covered_by_char.has(char_id):
			covered_by_char[char_id] = {}
		covered_by_char[char_id][skill_id] = true
		if max_level_of.has(skill_id):
			var cap := int(max_level_of[skill_id])
			var level := int(row.level)
			if level > cap:
				errors.append("character_base_skill[%s/%s].level=%d 超过这部非战斗技能的上限 %d（event_skill_def.max_level）" % [
					char_id, skill_id, level, cap,
				])
	for char_row: Resource in db.rows("character_base"):
		var char_id := str(char_row.char_id)
		var covered: Dictionary = covered_by_char.get(char_id, {})
		for skill_row: Resource in db.rows("event_skill_def"):
			var skill_id := str(skill_row.skill_id)
			if not covered.has(skill_id):
				errors.append("角色 %s 缺少非战斗技能 %s 的初始等级（漏一行＝静默按 0 级算）" % [char_id, skill_id])


## 招式用的伤害类型，代码必须真的支持——否则那招**永远没有按钮**（能学会、能装上、点不出来）。
## 由来（2026-10-03，决策 228／229）：`_is_supported_damage()` 里一条 DoT 里程碑之前的遗留
## 把 `category=dot` 一律挡掉，五毒掌／烈火掌那 6 招因此没有按钮；而**用例是直接 `sim.act()` 放招**的，
## 绕过了这道过滤，所以「管线对不对」绿着、「玩家点不点得到」坏着，一直没人发现。
## 这条就是把「代码支不支持」与「表里配了什么」对起来：唯一的支持名单在 BattleSimulator 里，
## 这里只**读**它，不抄第二份。
static func _check_skill_damage_types_supported(db, errors: PackedStringArray) -> void:
	var reported: Dictionary = {}
	for row: Resource in db.rows("skill_active"):
		var type_id := str(row.damage_type)
		if type_id.is_empty():
			continue       # 增益类招式本来就该留空（它靠 buff_grant 的 on_cast 生效）
		if BattleSimulatorScript.is_supported_damage_type(db, type_id):
			continue
		if reported.has(type_id):
			continue       # 同一个类型报一条就够，别刷屏
		reported[type_id] = true
		errors.append(
			"招式 %s 的 damage_type='%s' 代码不支持——它在战斗里永远没有按钮（能学会、能装上、点不出来）"
				% [str(row.skill_id), type_id]
		)


## 属性加点：`attribute_def.allocatable` 必须是 0 或 1，而且**至少留一个可加点属性**——
## 设计 0.13.0 把悟性／根骨锁成「资质」（创建时定死、只能靠图鉴奖励与内功提升）之后，
## 每级 5 点要是无处可加，玩家会看到一个永远点不动的加点界面。
## 枚举合法性由行类读不出原文（`allocatable=2` 会被 `int()` 读成 2，这一条拦得住），
## 「至少一个可加点」是第二条。
static func _check_attribute_allocatable(db, errors: PackedStringArray) -> void:
	var allocatable := 0
	for row: Resource in db.rows("attribute_def"):
		var value := int(row.allocatable)
		if value != 0 and value != 1:
			errors.append("attribute_def[%s].allocatable=%d，只允许 0（资质，不可加点）或 1（可加点）" % [
				str(row.attr_id), value,
			])
			continue
		if value == 1:
			allocatable += 1
	if allocatable == 0:
		errors.append("attribute_def 里没有一个 allocatable=1 的属性：每级的自由点数将无处可加")


## 增益与套装（08）：把 PS1 那一半 5.16／5.17 里**构建期查得动**的规则镜像过来。
##
## 为什么要有这一份：`buff_def.scope` 写错、`buff_grant.source_id` 指向不存在的内功、
## 套装档位永远凑不齐……这些以前**只有 PS1 那一半会报**，也就是「跑验收才发现」，
## 而构建期校验器一跑就绿。两张网各查各的可以，但**不能一边完全没有**——
## 0.7.0 的六张表加进来时只登记了表、没补规则（2026-10-03 才补，见框架说明决策 218）。
##
## 有一处**故意不镜像**：`buff_def.is_debuff` 在行类里是 bool（`str(row.get())` 得 true/false），
## CSV 里的 0/1 到这一层已经看不出区别（`is_debuff=2` 会被读成 true）——那一条只有直接读
## CSV 文本的 PS1 拦得住，这里如实说明，不假装查过。
static func _check_buff_tables(db, errors: PackedStringArray) -> void:
	_check_buff_stats(db, errors)
	_check_buff_defs(db, errors)
	_check_buff_grants(db, errors)
	_check_sets(db, errors)


## buff_stat：target 前缀与引用（`attr:` 查 attribute_def、`stat:` 查 stat_def）
static func _check_buff_stats(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("buff_stat"):
		var target := str(row.target)
		var parsed := _parse_prefixed_target(target)
		if parsed.is_empty():
			errors.append("buff_stat[%s].target='%s' 缺少 attr: 或 stat: 前缀" % [row.id, target])
			continue
		var table_name := "attribute_def" if str(parsed["kind"]) == "attr" else "stat_def"
		var id_column := "attr_id" if str(parsed["kind"]) == "attr" else "stat_id"
		if not _column_values(db, table_name, id_column).has(str(parsed["target_id"])):
			errors.append("buff_stat[%s].target='%s' 引用的%s不存在" % [
				row.id, target, "属性" if str(parsed["kind"]) == "attr" else "派生数值",
			])


## buff_def：数值型必须有加成、duration 形态、可叠层的 max_stack ≥ 2
static func _check_buff_defs(db, errors: PackedStringArray) -> void:
	var mods_by_buff: Dictionary = {}
	for row: Resource in db.rows("buff_stat"):
		mods_by_buff[str(row.buff_id)] = int(mods_by_buff.get(str(row.buff_id), 0)) + 1
	for row: Resource in db.rows("buff_def"):
		var buff_id := str(row.id)
		var effect_kind := str(row.effect_kind)
		if effect_kind == "stat" and int(mods_by_buff.get(buff_id, 0)) < 1:
			errors.append("buff_def[%s] 是数值型（effect_kind=stat），但 buff_stat 里一条加成都没有（装上等于没装）" % buff_id)
		var duration := int(row.duration)
		if duration < 0 and duration != -1:
			errors.append("buff_def[%s].duration=%d 只允许 -1（整场）或非负" % [buff_id, duration])
		if str(row.stack_rule) == "stack" and int(row.max_stack) < 2:
			errors.append("buff_def[%s] 是可叠层 buff（stack_rule=stack），max_stack=%d 必须 ≥ 2" % [
				buff_id, int(row.max_stack),
			])


## buff_grant：source_id 必须真实存在（按 source_type 分流）；
## 每部内功都要有一条 `on_cast`——没有的话「内功」指令点它是没有反应的。
static func _check_buff_grants(db, errors: PackedStringArray) -> void:
	var source_tables := {
		"skill_active": ["skill_active", "skill_id"],
		"skill_passive": ["skill_passive", "skill_id"],
		"equip": ["equip_base", "equip_id"],
		"set": ["set_def", "set_id"],
		"event": ["event_check", "check_id"],
		"item": ["item_base", "item_id"],
	}
	var castable: Dictionary = {}
	for row: Resource in db.rows("buff_grant"):
		var source_type := str(row.source_type)
		var source_id := str(row.source_id)
		# **这一行到底会不会触发**：`(source_type, trigger)` 必须是代码真的会读的组合。
		# 只查 id 存在是不够的——写一个没人读的时机，结果就是「配了却不发」而各处都绿（2026-10-03 补）。
		var fired: Array = BUFF_TRIGGER_COMBOS.get(source_type, [])
		if not fired.has(str(row.trigger)):
			errors.append(
				"buff_grant[%s]：source_type=%s 配 trigger=%s 这一行**永远不会触发**（代码会读的组合：%s）——"
				% [row.id, source_type, str(row.trigger), _combo_text(source_type)]
				+ "要加新时机得先在战斗侧接上，别只写表"
			)
		if not source_tables.has(source_type):
			continue       # 非法 source_type 由 ENUMS 报，这里不重复刷屏
		var spec: Array = source_tables[source_type]
		if not _column_values(db, str(spec[0]), str(spec[1])).has(source_id):
			errors.append("buff_grant[%s].source_id='%s' 在 %s 里不存在（发了也没人认）" % [
				row.id, source_id, str(spec[0]),
			])
		if source_type == "skill_passive" and str(row.trigger) == "on_cast":
			castable[source_id] = true
	for row: Resource in db.rows("skill_passive"):
		var passive_id := str(row.skill_id)
		if not castable.has(passive_id):
			errors.append("内功 %s 没有 on_cast 的 buff_grant，内功指令点它没有反应" % passive_id)


## 代码**真的会读**的 `(source_type, trigger)` 组合（唯一出处就是这张表，别在别处再抄一份）。
##
## 加新时机的顺序：先在战斗侧接上触发点，再往这里加一行——否则表里配了也不会发，
## 而「配了却不发」正是这套校验想拦住的那类坑。
## 现在没接、故意留空的：`event`（06 的 trigger 列表里没有 event 那一档，等设计）、
## `item + on_consume`（道具使用要等 `item_base` 效果列，见 `待策划确认.md` Q33）。
const BUFF_TRIGGER_COMBOS := {
	"skill_passive": ["on_cast"],                                  # 内功催动（六指令之一）
	"skill_active": ["on_cast", "on_hit"],                         # 招式：施放 / 命中（都按「一次行动/一段」）
	"equip": ["on_equip", "on_battle_start", "on_hit"],            # 装备：装备即生效 / 进场 / 命中
	"set": ["on_battle_start"],                                    # 套装档位：走贡献通道（常驻）
}


static func _combo_text(source_type: String) -> String:
	var fired: Array = BUFF_TRIGGER_COMBOS.get(source_type, [])
	if fired.is_empty():
		return "%s 这一档还没有任何触发点" % source_type
	return ", ".join(PackedStringArray(fired))


## 套装：成员类型要和 set_kind 对得上；档位必须是正数、且**够得着**（内功套按占格数之和）
static func _check_sets(db, errors: PackedStringArray) -> void:
	var kind_tables := {
		"equip": ["equip_base", "equip_id"],
		"skill_active": ["skill_active", "skill_id"],
		"skill_passive": ["skill_passive", "skill_id"],
	}
	var kind_of: Dictionary = {}
	var members_of: Dictionary = {}
	for row: Resource in db.rows("set_def"):
		var set_id := str(row.set_id)
		kind_of[set_id] = str(row.set_kind)
		members_of[set_id] = 0
	var slot_cost: Dictionary = {}
	for row: Resource in db.rows("skill_passive"):
		slot_cost[str(row.skill_id)] = int(row.slot_cost)
	var reachable: Dictionary = {}
	for row: Resource in db.rows("set_member"):
		var set_id := str(row.set_id)
		var member_id := str(row.member_id)
		var kind := str(kind_of.get(set_id, ""))
		if not kind_tables.has(kind):
			continue       # 非法 set_kind 由 ENUMS 报
		var spec: Array = kind_tables[kind]
		if not _column_values(db, str(spec[0]), str(spec[1])).has(member_id):
			errors.append("set_member[%s] 的成员 '%s' 不是合法的 %s" % [set_id, member_id, kind])
		members_of[set_id] = int(members_of.get(set_id, 0)) + 1
		# 内功套按**占格数之和**计（与 SetService.count_for 同一口径）
		var step := 1
		if kind == "skill_passive" and slot_cost.has(member_id):
			step = maxi(1, int(slot_cost[member_id]))
		reachable[set_id] = int(reachable.get(set_id, 0)) + step
	for row: Resource in db.rows("set_bonus"):
		var set_id := str(row.set_id)
		var tier := int(row.required_count)
		if tier < 1:
			errors.append("set_bonus[%s].required_count=%d 应为正整数" % [set_id, tier])
			continue
		var top := int(reachable.get(set_id, 0))
		if tier > top:
			var unit := "格" if str(kind_of.get(set_id, "")) == "skill_passive" else "件"
			errors.append("set_bonus[%s] 要求 %d %s，但成员最多只到 %d %s，永远凑不齐" % [
				set_id, tier, unit, top, unit,
			])
	for set_id: String in kind_of:
		if int(members_of.get(set_id, 0)) < 1:
			errors.append("套装 %s 一个成员都没有" % set_id)


## `attr:x` / `stat:x` → {kind, target_id}；没有前缀返回空字典
static func _parse_prefixed_target(target: String) -> Dictionary:
	if target.begins_with("attr:"):
		return {"kind": "attr", "target_id": target.substr(5)}
	if target.begins_with("stat:"):
		return {"kind": "stat", "target_id": target.substr(5)}
	return {}


## 某张表某一列的取值集合（引用校验用；与 `TableDb.column_values` 同一口径）
static func _column_values(db, table_name: String, column: String) -> Dictionary:
	var out: Dictionary = {}
	var rows: Array = db.rows(table_name)
	if rows.is_empty():
		return out
	var property := Registry.property_of(table_name, column)
	for row: Resource in rows:
		out[str(row.get(property))] = true
	return out


## 代码里那份「本章不开放的小地图」清单（`WorldMapService.LOCKED_SCENES`）里的每个 scene_id，
## 都必须是 `map_local` 里真实存在的一行。
##
## 由来（2026-10-03）：这份清单只有一处定义（single-source 门限盯着"别抄第二份"），
## 但**内容没人核**——把 `scene_ferry_locked` 打成一个字母（`scene_ferry_lockd`），
## `LOCKED_SCENES.has(...)` 永远为 false，废弃渡口**静默变成可进入**（进去是一张空图），
## 而所有用例照样绿。这和 `enemy_base.ai_template`／`difficulty_config.unlock_condition`
## 是同一类：**代码里硬编码的 id 清单，必须对得上表**。
static func _check_locked_scenes_exist(db, errors: PackedStringArray) -> void:
	var known: Dictionary = {}
	for row: Resource in db.rows("map_local"):
		known[str(row.scene_id)] = true
	for scene_id: String in WorldMapServiceScript.LOCKED_SCENES:
		if not known.has(scene_id):
			errors.append(
				"WorldMapService.LOCKED_SCENES 里的 '%s' 在 map_local 里不存在：锁了个不存在的场景 = 什么都没锁住"
				% scene_id
			)


## 成长常数里**有语义下限**的几个：下限值不是我编的，是**代码里的守卫**——
## 写了更小的值会被静默替换成 1.0／1／0，也就是"表里写的数根本没用上"（155／157／165 同一家族）。
##
## - 四个除数：`GrowthCalculator` 里都是 `maxf(constant(...), 1.0)` → 写了 0 或 0.5 会被当成 1.0，
##   容量曲线整条变样却没有任何提示；
## - `cultivate_cost_growth`：直接进 `pow(底数, 等级-1)`。**负数底数会算出负费用**
##   （打坐反而给你钱），没有守卫；
## - `codex_step`／`codex_cap`／`mastery_max`：代码里分别有 `maxi(1, ...)`／`maxi(0, ...)`／
##   `clampi(..., 1, maxi(1, ...))`，写小了同样是被静默换掉。
const GROWTH_CONST_FLOORS := {
	"active_slot_lv_div": [1.0, "代码里是 maxf(值, 1.0)：小于 1 会被当成 1.0"],
	"active_slot_attr_div": [1.0, "同上（悟性除数）"],
	"passive_cap_lv_div": [1.0, "同上（内功容量等级除数）"],
	"passive_cap_attr_div": [1.0, "同上（根骨除数）"],
	"cultivate_cost_growth": [0.0001, "它是 pow() 的底数：负数会算出负费用（打坐给你钱）"],
	"codex_step": [1.0, "代码里是 maxi(1, 值)"],
	"codex_cap": [0.0, "代码里是 maxi(0, 值)"],
	"mastery_max": [1.0, "代码里是 clampi(等级, 1, maxi(1, 值))"],
}


static func _check_growth_const_ranges(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("growth_const"):
		var const_id := str(row.const_id)
		if not GROWTH_CONST_FLOORS.has(const_id):
			continue
		var floor_value: float = float(GROWTH_CONST_FLOORS[const_id][0])
		var hint := str(GROWTH_CONST_FLOORS[const_id][1])
		if float(row.value) < floor_value:
			errors.append(
				"growth_const[%s].value = %s 小于可用下限 %s：%s"
				% [const_id, row.value, floor_value, hint]
			)


## 代码点了名的成长常数，表里必须都有。
##
## 成长系统是「算法在代码、常数在表」，而两边各有一份"缺行兜底值"——**表里漏一行不会崩，
## 只会静默用兜底值**。PS1 那半边有一份手抄的 14 个常数清单，但手抄的东西会漂：
## 代码里新加一个常数而清单忘了加，缺行就又变回静默。所以这里从**代码侧**取清单
## （`GrowthCalculator.DEFAULTS` 的键），表里少一个就报错。
## 战斗常数那边没有集中清单（名字散在 `constant("x", 默认值)` 的调用点里），
## 那颗由 `test_handshake._check_code_constants_exist_in_tables` 扫源码来盯——
## 扫源码的门限放测试里（导出后的包里不一定有 .gd 源码，运行期不该依赖它）。
static func _check_growth_constants_present(db, errors: PackedStringArray) -> void:
	var present: Dictionary = {}
	for row: Resource in db.rows("growth_const"):
		present[str(row.const_id)] = true
	for key: String in GrowthCalculatorScript.DEFAULTS:
		if not present.has(key):
			errors.append(
				"growth_const 缺少代码依赖的常数 '%s'：缺行时 GrowthCalculator 会**静默用兜底值**（%s）"
				% [key, GrowthCalculatorScript.DEFAULTS[key]]
			)


## 难度阶梯：**表里的行顺序就是难度递增顺序**（普通 → 困难 → 绝境，今天的数据就是这样）。
##
## 由来（2026-10-03）：原先只校验了五个倍率「> 0」，于是把困难档的攻击倍率写成 0.9
## （比普通还弱）**没有任何反应**——而它直接决定"更高难度到底是不是更难"。
## 这条与 `level_growth` 的"逐级不许下降"是同一类数据纪律。
const DIFFICULTY_LADDER_COLUMNS := [
	"enemy_hp_mul", "enemy_atk_mul", "enemy_def_mul", "exp_mul", "money_mul",
]


static func _check_difficulty_ladder(db, errors: PackedStringArray) -> void:
	var rows: Array = db.rows("difficulty_config")
	if rows.size() < 2:
		return
	for index in range(1, rows.size()):
		var before: Resource = rows[index - 1]
		var now: Resource = rows[index]
		var enemy_grew := false
		for column: String in DIFFICULTY_LADDER_COLUMNS:
			var low := float(before.get(column))
			var high := float(now.get(column))
			if high < low:
				errors.append(
					"difficulty_config[%s].%s = %s 比上一档（%s，%s）还低：难度越高数值反而越小"
					% [now.id, column, high, before.id, low]
				)
			elif high > low and column.begins_with("enemy_"):
				enemy_grew = true
		if not enemy_grew:
			errors.append(
				"difficulty_config[%s] 的敌人倍率与上一档（%s）完全一样：这一档只是换了个名字"
				% [now.id, before.id]
			)


## 难度解锁条件：`unlock_condition` 是**中文短语**，映射表在 `WorldMapService.DIFFICULTY_RULES`。
## 运行期碰到没登记的短语会 push_error（不静默），但那是玩家点开难度菜单才会发现的；
## 构建期就该拦住——和 `status_effect.extra_rule` 对 `KNOWN_STATUS_RULES` 是同一条纪律。
static func _check_difficulty_unlock_conditions(db, errors: PackedStringArray) -> void:
	var known: Dictionary = WorldMapServiceScript.DIFFICULTY_RULES
	for row: Resource in db.rows("difficulty_config"):
		var condition := str(row.unlock_condition).strip_edges()
		if not known.has(condition):
			errors.append(
				"difficulty_config[%s].unlock_condition = '%s' 没在 WorldMapService.DIFFICULTY_RULES 里登记：这一档永远解锁不了"
				% [row.id, condition]
			)


## 每个角色模板至少要有一部**武器对得上**的起手招式。
##
## 由来（2026-10-03）：把 01 的 4 行备选模板接上去跑战斗自检，整场**卡在第 1 回合打不完**——
## 白清和（内功续航流）的武器是拳（`eq_fist_02` 乌木拳套），而起手武学 `sk_xuanwei_qi_01`
## （玄微心法·一指）要求**剑** → `available_skills()` 永远是空 → 手动打不了这一回合，
## 自检的驱动循环原地打转（「连续 8 步没有任何变化」）。玩家侧的表现是：轮到他时一个招式按钮都没有，
## 只能按自动或撤退。**「造了一个永远出不了手的角色」这种数据错，构建期就该拦住**。
static func _check_start_skills_usable(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("character_base"):
		var weapon := str(row.weapon_type)
		var usable := 0
		for skill_id: String in row.skill_ids():
			var base: Resource = db.get_row("skill_base", skill_id)
			if base == null:
				continue     # 引用的存在性由别的规则报
			if str(base.skill_kind) != "active":
				continue     # 内功不算「打得出手」
			var need := str(base.weapon_type)
			if need.is_empty() or need == weapon:
				usable += 1
		if usable == 0:
			errors.append(
				"character_base[%s] 一部能用的起手招式都没有：模板武器是 '%s'，而 start_skill_ids 里的招式都要别的武器——"
				% [row.id, weapon]
				+ "这样的角色轮到出手时一个按钮都没有（只能按自动/撤退），战斗会卡住"
			)


## 模板的起始装备要**装得上**：武器类型必须与模板一致，等级门槛不能高于起始等级。
##
## 与 `_check_start_skills_usable` 是一对（2026-10-03 补）：起手招式对得上武器只是第一步，
## **起始装备**也得对得上——`GameState.seed_starting_equipment` 穿戴失败时会 push_error，
## 但那要等**开新档那一刻**才响（编辑器里按 F5 才看得到）；构建期就该拦住，
## 否则玩家会拿到一个"空着手、招式也用不了"的角色，正是上一轮踩过的那种死路。
static func _check_start_equipment_usable(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("character_base"):
		var weapon := str(row.weapon_type)
		var start_level := int(row.start_level)
		for equip_id: String in row.equip_ids():
			var equip: Resource = db.get_row("equip_base", equip_id)
			if equip == null:
				continue     # 引用的存在性由别的规则报
			var equip_weapon := str(equip.weapon_type)
			if not equip_weapon.is_empty() and equip_weapon != weapon:
				errors.append(
					"character_base[%s] 的起始装备 %s 是 '%s' 类武器，但模板武器是 '%s'：开局装不上，角色空手、招式也用不了"
					% [row.id, equip_id, equip_weapon, weapon]
				)
			if int(equip.level_req) > start_level:
				errors.append(
					"character_base[%s] 的起始装备 %s 要求 %d 级，而模板起始等级是 %d：开局装不上"
					% [row.id, equip_id, int(equip.level_req), start_level]
				)


## 见 `REQUIRED_CAPPED_STATS`：管线依赖的四个比率不许「无上限」。
static func _check_pipeline_caps(db, errors: PackedStringArray) -> void:
	for stat_id: String in REQUIRED_CAPPED_STATS:
		var row: Resource = db.get_row("stat_def", stat_id)
		if row == null:
			continue     # 缺行由引用完整性那几条报，这里只管「上限没写」
		if not row.has_max():
			errors.append(
				"stat_def[%s].max_value 空着（= 0，本表约定「无上限」），但伤害管线对这个比率有硬上限："
				% stat_id
				+ "留空会让代码静默换用兜底常数，表与实际行为分家——要么补一个上限，"
				+ "要么明确告诉开发侧「这个值要改成无上限」"
			)


static func _check_primary_keys(db, errors: PackedStringArray) -> void:
	for table_name: String in Registry.TABLES:
		var resource: Resource = db.tables.get(table_name)
		var rows: Array = db.rows(table_name)
		var index: Dictionary = resource.get("index")
		var seen: Dictionary = {}
		for row: Resource in rows:
			var values: Dictionary = {}
			var complete := true
			for key: String in Registry.PRIMARY_KEYS.get(table_name, []):
				var value: Variant = row.get(Registry.property_of(table_name, key))
				values[key] = value
				if value is String and str(value).strip_edges().is_empty():
					errors.append("%s 存在空主键列 %s 的行" % [table_name, key])
					complete = false
				elif value is int and int(value) == 0:
					errors.append("%s 存在主键列 %s = 0 的行" % [table_name, key])
					complete = false
			if not complete:
				continue
			var computed: String = Registry.build_id(table_name, values)
			if computed != str(row.id):
				errors.append("%s[%s] 的 id 与主键列不一致（按列算应为 '%s'）" % [table_name, row.id, computed])
			if seen.has(computed):
				errors.append("%s 主键重复：'%s'" % [table_name, computed])
			seen[computed] = true
			var position := int(index.get(str(row.id), -1))
			# 位置必须先夹在 rows 的范围内才能取——`rows[position]` 越界会当场报运行期错误，
			# **把整段主键检查掐断**：排在这张表后面的表一条主键错都不再查（探针实测：
			# 同时弄坏 level_growth 与 item_base 的 index，只有 level_growth 被点名）。
			# 位置越界本身就是「index 与 rows 不一致」，照报一条然后继续查后面的行／表。
			if position < 0 or position >= rows.size() or rows[position] != row:
				errors.append("%s 的 index 与 rows 不一致：%s（index 记位置 %d，实际 %d 行）"
					% [table_name, row.id, position, rows.size()])


static func _check_enums(db, errors: PackedStringArray) -> void:
	for table_name: String in ENUMS:
		for column: String in ENUMS[table_name]:
			var allowed: Array = ENUMS[table_name][column]
			for row: Resource in db.rows(table_name):
				var value := str(row.get(column))
				if not allowed.has(value):
					errors.append("%s[%s].%s = '%s' 不是合法取值（允许：%s）" % [
						table_name, row.id, column, value, ", ".join(PackedStringArray(allowed)),
					])


## 异常状态的特殊规则：表里写了一个代码不认的 `extra_rule`，效果就会静默丢失
## （2026-10-03 之前连报都不报，见框架说明决策 59）。允许空 = 这条状态没有特殊规则。
static func _check_status_rules(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("status_effect"):
		var rule := str(row.extra_rule).strip_edges()
		if BattleActorScript.KNOWN_STATUS_RULES.has(rule):
			continue
		# 「设计已声明、代码还没实现」的规则允许留着（运行期会播成暂缓规则），
		# 这样构建不会被一条待设计的规则卡住；**写错的 id** 仍然当场报错。
		if BattleActorScript.DECLARED_STATUS_RULES.has(rule):
			continue
		errors.append(
			"status_effect[%s].extra_rule = '%s' 既不是代码认得的规则（battle_actor.KNOWN_STATUS_RULES：%s），也不是设计已声明的待实现规则（DECLARED_STATUS_RULES：%s）"
			% [
				row.id, rule,
				", ".join(PackedStringArray(BattleActorScript.KNOWN_STATUS_RULES)),
				", ".join(PackedStringArray(BattleActorScript.DECLARED_STATUS_RULES.keys())),
			]
		)


## 敌人的 AI 模板：`enemy_base.ai_template` 必须落在 `EnemyFactory.AI_SKILL_MAP` 的键里——
## 拼错（或漏填）的话那个敌人**一套招都没有**，每回合空过，难度会静默塌掉。
## 顺手反向查两处：模板映射与逐敌人覆盖里引用的招式必须在 `skill_base` 里真实存在。
static func _check_ai_templates(db, errors: PackedStringArray) -> void:
	var templates: Array = EnemyFactoryScript.AI_TEMPLATES
	# 招式在 `enemy_skill.csv`（0.10.0）：先按敌人点一遍数
	var skill_counts: Dictionary = {}
	for row: Resource in db.rows("enemy_skill"):
		var enemy_key := str(row.enemy_id)
		skill_counts[enemy_key] = int(skill_counts.get(enemy_key, 0)) + 1
	for row: Resource in db.rows("enemy_base"):
		var template := str(row.ai_template).strip_edges()
		if not templates.has(template):
			errors.append(
				"enemy_base[%s].ai_template = '%s' 不在 EnemyFactory.AI_TEMPLATES 里（%s）——拼错会让它的行为走兜底"
				% [row.id, template, ", ".join(PackedStringArray(templates))]
			)
		# 除 `ai_neutral`（中立·不还手，如采药人）外，每个敌人都得在 enemy_skill 里有招式，
		# 否则它在战斗里只会「无可用招式，跳过」——那是静默的哑巴敌人。
		if template != "ai_neutral" and int(skill_counts.get(str(row.enemy_id), 0)) == 0:
			errors.append(
				"enemy_base[%s]（%s）在 enemy_skill.csv 里一条招式都没有——它一招都出不了"
					% [str(row.enemy_id), template]
			)


## 建筑：店铺必须有货架、服务型必须说清是哪个服务（0.10.0 加了木桩 `service` 之后才需要分开看）。
##
## 起因：`building_def.stock_group` 原来是**必填引用**，0.10.0 的 `bld_dummy`（木桩，service）
## 没有货架 → 构建当场红。改成「允许空 + 按类型分别要求」，语义反而更清楚：
## **shop 要货架**（不然进店是空架子）、**service 要 service_id**（不然按 E 不知道做什么）。
static func _check_building_rows(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("building_def"):
		var kind := str(row.building_type).strip_edges()
		if kind == "shop" and str(row.stock_group).strip_edges().is_empty():
			errors.append("building_def[%s] 是店铺却没有 stock_group（进店会是空货架）" % str(row.building_id))
		if kind == "service" and str(row.service_id).strip_edges().is_empty():
			errors.append("building_def[%s] 是 service 却没有 service_id（按 E 不知道该做什么）" % str(row.building_id))


## 引导链与招募表（0.10.0 新增）的自洽检查。
static func _check_guide_and_recruit(db, errors: PackedStringArray) -> void:
	# 招募：有且只有一个初始成员（0.10.0 起不再"取 character_base 前 4 行"）
	var initial := 0
	for row: Resource in db.rows("recruit_def"):
		if bool(row.is_initial):
			initial += 1
		else:
			if str(row.join_condition).strip_edges().is_empty():
				errors.append("recruit_def[%s] 不是初始成员却没有 join_condition（永远加不进来）" % str(row.char_id))
			if str(row.join_scene).strip_edges().is_empty():
				errors.append("recruit_def[%s] 没写 join_scene（不知道在哪加入）" % str(row.char_id))
			else:
				# `join_scene` 既可能是小地图（`scene_qingfengyi`），也可能是**大地图节点**
				# （林铁山在落雁坡相遇 → `n_luoyanpo`），所以两张表都认（0.10.0 的数据就是这样）。
				var scene_id := str(row.join_scene).strip_edges()
				if db.get_row("map_local", scene_id) == null and db.get_row("map_region", scene_id) == null:
					errors.append(
						"recruit_def[%s].join_scene = '%s' 既不在 map_local 也不在 map_region 里"
							% [str(row.char_id), scene_id]
					)
	if initial != 1:
		errors.append("recruit_def 里 is_initial=1 的行有 %d 条（必须恰好一条：开局只有他）" % initial)
	# 引导：sort_order 唯一且从 1 起、condition 非空、第一步必须是 start
	var orders: Dictionary = {}
	var first_condition := ""
	var first_order := 1 << 30
	for row: Resource in db.rows("guide_step"):
		var order := int(row.sort_order)
		if orders.has(order):
			errors.append("guide_step 的 sort_order=%d 被两条步骤共用（先后顺序会不确定）" % order)
		orders[order] = true
		if str(row.condition).strip_edges().is_empty():
			errors.append("guide_step[%s] 的 condition 是空的（这一步永远不满足）" % str(row.step_id))
		if order < first_order:
			first_order = order
			first_condition = str(row.condition).strip_edges()
	if not orders.is_empty() and not orders.has(1):
		errors.append("guide_step 的 sort_order 必须从 1 开始（现在是 %s）" % str(orders.keys()))
	if not first_condition.is_empty() and first_condition != "start":
		errors.append("guide_step 的第一步（sort_order=%d）的 condition 应当是 start（开局就该立起来）" % first_order)
	# 界面文案：玩家看得见的那句话不能是空的；note 是给设计/开发看的，也要求写上
	for row: Resource in db.rows("ui_text"):
		if str(row.text_cn).strip_edges().is_empty():
			errors.append("ui_text[%s] 的 text_cn 是空的（玩家会看到一句空话）" % str(row.text_id))
		if str(row.note).strip_edges().is_empty():
			errors.append("ui_text[%s] 没写 note（不知道什么时候用它）" % str(row.text_id))


## 敌人也受**招式槽**限制（设计 10 §二 连带规则①）：配的招式数 ≤ `growth_const` 那套公式算出的上限
## （`2 + 等级/5 + 悟性/8`，上限 9）。悟性取「七维 + 内功给的 `attr:` 加成」——
## 所以这条**只在七维配好的行上生效**；还没配的行由 `EnemyFactory._warn_derived_path` 点名（不静默）。
##
## 目标：配了 5 招却只带得动 3 招，构建期就报错，不会到战斗里才发现有几招永远放不出来。
static func _check_enemy_skill_slots(db, errors: PackedStringArray) -> void:
	var growth = GrowthCalculatorScript.new(db)
	var calculator = AttributeCalculatorScript.new(db)
	for row: Resource in db.rows("enemy_base"):
		var enemy_id := str(row.enemy_id)
		var attrs: Dictionary = row.attr_map()
		if attrs.is_empty():
			continue
		var contributions: Array = []
		var passives := PackedStringArray()
		for passive: Resource in db.rows_where("enemy_passive", "enemy_id", enemy_id):
			var skill_id := str(passive.skill_id)
			if not skill_id.is_empty():
				passives.append(skill_id)
		if not passives.is_empty():
			contributions.append_array(growth.passive_contributions(passives))
		var totals: Dictionary = calculator.attr_totals_of(attrs, {}, contributions)
		var slots: int = growth.active_slots(int(row.level), totals)
		var skill_count: int = db.rows_where("enemy_skill", "enemy_id", enemy_id).size()
		if skill_count > slots:
			errors.append(
				"enemy_base[%s] 配了 %d 招，但它的招式槽只带得动 %d（等级 %d／悟性 %d）——"
				% [enemy_id, skill_count, slots, int(row.level), int(totals.get("wu", 0.0))]
				+ "要么降招式数、要么提等级/悟性（含内功给的）"
			)


## 练功木桩（设计 0.14.0 的 `faction=training`）是**唯一允许没有掉落组**的敌人——
## 09 §3.3 的收益上限表写着「掉落：无」。这条把那个口子收住：别的阵营漏配掉落组照样报错。
static func _check_training_faction(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("enemy_base"):
		if str(row.faction) == "training":
			continue
		if str(row.drop_group).strip_edges().is_empty():
			errors.append(
				"enemy_base[%s] 没有 drop_group（只有 faction=training 的练功木桩允许空，那是「无掉落」的练级靶子）"
					% str(row.enemy_id)
			)


## 宝箱守护者（0.10.0；0.14.0 把文取拆成三列）：两条路都要能走通。
##
## 0.14.0 的列：`peace_item`（要交的道具）、`peace_check_source`（`skill:<非战斗技能>` 或
## `attr:<属性>`）、`peace_check_value`（判定门槛）。**三字段可叠加，满足任意一条即算过**，
## 所以校验也是「至少一条能走」而不是「必须三条齐」。
static func _check_npc_guards(db, errors: PackedStringArray) -> void:
	var groups: Dictionary = {}
	for row: Resource in db.rows("drop_table"):
		groups[str(row.drop_group)] = true
	for row: Resource in db.rows("npc_guard"):
		var guard_id := str(row.guard_id)
		var reward := str(row.reward_group).strip_edges()
		if not groups.has(reward):
			errors.append("npc_guard[%s] 的 reward_group='%s' 不在 drop_table 的任何一组里" % [guard_id, reward])
		var item := str(row.peace_item).strip_edges()
		var source := str(row.peace_check_source).strip_edges()
		var value := int(row.peace_check_value)
		if item.is_empty() and source.is_empty():
			errors.append("npc_guard[%s] 文取的两条路都没配（peace_item 与 peace_check_source 都空）" % guard_id)
		if not item.is_empty() and db.get_row("item_base", item) == null:
			errors.append("npc_guard[%s] 要的道具 %s 在 item_base 里没有" % [guard_id, item])
		if not source.is_empty():
			var parts := source.split(":", false)
			if parts.size() != 2:
				errors.append("npc_guard[%s] 的 peace_check_source='%s' 不是 '<类型>:<id>' 的形式" % [guard_id, source])
			else:
				var kind := parts[0].strip_edges()
				var target := parts[1].strip_edges()
				match kind:
					"skill":
						if db.get_row("event_skill_def", target) == null:
							errors.append("npc_guard[%s] 的判定要非战斗技能 %s，但 event_skill_def 里没有" % [guard_id, target])
					"attr":
						if db.get_row("attribute_def", target) == null:
							errors.append("npc_guard[%s] 的判定要属性 %s，但 attribute_def 里没有" % [guard_id, target])
					_:
						errors.append("npc_guard[%s] 的判定类型 '%s' 不认识（只认 skill／attr）" % [guard_id, kind])
			if value <= 0:
				errors.append("npc_guard[%s] 有判定条件（%s）却没写门槛值（peace_check_value=%d）" % [guard_id, source, value])


## 隐藏触发在哪个 trigger_type 下该用哪个 condition 键（消费方是
## `local_map_controller._condition_value()`／`_condition_text()`）。
## 键必须**一字不差**：拼错的后果不对称——`behavior` 会当成条件已满足（白送奖励）、
## `kill_style` 会永远触发不了（还提示「要用「」击杀目标」）、`completion` 会静默用默认阈值。
const TRIGGER_CONDITION_KEYS := {
	"sequence": "sequence",
	"behavior": "flag_stealth_full",
	"kill_style": "kill_with",
	"completion": "chest_open_rate",
}
## 这几种触发类型靠别的列判条件（道具／工具／带着东西），不该再写 required_condition
const TRIGGER_TYPES_WITHOUT_CONDITION := ["item", "space", "carry"]


static func _check_trigger_conditions(db, errors: PackedStringArray) -> void:
	# 击杀方式 = 状态 id + 「正面击杀」；从表里取，不手抄一份枚举
	var kill_styles := PackedStringArray(["normal"])
	for row: Resource in db.rows("status_effect"):
		kill_styles.append(str(row.status_id))
	for row: Resource in db.rows("hidden_trigger"):
		var trigger_type := str(row.trigger_type)
		var condition := str(row.required_condition).strip_edges()
		if TRIGGER_TYPES_WITHOUT_CONDITION.has(trigger_type):
			if not condition.is_empty():
				errors.append("hidden_trigger[%s]（%s）不该有 required_condition，但写了 '%s'" % [
					row.id, trigger_type, condition,
				])
			continue
		var want_key := str(TRIGGER_CONDITION_KEYS.get(trigger_type, ""))
		if want_key.is_empty():
			continue   # 没登记的 trigger_type 由枚举校验负责
		if condition.is_empty():
			errors.append("hidden_trigger[%s]（%s）缺少 required_condition（应为 %s=…）" % [
				row.id, trigger_type, want_key,
			])
			continue
		for segment: String in condition.split(";", false):
			var pieces := segment.split("=", false)
			if pieces.size() != 2:
				errors.append("hidden_trigger[%s].required_condition 段 '%s' 不是 key=value" % [
					row.id, segment.strip_edges(),
				])
				continue
			var key := pieces[0].strip_edges()
			var value := pieces[1].strip_edges()
			if key != want_key:
				errors.append("hidden_trigger[%s]（%s）的条件键是 '%s'，只认 '%s'（拼错会静默改变判定）" % [
					row.id, trigger_type, key, want_key,
				])
				continue
			_check_trigger_condition_value(row.id, trigger_type, value, kill_styles, errors)


static func _check_trigger_condition_value(
	trigger_id: String, trigger_type: String, value: String,
	kill_styles: PackedStringArray, errors: PackedStringArray
) -> void:
	match trigger_type:
		"behavior":
			if value != "0" and value != "1":
				errors.append("hidden_trigger[%s].flag_stealth_full 只能是 1 或 0，实际 '%s'" % [trigger_id, value])
		"kill_style":
			if not kill_styles.has(value):
				errors.append("hidden_trigger[%s].kill_with='%s' 不是合法击杀方式（%s）" % [
					trigger_id, value, ", ".join(kill_styles),
				])
		"completion":
			var rate := float(value)
			if not (rate > 0.0 and rate <= 1.0):
				errors.append("hidden_trigger[%s].chest_open_rate 要落在 (0, 1]，实际 '%s'" % [trigger_id, value])
		"sequence":
			# 形如 1-3-2：数字与短横线，且至少两段（具体解法是谜题，代码只校验形态）
			var steps := value.split("-", false)
			var shape_ok := steps.size() >= 2
			for step: String in steps:
				if not step.is_valid_int():
					shape_ok = false
			if not shape_ok:
				errors.append("hidden_trigger[%s].sequence='%s' 形态应为 1-3-2（数字与短横线，至少两段）" % [
					trigger_id, value,
				])


static func _check_references(db, errors: PackedStringArray) -> void:
	for spec: Dictionary in REFERENCES:
		var values := _target_values(db, spec)
		var allow_empty := bool(spec.get("allow_empty", false))
		var split := str(spec.get("split", ""))
		for row: Resource in db.rows(spec["table"]):
			var raw := str(row.get(spec["column"])).strip_edges()
			if raw.is_empty():
				if not allow_empty:
					errors.append("%s[%s].%s 为空，但这是必填引用" % [spec["table"], row.id, spec["column"]])
				continue
			var tokens := PackedStringArray([raw]) if split.is_empty() else raw.split(split, false)
			for token: String in tokens:
				var trimmed := token.strip_edges()
				if trimmed.is_empty():
					continue
				if not values.has(trimmed):
					errors.append("%s[%s].%s = '%s' 在 %s 里不存在" % [
						spec["table"], row.id, spec["column"], trimmed, spec["target"],
					])


static func _target_values(db, spec: Dictionary) -> Dictionary:
	if spec.has("target_column"):
		return db.column_values(spec["target"], spec["target_column"])
	var primary: Array = Registry.PRIMARY_KEYS.get(spec["target"], [])
	if primary.is_empty():
		return {}
	return db.column_values(spec["target"], primary[0])


static func _check_drop_item_refs(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("drop_table"):
		var item_type := str(row.item_type)
		var item_id := str(row.item_id)
		if item_type == "equip":
			if db.get_row("equip_base", item_id) == null:
				errors.append("drop_table[%s].item_id = '%s' 在 equip_base 里不存在" % [row.id, item_id])
		elif db.get_row("item_base", item_id) == null:
			errors.append("drop_table[%s].item_id = '%s' 在 item_base 里不存在" % [row.id, item_id])
	# 同一个 drop_group 里 slot 不能重复：`DropResolver` 是 `sort_custom(slot)`（**不稳定排序**），
	# 两个同号槽的相对顺序不确定——设计写的「按 drop_group 顺序判定槽位」就静默失效了，
	# 同一颗种子的两局可能掉出不同顺序（保底计数按 drop_group|drop_row_id，不受影响，所以更难发现）。
	var seen_slots: Dictionary = {}
	for row: Resource in db.rows("drop_table"):
		var group := str(row.drop_group)
		var slot := int(row.slot)
		var key := "%s|%d" % [group, slot]
		if seen_slots.has(key):
			errors.append("drop_table 里 %s 的槽位 %d 重复（%s 与 %s）：掉落顺序会不确定"
				% [group, slot, str(seen_slots[key]), str(row.id)])
		else:
			seen_slots[key] = str(row.id)


static func _check_hidden_reward_refs(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("hidden_trigger"):
		var reward_type := str(row.reward_type)
		var reward_id := str(row.reward_id)
		if reward_id.is_empty():
			continue
		var target := ""
		match reward_type:
			"boss":
				target = "enemy_base"
			"equip":
				target = "equip_base"
			"skillbook":
				target = "item_base"
			"room":
				target = "dungeon_room"
			_:
				continue
		if db.get_row(target, reward_id) == null:
			errors.append("hidden_trigger[%s].reward_id = '%s' 在 %s 里不存在" % [row.id, reward_id, target])


static func _check_enemy_team_members(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("enemy_team"):
		var members: Array = row.parsed_members()
		if members.is_empty():
			errors.append("enemy_team[%s].members 为空或无法解析" % row.id)
			continue
		for member: Dictionary in members:
			if int(member["count"]) <= 0:
				errors.append("enemy_team[%s] 成员 %s 的数量必须大于 0" % [row.id, member["enemy_id"]])
			if db.get_row("enemy_base", member["enemy_id"]) == null:
				errors.append("enemy_team[%s] 成员 '%s' 在 enemy_base 里不存在" % [row.id, member["enemy_id"]])


## character_base 的五维之和必须等于 attr_total，否则配表改了属性却忘了改总数。
static func _check_character_attr_total(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("character_base"):
		var sum := int(row.attr_sum())
		if sum != int(row.attr_total):
			errors.append("character_base[%s] 的 attr_total=%d，但五维相加是 %d" % [
				row.id, int(row.attr_total), sum,
			])
		# 起始等级必须落在 [1, 等级上限] 里：0 会被 `maxi(1, …)` 静默当成 1 级，
		# 超过上限则找不到 `level_growth` 那一行（等级成长静默失效）。上限就是 level_growth 的行数。
		var top_level: int = max_level(db)
		var start_level := int(row.start_level)
		if start_level < 1:
			errors.append("character_base[%s].start_level=%d：低于 1 级（会被静默当成 1 级）" % [row.id, start_level])
		elif top_level > 0 and start_level > top_level:
			errors.append(
				"character_base[%s].start_level=%d：超过等级上限 %d（level_growth 只配到这一级）"
				% [row.id, start_level, top_level]
			)


## 每张小地图的楼层号必须从 1 起、中间不空号。
##
## `DungeonService.floors()` 是按 `floor` 分组的，缺一层不会报错，只会让完成度面板少一行
## （「第 1 层 / 第 3 层」这种），而「已通关层」的判定也按这份分层来——数据和设计不一致时，
## 玩家看到的完成度会莫名其妙。与 `level_growth` 的连续性检查同一口径（决策 133）。
static func _check_dungeon_floors(db, errors: PackedStringArray) -> void:
	var by_scene: Dictionary = {}
	for row: Resource in db.rows("dungeon_room"):
		var scene_id := str(row.scene_id)
		if not by_scene.has(scene_id):
			by_scene[scene_id] = {}
		by_scene[scene_id][int(row.floor)] = true
	for scene_id: String in by_scene:
		var floors: Array = by_scene[scene_id].keys()
		floors.sort()
		if floors.is_empty():
			continue
		if int(floors[0]) != 1:
			errors.append("%s 的房间最低层是第 %d 层，不是第 1 层" % [scene_id, int(floors[0])])
		for index in range(1, floors.size()):
			var previous := int(floors[index - 1])
			var current := int(floors[index])
			if current != previous + 1:
				errors.append(
					"%s 缺第 %d 层（%d → %d）：完成度面板会少一行、分层判定也会跟着缺"
					% [scene_id, previous + 1, previous, current]
				)


## 这个行对象上有没有这个属性（校验规则自检用：列名写错时要能识别出来，而不是 `float(null)` 崩溃）
static func _has_property(row: Object, property_name: String) -> bool:
	if property_name.is_empty():
		return false
	for entry: Dictionary in row.get_property_list():
		if str(entry["name"]) == property_name:
			return true
	return false


## 等级上限：`level_growth` 里最大的那一级（与 `LevelService.max_level()` 同一口径）。
static func max_level(db) -> int:
	var top := 0
	for row: Resource in db.rows("level_growth"):
		top = maxi(top, int(row.level))
	return top


## 装备的等级需求必须落在 `[1, 等级上限]` 里。
##
## 超过上限 = **永远穿不上**：背包里会一直写着「需要 N 级」，玩家等到满级也穿不了，
## 而且不报错——设计写 25 级、而等级只到 20，这件装备就成了死内容（和「配了但拿不到」同类）。
static func _check_equip_level_reqs(db, errors: PackedStringArray) -> void:
	var top := max_level(db)
	if top <= 0:
		return
	for row: Resource in db.rows("equip_base"):
		var req := int(row.level_req)
		if req < 1:
			errors.append("equip_base[%s].level_req=%d：低于 1 级（等于人人可穿）" % [row.id, req])
		elif req > top:
			errors.append(
				"equip_base[%s].level_req=%d 超过等级上限 %d：这件装备永远穿不上"
				% [row.id, req, top]
			)


## `level_growth` 必须从 1 级起、一级一行、中间不许缺号。
##
## `LevelService` 是**按等级号直接查行**的（`db.get_row("level_growth", level)`），
## 缺一行会被当成「这一级不要经验」（`exp_to_next` 返回 0）→ 升级静默卡在缺号的前一级，
## 而经验池只会一直涨；`max_level()` 也会跟着算错（它取的是最大编号，不是行数）。
static func _check_level_growth_rows(db, errors: PackedStringArray) -> void:
	var levels: Array = []
	for row: Resource in db.rows("level_growth"):
		levels.append(int(row.level))
	levels.sort()
	if levels.is_empty():
		errors.append("level_growth 一行都没有：升级无从谈起")
		return
	if int(levels[0]) != 1:
		errors.append("level_growth 必须从 1 级开始（现在最小是 %d）" % int(levels[0]))
	for index in range(1, levels.size()):
		var previous := int(levels[index - 1])
		var current := int(levels[index])
		if current != previous + 1:
			errors.append(
				"level_growth 缺 %d 级这一行（%d → %d）：升级会静默卡在 %d 级、经验池却一直涨"
				% [previous + 1, previous, current, previous]
			)
	# 升级不该让人变弱：基础数值随等级不许下降。写错一行会让「升级反而掉属性」，
	# 面板上看得见、但没有任何报错（经验池、门槛、槽位公式都不会因此出问题）。
	var ordered: Array = db.rows("level_growth").duplicate()
	ordered.sort_custom(func(a: Resource, b: Resource) -> bool: return int(a.level) < int(b.level))
	for column: String in LEVEL_BASE_COLUMNS:
		for index in range(1, ordered.size()):
			var before := float(ordered[index - 1].get(column))
			var now := float(ordered[index].get(column))
			if now < before:
				errors.append(
					"level_growth[%d].%s = %s 比上一级（%d 级，%s）还低：升级会变弱"
					% [int(ordered[index].level), column, now, int(ordered[index - 1].level), before]
				)
	# 经验门槛与加点：写错一位就会让整段升级「白送」或「变便宜」，而代码不会报错——
	# 经验池照常涨、付得起就升（`LevelService` 只读 exp_to_next，不判断它合不合理）。
	# **封顶那一级除外**：最高等级（20）用 `exp_to_next = 0`／`upgrade_points = 0` 表示「没有下一级」，
	# 直接拿它比会把正常数据判红（第一版就是这样，探针实测）。
	var cap_level := int(ordered[ordered.size() - 1].level)
	for index in range(1, ordered.size()):
		if int(ordered[index].level) == cap_level:
			continue
		var exp_before := float(ordered[index - 1].get("exp_to_next"))
		var exp_now := float(ordered[index].get("exp_to_next"))
		if exp_now <= 0.0:
			errors.append(
				"level_growth[%d].exp_to_next = %s：不是封顶级（%d 级才是）就必须给经验门槛，否则这一级白送"
				% [int(ordered[index].level), exp_now, cap_level]
			)
		elif exp_now < exp_before:
			errors.append(
				"level_growth[%d].exp_to_next = %s 比上一级（%d 级，%s）还低：升级会突然变便宜"
				% [int(ordered[index].level), exp_now, int(ordered[index - 1].level), exp_before]
			)
		var point_before := float(ordered[index - 1].get("upgrade_points"))
		var point_now := float(ordered[index].get("upgrade_points"))
		if point_now < point_before:
			errors.append(
				"level_growth[%d].upgrade_points = %s 比上一级（%d 级，%s）还低：升级会给更少的加点"
				% [int(ordered[index].level), point_now, int(ordered[index - 1].level), point_before]
			)


## event_check.check_source 必须带 attr: / skill: 前缀，且指向存在的目标。
static func _check_event_sources(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("event_check"):
		var parsed: Dictionary = row.parsed_source()
		var kind: String = parsed["kind"]
		var target_id: String = parsed["target_id"]
		match kind:
			"attr":
				if db.get_row("attribute_def", target_id) == null:
					errors.append("event_check[%s].check_source 引用的属性 '%s' 不存在" % [row.id, target_id])
			"skill":
				if db.get_row("event_skill_def", target_id) == null:
					errors.append("event_check[%s].check_source 引用的非战斗技能 '%s' 不存在" % [row.id, target_id])
			_:
				errors.append("event_check[%s].check_source = '%s' 缺少 attr: 或 skill: 前缀" % [row.id, row.check_source])


## 事件奖励按类型查表（event 是自由事件名，不查）。
##
## 设计 06 允许的取值是 `item`/`equip`/`skillbook`/`room`/`boss`/`event`/`none`；
## 以前这里只查 `item` 与 `room`，其余**一律 continue**——`equip`／`skillbook`／`boss` 写错 id
## 也能加载成功，而运行时那三种当时又静默不发东西，配错了没有一点声音。
## 现在 id 检查补齐（与 `tools/validate_tables.ps1` 的规则一致），运行时侧也改成显式报错。
static func _check_event_rewards(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("event_check"):
		var reward_type := str(row.reward_type)
		var reward_id := str(row.reward_id)
		var target := ""
		match reward_type:
			"item", "skillbook":
				target = "item_base"
			"equip":
				target = "equip_base"
			"room":
				target = "dungeon_room"
			"boss":
				target = "enemy_base"
			_:
				continue
		if reward_id.is_empty():
			errors.append("event_check[%s] 的 reward_type=%s 但没有填 reward_id" % [row.id, reward_type])
		elif db.get_row(target, reward_id) == null:
			errors.append("event_check[%s].reward_id = '%s' 在 %s 里不存在" % [row.id, reward_id, target])


## 初始非战斗技能等级不能超过该技能的等级上限。
static func _check_event_skill_levels(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("character_base_skill"):
		var skill: Resource = db.get_row("event_skill_def", row.skill_id)
		if skill == null:
			continue
		if int(row.level) > int(skill.max_level):
			errors.append("character_base_skill[%s] 的 %s 等级 %d 超过上限 %d" % [
				row.id, row.skill_id, int(row.level), int(skill.max_level),
			])


## 货架上的东西要么是装备要么是物品，两处都查不到说明 id 写错了。
static func _check_shop_item_refs(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("shop_stock"):
		var item_id := str(row.item_id)
		if db.get_row("equip_base", item_id) == null and db.get_row("item_base", item_id) == null:
			errors.append("shop_stock[%s] 的 '%s' 既不在 equip_base 也不在 item_base" % [row.id, item_id])


## 卖出价必须严格低于买入价，否则玩家能在两家店之间刷钱。
static func _check_shop_prices(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("shop_stock"):
		if int(row.buy_price) <= int(row.sell_price):
			errors.append("shop_stock[%s] 买入 %d / 卖出 %d：卖出价必须低于买入价，否则可以刷钱" % [
				row.id, int(row.buy_price), int(row.sell_price),
			])


## 武器槽必须填合法 weapon_type，非武器槽不许填。
static func _check_weapon_slot_consistency(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("equip_base"):
		var slot: Resource = db.get_row("equip_slot_def", row.slot)
		if slot == null:
			# 槽位本身不存在已经由引用校验报过，这里不重复刷屏
			continue
		var weapon_type := str(row.weapon_type)
		if slot.is_weapon_slot():
			if weapon_type.is_empty():
				errors.append("equip_base[%s] 在武器槽 %s 上没填 weapon_type" % [row.id, row.slot])
			elif not slot.allowed_weapon_types().has(weapon_type):
				errors.append("equip_base[%s].weapon_type='%s' 不在 %s 允许的类型（%s）里" % [
					row.id, weapon_type, row.slot, ", ".join(slot.allowed_weapon_types()),
				])
		elif not weapon_type.is_empty():
			errors.append("equip_base[%s] 是非武器槽 %s，不该填 weapon_type='%s'" % [row.id, row.slot, weapon_type])


## 武学总表与明细表必须一一对应：active 有且只有一条 skill_active，passive 有且只有一条 skill_passive。
static func _check_skill_details(db, errors: PackedStringArray) -> void:
	var active_rows: Dictionary = {}
	for row: Resource in db.rows("skill_active"):
		var skill_id := str(row.skill_id)
		if active_rows.has(skill_id):
			errors.append("skill_active 里 %s 有重复行" % skill_id)
		active_rows[skill_id] = row
	var passive_rows: Dictionary = {}
	for row: Resource in db.rows("skill_passive"):
		var skill_id := str(row.skill_id)
		if passive_rows.has(skill_id):
			errors.append("skill_passive 里 %s 有重复行" % skill_id)
		passive_rows[skill_id] = row
	for row: Resource in db.rows("skill_base"):
		var skill_id := str(row.skill_id)
		if row.is_active() and not active_rows.has(skill_id):
			errors.append("武学 %s 是招式（active），但 skill_active 里没有它的数值行" % skill_id)
		if row.is_passive() and not passive_rows.has(skill_id):
			errors.append("武学 %s 是内功（passive），但 skill_passive 里没有它的明细行" % skill_id)
	for skill_id: String in active_rows:
		var base: Resource = db.get_row("skill_base", skill_id)
		if base != null and not base.is_active():
			errors.append("skill_active 里的 %s 在 skill_base 里不是 active" % skill_id)
	for skill_id: String in passive_rows:
		var base: Resource = db.get_row("skill_base", skill_id)
		if base != null and not base.is_passive():
			errors.append("skill_passive 里的 %s 在 skill_base 里不是 passive" % skill_id)
	# **攻击招式的段数必须 ≥ 1**：有伤害类型且倍率 > 0（= `is_attack()` 为真、界面会给出按钮）
	# 却写 `hit_count = 0`，结算循环一次都不跑——**能用、却打不出任何伤害，还不报错**。
	# 只对攻击招式要求：`sk_drunk_zuibu`（醉步）那种「还没配效果」的空壳（类型与倍率都是 0）
	# 有专门的门限盯着（校验 5.21 + 面板文案），不在这里重复报。
	for skill_id: String in active_rows:
		var row: Resource = active_rows[skill_id]
		var is_attack: bool = not str(row.damage_type).is_empty() and float(row.power_ratio) > 0.0
		if is_attack and int(row.hit_count) < 1:
			errors.append(
				"招式 %s 有伤害类型与倍率，但 hit_count = %d：结算一段都跑不到（能用却零伤害）"
				% [skill_id, int(row.hit_count)]
			)


## 招式绑定的武器类型：只能留空、any，或 weapon_type_def 里的类型。
static func _check_skill_weapon_types(db, errors: PackedStringArray) -> void:
	var known: Dictionary = db.column_values("weapon_type_def", "weapon_type")
	for row: Resource in db.rows("skill_base"):
		var weapon_type := str(row.weapon_type)
		if weapon_type.is_empty() or weapon_type == "any":
			continue
		if not known.has(weapon_type):
			errors.append("skill_base[%s].weapon_type='%s' 不是合法武器类型（可填空或 any）" % [row.id, weapon_type])


## 每部内功至少要有一条加成，否则装了等于没装。
static func _check_passive_stats(db, errors: PackedStringArray) -> void:
	var counts: Dictionary = {}
	for row: Resource in db.rows("skill_passive_stat"):
		var skill_id := str(row.skill_id)
		counts[skill_id] = int(counts.get(skill_id, 0)) + 1
		if is_zero_approx(float(row.value)):
			errors.append("skill_passive_stat[%s].value = 0：这条加成是白填（装了等于没装）" % row.id)
		var parsed: Dictionary = row.parsed_target()
		var kind: String = parsed["kind"]
		var target_id: String = parsed["target_id"]
		match kind:
			"attr":
				if db.get_row("attribute_def", target_id) == null:
					errors.append("skill_passive_stat[%s].target 引用的属性 '%s' 不存在" % [row.id, target_id])
			"stat":
				if db.get_row("stat_def", target_id) == null:
					errors.append("skill_passive_stat[%s].target 引用的派生数值 '%s' 不存在" % [row.id, target_id])
			_:
				errors.append("skill_passive_stat[%s].target='%s' 缺少 attr: 或 stat: 前缀" % [row.id, row.target])
	for row: Resource in db.rows("skill_passive"):
		if not counts.has(str(row.skill_id)):
			errors.append("内功 %s 在 skill_passive_stat 里一条加成都没有" % row.skill_id)


## 非「白板」装备至少要有一条加成。
##
## 05 的装备阶梯是「白板 → 良品 → 珍品 → 绝品」：白板指**没有随机词条**（但有固定属性，店里就卖这个），
## 而良品以上的装备如果一条固定加成都没有、又没到词条那一层，就和白板没区别——
## 玩家花更贵的价钱买到的是同一件东西。这类数据错不会报错，只会「安静地不对」。
static func _check_equip_bonuses(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("equip_base"):
		var rarity := str(row.rarity)
		if rarity.is_empty() or rarity == "common":
			continue
		var total := 0.0
		for value: Variant in row.attr_bonuses().values():
			total += float(value)
		for value: Variant in row.stat_bonuses().values():
			total += float(value)
		if is_zero_approx(total):
			errors.append(
				"equip_base[%s]（%s）是 %s 稀有度但一条加成都没有：和「白板」没区别"
				% [row.id, str(row.name_cn), rarity]
			)


## 词条的目标必须能被认出来：`affix_pool.target` 只能 `attr:<属性>` 或 `stat:<派生值>`，
## 且两者都存在；`value_kind` 也要跟前缀对上（`attr_point` ↔ `attr:`、`flat`/`rate` ↔ `stat:`）。
##
## 起因：`AffixRoller.contributions_of` 是**按前缀定层**的（`prefix == "attr"` 走属性点层、否则走固定值层），
## 前缀拼错（`attrr:str`、漏冒号 `str`）或目标 id 打错时，`AttributeCalculator` 查不到就**静默忽略**——
## 玩家掷到一条词条、装备描述里也写着「外功攻击 +7」，实际一点属性都不加，而且没有任何报错。
## 这和 `skill_passive_stat` 的校验是一对（那半边早就有了）。
static func _check_affix_targets(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("affix_pool"):
		var parsed: Dictionary = row.parsed_target()
		var kind: String = str(parsed["kind"])
		var target_id: String = str(parsed["target_id"])
		var value_kind := str(row.value_kind)
		match kind:
			"attr":
				if db.get_row("attribute_def", target_id) == null:
					errors.append("affix_pool[%s].target 引用的属性 '%s' 不存在" % [row.id, target_id])
				if value_kind != "attr_point":
					errors.append("affix_pool[%s] 是属性点词条，value_kind 应为 attr_point（现在是 '%s'）" % [row.id, value_kind])
			"stat":
				if db.get_row("stat_def", target_id) == null:
					errors.append("affix_pool[%s].target 引用的派生数值 '%s' 不存在" % [row.id, target_id])
				if value_kind != "flat" and value_kind != "rate":
					errors.append("affix_pool[%s] 是派生数值词条，value_kind 应为 flat 或 rate（现在是 '%s'）" % [row.id, value_kind])
			_:
				errors.append("affix_pool[%s].target='%s' 缺少 attr: 或 stat: 前缀" % [row.id, row.target])


## growth_const 必备常数（缺了槽位公式就没法算）。
static func _check_growth_constants(db, errors: PackedStringArray) -> void:
	var required := [
		"active_slot_base", "active_slot_lv_div", "active_slot_attr_div", "active_slot_cap",
		"passive_cap_base", "passive_cap_lv_div", "passive_cap_attr_div", "passive_cap_max",
		"mastery_max", "mastery_combat_gain", "cultivate_cost_growth",
		"codex_step", "codex_bonus", "codex_cap",
	]
	var present: Dictionary = db.column_values("growth_const", "const_id")
	for const_id: String in required:
		if not present.has(const_id):
			errors.append("growth_const 缺少必备常数 %s" % const_id)


## 星级定义必须是完整的 1~5。
static func _check_star_def(db, errors: PackedStringArray) -> void:
	var present: Dictionary = db.column_values("skill_star_def", "star")
	for star in range(1, 6):
		if not present.has(str(star)):
			errors.append("skill_star_def 缺少 %d 星" % star)


static func _check_ranges(db, errors: PackedStringArray) -> void:
	for rule: Dictionary in RANGES:
		var rows: Array = db.rows(rule["table"])
		if rows.is_empty():
			continue
		# 规则表自己写错列名时：`row.get()` 返回 null，`float(null)` 会**直接报运行期错误、
		# 让整个校验中途断掉**（后面的规则一条都不再跑）——这里显式挡一下，报一条错继续往下查。
		var property := Registry.property_of(str(rule["table"]), str(rule["column"]))
		if not _has_property(rows[0], property):
			errors.append("校验规则里的列不存在：%s.%s（RANGES 写错了）" % [rule["table"], rule["column"]])
			continue
		for row: Resource in rows:
			var value := float(row.get(property))
			if rule.has("min") and value < float(rule["min"]):
				errors.append("%s[%s].%s = %s 小于下限 %s" % [rule["table"], row.id, rule["column"], value, rule["min"]])
			if rule.has("max") and value > float(rule["max"]):
				errors.append("%s[%s].%s = %s 大于上限 %s" % [rule["table"], row.id, rule["column"], value, rule["max"]])


static func _check_relations(db, errors: PackedStringArray) -> void:
	for rule: Dictionary in RELATIONS:
		var rows: Array = db.rows(rule["table"])
		if rows.is_empty():
			continue
		var left_property := Registry.property_of(str(rule["table"]), str(rule["left"]))
		var right_property := Registry.property_of(str(rule["table"]), str(rule["right"]))
		if not _has_property(rows[0], left_property) or not _has_property(rows[0], right_property):
			errors.append("校验规则里的列不存在：%s.%s / %s（RELATIONS 写错了）"
				% [rule["table"], rule["left"], rule["right"]])
			continue
		for row: Resource in rows:
			var left := float(row.get(left_property))
			var right := float(row.get(right_property))
			# stat_def 的 max_value 为 0 表示无上限，不参与比较
			if rule["table"] == "stat_def" and right <= 0.0:
				continue
			if left > right:
				errors.append("%s[%s] %s不合法：%s(%s) > %s(%s)" % [
					rule["table"], row.id, rule["label"], rule["left"], left, rule["right"], right,
				])


static func _check_diminishing(db, errors: PackedStringArray) -> void:
	for row: Resource in db.rows("attr_to_stat"):
		if str(row.curve) != "diminishing":
			continue
		if float(row.param) <= 0.0:
			errors.append("attr_to_stat[%s] 走递减曲线但 param 为空/非正" % row.id)
		if float(row.cap) <= 0.0:
			errors.append("attr_to_stat[%s] 走递减曲线但 cap 为空/非正" % row.id)
