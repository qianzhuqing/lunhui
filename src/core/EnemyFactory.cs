using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 敌人实例化：`enemy_base` × `difficulty_config` → `BattleActor`（C# 版，对位原 `src/core/enemy_factory.gd`）。
///
/// 难度只改数值倍率，不改掉落清单（掉落倍率在 `DropResolver` 里处理）。
///
/// 招式来自 `enemy_skill.csv`（0.10.0 由设计交付，见 `docs/design/06_配置表说明.md`）。
/// 原来这里是两张写死的映射（`AI_SKILL_MAP` / `ENEMY_SKILL_OVERRIDE`）——Q48 问过设计后已作废：
/// 「谁用哪几招」现在设计侧改表就行，不用改代码。
/// 构建期 `TableValidatorStructure.CheckAiTemplates`（原 `table_validator._check_ai_templates`）
/// 仍然守着两件事：`ai_template` 必须是代码认的取值、
/// 除 `ai_neutral`（不还手，如采药人）外每个敌人在 `enemy_skill` 里至少要有一条招式。
///
/// **跨语言面**：2026-10-11 起 `BattleActor` 也搬成 C#，`BattleActor.from_enemy(...)`
/// **直接静态调用**（五个实参写全——默认参数对引擎调用不生效，见施工单「二点四七」坑⑵）。
/// `Inventory` 已是 C#，`equipment_contributions(...)` 直接静态调用（四个实参写全——坑②）。
/// </summary>
public partial class EnemyFactory : RefCounted
{
    /// <summary>
    /// 还在用旧派生列的敌人：**每个进程只报一次**（构建/自检日志里看得见，不刷屏）。
    /// 设计 10 §三的顺序就是「先改表结构与工厂、**跑通空数据**，再一起配数值」——
    /// 七维那一列空着时走这条路，配上了自动切到共用管线。
    /// </summary>
    private static bool _derived_path_warned;

    /// <summary>
    /// `enemy_base.ai_template` 的合法取值：**只决定行为**（`ai_boss` → 架势回复更高、
    /// `ai_neutral` → 不还手、`ai_sleeping` → 可奇袭……），不再决定招式（招式在 `enemy_skill.csv`）。
    /// 构建期校验器拿这份清单查表；06 目前没有登记这列，已记进 `待策划确认.md`（请设计补进数据字典）。
    ///
    /// **普通 `string[]`，不是 Godot 集合**：静态字段里放 Godot 集合会让终结器在 native 拆完之后才跑
    /// （施工单坑⑧，`SEHException`）。
    /// </summary>
    private static readonly string[] AI_TEMPLATES =
    {
        "ai_aggressive", "ai_basic", "ai_boss", "ai_chaser", "ai_debuffer",
        "ai_elite", "ai_neutral", "ai_ranged", "ai_sleeping",
    };

    // ---------------------------------------------------------------- 过渡期取值口
    // GDScript 读不到 C# 的静态字段（施工单坑⑩）：构建期校验器
    // `TableValidatorStructure.CheckAiTemplates` 与 `tests/test_enemy_template.gd` 都拿它当
    // 「AI 模板名单」的唯一真相，改成同名小写静态方法。

    /// <summary>AI 模板名单（返回副本，免得调用方把这份唯一真相改掉）。</summary>
    public static string[] ai_templates() => (string[])AI_TEMPLATES.Clone();

    // ---------------------------------------------------------------- 实例

    private readonly TableDb _db;

    public EnemyFactory(TableDb db)
    {
        _db = db;
    }

    /// <summary>
    /// GDScript 侧唯一的构造入口（`EnemyFactoryScript.create(db)`）。
    ///
    /// **静态工厂不能和实例方法同名**（施工单坑㊸）：`create` 还兼着「造一个敌人」那个实例方法，
    /// 两者同名时 Godot 生成的**方法表按名字去重、实例那条盖住静态那条**，GDScript 解析
    /// `EnemyFactoryScript.create(db)` 会报 `Static function "create()" not found in base "CSharpScript"`
    /// （`battle_screen.gd` 装场景时整个脚本加载失败，2026-10-11 实测）。所以实例那条改名
    /// `create_enemy`，静态工厂保住 `create`（与其余已搬的类口径一致）。
    /// </summary>
    public static EnemyFactory create(TableDb db) => new(db);

    /// <summary>生成一个敌人；`enemy_id` 不存在时报错并返回 null。</summary>
    public GodotObject create_enemy(string enemy_id, string difficulty_id)
    {
        var row = _db.get_row("enemy_base", enemy_id) as EnemyBaseRow;
        if (row == null)
        {
            GD.PushError($"[EnemyFactory] enemy_base 缺少 {enemy_id}");
            return null;
        }

        var difficulty = _db.get_row("difficulty_config", difficulty_id) as DifficultyConfigRow;
        if (difficulty == null)
        {
            GD.PushError($"[EnemyFactory] 未知难度 {difficulty_id}，退回 normal");
            difficulty = (DifficultyConfigRow)_db.require_row("difficulty_config", "normal");
        }

        Godot.Collections.Dictionary attrs = row.attr_map();
        Godot.Collections.Dictionary stats = attrs.Count > 0
            ? _stats_from_attrs(row, attrs, difficulty)
            : _stats_from_derived(row, difficulty);
        string[] skills = skills_of(row.enemy_id);

        BattleActor actor = BattleActor.from_enemy(row, stats, skills, row.level, _db);
        actor.actor_id = row.enemy_id;
        actor.reward_exp = (int)System.Math.Round(
            row.exp_reward * difficulty.exp_mul, System.MidpointRounding.AwayFromZero);
        actor.reward_money = (int)System.Math.Round(
            row.money * difficulty.money_mul, System.MidpointRounding.AwayFromZero);
        // `actor.tags` 是同一份原生字典，就地改就够（对位 GDScript 的 `actor.tags["difficulty"] = …`）
        actor.tags["difficulty"] = difficulty_id;
        // 异常抗性要真的挂上：毒手 50% 毒抗、大寨主／醉刀客 20~30% 三抗（enemy_base 的 res_* 列）
        actor.resistances = row.resistances();
        return actor;
    }

    /// <summary>
    /// 旧派生列那条路（过渡期回退）：`enemy_base` 那七列 `attr_*` 还空着时用。
    /// **内功／装备的 `stat:` 类加成照样叠上去**（毒抗、内伤抗性这些靠内功来的就是这一类），
    /// 而 `attr:` 类加成（例如某部内功给悟性 +5）要等七维配齐、走共用管线才生效——
    /// 这条差异会在 `_warn_derived_path` 里点名，不让它静默。
    /// </summary>
    private Godot.Collections.Dictionary _stats_from_derived(EnemyBaseRow row, DifficultyConfigRow difficulty)
    {
        // ① 旧派生列（**未乘难度**的基础值）
        var stats = new Godot.Collections.Dictionary
        {
            { "hp_max", (double)row.hp_base },
            { "qi_max", 0.0 },
            { "atk_phys", row.atk_phys },
            { "atk_qi", row.atk_qi },
            { "def_phys", row.def_phys },
            { "def_qi", row.def_qi },
            { "speed", row.speed },
            { "hit_rate", row.hit_rate },
            { "dodge_rate", row.dodge_rate },
            { "crit_rate", row.crit_rate },
            { "poise_break", 0.0 },
            { "hp_regen", 0.0 },
            { "qi_regen", 0.0 },
            { "res_internal", row.res_internal },
            // 敌方不穿透也不格挡（`enemy_base` 没有这两列；玩家的穿透与格挡先在玩家侧生效）
            { "pen_rate", 0.0 },
            { "block_rate", 0.0 },
            { "block_reduction", 0.0 },
            { "dmg_reduction", 0.0 },
        };
        // ② 装备/内功的 `stat:` 类加成 → **加在基础值上**（设计 10 §二：难度倍率照旧在最后乘）
        foreach (Variant contrib_variant in enemy_contributions(row.enemy_id))
        {
            Godot.Collections.Dictionary contrib = contrib_variant.AsGodotDictionary();
            if (_text(contrib, "kind") != AttributeCalculator.contrib_stat_flat())
            {
                continue;
            }

            string target = _text(contrib, "target");
            if (target.Length == 0)
            {
                continue;
            }

            double base_value = stats.ContainsKey(target) ? stats[target].AsDouble() : 0.0;
            stats[target] = base_value + _number(contrib, "value");
        }

        // ③ 难度倍率最后乘（只作用于 hp／内外功攻击／内外防，与旧口径一致）
        stats["hp_max"] = (double)_roundi(stats["hp_max"].AsDouble() * difficulty.enemy_hp_mul);
        stats["atk_phys"] = stats["atk_phys"].AsDouble() * difficulty.enemy_atk_mul;
        stats["atk_qi"] = stats["atk_qi"].AsDouble() * difficulty.enemy_atk_mul;
        stats["def_phys"] = stats["def_phys"].AsDouble() * difficulty.enemy_def_mul;
        stats["def_qi"] = stats["def_qi"].AsDouble() * difficulty.enemy_def_mul;
        _warn_derived_path(row.enemy_id);
        return stats;
    }

    /// <summary>
    /// 共用管线（设计 10 §二／§五／§七）：**七维 + 装备加成 + 内功加成** → `attr_to_stat`
    /// → 派生数值；难度倍率照旧在最后乘。与角色走的是同一个 `AttributeCalculator`，
    /// **只差最后那个 `include_level_base=false`**（0.28.0 答 Q57：敌人不吃 `level_growth` 的基础列）。
    /// </summary>
    private Godot.Collections.Dictionary _stats_from_attrs(
        EnemyBaseRow row, Godot.Collections.Dictionary attrs, DifficultyConfigRow difficulty)
    {
        Godot.Collections.Array contributions = enemy_contributions(row.enemy_id);
        // 内伤抗性这一列**保留**（设计只取消了毒/火/流血三条特权列），走固定值层进管线
        if (row.res_internal != 0.0)
        {
            contributions.Add(new Godot.Collections.Dictionary
            {
                { "kind", AttributeCalculator.contrib_stat_flat() },
                { "target", "res_internal" },
                { "value", row.res_internal },
                { "source", "enemy_base.res_internal" },
            });
        }

        AttributeCalculator calculator = AttributeCalculator.create(_db);
        Godot.Collections.Dictionary derived = calculator.compute(
            row.level, attrs, new Godot.Collections.Dictionary(), contributions, false);
        var stats = new Godot.Collections.Dictionary
        {
            { "hp_max", (double)_roundi(_number(derived, "hp_max") * difficulty.enemy_hp_mul) },
            { "qi_max", _number(derived, "qi_max") },
            { "atk_phys", _number(derived, "atk_phys") * difficulty.enemy_atk_mul },
            { "atk_qi", _number(derived, "atk_qi") * difficulty.enemy_atk_mul },
            { "def_phys", _number(derived, "def_phys") * difficulty.enemy_def_mul },
            { "def_qi", _number(derived, "def_qi") * difficulty.enemy_def_mul },
        };
        // 其余派生数值（身法／命中／闪避／暴击／回气／三种抗性…）照抄：它们不吃难度倍率
        foreach (Variant stat_key in derived.Keys)
        {
            string stat_id = stat_key.AsString();
            if (stats.ContainsKey(stat_id))
            {
                continue;
            }

            stats[stat_id] = derived[stat_key].AsDouble();
        }

        foreach (string extra in new[] { "pen_rate", "block_rate", "block_reduction", "dmg_reduction" })
        {
            if (!stats.ContainsKey(extra))
            {
                stats[extra] = 0.0;
            }
        }

        return stats;
    }

    /// <summary>
    /// 敌人的**装备**与**内功**加成，都走与角色相同的贡献通道：
    ///   · `enemy_equip` → `Inventory.equipment_contributions`（含装备自带的常驻 buff）
    ///   · `enemy_passive` → `GrowthCalculator.passive_contributions`（内功的 `stat:`／`attr:` 加成，
    ///     **三种元素抗性也从这儿来**——设计 10 §二取消了那三条特权列，改为「装内功才有」）
    ///
    /// 原本是 `_enemy_contributions`（私有）；**GDScript 读不到 C# 的 `private`**（施工单坑㊻，
    /// `tests/test_enemy_template.gd` 要直接调它做「装备／内功加成进了管线」那几条断言），
    /// 所以开成 public、去掉下划线。
    /// </summary>
    public Godot.Collections.Array enemy_contributions(string enemy_id)
    {
        var out_contributions = new Godot.Collections.Array();
        if (string.IsNullOrEmpty(enemy_id))
        {
            return out_contributions;
        }

        BuffService buff_service = BuffService.create(_db);
        foreach (Variant entry in _db.rows_where("enemy_equip", "enemy_id", enemy_id))
        {
            string equip_id = (entry.AsGodotObject() as EnemyEquipRow)?.equip_id ?? "";
            if (equip_id.Length == 0)
            {
                continue;
            }

            out_contributions.AddRange(
                Inventory.equipment_contributions(
                    _db, equip_id, $"enemy_equip:{equip_id}", buff_service));
        }

        var passives = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _db.rows_where("enemy_passive", "enemy_id", enemy_id))
        {
            string skill_id = (entry.AsGodotObject() as EnemyPassiveRow)?.skill_id ?? "";
            if (skill_id.Length > 0)
            {
                passives.Add(skill_id);
            }
        }

        if (passives.Count > 0)
        {
            // `mastery_levels` 传空：敌人没有熟练度（与构建期校验同口径）
            out_contributions.AddRange(
                GrowthCalculator.create(_db)
                    .passive_contributions(passives.ToArray(), new Godot.Collections.Dictionary())
            );
        }

        return out_contributions;
    }

    /// <summary>「这几行还在用旧派生列」——每个进程只报一次（自检/构建日志里看得见）。</summary>
    private void _warn_derived_path(string enemy_id)
    {
        if (_derived_path_warned)
        {
            return;
        }

        _derived_path_warned = true;
        var pending = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _db.rows("enemy_base"))
        {
            var row = entry.AsGodotObject() as EnemyBaseRow;
            if (row != null && row.attr_map().Count == 0)
            {
                pending.Add(row.enemy_id);
            }
        }

        GD.PushWarning(
            $"[EnemyFactory] {enemy_id} 还在用旧派生列（enemy_base 的 attr_* 七维还没配）——"
                + "这些行按 10 §三的过渡口径跑；配齐七维后自动切到与角色同一条管线。"
                + $"待配：{string.Join("、", pending)}"
        );
    }

    /// <summary>
    /// 一个敌人的招式清单（`enemy_skill.csv`，按 `sort_order` 排）。
    ///
    /// 表里没有这个敌人（正常不该发生，构建期会点名「一招都出不了」）时返回空——
    /// 空手敌人不会静默消失：战斗里它会走「无可用招式，跳过」并写进战报（`BattleSimulator.auto_act`）。
    ///
    /// 返回普通 `string[]`：GDScript 侧当 `PackedStringArray` 收（坑59）。
    /// </summary>
    public string[] skills_of(string enemy_id)
    {
        var rows = new System.Collections.Generic.List<EnemySkillRow>();
        foreach (Variant entry in _db.rows("enemy_skill"))
        {
            var row = entry.AsGodotObject() as EnemySkillRow;
            if (row != null && row.enemy_id == enemy_id)
            {
                rows.Add(row);
            }
        }

        rows.Sort((left, right) => left.sort_order.CompareTo(right.sort_order));
        var out_skills = new System.Collections.Generic.List<string>();
        foreach (EnemySkillRow row in rows)
        {
            out_skills.Add(row.skill_id);
        }

        return out_skills.ToArray();
    }

    /// <summary>按 `enemy_team` 生成整队，返回 `BattleActor` 数组。</summary>
    public Godot.Collections.Array create_team(string team_id, string difficulty_id)
    {
        var team = _db.get_row("enemy_team", team_id) as EnemyTeamRow;
        if (team == null)
        {
            GD.PushError($"[EnemyFactory] enemy_team 缺少 {team_id}");
            return new Godot.Collections.Array();
        }

        var out_actors = new Godot.Collections.Array();
        foreach (Variant member_variant in team.parsed_members())
        {
            Godot.Collections.Dictionary member = member_variant.AsGodotDictionary();
            string enemy_id = member["enemy_id"].AsString();
            int count = member["count"].AsInt32();
            for (int index = 0; index < count; index++)
            {
                GodotObject actor = create_enemy(enemy_id, difficulty_id);
                if (actor == null)
                {
                    continue;
                }

                actor.Set("actor_id", $"{enemy_id}#{index + 1}");
                out_actors.Add(actor);
            }
        }

        return out_actors;
    }

    /// <summary>小队涉及的全部掉落组（去重），打完后交给 `DropResolver` 结算。</summary>
    public string[] team_drop_groups(string team_id)
    {
        var team = _db.get_row("enemy_team", team_id) as EnemyTeamRow;
        if (team == null)
        {
            GD.PushError($"[EnemyFactory] enemy_team 缺少 {team_id}");
            return System.Array.Empty<string>();
        }

        var out_groups = new System.Collections.Generic.List<string>();
        foreach (Variant member_variant in team.parsed_members())
        {
            Godot.Collections.Dictionary member = member_variant.AsGodotDictionary();
            var row = _db.get_row("enemy_base", member["enemy_id"].AsString()) as EnemyBaseRow;
            if (row == null)
            {
                continue;
            }

            if (row.drop_group.Length == 0 || out_groups.Contains(row.drop_group))
            {
                continue;
            }

            out_groups.Add(row.drop_group);
        }

        return out_groups.ToArray();
    }

    /// <summary>威胁标示统计，用于明雷颜色环与调试输出。</summary>
    public string[] threat_tags(Godot.Collections.Array actors)
    {
        var out_tags = new System.Collections.Generic.List<string>();
        foreach (Variant actor_variant in actors)
        {
            GodotObject actor = actor_variant.AsGodotObject();
            Godot.Collections.Dictionary tags = actor.Get("tags").AsGodotDictionary();
            string tag = tags.ContainsKey("threat_tag") ? tags["threat_tag"].AsString() : "";
            if (tag.Length > 0 && !out_tags.Contains(tag))
            {
                out_tags.Add(tag);
            }
        }

        return out_tags.ToArray();
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>对位 GDScript 的 `contrib.get("kind", "")`：缺键给默认值（C# 直接取会抛）。</summary>
    private static string _text(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsString() : "";

    /// <summary>对位 GDScript 的 `derived.get("value", 0.0)`。</summary>
    private static double _number(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    /// <summary>
    /// 对位 GDScript 的 `roundi()`：**四舍五入、.5 一律向绝对值大的一边**。
    /// C# 的 `Math.Round` 默认是银行家舍入，差一个数就会让血量少 1（《GrowthCalculator》同一处坑）。
    /// </summary>
    private static int _roundi(double value) =>
        (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
}
