using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 战斗单位：玩家角色与敌人都用同一套快照结构。
///
/// 它只保存「已算好的属性快照 + 当前资源」，不含 AI、不含场景节点，
/// 这样战斗能在 headless 下整场跑完，也方便以后接 UI。
///
/// 对位原 `src/core/battle_actor.gd`（796 行），按职责拆成三份 partial：
/// 本文件是主壳（字段／常量／资源／工厂／属性重算），异常状态在 `BattleActorStatus.cs`、
/// buff 在 `BattleActorBuffs.cs`。
///
/// **跨语言约定（迁移期）**：GDScript 的 `preload("…/battle_actor.gd")` 一律换成
/// `preload("…/BattleActor.cs")`，`BattleActorScript.new()` 照旧能造实例。
/// 读**常量**要走小写取值口（`side_ally()`／`side_enemy()`）——GDScript 读不到 C# 的 `const`；
/// 带默认值的方法**一律按元数补重载**（默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
/// </summary>
public partial class BattleActor : RefCounted
{
    public const int SIDE_ALLY = 0;
    public const int SIDE_ENEMY = 1;

    /// <summary>阵营常量的取值口（GDScript 侧读不到 C# 的 `const`）。</summary>
    public static int side_ally() => SIDE_ALLY;

    public static int side_enemy() => SIDE_ENEMY;

    /// <summary>
    /// 只能由装备或临时效果提供的派生数值：没有来源时补 0，
    /// 保证伤害管线取得到（来源在 equip_base 的 bonus_* 列）。
    /// </summary>
    private static readonly string[] EQUIPMENT_ONLY_STATS =
        { "pen_rate", "block_rate", "block_reduction", "dmg_reduction" };

    /// <summary>
    /// 灼伤每层降低的外功防御比例。**正常取自 `status_effect.burn_def_cut`**（设计 08 参数口径
    /// Q86 已补列）；这里的常量只是老用例手搓状态的兜底值。
    /// </summary>
    public const double BURN_DEF_DOWN_PER_STACK = 0.05;

    /// <summary>内伤「内力回复减半」的兜底值，正常取自 `status_effect.qi_regen_mul`。</summary>
    public const double INTERNAL_QI_REGEN_FACTOR = 0.5;

    /// <summary>
    /// 架势（破防资源）。
    /// 敌人取自 `enemy_base.poise`；玩家没有对应列，先按「40 + 8×等级」给一条基线
    /// （记在交接表里请设计补一列，比如 character_base.poise 或 stat_def.poise_max）。
    /// 回合末回复比例：Boss「架势回复极快」，按 30% 处理（同样等表里补 poise_regen）。
    /// </summary>
    public const double PLAYER_POISE_BASE = 40.0;

    public const double PLAYER_POISE_PER_LEVEL = 8.0;
    public const double POISE_REGEN_DEFAULT = 0.1;
    public const double POISE_REGEN_BOSS = 0.3;

    // ------------------------------------------------------------------ 单位快照
    //
    // 全部写 `[Export] public … { get; set; }`：GDScript 直接读写的字段**只有 [Export] 才注册**
    // （裸 `public` 属性在 GDScript 里取不到，见施工单「二点四七」）。

    /// <summary>战斗内的唯一标识（同名敌人加序号）。</summary>
    [Export] public string actor_id { get; set; } = "";

    /// <summary>来源 id：角色为 character_base.char_id，敌人为 enemy_base.enemy_id。</summary>
    [Export] public string source_id { get; set; } = "";

    [Export] public string display_name { get; set; } = "";

    [Export] public int side { get; set; } = SIDE_ALLY;

    [Export] public int level { get; set; } = 1;

    /// <summary>stat_def.stat_id → 最终数值。</summary>
    [Export] public Godot.Collections.Dictionary stats { get; set; } = new();

    /// <summary>异常状态抗性（敌人专属，键为 poison/burn/bleed/internal）。</summary>
    [Export] public Godot.Collections.Dictionary resistances { get; set; } = new();

    /// <summary>附带标记：faction / threat_tag / ai_template 等，供上层筛选与表现用。</summary>
    [Export] public Godot.Collections.Dictionary tags { get; set; } = new();

    [Export] public string[] skills { get; set; } = System.Array.Empty<string>();

    /// <summary>已装配的内功（08 的「内功」指令按这个列表逐个催动）。</summary>
    [Export] public string[] passives { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// 进场时要发的常驻／整场 buff（由 PartyBuilder 按存档算好：装备 on_battle_start、战斗外增益…）。
    /// 每条 {buff_id, source_type, source_id, stacks}；`BattleSimulator.setup()` 开打时逐条施加。
    /// </summary>
    [Export] public Godot.Collections.Array pending_grants { get; set; } = new();

    /// <summary>命中时触发的装备特效（`buff_grant.trigger=on_hit`），由模拟器在命中后施加。</summary>
    [Export] public Godot.Collections.Array on_hit_grants { get; set; } = new();

    [Export] public int hp { get; set; } = 0;

    [Export] public int qi { get; set; } = 0;

    /// <summary>
    /// 命中基准。
    /// 文档没给命中公式，这里按两张表的语义各自落地：
    ///   玩家角色：敏换算出的 hit_rate 是「相对基准的加成」，基准 1.0
    ///   敌人：0.28.0 起同样统一成 1.0（设计 10 §七第四条，见 `from_enemy`）
    /// 命中率 = clamp(base_accuracy + hit_rate - 目标 dodge_rate, 5%, 99%)。
    /// </summary>
    [Export] public double base_accuracy { get; set; } = 1.0;

    /// <summary>击杀奖励与掉落组（敌人用；角色为 0 / 空）。</summary>
    [Export] public int reward_exp { get; set; } = 0;

    [Export] public int reward_money { get; set; } = 0;

    [Export] public string drop_group { get; set; } = "";

    /// <summary>武学熟练度：skill_id → 等级（0 表示没练过，倍率系数按 0 算）。</summary>
    [Export] public Godot.Collections.Dictionary skill_mastery { get; set; } = new();

    /// <summary>
    /// 天赋里 `rule:` 那几条（设计 12 §五）：`{rule_id: 合计值}`，由 `PartyBuilder` 从存档算好。
    /// **谁消费谁读**（先手在回合顺序里读、招式伤害在伤害管线里读），这里只当一条快照。
    /// 敌人没有天赋 → 空字典，读不到就是 0。
    /// </summary>
    [Export] public Godot.Collections.Dictionary talent_rules { get; set; } = new();

    /// <summary>
    /// 异常状态（DoT）。按 04_战斗与伤害.md 的持续伤害规则：
    ///   同源同类叠层、刷新时长（键 = status_id + source_id，不同来源独立计算）；
    ///   每层伤害在**施加那一刻锁定**（快照制，增益掉光也照原值跳）。
    /// 结构：[{status_id, source_id, rule, stacks: [每层伤害], remaining}]
    /// </summary>
    [Export] public Godot.Collections.Array statuses { get; set; } = new();

    /// <summary>
    /// 战斗内 buff（08）：常驻／整场／限时都在这里，和异常状态共用一套回合末结算。
    /// 结构：[{buff_id, source_type, source_id, remaining, stacks}]
    ///   remaining > 0 剩余回合 / 0 常驻（随来源存在）/ -1 整场（战斗结束才清）
    /// </summary>
    [Export] public Godot.Collections.Array buffs { get; set; } = new();

    /// <summary>
    /// 配置表（buff 结算与状态行都要它）。类型写 `GodotObject` 是迁移期的口径
    /// （面板那边同一条）：GDScript 从 `_db` 直接赋值，用前在 C# 里强转 `TableDb`。
    /// </summary>
    [Export] public GodotObject _db { get; set; }

    /// <summary>
    /// 表里没给数值／规则、代码也没实现的特判 buff：**不静默**，界面按「暂缓规则」播报。
    /// 每条 {buff_id, name, reason}
    /// </summary>
    [Export] public Godot.Collections.Array unhandled_buffs { get; set; } = new();

    /// <summary>本次战斗里遇到过的「不认识的状态规则」（界面会把它们播成「暂缓规则」）。</summary>
    [Export] public string[] unhandled_rules { get; set; } = System.Array.Empty<string>();

    /// <summary>破绽窗口：架势被打空后进入，持续 1 回合。</summary>
    [Export] public int poise { get; set; } = 0;

    [Export] public int poise_max { get; set; } = 0;

    [Export] public bool broken { get; set; } = false;

    [Export] public int broken_rounds_left { get; set; } = 0;

    [Export] public double poise_regen_ratio { get; set; } = POISE_REGEN_DEFAULT;

    // ------------------------------------------------------------------ 内部（GDScript 不碰）
    //
    // 属性重算的输入：只有玩家角色有（敌人是 EnemyFactory 直接给的快照）。

    private AttributeCalculator _calculator;
    private BuffService _buff_service_cache;
    private Godot.Collections.Dictionary _base_attrs = new();
    private Godot.Collections.Dictionary _allocations = new();
    private Godot.Collections.Array _base_contributions = new();

    /// <summary>
    /// 敌人没有属性链：buff 的 `stat:` 修正直接叠在这份快照上（`attr:` 类如实记进 unhandled_buffs）。
    /// </summary>
    private Godot.Collections.Dictionary _stats_baseline = new();

    /// <summary>`_db` 强转后的配置表；没接表时返回 null（调用方据此走兜底路径）。</summary>
    private TableDb _table_db => _db as TableDb;

    // ------------------------------------------------------------------ 架势

    public int max_poise() => poise_max;

    public bool is_broken() => broken;

    /// <summary>扣架势，返回实际扣掉的量；扣到 0 就进入破绽。</summary>
    public int take_poise_damage(int amount)
    {
        int lost = System.Math.Min(System.Math.Max(amount, 0), poise);
        poise -= lost;
        if (poise <= 0 && !broken)
        {
            poise = 0;
            broken = true;
            broken_rounds_left = 1;
        }
        return lost;
    }

    /// <summary>回合末：破绽窗口递减（结束时架势重置），否则按比例回复。</summary>
    public void end_of_round_poise()
    {
        if (!is_alive())
        {
            return;
        }
        if (broken)
        {
            broken_rounds_left -= 1;
            if (broken_rounds_left <= 0)
            {
                broken = false;
                poise = poise_max;      // 设计：破绽结束后架势重置
            }
            return;
        }
        poise = System.Math.Min(poise_max, poise + (int)((double)poise_max * poise_regen_ratio));
    }

    /// <summary>背袭等效果：降低初始架势（比例来自 combat_const.backstab_poise_reduce）。</summary>
    public void reduce_poise_ratio(double ratio)
    {
        poise = System.Math.Max(
            1,
            (int)System.Math.Round(
                (double)poise_max * (1.0 - System.Math.Clamp(ratio, 0.0, 1.0)),
                System.MidpointRounding.AwayFromZero));
    }

    public int mastery_of(string skill_id) =>
        skill_mastery.ContainsKey(skill_id) ? skill_mastery[skill_id].AsInt32() : 0;

    // ------------------------------------------------------------------ 资源与取值

    /// <summary>取派生数值，取不到返回 0。GDScript 两种元数都在用，故按元数留两条重载。</summary>
    public double stat(string stat_id) => stat(stat_id, 0.0);

    /// <summary>取派生数值，取不到返回 fallback。</summary>
    public double stat(string stat_id, double fallback)
    {
        Godot.Collections.Dictionary table = stats;
        return table != null && table.ContainsKey(stat_id) ? table[stat_id].AsDouble() : fallback;
    }

    /// <summary>
    /// 异常／持续伤害抗性：**角色与敌人共用的派生数值** `stat_def.res_<status_id>`
    /// （`poison`／`burn`／`bleed`／`internal`，见 status_effect.csv 的四个 id）。
    ///
    /// 设计 0.12.0：抗性不再是敌人模板的特权列——玩家练了五毒心法，`skill_passive_stat` 的
    /// `stat:res_poison` 就会经贡献通道算进派生数值，这里读到的和敌人读的是同一条。
    /// `resistances`（敌人行的快照）只作为**派生数值缺失时的兜底**（用例里手搓的战斗单位会走它）。
    /// </summary>
    public double resistance_of(string status_id)
    {
        string stat_id = $"res_{status_id}";
        if (stats != null && stats.ContainsKey(stat_id))
        {
            return stats[stat_id].AsDouble();
        }
        return resistances != null && resistances.ContainsKey(status_id)
            ? resistances[status_id].AsDouble()
            : 0.0;
    }

    public int max_hp() => System.Math.Max(0, (int)stat("hp_max"));

    public int max_qi() => System.Math.Max(0, (int)stat("qi_max"));

    /// <summary>按当前数值把气血内力补满。</summary>
    public void refill()
    {
        hp = max_hp();
        qi = max_qi();
        poise = poise_max;
        broken = false;
        broken_rounds_left = 0;
    }

    public bool is_alive() => hp > 0;

    public double hp_ratio()
    {
        int total = max_hp();
        if (total <= 0)
        {
            return 0.0;
        }
        return System.Math.Clamp((double)hp / total, 0.0, 1.0);
    }

    /// <summary>扣血，返回实际扣掉的量。</summary>
    public int take_damage(int amount)
    {
        int lost = System.Math.Min(System.Math.Max(amount, 0), hp);
        hp -= lost;
        return lost;
    }

    public int heal(int amount)
    {
        int before = hp;
        hp = System.Math.Min(hp + System.Math.Max(amount, 0), max_hp());
        return hp - before;
    }

    /// <summary>支付内力，不足则返回 false 且不扣。</summary>
    public bool spend_qi(int cost)
    {
        if (cost <= 0)
        {
            return true;
        }
        if (qi < cost)
        {
            return false;
        }
        qi -= cost;
        return true;
    }

    /// <summary>回合末回复（气血回复 / 内力回复）。</summary>
    public void end_of_round_regen()
    {
        if (!is_alive())
        {
            return;
        }
        heal((int)System.Math.Floor(stat("hp_regen")));
        // 内伤：内力回复减半（status_effect.extra_rule = rule_internal_qi）
        double regen = stat("qi_regen") * qi_regen_factor();
        qi = System.Math.Min(qi + (int)System.Math.Floor(regen), max_qi());
    }

    public Godot.Collections.Dictionary summary() => new()
    {
        { "actor_id", actor_id },
        { "name", display_name },
        { "side", side },
        { "level", level },
        { "hp", hp },
        { "hp_max", max_hp() },
        { "qi", qi },
        { "poise", poise },
        { "poise_max", poise_max },
        { "broken", broken },
        { "base_accuracy", base_accuracy },
        { "speed", stat("speed") },
        { "atk_phys", stat("atk_phys") },
        { "atk_qi", stat("atk_qi") },
        { "def_phys", stat("def_phys") },
        { "def_qi", stat("def_qi") },
    };

    // ------------------------------------------------------------------ 工厂

    /// <summary>
    /// 用玩家角色表造一个战斗单位。
    ///
    /// **五个实参由调用点写全**：GDScript **不能**静态调用「重载过的」C# 静态方法——
    /// 解析期直接报 `Static function "from_character()" not found in base "CSharpScript"`
    /// （2026-10-11 实测；同一批还量到**实例**方法的重载反倒能按元数分派，见 `stat`／`add_buff`），
    /// 而默认参数对引擎调用又本来就不生效（施工单「二点四七」坑⑵）。两条夹在一起只剩这条路：
    /// 一条签名 ＋ 调用点显式写出「零加点／空贡献」。
    /// </summary>
    public static BattleActor from_character(
        GodotObject db,
        Resource char_row,
        int level,
        Godot.Collections.Dictionary allocations,
        Godot.Collections.Array contributions)
    {
        CharacterBaseRow template = char_row as CharacterBaseRow;
        var calculator = AttributeCalculator.create(db as TableDb);
        int effective_level = level > 0 ? level : System.Math.Max(1, template.start_level);
        Godot.Collections.Dictionary computed = calculator.compute(
            effective_level,
            template.initial_attrs(),
            allocations,
            contributions,
            true);
        var actor = new BattleActor();
        // 常驻 buff 的贡献在贡献列表里（见 buff 一节），这里存一份 db 与快照基线：
        // 让「直接造一个 BattleActor」的调用方（用例／工具）也能加临时 buff
        actor._db = db;
        actor._stats_baseline = computed.Duplicate(true);
        actor.actor_id = template.char_id;
        actor.source_id = template.char_id;
        actor.display_name = template.name_cn;
        actor.side = SIDE_ALLY;
        actor.level = effective_level;
        actor.stats = computed;
        foreach (string stat_id in EQUIPMENT_ONLY_STATS)
        {
            if (!actor.stats.ContainsKey(stat_id))
            {
                actor.stats[stat_id] = 0.0;
            }
        }
        actor.skills = template.skill_ids();
        actor.tags = new Godot.Collections.Dictionary
        {
            { "role_tag", template.role_tag },
            { "faction", "party" },
        };
        actor.base_accuracy = 1.0;
        actor.poise_max = (int)(PLAYER_POISE_BASE + PLAYER_POISE_PER_LEVEL * effective_level);
        actor.refill();
        return actor;
    }

    /// <summary>用算好的数值造一个敌人（缩放由 EnemyFactory 负责）。</summary>
    public static BattleActor from_enemy(
        EnemyBaseRow enemy_row,
        Godot.Collections.Dictionary stats,
        string[] skills,
        int level,
        GodotObject db)
    {
        var actor = new BattleActor();
        // 敌人也要能接 buff（08：招式的 on_cast／命中触发对双方一视同仁）。
        // 以前这里没有 db，`add_buff` 会直接返回「这个单位没有接配置表」——那是一条**静默的**失败路径：
        // 设计一旦给某个敌人招式配 on_cast，效果会凭空消失（见框架说明决策 220）。
        if (db != null)
        {
            actor._db = db;
        }
        actor.actor_id = enemy_row.enemy_id;
        actor.source_id = enemy_row.enemy_id;
        actor.display_name = enemy_row.name_cn;
        actor.side = SIDE_ENEMY;
        actor.level = level > 0 ? level : enemy_row.level;
        actor.stats = stats;
        actor._stats_baseline = stats.Duplicate(true);
        actor.skills = skills;
        actor.resistances = enemy_row.resistances();
        // 命中基准敌我统一成 1.0（设计 10 §七第四条，0.28.0 答 Q57）。
        //
        // 以前敌人是 0.0、把 `enemy_base.hit_rate` 当**绝对**命中率用；七维改造之后
        // `hit_rate` 是 `attr_to_stat` 用 diminishing 曲线从**敏**算出来的加成
        // （cap 0.95／param 80）——敏 24 只给 0.22，敌人若还留着 0 基准就成了「十刀九空」，
        // 而且这条在数值表里完全看不出来。真正决定打不打得中的是**对方的闪避**
        // （10 §五：「想让敌人血厚防薄，用敏表达」）。
        actor.base_accuracy = 1.0;
        actor.poise_max = System.Math.Max(1, enemy_row.poise);
        actor.poise_regen_ratio =
            enemy_row.ai_template == "ai_boss" ? POISE_REGEN_BOSS : POISE_REGEN_DEFAULT;
        actor.tags = new Godot.Collections.Dictionary
        {
            { "faction", enemy_row.faction },
            { "threat_tag", enemy_row.threat_tag },
            { "ai_template", enemy_row.ai_template },
        };
        actor.drop_group = enemy_row.drop_group;
        actor.refill();
        return actor;
    }
}
