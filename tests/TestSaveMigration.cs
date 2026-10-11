using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tests;

/// <summary>
/// 存档迁移第一族：v1 直读与走存档槽／菜单写回、v2 往返、v3 补出「已学＋装配」、v4 装备没有词条。
/// （对位 `tests/test_save_migration.gd` 的前四节，2026-10-11 P4 第三十五批整体搬过来；
/// 公共基类与拆分理由见 `TestSaveMigrationBase`。）
///
/// 这一族盯的是**早期档的字段补齐**：老档里没有的记录（背包／经验池／已学／装配／词条）
/// 读进来必须补成能玩的状态，而不是空队／空装配那种「不报错但玩不下去」。
/// </summary>
public partial class TestSaveMigration : TestSaveMigrationBase
{
    public override string SuiteName() => "存档迁移 v1 → v4（早期档补齐）";

    public override void Run()
    {
        TableDb db = GetDb();
        // 存档结构版本写死绝对值：升版本必须同时写迁移与 `AddedIn`（漏一边就有档读不回来）
        CheckEq(
            GameState.save_version(), 15,
            "存档结构版本是 15（v15 = NPC 好感 npc_favor，设计 19）");
        _CleanDir(TestDir);
        _CheckDirectMigration(db);
        _CheckStoreMigration(db);
        _CheckV2RoundTrip(db);
        _CheckV3Migration(db);
        _CheckV4Migration(db);
    }

    private void _CheckDirectMigration(TableDb db)
    {
        GameState state = GameState.from_dict(_V1Dict(), db);
        CheckNotNull(state, "v1 存档能读进来");
        if (state == null)
        {
            return;
        }

        CheckEq(state.version, GameState.save_version(), "读入后版本升到当前版本");
        CheckEq(state.migrated_from, 1, "记下迁移来源");
        CheckEq(state.level_of("scholar_fallen"), 3, "等级保留");
        CheckEq(state.difficulty_id, "hard", "难度保留");
        CheckEq(state.created_unix, 111, "创建时间保留");
        CheckEq(state.saved_unix, 222, "保存时间保留");
        Inventory inventory = _InventoryOf(state);
        CheckEq(inventory.equipment_count(), 2, "自动补发模板的初始装备（0.10.2 起两件）");
        CheckEq(inventory.equipped_instances(db, "scholar_fallen").Length, 2, "补发的两件都穿上了");
        CheckEq(inventory.money, 0, "新迁移的存档铜钱为 0");
        CheckEq(
            state.available_points(db, "scholar_fallen"), 15,
            "3 级累计 15 点可分配（0.13.0 每级 5 点）");

        // 没有 db 时仍能升级版本，只是补不了装备
        GameState bare = GameState.from_dict(_V1Dict(), null);
        CheckEq(bare.version, GameState.save_version(), "没有 db 也能升版本");
        CheckEq(_InventoryOf(bare).equipment_count(), 0, "没有 db 时不补装备");
    }

    private void _CheckStoreMigration(TableDb db)
    {
        SaveStore store = SaveStore.create(TestDir, Slots);
        store.ensure_dir();
        WriteText(store.slot_path(1), Json.Stringify(_V1Dict()));

        Godot.Collections.Dictionary loaded = store.load_slot(1, db);
        CheckTrue(loaded["ok"].AsBool(), "v1 存档能读出来");
        var loadedState = (GameState)loaded["state"].AsGodotObject();
        CheckEq(loadedState.migrated_from, 1, "状态标记需要迁移");

        // 走菜单的读档流程：迁移后立刻写回
        MenuController controller = MenuController.create(
            db, store, System.Array.Empty<string>(), "normal");
        Godot.Collections.Dictionary result = controller.load_game(1);
        CheckTrue(result["ok"].AsBool(), "菜单读档成功");
        CheckTrue(
            result["message"].AsString().Contains("升级"),
            $"提示已升级存档：{result["message"].AsString()}");

        Variant raw = Json.ParseString(FileAccess.GetFileAsString(store.slot_path(1)));
        CheckTrue(GameState.is_valid_dict(raw), "写回后仍是合法存档");
        CheckEq(
            raw.AsGodotDictionary()["version"].AsInt32(), GameState.save_version(),
            "文件里的版本变成当前版本");

        Godot.Collections.Dictionary again = store.load_slot(1, db);
        var againState = (GameState)again["state"].AsGodotObject();
        CheckEq(againState.migrated_from, 0, "第二次读取不需要迁移");
        CheckEq(_InventoryOf(againState).equipment_count(), 2, "写回后的存档带着装备（两件）");
        CheckEq(againState.level_of("scholar_fallen"), 3, "写回后等级没丢");
    }

    private void _CheckV2RoundTrip(TableDb db)
    {
        // 这组用例验的是**迁移机制**（老档里的钱／材料／装备／加点能不能原样回来），
        // 与"表里有几个模板"无关；而装备件数会随模板的起始装备变化。所以把 roster 钉成
        // **单模板夹具**，让这条用例专测机制（框架说明决策 161）。
        TableDb roster = TableWithNChars(1);
        GameState state = GameState.new_game(roster, "normal", System.Array.Empty<string>(), 0);
        string charId = state.char_ids[0].AsString();
        Inventory inventory = _InventoryOf(state);
        inventory.money = 123;
        inventory.add_item(roster, "item_herb", 4);
        state.spend_point(roster, charId, "con");

        SaveStore store = SaveStore.create(TestDir, Slots);
        Godot.Collections.Dictionary saved = store.save_slot(2, state);
        CheckTrue(saved["ok"].AsBool(), "v2 存档写入成功");
        Godot.Collections.Dictionary loaded = store.load_slot(2, roster);
        CheckTrue(loaded["ok"].AsBool(), "v2 存档读出成功");
        var back = (GameState)loaded["state"].AsGodotObject();
        Inventory backInventory = _InventoryOf(back);
        CheckEq(back.migrated_from, 0, "v2 不需要迁移");
        CheckEq(backInventory.money, 123, "铜钱往返一致");
        CheckEq(backInventory.count("item_herb"), 4, "材料往返一致");
        CheckEq(backInventory.equipment_count(), 2, "装备往返一致（两件）");
        CheckEq(back.available_points(db, charId), 4, "加点往返一致（1 级 5 点，已用 1 点）");
        CheckEq(_I(back.allocations_of(charId), "con", 0), 1, "加点内容往返一致");
    }

    /// <summary>v3（有熟练度、没有已学与装配）老档：读进来要补出「已学 + 装配」，并带上熟练度里那份武学。</summary>
    private void _CheckV3Migration(TableDb db)
    {
        // 同 `_CheckV2RoundTrip`：验的是"从 v3 补出已学与装配"这套机制，
        // 图鉴条数会随模板数变化 → 钉成单模板夹具（框架说明决策 161）。
        TableDb roster = TableWithNChars(1);
        GameState fresh = GameState.new_game(roster, "normal", System.Array.Empty<string>(), 0);
        Godot.Collections.Dictionary data = fresh.to_dict();
        data["version"] = 3;
        data.Remove("char_learned");
        data.Remove("char_loadouts");
        data["skill_mastery"] = new Godot.Collections.Dictionary
        {
            { "scholar_fallen", new Godot.Collections.Dictionary { { "sk_wudu_01", 4 } } },
        };

        GameState state = GameState.from_dict(data, roster);
        CheckNotNull(state, "v3 存档能读进来");
        if (state == null)
        {
            return;
        }

        CheckEq(state.migrated_from, 3, "记下从 v3 迁移");
        CheckEq(state.version, GameState.save_version(), "版本升到当前");
        CheckTrue(state.is_learned("scholar_fallen", "sk_xuanwei_01"), "模板起始武学补进已学");
        CheckTrue(state.is_learned("scholar_fallen", "sk_wudu_01"), "熟练度里出现过的武学也算已学");
        CheckEq(state.mastery_of("scholar_fallen", "sk_wudu_01"), 4, "熟练度本身保留");
        Godot.Collections.Dictionary loadout = state.loadout_of("scholar_fallen");
        CheckGt(_ArrayOf(loadout, "active").Count, 0.0, "迁移时按容量铺好装配");
        CheckEq(state.collected_skill_count(), 2, "图鉴按去重计数：起始武学 + 熟练度里的那部");

        // 再写回一遍：v4 往返幂等
        GameState back = GameState.from_dict(state.to_dict(), roster);
        CheckEq(back.migrated_from, 0, "v4 写回后不再需要迁移");
        CheckEq(
            _Join(back.loadout_of("scholar_fallen")["active"].AsStringArray()),
            _Join(loadout["active"].AsStringArray()),
            "装配往返一致");
    }

    /// <summary>v4（装备实例还没有词条字段）老档：读进来默认没有词条，版本升到 v5。</summary>
    private void _CheckV4Migration(TableDb db)
    {
        // 装备件数 = 各模板起始装备之和（1 个模板 = 1 件）→ 钉成单模板夹具，
        // 专测 v4→v5 的词条补齐（框架说明决策 161）。
        TableDb roster = TableWithNChars(1);
        GameState fresh = GameState.new_game(roster, "normal", System.Array.Empty<string>(), 0);
        Godot.Collections.Dictionary data = fresh.to_dict();
        data["version"] = 4;
        // 把装备实例里的 affixes 拿掉，模拟 v4 存档的形状
        Godot.Collections.Dictionary equipment = data["inventory"].AsGodotDictionary()["equipment"]
            .AsGodotDictionary();
        foreach (Variant instanceId in equipment.Keys)
        {
            equipment[instanceId].AsGodotDictionary().Remove("affixes");
        }

        GameState state = GameState.from_dict(data, roster);
        CheckNotNull(state, "v4 存档能读进来");
        if (state == null)
        {
            return;
        }

        CheckEq(state.migrated_from, 4, "记下从 v4 迁移");
        CheckEq(state.version, GameState.save_version(), "版本升到当前");
        Inventory inventory = _InventoryOf(state);
        string instanceId2 = inventory.equipment_ids()[0];
        CheckTrue(inventory.affixes_of(instanceId2).Count == 0, "v4 的装备没有词条（不是空词条对象）");
        CheckEq(inventory.equipment_count(), 2, "装备本体还在（两件）");
    }
}
