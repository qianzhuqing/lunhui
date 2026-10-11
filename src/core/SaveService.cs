using Godot;

namespace Lunhui.Core;

/// <summary>
/// 存档时机（C# 版，对位原 `src/core/save_service.gd`；设计 02_地图与明雷.md「存档规则」）。
///
/// | 地点 | 规则 | 落在哪 |
/// |---|---|---|
/// | 城镇 | 存档点，可自由存 | 小地图里按 F5（`save_game`）手动存；占位场景有「保存游戏」按钮 |
/// | 大地图 | 切换小地图时自动存 | `enter_local_map()` 进入前存一次 |
/// | 副本内 | 自动存，退出重进不掉本层进度 | 清房间（战斗结算）、开宝箱、触发点位、扫荡、事件判定后各存一次 |
///
/// 「读」留在主菜单（存档槽列表已经在那儿），游戏内只做保存，免得两处各写一套槽位逻辑。
/// </summary>
public partial class SaveService : RefCounted
{
    /// <summary>
    /// 存档槽。**故意收成 `GodotObject` 而不是 `SaveStore`**：用例会注入只有 `save_slot(slot, state)`
    /// 的 GDScript 桩（`FailingStore`／`ReadOnlyStore`）来验「写不进去时不静默」，
    /// 而 GDScript 桩不可能是 C# 的 `SaveStore`——参数写死类型会在注入那一刻抛
    /// `InvalidCastException: Unable to cast 'Godot.RefCounted' to 'Lunhui.Core.SaveStore'`，
    /// 而且那条错误发生在**调用点**，看起来像「保存坏了」而不是「桩类型不对」。
    /// GDScript 侧只通过 `save()`／`describe()` 用，不直接读这个字段，
    /// 所以不给它 `[Export]`——`RefCounted` 也不是可导出的类型。
    /// </summary>
    public GodotObject store { get; set; }

    /// <summary>当前这一局的 `GameState`（还是 GDScript，所以按 `GodotObject` 拿着走 `Call`／`Get`）。</summary>
    public GodotObject state { get; set; }

    // 最近一次保存的结果，界面拿它播提示
    [Export]
    public bool last_ok { get; set; }

    [Export]
    public string last_reason { get; set; } = "";

    [Export]
    public bool last_auto { get; set; }

    [Export]
    public string last_error { get; set; } = "";

    public SaveService(GodotObject save_store, GodotObject game_state)
    {
        store = save_store;
        state = game_state;
    }

    /// <summary>GDScript 侧的构造入口（带参数的 `new(…)` 对 C# 脚本是解析期报错，见施工单坑②）。</summary>
    public static SaveService create(GodotObject save_store, GodotObject game_state) =>
        new(save_store, game_state);

    /// <summary>能不能存：有会话状态、有存档槽、目录可写。</summary>
    public Godot.Collections.Dictionary can_save()
    {
        if (state == null)
        {
            return Refuse("没有会话状态");
        }
        if (state.Get("slot").AsInt32() <= 0)
        {
            return Refuse("这一局还没有存档槽（从主菜单新建或读档才有）");
        }
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" } };
    }

    /// <summary>
    /// 保存一次。`automatic=true` 表示自动存档（提示文案不同）。
    /// 返回 {ok, error, slot, reason, automatic}。
    /// </summary>
    public Godot.Collections.Dictionary save(string reason, bool automatic)
    {
        Godot.Collections.Dictionary ready = can_save();
        if (!ready["ok"].AsBool())
        {
            last_ok = false;
            last_error = ready["error"].AsString();
            return SaveResult(false, last_error, 0, reason, automatic);
        }
        int slot = state.Get("slot").AsInt32();
        Godot.Collections.Dictionary result = store is SaveStore typed
            ? typed.save_slot(slot, state)
            : store.Call("save_slot", slot, state).AsGodotDictionary();
        last_ok = result["ok"].AsBool();
        last_error = result["error"].AsString();
        last_reason = reason;
        last_auto = automatic;
        return SaveResult(last_ok, last_error, slot, reason, automatic);
    }

    /// <summary>状态栏用的一句话：成功写「已自动保存（清房间）」，失败写原因。</summary>
    public string describe()
    {
        if (!last_ok)
        {
            return $"存档失败：{last_error}";
        }
        return $"{(last_auto ? "已自动" : "已")}保存成功（{last_reason}）";
    }

    /// <summary>
    /// 默认存档设施（名字是历史遗留：返回的是 **SaveStore**，不是 SaveService——
    /// 调用方拿到它再去 `create(store, state)`）。
    /// </summary>
    public static SaveStore make_default() =>
        SaveStore.create(SaveStore.default_dir(), SaveStore.DEFAULT_SLOT_COUNT);

    private static Godot.Collections.Dictionary Refuse(string error) =>
        new() { { "ok", false }, { "error", error } };

    private static Godot.Collections.Dictionary SaveResult(
        bool ok, string error, int slot, string reason, bool automatic) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "slot", slot },
            { "reason", reason },
            { "automatic", automatic },
        };
}
