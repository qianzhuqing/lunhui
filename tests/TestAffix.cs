using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 随机源与装备词条（C# 版）。
///
/// **2026-10-11（P4 第二十三批）起这份就是唯一一份**：旧链 `tests/test_affix.gd` 已并入本文件后退场。
/// 那一批补进来的是**整合部分**——词条真的进属性合成（穿上一件带词条的武器看五维与派生值）、
/// 进存档往返、进掉落（`BattleReward` 造实例时滚词条）、武器系别覆盖招式系别、
/// 以及「设计加一档稀有度时排序自动跟上」的夹具。这些当初留在 GDScript 是因为要 `GameState`／
/// `BattleReward`，现在它们都已搬 C#，所以补进这里。
/// </summary>
public partial class TestAffix : TestCase
{
    private const string CHAR_ID = "scholar_fallen";
    private const int SEED = 20261003;

    public override string SuiteName() => "随机源与装备词条";

    public override void Run()
    {
        var db = GetDb();
        _check_rng_is_reproducible();
        _check_rng_chance_and_pick();
        _check_rarity_rank(db);
        _check_affix_count_in_range(db);
        _check_count_edges(db);
        _check_pool_limited_by_slot_and_rarity(db);
        _check_slot_pool_breadth(db);
        _check_roll_and_value_range(db);
        _check_contributions_and_copy(db);
        _check_contributions_into_attributes(db);
        _check_save_round_trip(db);
        _check_drop_integration(db);
        _check_weapon_element(db);
        _check_new_rarity_supported(db);
    }

    private void _check_rng_is_reproducible()
    {
        var first = RngService.create(SEED);
        var second = RngService.create(SEED);
        CheckEq(first.get_seed(), SEED, "固定种子记在 RNG 上");
        for (var index = 0; index < 5; index++)
        {
            CheckFloat(first.randf(), second.randf(), $"同种子的第 {index + 1} 个 randf 必须一致");
        }

        CheckEq(
            RngService.create(SEED).randi_range(1, 100),
            RngService.create(SEED).randi_range(1, 100),
            "同种子的 randi_range 一致");
        CheckNe(
            RngService.create(SEED + 1).randf(),
            RngService.create(SEED).randf(),
            "换个种子结果就不同（不然种子等于没接）");
        for (var index = 0; index < 20; index++)
        {
            int roll = RngService.create(SEED + index).randi_range(3, 7);
            CheckInRange(roll, 3, 7, $"randi_range 落在闭区间内（第 {index + 1} 次）");
        }

        var ranged = RngService.create(SEED).randf_range(0.25, 0.75);
        CheckInRange(ranged, 0.25, 0.75, "randf_range 落在区间内");
    }

    private void _check_rng_chance_and_pick()
    {
        var rng = RngService.create(SEED);
        CheckFalse(rng.chance(0.0), "p = 0 恒不中");
        CheckFalse(rng.chance(-3.0), "负概率恒不中");
        CheckTrue(rng.chance(1.0), "p = 1 恒中");
        CheckTrue(rng.chance(9.0), "大于 1 也恒中");

        CheckTrue(
            rng.pick_weighted(new Godot.Collections.Array()).VariantType == Variant.Type.Nil,
            "空池抽不出东西（返回 null，不是报错）");
        var zero_weight = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary { { "key", "a" }, { "weight", 0.0 } },
            new Godot.Collections.Dictionary { { "key", "b" }, { "weight", 0.0 } },
        };
        CheckTrue(
            rng.pick_weighted(zero_weight).VariantType == Variant.Type.Nil,
            "权重全为 0 时返回 null");

        var one_sided = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary { { "key", "a" }, { "weight", 2.0 } },
            new Godot.Collections.Dictionary { { "key", "b" }, { "weight", 0.0 } },
        };
        CheckEq(rng.pick_weighted(one_sided).AsString(), "a", "只有一条有权重时必中它");
        CheckEq(
            RngService.create(SEED).pick_weighted(one_sided).AsString(),
            RngService.create(SEED).pick_weighted(one_sided).AsString(),
            "同种子的加权抽取一致");
    }

    private void _check_rarity_rank(TableDb db)
    {
        var roller = AffixRoller.create(db, RngService.create(SEED));
        Godot.Collections.Array rows = db.rows("rarity_def");
        CheckGt(rows.Count, 0, "rarity_def 有行（排名从行序来）");
        for (var index = 0; index < rows.Count; index++)
        {
            string rarity_id = rows[index].AsGodotObject().Get("rarity_id").AsString();
            CheckEq(roller.rarity_rank(rarity_id), index, $"{rarity_id} 的排名就是它在表里的行序 {index}");
        }

        CheckEq(
            roller.rarity_rank("没有这一档"),
            0,
            "表里查不到的稀有度按最低档算（决策 230：这里不再抄常量名单）");
    }

    private void _check_affix_count_in_range(TableDb db)
    {
        var roller = AffixRoller.create(db, RngService.create(SEED));
        foreach (Variant entry in db.rows("rarity_def"))
        {
            var row = entry.AsGodotObject() as RarityDefRow;
            if (row == null)
            {
                continue;
            }

            for (var index = 0; index < 20; index++)
            {
                int count = roller.affix_count(row.rarity_id);
                CheckInRange(
                    count,
                    row.affix_min,
                    row.affix_max,
                    $"{row.rarity_id} 的词条数 {count} 落在 [{row.affix_min}, {row.affix_max}]");
            }
        }

        CheckEq(
            AffixRoller.create(db, RngService.create(SEED)).affix_count("legend"),
            4,
            "传世固定 4 条（rarity_def.affix_min = affix_max = 4）");
    }

    private void _check_pool_limited_by_slot_and_rarity(TableDb db)
    {
        var roller = AffixRoller.create(db, RngService.create(SEED));
        Godot.Collections.Array weapon_pool = roller.candidates_for("weapon", "legend");
        CheckGt(weapon_pool.Count, 0, "武器槽在传世档有候选词条");
        var weapon_ids = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in weapon_pool)
        {
            var row = entry.AsGodotObject() as AffixPoolRow;
            if (row == null)
            {
                continue;
            }

            weapon_ids.Add(row.affix_id);
            CheckTrue(
                System.Array.IndexOf(row.slots(), "weapon") >= 0,
                $"候选里的 {row.affix_id} 允许出现在武器上");
            CheckTrue(
                roller.rarity_rank(row.min_rarity) <= roller.rarity_rank("legend"),
                $"{row.affix_id} 的最低稀有度不高于传世");
        }

        // af_hp_max_01 只允许 body|belt|shoulder|necklace——武器池里出现它就是"槽位限定失效"
        CheckFalse(weapon_ids.Contains("af_hp_max_01"), "护甲专属词条（气血上限）不在武器池里");

        // 稀有度单调：凡品能出的词条，传世一定也能出
        foreach (Variant entry in roller.candidates_for("weapon", "common"))
        {
            var row = entry.AsGodotObject() as AffixPoolRow;
            if (row != null)
            {
                CheckTrue(weapon_ids.Contains(row.affix_id), $"凡品武器能出的 {row.affix_id} 传世也在池里");
            }
        }
    }

    private void _check_roll_and_value_range(TableDb db)
    {
        var first = AffixRoller.create(db, RngService.create(SEED));
        var second = AffixRoller.create(db, RngService.create(SEED));
        Godot.Collections.Array rolled = first.roll_for("eq_sword_04");
        CheckEq(
            _signature(second.roll_for("eq_sword_04")),
            _signature(rolled),
            "同种子掷出的词条完全一致");
        CheckEq(rolled.Count, 4, "传世武器掷出 4 条词条");

        var base_row = db.get_row("equip_base", "eq_sword_04") as EquipBaseRow;
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in rolled)
        {
            Godot.Collections.Dictionary record = entry.AsGodotDictionary();
            string affix_id = record["affix_id"].AsString();
            var pool_row = db.get_row("affix_pool", affix_id) as AffixPoolRow;
            CheckNotNull(pool_row, $"掷出来的 {affix_id} 在词条池里");
            CheckTrue(seen.Add(affix_id), $"同一件装备不重复出 {affix_id}");
            if (pool_row == null || base_row == null)
            {
                continue;
            }

            CheckTrue(
                System.Array.IndexOf(pool_row.slots(), base_row.slot) >= 0,
                $"{affix_id} 允许出现在 {base_row.slot} 上");
            double value = record["value"].AsDouble();
            double slack = record["value_kind"].AsString() == "attr_point" ? 1.0 : 0.001;
            CheckInRange(
                value,
                pool_row.value_min - slack,
                pool_row.value_max + slack,
                $"{affix_id} 的数值 {value:0.###} 落在区间内");
        }
    }

    private void _check_contributions_and_copy(TableDb db)
    {
        // 存档里存的就是这个样子：affix_id + target + value_kind + value
        var records = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary
            {
                { "affix_id", "af_str_01" }, { "target", "attr:str" },
                { "value_kind", "attr_point" }, { "value", 3.0 },
            },
            new Godot.Collections.Dictionary
            {
                { "affix_id", "af_atk_phys_01" }, { "target", "stat:atk_phys" },
                { "value_kind", "flat" }, { "value", 7.0 },
            },
            new Godot.Collections.Dictionary
            {
                { "affix_id", "af_str_01" }, { "target", "attr:str" },
                { "value_kind", "attr_point" }, { "value", 0.0 },
            },
        };
        Godot.Collections.Array contribs = AffixRoller.contributions_of(db, records);
        CheckEq(contribs.Count, 2, "数值为 0 的词条不进贡献");
        Godot.Collections.Dictionary attr_contrib = contribs[0].AsGodotDictionary();
        CheckEq(
            attr_contrib["kind"].AsString(),
            AttributeCalculator.CONTRIB_ATTR_POINT,
            "attr: 走属性点层");
        CheckEq(attr_contrib["target"].AsString(), "str", "属性点词条的目标是属性 id");
        CheckEq(attr_contrib["value"].AsInt32(), 3, "属性点词条的值是整数");
        Godot.Collections.Dictionary stat_contrib = contribs[1].AsGodotDictionary();
        CheckEq(
            stat_contrib["kind"].AsString(),
            AttributeCalculator.CONTRIB_STAT_FLAT,
            "stat: 走固定值层");
        CheckEq(stat_contrib["target"].AsString(), "atk_phys", "派生数值词条的目标是 stat id");
        CheckFloat(stat_contrib["value"].AsDouble(), 7.0, "派生数值词条的数值原样带过来");

        Godot.Collections.Dictionary attr_record = records[0].AsGodotDictionary();
        var str_row = db.get_row("attribute_def", "str") as AttributeDefRow;
        CheckEq(
            AffixRoller.describe(db, attr_record),
            $"{str_row?.name_cn} +3",
            "属性词条文案：属性名 + 值");
        CheckEq(
            AffixRoller.target_of(db, new Godot.Collections.Dictionary { { "affix_id", "af_str_01" } }),
            "attr:str",
            "老档只剩 affix_id 时按表反查目标");

        // 百分比类走另一条文案分支：从池子里现找一条 rate 词条，不写死 id
        foreach (Variant entry in db.rows("affix_pool"))
        {
            var row = entry.AsGodotObject() as AffixPoolRow;
            if (row == null || row.value_kind != "rate" || !row.target.StartsWith("stat:"))
            {
                continue;
            }

            var rate_record = new Godot.Collections.Dictionary
            {
                { "affix_id", row.affix_id }, { "target", row.target }, { "value", 0.02 },
            };
            CheckTrue(
                AffixRoller.describe(db, rate_record).Contains('%'),
                $"{row.affix_id} 是百分比词条，文案要带 %");
            CheckEq(
                AffixRoller.summarize(db, new Godot.Collections.Array { rate_record }),
                AffixRoller.describe(db, rate_record),
                "只有一条时 summarize 等于 describe");
            break;
        }
    }

    /// <summary>凡品的边界：可能 0 条、也可能 1 条，但**不会出 2 条**（区间上界兜住）。</summary>
    private void _check_count_edges(TableDb db)
    {
        var roller = AffixRoller.create(db, RngService.create(SEED));
        var seen = new System.Collections.Generic.HashSet<int>();
        for (var index = 0; index < 40; index++)
        {
            seen.Add(roller.affix_count("common"));
        }

        CheckTrue(seen.Contains(0) || seen.Contains(1), "凡品最多 1 条（40 次掷出的集合里有 0 或 1）");
        foreach (int count in seen)
        {
            CheckLt(count, 2, $"凡品不会出 2 条（掷到 {count} 条）");
        }
    }

    /// <summary>
    /// 词条按槽位限定：拿五件真武器掷一遍，逐条验「这条词条允许出现在这个槽位」、
    /// 「同一件不重复出同一条」、「这件装备达到了该词条的最低稀有度」。
    /// </summary>
    private void _check_slot_pool_breadth(TableDb db)
    {
        var roller = AffixRoller.create(db, RngService.create(SEED));
        var weapon_rows = 0;
        foreach (string equip_id in new[] { "eq_sword_02", "eq_sword_03", "eq_sword_04", "eq_fist_02", "eq_spear_02" })
        {
            var base_row = db.get_row("equip_base", equip_id) as EquipBaseRow;
            Godot.Collections.Array records = roller.roll_for(equip_id);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Variant entry in records)
            {
                Godot.Collections.Dictionary record = entry.AsGodotDictionary();
                string affix_id = record["affix_id"].AsString();
                var pool_row = db.get_row("affix_pool", affix_id) as AffixPoolRow;
                weapon_rows += 1;
                CheckTrue(
                    pool_row != null && System.Array.IndexOf(pool_row.slots(), base_row.slot) >= 0,
                    $"{equip_id} 的词条 {affix_id} 允许出现在 {base_row.slot}");
                CheckTrue(seen.Add(affix_id), $"同一件装备不重复出同一条词条（{affix_id}）");
                CheckTrue(
                    pool_row != null
                        && roller.rarity_rank(pool_row.min_rarity) <= roller.rarity_rank(base_row.rarity),
                    $"{equip_id}（{base_row.rarity}）达到词条 {affix_id} 的最低稀有度");
            }
        }

        CheckGt(weapon_rows, 0, "武器确实掷出了词条");
    }

    /// <summary>
    /// 词条真的进属性：属性点词条走属性点层，派生数值词条走固定值层——
    /// 而且穿上之后面板上的五维与派生值都跟着变（不是只写在记录里）。
    /// </summary>
    private void _check_contributions_into_attributes(TableDb db)
    {
        GodotObject state = SoloState(db);
        CharacterSheet sheet = CharacterSheet.create(db, state, CHAR_ID);
        int str_before = sheet.total_attrs()["str"].AsInt32();
        double atk_before = sheet.stat_value("atk_phys");
        Inventory inventory = (Inventory)((GameState)state).inventory;

        // 词条记录是存档里存的样子：affix_id + target + value_kind + value
        var rolled = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary
            {
                { "affix_id", "af_str_01" }, { "target", "attr:str" },
                { "value_kind", "attr_point" }, { "value", 3.0 },
            },
            new Godot.Collections.Dictionary
            {
                { "affix_id", "af_atk_phys_01" }, { "target", "stat:atk_phys" },
                { "value_kind", "flat" }, { "value", 7.0 },
            },
        };
        string instance_id = inventory.add_equipment(db, "eq_sword_01", rolled);
        CheckEq(inventory.affixes_of(instance_id).Count, 2, "词条随实例存下来");
        var before_sources = _sources(inventory.contributions_for(db, CHAR_ID));
        CheckFalse(before_sources.Contains(instance_id), "没穿上时不算贡献");

        Godot.Collections.Dictionary equipped = inventory.equip(
            db, CHAR_ID, state.Call("level_of", CHAR_ID).AsInt32(), "sword", instance_id);
        CheckTrue(equipped["ok"].AsBool(), "带词条的武器能穿上");
        CharacterSheet after = CharacterSheet.create(db, state, CHAR_ID);
        CheckEq(after.total_attrs()["str"].AsInt32(), str_before + 3, "属性点词条进了五维");
        // 属性点还会经 attr_to_stat 换算：力 3 点带来的外功攻击要一起算上（所以只断言「涨了且不少于 7」）
        CheckGt(after.stat_value("atk_phys"), atk_before + 7.0, "派生数值词条 + 属性点换算都进了外功攻击");

        string summary = after.affix_summary_of(instance_id);
        CheckTrue(summary.Contains("力 +3"), $"词条文案给出属性：{summary}");
        CheckTrue(summary.Contains("外功攻击 +7"), $"词条文案给出派生数值：{summary}");
    }

    /// <summary>存档往返：词条跟着实例走，读回来贡献一致。</summary>
    private void _check_save_round_trip(TableDb db)
    {
        GodotObject state = SoloState(db);
        Inventory inventory = (Inventory)((GameState)state).inventory;
        Godot.Collections.Array rolled =
            AffixRoller.create(db, RngService.create(SEED)).roll_for("eq_ring_02");
        CheckGt(rolled.Count, 0, "良品戒指掷出了词条");
        string instance_id = inventory.add_equipment(db, "eq_ring_02", rolled);
        Godot.Collections.Array before = inventory.contributions_for(db, CHAR_ID);

        GameState back = GameState.from_dict(((GameState)state).to_dict(), db);
        CheckNotNull(back, "带词条的存档能读回来");
        if (back == null)
        {
            return;
        }

        var back_inventory = (Inventory)back.inventory;
        CheckEq(_signature(back_inventory.affixes_of(instance_id)), _signature(rolled), "词条原样保留");
        CheckEq(
            _signature_of(back_inventory.contributions_for(db, CHAR_ID), "kind", "target", "value", "source"),
            _signature_of(before, "kind", "target", "value", "source"),
            "贡献列表往返一致");
    }

    /// <summary>掉落接词条：`BattleReward` 造装备实例时会滚词条。</summary>
    private void _check_drop_integration(TableDb db)
    {
        GodotObject state = SoloState(db);
        var roller = AffixRoller.create(db, RngService.create(SEED));
        Godot.Collections.Dictionary applied = BattleReward.grant(
            db,
            state,
            0,
            0,
            new Godot.Collections.Array
            {
                new Godot.Collections.Dictionary { { "item_id", "eq_sword_03" }, { "qty", 1 } },
            },
            roller);
        Godot.Collections.Array equipment = applied["equipment"].AsGodotArray();
        CheckEq(equipment.Count, 1, "掉了一件装备");
        if (equipment.Count == 0)
        {
            return;
        }

        string instance_id = equipment[0].AsString();
        Inventory inventory = (Inventory)((GameState)state).inventory;
        Godot.Collections.Array affixes = inventory.affixes_of(instance_id);
        var base_row = db.get_row("equip_base", "eq_sword_03") as EquipBaseRow;
        var rarity_row = db.get_row("rarity_def", base_row.rarity) as RarityDefRow;
        CheckInRange(
            affixes.Count, rarity_row.affix_min, rarity_row.affix_max,
            $"掉落的 {base_row.name_cn}（{base_row.rarity}）带 {affixes.Count} 条词条");
        foreach (Variant entry in affixes)
        {
            Godot.Collections.Dictionary record = entry.AsGodotDictionary();
            CheckTrue(
                db.get_row("affix_pool", record["affix_id"].AsString()) != null,
                $"掉落的词条 {record["affix_id"].AsString()} 在词条池里");
        }
    }

    /// <summary>武器系别覆盖招式系别（05 文档：乌木拳套是内功拳那条规则）。</summary>
    private void _check_weapon_element(TableDb db)
    {
        GodotObject state = SoloState(db);
        ((GameState)state).char_levels[CHAR_ID] = 12;
        Inventory inventory = (Inventory)((GameState)state).inventory;
        string odd_sword = inventory.add_equipment(db, "eq_sword_04", new Godot.Collections.Array());
        Godot.Collections.Dictionary equipped = inventory.equip(db, CHAR_ID, 12, "sword", odd_sword);
        CheckTrue(equipped["ok"].AsBool(), "奇诡剑（10 级）能装上");

        BattleActor actor = PartyBuilder.build_actor(db, state, CHAR_ID);
        CheckNotNull(actor, "造出战斗单位");
        CheckEq(_tag(actor, "weapon_element"), "odd", "武器系别带进战斗单位");

        // 打一场，看事件里的系别是不是被武器覆盖过的
        Godot.Collections.Array enemies =
            EnemyFactory.create(db).create_team("team_wolf_pack", "normal");
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(SEED));
        sim.setup(new Godot.Collections.Array { actor }, enemies, new Godot.Collections.Dictionary());
        var guard = 0;
        var elements = new System.Collections.Generic.HashSet<string>();
        while (!sim.finished() && guard < 40)
        {
            guard += 1;
            if (!sim.in_round())
            {
                sim.begin_round();
            }

            if (sim.current_actor() is BattleActor current && current.actor_id == actor.actor_id)
            {
                Godot.Collections.Array skills = sim.available_skills(actor);
                if (skills.Count == 0)
                {
                    sim.auto_act();
                }
                else
                {
                    sim.act(actor, skills[0].As<Resource>(), (BattleActor)enemies[0].AsGodotObject());
                }
            }
            else
            {
                sim.auto_act();
            }

            foreach (Variant event_variant in sim.take_events())
            {
                Godot.Collections.Dictionary evt = event_variant.AsGodotDictionary();
                string kind = evt.ContainsKey("kind") ? evt["kind"].AsString() : "";
                string source = evt.ContainsKey("source") ? evt["source"].AsString() : "";
                if (kind == "hit" && source == actor.actor_id)
                {
                    elements.Add(evt.ContainsKey("element") ? evt["element"].AsString() : "");
                }
            }
        }

        CheckTrue(
            elements.Contains("odd"),
            $"招式本身是外功，但事件里的系别是武器给的奇诡：{string.Join("、", elements)}");

        // 卸下武器后回到招式自己的系别
        inventory.unequip(CHAR_ID, "weapon", 0);
        BattleActor bare = PartyBuilder.build_actor(db, state, CHAR_ID);
        CheckEq(_tag(bare, "weapon_element"), "", "卸下武器后没有武器系别");
    }

    /// <summary>
    /// 设计**加一档稀有度**时，代码要自动跟上（排序的唯一出处是 `rarity_def` 的行顺序）。
    ///
    /// 由来（2026-10-03，决策 230）：`AffixRoller` 以前抄了一份常量名单 `RARITY_ORDER`，
    /// 而设计侧校验器（PS1）是按表顺序算的——两边一旦分家，新档的 `rarity_rank()` 会返回
    /// **0（＝凡品）**，于是「要求高稀有度才出的词条」会落到新档上（或者反过来）：**门槛静默失效**。
    /// 这里用夹具加一档 `myth`（排在传世之后）+ 一条只给 myth 的词条，验排序与门槛都按表走。
    /// </summary>
    private void _check_new_rarity_supported(TableDb db)
    {
        var stub = new TableDb();
        stub.load_all();
        var rarities = (TableResource)stub.tables["rarity_def"].As<TableResource>().Duplicate(true);
        var myth = (RarityDefRow)rarities.rows[0].As<TableRow>().Duplicate(true);   // 复制凡品行再改
        myth.rarity_id = "myth";
        myth.id = "myth";
        myth.name_cn = "神话";
        myth.affix_min = 4;
        myth.affix_max = 4;
        rarities.rows.Add(myth);
        rarities.index["myth"] = rarities.rows.Count - 1;
        stub.tables["rarity_def"] = rarities;

        var pool = (TableResource)stub.tables["affix_pool"].As<TableResource>().Duplicate(true);
        var top = (AffixPoolRow)pool.rows[0].As<TableRow>().Duplicate(true);
        top.affix_id = "af_test_myth";
        top.id = "af_test_myth";
        top.min_rarity = "myth";
        top.allow_slots = "weapon";
        pool.rows.Add(top);
        pool.index["af_test_myth"] = pool.rows.Count - 1;
        stub.tables["affix_pool"] = pool;

        var roller = AffixRoller.create(stub, RngService.create(SEED));
        CheckEq(roller.rarity_rank("myth"), 5, "新档按表顺序排到最后（不再静默当凡品）");
        CheckEq(roller.rarity_rank("legend"), 4, "对照：传世还是第 4 档");
        Godot.Collections.Array myth_pool = roller.candidates_for("weapon", "myth");
        Godot.Collections.Array legend_pool = roller.candidates_for("weapon", "legend");
        CheckTrue(_pool_has(myth_pool, "af_test_myth"), "新档装备能刷到「要求 myth」的词条");
        CheckFalse(_pool_has(legend_pool, "af_test_myth"), "低一档（传世）刷不到它——门槛没有失效");
    }

    private static bool _pool_has(Godot.Collections.Array pool, string affix_id)
    {
        foreach (Variant entry in pool)
        {
            if ((entry.AsGodotObject() as AffixPoolRow)?.affix_id == affix_id)
            {
                return true;
            }
        }

        return false;
    }

    private static System.Collections.Generic.List<string> _sources(Godot.Collections.Array contributions)
    {
        var out_sources = new System.Collections.Generic.List<string>();
        foreach (Variant entry in contributions)
        {
            Godot.Collections.Dictionary record = entry.AsGodotDictionary();
            out_sources.Add(record.ContainsKey("source") ? record["source"].AsString() : "");
        }

        return out_sources;
    }

    private static string _tag(BattleActor actor, string key) =>
        actor.tags.ContainsKey(key) ? actor.tags[key].AsString() : "";

    /// <summary>掷出来的词条列表的可比形式（Godot 的 Array 判等不比内容，自己拼一个）。</summary>
    private static string _signature(Godot.Collections.Array records)
        => _signature_of(records, "affix_id", "target", "value_kind", "value");

    /// <summary>把一组记录拼成可比字符串（`key:key:…` 逐条、`|` 相连）——Godot 的 Array 判等不比内容。</summary>
    private static string _signature_of(Godot.Collections.Array records, params string[] keys)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry in records)
        {
            Godot.Collections.Dictionary record = entry.AsGodotDictionary();
            var fields = new System.Collections.Generic.List<string>();
            foreach (string key in keys)
            {
                fields.Add(record.ContainsKey(key) ? record[key].ToString() : "");
            }

            parts.Add(string.Join(":", fields));
        }

        return string.Join("|", parts);
    }
}
