using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 宝箱守卫（C# 版；设计 09 §一）。
///
/// 文取／武取两条路给**同一个**箱子；没有守卫的箱子照旧直接开。这份用例盯五件事：
///   ① 表侧配对（`reward_group` 就是那间房的 `chest_id`）；
///   ② 没有守卫的房间 `is_resolved` 恒真；宝箱键能解析出房间那半边；
///   ③ 物品路：没东西时不可用、给一份就扣掉并点亮旗标；
///   ④ 判定路：门槛取表里的值、**刚好等于门槛算过**、达不到就如实不可用、判定路不消耗东西；
///   ⑤ 武取：房间记录一写（＝打赢房里那场仗）就算解决。
///
/// **2026-10-11（P4 第十七批）起这份就是唯一一份**：旧链的 `tests/test_guard.gd` 已退场。
/// 退场前逐条核对过覆盖（41 处静态断言点两边对得上，且这一份**多**一条旗标名绝对值），
/// 核对依据记在施工单「二点七三」——所以这一批没有另外新建一个重复文件。
/// </summary>
public partial class TestGuard : TestCase
{
    private const string NoGuardRoom = "hf1_shed";

    public override string SuiteName() => "宝箱守卫（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckPeaceFlagShape();
        _CheckTablePairing(db);
        _CheckNoGuardRoom(db);
        _CheckItemPath(db);
        _CheckCheckPath(db);
        _CheckFightPath(db);
    }

    /// <summary>
    /// 旗标名写死绝对值：它决定「文取付过没有」，改坏了文取会白付（箱子永远解不开）。
    /// 变异探针那条 `peace_flag` case 改的是 C# 文件（不重编量不出来），绝对值靠这条钉。
    /// </summary>
    private void _CheckPeaceFlagShape()
    {
        CheckEq(GuardService.peace_flag("guard_hc_02"), "flag_guard_guard_hc_02_peace", "文取旗标名");
    }

    private void _CheckTablePairing(TableDb db)
    {
        Godot.Collections.Array guards = db.rows("npc_guard");
        CheckGt(guards.Count, 0, $"表里有守卫（{guards.Count} 条）");
        foreach (Variant entry in guards)
        {
            var row = (NpcGuardRow)entry.AsGodotObject();
            Resource room = db.get_row("dungeon_room", row.room_id);
            CheckNotNull(room, $"{row.guard_id} 的 room_id 在 dungeon_room 里");
            CheckEq(
                room is DungeonRoomRow typed ? typed.chest_id : "", row.reward_group,
                $"{row.guard_id} 的 reward_group 就是那间房的箱子（两条路同一个箱子）");
        }
    }

    /// <summary>没有守卫的房间：`is_resolved` 恒真——照旧「进房间就能开」。</summary>
    private void _CheckNoGuardRoom(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckNull(GuardService.guard_for_room(db, NoGuardRoom), "柴房确实没有守卫行");
        CheckTrue(
            GuardService.is_resolved(db, state, "scene_heifengzhai", NoGuardRoom),
            "没有守卫的房间照旧可直接开");
        CheckEq(
            GuardService.room_of_chest_key("hf1_shed|drop_chest_copper"), "hf1_shed",
            "宝箱键能解析出房间那半边");
        CheckEq(GuardService.room_of_chest_key("没有分隔符"), "", "认不出的键返回空（不当成某个房间）");
    }

    /// <summary>物品路：没东西时不可用；给一份 → 扣掉 + 点亮旗标 + 箱子解锁。</summary>
    private void _CheckItemPath(TableDb db)
    {
        var guard = (NpcGuardRow)db.rows("npc_guard")[0].AsGodotObject();
        string scene_id = _SceneOf(db, guard);
        GodotObject state = SoloState(db);
        CheckFalse(GuardService.is_resolved(db, state, scene_id, guard.room_id), "新档：守卫还没解决");

        string item_id = "";
        foreach (Variant entry in GuardService.peace_options(db, state, guard))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["kind"].AsString() != "item")
            {
                continue;
            }
            item_id = option["id"].AsString();
            CheckFalse(option["ok"].AsBool(), "身上没有那份东西时这条路不可用：" + option["reason"].AsString());
            CheckTrue(option["reason"].AsString().Contains("背包"), "原因说清是背包里没有");
        }
        CheckNe(item_id, "", "这位守卫的文取里写了物品那半（否则上面几条是空跑）");

        string demand = GuardService.demand_text(db, state, guard);
        CheckTrue(demand.Contains(guard.npc_name_cn), "开口文案写清是谁挡着：" + demand);
        CheckTrue(demand.Contains(guard.fight_note), "另一条路直接取表里的 fight_note（不自己编）");
        CheckFalse(demand.Contains("Q53"), "给玩家看的话里不出现内部编号：" + demand);

        _Inventory(state).Call("add_item", db, item_id, 2);
        Godot.Collections.Dictionary paid = GuardService.pay_peace(db, state, guard, "item", item_id);
        CheckTrue(paid["ok"].AsBool(), "给了东西就能付账：" + paid["error"].AsString());
        CheckEq(_InventoryCount(state, item_id), 1, "只扣一份");
        CheckTrue(
            state.Call("has_flag", GuardService.peace_flag(guard.guard_id)).AsBool(), "点亮文取旗标");
        CheckTrue(GuardService.is_resolved(db, state, scene_id, guard.room_id), "付过之后这个箱子解锁");
        CheckEq(paid["text"].AsString(), guard.peace_note, "放行台词取表里的 peace_note");

        GodotObject empty = SoloState(db);
        Godot.Collections.Dictionary refused = GuardService.pay_peace(db, empty, guard, "item", item_id);
        CheckFalse(refused["ok"].AsBool(), "身上没有时付不了账（控制器第二次按 E 会走到这里）");
        CheckFalse(
            empty.Call("has_flag", GuardService.peace_flag(guard.guard_id)).AsBool(), "付失败不点旗标");
    }

    /// <summary>判定路：过门槛就放行，且**不消耗任何东西**；门槛的边界是 `have >= need`。</summary>
    private void _CheckCheckPath(TableDb db)
    {
        var guard = (NpcGuardRow)db.rows("npc_guard")[0].AsGodotObject();
        string source = guard.peace_check_source;
        CheckNe(source, "", "这位守卫写了判定路（peace_check_source）");
        CheckGt(guard.peace_check_value, 0, $"判定路有门槛值（{guard.peace_check_value}）");

        GodotObject state = SoloState(db);
        bool found = false;
        foreach (Variant entry in GuardService.peace_options(db, state, guard))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["kind"].AsString() == "item")
            {
                continue;
            }
            found = true;
            int have = option["have"].AsInt32();
            int need = option["need"].AsInt32();
            CheckGt(have, 0, "判定路的当前值算得出来：" + option);
            CheckEq(need, guard.peace_check_value, "门槛取表里的值");
            CheckEq(option["ok"].AsBool(), have >= need, $"够门槛就标可用（当前 {have}／门槛 {need}）");
            if (have >= need)
            {
                int before = _ItemIds(state).Length;
                Godot.Collections.Dictionary paid = GuardService.pay_peace(
                    db, state, guard, option["kind"].AsString(), option["id"].AsString());
                CheckTrue(paid["ok"].AsBool(), "判定过了就能付账：" + paid["error"].AsString());
                CheckTrue(
                    state.Call("has_flag", GuardService.peace_flag(guard.guard_id)).AsBool(),
                    "判定路也点亮同一枚旗标");
                CheckEq(_ItemIds(state).Length, before, "判定路**不消耗任何东西**");
            }
        }
        CheckTrue(found, "这位守卫的判定路确实进了选项列表（否则上面几条是空跑）");

        // 门槛抬到不可能达成的值 → 如实不可用（不静默放行）
        NpcGuardRow strict_row = _EditedGuard(out TableDb local, out _);
        strict_row.peace_check_value = 999;
        GodotObject strict = SoloState(local);
        bool blocked = false;
        foreach (Variant entry in GuardService.peace_options(local, strict, strict_row))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["kind"].AsString() != "item" && !option["ok"].AsBool())
            {
                blocked = true;
            }
        }
        CheckTrue(blocked, "门槛高到达不到时如实标不可用（不是悄悄放行）");
        Godot.Collections.Dictionary refused = GuardService.pay_peace(
            local, strict, strict_row, "skill", source.Split(':')[1]);
        CheckFalse(refused["ok"].AsBool(), "达不到门槛时付不了账");
        CheckFalse(
            strict.Call("has_flag", GuardService.peace_flag(guard.guard_id)).AsBool(), "失败不点旗标");

        // 边界：**刚好等于门槛算过**（`have >= need`，不是 `>`）
        NpcGuardRow exact_row = _EditedGuard(out TableDb exact, out _);
        GodotObject exact_state = SoloState(exact);
        int value = GuardService.check_value_of(exact, exact_state, exact_row.peace_check_source);
        CheckGt(value, 0, $"先拿到当前判定值（{value}）");
        exact_row.peace_check_value = value;
        CheckEq(_OnlyCheckOk(exact, exact_state, exact_row), true, "判定值 == 门槛时算通过（含等号）");
        exact_row.peace_check_value = value + 1;
        CheckEq(_OnlyCheckOk(exact, exact_state, exact_row), false, "门槛再高一点就不过（不是「永远算过」）");
    }

    /// <summary>武取：房间记录一写（＝打赢房里那场仗）守卫就算解决，不需要额外按键。</summary>
    private void _CheckFightPath(TableDb db)
    {
        var guard = (NpcGuardRow)db.rows("npc_guard")[0].AsGodotObject();
        GodotObject state = SoloState(db);
        string scene_id = _SceneOf(db, guard);
        var room = db.get_row("dungeon_room", guard.room_id) as DungeonRoomRow;
        CheckNotNull(room, "守卫那间房在 dungeon_room 里");
        CheckEq(guard.fight_team, room != null ? room.enemy_team : "", "武取打的就是房里那支队伍（表里是同一条）");
        CheckFalse(GuardService.is_resolved(db, state, scene_id, guard.room_id), "还没打：先确认初始状态");
        state.Call("record_dungeon", scene_id, "rooms", guard.room_id);
        CheckTrue(GuardService.is_resolved(db, state, scene_id, guard.room_id), "打赢房间之后箱子解锁");
    }

    /// <summary>
    /// 内存夹具：拿一份**本用例私有的**表库，把第一条守卫改成「只有判定路」的副本。
    /// 表资源是按路径缓存的，直接改会污染别的用例，所以复制表、再复制那一行。
    /// </summary>
    private NpcGuardRow _EditedGuard(out TableDb local, out TableResource table)
    {
        local = new TableDb();
        local.load_all();
        var source = (TableResource)local.tables["npc_guard"].AsGodotObject();
        table = (TableResource)source.Duplicate(true);
        var row = (NpcGuardRow)((NpcGuardRow)table.rows[0].AsGodotObject()).Duplicate(true);
        row.peace_item = "";
        table.rows[0] = row;
        local.tables["npc_guard"] = table;
        return row;
    }

    /// <summary>只读判定路那一格的 `ok`（没有判定路时返回 false）。</summary>
    private static bool _OnlyCheckOk(TableDb db, GodotObject state, NpcGuardRow row)
    {
        foreach (Variant entry in GuardService.peace_options(db, state, row))
        {
            Godot.Collections.Dictionary option = entry.AsGodotDictionary();
            if (option["kind"].AsString() != "item")
            {
                return option["ok"].AsBool();
            }
        }
        return false;
    }

    private static string _SceneOf(TableDb db, NpcGuardRow guard) =>
        db.get_row("dungeon_room", guard.room_id) is DungeonRoomRow room ? room.scene_id : "";

    private static GodotObject _Inventory(GodotObject state) =>
        state.Get("inventory").AsGodotObject();

    private static int _InventoryCount(GodotObject state, string item_id) =>
        _Inventory(state).Call("count", item_id).AsInt32();

    private static string[] _ItemIds(GodotObject state) =>
        _Inventory(state).Call("item_ids").AsStringArray();
}
