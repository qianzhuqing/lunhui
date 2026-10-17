using Godot;

namespace Lunhui.Data;

/// <summary>
/// 配置表注册表（C# 版，对位原 `src/data/table_registry.gd`）：表清单、行类脚本、主键、列别名的唯一来源。
///
/// `tools/BuildTables.cs`（构建期）与 `src/core/TableDb.cs`（运行期）都读这里；
/// 新增一张表＝在 `data/tables/` 放 CSV、在 `src/data/tables/` 放行类，再登记到本文件三处
/// （<see cref="TABLES"/>／<see cref="ROW_SCRIPT_PATHS"/>／<see cref="PRIMARY_KEY_COLUMNS"/>）。
///
/// **两条形状上的硬约束，改之前先读这两段**：
///
/// ① **静态字段里只放纯托管类型**（`string[]`／`Dictionary&lt;string,…&gt;`），
/// Godot 集合**用完即建、当场交给调用方**。原因是一次真事故（2026-10-10）：把
/// `Godot.Collections.Dictionary` 放进 `static readonly` 之后，进程退出时 GC 才去回收它们，
/// 而那时引擎已经拆完，`GodotObject.Finalize()` 调 native 直接抛
/// `Fatal error. System.Runtime.InteropServices.SEHException` ——场景自检本身 `OK`，
/// 退出码却是非零，`run_all_checks` 报 `[FAIL] scene:shop`。
///
/// ② **数据一律经静态方法取**：迁移期还有 GDScript 调用方（`tools/BuildTables.cs`／
/// `src/core/TableValidator.cs`／`tests/test_table_pipeline.gd`／`test_handshake_docs.gd`），
/// 而 GDScript **读不到** C# 的 `const`／静态字段
/// （`Invalid access to property or key … on 'CSharpScript'`，2026-10-10 实测），只能调静态方法。
/// P5 全部搬完、GDScript 清零后可以把这些方法收回，只留字段。
/// </summary>
public partial class TableRegistry : RefCounted
{
    public const string TABLES_DIR = "res://data/tables";
    public const string OUTPUT_DIR = "res://data/generated";

    /// <summary>表清单，顺序与 docs/design/06_配置表说明.md 的分组一致。</summary>
    public static readonly string[] TABLES =
    {
        // 角色与属性
        "attribute_def",
        "stat_def",
        "curve_def",
        "attr_to_stat",
        "level_growth",
        "character_base",
        "character_base_skill",
        "event_skill_def",
        "event_check",
        // 引导与招募（0.10.0 新增，见 docs/design/09_试玩修正.md）
        "guide_step",
        "recruit_def",
        "ui_text",
        // 创建角色（0.16.0／0.17.0 新增，见 docs/design/12_天赋系统.md、13_创建角色流程.md）
        "origin_def",
        "talent_def",
        "talent_effect",
        // 主线与章节（0.22.0 新增，见 docs/design/18_主线流程与章节.md）
        "chapter_def",
        "story_node",
        // NPC 与大地图事件（设计 19 交付，见 docs/design/19_NPC与大地图事件.md）
        "npc_def",
        "npc_favor",
        "npc_offer",
        "npc_quest",
        "world_event",
        // 观察点（设计 20 §3.2，0.29.1）：最薄的一张表——一个位点一句话，不参与任何数值
        "flavor_point",
        // 对话容器（设计 20 §十一）：台词与选项的出口，剧情内容的前置
        "dialogue_node",
        "dialogue_option",
        // 战斗
        "damage_type",
        "element_def",
        "element_counter",
        "status_effect",
        "skill_base",
        "skill_active",
        "skill_passive",
        "skill_passive_stat",
        "skill_mastery_bonus",
        "skill_star_def",
        "combat_const",
        // 增益与套装（0.6.2 新增，见 docs/design/08_增益与套装.md）
        "buff_def",
        "buff_stat",
        "buff_grant",
        "set_def",
        "set_member",
        "set_bonus",
        "feature_toggle",
        // 成长
        "growth_const",
        // 装备与物品
        "rarity_def",
        "equip_base",
        "equip_slot_def",
        "weapon_type_def",
        "affix_pool",
        "item_base",
        // 商店与经济
        "building_def",
        "shop_stock",
        // 敌人与掉落
        "enemy_base",
        "enemy_team",
        "enemy_skill",
        "enemy_equip",
        "enemy_passive",
        "drop_table",
        "difficulty_config",
        "difficulty_drop_rate",
        // 地图与副本
        "map_region",
        "map_local",
        "roaming_spawn",
        "dungeon_room",
        "hidden_trigger",
        "npc_guard",
    };

    /// <summary>表名 → 行类脚本路径。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> ROW_SCRIPT_PATHS = new()
    {
        { "attribute_def", "res://src/data/tables/AttributeDefRow.cs" },
        { "stat_def", "res://src/data/tables/StatDefRow.cs" },
        { "curve_def", "res://src/data/tables/CurveDefRow.cs" },
        { "attr_to_stat", "res://src/data/tables/AttrToStatRow.cs" },
        { "level_growth", "res://src/data/tables/LevelGrowthRow.cs" },
        { "character_base", "res://src/data/tables/CharacterBaseRow.cs" },
        { "character_base_skill", "res://src/data/tables/CharacterBaseSkillRow.cs" },
        { "event_skill_def", "res://src/data/tables/EventSkillDefRow.cs" },
        { "event_check", "res://src/data/tables/EventCheckRow.cs" },
        { "guide_step", "res://src/data/tables/GuideStepRow.cs" },
        { "recruit_def", "res://src/data/tables/RecruitDefRow.cs" },
        { "ui_text", "res://src/data/tables/UiTextRow.cs" },
        { "origin_def", "res://src/data/tables/OriginDefRow.cs" },
        { "talent_def", "res://src/data/tables/TalentDefRow.cs" },
        { "talent_effect", "res://src/data/tables/TalentEffectRow.cs" },
        { "chapter_def", "res://src/data/tables/ChapterDefRow.cs" },
        { "story_node", "res://src/data/tables/StoryNodeRow.cs" },
        { "npc_def", "res://src/data/tables/NpcDefRow.cs" },
        { "npc_favor", "res://src/data/tables/NpcFavorRow.cs" },
        { "npc_offer", "res://src/data/tables/NpcOfferRow.cs" },
        { "npc_quest", "res://src/data/tables/NpcQuestRow.cs" },
        { "world_event", "res://src/data/tables/WorldEventRow.cs" },
        { "flavor_point", "res://src/data/tables/FlavorPointRow.cs" },
        { "dialogue_node", "res://src/data/tables/DialogueNodeRow.cs" },
        { "dialogue_option", "res://src/data/tables/DialogueOptionRow.cs" },
        { "damage_type", "res://src/data/tables/DamageTypeRow.cs" },
        { "element_def", "res://src/data/tables/ElementDefRow.cs" },
        { "element_counter", "res://src/data/tables/ElementCounterRow.cs" },
        { "status_effect", "res://src/data/tables/StatusEffectRow.cs" },
        { "skill_base", "res://src/data/tables/SkillBaseRow.cs" },
        { "skill_active", "res://src/data/tables/SkillActiveRow.cs" },
        { "skill_passive", "res://src/data/tables/SkillPassiveRow.cs" },
        { "skill_passive_stat", "res://src/data/tables/SkillPassiveStatRow.cs" },
        { "skill_mastery_bonus", "res://src/data/tables/SkillMasteryBonusRow.cs" },
        { "skill_star_def", "res://src/data/tables/SkillStarDefRow.cs" },
        { "combat_const", "res://src/data/tables/CombatConstRow.cs" },
        { "buff_def", "res://src/data/tables/BuffDefRow.cs" },
        { "buff_stat", "res://src/data/tables/BuffStatRow.cs" },
        { "buff_grant", "res://src/data/tables/BuffGrantRow.cs" },
        { "set_def", "res://src/data/tables/SetDefRow.cs" },
        { "set_member", "res://src/data/tables/SetMemberRow.cs" },
        { "set_bonus", "res://src/data/tables/SetBonusRow.cs" },
        { "feature_toggle", "res://src/data/tables/FeatureToggleRow.cs" },
        { "growth_const", "res://src/data/tables/GrowthConstRow.cs" },
        { "rarity_def", "res://src/data/tables/RarityDefRow.cs" },
        { "equip_base", "res://src/data/tables/EquipBaseRow.cs" },
        { "equip_slot_def", "res://src/data/tables/EquipSlotDefRow.cs" },
        { "weapon_type_def", "res://src/data/tables/WeaponTypeDefRow.cs" },
        { "affix_pool", "res://src/data/tables/AffixPoolRow.cs" },
        { "item_base", "res://src/data/tables/ItemBaseRow.cs" },
        { "building_def", "res://src/data/tables/BuildingDefRow.cs" },
        { "shop_stock", "res://src/data/tables/ShopStockRow.cs" },
        { "enemy_base", "res://src/data/tables/EnemyBaseRow.cs" },
        { "enemy_team", "res://src/data/tables/EnemyTeamRow.cs" },
        { "enemy_skill", "res://src/data/tables/EnemySkillRow.cs" },
        { "enemy_equip", "res://src/data/tables/EnemyEquipRow.cs" },
        { "enemy_passive", "res://src/data/tables/EnemyPassiveRow.cs" },
        { "drop_table", "res://src/data/tables/DropTableRow.cs" },
        { "difficulty_config", "res://src/data/tables/DifficultyConfigRow.cs" },
        { "difficulty_drop_rate", "res://src/data/tables/DifficultyDropRateRow.cs" },
        { "map_region", "res://src/data/tables/MapRegionRow.cs" },
        { "map_local", "res://src/data/tables/MapLocalRow.cs" },
        { "roaming_spawn", "res://src/data/tables/RoamingSpawnRow.cs" },
        { "dungeon_room", "res://src/data/tables/DungeonRoomRow.cs" },
        { "hidden_trigger", "res://src/data/tables/HiddenTriggerRow.cs" },
        { "npc_guard", "res://src/data/tables/NpcGuardRow.cs" },
    };

    /// <summary>表名 → 主键列名（顺序即 id 拼接顺序）。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string[]> PRIMARY_KEY_COLUMNS = new()
    {
        { "attribute_def", new[] { "attr_id" } },
        { "stat_def", new[] { "stat_id" } },
        { "curve_def", new[] { "curve_id" } },
        { "attr_to_stat", new[] { "id" } },
        { "level_growth", new[] { "level" } },
        { "character_base", new[] { "char_id" } },
        { "character_base_skill", new[] { "char_id", "skill_id" } },
        { "event_skill_def", new[] { "skill_id" } },
        { "event_check", new[] { "check_id" } },
        { "guide_step", new[] { "step_id" } },
        { "recruit_def", new[] { "char_id" } },
        { "ui_text", new[] { "text_id" } },
        { "origin_def", new[] { "origin_id" } },
        { "talent_def", new[] { "talent_id" } },
        { "talent_effect", new[] { "talent_id", "target" } },
        { "chapter_def", new[] { "chapter_id" } },
        { "story_node", new[] { "node_id" } },
        { "npc_def", new[] { "npc_id" } },
        { "npc_favor", new[] { "npc_id" } },
        { "npc_offer", new[] { "offer_id" } },
        { "npc_quest", new[] { "quest_id" } },
        { "world_event", new[] { "event_id" } },
        { "flavor_point", new[] { "point_id" } },
        { "dialogue_node", new[] { "node_id" } },
        { "dialogue_option", new[] { "option_id" } },
        { "damage_type", new[] { "type_id" } },
        { "element_def", new[] { "element_id" } },
        { "element_counter", new[] { "element_atk", "element_def" } },
        { "status_effect", new[] { "status_id" } },
        { "skill_base", new[] { "skill_id" } },
        { "skill_active", new[] { "skill_id" } },
        { "skill_passive", new[] { "skill_id" } },
        { "skill_passive_stat", new[] { "skill_id", "target" } },
        { "skill_mastery_bonus", new[] { "skill_id", "mastery", "target" } },
        { "skill_star_def", new[] { "star" } },
        { "combat_const", new[] { "const_id" } },
        { "buff_def", new[] { "buff_id" } },
        { "buff_stat", new[] { "buff_id", "target" } },
        { "buff_grant", new[] { "grant_id" } },
        { "set_def", new[] { "set_id" } },
        { "set_member", new[] { "set_id", "member_id" } },
        { "set_bonus", new[] { "set_id", "required_count" } },
        { "feature_toggle", new[] { "toggle_id" } },
        { "growth_const", new[] { "const_id" } },
        { "rarity_def", new[] { "rarity_id" } },
        { "equip_base", new[] { "equip_id" } },
        { "equip_slot_def", new[] { "slot_id" } },
        { "weapon_type_def", new[] { "weapon_type" } },
        { "affix_pool", new[] { "affix_id" } },
        { "item_base", new[] { "item_id" } },
        { "building_def", new[] { "building_id" } },
        { "shop_stock", new[] { "shop_id", "item_id" } },
        { "enemy_base", new[] { "enemy_id" } },
        { "enemy_team", new[] { "team_id" } },
        { "enemy_skill", new[] { "enemy_id", "skill_id" } },
        { "enemy_equip", new[] { "enemy_id", "slot_id" } },
        { "enemy_passive", new[] { "enemy_id", "skill_id" } },
        { "drop_table", new[] { "drop_row_id" } },
        { "difficulty_config", new[] { "difficulty_id" } },
        { "difficulty_drop_rate", new[] { "difficulty_id", "rarity_id" } },
        { "map_region", new[] { "node_id" } },
        { "map_local", new[] { "scene_id" } },
        { "roaming_spawn", new[] { "spawn_id" } },
        { "dungeon_room", new[] { "room_id" } },
        { "hidden_trigger", new[] { "trigger_id" } },
        { "npc_guard", new[] { "guard_id" } },
    };

    /// <summary>
    /// 表名 → { CSV 列名: 行类属性名 }，仅当两者不一致时登记。
    /// `id` 与 `exp` 在旧 GDScript 里分别是基类字段名与全局函数名，需要改名避让（C# 侧照旧沿用这套别名）。
    /// **写成三元组再拼**：纯字典字面量在 C# 里是嵌套的 `{ { … } }`，
    /// 而 `tools/validate_tables.ps1` 是按文本解析这份文件的（正则读不了嵌套花括号），三元组一行一条最省事。
    /// </summary>
    private static readonly (string Table, string Column, string Property)[] ALIAS_TRIPLES =
    {
        ("attr_to_stat", "id", "row_id"),
        ("enemy_base", "exp", "exp_reward"),
    };

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>
        COLUMN_ALIAS_MAP = BuildColumnAliases();

    // ---------------------------------------------------------------- 对 GDScript 的过渡接口

    public static string[] tables() => TABLES;

    public static Godot.Collections.Dictionary row_scripts()
    {
        var out_scripts = new Godot.Collections.Dictionary();
        foreach (System.Collections.Generic.KeyValuePair<string, string> entry in ROW_SCRIPT_PATHS)
        {
            out_scripts[entry.Key] = entry.Value;
        }

        return out_scripts;
    }

    public static Godot.Collections.Dictionary primary_keys()
    {
        var out_keys = new Godot.Collections.Dictionary();
        foreach (System.Collections.Generic.KeyValuePair<string, string[]> entry in PRIMARY_KEY_COLUMNS)
        {
            var columns = new Godot.Collections.Array();
            foreach (string column in entry.Value)
            {
                columns.Add(column);
            }

            out_keys[entry.Key] = columns;
        }

        return out_keys;
    }

    public static string tables_dir() => TABLES_DIR;

    public static string output_dir() => OUTPUT_DIR;

    /// <summary>行类属性名 → CSV 列名。</summary>
    public static string column_of(string table_name, string property_name)
    {
        System.Collections.Generic.Dictionary<string, string> aliases = AliasTable(table_name);
        foreach (System.Collections.Generic.KeyValuePair<string, string> pair in aliases)
        {
            if (pair.Value == property_name)
            {
                return pair.Key;
            }
        }

        return property_name;
    }

    /// <summary>CSV 列名 → 行类属性名。</summary>
    public static string property_of(string table_name, string column)
    {
        System.Collections.Generic.Dictionary<string, string> aliases = AliasTable(table_name);
        return aliases.TryGetValue(column, out string property) ? property : column;
    }

    /// <summary>复合主键拼成 id。</summary>
    public static string build_id(string table_name, Godot.Collections.Dictionary values)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (string column in PrimaryKeysOf(table_name))
        {
            parts.Add(values.ContainsKey(column) ? values[column].AsString() : "");
        }

        return string.Join("|", parts);
    }

    private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>
        BuildColumnAliases()
    {
        var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
        foreach ((string table, string column, string property) in ALIAS_TRIPLES)
        {
            if (!map.TryGetValue(table, out System.Collections.Generic.Dictionary<string, string> aliases))
            {
                aliases = new System.Collections.Generic.Dictionary<string, string>();
                map[table] = aliases;
            }

            aliases[column] = property;
        }

        return map;
    }

    private static System.Collections.Generic.Dictionary<string, string> AliasTable(string table_name)
    {
        return COLUMN_ALIAS_MAP.TryGetValue(table_name, out System.Collections.Generic.Dictionary<string, string> aliases)
            ? aliases
            : new System.Collections.Generic.Dictionary<string, string>();
    }

    private static string[] PrimaryKeysOf(string table_name)
    {
        return PRIMARY_KEY_COLUMNS.TryGetValue(table_name, out string[] columns)
            ? columns
            : System.Array.Empty<string>();
    }
}
