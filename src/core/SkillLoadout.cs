using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 武学装配：学（修习门槛）＋ 装（招式槽／内功容量）（C# 版，对位原 `src/core/skill_loadout.gd`）。
///
/// 规则按设计 01 落地，数值全在表里：
///   招式槽上限 = 2 + floor(等级/5) + floor(悟性/8)   内功容量 = 2 + floor(等级/6) + floor(根骨/10)
///   内功占格制：每部按星级占 1~3 格（`skill_passive.slot_cost`）
///   修习门槛：`skill_base.learn_req_attr` 达到 `learn_req_value`（行里没写值就取 `skill_star_def`）
///   招式绑定武器类型（`any`／留空 = 通用）：装的招式得配得上身上的武器
///
/// 口径：门槛与槽位用**含装备**的属性（与 GrowthCalculator 一致），但**不含已装内功的加成**——
/// 否则「装内功 → 属性涨 → 容量涨 → 又能装一部」会自我循环，装哪部内功就变成了顺序游戏。
///
/// **跨语言面**：`GameState`／`Inventory` 还是 GDScript，所以 `level_of`／`allocations_of`／
/// `collected_skill_count`／`learned_of`／`is_learned`／`learn_skill`／`loadout_of`／`set_loadout`／
/// `mastery_of` 走 `Call(...)`，`state.inventory` 走 `Get(...)`（施工单坑⑥／坑67）。
/// </summary>
public partial class SkillLoadout : RefCounted
{
    /// <summary>可选：面板已经把属性算好了就接进来，保证「面板显示的槽位」与这里判定的槽位是同一份数。
    /// **必须是 `[Export]` 的公开属性**：`CharacterSheet.cs` 直接写 `loadout.attrs_provider = new Callable(...)`，
    /// C# 的普通公开属性不进脚本 API（坑⑩／`MenuController.slots` 同形）。</summary>
    [Export]
    public Callable attrs_provider { get; set; } = default;

    private TableDb _db;
    private GodotObject _state;
    private string char_id = "";
    private AttributeCalculator _calculator;
    private GrowthCalculator _growth;
    private CharacterBaseRow _template;

    public SkillLoadout(TableDb table_db, GodotObject game_state, string member_id)
    {
        _db = table_db;
        _state = game_state;
        char_id = member_id;
        _template = _db?.get_row("character_base", char_id) as CharacterBaseRow;
        if (_db != null)
        {
            _calculator = AttributeCalculator.create(_db);
            _growth = GrowthCalculator.create(_db);
        }
    }

    /// <summary>带参数的构造走静态工厂（施工单「已定型的搬运形状」第 1 条）：GDScript 一律 `create(...)`。</summary>
    public static SkillLoadout create(TableDb table_db, GodotObject game_state, string member_id) =>
        new(table_db, game_state, member_id);

    public bool valid() => _db != null && _state != null && _template != null;

    public int level() => _state != null ? _state.Call("level_of", char_id).AsInt32() : 1;

    /// <summary>判定用的属性合计：模板 + 加点 + 装备（理由见文件头）。</summary>
    public Godot.Collections.Dictionary attrs()
    {
        if (_has_attrs_provider())
        {
            return attrs_provider.Call().AsGodotDictionary();
        }

        Godot.Collections.Dictionary base_attrs =
            _template != null ? _template.initial_attrs() : new Godot.Collections.Dictionary();
        Godot.Collections.Dictionary allocations = _state != null
            ? _state.Call("allocations_of", char_id).AsGodotDictionary()
            : new Godot.Collections.Dictionary();
        var contributions = new Godot.Collections.Array();
        GodotObject inventory = _state?.Get("inventory").AsGodotObject();
        if (inventory != null)
        {
            contributions = inventory.Call("contributions_for", _db, char_id).AsGodotArray();
        }

        // 图鉴奖励（收集本身也是成长）也要算进门槛与槽位：和面板那份属性同一套换算，
        // 不然会出现「面板显示够门槛、实际学不会」（这条踩过）。
        if (_state != null && _growth != null)
        {
            // 天赋「藏书癖」把每一档翻倍——**门槛这一侧也要用同一个倍数**
            double mult = TalentService.rule_value(
                _db, _state, char_id, "codex_bonus_multiplier", 0.0);
            contributions.AddRange(_growth.codex_contributions(
                _state.Call("collected_skill_count").AsInt32(), mult > 0.0 ? mult : 1.0));
        }

        return _calculator.attr_totals_of(base_attrs, allocations, contributions);
    }

    // ------------------------------------------------------------------ 已学

    public string[] learned_ids() =>
        _state != null
            ? _state.Call("learned_of", char_id).AsStringArray()
            : System.Array.Empty<string>();

    public bool is_learned(string skill_id) =>
        _state != null && _state.Call("is_learned", char_id, skill_id).AsBool();

    /// <summary>修习门槛：`{ok, attr, value, current, reason}`。</summary>
    public Godot.Collections.Dictionary requirement_of(string skill_id)
    {
        if (_db.get_row("skill_base", skill_id) is not SkillBaseRow row)
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "attr", "" },
                { "value", 0 },
                { "current", 0 },
                { "reason", $"没有这部武学：{skill_id}" },
            };
        }

        if (!row.has_learn_requirement())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", true },
                { "attr", "" },
                { "value", 0 },
                { "current", 0 },
                { "reason", "" },
            };
        }

        string attr_id = row.learn_req_attr;
        int value = row.learn_req_value;
        if (value <= 0)
        {
            Resource star = _growth.star_row(row.star);
            value = star is SkillStarDefRow star_row ? star_row.learn_req_value : 0;
        }

        // 天赋「悟剑」（设计 12：高星武学的**修习门槛 −3**）：从要求值上减，最低到 0——
        // 不减到负数（负数等于没有门槛，那是另一套语义；门槛本身还是"够属性才学得会"）
        if (value > 0 && _state != null)
        {
            value = System.Math.Max(0, value - (int)TalentService.rule_value(
                _db, _state, char_id, "learn_req_reduce", 0.0));
        }

        int current = attrs().ContainsKey(attr_id) ? attrs()[attr_id].AsInt32() : 0;
        bool ok = current >= value;
        return new Godot.Collections.Dictionary
        {
            { "ok", ok },
            { "attr", attr_id },
            { "value", value },
            { "current", current },
            { "reason", ok ? "" : $"{attribute_name(attr_id)}需 {value}（当前 {current}）" },
        };
    }

    public string attribute_name(string attr_id)
    {
        return _db.get_row("attribute_def", attr_id) is AttributeDefRow row
            ? row.name_cn
            : attr_id;
    }

    /// <summary>学一部武学。已经学过不算失败（战斗掉落会反复遇到同一个来源），返回 `{ok, new, error}`。</summary>
    public Godot.Collections.Dictionary learn(string skill_id)
    {
        if (_db.get_row("skill_base", skill_id) == null)
        {
            return _fail_learn($"没有这部武学：{skill_id}");
        }

        if (is_learned(skill_id))
        {
            return new Godot.Collections.Dictionary { { "ok", true }, { "new", false }, { "error", "" } };
        }

        Godot.Collections.Dictionary requirement = requirement_of(skill_id);
        if (!requirement["ok"].AsBool())
        {
            return _fail_learn($"修习门槛不足：{requirement["reason"].AsString()}");
        }

        _state.Call("learn_skill", char_id, skill_id);
        return new Godot.Collections.Dictionary { { "ok", true }, { "new", true }, { "error", "" } };
    }

    private static Godot.Collections.Dictionary _fail_learn(string error) => new()
    {
        { "ok", false },
        { "new", false },
        { "error", error },
    };

    // ------------------------------------------------------------------ 槽位

    public string[] active_ids() => _loadout_ids("active");

    public string[] passive_ids() => _loadout_ids("passive");

    public int active_slots() => _growth.active_slots(level(), attrs());

    public int passive_capacity() => _growth.passive_capacity(level(), attrs());

    public int active_used() => active_ids().Length;

    /// <summary>内功占格合计（不是部数）：容量点口径见 01 文档。</summary>
    public int passive_used()
    {
        int used = 0;
        foreach (string skill_id in passive_ids())
        {
            if (_db.get_row("skill_passive", skill_id) is SkillPassiveRow row)
            {
                used += row.slot_cost;
            }
        }

        return used;
    }

    /// <summary>面板顶部那行「招式 2/3　内功 1/2 格」。</summary>
    public Godot.Collections.Dictionary slot_summary() => new()
    {
        { "active_used", active_used() },
        { "active_slots", active_slots() },
        { "passive_used", passive_used() },
        { "passive_capacity", passive_capacity() },
    };

    public string equipped_weapon_type()
    {
        GodotObject inventory = _state?.Get("inventory").AsGodotObject();
        if (inventory == null)
        {
            return "";
        }

        foreach (string instance_id in inventory.Call("equipped_instances", _db, char_id).AsStringArray())
        {
            if (_db.get_row("equip_base", inventory.Call("base_of", instance_id).AsString())
                is EquipBaseRow equip
                && equip.slot == "weapon")
            {
                return equip.weapon_type;
            }
        }

        return "";
    }

    public string weapon_type_name(string weapon_type)
    {
        return _db.get_row("weapon_type_def", weapon_type) is WeaponTypeDefRow row
            ? row.name_cn
            : weapon_type;
    }

    // ------------------------------------------------------------------ 装配

    /// <summary>能不能装：`{ok, error}`。</summary>
    public Godot.Collections.Dictionary can_equip(string skill_id)
    {
        if (_db.get_row("skill_base", skill_id) is not SkillBaseRow row)
        {
            return _gate(false, $"没有这部武学：{skill_id}");
        }

        if (!is_learned(skill_id))
        {
            return _gate(false, $"还没学会（来源：{source_label(row)}）");
        }

        if (_contains(active_ids(), skill_id) || _contains(passive_ids(), skill_id))
        {
            return _gate(false, "已经装配");
        }

        if (row.is_active())
        {
            if (!accepts_current_weapon(row))
            {
                string have = equipped_weapon_type();
                return _gate(
                    false,
                    $"武器不符（需要{weapon_type_name(row.weapon_type)}，"
                    + (have.Length == 0 ? "当前没拿武器" : $"当前拿的是{weapon_type_name(have)}")
                    + "）");
            }

            if (active_used() >= active_slots())
            {
                return _gate(false, $"招式槽已满（{active_used()}/{active_slots()}）");
            }
        }
        else
        {
            var after = new System.Collections.Generic.List<string>(passive_ids()) { skill_id };
            Godot.Collections.Dictionary fit = _growth.passive_fit(after.ToArray(), level(), attrs());
            if (!fit["fits"].AsBool())
            {
                return _gate(
                    false,
                    $"内功容量不足（这部占 {passive_slot_cost(skill_id)} 格，"
                    + $"只剩 {System.Math.Max(0, passive_capacity() - passive_used())} 格）");
            }
        }

        return _gate(true, "");
    }

    public bool accepts_current_weapon(Resource row) =>
        row is SkillBaseRow skill
        && (skill.accepts_any_weapon() || skill.weapon_type == equipped_weapon_type());

    public int passive_slot_cost(string skill_id) =>
        _db.get_row("skill_passive", skill_id) is SkillPassiveRow row ? row.slot_cost : 0;

    public string source_label(Resource row)
    {
        string kind = row is SkillBaseRow skill ? skill.source_type : "";
        var names = new Godot.Collections.Dictionary
        {
            { "start", "开局自带" },
            { "npc", "门派／NPC传授" },
            { "drop", $"击败{(row is SkillBaseRow drop_row ? drop_row.source_id : "")}领悟" },
            { "story", "剧情" },
            { "hidden", "隐藏内容" },
            { "item", "秘籍" },
            { "shop", "商店" },
        };
        if (names.ContainsKey(kind))
        {
            return names[kind].AsString();
        }

        return kind.Length > 0 ? kind : "未知";
    }

    /// <summary>装上（超过容量会被拒）。返回 `{ok, error, replaced}`。</summary>
    public Godot.Collections.Dictionary equip(string skill_id)
    {
        Godot.Collections.Dictionary gate = can_equip(skill_id);
        if (!gate["ok"].AsBool())
        {
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "error", gate["error"] },
                { "replaced", "" },
            };
        }

        var row = (SkillBaseRow)_db.get_row("skill_base", skill_id);
        var active = new System.Collections.Generic.List<string>(active_ids());
        var passive = new System.Collections.Generic.List<string>(passive_ids());
        if (row.is_active())
        {
            active.Add(skill_id);
        }
        else
        {
            passive.Add(skill_id);
        }

        _set_loadout(active, passive);
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" }, { "replaced", "" } };
    }

    public Godot.Collections.Dictionary unequip(string skill_id)
    {
        var active = new System.Collections.Generic.List<string>(active_ids());
        var passive = new System.Collections.Generic.List<string>(passive_ids());
        string kind = "";
        if (active.Contains(skill_id))
        {
            active.Remove(skill_id);
            kind = "active";
        }
        else if (passive.Contains(skill_id))
        {
            passive.Remove(skill_id);
            kind = "passive";
        }

        if (kind.Length == 0)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "这部武学没有装配" } };
        }

        _set_loadout(active, passive);
        return new Godot.Collections.Dictionary { { "ok", true }, { "error", "" }, { "kind", kind } };
    }

    /// <summary>切换：装着的就卸下，没装的就装上（面板一个按钮搞定）。</summary>
    public Godot.Collections.Dictionary toggle(string skill_id)
    {
        if (_contains(active_ids(), skill_id) || _contains(passive_ids(), skill_id))
        {
            Godot.Collections.Dictionary off = unequip(skill_id);
            return new Godot.Collections.Dictionary
            {
                { "ok", off["ok"] },
                { "error", off["error"] },
                { "equipped", false },
            };
        }

        Godot.Collections.Dictionary on = equip(skill_id);
        return new Godot.Collections.Dictionary
        {
            { "ok", on["ok"] },
            { "error", on["error"] },
            { "equipped", on["ok"].AsBool() },
        };
    }

    /// <summary>按顺序把学过的都装上，能装多少装多少（新建游戏与老档迁移铺初始装配）。</summary>
    public void fill_from_learned()
    {
        if (!valid())
        {
            return;
        }

        var active = new System.Collections.Generic.List<string>();
        var passive = new System.Collections.Generic.List<string>();
        foreach (string skill_id in learned_ids())
        {
            if (_db.get_row("skill_base", skill_id) is not SkillBaseRow row)
            {
                continue;
            }

            if (row.is_active())
            {
                if (active.Count < active_slots())
                {
                    active.Add(skill_id);
                }
            }
            else
            {
                var after = new System.Collections.Generic.List<string>(passive) { skill_id };
                if (_growth.passive_fit(after.ToArray(), level(), attrs())["fits"].AsBool())
                {
                    passive.Add(skill_id);
                }
            }
        }

        _set_loadout(active, passive);
    }

    // ------------------------------------------------------------------ 战斗

    /// <summary>战斗里能用的招式 = 装配的招式（空着就打不出来，回合会被跳过）。</summary>
    public string[] battle_skill_ids() => active_ids();

    /// <summary>
    /// 内功装备即生效：加成转成 AttributeCalculator 的贡献列表。
    ///
    /// 0.15.0 起把**本队员对每部内功的熟练度**一起传进去——`stat:` 类加成按熟练度放大，
    /// `attr:` 类不放大（口径见 `GrowthCalculator.passive_contributions`）。
    /// </summary>
    public Godot.Collections.Array contributions()
    {
        if (_growth == null)
        {
            return new Godot.Collections.Array();
        }

        var levels = new Godot.Collections.Dictionary();
        foreach (string skill_id in passive_ids())
        {
            levels[skill_id] = _state != null ? _state.Call("mastery_of", char_id, skill_id).AsInt32() : 0;
        }

        return _growth.passive_contributions(passive_ids(), levels);
    }

    private Godot.Collections.Dictionary _gate(bool ok, string error) =>
        new() { { "ok", ok }, { "error", error } };

    /// <summary>
    /// 面板接进来的属性提供口有没有。**Godot 4.7 的 C# `Callable` 没有 `IsValid()`／`IsNull()`**
    /// （反射确认过公开面只有 `Target`／`Method`／`Delegate`／`Call`），所以按「目标 ＋ 方法名」判：
    /// GDScript 传的是 `Callable(self, "loadout_attrs")`，默认构造的 `Callable()` 两者都空。
    /// </summary>
    private bool _has_attrs_provider() =>
        attrs_provider.Target != null && !attrs_provider.Method.IsEmpty;

    private void _set_loadout(
        System.Collections.Generic.List<string> active,
        System.Collections.Generic.List<string> passive) =>
        _state.Call("set_loadout", char_id, active.ToArray(), passive.ToArray());

    private string[] _loadout_ids(string kind)
    {
        if (_state == null)
        {
            return System.Array.Empty<string>();
        }

        Godot.Collections.Dictionary loadout = _state.Call("loadout_of", char_id).AsGodotDictionary();
        if (!loadout.ContainsKey(kind))
        {
            return System.Array.Empty<string>();
        }

        // `loadout_of` 给的是 PackedStringArray，但设施里也可能塞进来 Array（两种形状都收，坑68）
        Variant value = loadout[kind];
        return value.VariantType == Variant.Type.PackedStringArray
            ? value.AsStringArray()
            : _strings_of(value.AsGodotArray());
    }

    private static string[] _strings_of(Godot.Collections.Array values)
    {
        var out_values = new System.Collections.Generic.List<string>();
        foreach (Variant entry in values)
        {
            out_values.Add(entry.AsString());
        }

        return out_values.ToArray();
    }

    private static bool _contains(string[] values, string wanted) =>
        System.Array.IndexOf(values, wanted) >= 0;
}
