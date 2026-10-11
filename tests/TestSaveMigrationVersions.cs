using Godot;
using Lunhui.Autoload;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 存档迁移第二族：v9 首杀、v10 战斗外气血、v11 战斗外增益、v12 大地图坐标、v13 天赋与自建角色，
/// 外加**逐档覆盖**（v1…v14 每一档都造一遍老档读回来）。
/// （对位 `tests/test_save_migration.gd` 的对应几节，2026-10-11 P4 第三十五批整体搬过来；
/// 公共基类与拆分理由见 `TestSaveMigrationBase`。）
///
/// 逐档那条盯的是：以前只显式覆盖 v1／v3／v4／v9／v10，别的档只是「被间接碰过」——
/// 中间某一档读不出来（比如那年加的字段少写一个默认值），用过老档的人一读档就空队／崩，
/// 而所有用例照样绿。这里按 `AddedIn` 把每一档的字段删掉再读，**版本号是权威**。
/// </summary>
public partial class TestSaveMigrationVersions : TestSaveMigrationBase
{
    public override string SuiteName() => "存档迁移 v9 → v14（后期字段与逐档覆盖）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckV9Migration(db);
        _CheckV10Migration(db);
        _CheckV12FieldBuffs(db);
        _CheckV13WorldPos(db);
        _CheckV14Creation(db);
        _CheckEveryLegacyVersion(db);
    }

    /// <summary>v10（没有战斗外气血）老档：读进来当作全队满血，版本升到 v11。</summary>
    private void _CheckV10Migration(TableDb db)
    {
        GameState fresh = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        string charId = fresh.char_ids[0].AsString();
        fresh.set_current_hp(charId, 7);
        Godot.Collections.Dictionary data = fresh.to_dict();
        data["version"] = 10;
        data.Remove("char_hp");

        GameState state = GameState.from_dict(data, db);
        CheckNotNull(state, "v10 存档能读进来");
        if (state == null)
        {
            return;
        }

        CheckEq(state.migrated_from, 10, "记下从 v10 迁移");
        CheckEq(state.version, GameState.save_version(), "版本升到当前");
        CheckEq(state.current_hp_of(charId), -1, "v10 老档没有气血记录");
        CheckFalse(state.is_wounded(charId), "没有记录就是满血");
        // 迁移后照常能记伤
        state.set_current_hp(charId, 12);
        CheckEq(state.current_hp_of(charId), 12, "迁移后能记战斗外气血");
        GameState back = GameState.from_dict(state.to_dict(), db);
        CheckEq(back.migrated_from, 0, "v11 写回后不再需要迁移");
        CheckEq(back.current_hp_of(charId), 12, "气血往返一致");
    }

    /// <summary>v9（没有首杀记录）老档：读进来首杀记录为空（当作没打过），版本升到 v10。</summary>
    private void _CheckV9Migration(TableDb db)
    {
        GameState fresh = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        Godot.Collections.Dictionary data = fresh.to_dict();
        data["version"] = 9;
        data.Remove("first_kills");

        GameState state = GameState.from_dict(data, db);
        CheckNotNull(state, "v9 存档能读进来");
        if (state == null)
        {
            return;
        }

        CheckEq(state.migrated_from, 9, "记下从 v9 迁移");
        CheckEq(state.version, GameState.save_version(), "版本升到当前");
        CheckEq(state.first_kill_count(), 0, "v9 老档没有首杀记录");
        CheckFalse(state.has_first_kill("drop_bd_boss"), "v9 老档视作没打过首杀");
        CheckTrue(state.mark_first_kill("drop_bd_boss"), "迁移后能正常记首杀");

        // 写回一遍：v10 往返幂等
        GameState back = GameState.from_dict(state.to_dict(), db);
        CheckEq(back.migrated_from, 0, "v10 写回后不再需要迁移");
        CheckTrue(back.has_first_kill("drop_bd_boss"), "首杀记录往返一致");
    }

    /// <summary>
    /// v11 → v12：战斗外增益进存档。老档（没有 `field_buffs`）读进来就是「没有增益」，
    /// 新档往返要**连到期时间戳一起保留**（按现实时间计时，离线那段时间照样算）。
    /// </summary>
    private void _CheckV12FieldBuffs(TableDb db)
    {
        var session = new GameSession();
        GameState state = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        session.set_state(state);
        double now = Time.GetUnixTimeFromSystem();
        CheckTrue(
            session.add_field_buff("buff_meditated", 10, "", 1)["ok"].AsBool(),
            "打坐余韵挂到存档上");
        CheckEq(state.field_buffs.Count, 1, "增益落在 GameState（v12 起可持久化），不是只在会话里");

        // 老档：把 v12 之后才有的字段删掉再读 → 当作没有增益
        Godot.Collections.Dictionary legacy = state.to_dict();
        legacy["version"] = 11;
        legacy.Remove("field_buffs");
        GameState old = GameState.from_dict(legacy, db);
        CheckNotNull(old, "v11 老档能读进来");
        if (old == null)
        {
            session.Free();
            return;
        }

        CheckEq(old.migrated_from, 11, "记下从 v11 迁移");
        CheckEq(old.field_buffs.Count, 0, "老档没有战斗外增益（当作没打坐）");

        // 往返：带到期时间戳一起回来
        legacy = state.to_dict();
        GameState back = GameState.from_dict(legacy, db);
        CheckEq(back.field_buffs.Count, 1, "v12 往返保留增益");
        CheckEq(_Text(_Row(back.field_buffs, 0), "buff_id"), "buff_meditated", "保留的是哪条增益");
        CheckGt(
            _Row(back.field_buffs, 0)["expires_at"].AsDouble(), now,
            "到期时间戳是**绝对时间**（离线也照走）");

        // 坏数据（缺 buff_id / 到期时间）直接丢掉，不读进一条「永远不过期」的增益
        legacy["field_buffs"] = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary { { "buff_id", "" }, { "expires_at", now + 600 } },
            new Godot.Collections.Dictionary { { "buff_id", "buff_meal" } },
            new Godot.Collections.Dictionary
            {
                { "buff_id", "buff_meal" }, { "expires_at", now + 600 }, { "stacks", 2 },
            },
        };
        GameState filtered = GameState.from_dict(legacy, db);
        CheckEq(filtered.field_buffs.Count, 1, "坏条目被丢掉，只留合法的");
        CheckEq(_Text(_Row(filtered.field_buffs, 0), "buff_id"), "buff_meal", "留下的那条是对的");
        session.Free();
    }

    /// <summary>
    /// v12 → v13：大地图坐标进存档（设计 09 §二：读档一律回大地图，落点用这个坐标）。
    /// 老档（没有 `world_pos`）读进来 = 没有记录（大地图退回默认出生点）；
    /// 新档往返要把坐标原样带回来——会话层写进去的那一份必须真的落进存档。
    /// </summary>
    private void _CheckV13WorldPos(TableDb db)
    {
        var session = new GameSession();
        GameState state = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        session.set_state(state);
        var spot = new Vector2(412.5f, 1337.25f);
        session.set_world_position(spot, default);
        CheckEq(state.world_position(), spot, "会话层写坐标时顺手同步进存档（v13 起）");

        // 新档往返：坐标原样回来，而且读档后会话字段也照它摆人
        Godot.Collections.Dictionary data = state.to_dict();
        CheckTrue(data.ContainsKey("world_pos"), "to_dict 里带上了 world_pos");
        GameState back = GameState.from_dict(data, db);
        CheckNotNull(back, "带坐标的档能读回来");
        if (back == null)
        {
            session.Free();
            return;
        }

        CheckEq(back.world_position(), spot, "v13 往返保留大地图坐标");
        var reloadedSession = new GameSession();
        reloadedSession.set_state(back);
        CheckEq(
            reloadedSession.world_position, spot,
            "读档后会话里的大地图落点就是存档里那个坐标");
        reloadedSession.Free();

        // 老档：v12 及更早没有这条记录 → 当作「没有坐标」，用默认出生点
        Godot.Collections.Dictionary legacy = data.Duplicate(true);
        legacy["version"] = 12;
        legacy.Remove("world_pos");
        GameState old = GameState.from_dict(legacy, db);
        CheckNotNull(old, "v12 老档能读进来");
        if (old != null)
        {
            CheckEq(old.migrated_from, 12, "记下从 v12 迁移");
            CheckEq(old.world_position(), Vector2.Zero, "老档没有坐标记录（退回默认出生点）");
        }

        // 写坏的坐标（x 是字符串）宁可丢掉，也不能读进一个 NaN 把玩家扔出地图
        Godot.Collections.Dictionary broken = data.Duplicate(true);
        broken["world_pos"] = new Godot.Collections.Dictionary { { "x", "左" }, { "y", 3 } };
        GameState salvaged = GameState.from_dict(broken, db);
        CheckEq(salvaged.world_position(), Vector2.Zero, "写坏的坐标被丢掉，不当成一条记录");
        session.Free();
    }

    /// <summary>
    /// v13 → v14：天赋与自建角色模板进存档（设计 12／13）。
    ///
    /// 两条都要：① 老档（没有这两份记录）读进来 = 没天赋、没自建角色，且版本升到 14；
    /// ② 新档往返：天赋列表原样回来；**自建角色的模板要重新注入 db**，
    /// 否则按 char_id 取 `character_base` 会查不到，装备与武学全落空。
    /// </summary>
    private void _CheckV14Creation(TableDb db)
    {
        GameState fresh = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        string charId = fresh.char_ids[0].AsString();
        fresh.talent_picks[charId] = new Godot.Collections.Array { "tal_tie_shen" };
        Godot.Collections.Dictionary data = fresh.to_dict();
        GameState back = GameState.from_dict(data, db);
        CheckEq(_ArrayOf(back.talent_picks, charId).Count, 1, "天赋随存档往返");
        CheckEq(_ArrayOf(back.talent_picks, charId)[0].AsString(), "tal_tie_shen", "天赋 id 原样");

        // 老档（v13）：把这两份记录删掉再读 → 空，版本升到 14
        Godot.Collections.Dictionary legacy = data.Duplicate(true);
        legacy["version"] = 13;
        legacy.Remove("talent_picks");
        legacy.Remove("custom_templates");
        GameState old = GameState.from_dict(legacy, db);
        CheckTrue(old.talent_picks.Count == 0, "v13 老档没有天赋记录（不是坏档）");
        CheckTrue(old.custom_templates.Count == 0, "v13 老档没有自建角色");
        CheckEq(old.version, GameState.save_version(), "老档读进来后升到当前版本");

        // 自建角色：模板进存档 → 读档时重新注入 character_base，按 char_id 查得到
        Godot.Collections.Dictionary custom = data.Duplicate(true);
        custom["version"] = 14;
        custom["char_ids"] = new Godot.Collections.Array { "char_custom" };
        custom["char_levels"] = new Godot.Collections.Dictionary { { "char_custom", 1 } };
        custom["talent_picks"] = new Godot.Collections.Dictionary();
        custom["custom_templates"] = new Godot.Collections.Dictionary
        {
            {
                "char_custom",
                new Godot.Collections.Dictionary
                {
                    { "name_cn", "自建书生" },
                    { "role_tag", "自建" },
                    { "weapon_type", "sword" },
                    {
                        "attrs",
                        new Godot.Collections.Dictionary
                        {
                            { "str", 8 }, { "con", 8 }, { "agi", 8 }, { "int", 9 },
                            { "luk", 5 }, { "wu", 6 }, { "gen", 5 },
                        }
                    },
                    { "start_level", 1 },
                    { "start_skill_ids", "sk_xuanwei_01" },
                    { "start_equip_ids", "eq_sword_01" },
                }
            },
        };
        GameState restored = GameState.from_dict(custom, db);
        CheckNotNull(restored, "自建角色的档读得回来");
        if (restored != null)
        {
            CheckTrue(restored.char_ids.Contains("char_custom"), "自建角色留在队伍里（没被当成下架角色清掉）");
            var row = db.get_row("character_base", "char_custom") as CharacterBaseRow;
            CheckNotNull(row, "读档时把自建模板重新注入 character_base");
            if (row != null)
            {
                CheckEq(row.initial_con, 8, "自建模板的七维从存档恢复");
                CheckEq(row.weapon_type, "sword", "武器类型也恢复");
            }
        }
    }

    /// <summary>
    /// 逐档迁移：v1…v14 **每一档**都要能读进来。断言：版本号被记下、升到当前版本、
    /// 队伍与背包都在，且「那一档该有的字段」还在、「那一档之后才有的字段」确实没被读进来。
    /// </summary>
    private void _CheckEveryLegacyVersion(TableDb db)
    {
        GameState current = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        string charId = current.char_ids[0].AsString();
        current.char_allocations[charId] = new Godot.Collections.Dictionary { { "str", 1 } };
        _InventoryOf(current).add_item(db, "item_herb", 2);
        current.party_exp = 30;
        current.record_event_check("ev_shed_trap", "done");
        current.set_flag("event_parley", true);
        current.mark_first_kill("drop_wolf");
        current.set_current_hp(charId, 12);
        current.record_purchase("bld_smith", "eq_sword_01", 1);
        current.record_dungeon("scene_heifengzhai", "chests", "hf1_shed");
        current.reveal_node("n_cave_collapse");

        for (int legacyVersion = 1; legacyVersion < GameState.save_version(); legacyVersion++)
        {
            Godot.Collections.Dictionary data = current.to_dict();
            data["version"] = legacyVersion;
            int erased = 0;
            foreach ((int addedVersion, string[] fields) in AddedIn)
            {
                if (addedVersion <= legacyVersion)
                {
                    continue;
                }

                foreach (string field in fields)
                {
                    if (data.ContainsKey(field))
                    {
                        data.Remove(field);
                        erased++;
                    }
                }
            }

            if (legacyVersion < 5)
            {
                // v5 才给装备实例加 affixes：更老的档里实例没有这个词条字段
                Godot.Collections.Dictionary dataInventory = data.ContainsKey("inventory")
                    ? data["inventory"].AsGodotDictionary()
                    : new Godot.Collections.Dictionary();
                Godot.Collections.Dictionary equipment = dataInventory.ContainsKey("equipment")
                    ? dataInventory["equipment"].AsGodotDictionary()
                    : new Godot.Collections.Dictionary();
                foreach (Variant instanceId in equipment.Keys)
                {
                    equipment[instanceId].AsGodotDictionary().Remove("affixes");
                }
            }

            GameState migrated = GameState.from_dict(data, db);
            string tag = $"v{legacyVersion} 老档（删了 {erased} 个新字段）";
            CheckNotNull(migrated, $"{tag} 能读进来");
            if (migrated == null)
            {
                continue;
            }

            CheckEq(migrated.migrated_from, legacyVersion, $"{tag} 记下从哪一档迁移");
            CheckEq(migrated.version, GameState.save_version(), $"{tag} 升到当前版本");
            CheckGt(migrated.char_ids.Count, 0.0, $"{tag} 读进来队伍不为空");
            CheckNotNull(migrated.inventory, $"{tag} 读进来有背包");
            // 后面每一条都要摸 inventory：它要是真丢了（迁移写坏），这里先停下，
            // 让失败信息停在「没背包」这一条上，别用空引用把整个用例炸掉。
            if (migrated.inventory == null)
            {
                continue;
            }

            Inventory inventory = _InventoryOf(migrated);
            CheckEq(migrated.difficulty_id, "normal", $"{tag} 难度保留");
            // v2 起才有背包与经验池：v1 老档要补出背包（初始装备），经验池归零
            if (legacyVersion >= 2)
            {
                CheckEq(migrated.party_exp, 30, $"{tag} 经验池保留");
                CheckEq(inventory.count("item_herb"), 2, $"{tag} 背包物品保留");
            }
            else
            {
                CheckEq(migrated.party_exp, 0, $"{tag} 经验池归零（那会儿还没有）");
                CheckGt(inventory.equipment.Count, 0.0, $"{tag} 补出了初始装备");
            }

            // v9 起才有事件记录；更老的档即使字典里被塞了这些字段也不该读进来
            if (legacyVersion >= 9)
            {
                CheckEq(migrated.event_check_result("ev_shed_trap"), "done", $"{tag} 事件记录保留");
                CheckTrue(migrated.has_flag("event_parley"), $"{tag} 剧情旗标保留");
            }
            else
            {
                CheckEq(migrated.event_check_result("ev_shed_trap"), "", $"{tag} 没有事件记录");
                CheckFalse(migrated.has_flag("event_parley"), $"{tag} 没有剧情旗标");
            }

            if (legacyVersion >= 10)
            {
                CheckTrue(migrated.has_first_kill("drop_wolf"), $"{tag} 首杀记录保留");
            }
            else
            {
                CheckFalse(migrated.has_first_kill("drop_wolf"), $"{tag} 首杀记录为空");
            }

            if (legacyVersion >= 11)
            {
                CheckEq(migrated.current_hp_of(charId), 12, $"{tag} 战斗外气血保留");
            }
            else
            {
                CheckEq(migrated.current_hp_of(charId), -1, $"{tag} 没有气血记录（=满血）");
            }

            if (legacyVersion >= 6)
            {
                CheckEq(migrated.purchased_count("bld_smith", "eq_sword_01"), 1, $"{tag} 商店买入次数保留");
            }
            else
            {
                CheckEq(migrated.purchased_count("bld_smith", "eq_sword_01"), 0, $"{tag} 没有商店记录");
            }

            if (legacyVersion >= 7)
            {
                CheckTrue(
                    _ArrayOf(migrated.dungeon_record("scene_heifengzhai"), "chests").Contains("hf1_shed"),
                    $"{tag} 副本完成度保留");
            }
            else
            {
                CheckFalse(
                    _ArrayOf(migrated.dungeon_record("scene_heifengzhai"), "chests").Contains("hf1_shed"),
                    $"{tag} 没有副本记录");
            }

            if (legacyVersion >= 8)
            {
                CheckTrue(migrated.is_node_revealed("n_cave_collapse"), $"{tag} 揭开的地标保留");
            }
            else
            {
                CheckFalse(migrated.is_node_revealed("n_cave_collapse"), $"{tag} 没有揭开记录");
            }
        }

        // 版本号是权威：字典里塞了 v11 才有的 char_hp，但 version 说 8 → 不许读进来
        Godot.Collections.Dictionary cheating = current.to_dict();
        cheating["version"] = 8;
        GameState cheated = GameState.from_dict(cheating, db);
        CheckNotNull(cheated, "版本号说 v8、但带着 v11 字段的档也能读");
        if (cheated != null)
        {
            CheckEq(cheated.current_hp_of(charId), -1, "v8 不该认 char_hp（版本号是权威）");
            CheckEq(cheated.migrated_from, 8, "还是记成从 v8 迁移");
        }
    }
}
