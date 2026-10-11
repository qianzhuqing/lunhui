using Godot;

namespace Lunhui.Core;

/// <summary>
/// 大地图：区域揭开与驿站传送／难度切换。C# 版，对位原 `src/core/world_map_service.gd`（322 行）。
///
/// 设计出处：
///   02_地图与明雷.md —— 「已探索的地标显示在地图上，未探索的部分用云雾盖着，走进去才揭开」；
///                        「步行 + 驿站传送（发现驿站后解锁）」。
///   03_副本_黑风寨.md —— 「难度可在大地图任意驿站切换」。
///
/// 规则全部读 `map_region.csv`：`reveal_on_map=1` 一开始就点亮，
/// `unlock_condition=proximity_N` 走近 N 格自动揭开，`discover_<node_id>` 要先发现那个地标。
/// 揭开状态写存档（`GameState.revealed_nodes`），所以「地图是自己走出来的」能留住。
/// </summary>
public partial class WorldMapService : RefCounted
{
    /// <summary>一格多少像素（与地编的 32×32 一致）</summary>
    public const double TILE = 32.0;

    /// <summary>
    /// 难度解锁条件 → 需要击败的敌人 id。
    /// `difficulty_config.unlock_condition` 是中文短语，不是 id，所以这里用短语当键做映射；
    /// 表里出现没登记过的短语就报错并按「未解锁」处理（改文案时能立刻发现）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string[]> DIFFICULTY_RULES = new()
    {
        { "默认解锁", System.Array.Empty<string>() },
        { "通关第一章", new[] { "en_bd_boss" } },
        { "通关第一章并击败醉刀客", new[] { "en_bd_boss", "en_hidden_drunk" } },
    };

    /// <summary>
    /// 本章不开放的小地图（**唯一定义处**：大地图控制器直接读这一份，决策 88）。
    /// 下面这一行的字面量就是 `_check_single_source_of_truth` 认的那条字符串——
    /// 改列表会连带改门限标签（故意的：这件事不许在别处再抄一份）。
    /// </summary>
    public static readonly string[] LOCKED_SCENES = { "scene_ferry_locked" };

    /// <summary>章节所在副本（解锁「通关第一章」看这里记录过的 Boss）</summary>
    public const string CHAPTER_SCENE = "scene_heifengzhai";

    public TableDb db;
    public GodotObject state;

    public WorldMapService(TableDb table_db, GodotObject game_state)
    {
        db = table_db;
        state = game_state;
    }

    public static WorldMapService create(TableDb db, GodotObject state) => new(db, state);

    // ------------------------------------------------------------------ 取值口（跨语言／校验器共用）

    /// <summary>
    /// 「本章不开放的小地图」清单的唯一出处（决策 88）。原先校验器/控制器是借
    /// `TableValidator.ScriptConstants(...)["LOCKED_SCENES"]` 读那份 GDScript 的常量表，
    /// 搬成 C# 之后走这个口——**还是只有这一份**。
    /// </summary>
    public static string[] locked_scenes() => LOCKED_SCENES;

    /// <summary>登记过的难度解锁短语（构建期校验 `difficulty_config.unlock_condition` 用）。</summary>
    public static string[] difficulty_conditions()
    {
        var out_list = new System.Collections.Generic.List<string>();
        foreach (string condition in DIFFICULTY_RULES.Keys)
        {
            out_list.Add(condition);
        }

        return out_list.ToArray();
    }

    // ------------------------------------------------------------------ 节点

    public Godot.Collections.Array all_nodes()
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("map_region"))
        {
            out_rows.Add(row_variant);
        }

        return out_rows;
    }

    public Resource node_row(string node_id) => db.get_row("map_region", node_id);

    public string node_name(string node_id)
    {
        Resource row = node_row(node_id);
        return row != null ? row.Get("name_cn").AsString() : node_id;
    }

    public bool is_revealed(string node_id) =>
        state != null && state.Call("is_node_revealed", node_id).AsBool();

    /// <summary>
    /// `unlock_condition=discover_X` 里的 X 没带 `n_` 前缀（表里写 discover_luoyanpo，
    /// 节点 id 却是 n_luoyanpo），所以两种写法都认一下。
    /// </summary>
    public bool discovery_target_revealed(string suffix) =>
        is_revealed(suffix) || is_revealed($"n_{suffix}");

    // ------------------------------------------------------------------ 揭开

    /// <summary>开工先点亮「一开始就该看见」的地标（`reveal_on_map=1`）。返回新揭开的 id。</summary>
    public string[] apply_initial_reveals()
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in all_nodes())
        {
            Resource row = row_variant.As<Resource>();
            if (!row.Get("reveal_on_map").AsBool())
            {
                continue;
            }

            // `reveal_on_map=1` 只说明「这类地点默认出现在地图上」——**持有类条件还要先满足**
            // （设计 0.26 §六：没拿到藏宝图之前，石隙迷窟在大地图上根本不存在）
            if (!_held_condition_met(row.Get("unlock_condition").AsString()))
            {
                continue;
            }

            if (_reveal(row.Get("node_id").AsString()))
            {
                out_ids.Add(row.Get("node_id").AsString());
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>
    /// 按玩家位置揭开：proximity_N 走进去就揭开。markers 传 {node_id: 位置(Vector2)}
    /// 返回这一次新揭开的 id（调用方拿去提示玩家）。
    /// </summary>
    public string[] apply_proximity_reveals(Vector2 player_position, Godot.Collections.Dictionary markers)
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in all_nodes())
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            if (is_revealed(node_id))
            {
                continue;
            }

            string condition = row.Get("unlock_condition").AsString();
            if (!condition.StartsWith("proximity_"))
            {
                continue;
            }

            if (!markers.ContainsKey(node_id))
            {
                continue;
            }

            double tiles = _to_float(condition.Substring("proximity_".Length));
            Vector2 position = markers[node_id].AsVector2();
            if (player_position.DistanceTo(position) <= tiles * TILE)
            {
                if (_reveal(node_id))
                {
                    out_ids.Add(node_id);
                }
            }
        }

        // 发现前置类：discover_X 要求 X 已揭开（同一次刷新里继续传播，处理链式依赖）
        bool changed = true;
        int guard = 0;
        int node_count = all_nodes().Count;
        while (changed && guard < node_count)
        {
            changed = false;
            guard += 1;
            foreach (Variant row_variant in all_nodes())
            {
                Resource row = row_variant.As<Resource>();
                string node_id = row.Get("node_id").AsString();
                if (is_revealed(node_id))
                {
                    continue;
                }

                string condition = row.Get("unlock_condition").AsString();
                if (!condition.StartsWith("discover_"))
                {
                    continue;
                }

                if (discovery_target_revealed(condition.Substring("discover_".Length)))
                {
                    if (_reveal(node_id))
                    {
                        out_ids.Add(node_id);
                        changed = true;
                    }
                }
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>
    /// 持有类解锁：`unlock_condition` 写的是**物品 id**（`item_treasure_map`），
    /// 也接受 `item:<id>` 这种带冒号的写法。
    ///
    /// 注意 `item_treasure_map` 这种 id **本身就带 `item_` 前缀**——所以不能一律按
    /// 「去掉 `item_`」解析（那样会得到 `treasure_map`，永远查不到）。这里先按**整串**查表，
    /// 查不到再看是不是 `item:` 写法。返回空串 = 这条不是持有类条件。
    ///
    /// **是静态的**：构建期校验器也要按同一条规则判「哪个兴趣点允许带图标」
    /// （0.32.0：兴趣点不给图标，例外只有「藏宝图指向的那一个」）——抄第二份就会漂。
    /// 这里只判「这个条件是持有类」，背包里有没有由 `_held_condition_met` 补。
    /// </summary>
    public static string held_item_id(TableDb table_db, string condition)
    {
        if (condition.Length == 0 || table_db == null)
        {
            return "";
        }

        if (condition.StartsWith("item:"))
        {
            string colon_id = condition.Substring("item:".Length);
            return table_db.get_row("item_base", colon_id) != null ? colon_id : "";
        }

        return table_db.get_row("item_base", condition) != null ? condition : "";
    }

    private bool _held_condition_met(string condition)
    {
        string item_id = held_item_id(db, condition);
        if (item_id.Length == 0)
        {
            // 不是持有类条件（其余写法由各自的分支管，别在这里拦）
            return true;
        }

        GodotObject inventory = state?.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return false;
        }

        return inventory.Call("has", item_id, 1).AsBool();
    }

    /// <summary>背包里拿到了持有类条件要的东西 → 立刻揭开（`偷到图` 不该等到下次走近刷新点）。</summary>
    public string[] apply_held_reveals()
    {
        var out_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in all_nodes())
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            if (is_revealed(node_id))
            {
                continue;
            }

            string condition = row.Get("unlock_condition").AsString();
            if (held_item_id(db, condition).Length == 0)
            {
                continue;
            }

            if (!_held_condition_met(condition))
            {
                continue;
            }

            if (_reveal(node_id))
            {
                out_ids.Add(node_id);
            }
        }

        return out_ids.ToArray();
    }

    /// <summary>
    /// 条件地表层（`Conditional`，设计 16 §3.2 的第 5 层，0.32.0 新增）：**整层**显隐的地表。
    ///
    /// 现在唯一的用法是大地图那条「落雁坡西 → 石隙迷窟」的碎石细径（22 格）：藏宝图到手之前
    /// 它在地图上不存在——「藏宝图上的一条线」（18 号 §5.6／19 号）。**条件不在这里写死物品 id**：
    /// 问这些地标的 `unlock_condition`，把持有类的那些要的东西列出来；
    /// **任一件到手 → 整层显示**（层是整层的，不为每个地标切一半）。
    /// 没有持有类地标 → apply=false（这层没有条件可依，保持地编摆的样子）。
    ///
    /// 静态：大地图与小地图两个控制器读同一条规则（谁都不许再抄一份）。
    /// </summary>
    public static Godot.Collections.Dictionary conditional_layer_rule(TableDb table_db, string[] node_ids)
    {
        var items = new System.Collections.Generic.List<string>();
        foreach (string node_id in node_ids ?? System.Array.Empty<string>())
        {
            Resource row = table_db != null ? table_db.get_row("map_region", node_id) : null;
            if (row == null)
            {
                continue;
            }

            string item_id = held_item_id(table_db, row.Get("unlock_condition").AsString());
            if (item_id.Length != 0 && !items.Contains(item_id))
            {
                items.Add(item_id);
            }
        }

        return new Godot.Collections.Dictionary
        {
            { "apply", items.Count != 0 },
            { "items", items.ToArray() },
        };
    }

    /// <summary>条件地表该不该显示（上一条规则 ＋ 背包里有没有）</summary>
    public static bool conditional_layer_visible(TableDb table_db, GodotObject state, string[] node_ids)
    {
        Godot.Collections.Dictionary rule = conditional_layer_rule(table_db, node_ids);
        if (!rule["apply"].AsBool())
        {
            return true;
        }

        GodotObject inventory = state?.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return false;
        }

        foreach (string item_id in rule["items"].AsStringArray())
        {
            if (inventory.Call("has", item_id, 1).AsBool())
            {
                return true;
            }
        }

        return false;
    }

    private bool _reveal(string node_id) =>
        state != null && state.Call("reveal_node", node_id).AsBool();

    public int revealed_count()
    {
        int count = 0;
        foreach (Variant row_variant in all_nodes())
        {
            if (is_revealed(row_variant.As<Resource>().Get("node_id").AsString()))
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>HUD 文案：「地图 3/7（未探索：黑风寨、塌陷山洞…）」</summary>
    public string progress_text()
    {
        var hidden = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in all_nodes())
        {
            Resource row = row_variant.As<Resource>();
            if (!is_revealed(row.Get("node_id").AsString()))
            {
                hidden.Add(row.Get("name_cn").AsString());
            }
        }

        string tail = hidden.Count == 0 ? "" : $"　（未探索：{string.Join("、", hidden)}）";
        return $"地图 {revealed_count()}/{all_nodes().Count}{tail}";
    }

    // ------------------------------------------------------------------ 驿站传送

    /// <summary>能从驿站去哪些地方：已揭开、有可进入的小地图、不是本章锁着的、也不是当前这张图</summary>
    public Godot.Collections.Array travel_targets(string current_scene_id = "")
    {
        var out_targets = new Godot.Collections.Array();
        foreach (Variant row_variant in all_nodes())
        {
            Resource row = row_variant.As<Resource>();
            string node_id = row.Get("node_id").AsString();
            string scene_id = row.Get("enter_scene").AsString();
            if (scene_id.Length == 0 || System.Array.IndexOf(LOCKED_SCENES, scene_id) >= 0)
            {
                continue;
            }

            if (!is_revealed(node_id))
            {
                continue;
            }

            out_targets.Add(new Godot.Collections.Dictionary
            {
                { "node_id", node_id },
                { "name", row.Get("name_cn").AsString() },
                { "scene_id", scene_id },
                { "current", scene_id == current_scene_id },
            });
        }

        return out_targets;
    }

    /// <summary>传送（只做校验与返回目标场景，切场景交给调用方）</summary>
    public Godot.Collections.Dictionary travel(string node_id, string current_scene_id = "")
    {
        foreach (Variant entry_variant in travel_targets(current_scene_id))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (entry["node_id"].AsString() == node_id)
            {
                return _travel_result(
                    true, "", entry["scene_id"].AsString(), entry["name"].AsString());
            }
        }

        Resource row = node_row(node_id);
        if (row == null)
        {
            return _travel_result(false, $"没有这个地标：{node_id}", "", "");
        }

        string node_name_text = row.Get("name_cn").AsString();
        if (!is_revealed(node_id))
        {
            return _travel_result(false, $"{node_name_text} 还没探索到（先走过去）", "", node_name_text);
        }

        if (row.Get("enter_scene").AsString().Length == 0)
        {
            return _travel_result(false, $"{node_name_text} 不是可进入的地图", "", node_name_text);
        }

        return _travel_result(false, $"{node_name_text} 本章不可前往", "", node_name_text);
    }

    private static Godot.Collections.Dictionary _travel_result(
        bool ok, string error, string scene_id, string name) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "scene_id", scene_id },
            { "name", name },
        };

    // ------------------------------------------------------------------ 难度切换

    /// <summary>难度能不能选：{ok, error, defeated, need}</summary>
    public Godot.Collections.Dictionary can_switch_difficulty(string difficulty_id)
    {
        Resource row = db.get_row("difficulty_config", difficulty_id);
        if (row == null)
        {
            return _difficulty_result(false, $"没有这个难度：{difficulty_id}", new(), new());
        }

        string condition = row.Get("unlock_condition").AsString();
        if (!DIFFICULTY_RULES.ContainsKey(condition))
        {
            GD.PushError($"[WorldMapService] difficulty_config 里没登记过的解锁条件：{condition}");
            return _difficulty_result(false, $"解锁条件未登记：{condition}", new(), new());
        }

        string[] need = DIFFICULTY_RULES[condition];
        if (need.Length == 0)
        {
            return _difficulty_result(true, "", new(), new());
        }

        Godot.Collections.Array defeated = _defeated_bosses();
        var missing = new System.Collections.Generic.List<string>();
        foreach (string enemy_id in need)
        {
            if (defeated.Contains(enemy_id))
            {
                continue;
            }

            Resource enemy = db.get_row("enemy_base", enemy_id);
            missing.Add(enemy != null ? enemy.Get("name_cn").AsString() : enemy_id);
        }

        if (missing.Count == 0)
        {
            return _difficulty_result(true, "", defeated, _need_array(need));
        }

        return _difficulty_result(
            false, $"还没满足条件：先击败 {string.Join("、", missing)}", defeated, _need_array(need));
    }

    /// <summary>切换难度（只改存档里的 difficulty_id，战斗与掉落都会跟着换）</summary>
    public Godot.Collections.Dictionary switch_difficulty(string difficulty_id)
    {
        Godot.Collections.Dictionary check = can_switch_difficulty(difficulty_id);
        if (!check["ok"].AsBool())
        {
            return check;
        }

        if (state == null)
        {
            return _difficulty_result(false, "没有会话状态", new(), new());
        }

        string previous = state.Get("difficulty_id").AsString();
        state.Set("difficulty_id", difficulty_id);
        Resource row = db.get_row("difficulty_config", difficulty_id);
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "error", "" },
            { "defeated", check["defeated"] },
            { "need", check["need"] },
            { "name", row != null ? row.Get("name_cn").AsString() : difficulty_id },
            { "previous", previous },
        };
    }

    /// <summary>驿站面板用：三个难度的状态一览</summary>
    public Godot.Collections.Array difficulty_rows()
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("difficulty_config"))
        {
            Resource row = row_variant.As<Resource>();
            string difficulty_id = row.Get("difficulty_id").AsString();
            Godot.Collections.Dictionary check = can_switch_difficulty(difficulty_id);
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "difficulty_id", difficulty_id },
                { "name", row.Get("name_cn").AsString() },
                { "condition", row.Get("unlock_condition").AsString() },
                { "ok", check["ok"].AsBool() },
                { "error", check["error"].AsString() },
                { "current", state != null && state.Get("difficulty_id").AsString() == difficulty_id },
            });
        }

        return out_rows;
    }

    /// <summary>`CHAPTER_SCENE` 那本账里记过的 Boss（解锁「通关第一章」看它）</summary>
    private Godot.Collections.Array _defeated_bosses()
    {
        if (state == null)
        {
            return new Godot.Collections.Array();
        }

        Godot.Collections.Dictionary record =
            state.Call("dungeon_record", CHAPTER_SCENE).AsGodotDictionary();
        return record.ContainsKey("bosses")
            ? record["bosses"].AsGodotArray()
            : new Godot.Collections.Array();
    }

    private static Godot.Collections.Array _need_array(string[] need)
    {
        var out_need = new Godot.Collections.Array();
        foreach (string enemy_id in need)
        {
            out_need.Add(enemy_id);
        }

        return out_need;
    }

    private static Godot.Collections.Dictionary _difficulty_result(
        bool ok, string error, Godot.Collections.Array defeated, Godot.Collections.Array need) =>
        new()
        {
            { "ok", ok },
            { "error", error },
            { "defeated", defeated },
            { "need", need },
        };

    /// <summary>`proximity_N` 里的 N：表里写的是小数（`proximity_5`／`proximity_2.5` 都收）。</summary>
    private static double _to_float(string text)
    {
        double value = 0.0;
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
        return value;
    }
}
