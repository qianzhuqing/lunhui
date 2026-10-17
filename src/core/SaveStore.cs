using Godot;

namespace Lunhui.Core;

/// <summary>
/// 存档槽的读写与枚举（C# 版，对位原 `src/core/save_store.gd`）。
///
/// 这一版只做「多存档槽 + JSON 落盘」，分周目存档、全局存档、版本迁移属于
/// save_system 的后续范围（见 docs/dev/框架说明.md）。
///
/// 目录可注入：游戏里用 user://saves，自检里注入 res://.logs/... 这样的临时目录，
/// 避免测试依赖 user:// 的写权限。
///
/// **成员名保持 snake_case**：还没搬走的 GDScript（控制器／菜单／用例）里到处是
/// `store.slot_count` 这种取值，Godot 的 C# 脚本按成员名暴露属性。
/// </summary>
public partial class SaveStore : RefCounted
{
    public const string DEFAULT_DIR = "user://saves";
    public const int DEFAULT_SLOT_COUNT = 6;
    public const string FILE_PREFIX = "slot_";
    public const string FILE_EXT = ".json";

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的 `const`（与 `BuffService.POLARITY_*` 同一个坑）：
    // `test_save_store` 读 `DEFAULT_DIR`／`test_menu` 读 `DEFAULT_SLOT_COUNT`。

    public static string default_dir_name() => DEFAULT_DIR;
    public static int default_slot_count() => DEFAULT_SLOT_COUNT;

    [Export]
    public string base_dir { get; set; } = "";

    [Export]
    public int slot_count { get; set; }

    /// <summary>引擎那侧 `SaveStore.new()`（零参）走这一条：默认目录 + 默认槽位数。</summary>
    public SaveStore() : this(DEFAULT_DIR, DEFAULT_SLOT_COUNT)
    {
    }

    public SaveStore(string dir_path, int slots)
    {
        base_dir = dir_path;
        slot_count = Mathf.Max(1, slots);
    }

    /// <summary>
    /// GDScript 侧的构造入口。**GDScript 的 `new(参数…)` 对 C# 脚本是解析期报错**
    /// （见施工单坑②），所以带参数的构造一律走这个静态工厂，调用点写成
    /// `SaveStoreScript.create(dir, slots)`。
    /// </summary>
    public static SaveStore create(string dir_path, int slots) => new(dir_path, slots);

    /// <summary>
    /// 存档目录的默认值。
    ///
    /// 优先级：命令行调试参数 `-- --save-dir=&lt;路径&gt;` → 项目设置 `lunhui/save_dir` → user://saves。
    /// 调试参数是给受限环境（沙箱、只读用户目录）跑真实场景自检用的。
    /// </summary>
    public static string default_dir()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--save-dir="))
            {
                return argument.Substring("--save-dir=".Length);
            }
        }
        string configured = ProjectSettings.GetSetting("lunhui/save_dir", "").AsString();
        return configured.Length > 0 ? configured : DEFAULT_DIR;
    }

    public string slot_path(int slot) => $"{base_dir}/{FILE_PREFIX}{slot:D2}{FILE_EXT}";

    /// <summary>确保存档目录存在，返回是否可用。</summary>
    public bool ensure_dir()
    {
        Error error = DirAccess.MakeDirRecursiveAbsolute(base_dir);
        return error == Error.Ok || error == Error.AlreadyExists;
    }

    public bool slot_exists(int slot) => FileAccess.FileExists(slot_path(slot));

    /// <summary>枚举全部槽位。每项：{slot, exists, corrupt, state, saved_unix, label, error}。</summary>
    public Godot.Collections.Array list_slots(TableDb db)
    {
        var out_slots = new Godot.Collections.Array();
        for (int slot = 1; slot <= slot_count; slot++)
        {
            out_slots.Add(describe_slot(slot, db));
        }
        return out_slots;
    }

    public Godot.Collections.Dictionary describe_slot(int slot, TableDb db)
    {
        var entry = new Godot.Collections.Dictionary
        {
            { "slot", slot },
            { "exists", false },
            { "corrupt", false },
            { "state", default(Variant) },
            { "saved_unix", 0 },
            { "label", "空存档位" },
            { "error", "" },
        };
        if (!slot_exists(slot))
        {
            return entry;
        }
        entry["exists"] = true;
        Godot.Collections.Dictionary result = load_slot(slot, db);
        if (!result["ok"].AsBool())
        {
            entry["corrupt"] = true;
            entry["label"] = "存档损坏";
            entry["error"] = result["error"];
            return entry;
        }
        GodotObject state = result["state"].AsGodotObject();
        entry["state"] = result["state"];
        entry["saved_unix"] = state.Get("saved_unix");
        entry["label"] = db != null
            ? state.Call("short_label", db)
            : GameState.format_time(state.Get("saved_unix").AsInt32());
        return entry;
    }

    public bool has_any_save()
    {
        for (int slot = 1; slot <= slot_count; slot++)
        {
            if (slot_exists(slot))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>读一个槽。db 用于把旧版本存档迁移到当前格式（补初始装备）。返回 {ok, error, state}。</summary>
    public Godot.Collections.Dictionary load_slot(int slot, TableDb db)
    {
        string path = slot_path(slot);
        if (!FileAccess.FileExists(path))
        {
            return LoadResult(false, $"第 {slot} 格没有存档", null);
        }
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return LoadResult(false, $"存档读不出来：{path}", null);
        }
        string text = file.GetAsText();
        file.Close();
        var json = new Json();
        if (json.Parse(text) != Error.Ok)
        {
            // 用 Json 实例而不是 Json.ParseString：不要往引擎日志里喷错误，
            // 把原因交给菜单显示给玩家。
            return LoadResult(false, $"存档损坏：{json.GetErrorMessage()}", null);
        }
        Variant parsed = json.Data;
        if (!GameState.is_valid_dict(parsed))
        {
            return LoadResult(false, $"存档格式不对或已损坏：{path}", null);
        }
        // 版本闸门（2026-10-13 用户口径修正）：**旧档照读、只有"比当前新"的档才拒**。
        //
        // 起因：v16 那次升版一开始写成了"版本对不上就拒读"（照策划转述的"不做兼容迁移"），
        // 结果玩家手里 v15 的档一进游戏就报"读不了"——**看起来就像存档坏了**，用户当场反馈。
        // v15 → v16 的结构差异只有 `char_loadouts.basic` 这一项（**新增**的键，缺了取默认 `""` ✓），
        // 所以旧档本来就完全读得动；把"读不了"改成"读得动"只需要放开这一侧。
        // 反过来，**比当前版本新的档必须拒**——那是未来版本的格式，硬读只会静静丢东西。
        // 两个方向都**给玩家一句人话**（静默丢档/静默读坏是最糟的两种）。
        int file_version = parsed.AsGodotDictionary().ContainsKey("version")
            ? parsed.AsGodotDictionary()["version"].AsInt32()
            : 0;
        if (file_version > GameState.save_version())
        {
            return LoadResult(
                false,
                $"这个存档来自更新的版本（v{file_version}），当前版本（v{GameState.save_version()}）读不了",
                null);
        }

        GodotObject state = GameState.from_dict(parsed.AsGodotDictionary(), db);
        if (state == null)
        {
            return LoadResult(false, $"存档解析失败：{path}", null);
        }
        state.Set("slot", slot);
        return LoadResult(true, "", state);
    }

    /// <summary>写一个槽。返回 {ok, error}。</summary>
    public Godot.Collections.Dictionary save_slot(int slot, GodotObject state)
    {
        if (!ensure_dir())
        {
            return WriteResult(false, "存档目录建不出来，user:// 可能不可写");
        }
        using FileAccess file = FileAccess.Open(slot_path(slot), FileAccess.ModeFlags.Write);
        if (file == null)
        {
            return WriteResult(false, $"存档写不进去：{slot_path(slot)}");
        }
        state.Set("slot", slot);
        state.Call("touch_saved");
        file.StoreString(Json.Stringify(state.Call("to_dict"), "  "));
        file.Close();
        return WriteResult(true, "");
    }

    public bool delete_slot(int slot)
    {
        if (!slot_exists(slot))
        {
            return false;
        }
        return DirAccess.RemoveAbsolute(slot_path(slot)) == Error.Ok;
    }

    /// <summary>第一个空槽，没有则返回 -1。</summary>
    public int first_free_slot()
    {
        for (int slot = 1; slot <= slot_count; slot++)
        {
            if (!slot_exists(slot))
            {
                return slot;
            }
        }
        return -1;
    }

    /// <summary>最旧的**可用**存档槽（按 saved_unix），跳过损坏的，没有则返回 -1。</summary>
    public int oldest_slot()
    {
        int oldest = -1;
        int oldest_time = 0;
        foreach (Variant entry in list_slots(null))
        {
            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            if (!row["exists"].AsBool() || row["corrupt"].AsBool())
            {
                continue;
            }
            int saved_unix = row["saved_unix"].AsInt32();
            if (oldest < 0 || saved_unix < oldest_time)
            {
                oldest = row["slot"].AsInt32();
                oldest_time = saved_unix;
            }
        }
        return oldest;
    }

    /// <summary>第一个损坏的槽位（可以回收来用），没有则返回 -1。</summary>
    public int first_corrupt_slot()
    {
        foreach (Variant entry in list_slots(null))
        {
            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            if (row["exists"].AsBool() && row["corrupt"].AsBool())
            {
                return row["slot"].AsInt32();
            }
        }
        return -1;
    }

    /// <summary>
    /// 新游戏该写哪个槽：空槽 → 损坏槽（回收）→ 覆盖最旧的可用存档。
    /// 返回 {ok, slot, overwrite, error}。
    /// </summary>
    public Godot.Collections.Dictionary slot_for_new_game()
    {
        int free_slot = first_free_slot();
        if (free_slot > 0)
        {
            return Placement(true, free_slot, false, "");
        }
        int corrupt_slot = first_corrupt_slot();
        if (corrupt_slot > 0)
        {
            return Placement(true, corrupt_slot, true, "");
        }
        int victim = oldest_slot();
        if (victim > 0)
        {
            return Placement(true, victim, true, "");
        }
        // 兜底：目录里有东西但都读不出来
        for (int slot = 1; slot <= slot_count; slot++)
        {
            if (slot_exists(slot))
            {
                return Placement(true, slot, true, "");
            }
        }
        return Placement(false, 0, false, "没有可用的存档槽");
    }

    // ---------------------------------------------------------------- 结果字典
    // Godot 的 `Dictionary` 不收 C# 的 `null`：空状态要写 `default(Variant)`（Nil），
    // 否则 GDScript 那边 `result["state"] == null` 的判空会失灵。

    private static Godot.Collections.Dictionary LoadResult(bool ok, string error, GodotObject state)
    {
        return new Godot.Collections.Dictionary
        {
            { "ok", ok },
            { "error", error },
            { "state", state == null ? default(Variant) : Variant.CreateFrom(state) },
        };
    }

    private static Godot.Collections.Dictionary WriteResult(bool ok, string error)
    {
        return new Godot.Collections.Dictionary { { "ok", ok }, { "error", error } };
    }

    private static Godot.Collections.Dictionary Placement(bool ok, int slot, bool overwrite, string error)
    {
        return new Godot.Collections.Dictionary
        {
            { "ok", ok },
            { "slot", slot },
            { "overwrite", overwrite },
            { "error", error },
        };
    }
}
