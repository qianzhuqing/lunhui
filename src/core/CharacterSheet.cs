using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 角色面板的数据集合（C# 版，对位原 `src/core/character_sheet.gd`）。
///
/// 把「模板 + 加点 + 装备」合成面板要显示的一切：五维、派生数值、非战斗技能与判定值、
/// 装备槽位视图、招式列表、未分配点数。界面只读它，不自己算数。
///
/// 判定值口径按 docs/design/01_角色系统.md：**只用裸属性（模板 + 加点，不含装备）**，
/// `attr:x` 取裸值，`skill:y` = 技能等级 + floor(相关裸属性 / 5)。
///
/// **跨语言面**：这一批是 core 的汇总口——`GameState` 还是 GDScript（`game_state.gd`），
/// 对它的字段／方法一律 `Get`／`Call`；`Inventory`／`SkillLoadout`／`AttributeCalculator`／
/// `GrowthCalculator`／`SetService`／`BuffService`／`AffixRoller`／`TalentService`／`StoryService`
/// 都已经是 C#，直调（语言边界只剩 `GameState` 这一层，坑⑥／坑67）。
///
/// `char_id`／`template`／`level` 是 `[Export]` 公开属性：`PartyBuilder.cs` 直接读
/// `sheet.template`／`sheet.level`，`character_screen.gd` 读 `panel.char_id`
/// （坑⑩：C# 的普通公开属性不进脚本 API）。
/// </summary>
public partial class CharacterSheet : RefCounted
{
    /// <summary>非战斗技能的判定缩放：floor(裸属性 / 5)。</summary>
    public const int EVENT_SKILL_ATTR_DIVISOR = 5;

    /// <summary>面板的战斗属性分组（覆盖 stat_def 全部 22 项）。</summary>
    public static readonly Godot.Collections.Dictionary STAT_GROUPS = new()
    {
        {
            "攻击",
            new Godot.Collections.Array
            {
                "atk_phys", "atk_qi", "pen_rate", "crit_rate", "crit_dmg", "poise_break",
                "debuff_power", "debuff_chance",
            }
        },
        {
            "防御",
            new Godot.Collections.Array
            {
                "def_phys", "def_qi", "hp_max", "block_rate", "block_reduction", "dmg_reduction",
                "dodge_rate", "res_internal",
            }
        },
        {
            "资源与行动",
            new Godot.Collections.Array { "qi_max", "hp_regen", "qi_regen", "speed", "hit_rate", "drop_rate" }
        },
        { "成长", new Godot.Collections.Array { "slot_active", "passive_capacity", "mastery_gain" } },
    };

    /// <summary>角色 id（面板每一处查询都用它）。</summary>
    [Export]
    public string char_id { get; set; } = "";

    /// <summary>`character_base` 的那一行（模板无效时为 null）。</summary>
    [Export]
    public CharacterBaseRow template { get; set; }

    /// <summary>等级取自存档（没有会话状态时按 1 级算）。</summary>
    [Export]
    public int level { get; set; } = 1;

    private readonly TableDb _db;
    private readonly GodotObject _state;
    private readonly AttributeCalculator _calculator;
    private readonly GrowthCalculator _growth;
    private readonly SkillLoadout _loadout;

    public CharacterSheet(TableDb table_db, GodotObject game_state, string member_id)
    {
        _db = table_db;
        _state = game_state;
        char_id = member_id;
        template = _db?.get_row("character_base", char_id) as CharacterBaseRow;
        level = _state != null ? _state.Call("level_of", char_id).AsInt32() : 1;
        if (_db != null)
        {
            _calculator = AttributeCalculator.create(_db);
            _growth = GrowthCalculator.create(_db);
        }
        _loadout = SkillLoadout.create(_db, _state, char_id);
        // 门槛与槽位判定复用面板这份属性，两边不会各算一套
        if (_loadout != null)
        {
            _loadout.attrs_provider = new Callable(this, "loadout_attrs");
        }
    }

    /// <summary>带参构造走静态工厂：GDScript 一律 `create(...)`（C# 构造对 GDScript 不可见）。</summary>
    public static CharacterSheet create(TableDb table_db, GodotObject game_state, string member_id) =>
        new(table_db, game_state, member_id);

    // ------------------------------------------------------------------ 基本

    /// <summary>武学装配（学 + 装）。界面与战斗都从这里走，别在别处重算槽位。</summary>
    public SkillLoadout loadout() => _loadout;

    public Godot.Collections.Dictionary slot_summary() => _loadout != null
        ? _loadout.slot_summary()
        : new Godot.Collections.Dictionary
        {
            { "active_used", 0 },
            { "active_slots", 0 },
            { "passive_used", 0 },
            { "passive_capacity", 0 },
        };

    public bool valid() => template != null;

    public string display_name() => template != null ? template.name_cn : char_id;

    public string role_tag() => template != null ? template.role_tag : "";

    public string weapon_type_id() => template != null ? template.weapon_type : "";

    public string weapon_type_name()
    {
        Resource row = _db?.get_row("weapon_type_def", weapon_type_id());
        return row is WeaponTypeDefRow typed ? typed.name_cn : weapon_type_id();
    }

    public string template_desc() => template != null ? template.desc : "";

    // ------------------------------------------------------------------ 属性

    public Godot.Collections.Dictionary base_attrs() =>
        template != null ? template.initial_attrs() : new Godot.Collections.Dictionary();

    public Godot.Collections.Dictionary allocations() =>
        _state != null ? _state.Call("allocations_of", char_id).AsGodotDictionary() : new();

    /// <summary>图鉴奖励（设计 01「收集本身也是成长」）：每收集 `codex_step` 部武学，七项属性各 +`codex_bonus`，
    /// 最多 `codex_cap` 次。常数在 `growth_const`、公式在 `GrowthCalculator`——这里只负责**接进属性合成**。
    ///
    /// 口径（设计没写细节，按「与加点同类的永久成长」处理）：
    ///   · 进战斗属性（`contributions()` → `stats()`）
    ///   · 进判定值（`naked_attrs()` 也算它——判定只排除「装备」，收集成长不是装备）
    ///   · 进门槛与槽位（`loadout_attrs()` 也算它——上限只有 +10，不像内功那样自我循环）
    /// </summary>
    public Godot.Collections.Dictionary codex_bonus() =>
        _growth != null
            ? _growth.codex_bonus(collected_skill_count(), _codex_multiplier())
            : new Godot.Collections.Dictionary();

    /// <summary>天赋「藏书癖」（`rule:codex_bonus_multiplier`）：把图鉴每一档的奖励翻倍。
    /// 表里那一条写的是**倍数**（2 = 翻倍），不是增量——所以缺行（0）时按 1 倍算。</summary>
    private double _codex_multiplier()
    {
        double raw = TalentService.rule_value(_db, _state, char_id, "codex_bonus_multiplier", 0.0);
        return raw > 0.0 ? raw : 1.0;
    }

    public int collected_skill_count() =>
        _state != null ? _state.Call("collected_skill_count").AsInt32() : 0;

    /// <summary>图鉴奖励转成属性点层贡献；换算在 `GrowthCalculator.codex_contributions()` 里只写一份。</summary>
    public Godot.Collections.Array codex_contributions() =>
        _growth != null
            ? _growth.codex_contributions(collected_skill_count(), _codex_multiplier())
            : new Godot.Collections.Array();

    /// <summary>抉择的永久增益（设计 20 §八，0.29.1）：`kind=choice` 的剧情节点发的资质／派生上限。
    /// 与图鉴奖励**同一条路**（属性点层／固定值层），也进判定与门槛——它是"永久成长"，
    /// 不是装备（装备会被换掉、还能卖，"永久"就没了）。</summary>
    public Godot.Collections.Array story_contributions() =>
        StoryService.permanent_contributions(_db, _state, char_id);

    /// <summary>裸属性：模板 + 加点 + 图鉴奖励，**不含装备**（事件判定与面板上的「裸值」都用它）。</summary>
    public Godot.Collections.Dictionary naked_attrs() =>
        _calculator.attr_totals_of(base_attrs(), allocations(), _permanent_growth_contributions());

    /// <summary>「永久成长」那一类贡献：图鉴奖励 ＋ 抉择增益。两样都不是装备，判定与门槛都算它们。</summary>
    private Godot.Collections.Array _permanent_growth_contributions()
    {
        Godot.Collections.Array out_list = codex_contributions();
        out_list.AddRange(story_contributions());
        return out_list;
    }

    /// <summary>含装备属性点的五维合计（面板五维显示用）。</summary>
    public Godot.Collections.Dictionary total_attrs() =>
        _calculator.attr_totals_of(base_attrs(), allocations(), contributions());

    /// <summary>武学判定用的属性：模板 + 加点 + 装备，**不含已装内功**。
    /// 内功自己给悟性／根骨的话，「装内功 → 槽位涨 → 又能装一部」会变成顺序游戏（决策 22），
    /// 所以门槛与槽位都按这份算；内功加成只进派生数值。</summary>
    public Godot.Collections.Dictionary loadout_attrs()
        => _loadout_attrs_of(_inventory());

    /// <summary>同上，但装备那一层按**指定的背包**算（「换上之后」的预演传副本）。</summary>
    private Godot.Collections.Dictionary _loadout_attrs_of(Inventory inventory)
    {
        var equipment_only = new Godot.Collections.Array();
        if (_state != null && inventory != null)
        {
            equipment_only = inventory.contributions_for(_db, char_id);
        }
        equipment_only.AddRange(_permanent_growth_contributions());
        // 天赋是**创建时就定死的**（不像内功／套装那样能反复穿脱），所以放进门槛与槽位这一份，
        // 「悟性 +3」「内功容量 +1」这类天赋才真的抬得动招式槽与内功容量。
        equipment_only.AddRange(TalentService.contributions(_db, _state, char_id));
        return _calculator.attr_totals_of(base_attrs(), allocations(), equipment_only);
    }

    public Godot.Collections.Array contributions()
    {
        Inventory inventory = _state != null ? _state.Get("inventory").As<Inventory>() : null;
        if (_state == null || inventory == null)
        {
            return _loadout != null ? _loadout.contributions() : new Godot.Collections.Array();
        }
        Godot.Collections.Array out_list = inventory.contributions_for(_db, char_id);
        // 内功「装备即生效」：装配的内功加成走同一条贡献通道，属性点层与固定值层已分开
        out_list.AddRange(_loadout.contributions());
        // 套装档位（08）：只算已装备／已装配的成员，档位向下兼容
        out_list.AddRange(set_contributions());
        // 图鉴奖励也走同一条通道（属性点层）
        out_list.AddRange(codex_contributions());
        // 抉择的永久增益（设计 20 §八）：与图鉴同一条通道
        out_list.AddRange(story_contributions());
        // 天赋（0.16.0）：`attr:`／`stat:` 两类效果走同一条贡献通道
        out_list.AddRange(TalentService.contributions(_db, _state, char_id));
        return out_list;
    }

    /// <summary>生效的套装档位 buff 转成贡献。
    ///
    /// **故意不进 `loadout_attrs()`**：套装按「装配了几招／几格」计数，如果它的加成又回头影响
    /// 招式槽与内功容量，就变成「装上去→属性涨→又能装」的顺序游戏（与内功同一条纪律，见决策 22）。
    /// </summary>
    public Godot.Collections.Array set_contributions()
        => _set_contributions_of(_inventory());

    /// <summary>当前这一局的背包（没有局就是 null）。</summary>
    private Inventory _inventory() => _state != null ? _state.Get("inventory").As<Inventory>() : null;

    /// <summary>
    /// 「套装档位 → 贡献」的**唯一实现**，按传进来的背包算。
    ///
    /// 平时传真背包（`set_contributions()`）；「换上之后」的预演传的是**背包副本**
    /// （见 `preview_swap_stats`）——套装数的是「身上穿着几件成员」，预演要是不跟着副本走，
    /// 「这一件正好凑齐一套」那一档就会漏掉。
    /// </summary>
    private Godot.Collections.Array _set_contributions_of(Inventory inventory)
    {
        if (_state == null || inventory == null)
        {
            return new Godot.Collections.Array();
        }
        SetService sets = SetService.create(_db);
        BuffService buffs_service = BuffService.create(_db);
        var out_list = new Godot.Collections.Array();
        foreach (Variant row_variant in sets.all_defs())
        {
            var row = row_variant.As<SetDefRow>();
            string set_id = row.set_id;
            // `SetService` 已经是 C#，`count_for()` 的返回值没有静态类型，`:=` 推不出来——写死类型
            int count = sets.count_for(set_id, char_id, inventory, _state);
            if (count <= 0)
            {
                continue;
            }
            foreach (string buff_id in sets.active_buff_ids(set_id, count))
            {
                // 套装档位的 buff 不叠层（每档发一次），所以 stacks 传 1
                out_list.AddRange(buffs_service.contributions_of(buff_id, $"set:{set_id}", 1));
            }
        }
        return out_list;
    }

    /// <summary>全部派生数值：四层公式 + 装备加成。
    /// slot_active / passive_capacity 不来自 attr_to_stat，由 growth_const 公式算出来合并进来。</summary>
    public Godot.Collections.Dictionary stats()
    {
        Godot.Collections.Dictionary computed =
            _calculator.compute(level, base_attrs(), allocations(), contributions(), true);
        // 槽位那两个数必须与装配判定的口径一致，否则面板会显示一个装不下的容量
        Godot.Collections.Dictionary derived = _growth.derived_stats(level, loadout_attrs());
        foreach (Variant key in derived.Keys)
        {
            computed[key] = derived[key];
        }
        return computed;
    }

    /// <summary>招式槽上限（个数）与内功容量（容量点）</summary>
    public int active_slots() => _growth.active_slots(level, total_attrs());

    /// <summary>气血上限（走同一份派生数值，别另算一套）</summary>
    public int max_hp()
    {
        Godot.Collections.Dictionary computed = stats();
        double value = computed.ContainsKey("hp_max") ? computed["hp_max"].AsDouble() : 0.0;
        return System.Math.Max(1, (int)value);
    }

    /// <summary>当前气血（战斗外气血 v11 起进存档；没有记录 = 满血）</summary>
    public int current_hp()
    {
        if (_state == null)
        {
            return max_hp();
        }
        int saved = _state.Call("current_hp_of", char_id).AsInt32();
        if (saved < 0)
        {
            return max_hp();
        }
        return System.Math.Clamp(saved, 1, max_hp());
    }

    public int passive_capacity() => _growth.passive_capacity(level, total_attrs());

    /// <summary>武学熟练度（0~mastery_max，存在存档里）</summary>
    public int mastery_of(string skill_id) =>
        _state != null ? _state.Call("mastery_of", char_id, skill_id).AsInt32() : 0;

    public double stat_value(string stat_id)
    {
        Godot.Collections.Dictionary computed = stats();
        return computed.ContainsKey(stat_id) ? computed[stat_id].AsDouble() : 0.0;
    }

    /// <summary>显示用文本：百分比类乘 100 加 %，其余按 show_decimals 取整。</summary>
    public string stat_label(string stat_id) => format_stat_value(stat_id, stat_value(stat_id));

    /// <summary>
    /// 把**任意一个**派生数值写成显示文本（百分比乘 100 加 %、其余按 `show_decimals` 取整）。
    /// 单拎出来是因为「换上之后」要写的是**预演值**，不是当前值——格式只能有这一处，
    /// 否则面板上同一个数会出现两种写法（`stat_label()` 也走它）。
    /// </summary>
    public string format_stat_value(string stat_id, double value)
    {
        var row = _db.get_row("stat_def", stat_id) as StatDefRow;
        if (row == null)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (row.is_percent)
        {
            return $"{value * 100.0:0.0}%";
        }
        if (row.show_decimals > 0)
        {
            return $"{value:0.0}";
        }
        return ((int)value).ToString();
    }

    public string stat_name(string stat_id)
    {
        Resource row = _db.get_row("stat_def", stat_id);
        return row is StatDefRow typed ? typed.name_cn : stat_id;
    }

    public string attribute_name(string attr_id)
    {
        Resource row = _db.get_row("attribute_def", attr_id);
        return row is AttributeDefRow typed ? typed.name_cn : attr_id;
    }

    // ------------------------------------------------------------------ 非战斗技能与判定

    public int event_skill_level(string skill_id)
    {
        foreach (Variant row_variant in _db.rows("character_base_skill"))
        {
            var row = row_variant.As<CharacterBaseSkillRow>();
            if (row.char_id == char_id && row.skill_id == skill_id)
            {
                return row.level;
            }
        }
        return 0;
    }

    /// <summary>面板上的非战斗技能行：技能名、等级、关联属性、判定值。</summary>
    public Godot.Collections.Array event_skill_rows()
    {
        var out_rows = new Godot.Collections.Array();
        foreach (Variant row_variant in _db.rows("event_skill_def"))
        {
            var row = row_variant.As<EventSkillDefRow>();
            string skill_id = row.skill_id;
            string related = row.related_attr;
            Godot.Collections.Dictionary check = event_check_value($"skill:{skill_id}");
            out_rows.Add(new Godot.Collections.Dictionary
            {
                { "skill_id", skill_id },
                { "name", row.name_cn },
                { "level", event_skill_level(skill_id) },
                { "max_level", row.max_level },
                { "related_attr", related },
                { "related_attr_name", attribute_name(related) },
                { "check_value", check.ContainsKey("value") ? check["value"].AsInt32() : 0 },
            });
        }
        return out_rows;
    }

    /// <summary>判定值。返回 {kind, target_id, value, naked}</summary>
    public Godot.Collections.Dictionary event_check_value(string source)
    {
        string[] parts = source.Split(':', System.StringSplitOptions.None);
        string kind = parts.Length > 0 ? parts[0] : "";
        string target_id = parts.Length > 1 ? parts[1] : "";
        Godot.Collections.Dictionary naked = naked_attrs();
        if (kind == "attr")
        {
            double raw = naked.ContainsKey(target_id) ? naked[target_id].AsDouble() : 0.0;
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "target_id", target_id },
                { "naked", true },
                // 判定 +1 那类天赋（见多识广）对**所有**判定都算，属性判定也在内
                { "value", (int)System.Math.Floor(raw) + TalentService.check_bonus(_db, _state, char_id, "") },
            };
        }
        if (kind == "skill")
        {
            var skill = _db.get_row("event_skill_def", target_id) as EventSkillDefRow;
            string related = skill != null ? skill.related_attr : "";
            double raw = naked.ContainsKey(related) ? naked[related].AsDouble() : 0.0;
            int bonus = (int)System.Math.Floor(raw / EVENT_SKILL_ATTR_DIVISOR);
            return new Godot.Collections.Dictionary
            {
                { "kind", kind },
                { "target_id", target_id },
                // 天赋的判定加成（12 §六）：通用 +1 与「只加这一门」（文学／医术各 +2）
                {
                    "value",
                    event_skill_level(target_id) + bonus
                        + TalentService.check_bonus(_db, _state, char_id, target_id)
                },
                { "naked", true },
            };
        }
        return new Godot.Collections.Dictionary
        {
            { "kind", kind }, { "target_id", target_id }, { "value", 0 }, { "naked", true },
        };
    }

    // ------------------------------------------------------------------ 装备与招式

    /// <summary>槽位视图：slot_id → Array（长度 = max_equip），每格是
    /// {slot_id, index, empty, instance_id, base, name, rarity_color, summary, level_req}</summary>
    public Godot.Collections.Array equipped_rows()
    {
        var out_rows = new Godot.Collections.Array();
        Inventory inventory = _state != null ? _state.Get("inventory").As<Inventory>() : null;
        if (_state == null || inventory == null)
        {
            return out_rows;
        }
        Godot.Collections.Dictionary slots = inventory.equipment_slots(_db, char_id);
        foreach (Variant slot_variant in _db.rows("equip_slot_def"))
        {
            var slot = slot_variant.As<EquipSlotDefRow>();
            string slot_id = slot.slot_id;
            Godot.Collections.Array entries = slots.ContainsKey(slot_id)
                ? slots[slot_id].AsGodotArray()
                : new Godot.Collections.Array { "" };
            for (int index = 0; index < entries.Count; index++)
            {
                string instance_id = entries[index].AsString();
                EquipBaseRow base_row = null;
                if (instance_id.Length > 0)
                {
                    base_row = _db.get_row("equip_base", inventory.base_of(instance_id)) as EquipBaseRow;
                }
                bool empty = instance_id.Length == 0 || base_row == null;
                out_rows.Add(new Godot.Collections.Dictionary
                {
                    { "slot_id", slot_id },
                    { "slot_name", slot.name_cn },
                    { "index", index },
                    { "empty", empty },
                    { "instance_id", instance_id },
                    { "base", base_row },
                    { "name", base_row != null ? base_row.name_cn : "空" },
                    { "rarity_color", base_row != null ? rarity_color(base_row.rarity) : "" },
                    { "summary", equipment_summary(base_row) },
                    { "affix_summary", affix_summary_of(instance_id) },
                    { "level_req", base_row != null ? base_row.level_req : 0 },
                });
            }
        }
        return out_rows;
    }

    /// <summary>
    /// 「换上之后」的预演（UI 清单 §二-5：选中候选后即时重算，与面板同一个 `AttributeCalculator`）。
    ///
    /// **做法：在背包的一份副本上真做一遍换装，再按副本算一遍全量数值。** 不手工「减掉这一件、
    /// 加上那一件」，是因为套装（08）数的是「身上穿着几件成员」——候选正好凑齐一套时，
    /// 手工替换会漏掉那一档，预演就成了瞎话。副本走 `to_dict()`／`from_dict()`（与存读档同一条路），
    /// 用完即弃，**不动真存档**。
    ///
    /// `candidate_instance_id` 给空串＝只看「卸下这一格之后」。
    /// </summary>
    public Godot.Collections.Dictionary preview_swap_stats(
        string slot_id, int index, string candidate_instance_id)
    {
        Inventory inventory = _inventory();
        if (inventory == null || slot_id.Length == 0)
        {
            return new Godot.Collections.Dictionary();
        }

        Inventory trial = Inventory.from_dict(inventory.to_dict());
        if (trial == null)
        {
            return new Godot.Collections.Dictionary();
        }

        trial.unequip(char_id, slot_id, index);
        if (candidate_instance_id.Length > 0)
        {
            // 装不上（等级／武器不符）时 `equip()` 返回 ok=false、什么也不动——预演就退回「卸下之后」。
            // 界面上那件候选本来就置灰（原因出自同一份 `can_equip`），这里不重复报一次错。
            trial.equip(_db, char_id, level, weapon_type_id(), candidate_instance_id);
        }

        var contributions = new Godot.Collections.Array();
        contributions.AddRange(trial.contributions_for(_db, char_id));
        contributions.AddRange(_loadout != null ? _loadout.contributions() : new Godot.Collections.Array());
        contributions.AddRange(_set_contributions_of(trial));
        contributions.AddRange(codex_contributions());
        contributions.AddRange(story_contributions());
        contributions.AddRange(TalentService.contributions(_db, _state, char_id));
        Godot.Collections.Dictionary computed =
            _calculator.compute(level, base_attrs(), allocations(), contributions, true);
        // 槽位／内功容量那两个数走另一条公式（与 `stats()` 同一处口径）：装备带属性点时它们也会动
        Godot.Collections.Dictionary derived = _growth.derived_stats(level, _loadout_attrs_of(trial));
        foreach (Variant key in derived.Keys)
        {
            computed[key] = derived[key];
        }

        return computed;
    }

    /// <summary>这件装备的随机词条文案（空串 = 没有词条）</summary>
    public string affix_summary_of(string instance_id)
    {
        Inventory inventory = _state != null ? _state.Get("inventory").As<Inventory>() : null;
        if (_state == null || inventory == null || instance_id.Length == 0)
        {
            return "";
        }
        return AffixRoller.summarize(_db, inventory.affixes_of(instance_id));
    }

    /// <summary>装备的关键属性摘要（前几条，供槽位与背包列表显示）。</summary>
    public string equipment_summary(EquipBaseRow base_row)
    {
        if (base_row == null)
        {
            return "";
        }
        var parts = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary attr_bonuses = base_row.attr_bonuses();
        foreach (Variant attr_id in attr_bonuses.Keys)
        {
            int value = attr_bonuses[attr_id].AsInt32();
            if (value != 0)
            {
                parts.Add($"{attribute_name(attr_id.AsString())}+{value}");
            }
        }
        Godot.Collections.Dictionary stat_bonuses = base_row.stat_bonuses();
        foreach (Variant stat_id in stat_bonuses.Keys)
        {
            double value = stat_bonuses[stat_id].AsDouble();
            if (value == 0.0)
            {
                continue;
            }
            var row = _db.get_row("stat_def", stat_id.AsString()) as StatDefRow;
            if (row != null && row.is_percent)
            {
                parts.Add($"{row.name_cn}+{value * 100.0:0.0}%");
            }
            else
            {
                parts.Add($"{(row != null ? row.name_cn : stat_id.AsString())}+{(int)value}");
            }
        }
        return parts.Count == 0 ? "无附加属性" : string.Join("、", parts);
    }

    public string rarity_color(string rarity_id)
    {
        Resource row = _db.get_row("rarity_def", rarity_id);
        return row is RarityDefRow typed ? typed.color : "#FFFFFF";
    }

    public string rarity_name(string rarity_id)
    {
        Resource row = _db.get_row("rarity_def", rarity_id);
        return row is RarityDefRow typed ? typed.name_cn : rarity_id;
    }

    public string weapon_type_name_of(EquipBaseRow base_row)
    {
        if (base_row == null || base_row.weapon_type.Length == 0)
        {
            return "";
        }
        Resource row = _db.get_row("weapon_type_def", base_row.weapon_type);
        return row is WeaponTypeDefRow typed ? typed.name_cn : base_row.weapon_type;
    }

    /// <summary>武学列表：**已学**的武学（模板起始 + 存档记录）。
    /// 每行带上星级、熟练度、熟练度倍率、是否已装配、能不能装（不能装时写清原因）；
    /// 招式附战斗数值，内功附占格与加成摘要。界面的装配按钮就用这里的 can_equip／equip_error。</summary>
    public Godot.Collections.Array skill_rows()
    {
        var out_rows = new Godot.Collections.Array();
        if (template == null)
        {
            return out_rows;
        }
        var seen = new System.Collections.Generic.HashSet<string>();
        var ids = new System.Collections.Generic.List<string>();
        foreach (string skill_id in _loadout.learned_ids())
        {
            if (seen.Add(skill_id))
            {
                ids.Add(skill_id);
            }
        }
        if (_state != null)
        {
            foreach (string skill_id in _state.Call("masteries_of", char_id).AsStringArray())
            {
                if (seen.Add(skill_id))
                {
                    ids.Add(skill_id);
                }
            }
        }
        foreach (string skill_id in ids)
        {
            if (_db.get_row("skill_base", skill_id) is not SkillBaseRow row)
            {
                continue;
            }
            var star_row = _growth.star_row(row.star) as SkillStarDefRow;
            Godot.Collections.Dictionary requirement = _loadout.requirement_of(skill_id);
            bool equipped = _has(_loadout.active_ids(), skill_id) || _has(_loadout.passive_ids(), skill_id);
            string equip_error = "";
            if (!equipped)
            {
                Godot.Collections.Dictionary can = _loadout.can_equip(skill_id);
                equip_error = can.ContainsKey("error") ? can["error"].AsString() : "";
            }
            var entry = new Godot.Collections.Dictionary
            {
                { "skill_id", skill_id },
                { "name", row.name_cn },
                { "kind", row.skill_kind },
                { "star", row.star },
                { "star_name", star_row != null ? star_row.name_cn : "" },
                { "star_color", star_row != null ? star_row.color : "#FFFFFF" },
                { "weapon_type", row.weapon_type },
                { "school_id", row.school_id },
                { "mastery", mastery_of(skill_id) },
                { "mastery_max", _growth.mastery_max() },
                { "mastery_multiplier", _growth.mastery_multiplier(row.star, mastery_of(skill_id)) },
                { "learn_req_attr", row.learn_req_attr },
                { "learn_req_value", row.learn_req_value },
                { "learn_req_reason", requirement.ContainsKey("reason") ? requirement["reason"].AsString() : "" },
                { "equipped", equipped },
                { "equip_error", equip_error },
                { "desc", row.desc },
            };
            if (row.is_active())
            {
                if (_db.get_row("skill_active", skill_id) is SkillActiveRow active)
                {
                    entry["element"] = active.element;
                    entry["damage_type"] = active.damage_type;
                    entry["power_ratio"] = active.power_ratio;
                    entry["qi_cost"] = active.qi_cost;
                    entry["poise_damage"] = active.poise_damage;
                    entry["hit_count"] = active.hit_count;
                    entry["target_type"] = active.target_type;
                    // 「这招在战斗里能不能用出来」：伤害型看 `is_attack()`（有伤害类型且倍率 > 0）；
                    // 增益招式（例：醉里乾坤·醉步）：没有伤害、但表里配了 `on_cast` 发放 → 战斗里能用，
                    // 施放一次给自己上 buff。判定与战斗侧同一处（`BuffService.has_cast_grant`），别各写一套。
                    bool support = !active.is_attack() && BuffService.create(_db).has_cast_grant(skill_id);
                    entry["battle_usable"] = active.is_attack() || support;
                    if (active.is_attack())
                    {
                        entry["battle_note"] = "";
                    }
                    else if (support)
                    {
                        entry["battle_note"] = "增益招式：施放时给自己上 buff";
                    }
                    else
                    {
                        entry["battle_note"] = "战斗里用不出来（效果未配）";
                    }
                }
            }
            else if (_db.get_row("skill_passive", skill_id) is SkillPassiveRow passive)
            {
                entry["slot_cost"] = passive.slot_cost;
                entry["passive_effect"] = passive.passive_effect;
                entry["bonus_summary"] = passive_bonus_summary(skill_id);
                // 表里填了「特殊效果 id」但**没有定义表、代码里也没有落点**（4 部高星内功都是这样）：
                // 面板要如实说一句，别让玩家以为「装了这部就该有个特别效果，怎么没反应」。
                // 注意**不要把 id 原样显示**（界面不许出现表内英文 id，有文案审计盯着），只说「暂未生效」。
                entry["special_effect_pending"] = passive.passive_effect.Trim().Length > 0;
            }
            out_rows.Add(entry);
        }
        return out_rows;
    }

    /// <summary>内功加成摘要，如「内力上限+30、智+5」</summary>
    public string passive_bonus_summary(string skill_id)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in _db.rows_where("skill_passive_stat", "skill_id", skill_id))
        {
            var row = row_variant.As<SkillPassiveStatRow>();
            Godot.Collections.Dictionary parsed = row.parsed_target();
            string kind = parsed.ContainsKey("kind") ? parsed["kind"].AsString() : "";
            string target_id = parsed.ContainsKey("target_id") ? parsed["target_id"].AsString() : "";
            if (kind == "attr")
            {
                parts.Add($"{attribute_name(target_id)}+{(int)row.value}");
            }
            else if (kind == "stat")
            {
                parts.Add($"{stat_name(target_id)}+{_format_plain_stat(target_id, row.value)}");
            }
        }
        return parts.Count == 0 ? "无加成" : string.Join("、", parts);
    }

    private string _format_plain_stat(string stat_id, double value)
    {
        var row = _db.get_row("stat_def", stat_id) as StatDefRow;
        if (row != null && row.is_percent)
        {
            return $"{value * 100.0:0.0}%";
        }
        if (row != null && row.show_decimals > 0)
        {
            return $"{value:0.0}";
        }
        return ((int)value).ToString();
    }

    // ------------------------------------------------------------------ 加点

    public int available_points() =>
        _state != null ? _state.Call("available_points", _db, char_id).AsInt32() : 0;

    /// <summary>加一点并返回 {ok, error, remaining}</summary>
    public Godot.Collections.Dictionary spend_point(string attr_id)
    {
        if (_state == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false }, { "error", "没有会话状态" }, { "remaining", 0 },
            };
        }
        return _state.Call("spend_point", _db, char_id, attr_id).AsGodotDictionary();
    }

    /// <summary>这个属性能不能加点；能加返回空串，不能加返回原因（设计 0.13.0：悟性／根骨是资质）。</summary>
    public string allocation_block_reason(string attr_id)
    {
        if (_state == null)
        {
            return "没有会话状态";
        }
        return _state.Call("allocation_block_reason", _db, attr_id).AsString();
    }

    private static bool _has(string[] values, string wanted) =>
        System.Array.IndexOf(values, wanted) >= 0;
}
