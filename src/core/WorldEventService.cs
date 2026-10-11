using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 大地图随机事件（C# 版，对位原 `src/core/world_event_service.gd`；设计 19 §四）。
///
/// 明雷默认关之后大地图太空，所以按**区域权重**抽非战斗事件：
/// 货商／门派弟子／江湖人／官府巡查／天象奇遇／野物。三条纪律写在 19 号里：
///   ① **以非战斗为主**（战斗只保留「切磋」，而且玩家主动选）；
///   ② **权重按区域给**（每个区域的池子不同）；
///   ③ **事件要给东西或信息**——只是看一眼就走的随机事件等于空气。
///
/// 0.25.1 又加了两处提示：HUD 一行**模糊**的（`prompt_text_cn`，例「远处有车马声」）
/// 与地图上的**临时淡色光点**。三条规则：提示只在事件存活期间出现、同一时刻最多一条、
/// 走开或超时标记消失——**随机事件是「路过遇上」，不是「待领的任务」**。
///
/// 效果这一半：0.28.0（Q64）把八行的 `effect_id` 填齐之后接通，五类各接一处——
///   `gift`／`hint`／`check` 在**这里**就能结算（纯逻辑：发物品、记线索、走判定）；
///   `trade`／`spar` 要挂在**当前场景**上（开货架界面、切战斗场景），服务层只交一个
///   `scene_action` 请求出去，由场景层动手——服务层碰不到节点，这是分层的既有口径。
/// `effect_id` 空着时仍然**如实说「没配」**，不假装发了什么（19 §四第 3 条）。
/// </summary>
public partial class WorldEventService : RefCounted
{

    /// <summary>事件存活时间（秒）：超时就消失。</summary>
    public const double LIFETIME = 45.0;

    /// <summary>玩家走开多远就消失（像素；一格 32）。</summary>
    public const double DESPAWN_DISTANCE = 320.0;

    /// <summary>触发半径（走到光点上）。</summary>
    public const double TRIGGER_DISTANCE = 24.0;

    /// <summary>生成距离：在玩家附近这个半径内挑一点（不要贴脸出现）。</summary>
    public const double SPAWN_MIN = 96.0;

    public const double SPAWN_MAX = 224.0;

    /// <summary>两次事件之间的最短间隔（秒）——刚遇过一场别马上再来一场。</summary>
    public const double COOLDOWN = 20.0;

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的 `const`（施工单坑⑩）：控制器现在读
    // `WorldEventServiceScript.LIFETIME` 这类名字，改成同名小写静态方法；
    // P5 清零后再把它连着调用方一起删掉。

    public static double lifetime() => LIFETIME;
    public static double despawn_distance() => DESPAWN_DISTANCE;
    public static double trigger_distance() => TRIGGER_DISTANCE;
    public static double spawn_min() => SPAWN_MIN;
    public static double spawn_max() => SPAWN_MAX;
    public static double cooldown() => COOLDOWN;

    // ------------------------------------------------------------------ 池子

    /// <summary>这个区域能出哪些事件（`region_tags` 用 ";" 分隔）。</summary>
    public static Godot.Collections.Array rows_for_region(TableDb db, string region_id)
    {
        var out_rows = new Godot.Collections.Array();
        if (db == null || region_id.Length == 0)
        {
            return out_rows;
        }
        foreach (Variant entry in db.rows("world_event"))
        {
            if (entry.AsGodotObject() is not WorldEventRow row)
            {
                continue;
            }
            foreach (string tag in row.region_tags.Split(
                         ";", System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (tag.StripEdges() == region_id)
                {
                    out_rows.Add(row);
                    break;
                }
            }
        }
        return out_rows;
    }

    /// <summary>一次性事件的记账旗标（写在旗标里，不用新开存档字段）。</summary>
    public static string done_flag(string event_id) => $"world_event_done_{event_id}";

    /// <summary>
    /// 「给线索」类效果记的旗标（`effect_id` 指向的是 `hidden_trigger.trigger_id`）。
    /// 线索本（`ClueService`）认得它，会在那一条后面标「已从传闻中听说」——
    /// 线索本身没藏着（03 要求线索必须找得到），所以这里不是「解锁」，
    /// 是「你确实听人说过」的凭据。
    /// </summary>
    public static string hint_flag(string trigger_id) => $"clue_hint_{trigger_id}";

    public static bool is_done(GodotObject state, string event_id) =>
        state != null && state.Call("has_flag", done_flag(event_id)).AsBool();

    /// <summary>现在能出的事件：条件旗标满足 ＋（可重复 或 还没出过）。</summary>
    public static Godot.Collections.Array eligible(TableDb db, GodotObject state, string region_id)
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant entry in rows_for_region(db, region_id))
        {
            if (entry.AsGodotObject() is not WorldEventRow row)
            {
                continue;
            }
            if (row.repeatable == 0 && is_done(state, row.event_id))
            {
                continue;
            }
            if (!ConditionOk(state, row.condition))
            {
                continue;
            }
            out_rows.Add(row);
        }
        return out_rows;
    }

    /// <summary>
    /// 按权重抽一个；池子空返回 `null`。
    /// `roll` 是 0~1 的随机数（用例传固定值好复现）。
    /// </summary>
    public static Resource pick(TableDb db, GodotObject state, string region_id, double roll)
    {
        Godot.Collections.Array pool = eligible(db, state, region_id);
        if (pool.Count == 0)
        {
            return null;
        }
        int total = 0;
        foreach (Variant entry in pool)
        {
            total += Mathf.Max(0, ((WorldEventRow)entry.AsGodotObject()).weight);
        }
        if (total <= 0)
        {
            return pool[0].AsGodotObject() as Resource;
        }
        double target = Mathf.Clamp(roll, 0.0, 0.999999) * total;
        double acc = 0.0;
        foreach (Variant entry in pool)
        {
            var row = (WorldEventRow)entry.AsGodotObject();
            acc += Mathf.Max(0, row.weight);
            if (target < acc)
            {
                return row;
            }
        }
        return pool[pool.Count - 1].AsGodotObject() as Resource;
    }

    // ------------------------------------------------------------------ 触发

    /// <summary>
    /// 触发一个事件：按 `effect_kind` 分发效果，一次性事件记账。
    ///
    /// 返回 `{ok, event_id, text, prompt, effect, error}`——`effect` 里带
    /// `{kind, id, applied, note, scene_action, text}`：
    ///   · **`effect_id` 空着时 `applied=false` 且 note 写明原因**；
    ///   · `scene_action` 非空 = 这一半要场景层动手（`open_shop`／`start_battle`）；
    ///   · `text` 是**玩家可见**的结算文案（`check` 类才有）。
    ///
    /// `rng` 传 `null` 就用随机种子（正常游戏）；用例传固定种子的 `RngService` 拿确定结果。
    /// </summary>
    public static Godot.Collections.Dictionary trigger(
        TableDb db, GodotObject state, string event_id, RngService rng)
    {
        var row = (db == null ? null : db.get_row("world_event", event_id)) as WorldEventRow;
        if (row == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "event_id", event_id },
                { "text", "" },
                { "prompt", "" },
                { "effect", new Godot.Collections.Dictionary() },
                { "error", "没有这个事件" },
            };
        }
        Godot.Collections.Dictionary effect = apply_effect(db, state, row, rng);
        if (row.repeatable == 0)
        {
            state.Call("set_flag", done_flag(event_id), true);
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "event_id", event_id },
            { "text", row.text_cn },
            { "prompt", row.prompt_text_cn },
            { "effect", effect },
            { "error", "" },
        };
    }

    /// <summary>
    /// 效果分发。**`effect_id` 没配时如实说「没配」，不假装发了什么**（19 §四第 3 条）。
    ///
    /// 名字由原来的 `_apply_effect` 改成 `apply_effect`：C# 的 `private` 不给 GDScript 看见，
    /// 而用例要能**直接传一行改过的世界事件行**进来（`test_world_event` 的 `_check_unwired`），
    /// 所以这里开成 public；它在游戏路径里也被 `trigger()` 调用，不是「只给用例的接口」。
    /// </summary>
    public static Godot.Collections.Dictionary apply_effect(
        TableDb db, GodotObject state, Resource row_resource, RngService rng)
    {
        var row = (WorldEventRow)row_resource;
        string kind = row.effect_kind;
        string target = row.effect_id;
        if (target.Length == 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", "" },
                { "applied", false },
                { "scene_action", "" },
                { "text", "" },
                // 玩家可见（大地图状态栏）：不许出现表名／列名，也不许出现 Q 编号（AGENTS 硬规矩）
                { "note", "这条事件的效果还没配（等设计给指向）" },
            };
        }
        if (kind == "gift")
        {
            Godot.Collections.Dictionary granted = BattleReward.grant_item(db, state, target, 1, null);
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target },
                { "applied", granted.ContainsKey("ok") && granted["ok"].AsBool() },
                { "scene_action", "" },
                { "text", "" },
                { "note", "" },
                { "granted", granted },
            };
        }
        if (kind == "hint")
        {
            // 「给信息」也算给东西（19 §四第 3 条）：把这条线索记进线索本
            if (state != null)
            {
                state.Call("set_flag", hint_flag(target), true);
            }
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target },
                { "applied", true },
                { "scene_action", "" },
                { "text", "" },
                { "note", "线索已记进线索本" },
            };
        }
        if (kind == "check")
        {
            // 不要求玩家走到地图位点：官道关卡与随机事件共用同一行判定（Q64）
            EventCheckService checker = EventCheckService.create(db, state, rng);
            Godot.Collections.Dictionary result = checker.resolve(target);
            Godot.Collections.Dictionary reward = result.ContainsKey("reward")
                ? result["reward"].AsGodotDictionary()
                : new Godot.Collections.Dictionary();
            string boss_id = "";
            string scene_action = "";
            if (reward.ContainsKey("start_battle") && reward["start_battle"].AsBool())
            {
                boss_id = reward.ContainsKey("id") ? reward["id"].AsString() : "";
                scene_action = "start_battle";
            }
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target },
                { "applied", result.ContainsKey("ok") && result["ok"].AsBool() },
                { "scene_action", scene_action },
                { "text", result.ContainsKey("text") ? result["text"].AsString() : "" },
                { "note", "" },
                { "success", result.ContainsKey("success") && result["success"].AsBool() },
                { "battle_enemy_id", boss_id },
            };
        }
        if (kind == "trade")
        {
            // 开货架要挂在当前场景上（界面是节点），服务层只交请求
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target },
                { "applied", false },
                { "scene_action", "open_shop" },
                { "text", "" },
                { "note", "" },
            };
        }
        if (kind == "spar")
        {
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "id", target },
                { "applied", false },
                { "scene_action", "start_battle" },
                { "text", "" },
                { "note", "" },
            };
        }
        return new Godot.Collections.Dictionary
        {
            { "kind", kind },
            { "id", target },
            { "applied", false },
            { "scene_action", "" },
            { "text", "" },
            { "note", $"没见过的效果类型：{kind}" },
        };
    }

    /// <summary>
    /// 「这一行现在能不能出」的条件判定：**留空 = 无条件**（与 `DialogueService.condition_ok` 同一口径）。
    ///
    /// 别把空串直接塞给 `GuideService.condition_met`——那条条件语言把空串当「写坏了」返回 false，
    /// 而 `world_event.condition` 这一列发行数据**全是空的**（＝没有前置）。于是 `eligible()`
    /// 会静悄悄永远返回空池，大地图上一场随机事件都出不来（19 §四整节形同虚设；
    /// C# 侧的新用例把这条钉住了：`roll=0` 必须抽得到巡查）。
    /// </summary>
    private static bool ConditionOk(GodotObject state, string condition)
    {
        string trimmed = condition == null ? "" : condition.StripEdges();
        return trimmed.Length == 0 || ConditionMet(state, trimmed);
    }

    /// <summary>条件语言与引导共用一份（`GuideService.condition_met`），别另造一套。</summary>
    private static bool ConditionMet(GodotObject state, string condition) =>
        GuideService.condition_met(state, condition);
}
