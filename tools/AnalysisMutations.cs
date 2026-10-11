using Godot;
using Lunhui.Core;
using Lunhui.Data;

namespace Lunhui.Tools;

/// <summary>
/// 变异探针（C# 版，对位 `tools/analysis_mutations.gd`，211 行）——只读·手动工具，**不进验收**：
/// 把数据改坏，看**构建期校验器**抓不抓得住。
///
/// 为什么需要它：`TableValidator.cs` 里几十条规则是几十轮里一条条加出来的，而「规则写了但抓不住」
/// 和「规则没问题」在验收里长得一模一样（都是绿）。前两轮靠临时脚本扫，扫出过 rarities 区间、
/// 经验曲线下降、兜底常量分家等真洞（见 `docs/dev/框架说明.md` 决策 152／154／155）。
/// 这个工具把那套临时脚本固定下来：`tools\run_mutation_sweep.bat` 一起跑它和 PS1 那一半。
///
/// 做法：`TableDb` 载入真表 → **在内存里复制一份改坏**（**绝不碰 CSV／.tres**）→ 调 `validate()`
/// → 看有没有报出这条错。报错文案对不上也算「没抓住」，并原样打出来便于判断是规则漏了还是 case 写错了。
///
/// 用法：`tools\run_mutation_sweep.bat`（C# 工具进不了 `--script`，入口是场景 `tools/analysis_mutations_cs.tscn`）。
/// 退出码：0 = 每个 case 都被抓住；1 = 有 case 没被抓住（或 case 本身失效）。
/// </summary>
public partial class AnalysisMutations : Node
{
    /// <summary>
    /// 一条用例：`name`／`table`／（`id` 或 `index`）／（`column` 或 `columns`）／`value`／
    /// `expect`（报错里必须出现的片段）。
    ///
    /// 用例里的列名写成 **CSV 里的列名**（和策划看到的表一致），由 registry 映射到行类属性——
    /// 有些列两者不同名（`enemy_base.csv` 的 `exp` 在行类里叫 `exp_reward`），不映射就会误报 `[INVALID]`。
    /// 想一次改多列（比如「把一件良品装的加成全清零」）就用 `columns`（一起赋值）。
    /// 注意：case 写错时工具会报 `[INVALID]`，不会装作"抓住了"——这是刻意的（量不出来 ≠ 量过了）。
    /// </summary>
    private sealed class Case
    {
        public string name = "";
        public string table = "";
        public string id = "";
        public int index = -1;
        public string[] columns = System.Array.Empty<string>();
        public Variant value = default;
        public string expect = "";
    }

    /// <summary>按 id 找行（稳），改一列。</summary>
    private static Case ById(string name, string table, string id, string column, Variant value, string expect) =>
        new()
        {
            name = name, table = table, id = id,
            columns = new[] { column }, value = value, expect = expect,
        };

    /// <summary>按 id 找行（稳），一次改多列。</summary>
    private static Case ByIdCols(string name, string table, string id, string[] columns, Variant value, string expect) =>
        new() { name = name, table = table, id = id, columns = columns, value = value, expect = expect };

    /// <summary>按序号找行（省事，用于 id 是复合主键、懒得拼的表）。</summary>
    private static Case ByIndex(string name, string table, int index, string column, Variant value, string expect) =>
        new()
        {
            name = name, table = table, index = index,
            columns = new[] { column }, value = value, expect = expect,
        };

    private static readonly Case[] Cases =
    {
        ById("装备等级门槛为负", "equip_base", "eq_sword_01", "level_req", -1, "level_req"),
        ById("稀有度非法", "equip_base", "eq_sword_01", "rarity", "legendary", "rarity"),
        ById("槽位非法", "equip_base", "eq_sword_01", "slot", "bogus_slot", "slot"),
        ByIdCols("良品装备一条加成都没有", "equip_base", "eq_ring_03", new[]
        {
            "attr_str", "attr_con", "attr_agi", "attr_int", "attr_luk", "bonus_atk_phys", "bonus_atk_qi",
            "bonus_def_phys", "bonus_hp_max", "bonus_speed", "bonus_crit_rate", "bonus_pen_rate",
            "bonus_block_rate", "bonus_block_reduction", "bonus_dmg_reduction",
        }, 0, "一条加成都没有"),
        ById("词条条数区间倒挂", "rarity_def", "common", "affix_min", 9, "区间"),
        ById("经验门槛突然变便宜", "level_growth", "6", "exp_to_next", 1, "exp_to_next"),
        ById("非封顶级经验门槛为 0", "level_growth", "4", "exp_to_next", 0, "封顶"),
        ById("加点低于上一级", "level_growth", "6", "upgrade_points", 1, "upgrade_points"),
        ById("招式段数为 0", "skill_active", "sk_xuanwei_01", "hit_count", 0, "hit"),
        ById("物品堆叠上限为 0", "item_base", "item_iron", "stack_max", 0, "stack_max"),
        ById("物品类型非法", "item_base", "item_iron", "item_type", "bogus", "item_type"),
        ById("管线比率没有上限", "stat_def", "pen_rate", "max_value", 0, "硬上限"),
        ById("内伤抗性没写上限", "stat_def", "res_internal", "max_value", 0, "硬上限"),
        ById("槽位可佩戴数量为 0", "equip_slot_def", "weapon", "max_equip", 0, "max_equip"),
        ById("异常状态规则写错", "status_effect", "poison", "extra_rule", "rule_bogus", "extra_rule"),
        ById("掉落数量区间倒挂", "drop_table", "dr_boss_01", "qty_min", 9999, "区间"),
        ById("掉落指向不存在的装备", "drop_table", "dr_boss_01", "item_id", "eq_bogus", "eq_bogus"),
        ById("词条目标不存在", "affix_pool", "af_str_01", "target", "attr:bogus", "bogus"),
        ById("副本楼层为 0", "dungeon_room", "hf1_yard", "floor", 0, "floor"),
        ById("判定来源缺前缀", "event_check", "ev_gamble", "check_source", "luk", "check_source"),
        ById("敌人血量下限", "enemy_base", "en_wolf", "hp_base", 0, "hp_base"),
        // 设计 10 §二 连带规则①的**容量**那半边（2026-10-04 补的规则）：屠夫身上那部内功占格 2 → 3，
        // 3＋1＝4 格 > 它 8 级／根骨 8 的容量 3——构建期必须点名它
        ById("敌人内功超编（占格被写大）", "skill_passive", "pf_chensha_02", "slot_cost", 3, "内功超编"),
        // 装备图标列（A14，0.31.1）：`icon` 必须等于自己的 equip_id——写错不会报错，只会画出别人家的图标
        ById("装备图标列指向别人", "equip_base", "eq_sword_02", "icon", "eq_sword_01", "icon"),
        ById("内功被动加成为 0", "skill_passive_stat", "pf_xuanwei_01|stat:qi_max", "value", 0, "白填"),
        ById("困难档比普通还弱", "difficulty_config", "hard", "enemy_atk_mul", 0.5, "难度越高数值反而越小"),
        ById("难度解锁短语写错", "difficulty_config", "nightmare", "unlock_condition",
            "通关第一章并击败醉刀客（误）", "永远解锁不了"),
        ById("成长常数被改名（代码依赖它）", "growth_const", "mastery_max", "const_id", "mastery_max_typo",
            "缺少代码依赖的常数"),
        ById("起始装备与模板武器不符", "character_base", "scholar_fallen", "start_equip_ids", "eq_fist_01",
            "开局装不上"),
        ById("打坐费用底数为负", "growth_const", "cultivate_cost_growth", "value", -1.6, "小于可用下限"),
        ById("招式槽除数被写成 0", "growth_const", "active_slot_lv_div", "value", 0, "小于可用下限"),
        // —— 下面这批是「按序号挑行」的广覆盖用例：只要求这张表报错，用来找**还不存在的规则** ——
        ByIndex("招式内力消耗为负", "skill_active", 1, "qi_cost", -5, "qi_cost"),
        ByIndex("招式冷却为负", "skill_active", 1, "cooldown", -3, "cooldown"),
        ByIndex("内功占格为 0", "skill_passive", 1, "slot_cost", 0, "slot_cost"),
        ByIndex("内功占格超过 3", "skill_passive", 1, "slot_cost", 9, "slot_cost"),
        ByIndex("星级成长系数为负", "skill_star_def", 1, "mastery_gain", -0.5, "mastery_gain"),
        ByIndex("状态层数上限为 0", "status_effect", 1, "max_stack", 0, "max_stack"),
        ByIndex("状态持续 0 回合", "status_effect", 1, "duration", 0, "duration"),
        ByIndex("状态触发率超过 1", "status_effect", 1, "base_chance", 1.5, "base_chance"),
        ByIndex("槽位排序为 0", "equip_slot_def", 1, "sort_order", 0, "sort_order"),
        ByIndex("词条权重为 0", "affix_pool", 1, "weight", 0, "weight"),
        ByIndex("物品售价为负", "item_base", 1, "sell_price", -5, "sell_price"),
        ByIndex("货架限量为负", "shop_stock", 1, "stock_limit", -1, "stock_limit"),
        ByIndex("敌人铜钱为负", "enemy_base", 1, "money", -10, "money"),
        // —— 增益与套装（08）：这 16 条盯的是 2026-10-03 才补进**构建期**那一侧的规则（决策 218）——
        // 补之前它们只有 PS1 会报，也就是「跑验收才发现」；现在每一条都在构建期当场报出来。
        ById("buff 作用域非法", "buff_def", "buff_guard", "scope", "bogus", "scope"),
        ById("buff 叠加规则非法", "buff_def", "buff_guard", "stack_rule", "bogus", "stack_rule"),
        ById("buff 效果类型非法", "buff_def", "buff_guard", "effect_kind", "bogus", "effect_kind"),
        ById("数值型 buff 一条加成都没有", "buff_def", "buff_yunqi", "effect_kind", "stat", "一条加成都没有"),
        ById("可叠层 buff 的 max_stack 只有 1", "buff_def", "buff_poison_amp", "max_stack", 1, "max_stack"),
        ById("buff 持续写成 -2", "buff_def", "buff_guard", "duration", -2, "duration"),
        ById("buff 修正缺 attr:/stat: 前缀", "buff_stat", "buff_guard|stat:dmg_reduction", "target",
            "dmg_reduction", "前缀"),
        // expect 要写**我自己那条规则的话**：改 target 会连带改掉复合主键，主键一致性检查也会报一条含有
        // `stat:bogus_stat` 的错——只写 `bogus_stat` 的话会被那条蒙混过关（第一版就是这样，读打印才发现）
        ById("buff 修正指向不存在的派生数值", "buff_stat", "buff_guard|stat:dmg_reduction", "target",
            "stat:bogus_stat", "引用的派生数值不存在"),
        ById("buff 发放来源非法", "buff_grant", "gr_pf_xuanwei_01", "source_type", "bogus", "source_type"),
        ById("buff 发放的来源 id 不存在", "buff_grant", "gr_pf_xuanwei_01", "source_id", "pf_bogus", "pf_bogus"),
        ById("某部内功没有 on_cast（内功指令点它没反应）", "buff_grant", "gr_pf_xuanwei_05", "source_id",
            "pf_xuanwei_04", "pf_xuanwei_05"),
        ById("套装类别非法", "set_def", "set_heifeng", "set_kind", "bogus", "set_kind"),
        ById("套装成员类型不符", "set_member", "set_heifeng|eq_sword_03", "member_id", "sk_xuanwei_01",
            "不是合法的 equip"),
        ById("套装档位写成 0", "set_bonus", "set_heifeng|2", "required_count", 0, "正整数"),
        ById("套装档位永远凑不齐", "set_bonus", "set_xuanwei_sword|3", "required_count", 99, "凑不齐"),
        ById("全局开关取值不是 0/1", "feature_toggle", "overworld_roaming_enemy", "value", 2, "value"),
        // —— 非战斗技能（01 的「每个模板给出全部非战斗技能初始等级」，2026-10-03 才补进构建期）——
        ById("角色漏配一项非战斗技能", "character_base_skill", "scholar_fallen|wenxue", "skill_id",
            "qimen", "缺少非战斗技能"),
        ById("非战斗技能等级超过上限", "character_base_skill", "scholar_fallen|wenxue", "level", 99,
            "超过这部非战斗技能的上限"),
        // —— buff_grant 的「这一行会不会触发」：写完时机的表必须真的被代码读（2026-10-03 补的规则）——
        ById("内功挂了一个没人读的时机", "buff_grant", "gr_pf_xuanwei_01", "trigger", "on_hit", "永远不会触发"),
        // 单列改就够：把来源改成 `event`，而 `event` 这一档代码里没有任何触发点（组合表里没有它）
        ById("event 来源的 buff（代码里还没有这个时机）", "buff_grant", "gr_pf_xuanwei_01", "source_type",
            "event", "永远不会触发"),
        // 招式配了代码不支持的伤害类型 → 那招永远没有按钮（2026-10-03 的 DoT 事故就是这一类的反面：
        // 当时是「代码过滤把 dot 当不支持」，而数据侧完全合法——这条规则能把它当场抓出来）
        ById("招式配了代码不支持的伤害类型", "skill_active", "sk_xuanwei_01", "damage_type", "dmg_reflect",
            "永远没有按钮"),
    };

    // 探针里**有意没有**的几类（写清楚，免得下轮又当洞，见框架说明决策 157）：
    //   · `skill_active.power_ratio = 0`、`difficulty_drop_rate.rate_multiplier = 0`：由 **PS1 那一半**管
    //     （5.21 招式效果警告 / 难度倍率检查），所以它们的 case 在 analysis_mutations.ps1 里。
    //   · `enemy_base.exp = 0`、`roaming_spawn.alert_radius = 0`：0 是否能算"合法的无害敌人/被动位点"
    //     属于设计口径，开发侧不编规则，只登记。
    //   · `rarity_def.drop_weight = 0`：这一列**运行时没人读**（已登记在待设计清单里），改动没有效果。
    //   · `growth_const.value = 0`：不同常数 0 的含义不同，写不出通用规则（缺常数由另一条规则管）。

    public override void _Ready() =>
        Callable.From(() => GetTree().Quit(Run())).CallDeferred();

    private static int Run()
    {
        GD.Print("=== 变异探针：构建期校验器 ===");
        var missed = new System.Collections.Generic.List<string>();
        var invalid = new System.Collections.Generic.List<string>();
        foreach (Case mutation in Cases)
        {
            string verdict = _run_case(mutation);
            if (verdict == "invalid")
            {
                invalid.Add(mutation.name);
            }
            else if (verdict == "missed")
            {
                missed.Add(mutation.name);
            }
        }

        GD.Print($"MUTATIONS: {Cases.Length} 个 case，漏 {missed.Count}，case 失效 {invalid.Count}");
        if (missed.Count > 0)
        {
            GD.Print("MUTATIONS MISSED: " + string.Join(", ", missed));
        }

        if (invalid.Count > 0)
        {
            GD.Print("MUTATIONS INVALID: " + string.Join(", ", invalid));
        }

        // ASCII 判定行给 findstr 用（中文在 cmd 控制台代码页下匹配不到）
        bool ok = missed.Count == 0 && invalid.Count == 0;
        GD.Print(ok ? "MUTATIONS: OK" : "MUTATIONS: FAILED");
        return ok ? 0 : 1;
    }

    /// <summary>返回 "caught" / "missed" / "invalid"。</summary>
    private static string _run_case(Case mutation)
    {
        var db = new TableDb();
        db.load_all();
        if (db.errors.Length > 0)
        {
            GD.Print($"  [SKIP] 表加载不了：{string.Join(", ", db.errors)}");
            return "invalid";
        }

        string tableName = mutation.table;
        // 用例必须写明"该报哪条错"：只要求"这张表报错"的话，**别的**错也能让它蒙混过关
        // （2026-10-03 把 13 个空 expect 的用例逐个补上，并在这里强制）。
        if (mutation.expect.Length == 0)
        {
            GD.Print($"  [INVALID] 用例 {mutation.name} 没写 expect（该报哪条错）");
            return "invalid";
        }

        TableResource table = db.tables.ContainsKey(tableName)
            ? db.tables[tableName].AsGodotObject() as TableResource
            : null;
        if (table == null)
        {
            GD.Print($"  [INVALID] 没有表 {tableName}");
            return "invalid";
        }

        var copy = (TableResource)table.Duplicate(true);
        // 行可以按 id 找（稳），也可以按序号找（省事，用于 id 是复合主键、懒得拼的表）
        Resource target = null;
        if (mutation.index >= 0)
        {
            if (mutation.index < copy.rows.Count)
            {
                target = copy.rows[mutation.index].AsGodotObject() as Resource;
            }
        }
        else
        {
            target = _find_row(copy, mutation.id);
        }

        if (target == null)
        {
            GD.Print($"  [INVALID] {tableName} 里找不到目标行（id={mutation.id} index={mutation.index}）");
            return "invalid";
        }

        var mapped = new System.Collections.Generic.List<string>();
        foreach (string column in mutation.columns)
        {
            string property = TableRegistry.property_of(tableName, column);
            if (!_has_property(target, property))
            {
                GD.Print($"  [INVALID] {tableName} 没有列 {column}");
                return "invalid";
            }

            mapped.Add(property);
            target.Set(property, mutation.value);
        }

        string columnList = string.Join(", ", mutation.columns);
        db.tables[tableName] = copy;

        string[] errors = TableValidator.validate(db);
        bool caught = false;
        string matched = "";
        foreach (string message in errors)
        {
            if (message.Contains(mutation.expect) && matched.Length == 0)
            {
                caught = true;
                matched = message;
            }
        }

        if (caught)
        {
            // 把**匹配到的那条错**打出来：用途是核对"抓对了没有"。
            GD.Print($"  [OK]   {mutation.name} ← {(matched.Length <= 60 ? matched : matched.Substring(0, 60))}");
            return "caught";
        }

        GD.Print($"  *** 没抓住 *** {mutation.name}（{tableName}：{columnList}，总错误 {errors.Length}）");
        return "missed";
    }

    /// <summary>主键列拼出的 id 直接存在行上（`row.id`），不用再猜它由哪几列拼成。</summary>
    private static Resource _find_row(TableResource table, string row_id)
    {
        foreach (Variant entry in table.rows)
        {
            if (entry.AsGodotObject() is Resource row && row.Get("id").AsString() == row_id)
            {
                return row;
            }
        }

        return null;
    }

    private static bool _has_property(Resource row, string column)
    {
        foreach (Godot.Collections.Dictionary property in row.GetPropertyList())
        {
            if (property["name"].AsString() == column)
            {
                return true;
            }
        }

        return false;
    }
}