using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// NPC 交往（设计 19 §二，0.25 交付）：好感度、赠礼、切磋、偷窃、兑换、专属任务。
/// C# 版，对位原 `src/core/npc_service.gd`（517 行）。为守 800 行硬上限拆三份 partial：
/// 本文件＝常量／查表／好感，`NpcServiceSocial.cs`＝赠礼·切磋·偷窃，`NpcServiceQuest.cs`＝兑换·专属任务。
///
/// 五张表各管一摊：`npc_def`（身份／位置／切磋队伍／基本信息）、`npc_favor`（好感规则）、
/// `npc_offer`（可兑换与可偷）、`npc_quest`（专属任务线）、`world_event`（大地图随机事件，
/// 在 `WorldEventService` 里，不在这张表）。
///
/// 三条设计纪律在代码里的落点：
///   · **喜好分两级**：`like_item_ids`（最爱）与 `like_categories`（类别）——
///     「送对具体物品是惊喜，送对类别只是礼貌」，所以两个加成不一样大；
///   · **偷窃走事件判定**（不是战斗），失败**扣好感**；`steal_difficulty=0` = **不可偷**
///     （给纯善意的 NPC 留的口子）；
///   · **任务条件复用既有语言**：`item:&lt;id&gt;:&lt;数&gt;`／`flag_xxx`／`event:&lt;check_id&gt;`／`kill_style:&lt;x&gt;`，
///     不新造一套——以后加 NPC 不用改代码。
///
/// 四个数按设计 0.28.0 的答复（Q63）：送中最爱 **＋8**、送对类别 **＋2**、送错 **−3**、
/// 偷窃失败 **−8**——比例是关键（「偷一下试试、失手只是回到原点」就不是最优解了，
/// **偷窃的紧张感来自失手真的疼**）。切磋输**不罚**（输了不扣好感，否则玩家不敢切磋）。
/// </summary>
public partial class NpcService : RefCounted
{
    /// <summary>送中最爱：在 `gift_favor` 之上再加这么多（「惊喜」，设计 0.28.0 定 ＋8）</summary>
    public const int LIKE_ITEM_BONUS = 8;

    /// <summary>送对类别：只加一点（「礼貌」）</summary>
    public const int LIKE_CATEGORY_BONUS = 2;

    /// <summary>送了对方讨厌的东西（设计 0.28.0 定 −3——比「偷窃失手」轻，但仍要疼）</summary>
    public const int DISLIKE_PENALTY = -3;

    /// <summary>
    /// 位点命名的两个前缀（07 §九 第 12 条／§十二）：`npc_` 开头的才算 NPC 位点，
    /// 其中 `npc_slot_` 是**占位命名**（按行序绑人），`npc_&lt;npc_id&gt;` 是**按 id 绑**。
    /// </summary>
    public const string SLOT_PREFIX = "npc_";

    public const string SLOT_ID_PREFIX = "npc_slot_";

    /// <summary>偷窃失败被当场发现（设计 0.28.0 定 −8：**失手真的疼**，这才是偷窃的紧张感来源）</summary>
    public const int STEAL_FAIL_PENALTY = -8;

    /// <summary>
    /// 好感的地板。**好感可以是负的**：`npc_favor.initial_favor` 里陈氏就是 **−10**
    /// （设计 20 号 §九 也写着「陈氏好感由 −10 转正」），而设计 19 §2.2 只给了「初始值与上限」，
    /// 没有下限列——所以这里**按数据里最深的那一个**（−10）当地板，不自己发明更狠的数：
    /// 有地板才谈得上「偷窃失手真的疼」（Q63 的比例）又不会一路扣到救不回来。
    /// 设计若要更狠或更浅，给 `npc_favor` 加一列 `favor_min`，改这一处即可。
    /// </summary>
    public const int FAVOR_MIN = -10;

    /// <summary>
    /// 对话容器里的**特殊说话人**记号：`player` = 主角自己。
    ///
    /// 它**不是一个人**（没有 `npc_def`／`npc_favor` 行）：好感、赠礼、切磋、偷窃都不适用——
    /// 所以这种对话一律用 `NpcPanel.MODE_STORY` 渲染（只有台词与选项）。
    /// </summary>
    public const string PLAYER_ID = "player";

    /// <summary>
    /// 好感的三个档位（设计 20 §十 那张表，0.32.0）：**并肩 30／知心 60／生死 90**。
    /// 名字与数值**只在这一处**：`npc_favor` 没有档位列，20 §十 当初就是按固定三档写的。
    /// </summary>
    public static readonly Godot.Collections.Array FAVOR_TIERS = new()
    {
        new Godot.Collections.Dictionary { { "at", 30 }, { "name", "并肩" } },
        new Godot.Collections.Dictionary { { "at", 60 }, { "name", "知心" } },
        new Godot.Collections.Dictionary { { "at", 90 }, { "name", "生死" } },
    };

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的 `const`（施工单坑⑩）：`tests/test_dialogue.gd` 读 `FAVOR_MIN`／`PLAYER_ID`，
    // 改成同名小写静态方法；P5 清零后再连着调用方一起删。
    // （`public static` 不在 `test_handshake_code.gd` 的 C# 定义正则里，故不会触发「只被测试调用」那道门限。）

    public static int favor_min() => FAVOR_MIN;

    public static string player_id() => PLAYER_ID;

    /// <summary>档位刻度那一行的文案（面板与用例共用，避免两边各拼一份）。</summary>
    public static string favor_tier_text()
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant tier_variant in FAVOR_TIERS)
        {
            Godot.Collections.Dictionary tier = tier_variant.AsGodotDictionary();
            parts.Add($"{tier["at"].AsInt32()} {tier["name"].AsString()}");
        }
        return "刻度：" + string.Join("　", parts);
    }

    // ------------------------------------------------------------------ 静态查表

    public static Resource def_of(TableDb db, string npc_id) =>
        db != null ? db.get_row("npc_def", npc_id) : null;

    /// <summary>同伴的所在地：`recruit_def.join_scene`（小地图或大地图节点，与招募链同一处口径）。</summary>
    public static string join_scene_of(TableDb db, string char_id)
    {
        var row = db != null ? db.get_row("recruit_def", char_id) as RecruitDefRow : null;
        return row != null ? row.join_scene : "";
    }

    /// <summary>
    /// 「人」的统一取法：`npc_def` 里的人，**或者同伴**（`character_base`）。
    ///
    /// 同伴没有 `npc_def` 行（设计 20 §十 只写了「同伴与城镇 NPC 共用一套好感规则」，
    /// 没有再给四行），所以这里把同伴**按 `npc_def` 的形状合成一行**——
    /// 名字／称号／等级／介绍／所在地从 `character_base` ＋ `recruit_def` ＋ 存档里取，
    /// 面板与各处调用方就不用为「两种人」各写一套。
    ///
    /// 合成行**不进表**（每次调用新建一个 Resource），所以谁也改不动它。
    /// </summary>
    public static Resource person_of(TableDb db, GodotObject state, string person_id)
    {
        if (db == null || string.IsNullOrEmpty(person_id))
        {
            return null;
        }
        // 特殊记号 `player`：主角自己——按运行期第一位（玩家选的那个出身）解析
        if (person_id == PLAYER_ID)
        {
            if (state == null)
            {
                return null;
            }
            Godot.Collections.Array char_ids = state.Get("char_ids").AsGodotArray();
            if (char_ids.Count == 0)
            {
                return null;
            }
            return person_of(db, state, char_ids[0].AsString());
        }
        Resource def = def_of(db, person_id);
        if (def != null)
        {
            return def;
        }
        if (db.get_row("character_base", person_id) is not CharacterBaseRow char_row)
        {
            return null;
        }
        return new NpcDefRow
        {
            npc_id = person_id,
            name_cn = char_row.name_cn,
            title_cn = char_row.role_tag,
            level = state != null ? state.Call("level_of", person_id).AsInt32() : char_row.start_level,
            place_id = join_scene_of(db, person_id),
            faction = "companion",
            spar_team_id = "",
            greet_text_cn = "",
            info_text_cn = char_row.desc,
        };
    }

    public static Resource favor_row_of(TableDb db, string npc_id) =>
        db != null ? db.get_row("npc_favor", npc_id) : null;

    /// <summary>
    /// 某个地点上的人：`place_id` 命中这张图（或它的父区域节点）。
    /// 与 `RecruitService`／`StoryService` 同一套地点口径。
    /// </summary>
    public static Godot.Collections.Array npcs_at(TableDb db, string place_id)
    {
        var out_rows = new Godot.Collections.Array();
        if (db == null || string.IsNullOrEmpty(place_id))
        {
            return out_rows;
        }
        var local = db.get_row("map_local", place_id) as MapLocalRow;
        foreach (Variant entry in db.rows("npc_def"))
        {
            if (entry.AsGodotObject() is not NpcDefRow row)
            {
                continue;
            }
            string want = row.place_id;
            if (want == place_id)
            {
                out_rows.Add(row);
            }
            else if (local != null && want == local.parent_node)
            {
                out_rows.Add(row);
            }
        }
        return out_rows;
    }

    /// <summary>
    /// 位点名 → 人（07 §九 第 12 条／§十二）。**两种命名都认**：
    ///   · **按 id 绑**：位点名写成 `npc_&lt;npc_id&gt;`——**只认这个地点上的人**，认不出来就返回空串
    ///     （**绝不退回「按行序」**：那会把旁边的人认成他，比认不出来更糟）；
    ///   · **占位命名**：`npc_slot_0N` → 这个地点的第 N 个人（`npc_def` 行序，老口径）。
    /// </summary>
    public static string npc_for_slot(TableDb db, string slot_name, string place_id)
    {
        if (!slot_name.StartsWith(SLOT_PREFIX))
        {
            return "";
        }
        Godot.Collections.Array rows = npcs_at(db, place_id);
        if (!slot_name.StartsWith(SLOT_ID_PREFIX))
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is NpcDefRow row && row.npc_id == slot_name)
                {
                    return slot_name;
                }
            }
            return "";
        }
        string digits = "";
        foreach (char ch in slot_name)
        {
            if (ch >= '0' && ch <= '9')
            {
                digits += ch;
            }
        }
        if (digits.Length == 0)
        {
            return "";
        }
        int index = System.Math.Max(0, int.Parse(digits) - 1);
        if (index >= rows.Count)
        {
            return "";
        }
        return (rows[index].AsGodotObject() as NpcDefRow)?.npc_id ?? "";
    }

    // ------------------------------------------------------------------ 好感度

    /// <summary>当前好感（没记录就取表里的 `initial_favor`），并夹到 `[FAVOR_MIN, favor_max]`。</summary>
    public static int favor_of(TableDb db, GodotObject state, string npc_id)
    {
        Resource row = favor_row_of(db, npc_id);
        int cap = row is NpcFavorRow favor_row ? favor_row.favor_max : 100;
        int fallback = row is NpcFavorRow favor_row2 ? favor_row2.initial_favor : 0;
        Godot.Collections.Dictionary map = state.Get("npc_favor").AsGodotDictionary();
        int value = map.ContainsKey(npc_id) ? map[npc_id].AsInt32() : fallback;
        return Mathf.Clamp(value, FAVOR_MIN, System.Math.Max(1, cap));
    }

    /// <summary>改好感。返回 `{ok, before, after, delta, capped, error}`。</summary>
    public static Godot.Collections.Dictionary add_favor(
        TableDb db, GodotObject state, string npc_id, int delta)
    {
        if (state == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "before", 0 }, { "after", 0 }, { "delta", 0 },
                { "capped", false }, { "error", "没有会话状态" },
            };
        }
        if (favor_row_of(db, npc_id) is not NpcFavorRow row)
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[NpcService] npc_favor 里没有 {npc_id}");
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "before", 0 }, { "after", 0 }, { "delta", 0 },
                { "capped", false }, { "error", "这个人的交情没记（数据错，已记进日志）" },
            };
        }
        int before = favor_of(db, state, npc_id);
        int cap = System.Math.Max(1, row.favor_max);
        int after = Mathf.Clamp(before + delta, FAVOR_MIN, cap);
        Godot.Collections.Dictionary map = state.Get("npc_favor").AsGodotDictionary();
        map[npc_id] = after;
        state.Set("npc_favor", map);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "before", before }, { "after", after },
            { "delta", after - before }, { "capped", after != before + delta }, { "error", "" },
        };
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>分号分隔的 id 串里有没有这一个（空白段跳过，口径同 `split(";", false)`）。</summary>
    private static bool _in_list(string joined, string value)
    {
        if (string.IsNullOrEmpty(joined.Trim()) || value.Length == 0)
        {
            return false;
        }
        foreach (string part in joined.Split(';'))
        {
            if (part.Length == 0)
            {
                continue;
            }
            if (part.Trim() == value)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>给人看的物品名：先查装备表、再查道具表，都没有就回 id。</summary>
    private static string _item_name(TableDb db, string item_id)
    {
        if (db.get_row("equip_base", item_id) is EquipBaseRow equip)
        {
            return equip.name_cn;
        }
        return db.get_row("item_base", item_id) is ItemBaseRow item ? item.name_cn : item_id;
    }

    /// <summary>给人看的名字：NPC 优先，其次同伴（`character_base`），都没有就回 id。</summary>
    private static string _display_name(TableDb db, string npc_id)
    {
        if (db != null && db.get_row("npc_def", npc_id) is NpcDefRow def)
        {
            return def.name_cn;
        }
        return db != null && db.get_row("character_base", npc_id) is CharacterBaseRow char_row
            ? char_row.name_cn
            : npc_id;
    }

    /// <summary>`%+d` 的同款写法：正数与 0 都带 `+`（`0 → "+0"`，与 Godot 的 `%+d` 一致）。</summary>
    private static string _signed(int value) => value >= 0 ? $"+{value}" : $"{value}";
}
