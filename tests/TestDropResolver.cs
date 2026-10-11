using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 掉落掷点与保底（C# 版，对位 `tests/test_drop.gd` 的判定那几节）。
///
/// 这里盯的是**统计口径**：必掉槽每次都出、概率槽落在区间里、难度只改几率不改清单、
/// 首杀必掉一件（武器优先）、保底到点必出。概率类断言都用固定种子，跑多少遍都一样。
/// </summary>
public partial class TestDropResolver : TestCase
{
    private const int Seed = 90210;
    private const int Rolls = 2000;

    public override string SuiteName() => "掉落与保底（C# 侧）";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckMissingGroup(db);
        _CheckGuaranteedAndRate(db);
        _CheckQtyRange(db);
        _CheckDifficultyScaling(db);
        _CheckDifficultyKeepsList(db);
        _CheckFirstKillEquipment(db);
        _CheckPityForced();
        _CheckDropBonus(db);
        _CheckPartyBonus(db);
    }

    /// <summary>掉落组不存在：返回空表并出声，不装作掉了一件空气。</summary>
    private void _CheckMissingGroup(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        CheckEq(
            resolver.roll_group("drop_does_not_exist", "normal", new Godot.Collections.Dictionary()).Count,
            0, "不存在的掉落组返回空表");
    }

    /// <summary>必掉槽每次都出；概率槽落在配置区间。</summary>
    private void _CheckGuaranteedAndRate(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        int money = 0;
        int herb = 0;
        for (int index = 0; index < Rolls; index++)
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_wolf", "normal", new Godot.Collections.Dictionary());
            money += _CountOccurrences(drops, "item_money");
            herb += _CountOccurrences(drops, "item_herb");
        }
        CheckEq(money, Rolls, "必掉槽每次都出铜钱");
        CheckInRange(herb, Rolls * 0.20, Rolls * 0.30, "草药掉率约 25%");
    }

    /// <summary>数量区间读表（野狼的铜钱配的是 3~8）。</summary>
    private void _CheckQtyRange(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        bool ok = true;
        for (int index = 0; index < 200; index++)
        {
            foreach (Variant entry in resolver.roll_group(
                         "drop_wolf", "normal", new Godot.Collections.Dictionary()))
            {
                Godot.Collections.Dictionary row = entry.AsGodotDictionary();
                if (row["item_id"].AsString() == "item_money")
                {
                    int qty = row["qty"].AsInt32();
                    ok = ok && qty >= 3 && qty <= 8;
                }
            }
        }
        CheckTrue(ok, "铜钱数量落在配置区间 3~8");
    }

    /// <summary>难度只放大几率（凡品 ×1.5）。</summary>
    private void _CheckDifficultyScaling(TableDb db)
    {
        int normal = _CountDrops(db, "drop_wolf", "normal", "item_herb", 0.0);
        int hard = _CountDrops(db, "drop_wolf", "hard", "item_herb", 0.0);
        CheckGt(hard, normal * 1.2, "困难难度对凡品倍率 ×1.5，掉落明显增多");
    }

    /// <summary>难度**不改清单**：必掉的那件两个难度都得在。</summary>
    private void _CheckDifficultyKeepsList(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        foreach (string difficulty in new[] { "normal", "nightmare" })
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_hidden_drunk", difficulty, new Godot.Collections.Dictionary());
            CheckEq(
                _CountOccurrences(drops, "item_scroll_drunk"), 1,
                $"{difficulty} 下必掉隐藏武学残卷");
            // 设计 10 §五（0.14.0）：drop_table 不再列装备，装备改由 enemy_equip 推
            CheckEq(
                _CountOccurrences(drops, "eq_sword_04"), 0,
                $"{difficulty} 下掉落表里不再列装备");
        }
    }

    /// <summary>首杀必掉一件（武器优先），重复刷回到稀有度概率。</summary>
    private void _CheckFirstKillEquipment(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        Godot.Collections.Array first = resolver.roll_enemy_equipment("en_bd_boss", true);
        CheckEq(_CountOccurrences(first, "eq_sword_03"), 1, "首杀必掉他身上的武器（黑风刀）");

        int hits = 0;
        for (int index = 0; index < 200; index++)
        {
            if (_CountOccurrences(resolver.roll_enemy_equipment("en_bd_boss", false), "eq_sword_03") > 0)
            {
                hits++;
            }
        }
        CheckInRange(hits, 50, 110, $"重复刷黑风刀按良品 40% 出货（200 次里 {hits} 次）");
        CheckEq(
            resolver.roll_enemy_equipment("enemy_does_not_exist", true).Count, 0,
            "没有装备表的敌人不硬发东西");
    }

    /// <summary>
    /// 保底到点必出：发行数据里 0.14.0 之后没有带保底的槽了，所以照 `test_drop` 的老办法
    /// 用**内存夹具**（复制一行改内存，不碰策划的 CSV）。
    /// </summary>
    private void _CheckPityForced()
    {
        var local = new TableDb();
        local.load_all();
        var table = local.tables["drop_table"].AsGodotObject() as TableResource;
        CheckNotNull(table, "内存表库里读得到 drop_table");
        var copy = (TableResource)table!.Duplicate(true);
        var template = (DropTableRow)((DropTableRow)copy.rows[0].AsGodotObject()).Duplicate(true);
        template.drop_row_id = "dr_test_pity";
        template.drop_group = "drop_test_pity";
        template.slot = 1;
        template.item_id = "eq_sword_02";
        template.item_type = "equip";
        template.qty_min = 1;
        template.qty_max = 1;
        template.base_rate = 0.0; // 永远掷不中 → 只能靠保底出
        template.roll_type = "independent";
        template.pity_count = 20;
        template.difficulty_scaled = false;
        template.first_kill_only = false;
        copy.rows.Add(template);
        copy.index[template.id] = copy.rows.Count - 1;
        local.tables["drop_table"] = copy;

        PityTracker pity = PityTracker.create();
        DropResolver resolver = DropResolver.create(local, RngService.create(Seed), pity);
        int found_at = -1;
        for (int index = 0; index < 200; index++)
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_test_pity", "normal", new Godot.Collections.Dictionary());
            if (_CountOccurrences(drops, "eq_sword_02") > 0)
            {
                found_at = index + 1;
                break;
            }
        }
        CheckGt(found_at, 0.0, "保底槽在 20 次内必出货（base_rate=0 也能被保底顶出来）");
        CheckTrue(found_at <= 20, $"保底计数不超过 20（实际第 {found_at} 次）");
        CheckEq(pity.attempts("drop_test_pity|dr_test_pity"), 0, "出货后保底计数清零");

        // 计数塞进另一份跟踪器（＝存档往返）之后照样接着数
        var restored = PityTracker.create();
        restored.from_dict(pity.to_dict());
        for (int index = 0; index < 19; index++)
        {
            restored.register_attempt("drop_x|dr_x");
        }
        CheckFalse(restored.should_force("drop_x|dr_x", 20), "19 次还没到保底");
        restored.register_attempt("drop_x|dr_x");
        CheckTrue(restored.should_force("drop_x|dr_x", 20), "第 20 次触发保底");
    }

    /// <summary>运属性的掉落机缘只放大非必掉槽。</summary>
    private void _CheckDropBonus(TableDb db)
    {
        int plain = _CountDrops(db, "drop_wolf", "normal", "item_herb", 0.0);
        int boosted = _CountDrops(db, "drop_wolf", "normal", "item_herb", 0.5);
        CheckGt(boosted, plain * 1.2, "掉落机缘按倍率放大非必掉槽");
    }

    /// <summary>全队机缘取最高的那一个；空队伍是 0。</summary>
    private void _CheckPartyBonus(TableDb db)
    {
        GodotObject state = SoloState(db);
        Godot.Collections.Array actors = PartyBuilder.build_actors(db, state, new Godot.Collections.Array());
        CheckGt(actors.Count, 0.0, "建得出战斗单位（PartyBuilder 统一入口）");
        double bonus = DropResolver.party_drop_bonus(actors);
        CheckTrue(bonus >= 0.0, $"机缘不为负（实际 {bonus}）");
        CheckLt(bonus, 1.0, $"机缘是概率，不会到 1（实际 {bonus}）");
        CheckEq(DropResolver.party_drop_bonus(new Godot.Collections.Array()), 0.0, "空队伍机缘为 0");
    }

    private static int _CountDrops(TableDb db, string group, string difficulty, string item_id, double bonus)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        var options = new Godot.Collections.Dictionary { { "drop_rate_bonus", bonus } };
        int total = 0;
        for (int index = 0; index < Rolls; index++)
        {
            total += _CountOccurrences(resolver.roll_group(group, difficulty, options), item_id);
        }
        return total;
    }

    private static int _CountOccurrences(Godot.Collections.Array drops, string item_id)
    {
        int total = 0;
        foreach (Variant entry in drops)
        {
            if (entry.AsGodotDictionary()["item_id"].AsString() == item_id)
            {
                total++;
            }
        }
        return total;
    }
}
