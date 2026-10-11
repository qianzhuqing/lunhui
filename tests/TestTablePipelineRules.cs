using System;
using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 数据管线（二·负例）：这一份是「表写错了会怎样」的账本。
///
/// 每一节都把真表深拷贝一份、只改坏**一格或一行**，然后要求 `TableValidator` 报出来——
/// 报的都是「改了不报错、只在游戏里静默出坏手感」的那几类（数值反向、永远解锁不了、
/// 开局装不上、槽位重复、词条白填…，来由逐条写在每段的注释里）。
/// （对位 `tests/test_table_pipeline.gd` 的后四节，2026-10-11 P4 第三十七批整体搬过来；
/// 正例在 `tests/TestTablePipeline.cs`，两族共用 `TestTablePipelineBase`。）
/// </summary>
public partial class TestTablePipelineRules : TestTablePipelineBase
{
    public override string SuiteName() => "数据管线负例";

    public override void Run()
    {
        _CheckBrokenReferenceIsCaught();
        _CheckShopAndSlotNegatives();
        _CheckStaleIndexIsCaught();
        _CheckGrowthAndRarityNegatives();
    }

    /// <summary>故意写坏一条引用，校验必须报错（保证校验不是摆设）。</summary>
    private void _CheckBrokenReferenceIsCaught()
    {
        TableDb stub = Stub();
        CheckNotNull(
            stub.tables.ContainsKey("attr_to_stat") ? stub.tables["attr_to_stat"].AsGodotObject() : null,
            "attr_to_stat 已加载");

        TableDb broken = Broken(
            "attr_to_stat", rows => Row<AttrToStatRow>(rows, 0).attr_id = "not_an_attribute");
        CheckGt(TableValidator.validate(broken).Length, 0.0, "写坏引用后校验应报错");

        TableDb brokenPk = Broken("enemy_base", rows =>
        {
            Row<EnemyBaseRow>(rows, 1).enemy_id = Row<EnemyBaseRow>(rows, 0).enemy_id;
        });
        CheckGt(TableValidator.validate(brokenPk).Length, 0.0, "主键重复应被校验抓到");

        // 属性总数与五维对不上，必须报错
        TableDb brokenAttr = Broken("character_base", rows => Row<CharacterBaseRow>(rows, 0).attr_total = 99);
        CheckGt(TableValidator.validate(brokenAttr).Length, 0.0, "attr_total 与五维对不上应被校验抓到");

        // 事件判定来源写错前缀，必须报错
        TableDb brokenEvent = Broken("event_check", rows => Row<EventCheckRow>(rows, 0).check_source = "qimen");
        CheckGt(TableValidator.validate(brokenEvent).Length, 0.0, "事件判定缺前缀应被校验抓到");
    }

    /// <summary>
    /// 生成资源的 `index` 与 `rows` 对不上（有人手改 .tres、或行被删过没重建索引）时，
    /// 校验必须逐行点出来**并且不中断**——`rows[position]` 越界会当场报运行期错误、把整段主键检查掐断，
    /// 排在这张表后面的表一条都不再查（以前 `level_growth` 一旦坏，后面 24 张表的主键检查全部静默跳过；
    /// 见框架说明决策 147）。所以这里同时弄坏两张表，要求两张都被点名。
    /// </summary>
    private void _CheckStaleIndexIsCaught()
    {
        TableDb broken = Stub();
        DropFirstRowKeepIndex(broken, "level_growth");
        DropFirstRowKeepIndex(broken, "item_base");
        var named = new System.Collections.Generic.List<string>();
        int staleCount = 0;
        foreach (string message in TableValidator.validate(broken))
        {
            if (!message.Contains("index 与 rows 不一致"))
            {
                continue;
            }

            staleCount++;
            string tableName = message.Split(' ')[0];
            if (!named.Contains(tableName))
            {
                named.Add(tableName);
            }
        }

        CheckTrue(named.Contains("level_growth"), $"level_growth 的 index 错位应被点名（实报：{Join(named)}）");
        CheckTrue(
            named.Contains("item_base"),
            $"排在其后的 item_base 也要被查到——越界曾把整段主键检查掐断（实报：{Join(named)}）");
        // 两张表各删一行、index 未重建：每张表被删行之后的每一行都错位，条数必然 > 2
        CheckGt(staleCount, 2.0, $"错位要逐行点出，不能只报一条（实报 {staleCount} 条）");
    }

    /// <summary>
    /// 两条「写错了不报错」的规则（2026-10-03 用变异探针扫出来的，见框架说明决策 152）：
    /// ① `rarity_def` 的条数区间写成 min>max —— `AffixRoller.affix_count()` 里有 `maxi(low, high)` 兜着，
    ///    所以**不会炸**，只是每件装备都只出 min 条：分布没了，界面上一点看不出来；
    /// ② `level_growth.exp_to_next` 突然变便宜 —— 经验池照涨、付得起就升，没人检查它合不合理。
    /// 同时钉住「封顶级」的例外：最高等级用 0 表示「没有下一级」，不许被误判成白送一级。
    /// </summary>
    private void _CheckGrowthAndRarityNegatives()
    {
        TableDb brokenRarity = Broken("rarity_def", rows => Row<RarityDefRow>(rows, 0).affix_min = 9);
        CheckTrue(
            HasAll(TableValidator.validate(brokenRarity), "词条数量区间不合法"),
            "词条条数区间写成 min>max 应被校验抓到");

        TableDb brokenExp = Broken(
            "level_growth", rows => Row<LevelGrowthRow>(rows, 5).exp_to_next = 1); // 第 6 级，门槛反而比第 5 级低
        CheckTrue(
            HasAll(TableValidator.validate(brokenExp), "exp_to_next", "还低"),
            "exp_to_next 低于上一级应被校验抓到");

        TableDb brokenZero = Broken(
            "level_growth", rows => Row<LevelGrowthRow>(rows, 3).exp_to_next = 0); // 第 4 级不是封顶级
        CheckTrue(
            HasAll(TableValidator.validate(brokenZero), "不是封顶级"),
            "非封顶级的 exp_to_next=0（白送一级）应被校验抓到");

        // 管线依赖的四个比率「必须写上上限」：max_value 留空（= 0 = 本表约定「无上限」）时代码会
        // **静默换用兜底常数**（穿透/格挡 0.75、格挡减伤 0.8、减伤 0.6）——表与行为分家且无人报错。
        TableDb brokenCap = Broken("stat_def", rows =>
        {
            StatDefRow row = First<StatDefRow>(rows, item => item.stat_id == "pen_rate");
            if (row != null)
            {
                row.max_value = 0.0;
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(brokenCap), "pen_rate", "硬上限"),
            "管线用的比率没写上限（留空='无上限'）应被校验抓到");

        // 「造了一个永远出不了手的角色」：起手招式要的武器与模板武器对不上（真实案例见决策 162——
        // 01 的备选模板里 ch_gang 拿刀配拳法、ch_qi 拿拳配剑法，轮到出手时一个按钮都没有）。
        TableDb brokenStart = Broken("character_base", rows => Row<CharacterBaseRow>(rows, 0).weapon_type = "fist");
        CheckTrue(
            HasAll(TableValidator.validate(brokenStart), "一部能用的起手招式都没有"),
            "起手招式与模板武器对不上应被校验抓到");

        // 难度阶梯：更高难度不许更弱（原先只查「> 0」，把困难档写成比普通还弱也没人管）
        TableDb weakHard = Broken("difficulty_config", rows =>
        {
            DifficultyConfigRow row = First<DifficultyConfigRow>(rows, item => item.id == "hard");
            if (row != null)
            {
                row.enemy_atk_mul = 0.5;
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(weakHard), "难度越高数值反而越小"),
            "高难度倍率低于上一档应被校验抓到");

        // 解锁短语必须被代码认得：写错一个字的短语 = 这一档永远解锁不了
        TableDb badUnlock = Broken("difficulty_config", rows =>
        {
            DifficultyConfigRow row = First<DifficultyConfigRow>(rows, item => item.id == "nightmare");
            if (row != null)
            {
                row.unlock_condition = "通关第一章并击败醉刀客（误）";
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(badUnlock), "永远解锁不了"),
            "没登记的难度解锁短语应被校验抓到");

        // 代码点了名的成长常数，表里少一个就该报（缺行只会静默用兜底值）
        TableDb missingConst = Broken("growth_const", rows =>
        {
            GrowthConstRow row = First<GrowthConstRow>(rows, item => item.const_id == "mastery_max");
            if (row != null)
            {
                row.const_id = "mastery_max_typo";
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(missingConst), "缺少代码依赖的常数 'mastery_max'"),
            "growth_const 少了代码依赖的常数应被校验抓到");

        // 起始装备"装不上"：武器类型不符 / 等级门槛高于起始等级（两条都是"开局空手"的死路）
        TableDb badWeaponEquip = Broken(
            "character_base", rows => Row<CharacterBaseRow>(rows, 0).start_equip_ids = "eq_fist_01"); // 模板是剑，配拳套
        CheckTrue(
            HasAll(TableValidator.validate(badWeaponEquip), "开局装不上"),
            "起始装备与模板武器类型不符应被校验抓到");

        TableDb highLevelEquip = Broken("equip_base", rows =>
        {
            EquipBaseRow row = First<EquipBaseRow>(rows, item => item.id == "eq_sword_01");
            if (row != null)
            {
                row.level_req = 9;
            }
        });
        CheckTrue(
            HasAll(TableValidator.validate(highLevelEquip), "开局装不上"),
            "起始装备等级门槛高于起始等级应被校验抓到");

        // 成长常数的「可用下限」：低于代码守卫的下限会被静默换掉（除数→1.0；费用底数负数→负费用）
        TableDb badGrowth = Broken("growth_const", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is not GrowthConstRow row)
                {
                    continue;
                }

                if (row.const_id == "cultivate_cost_growth")
                {
                    row.value = -1.6;
                }
                else if (row.const_id == "active_slot_lv_div")
                {
                    row.value = 0;
                }
            }
        });
        CheckEq(
            CountWith(TableValidator.validate(badGrowth), "小于可用下限"), 2,
            "两条越界的成长常数各报一条");

        // 五条变异探针扫出来的下限/区间（决策 157）：0 层状态、0 回合持续、>100% 触发率、
        // 负售价、负铜钱——今天两边都没有规则，改了没有任何反应。
        _CheckScannedRangeRules();

        // **校验器自己的规则表自检**（规则里点到的表/列必须真实存在）
        CheckValidatorRuleTargets(new TableDb());
    }

    /// <summary>变异探针扫出来的五条边界规则：每条都要能真的报错（决策 157）。</summary>
    private void _CheckScannedRangeRules()
    {
        CheckOneCellCaught("status_effect", "poison", "max_stack", 0, "小于下限");
        CheckOneCellCaught("status_effect", "poison", "duration", 0, "小于下限");
        CheckOneCellCaught("status_effect", "poison", "base_chance", 1.5, "大于上限");
        CheckOneCellCaught("item_base", "item_iron", "sell_price", -5, "小于下限");
        CheckOneCellCaught("enemy_base", "en_wolf", "money", -10, "小于下限");
    }

    /// <summary>0.5.0 新增的四条规则 ＋ 后面几轮补的几条，每条都要能真的报错。</summary>
    private void _CheckShopAndSlotNegatives()
    {
        TableDb brokenSlot = Broken("equip_base", rows => Row<EquipBaseRow>(rows, 0).slot = "boots");
        CheckGt(TableValidator.validate(brokenSlot).Length, 0.0, "非法槽位应被校验抓到");

        TableDb brokenPrice = Broken("shop_stock", rows =>
        {
            ShopStockRow row = Row<ShopStockRow>(rows, 0);
            row.sell_price = row.buy_price;
        });
        CheckGt(TableValidator.validate(brokenPrice).Length, 0.0, "卖出价不低于买入价应被校验抓到");

        TableDb brokenUse = Broken("item_base", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is ItemBaseRow row && row.item_id == "item_med_01")
                {
                    row.use_context = "arena";
                }
            }
        });
        CheckGt(TableValidator.validate(brokenUse).Length, 0.0, "use_context 非法值应被校验抓到");

        TableDb brokenWeapon = Broken("equip_base", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is EquipBaseRow row && row.slot == "weapon")
                {
                    row.weapon_type = "";
                    break;
                }
            }
        });
        CheckGt(TableValidator.validate(brokenWeapon).Length, 0.0, "武器槽缺 weapon_type 应被校验抓到");

        // 异常状态的特殊规则：写错一个字母（rule_burn_def_downd）时，效果会静默丢失，
        // 所以构建期就要拦下来——允许清单来自 battle_actor.KNOWN_STATUS_RULES，不手抄。
        TableDb brokenRule = Broken("status_effect", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is StatusEffectRow row && row.status_id == "burn")
                {
                    row.extra_rule = "rule_burn_def_downd";
                }
            }
        });
        string[] ruleErrors = TableValidator.validate(brokenRule);
        CheckGt(ruleErrors.Length, 0.0, $"extra_rule 写错应被校验抓到：{Join(ruleErrors)}");
        CheckTrue(
            HasAll(ruleErrors, "extra_rule"), $"报错要点名 extra_rule：{Join(ruleErrors)}");

        // 隐藏触发的条件键写错：后果不对称（behavior 会当成条件已满足直接发奖励），
        // 所以构建期就要拦下来。这里把 `flag_stealth_full` 拼成 `flag_stealth_fullx`。
        TableDb brokenCond = Broken("hidden_trigger", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is HiddenTriggerRow row && row.trigger_id == "trig_stealth_clear")
                {
                    row.required_condition = "flag_stealth_fullx=1";
                }
            }
        });
        string[] condErrors = TableValidator.validate(brokenCond);
        CheckTrue(
            HasAll(condErrors, "flag_stealth_fullx"), $"条件键拼错应被校验抓到：{Join(condErrors)}");

        // 击杀方式的值也要校验（写个不存在的死因 → 那条隐藏内容永远拿不到）
        TableDb brokenStyle = Broken("hidden_trigger", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is HiddenTriggerRow row && row.trigger_id == "trig_poison_kill")
                {
                    row.required_condition = "kill_with=poisoned";
                }
            }
        });
        string[] styleErrors = TableValidator.validate(brokenStyle);
        CheckTrue(HasAll(styleErrors, "kill_with"), $"非法击杀方式应被校验抓到：{Join(styleErrors)}");

        // AI 模板拼错：那个敌人会一套招都没有、每回合空过（难度静默塌掉）。
        // 允许清单是 EnemyFactory.AI_SKILL_MAP 的键（单一真相），这里把 ai_basic 拼成 ai_basics。
        TableDb brokenAi = Broken("enemy_base", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is EnemyBaseRow row && row.ai_template == "ai_basic")
                {
                    row.ai_template = "ai_basics";
                    break;
                }
            }
        });
        string[] aiErrors = TableValidator.validate(brokenAi);
        CheckTrue(HasAll(aiErrors, "ai_basics"), $"ai_template 拼错应被校验抓到：{Join(aiErrors)}");

        // 攻击招式写成 0 段：界面会给按钮（`is_attack()` 为真），但结算一段都跑不到——能用却零伤害。
        TableDb brokenHits = Broken("skill_active", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is SkillActiveRow row && row.skill_id == "sk_xuanwei_01")
                {
                    row.hit_count = 0;
                    break;
                }
            }
        });
        string[] hitErrors = TableValidator.validate(brokenHits);
        CheckTrue(
            HasAll(hitErrors, "sk_xuanwei_01", "hit_count"),
            $"攻击招式的 hit_count=0 应被校验抓到：{Join(hitErrors)}");
        // 反过来：只把「还没配效果」的空壳（类型与倍率都是 0）留着不算错——它有别的门限盯着
        foreach (Variant entry in brokenHits.rows("skill_active"))
        {
            if (entry.AsGodotObject() is SkillActiveRow row && row.skill_id == "sk_xuanwei_01")
            {
                row.hit_count = 1;
                break;
            }
        }

        CheckEq(TableValidator.validate(brokenHits).Length, 0, "改回 1 段后校验恢复零错误");

        // 负的 poise_damage：被 `maxi(amount, 0)` 静默吃掉，招式看着能破架势、实际一点都破不了
        TableDb brokenPoise = Broken("skill_active", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is SkillActiveRow row && row.skill_id == "sk_chensha_01")
                {
                    row.poise_damage = -5;
                    break;
                }
            }
        });
        CheckTrue(HasAll(TableValidator.validate(brokenPoise), "poise_damage"), "负的 poise_damage 应被校验抓到");

        // 起始等级越界：0 被静默当成 1 级；超过等级上限则找不到 level_growth 那一行
        TableDb brokenLevel = Broken("character_base", rows => Row<CharacterBaseRow>(rows, 0).start_level = 0);
        CheckTrue(
            HasAll(TableValidator.validate(brokenLevel), "start_level"), "start_level < 1 应被校验抓到");
        Row<CharacterBaseRow>(brokenLevel.rows("character_base"), 0).start_level =
            brokenLevel.rows("level_growth").Count + 1;
        CheckTrue(
            HasAll(TableValidator.validate(brokenLevel), "超过等级上限"),
            "start_level 超过等级上限应被校验抓到");

        // 负的异常几率倍率：掷点永远掷不中（招式看着能上毒、实际一次都上不了）
        TableDb brokenStatus = Broken(
            "skill_active", rows => Row<SkillActiveRow>(rows, 0).status_chance_mul = -1.0);
        CheckTrue(
            HasAll(TableValidator.validate(brokenStatus), "status_chance_mul"),
            "负的 status_chance_mul 应被校验抓到");

        // 负的经验门槛 / 负的加点：前者让等级一路跳到上限，后者让面板出现「未分配点数：-3」
        TableDb brokenGrowthTable = Broken("level_growth", rows =>
        {
            LevelGrowthRow row = Row<LevelGrowthRow>(rows, 0);
            row.exp_to_next = -1;
            row.upgrade_points = -2;
        });
        int growthNamed = 0;
        foreach (string message in TableValidator.validate(brokenGrowthTable))
        {
            if (message.Contains("exp_to_next") || message.Contains("upgrade_points"))
            {
                growthNamed++;
            }
        }

        CheckEq(growthNamed, 2, "负的 exp_to_next 与 upgrade_points 各报一条");

        // 同一个 drop_group 里 slot 重复：`DropResolver` 按 slot 做**不稳定排序**，
        // 两个同号槽的先后就不确定了——「按 drop_group 顺序判定槽位」静默失效
        // （保底键按行 id，不受影响，更难发现）
        TableDb brokenSlots = Broken("drop_table", rows =>
        {
            if (rows.Count > 1)
            {
                DropTableRow first = Row<DropTableRow>(rows, 0);
                DropTableRow second = Row<DropTableRow>(rows, 1);
                second.drop_group = first.drop_group;
                second.slot = first.slot;
            }
        });
        CheckGt(brokenSlots.rows("drop_table").Count, 1.0, "掉落表至少两行才好做这个测试");
        CheckTrue(
            HasAll(TableValidator.validate(brokenSlots), "槽位", "重复"), "同一组里 slot 重复应被校验抓到");

        // level_growth 缺一行：升级会静默卡在缺号前一级（`get_row` 取不到 → 当成「这一级不要经验」）
        TableDb brokenLevels = Broken(
            "level_growth", rows => RemoveRows<LevelGrowthRow>(rows, row => row.level == 7));
        CheckTrue(HasAll(TableValidator.validate(brokenLevels), "缺 7 级"), "level_growth 缺号应被校验抓到");

        // 词条目标写错：前缀漏写 / 目标不存在 / value_kind 与前缀对不上——
        // 三种都会让词条「看起来有用、实际一点不加」，而且没有任何报错
        TableDb brokenAffix = Broken("affix_pool", rows =>
        {
            Row<AffixPoolRow>(rows, 0).target = "str"; // 漏前缀
            Row<AffixPoolRow>(rows, 1).target = "attr:strength"; // 目标不存在
            Row<AffixPoolRow>(rows, 2).value_kind = "flat"; // attr: 却写 flat
        });
        var affixErrors = new System.Collections.Generic.List<string>();
        foreach (string message in TableValidator.validate(brokenAffix))
        {
            if (message.Contains("affix_pool"))
            {
                affixErrors.Add(message);
            }
        }

        CheckEq(affixErrors.Count, 3, $"三种词条目标错各报一条：{Join(affixErrors)}");

        // 升级不该让人变弱：把某一级的基础气血写得比上一级低 → 校验必须点名
        TableDb brokenBase = Broken("level_growth", rows =>
        {
            LevelGrowthRow row = First<LevelGrowthRow>(rows, item => item.level == 6);
            if (row != null)
            {
                row.base_hp = 1;
            }
        });
        CheckTrue(HasAll(TableValidator.validate(brokenBase), "升级会变弱"), "基础数值随等级下降应被校验抓到");

        // 装备等级需求超过上限：永远穿不上（背包里一直写「需要 N 级」，等到满级也没用）
        TableDb reqStub = Stub();
        int topLevel = TableValidator.max_level(reqStub);
        CheckGt(topLevel, 0.0, $"等级上限算得出来（{topLevel}）");
        TableDb brokenReq = Broken("equip_base", rows =>
        {
            Row<EquipBaseRow>(rows, 0).level_req = topLevel + 5;
            Row<EquipBaseRow>(rows, 1).level_req = 0;
        });
        CheckEq(
            CountWith(TableValidator.validate(brokenReq), "level_req"), 2,
            "超上限与低于 1 级各报一条");

        // 敌人战斗数值：负防御会让伤害公式反向；比率超过 1 没有意义
        TableDb brokenEnemy = Broken("enemy_base", rows =>
        {
            EnemyBaseRow row = Row<EnemyBaseRow>(rows, 0);
            row.def_phys = -5;
            // 0.14.0 起 `res_poison` 那三条特权列取消了（敌人要抗性就装内功），
            // 上限检查改用**保留下来**的 `res_internal`
            row.res_internal = 1.5;
        });
        CheckEq(
            CountWith(TableValidator.validate(brokenEnemy), "enemy_base"), 2,
            "负防御与超额抗性各报一条");

        // 难度倍率必须严格为正：0 倍血 = 敌人一出生就死；0 倍经验 = 这个难度白练
        TableDb brokenDiff = Broken("difficulty_config", rows =>
        {
            DifficultyConfigRow row = Row<DifficultyConfigRow>(rows, 0);
            row.enemy_hp_mul = 0.0;
            row.exp_mul = -1.0;
        });
        CheckEq(
            CountWith(TableValidator.validate(brokenDiff), "difficulty_config"), 2,
            "0 倍血与负经验倍率各报一条");

        // 非白板装备全零加成 = 和「白板」没区别；内功加成的 value=0 = 白填
        var wiped = false;
        TableDb brokenBonus = Broken("equip_base", rows =>
        {
            foreach (Variant entry in rows)
            {
                if (entry.AsGodotObject() is not EquipBaseRow row || row.rarity == "common")
                {
                    continue;
                }

                row.attr_str = 0;
                row.attr_con = 0;
                row.attr_agi = 0;
                row.attr_int = 0;
                row.attr_luk = 0;
                row.bonus_atk_phys = 0;
                row.bonus_atk_qi = 0;
                row.bonus_def_phys = 0;
                row.bonus_hp_max = 0;
                row.bonus_speed = 0;
                row.bonus_crit_rate = 0;
                row.bonus_pen_rate = 0;
                row.bonus_block_rate = 0;
                row.bonus_block_reduction = 0;
                row.bonus_dmg_reduction = 0;
                wiped = true;
                break;
            }
        });
        // 反例保底：找不到一件非白板装备就说明夹具失效了，别让它静默变成"这条没测"
        CheckTrue(wiped, "找到了一件非白板装备用来做这个测试");
        string[] bonusErrors = TableValidator.validate(brokenBonus);
        CheckTrue(
            HasAll(bonusErrors, "和白板") || HasAll(bonusErrors, "一条加成都没有"),
            "非白板装备全零加成应被校验抓到");

        TableDb brokenPassive = Broken(
            "skill_passive_stat", rows => Row<SkillPassiveStatRow>(rows, 0).value = 0.0);
        CheckTrue(HasAll(TableValidator.validate(brokenPassive), "白填"), "内功加成 value=0 应被校验抓到");

        // 楼层号：0 层会被面板显示成「第 0 层」；缺一层会让完成度面板少一行
        TableDb brokenFloor = Broken("dungeon_room", rows =>
        {
            DungeonRoomRow row = First<DungeonRoomRow>(rows, item => item.floor == 1);
            if (row != null)
            {
                row.floor = 0;
            }
        });
        CheckTrue(HasAll(TableValidator.validate(brokenFloor), "第 0 层"), "floor=0 应被校验抓到");

        TableDb brokenGap = Broken(
            "dungeon_room",
            rows => RemoveRows<DungeonRoomRow>(
                rows, row => row.scene_id == "scene_heifengzhai" && row.floor == 2));
        CheckTrue(HasAll(TableValidator.validate(brokenGap), "缺第 2 层"), "楼层缺号应被校验抓到");
    }
}
