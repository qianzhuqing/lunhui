using Godot;
using Lunhui.Core;

namespace Lunhui.Tests;

/// <summary>
/// 存档时机（02_地图与明雷.md「存档规则」）：城镇手动存、大地图切图自动存、副本内自动存——对位
/// `tests/test_save_timing.gd`（2026-10-11 P4 第六批搬 C#）。
///
/// 这一份钉两层：`SaveService` 的「没槽位不让存／有槽位落盘并记下原因」，以及**存档内容真的是当前进度**
/// （等级／加点／背包／装备实例／完成度／事件判定读回来一模一样）。
/// </summary>
public partial class TestSaveTiming : TestCase
{
    private const string TestDir = "res://.logs/test_save_timing";
    private const string CharId = "scholar_fallen";

    public override string SuiteName() => "存档时机";

    public override void Run()
    {
        TableDb db = GetDb();
        _clean_dir(TestDir);
        _check_service(db);
        _check_round_trip(db);
    }

    /// <summary>`SaveService`：没槽位不让存；有槽位落盘并记下原因。</summary>
    private void _check_service(TableDb db)
    {
        SaveStore store = SaveStore.create(TestDir, 3);
        GodotObject bare = SoloState(db);
        var gd_state = (GameState)bare;
        SaveService service = SaveService.create(store, bare);
        CheckFalse(_b(service.can_save(), "ok"), "没有存档槽时不能存");
        CheckTrue(_s(service.can_save(), "error").Contains("存档槽"), $"说明是缺槽位：{_s(service.can_save(), "error")}");
        Godot.Collections.Dictionary refused = service.save("测试", false);
        CheckFalse(_b(refused, "ok"), "没槽位时保存被拒");
        CheckFalse(store.slot_exists(2), "被拒时不会写文件");

        // 给个槽位：正常落盘
        gd_state.slot = 2;
        Godot.Collections.Dictionary saved = service.save("城镇存档点", false);
        CheckTrue(_b(saved, "ok"), $"有槽位就能存：{_s(saved, "error")}");
        CheckEq(_i(saved, "slot"), 2, "写在当前槽位");
        CheckTrue(store.slot_exists(2), "文件真的落盘了");
        CheckGt(gd_state.saved_unix, 0.0, "更新时间戳");
        CheckEq(service.last_reason, "城镇存档点", "记下存档原因");
        CheckFalse(service.last_auto, "手动存档标记对");
        CheckTrue(service.describe().Contains("保存成功"), $"提示文案：{service.describe()}");
        // 自动存档：文案不同，仍是同一个槽
        Godot.Collections.Dictionary auto = service.save("清房间", true);
        CheckTrue(_b(auto, "ok") && service.last_auto, "自动存档也走同一条路");
        CheckTrue(service.describe().Contains("已自动保存"), $"自动存档文案：{service.describe()}");
        CheckEq(gd_state.slot, 2, "自动存档不会换槽");
    }

    /// <summary>存档内容真的是当前进度：等级、加点、背包、完成度都要在。</summary>
    private void _check_round_trip(TableDb db)
    {
        SaveStore store = SaveStore.create(TestDir, 3);
        GodotObject state_object = SoloState(db);
        var state = (GameState)state_object;
        state.slot = 1;
        state.append_level(CharId, 1);
        state.spend_point(db, CharId, "str");
        ((Inventory)state.inventory).add_item(db, "item_herb", 3);
        ((Inventory)state.inventory).add_equipment(db, "eq_sword_02", new Godot.Collections.Array());
        state.record_dungeon("scene_heifengzhai", "chests", "hf1_shed|drop_chest_copper");
        state.record_event_check("ev_shed_trap", "done");
        state.party_exp = 77;
        int level_before = state.level_of(CharId);
        int str_before = (int)CharacterSheet.create(db, state, CharId).total_attrs()["str"].AsDouble();

        SaveService service = SaveService.create(store, state);
        CheckTrue(_b(service.save("测试落盘", false), "ok"), "保存成功");
        Godot.Collections.Dictionary loaded = store.load_slot(1, db);
        CheckTrue(_b(loaded, "ok"), $"读回来成功：{_s(loaded, "error")}");
        if (!_b(loaded, "ok"))
        {
            return;
        }

        var back = (GameState)loaded["state"].AsGodotObject();
        CheckEq(back.level_of(CharId), level_before, "等级在存档里");
        CheckEq(
            (int)CharacterSheet.create(db, back, CharId).total_attrs()["str"].AsDouble(),
            str_before,
            "加点效果在存档里");
        CheckEq(((Inventory)back.inventory).count("item_herb"), 3, "背包在存档里");
        CheckEq(((Inventory)back.inventory).equipment_count(), 3, "装备实例在存档里（0.10.2 起初始装备两件）");
        CheckEq(back.party_exp, 77, "队伍经验在存档里");
        CheckTrue(
            back.dungeon_record("scene_heifengzhai")["chests"].AsGodotArray().Contains("hf1_shed|drop_chest_copper"),
            "完成度在存档里");
        CheckEq(back.event_check_result("ev_shed_trap"), "done", "事件判定在存档里");
    }

    /// <summary>用例自己清场：上一条用例留下的槽位文件会让「没槽位」那几条断言失去意义。</summary>
    private static void _clean_dir(string path)
    {
        if (DirAccess.DirExistsAbsolute(path))
        {
            DirAccess dir = DirAccess.Open(path);
            if (dir != null)
            {
                foreach (var file_name in dir.GetFiles())
                {
                    dir.Remove(file_name);
                }
            }
        }

        DirAccess.MakeDirRecursiveAbsolute(path);
    }

    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();
}
