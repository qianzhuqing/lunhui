using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 副本完成度与扫荡（C# 版，对位原 `tests/test_dungeon.gd`）。
///
/// 完成度四项：宝箱 / 隐藏房间 / 隐藏 Boss / 事件，只作追求目标，不影响奖励。
/// 扫荡：已通关层（该层带敌人的房间全清过）可扫荡，**只结算掉落、不给经验**。
///
/// `BattleReward` 那几节（背包含量／溢出计数）已经在 `TestBattleReward.cs` 里钉住，这里不重复。
/// </summary>
public partial class TestDungeon : TestCase
{
    private const string Scene = "scene_heifengzhai";
    private const int Seed = 20261003;

    public override string SuiteName() => "副本完成度与扫荡";

    public override void Run()
    {
        TableDb db = GetDb();
        GodotObject state = SoloState(db);
        DungeonService service = DungeonService.create(db, state, RngService.create(Seed));
        _CheckProgressItems(db, service);
        _CheckProgressMath(db, state, service);
        _CheckFloors(db, service);
        _CheckSweep(db, service);
        _CheckSave(db, state);
        _CheckExitSymmetry(db);
    }

    /// <summary>完成度的分母来自表：宝箱 4、隐藏房间 5、隐藏 Boss 1（醉刀客）、事件 2。</summary>
    private void _CheckProgressItems(TableDb db, DungeonService service)
    {
        Godot.Collections.Dictionary snapshot = service.progress(Scene);
        CheckEq(snapshot["chests"].AsGodotDictionary()["total"].AsInt32(), 4, "黑风寨 4 个宝箱");
        CheckEq(snapshot["hidden_rooms"].AsGodotDictionary()["total"].AsInt32(), 5, "5 个隐藏房间");
        CheckEq(
            snapshot["hidden_boss"].AsGodotDictionary()["total"].AsInt32(), 1,
            "隐藏 Boss 只有醉刀客（大寨主算章节 Boss）");
        CheckEq(snapshot["events"].AsGodotDictionary()["total"].AsInt32(), 2, "2 个事件触发（潜行入寨／锈剑共鸣）");
        CheckEq(snapshot["percent"].AsInt32(), 0, "新档完成度 0%");

        // 另一张图的分母也跟着表走
        Godot.Collections.Dictionary cave = service.progress("scene_cave");
        CheckEq(cave["chests"].AsGodotDictionary()["total"].AsInt32(), 0, "塌陷山洞没有宝箱");
        CheckEq(cave["hidden_rooms"].AsGodotDictionary()["total"].AsInt32(), 1, "山洞 1 个隐藏房间（塌方深处）");
    }

    /// <summary>记录齐了就是 100%，只记一半按比例算。</summary>
    private void _CheckProgressMath(TableDb db, GodotObject state, DungeonService service)
    {
        state.Call("record_dungeon", Scene, "chests", "hf1_shed|drop_chest_copper");
        state.Call("record_dungeon", Scene, "chests", "hf1_vault|drop_chest_silver");
        state.Call("record_dungeon", Scene, "rooms_entered", "hf1_deep");
        state.Call("record_dungeon", Scene, "bosses", "en_hidden_drunk");
        state.Call("record_dungeon", Scene, "triggers", "trig_rusty_sword");
        Godot.Collections.Dictionary half = service.progress(Scene);
        CheckEq(half["chests"].AsGodotDictionary()["done"].AsInt32(), 2, "开了 2 个宝箱");
        CheckEq(half["hidden_rooms"].AsGodotDictionary()["done"].AsInt32(), 1, "进过 1 个隐藏房间");
        CheckEq(half["hidden_boss"].AsGodotDictionary()["done"].AsInt32(), 1, "醉刀客已击败");
        CheckEq(half["events"].AsGodotDictionary()["done"].AsInt32(), 1, "触发过 1 个事件");
        CheckEq(half["done"].AsInt32(), 5, "合计 5 项");
        CheckEq(half["total"].AsInt32(), 12, "分母 4 + 5 + 1 + 2 = 12");
        CheckEq(half["percent"].AsInt32(), 42, "5/12 → 42%（四舍五入）");
        string text = service.progress_text(Scene);
        CheckTrue(text.Contains("完成度 42%"), $"HUD 文案带上完成度：{text}");

        // 重复记录不叠加
        CheckFalse(
            state.Call("record_dungeon", Scene, "chests", "hf1_shed|drop_chest_copper").AsBool(),
            "同一项重复记录会被拒");
        // 补齐剩下的
        foreach (string chest_id in new[] { "hf1_secret|drop_chest_silver", "hf2_vault|drop_chest_gold" })
        {
            state.Call("record_dungeon", Scene, "chests", chest_id);
        }

        foreach (string room_id in new[] { "hf1_vault", "hf1_secret", "hf2_tunnel", "hf3_dungeon" })
        {
            state.Call("record_dungeon", Scene, "rooms_entered", room_id);
        }

        state.Call("record_dungeon", Scene, "triggers", "trig_stealth_clear");
        CheckEq(service.progress(Scene)["percent"].AsInt32(), 100, "全清之后 100%");
    }

    /// <summary>层视图：每层哪些房间带敌人、清过没、能不能扫荡。</summary>
    private void _CheckFloors(TableDb db, DungeonService service)
    {
        GodotObject fresh = SoloState(db);
        DungeonService plan = DungeonService.create(db, fresh, RngService.create(Seed));
        Godot.Collections.Array listed = plan.floors(Scene);
        CheckEq(listed.Count, 3, "黑风寨三层");
        Godot.Collections.Dictionary first = listed[0].AsGodotDictionary();
        CheckEq(first["floor"].AsInt32(), 1, "第一项是第 1 层");
        CheckEq(first["enemy_rooms"].AsGodotArray().Count, 1, "第 1 层只有前院有敌人");
        CheckFalse(first["sweepable"].AsBool(), "没打过就不能扫荡");
        CheckTrue(plan.sweepable_floors(Scene).Count == 0, "新档没有可扫荡的层");
        fresh.Call("record_dungeon", Scene, "rooms", "hf1_yard");
        CheckTrue(
            plan.floors(Scene)[0].AsGodotDictionary()["sweepable"].AsBool(),
            "清过前院后第 1 层可扫荡");
        Godot.Collections.Array sweepable = plan.sweepable_floors(Scene);
        CheckEq(sweepable.Count, 1, "可扫荡层只有一层");
        CheckEq(sweepable[0].AsInt32(), 1, "可扫荡层列出第 1 层");
    }

    /// <summary>扫荡：只能扫已通关的层、只结算掉落（不给经验）。</summary>
    private void _CheckSweep(TableDb db, DungeonService service)
    {
        GodotObject fresh = SoloState(db);
        DungeonService runner = DungeonService.create(db, fresh, RngService.create(Seed));
        Godot.Collections.Dictionary early = runner.sweep(Scene, 1);
        CheckFalse(early["ok"].AsBool(), "没通关的层不能扫荡");
        CheckTrue(
            early["error"].AsString().Contains("还没通关"),
            $"说明差哪些房间：{early["error"].AsString()}");

        fresh.Call("record_dungeon", Scene, "rooms", "hf1_yard");
        int exp_before = fresh.Get("party_exp").AsInt32();
        GodotObject inventory = fresh.Get("inventory").AsGodotObject();
        int money_before = inventory.Get("money").AsInt32();
        int equipment_before = inventory.Call("equipment_count").AsInt32();
        Godot.Collections.Dictionary swept = runner.sweep(Scene, 1);
        CheckTrue(swept["ok"].AsBool(), $"通关的层能扫荡：{swept["error"].AsString()}");
        CheckEq(fresh.Get("party_exp").AsInt32(), exp_before, "扫荡不给经验（设计：仅结算掉落）");
        CheckGt(
            inventory.Get("money").AsInt32() - money_before, 0.0,
            "扫荡结算了掉落里的铜钱");
        CheckGt(swept["drops"].AsGodotArray().Count, 0.0, "掷出了掉落条目");
        CheckTrue(
            swept["summary"].AsString().Contains("铜钱"),
            $"摘要里有铜钱：{swept["summary"].AsString()}");
        CheckTrue(
            inventory.Call("equipment_count").AsInt32() >= equipment_before,
            $"掉落装备会进背包（{equipment_before} → {inventory.Call("equipment_count").AsInt32()}）");
        // 可以反复扫——这是刷子循环的效率口
        Godot.Collections.Dictionary again = runner.sweep(Scene, 1);
        CheckTrue(again["ok"].AsBool(), "同一层可以重复扫荡");
    }

    /// <summary>完成度与保底都进存档。</summary>
    private void _CheckSave(TableDb db, GodotObject state)
    {
        state.Call("record_dungeon", Scene, "rooms", "hf2_poison");
        state.Set(
            "pity",
            new Godot.Collections.Dictionary
            {
                { "version", 1 },
                {
                    "counters",
                    new Godot.Collections.Dictionary { { "drop_bd_elite|slot_1", 3 } }
                },
            });
        GodotObject back = GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), db);
        CheckNotNull(back, "带完成度的存档能读回来");
        if (back == null)
        {
            return;
        }

        CheckEq(back.Get("migrated_from").AsInt32(), 0, "同版本往返不需要迁移");
        CheckTrue(
            back.Call("dungeon_record", Scene).AsGodotDictionary()["rooms"].AsGodotArray()
                .Contains("hf2_poison"),
            "打过的房间往返一致");
        Godot.Collections.Dictionary pity = back.Get("pity").AsGodotDictionary();
        CheckEq(
            pity["counters"].AsGodotDictionary()["drop_bd_elite|slot_1"].AsInt32(), 3,
            "保底计数往返一致");
        CheckEq(
            back.Call("pity_tracker").As<PityTracker>().attempts("drop_bd_elite|slot_1"), 3,
            "保底计数器读得到");
    }

    /// <summary>
    /// 设计 07 §九 第 3 条：地图上走廊是双向的，所以 `dungeon_room.exit_rooms` 必须成对声明——
    /// a→b 写了、b→a 没写就是漏了一格（决策 326 把它从建议升成断言，防止再漂回去）。
    /// </summary>
    private void _CheckExitSymmetry(TableDb db)
    {
        var one_way = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("dungeon_room"))
        {
            if (entry.AsGodotObject() is not DungeonRoomRow row)
            {
                continue;
            }

            string room_id = row.room_id;
            foreach (string raw in row.exit_rooms.Split(
                '|', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string target = raw.StripEdges();
                if (target.Length == 0)
                {
                    continue;
                }

                // 指向不存在的房间由构建期引用校验报，这里不重复
                if (db.get_row("dungeon_room", target) is not DungeonRoomRow back)
                {
                    continue;
                }

                bool has_back = false;
                foreach (string back_raw in back.exit_rooms.Split(
                    '|', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if (back_raw.StripEdges() == room_id)
                    {
                        has_back = true;
                        break;
                    }
                }

                if (!has_back)
                {
                    one_way.Add($"{room_id}→{target}");
                }
            }
        }

        string detail = one_way.Count == 0 ? "无" : string.Join(", ", one_way);
        CheckEq(one_way.Count, 0, $"出口成对声明（单向的：{detail}）");
    }
}
