using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 武学「来源 → 学会」的解析（C# 版，对位原 `src/core/skill_grant.gd`）。
///
/// 已实现五类来源（都按 `skill_base.source_type` + `source_id` 配表驱动）：
///   `drop`   击败指名的敌人（战斗结算调用）
///   `hidden` 触发指名的隐藏点位（小地图触发点调用）
///   `item`   研读指名的秘籍（背包里研读调用；一次性，学会就消耗一本）
///   `story`  剧情节点（`StoryService` 在「条件旗标点亮 + 到了指定地点」时发放）
///   `origin` 出身本命机遇（与 `story` 同一条通道，区别只在条件：一个看旗标、一个看"主角是不是这个出身"）
/// 未实现：`npc`（门派传授，要「谁在哪儿教哪一招」的内容）；`shop` 自 2026-10-04（Q3）起没有数据再用
/// ——那 5 部武学改成 `item` 秘籍了，留着这个取值是防将来又落回来。
///
/// 口径：学到的武学进**每个队员**的已学列表（队伍共享，图鉴按去重计数），
/// 单人专属来源（门派指定传人、秘籍指定某人）未做。
///
/// **跨语言面**：`state`（`GameState`）还是 GDScript，走 `Call`／`Get`；判定「够不够门槛」的
/// `SkillLoadout` 与背包 `Inventory` 都已是 C#，直接 `create(...)`／`As&lt;Inventory&gt;()` 调
/// （P3 之前那两条 `GD.Load<GDScript>` 通道都已拆）。
/// </summary>
public partial class SkillGrant : RefCounted
{
    /// <summary>
    /// 已接入的来源类型（其余类型即使 `source_id` 撞上了也不发放）。
    /// **普通 `string[]`，不是 Godot 集合**：静态字段里放 Godot 集合会让终结器在 native 拆完之后才跑
    /// （施工单坑⑧，`SEHException`）。
    /// </summary>
    private static readonly string[] IMPLEMENTED_SOURCES = { "drop", "hidden", "item", "story", "origin" };

    // ---------------------------------------------------------------- 过渡期取值口
    // `tests/TestHandshakeFlags.cs` 拿它当「来源已实现」的唯一真相。早年 GDScript 读不到 C# 的
    // 静态字段（施工单坑⑩），所以留了这个同名小写静态方法，别改回属性。

    /// <summary>已实现的来源名单（返回副本，免得调用方把这份唯一真相改掉）。</summary>
    public static string[] implemented_sources() =>
        (string[])IMPLEMENTED_SOURCES.Clone();

    /// <summary>某个来源带哪些武学（source_type + source_id 命中）。</summary>
    public static string[] skills_from_source(TableDb db, string source_type, string source_id)
    {
        var out_skills = new System.Collections.Generic.List<string>();
        if (db == null || !_implements(source_type) || string.IsNullOrEmpty(source_id))
        {
            return out_skills.ToArray();
        }
        foreach (Variant entry in db.rows_where("skill_base", "source_id", source_id))
        {
            if (entry.AsGodotObject() is SkillBaseRow row && row.source_type == source_type)
            {
                out_skills.Add(row.skill_id);
            }
        }
        string[] sorted = out_skills.ToArray();
        // GDScript 的 `PackedStringArray.sort()` 按码点排；C# 默认是文化相关比较，会排出不同的次序
        System.Array.Sort(sorted, System.StringComparer.Ordinal);
        return sorted;
    }

    /// <summary>某个敌人身上带哪些武学（source_type=drop）。</summary>
    public static string[] skills_from_enemy(TableDb db, string enemy_id) =>
        skills_from_source(db, "drop", enemy_id);

    /// <summary>按来源授予（触发点／秘籍与战斗结算共用）。返回结构同 `grant_from_defeated`。</summary>
    public static Godot.Collections.Array grant_from_source(
        TableDb db, GodotObject state, string source_type, string source_id) =>
        _grant_skills(db, state, skills_from_source(db, source_type, source_id));

    /// <summary>
    /// 战斗胜利结算：按被击败的敌人领悟武学（同一个 skill_id 只报一次）。
    /// 返回 `[{skill_id, name, kind, star, learned, blocked, already}]`。
    /// </summary>
    public static Godot.Collections.Array grant_from_defeated(
        TableDb db, GodotObject state, string[] enemy_ids)
    {
        var out_entries = new Godot.Collections.Array();
        if (db == null || state == null)
        {
            return out_entries;
        }
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (string enemy_id in enemy_ids)
        {
            foreach (Variant entry_variant in grant_from_source(db, state, "drop", enemy_id))
            {
                var entry = entry_variant.AsGodotDictionary();
                string skill_id = entry["skill_id"].AsString();
                if (!seen.Add(skill_id))
                {
                    continue;
                }
                out_entries.Add(entry);
            }
        }
        return out_entries;
    }

    private static Godot.Collections.Array _grant_skills(TableDb db, GodotObject state, string[] skill_ids)
    {
        var out_entries = new Godot.Collections.Array();
        if (db == null || state == null)
        {
            return out_entries;
        }
        foreach (string skill_id in skill_ids)
        {
            Godot.Collections.Dictionary entry = _grant_one(db, state, skill_id);
            if (entry.Count > 0)
            {
                out_entries.Add(entry);
            }
        }
        return out_entries;
    }

    /// <summary>单个武学：全队逐人过修习门槛，够门槛的才学会。</summary>
    private static Godot.Collections.Dictionary _grant_one(TableDb db, GodotObject state, string skill_id)
    {
        var row = db.get_row("skill_base", skill_id) as SkillBaseRow;
        if (row == null)
        {
            GD.PushError($"[SkillGrant] skill_base 缺少 {skill_id}");
            return new Godot.Collections.Dictionary();
        }
        var learned = new System.Collections.Generic.List<string>();
        var blocked = new Godot.Collections.Array();
        int already = 0;
        foreach (string char_id in state.Get("char_ids").AsStringArray())
        {
            SkillLoadout loadout = _new_loadout(db, state, char_id);
            Godot.Collections.Dictionary result = loadout.learn(skill_id);
            if (result["ok"].AsBool() && result["new"].AsBool())
            {
                learned.Add(char_id);
            }
            else if (!result["ok"].AsBool())
            {
                blocked.Add(new Godot.Collections.Dictionary
                {
                    { "char_id", char_id },
                    { "name", state.Call("char_name", db, char_id) },
                    { "reason", result["error"] },
                });
            }
            else
            {
                already += 1;
            }
        }
        // 全队早就学过：不必每场都播报一次
        if (learned.Count == 0 && blocked.Count == 0)
        {
            return new Godot.Collections.Dictionary();
        }
        return new Godot.Collections.Dictionary
        {
            { "skill_id", skill_id },
            { "name", row.name_cn },
            { "kind", row.skill_kind },
            { "star", row.star },
            { "learned", learned.ToArray() },
            { "blocked", blocked },
            { "already", already },
        };
    }

    /// <summary>结算面板上显示的一行文案。</summary>
    public static string describe(Godot.Collections.Dictionary entry)
    {
        string name = _text(entry, "name");
        var parts = new System.Collections.Generic.List<string>();
        if (_strings(entry, "learned").Length > 0)
        {
            parts.Add($"领悟「{name}」");
        }
        foreach (Variant row_variant in _array(entry, "blocked"))
        {
            var row = row_variant.AsGodotDictionary();
            parts.Add($"{row["name"].AsString()}没能领悟「{name}」：{row["reason"].AsString()}");
        }
        return string.Join("；", parts);
    }

    /// <summary>
    /// 结算面板用的紧凑汇总：学到的合成一行、没学到的合成一行。
    /// 一场战斗可能一次播报好几部武学，一行一条会把结算框顶出窗口（界面上直接看不全）。
    /// </summary>
    public static string[] summarize(Godot.Collections.Array entries)
    {
        var learned = new System.Collections.Generic.List<string>();
        var blocked = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in entries)
        {
            var entry = entry_variant.AsGodotDictionary();
            string name = _text(entry, "name");
            if (_strings(entry, "learned").Length > 0)
            {
                learned.Add(name);
            }
            foreach (Variant row_variant in _array(entry, "blocked"))
            {
                var row = row_variant.AsGodotDictionary();
                blocked.Add($"{name}（{row["reason"].AsString()}）");
            }
        }
        var out_lines = new System.Collections.Generic.List<string>();
        if (learned.Count > 0)
        {
            out_lines.Add("领悟：" + string.Join("、", learned));
        }
        if (blocked.Count > 0)
        {
            out_lines.Add("未习得：" + string.Join("；", blocked));
        }
        return out_lines.ToArray();
    }

    // ------------------------------------------------------------------ 秘籍（item 来源）

    /// <summary>这部秘籍现在能不能研读：`{ok, error, skills}`。</summary>
    public static Godot.Collections.Dictionary can_study(TableDb db, GodotObject state, string item_id)
    {
        GodotObject inventory = state == null ? null : state.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return _can_study_result(false, "没有会话状态", System.Array.Empty<string>());
        }
        if (!inventory.Call("has", item_id, 1).AsBool())
        {
            return _can_study_result(false, "背包里没有这本秘籍", System.Array.Empty<string>());
        }
        string[] skills = skills_from_source(db, "item", item_id);
        if (skills.Length == 0)
        {
            // 例：醉里乾坤残卷（钥匙道具，要「集齐」才解锁，集齐机制没做）
            return _can_study_result(
                false, "这本秘籍现在没有可学的武学（来源没接或要集齐多本，集齐机制未做）", skills);
        }
        return _can_study_result(true, "", skills);
    }

    /// <summary>
    /// 研读一本秘籍：门槛不够或已经学过就**不消耗**（别浪费稀有残页）。
    /// 返回 `{ok, error, entries, learned, consumed}`。
    /// </summary>
    public static Godot.Collections.Dictionary study(TableDb db, GodotObject state, string item_id)
    {
        Godot.Collections.Dictionary check = can_study(db, state, item_id);
        if (!check["ok"].AsBool())
        {
            return _study_result(false, check["error"].AsString(), new Godot.Collections.Array(),
                System.Array.Empty<string>(), false);
        }
        Godot.Collections.Array entries = _grant_skills(db, state, check["skills"].AsStringArray());
        var learned = new System.Collections.Generic.List<string>();
        var blocked = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in entries)
        {
            var entry = entry_variant.AsGodotDictionary();
            foreach (string char_id in _strings(entry, "learned"))
            {
                if (!learned.Contains(char_id))
                {
                    learned.Add(char_id);
                }
            }
            foreach (Variant row_variant in _array(entry, "blocked"))
            {
                var row = row_variant.AsGodotDictionary();
                string text = $"{entry["name"].AsString()}（{row["reason"].AsString()}）";
                if (!blocked.Contains(text))
                {
                    blocked.Add(text);
                }
            }
        }
        if (learned.Count == 0)
        {
            string reason = "这几部武学都已经学过了";
            if (blocked.Count > 0)
            {
                reason = "修习门槛不足：" + string.Join("；", blocked);
            }
            return _study_result(false, reason, entries, System.Array.Empty<string>(), false);
        }
        // 学会才消耗：秘籍是「一次性灌注」
        state.Get("inventory").As<Inventory>().consume(item_id, 1);
        string error = blocked.Count == 0 ? "" : "部分武学没学会：" + string.Join("；", blocked);
        return _study_result(true, error, entries, learned.ToArray(), true);
    }

    /// <summary>
    /// 界面用：这本秘籍能不能研读（含修习门槛），不能就给一条能读的原因。
    /// `can_study` 只看表，这里再看「队伍里有没有人够门槛」，这样按钮能正确置灰。
    /// </summary>
    public static Godot.Collections.Dictionary study_gate(TableDb db, GodotObject state, string item_id)
    {
        Godot.Collections.Dictionary check = can_study(db, state, item_id);
        if (!check["ok"].AsBool())
        {
            return check;
        }
        string[] skills = check["skills"].AsStringArray();
        bool any_reachable = false;
        var reasons = new System.Collections.Generic.List<string>();
        foreach (string skill_id in skills)
        {
            foreach (string char_id in state.Get("char_ids").AsStringArray())
            {
                SkillLoadout loadout = _new_loadout(db, state, char_id);
                Godot.Collections.Dictionary requirement = loadout.requirement_of(skill_id);
                if (requirement["ok"].AsBool())
                {
                    any_reachable = true;
                    break;
                }
                string reason = requirement["reason"].AsString();
                if (reason.Length > 0 && !reasons.Contains(reason))
                {
                    reasons.Add(reason);
                }
            }
            if (any_reachable)
            {
                break;
            }
        }
        if (!any_reachable)
        {
            return _can_study_result(false, "修习门槛不足：" + string.Join("、", reasons), skills);
        }
        return check;
    }

    // ------------------------------------------------------------------ 内部工具

    private static bool _implements(string source_type)
    {
        foreach (string known in IMPLEMENTED_SOURCES)
        {
            if (known == source_type)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>建一个 `SkillLoadout` 实例（坑①：带参数构造走静态工厂）。</summary>
    private static SkillLoadout _new_loadout(TableDb db, GodotObject state, string char_id) =>
        SkillLoadout.create(db, state, char_id);

    private static Godot.Collections.Dictionary _can_study_result(bool ok, string error, string[] skills) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "skills", skills },
        };

    private static Godot.Collections.Dictionary _study_result(
        bool ok, string error, Godot.Collections.Array entries, string[] learned, bool consumed) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "entries", entries },
            { "learned", learned },
            { "consumed", consumed },
        };

    /// <summary>
    /// 对位 GDScript 的 `PackedStringArray(dict.get(key, PackedStringArray()))`：
    /// 缺键给空表；值可能是 `string[]`（我们自己塞的）也可能是 `Array`（调用方拼的）。
    /// </summary>
    private static string[] _strings(Godot.Collections.Dictionary data, string key)
    {
        if (!data.ContainsKey(key))
        {
            return System.Array.Empty<string>();
        }
        Variant raw = data[key];
        if (raw.VariantType == Variant.Type.PackedStringArray)
        {
            return raw.AsStringArray();
        }
        if (raw.VariantType == Variant.Type.Array)
        {
            var out_values = new System.Collections.Generic.List<string>();
            foreach (Variant item in raw.AsGodotArray())
            {
                out_values.Add(item.AsString());
            }
            return out_values.ToArray();
        }
        return System.Array.Empty<string>();
    }

    /// <summary>对位 GDScript 的 `Array(dict.get(key, []))`。</summary>
    private static Godot.Collections.Array _array(Godot.Collections.Dictionary data, string key) =>
        data.ContainsKey(key) ? data[key].AsGodotArray() : new Godot.Collections.Array();

    /// <summary>对位 GDScript 的 `str(dict.get(key, ""))`。</summary>
    private static string _text(Godot.Collections.Dictionary data, string key) =>
        data.ContainsKey(key) ? data[key].AsString() : "";
}
