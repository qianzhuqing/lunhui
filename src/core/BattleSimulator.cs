using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 无界面战斗模拟器：把「出手顺序 → 选招 → 结算 → 回合末回复 → 胜负」跑完。
///
/// 它的存在是为了让伤害管线、属性、掉落能在 headless 下端到端验证。
///
/// 对位原 `src/core/battle_simulator.gd`（1524 行），按职责拆成五份 partial：
/// 本文件是主壳（常量／字段／装配／回合管线／进出场顺序）；
/// 选招与目标选择在 `BattleSimulatorSelect.cs`、六指令与逃跑拆招在 `BattleSimulatorActions.cs`、
/// 结算与回合末状态在 `BattleSimulatorSettle.cs`、
/// 冷却与战斗过程记录在 `BattleSimulatorProgress.cs`。
///
/// **跨语言约定（迁移期）**：GDScript 的 `preload("…/battle_simulator.gd")` 一律换成
/// `preload("…/BattleSimulator.cs")`，`BattleSimulatorScript.new(db, rng)` 换成 `.create(db, rng)`
/// （C# 的带参构造从 GDScript `.new()` 走不通，见施工单「二点五〇」）。
/// 读**常量**要走小写取值口（GDScript 读不到 C# 的 `const`）；
/// 带默认值的方法**一律按元数补重载**（默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
/// </summary>
public partial class BattleSimulator : RefCounted
{
    // ------------------------------------------------------------------ 常量

    public const int MAX_ROUNDS = 50;
    public const string WINNER_ALLY = "ally";
    public const string WINNER_ENEMY = "enemy";
    public const string WINNER_DRAW = "draw";

    /// <summary>主动撤退：不是胜负，是「离开战斗」——不结算经验铜钱掉落，明雷也不清。</summary>
    public const string WINNER_FLEE = "flee";

    /// <summary>
    /// 逃跑判定（设计 02 只写了「被追上第一回合不能逃」，成功率公式没给，以下是开发侧定的）：
    /// 以双方「最高身法」对比，同速 50%，快的一边接近上限、慢的一边接近下限，
    /// 用 (mine-theirs)/(mine+theirs) 自归一化，不需要额外的量纲常数。数值待设计确认。
    /// </summary>
    public const double FLEE_BASE_CHANCE = 0.5;

    public const double FLEE_MIN_CHANCE = 0.25;
    public const double FLEE_MAX_CHANCE = 0.95;

    /// <summary>破绽窗口里的增伤（设计：架势打空后的窗口「受绝招伤害大幅提高」）。</summary>
    public const double POISE_BREAK_DAMAGE_BONUS = 0.5;

    /*
     * 自动战斗策略（设计 04 只给了三个名字：保守／均衡／全力，语义是开发侧定的，等设计确认）：
     *   balanced     均衡（默认）：期望总伤害 = 倍率 × 段数 × 能打到的目标数 —— 多人时偏爱群攻
     *   all_out      全力：只挑单发倍率最高的招式，不管几段、不管能打几个 —— 「把伤害压在一下上」
     *   conservative 保守：最省内力（同消耗时挑倍率最低的）—— 能用起手式就用起手式
     * 只影响**我方自动战斗**；敌人 AI 永远走均衡，免得换策略就改了敌人的行为。
     */
    public const string STRATEGY_BALANCED = "balanced";
    public const string STRATEGY_ALL_OUT = "all_out";
    public const string STRATEGY_CONSERVATIVE = "conservative";
    public static readonly string[] STRATEGY_ORDER =
        { STRATEGY_CONSERVATIVE, STRATEGY_BALANCED, STRATEGY_ALL_OUT };
    public static readonly Godot.Collections.Dictionary STRATEGY_LABELS = new()
    {
        { STRATEGY_CONSERVATIVE, "保守" },
        { STRATEGY_BALANCED, "均衡" },
        { STRATEGY_ALL_OUT, "全力" },
    };

    /*
     * 拆招（设计 04 核心循环）：敌方出招前有一回合预兆，玩家花一次行动拆招，
     * 读到的那一招**伤害减半**，拆招者**反涨自身架势**。
     * 减半是设计原文写死的；反涨的比例设计没给数值，先定「自身架势上限的 15%」，
     * 和 poise_regen 一样写成代码常量并记进交接表，等设计补表列。
     */
    public const double PARRY_DAMAGE_MULT = 0.5;
    public const double PARRY_POISE_GAIN = 0.15;

    /*
     * 普通攻击：不耗内力、不看武器与内力门槛（谁都能打一下），固定倍率与破架势。
     * 这两条数值在表里**没有落点**（没有任何「普通攻击」行，`combat_const` 也没有），
     * 用开发侧常数并在交接表登记请设计补列——别让它变成第二份没人管的数值。
     */
    public const string BASIC_ATTACK_ID = "__basic_attack__";

    /// <summary>中毒的 `status_effect` 行 id：满级签名「引毒」要给普攻补这一条（普攻不落表、没有 status_id）。</summary>
    public const string POISON_STATUS_ID = "poison";
    public const double BASIC_ATTACK_POWER = 1.0;
    public const int BASIC_ATTACK_POISE = 8;

    public const string DEFEND_BUFF_ID = "buff_guard";
    public const double DEFEND_POISE_GAIN = 0.15;

    // ------------------------------------------------------------------ 常量取值口
    //
    // GDScript 读不到 C# 的 `const`（`preload("…cs").X` 取不到静态常量），
    // 所以每个常量配一条同名小写静态取值口。`PARRY_DAMAGE_MULT` 因为和实例方法
    // `parry_damage_mult()` 撞名，取值口另起一个不冲突的名字（GDScript 静态方法不能重载）。

    public static int max_rounds() => MAX_ROUNDS;
    public static string winner_ally() => WINNER_ALLY;
    public static string winner_enemy() => WINNER_ENEMY;
    public static string winner_draw() => WINNER_DRAW;
    public static string winner_flee() => WINNER_FLEE;
    public static double flee_base_chance() => FLEE_BASE_CHANCE;
    public static double flee_min_chance() => FLEE_MIN_CHANCE;
    public static double flee_max_chance() => FLEE_MAX_CHANCE;
    public static double poise_break_damage_bonus() => POISE_BREAK_DAMAGE_BONUS;
    public static double parry_damage_mult_default() => PARRY_DAMAGE_MULT;
    public static double parry_poise_gain() => PARRY_POISE_GAIN;
    public static string basic_attack_id() => BASIC_ATTACK_ID;
    public static double basic_attack_power() => BASIC_ATTACK_POWER;
    public static int basic_attack_poise() => BASIC_ATTACK_POISE;
    public static string defend_buff_id() => DEFEND_BUFF_ID;
    public static double defend_poise_gain() => DEFEND_POISE_GAIN;
    public static string strategy_balanced() => STRATEGY_BALANCED;
    public static string strategy_all_out() => STRATEGY_ALL_OUT;
    public static string strategy_conservative() => STRATEGY_CONSERVATIVE;
    public static string[] strategy_order() => STRATEGY_ORDER;

    // ------------------------------------------------------------------ 状态字段

    private TableDb _db;
    private GodotObject _rng;
    private DamageResolver _resolver;
    /// <summary>BuffService 只留一份（它内部有按来源/buff 的缓存；`_is_support_skill()` 会被循环调用）。</summary>
    private BuffService _buff_service_cache = null;

    /// <summary>战斗状态（分步执行用）：setup → step_round × N → result。</summary>
    private Godot.Collections.Array _allies = new();
    private Godot.Collections.Array _enemies = new();
    /// <summary>`setup` 传进来的参数袋；用例会直接改它（`sim._options["max_rounds"] = 1`），故走 `[Export]`。</summary>
    [Export] public Godot.Collections.Dictionary _options { get; set; } = new();
    private Godot.Collections.Array _log = new();
    private int _rounds = 0;
    private Godot.Collections.Dictionary _damage_dealt = new();

    /// <summary>结构化事件（给表现层用：跳字、受击反馈）。UI 用 `take_events()` 取走，不走日志解析。</summary>
    private Godot.Collections.Array _events = new();
    private Encounter _encounter = null;

    // 当前回合的行动顺序与游标（逐行动推进用）
    private Godot.Collections.Array _order = new();
    private int _order_index = 0;
    /// <summary>本回合每个敌人的预兆招式（enemy.actor_id → skill_id）。</summary>
    private Godot.Collections.Dictionary _intents = new();
    /// <summary>本回合已经被拆招的敌人（enemy.actor_id → true）。</summary>
    private Godot.Collections.Dictionary _parried = new();
    /// <summary>已经成功撤退（战斗以「离开」收场）。</summary>
    private bool _escaped = false;
    /// <summary>我方自动战斗策略（见 STRATEGY_* 常量；敌人不受影响）。</summary>
    private string _strategy = STRATEGY_BALANCED;

    /// <summary>
    /// 谁在战斗里施放过哪些招式（actor_id → {skill_id: 次数}）。
    /// 打完用来结算熟练度（growth_const.mastery_combat_gain 每次施放 +1），
    /// 一次行动只记一次——多目标招式会在 _act_once 里循环，不能按次记。
    /// </summary>
    private Godot.Collections.Dictionary _skill_uses = new();

    /// <summary>
    /// actor_id → 死因（"normal" 直伤 / "poison" 中毒 / "burn" 灼伤 …）。
    /// 隐藏内容要按击杀方式判定（`hidden_trigger.required_condition = kill_with=poison`）。
    /// </summary>
    private Godot.Collections.Dictionary _kill_styles = new();
    /// <summary>actor_id → {skill_id: 可以再用的回合数}（skill_active.cooldown：放完要等 N 个回合）。</summary>
    private Godot.Collections.Dictionary _cooldowns = new();
    /// <summary>主动防御的「下回合先手」标记（actor_id → true），见 `defend`／`_apply_first_strike`。</summary>
    private Godot.Collections.Dictionary _pending_first_next_round = new();

    /// <summary>现造的「普通攻击」行（不落表，缓存一份）。</summary>
    private SkillActiveRow _basic_attack_row = null;

    // ------------------------------------------------------------------ 装配

    public static BattleSimulator create(TableDb db, GodotObject rng) => new(db, rng);

    public BattleSimulator(TableDb db, GodotObject rng)
    {
        _db = db;
        _rng = rng;
        _resolver = DamageResolver.create(db, rng);
    }

    /// <summary>摆好一场战斗。options: {"max_rounds": int, "modifiers": Dictionary, "encounter": Encounter}</summary>
    public void setup(Godot.Collections.Array allies, Godot.Collections.Array enemies) =>
        setup(allies, enemies, new Godot.Collections.Dictionary());

    /// <inheritdoc cref="setup(Godot.Collections.Array, Godot.Collections.Array)"/>
    public void setup(
        Godot.Collections.Array allies, Godot.Collections.Array enemies, Godot.Collections.Dictionary options)
    {
        _allies = allies;
        _enemies = enemies;
        _options = options;
        _log = new Godot.Collections.Array();
        _rounds = 0;
        _damage_dealt = new Godot.Collections.Dictionary();
        _events = new Godot.Collections.Array();
        _encounter = _options.ContainsKey("encounter")
            ? _options["encounter"].AsGodotObject() as Encounter
            : null;
        _order = new Godot.Collections.Array();
        _order_index = 0;
        _intents = new Godot.Collections.Dictionary();
        _parried = new Godot.Collections.Dictionary();
        _escaped = false;
        _strategy = _s(_options, "strategy", STRATEGY_BALANCED);
        if (System.Array.IndexOf(STRATEGY_ORDER, _strategy) < 0)
        {
            _strategy = STRATEGY_BALANCED;
        }
        _apply_encounter_opening();
        _apply_pending_grants();
    }

    /// <summary>
    /// 开打时把「进场 buff」逐条发下去（装备的 on_equip／on_battle_start、战斗外增益）。
    /// 常驻 buff 不走这里——它们在构造角色时就进了贡献列表（见 BattleActor 的 buff 一节）。
    /// </summary>
    private void _apply_pending_grants()
    {
        foreach (Variant actor_variant in _both_sides())
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            Godot.Collections.Array grants = actor.pending_grants;
            if (grants.Count == 0)
            {
                continue;
            }
            foreach (Variant grant_variant in grants)
            {
                Godot.Collections.Dictionary grant = grant_variant.AsGodotDictionary();
                Godot.Collections.Dictionary result = _add_buff(
                    actor, _s(grant, "buff_id"), _s(grant, "source_type"), _s(grant, "source_id"),
                    _i(grant, "stacks", 1));
                if (result["applied"].AsBool())
                {
                    _log.Add(
                        $"{actor.display_name} 带着 {result["name"].AsString()} 入场（{_duration_text(result)}）");
                }
            }
            actor.pending_grants = new Godot.Collections.Array();
        }
    }

    private string _duration_text(Godot.Collections.Dictionary buff_result)
    {
        int remaining = _i(buff_result, "remaining", 0);
        if (remaining > 0)
        {
            return $"{remaining} 回合";
        }
        return remaining < 0 ? "整场" : "常驻";
    }

    /// <summary>开局修正：背袭时敌方架势初始值降低（比例来自 combat_const）。</summary>
    private void _apply_encounter_opening()
    {
        if (_encounter == null)
        {
            return;
        }
        double ratio = _encounter.enemy_poise_reduce;
        if (ratio <= 0.0)
        {
            return;
        }
        foreach (Variant enemy_variant in _enemies)
        {
            ((BattleActor)enemy_variant.AsGodotObject()).reduce_poise_ratio(ratio);
        }
    }

    /// <summary>`combat_const` 查表（缺行退回代码兜底值并报一次错，与 `DamageResolver.constant()` 同一口径）。</summary>
    private double _combat_const(string const_id, double fallback) => _resolver.constant(const_id, fallback);

    // ------------------------------------------------------------------ 回合推进

    /// <summary>开一个新回合（算好出手顺序，游标归零），之后可以逐行动推进。</summary>
    public void begin_round()
    {
        if (finished())
        {
            return;
        }
        int max_rounds = _i(_options, "max_rounds", MAX_ROUNDS);
        if (_rounds >= max_rounds)
        {
            return;
        }
        _rounds += 1;
        _log.Add($"—— 第 {_rounds} 回合 ——");
        // 满级签名「百草」：**每回合开始清掉自身 1 层减益**（设计 08）。放在这里而不是"轮到自己时"——
        // 它是"回合开始"的规则，玩家在预兆那一拍就该看到层数掉了。
        foreach (Variant actor_variant in _both_sides())
        {
            var cleanser = (BattleActor)actor_variant.AsGodotObject();
            if (cleanser.is_alive() && cleanser.mastery_marker("cleanse_one") > 0.0)
            {
                int removed = cleanser.cleanse_one_debuff();
                if (removed > 0)
                {
                    _log.Add($"R{_rounds} {cleanser.display_name} 的满级签名清掉 1 层减益");
                }
            }
        }

        _order = turn_order(_allies, _enemies);
        if (_rounds == 1 && _encounter != null && _encounter.first_side >= 0)
        {
            _order = _prioritize(_order, _encounter.first_side);
        }
        // 天赋「先发制人」（设计 12 §六：首回合必定先手）：第 1 回合把带着这条规则的人提到最前
        _order = _apply_talent_first_strike(_order, _rounds);
        // 主动防御的「下回合先手」：把上回合用过防御姿态的人提到本回合最前
        _order = _apply_first_strike(_order);
        _order_index = 0;
        _refresh_intents();
    }

    /// <summary>把手上有「先手」标记的单位提到出手序最前（互相之间保持原来的身法顺序），用完即清。</summary>
    private Godot.Collections.Array _apply_first_strike(Godot.Collections.Array order)
    {
        if (_pending_first_next_round.Count == 0)
        {
            return order;
        }
        var head = new Godot.Collections.Array();
        var rest = new Godot.Collections.Array();
        foreach (Variant actor_variant in order)
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (_pending_first_next_round.ContainsKey(actor.actor_id))
            {
                head.Add(actor);
            }
            else
            {
                rest.Add(actor);
            }
        }
        _pending_first_next_round.Clear();
        foreach (Variant actor_variant in rest)
        {
            head.Add(actor_variant);
        }
        return head;
    }

    /// <summary>
    /// 天赋「先发制人」（设计 12 §六：**首回合必定先手**）：
    /// 只在第 1 回合生效，把带着 `rule:first_round_priority` 的单位提到出手序最前
    /// （互相之间仍是原来的身法顺序）。与上面那条分开：那条是「主动防御的下回合先手」，
    /// 跨回合、且要清标记；这条是**开局一次**的规则，读战斗单位上的天赋快照。
    /// </summary>
    public Godot.Collections.Array _apply_talent_first_strike(Godot.Collections.Array order, int round_no)
    {
        if (round_no != 1)
        {
            return order;
        }
        var head = new Godot.Collections.Array();
        var rest = new Godot.Collections.Array();
        foreach (Variant actor_variant in order)
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            Godot.Collections.Dictionary rules =
                actor.talent_rules != null ? actor.talent_rules : new Godot.Collections.Dictionary();
            if (_f(rules, "first_round_priority", 0.0) > 0.0)
            {
                head.Add(actor);
            }
            else
            {
                rest.Add(actor);
            }
        }
        if (head.Count == 0)
        {
            return order;
        }
        foreach (Variant actor_variant in rest)
        {
            head.Add(actor_variant);
        }
        return head;
    }

    /// <summary>当前该行动的单位；本回合跑完或该单位无法行动时返回 null。</summary>
    public GodotObject current_actor()
    {
        while (_order_index < _order.Count)
        {
            var actor = (BattleActor)_order[_order_index].AsGodotObject();
            if (actor.is_alive() && _has_living(_allies) && _has_living(_enemies))
            {
                return actor;
            }
            _order_index += 1;
        }
        return null;
    }

    public bool in_round() => current_actor() != null;

    /// <summary>取走本批事件（取完即清空）。</summary>
    public Godot.Collections.Array take_events()
    {
        Godot.Collections.Array out_arr = _events;
        _events = new Godot.Collections.Array();
        return out_arr;
    }

    /// <summary>跑完一整场（setup + 逐回合推进）。</summary>
    public Godot.Collections.Dictionary simulate(Godot.Collections.Array allies, Godot.Collections.Array enemies) =>
        simulate(allies, enemies, new Godot.Collections.Dictionary());

    /// <inheritdoc cref="simulate(Godot.Collections.Array, Godot.Collections.Array)"/>
    public Godot.Collections.Dictionary simulate(
        Godot.Collections.Array allies, Godot.Collections.Array enemies, Godot.Collections.Dictionary options)
    {
        setup(allies, enemies, options);
        int max_rounds = _i(_options, "max_rounds", MAX_ROUNDS);
        while (!finished() && _rounds < max_rounds)
        {
            step_round();
        }
        return result();
    }

    /// <summary>打完一回合，返回这一回合新增的日志行（战斗界面按回合播放）。</summary>
    public Godot.Collections.Array step_round()
    {
        int from_index = _log.Count;
        if (finished())
        {
            return new Godot.Collections.Array();
        }
        int max_rounds = _i(_options, "max_rounds", MAX_ROUNDS);
        if (_rounds >= max_rounds)
        {
            return new Godot.Collections.Array();
        }
        _rounds += 1;
        _log.Add($"—— 第 {_rounds} 回合 ——");
        _refresh_intents();
        Godot.Collections.Array order = turn_order(_allies, _enemies);
        if (_rounds == 1 && _encounter != null && _encounter.first_side >= 0)
        {
            order = _prioritize(order, _encounter.first_side);
        }
        order = _apply_talent_first_strike(order, _rounds);
        foreach (Variant actor_variant in order)
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (!actor.is_alive())
            {
                continue;
            }
            if (!_has_living(_allies) || !_has_living(_enemies))
            {
                break;
            }
            Godot.Collections.Array targets = _pick_targets(actor, _allies, _enemies);
            if (targets.Count == 0)
            {
                continue;
            }
            Resource skill = _skill_for(actor);
            if (skill == null)
            {
                _log.Add($"R{_rounds} {actor.display_name} 无可用招式，跳过");
                continue;
            }
            _record_use(actor, skill);
            _start_cooldown(actor, skill);
            foreach (Variant target_variant in targets)
            {
                // 和界面那条路（begin_round/act）共用同一个「一次行动」：
                // 架势、异常状态、斩杀规则都只写一份，免得两条路行为不一致
                _act_once(actor, skill, (BattleActor)target_variant.AsGodotObject());
            }
            _spend_skill_qi(actor, skill);
            _apply_cast_grants(actor, skill);
        }
        _finish_round();
        return _slice(_log, from_index);
    }

    public bool finished()
    {
        if (_escaped)
        {
            return true;
        }
        int max_rounds = _i(_options, "max_rounds", MAX_ROUNDS);
        if (!_has_living(_allies) || !_has_living(_enemies))
        {
            return true;
        }
        return _rounds >= max_rounds;
    }

    public string winner()
    {
        if (_escaped)
        {
            return WINNER_FLEE;
        }
        if (_has_living(_allies) && !_has_living(_enemies))
        {
            return WINNER_ALLY;
        }
        if (_has_living(_enemies) && !_has_living(_allies))
        {
            return WINNER_ENEMY;
        }
        return WINNER_DRAW;
    }

    public int rounds_played() => _rounds;

    public Godot.Collections.Dictionary result()
    {
        var out_dict = new Godot.Collections.Dictionary
        {
            { "winner", winner() },
            { "escaped", _escaped },
            { "rounds", _rounds },
            { "log", _log },
            { "rewards", _rewards(_enemies) },
            { "damage_dealt", _damage_dealt },
            { "allies", _allies },
            { "enemies", _enemies },
        };
        if (_encounter != null)
        {
            out_dict["encounter"] = _encounter.summary();
            var rules_arr = new Godot.Collections.Array();
            foreach (string rule in _encounter.pending_rules)
            {
                rules_arr.Add(rule);
            }
            out_dict["pending_rules"] = rules_arr;
        }
        // 「表里配了、代码还不认」的状态规则也要报出去（界面会播成「暂缓规则」）：
        // 静默忽略会让玩家（和下一个人）以为它生效了。见 battle_actor.KNOWN_STATUS_RULES。
        var pending = new Godot.Collections.Array();
        if (out_dict.ContainsKey("pending_rules"))
        {
            foreach (Variant rule_variant in out_dict["pending_rules"].AsGodotArray())
            {
                pending.Add(rule_variant);
            }
        }
        foreach (Variant actor_variant in _both_sides())
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            foreach (string rule in actor.unhandled_rules)
            {
                // 说中文状态名，别把 `rule_bleed_move` 这种表内 id 甩给玩家（文案纪律）
                string note = $"{_status_name_for_rule(actor, rule)} 的特殊规则还没接（设计未定规则，暂不触发）";
                if (!pending.Contains(note))
                {
                    pending.Add(note);
                }
            }
            // 同理：`effect_kind=special` 的 buff（例：淬毒）表里/代码里都还没有效果实现，
            // 玩家花容量装上却只拿到层数——必须如实播出来，不能静默。
            foreach (Variant entry_variant in actor.unhandled_buffs)
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                string buff_note =
                    $"{actor.display_name} 的「{_s(entry, "name")}」还没实现（{_s(entry, "reason")}）";
                if (!pending.Contains(buff_note))
                {
                    pending.Add(buff_note);
                }
            }
        }
        if (pending.Count > 0)
        {
            out_dict["pending_rules"] = pending;
        }
        return out_dict;
    }

    /// <summary>状态规则 id → 玩家看得懂的名字（查 `status_effect.name_cn`）；查不到才回 id。</summary>
    private string _status_name_for_rule(BattleActor actor, string rule)
    {
        foreach (Variant entry_variant in actor.statuses)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            if (_s(entry, "rule") != rule)
            {
                continue;
            }
            Resource row = _db.get_row("status_effect", _s(entry, "status_id"));
            if (row != null)
            {
                return row.Get("name_cn").AsString();
            }
        }
        return rule;
    }

    /// <summary>首回合的先手方排到前面（同侧内部保持原有顺序）。</summary>
    private Godot.Collections.Array _prioritize(Godot.Collections.Array order, int side)
    {
        var first = new Godot.Collections.Array();
        var rest = new Godot.Collections.Array();
        foreach (Variant actor_variant in order)
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (actor.side == side)
            {
                first.Add(actor);
            }
            else
            {
                rest.Add(actor);
            }
        }
        foreach (Variant actor_variant in rest)
        {
            first.Add(actor_variant);
        }
        return first;
    }

    /// <summary>出手顺序：身法高的先手，同速按传入次序稳定排列。</summary>
    public Godot.Collections.Array turn_order(Godot.Collections.Array allies, Godot.Collections.Array enemies)
    {
        var living = new Godot.Collections.Array();
        foreach (Variant actor_variant in _both(allies, enemies))
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (actor.is_alive())
            {
                living.Add(actor);
            }
        }
        var indexed = new System.Collections.Generic.List<(int index, BattleActor actor)>();
        for (int i = 0; i < living.Count; i++)
        {
            indexed.Add((i, (BattleActor)living[i].AsGodotObject()));
        }
        indexed.Sort((a, b) =>
        {
            double speed_a = a.actor.stat("speed");
            double speed_b = b.actor.stat("speed");
            if (Mathf.IsEqualApprox(speed_a, speed_b))
            {
                return a.index.CompareTo(b.index);
            }
            return speed_b.CompareTo(speed_a);
        });
        var out_arr = new Godot.Collections.Array();
        foreach ((int index, BattleActor actor) in indexed)
        {
            out_arr.Add(actor);
        }
        return out_arr;
    }

    // ------------------------------------------------------------------ BuffService 共用一份

    /// <summary>
    /// 增益招式（「醉里乾坤·醉步」就是这一档）：没有伤害类型与倍率，靠 `on_cast` 给自己发 buff。
    /// **能不能用由表决定**——判定收敛到 `BuffService.has_cast_grant()`，不在界面或模拟器里另写一套。
    /// </summary>
    private bool _is_support_skill(string skill_id)
    {
        if (_db == null)
        {
            return false;
        }
        return _buff_service().has_cast_grant(skill_id);
    }

    /// <summary>模拟器自己留一份 BuffService（内部按来源分桶 + 按 buff 缓存，别在循环里反复 new）。</summary>
    private BuffService _buff_service()
    {
        if (_buff_service_cache == null)
        {
            _buff_service_cache = BuffService.create(_db);
        }
        return _buff_service_cache;
    }

    // ------------------------------------------------------------------ 胜负与奖励

    private Godot.Collections.Dictionary _rewards(Godot.Collections.Array enemies)
    {
        int exp = 0;
        int money = 0;
        foreach (Variant enemy_variant in enemies)
        {
            var enemy = (BattleActor)enemy_variant.AsGodotObject();
            if (enemy.is_alive())
            {
                // 没打死的敌人不算奖励
                continue;
            }
            exp += enemy.reward_exp;
            money += enemy.reward_money;
        }
        return new Godot.Collections.Dictionary { { "exp", exp }, { "money", money } };
    }

    private bool _has_living(Godot.Collections.Array actors)
    {
        foreach (Variant actor_variant in actors)
        {
            if (((BattleActor)actor_variant.AsGodotObject()).is_alive())
            {
                return true;
            }
        }
        return false;
    }

}
