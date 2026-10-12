using System.Collections.Generic;
using Godot;
using Lunhui.Data;

namespace Lunhui.Core;

/// <summary>
/// 配置表交叉校验（C# 版，对位原 `src/core/table_validator.gd`）：构建期（`build_tables`）
/// 与运行期（`GameData`）共用同一套规则。校验分四类：主键、枚举、引用、数值区间。
///
/// **为什么拆成四个文件**（原 `.gd` 2049 行，见 `docs/dev/代码拆分清单.md` 的「按表族拆」）：
///   本文件           —— 词表（ENUMS／REFERENCES／RANGES／RELATIONS…）、入口 `validate`、取值口、共享工具
///   TableValidatorStructure.cs —— 表结构类：主键／枚举／引用／区间／关系／代码与数据一致性
///   TableValidatorActors.cs    —— 角色／武学／装备／敌人／掉落／事件／商店
///   TableValidatorWorld.cs     —— 剧情／对话／NPC／引导／创建角色／增益与套装／难度／地图
/// 三份规则文件是**纯 C# 静态类**（不派生 GodotObject）——Godot 只把本文件当脚本资源，
/// GDScript 侧照旧 `preload("res://src/core/TableValidator.cs").validate(db)`。
///
/// **跨语言的三条实测结论**（2026-10-10，探针跑出来的，别再猜）：
///   1. C# 读 GDScript 常量／属性：`GD.Load&lt;GDScript&gt;(path).GetScriptConstantMap()`；
///   2. C# 调 GDScript **静态**方法：`script.Call("held_item_id", db, "…")`——正常；
///   3. GDScript **读不到** C# 的 `const`／静态字段（`Invalid access to property or key …`），
///      只能调静态方法。所以本文件把四张规则表包成 `enums()`／`references()`／`ranges()`／
///      `relations()`，过渡期的 GDScript 调用方走这四个口；P5 清零后可以收回只剩字段。
/// </summary>
public partial class TableValidator : RefCounted
{
    // 代码辨认的伤害元素／道具类型／威胁标签（ENUMS 里多次引用，写一份别抄第二份）。
    internal static readonly string[] ELEMENTS = { "external", "internal", "odd" };
    internal static readonly string[] ITEM_TYPES =
        { "currency", "material", "consumable", "key", "tool", "skillbook" };
    internal static readonly string[] DROP_ITEM_TYPES =
        { "money", "material", "consumable", "equip", "key", "tool", "skillbook" };
    internal static readonly string[] THREAT_TAGS = { "green", "yellow", "red", "purple" };

    /// <summary>
    /// 表名 → { 列名: 允许取值 }，含 "" 表示允许留空。
    ///
    /// 这张表是「枚举三处同改」的第二处（另两处：`docs/design/06_配置表说明.md`、
    /// `tools/validate_tables.ps1`）——门限 `test_handshake_docs._check_doc_enums_match_code`
    /// 与 `test_handshake_repo._check_ps1_mentions_enum_columns` 盯着它们不许分家。
    /// </summary>
    internal static readonly Dictionary<string, Dictionary<string, string[]>> ENUMS = new()
    {
        ["attr_to_stat"] = new() { ["curve"] = new[] { "linear", "diminishing", "piecewise" } },
        ["stat_def"] = new() { ["value_kind"] = new[] { "int", "float" } },
        ["damage_type"] = new()
        {
            ["category"] = new[] { "instant", "dot" },
            ["tick_timing"] = new[] { "immediate", "turn_end" },
        },
        ["element_counter"] = new() { ["element_atk"] = ELEMENTS, ["element_def"] = ELEMENTS },
        ["weapon_type_def"] = new() { ["default_element"] = ELEMENTS },
        // `enemy_base.element`（设计 22 §六-6，2026-10-12）：**空 ＝ external 外功**（与加列之前一致），
        // 所以白名单里带 ""——与 `skill_active.element` 同一写法。
        ["enemy_base"] = new() { ["element"] = new[] { "", "external", "internal", "odd" } },
        ["event_check"] = new()
        {
            ["check_type"] = new[] { "hard", "soft" },
            // 设计 06 允许 7 种：item／equip／skillbook／room／boss／event／none。
            // 这里以前只留了 4 种，于是「给判定配一条 equip 奖励」这种 06 允许、PS1 也允许、
            // EventCheckService 也实现的写法会被构建期校验器拒掉（决策 87）。
            ["reward_type"] = new[] { "none", "item", "equip", "skillbook", "room", "boss", "event" },
        },
        ["skill_base"] = new()
        {
            ["skill_kind"] = new[] { "active", "passive" },
            // `origin`＝出身本命机遇（设计 21 §九，0.31.0）：与 `story` 同一条发放通道
            ["source_type"] = new[]
                { "start", "npc", "drop", "story", "hidden", "item", "shop", "origin" },
        },
        ["skill_active"] = new()
        {
            ["element"] = new[] { "", "external", "internal", "odd" },
            ["target_type"] = new[] { "single", "self", "all_enemy" },
        },
        // 槽位不写死：equip_base.slot 与 affix_pool.allow_slots 走 equip_slot_def 的动态引用
        ["equip_base"] = new() { ["element"] = new[] { "", "external", "internal", "odd" } },
        ["affix_pool"] = new() { ["value_kind"] = new[] { "attr_point", "flat", "rate" } },
        ["item_base"] = new()
        {
            ["item_type"] = ITEM_TYPES,
            ["use_context"] = new[] { "", "field", "battle" },
        },
        // 设计 09 正文写 `interactive`、数据里是 `service`——这里以数据为准（差异记进 待策划确认.md）
        ["building_def"] = new() { ["building_type"] = new[] { "shop", "service" } },
        // 06 没有列过 faction 的枚举，所以这里就是唯一出处（改数据要同时改这一行）
        ["enemy_base"] = new()
        {
            ["faction"] = new[] { "beast", "neutral", "bandit", "rogue", "hidden", "training" },
            ["threat_tag"] = THREAT_TAGS,
        },
        ["enemy_team"] = new() { ["threat_tag"] = THREAT_TAGS },
        ["drop_table"] = new()
        {
            ["item_type"] = DROP_ITEM_TYPES,
            ["roll_type"] = new[] { "independent", "exclusive" },
        },
        ["map_region"] = new()
        {
            ["node_type"] = new[] { "town", "fast_travel", "wild", "dungeon", "poi" },
        },
        ["map_local"] = new() { ["scene_type"] = new[] { "town", "dungeon", "poi" } },
        ["roaming_spawn"] = new()
        {
            ["behavior"] = new[] { "idle", "patrol", "wander", "chase", "sleep" },
        },
        ["dungeon_room"] = new()
        {
            // `side`：设计 0.26.0 给「石隙迷窟」加的死路房（空室这种，走进去只能回头）
            ["room_type"] = new[]
                { "entrance", "battle", "treasure", "trap", "secret", "elite", "story", "boss", "side" },
            ["branch_group"] = new[] { "", "main", "side", "hidden" },
        },
        ["hidden_trigger"] = new()
        {
            ["trigger_type"] = new[]
                { "item", "space", "sequence", "behavior", "kill_style", "completion", "carry" },
            ["reward_type"] = new[] { "boss", "equip", "skillbook", "room", "event" },
        },
        // 增益与套装（0.7.0 的表；2026-10-03 补进构建期这一侧，见决策 218）。
        // `buff_def.is_debuff` 是 **bool 列**（与 CSV 里的 0/1 不同名），所以不在 ENUMS 里——
        // 那一条由 PS1 按原始 CSV 文本查。
        ["buff_def"] = new()
        {
            ["scope"] = new[] { "battle", "field" },
            ["stack_rule"] = new[] { "refresh", "stack", "unique" },
            ["effect_kind"] = new[] { "stat", "computed", "special" },
        },
        ["buff_grant"] = new()
        {
            ["source_type"] = new[]
                { "skill_active", "skill_passive", "equip", "set", "event", "item" },
            ["trigger"] = new[]
                { "on_equip", "on_battle_start", "on_cast", "on_hit", "on_consume" },
        },
        ["set_def"] = new() { ["set_kind"] = new[] { "equip", "skill_active", "skill_passive" } },
        ["feature_toggle"] = new() { ["value"] = new[] { "0", "1" } },
        // 创建角色（0.16.0／0.17.0）：天赋六个方向（设计 12 的 category 一列）。
        // `origin_def` 没有枚举列——它的合法性全在「char_id 指向真实的 character_base 行」上。
        ["talent_def"] = new()
        {
            ["category"] = new[] { "combat", "body", "agile", "mind", "social", "fortune" },
        },
        // 主线与章节（0.22.0）：`chapter_end` = 章节收束节点／`faction` = 门派支线
        ["story_node"] = new()
        {
            ["kind"] = new[] { "chapter_end", "faction", "choice", "origin_gift" },
        },
        // NPC 与大地图事件（设计 19）
        ["npc_offer"] = new() { ["kind"] = new[] { "offer", "steal" } },
        ["world_event"] = new()
        {
            ["effect_kind"] = new[] { "trade", "spar", "hint", "gift", "check" },
        },
    };

    /// <summary>单值 / 多值引用规则：{table, column, target, target_column?, split?, allow_empty?}。</summary>
    internal sealed class ReferenceRule
    {
        public string Table;
        public string Column;
        public string Target;
        public string TargetColumn;
        public string Split;
        public bool AllowEmpty;
    }

    /// <summary>数值区间规则（min／max 只有一个也行）。</summary>
    internal sealed class RangeRule
    {
        public string Table;
        public string Column;
        public double? Min;
        public double? Max;
    }

    /// <summary>关系规则：left ≤ right。</summary>
    internal sealed class RelationRule
    {
        public string Table;
        public string Left;
        public string Right;
        public string Label;
    }

    private static ReferenceRule R(
        string table, string column, string target,
        string targetColumn = null, string split = null, bool allowEmpty = false
    ) => new()
    {
        Table = table,
        Column = column,
        Target = target,
        TargetColumn = targetColumn,
        Split = split,
        AllowEmpty = allowEmpty,
    };

    private static RangeRule G(string table, string column, double? min = null, double? max = null) =>
        new() { Table = table, Column = column, Min = min, Max = max };

    private static RelationRule L(string table, string left, string right, string label) =>
        new() { Table = table, Left = left, Right = right, Label = label };

    internal static readonly ReferenceRule[] REFERENCES =
    {
        // 角色与属性
        R("attr_to_stat", "attr_id", "attribute_def"),
        R("attr_to_stat", "stat_id", "stat_def"),
        R("attr_to_stat", "curve", "curve_def"),
        R("character_base", "start_skill_ids", "skill_base", split: ";", allowEmpty: true),
        R("character_base", "weapon_type", "weapon_type_def"),
        R("character_base", "start_equip_ids", "equip_base", split: ";", allowEmpty: true),
        R("character_base_skill", "char_id", "character_base"),
        R("character_base_skill", "skill_id", "event_skill_def"),
        R("event_skill_def", "related_attr", "attribute_def"),
        R("event_check", "region_id", "map_region", allowEmpty: true),
        R("event_check", "scene_id", "map_local", allowEmpty: true),
        R("event_check", "room_id", "dungeon_room", allowEmpty: true),
        // 战斗
        R("status_effect", "damage_type", "damage_type"),
        R("status_effect", "trigger_stat", "attribute_def"),
        R("status_effect", "power_stat", "attribute_def"),
        // skill_base 0.6.0 起只放身份与门槛：伤害/消耗在 skill_active，内功在 skill_passive
        R("skill_base", "learn_req_attr", "attribute_def", allowEmpty: true),
        R("skill_base", "star", "skill_star_def", targetColumn: "star"),
        R("skill_active", "skill_id", "skill_base"),
        R("skill_active", "damage_type", "damage_type", allowEmpty: true),
        R("skill_active", "status_id", "status_effect", allowEmpty: true),
        R("skill_passive", "skill_id", "skill_base"),
        R("skill_passive_stat", "skill_id", "skill_passive"),
        // 装备与物品
        R("equip_base", "rarity", "rarity_def"),
        R("equip_base", "slot", "equip_slot_def"),
        R("equip_base", "weapon_type", "weapon_type_def", allowEmpty: true),
        R("affix_pool", "allow_slots", "equip_slot_def", split: "|"),
        R("equip_slot_def", "allow_weapon_types", "weapon_type_def", split: "|", allowEmpty: true),
        R("weapon_type_def", "attr_focus", "attribute_def", split: "|", allowEmpty: true),
        R("weapon_type_def", "skill_school", "skill_base", targetColumn: "school_id", allowEmpty: true),
        // 商店与经济：0.10.0 起 building_def 里有非店铺行（木桩 service），所以这里允许空——
        // 「店铺必须有货架」改由 `_check_building_rows` 单独管
        R("building_def", "stock_group", "shop_stock", targetColumn: "shop_id", allowEmpty: true),
        R("shop_stock", "shop_id", "building_def", targetColumn: "stock_group"),
        R("affix_pool", "min_rarity", "rarity_def"),
        R("item_base", "rarity", "rarity_def"),
        // 敌人与掉落：`drop_group` 允许为空（练功木桩无掉落），空值这条口子由 `_check_training_faction` 收口
        R("enemy_base", "drop_group", "drop_table", targetColumn: "drop_group", allowEmpty: true),
        R("enemy_skill", "enemy_id", "enemy_base"),
        R("enemy_skill", "skill_id", "skill_base"),
        R("enemy_equip", "enemy_id", "enemy_base"),
        R("enemy_equip", "slot_id", "equip_slot_def"),
        R("enemy_equip", "equip_id", "equip_base"),
        R("enemy_passive", "enemy_id", "enemy_base"),
        R("enemy_passive", "skill_id", "skill_passive"),
        R("difficulty_drop_rate", "difficulty_id", "difficulty_config"),
        R("difficulty_drop_rate", "rarity_id", "rarity_def"),
        // 地图与副本
        R("map_region", "enter_scene", "map_local", allowEmpty: true),
        R("map_local", "parent_node", "map_region", allowEmpty: true),
        R("roaming_spawn", "region_id", "map_region"),
        R("roaming_spawn", "team_id", "enemy_team"),
        R("dungeon_room", "scene_id", "map_local"),
        R("dungeon_room", "enemy_team", "enemy_team", allowEmpty: true),
        R("dungeon_room", "chest_id", "drop_table", targetColumn: "drop_group", allowEmpty: true),
        R("dungeon_room", "hidden_trigger", "hidden_trigger", allowEmpty: true),
        R("dungeon_room", "exit_rooms", "dungeon_room", split: "|", allowEmpty: true),
        R("hidden_trigger", "scene_id", "map_local"),
        R("hidden_trigger", "room_id", "dungeon_room", allowEmpty: true),
        R("hidden_trigger", "required_item", "item_base", allowEmpty: true),
        // 引导／招募／宝箱守护者（0.10.0 新增，见 docs/design/09_试玩修正.md）
        R("recruit_def", "char_id", "character_base"),
        R("npc_guard", "room_id", "dungeon_room"),
        R("npc_guard", "fight_team", "enemy_team", allowEmpty: true),
        // 创建角色（0.16.0／0.17.0）
        R("origin_def", "char_id", "character_base"),
        R("talent_effect", "talent_id", "talent_def"),
        // 主线与章节（0.22.0）：门派／结束节点挂在真实章节上；地点留空表示"不限地点"
        R("story_node", "chapter_id", "chapter_def"),
        // `story_node.place_id` 可以是小地图也可以是大地图节点，所以不进这张单值表
        // 抉择增益的四列（0.29.1）：属性点层引 attribute_def、固定值层引 stat_def
        R("story_node", "bonus_attr_id", "attribute_def", allowEmpty: true),
        R("story_node", "bonus_stat_id", "stat_def", allowEmpty: true),
        // 观察点（0.29.1）：小地图／房间／大地图节点三选一
        R("flavor_point", "scene_id", "map_local", allowEmpty: true),
        R("flavor_point", "room_id", "dungeon_room", allowEmpty: true),
        R("flavor_point", "region_id", "map_region", allowEmpty: true),
        // 对话容器（0.31.0）：跳转目标只能是对话节点
        R("dialogue_node", "next_node_id", "dialogue_node", allowEmpty: true),
        R("dialogue_option", "node_id", "dialogue_node"),
        R("dialogue_option", "next_node_id", "dialogue_node", allowEmpty: true),
        R("guide_step", "chapter_id", "chapter_def", allowEmpty: true),
        R("chapter_def", "next_chapter_id", "chapter_def", allowEmpty: true),
        // NPC 与大地图事件（设计 19）：`npc_favor`／`npc_quest` 的 npc_id 可 NPC 可同伴，另查
        R("npc_offer", "npc_id", "npc_def"),
        R("npc_def", "spar_team_id", "enemy_team", allowEmpty: true),
    };

    /// <summary>数值区间规则。</summary>
    internal static readonly RangeRule[] RANGES =
    {
        G("attr_to_stat", "rate", 0.0),
        G("event_skill_def", "max_level", 1.0),
        G("event_check", "difficulty", 0.0),
        G("character_base", "attr_total", 0.0),
        G("character_base_skill", "level", 0.0),
        G("equip_slot_def", "max_equip", 1.0),
        G("equip_slot_def", "sort_order", 1.0),
        G("building_def", "service_price", 0.0),
        G("shop_stock", "sort_order", 1.0),
        G("shop_stock", "buy_price", 1.0),
        G("shop_stock", "sell_price", 0.0),
        G("shop_stock", "stock_limit", 0.0),
        G("skill_active", "power_ratio", 0.0),
        G("skill_active", "qi_cost", 0.0),
        G("skill_active", "hit_count", 0.0),
        G("skill_active", "cooldown", 0.0),
        // 负的 poise_damage 会被 `take_poise_damage` 的 `maxi(amount, 0)` 静默吃掉
        G("skill_active", "poise_damage", 0.0),
        // stack_max = 0 会被 `maxi(1, …)` 当成 1，负值更没意义
        G("item_base", "stack_max", 1.0),
        // 负的 status_chance_mul：异常几率会算成负数，掷点永远掷不中
        G("skill_active", "status_chance_mul", 0.0),
        // 负的经验门槛：等级会一路跳到上限
        G("level_growth", "exp_to_next", 0.0),
        // 负的加点：面板上「未分配点数：-3」
        G("level_growth", "upgrade_points", 0.0),
        G("skill_passive", "slot_cost", 1.0, 3.0),
        G("skill_star_def", "mastery_gain", 0.0001),
        G("skill_star_def", "cultivate_cost_base", 1.0),
        G("character_base", "initial_str", 0.0),
        G("character_base", "initial_con", 0.0),
        G("character_base", "initial_agi", 0.0),
        G("character_base", "initial_int", 0.0),
        G("character_base", "initial_luk", 0.0),
        G("affix_pool", "weight", 1.0),
        G("drop_table", "base_rate", 0.0, 1.0),
        G("drop_table", "slot", 1.0),
        G("drop_table", "qty_min", 0.0),
        G("difficulty_drop_rate", "rate_multiplier", 0.0),
        // 难度倍率必须**严格为正**：0 会让敌人一出生就是 0 血、经验一点不给
        G("difficulty_config", "enemy_hp_mul", 0.0001),
        G("difficulty_config", "enemy_atk_mul", 0.0001),
        G("difficulty_config", "enemy_def_mul", 0.0001),
        G("difficulty_config", "exp_mul", 0.0001),
        G("difficulty_config", "money_mul", 0.0001),
        G("enemy_base", "level", 1.0),
        G("enemy_base", "hp_base", 1.0),
        // 敌人战斗数值：负的攻击/防御会让伤害公式反向，比率类超过 1 也没有意义
        G("enemy_base", "atk_phys", 0.0),
        G("enemy_base", "atk_qi", 0.0),
        G("enemy_base", "def_phys", 0.0),
        G("enemy_base", "def_qi", 0.0),
        G("enemy_base", "speed", 0.0),
        G("enemy_base", "poise", 0.0),
        // 创建角色（0.16.0／0.17.0）：天赋点花费 1~3（设计 12 的池子），出身卡排序从 1 起
        G("talent_def", "cost", 1.0, 3.0),
        G("origin_def", "sort_order", 1.0),
        G("chapter_def", "sort_order", 1.0),
        G("npc_def", "level", 1.0),
        G("npc_favor", "favor_max", 1.0),
        G("npc_favor", "gift_favor", 0.0),
        G("npc_favor", "spar_favor", 0.0),
        G("npc_favor", "steal_difficulty", 0.0),
        G("npc_offer", "favor_required", 0.0),
        G("npc_offer", "price", 0.0),
        G("npc_quest", "sort_order", 1.0),
        G("npc_quest", "reward_favor", 0.0),
        G("world_event", "weight", 0.0),
        G("enemy_base", "hit_rate", 0.0, 1.0),
        G("enemy_base", "dodge_rate", 0.0, 1.0),
        G("enemy_base", "crit_rate", 0.0, 1.0),
        // 0.14.0：毒/火/流血三条特权列取消了（敌人要抗性就装内功）；内伤抗性这一列保留
        G("enemy_base", "res_internal", 0.0, 1.0),
        // 七维：非负；总和不做硬性要求（敌人不像角色卡那样定死 49）
        G("enemy_base", "attr_str", 0.0),
        G("enemy_base", "attr_con", 0.0),
        G("enemy_base", "attr_agi", 0.0),
        G("enemy_base", "attr_int", 0.0),
        G("enemy_base", "attr_luk", 0.0),
        G("enemy_base", "attr_wu", 0.0),
        G("enemy_base", "attr_gen", 0.0),
        G("roaming_spawn", "alert_radius", 0.0),
        G("roaming_spawn", "chase_speed", 0.0),
        G("roaming_spawn", "respawn_sec", 0.0),
        G("map_region", "pos_x", 0.0),
        G("map_region", "pos_y", 0.0),
        // 楼层号从 1 起：0 或负数会让完成度面板显示「第 0 层」
        G("dungeon_room", "floor", 1.0),
        // 2026-10-03 用变异探针扫出来的五条（两边的网都没盖住，见决策 157）
        G("status_effect", "max_stack", 1.0),
        G("status_effect", "duration", 1.0),
        G("status_effect", "base_chance", 0.0, 1.0),
        G("item_base", "sell_price", 0.0),
        G("enemy_base", "money", 0.0),
    };

    /// <summary>`level_growth` 的基础数值列：升级不该让人变弱，这些列随等级不许下降（允许持平）。</summary>
    internal static readonly string[] LEVEL_BASE_COLUMNS =
    {
        "base_hp", "base_qi", "base_atk_phys", "base_atk_qi",
        "base_def_phys", "base_def_qi", "base_speed",
    };

    /// <summary>关系规则：left ≤ right。</summary>
    internal static readonly RelationRule[] RELATIONS =
    {
        L("affix_pool", "value_min", "value_max", "词条区间"),
        L("drop_table", "qty_min", "qty_max", "掉落数量区间"),
        L("stat_def", "min_value", "max_value", "派生数值上下限"),
        // 稀有度 → 词条条数区间：写成 min>max 不会炸（`AffixRoller` 有 `maxi` 兜着），
        // 结果是每件都只出 min 条——看着正常、分布却没了，所以按区间比一次。
        L("rarity_def", "affix_min", "affix_max", "词条数量区间"),
    };

    /// <summary>
    /// 九步管线里有五个比率**必须**有上限，代码取的是 `stat_def.max_value`。
    /// 表里留空（= 0 = 本表约定「无上限」）时代码会**静默换用兜底常数**，表与行为分家且无人报错
    /// （2026-10-03 查兜底常数时发现，决策 155／180）。
    /// </summary>
    internal static readonly string[] REQUIRED_CAPPED_STATS =
        { "pen_rate", "block_rate", "block_reduction", "dmg_reduction", "res_internal" };

    /// <summary>
    /// 入口：把加载错误原样带回（缺表时引用校验会刷屏，直接返回），否则逐族跑规则。
    /// 顺序与原 GDScript 一致——报错顺序会影响第一次失败时看到哪一条。
    /// </summary>
    public static string[] validate(TableDb db)
    {
        var errors = new List<string>();
        foreach (string message in db.errors)
        {
            errors.Add(message);
        }

        if (errors.Count != 0)
        {
            return errors.ToArray();
        }

        TableValidatorStructure.CheckPrimaryKeys(db, errors);
        TableValidatorStructure.CheckEnums(db, errors);
        TableValidatorStructure.CheckStatusRules(db, errors);
        TableValidatorStructure.CheckTriggerConditions(db, errors);
        TableValidatorStructure.CheckAiTemplates(db, errors);
        TableValidatorStructure.CheckReferences(db, errors);
        TableValidatorActors.CheckDropItemRefs(db, errors);
        TableValidatorActors.CheckHiddenRewardRefs(db, errors);
        TableValidatorActors.CheckEnemyTeamMembers(db, errors);
        TableValidatorActors.CheckTrainingFaction(db, errors);
        TableValidatorActors.CheckEnemySkillSlots(db, errors);
        TableValidatorActors.CheckEnemyPassiveCapacity(db, errors);
        TableValidatorActors.CheckCharacterAttrTotal(db, errors);
        TableValidatorStructure.CheckAttributeAllocatable(db, errors);
        TableValidatorActors.CheckLevelGrowthRows(db, errors);
        TableValidatorActors.CheckEquipLevelReqs(db, errors);
        TableValidatorActors.CheckDungeonFloors(db, errors);
        TableValidatorActors.CheckEventSources(db, errors);
        TableValidatorActors.CheckEventRewards(db, errors);
        TableValidatorActors.CheckEventSkillLevels(db, errors);
        TableValidatorActors.CheckShopItemRefs(db, errors);
        TableValidatorActors.CheckShopPrices(db, errors);
        TableValidatorWorld.CheckBuildingRows(db, errors);
        TableValidatorWorld.CheckGuideAndRecruit(db, errors);
        TableValidatorActors.CheckNpcGuards(db, errors);
        TableValidatorWorld.CheckDialogue(db, errors);
        TableValidatorWorld.CheckNpcPeople(db, errors);
        TableValidatorActors.CheckWeaponSlotConsistency(db, errors);
        TableValidatorActors.CheckSkillDetails(db, errors);
        TableValidatorActors.CheckSkillWeaponTypes(db, errors);
        TableValidatorActors.CheckPassiveStats(db, errors);
        TableValidatorActors.CheckAffixTargets(db, errors);
        TableValidatorActors.CheckEquipBonuses(db, errors);
        TableValidatorActors.CheckGrowthConstants(db, errors);
        TableValidatorStructure.CheckStarDef(db, errors);
        TableValidatorStructure.CheckRanges(db, errors);
        TableValidatorStructure.CheckRelations(db, errors);
        TableValidatorStructure.CheckPipelineCaps(db, errors);
        TableValidatorStructure.CheckDiminishing(db, errors);
        TableValidatorActors.CheckStartSkillsUsable(db, errors);
        TableValidatorActors.CheckStartEquipmentUsable(db, errors);
        TableValidatorWorld.CheckDifficultyLadder(db, errors);
        TableValidatorWorld.CheckDifficultyUnlockConditions(db, errors);
        TableValidatorWorld.CheckGrowthConstantsPresent(db, errors);
        TableValidatorWorld.CheckGrowthConstRanges(db, errors);
        TableValidatorStructure.CheckLockedScenesExist(db, errors);
        TableValidatorWorld.CheckBuffTables(db, errors);
        TableValidatorActors.CheckCharacterEventSkills(db, errors);
        TableValidatorActors.CheckSkillDamageTypesSupported(db, errors);
        TableValidatorWorld.CheckCreationTables(db, errors);
        TableValidatorWorld.CheckStoryAndGuides(db, errors);
        TableValidatorActors.CheckEnemyWeaponMatchesSkills(db, errors);
        TableValidatorStructure.CheckIconSelfReference(db, errors);
        TableValidatorWorld.CheckMapRegionRules(db, errors);
        return errors.ToArray();
    }

    // ---------------------------------------------------------------- 过渡期给 GDScript 的取值口
    //
    // GDScript 读不到 C# 的字段（`Invalid access to property or key … on 'CSharpScript'`，2026-10-10 实测），
    // 所以四张规则表各留一个静态方法。P5 把 GDScript 清零之后把它连着调用方一起删掉。
    // **别把 Godot 集合放进静态字段**（选成 `static readonly Dictionary` 会让终结器在引擎拆完后跑，
    // 退出码非零的假红——`TableRegistry` 那份注释里有完整现场），这里一律用完即建。

    /// <summary>表名 → { 列名: 允许取值（PackedStringArray） }。</summary>
    public static Godot.Collections.Dictionary enums()
    {
        var out_enums = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, Dictionary<string, string[]>> table in ENUMS)
        {
            var columns = new Godot.Collections.Dictionary();
            foreach (KeyValuePair<string, string[]> column in table.Value)
            {
                columns[column.Key] = StringArray(column.Value);
            }

            out_enums[table.Key] = columns;
        }

        return out_enums;
    }

    /// <summary>引用规则表，元素形如 {table, column, target[, target_column][, split][, allow_empty]}。</summary>
    public static Godot.Collections.Array references()
    {
        var out_rules = new Godot.Collections.Array();
        foreach (ReferenceRule rule in REFERENCES)
        {
            var entry = new Godot.Collections.Dictionary
            {
                ["table"] = rule.Table,
                ["column"] = rule.Column,
                ["target"] = rule.Target,
            };
            if (!string.IsNullOrEmpty(rule.TargetColumn))
            {
                entry["target_column"] = rule.TargetColumn;
            }

            if (!string.IsNullOrEmpty(rule.Split))
            {
                entry["split"] = rule.Split;
            }

            if (rule.AllowEmpty)
            {
                entry["allow_empty"] = true;
            }

            out_rules.Add(entry);
        }

        return out_rules;
    }

    /// <summary>区间规则表，元素形如 {table, column[, min][, max]}。</summary>
    public static Godot.Collections.Array ranges()
    {
        var out_rules = new Godot.Collections.Array();
        foreach (RangeRule rule in RANGES)
        {
            var entry = new Godot.Collections.Dictionary { ["table"] = rule.Table, ["column"] = rule.Column };
            if (rule.Min.HasValue)
            {
                entry["min"] = rule.Min.Value;
            }

            if (rule.Max.HasValue)
            {
                entry["max"] = rule.Max.Value;
            }

            out_rules.Add(entry);
        }

        return out_rules;
    }

    /// <summary>关系规则表，元素形如 {table, left, right, label}。</summary>
    public static Godot.Collections.Array relations()
    {
        var out_rules = new Godot.Collections.Array();
        foreach (RelationRule rule in RELATIONS)
        {
            out_rules.Add(new Godot.Collections.Dictionary
            {
                ["table"] = rule.Table,
                ["left"] = rule.Left,
                ["right"] = rule.Right,
                ["label"] = rule.Label,
            });
        }

        return out_rules;
    }

    /// <summary>等级上限：`level_growth` 里最大的那一级（与 `LevelService.max_level()` 同一口径）。</summary>
    public static int max_level(TableDb db)
    {
        var top = 0;
        foreach (Resource row in Rows(db, "level_growth"))
        {
            top = Mathf.Max(top, Int(row, "level"));
        }

        return top;
    }

    // ---------------------------------------------------------------- 共享工具

    /// <summary>把一张表的行取成 `Resource` 列表（行类都是 `TableRow` 子类）。</summary>
    internal static List<Resource> Rows(TableDb db, string table_name)
    {
        var out_rows = new List<Resource>();
        foreach (Variant entry in db.rows(table_name))
        {
            out_rows.Add(entry.As<Resource>());
        }

        return out_rows;
    }

    internal static string Str(GodotObject row, string property) => row.Get(property).AsString();

    internal static int Int(GodotObject row, string property) => (int)row.Get(property).AsInt64();

    internal static double Num(GodotObject row, string property) => row.Get(property).AsDouble();

    internal static bool Flag(GodotObject row, string property) => row.Get(property).AsBool();

    /// <summary>取字符串列并去首尾空白（对位 GDScript 的 `str(row.x).strip_edges()`）。</summary>
    internal static string Trimmed(GodotObject row, string property) => Str(row, property).Trim();

    /// <summary>行 id（`TableRow.id` 是构建期写进去的，等于主键列拼出来的值）。</summary>
    internal static string RowId(GodotObject row) => Str(row, "id");

    /// <summary>把一个 Variant 数组（PackedStringArray／Array）读成字符串数组。</summary>
    internal static List<string> ToStrings(Variant value)
    {
        var out_values = new List<string>();
        if (value.VariantType == Variant.Type.PackedStringArray)
        {
            foreach (string entry in value.AsStringArray())
            {
                out_values.Add(entry);
            }

            return out_values;
        }

        if (value.VariantType == Variant.Type.String)
        {
            out_values.Add(value.AsString());
            return out_values;
        }

        foreach (Variant entry in value.AsGodotArray())
        {
            out_values.Add(entry.AsString());
        }

        return out_values;
    }

    internal static string Joined(System.Collections.Generic.IEnumerable<string> values) =>
        string.Join(", ", values);

    /// <summary>某张表某一列的取值集合（引用校验用；与 `TableDb.column_values` 同一口径）。</summary>
    internal static Godot.Collections.Dictionary ColumnValues(TableDb db, string table_name, string column)
    {
        var out_values = new Godot.Collections.Dictionary();
        List<Resource> rows = Rows(db, table_name);
        if (rows.Count == 0)
        {
            return out_values;
        }

        string property = TableRegistry.property_of(table_name, column);
        foreach (Resource row in rows)
        {
            out_values[Str(row, property)] = true;
        }

        return out_values;
    }

    /// <summary>这个行对象上有没有这个属性（规则表里列名写错时要能识别出来，而不是 `float(null)` 崩溃）。</summary>
    internal static bool HasProperty(GodotObject row, string property_name)
    {
        if (string.IsNullOrEmpty(property_name))
        {
            return false;
        }

        foreach (Godot.Collections.Dictionary entry in row.GetPropertyList())
        {
            if (entry["name"].AsString() == property_name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>`attr:x` / `stat:x` → {kind, target_id}；没有前缀时两项都是空串。</summary>
    internal static (string Kind, string TargetId) ParsePrefixedTarget(string target)
    {
        if (target.StartsWith("attr:"))
        {
            return ("attr", target.Substring("attr:".Length));
        }

        if (target.StartsWith("stat:"))
        {
            return ("stat", target.Substring("stat:".Length));
        }

        return ("", "");
    }

    /// <summary>引用取值的来源：写了 target_column 就取那一列，否则取目标表的第一个主键列。</summary>
    internal static Godot.Collections.Dictionary TargetValues(TableDb db, ReferenceRule spec)
    {
        if (!string.IsNullOrEmpty(spec.TargetColumn))
        {
            return db.column_values(spec.Target, spec.TargetColumn);
        }

        Godot.Collections.Dictionary primary = TableRegistry.primary_keys();
        if (!primary.ContainsKey(spec.Target))
        {
            return new Godot.Collections.Dictionary();
        }

        var keys = primary[spec.Target].AsGodotArray();
        if (keys.Count == 0)
        {
            return new Godot.Collections.Dictionary();
        }

        return db.column_values(spec.Target, keys[0].AsString());
    }

    /// <summary>给的**东西**也要真的存在：写错一个字母＝玩家什么也拿不到、而且没有任何提示。</summary>
    internal static void CheckPersonItem(
        TableDb db, string table_name, string row_id, string item_id, List<string> errors
    )
    {
        string token = item_id.Trim();
        if (token.Length == 0)
        {
            return;
        }

        if (db.get_row("item_base", token) == null && db.get_row("equip_base", token) == null)
        {
            errors.Add(
                $"{table_name}[{row_id}] 给的 {token} 既不是道具也不是装备——玩家拿不到，而且不会有提示"
            );
        }
    }

    private static Godot.Collections.Array StringArray(string[] values)
    {
        var out_array = new Godot.Collections.Array();
        foreach (string value in values)
        {
            out_array.Add(value);
        }

        return out_array;
    }
}
