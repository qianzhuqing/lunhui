using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `BattleSimulator` 的「选招与目标」这一块（partial 之一）：
/// 可用招式／体检表、自动选招与打分、武器与伤害类型门槛、策略三件套、目标选择。
/// </summary>
public partial class BattleSimulator
{
    /// <summary>当前单位能用的招式（内力够、武器匹配、伤害类型支持）。</summary>
    public Godot.Collections.Array available_skills(BattleActor actor)
    {
        var out_arr = new Godot.Collections.Array();
        foreach (string skill_id in actor.skills)
        {
            var active = _db.get_row("skill_active", skill_id) as SkillActiveRow;
            if (active == null)
            {
                continue;
            }
            // 增益招式（没有伤害、但表里配了 on_cast 发放）也算「能用」——施放一次给自己上 buff
            if (!active.is_attack() && !_is_support_skill(skill_id))
            {
                continue;
            }
            // 冷却中不算「可用」（界面上会灰着写清楚还差几回合）
            if (cooldown_left(actor.actor_id, skill_id) > 0)
            {
                continue;
            }
            if (actor.max_qi() > 0 && active.qi_cost > actor.qi)
            {
                continue;
            }
            // 增益招式没有伤害类型，跳过「伤害类型支持」这一条（它本来就不走伤害管线）
            if (active.is_attack() && !_is_supported_damage(active.damage_type))
            {
                continue;
            }
            var base_row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
            if (base_row != null && !_weapon_allows(actor, base_row))
            {
                continue;
            }
            out_arr.Add(active);
        }
        return out_arr;
    }

    /// <summary>
    /// 这个单位身上「这一招能不能放、为什么不能」的完整体检表（给界面用）：
    /// [{skill_id, name, qi_cost, power_ratio, hit_count, target_type, support, ok, reason, cooldown_left}]
    /// </summary>
    public Godot.Collections.Array skill_options(BattleActor actor)
    {
        var out_arr = new Godot.Collections.Array();
        if (actor == null)
        {
            return out_arr;
        }
        foreach (string skill_id in actor.skills)
        {
            var active = _db.get_row("skill_active", skill_id) as SkillActiveRow;
            if (active == null)
            {
                continue;
            }
            if (!active.is_attack() && !_is_support_skill(skill_id))
            {
                continue;
            }
            bool is_support = !active.is_attack();
            var base_row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
            string reason = "";
            int left = cooldown_left(actor.actor_id, skill_id);
            if (left > 0)
            {
                reason = $"冷却中（第 {cooldown_ready_round(actor.actor_id, skill_id)} 回合起可用）";
            }
            else if (actor.max_qi() > 0 && active.qi_cost > actor.qi)
            {
                reason = $"内力不足（要 {active.qi_cost}）";
            }
            else if (!is_support && !_is_supported_damage(active.damage_type))
            {
                reason = _unsupported_damage_reason(active.damage_type);
            }
            else if (base_row != null && !_weapon_allows(actor, base_row))
            {
                reason = "武器不符";
            }
            out_arr.Add(new Godot.Collections.Dictionary
            {
                { "skill_id", skill_id },
                { "name", base_row != null ? base_row.name_cn : skill_id },
                { "qi_cost", active.qi_cost },
                { "power_ratio", active.power_ratio },
                { "hit_count", _effective_hits(actor, active) },
                { "target_type", active.target_type },
                { "support", is_support },
                { "ok", reason.Length == 0 },
                { "reason", reason },
                { "cooldown_left", left },
            });
        }
        return out_arr;
    }

    /// <summary>
    /// 选招：取倍率最高的可用招式。
    ///
    /// 0.6.0 起招式数值在 skill_active（skill_base 只放身份与门槛），所以这里返回的是 skill_active 行。
    /// 过滤条件：能支付内力、武器类型匹配、伤害类型被管线支持。
    /// </summary>
    public Resource pick_skill(BattleActor actor)
    {
        SkillActiveRow best = null;
        // 从 -INF 起：保守策略的分数是负的（省内力），用 -1 当初值会一个都挑不出来
        double best_power = double.NegativeInfinity;
        foreach (string skill_id in actor.skills)
        {
            var active = _db.get_row("skill_active", skill_id) as SkillActiveRow;
            if (!_is_usable(actor, active))
            {
                continue;
            }
            // 只看倍率的话，横扫（1.3 倍 × 3 段 × 全体）永远输给斩风（2.1 倍单体），那张表就白配了。
            double power = _skill_score(actor, active);
            if (power > best_power)
            {
                best = active;
                best_power = power;
            }
        }
        return best;
    }

    /// <summary>这一招现在能不能选（冷却、内力、伤害类型、武器四条，与 available_skills 同一口径）。</summary>
    public bool _is_usable(BattleActor actor, SkillActiveRow active)
    {
        if (active == null || !active.is_attack())
        {
            return false;
        }
        if (cooldown_left(actor.actor_id, active.skill_id) > 0)
        {
            return false;
        }
        // 敌人没有内力池（enemy_base 没有 qi 列，EnemyFactory 给 qi_max=0），
        // 所以内力消耗只对**有内力池的单位**生效——这条缺口记在交接表的 enemy_data 备注里。
        if (actor.max_qi() > 0 && active.qi_cost > actor.qi)
        {
            return false;
        }
        if (!_is_supported_damage(active.damage_type))
        {
            return false;
        }
        var base_row = _db.get_row("skill_base", active.skill_id) as SkillBaseRow;
        if (base_row != null && !_weapon_allows(actor, base_row))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// 本回合该用什么招：敌人**照预兆里定好的那一招**打（预兆了却临时变招，玩家就白拆了）；
    /// 预兆的那招这一回合变得不可用（例如被拆到没内力）才退回重新选。
    /// </summary>
    private Resource _skill_for(BattleActor actor)
    {
        if (actor.side == BattleActor.side_enemy())
        {
            string planned = intent_of(actor.actor_id);
            if (planned.Length > 0)
            {
                var active = _db.get_row("skill_active", planned) as SkillActiveRow;
                if (_is_usable(actor, active))
                {
                    return active;
                }
            }
        }
        return pick_skill(actor);
    }

    /// <summary>
    /// 一招实际打几段：`skill_active.hit_count` ＋ 角色派生值 `extra_hit`（上限由 stat_def 夹）。
    /// **只作用于招式**——普通攻击不吃（设计 08 参数口径 Q84：否则套装白送普攻流一段）。
    /// </summary>
    public int _effective_hits(BattleActor actor, Resource skill)
    {
        return effective_hits_for(
            skill.Get("skill_id").AsString(), skill.Get("hit_count").AsInt32(), actor.stat("extra_hit"));
    }

    /// <summary>
    /// 上面那条的**唯一算法**：`hit_count ＋ extra_hit`，至少一段；普攻不吃 `extra_hit`。
    ///
    /// 单拎出来是因为**角色面板也要写这个数**（UI 清单 §二-6「招式段数」写的是这个角色手里的段数，
    /// 不是表里的裸值——玄微剑「3 招档 +1」那种加成得看得见），而面板手上没有 `BattleActor`。
    /// 两处各写一遍的话，改了一处另一处就开始说谎。
    /// </summary>
    public static int effective_hits_for(string skill_id, int base_hits, double extra_hit)
    {
        int hits = System.Math.Max(1, base_hits);
        if (skill_id == BASIC_ATTACK_ID)
        {
            return hits;
        }
        return System.Math.Max(1, hits + (int)extra_hit);
    }

    /// <summary>一次行动的期望总伤害权重（不含随机与实际防御，只用来排序）。</summary>
    public double _skill_score(BattleActor actor, Resource active)
    {
        // 策略只改我方的选招；敌人永远走均衡，免得玩家换个策略就把敌人行为也改了
        if (actor.side == BattleActor.side_ally())
        {
            if (_strategy == STRATEGY_ALL_OUT)
            {
                return active.Get("power_ratio").AsDouble();
            }
            if (_strategy == STRATEGY_CONSERVATIVE)
            {
                // 主序：内力消耗越低越好；同消耗再看倍率（越低越省）
                return -active.Get("qi_cost").AsInt32() * 1000.0 - active.Get("power_ratio").AsDouble();
            }
        }
        int targets = 1;
        if (active.Get("target_type").AsString() == "all_enemy")
        {
            targets = 0;
            Godot.Collections.Array opposing = actor.side == BattleActor.side_ally() ? _enemies : _allies;
            foreach (Variant candidate_variant in opposing)
            {
                if (((BattleActor)candidate_variant.AsGodotObject()).is_alive())
                {
                    targets += 1;
                }
            }
            targets = System.Math.Max(1, targets);
        }
        return active.Get("power_ratio").AsDouble() * _effective_hits(actor, active) * targets;
    }

    /// <summary>当前自动战斗策略（只影响我方）。</summary>
    public string strategy() => _strategy;

    public string strategy_label() => _s(STRATEGY_LABELS, _strategy, _strategy);

    /// <summary>换策略；不认识的 id 返回 false 且不改现状。</summary>
    public bool set_strategy(string strategy_id)
    {
        if (System.Array.IndexOf(STRATEGY_ORDER, strategy_id) < 0)
        {
            return false;
        }
        _strategy = strategy_id;
        return true;
    }

    /// <summary>按「保守 → 均衡 → 全力 → 保守」轮换（界面按钮用），返回新策略 id。</summary>
    public string cycle_strategy()
    {
        int index = System.Array.IndexOf(STRATEGY_ORDER, _strategy);
        _strategy = STRATEGY_ORDER[(index + 1) % STRATEGY_ORDER.Length];
        return _strategy;
    }

    /// <summary>
    /// 伤害类型是否被当前管线支持。**只有反伤（`dmg_reflect`）不支持**：`resolve()` 会显式拒绝它；
    /// DoT 类（`category=dot`）走的是同一份 resolve 的 dot 分支 + `resolve_dot()` 快照，是支持的。
    ///
    /// **这份判断的唯一出处是 `is_supported_damage_type()`（静态）**：构建期校验器也用它，
    /// 所以「表里给招式配了一个代码不认的伤害类型」会在构建期就报出来（那招会永远没有按钮，见决策 229）。
    /// </summary>
    public bool _is_supported_damage(string type_id) => is_supported_damage_type(_db, type_id);

    public static bool is_supported_damage_type(TableDb db, string type_id)
    {
        Resource row = db != null ? db.get_row("damage_type", type_id) : null;
        if (row == null)
        {
            return false;
        }
        // **DoT 是支持的**：`DamageResolver.resolve()` 对 `category=dot` 走「这一下只做命中判定 + 挂层」
        // 那条分支（每层伤害由 `resolve_dot()` 在施加那一刻算好锁死），回合末按 status_effect 结算。
        // 以前这里把 dot 一律当「没接」挡掉，于是**五毒掌／烈火掌那 6 招永远没有按钮**、自动战斗也不选它——
        // 而用例是直接 `sim.act()` 放招的，绕过了这道过滤，所以一直没人发现（见框架说明决策 228）。
        // 真正没实现的只有反伤（`dmg_reflect`，`resolve()` 会显式拒绝）。
        return type_id != "dmg_reflect";
    }

    /// <summary>
    /// 招式因为伤害类型而用不了时给玩家看的那句话。
    /// 说中文名，别把表里的英文 id（dot_poison）甩到战斗界面上——玩家看不懂那是什么。
    /// </summary>
    public string _unsupported_damage_reason(string type_id)
    {
        Resource row = _db.get_row("damage_type", type_id);
        if (row == null)
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[BattleSimulator] damage_type 缺少 {type_id}");
            return "这一招的伤害类型没配（数据错，已记进日志）";
        }
        return $"{row.Get("name_cn").AsString()} 还没接结算";
    }

    /// <summary>
    /// 招式绑定的武器类型要跟身上的武器对上。
    /// 只有带 weapon_type 标记的单位（玩家队伍）才受这条限制，敌人沿用队伍配置的招式。
    /// </summary>
    public bool _weapon_allows(BattleActor actor, SkillBaseRow base_row)
    {
        if (base_row.accepts_any_weapon())
        {
            return true;
        }
        if (!actor.tags.ContainsKey("weapon_type"))
        {
            return true;
        }
        string weapon = _s(actor.tags, "weapon_type");
        if (weapon.Length == 0)
        {
            return false;
        }
        return base_row.weapon_type == weapon;
    }

    private string _skill_name(Resource active_row)
    {
        if (active_row.Get("skill_id").AsString() == BASIC_ATTACK_ID)
        {
            return "普通攻击";
        }
        Resource base_row = _db.get_row("skill_base", active_row.Get("skill_id").AsString());
        return base_row != null ? base_row.Get("name_cn").AsString() : active_row.Get("skill_id").AsString();
    }

    /// <summary>按 skill_id 取中文名（预兆与拆招播报用；界面也读这个）。</summary>
    public string skill_display_name(string skill_id)
    {
        Resource base_row = _db.get_row("skill_base", skill_id);
        return base_row != null ? base_row.Get("name_cn").AsString() : skill_id;
    }

    /// <summary>目标选择：对面血量最低的活人；群攻招式返回全部活人。</summary>
    private Godot.Collections.Array _pick_targets(
        BattleActor actor, Godot.Collections.Array allies, Godot.Collections.Array enemies)
    {
        Godot.Collections.Array opposing = actor.side == BattleActor.side_ally() ? enemies : allies;
        var living = new Godot.Collections.Array();
        foreach (Variant target_variant in opposing)
        {
            var target = (BattleActor)target_variant.AsGodotObject();
            if (target.is_alive())
            {
                living.Add(target);
            }
        }
        if (living.Count == 0)
        {
            return new Godot.Collections.Array();
        }
        Resource skill = _skill_for(actor);
        if (skill != null)
        {
            // `self`：打自己（反噬类）——自动战斗也必须遵守，否则它会去打敌人（06 的三种 target_type 之一）
            if (skill.Get("target_type").AsString() == "self")
            {
                var only_self = new Godot.Collections.Array { actor };
                return only_self;
            }
            if (skill.Get("target_type").AsString() == "all_enemy")
            {
                return living;
            }
        }
        var best = (BattleActor)living[0].AsGodotObject();
        foreach (Variant target_variant in living)
        {
            var target = (BattleActor)target_variant.AsGodotObject();
            if (target.hp < best.hp)
            {
                best = target;
            }
        }
        var out_arr = new Godot.Collections.Array { best };
        return out_arr;
    }

    /// <summary>手选招式时的目标集合：all_enemy 展开成全部活着的对手，其余就是选中的那一个。</summary>
    private Godot.Collections.Array _targets_for(BattleActor actor, Resource skill, BattleActor chosen)
    {
        // `target_type=self`：打的是**自己**（反噬／自伤这类招式），玩家点谁都不改变这一点。
        // 以前它掉进下面的「就是选中的那一个」分支——于是自伤招式会打在敌人身上（06 允许 self，代码却没认）。
        if (skill.Get("target_type").AsString() == "self")
        {
            var only_self = new Godot.Collections.Array { actor };
            return only_self;
        }
        if (skill.Get("target_type").AsString() != "all_enemy")
        {
            var only_chosen = new Godot.Collections.Array { chosen };
            return only_chosen;
        }
        Godot.Collections.Array opposing = actor.side == BattleActor.side_ally() ? _enemies : _allies;
        var out_arr = new Godot.Collections.Array();
        foreach (Variant candidate_variant in opposing)
        {
            var candidate = (BattleActor)candidate_variant.AsGodotObject();
            if (candidate.is_alive())
            {
                out_arr.Add(candidate);
            }
        }
        if (out_arr.Count == 0)
        {
            var only_chosen = new Godot.Collections.Array { chosen };
            return only_chosen;
        }
        return out_arr;
    }
}
