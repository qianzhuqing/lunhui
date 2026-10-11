using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 线索本（03_副本_黑风寨.md：「线索必须能被找到，否则就是猜谜。每条隐藏至少给两个线索来源」）。
/// C# 版，对位原 `src/core/clue_service.gd`（516 行），拆两份 partial：
/// 本文件＝常量／取线索／纪事五类分页，`ClueServiceText.cs`＝查表拼文案（含 `describe`／`source_label`）。
///
/// 数据全在表里：`hidden_trigger.clue_source` 与 `event_check.clue_source` 都是分号分隔的线索来源
/// （行类的 `clues()` 解析）。这里把它们整理成玩家能读的列表，并带上「做过没」的状态：
///   隐藏内容看存档 `dungeon_records.triggers`
///   事件判定看存档 `event_checks`
///   大地图上的事件按 `region_id` 归到地标名下（玩家在大地图按 K 就能翻线索本）
/// </summary>
public partial class ClueService : RefCounted
{
    /// <summary>
    /// 纪事（线索本）的五类分页（设计 0.32.0「纪事＝线索本扩成五类」，逐条见 UI 落地清单 §二-10）。
    ///
    /// **顺序就是页签顺序**；「隐藏」那一页跟着打开时的视角（小地图／大地图）走，另外四类是全局的。
    /// 这里只认**现成的表**：主线＝引导步骤、支线＝NPC 委托、传闻＝随机事件的 hint ＋ 观察点碎句、
    /// 角色＝招募表与 NPC 表、隐藏＝隐藏内容与事件判定。**不为这五页新开表、不加列。**
    /// </summary>
    public static readonly Godot.Collections.Array SECTION_ORDER =
        new() { "main", "side", "rumor", "cast", "hidden" };

    public static readonly Godot.Collections.Dictionary SECTION_TITLES = new()
    {
        { "main", "主线" },
        { "side", "支线" },
        { "rumor", "传闻" },
        { "cast", "角色" },
        { "hidden", "隐藏" },
    };

    /// <summary>「这条碎句我读到过」的旗标前缀（`LocalMapController`／`OverworldController` 读到观察点时置）。</summary>
    public const string OBSERVED_PREFIX = "flag_obs_";

    /// <summary>
    /// 传闻的来源之一：随机事件里 `effect_kind=hint` 那一类（另一类是观察点碎句）。
    /// 注意是 `effect_kind` 不是 `kind`——`kind` 是事件的门类（caravan／omen…），
    /// 「驿站风声」那条的 `kind` 写的是 `omen`，只有 `effect_kind` 才是 `hint`。
    /// </summary>
    public const string HINT_KIND = "hint";

    public TableDb db;
    public GodotObject state;

    public ClueService(TableDb table_db, GodotObject game_state)
    {
        db = table_db;
        state = game_state;
    }

    /// <summary>静态工厂：GDScript 侧不能写 `.new(db, state)`（带参构造过不去），一律走 `create`。</summary>
    public static ClueService create(TableDb db, GodotObject state) => new(db, state);

    /// <summary>一张小地图的线索：隐藏内容 + 事件判定。</summary>
    public Godot.Collections.Array clues_for_scene(string scene_id)
    {
        var out_rows = new Godot.Collections.Array();
        Godot.Collections.Array done_triggers = new();
        if (state != null)
        {
            Godot.Collections.Dictionary record = state.Call("dungeon_record", scene_id).AsGodotDictionary();
            if (record.ContainsKey("triggers"))
            {
                done_triggers = record["triggers"].AsGodotArray();
            }
        }
        foreach (Variant row_variant in db.rows("hidden_trigger"))
        {
            if (row_variant.AsGodotObject() is not HiddenTriggerRow row || row.scene_id != scene_id)
            {
                continue;
            }
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "kind", "hidden" },
                { "id", row.trigger_id },
                { "name", row.name_cn },
                { "clues", _clues_of(row.clues()) },
                { "note", row.note },
                { "done", done_triggers.Contains(row.trigger_id) },
                {
                    "hint_known",
                    state != null
                        && state.Call("has_flag", WorldEventService.hint_flag(row.trigger_id)).AsBool()
                },
                { "unsupported", row.trigger_type == "sequence" },
                { "condition", row.required_condition },
            });
        }
        foreach (Variant row_variant in db.rows("event_check"))
        {
            if (row_variant.AsGodotObject() is not EventCheckRow row || row.scene_id != scene_id)
            {
                continue;
            }
            out_rows.Add(_event_entry(row));
        }
        return out_rows;
    }

    /// <summary>大地图按地标分组的线索（`event_check.region_id`）。</summary>
    public Godot.Collections.Array clues_for_region(string region_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("event_check"))
        {
            if (row_variant.AsGodotObject() is not EventCheckRow row || row.region_id != region_id)
            {
                continue;
            }
            Godot.Collections.Dictionary entry = _event_entry(row);
            entry["region_name"] = node_name(region_id);
            out_rows.Add(entry);
        }
        return out_rows;
    }

    /// <summary>全部大地图线索（按地标名分组显示用）。</summary>
    public Godot.Collections.Array region_clues()
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("map_region"))
        {
            if (row_variant.AsGodotObject() is MapRegionRow row)
            {
                foreach (Variant entry in clues_for_region(row.node_id))
                {
                    out_rows.Add(entry);
                }
            }
        }
        return out_rows;
    }

    // ---------------------------------------------------------------- 纪事：五类分页

    /// <summary>
    /// 一页的内容：`{id, title, subtitle, entries}`；每条 `entries` 是 `{id, name, text, done}`。
    ///
    /// 文案只在这里拼——面板负责摆，不负责编话（同 `describe()` 的口径）。
    /// </summary>
    public Godot.Collections.Dictionary section(
        string section_id, string scope = "scene", string scene_id = "")
    {
        switch (section_id)
        {
            case "main":
                return _main_section();
            case "side":
                return _side_section();
            case "rumor":
                return _rumor_section();
            case "cast":
                return _cast_section();
            default:
                return _hidden_section(scope, scene_id);
        }
    }

    private static Godot.Collections.Dictionary _section_entry(
        string id, string name, string text, bool done) =>
        new() { { "id", id }, { "name", name }, { "text", text }, { "done", done } };

    /// <summary>
    /// 主线：`guide_step` 按 `sort_order` 排，当前步＝条件已满足的最后一行（与 `GuideService` 同一口径）。
    /// 顶部写百分比（已了步数／总步数），每步写状态词——**旗标名一个都不许露**。
    /// </summary>
    private Godot.Collections.Dictionary _main_section()
    {
        Godot.Collections.Array rows = GuideService.steps(db);
        Godot.Collections.Dictionary current_page = GuideService.current(db, state);
        int current = current_page.ContainsKey("index") ? current_page["index"].AsInt32() : 0;
        int done_steps = System.Math.Max(0, current - 1);
        int percent = 0;
        if (rows.Count > 0)
        {
            percent = Mathf.RoundToInt(100.0 * done_steps / rows.Count);
        }
        var entries = new Godot.Collections.Array();
        foreach (Variant row_variant in rows)
        {
            if (row_variant.AsGodotObject() is not GuideStepRow row)
            {
                continue;
            }
            int index = row.sort_order;
            string status = "未接";
            if (index < current)
            {
                status = "已了";
            }
            else if (index == current)
            {
                status = "进行中";
            }
            var lines = new System.Collections.Generic.List<string>
            {
                $"{row.text_cn}　［{status}］",
            };
            if (row.condition_spec.Length > 0)
            {
                lines.Add($"　{row.condition_spec}");
            }
            entries.Add(_section_entry(row.step_id, row.text_cn, string.Join("\n", lines), status == "已了"));
        }
        return new Godot.Collections.Dictionary
        {
            { "id", "main" },
            { "title", $"纪事　（主线 · {_chapter_name(rows)}）" },
            { "subtitle", $"章节进度 {percent}%　（已了 {done_steps}／共 {rows.Count} 步）" },
            { "entries", entries },
        };
    }

    /// <summary>支线：`npc_quest` 逐条摆。状态词按 `NpcService` 那套判——已了／待交（条件已达成）／待接（前置没齐）。</summary>
    private Godot.Collections.Dictionary _side_section()
    {
        var rows = new System.Collections.Generic.List<NpcQuestRow>();
        foreach (Variant row_variant in db.rows("npc_quest"))
        {
            if (row_variant.AsGodotObject() is NpcQuestRow row)
            {
                rows.Add(row);
            }
        }
        rows.Sort((a, b) => a.npc_id == b.npc_id
            ? a.sort_order.CompareTo(b.sort_order)
            : string.CompareOrdinal(a.npc_id, b.npc_id));
        var entries = new Godot.Collections.Array();
        int done_count = 0;
        foreach (NpcQuestRow row in rows)
        {
            bool done = NpcService.quest_done(state, row.quest_id);
            string status = "待接";
            if (done)
            {
                status = "已了";
                done_count += 1;
            }
            else if (NpcService.available_quest(db, state, row.npc_id) is NpcQuestRow available
                     && available.quest_id == row.quest_id)
            {
                status = "待交";
            }
            var lines = new System.Collections.Generic.List<string>
            {
                $"{row.text_cn}　［{status}］",
                $"　委托人：{_npc_name(row.npc_id)}　{_requirement_text(row.requirement)}",
            };
            string reward = _reward_text(row);
            if (reward.Length > 0)
            {
                lines.Add($"　奖励：{reward}");
            }
            entries.Add(_section_entry(row.quest_id, row.text_cn, string.Join("\n", lines), done));
        }
        return new Godot.Collections.Dictionary
        {
            { "id", "side" },
            { "title", "纪事　（支线 · 委托）" },
            {
                "subtitle",
                $"共 {rows.Count} 条委托　已了 {done_count} 条　（每条写清了委托人与奖励）"
            },
            { "entries", entries },
        };
    }

    /// <summary>
    /// 传闻：**只听、只看现成的两处来源**——① 随机事件的 `hint` 效果（听到即记下）；
    /// ② 观察点 `flavor_point` 读到的碎句。
    ///
    /// 「验」＝这条线索**指向的东西被真触发**：指向隐藏内容看 `dungeon_records.triggers`，
    /// 指向事件判定看 `event_checks`。指向关系**不新开列**——拿碎句去 `clue_source` 里逐字对，
    /// 对得上就是同一个东西（观察点与隐藏线索本来就是同一批文案）。
    /// </summary>
    private Godot.Collections.Dictionary _rumor_section()
    {
        var entries = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("world_event"))
        {
            if (row_variant.AsGodotObject() is not WorldEventRow row || row.effect_kind != HINT_KIND)
            {
                continue;
            }
            string trigger_id = row.effect_id;
            if (state == null
                || !state.Call("has_flag", WorldEventService.hint_flag(trigger_id)).AsBool())
            {
                continue;
            }
            Godot.Collections.Dictionary target = _trigger_target(trigger_id);
            var lines = new System.Collections.Generic.List<string>
            {
                // 行 1 是事件名（「驿站风声」这类），下面才是**听到了什么**——
                // 策划 2026-10-04 拍：传闻行＝事件名 ＋ 听到的那句话 ＋ 已验／未验，不加前置条件栏。
                $"{row.name_cn}　［{_verify_word(target)}］",
            };
            if (row.prompt_text_cn.Length > 0)
            {
                lines.Add($"　{row.prompt_text_cn}");
            }
            if (row.text_cn.Length > 0)
            {
                lines.Add($"　{row.text_cn}");
            }
            lines.Add($"　指向：{_TargetName(target, "线索")}");
            entries.Add(_section_entry(
                row.event_id, row.name_cn, string.Join("\n", lines), _TargetDone(target)));
        }
        foreach (Variant row_variant in db.rows("flavor_point"))
        {
            if (row_variant.AsGodotObject() is not FlavorPointRow row)
            {
                continue;
            }
            if (state == null
                || !state.Call("has_flag", OBSERVED_PREFIX + row.point_id).AsBool())
            {
                continue;
            }
            Godot.Collections.Dictionary target = _clue_target(row.text_cn);
            string sentence = _trim_sentence(row.text_cn);
            var lines = new System.Collections.Generic.List<string>
            {
                $"{sentence}　［{_verify_word(target)}］",
            };
            string place = _point_place(row);
            if (place.Length > 0)
            {
                lines.Add($"　看到于：{place}");
            }
            if (_TargetName(target, "").Length > 0)
            {
                lines.Add($"　指向：{_TargetName(target, "")}");
            }
            entries.Add(_section_entry(row.point_id, sentence, string.Join("\n", lines), _TargetDone(target)));
        }
        return new Godot.Collections.Dictionary
        {
            { "id", "rumor" },
            { "title", "纪事　（传闻）" },
            { "subtitle", _rumor_subtitle(entries) },
            { "entries", entries },
        };
    }

    /// <summary>
    /// 角色：同伴（`recruit_def`，主角自己不算）＋ 关键人物（`npc_def`）。
    /// 同伴状态＝已入队／待入队（加入条件已达成）／未遇；关键人物＝已结识／未遇（好感账本里有没有这一号）。
    /// </summary>
    private Godot.Collections.Dictionary _cast_section()
    {
        var entries = new Godot.Collections.Array();
        string[] party = state != null ? state.Get("char_ids").AsStringArray() : System.Array.Empty<string>();
        string player_id = party.Length > 0 ? party[0] : "";
        int joined = 0;
        foreach (Variant row_variant in db.rows("recruit_def"))
        {
            if (row_variant.AsGodotObject() is not RecruitDefRow row || row.char_id == player_id)
            {
                continue;
            }
            bool in_party = System.Array.IndexOf(party, row.char_id) >= 0;
            bool met = GuideService.condition_met(state, row.join_condition);
            string status = "未遇";
            if (in_party)
            {
                status = "已入队";
                joined += 1;
            }
            else if (met)
            {
                status = "待入队";
            }
            var lines = new System.Collections.Generic.List<string>
            {
                $"{_char_name(row.char_id)}　［{status}］",
            };
            string scene_name = _scene_name(row.join_scene);
            if (scene_name.Length > 0)
            {
                lines.Add($"　在哪：{scene_name}");
            }
            string favor_line = _favor_text(row.char_id);
            if (favor_line.Length > 0)
            {
                lines.Add($"　{favor_line}");
            }
            if (row.join_note.Length > 0)
            {
                lines.Add($"　{row.join_note}");
            }
            entries.Add(_section_entry(row.char_id, _char_name(row.char_id), string.Join("\n", lines), in_party));
        }
        int npc_total = 0;
        int known_count = 0;
        Godot.Collections.Dictionary favor_book = state != null
            ? state.Get("npc_favor").AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        foreach (Variant row_variant in db.rows("npc_def"))
        {
            if (row_variant.AsGodotObject() is not NpcDefRow row)
            {
                continue;
            }
            npc_total += 1;
            bool known = state != null && favor_book.ContainsKey(row.npc_id);
            if (known)
            {
                known_count += 1;
            }
            var lines = new System.Collections.Generic.List<string>
            {
                $"{row.name_cn}　［{(known ? "已结识" : "未遇")}］",
                $"　{row.title_cn}　Lv.{row.level}",
            };
            string npc_favor_line = _favor_text(row.npc_id);
            if (npc_favor_line.Length > 0)
            {
                lines.Add($"　{npc_favor_line}");
            }
            entries.Add(_section_entry(row.npc_id, row.name_cn, string.Join("\n", lines), known));
        }
        return new Godot.Collections.Dictionary
        {
            { "id", "cast" },
            { "title", "纪事　（角色）" },
            {
                "subtitle",
                $"同伴 {entries.Count - npc_total} 人（已入队 {joined}）　关键人物 {npc_total} 人（已结识 {known_count}）"
            },
            { "entries", entries },
        };
    }

    /// <summary>
    /// 隐藏：隐藏内容 ＋ 事件判定，跟着视线（小地图／大地图）走——与打开线索本那两处入口同一口径。
    /// 小节行写清「共 N 处 · 已揭 M 处 · 未揭 K 处」；**每一处都带自己的线索来源**（03 副本那条设计：
    /// 「线索必须能被找到，否则就是猜谜」），所以这里列名字是有意的，不按示意稿的「只报个数」来。
    /// </summary>
    private Godot.Collections.Dictionary _hidden_section(string scope, string scene_id)
    {
        Godot.Collections.Array raw = scope == "region" ? region_clues() : clues_for_scene(scene_id);
        var entries = new Godot.Collections.Array();
        int done_count = 0;
        foreach (Variant entry_variant in raw)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            bool done = entry.ContainsKey("done") && entry["done"].AsBool();
            if (done)
            {
                done_count += 1;
            }
            entries.Add(_section_entry(
                entry["id"].AsString(), entry["name"].AsString(), describe(entry), done));
        }
        string where = scope == "region" ? "野外（按地标）" : _scene_name(scene_id);
        string head = where.Length == 0 ? "" : $"{where}　";
        return new Godot.Collections.Dictionary
        {
            { "id", "hidden" },
            { "title", "纪事　（隐藏）" },
            {
                "subtitle",
                $"{head}共 {raw.Count} 处　已揭 {done_count} 处　未揭 {raw.Count - done_count} 处"
            },
            { "entries", entries },
        };
    }

    private static string _rumor_subtitle(Godot.Collections.Array entries)
    {
        int verified = 0;
        foreach (Variant entry_variant in entries)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry.ContainsKey("done") && entry["done"].AsBool())
            {
                verified += 1;
            }
        }
        return $"听到 {entries.Count} 条　已验 {verified} 条　（还只是听来的，找到它指向的东西才算数）";
    }

    /// <summary>`target.get("name", fallback)`——目标字典可能整个是空的。</summary>
    private static string _TargetName(Godot.Collections.Dictionary target, string fallback) =>
        target.ContainsKey("name") ? target["name"].AsString() : fallback;

    private static bool _TargetDone(Godot.Collections.Dictionary target) =>
        target.ContainsKey("done") && target["done"].AsBool();
}
