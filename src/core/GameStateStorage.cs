using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `GameState` 的存档读写与版本迁移（对位原 `src/core/game_state.gd` 的 `to_dict`／`from_dict`
/// 与一串 `_read_*`）。拆出来的理由：单文件 800 行硬上限。
///
/// **迁移期的两条铁律**（改这份文件之前先读 `src/core/AGENTS.md` 与决策 377）：
///   1. **字段名与类型不许变**：存档 v1 起一路兼容，改结构必须升 `VERSION` 并在这里写迁移分支；
///   2. **迁移顺序有意义**：`_read_custom_templates()`（注入自建角色）必须早于
///      `_prune_unknown_characters()`（剔掉模板下架的角色），否则自建角色会被当成下架清掉。
/// </summary>
public partial class GameState
{
    public Godot.Collections.Dictionary to_dict()
    {
        Inventory inv = Inv ?? new Inventory();
        return new Godot.Collections.Dictionary
        {
            { "version", version },
            { "char_ids", char_ids.Duplicate() },
            { "char_levels", char_levels.Duplicate() },
            { "difficulty_id", difficulty_id },
            { "chapter_id", chapter_id },
            { "slot", slot },
            { "created_unix", created_unix },
            { "saved_unix", saved_unix },
            { "inventory", inv.to_dict() },
            { "char_allocations", char_allocations.Duplicate(true) },
            { "talent_picks", talent_picks.Duplicate(true) },
            { "custom_templates", custom_templates.Duplicate(true) },
            { "npc_favor", npc_favor.Duplicate() },
            { "party_exp", party_exp },
            { "skill_mastery", skill_mastery.Duplicate(true) },
            { "char_learned", char_learned.Duplicate(true) },
            { "char_loadouts", char_loadouts.Duplicate(true) },
            { "shop_state", shop_state.Duplicate(true) },
            { "dungeon_records", dungeon_records.Duplicate(true) },
            { "pity", pity.Duplicate(true) },
            { "revealed_nodes", revealed_nodes.Duplicate() },
            { "event_checks", event_checks.Duplicate(true) },
            { "flags", flags.Duplicate(true) },
            { "first_kills", first_kills.Duplicate(true) },
            { "char_hp", char_hp.Duplicate(true) },
            { "field_buffs", field_buffs.Duplicate(true) },
            { "world_pos", world_pos.Duplicate(true) },
        };
    }

    /// <summary>结构是否像一份存档（存档文件损坏时用来判定）。</summary>
    public static bool is_valid_dict(Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        Godot.Collections.Dictionary dictionary = data.AsGodotDictionary();
        if (!dictionary.ContainsKey("char_ids") || dictionary["char_ids"].VariantType != Variant.Type.Array)
        {
            return false;
        }

        if (dictionary["char_ids"].AsGodotArray().Count == 0)
        {
            return false;
        }

        if (!dictionary.ContainsKey("char_levels") || dictionary["char_levels"].VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        if (!dictionary.ContainsKey("difficulty_id") || dictionary["difficulty_id"].VariantType != Variant.Type.String)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 读一份存档。db 用于旧版本迁移（补初始装备）；不传 db 时背包会是空的。
    /// **两个参数都要传**（没有默认参数，见坑⑲）。
    /// </summary>
    public static GameState from_dict(Godot.Collections.Dictionary data, TableDb db)
    {
        if (!is_valid_dict(data))
        {
            return null;
        }

        var state = new GameState();
        state.version = data.ContainsKey("version") ? data["version"].AsInt32() : VERSION;
        int source_version = state.version;
        state.char_ids = data["char_ids"].AsGodotArray().Duplicate();
        state.char_levels = new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary levels = data["char_levels"].AsGodotDictionary();
        foreach (Variant key in levels.Keys)
        {
            state.char_levels[key.AsString()] = levels[key].AsInt32();
        }

        state.difficulty_id = data["difficulty_id"].AsString();
        state.chapter_id = data.ContainsKey("chapter_id") ? data["chapter_id"].AsString() : DEFAULT_CHAPTER;
        state.slot = _get_int(data, "slot", 0);
        state.created_unix = _get_int(data, "created_unix", 0);
        state.saved_unix = _get_int(data, "saved_unix", 0);
        if (source_version >= 2)
        {
            state.inventory = Inventory.from_dict(
                data.ContainsKey("inventory") ? data["inventory"] : new Godot.Collections.Dictionary());
            // 配表改过槽位（删槽位／max_equip 改小）时，把存档里装不下的穿戴记录退回背包：
            // 不这么做它们会「界面看不见、却照常加属性」（见 inventory.reclaim_stranded_equipped）。
            if (db != null)
            {
                int reclaimed_total = 0;
                // 键先拷一份：`reclaim_stranded_equipped()` 会就地改 `equipped`，
                // 直接遍历活视图会撞「集合被改过」（GDScript 那边 `.keys()` 本来就是个新数组）。
                var equipped_ids = new System.Collections.Generic.List<Variant>();
                foreach (Variant key in ((Inventory)state.inventory).equipped.Keys)
                {
                    equipped_ids.Add(key);
                }

                foreach (Variant key in equipped_ids)
                {
                    string char_id = key.AsString();
                    string[] reclaimed = ((Inventory)state.inventory).reclaim_stranded_equipped(db, char_id);
                    if (reclaimed.Length > 0)
                    {
                        reclaimed_total += reclaimed.Length;
                        GD.PushWarning($"[GameState] 配表装不下的穿戴记录已退回背包（{char_id}）：{string.Join(", ", reclaimed)}");
                    }
                }

                state.reclaimed_equipped = reclaimed_total;
            }

            if (_is_dict(data, "char_allocations"))
            {
                Godot.Collections.Dictionary allocations = data["char_allocations"].AsGodotDictionary();
                foreach (Variant char_id in allocations.Keys)
                {
                    if (allocations[char_id].VariantType != Variant.Type.Dictionary)
                    {
                        continue;
                    }

                    var normalized = new Godot.Collections.Dictionary();
                    Godot.Collections.Dictionary allocation = allocations[char_id].AsGodotDictionary();
                    foreach (Variant attr_id in allocation.Keys)
                    {
                        normalized[attr_id.AsString()] = allocation[attr_id].AsInt32();
                    }

                    state.char_allocations[char_id.AsString()] = normalized;
                }
            }

            state.party_exp = _get_int(data, "party_exp", 0);
        }

        if (source_version >= 3 && _is_dict(data, "skill_mastery"))
        {
            Godot.Collections.Dictionary masteries = data["skill_mastery"].AsGodotDictionary();
            foreach (Variant char_id in masteries.Keys)
            {
                if (masteries[char_id].VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var normalized = new Godot.Collections.Dictionary();
                Godot.Collections.Dictionary entries = masteries[char_id].AsGodotDictionary();
                foreach (Variant skill_id in entries.Keys)
                {
                    normalized[skill_id.AsString()] = entries[skill_id].AsInt32();
                }

                state.skill_mastery[char_id.AsString()] = normalized;
            }
        }

        if (source_version >= 4)
        {
            state._read_learned(data);
            state._read_loadouts(data);
        }

        if (source_version >= 6)
        {
            state._read_shop_state(data);
        }

        if (source_version >= 7)
        {
            state._read_dungeon_records(data);
        }

        if (source_version >= 8 && _is_array(data, "revealed_nodes"))
        {
            foreach (Variant node_id in data["revealed_nodes"].AsGodotArray())
            {
                state.reveal_node(node_id.AsString());
            }
        }

        if (source_version >= 9)
        {
            if (_is_dict(data, "event_checks"))
            {
                Godot.Collections.Dictionary checks = data["event_checks"].AsGodotDictionary();
                foreach (Variant check_id in checks.Keys)
                {
                    state.record_event_check(check_id.AsString(), checks[check_id].AsString());
                }
            }

            if (_is_dict(data, "flags"))
            {
                Godot.Collections.Dictionary flags_data = data["flags"].AsGodotDictionary();
                foreach (Variant flag_id in flags_data.Keys)
                {
                    state.set_flag(flag_id.AsString(), flags_data[flag_id].AsBool());
                }
            }
        }

        if (source_version >= 10)
        {
            state._read_first_kills(data);
        }

        if (source_version >= 11)
        {
            state._read_char_hp(data);
        }

        if (source_version >= 12)
        {
            state._read_field_buffs(data);
        }

        if (source_version >= 13)
        {
            state._read_world_pos(data);
        }

        if (source_version >= 14)
        {
            state._read_talent_picks(data);
            // **必须在 `_prune_unknown_characters()` 之前**：自建角色不在任何 CSV 里，
            // 先注入模板，它才不会被当成「模板下架的角色」清掉。
            state._read_custom_templates(data, db);
        }

        if (source_version >= 15)
        {
            state._read_npc_favor(data);
        }

        // 老档补齐：v1 补背包与初始装备，v1~v3 补「已学 + 装配」
        if (source_version < 2)
        {
            state.inventory = new Inventory();
            if (db != null)
            {
                state.seed_starting_equipment(db);
            }
        }

        if (source_version < 4)
        {
            if (db != null)
            {
                state.seed_starting_skills(db);
            }

            state.learn_mastered_skills();
            if (db != null)
            {
                state._fill_empty_loadouts(db);
            }
        }

        // 模板下架的角色要在进游戏前清掉（否则队伍是空的，每场战斗瞬间判负）
        state._prune_unknown_characters(db);
        if (source_version < VERSION)
        {
            state.migrated_from = source_version;
        }

        state.version = VERSION;
        return state;
    }

    /// <summary>老档（v1~v3）补装配：只补空着的，不覆盖新档里玩家自己卸空的槽。</summary>
    private void _fill_empty_loadouts(TableDb db)
    {
        foreach (string char_id in _strings(char_ids))
        {
            Godot.Collections.Dictionary current = loadout_of(char_id);
            if (current["active"].AsGodotArray().Count > 0 || current["passive"].AsGodotArray().Count > 0)
            {
                continue;
            }

            if (learned_of(char_id).Length == 0)
            {
                continue;
            }

            SkillLoadout.create(db, this, char_id).fill_from_learned();
        }
    }

    private void _read_dungeon_records(Godot.Collections.Dictionary data)
    {
        if (_is_dict(data, "dungeon_records"))
        {
            Godot.Collections.Dictionary records = data["dungeon_records"].AsGodotDictionary();
            foreach (Variant scene_id in records.Keys)
            {
                if (records[scene_id].VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                Godot.Collections.Dictionary entry = records[scene_id].AsGodotDictionary();
                foreach (string kind in new[] { "chests", "triggers", "rooms", "rooms_entered", "bosses" })
                {
                    if (!entry.ContainsKey(kind) || entry[kind].VariantType != Variant.Type.Array)
                    {
                        continue;
                    }

                    foreach (Variant key in entry[kind].AsGodotArray())
                    {
                        record_dungeon(scene_id.AsString(), kind, key.AsString());
                    }
                }
            }
        }

        if (_is_dict(data, "pity"))
        {
            pity = data["pity"].AsGodotDictionary().Duplicate(true);
        }
    }

    private void _read_shop_state(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "shop_state"))
        {
            return;
        }

        Godot.Collections.Dictionary shops = data["shop_state"].AsGodotDictionary();
        foreach (Variant building_id in shops.Keys)
        {
            if (shops[building_id].VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            string key = building_id.AsString();
            Godot.Collections.Dictionary entry = shops[building_id].AsGodotDictionary();
            if (entry.ContainsKey("purchased") && entry["purchased"].VariantType == Variant.Type.Dictionary)
            {
                Godot.Collections.Dictionary purchased = entry["purchased"].AsGodotDictionary();
                foreach (Variant item_id in purchased.Keys)
                {
                    record_purchase(key, item_id.AsString(), purchased[item_id].AsInt32());
                }
            }

            if (entry.ContainsKey("buyback") && entry["buyback"].VariantType == Variant.Type.Array)
            {
                foreach (Variant record in entry["buyback"].AsGodotArray())
                {
                    if (record.VariantType != Variant.Type.Dictionary)
                    {
                        continue;
                    }

                    // 读档不设上限：上限是给「继续卖东西」用的，历史条目按原样保留
                    push_buyback(key, record.AsGodotDictionary(), 0);
                }
            }
        }
    }

    private void _read_learned(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "char_learned"))
        {
            return;
        }

        Godot.Collections.Dictionary learned_map = data["char_learned"].AsGodotDictionary();
        foreach (Variant char_id in learned_map.Keys)
        {
            if (learned_map[char_id].VariantType != Variant.Type.Array)
            {
                continue;
            }

            Godot.Collections.Array learned = char_learned.ContainsKey(char_id.AsString())
                ? char_learned[char_id.AsString()].AsGodotArray()
                : new Godot.Collections.Array();
            foreach (Variant skill_id in learned_map[char_id].AsGodotArray())
            {
                string id = skill_id.AsString();
                if (!string.IsNullOrEmpty(id) && !learned.Contains(id))
                {
                    learned.Add(id);
                }
            }

            char_learned[char_id.AsString()] = learned;
        }
    }

    private void _read_loadouts(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "char_loadouts"))
        {
            return;
        }

        Godot.Collections.Dictionary loadouts = data["char_loadouts"].AsGodotDictionary();
        foreach (Variant char_id in loadouts.Keys)
        {
            if (loadouts[char_id].VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary entry = loadouts[char_id].AsGodotDictionary();
            set_loadout(
                char_id.AsString(),
                _string_array(entry, "active"),
                _string_array(entry, "passive"));
            set_basic_skill(
                char_id.AsString(),
                entry.ContainsKey("basic") ? entry["basic"].AsString() : "");
        }
    }

    /// <summary>天赋（v14 起）：`char_id → [talent_id]`，只保留表里还存在的天赋。</summary>
    private void _read_talent_picks(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "talent_picks"))
        {
            return;
        }

        Godot.Collections.Dictionary picks_map = data["talent_picks"].AsGodotDictionary();
        foreach (Variant char_id in picks_map.Keys)
        {
            if (picks_map[char_id].VariantType != Variant.Type.Array)
            {
                continue;
            }

            Godot.Collections.Array picks = picks_map[char_id].AsGodotArray();
            var out_picks = new Godot.Collections.Array();
            foreach (Variant talent_id in picks)
            {
                out_picks.Add(talent_id.AsString());
            }

            talent_picks[char_id.AsString()] = out_picks;
        }
    }

    /// <summary>自建角色模板（v14 起）：读进内存并**立刻注入 db**，后面按 char_id 取模板的地方才查得到。</summary>
    private void _read_custom_templates(Godot.Collections.Dictionary data, TableDb db)
    {
        if (!_is_dict(data, "custom_templates"))
        {
            return;
        }

        Godot.Collections.Dictionary templates = data["custom_templates"].AsGodotDictionary();
        foreach (Variant char_id in templates.Keys)
        {
            if (templates[char_id].VariantType == Variant.Type.Dictionary)
            {
                custom_templates[char_id.AsString()] = templates[char_id].AsGodotDictionary().Duplicate(true);
            }
        }

        apply_custom_templates(db);
    }

    /// <summary>NPC 好感（v15 起）：只收表里还认得的 npc_id；值归一成 int。</summary>
    private void _read_npc_favor(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "npc_favor"))
        {
            return;
        }

        Godot.Collections.Dictionary favors = data["npc_favor"].AsGodotDictionary();
        foreach (Variant npc_id in favors.Keys)
        {
            npc_favor[npc_id.AsString()] = favors[npc_id].AsInt32();
        }
    }

    /// <summary>
    /// 把 `custom_templates` 里的每一份自建模板注入 `character_base`（幂等：同 id 覆盖）。
    ///
    /// 创建流程（建完那一刻）与读档（`_read_custom_templates`）都调它——
    /// 注入是内存行为，CSV 与 `data/generated` 都不动。
    /// </summary>
    public int apply_custom_templates(TableDb db)
    {
        if (db == null || custom_templates.Count == 0)
        {
            return 0;
        }

        int applied = 0;
        foreach (Variant char_id in custom_templates.Keys)
        {
            Godot.Collections.Dictionary spec = custom_templates[char_id].AsGodotDictionary();
            var row = new CharacterBaseRow();
            row.id = char_id.AsString();
            row.char_id = char_id.AsString();
            row.name_cn = _get_string(spec, "name_cn", char_id.AsString());
            row.role_tag = _get_string(spec, "role_tag", "自建");
            row.weapon_type = _get_string(spec, "weapon_type", "");
            Godot.Collections.Dictionary attrs = _is_dict(spec, "attrs")
                ? spec["attrs"].AsGodotDictionary()
                : new Godot.Collections.Dictionary();
            row.initial_str = _get_int(attrs, "str", 0);
            row.initial_con = _get_int(attrs, "con", 0);
            row.initial_agi = _get_int(attrs, "agi", 0);
            row.initial_int = _get_int(attrs, "int", 0);
            row.initial_luk = _get_int(attrs, "luk", 0);
            row.initial_wu = _get_int(attrs, "wu", 0);
            row.initial_gen = _get_int(attrs, "gen", 0);
            row.attr_total = row.attr_sum();
            row.start_level = Mathf.Max(1, _get_int(spec, "start_level", 1));
            row.start_skill_ids = _get_string(spec, "start_skill_ids", "");
            row.start_equip_ids = _get_string(spec, "start_equip_ids", "");
            row.desc = _get_string(spec, "desc", "自建角色（不使用模板）");
            db.inject_row("character_base", row);
            applied += 1;
        }

        return applied;
    }

    /// <summary>首杀记录（v10 起）。只有 true 有意义，读的时候把任何真值归一成 true。</summary>
    private void _read_first_kills(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "first_kills"))
        {
            return;
        }

        Godot.Collections.Dictionary kills = data["first_kills"].AsGodotDictionary();
        foreach (Variant group_id in kills.Keys)
        {
            if (kills[group_id].AsBool())
            {
                first_kills[group_id.AsString()] = true;
            }
        }
    }

    /// <summary>
    /// 战斗外增益（v12 起）：只收形态合法的条目（有 buff_id 与将来的到期时间），
    /// 坏数据直接丢掉——比读进一条「永远不过期」的增益安全。过期的条目留着不碍事：
    /// `GameSession.active_field_buffs(-1)` 一问就会顺手清掉（时钟可注入，用例不必真等）。
    /// </summary>
    private void _read_field_buffs(Godot.Collections.Dictionary data)
    {
        if (!_is_array(data, "field_buffs"))
        {
            return;
        }

        foreach (Variant entry in data["field_buffs"].AsGodotArray())
        {
            if (entry.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            string buff_id = _get_string(row, "buff_id", "");
            int expires_at = _get_int(row, "expires_at", 0);
            if (string.IsNullOrEmpty(buff_id) || expires_at <= 0)
            {
                continue;
            }

            field_buffs.Add(new Godot.Collections.Dictionary
            {
                { "buff_id", buff_id },
                { "source_id", _get_string(row, "source_id", "") },
                { "stacks", Mathf.Max(1, _get_int(row, "stacks", 1)) },
                { "expires_at", expires_at },
            });
        }
    }

    /// <summary>战斗外气血（v11 起）。下限 1 与 set_current_hp 同一口径，坏数据不至于把人写成 0。</summary>
    private void _read_char_hp(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "char_hp"))
        {
            return;
        }

        Godot.Collections.Dictionary hp_map = data["char_hp"].AsGodotDictionary();
        foreach (Variant char_id in hp_map.Keys)
        {
            int value = hp_map[char_id].AsInt32();
            if (value > 0)
            {
                char_hp[char_id.AsString()] = Mathf.Max(1, value);
            }
        }
    }

    /// <summary>
    /// 大地图坐标（v13 起）。只收「x、y 都是数」的条目——写坏的档宁可当作没有记录
    /// （退回默认出生点），也不要读进一个 NaN 把玩家扔到地图外面。
    /// </summary>
    private void _read_world_pos(Godot.Collections.Dictionary data)
    {
        if (!_is_dict(data, "world_pos"))
        {
            return;
        }

        Godot.Collections.Dictionary pair = data["world_pos"].AsGodotDictionary();
        if (!_is_number(pair, "x") || !_is_number(pair, "y"))
        {
            return;
        }

        var pos = new Vector2((float)_number(pair, "x"), (float)_number(pair, "y"));
        if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsInfinity(pos.X) || float.IsInfinity(pos.Y))
        {
            return;
        }

        world_pos = new Godot.Collections.Dictionary { { "x", pos.X }, { "y", pos.Y } };
    }

    // ------------------------------------------------------------------ 读字段的几个小助手

    private static bool _is_dict(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) && source[key].VariantType == Variant.Type.Dictionary;

    private static bool _is_array(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) && source[key].VariantType == Variant.Type.Array;

    private static bool _is_number(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key)
            && (source[key].VariantType == Variant.Type.Float || source[key].VariantType == Variant.Type.Int);

    private static int _get_int(Godot.Collections.Dictionary source, string key, int fallback)
        => source.ContainsKey(key) ? source[key].AsInt32() : fallback;

    private static string _get_string(Godot.Collections.Dictionary source, string key, string fallback)
        => source.ContainsKey(key) ? source[key].AsString() : fallback;

    private static string[] _string_array(Godot.Collections.Dictionary source, string key)
    {
        Godot.Collections.Array values = source.ContainsKey(key)
            ? source[key].AsGodotArray()
            : new Godot.Collections.Array();
        var out_values = new string[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            out_values[i] = values[i].AsString();
        }

        return out_values;
    }
}
