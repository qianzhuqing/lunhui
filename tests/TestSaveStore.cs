using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 存档槽与存档时机（C# 版）。
///
/// 用 `res://.logs/` 下的临时目录，不依赖 `user://` 的写权限（沙箱里 `user://` 不可写）。
/// 开局夹具走 `TestCase.NewGame`（`GameState.new_game`）——正是这一层把
/// 「存档写盘→读回→字段还在」整条路在 C# 侧也盖住了。
///
/// **2026-10-11（P4 第二十四批）起 `tests/test_save_store.gd` 已并入本文件后退场**
/// （`tests/test_save_timing.gd` 更早一批已退场）。那一批补进来三块：开场队伍照 `recruit_def`
/// 逐人一致＋落盘 JSON 的合法性与版本号、**全字段往返**（27 个字段全设成非默认值再 diff）、
/// 「三个可用档按 `saved_unix` 选最旧」与**存档体积量级**（4 人＋大背包在 1~64 KB 且往返不膨胀）。
/// </summary>
public partial class TestSaveStore : TestCase
{
    private const string TestDir = "res://.logs/test_save_store_cs";
    private const int Slots = 3;

    public override string SuiteName() => "存档槽与存档时机";

    public override void Run()
    {
        TableDb db = GetDb();
        CleanDir(TestDir);
        SaveStore store = SaveStore.create(TestDir, Slots);

        CheckTrue(store.ensure_dir(), "存档目录应能创建");
        CheckEq(SaveStore.default_dir_name(), "user://saves", "默认目录常量是 user://saves");
        CheckEq(SaveStore.default_slot_count(), 6, "默认槽位数 = 6（改它要连用例一起改）");
        CheckEq(store.slot_count, Slots, "槽位数照传进来的值");
        CheckEq(store.base_dir, TestDir, "目录照传进来的值");
        CheckEq(store.slot_path(1), TestDir + "/slot_01.json", "槽位路径按 slot_%02d 拼");

        _CheckEmpty(store);
        _CheckRoundTrip(db, store);
        _CheckEveryFieldRoundTrip(db, store);
        _CheckCorrupt(store);
        _CheckSlotPicking(db, store);
        _CheckDelete(store);
        _CheckSaveSize(db);
        _CheckService(db, store);
    }

    private void _CheckEmpty(SaveStore store)
    {
        CheckFalse(store.has_any_save(), "空目录里没有存档");
        CheckEq(store.first_free_slot(), 1, "第一个空槽是 1");
        CheckEq(store.oldest_slot(), -1, "没有存档时没有最旧槽");
        CheckEq(store.first_corrupt_slot(), -1, "没有存档时没有损坏槽");
        Godot.Collections.Array slots = store.list_slots(null);
        CheckEq(slots.Count, Slots, "槽位数量与配置一致");
        foreach (Variant entry in slots)
        {
            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            CheckFalse(row["exists"].AsBool(), $"第 {row["slot"].AsInt32()} 格应为空");
            CheckEq(row["label"].AsString(), "空存档位", "空槽的显示文案");
        }
    }

    private void _CheckRoundTrip(TableDb db, SaveStore store)
    {
        GodotObject state = NewGame(db, "normal");
        // 0.10.0 起开局队伍 = `recruit_def.is_initial=1` 的成员（设计 09 §3.2：开局只有书生一人，
        // 同伴在剧情里加入），不再取 character_base 的前 4 行。按**表**核，不写死人数。
        string[] want_party = GameState.default_party_ids_from_recruit(db);
        CheckEq(
            state.Call("party_size").AsInt32(), want_party.Length,
            $"开局队伍取 recruit_def 的初始成员（{want_party.Length} 人）");
        CheckEq(
            _JoinIds(state.Get("char_ids").AsStringArray()),
            _JoinIds(want_party),
            "开局队伍逐人一致（顺序也照 recruit_def）");
        CheckEq(state.Get("difficulty_id").AsString(), "normal", "新游戏默认普通难度");

        Godot.Collections.Dictionary saved = store.save_slot(1, state);
        CheckTrue(saved["ok"].AsBool(), $"存档应写入成功：{saved["error"].AsString()}");
        CheckTrue(store.slot_exists(1), "槽位 1 有文件");
        CheckTrue(FileAccess.FileExists(store.slot_path(1)), "存档路径与预期一致");
        CheckTrue(store.has_any_save(), "写入后 has_any_save 为真");

        Godot.Collections.Dictionary loaded = store.load_slot(1, null);
        CheckTrue(loaded["ok"].AsBool(), $"存档应能读回：{loaded["error"].AsString()}");
        GodotObject restored = loaded["state"].AsGodotObject();
        CheckNotNull(restored, "读回的 state 不是空");
        if (restored == null)
        {
            return;
        }
        CheckEq(
            restored.Call("party_size").AsInt32(), state.Call("party_size").AsInt32(),
            "队伍人数读回一致");
        CheckEq(restored.Get("difficulty_id").AsString(), "normal", "难度读回一致");
        CheckEq(restored.Get("slot").AsInt32(), 1, "读回后槽位号会补上");
        CheckGt(restored.Get("saved_unix").AsInt64(), 0.0, "保存时间被记录");
        CheckTrue(
            restored.Call("short_label", db).AsString().Contains("Lv"),
            "存档条目显示队伍与等级");
        CheckTrue(
            restored.Call("short_label", db).AsString().Contains("-"),
            "存档条目显示保存时间");

        Godot.Collections.Dictionary entry = store.describe_slot(1, db);
        CheckTrue(entry["exists"].AsBool(), "枚举能看到槽位 1");
        CheckFalse(entry["corrupt"].AsBool(), "刚写的存档不应判定为损坏");
        CheckEq(entry["slot"].AsInt32(), 1, "条目里带槽位号");
        CheckTrue(
            entry["label"].AsString().Contains("Lv"),
            $"带 db 时条目文案走 GameState.short_label：{entry["label"].AsString()}");
        CheckEq(store.first_free_slot(), 2, "写入槽 1 后第一个空槽是 2");

        // 落盘的是**当前版本**的 JSON：结构自检过、版本号对上（读档迁移靠它认老档）
        string raw = FileAccess.GetFileAsString(store.slot_path(1));
        Variant parsed = Json.ParseString(raw);
        CheckTrue(GameState.is_valid_dict(parsed), "落盘的是合法 JSON 结构");
        CheckEq(
            parsed.AsGodotDictionary()["version"].AsInt32(), GameState.save_version(),
            "存档带结构版本号");
    }

    /// <summary>
    /// 全字段往返：把**每一个**存档字段都设成非默认值，落盘再读回，两份 `to_dict` 必须零差异。
    ///
    /// 这条兜的是「字段级遗漏」：写了却没人读、读了却没写、或者新字段只加了一半——
    /// 逐个字段写断言容易漏，比字典更省事也更严。
    /// </summary>
    private void _CheckEveryFieldRoundTrip(TableDb db, SaveStore store)
    {
        GodotObject state = NewGame(db, "normal");
        var game = (GameState)state;
        string char_id = game.char_ids[0].AsString();
        game.difficulty_id = "nightmare";
        game.chapter_id = "chapter_99";
        game.created_unix = 111;
        game.char_levels[char_id] = 12;

        // 背包：材料 + 带词条的装备 + 穿戴映射
        var inventory = (Inventory)game.inventory;
        inventory.money = 321;
        inventory.add_item(db, "item_herb", 5);
        string ring = inventory.add_equipment(
            db,
            "eq_ring_01",
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary
                {
                    { "affix_id", "af_str_01" }, { "target", "attr:str" },
                    { "value_kind", "attr_point" }, { "value", 2.0 },
                },
            });
        Godot.Collections.Dictionary equipped =
            inventory.equip(db, char_id, game.level_of(char_id), "", ring);
        CheckTrue(equipped["ok"].AsBool(), $"测试用的戒指能戴上：{_Text(equipped, "error")}");

        game.char_allocations[char_id] = new Godot.Collections.Dictionary { { "str", 2 }, { "con", 1 } };
        game.party_exp = 456;
        game.skill_mastery[char_id] = new Godot.Collections.Dictionary { { "sk_xuanwei_01", 7 } };
        game.learn_skill(char_id, "sk_wudu_01");
        game.set_loadout(char_id, new[] { "sk_xuanwei_01" }, System.Array.Empty<string>());
        game.record_purchase("bld_smith", "eq_sword_01", 2);
        game.push_buyback(
            "bld_smith",
            new Godot.Collections.Dictionary { { "item_id", "item_herb" }, { "qty", 1 }, { "price", 5 } },
            20);
        game.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper");
        game.record_dungeon("scene_heifengzhai", "triggers", "trig_wine");
        game.record_dungeon("scene_heifengzhai", "rooms", "hf1_yard");
        game.record_dungeon("scene_heifengzhai", "rooms_entered", "hf1_shed");
        game.record_dungeon("scene_heifengzhai", "bosses", "hf3_dungeon");
        PityTracker pity = game.pity_tracker();
        pity.register_attempt("drop_bd_elite|dr_elite_03");
        game.store_pity(pity);
        game.reveal_node("n_cave_collapse");
        game.record_event_check("ev_force_gate", "failed");
        game.set_flag("flag_demo", true);
        game.mark_first_kill("drop_bd_boss");
        game.set_current_hp(char_id, 33);
        game.set_world_pos(new Vector2(1234.5f, -678.25f));

        Godot.Collections.Dictionary saved = store.save_slot(2, state);
        CheckTrue(saved["ok"].AsBool(), $"全字段存档写入成功：{saved["error"].AsString()}");
        Godot.Collections.Dictionary loaded = store.load_slot(2, db);
        CheckTrue(loaded["ok"].AsBool(), $"全字段存档读出成功：{loaded["error"].AsString()}");
        if (!loaded["ok"].AsBool())
        {
            return;
        }

        var loaded_state = (GameState)loaded["state"].AsGodotObject();
        CheckEq(loaded_state.migrated_from, 0, "当前版本的档不需要迁移");
        string diff = _DiffPath(state.Call("to_dict"), loaded_state.to_dict());
        CheckEq(diff, "", $"全字段往返零差异（第一处差异：{diff}）");
        // 27 = 26 + v15 的 npc_favor（NPC 好感）＋ v16 的普攻位（char_loadouts 里那一项不占字段）；
        // 新增字段要一起登记
        CheckEq(game.to_dict().Count, 27, "to_dict 的字段数没变（新增字段要一起登记）");
        CheckEq(loaded_state.world_position(), new Vector2(1234.5f, -678.25f), "大地图坐标（v13）往返一致");
        // 自查：比较器本身真的能报差异，否则上面那条断言就是空的
        CheckNe(
            _DiffPath(
                new Godot.Collections.Dictionary { { "a", 1 }, { "b", 2 } },
                new Godot.Collections.Dictionary { { "a", 1 } }),
            "", "比较器能发现丢字段");
        CheckNe(
            _DiffPath(
                new Godot.Collections.Dictionary { { "a", 1 } },
                new Godot.Collections.Dictionary { { "a", 2 } }),
            "", "比较器能发现值不同");
        CheckNe(
            _DiffPath(
                new Godot.Collections.Dictionary { { "a", new Godot.Collections.Array { 1, 2 } } },
                new Godot.Collections.Dictionary { { "a", new Godot.Collections.Array { 1, 2, 3 } } }),
            "", "比较器能发现数组长度不同");
        CheckEq(
            _DiffPath(
                new Godot.Collections.Dictionary
                {
                    {
                        "a",
                        new Godot.Collections.Array
                        {
                            1,
                            new Godot.Collections.Dictionary { { "b", true } },
                        }
                    },
                },
                new Godot.Collections.Dictionary
                {
                    {
                        "a",
                        new Godot.Collections.Array
                        {
                            1,
                            new Godot.Collections.Dictionary { { "b", true } },
                        }
                    },
                }),
            "", "比较器对相同结构返回空");
    }

    /// <summary>递归比较两份 JSON 结构，返回第一处不同的路径；完全一致返回空串。</summary>
    private static string _DiffPath(Variant a, Variant b, string path = "root")
    {
        if (a.VariantType == Variant.Type.Dictionary && b.VariantType == Variant.Type.Dictionary)
        {
            Godot.Collections.Dictionary a_dict = a.AsGodotDictionary();
            Godot.Collections.Dictionary b_dict = b.AsGodotDictionary();
            Godot.Collections.Array keys = _UnionKeys(a_dict, b_dict);
            foreach (Variant key in keys)
            {
                if (!a_dict.ContainsKey(key))
                {
                    return $"{path}.{key}（读回时多了）";
                }

                if (!b_dict.ContainsKey(key))
                {
                    return $"{path}.{key}（读回时丢了）";
                }

                string sub = _DiffPath(a_dict[key], b_dict[key], $"{path}.{key}");
                if (sub.Length > 0)
                {
                    return sub;
                }
            }

            return "";
        }

        if (a.VariantType == Variant.Type.Array && b.VariantType == Variant.Type.Array)
        {
            Godot.Collections.Array a_array = a.AsGodotArray();
            Godot.Collections.Array b_array = b.AsGodotArray();
            if (a_array.Count != b_array.Count)
            {
                return $"{path}（长度 {a_array.Count} vs {b_array.Count}）";
            }

            for (var index = 0; index < a_array.Count; index++)
            {
                string sub = _DiffPath(a_array[index], b_array[index], $"{path}[{index}]");
                if (sub.Length > 0)
                {
                    return sub;
                }
            }

            return "";
        }

        bool a_number = a.VariantType is Variant.Type.Float or Variant.Type.Int;
        bool b_number = b.VariantType is Variant.Type.Float or Variant.Type.Int;
        if (a_number || b_number)
        {
            return Mathf.IsEqualApprox(a.AsDouble(), b.AsDouble()) ? "" : $"{path}（{a} vs {b}）";
        }

        return a.ToString() == b.ToString() ? "" : $"{path}（{a} vs {b}）";
    }

    private static Godot.Collections.Array _UnionKeys(
        Godot.Collections.Dictionary a_dict, Godot.Collections.Dictionary b_dict)
    {
        var keys = new Godot.Collections.Array();
        var seen = new Godot.Collections.Dictionary();
        foreach (Variant key in a_dict.Keys)
        {
            seen[key] = true;
            keys.Add(key);
        }

        foreach (Variant key in b_dict.Keys)
        {
            if (!seen.ContainsKey(key))
            {
                seen[key] = true;
                keys.Add(key);
            }
        }

        return keys;
    }

    private void _CheckCorrupt(SaveStore store)
    {
        WriteText(store.slot_path(2), "{ 这不是合法 JSON");
        Godot.Collections.Dictionary entry = store.describe_slot(2, null);
        CheckTrue(entry["exists"].AsBool(), "损坏文件仍算「存在」");
        CheckTrue(entry["corrupt"].AsBool(), "非 JSON 应判定为损坏");
        CheckEq(entry["label"].AsString(), "存档损坏", "损坏槽的显示文案");
        CheckFalse(store.load_slot(2, null)["ok"].AsBool(), "损坏存档读不出来而不是崩溃");
        CheckTrue(store.has_any_save(), "有损坏存档时仍算有存档");

        // 结构对但字段缺失，同样按损坏处理
        WriteText(store.slot_path(3), Json.Stringify(new Godot.Collections.Dictionary
        {
            { "version", 1 },
            { "char_ids", new Godot.Collections.Array() },
        }));
        CheckTrue(store.describe_slot(3, null)["corrupt"].AsBool(), "字段缺失的存档判定为损坏");
    }

    private void _CheckSlotPicking(TableDb db, SaveStore store)
    {
        // 槽 1 正常、2/3 损坏 → 没有空槽，先回收损坏槽
        Godot.Collections.Dictionary placement = store.slot_for_new_game();
        CheckTrue(placement["ok"].AsBool(), "满了也要给出可用槽");
        CheckTrue(placement["overwrite"].AsBool(), "没有空槽时是覆盖");
        CheckEq(placement["slot"].AsInt32(), 2, "优先回收损坏的槽位");
        CheckEq(store.oldest_slot(), 1, "最旧可用存档跳过损坏槽");
        CheckEq(store.first_free_slot(), -1, "三个槽都占满了");

        // 人为写三个**可用**且时间不同的存档，验证「最旧」是按 saved_unix 判定
        CleanDir(TestDir);
        for (var slot = 1; slot <= Slots; slot++)
        {
            GameState state = GameState.new_game(db, "normal", System.Array.Empty<string>(), slot);
            Godot.Collections.Dictionary data = state.to_dict();
            data["saved_unix"] = 1000 * slot;
            WriteText(store.slot_path(slot), Json.Stringify(data));
        }

        CheckEq(store.oldest_slot(), 1, "saved_unix 最小的槽最旧");
        CheckEq(store.slot_for_new_game()["slot"].AsInt32(), 1, "无空槽时覆盖最旧的那个");
        CheckEq(store.first_free_slot(), -1, "三个槽都占满");
        CheckEq(store.first_corrupt_slot(), -1, "没有损坏槽可回收");
    }

    private void _CheckDelete(SaveStore store)
    {
        CheckTrue(store.delete_slot(1), "删档成功");
        CheckFalse(store.slot_exists(1), "删档后文件不在了");
        CheckFalse(store.delete_slot(1), "删不存在的档返回 false");
        CheckEq(store.first_free_slot(), 1, "删档后槽位重新可用");
    }

    /// <summary>存档时机那一层：没有槽位要说清楚、存完记下原因、自动与手动文案不同。</summary>
    private void _CheckService(TableDb db, SaveStore store)
    {
        CheckNotNull(SaveService.make_default(), "make_default 给的是默认存档设施");

        GodotObject bare = NewGame(db, "normal");
        bare.Set("slot", 0);
        var service = SaveService.create(store, bare);
        CheckFalse(service.can_save()["ok"].AsBool(), "没有存档槽时不能存");
        CheckTrue(
            service.can_save()["error"].AsString().Contains("存档槽"),
            $"说明是缺槽位：{service.can_save()["error"].AsString()}");
        CheckFalse(service.save("测试", false)["ok"].AsBool(), "没有槽位时保存被拒");
        CheckFalse(service.last_ok, "失败要记下来（界面照实说）");
        CheckTrue(service.describe().Contains("存档失败"), $"失败文案：{service.describe()}");

        bare.Set("slot", 1);
        CheckTrue(service.can_save()["ok"].AsBool(), "有槽位就能存");
        Godot.Collections.Dictionary saved = service.save("城镇存档点", false);
        CheckTrue(saved["ok"].AsBool(), $"保存成功：{service.last_error}");
        CheckEq(saved["slot"].AsInt32(), 1, "回执里带槽位号");
        CheckEq(service.last_reason, "城镇存档点", "记下存档原因");
        CheckFalse(service.last_auto, "手动存档标记对");
        CheckTrue(service.describe().Contains("保存成功"), $"提示文案：{service.describe()}");

        Godot.Collections.Dictionary auto = service.save("清房间", true);
        CheckTrue(auto["ok"].AsBool() && service.last_auto, "自动存档也走同一条路");
        CheckEq(auto["automatic"].AsBool(), true, "回执里标了自动");
        CheckTrue(
            service.describe().Contains("已自动保存"),
            $"自动存档文案：{service.describe()}");
    }

    /// <summary>
    /// 存档体积：把「4 人 + 大背包（道具堆满 + 30 件装备）+ 熟练度/加点/装配/副本记录/首杀/揭开」
    /// 都塞进去，量一份 JSON 有多大，并验证**读回来再写回去不会膨胀**。
    /// 不追求小，只防「数量级」的意外：比如不小心把整张配置表、每帧日志或重复实例写进存档
    /// （这类错不会报错，只会让存档悄悄涨到几百 KB～几 MB）。实际值会打进日志，给后续做参考。
    /// </summary>
    private void _CheckSaveSize(TableDb db)
    {
        TableDb wide = TableWithNChars(4);
        GodotObject state = PartyState(wide, 4);
        var game = (GameState)state;
        Godot.Collections.Array ids = game.char_ids;
        string first_id = ids[0].AsString();
        var inventory = (Inventory)game.inventory;
        inventory.money = 999999;
        game.party_exp = 123456;
        foreach (Variant entry in db.rows("item_base"))
        {
            var row = entry.AsGodotObject() as ItemBaseRow;
            if (row == null || row.item_type == "currency")
            {
                continue;
            }

            inventory.add_item(db, row.item_id, System.Math.Max(1, row.stack_max));
        }

        foreach (string base_id in new[] { "eq_sword_01", "eq_ring_01", "eq_head_01", "eq_armor_01", "eq_acc_01" })
        {
            for (var index = 0; index < 6; index++)
            {
                string instance_id = inventory.add_equipment(db, base_id, new Godot.Collections.Array());
                var base_row = wide.get_row("character_base", first_id) as CharacterBaseRow;
                inventory.equip(
                    wide, first_id, game.level_of(first_id), base_row.weapon_type, instance_id);
            }
        }

        foreach (Variant char_variant in ids)
        {
            string char_id = char_variant.AsString();
            game.set_mastery(char_id, "sk_xuanwei_01", 5, 10);
            game.spend_point(wide, char_id, "str");
        }

        game.record_dungeon("scene_heifengzhai", "chests", "hf1_yard|chest_01");
        game.mark_first_kill("drop_bd_boss");

        string text = Json.Stringify(game.to_dict(), "  ");
        double kb = text.Length / 1024.0;
        GD.Print($"  · 存档体积观测：4 人 + 大背包 = {kb:F1} KB（{text.Length} 字符）");
        CheckGt(kb, 1.0, $"真写进去了东西（实际 {kb:F1} KB）");
        CheckLt(kb, 64.0, $"存档体积在合理量级（实际 {kb:F1} KB，上限 64 KB）");

        GameState back = GameState.from_dict(Json.ParseString(text).AsGodotDictionary(), wide);
        CheckNotNull(back, "这份大档能读回来");
        if (back == null)
        {
            return;
        }

        string again = Json.Stringify(back.to_dict(), "  ");
        double growth = (again.Length - text.Length) / (double)System.Math.Max(1, text.Length);
        CheckLt(
            System.Math.Abs(growth), 0.02,
            $"读回来再写回去体积基本不变（变化 {growth * 100.0:F2}%）");
    }

    /// <summary>`str(PackedStringArray)` 的可比形式（Godot 的数组判等不比内容）。</summary>
    private static string _JoinIds(string[] ids) => string.Join(",", ids);

    /// <summary>对位 GDScript 的 `dict.get(key, "")`：缺键给空串（C# 直接取会抛）。</summary>
    private static string _Text(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static void WriteText(string path, string text)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file?.StoreString(text);
        file?.Close();
    }

    private static void CleanDir(string path)
    {
        if (DirAccess.DirExistsAbsolute(path))
        {
            DirAccess dir = DirAccess.Open(path);
            if (dir != null)
            {
                foreach (string file_name in dir.GetFiles())
                {
                    dir.Remove(file_name);
                }
            }
        }
        DirAccess.MakeDirRecursiveAbsolute(path);
    }
}
