using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 端到端：加载表 → 组队 → 打一场明雷 → 结算奖励与掉落
/// （对位 `tests/test_end_to_end.gd`，2026-10-11 P4 第三十四批整体搬过来）。
///
/// 这条链是「一个系统一个系统都验绿、合起来却跑不通」的那类问题的唯一防线：
/// 顶上是真的把表校验 ＋ 组队 ＋ 模拟一场战斗 ＋ 掷掉落串起来跑一遍，下面是三条跨系统的持久化
/// （带伤进下一场与医馆真治／奖励落账后存档往返／多人队伍的等级与加点互不串且随存档走）。
/// </summary>
public partial class TestEndToEnd : TestCase
{
    private const int Seed = 2026;
    private const string SaveDir = "res://.logs/test_end_to_end";
    private const string WoundedSaveDir = "res://.logs/test_end_to_end_hp";

    public override string SuiteName() => "端到端：明雷战斗与掉落";

    public override void Run()
    {
        TableDb db = GetDb();
        CheckEq(TableValidator.validate(db).Length, 0, "开局校验通过");

        EnemyFactory factory = EnemyFactory.create(db);
        Godot.Collections.Array allies = _Party(db, 5);
        Godot.Collections.Array enemies = factory.create_team("team_bandit_patrol", "normal");
        CheckEq(enemies.Count, 3, "山贼巡山队 2 刀 1 弓");

        BattleSimulator simulator = BattleSimulator.create(db, RngService.create(Seed));
        Godot.Collections.Dictionary result = simulator.simulate(allies, enemies);
        CheckEq(result["winner"].AsString(), BattleSimulator.winner_ally(), "5 级两人组能清掉巡山队");
        CheckGt(result["rewards"].AsGodotDictionary()["exp"].AsDouble(), 0.0, "打完有经验入账");
        CheckGt(result["rewards"].AsGodotDictionary()["money"].AsDouble(), 0.0, "打完有铜钱入账");

        DropResolver resolver = DropResolver.create(db, RngService.create(Seed + 1), PityTracker.create());
        double bonus = DropResolver.party_drop_bonus(allies);
        CheckInRange(bonus, 0.0, 0.25, "全队掉落机缘不超过保守方案上限");

        var collected = new Godot.Collections.Dictionary();
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = enemy_variant.As<BattleActor>();
            var options = new Godot.Collections.Dictionary { { "drop_rate_bonus", bonus } };
            foreach (Variant entry_variant in resolver.roll_group(enemy.drop_group, "normal", options))
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                string item_id = entry["item_id"].AsString();
                collected[item_id] = _I(collected, item_id, 0) + entry["qty"].AsInt32();
            }
        }
        CheckGt(collected.Count, 0.0, "打完后至少掉落一种物品");
        CheckGt(_I(collected, "item_money", 0), 0.0, "铜钱必掉");

        GD.Print(
            $"      端到端：{result["rounds"].AsInt32()} 回合，"
            + $"奖励 经验 {result["rewards"].AsGodotDictionary()["exp"].AsInt32()} / "
            + $"铜钱 {result["rewards"].AsGodotDictionary()["money"].AsInt32()}，掉落 {collected.Count} 种");
        _CheckRewardsAndPersistence(db);
        _CheckWoundedPersistence(db);
        _CheckMultiCharacterState(db);
    }

    /// <summary>战斗外气血（v11）：带伤进下一场 → 伤随存档往返 → 医馆按缺失收费并真的回满。</summary>
    private void _CheckWoundedPersistence(TableDb db)
    {
        GodotObject state = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        CharacterSheet sheet = CharacterSheet.create(db, state, char_id);
        int cap = sheet.max_hp();
        CheckEq(sheet.current_hp(), cap, "新档是满血");
        CheckFalse(state.Call("is_wounded", char_id).AsBool(), "新档没有伤记录");

        // 模拟「上一场没打满」：打伤 40 点
        state.Call("set_current_hp", char_id, System.Math.Max(1, cap - 40));
        Godot.Collections.Array built = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        CheckEq(built[0].As<BattleActor>().hp, cap - 40, $"带着伤进下一场（{built[0].As<BattleActor>().hp} / {cap}）");
        CheckEq(sheet.current_hp(), cap - 40, "面板读同一个数");

        // 伤随存档往返
        _InventoryOf(state).money = 500;
        _CleanDir(WoundedSaveDir);
        SaveStore store = SaveStore.create(WoundedSaveDir, 2);
        store.ensure_dir();
        CheckTrue(_B(store.save_slot(1, state), "ok"), "带伤的存档写入成功");
        Godot.Collections.Dictionary loaded = store.load_slot(1, db);
        CheckTrue(_B(loaded, "ok"), "带伤的存档读出成功");
        if (!_B(loaded, "ok"))
        {
            return;
        }
        GameState back = (GameState)loaded["state"].AsGodotObject();
        CheckEq(back.migrated_from, 0, "v11 存档不需要迁移");
        CheckEq(back.current_hp_of(char_id), cap - 40, "伤随存档往返");

        // 医馆治疗：按缺失收费并回满
        ShopService shop = ShopService.create(db, back);
        Godot.Collections.Dictionary healed = shop.heal_party("bld_clinic");
        CheckTrue(_B(healed, "ok"), $"医馆能治：{_S(healed, "error")}");
        CheckEq(healed["healed"].AsInt32(), 40, "回 40 点");
        CheckEq(healed["cost"].AsInt32(), 80, "40 点 × 2 文 = 80");
        CheckEq(_InventoryOf(back).money, 420, "钱扣掉了");
        Godot.Collections.Array after = PartyBuilder.build_actors(db, back, new Godot.Collections.Array());
        CheckEq(after[0].As<BattleActor>().hp, cap, "治完满血进战斗");
    }

    /// <summary>战斗奖励落账 → 存档往返 → 装备加成确实进了战斗单位。</summary>
    private void _CheckRewardsAndPersistence(TableDb db)
    {
        GodotObject state = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
        string char_id = state.Get("char_ids").AsStringArray()[0];
        Inventory inventory = _InventoryOf(state);
        EnemyFactory factory = EnemyFactory.create(db);
        Godot.Collections.Array enemies = factory.create_team("team_wolf_pack", "normal");

        // 带装备的战斗单位：外功比脱掉武器后更高
        Godot.Collections.Array armed = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        // 队伍人数来自存档（`character_base` 的行数会随设计推进变化）——按**存档里的**人数核，不写死 1
        CheckEq(
            armed.Count,
            state.Get("char_ids").AsStringArray().Length,
            $"队伍按存档构造出战斗单位（{state.Get("char_ids").AsStringArray().Length} 人）");
        double atk_armed = armed[0].As<BattleActor>().stat("atk_phys");
        Godot.Collections.Dictionary unequipped = inventory.unequip(char_id, "weapon", 0);
        CheckTrue(_B(unequipped, "ok"), "卸下初始武器");
        Godot.Collections.Array bare = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        double atk_bare = bare[0].As<BattleActor>().stat("atk_phys");
        CheckGt(atk_armed, atk_bare, "装备加成进了战斗单位（外功更高）");
        inventory.equip(
            db,
            char_id,
            state.Call("level_of", char_id).AsInt32(),
            "sword",
            _S(unequipped, "instance_id"));

        // 内功「装备即生效」也要穿过 PartyBuilder 到**战斗单位**（不只是面板）：
        // CharacterSheet 那层由 TestSkillLoadout 钉着，但「装配 → BattleActor」这一段以前没人验——
        // 内功是 0.6.x 的主角系统（24 部、占格制），它要是在这一层断了，全系统就只剩面板数字。
        SkillLoadout loadout = SkillLoadout.create(db, state, char_id);
        loadout.learn("pf_xuanwei_01");
        CheckTrue(_B(loadout.equip("pf_xuanwei_01"), "ok"), "装上★1 内功");
        double qi_with = _FirstActor(db, state).stat("qi_max");
        loadout.unequip("pf_xuanwei_01");
        double qi_without = _FirstActor(db, state).stat("qi_max");
        CheckEq(qi_with - qi_without, 30.0, "内功加成进了战斗单位（内力上限 +30）");
        loadout.equip("pf_xuanwei_01");

        // 打一场并落账
        Godot.Collections.Dictionary result = BattleSimulator.create(db, RngService.create(Seed)).simulate(
            PartyBuilder.build_actors(db, state, new Godot.Collections.Array()),
            enemies);
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed + 7), PityTracker.create());
        Godot.Collections.Array drops = resolver.roll_group(
            "drop_wolf", "normal", new Godot.Collections.Dictionary { { "drop_rate_bonus", 1.0 } });
        BattleReward.apply(db, state, result, drops, null);
        CheckEq(
            state.Get("party_exp").AsInt32(),
            result["rewards"].AsGodotDictionary()["exp"].AsInt32(),
            "经验记进队伍池");
        CheckGt(
            inventory.money,
            result["rewards"].AsGodotDictionary()["money"].AsDouble() - 0.5,
            "铜钱入账");
        CheckGt(inventory.item_ids().Length + inventory.equipment_count(), 0.0, "掉落进了背包");

        // 存档往返后奖励仍在
        _CleanDir(SaveDir);
        SaveStore store = SaveStore.create(SaveDir, 2);
        store.ensure_dir();
        CheckTrue(_B(store.save_slot(1, state), "ok"), "带奖励的存档写入成功");
        Godot.Collections.Dictionary loaded = store.load_slot(1, db);
        CheckTrue(_B(loaded, "ok"), "带奖励的存档读取成功");
        if (_B(loaded, "ok"))
        {
            GameState back = (GameState)loaded["state"].AsGodotObject();
            Inventory back_inventory = _InventoryOf(back);
            CheckEq(back.party_exp, state.Get("party_exp").AsInt32(), "经验往返一致");
            CheckEq(back_inventory.money, inventory.money, "铜钱往返一致");
            CheckEq(back_inventory.equipment_count(), inventory.equipment_count(), "装备往返一致");
            CheckEq(
                string.Join(",", back_inventory.item_ids()),
                string.Join(",", inventory.item_ids()),
                "材料往返一致");
        }
    }

    /// <summary>
    /// 多人队伍：`character_base` 现在只有 1 行，所以**所有「多人」分支从来没被跑过**——
    /// 而设计说的是 3–4 人队伍（00_总览／04）。这条不往策划的表里加东西，而是**复制模板行**
    /// 造出 N 个角色（同属性、只改 id/名字，其中一个智更高），把多人分支逐个跑一遍：
    /// 队伍上限 4 / 每人等级与加点互不串 / 判定「谁行谁上」 / 升级先补等级最低的 /
    /// 医馆按全队缺失气血求和 / 多角色存档往返。
    /// </summary>
    private void _CheckMultiCharacterState(TableDb db)
    {
        TableDb wide = TableWithNChars(5);
        // ① 多人队伍：0.10.0 起 `new_game()` 只给 `recruit_def.is_initial` 的成员（设计 09 §3.2），
        // 所以这里**显式点名 4 人**来跑多人分支；默认组队按 recruit_def 核（不再按 character_base 前 4 行）
        var initial_ids = new System.Collections.Generic.List<string>();
        foreach (Variant entry in GetDb().rows("recruit_def"))
        {
            var row = (RecruitDefRow)entry.As<TableRow>();
            if (row.is_initial)
            {
                initial_ids.Add(row.char_id);
            }
        }
        GodotObject default_state = GameState.new_game(wide, "normal", System.Array.Empty<string>(), 0);
        CheckEq(
            default_state.Call("party_size").AsInt32(),
            initial_ids.Count,
            $"默认队伍 = recruit_def 的初始成员（{initial_ids.Count} 人）");
        GodotObject state = PartyState(wide, 4);
        string[] ids = state.Get("char_ids").AsStringArray();
        CheckEq(ids.Length, 4, "显式点名的多人队伍真的带 4 个角色");

        // ② 等级、加点都是**按人**记的，互不串
        state.Call("append_level", ids[2], 3);
        CheckEq(state.Call("level_of", ids[2]).AsInt32(), 4, "给第 3 个角色升 3 级");
        CheckEq(state.Call("level_of", ids[1]).AsInt32(), 1, "第 2 个角色的等级没被连带");
        state.Call("spend_point", wide, ids[1], "str");
        CheckEq(
            _I(state.Call("allocations_of", ids[1]).AsGodotDictionary(), "str", 0),
            1, "第 2 个角色加了一点力");
        CheckEq(
            _I(state.Call("allocations_of", ids[2]).AsGodotDictionary(), "str", 0),
            0, "第 3 个角色的加点没被动过");

        // ③ 判定「队里谁行谁上」：第 3 个角色的智被我调高，文学判定就该他上
        GodotObject judge_state = PartyState(wide, 4);
        string[] judge_ids = judge_state.Get("char_ids").AsStringArray();
        EventCheckService checks = EventCheckService.create(wide, judge_state, null);
        Godot.Collections.Dictionary best = checks.best_check_value("skill:wenxue");
        CheckEq(best["char_id"].AsString(), judge_ids[2], "判定取全队最高的那个（智最高的第 3 人）");
        CharacterSheet sheet_third = CharacterSheet.create(wide, judge_state, judge_ids[2]);
        CharacterSheet sheet_first = CharacterSheet.create(wide, judge_state, judge_ids[0]);
        CheckEq(
            best["value"].AsInt32(),
            sheet_third.event_check_value("skill:wenxue")["value"].AsInt32(),
            "取的就是他本人的判定值");
        CheckGt(
            best["value"].AsDouble(),
            sheet_first.event_check_value("skill:wenxue")["value"].AsDouble(),
            "比第 1 个角色的判定值高");

        // ④ 经验池先补给**等级最低**的那个（开发侧口径，多人时才有意义）
        GodotObject gap_state = PartyState(wide, 4);
        string[] gap_ids = gap_state.Get("char_ids").AsStringArray();
        Godot.Collections.Dictionary levels_of = gap_state.Get("char_levels").AsGodotDictionary();
        for (int index = 0; index < gap_ids.Length; index++)
        {
            levels_of[gap_ids[index]] = index == 1 ? 3 : 5;
        }
        // 经验池故意给到**够升 5 级那一档**（900）：这样「等级 3 的」和「等级 5 的」都付得起，
        // 才是真的在比较「先补谁」。第一版只给到 3 级那一档（420）→ 只有一个人付得起，
        // 把「挑最低」写成「挑最高」也照样绿（反向验证当场抓到我这条断言太弱，才改的）。
        var row5 = (LevelGrowthRow)wide.get_row("level_growth", 5);
        gap_state.Set("party_exp", row5.exp_to_next);
        LevelService levels = LevelService.create(wide, gap_state);
        Godot.Collections.Dictionary applied = levels.apply_available_levels();
        CheckEq(gap_state.Call("level_of", gap_ids[1]).AsInt32(), 4, "先补等级最低的那个（3 → 4）");
        CheckEq(gap_state.Call("level_of", gap_ids[0]).AsInt32(), 5, "高等级的不动");
        CheckEq(
            gap_state.Get("party_exp").AsInt32(),
            row5.exp_to_next - 420,
            "经验池扣的正是 3→4 那一份（420）");
        CheckEq(applied["changes"].AsGodotArray().Count, 1, "只升了 1 级");

        // ⑤ 医馆按**全队**缺失气血求和（不是只算一个人）
        GodotObject heal_state = PartyState(wide, 4);
        string[] heal_ids = heal_state.Get("char_ids").AsStringArray();
        _InventoryOf(heal_state).money = 1000;
        Godot.Collections.Array actors = PartyBuilder.build_actors(wide, heal_state, new Godot.Collections.Array());
        heal_state.Call(
            "set_current_hp", heal_ids[0], System.Math.Max(1, actors[0].As<BattleActor>().max_hp() - 10));
        heal_state.Call(
            "set_current_hp", heal_ids[1], System.Math.Max(1, actors[1].As<BattleActor>().max_hp() - 20));
        ShopService shop = ShopService.create(wide, heal_state);
        Godot.Collections.Dictionary preview = shop.heal_preview("bld_clinic");
        CheckEq(preview["missing"].AsInt32(), 30, "全队缺失 10 + 20 = 30");
        CheckEq(preview["cost"].AsInt32(), 60, "每点 2 文 → 60 文");
        Godot.Collections.Dictionary healed = shop.heal_party("bld_clinic");
        CheckTrue(_B(healed, "ok"), "全队治疗成功");
        CheckEq(_InventoryOf(heal_state).money, 940, "钱扣掉 60");
        foreach (string char_id in heal_ids)
        {
            CheckFalse(heal_state.Call("is_wounded", char_id).AsBool(), "治完全队都不带伤");
        }

        // ⑥ 多角色存档往返：等级与加点一个都不能丢
        GameState back = GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), wide);
        CheckNotNull(back, "多角色存档能读回来");
        if (back != null)
        {
            CheckEq(back.char_ids.Count, 4, "4 个角色都在");
            CheckEq(back.Call("level_of", ids[2]).AsInt32(), 4, "第 3 个角色的等级往返一致");
            CheckEq(
                _I(back.Call("allocations_of", ids[1]).AsGodotDictionary(), "str", 0),
                1, "加点往返一致");
        }
    }

    /// <summary>
    /// 造一队两人：0.4.0 起只有一个角色模板，同模板复制成两个（对位旧链 `_party(db, level)`）。
    /// </summary>
    private static Godot.Collections.Array _Party(TableDb db, int level)
    {
        var out_actors = new Godot.Collections.Array();
        Resource row = db.get_row("character_base", "scholar_fallen");
        for (int index = 0; index < 2; index++)
        {
            BattleActor actor = BattleActor.from_character(
                db, row, level, new Godot.Collections.Dictionary(), new Godot.Collections.Array());
            actor.actor_id = $"scholar_{index + 1}";
            actor.display_name = $"书生 {index + 1}";
            out_actors.Add(actor);
        }
        return out_actors;
    }

    private static BattleActor _FirstActor(TableDb db, GodotObject state) =>
        PartyBuilder.build_actors(db, state, new Godot.Collections.Array())[0].As<BattleActor>();

    private static Inventory _InventoryOf(GodotObject state) =>
        state.Get("inventory").As<Inventory>();

    /// <summary>把存档目录里的旧文件清掉（旧链那两段 `DirAccess` 清理的对位写法）。</summary>
    private static void _CleanDir(string path)
    {
        if (!DirAccess.DirExistsAbsolute(path))
        {
            return;
        }
        DirAccess dir = DirAccess.Open(path);
        if (dir == null)
        {
            return;
        }
        foreach (string file_name in dir.GetFiles())
        {
            dir.Remove(file_name);
        }
    }

    private static bool _B(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static string _S(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    /// <summary>`Godot.Collections.Dictionary` 没有带默认值的取法，自己包一层。</summary>
    private static int _I(Godot.Collections.Dictionary dict, string key, int fallback) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : fallback;
}
