using Godot;

namespace Lunhui.Core;

/// <summary>
/// 从存档构造战斗单位。C# 版，对位原 `src/core/party_builder.gd`（122 行）——**`src/` 下最后一个
/// GDScript 文件**。
///
/// 统一入口的意义：等级、加点、装备加成都在这里算进去，
/// 所以「穿上装备」「加了点」一定影响战斗，而不只是面板上好看。
///
/// 原 GDScript 里那五种 `preload("…/*.cs")`（`BattleActor`／`CharacterSheet`／`AttributeCalculator`／
/// `BuffService`／`SetService`）是「GDScript 侧先把 C# 类加载一次、让引擎注册它」的迁就做法，
/// 搬成 C# 之后整条链都在同一个程序集里，不再需要——这也正是这一批能把它删掉的原因。
/// </summary>
public partial class PartyBuilder : RefCounted
{
    /// <summary>
    /// 造一个战斗单位（**不带**战斗外增益）。造不出来（表里没这个角色）返回 null。
    /// </summary>
    public static BattleActor build_actor(TableDb db, GodotObject state, string char_id) =>
        _build_actor(db, state, char_id, null);

    /// <summary>
    /// 按存档里的队伍造所有人。`field_buffs` 是会话里还活着的战斗外增益（08）——
    /// **实参写满**：C# 的默认参数对引擎调用不生效（施工单坑⑵），GDScript 调用点一律传 `[]`。
    /// </summary>
    public static Godot.Collections.Array build_actors(
        TableDb db, GodotObject state, Godot.Collections.Array field_buffs)
    {
        var out_actors = new Godot.Collections.Array();
        if (state == null)
        {
            return out_actors;
        }

        foreach (string char_id in state.Get("char_ids").AsStringArray())
        {
            BattleActor actor = _build_actor(db, state, char_id, field_buffs);
            if (actor != null)
            {
                out_actors.Add(actor);
            }
        }

        return out_actors;
    }

    private static BattleActor _build_actor(
        TableDb db, GodotObject state, string char_id, Godot.Collections.Array field_buffs)
    {
        CharacterSheet sheet = CharacterSheet.create(db, state, char_id);
        if (!sheet.valid())
        {
            GD.PushError($"[PartyBuilder] character_base 里没有角色 {char_id}");
            return null;
        }

        BattleActor actor = BattleActor.from_character(
            db,
            sheet.template,
            sheet.level,
            sheet.allocations(),
            sheet.contributions());
        if (actor == null)
        {
            return null;
        }

        // 战斗里能用哪些招，由**装配**决定，不再是模板里有什么就全会
        SkillLoadout loadout = sheet.loadout();
        if (loadout != null)
        {
            actor.skills = loadout.battle_skill_ids();
            // 兜底：从没铺过装配的老档（已学为空）仍然按模板起始武学打，免得整场无招可出
            if (actor.skills.Length == 0 && loadout.learned_ids().Length == 0)
            {
                actor.skills = sheet.template.skill_ids();
            }
        }

        if (state != null)
        {
            foreach (string skill_id in state.Call("masteries_of", char_id).AsStringArray())
            {
                actor.skill_mastery[skill_id] = state.Call("mastery_of", char_id, skill_id);
            }

            // 天赋的规则性效果（设计 12 §五 的 `rule:`）：整份交给战斗单位，谁消费谁读
            actor.talent_rules = TalentService.rules(db, state, char_id);
        }

        // 战斗外气血（v11）：带着上一场的伤进这一场；没有记录就是满血
        int saved_hp = state != null ? state.Call("current_hp_of", char_id).AsInt32() : -1;
        if (saved_hp >= 0)
        {
            actor.hp = System.Math.Clamp(saved_hp, 1, actor.max_hp());
        }

        // 招式绑定武器类型：用身上那把武器的类型做限制（没武器就只能用通用招式）
        Resource weapon = equipped_weapon(db, state, char_id);
        actor.tags["weapon_type"] = weapon != null ? weapon.Get("weapon_type").AsString() : "";
        // 武器系别决定实际系别（05 文档：具体系别以每件装备自己的 element 为准）
        actor.tags["weapon_element"] = weapon != null ? weapon.Get("element").AsString() : "";

        // 内功指令要用「装了哪几部」；临时 buff 的属性重算也在这里把输入交给它
        if (loadout != null)
        {
            actor.passives = loadout.passive_ids();
        }

        actor.setup_recompute(
            db, AttributeCalculator.create(db), sheet.level,
            sheet.base_attrs(), sheet.allocations(), sheet.contributions());

        // 进场 buff：装备的 on_equip／on_battle_start 里**非常驻**的那些（常驻的已进贡献列表，
        // 再发一次就重复计算了）；战斗外增益（`field`）也走这里带进战斗。
        actor.pending_grants = equip_grants(db, state, char_id, new[] { "on_equip", "on_battle_start" });
        if (field_buffs != null)
        {
            foreach (Variant entry_variant in field_buffs)
            {
                Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
                // 战斗外增益（08）：会话里还活着的带进战场，**进战斗后维持整场**（不按回合递减）
                actor.pending_grants.Add(new Godot.Collections.Dictionary
                {
                    { "buff_id", entry.ContainsKey("buff_id") ? entry["buff_id"].AsString() : "" },
                    { "source_type", "field" },
                    { "source_id", entry.ContainsKey("source_id") ? entry["source_id"].AsString() : "" },
                    {
                        "stacks",
                        System.Math.Max(1, entry.ContainsKey("stacks") ? entry["stacks"].AsInt32() : 1)
                    },
                });
            }
        }

        // 命中时触发的装备特效（例：淬毒指环命中叠一层淬毒）——由模拟器在命中后发
        actor.on_hit_grants = equip_grants(db, state, char_id, new[] { "on_hit" });
        return actor;
    }

    /// <summary>
    /// 某角色身上装备发出的 buff 条目（带 `trigger`，由调用方按时机施加）。
    ///
    /// 只收**非常驻**的：常驻 buff（duration=0，装备 on_equip 与套装档位）在 `contributions()`
    /// 里就算进属性了，再发一次会重复。这里收的是「整场有效（-1）」与将来的战斗外增益。
    /// </summary>
    public static Godot.Collections.Array equip_grants(
        TableDb db, GodotObject state, string char_id, string[] triggers)
    {
        var out_grants = new Godot.Collections.Array();
        if (state == null)
        {
            return out_grants;
        }

        Inventory inventory = state.Get("inventory").As<Inventory>();
        if (inventory == null)
        {
            return out_grants;
        }

        BuffService service = BuffService.create(db);
        foreach (string instance_id in inventory.equipped_instances(db, char_id))
        {
            string base_id = inventory.base_of(instance_id);
            foreach (string trigger in triggers)
            {
                foreach (Variant grant_variant in service.grants_for("equip", base_id, trigger))
                {
                    Resource grant = grant_variant.As<Resource>();
                    string buff_id = grant.Get("buff_id").AsString();
                    if (service.is_permanent(buff_id))
                    {
                        continue;
                    }

                    out_grants.Add(new Godot.Collections.Dictionary
                    {
                        { "buff_id", buff_id },
                        { "source_type", "equip" },
                        { "trigger", trigger },
                        { "source_id", base_id },
                        { "stacks", System.Math.Max(1, grant.Get("stacks").AsInt32()) },
                    });
                }
            }
        }

        return out_grants;
    }

    /// <summary>角色当前装备的武器本体（equip_base 行）；没装备返回 null。</summary>
    public static Resource equipped_weapon(TableDb db, GodotObject state, string char_id)
    {
        if (state == null)
        {
            return null;
        }

        Inventory inventory = state.Get("inventory").As<Inventory>();
        if (inventory == null)
        {
            return null;
        }

        foreach (string instance_id in inventory.equipped_instances(db, char_id))
        {
            Resource base_row = db.get_row("equip_base", inventory.base_of(instance_id));
            if (base_row != null && base_row.Get("slot").AsString() == "weapon")
            {
                return base_row;
            }
        }

        return null;
    }

    /// <summary>角色当前装备的武器类型；没装备返回空串。</summary>
    public static string equipped_weapon_type(TableDb db, GodotObject state, string char_id)
    {
        Resource weapon = equipped_weapon(db, state, char_id);
        return weapon != null ? weapon.Get("weapon_type").AsString() : "";
    }
}
