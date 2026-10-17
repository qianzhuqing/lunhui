using System.Collections.Generic;
using Godot;

namespace Lunhui.Core;

/// <summary>
/// 校验器的「角色／武学／装备／敌人／掉落／事件／商店」那一族（C# 版，对位原
/// `src/core/table_validator.gd` 的同名函数）。纯 C# 静态类，只被 `TableValidator.validate()` 调用。
/// </summary>
internal static class TableValidatorActors
{

    /// <summary>
    /// 代码**真的会读**的 `(source_type, trigger)` 组合（唯一出处就是这张表，别在别处再抄一份）。
    /// 加新时机的顺序：先在战斗侧接上触发点，再往这里加一行——否则表里配了也不会发。
    /// 故意留空的：`event`（06 的 trigger 列表里没有 event 那一档，等设计）、
    /// `item + on_consume`（道具使用要等 `item_base` 效果列，见 待策划确认.md Q33）。
    /// </summary>
    private static readonly Dictionary<string, string[]> BUFF_TRIGGER_COMBOS = new()
    {
        ["skill_passive"] = new[] { "on_cast" },                        // 内功催动（六指令之一）
        ["skill_active"] = new[] { "on_cast", "on_hit" },               // 招式：施放 / 命中
        ["equip"] = new[] { "on_equip", "on_battle_start", "on_hit" },  // 装备：装备即生效 / 进场 / 命中
        ["set"] = new[] { "on_battle_start" },                          // 套装档位：走贡献通道（常驻）
    };

    /// <summary>
    /// 已知超编的敌人：数据是设计侧配的，「削内功」还是「提根骨」属内容决定
    /// （见 待策划确认.md Q79）。**双向维护**：新出现的超编当场报错；清单里那一行不再超编也报错。
    /// </summary>
    private static readonly Dictionary<string, string> KNOWN_OVER_CAPACITY_ENEMIES = new()
    {
        ["en_bd_boss"] = "占 5 格 > 容量 4（等级 12／根骨 8）",
        ["en_hidden_drunk"] = "占 7 格 > 容量 4（等级 15／根骨 8）",
    };

    /// <summary>掉落的东西要真的存在；同一个 drop_group 里 slot 不能重复（`sort_custom` 不稳定）。</summary>
    internal static void CheckDropItemRefs(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "drop_table"))
        {
            string item_type = TableValidator.Str(row, "item_type");
            string item_id = TableValidator.Str(row, "item_id");
            if (item_type == "equip")
            {
                if (db.get_row("equip_base", item_id) == null)
                {
                    errors.Add(
                        $"drop_table[{TableValidator.RowId(row)}].item_id = '{item_id}' 在 equip_base 里不存在"
                    );
                }
            }
            else if (db.get_row("item_base", item_id) == null)
            {
                errors.Add(
                    $"drop_table[{TableValidator.RowId(row)}].item_id = '{item_id}' 在 item_base 里不存在"
                );
            }
        }

        var seen_slots = new Dictionary<string, string>();
        foreach (Resource row in TableValidator.Rows(db, "drop_table"))
        {
            string group = TableValidator.Str(row, "drop_group");
            int slot = TableValidator.Int(row, "slot");
            string key = $"{group}|{slot}";
            if (seen_slots.TryGetValue(key, out string first))
            {
                errors.Add(
                    $"drop_table 里 {group} 的槽位 {slot} 重复（{first} 与 {TableValidator.RowId(row)}）："
                    + "掉落顺序会不确定"
                );
            }
            else
            {
                seen_slots[key] = TableValidator.RowId(row);
            }
        }
    }

    internal static void CheckHiddenRewardRefs(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "hidden_trigger"))
        {
            string reward_type = TableValidator.Str(row, "reward_type");
            string reward_id = TableValidator.Str(row, "reward_id");
            if (reward_id.Length == 0)
            {
                continue;
            }

            string target = reward_type switch
            {
                "boss" => "enemy_base",
                "equip" => "equip_base",
                "skillbook" => "item_base",
                "room" => "dungeon_room",
                _ => "",
            };
            if (target.Length == 0)
            {
                continue;
            }

            if (db.get_row(target, reward_id) == null)
            {
                errors.Add(
                    $"hidden_trigger[{TableValidator.RowId(row)}].reward_id = '{reward_id}' 在 {target} 里不存在"
                );
            }
        }
    }

    internal static void CheckEnemyTeamMembers(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "enemy_team"))
        {
            Godot.Collections.Array members = row.Call("parsed_members").AsGodotArray();
            if (members.Count == 0)
            {
                errors.Add($"enemy_team[{TableValidator.RowId(row)}].members 为空或无法解析");
                continue;
            }

            foreach (Variant entry in members)
            {
                Godot.Collections.Dictionary member = entry.AsGodotDictionary();
                string enemy_id = member["enemy_id"].AsString();
                if (member["count"].AsInt32() <= 0)
                {
                    errors.Add($"enemy_team[{TableValidator.RowId(row)}] 成员 {enemy_id} 的数量必须大于 0");
                }

                if (db.get_row("enemy_base", enemy_id) == null)
                {
                    errors.Add(
                        $"enemy_team[{TableValidator.RowId(row)}] 成员 '{enemy_id}' 在 enemy_base 里不存在"
                    );
                }
            }
        }
    }

    /// <summary>
    /// 练功木桩（`faction=training`）是**唯一允许没有掉落组**的敌人——09 §3.3 的收益上限表写着「掉落：无」。
    /// 只对「玩家真会打到它」的敌人（明雷／房间队伍展开出来的）报错。
    /// </summary>
    internal static void CheckTrainingFaction(TableDb db, List<string> errors)
    {
        var reachable_enemies = new HashSet<string>();
        var reachable_teams = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "roaming_spawn"))
        {
            reachable_teams.Add(TableValidator.Str(row, "team_id"));
        }

        foreach (Resource row in TableValidator.Rows(db, "dungeon_room"))
        {
            string team_id = TableValidator.Str(row, "enemy_team");
            if (team_id.Length != 0)
            {
                reachable_teams.Add(team_id);
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "enemy_team"))
        {
            if (!reachable_teams.Contains(TableValidator.Str(row, "team_id")))
            {
                continue;
            }

            foreach (Variant entry in row.Call("parsed_members").AsGodotArray())
            {
                reachable_enemies.Add(entry.AsGodotDictionary()["enemy_id"].AsString());
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "enemy_base"))
        {
            if (TableValidator.Str(row, "faction") == "training")
            {
                continue;
            }

            // 0.26.0 起还有一类**够不着的敌人**允许空掉落组：设计先把数据给出来、入口后接
            //（例：世界事件「门派弟子历练」的 en_wanderer_disciple，入口等 world_event.effect_id，见 Q64）。
            string enemy_id = TableValidator.Str(row, "enemy_id");
            if (!reachable_enemies.Contains(enemy_id))
            {
                continue;
            }

            if (TableValidator.Trimmed(row, "drop_group").Length == 0)
            {
                errors.Add(
                    $"enemy_base[{enemy_id}] 能被明雷或房间打到，却没有 drop_group"
                    + "（练功木桩那种「无掉落」的靶子要写 faction=training）"
                );
            }
        }
    }

    /// <summary>
    /// 敌人也受**招式槽**限制（设计 10 §二 连带规则①）：配的招式数 ≤ `growth_const` 那套公式算出的上限。
    /// 只在七维配好的行上生效（没配的行由 `EnemyFactory._warn_derived_path` 点名）。
    /// </summary>
    internal static void CheckEnemySkillSlots(TableDb db, List<string> errors)
    {
        GrowthCalculator growth = GrowthCalculator.create(db);
        AttributeCalculator calculator = AttributeCalculator.create(db);
        foreach (Resource row in TableValidator.Rows(db, "enemy_base"))
        {
            string enemy_id = TableValidator.Str(row, "enemy_id");
            Godot.Collections.Dictionary attrs = row.Call("attr_map").AsGodotDictionary();
            if (attrs.Count == 0)
            {
                continue;
            }

            string[] passives = PassivesOf(db, enemy_id);
            Godot.Collections.Array contributions = new();
            if (passives.Length != 0)
            {
                contributions = growth.passive_contributions(passives, new Godot.Collections.Dictionary());
            }

            Godot.Collections.Dictionary totals = calculator.attr_totals_of(
                attrs, new Godot.Collections.Dictionary(), contributions
            );
            int slots = growth.active_slots(TableValidator.Int(row, "level"), totals);
            int skill_count = RowsWhere(db, "enemy_skill", "enemy_id", enemy_id).Count;
            if (skill_count > slots)
            {
                errors.Add(
                    $"enemy_base[{enemy_id}] 配了 {skill_count} 招，但它的招式槽只带得动 {slots}"
                    + $"（等级 {TableValidator.Int(row, "level")}／悟性 {AptitudeOf(totals)}）——"
                    + "要么降招式数、要么提等级/悟性（含内功给的）"
                );
            }
        }
    }

    /// <summary>
    /// 敌人配的内功**占格 ≤ 容量**（设计 10 §二 连带规则①的另一半；与角色同一组公式）。
    /// 已知超编的两行写在 `KNOWN_OVER_CAPACITY_ENEMIES` 里，双向维护。
    /// </summary>
    internal static void CheckEnemyPassiveCapacity(TableDb db, List<string> errors)
    {
        GrowthCalculator growth = GrowthCalculator.create(db);
        AttributeCalculator calculator = AttributeCalculator.create(db);
        var checked_count = 0;
        var over = new Dictionary<string, string>();
        foreach (Resource row in TableValidator.Rows(db, "enemy_base"))
        {
            string enemy_id = TableValidator.Str(row, "enemy_id");
            Godot.Collections.Dictionary attrs = row.Call("attr_map").AsGodotDictionary();
            if (attrs.Count == 0)
            {
                continue; // 七维没配的行由 `EnemyFactory._warn_derived_path` 点名（不在这里重复报）
            }

            checked_count += 1;
            string[] passives = PassivesOf(db, enemy_id);
            Godot.Collections.Array contributions = new();
            if (passives.Length != 0)
            {
                contributions = growth.passive_contributions(passives, new Godot.Collections.Dictionary());
            }

            Godot.Collections.Dictionary totals = calculator.attr_totals_of(
                attrs, new Godot.Collections.Dictionary(), contributions
            );
            Godot.Collections.Dictionary fit = growth.passive_fit(
                passives, TableValidator.Int(row, "level"), totals
            );
            if (fit["fits"].AsBool())
            {
                continue;
            }

            over[enemy_id] =
                $"占 {fit["used"].AsInt32()} 格 > 容量 {fit["capacity"].AsInt32()}"
                + $"（等级 {TableValidator.Int(row, "level")}／根骨 {TotalsOf(totals, "gen")}）";
        }

        if (checked_count == 0)
        {
            errors.Add("enemy_base 里没有一行配了七维，敌人内功容量这条规则量不到任何东西（表结构变了吗？）");
        }

        foreach (KeyValuePair<string, string> entry in over)
        {
            if (!KNOWN_OVER_CAPACITY_ENEMIES.ContainsKey(entry.Key))
            {
                errors.Add(
                    $"enemy_base[{entry.Key}] 配的内功超编：{entry.Value}——设计 10 §二 要求敌人与角色同一组公式"
                    + "（招式槽／内功容量），要么削内功、要么提根骨（根骨同时绑气血与外防），"
                    + "或写进 KNOWN_OVER_CAPACITY_ENEMIES 并写明等谁（`待策划确认.md` Q79）"
                );
            }
        }

        foreach (KeyValuePair<string, string> entry in KNOWN_OVER_CAPACITY_ENEMIES)
        {
            if (over.ContainsKey(entry.Key))
            {
                continue;
            }

            errors.Add(
                $"enemy_base[{entry.Key}] 已经不再超编了（白名单记的是「{entry.Value}」），"
                + "把 KNOWN_OVER_CAPACITY_ENEMIES 里那一行删掉"
            );
        }
    }

    /// <summary>character_base 的七维之和必须等于 attr_total；起始等级必须落在 [1, 等级上限] 里。</summary>
    internal static void CheckCharacterAttrTotal(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "character_base"))
        {
            int sum = row.Call("attr_sum").AsInt32();
            if (sum != TableValidator.Int(row, "attr_total"))
            {
                errors.Add(
                    $"character_base[{TableValidator.RowId(row)}] 的 attr_total={TableValidator.Int(row, "attr_total")}，"
                    + $"但五维相加是 {sum}"
                );
            }

            // 起始等级必须落在 [1, 等级上限] 里：0 会被 `maxi(1, …)` 静默当成 1 级，
            // 超过上限则找不到 `level_growth` 那一行（等级成长静默失效）。
            int top_level = TableValidator.max_level(db);
            int start_level = TableValidator.Int(row, "start_level");
            if (start_level < 1)
            {
                errors.Add(
                    $"character_base[{TableValidator.RowId(row)}].start_level={start_level}："
                    + "低于 1 级（会被静默当成 1 级）"
                );
            }
            else if (top_level > 0 && start_level > top_level)
            {
                errors.Add(
                    $"character_base[{TableValidator.RowId(row)}].start_level={start_level}："
                    + $"超过等级上限 {top_level}（level_growth 只配到这一级）"
                );
            }
        }
    }

    /// <summary>
    /// `level_growth` 必须从 1 级起、一级一行、中间不许缺号；升级不许变弱；
    /// 经验门槛与加点不许下降（封顶那一级除外）。
    /// </summary>
    internal static void CheckLevelGrowthRows(TableDb db, List<string> errors)
    {
        var levels = new List<int>();
        foreach (Resource row in TableValidator.Rows(db, "level_growth"))
        {
            levels.Add(TableValidator.Int(row, "level"));
        }

        levels.Sort();
        if (levels.Count == 0)
        {
            errors.Add("level_growth 一行都没有：升级无从谈起");
            return;
        }

        if (levels[0] != 1)
        {
            errors.Add($"level_growth 必须从 1 级开始（现在最小是 {levels[0]}）");
        }

        for (var index = 1; index < levels.Count; index++)
        {
            int previous = levels[index - 1];
            int current = levels[index];
            if (current != previous + 1)
            {
                errors.Add(
                    $"level_growth 缺 {previous + 1} 级这一行（{previous} → {current}）："
                    + $"升级会静默卡在 {previous} 级、经验池却一直涨"
                );
            }
        }

        // 升级不该让人变弱：基础数值随等级不许下降（面板上看得见、却没有任何报错）。
        var ordered = new List<Resource>(TableValidator.Rows(db, "level_growth"));
        ordered.Sort((a, b) => TableValidator.Int(a, "level").CompareTo(TableValidator.Int(b, "level")));
        foreach (string column in TableValidator.LEVEL_BASE_COLUMNS)
        {
            for (var index = 1; index < ordered.Count; index++)
            {
                double before = TableValidator.Num(ordered[index - 1], column);
                double now = TableValidator.Num(ordered[index], column);
                if (now < before)
                {
                    errors.Add(
                        $"level_growth[{TableValidator.Int(ordered[index], "level")}].{column} = {now} "
                        + $"比上一级（{TableValidator.Int(ordered[index - 1], "level")} 级，{before}）还低：升级会变弱"
                    );
                }
            }
        }

        // 经验门槛与加点：写错一位就会让整段升级「白送」或「变便宜」，而代码不会报错。
        // **封顶那一级除外**：最高等级用 `exp_to_next = 0`／`upgrade_points = 0` 表示「没有下一级」。
        int cap_level = TableValidator.Int(ordered[^1], "level");
        for (var index = 1; index < ordered.Count; index++)
        {
            if (TableValidator.Int(ordered[index], "level") == cap_level)
            {
                continue;
            }

            double exp_before = TableValidator.Num(ordered[index - 1], "exp_to_next");
            double exp_now = TableValidator.Num(ordered[index], "exp_to_next");
            if (exp_now <= 0.0)
            {
                errors.Add(
                    $"level_growth[{TableValidator.Int(ordered[index], "level")}].exp_to_next = {exp_now}："
                    + $"不是封顶级（{cap_level} 级才是）就必须给经验门槛，否则这一级白送"
                );
            }
            else if (exp_now < exp_before)
            {
                errors.Add(
                    $"level_growth[{TableValidator.Int(ordered[index], "level")}].exp_to_next = {exp_now} "
                    + $"比上一级（{TableValidator.Int(ordered[index - 1], "level")} 级，{exp_before}）还低："
                    + "升级会突然变便宜"
                );
            }

            double point_before = TableValidator.Num(ordered[index - 1], "upgrade_points");
            double point_now = TableValidator.Num(ordered[index], "upgrade_points");
            if (point_now < point_before)
            {
                errors.Add(
                    $"level_growth[{TableValidator.Int(ordered[index], "level")}].upgrade_points = {point_now} "
                    + $"比上一级（{TableValidator.Int(ordered[index - 1], "level")} 级，{point_before}）还低："
                    + "升级会给更少的加点"
                );
            }
        }
    }

    /// <summary>装备的等级需求必须落在 `[1, 等级上限]` 里——超过上限 = **永远穿不上**。</summary>
    internal static void CheckEquipLevelReqs(TableDb db, List<string> errors)
    {
        int top = TableValidator.max_level(db);
        if (top <= 0)
        {
            return;
        }

        foreach (Resource row in TableValidator.Rows(db, "equip_base"))
        {
            int req = TableValidator.Int(row, "level_req");
            if (req < 1)
            {
                errors.Add($"equip_base[{TableValidator.RowId(row)}].level_req={req}：低于 1 级（等于人人可穿）");
            }
            else if (req > top)
            {
                errors.Add(
                    $"equip_base[{TableValidator.RowId(row)}].level_req={req} 超过等级上限 {top}：这件装备永远穿不上"
                );
            }
        }
    }

    /// <summary>每张小地图的楼层号必须从 1 起、中间不空号（`DungeonService.floors()` 按它分组）。</summary>
    internal static void CheckDungeonFloors(TableDb db, List<string> errors)
    {
        var by_scene = new Dictionary<string, HashSet<int>>();
        foreach (Resource row in TableValidator.Rows(db, "dungeon_room"))
        {
            string scene_id = TableValidator.Str(row, "scene_id");
            if (!by_scene.TryGetValue(scene_id, out HashSet<int> floors))
            {
                floors = new HashSet<int>();
                by_scene[scene_id] = floors;
            }

            floors.Add(TableValidator.Int(row, "floor"));
        }

        foreach (KeyValuePair<string, HashSet<int>> entry in by_scene)
        {
            var floors = new List<int>(entry.Value);
            floors.Sort();
            if (floors.Count == 0)
            {
                continue;
            }

            if (floors[0] != 1)
            {
                errors.Add($"{entry.Key} 的房间最低层是第 {floors[0]} 层，不是第 1 层");
            }

            for (var index = 1; index < floors.Count; index++)
            {
                int previous = floors[index - 1];
                int current = floors[index];
                if (current != previous + 1)
                {
                    errors.Add(
                        $"{entry.Key} 缺第 {previous + 1} 层（{previous} → {current}）："
                        + "完成度面板会少一行、分层判定也会跟着缺"
                    );
                }
            }
        }
    }

    /// <summary>event_check.check_source 必须带 attr: / skill: 前缀，且指向存在的目标。</summary>
    internal static void CheckEventSources(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "event_check"))
        {
            Godot.Collections.Dictionary parsed = row.Call("parsed_source").AsGodotDictionary();
            string kind = parsed["kind"].AsString();
            string target_id = parsed["target_id"].AsString();
            switch (kind)
            {
                case "attr":
                    if (db.get_row("attribute_def", target_id) == null)
                    {
                        errors.Add(
                            $"event_check[{TableValidator.RowId(row)}].check_source 引用的属性 '{target_id}' 不存在"
                        );
                    }

                    break;
                case "skill":
                    if (db.get_row("event_skill_def", target_id) == null)
                    {
                        errors.Add(
                            $"event_check[{TableValidator.RowId(row)}].check_source 引用的非战斗技能 '{target_id}' 不存在"
                        );
                    }

                    break;
                default:
                    errors.Add(
                        $"event_check[{TableValidator.RowId(row)}].check_source = "
                        + $"'{TableValidator.Str(row, "check_source")}' 缺少 attr: 或 skill: 前缀"
                    );
                    break;
            }
        }
    }

    /// <summary>事件奖励按类型查表（event 是自由事件名，不查）。</summary>
    internal static void CheckEventRewards(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "event_check"))
        {
            string reward_type = TableValidator.Str(row, "reward_type");
            string reward_id = TableValidator.Str(row, "reward_id");
            string target = reward_type switch
            {
                "item" or "skillbook" => "item_base",
                "equip" => "equip_base",
                "room" => "dungeon_room",
                "boss" => "enemy_base",
                _ => "",
            };
            if (target.Length == 0)
            {
                continue;
            }

            if (reward_id.Length == 0)
            {
                errors.Add(
                    $"event_check[{TableValidator.RowId(row)}] 的 reward_type={reward_type} 但没有填 reward_id"
                );
            }
            else if (db.get_row(target, reward_id) == null)
            {
                errors.Add(
                    $"event_check[{TableValidator.RowId(row)}].reward_id = '{reward_id}' 在 {target} 里不存在"
                );
            }
        }
    }

    /// <summary>初始非战斗技能等级不能超过该技能的等级上限。</summary>
    internal static void CheckEventSkillLevels(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "character_base_skill"))
        {
            Resource skill = db.get_row("event_skill_def", TableValidator.Str(row, "skill_id"));
            if (skill == null)
            {
                continue;
            }

            if (TableValidator.Int(row, "level") > TableValidator.Int(skill, "max_level"))
            {
                errors.Add(
                    $"character_base_skill[{TableValidator.RowId(row)}] 的 {TableValidator.Str(row, "skill_id")} "
                    + $"等级 {TableValidator.Int(row, "level")} 超过上限 {TableValidator.Int(skill, "max_level")}"
                );
            }
        }
    }

    /// <summary>货架上的东西要么是装备要么是物品，两处都查不到说明 id 写错了。</summary>
    internal static void CheckShopItemRefs(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "shop_stock"))
        {
            string item_id = TableValidator.Str(row, "item_id");
            if (db.get_row("equip_base", item_id) == null && db.get_row("item_base", item_id) == null)
            {
                errors.Add(
                    $"shop_stock[{TableValidator.RowId(row)}] 的 '{item_id}' 既不在 equip_base 也不在 item_base"
                );
            }
        }
    }

    /// <summary>卖出价必须严格低于买入价，否则玩家能在两家店之间刷钱。</summary>
    internal static void CheckShopPrices(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "shop_stock"))
        {
            if (TableValidator.Int(row, "buy_price") <= TableValidator.Int(row, "sell_price"))
            {
                errors.Add(
                    $"shop_stock[{TableValidator.RowId(row)}] 买入 {TableValidator.Int(row, "buy_price")} / "
                    + $"卖出 {TableValidator.Int(row, "sell_price")}：卖出价必须低于买入价，否则可以刷钱"
                );
            }
        }
    }

    /// <summary>
    /// 宝箱守护者（0.10.0；0.14.0 把文取拆成三列）：两条路都要能走通。
    /// `peace_item`／`peace_check_source`／`peace_check_value` 三字段可叠加，满足任意一条即算过，
    /// 所以校验也是「至少一条能走」而不是「必须三条齐」。
    /// </summary>
    internal static void CheckNpcGuards(TableDb db, List<string> errors)
    {
        var groups = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "drop_table"))
        {
            groups.Add(TableValidator.Str(row, "drop_group"));
        }

        foreach (Resource row in TableValidator.Rows(db, "npc_guard"))
        {
            string guard_id = TableValidator.Str(row, "guard_id");
            string reward = TableValidator.Trimmed(row, "reward_group");
            if (!groups.Contains(reward))
            {
                errors.Add($"npc_guard[{guard_id}] 的 reward_group='{reward}' 不在 drop_table 的任何一组里");
            }

            string item = TableValidator.Trimmed(row, "peace_item");
            string source = TableValidator.Trimmed(row, "peace_check_source");
            int value = TableValidator.Int(row, "peace_check_value");
            if (item.Length == 0 && source.Length == 0)
            {
                errors.Add($"npc_guard[{guard_id}] 文取的两条路都没配（peace_item 与 peace_check_source 都空）");
            }

            if (item.Length != 0 && db.get_row("item_base", item) == null)
            {
                errors.Add($"npc_guard[{guard_id}] 要的道具 {item} 在 item_base 里没有");
            }

            if (source.Length != 0)
            {
                string[] parts = source.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    errors.Add($"npc_guard[{guard_id}] 的 peace_check_source='{source}' 不是 '<类型>:<id>' 的形式");
                }
                else
                {
                    string kind = parts[0].Trim();
                    string target = parts[1].Trim();
                    switch (kind)
                    {
                        case "skill":
                            if (db.get_row("event_skill_def", target) == null)
                            {
                                errors.Add(
                                    $"npc_guard[{guard_id}] 的判定要非战斗技能 {target}，但 event_skill_def 里没有"
                                );
                            }

                            break;
                        case "attr":
                            if (db.get_row("attribute_def", target) == null)
                            {
                                errors.Add($"npc_guard[{guard_id}] 的判定要属性 {target}，但 attribute_def 里没有");
                            }

                            break;
                        default:
                            errors.Add($"npc_guard[{guard_id}] 的判定类型 '{kind}' 不认识（只认 skill／attr）");
                            break;
                    }

                    if (value <= 0)
                    {
                        errors.Add(
                            $"npc_guard[{guard_id}] 有判定条件（{source}）却没写门槛值（peace_check_value={value}）"
                        );
                    }
                }
            }
        }
    }

    /// <summary>武器槽必须填合法 weapon_type，非武器槽不许填。</summary>
    internal static void CheckWeaponSlotConsistency(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "equip_base"))
        {
            Resource slot = db.get_row("equip_slot_def", TableValidator.Str(row, "slot"));
            if (slot == null)
            {
                // 槽位本身不存在已经由引用校验报过，这里不重复刷屏
                continue;
            }

            string weapon_type = TableValidator.Str(row, "weapon_type");
            if (slot.Call("is_weapon_slot").AsBool())
            {
                if (weapon_type.Length == 0)
                {
                    errors.Add(
                        $"equip_base[{TableValidator.RowId(row)}] 在武器槽 {TableValidator.Str(row, "slot")} 上没填 weapon_type"
                    );
                    continue;
                }

                List<string> allowed = TableValidator.ToStrings(slot.Call("allowed_weapon_types"));
                if (!allowed.Contains(weapon_type))
                {
                    errors.Add(
                        $"equip_base[{TableValidator.RowId(row)}].weapon_type='{weapon_type}' 不在 "
                        + $"{TableValidator.Str(row, "slot")} 允许的类型（{TableValidator.Joined(allowed)}）里"
                    );
                }
            }
            else if (weapon_type.Length != 0)
            {
                errors.Add(
                    $"equip_base[{TableValidator.RowId(row)}] 是非武器槽 {TableValidator.Str(row, "slot")}，"
                    + $"不该填 weapon_type='{weapon_type}'"
                );
            }
        }
    }

    /// <summary>
    /// 武学总表与明细表必须一一对应：active 有且只有一条 skill_active，passive 有且只有一条 skill_passive；
    /// 而且**攻击招式的段数必须 ≥ 1**（写 0 会用得出、却打不出任何伤害）。
    /// </summary>
    internal static void CheckSkillDetails(TableDb db, List<string> errors)
    {
        var active_rows = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "skill_active"))
        {
            string skill_id = TableValidator.Str(row, "skill_id");
            if (!active_rows.Add(skill_id))
            {
                errors.Add($"skill_active 里 {skill_id} 有重复行");
            }
        }

        var passive_rows = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "skill_passive"))
        {
            string skill_id = TableValidator.Str(row, "skill_id");
            if (!passive_rows.Add(skill_id))
            {
                errors.Add($"skill_passive 里 {skill_id} 有重复行");
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "skill_base"))
        {
            string skill_id = TableValidator.Str(row, "skill_id");
            if (row.Call("is_active").AsBool() && !active_rows.Contains(skill_id))
            {
                errors.Add($"武学 {skill_id} 是招式（active），但 skill_active 里没有它的数值行");
            }

            if (row.Call("is_passive").AsBool() && !passive_rows.Contains(skill_id))
            {
                errors.Add($"武学 {skill_id} 是内功（passive），但 skill_passive 里没有它的明细行");
            }
        }

        foreach (string skill_id in active_rows)
        {
            Resource base_row = db.get_row("skill_base", skill_id);
            if (base_row != null && !base_row.Call("is_active").AsBool())
            {
                errors.Add($"skill_active 里的 {skill_id} 在 skill_base 里不是 active");
            }
        }

        foreach (string skill_id in passive_rows)
        {
            Resource base_row = db.get_row("skill_base", skill_id);
            if (base_row != null && !base_row.Call("is_passive").AsBool())
            {
                errors.Add($"skill_passive 里的 {skill_id} 在 skill_base 里不是 passive");
            }
        }

        // **攻击招式的段数必须 ≥ 1**：有伤害类型且倍率 > 0（界面会给出按钮）却写 `hit_count = 0`，
        // 结算循环一次都不跑——**能用、却打不出任何伤害，还不报错**。
        // 只对攻击招式要求：`sk_drunk_zuibu`（醉步）那种空壳有专门的门限盯着（校验 5.21）。
        foreach (Resource row in TableValidator.Rows(db, "skill_active"))
        {
            var is_attack = TableValidator.Str(row, "damage_type").Length != 0
                && TableValidator.Num(row, "power_ratio") > 0.0;
            if (is_attack && TableValidator.Int(row, "hit_count") < 1)
            {
                errors.Add(
                    $"招式 {TableValidator.Str(row, "skill_id")} 有伤害类型与倍率，"
                    + $"但 hit_count = {TableValidator.Int(row, "hit_count")}：结算一段都跑不到（能用却零伤害）"
                );
            }
        }
    }

    /// <summary>招式绑定的武器类型：只能留空、any，或 weapon_type_def 里的类型。</summary>
    internal static void CheckSkillWeaponTypes(TableDb db, List<string> errors)
    {
        Godot.Collections.Dictionary known = db.column_values("weapon_type_def", "weapon_type");
        foreach (Resource row in TableValidator.Rows(db, "skill_base"))
        {
            string weapon_type = TableValidator.Str(row, "weapon_type");
            if (weapon_type.Length == 0 || weapon_type == "any")
            {
                continue;
            }

            if (!known.ContainsKey(weapon_type))
            {
                errors.Add(
                    $"skill_base[{TableValidator.RowId(row)}].weapon_type='{weapon_type}' "
                    + "不是合法武器类型（可填空或 any）"
                );
            }
        }
    }

    /// <summary>
    /// 内功满级签名（`skill_mastery_bonus`，设计 08 §内功满级签名）——逐行查四件事：
    /// ① `skill_id` 得是真内功（挂错表就是一条**永远不会生效**的签名）；② `mastery` 在 1..`mastery_max` 之间
    /// （超过满级就永远够不到）；③ `value` 不为 0（白填）；④ `target` 前缀只能是 `stat:`／`effect:`，
    /// 且 `stat:` 引用的派生数值要真存在。
    /// **`effect:` 的标记不在这里查"接线了没有"**——丙那 6 条按设计就是"等第一章试玩后再排"，
    /// 这里报了会把明知要等的活变成红的；接线状态由 `MasteryBonus.wired()` 一处说，
    /// 面板上对没接线的如实写「还没接」（与内功 `passive_effect` 那套同一口径）。
    /// </summary>
    internal static void CheckMasteryBonuses(TableDb db, List<string> errors)
    {
        int mastery_max = 10;
        foreach (Resource row in TableValidator.Rows(db, "growth_const"))
        {
            if (TableValidator.Str(row, "const_id") == "mastery_max")
            {
                mastery_max = (int)TableValidator.Num(row, "value");
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "skill_mastery_bonus"))
        {
            string id = TableValidator.RowId(row);
            string skill_id = TableValidator.Str(row, "skill_id");
            if (db.get_row("skill_passive", skill_id) == null)
            {
                errors.Add(
                    $"skill_mastery_bonus[{id}].skill_id='{skill_id}' 不是内功（满级签名只挂在内功上）"
                );
            }

            int mastery = (int)TableValidator.Num(row, "mastery");
            if (mastery <= 0 || mastery > mastery_max)
            {
                errors.Add(
                    $"skill_mastery_bonus[{id}].mastery={mastery} 超出 1..{mastery_max}"
                    + "（超过满级就永远够不到）"
                );
            }

            if (Mathf.IsZeroApprox(TableValidator.Num(row, "value")))
            {
                errors.Add($"skill_mastery_bonus[{id}].value = 0：这条签名是白填（练满等于没练）");
            }

            Godot.Collections.Dictionary parsed = row.Call("parsed_target").AsGodotDictionary();
            string kind = parsed["kind"].AsString();
            string target_id = parsed["target_id"].AsString();
            if (kind == "stat")
            {
                if (db.get_row("stat_def", target_id) == null)
                {
                    errors.Add(
                        $"skill_mastery_bonus[{id}].target 引用的派生数值 '{target_id}' 不存在"
                    );
                }
            }
            else if (kind != "effect")
            {
                errors.Add(
                    $"skill_mastery_bonus[{id}].target='{TableValidator.Str(row, "target")}' "
                    + "缺少 stat: 或 effect: 前缀"
                );
            }
        }
    }

    /// <summary>每部内功至少要有一条加成，否则装了等于没装。</summary>
    internal static void CheckPassiveStats(TableDb db, List<string> errors)
    {
        var counts = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "skill_passive_stat"))
        {
            string skill_id = TableValidator.Str(row, "skill_id");
            counts.TryGetValue(skill_id, out int count);
            counts[skill_id] = count + 1;
            if (Mathf.IsZeroApprox(TableValidator.Num(row, "value")))
            {
                errors.Add(
                    $"skill_passive_stat[{TableValidator.RowId(row)}].value = 0：这条加成是白填（装了等于没装）"
                );
            }

            Godot.Collections.Dictionary parsed = row.Call("parsed_target").AsGodotDictionary();
            string kind = parsed["kind"].AsString();
            string target_id = parsed["target_id"].AsString();
            switch (kind)
            {
                case "attr":
                    if (db.get_row("attribute_def", target_id) == null)
                    {
                        errors.Add(
                            $"skill_passive_stat[{TableValidator.RowId(row)}].target 引用的属性 '{target_id}' 不存在"
                        );
                    }

                    break;
                case "stat":
                    if (db.get_row("stat_def", target_id) == null)
                    {
                        errors.Add(
                            $"skill_passive_stat[{TableValidator.RowId(row)}].target 引用的派生数值 '{target_id}' 不存在"
                        );
                    }

                    break;
                default:
                    errors.Add(
                        $"skill_passive_stat[{TableValidator.RowId(row)}].target='{TableValidator.Str(row, "target")}' "
                        + "缺少 attr: 或 stat: 前缀"
                    );
                    break;
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "skill_passive"))
        {
            if (!counts.ContainsKey(TableValidator.Str(row, "skill_id")))
            {
                errors.Add($"内功 {TableValidator.Str(row, "skill_id")} 在 skill_passive_stat 里一条加成都没有");
            }
        }
    }

    /// <summary>
    /// 词条的目标必须能被认出来：`affix_pool.target` 只能 `attr:` 或 `stat:`，且两者都存在；
    /// `value_kind` 也要跟前缀对上——前缀拼错时 `AttributeCalculator` 查不到就**静默忽略**。
    /// </summary>
    internal static void CheckAffixTargets(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "affix_pool"))
        {
            Godot.Collections.Dictionary parsed = row.Call("parsed_target").AsGodotDictionary();
            string kind = parsed["kind"].AsString();
            string target_id = parsed["target_id"].AsString();
            string value_kind = TableValidator.Str(row, "value_kind");
            switch (kind)
            {
                case "attr":
                    if (db.get_row("attribute_def", target_id) == null)
                    {
                        errors.Add(
                            $"affix_pool[{TableValidator.RowId(row)}].target 引用的属性 '{target_id}' 不存在"
                        );
                    }

                    if (value_kind != "attr_point")
                    {
                        errors.Add(
                            $"affix_pool[{TableValidator.RowId(row)}] 是属性点词条，"
                            + $"value_kind 应为 attr_point（现在是 '{value_kind}'）"
                        );
                    }

                    break;
                case "stat":
                    if (db.get_row("stat_def", target_id) == null)
                    {
                        errors.Add(
                            $"affix_pool[{TableValidator.RowId(row)}].target 引用的派生数值 '{target_id}' 不存在"
                        );
                    }

                    if (value_kind != "flat" && value_kind != "rate")
                    {
                        errors.Add(
                            $"affix_pool[{TableValidator.RowId(row)}] 是派生数值词条，"
                            + $"value_kind 应为 flat 或 rate（现在是 '{value_kind}'）"
                        );
                    }

                    break;
                default:
                    errors.Add(
                        $"affix_pool[{TableValidator.RowId(row)}].target='{TableValidator.Str(row, "target")}' "
                        + "缺少 attr: 或 stat: 前缀"
                    );
                    break;
            }
        }
    }

    /// <summary>非「白板」装备至少要有一条加成（否则玩家花更贵的价钱买到的是同一件东西）。</summary>
    internal static void CheckEquipBonuses(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "equip_base"))
        {
            string rarity = TableValidator.Str(row, "rarity");
            if (rarity.Length == 0 || rarity == "common")
            {
                continue;
            }

            var total = 0.0;
            foreach (Variant value in row.Call("attr_bonuses").AsGodotDictionary().Values)
            {
                total += value.AsDouble();
            }

            foreach (Variant value in row.Call("stat_bonuses").AsGodotDictionary().Values)
            {
                total += value.AsDouble();
            }

            if (Mathf.IsZeroApprox(total))
            {
                errors.Add(
                    $"equip_base[{TableValidator.RowId(row)}]（{TableValidator.Str(row, "name_cn")}）"
                    + $"是 {rarity} 稀有度但一条加成都没有：和「白板」没区别"
                );
            }
        }
    }

    /// <summary>growth_const 必备常数（缺了槽位公式就没法算）。</summary>
    internal static void CheckGrowthConstants(TableDb db, List<string> errors)
    {
        string[] required =
        {
            "active_slot_base", "active_slot_lv_div", "active_slot_attr_div", "active_slot_cap",
            "passive_cap_base", "passive_cap_lv_div", "passive_cap_attr_div", "passive_cap_max",
            "mastery_max", "mastery_combat_gain", "cultivate_cost_growth",
            "codex_step", "codex_bonus", "codex_cap",
        };
        Godot.Collections.Dictionary present = db.column_values("growth_const", "const_id");
        foreach (string const_id in required)
        {
            if (!present.ContainsKey(const_id))
            {
                errors.Add($"growth_const 缺少必备常数 {const_id}");
            }
        }
    }

    /// <summary>
    /// 每个角色模板至少要有一部**武器对得上**的起手招式——否则轮到出手时一个按钮都没有，
    /// 战斗会卡住（真实案例见决策 162）。
    /// </summary>
    internal static void CheckStartSkillsUsable(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "character_base"))
        {
            string weapon = TableValidator.Str(row, "weapon_type");
            var usable = 0;
            foreach (string skill_id in TableValidator.ToStrings(row.Call("skill_ids")))
            {
                Resource base_row = db.get_row("skill_base", skill_id);
                if (base_row == null)
                {
                    continue; // 引用的存在性由别的规则报
                }

                if (TableValidator.Str(base_row, "skill_kind") != "active")
                {
                    continue; // 内功不算「打得出手」
                }

                string need = TableValidator.Str(base_row, "weapon_type");
                if (need.Length == 0 || need == weapon)
                {
                    usable += 1;
                }
            }

            if (usable == 0)
            {
                errors.Add(
                    $"character_base[{TableValidator.RowId(row)}] 一部能用的起手招式都没有："
                    + $"模板武器是 '{weapon}'，而 start_skill_ids 里的招式都要别的武器——"
                    + "这样的角色轮到出手时一个按钮都没有（只能按自动/撤退），战斗会卡住"
                );
            }
        }
    }

    /// <summary>模板的起始装备要**装得上**：武器类型必须与模板一致，等级门槛不能高于起始等级。</summary>
    internal static void CheckStartEquipmentUsable(TableDb db, List<string> errors)
    {
        foreach (Resource row in TableValidator.Rows(db, "character_base"))
        {
            string weapon = TableValidator.Str(row, "weapon_type");
            int start_level = TableValidator.Int(row, "start_level");
            foreach (string equip_id in TableValidator.ToStrings(row.Call("equip_ids")))
            {
                Resource equip = db.get_row("equip_base", equip_id);
                if (equip == null)
                {
                    continue; // 引用的存在性由别的规则报
                }

                string equip_weapon = TableValidator.Str(equip, "weapon_type");
                if (equip_weapon.Length != 0 && equip_weapon != weapon)
                {
                    errors.Add(
                        $"character_base[{TableValidator.RowId(row)}] 的起始装备 {equip_id} 是 "
                        + $"'{equip_weapon}' 类武器，但模板武器是 '{weapon}'：开局装不上，角色空手、招式也用不了"
                    );
                }

                if (TableValidator.Int(equip, "level_req") > start_level)
                {
                    errors.Add(
                        $"character_base[{TableValidator.RowId(row)}] 的起始装备 {equip_id} 要求 "
                        + $"{TableValidator.Int(equip, "level_req")} 级，而模板起始等级是 {start_level}：开局装不上"
                    );
                }
            }
        }
    }

    /// <summary>
    /// 非战斗技能（01 文档的「每个模板给出全部非战斗技能的初始等级」）：漏一行**不会报错**——
    /// `CharacterSheet.event_skill_level()` 找不到就返回 0，「忘了配」和「故意配 0 级」长得一模一样。
    /// 上限**不写死 10**：按 `event_skill_def.max_level` 逐条查。
    /// </summary>
    internal static void CheckCharacterEventSkills(TableDb db, List<string> errors)
    {
        var max_level_of = new Dictionary<string, int>();
        foreach (Resource row in TableValidator.Rows(db, "event_skill_def"))
        {
            max_level_of[TableValidator.Str(row, "skill_id")] = TableValidator.Int(row, "max_level");
        }

        var covered_by_char = new Dictionary<string, HashSet<string>>();
        foreach (Resource row in TableValidator.Rows(db, "character_base_skill"))
        {
            string char_id = TableValidator.Str(row, "char_id");
            string skill_id = TableValidator.Str(row, "skill_id");
            if (!covered_by_char.TryGetValue(char_id, out HashSet<string> covered))
            {
                covered = new HashSet<string>();
                covered_by_char[char_id] = covered;
            }

            covered.Add(skill_id);
            if (max_level_of.TryGetValue(skill_id, out int cap) && TableValidator.Int(row, "level") > cap)
            {
                errors.Add(
                    $"character_base_skill[{char_id}/{skill_id}].level={TableValidator.Int(row, "level")} "
                    + $"超过这部非战斗技能的上限 {cap}（event_skill_def.max_level）"
                );
            }
        }

        foreach (Resource char_row in TableValidator.Rows(db, "character_base"))
        {
            string char_id = TableValidator.Str(char_row, "char_id");
            covered_by_char.TryGetValue(char_id, out HashSet<string> covered);
            covered ??= new HashSet<string>();
            foreach (Resource skill_row in TableValidator.Rows(db, "event_skill_def"))
            {
                string skill_id = TableValidator.Str(skill_row, "skill_id");
                if (!covered.Contains(skill_id))
                {
                    errors.Add($"角色 {char_id} 缺少非战斗技能 {skill_id} 的初始等级（漏一行＝静默按 0 级算）");
                }
            }
        }
    }

    /// <summary>
    /// 招式用的伤害类型，代码必须真的支持——否则那招**永远没有按钮**（决策 228／229）。
    /// 唯一的支持名单在 BattleSimulator 里，这里只**读**它，不抄第二份。
    /// </summary>
    internal static void CheckSkillDamageTypesSupported(TableDb db, List<string> errors)
    {
        var reported = new HashSet<string>();
        foreach (Resource row in TableValidator.Rows(db, "skill_active"))
        {
            string type_id = TableValidator.Str(row, "damage_type");
            if (type_id.Length == 0)
            {
                continue; // 增益类招式本来就该留空（它靠 buff_grant 的 on_cast 生效）
            }

            if (BattleSimulator.is_supported_damage_type(db, type_id))
            {
                continue;
            }

            if (!reported.Add(type_id))
            {
                continue; // 同一个类型报一条就够，别刷屏
            }

            errors.Add(
                $"招式 {TableValidator.Str(row, "skill_id")} 的 damage_type='{type_id}' 代码不支持——"
                + "它在战斗里永远没有按钮（能学会、能装上、点不出来）"
            );
        }
    }

    /// <summary>
    /// 敌人装备的武器类型必须能匹配它的每一招（设计 0.23.0 的新门限 5.20）——
    /// `BattleSimulator._weapon_allows` 会**静默挡掉**配不上的那一招，运行期不报错。
    /// </summary>
    internal static void CheckEnemyWeaponMatchesSkills(TableDb db, List<string> errors)
    {
        var weapon_of_enemy = new Dictionary<string, string>();
        foreach (Resource row in TableValidator.Rows(db, "enemy_equip"))
        {
            if (TableValidator.Str(row, "slot_id") == "weapon")
            {
                weapon_of_enemy[TableValidator.Str(row, "enemy_id")] = TableValidator.Str(row, "equip_id");
            }
        }

        foreach (Resource row in TableValidator.Rows(db, "enemy_skill"))
        {
            string enemy_id = TableValidator.Str(row, "enemy_id");
            if (!weapon_of_enemy.TryGetValue(enemy_id, out string equip_id))
            {
                continue;
            }

            Resource equip = db.get_row("equip_base", equip_id);
            Resource skill = db.get_row("skill_base", TableValidator.Str(row, "skill_id"));
            if (equip == null || skill == null)
            {
                continue;
            }

            string have = TableValidator.Str(equip, "weapon_type");
            string need = TableValidator.Str(skill, "weapon_type");
            if (need.Length == 0 || need == "any")
            {
                continue;
            }

            if (have != need)
            {
                errors.Add(
                    $"enemy_skill[{enemy_id}] 的 {TableValidator.Str(row, "skill_id")} 要求武器「{need}」，"
                    + $"而它装备的是「{have}」——这一招会被静默挡掉"
                );
            }
        }
    }

    private static string[] PassivesOf(TableDb db, string enemy_id)
    {
        var passives = new List<string>();
        foreach (Resource passive in RowsWhere(db, "enemy_passive", "enemy_id", enemy_id))
        {
            string skill_id = TableValidator.Str(passive, "skill_id");
            if (skill_id.Length != 0)
            {
                passives.Add(skill_id);
            }
        }

        return passives.ToArray();
    }

    private static List<Resource> RowsWhere(TableDb db, string table, string column, string value)
    {
        var out_rows = new List<Resource>();
        foreach (Variant entry in db.rows_where(table, column, value))
        {
            out_rows.Add(entry.As<Resource>());
        }

        return out_rows;
    }

    /// <summary>`totals.get("wu", 0.0)` 的整数版（报错文案里用的是整数）。</summary>
    private static int AptitudeOf(Godot.Collections.Dictionary totals) => TotalsOf(totals, "wu");

    private static int TotalsOf(Godot.Collections.Dictionary totals, string key) =>
        totals.ContainsKey(key) ? (int)totals[key].AsDouble() : 0;

    /// <summary>`BUFF_TRIGGER_COMBOS` 的文案版（`_combo_text`）。</summary>
    internal static string ComboText(string source_type)
    {
        if (!BUFF_TRIGGER_COMBOS.TryGetValue(source_type, out string[] fired))
        {
            return $"{source_type} 这一档还没有任何触发点";
        }

        return TableValidator.Joined(fired);
    }

    /// <summary>`(source_type, trigger)` 是不是代码真的会读的组合。</summary>
    internal static bool IsFiredCombo(string source_type, string trigger)
    {
        return BUFF_TRIGGER_COMBOS.TryGetValue(source_type, out string[] fired)
            && System.Array.IndexOf(fired, trigger) >= 0;
    }
}
