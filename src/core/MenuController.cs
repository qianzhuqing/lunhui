using Godot;

namespace Lunhui.Core;

/// <summary>
/// 启动菜单的决策逻辑（C# 版，对位原 `src/core/menu_controller.gd`）。
///
/// 与场景节点分开：这里只决定「该做什么」，退出与切场景由视图执行，
/// 所以整套菜单流程能在 headless 下被用例覆盖（见 `tests/test_menu.gd` 与 `tests/TestMenu.cs`）。
///
/// **跨语言面**：`_db` 收 `TableDb`（已是 C#）；`_store` 收 `GodotObject`——
/// 实测 `tests/test_menu.gd` 会塞一个 GDScript 的 `ReadOnlyStore` 桩进来
/// （验「存档目录不可写也要能开局」），所以这里只走 `Call`，不硬转 `SaveStore`
/// （桩有 `list_slots`／`slot_for_new_game`／`save_slot` 三个口就够）。
/// `state` 是 GDScript 的 `GameState`，读写走 `Get`／`Set`／`Call`。
/// 角色创建那半边（`CreationService.build`／`origins`／`default_name_of`）还是 GDScript，
/// 过渡期 `GD.Load&lt;GDScript&gt;(...).Call(...)`。
/// </summary>
public partial class MenuController : RefCounted
{
    public const string ACTION_PLAY = "play";
    public const string ACTION_QUIT = "quit";
    public const string ACTION_NONE = "none";

    /// <summary>
    /// 进游戏后落在哪一页（设计 09 §二）。
    /// **读档一律回大地图**（不管存档时人在哪，落点用存档里的 `world_pos`）；
    /// 新建游戏仍从占位枢纽页起步——那里有「进入大地图」的入口与音量开关。
    /// </summary>
    public const string START_HUB = "hub";

    public const string START_OVERWORLD = "overworld";

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的 `const`（施工单坑⑩）：视图与用例现在读
    // `MenuControllerScript.ACTION_PLAY` 这类名字，改成同名小写静态方法；
    // P5 清零后再把它连着调用方一起删掉。

    public static string action_play() => ACTION_PLAY;
    public static string action_quit() => ACTION_QUIT;
    public static string action_none() => ACTION_NONE;
    public static string start_hub() => START_HUB;
    public static string start_overworld() => START_OVERWORLD;

    /// <summary>
    /// 槽位视图。**必须是 `[Export]` 的公开属性**：GDScript 那边直接读 `controller.slots`
    /// （`main_menu.gd` 逐格建按钮），C# 的普通公开属性不进脚本 API（`Encounter` 那几个字段同理）。
    /// </summary>
    [Export]
    public Godot.Collections.Array slots { get; set; } = new();

    [Export]
    public string message { get; set; } = "";

    private TableDb _db;
    private GodotObject _store;
    private string[] _party_ids = System.Array.Empty<string>();
    private string _difficulty_id = "normal";

    /// <summary>
    /// 带参数的构造走静态工厂（施工单「已定型的搬运形状」第 1 条）：GDScript 的
    /// `MenuControllerScript.new(…)` 传不进实参，所以调用方一律改写成 `create(…)`。
    /// **实参写全**（C# 的默认值对 GDScript 不生效，坑②）：四个参数一个都不省。
    /// </summary>
    public static MenuController create(
        TableDb db, GodotObject store, string[] party_ids, string difficulty_id)
    {
        var controller = new MenuController
        {
            _db = db,
            _store = store,
            _party_ids = party_ids ?? System.Array.Empty<string>(),
            _difficulty_id = difficulty_id,
        };
        controller.refresh();
        return controller;
    }

    public GodotObject store() => _store;

    /// <summary>重新读一遍存档列表（返回主菜单或删档后调用）。</summary>
    public void refresh()
    {
        Godot.Collections.Array listed = _store.Call("list_slots", _db).AsGodotArray();
        slots = listed ?? new Godot.Collections.Array();
        message = "";
    }

    public bool has_saves()
    {
        foreach (Variant entry in slots)
        {
            if (entry.AsGodotDictionary()["exists"].AsBool())
            {
                return true;
            }
        }
        return false;
    }

    public int save_count()
    {
        int total = 0;
        foreach (Variant entry in slots)
        {
            if (entry.AsGodotDictionary()["exists"].AsBool())
            {
                total += 1;
            }
        }
        return total;
    }

    /// <summary>读取存档是否可用（有档才能点）。</summary>
    public bool can_load() => has_saves();

    /// <summary>新建游戏是否可用（配置表就绪才能开局）。</summary>
    public bool can_start_new_game() => _db != null && _db.tables.Count > 0;

    /// <summary>
    /// 新建游戏：挑槽位 → 造状态 → 立刻落盘（新游戏自动存档）。
    /// 返回 `{ok, action, slot, state, overwrite, message, start_scene}`。
    ///
    /// `spec` 是创建角色那两步的结果（设计 13）；传空表时用**默认出身**
    /// （`origin_def` 里 `sort_order` 最小的那张卡、不选天赋）——菜单自检与「快速开局」走的就是这条，
    /// 正式流程由 `creation_screen` 传 spec 进来。**空表要显式传**（`new_game({})`）。
    /// </summary>
    public Godot.Collections.Dictionary new_game(Godot.Collections.Dictionary spec)
    {
        if (!can_start_new_game())
        {
            return _result(
                false, ACTION_NONE, 0, null, false,
                "配置表未就绪，先运行 tools\\run_tests.bat 生成配置表");
        }
        Godot.Collections.Dictionary placement = _store.Call("slot_for_new_game").AsGodotDictionary();
        if (!placement["ok"].AsBool())
        {
            return _result(false, ACTION_NONE, 0, null, false, _text(placement, "error"));
        }
        int slot = placement["slot"].AsInt32();
        Godot.Collections.Dictionary creation = CreationService.build(_db, _with_defaults(spec));
        if (!creation["ok"].AsBool())
        {
            return _result(false, ACTION_NONE, slot, null, false, _text(creation, "error"));
        }
        GodotObject state = creation["state"].AsGodotObject();
        if (state == null || state.Call("party_size").AsInt32() == 0)
        {
            // 数据错：表名只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            // 一行写完：`tests/TestCopyAudit.cs` 的表名扫描只跳过**同一行**里带 `PushError` 的语句
            // （两份实现都是按行扫，所以那行本来就豁免）。表名只进日志，玩家看不到。
            GD.PushError("[MenuController] 开局队伍为空：character_base.csv 里没有可用的初始成员（看 recruit_def.is_initial）");
            return _result(false, ACTION_NONE, slot, null, false, "开局队伍为空（数据错，已记进日志）");
        }
        state.Set("slot", slot);
        state.Set("difficulty_id", _difficulty_id);
        Godot.Collections.Dictionary save_result = _store.Call("save_slot", slot, state).AsGodotDictionary();
        string notice = "";
        if (!save_result["ok"].AsBool())
        {
            notice = "（自动存档失败：" + _text(save_result, "error") + "，本次进度不会保留）";
        }
        else if (placement["overwrite"].AsBool())
        {
            notice = "（存档位已满，已覆盖最旧的存档）";
        }
        Godot.Collections.Dictionary out_result = _result(
            true, ACTION_PLAY, slot, state, placement["overwrite"].AsBool(), notice);
        out_result["start_scene"] = START_HUB;
        return out_result;
    }

    /// <summary>补齐创建方案里没给的字段：默认用第一张出身卡当模板。</summary>
    private Godot.Collections.Dictionary _with_defaults(Godot.Collections.Dictionary spec)
    {
        Godot.Collections.Dictionary out_spec = spec.Duplicate(true);
        if (out_spec.Count == 0)
        {
            out_spec["route"] = "origin";
        }
        if (_str(out_spec, "route", "origin") == "origin" && _str(out_spec, "origin_id", "").Length == 0)
        {
            Godot.Collections.Array cards = CreationService.origins(_db);
            if (cards != null && cards.Count > 0)
            {
                string first_id = cards[0].AsGodotDictionary()["origin_id"].AsString();
                out_spec["origin_id"] = first_id;
                out_spec["name_cn"] = DefaultNameOf(first_id);
            }
        }
        if (!out_spec.ContainsKey("talents"))
        {
            out_spec["talents"] = new Godot.Collections.Array();
        }
        string name_cn = _str(out_spec, "name_cn", "");
        if (name_cn.StripEdges().Length == 0)
        {
            string origin_id = _str(out_spec, "origin_id", "");
            Resource card = _db.get_row("origin_def", origin_id);
            out_spec["name_cn"] = card != null ? DefaultNameOf(origin_id) : "无名";
        }
        return out_spec;
    }

    /// <summary>读取存档：载入指定槽。返回 `{ok, action, slot, state, message, start_scene}`。</summary>
    public Godot.Collections.Dictionary load_game(int slot)
    {
        Godot.Collections.Dictionary result = _store.Call("load_slot", slot, _db).AsGodotDictionary();
        if (!result["ok"].AsBool())
        {
            return _result(false, ACTION_NONE, slot, null, false, _text(result, "error"));
        }
        GodotObject state = result["state"].AsGodotObject();
        string notice = $"已读取第 {slot} 格存档";
        int migrated = state.Get("migrated_from").AsInt32();
        int pruned = state.Get("pruned_characters").AsInt32();
        int reclaimed = state.Get("reclaimed_equipped").AsInt32();
        if (migrated > 0 || pruned > 0 || reclaimed > 0)
        {
            // 旧版本存档、档里有已下架的角色、或者穿着当前配表装不下的装备：整理完立刻写回，玩家无感
            Godot.Collections.Dictionary saved = _store.Call("save_slot", slot, state).AsGodotDictionary();
            if (saved["ok"].AsBool())
            {
                if (migrated > 0)
                {
                    notice = "已读取第 " + slot + " 格存档（已从 v" + migrated
                        + " 升级到 v" + state.Get("version").AsInt32() + "）";
                }
                else if (pruned > 0)
                {
                    notice = "已读取第 " + slot + " 格存档（已清理 " + pruned + " 个已下架角色）";
                }
                else
                {
                    notice = "已读取第 " + slot + " 格存档（已按当前配表退回 " + reclaimed + " 件穿不上的装备）";
                }
            }
            else
            {
                notice = "已读取第 " + slot + " 格存档（迁移写回失败：" + _text(saved, "error") + "）";
            }
        }
        Godot.Collections.Dictionary out_result = _result(true, ACTION_PLAY, slot, state, false, notice);
        // 设计 09 §二：读档一律回大地图（落点用存档里的 world_pos，没有就用默认出生点）
        out_result["start_scene"] = START_OVERWORLD;
        return out_result;
    }

    /// <summary>退出游戏：只回一个动作，真正退出交给视图（headless 用例不会真的退）。</summary>
    public Godot.Collections.Dictionary quit_game() => _result(true, ACTION_QUIT, 0, null, false, "");

    /// <summary>给 UI 用的槽位显示文案。</summary>
    public string[] slot_labels()
    {
        var out_labels = new System.Collections.Generic.List<string>();
        foreach (Variant entry in slots)
        {
            Godot.Collections.Dictionary slot = entry.AsGodotDictionary();
            out_labels.Add("第 " + slot["slot"].AsInt32() + " 格　" + _text(slot, "label"));
        }
        return out_labels.ToArray();
    }

    private Godot.Collections.Dictionary _result(
        bool ok, string action, int slot, GodotObject state, bool overwrite, string notice)
    {
        if (notice.Length > 0)
        {
            message = notice;
        }
        // **`Dictionary` 不收 C# 的 `null`**（施工单坑⑥）：没有会话状态时写一个空的 Variant，
        // GDScript 那边 `check_null(result["state"])` 才是真的过。
        Variant state_variant = default;
        if (state != null)
        {
            state_variant = state;
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", ok },
            { "action", action },
            { "slot", slot },
            { "state", state_variant },
            { "overwrite", overwrite },
            { "message", notice },
        };
    }

    /// <summary>`CreationService.default_name_of(db, origin_id)`（core 第十八批起是 C#，直调）。</summary>
    private string DefaultNameOf(string origin_id) => CreationService.default_name_of(_db, origin_id);

    /// <summary>对位 GDScript 的 `str(dict.get(key, fallback))`（缺键给兜底，其余按字符串转）。</summary>
    private static string _str(Godot.Collections.Dictionary data, string key, string fallback) =>
        data.ContainsKey(key) ? data[key].AsString() : fallback;

    /// <summary>对位 GDScript 的 `str(dict.get(key, ""))`。</summary>
    private static string _text(Godot.Collections.Dictionary data, string key) => _str(data, key, "");
}
