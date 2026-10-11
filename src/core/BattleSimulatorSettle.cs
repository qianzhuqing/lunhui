using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// `BattleSimulator` 的「结算」这一块（partial 之一）：
/// 一次行动结算（逐段命中／暴击／格挡／破架势）、命中与施放触发的 buff、
/// 异常状态施加与回合末结算、斩杀规则、回合末 buff／DoT、修正袋。
/// </summary>
public partial class BattleSimulator
{
    /// <summary>多段循环里记录上一段是否暴击（只为了日志里能写「暴击」）。</summary>
    private bool _last_hit_crit = false;

    /// <summary>一次结算（群攻招式对每个目标各调一次，但整段只算一次行动）。</summary>
    private void _act_once(BattleActor actor, Resource skill, BattleActor target)
    {
        Godot.Collections.Dictionary modifiers = _modifiers_for(actor);
        if (target.is_broken())
        {
            // 破绽窗口：受到伤害提高（窗口由上一击打空架势产生）
            modifiers["dmg_up"] = _f(modifiers, "dmg_up", 0.0)
                + _combat_const("poise_break_bonus", POISE_BREAK_DAMAGE_BONUS);
        }
        // 拆招：被读破的那一招整招伤害减半（设计 04）。乘在现有增伤之后再减半，
        // 免得「破绽增伤 +50%」和「拆招 −50%」在同一层互相抵消。
        if (actor.side == BattleActor.side_enemy() && _parried.ContainsKey(actor.actor_id))
        {
            double up = 1.0 + _f(modifiers, "dmg_up", 0.0);
            modifiers["dmg_up"] = up * parry_damage_mult() - 1.0;
        }
        // 攻击段数：多段招式每段各掷一次命中/暴击/格挡，伤害与破架势逐段结算
        // （skill_active.hit_count ＋ 派生值 extra_hit；目标死了就不再打剩下的段）
        int hits = _effective_hits(actor, skill);
        int total_dealt = 0;
        int landed = 0;
        bool any_crit = false;
        for (int hit_index = 0; hit_index < hits; hit_index++)
        {
            if (!target.is_alive())
            {
                break;
            }
            int landed_this = _resolve_one_hit(actor, target, skill, modifiers, hit_index, hits);
            if (landed_this < 0)
            {
                break;
            }
            total_dealt += landed_this;
            landed += 1;
            any_crit = any_crit || _last_hit_crit;
        }
        if (landed > 0)
        {
            string suffix = "";
            if (any_crit)
            {
                suffix += "（暴击）";
            }
            if (hits > 1)
            {
                suffix += $"（{hits} 段）";
            }
            _log.Add(
                $"R{_rounds} {actor.display_name} 用 {_skill_name(skill)} 打 {target.display_name}：{total_dealt}{suffix}");
        }
        if (target.is_broken())
        {
            _log.Add($"R{_rounds} {target.display_name} 的架势被打破，进入破绽！");
        }
    }

    /// <summary>
    /// 命中后发「命中触发」的 buff：**每段各算一次**（与逐段结算同一口径）。
    ///
    /// 两个来源：
    ///   · 装备特效（`actor.on_hit_grants`，由 `PartyBuilder` 从 `buff_grant(source_type=equip, trigger=on_hit)` 收集，
    ///     例：淬毒指环命中叠一层淬毒）；
    ///   · **招式自带**（`buff_grant(source_type=skill_active, trigger=on_hit)`，2026-10-03 补上——
    ///     `on_cast` 那次只接了「施放」，命中这一半漏了：设计写着「招式的增益效果＝命中/施放时发的 buff」）。
    /// 两者都上不去时**不静默**：把原因写进战报。
    /// </summary>
    private void _apply_hit_grants(BattleActor actor, Resource skill)
    {
        if (actor == null)
        {
            return;
        }
        Godot.Collections.Array grants = actor.on_hit_grants.Duplicate();
        string skill_id = skill != null ? skill.Get("skill_id").AsString() : "";
        if (skill_id.Length > 0 && _db != null)
        {
            foreach (Variant row_variant in _buff_service().grants_for("skill_active", skill_id, "on_hit"))
            {
                var row = row_variant.AsGodotObject() as BuffGrantRow;
                grants.Add(new Godot.Collections.Dictionary
                {
                    { "buff_id", row.buff_id },
                    { "source_type", "skill_active" },
                    { "source_id", skill_id },
                    { "stacks", System.Math.Max(1, row.stacks) },
                });
            }
        }
        if (grants.Count == 0)
        {
            return;
        }
        foreach (Variant grant_variant in grants)
        {
            Godot.Collections.Dictionary grant = grant_variant.AsGodotDictionary();
            Godot.Collections.Dictionary result = _add_buff(
                actor, _s(grant, "buff_id"), _s(grant, "source_type"), _s(grant, "source_id"),
                _i(grant, "stacks", 1));
            if (!result["applied"].AsBool())
            {
                if (_s(result, "reason") != "unique")
                {
                    _log.Add(
                        $"{actor.display_name} 的 {_skill_name(skill)} 命中附带 "
                        + $"{_s(grant, "buff_id")} 没上上去：{_s(result, "reason")}");
                }
                continue;
            }
            if (result["stacks"].AsInt32() > 1)
            {
                _log.Add($"{actor.display_name} 的 {result["name"].AsString()} 叠到 {result["stacks"].AsInt32()} 层");
            }
            _events.Add(new Godot.Collections.Dictionary
            {
                { "kind", "buff_gain" },
                { "source", actor.actor_id },
                { "target", actor.actor_id },
                { "buff_id", result["buff_id"].AsString() },
                { "damage", 0 },
            });
        }
    }

    /// <summary>结算一段。返回这一段造成的伤害；未命中返回 0；结算失败返回 -1（调用方停手）。</summary>
    private int _resolve_one_hit(
        BattleActor actor, BattleActor target, Resource skill,
        Godot.Collections.Dictionary modifiers, int hit_index, int hit_count)
    {
        Godot.Collections.Dictionary result = _resolver.resolve(actor, target, skill, modifiers);
        if (!_b(result, "ok"))
        {
            _log.Add(
                $"R{_rounds} {actor.display_name} 招式 {_skill_name(skill)} 结算失败：{_s(result, "error")}");
            _events.Add(new Godot.Collections.Dictionary
            {
                { "kind", "failed" }, { "source", actor.actor_id }, { "target", target.actor_id }, { "damage", 0 },
            });
            return -1;
        }
        _last_hit_crit = _b(result, "is_crit");
        if (!_b(result, "is_hit"))
        {
            _log.Add($"R{_rounds} {actor.display_name} 用 {_skill_name(skill)} 打 {target.display_name}：未命中");
            _events.Add(new Godot.Collections.Dictionary
            {
                { "kind", "miss" },
                { "source", actor.actor_id },
                { "target", target.actor_id },
                { "damage", 0 },
                { "hit_index", hit_index },
                { "hit_count", hit_count },
            });
            return 0;
        }
        int dealt = target.take_damage((int)result["damage"].AsDouble());
        _damage_dealt[actor.actor_id] = _i(_damage_dealt, actor.actor_id, 0) + dealt;
        if (!target.is_alive() && !_kill_styles.ContainsKey(target.actor_id))
        {
            _kill_styles[target.actor_id] = "normal";
        }
        // 架势伤害 = 招式破架势值 + 攻击者的破架势派生数值
        int poise_hit = skill.Get("poise_damage").AsInt32() + (int)actor.stat("poise_break");
        target.take_poise_damage(poise_hit);
        // 命中触发（buff_grant.trigger=on_hit）：装备特效（淬毒指环）与招式自带的都在这发，逐段各一次
        _apply_hit_grants(actor, skill);
        Godot.Collections.Dictionary detail = result.ContainsKey("detail")
            ? result["detail"].AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        // 异常状态：命中之后掷一次（伤害类型是 dot 的招式本身没有直伤，全靠这里挂层）
        Godot.Collections.Dictionary status_event = _apply_status(actor, target, skill, modifiers);
        // 斩杀规则：血量掉到阈值以下，剩余 DoT 立刻结算（combat_const.dot_execute_threshold）
        Godot.Collections.Array executed = _settle_execute(target);
        _events.Add(new Godot.Collections.Dictionary
        {
            { "kind", "hit" },
            { "source", actor.actor_id },
            { "target", target.actor_id },
            { "damage", dealt },
            { "is_crit", _b(result, "is_crit") },
            // 系别：招式自己的，或被武器覆盖后的（见 _modifiers_for），界面与用例都能读到
            { "element", _s(detail, "element") },
            { "poise_damage", poise_hit },
            { "was_broken", target.is_broken() },
            { "killed", !target.is_alive() },
            { "hit_index", hit_index },
            { "hit_count", hit_count },
        });
        if (status_event.Count != 0)
        {
            _events.Add(status_event);
        }
        foreach (Variant entry_variant in executed)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            _events.Add(new Godot.Collections.Dictionary
            {
                { "kind", "dot" },
                { "source", _s(entry, "source_id") },
                { "target", target.actor_id },
                { "damage", _i(entry, "damage") },
                { "status_id", _s(entry, "status_id") },
                { "execute", true },
            });
        }
        return dealt;
    }

    /// <summary>
    /// 招式自带的增益（08：`skill_active` 的 `on_cast` 发放——「招式的增益效果＝施放时发的 buff」）。
    ///
    /// 与「命中触发」（`on_hit`，见 `_apply_hit_grants`）分开：**施放**一次只发一次（不管中没中、
    /// 也不管打到几个人），命中触发按命中段数各发一次。
    /// buff 上不去时**不静默**：把原因写进战报（表里给招式配了一条不存在的 buff 时，这条就是唯一的线索）。
    /// </summary>
    private void _apply_cast_grants(BattleActor actor, Resource skill)
    {
        if (actor == null || skill == null || _db == null)
        {
            return;
        }
        string skill_id = skill.Get("skill_id").AsString();
        if (skill_id.Length == 0)
        {
            return;
        }
        foreach (Variant grant_variant in _buff_service().grants_for("skill_active", skill_id, "on_cast"))
        {
            var grant = grant_variant.AsGodotObject() as BuffGrantRow;
            Godot.Collections.Dictionary result = _add_buff(
                actor, grant.buff_id, "skill_active", skill_id, System.Math.Max(1, grant.stacks));
            if (result["applied"].AsBool())
            {
                _log.Add(
                    $"{actor.display_name} 的 {_skill_name(skill)} 附带 {result["name"].AsString()}（{_duration_text(result)}）");
                _events.Add(new Godot.Collections.Dictionary
                {
                    { "kind", "buff_gain" },
                    { "source", actor.actor_id },
                    { "target", actor.actor_id },
                    { "buff_id", result["buff_id"].AsString() },
                    { "damage", 0 },
                });
                continue;
            }
            if (_s(result, "reason") == "unique")
            {
                continue; // 已经有了（unique 不重复获得）——这是正常情况，不刷屏
            }
            _log.Add(
                $"{actor.display_name} 的 {_skill_name(skill)} 附带 {grant.buff_id} 没上上去：{_s(result, "reason")}");
            _events.Add(new Godot.Collections.Dictionary
            {
                { "kind", "buff_failed" },
                { "source", actor.actor_id },
                { "target", actor.actor_id },
                { "buff_id", grant.buff_id },
                { "damage", 0 },
            });
        }
    }

    /// <summary>
    /// 上状态的几率（04 文档：运决定触发几率）= base_chance × status_chance_mul + 异常触发率，
    /// 再乘 (1 − 目标抗性)，夹在 0~95%。
    ///
    /// `status_effect.trigger_stat` 记的是**负责的属性**（运），战斗单位身上只有算好的派生数值，
    /// 所以读 `debuff_chance`（attr_to_stat 1016：运 → 异常触发率）。强度同理读 `debuff_power`。
    /// </summary>
    public double status_chance(BattleActor actor, BattleActor target, Resource skill)
    {
        string status_id = skill.Get("status_id").AsString();
        if (status_id.Length == 0)
        {
            return 0.0;
        }
        var row = _db.get_row("status_effect", status_id) as StatusEffectRow;
        if (row == null)
        {
            return 0.0;
        }
        double chance = row.base_chance * skill.Get("status_chance_mul").AsDouble();
        chance += _resolver.stat_of(actor, "debuff_chance", 0.0);
        // 抗性走 `BattleActor.resistance_of()`：**角色与敌人同一条派生数值**（stat_def.res_<status_id>），
        // 玩家练五毒心法也有毒抗（设计 0.12.0）。敌人行那份快照只是缺派生数值时的兜底。
        chance *= 1.0 - System.Math.Clamp(target.resistance_of(status_id), 0.0, 1.0);
        return System.Math.Clamp(chance, 0.0, 0.95);
    }

    /// <summary>
    /// 命中之后掷异常状态：几率 = 表里 base_chance × 招式的 status_chance_mul + 异常触发率（运），
    /// 再乘 (1 − 目标抗性)；强度走 resolve_dot 的快照伤害。返回一个事件（没上上去就是空字典）。
    /// </summary>
    private Godot.Collections.Dictionary _apply_status(
        BattleActor actor, BattleActor target, Resource skill, Godot.Collections.Dictionary modifiers)
    {
        string status_id = skill.Get("status_id").AsString();
        if (status_id.Length == 0 || !target.is_alive())
        {
            return new Godot.Collections.Dictionary();
        }
        var status_row = _db.get_row("status_effect", status_id) as StatusEffectRow;
        if (status_row == null)
        {
            GD.PushError($"[BattleSimulator] status_effect 缺少 {status_id}");
            return new Godot.Collections.Dictionary();
        }
        double chance = status_chance(actor, target, skill);
        var type_row = _db.get_row("damage_type", status_row.damage_type) as DamageTypeRow;
        int duration = status_row.duration;
        int max_stack = status_row.max_stack;
        if (type_row != null)
        {
            duration = System.Math.Max(duration, type_row.base_duration);
            max_stack = System.Math.Max(max_stack, type_row.max_stack);
        }
        if (!_rng.Call("chance", chance).AsBool())
        {
            _log.Add(
                $"R{_rounds} {actor.display_name} 的 {_status_name(status_row)} 没上到 {target.display_name}"
                + $"（几率 {chance * 100.0:F0}%）");
            return new Godot.Collections.Dictionary();
        }
        // 用状态自己的伤害类型算每层伤害（中毒无视外防、流血吃外防、内伤吃内防 + 内伤抗性）
        Godot.Collections.Dictionary dot = _resolver.resolve_dot(
            actor, target, skill, modifiers, status_row.damage_type);
        if (!_b(dot, "ok"))
        {
            return new Godot.Collections.Dictionary();
        }
        Godot.Collections.Dictionary applied = target.add_status(
            status_id, actor.actor_id, dot["damage"].AsDouble(),
            max_stack, duration, status_row.extra_rule, status_row.dispellable);
        _log.Add(
            $"R{_rounds} {actor.display_name} 让 {target.display_name} 中了{_status_name(status_row)}"
            + $" ×{applied["stacks"].AsInt32()}（每层 {(int)dot["damage"].AsDouble()}，持续 {duration} 回合）"
            + (_b(applied, "refreshed") ? "（已满层，只刷新时长）" : ""));
        return new Godot.Collections.Dictionary
        {
            { "kind", "status" },
            { "source", actor.actor_id },
            { "target", target.actor_id },
            { "status_id", status_id },
            { "stacks", applied["stacks"].AsInt32() },
            { "per_stack", (int)dot["damage"].AsDouble() },
            { "damage", 0 },
        };
    }

    private string _status_name(Resource status_row) =>
        status_row != null ? status_row.Get("name_cn").AsString() : "异常";

    private string _status_name_by_id(string status_id)
    {
        Resource row = _db.get_row("status_effect", status_id);
        return row != null ? row.Get("name_cn").AsString() : status_id;
    }

    private string _buff_name(string buff_id)
    {
        Resource row = _db.get_row("buff_def", buff_id);
        return row != null ? row.Get("name_cn").AsString() : buff_id;
    }

    /// <summary>斩杀：目标血量比例低于 combat_const.dot_execute_threshold 时，剩余层数立刻结算并清空。</summary>
    private Godot.Collections.Array _settle_execute(BattleActor target)
    {
        if (!target.is_alive() || target.statuses.Count == 0)
        {
            return new Godot.Collections.Array();
        }
        double ratio = target.hp / System.Math.Max(1.0, target.max_hp());
        if (ratio >= _execute_threshold())
        {
            return new Godot.Collections.Array();
        }
        Godot.Collections.Array settled = target.consume_all_statuses();
        int total = 0;
        foreach (Variant entry_variant in settled)
        {
            total += entry_variant.AsGodotDictionary()["damage"].AsInt32();
        }
        if (total > 0)
        {
            target.take_damage(total);
            if (!target.is_alive() && !_kill_styles.ContainsKey(target.actor_id))
            {
                _kill_styles[target.actor_id] = settled[0].AsGodotDictionary()["status_id"].AsString();
            }
            _log.Add(
                $"R{_rounds} 斩杀：{target.display_name} 气血低于 {_execute_threshold() * 100.0:F0}%，"
                + $"剩余持续伤害立即结算 {total}");
        }
        return settled;
    }

    private double _execute_threshold()
    {
        double value = 0.15;
        Resource row = _db.get_row("combat_const", "dot_execute_threshold");
        if (row != null)
        {
            value = row.Get("value").AsDouble();
        }
        return value;
    }

    private Godot.Collections.Dictionary _modifiers_for(BattleActor actor)
    {
        Godot.Collections.Dictionary modifiers = _d(_options, "modifiers").Duplicate();
        if (_encounter != null)
        {
            Godot.Collections.Dictionary extra = _encounter.round_modifiers(_rounds, actor.side);
            foreach (Variant key in extra.Keys)
            {
                string key_name = key.AsString();
                modifiers[key_name] = _f(modifiers, key_name, 0.0) + extra[key_name].AsDouble();
            }
        }
        // 武器系别覆盖招式系别（05 文档：具体系别以每件装备自己的 element 为准）
        // 敌人没有装备，tags 里没有这一项，自然还是按招式自己的系别
        string weapon_element = _s(actor.tags, "weapon_element");
        if (weapon_element.Length > 0 && !modifiers.ContainsKey("element_override"))
        {
            modifiers["element_override"] = weapon_element;
        }
        return modifiers;
    }

    private void _finish_round()
    {
        foreach (Variant actor_variant in _both_sides())
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            actor.end_of_round_regen();
            actor.end_of_round_poise();
        }
        _tick_buffs();
        _tick_statuses();
    }

    /// <summary>回合末：限时 buff 各 −1，归零播报（常驻与整场不动，见 BattleActor.tick_buffs）。</summary>
    private void _tick_buffs()
    {
        foreach (Variant actor_variant in _both_sides())
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            foreach (Variant entry_variant in actor.tick_buffs())
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                _log.Add(
                    $"R{_rounds} {actor.display_name} 的{_buff_name(_s(entry, "buff_id"))}结束");
                _events.Add(new Godot.Collections.Dictionary
                {
                    { "kind", "buff_expired" },
                    { "source", _s(entry, "source_id") },
                    { "target", actor.actor_id },
                    { "buff_id", _s(entry, "buff_id") },
                    { "damage", 0 },
                });
            }
        }
    }

    /// <summary>回合末统一结算所有 DoT（设计：不按行动条各自计时，全部在回合末跳字）。</summary>
    private void _tick_statuses()
    {
        foreach (Variant actor_variant in _both_sides())
        {
            var actor = (BattleActor)actor_variant.AsGodotObject();
            if (!actor.is_alive() || actor.statuses.Count == 0)
            {
                continue;
            }
            foreach (Variant entry_variant in actor.tick_statuses())
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                int damage = _i(entry, "damage");
                if (damage > 0)
                {
                    actor.take_damage(damage);
                }
                if (!actor.is_alive() && !_kill_styles.ContainsKey(actor.actor_id))
                {
                    // 死于持续伤害：记下是哪一类（毒杀判定读这个）
                    _kill_styles[actor.actor_id] = _s(entry, "status_id");
                }
                _log.Add(
                    $"R{_rounds} {actor.display_name} 受{_status_name_by_id(_s(entry, "status_id"))}"
                    + $" ×{_i(entry, "stacks")}：{damage}" + (_b(entry, "expired") ? "（结束）" : ""));
                _events.Add(new Godot.Collections.Dictionary
                {
                    { "kind", "dot" },
                    { "source", _s(entry, "source_id") },
                    { "target", actor.actor_id },
                    { "damage", damage },
                    { "status_id", _s(entry, "status_id") },
                    { "killed", !actor.is_alive() },
                    { "execute", false },
                });
            }
            // 回合末的 DoT 也可能把血打下去：斩杀规则同样生效
            foreach (Variant entry_variant in _settle_execute(actor))
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                _events.Add(new Godot.Collections.Dictionary
                {
                    { "kind", "dot" },
                    { "source", _s(entry, "source_id") },
                    { "target", actor.actor_id },
                    { "damage", _i(entry, "damage") },
                    { "status_id", _s(entry, "status_id") },
                    { "execute", true },
                });
            }
        }
    }
}
