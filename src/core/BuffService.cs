using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 统一 buff 系统（C# 版，对位原 `src/core/buff_service.gd`；设计 08）。
///
/// 边界：这个类只管「表怎么读 + 一个 buff 的静态规则」——极性、作用域、持续、叠加、
/// 它带哪些数值修正、谁会发它。**结算不在这里**：上 buff / 每回合递减 / 把修正并进属性，
/// 都要动已经跑通的血气／内力／架势管线，落在战斗侧（`BattleActor` / `BattleSimulator`）。
///
/// 与 `status_effect` 的分工（设计原文）：**会逐回合掉血的走 status_effect，其余走 buff_def**。
///
/// **过渡期的常量取值口**：GDScript 读不到 C# 的 `const`（`Invalid access to property or key`，
/// 与 `AttributeCalculator.CONTRIB_*` 同一个坑），而 `battle_actor.gd`／`test_buff.gd` 现在直接读
/// `BuffServiceScript.STACK_UNIQUE` 这类名字。所以每个对外常量配一个同名小写静态方法，
/// 调用方走 `BuffServiceScript.stack_unique()`；P5 把 GDScript 清零后把它连着调用方一起删掉。
/// </summary>
public partial class BuffService : RefCounted
{
    /// <summary>`buff_def.is_debuff`：0 增益 / 1 减益（同一套机制，UI 上分红蓝）。</summary>
    public const int POLARITY_BUFF = 0;
    public const int POLARITY_DEBUFF = 1;

    /// <summary>`buff_def.scope`。</summary>
    public const string SCOPE_BATTLE = "battle";   // 进战斗清空、战斗结束清除
    public const string SCOPE_FIELD = "field";     // 跨场景保留，带进战斗后按回合递减

    /// <summary>`buff_def.stack_rule`。</summary>
    public const string STACK_REFRESH = "refresh"; // 只刷新剩余回合，不叠层
    public const string STACK_STACK = "stack";     // 叠层，上限 max_stack
    public const string STACK_UNIQUE = "unique";   // 全局唯一，重复获得无效

    /// <summary>`buff_def.duration`：&gt;0 剩余回合；0 常驻（随来源存在而存在）；-1 持续到本场结束。</summary>
    public const int DURATION_PERMANENT = 0;
    public const int DURATION_UNTIL_BATTLE_END = -1;

    /// <summary>`buff_def.effect_kind`。</summary>
    public const string EFFECT_KIND_STAT = "stat";         // 数值型：加成写在 buff_stat
    public const string EFFECT_KIND_COMPUTED = "computed"; // 算出来（通用运功＝该内功常驻加成 ×2）
    public const string EFFECT_KIND_SPECIAL = "special";   // 特判

    // ---------------------------------------------------------------- 过渡期取值口

    public static int polarity_buff() => POLARITY_BUFF;
    public static int polarity_debuff() => POLARITY_DEBUFF;
    public static string scope_battle() => SCOPE_BATTLE;
    public static string scope_field() => SCOPE_FIELD;
    public static string stack_refresh() => STACK_REFRESH;
    public static string stack_stack() => STACK_STACK;
    public static string stack_unique() => STACK_UNIQUE;
    public static int duration_permanent() => DURATION_PERMANENT;
    public static int duration_until_battle_end() => DURATION_UNTIL_BATTLE_END;
    public static string effect_kind_stat() => EFFECT_KIND_STAT;
    public static string effect_kind_computed() => EFFECT_KIND_COMPUTED;
    public static string effect_kind_special() => EFFECT_KIND_SPECIAL;

    // ---------------------------------------------------------------- 实例

    private readonly TableDb _db;

    /// <summary>
    /// 表在运行期是**不可变**的（构建期生成、只读），所以这里可以放心缓存两样经常被反复问的东西：
    ///   · `buff_grant` 按「来源」分桶——`grants_for` 以前每次调用都全表扫一遍（4 人满装时逐件装备
    ///     各扫一次，把「构造」从 1.6ms 抬到 5.3ms，性能观测工具量出来的）；
    ///   · `buff_stat` 按 buff_id 的结果——同一 buff 会被反复问。
    /// 要改表请新建一个 TableDb 实例（夹具就是这么做的），别在这个实例上就地改。
    /// </summary>
    private readonly Godot.Collections.Dictionary _grants_by_source = new();
    private bool _grants_indexed;
    private readonly Godot.Collections.Dictionary _stat_mods_cache = new();

    public BuffService(TableDb table_db)
    {
        _db = table_db;
    }

    /// <summary>GDScript 侧唯一的构造入口。</summary>
    public static BuffService create(TableDb table_db) => new(table_db);

    public Godot.Collections.Array all_defs() => _db.rows("buff_def");

    public BuffDefRow def_of(string buff_id) => _db.get_row("buff_def", buff_id) as BuffDefRow;

    public bool exists(string buff_id) => def_of(buff_id) != null;

    public bool is_debuff(string buff_id) => def_of(buff_id)?.is_debuff ?? false;

    public int polarity_of(string buff_id) => is_debuff(buff_id) ? POLARITY_DEBUFF : POLARITY_BUFF;

    public string scope_of(string buff_id) => def_of(buff_id)?.scope ?? "";

    public bool is_field_buff(string buff_id) => scope_of(buff_id) == SCOPE_FIELD;

    public int duration_of(string buff_id) => def_of(buff_id)?.duration ?? 0;

    /// <summary>
    /// 战斗外增益的有效分钟数（0.8.0 起：`scope=field` 用**现实时间**计时，
    /// 进战斗后转为整场有效、不按回合递减）。`battle` 的 buff 这里读出来是 0。
    /// </summary>
    public int field_minutes_of(string buff_id) => def_of(buff_id)?.field_minutes ?? 0;

    public bool is_permanent(string buff_id) => duration_of(buff_id) == DURATION_PERMANENT;

    public string stack_rule_of(string buff_id) => def_of(buff_id)?.stack_rule ?? "";

    public int max_stack_of(string buff_id) => def_of(buff_id)?.max_stack ?? 1;

    /// <summary>`stat` 数值型（走 buff_stat）／`computed` 算出来／`special` 特判。</summary>
    public string effect_kind_of(string buff_id) => def_of(buff_id)?.effect_kind ?? "";

    /// <summary>
    /// 一个 buff 带的数值修正：`[{"kind": "attr"|"stat", "target_id": String, "value": float}]`。
    /// `attr:*` 是属性点（和加点一样先经 attr_to_stat），`stat:*` 是派生数值（固定值层）。
    /// </summary>
    public Godot.Collections.Array stat_mods_of(string buff_id)
    {
        if (_stat_mods_cache.ContainsKey(buff_id))
        {
            return _stat_mods_cache[buff_id].AsGodotArray();
        }

        var out_mods = new Godot.Collections.Array();
        foreach (Variant entry in _db.rows("buff_stat"))
        {
            var row = entry.AsGodotObject() as BuffStatRow;
            if (row == null || row.buff_id != buff_id)
            {
                continue;
            }

            Godot.Collections.Dictionary parsed = row.parsed_target();
            out_mods.Add(new Godot.Collections.Dictionary
            {
                { "kind", parsed["kind"].AsString() },
                { "target_id", parsed["target_id"].AsString() },
                { "value", row.value },
            });
        }

        _stat_mods_cache[buff_id] = out_mods;
        return out_mods;
    }

    /// <summary>
    /// 把一个 buff 的数值修正转成 `AttributeCalculator` 的贡献列表。
    ///
    /// 常驻 buff（装备 on_equip、套装档位）走**这条通道**并在属性计算里生效——
    /// 这样「装备即生效」在角色面板与战斗里读到的是同一个数，不必再加第二条结算路径。
    /// 临时 buff 由 `BattleActor` 在战斗里调用同一个函数重新算一遍属性。
    /// 迁移期**不留默认值**：GDScript 调用方一律写明 `source`／`stacks`。
    /// </summary>
    public Godot.Collections.Array contributions_of(string buff_id, string source, int stacks)
    {
        var out_contribs = new Godot.Collections.Array();
        string label = source.Length > 0 ? source : $"buff:{buff_id}";
        foreach (Variant mod_entry in stat_mods_of(buff_id))
        {
            Godot.Collections.Dictionary mod = mod_entry.AsGodotDictionary();
            string kind = mod["kind"].AsString() == "attr"
                ? AttributeCalculator.CONTRIB_ATTR_POINT
                : AttributeCalculator.CONTRIB_STAT_FLAT;
            for (var index = 0; index < System.Math.Max(1, stacks); index++)
            {
                out_contribs.Add(new Godot.Collections.Dictionary
                {
                    { "kind", kind },
                    { "target", mod["target_id"].AsString() },
                    { "value", mod["value"].AsDouble() },
                    { "source", label },
                });
            }
        }

        return out_contribs;
    }

    /// <summary>谁会发这个 buff（`buff_grant` 里 source_id == 它）；`trigger` 留空表示不限时机。</summary>
    public Godot.Collections.Array grants_of_buff(string buff_id, string trigger)
    {
        var out_grants = new Godot.Collections.Array();
        foreach (Variant entry in _db.rows("buff_grant"))
        {
            var row = entry.AsGodotObject() as BuffGrantRow;
            if (row == null || row.buff_id != buff_id)
            {
                continue;
            }

            if (trigger.Length > 0 && row.trigger != trigger)
            {
                continue;
            }

            out_grants.Add(row);
        }

        return out_grants;
    }

    /// <summary>某个来源（装备／内功／招式／套装）在指定时机发的 buff 行。</summary>
    public Godot.Collections.Array grants_for(string source_type, string source_id, string trigger)
    {
        var out_grants = new Godot.Collections.Array();
        foreach (Variant entry in _grants_of_source(source_type, source_id))
        {
            var row = entry.AsGodotObject() as BuffGrantRow;
            if (row == null)
            {
                continue;
            }

            if (trigger.Length > 0 && row.trigger != trigger)
            {
                continue;
            }

            out_grants.Add(row);
        }

        return out_grants;
    }

    /// <summary>按「来源」取发放行（走一次建好的分桶索引，不再每次全表扫）。</summary>
    private Godot.Collections.Array _grants_of_source(string source_type, string source_id)
    {
        if (!_grants_indexed)
        {
            foreach (Variant entry in _db.rows("buff_grant"))
            {
                var row = entry.AsGodotObject() as BuffGrantRow;
                if (row == null)
                {
                    continue;
                }

                string key = $"{row.source_type}|{row.source_id}";
                Godot.Collections.Array bucket = _grants_by_source.ContainsKey(key)
                    ? _grants_by_source[key].AsGodotArray()
                    : new Godot.Collections.Array();
                bucket.Add(row);
                _grants_by_source[key] = bucket;
            }

            _grants_indexed = true;
        }

        string wanted = $"{source_type}|{source_id}";
        return _grants_by_source.ContainsKey(wanted)
            ? _grants_by_source[wanted].AsGodotArray()
            : new Godot.Collections.Array();
    }

    /// <summary>
    /// 这个招式有没有「施放时发 buff」的配置（`skill_active` 的 `on_cast`）。
    ///
    /// **增益招式**（例：醉里乾坤·醉步 → 忘忧）就是靠这个判定「能不能用」：它没有伤害类型与倍率
    /// （`is_attack()` 为假），但表里给它配了 on_cast 发放——施放一次给自己上 buff，不走伤害管线。
    /// 判定放这里，是因为「谁的 on_cast 发什么」的唯一出处就是这张表（见框架说明决策 221）。
    /// </summary>
    public bool has_cast_grant(string skill_id) =>
        skill_id.Length > 0 && grants_for("skill_active", skill_id, "on_cast").Count > 0;

    /// <summary>常驻类（`duration == 0`）：随来源存在而存在——装备与内功被动的数值加成走这条。</summary>
    public Godot.Collections.Array persistent_grants_for(string source_type, string source_id)
    {
        var out_grants = new Godot.Collections.Array();
        foreach (Variant entry in grants_for(source_type, source_id, ""))
        {
            var row = entry.AsGodotObject() as BuffGrantRow;
            if (row != null && is_permanent(row.buff_id))
            {
                out_grants.Add(row);
            }
        }

        return out_grants;
    }
}
