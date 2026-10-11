using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 掉落结算（C# 版，对位原 `src/core/drop_resolver.gd`）。
///
/// 规则来自 `docs/design/05_装备与掉落.md`：
///   难度不改变掉落清单，只改变掉落几率 —— 唯一实现处就是 `difficulty_drop_rate`；
///   保底按掉落槽计数、跨难度继承（把 `PityTracker` 存进存档即可）；跨周目继承随多周目暂缓。
/// 本里程碑只按槽位掷点，装备的词条生成留到后续（掉落只产出「哪件装备、几件」）。
/// </summary>
public partial class DropResolver : RefCounted
{
    private readonly TableDb _db;
    private readonly RngService _rng;
    private PityTracker _pity;

    public DropResolver(TableDb db, RngService rng, PityTracker pity)
    {
        _db = db;
        _rng = rng;
        _pity = pity;
    }

    /// <summary>GDScript 侧的构造入口（带参数的 `new(…)` 对 C# 脚本是解析期报错，见施工单坑②）。</summary>
    public static DropResolver create(TableDb db, RngService rng, PityTracker pity) => new(db, rng, pity);

    public void set_pity(PityTracker pity) => _pity = pity;

    /// <summary>
    /// 全队掉落机缘：取队伍里最高的运属性 `drop_rate`（保守方案，上限 25% 写在表里）。
    ///
    /// `allies` 里放的是**战斗单位**。2026-10-11 起 `BattleActor` 已是 C#，直接强转调 `stat()`；
    /// 别的对象（还没有的形状／用例里的存根）仍走鸭子类型那条 `Call("stat")` 兜底。
    /// </summary>
    public static double party_drop_bonus(Godot.Collections.Array allies)
    {
        double best = 0.0;
        foreach (Variant entry in allies)
        {
            GodotObject actor = entry.AsGodotObject();
            if (actor == null)
            {
                continue;
            }
            double value = actor is BattleActor battle_actor
                ? battle_actor.stat("drop_rate")
                : actor.Call("stat", "drop_rate").AsSingle();
            best = Mathf.Max(best, value);
        }
        return best;
    }

    /// <summary>
    /// 结算一个掉落组。
    ///
    /// `options`：`{"first_kill": bool, "drop_rate_bonus": float}`
    /// 返回 `[{drop_row_id, item_id, item_type, qty, rarity, from_pity}, …]`。
    ///
    /// `difficulty_id` 与 `options` **不留可选参数**（见施工单坑②）：调用方写全实参。
    /// </summary>
    public Godot.Collections.Array roll_group(
        string drop_group,
        string difficulty_id,
        Godot.Collections.Dictionary options)
    {
        var rows = new System.Collections.Generic.List<DropTableRow>();
        foreach (Variant entry in _db.rows_where("drop_table", "drop_group", drop_group))
        {
            if (entry.AsGodotObject() is DropTableRow row)
            {
                rows.Add(row);
            }
        }
        if (rows.Count == 0)
        {
            GD.PushError($"DropResolver：drop_table 里没有掉落组 {drop_group}");
            return new Godot.Collections.Array();
        }
        rows.Sort((a, b) => a.slot.CompareTo(b.slot));

        bool first_kill = options.ContainsKey("first_kill") && options["first_kill"].AsBool();
        double drop_bonus = Mathf.Max(
            0.0,
            options.ContainsKey("drop_rate_bonus") ? options["drop_rate_bonus"].AsSingle() : 0.0);
        var out_rows = new Godot.Collections.Array();
        bool exclusive_taken = false;

        foreach (DropTableRow row in rows)
        {
            if (row.first_kill_only && !first_kill)
            {
                continue;
            }
            if (row.roll_type == "exclusive" && exclusive_taken)
            {
                continue;
            }

            string rarity = _item_rarity(row);
            double rate = row.base_rate;
            if (row.difficulty_scaled)
            {
                rate *= _difficulty_multiplier(difficulty_id, rarity);
            }
            if (!row.is_guaranteed())
            {
                // 运属性只对非必掉槽生效，避免把保底槽也放大
                rate *= 1.0 + drop_bonus;
            }
            rate = Mathf.Clamp(rate, 0.0, 1.0);

            string key = $"{row.drop_group}|{row.drop_row_id}";
            bool success = true;
            if (rate < 1.0)
            {
                success = _rng.chance(rate);
            }
            bool from_pity = false;
            if (!success && row.has_pity() && _pity != null)
            {
                _pity.register_attempt(key);
                if (_pity.should_force(key, row.pity_count))
                {
                    success = true;
                    from_pity = true;
                }
            }
            if (success && row.has_pity() && _pity != null)
            {
                _pity.reset(key);
            }
            if (!success)
            {
                continue;
            }
            if (row.roll_type == "exclusive")
            {
                exclusive_taken = true;
            }

            int qty_min = row.qty_min;
            int qty_max = Mathf.Max(qty_min, row.qty_max);
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "drop_row_id", row.drop_row_id },
                { "item_id", row.item_id },
                { "item_type", row.item_type },
                { "qty", _rng.randi_range(qty_min, qty_max) },
                { "rarity", rarity },
                { "from_pity", from_pity },
            });
        }
        return out_rows;
    }

    /// <summary>组内非必掉槽全部掷一遍（用于「重复刷」的期望值统计与测试）。</summary>
    public Godot.Collections.Array roll_group_many(
        string drop_group,
        int times,
        string difficulty_id,
        Godot.Collections.Dictionary options)
    {
        var out_rows = new Godot.Collections.Array();
        for (int index = 0; index < Mathf.Max(0, times); index++)
        {
            out_rows.Add(roll_group(drop_group, difficulty_id, options));
        }
        return out_rows;
    }

    private double _difficulty_multiplier(string difficulty_id, string rarity_id)
    {
        var row = _db.get_row("difficulty_drop_rate", $"{difficulty_id}|{rarity_id}") as DifficultyDropRateRow;
        return row?.rate_multiplier ?? 1.0;
    }

    /// <summary>
    /// 敌人的**装备掉落**（设计 10 §五，0.14.0 起）：不再在 `drop_table` 里重复列装备，
    /// 而是**从 `enemy_equip` 推**——身上穿的，就是能掉的。
    ///
    /// 口径（表里只写了「概率复用 `rarity_def.drop_weight`」，其余是开发侧按那句定的，
    /// 已登记 `待策划确认.md` Q56）：
    ///   · 每件装备各自掷一次，概率 = `drop_weight / 100`（凡品 100 → 必掉；传世 0.5 → 0.5%）
    ///   · **首杀必掉一件**：优先**武器槽**（设计说「大寨主身上挂的就是黑风刀，效果不变」，
    ///     醉刀客那把锈月也在武器槽上），没有武器就退而取稀有度最高的那件
    ///   · 掷出来的条目与 `drop_table` 的条目同形（`{item_id, qty}`），直接交给 `BattleReward`
    ///     的掉落分支（装备会建实例并滚词条）
    /// </summary>
    public Godot.Collections.Array roll_enemy_equipment(string enemy_id, bool first_kill)
    {
        var rows = new System.Collections.Generic.List<EnemyEquipRow>();
        foreach (Variant entry in _db.rows_where("enemy_equip", "enemy_id", enemy_id))
        {
            if (entry.AsGodotObject() is EnemyEquipRow row)
            {
                rows.Add(row);
            }
        }
        if (rows.Count == 0)
        {
            return new Godot.Collections.Array();
        }
        var out_rows = new Godot.Collections.Array();
        bool guaranteed_done = false;
        // 首杀那一件先定下来（武器优先），免得「随机掷出来的」把保底名额占掉之后还要补一件
        string guaranteed_id = first_kill ? _first_kill_pick(rows) : "";
        foreach (EnemyEquipRow row in rows)
        {
            string equip_id = row.equip_id;
            if (equip_id.Length == 0)
            {
                continue;
            }
            if (guaranteed_id.Length > 0 && equip_id == guaranteed_id && !guaranteed_done)
            {
                guaranteed_done = true;
                out_rows.Add(new Godot.Collections.Dictionary
                {
                    { "item_id", equip_id },
                    { "qty", 1 },
                    { "source", "enemy_equip_guaranteed" },
                });
                continue;
            }
            if (_rng.chance(_equipment_rate(equip_id)))
            {
                out_rows.Add(new Godot.Collections.Dictionary
                {
                    { "item_id", equip_id },
                    { "qty", 1 },
                    { "source", "enemy_equip" },
                });
            }
        }
        if (first_kill && !guaranteed_done && guaranteed_id.Length > 0)
        {
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "item_id", guaranteed_id },
                { "qty", 1 },
                { "source", "enemy_equip_guaranteed" },
            });
        }
        return out_rows;
    }

    /// <summary>一件装备的掉落概率：稀有度的 `drop_weight` ÷ 100，夹在 0~1（表里 100 表示「必掉」）。</summary>
    private double _equipment_rate(string equip_id)
    {
        var equip = _db.get_row("equip_base", equip_id) as EquipBaseRow;
        if (equip == null)
        {
            return 0.0;
        }
        var rarity = _db.get_row("rarity_def", equip.rarity) as RarityDefRow;
        return rarity == null ? 0.0 : Mathf.Clamp(rarity.drop_weight / 100.0, 0.0, 1.0);
    }

    /// <summary>首杀保底给哪一件：优先武器槽，没有武器就取稀有度最高的（同稀有度取列表里靠前的）。</summary>
    private string _first_kill_pick(System.Collections.Generic.List<EnemyEquipRow> rows)
    {
        string weapon_id = "";
        string best_id = "";
        double best_weight = -1.0;
        foreach (EnemyEquipRow row in rows)
        {
            string equip_id = row.equip_id;
            var equip = _db.get_row("equip_base", equip_id) as EquipBaseRow;
            if (equip == null)
            {
                continue;
            }
            double weight = 0.0;
            var rarity = _db.get_row("rarity_def", equip.rarity) as RarityDefRow;
            if (rarity != null)
            {
                weight = rarity.drop_weight;
            }
            if (equip.slot == "weapon" && weapon_id.Length == 0)
            {
                weapon_id = equip_id;
            }
            if (weight > best_weight)
            {
                best_weight = weight;
                best_id = equip_id;
            }
        }
        return weapon_id.Length > 0 ? weapon_id : best_id;
    }

    private string _item_rarity(DropTableRow drop_row)
    {
        if (drop_row.item_type == "equip")
        {
            var equip = _db.get_row("equip_base", drop_row.item_id) as EquipBaseRow;
            if (equip == null)
            {
                GD.PushError($"DropResolver：equip_base 缺少 {drop_row.item_id}");
                return "common";
            }
            return equip.rarity;
        }
        var item = _db.get_row("item_base", drop_row.item_id) as ItemBaseRow;
        if (item == null)
        {
            GD.PushError($"DropResolver：item_base 缺少 {drop_row.item_id}");
            return "common";
        }
        return item.rarity;
    }
}
