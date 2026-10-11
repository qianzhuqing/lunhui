using Godot;

namespace Lunhui.Core;

/// <summary>
/// 事件判定（01_角色系统.md 的「统一的判定接口」＋ `event_check.csv`）。C# 版，对位原
/// `src/core/event_check_service.gd`（412 行）。
///
/// 判定值来自 `CharacterSheet.event_check_value()`：`attr:x` 取裸属性、`skill:y` = 技能等级 +
/// floor(裸属性/5)。队伍里**谁行谁上**（取最高的那个判定值），当前只有一名角色时等价于他本人。
///
/// 两种强度（照 01 原文实现）：
/// - `hard`：判定值 ≥ 难度必定成功，**不足则无法进行**——确定性，不掷骰、也不扣任何东西；
/// - `soft`：达标必定成功；不足可掷骰，成功率 `0.5 + (判定值 − 难度) × 0.1`，夹在 5%~95%，
///   而且**失败不惩罚**，只是拿不到好处（01：「否则玩家会读档重来，判定就失去意义了」）。
///
/// 这里记一笔曾经的错：12 条判定以前全按「达标才过」跑，等于把 5 条 `soft` 也当成了硬门槛。
/// 公式与数值设计都写在 01 文档里，所以直接照抄成 `SOFT_*` 常量；等设计把常数挪进表再改读表。
///
/// 奖励（设计 06 允许的 7 种，**全部有落点**）：`item`／`skillbook`（同一本书，进背包后走研读流程）、
/// `equip`（建装备实例进背包）、`room`（去某个房间／揭开某个地标）、`event`（记剧情旗标）、
/// `none`（判定本身就是收益）、`boss`（Boss 现身开战）。`boss` 只把「开战请求」（`start_battle`
/// ＋敌人 id）交出去：本服务是纯逻辑、不碰场景，由场景层 `resolve_event()` 各自的开战通道发起。
/// 认不出的 `reward_type` 仍然**显式报错**，不静默当成「没有奖励」。
/// </summary>
public partial class EventCheckService : RefCounted
{
    /// <summary>软判定掷骰（公式出自 01_角色系统.md「两种判定强度」）。</summary>
    public const double SOFT_BASE = 0.5;

    public const double SOFT_PER_POINT = 0.1;
    public const double SOFT_MIN = 0.05;
    public const double SOFT_MAX = 0.95;

    /// <summary>
    /// `fail_note` 里写的是「会付出代价」、但设计还没给数值的判定。
    ///
    /// 现在这一类只有落石陷阱一条（`ev_shed_trap`：原文「触发落石陷阱损失气血」）。设计没给
    /// 「扣多少血」，所以**不自己编**；但也不能让玩家以为真掉了血——按 04 文档「暂缓规则」的同一口径
    /// 在界面上如实说明。设计补了代价列（如 `hp_cost`）之后把这条清单和说明一起删掉。
    /// </summary>
    public static readonly string[] PENDING_COST_CHECKS = { "ev_shed_trap" };

    /// <summary>
    /// 「这条判定过了 → 顺手置这枚旗标」。现例：地牢里**救治被囚村民**（`ev_cell_heal`）＝救出阿福 →
    /// `flag_qiutu_saved`。钱大夫的委托（`nq_qian_01`）等的就是它——没有这条映射，那条委托永远交不了。
    ///
    /// 为什么写在这里而不是表里：`event_check` 的奖励是「一 row 一种类型」而这条判定**既给药品又记账**。
    /// 已记 `待策划确认.md` Q70——设计给列（例如 `event_check.set_flag`）就挪进表。
    /// </summary>
    public static readonly Godot.Collections.Dictionary SUCCESS_FLAGS = new()
    {
        { "ev_cell_heal", "flag_qiutu_saved" },
    };

    public TableDb db;
    public GodotObject state;
    private GodotObject _rng;

    /// <summary>rng 传 null 就用随机种子（正常游戏）；测试传固定种子的 RngService 拿确定结果。</summary>
    public EventCheckService(TableDb table_db, GodotObject game_state, GodotObject rng = null)
    {
        db = table_db;
        state = game_state;
        _rng = rng ?? RngService.create_random();
    }

    /// <summary>
    /// 静态工厂（GDScript 调用点一律把三个实参写满——C# 默认参数只服务 C# 侧，见施工单「二点五一」）。
    /// </summary>
    public static EventCheckService create(TableDb db, GodotObject state, GodotObject rng) =>
        new(db, state, rng);

    public Resource check_row(string check_id) => db.get_row("event_check", check_id);

    /// <summary>这张小地图上的事件位点（给界面／自检用）</summary>
    public Godot.Collections.Array checks_for_scene(string scene_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("event_check"))
        {
            if (row_variant.AsGodotObject() is Resource row
                && row.Get("scene_id").AsString() == scene_id)
            {
                out_rows.Add(row);
            }
        }
        return out_rows;
    }

    /// <summary>这张小地图在配置表里挂在哪个大地图节点（`map_local.parent_node`）</summary>
    public string parent_node_of(string scene_id)
    {
        Resource local = db.get_row("map_local", scene_id);
        return local != null ? local.Get("parent_node").AsString() : "";
    }

    /// <summary>
    /// 这条判定**允许**出现在这张小地图上吗。
    ///
    /// 两种情况：① 表里直接写了 `scene_id`；② 表里只写了 `region_id`，而它正好是这张图的父节点
    /// （07 文档把赌局摆在清风驿·客栈，而表里 `ev_gamble` 的 `scene_id` 是空的）。**注意这只是「允许」，
    /// 不是「存在」**：真正接不接得上由控制器决定——图上必须有 `Event_&lt;check_id&gt;` 位点。
    /// </summary>
    public bool check_allowed_in_scene(string check_id, string scene_id)
    {
        Resource row = check_row(check_id);
        if (row == null)
        {
            return false;
        }
        string row_scene = row.Get("scene_id").AsString();
        if (row_scene == scene_id)
        {
            return true;
        }
        if (row_scene.Length != 0)
        {
            return false;
        }
        string parent = parent_node_of(scene_id);
        return parent.Length != 0 && row.Get("region_id").AsString() == parent;
    }

    /// <summary>大地图上的事件位点（按 region_id 归属）</summary>
    public Godot.Collections.Array checks_for_region(string region_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in db.rows("event_check"))
        {
            if (row_variant.AsGodotObject() is Resource row
                && row.Get("region_id").AsString() == region_id)
            {
                out_rows.Add(row);
            }
        }
        return out_rows;
    }

    /// <summary>全队最高的判定值：{value, char_id, name, source, naked}</summary>
    public Godot.Collections.Dictionary best_check_value(string source)
    {
        var best = new Godot.Collections.Dictionary
        {
            { "value", -999 },
            { "char_id", "" },
            { "name", "" },
            { "source", source },
            { "naked", false },
        };
        if (state == null)
        {
            return best;
        }
        // CharacterSheet.event_check_value 对未知来源会返回 0，这里先自己校验来源有效
        // （表里写错一个 check_source 时，应该报「来源无效」而不是静悄悄按 0 判失败）
        string[] parts = source.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !_source_valid(parts[0], parts[1]))
        {
            return best;
        }
        foreach (string char_id in state.Get("char_ids").AsStringArray())
        {
            CharacterSheet sheet = CharacterSheet.create(db, state, char_id);
            Godot.Collections.Dictionary result = sheet.event_check_value(source);
            if (result["value"].AsInt32() > best["value"].AsInt32())
            {
                best = new Godot.Collections.Dictionary
                {
                    { "value", result["value"].AsInt32() },
                    { "char_id", char_id },
                    { "name", state.Call("char_name", db, char_id).AsString() },
                    { "source", source },
                    { "naked", result.ContainsKey("naked") && result["naked"].AsBool() },
                };
            }
        }
        return best;
    }

    private bool _source_valid(string kind, string target_id)
    {
        if (kind == "skill")
        {
            return db.get_row("event_skill_def", target_id) != null;
        }
        if (kind == "attr")
        {
            return db.get_row("attribute_def", target_id) != null;
        }
        return false;
    }

    /// <summary>
    /// 软判定不足时的成功率：`0.5 + (判定值 − 难度) × 0.1`，夹在 5%~95%。达标（差值 ≥ 0）
    /// 不给 100% 以外的花样，由调用方直接当成功处理。
    /// </summary>
    public double soft_chance(int value, int difficulty) =>
        Mathf.Clamp(SOFT_BASE + (value - difficulty) * SOFT_PER_POINT, SOFT_MIN, SOFT_MAX);

    /// <summary>
    /// 有天赋加成时**实际要比的门槛**（设计 12 §六：江湖百晓生「判定门槛 −2」）。队伍里只要有人带着
    /// `rule:event_check_difficulty`，门槛就往下压（取队里最大的一份）；压到 0 为止，**不写负数**。
    /// </summary>
    public int effective_difficulty(GodotObject row)
    {
        int baseline = row.Get("difficulty").AsInt32();
        if (db == null || state == null)
        {
            return baseline;
        }
        return System.Math.Max(
            0,
            baseline - (int)TalentService.party_rule_value(db, state, "event_check_difficulty", 0.0));
    }

    /// <summary>
    /// 判定：{ok, success, roll_needed, chance, value, difficulty, source_label, who, text}。
    ///
    /// **这里不掷骰**：`judge()` 是可以重复调用的纯查询（界面预览、用例、线索本都调它），
    /// 掷骰只在 `resolve()` 里发生一次。软判定不足时 `success=false` 但 `roll_needed=true`。
    /// </summary>
    public Godot.Collections.Dictionary judge(string check_id)
    {
        Resource row = check_row(check_id);
        if (row == null)
        {
            // 位点/表对不上是**数据错**：id 只进日志，玩家看一句人话（AGENTS：玩家可见文案不许出现表内 id）
            GD.PushError($"[EventCheckService] 没有这条事件判定：{check_id}");
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "success", false },
                { "text", "这里没有可做的判定（数据错，已记进日志）" },
            };
        }
        string note = row.Get("note").AsString();
        if (row.Get("once_only").AsBool() && state != null
            && state.Call("event_check_result", check_id).AsString() == "done")
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "success", false },
                { "text", $"{note}：已经做过了" },
                { "done", true },
            };
        }
        string source = row.Get("check_source").AsString();
        Godot.Collections.Dictionary best = best_check_value(source);
        if (best["value"].AsInt32() < -900)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "success", false },
                { "text", $"{note}：判定来源 {source} 无效" },
            };
        }
        int difficulty = effective_difficulty(row);
        int value = best["value"].AsInt32();
        string check_type = row.Get("check_type").AsString();
        bool success = value >= difficulty;
        bool roll_needed = check_type == "soft" && !success;
        double chance = 0.0;
        if (success)
        {
            chance = 1.0;
        }
        else if (roll_needed)
        {
            chance = soft_chance(value, difficulty);
        }
        string verdict = success ? "成功" : "失败";
        if (roll_needed)
        {
            verdict = $"软判定，掷骰 {Mathf.RoundToInt(chance * 100.0)}%";
        }
        string source_label_text = source_label(source);
        string compare_word = success ? "≥" : "<";
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "success", success },
            { "roll_needed", roll_needed },
            { "chance", chance },
            { "value", value },
            { "difficulty", difficulty },
            { "source", source },
            { "source_label", source_label_text },
            { "who", best["name"].AsString() },
            { "check_type", check_type },
            { "naked", best["naked"].AsBool() },
            { "clue", row.Get("clue_source").AsString() },
            { "note", note },
            {
                "text",
                $"{note}：{source_label_text} {value} {compare_word} {difficulty}，{verdict}"
            },
        };
    }

    /// <summary>走一次事件：判定 → 成功发奖励／失败转述代价，结果写存档。返回 {ok, success, text, reward, detail}</summary>
    public Godot.Collections.Dictionary resolve(string check_id)
    {
        Godot.Collections.Dictionary judged = judge(check_id);
        if (!judged.ContainsKey("ok") || !judged["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "success", false },
                { "reward", new Godot.Collections.Dictionary() },
                { "text", judged.ContainsKey("text") ? judged["text"].AsString() : "" },
                { "already", judged.ContainsKey("done") && judged["done"].AsBool() },
            };
        }
        Resource row = check_row(check_id);
        bool success = judged["success"].AsBool();
        var reward = new Godot.Collections.Dictionary { { "type", "none" }, { "id", "" } };
        string text = judged["text"].AsString();
        bool rolled = false;
        if (judged.ContainsKey("roll_needed") && judged["roll_needed"].AsBool())
        {
            // 软判定不足：掷一次骰。失败不惩罚，只是拿不到好处（01 原文）。
            success = _rng.Call("chance", judged["chance"].AsDouble()).AsBool();
            rolled = true;
            string roll_word = success ? "成功" : "失败";
            text += $"　掷骰 → {roll_word}";
        }
        if (success)
        {
            reward = _grant(row);
            string error = reward.ContainsKey("error") ? reward["error"].AsString() : "";
            if (error.Length != 0)
            {
                text += $"　但{error}";
            }
            else
            {
                string grant_text = reward.ContainsKey("text") ? reward["text"].AsString() : "";
                if (grant_text.Length != 0)
                {
                    text += $"　{grant_text}";
                }
            }
            if (state != null)
            {
                state.Call("record_event_check", check_id, "done");
                // 「过了这条判定 → 置旗标」（见 `SUCCESS_FLAGS`）：判定成功才记账，失败不置
                if (SUCCESS_FLAGS.ContainsKey(check_id))
                {
                    state.Call("set_flag", SUCCESS_FLAGS[check_id].AsString(), true);
                }
            }
        }
        else
        {
            text += $"　{row.Get("fail_note").AsString()}";
            if (System.Array.IndexOf(PENDING_COST_CHECKS, check_id) >= 0)
            {
                text += "（这条代价还没接：设计未给数值，暂不结算）";
            }
            if (state != null)
            {
                state.Call("record_event_check", check_id, "failed");
            }
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "success", success },
            { "reward", reward },
            { "text", text },
            { "already", false },
            { "check_id", check_id },
            { "rolled", rolled },
            { "chance", judged.ContainsKey("chance") ? judged["chance"].AsDouble() : 0.0 },
        };
    }

    /// <summary>
    /// 发奖励：item / skillbook / equip / room / event / none / boss。
    ///
    /// `skillbook` 与 `item` **同一条路**（它就是一本书）；`boss` 只交出「开战请求」；
    /// 认不出的 `reward_type` 显式报错——以前它们会**静默**落进「没有奖励」的兜底，
    /// 等于策划配了不发东西。**public 是因为 `tests/test_event_check.gd` 白盒调它**（`_` 前缀，
    /// 「只被测试调用」那道门限跳过它）。
    /// </summary>
    public Godot.Collections.Dictionary _grant(Resource row)
    {
        string reward_type = row.Get("reward_type").AsString();
        string reward_id = row.Get("reward_id").AsString();
        if (state == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "type", reward_type }, { "id", reward_id }, { "error", "没有会话状态" },
            };
        }
        switch (reward_type)
        {
            case "item":
            case "skillbook":
            {
                // 走与战斗结算同一套入账口径：货币进钱、装备建实例、其余堆叠。
                // （以前这里直接 add_item，「赌局赢钱」就把铜钱当成一件背包物品发了，钱一分没涨。）
                Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, reward_id, 1, null);
                if (!granted["ok"].AsBool())
                {
                    return new Godot.Collections.Dictionary
                    {
                        { "type", reward_type }, { "id", reward_id },
                        { "error", granted["error"].AsString() },
                    };
                }
                if (granted["kind"].AsString() == "money")
                {
                    int money = granted["qty"].AsInt32();
                    return new Godot.Collections.Dictionary
                    {
                        { "type", "money" }, { "id", reward_id }, { "qty", money },
                        { "text", $"得到 {money} 文钱" },
                    };
                }
                return new Godot.Collections.Dictionary
                {
                    { "type", reward_type }, { "id", reward_id }, { "qty", granted["qty"].AsInt32() },
                    { "text", $"得到 {item_name(reward_id)}" },
                };
            }
            case "equip":
            {
                GodotObject inventory = state.Get("inventory").AsGodotObject();
                string instance_id = inventory
                    .Call("add_equipment", db, reward_id, new Godot.Collections.Array())
                    .AsString();
                if (instance_id.Length == 0)
                {
                    GD.PushError($"[EventCheckService] event_check 的 reward_type=equip 指向的装备不存在：{reward_id}");
                    return new Godot.Collections.Dictionary
                    {
                        { "type", reward_type }, { "id", reward_id },
                        { "error", "奖励的装备不存在（数据错，已记进日志）" },
                    };
                }
                return new Godot.Collections.Dictionary
                {
                    { "type", reward_type }, { "id", reward_id }, { "instance_id", instance_id },
                    { "text", $"得到 {item_name(reward_id)}" },
                };
            }
            case "room":
            {
                // 房间奖励有两种落点：小地图里换房间、大地图上揭开地标
                Resource node = db.get_row("map_region", reward_id);
                if (node != null)
                {
                    state.Call("reveal_node", reward_id);
                    string node_name = node.Get("name_cn").AsString();
                    return new Godot.Collections.Dictionary
                    {
                        { "type", "region" }, { "id", reward_id },
                        { "text", $"在地图上标出了 {node_name}" },
                    };
                }
                Resource room = db.get_row("dungeon_room", reward_id);
                if (room != null)
                {
                    // 房间奖励顺带把这张图对应的地标也标出来（例：追足迹 → 塌陷山洞出现在地图上）
                    string landmark = _node_for_scene(room.Get("scene_id").AsString());
                    if (landmark.Length != 0)
                    {
                        state.Call("reveal_node", landmark);
                    }
                    string landmark_text = landmark.Length != 0
                        ? $"，地图上也标出了 {_node_name(landmark)}"
                        : "";
                    string room_name = room.Get("room_name").AsString();
                    return new Godot.Collections.Dictionary
                    {
                        { "type", "room" }, { "id", reward_id },
                        { "landmark", landmark },
                        {
                            "text",
                            $"找到通往「{room_name}」的路{landmark_text}"
                        },
                    };
                }
                GD.PushError($"[EventCheckService] event_check 的 reward_type=room 指向的房间／地标不存在：{reward_id}");
                return new Godot.Collections.Dictionary
                {
                    { "type", reward_type }, { "id", reward_id },
                    { "error", "奖励的房间或地标不存在（数据错，已记进日志）" },
                };
            }
            case "event":
            {
                state.Call("set_flag", reward_id, true);
                // 旗标名（event_xxx）是内部记账，玩家看判定那句就够
                return new Godot.Collections.Dictionary
                {
                    { "type", "event" }, { "id", reward_id }, { "text", "记下了这段经过" },
                };
            }
            case "boss":
            {
                // 语义与 `hidden_trigger.reward_type=boss` 完全一致：本服务是纯逻辑、不碰场景，
                // 所以只把「开战请求」交出去，由场景层 `resolve_event` 发起。
                Resource enemy = db.get_row("enemy_base", reward_id);
                if (enemy == null)
                {
                    GD.PushError($"[EventCheckService] event_check 的 reward_type=boss 指向的敌人不存在：{reward_id}");
                    return new Godot.Collections.Dictionary
                    {
                        { "type", "boss" }, { "id", reward_id },
                        { "error", "奖励指向的敌人不存在（数据错，已记进日志）" },
                    };
                }
                string enemy_name = enemy.Get("name_cn").AsString();
                return new Godot.Collections.Dictionary
                {
                    { "type", "boss" }, { "id", reward_id }, { "start_battle", true },
                    { "enemy_name", enemy_name },
                    { "text", $"{enemy_name} 现身！" },
                };
            }
            case "none":
                // 设计 06：`none` 表示「判定本身就是收益」（例如避开了陷阱）。不留提示文案——
                // 原来那半句「（这一步的效果由场景自己处理）」是写给开发看的，却被拼进状态栏给玩家看了。
                return new Godot.Collections.Dictionary
                {
                    { "type", "none" }, { "id", "" }, { "text", "" },
                };
            default:
                // 走到这儿说明 reward_type 不在设计允许的取值里：校验器会红，运行时也不能装没事
                GD.PushError($"[EventCheckService] event_check 的 reward_type={reward_type}（{reward_id}）不是设计允许的取值");
                return new Godot.Collections.Dictionary
                {
                    { "type", reward_type }, { "id", reward_id },
                    { "error", "奖励类型不是设计允许的取值（数据错，已记进日志）" },
                };
        }
    }

    /// <summary>哪张大地图节点通往这个小地图（`map_region.enter_scene`）</summary>
    private string _node_for_scene(string scene_id)
    {
        if (scene_id.Length == 0)
        {
            return "";
        }
        foreach (Variant row_variant in db.rows("map_region"))
        {
            if (row_variant.AsGodotObject() is Resource row
                && row.Get("enter_scene").AsString() == scene_id)
            {
                return row.Get("node_id").AsString();
            }
        }
        return "";
    }

    private string _node_name(string node_id)
    {
        Resource row = db.get_row("map_region", node_id);
        return row != null ? row.Get("name_cn").AsString() : node_id;
    }

    public string item_name(string item_id)
    {
        Resource equip = db.get_row("equip_base", item_id);
        if (equip != null)
        {
            return equip.Get("name_cn").AsString();
        }
        Resource item = db.get_row("item_base", item_id);
        if (item != null)
        {
            return item.Get("name_cn").AsString();
        }
        // 两张表都查不到 = 数据错：id 只进日志（上面那句 push_error），玩家看一句人话
        GD.PushError($"[EventCheckService] 奖励 id 在 item_base／equip_base 里都查不到：{item_id}");
        return "那件东西（数据错，已记进日志）";
    }

    /// <summary>`skill:qimen` → 「奇门 3」；`attr:agi` → 「敏 9」</summary>
    public string source_label(string source)
    {
        string[] parts = source.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return source;
        }
        if (parts[0] == "skill")
        {
            Resource row = db.get_row("event_skill_def", parts[1]);
            return row != null ? row.Get("name_cn").AsString() : parts[1];
        }
        Resource attr = db.get_row("attribute_def", parts[1]);
        return attr != null ? attr.Get("name_cn").AsString() : parts[1];
    }
}
