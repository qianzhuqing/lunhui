using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 天赋：花费与挑选规则 ＋ `rule:` 规则性效果（C# 版）。
///
/// 前半段（花费／挑选／开关）对位 `tests/test_creation.gd::_check_talents`；
/// 后半段（`rule:` 效果）对位原 `tests/test_talent.gd`。
///
/// **2026-10-11（P4 第二十一批）起这份就是唯一一份**：旧链 `tests/test_talent.gd` 已并入本文件后退场。
/// 设计 12 §五把 `rule:` 定义为「表达不成数值的东西用」，一行挂在 `talent_effect` 上、
/// **没有任何东西会报错**——所以这一半的用处是逐条钉「选了天赋 → 那个系统真的变了」，
/// 并且钉**不该受影响的地方没变**（例：三寸不烂之舌只加文学，医术判定不动）。
/// </summary>
public partial class TestTalent : TestCase
{
    private const string CHAR_ID = "scholar_fallen";
    private const int SEED = 20261004;

    /// <summary>
    /// `rule:` 的**还没接**的四条——各自等设计或等实现（见 `待策划确认.md` Q75）。
    /// 用 `System.Collections.Generic`：Godot 集合挂静态字段会活到进程收尾（施工单坑⑧）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> UNCONSUMED = new()
    {
        {
            "mastery_gain",
            "熟练度成长 +30% 要小数进度（熟练度是 0~10 的整数，+30% 落不到整点上），要设计定口径"
        },
        { "bonus_skill", "天生剑骨给的是「额外一部剑法」——哪一部设计没点名" },
        { "defeat_heal_bonus", "九命的「败北恢复更高」：现在败北本来就是回满气血，这条得先定语义" },
    };

    public override string SuiteName() => "天赋花费与规则效果";

    public override void Run()
    {
        var db = GetDb();
        _check_budget_and_cards(db);
        _check_cost_and_remaining(db);
        _check_can_pick_rules(db);
        _check_toggle(db);
        _check_table_is_consumed(db);
        _check_check_bonus(db);
        _check_check_difficulty(db);
        _check_codex_multiplier(db);
        _check_shop_discount(db);
        _check_first_strike(db);
        _check_skill_damage(db);
    }

    private void _check_budget_and_cards(TableDb db)
    {
        CheckEq(TalentService.budget(db), 5, "天赋总点数 5（growth_const.talent_points）");
        Godot.Collections.Array cards = TalentService.cards(db);
        CheckEq(cards.Count, 24, "天赋池 24 条");
        CheckEq(TalentService.cost_of(db, new[] { "tal_tie_shen" }), 1, "「铁打身板」1 点");
        CheckEq(TalentService.cost_of(db, new[] { "tal_dan_shi" }), 3, "「胆识过人」3 点");

        // 排序口径：category → cost → talent_id（对位 sort_custom 的比较器）
        for (var index = 1; index < cards.Count; index++)
        {
            Godot.Collections.Dictionary before = cards[index - 1].AsGodotDictionary();
            Godot.Collections.Dictionary after = cards[index].AsGodotDictionary();
            string before_category = before["category"].AsString();
            string after_category = after["category"].AsString();
            if (before_category != after_category)
            {
                CheckLt(
                    string.CompareOrdinal(before_category, after_category),
                    0,
                    $"第 {index} 张卡的 category 不比前一张小");
                continue;
            }

            int before_cost = before["cost"].AsInt32();
            int after_cost = after["cost"].AsInt32();
            if (before_cost != after_cost)
            {
                CheckLt(before_cost, after_cost, $"同 category 内按 cost 升序（第 {index} 张）");
                continue;
            }

            CheckLt(
                string.CompareOrdinal(before["talent_id"].AsString(), after["talent_id"].AsString()),
                0,
                $"同 category 同 cost 时按 talent_id 升序（第 {index} 张）");
        }

        foreach (Variant entry in cards)
        {
            Godot.Collections.Dictionary card = entry.AsGodotDictionary();
            string talent_id = card["talent_id"].AsString();
            CheckTrue(card["desc"].AsString().Length > 0, $"{talent_id} 的 desc 不许为空");
        }
    }

    private void _check_cost_and_remaining(TableDb db)
    {
        CheckEq(TalentService.remaining_points(db, new[] { "tal_tie_shen" }), 4, "选了 1 点的「铁打身板」还剩 4 点");
        CheckEq(
            TalentService.remaining_points(db, System.Array.Empty<string>()),
            TalentService.budget(db),
            "一个都没选时剩余 = 总点数");
        CheckTrue(
            TalentService.cost_of(db, new[] { "tal_dan_shi" }) > 1,
            "「胆识过人」这类高价天赋的花费大于 1");
    }

    private void _check_can_pick_rules(TableDb db)
    {
        CheckTrue(
            TalentService.can_pick(db, System.Array.Empty<string>(), "tal_tie_shen")["ok"].AsBool(),
            "空手能选第一个天赋");
        CheckFalse(
            TalentService.can_pick(db, new[] { "tal_tie_shen" }, "tal_tie_shen")["ok"].AsBool(),
            "同一个天赋不能选两次");
        CheckTrue(
            TalentService.can_pick(db, new[] { "tal_tie_shen" }, "tal_tie_shen")["error"]
                .AsString()
                .Contains("已经选了"),
            "重复选择的拒绝理由说清是「已经选了」");
        CheckFalse(
            TalentService.can_pick(db, new[] { "tal_dan_shi" }, "tal_jiu_ming")["ok"].AsBool(),
            "3 + 3 超过 5 点：第二个选不上");
        CheckFalse(
            TalentService.can_pick(db, System.Array.Empty<string>(), "没有这个天赋")["ok"].AsBool(),
            "表里没有的天赋选不上");
        CheckTrue(
            TalentService.can_pick(db, System.Array.Empty<string>(), "没有这个天赋")["error"]
                .AsString()
                .Contains("没有这个天赋"),
            "不存在的天赋给的是「没有这个天赋」");
    }

    private void _check_toggle(TableDb db)
    {
        Godot.Collections.Dictionary picked = TalentService.toggle(
            db, System.Array.Empty<string>(), "tal_tie_shen");
        CheckTrue(picked["ok"].AsBool(), "选中成功");
        CheckEq(picked["picks"].AsStringArray().Length, 1, "选完之后列表里有一个");
        CheckEq(picked["picks"].AsStringArray()[0], "tal_tie_shen", "选的是刚点的那个");

        Godot.Collections.Dictionary again = TalentService.toggle(
            db, picked["picks"].AsStringArray(), "tal_tie_shen");
        CheckTrue(again["ok"].AsBool(), "再点一下是取消（不算失败）");
        CheckEq(again["picks"].AsStringArray().Length, 0, "取消之后列表清空");

        Godot.Collections.Dictionary refused = TalentService.toggle(
            db, new[] { "tal_dan_shi" }, "tal_jiu_ming");
        CheckFalse(refused["ok"].AsBool(), "点数不够时选不上");
        CheckEq(refused["picks"].AsStringArray().Length, 1, "被拒时原列表原样退回");
        CheckEq(refused["picks"].AsStringArray()[0], "tal_dan_shi", "退回的那一条还是原来那个");
        CheckTrue(refused["error"].AsString().Contains("点数不够"), "拒绝理由说清是「点数不够」");
    }

    // ---------------------------------------------------------------- rule: 效果（旧 test_talent.gd）

    /// <summary>
    /// `rule:` 的每一行都要有人在读——**这条是防「又添一条没人读的规则」的**。
    /// 还没接的四条写在 `UNCONSUMED` 里（各自等设计或等实现，见 `待策划确认.md` Q75）。
    /// </summary>
    private void _check_table_is_consumed(TableDb db)
    {
        string corpus = "";
        foreach (string path in new[]
                 {
                     "src/core/TalentService.cs", "src/core/CharacterSheet.cs",
                     "src/core/EventCheckService.cs", "src/core/ShopService.cs",
                     "src/core/BattleSimulator.cs", "src/core/BattleSimulatorActions.cs",
                     "src/core/BattleSimulatorSelect.cs", "src/core/BattleSimulatorSettle.cs",
                     "src/core/DamageResolver.cs",
                     "src/core/SkillLoadout.cs", "src/core/CreationService.cs",
                 })
        {
            corpus += FileAccess.GetFileAsString("res://" + path);
        }

        var ids = new Godot.Collections.Dictionary();
        foreach (Variant entry in db.rows("talent_effect"))
        {
            string target = (entry.AsGodotObject() as TalentEffectRow)?.target ?? "";
            if (target.StartsWith("rule:"))
            {
                ids[target.Substring("rule:".Length)] = true;
            }
        }

        CheckGt(ids.Count, 5, $"表里有足够多的 rule: 效果（{ids.Count} 条）");
        var unread = new System.Collections.Generic.List<string>();
        foreach (Variant rule_variant in ids.Keys)
        {
            string rule_id = rule_variant.AsString();
            if (UNCONSUMED.ContainsKey(rule_id))
            {
                continue;
            }

            if (rule_id == "event_check_bonus")
            {
                continue;    // 通用加成与「只加某一门」都是按前缀拼出来的，见 TalentService.check_bonus
            }

            if (rule_id.StartsWith("event_check_bonus_"))
            {
                continue;    // `event_check_bonus_<技能>` 由同一个模板拼，语料里当然找不到字面量
            }

            if (!corpus.Contains(rule_id))
            {
                unread.Add(rule_id);
            }
        }

        CheckEq(
            unread.Count, 0,
            $"每条 rule: 都有人在读（没人读的：{string.Join("、", unread)}）——"
                + "要么接上，要么写进 UNCONSUMED 并说明等谁");
    }

    /// <summary>判定值上的加成：见多识广（所有判定 +1）／三寸不烂之舌（文学 +2）／悬壶（医术 +2）。</summary>
    private void _check_check_bonus(TableDb db)
    {
        GodotObject state = SoloState(db);
        CharacterSheet plain = CharacterSheet.create(db, state, CHAR_ID);
        int wenxue = plain.event_check_value("skill:wenxue")["value"].AsInt32();
        int yishu = plain.event_check_value("skill:yishu")["value"].AsInt32();
        int attr_check = plain.event_check_value("attr:int")["value"].AsInt32();

        _SetPicks(state, "tal_jian_duo");
        CharacterSheet broad = CharacterSheet.create(db, state, CHAR_ID);
        CheckEq(
            broad.event_check_value("skill:wenxue")["value"].AsInt32(), wenxue + 1,
            "见多识广：所有判定 +1（文学）");
        CheckEq(
            broad.event_check_value("skill:yishu")["value"].AsInt32(), yishu + 1,
            "见多识广：所有判定 +1（医术也算）");
        CheckEq(
            broad.event_check_value("attr:int")["value"].AsInt32(), attr_check + 1,
            "见多识广：属性判定同样 +1");

        _SetPicks(state, "tal_san_cun");
        CharacterSheet wen_holder = CharacterSheet.create(db, state, CHAR_ID);
        CheckEq(
            wen_holder.event_check_value("skill:wenxue")["value"].AsInt32(), wenxue + 2,
            "三寸不烂之舌：文学 +2");
        CheckEq(
            wen_holder.event_check_value("skill:yishu")["value"].AsInt32(), yishu,
            "它**只**加文学，医术不动");
    }

    /// <summary>江湖百晓生：判定门槛 −2（设计 12 §六「让不想打的人也能推内容」）。</summary>
    private void _check_check_difficulty(TableDb db)
    {
        GodotObject state = SoloState(db);
        EventCheckService service = EventCheckService.create(db, state, RngService.create(SEED));
        Resource row = db.get_row("event_check", "ev_shen_rescue");
        CheckEq(
            service.effective_difficulty(row), row.Get("difficulty").AsInt32(),
            "没天赋时门槛就是表里那个数");

        _SetPicks(state, "tal_bai_xiao");
        EventCheckService discounted = EventCheckService.create(db, state, RngService.create(SEED));
        CheckEq(
            discounted.effective_difficulty(row), System.Math.Max(0, row.Get("difficulty").AsInt32() - 2),
            "江湖百晓生：门槛 −2");

        // 门槛压到 0 就不再往下压（负数会让硬判定变成永远能做，那是另一套语义）
        Resource hard = db.get_row("event_check", "ev_shed_trap");
        for (var index = 0; index < 3; index++)
        {
            _SetPicks(state, "tal_bai_xiao");
        }

        CheckEq(
            EventCheckService.create(db, state, RngService.create(SEED)).effective_difficulty(hard),
            System.Math.Max(0, hard.Get("difficulty").AsInt32() - 2),
            "门槛只减到 0，不写负数");
    }

    /// <summary>藏书癖：图鉴奖励翻倍（设计 12 §六「把收集癖变成正经 build」）。</summary>
    private void _check_codex_multiplier(TableDb db)
    {
        GodotObject state = SoloState(db);
        // 收集 5 部武学 = 一档奖励（`growth_const.codex_step`）
        int learned = 0;
        foreach (Variant entry in db.rows("skill_base"))
        {
            if (learned >= 5)
            {
                break;
            }

            var row = entry.AsGodotObject() as SkillBaseRow;
            if (row != null && state.Call("learn_skill", CHAR_ID, row.skill_id).AsBool())
            {
                learned += 1;
            }
        }

        CheckEq(learned, 5, "先学会 5 部武学（夹具）");
        CharacterSheet plain = CharacterSheet.create(db, state, CHAR_ID);
        Godot.Collections.Dictionary before = plain.codex_bonus();
        CheckEq(before["times"].AsInt32(), 1, "5 部 = 一档");
        CheckEq(before["per_tier"].AsInt32(), 1, "没天赋时每档七项各 +1");
        // **先把这个数取下来**：`CharacterSheet` 持有的是同一份 state，后面改了天赋它就跟着变
        int wu_plain = _int(plain.naked_attrs(), "wu");

        _SetPicks(state, "tal_cang_shu");
        CharacterSheet doubled = CharacterSheet.create(db, state, CHAR_ID);
        Godot.Collections.Dictionary after = doubled.codex_bonus();
        CheckEq(after["per_tier"].AsInt32(), 2, "藏书癖：每档翻倍（+2）");
        CheckEq(after["bonus"].AsInt32(), 2, "这一档合计 +2");
        // 翻倍必须真的进属性合成（不然只是面板上的字）
        int wu_doubled = _int(doubled.naked_attrs(), "wu");
        CheckEq(
            wu_doubled - wu_plain, 1,
            $"翻倍真的进属性（悟性多涨 1：{wu_plain} → {wu_doubled}）");
        // 门槛那一侧也要用同一个倍数（面板够、门槛不够是踩过的坑）
        CheckTrue(
            TalentService.rule_value(db, state, CHAR_ID, "codex_bonus_multiplier", 0.0) == 2.0,
            "表里的值是倍数（2 = 翻倍），不是增量");
    }

    /// <summary>过日子：买东西便宜一成（`rule:shop_buy_price 0.10`）。</summary>
    private void _check_shop_discount(TableDb db)
    {
        GodotObject state = SoloState(db);
        ShopService shop = ShopService.create(db, state);
        // `stock_row()` 要的是**建筑 id**（它自己按 `building_def.stock_group` 换算到货架组）
        const string building = "bld_smith";
        string stock_group = db.get_row("building_def", building).Get("stock_group").AsString();
        string item = "";
        foreach (Variant entry in db.rows("shop_stock"))
        {
            GodotObject row = entry.AsGodotObject();
            if (row.Get("shop_id").AsString() == stock_group && row.Get("buy_price").AsInt32() > 0)
            {
                item = row.Get("item_id").AsString();
                break;
            }
        }

        CheckTrue(item.Length > 0, "铁匠铺有在卖的货（夹具）");
        if (item.Length == 0)
        {
            return;
        }

        int base_price = shop.buy_price(building, item);
        CheckGt(base_price, 0, $"原价 {base_price}");
        _SetPicks(state, "tal_guo_ri");
        ShopService discounted = ShopService.create(db, state);
        CheckEq(
            discounted.buy_price(building, item),
            System.Math.Max(1, (int)System.Math.Floor(base_price * 0.9)),
            $"过日子：买入价便宜一成（{base_price} → {discounted.buy_price(building, item)}）");
        CheckEq(
            discounted.sell_price(building, item), shop.sell_price(building, item),
            "卖出价不受影响（改它等于把玩家的钱变少）");
    }

    /// <summary>先发制人：首回合必定先手（设计 12 §六）。</summary>
    private void _check_first_strike(TableDb db)
    {
        BattleActor slow = _Actor("ally_slow", BattleActor.side_ally(), 5.0, 200);
        BattleActor fast = _Actor("enemy_fast", BattleActor.side_enemy(), 30.0, 200);
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(SEED));
        sim.setup(
            new Godot.Collections.Array { slow },
            new Godot.Collections.Array { fast },
            new Godot.Collections.Dictionary
            {
                { "modifiers", new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } } },
            });
        Godot.Collections.Array plain_order =
            sim.turn_order(new Godot.Collections.Array { slow }, new Godot.Collections.Array { fast });
        CheckEq(
            ((BattleActor)plain_order[0].AsGodotObject()).actor_id, "enemy_fast",
            "没天赋时身法快的先手");

        slow.talent_rules = new Godot.Collections.Dictionary { { "first_round_priority", 1.0 } };
        Godot.Collections.Array with_talent = sim._apply_talent_first_strike(
            sim.turn_order(new Godot.Collections.Array { slow }, new Godot.Collections.Array { fast }), 1);
        CheckEq(
            ((BattleActor)with_talent[0].AsGodotObject()).actor_id, "ally_slow",
            "先发制人：第 1 回合他抢到最前");
        Godot.Collections.Array round_two = sim._apply_talent_first_strike(
            sim.turn_order(new Godot.Collections.Array { slow }, new Godot.Collections.Array { fast }), 2);
        CheckEq(
            ((BattleActor)round_two[0].AsGodotObject()).actor_id, "enemy_fast",
            "只在第 1 回合（后面照身法走）");
    }

    /// <summary>天生武胆：招式伤害 +15%（设计 12 §六；花满 3 点、自带内伤抗性 −20% 的代价）。</summary>
    private void _check_skill_damage(TableDb db)
    {
        DamageResolver resolver = DamageResolver.create(db, RngService.create(SEED));
        BattleActor attacker = _Actor("attacker", BattleActor.side_ally(), 20.0, 100);
        BattleActor defender = _Actor("defender", BattleActor.side_enemy(), 10.0, 300);
        attacker.level = 10;
        defender.level = 5;
        attacker.stats["atk_phys"] = 100.0;
        attacker.stats["crit_rate"] = 0.0;
        attacker.stats["hit_rate"] = 0.9;
        defender.stats["def_phys"] = 60.0;
        defender.stats["dodge_rate"] = 0.0;

        Resource skill = db.get_row("skill_active", "sk_xuanwei_03");
        CheckNotNull(skill, "黄金算式那条招式还在（夹具）");
        if (skill == null)
        {
            return;
        }

        var mods = new Godot.Collections.Dictionary { { "force_hit", true }, { "no_variance", true } };
        Godot.Collections.Dictionary plain = resolver.resolve(attacker, defender, skill, mods);
        attacker.talent_rules = new Godot.Collections.Dictionary { { "skill_damage", 0.15 } };
        Godot.Collections.Dictionary boosted = resolver.resolve(attacker, defender, skill, mods);
        CheckFloat(
            boosted["detail"].AsGodotDictionary()["talent_skill_factor"].AsDouble(), 1.15,
            "乘区 = 1 + 15%", 0.0001);
        CheckFloat(
            plain["detail"].AsGodotDictionary()["talent_skill_factor"].AsDouble(), 1.0,
            "没天赋时是 1.0");
        CheckEq(
            boosted["raw_damage"].AsInt32(),
            (int)System.Math.Floor(plain["raw_damage"].AsDouble() * 1.15),
            $"伤害按 +15%% 算（{plain["raw_damage"].AsInt32()} → {boosted["raw_damage"].AsInt32()}）");
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>`state.talent_picks[CHAR_ID] = picks`——GameState 走 `Get`（字典是引用类型，就地改）。</summary>
    private static void _SetPicks(GodotObject state, params string[] picks)
    {
        var list = new Godot.Collections.Array();
        foreach (string pick in picks)
        {
            list.Add(pick);
        }

        state.Get("talent_picks").AsGodotDictionary()[CHAR_ID] = list;
    }

    /// <summary>对位 GDScript 的 `dict.get(key, 0)`：缺键给 0，C# 直接取会抛。</summary>
    private static int _int(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    /// <summary>手搓一个战斗单位（只要身法／气血这两样就够验顺序）。</summary>
    private static BattleActor _Actor(string actor_id, int side, double speed, int hp)
    {
        var actor = new BattleActor
        {
            actor_id = actor_id,
            display_name = actor_id,
            side = side,
            level = 5,
            base_accuracy = 0.0,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", (double)hp },
                { "qi_max", 100.0 },
                { "atk_phys", 20.0 },
                { "atk_qi", 0.0 },
                { "def_phys", 10.0 },
                { "def_qi", 0.0 },
                { "speed", speed },
                { "hit_rate", 0.9 },
                { "dodge_rate", 0.0 },
                { "crit_rate", 0.0 },
                { "crit_dmg", 0.0 },
                { "pen_rate", 0.0 },
                { "block_rate", 0.0 },
                { "block_reduction", 0.0 },
                { "dmg_reduction", 0.0 },
            },
        };
        actor.refill();
        return actor;
    }
}
