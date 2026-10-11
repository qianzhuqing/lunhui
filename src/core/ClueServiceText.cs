using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `ClueService` 的「查表拼文案」那一半（对位原 `src/core/clue_service.gd` 的同名段落）：
/// 条件与奖励说成人话、碎句找得到它指向的那件事、`describe()` 拼出面板用的一行。
/// 玩家可见文案**不许出现表内 id**（决策 329／330）——查不到中文名时宁可说「线索」也不回 id，
/// 唯一例外是 `node_name()`／`_char_name()`／`_npc_name()` 这类「本来就没有中文名」的兜底。
/// </summary>
public partial class ClueService
{
    // ---------------------------------------------------------------- 条件与奖励

    /// <summary>
    /// `item:item_iron:10` → 「需 铁矿石 ×10」；`flag_xxx` 这类旗标**不翻译**
    /// （表里没有中文名，翻出来就会把表内 id 摆到玩家眼前）。
    /// </summary>
    private string _requirement_text(string requirement)
    {
        string text = requirement.Trim();
        if (text.Length == 0)
        {
            return "随时可接";
        }
        string[] parts = text.Split(':');
        switch (parts[0])
        {
            case "item":
                return parts.Length >= 3 ? $"需 {_item_name(parts[1])} ×{parts[2]}" : "需带一件东西去";
            case "event":
                return "需先了结一桩事";
            case "kill_style":
                return "需以特定手法击杀";
            default:
                return "需先办妥前事";
        }
    }

    /// <summary>委托奖励：好感 ＋ 东西的中文名。</summary>
    private string _reward_text(NpcQuestRow row)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (row.reward_favor != 0)
        {
            parts.Add($"好感 +{row.reward_favor}");
        }
        foreach (string raw in row.reward_item_ids.Split(';'))
        {
            if (raw.Length == 0)
            {
                continue;   // 口径同 `split(";", false)`：空段跳过
            }
            string name = _item_name(raw.Trim());
            if (name.Length > 0)
            {
                parts.Add(name);
            }
        }
        return string.Join("、", parts);
    }

    // ---------------------------------------------------------------- 线索指向

    /// <summary>`clue_source` 里逐字对得上的那一条 → `{kind, id, scene_id, name, done}`；对不上返回空字典。</summary>
    private Godot.Collections.Dictionary _clue_target(string text)
    {
        string needle = text.Trim();
        if (needle.Length == 0)
        {
            return new Godot.Collections.Dictionary();
        }
        foreach (Variant row_variant in db.rows("hidden_trigger"))
        {
            if (row_variant.AsGodotObject() is HiddenTriggerRow row
                && System.Array.IndexOf(row.clues(), needle) >= 0)
            {
                return _trigger_target(row.trigger_id);
            }
        }
        foreach (Variant row_variant in db.rows("event_check"))
        {
            if (row_variant.AsGodotObject() is not EventCheckRow row
                || System.Array.IndexOf(row.clues(), needle) < 0)
            {
                continue;
            }
            string check_id = row.check_id;
            return new Godot.Collections.Dictionary
            {
                { "kind", "event" },
                { "id", check_id },
                { "name", row.note },
                { "done", state != null && state.Call("event_check_result", check_id).AsString() == "done" },
            };
        }
        return new Godot.Collections.Dictionary();
    }

    /// <summary>隐藏内容的「验没验」看存档的 `dungeon_records.triggers`（与 `clues_for_scene` 同一口径）。</summary>
    private Godot.Collections.Dictionary _trigger_target(string trigger_id)
    {
        var row = db.get_row("hidden_trigger", trigger_id) as HiddenTriggerRow;
        if (row == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "kind", "hidden" }, { "id", trigger_id }, { "name", "线索" }, { "done", false },
            };
        }
        Godot.Collections.Array done_triggers = new();
        if (state != null)
        {
            Godot.Collections.Dictionary record = state.Call("dungeon_record", row.scene_id).AsGodotDictionary();
            if (record.ContainsKey("triggers"))
            {
                done_triggers = record["triggers"].AsGodotArray();
            }
        }
        return new Godot.Collections.Dictionary
        {
            { "kind", "hidden" }, { "id", trigger_id }, { "name", row.name_cn },
            { "done", done_triggers.Contains(trigger_id) },
        };
    }

    private static string _verify_word(Godot.Collections.Dictionary target)
    {
        if (target.Count == 0)
        {
            return "已听到";
        }
        return _TargetDone(target) ? "已验" : "未验";
    }

    /// <summary>
    /// 观察点是按 room 还是按 region 挂的——两种都要能说清「在哪儿看到的」。
    ///
    /// **回归**（2026-10-11 搬 C# 时发现）：原 GDScript 取的是 `room.name_cn`，而 `dungeon_room`
    /// 的列叫 `room_name`——那一格一直取不到（运行期报 Invalid get index，房间名印成空／`&lt;null&gt;`）。
    /// 这里按真列名取，并把这条钉进 `tests/test_clue.gd`。
    /// </summary>
    private string _point_place(FlavorPointRow row)
    {
        if (row.region_id.Length > 0)
        {
            return node_name(row.region_id);
        }
        string scene_name = _scene_name(row.scene_id);
        if (row.room_id.Length == 0)
        {
            return scene_name;
        }
        var room = db.get_row("dungeon_room", row.room_id) as DungeonRoomRow;
        string room_name = room != null ? room.room_name : "";
        if (scene_name.Length == 0)
        {
            return room_name;
        }
        if (room_name.Length == 0)
        {
            return scene_name;
        }
        return $"{scene_name} · {room_name}";
    }

    // ---------------------------------------------------------------- 表内 id → 中文名

    private string _chapter_name(Godot.Collections.Array rows)
    {
        if (rows.Count == 0)
        {
            return "暂无章节";
        }
        string chapter_id = rows[0].AsGodotObject() is GuideStepRow first ? first.chapter_id : "";
        var row = db.get_row("chapter_def", chapter_id) as ChapterDefRow;
        return row != null ? row.name_cn : "第一段路";
    }

    private string _scene_name(string scene_id)
    {
        if (scene_id.Length == 0)
        {
            return "";
        }
        var row = db.get_row("map_local", scene_id) as MapLocalRow;
        return row != null ? row.name_cn : "";
    }

    private string _char_name(string char_id)
    {
        var row = db.get_row("character_base", char_id) as CharacterBaseRow;
        return row != null ? row.name_cn : char_id;
    }

    private string _npc_name(string npc_id)
    {
        var row = db.get_row("npc_def", npc_id) as NpcDefRow;
        return row != null ? row.name_cn : npc_id;
    }

    private string _item_name(string item_id)
    {
        var row = db.get_row("item_base", item_id) as ItemBaseRow;
        return row != null ? row.name_cn : "";
    }

    /// <summary>打过交道才有好感这一行（好感账本里没这一号人＝还没见过他，别摆一个「好感 0」）。</summary>
    private string _favor_text(string id)
    {
        if (state == null)
        {
            return "";
        }
        Godot.Collections.Dictionary favor_book = state.Get("npc_favor").AsGodotDictionary();
        return favor_book.ContainsKey(id) ? $"好感 {favor_book[id].AsInt32()}" : "";
    }

    /// <summary>碎句本来就短，摆进一行时去掉结尾的句号，读起来更像「线索」不像「段落」。</summary>
    private static string _trim_sentence(string text)
    {
        string trimmed = text.Trim();
        return trimmed.EndsWith("。") ? trimmed[..^1] : trimmed;
    }

    /// <summary>`row.clues()`（`string[]`）→ 一个字符串数组（对位 GDScript 的 `Array(row.clues())`）。</summary>
    private static Godot.Collections.Array _clues_of(string[] clues)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (string clue in clues)
        {
            out_rows.Add(clue);
        }
        return out_rows;
    }

    // ---------------------------------------------------------------- 条目与文案

    private Godot.Collections.Dictionary _event_entry(EventCheckRow row)
    {
        string check_id = row.check_id;
        string result = state != null ? state.Call("event_check_result", check_id).AsString() : "";
        return new Godot.Collections.Dictionary
        {
            { "kind", "event" },
            { "id", check_id },
            { "name", row.note },
            { "clues", _clues_of(row.clues()) },
            { "note", row.fail_note },
            { "source", row.check_source },
            { "source_label", source_label(row.check_source) },
            { "difficulty", row.difficulty },
            { "done", result == "done" },
            { "failed", result == "failed" },
            { "unsupported", false },
            { "condition", "" },
        };
    }

    /// <summary>`skill:qimen` → 「奇门 3」的样式（线索本要告诉玩家拿什么去判）。</summary>
    public string source_label(string source)
    {
        string[] parts = source.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return source;
        }
        if (parts[0] == "skill")
        {
            var skill = db.get_row("event_skill_def", parts[1]) as EventSkillDefRow;
            return skill != null ? skill.name_cn : parts[1];
        }
        var attr = db.get_row("attribute_def", parts[1]) as AttributeDefRow;
        return attr != null ? attr.name_cn : parts[1];
    }

    public string node_name(string node_id)
    {
        var row = db.get_row("map_region", node_id) as MapRegionRow;
        return row != null ? row.name_cn : node_id;
    }

    /// <summary>面板用的一行文案。</summary>
    public string describe(Godot.Collections.Dictionary entry)
    {
        string status = "未完成";
        if (_TargetDone(entry))
        {
            status = "已完成";
        }
        else if (entry.ContainsKey("failed") && entry["failed"].AsBool())
        {
            status = "失败过";
        }
        string name = entry.ContainsKey("name") ? entry["name"].AsString() : "";
        var lines = new System.Collections.Generic.List<string> { $"{name}（{status}）" };
        Godot.Collections.Array clues = entry.ContainsKey("clues")
            ? entry["clues"].AsGodotArray()
            : new Godot.Collections.Array();
        if (clues.Count > 0)
        {
            var clue_texts = new System.Collections.Generic.List<string>();
            foreach (Variant clue in clues)
            {
                clue_texts.Add(clue.AsString());
            }
            string suffix = entry.ContainsKey("hint_known") && entry["hint_known"].AsBool()
                ? "　（已从传闻中听说）"
                : "";
            lines.Add($"　线索：{string.Join("；", clue_texts)}{suffix}");
        }
        string source_label_text = entry.ContainsKey("source_label")
            ? entry["source_label"].AsString()
            : "";
        string note = entry.ContainsKey("note") ? entry["note"].AsString() : "";
        if (source_label_text.Length == 0)
        {
            if (note.Length > 0)
            {
                lines.Add($"　{note}");
            }
        }
        else
        {
            int difficulty = entry.ContainsKey("difficulty") ? entry["difficulty"].AsInt32() : 0;
            lines.Add($"　判定：{source_label_text} ≥ {difficulty}　失败：{note}");
        }
        if (entry.ContainsKey("unsupported") && entry["unsupported"].AsBool())
        {
            lines.Add("　（这一类还缺地图位点，暂时做不了）");
        }
        return string.Join("\n", lines);
    }
}
