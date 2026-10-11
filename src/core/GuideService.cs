using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 开局引导（C# 版，对位原 `src/core/guide_service.gd`；设计 09 §3.1 → 18 §3.2）。
/// HUD 上常驻一行「当前目标」，条件满足就自动推进。
///
/// 数据源 `guide_step`（6 步）。每行的 `condition` 就是**「这一步成为当前目标」的判据**：
/// 取值是剧情旗标（`GameState.flags`），第一步用哨兵 `start`（开局即满足，表里没有对应的旗标）。
///
/// 口径：**当前步 = 条件已满足的最后一行**——旗标一个个点亮，目标就一行行往前走。
/// 条件本身由谁点亮是各系统的事（悬赏板发 `flag_board_read`、招募发 `flag_ch_ci_joined`…），
/// 这里只读旗标，不自己发明触发点。
///
/// **跨语言面**：`state` 还是 GDScript（`game_state.gd`），所以对它的字段全程 `Get`／`Call`；
/// `inventory` 已经搬到 C#（`Inventory.cs`），但取到它仍走 `state.Get("inventory")` 再 `Call`
/// （与 `GameState` 同一层语言边界，等 `game_state` 搬完再收）。
/// </summary>
public partial class GuideService : RefCounted
{
    /// <summary>第一步的哨兵：设计表里写的是「开局」，没有旗标可查。</summary>
    public const string START_CONDITION = "start";

    /// <summary>条件语言的前缀（设计 21 §9.3）：`origin:&lt;char_id&gt;` = 主角正是这个出身。前缀只在这里写一次。</summary>
    public const string ORIGIN_PREFIX = "origin:";

    /// <summary>`item:&lt;id&gt;`／`item:&lt;id&gt;:&lt;数量&gt;` = 背包里有这件东西（数量省略＝1），与 `npc_quest.requirement` 同一种写法。</summary>
    public const string ITEM_PREFIX = "item:";

    /// <summary>备货旗标（设计 18 §3.2 的 `gs_04`）：判定口径写在 `condition_spec` 里，由 `refresh_derived_flags()` 算出来。</summary>
    public const string SUPPLIES_FLAG = "flag_supplies_ready";

    /// <summary>备货要求的等级（设计 18 的 `condition_spec` 写明 5 级）。</summary>
    public const int SUPPLIES_LEVEL = 5;

    /// <summary>进寨旗标（设计 18 的断点 2）：`flag_arrived_heifengzhai` 原来没有来源。</summary>
    public const string ARRIVED_HEIFENGZHAI_FLAG = "flag_arrived_heifengzhai";

    /// <summary>「前寨」那张图（`map_local.scene_id`）——**别在别处再写一遍这个字符串**。</summary>
    public const string HEIFENGZHAI_SCENE = "scene_heifengzhai";

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的 `const`（施工单坑⑩）：`tests/test_guide.gd` 读 `START_CONDITION`、
    // `tests/test_story.gd` 读两个旗标名，改成同名小写静态方法；P5 清零后再连着调用方一起删。

    public static string start_condition() => START_CONDITION;
    public static string origin_prefix() => ORIGIN_PREFIX;
    public static string item_prefix() => ITEM_PREFIX;
    public static string supplies_flag() => SUPPLIES_FLAG;
    public static int supplies_level() => SUPPLIES_LEVEL;
    public static string arrived_heifengzhai_flag() => ARRIVED_HEIFENGZHAI_FLAG;
    public static string heifengzhai_scene() => HEIFENGZHAI_SCENE;

    /// <summary>按 `sort_order` 排好的引导步骤（表里的顺序就是玩家看到的顺序）。</summary>
    public static Godot.Collections.Array steps(TableDb db)
    {
        if (db == null)
        {
            return new Godot.Collections.Array();
        }
        var sorted = new System.Collections.Generic.List<GuideStepRow>();
        foreach (Variant entry in db.rows("guide_step"))
        {
            if (entry.AsGodotObject() is GuideStepRow row)
            {
                sorted.Add(row);
            }
        }
        sorted.Sort((a, b) => a.sort_order.CompareTo(b.sort_order));
        var out_rows = new Godot.Collections.Array();
        foreach (GuideStepRow row in sorted)
        {
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>
    /// 一步的判据过没过。条件可以用 `&`（**全部满足**）、`|`（**任一满足**）、`!`（取反，写在整条前面）
    /// 串起来，另有 `origin:`／`item:` 两种写法；其余按剧情旗标查。
    ///
    /// **判错时宁可"不满足"**：条件写坏了，玩家看不到那段内容（有用例兜着），
    /// 而不是把「没账册也能做终局抉择」这种漏洞放进去。
    /// </summary>
    public static bool condition_met(GodotObject state, string condition)
    {
        string text = condition == null ? "" : condition.StripEdges();
        if (text == START_CONDITION)
        {
            return true;
        }
        if (state == null)
        {
            return false;
        }
        // 空串没人用，`&`／`|` 两侧也不许有空段（`allow_empty=false` 与 GDScript 的 split 一致）。
        if (text.Contains('&'))
        {
            foreach (string part in text.Split('&', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.StripEdges();
                if (p.Length == 0 || !condition_met(state, p))
                {
                    return false;
                }
            }
            return true;
        }
        // 拆分顺序是 `&` 先、`|` 后，所以 `a|b&c` = (a 或 b) 且 c。
        if (text.Contains('|'))
        {
            foreach (string part in text.Split('|', System.StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.StripEdges();
                if (p.Length == 0)
                {
                    return false;
                }
                if (condition_met(state, p))
                {
                    return true;
                }
            }
            return false;
        }
        if (text.StartsWith("!", System.StringComparison.Ordinal))
        {
            string inner = text.Substring(1).StripEdges();
            if (inner.Length == 0)
            {
                return false;
            }
            return !condition_met(state, inner);
        }
        // 出身是**创建时就定死**的事实，不是一个"发生过的事件"：能置旗的两个通道都会污染计数
        // （`event_check` 进事件判定＋线索本＋副本完成度、`hidden_trigger` 进隐藏内容），
        // 所以走条件语言。主角就是开局第一个人（`char_ids[0]`）。
        if (text.StartsWith(ORIGIN_PREFIX, System.StringComparison.Ordinal))
        {
            string origin_id = text.Substring(ORIGIN_PREFIX.Length);
            string[] char_ids = state.Get("char_ids").AsStringArray();
            return origin_id.Length > 0 && char_ids.Length > 0 && char_ids[0] == origin_id;
        }
        if (text.StartsWith(ITEM_PREFIX, System.StringComparison.Ordinal))
        {
            GodotObject inventory = state.Get("inventory").AsGodotObject();
            if (inventory == null)
            {
                return false;
            }
            string[] parts = text.Substring(ITEM_PREFIX.Length).Split(':');
            if (parts.Length == 0 || parts[0].StripEdges().Length == 0)
            {
                return false;
            }
            int need = 1;
            if (parts.Length > 1)
            {
                need = System.Math.Max(1, _to_int(parts[1]));
            }
            return inventory.Call("count", parts[0].StripEdges()).AsInt32() >= need;
        }
        return state.Call("has_flag", text).AsBool();
    }

    /// <summary>
    /// 把「判定口径在表里、但没人去置」的旗标算出来（设计 18 的断点 1）。目前只有 `gs_04` 的
    /// `flag_supplies_ready`。返回这次真正点亮的旗标名，调用方可以据此播报。
    /// </summary>
    public static string[] refresh_derived_flags(TableDb db, GodotObject state)
    {
        var set_now = new System.Collections.Generic.List<string>();
        if (db == null || state == null)
        {
            return set_now.ToArray();
        }
        if (!state.Call("has_flag", SUPPLIES_FLAG).AsBool() && supplies_ready(db, state))
        {
            state.Call("set_flag", SUPPLIES_FLAG, true);
            set_now.Add(SUPPLIES_FLAG);
        }
        return set_now.ToArray();
    }

    /// <summary>
    /// 备货够了没有：**队里至少一人到 5 级**（木桩练级是练"当前这个人"的，要求全员到 5 太苛刻）
    /// 且背包里有消耗品。
    ///
    /// 为什么是"任意消耗品"：设计 18 写的是「回血道具」，可 `item_base` **没有一列说这件东西回什么**
    /// （只有 `use_context`），开发侧不解析 `desc` 里的散文取数。这条宽口径已记 `待策划确认.md` Q59。
    /// </summary>
    public static bool supplies_ready(TableDb db, GodotObject state)
    {
        if (state == null || db == null)
        {
            return false;
        }
        bool level_ok = false;
        foreach (string char_id in state.Get("char_ids").AsStringArray())
        {
            if (state.Call("level_of", char_id).AsInt32() >= SUPPLIES_LEVEL)
            {
                level_ok = true;
                break;
            }
        }
        if (!level_ok)
        {
            return false;
        }
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return false;
        }
        foreach (string item_id in inventory.Call("item_ids").AsStringArray())
        {
            var row = db.get_row("item_base", item_id) as ItemBaseRow;
            if (row == null)
            {
                continue;
            }
            if (row.item_type == "consumable" && row.use_context.StripEdges().Length > 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>某张图的「入口房间」（`dungeon_room.room_type=entrance`）。</summary>
    public static string entrance_room_of(TableDb db, string scene_id)
    {
        if (db == null || string.IsNullOrEmpty(scene_id))
        {
            return "";
        }
        foreach (Variant entry in db.rows("dungeon_room"))
        {
            var row = entry.AsGodotObject() as DungeonRoomRow;
            if (row != null && row.scene_id == scene_id && row.room_type == "entrance")
            {
                return row.room_id;
            }
        }
        return "";
    }

    /// <summary>
    /// 玩家走进某个房间时调一次：点亮**只能在场景里判**的那几条旗标。
    /// 目前只有一条——「首次进入黑风寨前寨」（设计 18 的断点 2）。判据是**入口房间**
    /// （`room_type=entrance`）而不是"进了这张图"：从后山密道直接下到三层地牢不算"进寨"。
    /// </summary>
    public static string[] note_room_entered(TableDb db, GodotObject state, string scene_id, string room_id)
    {
        var set_now = new System.Collections.Generic.List<string>();
        if (db == null || state == null || string.IsNullOrEmpty(room_id))
        {
            return set_now.ToArray();
        }
        if (state.Call("has_flag", ARRIVED_HEIFENGZHAI_FLAG).AsBool())
        {
            return set_now.ToArray();
        }
        if (scene_id != HEIFENGZHAI_SCENE)
        {
            return set_now.ToArray();
        }
        if (room_id != entrance_room_of(db, scene_id))
        {
            return set_now.ToArray();
        }
        state.Call("set_flag", ARRIVED_HEIFENGZHAI_FLAG, true);
        set_now.Add(ARRIVED_HEIFENGZHAI_FLAG);
        return set_now.ToArray();
    }

    /// <summary>当前目标行；表为空（或没配 `start`）时返回 `{}`。</summary>
    public static Godot.Collections.Dictionary current(TableDb db, GodotObject state)
    {
        var result = new Godot.Collections.Dictionary();
        foreach (Variant entry in steps(db))
        {
            var row = (GuideStepRow)entry.AsGodotObject();
            if (condition_met(state, row.condition))
            {
                result = new Godot.Collections.Dictionary
                {
                    { "step_id", row.step_id },
                    { "index", row.sort_order },
                    { "text_cn", row.text_cn },
                    { "condition", row.condition },
                };
            }
        }
        return result;
    }

    /// <summary>HUD 那一行的文案：`当前目标：…（2/6）`；没配引导表时返回空串（HUD 不占位）。</summary>
    public static string hud_text(TableDb db, GodotObject state)
    {
        Godot.Collections.Dictionary row = current(db, state);
        if (row.Count == 0)
        {
            return "";
        }
        return $"当前目标：{row["text_cn"].AsString()}（{row["index"].AsInt32()}/{steps(db).Count}）";
    }

    /// <summary>
    /// 对位 GDScript 的 `int("…")`：认前导数字（可带正负号），认不出来给 0。
    /// 不用 `string.ToInt()` 那种扩展，是为了让「空串＝0、`12abc`＝12」这条口径写在明处。
    /// </summary>
    private static int _to_int(string text)
    {
        string trimmed = text == null ? "" : text.StripEdges();
        int index = 0;
        if (index < trimmed.Length && (trimmed[index] == '-' || trimmed[index] == '+'))
        {
            index++;
        }
        int start = index;
        while (index < trimmed.Length && char.IsAsciiDigit(trimmed[index]))
        {
            index++;
        }
        if (index == start)
        {
            return 0;
        }
        return int.TryParse(trimmed.Substring(0, index), out int value) ? value : 0;
    }
}
