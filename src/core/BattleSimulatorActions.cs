using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `BattleSimulator` 的「行动」这一块（partial 之一）：
/// 六指令（普通攻击／招式／内功／道具／主动防御／逃跑）＋预兆与拆招，
/// 以及统一「花掉一次行动」的收尾（`_spend_action`）。
/// </summary>
public partial class BattleSimulator
{
    /// <summary>拆招把被读破那一招的伤害压到几成。界面（拆招按钮的提示）也读这里，别在界面写死数字。</summary>
    public double parry_damage_mult() => _combat_const("parry_damage_mult", PARRY_DAMAGE_MULT);

    /// <summary>用指定招式打指定目标（手选招式走这里），返回本行动的日志行。</summary>
    public Godot.Collections.Array act(BattleActor actor, Resource skill, BattleActor target)
    {
        if (actor == null || skill == null || target == null)
        {
            return new Godot.Collections.Array();
        }
        int from_index = _log.Count;
        // 增益招式：没有伤害、不走伤害管线——施放一次给自己上 buff（表里那条 on_cast 发放）
        var active_skill = skill as SkillActiveRow;
        bool skill_is_attack = active_skill != null && active_skill.is_attack();
        if (!skill_is_attack)
        {
            if (!_is_support_skill(skill.Get("skill_id").AsString()))
            {
                return new Godot.Collections.Array();
            }
            _record_use(actor, skill);
            _start_cooldown(actor, skill);
            _log.Add($"R{_rounds} {actor.display_name} 施放 {_skill_name(skill)}（增益）");
            _apply_cast_grants(actor, skill);
            _spend_skill_qi(actor, skill);
            _order_index += 1;
            if (_order.Count > 0 && current_actor() == null)
            {
                _finish_round();
            }
            return _slice(_log, from_index);
        }
        _record_use(actor, skill);
        _start_cooldown(actor, skill);
        // 手选招式也要遵守 target_type：全体招式点谁都一样，打的是全场
        foreach (Variant hit_target_variant in _targets_for(actor, skill, target))
        {
            _act_once(actor, skill, (BattleActor)hit_target_variant.AsGodotObject());
        }
        _spend_skill_qi(actor, skill);
        _apply_cast_grants(actor, skill);
        _order_index += 1;
        // 只有「本来就在回合里」才结算回合末：没有 begin_round 就调 act（用例／脚本直接出招）
        // 不该顺手把刚打出来的破绽窗口和架势回复跑掉
        if (_order.Count > 0 && current_actor() == null)
        {
            _finish_round();
        }
        return _slice(_log, from_index);
    }

    /// <summary>
    /// 出一招扣多少内力：**四个扣气点都走这里**（手选／自动／敌人那条路）——分散写就会出现
    /// "某条路径忘了这条签名"。算法仍旧只有 `SkillCost` 一处（实际气耗＝表值 × 系数、只作用我方）；
    /// 这里多加的是**满级签名「引气」**：每场第一招不耗内力（设计 08，用 `first_move_done` 记一次）。
    /// </summary>
    private void _spend_skill_qi(BattleActor actor, Resource skill)
    {
        int base_cost = skill.Get("qi_cost").AsInt32();
        bool free_first = !actor.first_move_done && actor.mastery_marker("first_move_free") > 0.0;
        actor.first_move_done = true;
        if (free_first)
        {
            return;
        }

        actor.spend_qi(SkillCost.effective_qi_cost(
            _db, base_cost, actor.side == BattleActor.side_ally()));
    }

    /// <summary>让当前单位按自动策略行动。</summary>
    public Godot.Collections.Array auto_act()
    {
        var actor = (BattleActor)current_actor();
        if (actor == null)
        {
            return new Godot.Collections.Array();
        }
        int from_index = _log.Count;
        Resource skill = _skill_for(actor);
        if (skill == null)
        {
            _log.Add($"R{_rounds} {actor.display_name} 无可用招式，跳过");
            _order_index += 1;
            if (current_actor() == null)
            {
                _finish_round();
            }
            return _slice(_log, from_index);
        }
        _record_use(actor, skill);
        _start_cooldown(actor, skill);
        foreach (Variant target_variant in _pick_targets(actor, _allies, _enemies))
        {
            _act_once(actor, skill, (BattleActor)target_variant.AsGodotObject());
        }
        _spend_skill_qi(actor, skill);
        _apply_cast_grants(actor, skill);
        _order_index += 1;
        if (current_actor() == null)
        {
            _finish_round();
        }
        return _slice(_log, from_index);
    }

    // ------------------------------------------------------------------ 逃跑（设计 02：被追上第一回合不能逃）

    /// <summary>现在能不能撤；能撤返回空串，否则返回给玩家看的原因。</summary>
    public string flee_block_reason(BattleActor actor)
    {
        if (actor == null)
        {
            return "现在不是我方行动";
        }
        if (actor.side != BattleActor.side_ally())
        {
            return "只能我方决定撤不撤";
        }
        if (_escaped || finished())
        {
            return "战斗已经结束了";
        }
        // 设计 02 的接触结果表：被追击抓到 → 敌方先手，且第一回合无法逃跑
        if (_rounds <= 1 && _encounter != null && _encounter.contact == Encounter.contact_caught())
        {
            return "被追上来的第一回合撤不掉";
        }
        if (_order.Count == 0 || current_actor() != actor)
        {
            return "现在不是我方行动";
        }
        return "";
    }

    /// <summary>逃跑成功率：双方最高身法对比，同速 50%，快的一方更高（夹在 25%~95%）。</summary>
    public double flee_chance() => flee_chance(null);

    /// <inheritdoc cref="flee_chance()"/>
    public double flee_chance(BattleActor _actor)
    {
        double mine = _best_speed(_allies);
        double theirs = _best_speed(_enemies);
        double total = mine + theirs;
        if (total <= 0.0)
        {
            return _combat_const("flee_base", FLEE_BASE_CHANCE);
        }
        double base_chance = _combat_const("flee_base", FLEE_BASE_CHANCE);
        double chance = base_chance + (1.0 - base_chance) * ((mine - theirs) / total);
        return System.Math.Clamp(
            chance,
            _combat_const("flee_min", FLEE_MIN_CHANCE),
            _combat_const("flee_max", FLEE_MAX_CHANCE));
    }

    private double _best_speed(Godot.Collections.Array actors)
    {
        double best = 0.0;
        foreach (Variant actor_variant in actors)
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (actor.is_alive())
            {
                best = System.Math.Max(best, actor.stat("speed"));
            }
        }
        return best;
    }

    /// <summary>
    /// 逃跑：花掉这次行动掷一次判定。成功 → 战斗以「撤退」收场（无经验无掉落、明雷不清）；
    /// 失败 → 白费一次行动。返回 {ok, escaped, error, lines, chance, roll}
    /// </summary>
    public Godot.Collections.Dictionary flee(BattleActor actor)
    {
        string reason = flee_block_reason(actor);
        if (reason.Length > 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "escaped", false }, { "error", reason },
                { "lines", new Godot.Collections.Array() }, { "chance", 0.0 }, { "roll", 0.0 },
            };
        }
        double chance = flee_chance(actor);
        double roll = _rng.Call("randf").AsDouble();
        Godot.Collections.Dictionary modifiers = _d(_options, "modifiers");
        bool escaped = roll < chance;
        // 测试开关：force_flee / no_flee（和 force_hit／force_crit 同一条路子）
        if (_b(modifiers, "force_flee"))
        {
            escaped = true;
        }
        else if (_b(modifiers, "no_flee"))
        {
            escaped = false;
        }
        var lines = new Godot.Collections.Array();
        if (escaped)
        {
            _escaped = true;
            lines.Add($"R{_rounds} {actor.display_name} 带队撤退成功（掷 {roll:F2} < {chance:F2}）");
        }
        else
        {
            lines.Add($"R{_rounds} {actor.display_name} 想撤，被缠住了（掷 {roll:F2} ≥ {chance:F2}）");
            _order_index += 1;
            if (_order.Count > 0 && current_actor() == null)
            {
                _finish_round();
            }
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "escaped", escaped }, { "error", "" },
            { "lines", lines }, { "chance", chance }, { "roll", roll },
        };
    }

    // ------------------------------------------------------------------ 预兆与拆招（设计 04 核心循环）

    /// <summary>每回合开始给每个活着的敌人定好这一回合的招式，玩家在轮到自己之前就能看见预兆。</summary>
    private void _refresh_intents()
    {
        _intents = new Godot.Collections.Dictionary();
        _parried = new Godot.Collections.Dictionary();
        foreach (Variant enemy_variant in _enemies)
        {
            var enemy = (BattleActor)enemy_variant.AsGodotObject();
            if (!enemy.is_alive())
            {
                continue;
            }
            Resource skill = pick_skill(enemy);
            if (skill != null)
            {
                _intents[enemy.actor_id] = skill.Get("skill_id").AsString();
            }
        }
    }

    /// <summary>某个敌人这一回合预兆的招式 id（没有返回空串）。</summary>
    public string intent_of(string actor_id) =>
        _intents.ContainsKey(actor_id) ? _intents[actor_id].AsString() : "";

    public bool is_parried(string actor_id) => _parried.ContainsKey(actor_id);

    /// <summary>预兆文案（界面与战报用）：["大寨主 将用 黑风刀法·斩风", ...]</summary>
    public string[] intent_lines()
    {
        var out_rows = new System.Collections.Generic.List<string>();
        foreach (Variant enemy_variant in _enemies)
        {
            var enemy = (BattleActor)enemy_variant.AsGodotObject();
            if (!enemy.is_alive())
            {
                continue;
            }
            string skill_id = intent_of(enemy.actor_id);
            if (skill_id.Length == 0)
            {
                continue;
            }
            out_rows.Add($"{enemy.display_name} 将用 {skill_display_name(skill_id)}");
        }
        return out_rows.ToArray();
    }

    /// <summary>拆招能不能用；能用返回空串，否则返回给玩家看的原因。</summary>
    public string parry_block_reason(BattleActor actor, BattleActor target)
    {
        if (actor == null)
        {
            return "现在不是我方行动";
        }
        if (target == null)
        {
            return "没有可拆的目标";
        }
        if (actor.side != BattleActor.side_ally() || target.side != BattleActor.side_enemy())
        {
            return "只能拆敌人的招";
        }
        if (!target.is_alive())
        {
            return $"{target.display_name} 已经倒下";
        }
        if (!_has_living(_allies) || !_has_living(_enemies))
        {
            return "战斗已结束";
        }
        string target_id = target.actor_id;
        if (!_intents.ContainsKey(target_id))
        {
            return $"{target.display_name} 这一回合没有出招预兆";
        }
        if (_parried.ContainsKey(target_id))
        {
            return $"{target.display_name} 的招这一回合已经拆过了";
        }
        return "";
    }

    /// <summary>
    /// 拆招：花掉这次行动，读破目标的预兆招式（那一招伤害减半），并反涨自身架势。
    /// 返回 {ok, error, lines, poise_gain}
    /// </summary>
    public Godot.Collections.Dictionary parry(BattleActor actor, BattleActor target)
    {
        string reason = parry_block_reason(actor, target);
        if (reason.Length > 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", reason }, { "lines", new Godot.Collections.Array() }, { "poise_gain", 0 },
            };
        }
        string target_id = target.actor_id;
        _parried[target_id] = true;
        int before = actor.poise;
        // 架势池为 0 的单位（没有架势机制的临时夹具）没有「反涨」可言，跳过而不是加 0 条假日志
        int pool = actor.max_poise();
        if (pool > 0)
        {
            // 满级签名「定军」：拆招成功时**架势反涨翻倍**（设计 08；这一条的命中加成在
            // `DamageResolver.hit_chance()` 那一侧）
            double parry_gain = _combat_const("parry_poise_gain", PARRY_POISE_GAIN)
                * (1.0 + actor.mastery_marker("parry_double"));
            int gain = System.Math.Max(1, (int)System.Math.Round(pool * parry_gain));
            actor.poise = System.Math.Min(pool, actor.poise + gain);
        }
        var lines = new Godot.Collections.Array();
        lines.Add(
            $"R{_rounds} {actor.display_name} 拆招，读破 {target.display_name} 的 "
            + $"{skill_display_name(_intents[target_id].AsString())}（这一招伤害减半）");
        lines.Add($"R{_rounds} {actor.display_name} 架势 +{actor.poise - before}（{actor.poise}/{actor.max_poise()}）");
        _order_index += 1;
        // 拆招也是一次行动：拆完这回合的行动就用掉了，回合末结算照旧
        if (_order.Count > 0 && current_actor() == null)
        {
            _finish_round();
        }
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "lines", lines },
            { "poise_gain", actor.poise - before }, { "target", target_id },
        };
    }

    // ------------------------------------------------------------------ 六指令（08：唯一的行动入口）
    //
    // 六条：普通攻击／招式／内功／道具／主动防御／逃跑。招式（`act`）与逃跑（`flee`）早早就有，
    // 这里补齐其余四条，并统一「花掉一次行动」的收尾（本回合跑完就结算回合末，与 `act` 同一条路）。

    /// <summary>
    /// 现造一条招式行（不落表）：走与招式完全同一条伤害管线，只是没有 skill_base 身份
    /// （星级按缺省 1 星、无熟练度，见 DamageResolver 的 null 兜底）。
    /// </summary>
    public Resource basic_attack_skill()
    {
        if (_basic_attack_row == null)
        {
            _basic_attack_row = new SkillActiveRow
            {
                skill_id = BASIC_ATTACK_ID,
                damage_type = "dmg_normal",
                power_ratio = BASIC_ATTACK_POWER,
                qi_cost = 0,
                poise_damage = BASIC_ATTACK_POISE,
                hit_count = 1,
                target_type = "single",
            };
        }
        return _basic_attack_row;
    }

    /// <summary>
    /// 这一下"普通攻击"用的是哪一行：**普攻位装了免费招就用它**（设计 04 §武学页三块玩家配置），
    /// 空着才退回系统普攻那一行（`__basic_attack__`，不落表）。
    ///
    /// 装上去之后那三条差异**自动成立**：段数吃 `extra_hit`（`skill_id` 不再是普攻那条，
    /// `effective_hits_for()` 自然走招式那一支）、计入招式使用记录（`_record_use` 上面那个
    /// 普攻早返回只认 `__basic_attack__`）、武器类型限制（装配时 `can_set_basic` 就拦了一道）。
    /// </summary>
    private Resource basic_attack_skill_for(BattleActor actor)
    {
        string basic_id = actor != null ? actor.basic_skill_id : "";
        if (basic_id.Length > 0 && _db.get_row("skill_active", basic_id) is SkillActiveRow row)
        {
            return row;
        }

        return basic_attack_skill();
    }

    public Godot.Collections.Array basic_attack(BattleActor actor, BattleActor target)
    {
        if (actor == null || target == null || !target.is_alive())
        {
            return new Godot.Collections.Array();
        }
        int from_index = _log.Count;
        Resource skill = basic_attack_skill_for(actor);
        foreach (Variant hit_target_variant in _targets_for(actor, skill, target))
        {
            _act_once(actor, skill, (BattleActor)hit_target_variant.AsGodotObject());
        }
        _spend_action(actor);
        return _slice(_log, from_index);
    }

    /// <summary>
    /// 主动防御：「花一次行动换 buff_def 里的防御姿态」。
    /// `buff_stat` 给的是减伤与外防；**架势回复与下回合先手表里都没有字段**——
    /// 架势回复先按开发侧常数做（与拆招同一档 15%，已记交接表），下回合先手在这条代码里实现
    /// （`_pending_first_next_round`），因为它是这套 buff 唯一无法用数值列表达的语义。
    /// </summary>
    public string defend_block_reason(BattleActor actor)
    {
        if (actor == null || !actor.is_alive())
        {
            return "这个单位不能行动";
        }
        BuffService service = actor.buff_service();
        if (service == null || !service.exists(DEFEND_BUFF_ID))
        {
            // 数据错：id 只进日志（AGENTS：玩家可见文案不许出现表内 id，见框架说明决策 330）
            GD.PushError($"[BattleSimulator] buff_def 里没有防御姿态（{DEFEND_BUFF_ID}）");
            return "防御姿态的配置对不上（数据错，已记进日志）";
        }
        if (_escaped || finished())
        {
            return "战斗已经结束了";
        }
        if (actor.has_buff(DEFEND_BUFF_ID))
        {
            return "已经在这个姿态里了";
        }
        return "";
    }

    /// <summary>返回 {ok, error, lines, poise_gain}。</summary>
    public Godot.Collections.Dictionary defend(BattleActor actor)
    {
        string reason = defend_block_reason(actor);
        if (reason.Length > 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", reason }, { "lines", new Godot.Collections.Array() }, { "poise_gain", 0 },
            };
        }
        int from_index = _log.Count;
        Godot.Collections.Dictionary result = actor.add_buff(DEFEND_BUFF_ID, "action", "defend");
        if (!result["applied"].AsBool())
        {
            string why = _s(result, "reason");
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "poise_gain", 0 }, { "lines", new Godot.Collections.Array() },
                { "error", $"防御姿态上不去：{(why == "unique" ? "这个姿态是唯一的，已经有一层了" : why)}" },
            };
        }
        int before_poise = actor.poise;
        int gain = System.Math.Max(1, (int)System.Math.Ceiling(
            actor.max_poise() * _combat_const("defend_poise_gain", DEFEND_POISE_GAIN)));
        actor.poise = System.Math.Min(actor.max_poise(), actor.poise + gain);
        _pending_first_next_round[actor.actor_id] = true;
        _log.Add(
            $"R{_rounds} {actor.display_name} 进入防御姿态（本回合减伤、下回合先手，架势 +{actor.poise - before_poise}）");
        _events.Add(new Godot.Collections.Dictionary
        {
            { "kind", "buff_gain" }, { "source", actor.actor_id }, { "target", actor.actor_id },
            { "buff_id", DEFEND_BUFF_ID }, { "damage", 0 },
        });
        _spend_action(actor);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "lines", _slice(_log, from_index) },
            { "poise_gain", actor.poise - before_poise },
        };
    }

    /// <summary>
    /// 已装内功 → 它能催动出的运功 buff（界面按这个列表排「内功」指令的按钮）。
    /// 返回 [{skill_id, name, slot_cost, buff_id, buff_name, qi_cost, ok, reason}]
    /// </summary>
    public Godot.Collections.Array passive_cast_options(BattleActor actor)
    {
        var out_arr = new Godot.Collections.Array();
        if (actor == null)
        {
            return out_arr;
        }
        BuffService service = BuffService.create(_db);
        foreach (string skill_id in actor.passives)
        {
            var base_row = _db.get_row("skill_base", skill_id) as SkillBaseRow;
            var passive = _db.get_row("skill_passive", skill_id) as SkillPassiveRow;
            Godot.Collections.Array grants = service.grants_for("skill_passive", skill_id, "on_cast");
            string buff_id = grants.Count > 0 ? ((BuffGrantRow)grants[0].AsGodotObject()).buff_id : "";
            out_arr.Add(new Godot.Collections.Dictionary
            {
                { "skill_id", skill_id },
                { "name", base_row != null ? base_row.name_cn : skill_id },
                { "slot_cost", passive != null ? passive.slot_cost : 0 },
                { "buff_id", buff_id },
                { "buff_name", buff_id.Length > 0 ? _buff_name(buff_id) : "" },
                { "qi_cost", passive_qi_cost(skill_id) },
                { "ok", passive_cast_block_reason(actor, skill_id).Length == 0 },
                { "reason", passive_cast_block_reason(actor, skill_id) },
            });
        }
        return out_arr;
    }

    /// <summary>催动一部内功要多少内力（`skill_passive.qi_cost`，按占格 ×6；设计 08 参数口径 Q87）。</summary>
    public int passive_qi_cost(string skill_id)
    {
        var passive = _db.get_row("skill_passive", skill_id) as SkillPassiveRow;
        return passive != null ? System.Math.Max(0, passive.qi_cost) : 0;
    }

    /// <summary>
    /// 内功催动：现在能不能催动；能用返回空串。
    /// 内力消耗取自 `skill_passive.qi_cost`（占格 ×6：1 格 6／2 格 12／3 格 18）。
    /// </summary>
    public string passive_cast_block_reason(BattleActor actor, string skill_id)
    {
        if (actor == null || !actor.is_alive())
        {
            return "这个单位不能行动";
        }
        if (System.Array.IndexOf(actor.passives, skill_id) < 0)
        {
            return "这部内功没有装配";
        }
        BuffService service = BuffService.create(_db);
        if (service.grants_for("skill_passive", skill_id, "on_cast").Count == 0)
        {
            return "这部内功没有运功 buff（表里没有 on_cast 的发放）";
        }
        if (_escaped || finished())
        {
            return "战斗已经结束了";
        }
        int cost = passive_qi_cost(skill_id);
        if (actor.max_qi() > 0 && cost > actor.qi)
        {
            return $"内力不足（要 {cost}）";
        }
        return "";
    }

    /// <summary>返回 {ok, error, lines, buff_id}。</summary>
    public Godot.Collections.Dictionary cast_passive(BattleActor actor, string skill_id)
    {
        string reason = passive_cast_block_reason(actor, skill_id);
        if (reason.Length > 0)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", reason }, { "lines", new Godot.Collections.Array() }, { "buff_id", "" },
            };
        }
        int from_index = _log.Count;
        BuffService service = BuffService.create(_db);
        Godot.Collections.Array grants = service.grants_for("skill_passive", skill_id, "on_cast");
        var applied = new System.Collections.Generic.List<string>();
        foreach (Variant grant_variant in grants)
        {
            var grant = (BuffGrantRow)grant_variant.AsGodotObject();
            Godot.Collections.Dictionary result = _add_buff(
                actor, grant.buff_id, "skill_passive", skill_id, System.Math.Max(1, grant.stacks));
            if (result["applied"].AsBool() || _s(result, "reason") == "unique")
            {
                applied.Add(result["name"].AsString());
            }
        }
        int cost = passive_qi_cost(skill_id);
        actor.spend_qi(cost);
        _log.Add(
            $"R{_rounds} {actor.display_name} 催动 {skill_display_name(skill_id)}：{string.Join("、", applied)}"
            + (cost > 0 ? $"（耗气 {cost}）" : ""));
        _events.Add(new Godot.Collections.Dictionary
        {
            { "kind", "buff_gain" }, { "source", actor.actor_id }, { "target", actor.actor_id },
            { "buff_id", skill_id }, { "damage", 0 },
        });
        _spend_action(actor);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "lines", _slice(_log, from_index) },
            { "buff_id", ((BuffGrantRow)grants[0].AsGodotObject()).buff_id },
        };
    }

    /// <summary>
    /// 道具（六指令之一）：`item_base` 里 `use_context=battle` 的道具。
    /// **效果数值列还没有**——设计把效果写在 `desc` 文字里（例：「战斗中恢复气血60」）。
    /// 所以这里只把指令与「为什么现在用不了」如实列出来：不解析中文、也不自己编数值。
    /// </summary>
    public Godot.Collections.Array battle_item_options(GodotObject inventory)
    {
        var inv = inventory as Inventory;
        var out_arr = new Godot.Collections.Array();
        foreach (Variant row_variant in _db.rows("item_base"))
        {
            var row = (ItemBaseRow)row_variant.AsGodotObject();
            if (row.use_context != "battle")
            {
                continue;
            }
            int qty = inv != null ? inv.count(row.item_id) : 0;
            // Q33 三列落表之后（2026-10-13），"能不能用"就是真判定：有数量、而且三列里至少有一条
            bool has_effect = row.heal_hp > 0 || row.heal_qi > 0 || row.clear_status.Length > 0;
            string reason = qty <= 0
                ? "身上没有了"
                : (has_effect ? "" : "这件道具还没配效果（效果数值三列都是空的）");
            out_arr.Add(new Godot.Collections.Dictionary
            {
                { "item_id", row.item_id },
                { "name", row.name_cn },
                { "qty", qty },
                { "desc", row.desc },
                { "usable", qty > 0 && has_effect },
                { "reason", reason },
            });
        }
        return out_arr;
    }

    /// <summary>
    /// 六指令之一「道具」：真用一件（`use_context = battle`）。返回 `{ok, error, lines}`。
    ///
    /// 效果读的是 Q33 那三列（回气血／回内力／解异常）——**不解析 `desc` 里那句中文**，
    /// 与战斗外那条路（`ItemUse.use_field()`）同一套口径，免得"屏上写回 60、实际回 40"。
    /// **花一次行动**（设计里"用药不消耗行动"是满级签名「养生」的丙档，还没接）；
    /// 目标是**用的人自己**（六指令表没写目标选择）。
    /// </summary>
    public Godot.Collections.Dictionary use_item(BattleActor actor, string item_id, GodotObject inventory)
    {
        var lines = new Godot.Collections.Array();
        Godot.Collections.Dictionary fail(string error) => new()
        {
            { "ok", false }, { "error", error }, { "lines", new Godot.Collections.Array() },
        };

        if (actor == null || !actor.is_alive())
        {
            return fail("这个人现在动不了");
        }

        if (_db.get_row("item_base", item_id) is not ItemBaseRow row || row.use_context != "battle")
        {
            return fail("这件道具在战斗里用不上");
        }

        var inv = inventory as Inventory;
        if (inv == null || inv.count(item_id) <= 0)
        {
            return fail($"身上没有{row.name_cn}了");
        }

        if (row.heal_hp <= 0 && row.heal_qi <= 0 && row.clear_status.Length == 0)
        {
            return fail("这件道具还没配效果（效果数值三列都是空的）");
        }

        if (row.heal_hp > 0)
        {
            int healed = actor.heal(row.heal_hp);
            lines.Add(healed > 0
                ? $"{actor.display_name} 回了 {healed} 点气血（{actor.hp}/{actor.max_hp()}）"
                : $"{actor.display_name} 的气血是满的，这一口没回上去");
        }

        if (row.heal_qi > 0)
        {
            int gained = actor.gain_qi(row.heal_qi);
            lines.Add(gained > 0
                ? $"{actor.display_name} 回了 {gained} 点内力（{actor.qi}/{actor.max_qi()}）"
                : $"{actor.display_name} 的内力是满的，这一口没回上去");
        }

        if (row.clear_status.Length > 0)
        {
            int cleared = actor.clear_status(row.clear_status);
            lines.Add(cleared > 0
                ? $"{actor.display_name} 身上的{_status_name(row.clear_status)}解了"
                : $"{actor.display_name} 身上没有{_status_name(row.clear_status)}（这一口白喝）");
        }

        inv.consume(item_id, 1);
        _spend_action(actor);
        return new Godot.Collections.Dictionary
        {
            { "ok", true }, { "error", "" }, { "lines", lines },
        };
    }

    /// <summary>异常状态的中文名（照 `status_effect` 读；查不到给空串——战报里不印 id）。</summary>
    private string _status_name(string status_id)
    {
        var row = _db.get_row("status_effect", status_id) as StatusEffectRow;
        return row != null ? row.name_cn : "";
    }

    /// <summary>一次行动的收尾：轮到下一位；本回合跑完就结算回合末。</summary>
    private void _spend_action(BattleActor _actor)
    {
        _order_index += 1;
        if (_order.Count > 0 && current_actor() == null)
        {
            _finish_round();
        }
    }
}
