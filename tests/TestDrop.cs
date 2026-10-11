using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 掉落：必掉／概率、难度倍率不改清单、保底跨难度继承——对位 `tests/test_drop.gd`
/// （2026-10-11 P4 第五批搬 C#）。
///
/// 与 `TestDropResolver.cs` 的分工：那一份是 `DropResolver` 搬 C# 时补的对位用例（必掉槽／概率区间／
/// 难度只改几率／首杀武器优先／保底到点必出／机缘倍率），**这一份是旧链那份"掉落与保底"**——
/// 多出来的是**保底计数器的语义与序列化**、**首杀记录进存档（含 v9 老档迁移）**、
/// **权重抽取器 `pick_weighted`** 这三块。
/// </summary>
public partial class TestDrop : TestCase
{
    private const int Seed = 90210;
    private const int Rolls = 2000;

    // 名字**故意与 `TestDropResolver` 那份区分开**：那一份的 suite 名是「掉落与保底（C# 侧）」，
    // 两份都叫「掉落与保底」时日志里看不出哪条红了（这份管的是保底计数器语义／首杀记录进存档／权重抽取）。
    public override string SuiteName() => "掉落、首杀与保底";

    public override void Run()
    {
        TableDb db = GetDb();
        // 保底字典里的版本标记（只写不读，跨读档不用它做分支）——写死绝对值，动它要同时想清楚兼容
        CheckEq(PityTracker.version(), 1, "保底字典的版本标记 = 1");
        _check_guaranteed_and_rate(db);
        _check_weighted_pick();
        _check_difficulty_scaling(db);
        _check_difficulty_keeps_list(db);
        _check_first_kill_only(db);
        _check_first_kill_persistence(db);
        _check_pity(db);
        _check_pity_cross_difficulty(db);
        _check_drop_bonus(db);
    }

    private void _check_guaranteed_and_rate(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        var money = 0;
        var herb = 0;
        for (var index = 0; index < Rolls; index++)
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_wolf", "normal", new Godot.Collections.Dictionary());
            money += _count_occurrences(drops, "item_money");
            herb += _count_occurrences(drops, "item_herb");
        }

        CheckEq(money, Rolls, "必掉槽每次都出铜钱");
        CheckInRange(herb, Rolls * 0.20, Rolls * 0.30, "草药掉率约 25%");

        // 数量区间：野狼铜钱配置为 3~8
        var qty_ok = true;
        for (var index = 0; index < 200; index++)
        {
            foreach (Variant entry_variant in resolver.roll_group(
                         "drop_wolf", "normal", new Godot.Collections.Dictionary()))
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                if (_s(entry, "item_id") == "item_money")
                {
                    int qty = _i(entry, "qty");
                    qty_ok = qty_ok && qty >= 3 && qty <= 8;
                }
            }
        }

        CheckTrue(qty_ok, "铜钱数量落在配置区间 3~8");
    }

    /// <summary>`RngService.pick_weighted` 是以后生成装备词条要用的抽取器，这里先钉住行为。</summary>
    private void _check_weighted_pick()
    {
        RngService rng = RngService.create(Seed);
        var entries = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary { { "key", "a" }, { "weight", 1.0 } },
            new Godot.Collections.Dictionary { { "key", "b" }, { "weight", 3.0 } },
        };
        var hits = 0;
        for (var index = 0; index < 2000; index++)
        {
            if (rng.pick_weighted(entries).AsString() == "b")
            {
                hits += 1;
            }
        }

        CheckInRange(hits, 1400.0, 1600.0, "权重 1:3 时大致四分之三落在 b");
        // 空表返回的是「装着 null 的 Variant」，得先取成对象再比——直接拿 Variant 比 null 会在装箱那层判不等
        CheckEq(rng.pick_weighted(new Godot.Collections.Array()).AsGodotObject(), null, "空权重表返回 null");
    }

    private void _check_difficulty_scaling(TableDb db)
    {
        int normal = _count_drops(db, "drop_wolf", "normal", "item_herb");
        int hard = _count_drops(db, "drop_wolf", "hard", "item_herb");
        CheckGt(hard, normal * 1.2, "困难难度对凡品倍率 ×1.5，掉落明显增多");
    }

    private void _check_difficulty_keeps_list(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        foreach (var difficulty in new[] { "normal", "nightmare" })
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_hidden_drunk", difficulty, new Godot.Collections.Dictionary());
            CheckEq(_count_occurrences(drops, "item_scroll_drunk"), 1, $"{difficulty} 下必掉隐藏武学残卷");
            // 设计 10 §五（0.14.0）：**`drop_table` 不再列装备**——装备改由 `enemy_equip` 推（见下面那条）
            CheckEq(_count_occurrences(drops, "eq_sword_04"), 0, $"{difficulty} 下掉落表里不再列装备（装备走 enemy_equip）");
        }
    }

    private void _check_first_kill_only(TableDb db)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        // 0.14.0 起「首杀必掉装备」由 `enemy_equip` 接管（大寨主身上挂的就是黑风刀）
        Godot.Collections.Array first_drops = resolver.roll_enemy_equipment("en_bd_boss", true);
        CheckEq(_count_occurrences(first_drops, "eq_sword_03"), 1, "首杀必掉他身上的武器（黑风刀）");
        CheckEq(
            _count_occurrences(first_drops, "eq_sword_03"),
            1,
            "首杀那一次**只保一件**，不会因为保底再额外多给一件");
        // 重复刷：黑风刀回到它自己的稀有度概率（良品 → 40%），不再必掉
        var hits = 0;
        for (var index = 0; index < 200; index++)
        {
            Godot.Collections.Array drops = resolver.roll_enemy_equipment("en_bd_boss", false);
            if (_count_occurrences(drops, "eq_sword_03") > 0)
            {
                hits += 1;
            }
        }

        CheckInRange(hits, 50.0, 110.0, $"重复刷黑风刀按良品的 40%% 出货（200 次里 {hits} 次）");
    }

    /// <summary>首杀记录必须进存档：以前它只存在会话里，退出重进就能再领一次首杀固定掉落。</summary>
    private void _check_first_kill_persistence(TableDb db)
    {
        GodotObject state = SoloState(db);
        var gd_state = (GameState)state;
        CheckFalse(gd_state.has_first_kill("drop_bd_boss"), "新档没有首杀记录");
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);

        bool first_pass = gd_state.mark_first_kill("drop_bd_boss");
        CheckTrue(first_pass, "第一次击倒算首杀");
        CheckTrue(gd_state.has_first_kill("drop_bd_boss"), "首杀记录写进状态");
        CheckEq(
            _count_occurrences(resolver.roll_enemy_equipment("en_bd_boss", first_pass), "eq_sword_03"),
            1,
            "首杀那次必掉他身上的武器（黑风刀）");

        bool second_pass = gd_state.mark_first_kill("drop_bd_boss");
        CheckFalse(second_pass, "同一掉落组第二次不再算首杀");
        CheckTrue(
            _count_occurrences(resolver.roll_enemy_equipment("en_bd_boss", second_pass), "eq_sword_03") <= 1,
            "第二次不再保底（最多按良品 40% 掷中一件）");

        // 存档往返 = 退出重进：首杀记录必须留着（这就是漏洞的正面用例）
        GameState back = GameState.from_dict(gd_state.to_dict(), db);
        CheckEq(back.migrated_from, 0, "v10 存档不需要迁移");
        CheckTrue(back.has_first_kill("drop_bd_boss"), "存档往返后仍是已首杀");
        CheckEq(back.first_kill_count(), 1, "首杀记录数量往返一致");
        CheckFalse(back.mark_first_kill("drop_bd_boss"), "读档后同一组依旧不算首杀");

        // v9 及更早的老档没有这份记录 → 当作「还没打过首杀」（口径写进交接表）
        Godot.Collections.Dictionary legacy = gd_state.to_dict();
        legacy["version"] = 9;
        legacy.Remove("first_kills");
        GameState migrated = GameState.from_dict(legacy, db);
        CheckNotNull(migrated, "v9 老档能读进来");
        if (migrated != null)
        {
            CheckEq(migrated.migrated_from, 9, "v9 老档记下迁移来源");
            CheckEq(migrated.version, GameState.save_version(), "v9 迁移后版本升到当前");
            CheckFalse(migrated.has_first_kill("drop_bd_boss"), "v9 老档视作没打过首杀");
        }
    }

    private void _check_pity(TableDb db)
    {
        // 0.14.0 起 `drop_table` 里那 12 行「敌人身上装备」被删掉了（设计 10 §五），
        // 其中就包括带保底的那两条——所以**保底机制本身**改用内存夹具验（复制表、只改内存，
        // 不碰策划的 CSV；同 `test_case.table_with_*` 的做法）。
        TableDb local = _db_with_pity_row("dr_test_pity", "drop_test_pity", 20);

        PityTracker pity = PityTracker.create();
        DropResolver resolver = DropResolver.create(local, RngService.create(Seed), pity);
        var found_at = -1;
        for (var index = 0; index < 200; index++)
        {
            Godot.Collections.Array drops = resolver.roll_group(
                "drop_test_pity", "normal", new Godot.Collections.Dictionary());
            if (_count_occurrences(drops, "eq_sword_02") > 0)
            {
                found_at = index + 1;
                break;
            }
        }

        CheckGt(found_at, 0.0, "保底槽在 20 次内必出货（base_rate=0 也能被保底顶出来）");
        CheckTrue(found_at <= 20, $"保底计数不超过 20（实际第 {found_at} 次）");
        CheckEq(pity.attempts("drop_test_pity|dr_test_pity"), 0, "出货后保底计数清零");

        // 保底计数单位本身的语义
        PityTracker tracker = PityTracker.create();
        for (var index = 0; index < 19; index++)
        {
            tracker.register_attempt("drop_x|dr_x");
        }

        CheckFalse(tracker.should_force("drop_x|dr_x", 20), "19 次还没到保底");
        tracker.register_attempt("drop_x|dr_x");
        CheckTrue(tracker.should_force("drop_x|dr_x", 20), "第 20 次触发保底");
        PityTracker restored = PityTracker.create();
        restored.from_dict(tracker.to_dict());
        CheckEq(restored.attempts("drop_x|dr_x"), 20, "保底计数可序列化与恢复（跨周目继承的落点）");
        restored.reset("drop_x|dr_x");
        CheckEq(restored.attempts("drop_x|dr_x"), 0, "reset 清空计数");
    }

    /// <summary>
    /// 保底跨难度继承：同一个 `PityTracker` 带着走，切难度不影响计数。
    /// 同样用**内存夹具**（那两条带保底的发行数据行 0.14.0 已删，见 `_check_pity` 的说明）。
    /// </summary>
    private void _check_pity_cross_difficulty(TableDb db)
    {
        TableDb local = _db_with_pity_row("dr_test_pity_x", "drop_test_pity_x", 20);
        PityTracker pity = PityTracker.create();
        const string key = "drop_test_pity_x|dr_test_pity_x";
        for (var index = 0; index < 19; index++)
        {
            pity.register_attempt(key);
        }

        DropResolver resolver = DropResolver.create(local, RngService.create(Seed), pity);
        Godot.Collections.Array drops = resolver.roll_group(
            "drop_test_pity_x", "hard", new Godot.Collections.Dictionary());
        CheckEq(_count_occurrences(drops, "eq_sword_02"), 1, "带着 19 次计数切到困难，下一次必出");
    }

    private void _check_drop_bonus(TableDb db)
    {
        int base_count = _count_drops(db, "drop_wolf", "normal", "item_herb");
        int bonus = _count_drops(
            db,
            "drop_wolf",
            "normal",
            "item_herb",
            new Godot.Collections.Dictionary { { "drop_rate_bonus", 0.5 } });
        CheckGt(bonus, base_count * 1.2, "运属性掉落机缘按倍率放大非必掉槽");
    }

    /// <summary>
    /// 造一份「一条 base_rate=0 的保底槽」表库（复制真实表 → 复制首行改数 → 写回 rows／index）。
    /// 保底机制本身要验，但发行数据里那两条带保底的行 0.14.0 已删，所以只能靠夹具。
    /// </summary>
    private static TableDb _db_with_pity_row(string row_id, string group_id, int pity_count)
    {
        var local = new TableDb();
        local.load_all();
        var table = (TableResource)local.tables["drop_table"].As<TableResource>().Duplicate(true);
        var template = (DropTableRow)table.rows[0].As<TableRow>().Duplicate(true);
        template.drop_row_id = row_id;
        template.drop_group = group_id;
        template.slot = 1;
        template.item_id = "eq_sword_02";
        template.item_type = "equip";
        template.qty_min = 1;
        template.qty_max = 1;
        template.base_rate = 0.0; // 永远掷不中 → 只能靠保底出
        template.roll_type = "independent";
        template.pity_count = pity_count;
        template.difficulty_scaled = false;
        template.first_kill_only = false;
        table.rows.Add(template);
        table.index[template.id] = table.rows.Count - 1;
        local.tables["drop_table"] = table;
        return local;
    }

    private int _count_drops(
        TableDb db,
        string group,
        string difficulty,
        string item_id,
        Godot.Collections.Dictionary options = null)
    {
        DropResolver resolver = DropResolver.create(db, RngService.create(Seed), null);
        var total = 0;
        for (var index = 0; index < Rolls; index++)
        {
            total += _count_occurrences(
                resolver.roll_group(group, difficulty, options ?? new Godot.Collections.Dictionary()),
                item_id);
        }

        return total;
    }

    /// <summary>出现次数（掉率统计用；数量区间另行校验）。</summary>
    private static int _count_occurrences(Godot.Collections.Array drops, string item_id)
    {
        var total = 0;
        foreach (Variant entry_variant in drops)
        {
            if (_s(entry_variant.AsGodotDictionary(), "item_id") == item_id)
            {
                total += 1;
            }
        }

        return total;
    }

    private static string _s(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;
}
