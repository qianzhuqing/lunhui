using System.Collections.Generic;
using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 校验器的「剧情／对话／NPC／引导／创建角色／增益与套装／难度／地图」那一族
/// （C# 版，对位原 `src/core/table_validator.gd` 的同名函数）。
/// 纯 C# 静态类，只被 `TableValidator.validate()` 调用。
/// </summary>
internal static class TableValidatorWorld
{
    /// <summary>
    /// `rule:` 类的效果目标**代码真的认得的**那批（设计 12：新增一个目标 = 改一处代码 + 加一行表）。
    /// 这里先放着：天赋的运行期（`TalentService`）落地后这份名单要**搬过去当唯一出处**。
    /// </summary>
    private static readonly string[] KNOWN_TALENT_RULES =
    {
        "bonus_skill", "mastery_gain", "learn_req_reduce", "skill_damage", "defeat_heal_bonus",
        "first_round_priority", "event_check_bonus", "event_check_bonus_wenxue",
        "event_check_bonus_yishu", "event_check_difficulty", "start_money",
        "shop_buy_price", "codex_bonus_multiplier",
    };

    /// <summary>
    /// 成长常数里**有语义下限**的几个：下限值不是我编的，是**代码里的守卫**——
    /// 写了更小的值会被静默替换成 1.0／1／0，也就是「表里写的数根本没用上」（155／157／165 同一家族）。
    /// </summary>
    private static readonly Dictionary<string, (double Floor, string Hint)> GROWTH_CONST_FLOORS = new()
    {
        ["active_slot_lv_div"] = (1.0, "代码里是 maxf(值, 1.0)：小于 1 会被当成 1.0"),
        ["active_slot_attr_div"] = (1.0, "同上（悟性除数）"),
        ["passive_cap_lv_div"] = (1.0, "同上（内功容量等级除数）"),
        ["passive_cap_attr_div"] = (1.0, "同上（根骨除数）"),
        ["cultivate_cost_growth"] = (0.0001, "它是 pow() 的底数：负数会算出负费用（打坐给你钱）"),
        ["codex_step"] = (1.0, "代码里是 maxi(1, 值)"),
        ["codex_cap"] = (0.0, "代码里是 maxi(0, 值)"),
        ["mastery_max"] = (1.0, "代码里是 clampi(等级, 1, maxi(1, 值))"),
    };

    /// <summary>
    /// 难度阶梯：**表里的行顺序就是难度递增顺序**（普通 → 困难 → 绝境）。
    /// 以前只校验「> 0」，于是把困难档的攻击倍率写成 0.9（比普通还弱）**没有任何反应**。
    /// </summary>
    private static readonly string[] DIFFICULTY_LADDER_COLUMNS =
    {
        "enemy_hp_mul", "enemy_atk_mul", "enemy_def_mul", "exp_mul", "money_mul",
    };

    /// <summary>
    /// 大地图探索口径（0.32.0）：**默认公开的只有城镇与驿站**；兴趣点不给图标
    /// （唯一例外是「持有类解锁」的那一个，问 `WorldMapService.held_item_id`——唯一出处）。
    /// </summary>
    internal static void CheckMapRegionRules(TableDb db, List<string> errors)
    {
        string[] default_public_types = { "town", "fast_travel" };
        foreach (Resource row in TableValidator.Rows(db, "map_region"))
        {
            string node_id = TableValidator.Str(row, "node_id");
            string node_type = TableValidator.Str(row, "node_type");
            // ① 默认公开只能给城镇／驿站
            if (TableValidator.Flag(row, "reveal_on_map") && System.Array.IndexOf(default_public_types, node_type) < 0)
            {
                errors.Add(
                    $"map_region[{node_id}].reveal_on_map=1，但 node_type={node_type}——"
                    + "设计 0.32.0 只许城镇（town）与驿站（fast_travel）开局公开，"
                    + "其余要玩家自己走到跟前才揭开"
                );
            }

            // ② 兴趣点不给图标（例外：藏宝图那类「持有类解锁」的兴趣点）
            if (node_type != "poi")
            {
                continue;
            }

            if (TableValidator.Trimmed(row, "icon").Length == 0)
            {
                continue;
            }

            string held = WorldMapService.held_item_id(
                db, TableValidator.Str(row, "unlock_condition")
            );
            if (held.Length != 0)
            {
                continue;
            }

            errors.Add(
                $"map_region[{node_id}].icon='{TableValidator.Str(row, "icon")}'，但它是兴趣点——"
                + "设计 16 §3.5：兴趣点靠地形认（山洞＝山体上的洞口、荒村＝焦黑屋舍群、渡口＝水岸），不给图标；"
                + "唯一例外是「持有类解锁」的兴趣点（藏宝图上有位置）"
            );
        }
    }

    /// <summary>
    /// 剧情节点与引导链（0.22.0，设计 18）：补的是设计侧那七个断点里**能机械核的四个**
    /// （story 武学有没有落地节点／章节结束节点有没有置完成条件／引导链条件形状／每一步的判定口径）。
    /// </summary>
    internal static void CheckStoryAndGuides(TableDb db, List<string> errors)
    {
        var nodes = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "story_node"))
        {
            nodes.Add(TableValidator.Str(row, "node_id"));
            // `place_id`：小地图或大地图节点，二选一命中即可（留空 = 地点不限）
            string place = TableValidator.Str(row, "place_id");
            if (place.Length != 0)
            {
                var in_local = db.get_row("map_local", place) != null;
                var in_region = db.get_row("map_region", place) != null;
                if (!in_local && !in_region)
                {
                    errors.Add(
                        $"story_node[{TableValidator.Str(row, "node_id")}].place_id={place} "
                        + "既不是小地图场景也不是大地图节点"
                    );
                }
            }

            // `trigger_condition`：条件语言目前两种写法——`flag_*` 与 `origin:<char_id>`
            string trigger = TableValidator.Str(row, "trigger_condition");
            if (trigger.Length != 0 && !trigger.StartsWith("flag_") && !trigger.StartsWith("origin:"))
            {
                errors.Add(
                    $"story_node[{TableValidator.Str(row, "node_id")}].trigger_condition={trigger} "
                    + "既不是 flag_* 也不是 origin:<char_id>"
                );
            }

            if (trigger.StartsWith("origin:"))
            {
                string origin_id = trigger.Substring("origin:".Length);
                if (db.get_row("character_base", origin_id) == null)
                {
                    errors.Add(
                        $"story_node[{TableValidator.Str(row, "node_id")}] 的出身条件 origin:{origin_id} "
                        + "不是 character_base 里的角色"
                    );
                }
            }

            foreach (string skill_id in TableValidator.Str(row, "grant_skill_ids")
                         .Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (db.get_row("skill_base", skill_id) == null)
                {
                    errors.Add($"story_node[{TableValidator.RowId(row)}] 要发的武学 {skill_id} 不在 skill_base 里");
                }
            }

            // `kind=choice`（设计 20 §八）：它存在的意义就是发永久增益——一条都不填等于配了个空节点
            if (TableValidator.Str(row, "kind") == "choice"
                && row.Call("bonus_contributions").AsGodotArray().Count == 0)
            {
                errors.Add(
                    $"story_node[{TableValidator.Str(row, "node_id")}] 是 kind=choice，"
                    + "但四列增益全空——玩家选完什么也得不到"
                );
            }
        }

        // ①b `source_type=origin`（设计 21 §九）：与 `story` 同一条通道，也要指到真实节点
        foreach (Resource row in TableValidator.Rows(db, "skill_base"))
        {
            if (TableValidator.Str(row, "source_type") != "origin")
            {
                continue;
            }

            string origin_source = TableValidator.Str(row, "source_id");
            if (!nodes.Contains(origin_source))
            {
                errors.Add(
                    $"skill_base[{TableValidator.RowId(row)}] 标着 source_type=origin，但 "
                    + $"source_id={origin_source} 不在 story_node 里——这条本命机遇玩家永远拿不到"
                );
            }
        }

        // ①c 观察点（设计 20 §3.2）：**小地图与大地图二选一**，且必须落在一个真地方
        foreach (Resource row in TableValidator.Rows(db, "flavor_point"))
        {
            string point_scene = TableValidator.Str(row, "scene_id");
            string point_region = TableValidator.Str(row, "region_id");
            if (point_scene.Length == 0 && point_region.Length == 0)
            {
                errors.Add(
                    $"flavor_point[{TableValidator.Str(row, "point_id")}] 既没有 scene_id 也没有 region_id"
                    + "——这一句碎句玩家永远看不到"
                );
            }
            else if (point_scene.Length != 0 && point_region.Length != 0)
            {
                errors.Add(
                    $"flavor_point[{TableValidator.Str(row, "point_id")}] 同时填了 scene_id 与 region_id"
                    + "——两边都会收它，位点只该有一个"
                );
            }

            if (TableValidator.Str(row, "text_cn").Length == 0)
            {
                errors.Add(
                    $"flavor_point[{TableValidator.Str(row, "point_id")}] 没有 text_cn：观察点就是那一句话"
                );
            }
        }

        // ① story 武学必须有落地节点（设计侧新加的同一道门限）
        foreach (Resource row in TableValidator.Rows(db, "skill_base"))
        {
            if (TableValidator.Str(row, "source_type") != "story")
            {
                continue;
            }

            string source_id = TableValidator.Str(row, "source_id");
            if (!nodes.Contains(source_id))
            {
                errors.Add(
                    $"skill_base[{TableValidator.RowId(row)}] 标着 source_type=story，"
                    + $"但 source_id={source_id} 不在 story_node 里——这部武学玩家永远拿不到"
                );
            }
        }

        // ② 章节完成条件要有节点去置它
        var granted_flags = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "story_node"))
        {
            if (TableValidator.Str(row, "kind") == "chapter_end")
            {
                granted_flags.Add(TableValidator.Str(row, "chapter_id"));
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "chapter_def"))
        {
            string chapter_id = TableValidator.Str(row, "chapter_id");
            string complete = TableValidator.Str(row, "complete_condition");
            // 「最后一章」允许没有完成条件：只有**声明了下一章**的章节才必须能结束。
            if (complete.Length == 0)
            {
                if (TableValidator.Str(row, "next_chapter_id").Length != 0)
                {
                    errors.Add(
                        $"chapter_def[{chapter_id}] 声明了下一章，却没有 complete_condition：这一章永远不会结束"
                    );
                }

                continue;
            }

            if (granted_flags.Count == 0)
            {
                errors.Add(
                    $"chapter_def[{chapter_id}] 要靠 {complete} 完成，但没有任何 chapter_end 剧情节点"
                );
            }
        }

        // ③④ 引导链：条件形状 + 判定口径
        foreach (Resource row in TableValidator.Rows(db, "guide_step"))
        {
            string condition = TableValidator.Str(row, "condition");
            if (condition != "start" && !condition.StartsWith("flag_"))
            {
                errors.Add(
                    $"guide_step[{TableValidator.RowId(row)}].condition={condition} "
                    + "既不是 start 也不是 flag_*（拼错就会静默断链）"
                );
            }

            if (TableValidator.Str(row, "condition_spec").Length == 0)
            {
                errors.Add(
                    $"guide_step[{TableValidator.RowId(row)}] 缺 condition_spec："
                    + "程序没法知道这一步什么时候算满足"
                );
            }
        }
    }

    /// <summary>
    /// 创建角色那两张表（`origin_def`／`talent_def`＋`talent_effect`，设计 12／13）：
    /// 能机械核的三条都在这里；剩下两条（`rule:` 目标必须真实现、「创建没走完不写存档」）属于运行期。
    /// </summary>
    internal static void CheckCreationTables(TableDb db, List<string> errors)
    {
        List<Resource> origins = TableValidator.Rows(db, "origin_def");
        if (origins.Count == 0)
        {
            errors.Add("origin_def 一行都没有：创建角色「使用模板」那条路会空着");
        }

        var seen_chars = new HashSet<string>();
        var seen_order = new HashSet<int>();
        foreach (Resource row in origins)
        {
            string char_id = TableValidator.Str(row, "char_id");
            if (!seen_chars.Add(char_id))
            {
                errors.Add($"origin_def 有两张卡指向同一个模板 {char_id}（出身卡必须一一对应）");
            }

            int order = TableValidator.Int(row, "sort_order");
            if (!seen_order.Add(order))
            {
                errors.Add($"origin_def 的 sort_order={order} 重复（两行的展示顺序会不稳定）");
            }

            // 默认名（设计 21 §七 第 5 条）：**每一张卡都要有真名**——空着就会退回卡标题
            if (TableValidator.Trimmed(row, "default_name_cn").Length == 0)
            {
                errors.Add(
                    $"origin_def[{TableValidator.Str(row, "origin_id")}] 没有 default_name_cn："
                    + $"默认名会退回卡标题（{TableValidator.Str(row, "name_cn")}）——"
                    + "给个真名（设计 21 §七 第 5 条）"
                );
            }
        }

        var talents = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "talent_def"))
        {
            talents.Add(TableValidator.Str(row, "talent_id"));
        }

        var effects_by_talent = new Dictionary<string, List<Resource>>();
        foreach (Resource row in TableValidator.Rows(db, "talent_effect"))
        {
            string talent_id = TableValidator.Str(row, "talent_id");
            string target = TableValidator.Str(row, "target");
            string[] parts = target.Split(':', 2);
            if (parts.Length != 2 || parts[1].Length == 0)
            {
                errors.Add(
                    $"talent_effect[{TableValidator.RowId(row)}].target={target} 不是 `<前缀>:<目标>` 的写法"
                );
                continue;
            }

            string prefix = parts[0];
            string target_id = parts[1];
            switch (prefix)
            {
                case "attr":
                    if (db.get_row("attribute_def", target_id) == null)
                    {
                        errors.Add(
                            $"talent_effect[{TableValidator.RowId(row)}] 的属性目标 {target_id} 不在 attribute_def 里"
                        );
                    }

                    break;
                case "stat":
                    if (db.get_row("stat_def", target_id) == null)
                    {
                        errors.Add(
                            $"talent_effect[{TableValidator.RowId(row)}] 的派生数值目标 {target_id} 不在 stat_def 里"
                        );
                    }

                    break;
                case "rule":
                    if (System.Array.IndexOf(KNOWN_TALENT_RULES, target_id) < 0)
                    {
                        errors.Add(
                            $"talent_effect[{TableValidator.RowId(row)}] 的规则目标 {target_id} 代码里没有实现——"
                            + "新增一个 rule: 目标要同时改代码（TalentService）与加一行表"
                        );
                    }

                    break;
                default:
                    errors.Add(
                        $"talent_effect[{TableValidator.RowId(row)}] 的前缀 {prefix} 不是 attr:/stat:/rule: 三者之一"
                    );
                    break;
            }

            if (!effects_by_talent.TryGetValue(talent_id, out List<Resource> effects))
            {
                effects = new List<Resource>();
                effects_by_talent[talent_id] = effects;
            }

            effects.Add(row);
        }

        var budget = 0.0;
        foreach (Resource row in TableValidator.Rows(db, "growth_const"))
        {
            if (TableValidator.Str(row, "const_id") == "talent_points")
            {
                budget = TableValidator.Num(row, "value");
            }
        }

        if (budget <= 0.0)
        {
            errors.Add("growth_const 缺少 talent_points（没有它创建界面的天赋预算没有出处）");
        }

        // 设计 12 第一条纪律：**动七维的天赋总和必须为零**。这条先只报警告、不当错误：
        // 0.16.0 交付的数据本身就和这句话对不上（见 待策划确认.md Q58／决策 270）。
        foreach (string talent_id in talents)
        {
            Resource talent = db.get_row("talent_def", talent_id);
            if (TableValidator.Num(talent, "cost") > budget)
            {
                errors.Add(
                    $"talent_def[{talent_id}].cost={TableValidator.Num(talent, "cost")} "
                    + $"超过天赋总点数 {budget}（这个天赋永远买不起）"
                );
            }

            if (!effects_by_talent.TryGetValue(talent_id, out List<Resource> effects))
            {
                effects = new List<Resource>();
            }

            if (effects.Count == 0)
            {
                errors.Add($"talent_def[{talent_id}] 一条 talent_effect 都没有（选了没有任何效果）");
            }

            var attr_sum = 0.0;
            var attr_detail = new List<string>();
            foreach (Resource effect in effects)
            {
                string effect_target = TableValidator.Str(effect, "target");
                if (effect_target.StartsWith("attr:"))
                {
                    attr_sum += TableValidator.Num(effect, "value");
                    attr_detail.Add($"{effect_target.Substring(5)} {TableValidator.Num(effect, "value")}");
                }
            }

            if (System.Math.Abs(attr_sum) > 0.001)
            {
                string sign_str = attr_sum >= 0.0 ? "+" : "-";
                GD.PushWarning(
                    $"[TableValidator] talent_def[{talent_id}] 动七维（{string.Join("／", attr_detail)}）"
                    + $"净 {sign_str}{System.Math.Abs(attr_sum):0.0}，不是 0——设计 12 说偏移类必须自平，"
                    + "但 0.16.0 的发行数据就是这样（`待策划确认.md` Q58 已问设计）"
                );
            }
        }
    }

    /// <summary>
    /// 引导链与招募表（0.10.0 新增）的自洽检查：恰好一个初始成员、join_scene 落在真地方、
    /// 引导 sort_order 唯一且从 1 起、第一步是 start、界面文案不许空。
    /// </summary>
    internal static void CheckGuideAndRecruit(TableDb db, List<string> errors)
    {
        var initial = 0;
        foreach (Resource row in TableValidator.Rows(db, "recruit_def"))
        {
            if (TableValidator.Flag(row, "is_initial"))
            {
                initial += 1;
                continue;
            }

            if (TableValidator.Trimmed(row, "join_condition").Length == 0)
            {
                errors.Add(
                    $"recruit_def[{TableValidator.Str(row, "char_id")}] 不是初始成员却没有 join_condition"
                    + "（永远加不进来）"
                );
            }

            string join_scene = TableValidator.Trimmed(row, "join_scene");
            if (join_scene.Length == 0)
            {
                errors.Add($"recruit_def[{TableValidator.Str(row, "char_id")}] 没写 join_scene（不知道在哪加入）");
            }
            // `join_scene` 既可能是小地图，也可能是**大地图节点**（林铁山在落雁坡相遇），两张表都认
            else if (db.get_row("map_local", join_scene) == null && db.get_row("map_region", join_scene) == null)
            {
                errors.Add(
                    $"recruit_def[{TableValidator.Str(row, "char_id")}].join_scene = '{join_scene}' "
                    + "既不在 map_local 也不在 map_region 里"
                );
            }
        }

        if (initial != 1)
        {
            errors.Add($"recruit_def 里 is_initial=1 的行有 {initial} 条（必须恰好一条：开局只有他）");
        }

        var orders = new HashSet<int>();
        var first_condition = "";
        var first_order = 1 << 30;
        foreach (Resource row in TableValidator.Rows(db, "guide_step"))
        {
            int order = TableValidator.Int(row, "sort_order");
            if (!orders.Add(order))
            {
                errors.Add($"guide_step 的 sort_order={order} 被两条步骤共用（先后顺序会不确定）");
            }

            string condition = TableValidator.Trimmed(row, "condition");
            if (condition.Length == 0)
            {
                errors.Add(
                    $"guide_step[{TableValidator.Str(row, "step_id")}] 的 condition 是空的（这一步永远不满足）"
                );
            }

            if (order < first_order)
            {
                first_order = order;
                first_condition = condition;
            }
        }

        if (orders.Count != 0 && !orders.Contains(1))
        {
            errors.Add($"guide_step 的 sort_order 必须从 1 开始（现在是 [{TableValidator.Joined(OrderText(orders))}]）");
        }

        if (first_condition.Length != 0 && first_condition != "start")
        {
            errors.Add(
                $"guide_step 的第一步（sort_order={first_order}）的 condition 应当是 start（开局就该立起来）"
            );
        }

        // 界面文案：玩家看得见的那句话不能是空的；note 是给设计/开发看的，也要求写上
        foreach (Resource row in TableValidator.Rows(db, "ui_text"))
        {
            if (TableValidator.Trimmed(row, "text_cn").Length == 0)
            {
                errors.Add($"ui_text[{TableValidator.Str(row, "text_id")}] 的 text_cn 是空的（玩家会看到一句空话）");
            }

            if (TableValidator.Trimmed(row, "note").Length == 0)
            {
                errors.Add($"ui_text[{TableValidator.Str(row, "text_id")}] 没写 note（不知道什么时候用它）");
            }
        }
    }

    /// <summary>建筑：店铺必须有货架、服务型必须说清是哪个服务（0.10.0 加了木桩 `service` 之后才分开看）。</summary>
    internal static void CheckBuildingRows(TableDb db, List<string> errors)
    {
        // 允许空 + 按类型分别要求：**shop 要货架**（不然进店是空架子）、**service 要 service_id**
        foreach (Resource row in TableValidator.Rows(db, "building_def"))
        {
            string kind = TableValidator.Trimmed(row, "building_type");
            if (kind == "shop" && TableValidator.Trimmed(row, "stock_group").Length == 0)
            {
                errors.Add(
                    $"building_def[{TableValidator.Str(row, "building_id")}] 是店铺却没有 stock_group（进店会是空货架）"
                );
            }

            if (kind == "service" && TableValidator.Trimmed(row, "service_id").Length == 0)
            {
                errors.Add(
                    $"building_def[{TableValidator.Str(row, "building_id")}] 是 service 却没有 service_id"
                    + "（按 E 不知道该做什么）"
                );
            }
        }
    }

    /// <summary>
    /// 好感与委托的 `npc_id`：**NPC 或同伴**（设计 20 §十）——写错了没人能发现，所以两道网都查；
    /// 给的**东西**也要真的存在（写错一个字母＝玩家交完委托什么也拿不到、而且没有任何提示）。
    /// </summary>
    internal static void CheckNpcPeople(TableDb db, List<string> errors)
    {
        foreach (string table_name in new[] { "npc_favor", "npc_quest" })
        {
            foreach (Resource row in TableValidator.Rows(db, table_name))
            {
                string person_id = TableValidator.Str(row, "npc_id");
                if (person_id.Length == 0)
                {
                    errors.Add($"{table_name}[{TableValidator.RowId(row)}] 没有 npc_id");
                    continue;
                }

                if (db.get_row("npc_def", person_id) == null
                    && db.get_row("character_base", person_id) == null)
                {
                    errors.Add(
                        $"{table_name}[{TableValidator.RowId(row)}].npc_id={person_id} "
                        + "既不是 npc_def 里的人也不是 character_base 里的同伴"
                    );
                }
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "npc_offer"))
        {
            TableValidator.CheckPersonItem(
                db, "npc_offer", TableValidator.Str(row, "offer_id"),
                TableValidator.Str(row, "item_id"), errors
            );
        }

        foreach (Resource row in TableValidator.Rows(db, "npc_quest"))
        {
            foreach (string item_id in TableValidator.Str(row, "reward_item_ids")
                         .Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                TableValidator.CheckPersonItem(db, "npc_quest", TableValidator.Str(row, "quest_id"), item_id, errors);
            }
        }
    }

    /// <summary>
    /// 对话容器（设计 20 §十一）：说话人、跳转、效果三样都要有落点。
    ///   ① **说话人可以是被交互的 NPC，也可以是同伴**，所以两处任一命中即可；
    ///   ② 跳转目标必须是真实节点（写错一个字母 = 那句永远接不上）；
    ///   ③ 选项给的东西按道具／装备分流查表。
    /// </summary>
    internal static void CheckDialogue(TableDb db, List<string> errors)
    {
        string player_id = NpcService.PLAYER_ID;
        var node_ids = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "dialogue_node"))
        {
            node_ids.Add(TableValidator.Str(row, "node_id"));
            string speaker = TableValidator.Str(row, "speaker_id");
            if (speaker.Length == 0)
            {
                errors.Add(
                    $"dialogue_node[{TableValidator.Str(row, "node_id")}] 没有 speaker_id：这句话没人说"
                );
            }
            else if (speaker != player_id
                     && db.get_row("npc_def", speaker) == null
                     && db.get_row("character_base", speaker) == null)
            {
                errors.Add(
                    $"dialogue_node[{TableValidator.Str(row, "node_id")}].speaker_id={speaker} "
                    + $"既不是 npc_def 里的人／character_base 里的同伴，也不是 {player_id}（主角自己）"
                );
            }

            if (TableValidator.Str(row, "text_cn").Length == 0)
            {
                errors.Add(
                    $"dialogue_node[{TableValidator.Str(row, "node_id")}] 没有 text_cn：对话节点就是那一句话"
                );
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "dialogue_node"))
        {
            string next_id = TableValidator.Str(row, "next_node_id");
            if (next_id.Length != 0 && !node_ids.Contains(next_id))
            {
                errors.Add(
                    $"dialogue_node[{TableValidator.Str(row, "node_id")}].next_node_id={next_id} 不是对话节点"
                );
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "dialogue_option"))
        {
            string option_id = TableValidator.Str(row, "option_id");
            string node_id = TableValidator.Str(row, "node_id");
            if (!node_ids.Contains(node_id))
            {
                errors.Add($"dialogue_option[{option_id}].node_id={node_id} 不是对话节点");
            }

            string next_id = TableValidator.Str(row, "next_node_id");
            if (next_id.Length != 0 && !node_ids.Contains(next_id))
            {
                errors.Add($"dialogue_option[{option_id}].next_node_id={next_id} 不是对话节点");
            }

            if (TableValidator.Str(row, "text_cn").Length == 0)
            {
                errors.Add($"dialogue_option[{option_id}] 没有 text_cn：选项就是玩家要按的那句话");
            }

            // `set_flag` 可以写多个（分号隔开，Q83）：**每一段都要认得出来**——拆法调行类那一处
            foreach (string flag in DialogueOptionRow.parse_set_flags(TableValidator.Str(row, "set_flag")))
            {
                if (!flag.StartsWith("flag_") && !flag.StartsWith("heart_"))
                {
                    errors.Add(
                        $"dialogue_option[{option_id}].set_flag 里的 '{flag}' 既不是 flag_* 也不是心性"
                        + "（heart_*；多个旗标用分号隔开）"
                    );
                }
            }

            string item_id = TableValidator.Str(row, "grant_item_id");
            if (item_id.Length != 0
                && db.get_row("item_base", item_id) == null
                && db.get_row("equip_base", item_id) == null)
            {
                errors.Add($"dialogue_option[{option_id}].grant_item_id={item_id} 既不是道具也不是装备");
            }
        }
    }

    /// <summary>
    /// 难度阶梯：表里的行顺序就是难度递增顺序，每一档不许比上一档弱，也不许只是换了个名字。
    /// </summary>
    internal static void CheckDifficultyLadder(TableDb db, List<string> errors)
    {
        List<Resource> rows = TableValidator.Rows(db, "difficulty_config");
        if (rows.Count < 2)
        {
            return;
        }

        for (var index = 1; index < rows.Count; index++)
        {
            Resource before = rows[index - 1];
            Resource now = rows[index];
            var enemy_grew = false;
            foreach (string column in DIFFICULTY_LADDER_COLUMNS)
            {
                double low = TableValidator.Num(before, column);
                double high = TableValidator.Num(now, column);
                if (high < low)
                {
                    errors.Add(
                        $"difficulty_config[{TableValidator.RowId(now)}].{column} = {high} "
                        + $"比上一档（{TableValidator.RowId(before)}，{low}）还低：难度越高数值反而越小"
                    );
                }
                else if (high > low && column.StartsWith("enemy_"))
                {
                    enemy_grew = true;
                }
            }

            if (!enemy_grew)
            {
                errors.Add(
                    $"difficulty_config[{TableValidator.RowId(now)}] 的敌人倍率与上一档"
                    + $"（{TableValidator.RowId(before)}）完全一样：这一档只是换了个名字"
                );
            }
        }
    }

    /// <summary>
    /// 难度解锁条件：`unlock_condition` 是**中文短语**，映射表在 `WorldMapService.DIFFICULTY_RULES`。
    /// 运行期碰到没登记的短语会 push_error（不静默），但那要等玩家点开难度菜单；构建期就该拦住。
    /// </summary>
    internal static void CheckDifficultyUnlockConditions(TableDb db, List<string> errors)
    {
        var known = new HashSet<string>(WorldMapService.difficulty_conditions());
        foreach (Resource row in TableValidator.Rows(db, "difficulty_config"))
        {
            string condition = TableValidator.Trimmed(row, "unlock_condition");
            if (!known.Contains(condition))
            {
                errors.Add(
                    $"difficulty_config[{TableValidator.RowId(row)}].unlock_condition = '{condition}' "
                    + "没在 WorldMapService.DIFFICULTY_RULES 里登记：这一档永远解锁不了"
                );
            }
        }
    }

    /// <summary>
    /// 代码点了名的成长常数，表里必须都有——缺行不会崩，只会**静默用兜底值**。
    /// 清单从**代码侧**取（`GrowthCalculator.Defaults()` 的键），不手抄一份（手抄的会漂）。
    /// </summary>
    internal static void CheckGrowthConstantsPresent(TableDb db, List<string> errors)
    {
        Godot.Collections.Dictionary defaults = GrowthCalculator.Defaults();
        var present = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "growth_const"))
        {
            present.Add(TableValidator.Str(row, "const_id"));
        }

        foreach (Variant key in defaults.Keys)
        {
            string const_id = key.AsString();
            if (!present.Contains(const_id))
            {
                errors.Add(
                    $"growth_const 缺少代码依赖的常数 '{const_id}'：缺行时 GrowthCalculator 会"
                    + $"**静默用兜底值**（{defaults[key].AsString()}）"
                );
            }
        }
    }

    /// <summary>成长常数里那几个「有语义下限」的：写了更小的值会被代码的守卫静默换掉。</summary>
    internal static void CheckGrowthConstRanges(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "growth_const"))
        {
            string const_id = TableValidator.Str(row, "const_id");
            if (!GROWTH_CONST_FLOORS.TryGetValue(const_id, out (double Floor, string Hint) floor))
            {
                continue;
            }

            if (TableValidator.Num(row, "value") < floor.Floor)
            {
                errors.Add(
                    $"growth_const[{const_id}].value = {TableValidator.Num(row, "value")} "
                    + $"小于可用下限 {floor.Floor}：{floor.Hint}"
                );
            }
        }
    }

    /// <summary>
    /// 增益与套装（08）：把 PS1 那一半 5.16／5.17 里**构建期查得动**的规则镜像过来。
    /// 有一处**故意不镜像**：`buff_def.is_debuff` 在行类里是 bool，CSV 里的 0/1 到这一层
    /// 已经看不出区别——那一条只有直接读 CSV 文本的 PS1 拦得住，这里如实说明，不假装查过。
    /// </summary>
    internal static void CheckBuffTables(TableDb db, List<string> errors)
    {
        CheckBuffStats(db, errors);
        CheckBuffDefs(db, errors);
        CheckBuffGrants(db, errors);
        CheckSets(db, errors);
    }

    /// <summary>buff_stat：target 前缀与引用（`attr:` 查 attribute_def、`stat:` 查 stat_def）。</summary>
    private static void CheckBuffStats(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "buff_stat"))
        {
            string target = TableValidator.Str(row, "target");
            (string kind, string target_id) = TableValidator.ParsePrefixedTarget(target);
            if (kind.Length == 0)
            {
                errors.Add(
                    $"buff_stat[{TableValidator.RowId(row)}].target='{target}' 缺少 attr: 或 stat: 前缀"
                );
                continue;
            }

            string table_name = kind == "attr" ? "attribute_def" : "stat_def";
            string id_column = kind == "attr" ? "attr_id" : "stat_id";
            if (!TableValidator.ColumnValues(db, table_name, id_column).ContainsKey(target_id))
            {
                errors.Add(
                    $"buff_stat[{TableValidator.RowId(row)}].target='{target}' 引用的"
                    + $"{(kind == "attr" ? "属性" : "派生数值")}不存在"
                );
            }
        }
    }

    /// <summary>buff_def：数值型必须有加成、duration 形态、可叠层的 max_stack ≥ 2。</summary>
    private static void CheckBuffDefs(TableDb db, List<string> errors)
    {
        var mods_by_buff = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "buff_stat"))
        {
            string buff_id = TableValidator.Str(row, "buff_id");
            mods_by_buff.TryGetValue(buff_id, out int count);
            mods_by_buff[buff_id] = count + 1;
        }

        foreach (Resource row in TableValidator.Rows(db, "buff_def"))
        {
            string buff_id = TableValidator.RowId(row);
            string effect_kind = TableValidator.Str(row, "effect_kind");
            mods_by_buff.TryGetValue(buff_id, out int mods);
            if (effect_kind == "stat" && mods < 1)
            {
                errors.Add(
                    $"buff_def[{buff_id}] 是数值型（effect_kind=stat），"
                    + "但 buff_stat 里一条加成都没有（装上等于没装）"
                );
            }

            int duration = TableValidator.Int(row, "duration");
            if (duration < 0 && duration != -1)
            {
                errors.Add($"buff_def[{buff_id}].duration={duration} 只允许 -1（整场）或非负");
            }

            if (TableValidator.Str(row, "stack_rule") == "stack" && TableValidator.Int(row, "max_stack") < 2)
            {
                errors.Add(
                    $"buff_def[{buff_id}] 是可叠层 buff（stack_rule=stack），"
                    + $"max_stack={TableValidator.Int(row, "max_stack")} 必须 ≥ 2"
                );
            }
        }
    }

    /// <summary>
    /// buff_grant：source_id 必须真实存在（按 source_type 分流）；`(source_type, trigger)`
    /// 必须是代码真的会读的组合；每部内功都要有一条 `on_cast`。
    /// </summary>
    private static void CheckBuffGrants(TableDb db, List<string> errors)
    {
        var source_tables = new Dictionary<string, string[]>
        {
            ["skill_active"] = new[] { "skill_active", "skill_id" },
            ["skill_passive"] = new[] { "skill_passive", "skill_id" },
            ["equip"] = new[] { "equip_base", "equip_id" },
            ["set"] = new[] { "set_def", "set_id" },
            ["event"] = new[] { "event_check", "check_id" },
            ["item"] = new[] { "item_base", "item_id" },
        };
        var castable = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "buff_grant"))
        {
            string source_type = TableValidator.Str(row, "source_type");
            string source_id = TableValidator.Str(row, "source_id");
            string trigger = TableValidator.Str(row, "trigger");
            // **这一行到底会不会触发**：只查 id 存在是不够的——写一个没人读的时机，
            // 结果就是「配了却不发」而各处都绿（2026-10-03 补）。
            if (!TableValidatorActors.IsFiredCombo(source_type, trigger))
            {
                errors.Add(
                    $"buff_grant[{TableValidator.RowId(row)}]：source_type={source_type} 配 trigger={trigger} "
                    + $"这一行**永远不会触发**（代码会读的组合：{TableValidatorActors.ComboText(source_type)}）——"
                    + "要加新时机得先在战斗侧接上，别只写表"
                );
            }

            if (!source_tables.TryGetValue(source_type, out string[] spec))
            {
                continue; // 非法 source_type 由 ENUMS 报，这里不重复刷屏
            }

            if (!TableValidator.ColumnValues(db, spec[0], spec[1]).ContainsKey(source_id))
            {
                errors.Add(
                    $"buff_grant[{TableValidator.RowId(row)}].source_id='{source_id}' 在 {spec[0]} 里不存在"
                    + "（发了也没人认）"
                );
            }

            if (source_type == "skill_passive" && trigger == "on_cast")
            {
                castable.Add(source_id);
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "skill_passive"))
        {
            string passive_id = TableValidator.Str(row, "skill_id");
            if (!castable.Contains(passive_id))
            {
                errors.Add($"内功 {passive_id} 没有 on_cast 的 buff_grant，内功指令点它没有反应");
            }
        }
    }

    /// <summary>套装：成员类型要和 set_kind 对得上；档位必须是正数、且**够得着**（内功套按占格数之和）。</summary>
    private static void CheckSets(TableDb db, List<string> errors)
    {
        var kind_tables = new Dictionary<string, string[]>
        {
            ["equip"] = new[] { "equip_base", "equip_id" },
            ["skill_active"] = new[] { "skill_active", "skill_id" },
            ["skill_passive"] = new[] { "skill_passive", "skill_id" },
        };
        var kind_of = new Dictionary<string, string>();
        var members_of = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "set_def"))
        {
            string set_id = TableValidator.Str(row, "set_id");
            kind_of[set_id] = TableValidator.Str(row, "set_kind");
            members_of[set_id] = 0;
        }

        var slot_cost = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "skill_passive"))
        {
            slot_cost[TableValidator.Str(row, "skill_id")] = TableValidator.Int(row, "slot_cost");
        }

        var reachable = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "set_member"))
        {
            string set_id = TableValidator.Str(row, "set_id");
            string member_id = TableValidator.Str(row, "member_id");
            kind_of.TryGetValue(set_id, out string kind);
            kind ??= "";
            if (!kind_tables.TryGetValue(kind, out string[] spec))
            {
                continue; // 非法 set_kind 由 ENUMS 报
            }

            if (!TableValidator.ColumnValues(db, spec[0], spec[1]).ContainsKey(member_id))
            {
                errors.Add($"set_member[{set_id}] 的成员 '{member_id}' 不是合法的 {kind}");
            }

            members_of.TryGetValue(set_id, out int members);
            members_of[set_id] = members + 1;
            // 内功套按**占格数之和**计（与 SetService.count_for 同一口径）
            var step = 1;
            if (kind == "skill_passive" && slot_cost.TryGetValue(member_id, out int cost))
            {
                step = Mathf.Max(1, cost);
            }

            reachable.TryGetValue(set_id, out int total);
            reachable[set_id] = total + step;
        }

        foreach (Resource row in TableValidator.Rows(db, "set_bonus"))
        {
            string set_id = TableValidator.Str(row, "set_id");
            int tier = TableValidator.Int(row, "required_count");
            if (tier < 1)
            {
                errors.Add($"set_bonus[{set_id}].required_count={tier} 应为正整数");
                continue;
            }

            reachable.TryGetValue(set_id, out int top);
            if (tier > top)
            {
                kind_of.TryGetValue(set_id, out string kind);
                string unit = kind == "skill_passive" ? "格" : "件";
                errors.Add(
                    $"set_bonus[{set_id}] 要求 {tier} {unit}，但成员最多只到 {top} {unit}，永远凑不齐"
                );
            }
        }

        foreach (string set_id in kind_of.Keys)
        {
            members_of.TryGetValue(set_id, out int members);
            if (members < 1)
            {
                errors.Add($"套装 {set_id} 一个成员都没有");
            }
        }
    }

    /// <summary>`guide_step.sort_order` 的报错文案要的是「[1, 2]」这样的整数列表。</summary>
    private static List<string> OrderText(HashSet<int> orders)
    {
        var values = new List<int>(orders);
        values.Sort();
        var out_values = new List<string>();
        foreach (int value in values)
        {
            out_values.Add(value.ToString());
        }

        return out_values;
    }
}
