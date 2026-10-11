using Godot;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 创建角色（设计 13，0.17.0）＋「使用模板／不使用模板」两条路（C# 版，对位原 `src/core/creation_service.gd`）。
///
///   使用模板：选出身（`origin_def` 的 5 张卡，直接复用 `character_base` 的 5 行）→ 选天赋 → 起名
///   不使用模板：分七维（总和 `create_attr_total`、单项 `create_attr_min`~`create_attr_max`）
///               → 选武器 → 选本系 ★1 起始武学 → 选天赋 → 起名
///
/// 两条路**给的东西完全等价**（设计 13），所以这里只有「模板从哪来」一处差别：
/// 模板路线直接引用现成行；自建路线现造一行、注入 `character_base`、并把这份 spec 存进存档
/// （`GameState.custom_templates`）——不然读档时按 char_id 查不到模板。
///
/// 两条纪律：
///   ① **创建没走完不写存档**（设计 13 的准话）——本服务只造 `GameState`，落盘由调用方在确认后做；
///   ② **选了谁当主角，谁就不作为同伴出现**——不用额外记账：`RecruitService` 只收不在队里的人。
///
/// **跨语言面**：`GameState` 与 `CharacterSheet` 还是 GDScript，所以 `new_game`／`apply_custom_templates`／
/// `to_dict`／`char_name` 走 `GD.Load&lt;GDScript&gt;(…).Call(…)`，`state` 本体收 `GodotObject`
/// 再用 `Get`／`Set` 读写（施工单坑⑥／坑67：属性用 `Get`，方法用 `Call`）。
/// </summary>
public partial class CreationService : RefCounted
{
    /// <summary>自建角色的固定 id：一局只有一个主角，进存档时也按它认。</summary>
    public const string CUSTOM_CHAR_ID = "char_custom";

    /// <summary>自建角色的起始护甲：与 5 个模板一样给凡品布衣。</summary>
    private const string CUSTOM_ARMOR_ID = "eq_armor_01";

    /// <summary>GDScript 读不到 C# 的常量（施工单坑⑩／㊹），`test_creation.gd` 拿它认主角 id。</summary>
    public static string custom_char_id() => CUSTOM_CHAR_ID;

    /// <summary>出身卡（创建界面直接渲染这个）。`playstyle`／`tagline` 就是 `origin_def` 的展示列。</summary>
    public static Godot.Collections.Array origins(TableDb db)
    {
        var out_cards = new Godot.Collections.Array();
        if (db == null)
        {
            return out_cards;
        }

        var cards = new System.Collections.Generic.List<Godot.Collections.Dictionary>();
        foreach (Variant entry in db.rows("origin_def"))
        {
            var row = entry.AsGodotObject() as OriginDefRow;
            var card = new Godot.Collections.Dictionary
            {
                { "origin_id", row.origin_id },
                { "char_id", row.char_id },
                { "name_cn", row.name_cn },
                // 默认名（设计 21 §七 第 5 条）：卡片第二行与"名字"预填都用它
                { "default_name_cn", default_name_of(db, row.origin_id) },
                { "playstyle", row.playstyle },
                { "tagline", row.tagline },
                { "sort_order", row.sort_order },
                { "desc", row.desc },
            };
            // 设计 13 §二：**卡片必须显示七维与起始武学**（"不能只给一句文案——玩家是在做数值选择"），
            // §六 还要求七维用**条形图**（"五个出身一眼可比，比读数字快"）。所以在服务层就把这几样
            // 备好，界面只负责渲染——**别让界面自己去 character_base 里翻**（同一份换算只写一处）。
            var char_row = db.get_row("character_base", row.char_id) as CharacterBaseRow;
            if (char_row != null)
            {
                card["attrs"] = char_row.initial_attrs();
                card["weapon_type"] = char_row.weapon_type;
                card["weapon_name"] = weapon_name_of(db, char_row.weapon_type);
                var skill_names = new System.Collections.Generic.List<string>();
                foreach (string skill_id in char_row.skill_ids())
                {
                    skill_names.Add(skill_name_of(db, skill_id));
                }

                card["skill_names"] = skill_names.ToArray();
            }

            cards.Add(card);
        }

        // 对位 GDScript 的 `sort_custom`；数据里 sort_order 不重复，所以用「排序键 + 原始下标」兜底保证确定性。
        var indexed = new System.Collections.Generic.List<(int Order, int Index, Godot.Collections.Dictionary Card)>();
        for (int index = 0; index < cards.Count; index++)
        {
            indexed.Add((cards[index]["sort_order"].AsInt32(), index, cards[index]));
        }

        indexed.Sort((left, right) =>
            left.Order != right.Order ? left.Order.CompareTo(right.Order) : left.Index.CompareTo(right.Index));
        foreach (var (order, index, card) in indexed)
        {
            out_cards.Add(card);
        }

        return out_cards;
    }

    /// <summary>武器类型的中文名（15 §六 的表里那列；查不到就退回 id，界面照实显示）。</summary>
    public static string weapon_name_of(TableDb db, string weapon_type)
    {
        var row = db == null ? null : db.get_row("weapon_type_def", weapon_type) as WeaponTypeDefRow;
        return row != null ? row.name_cn : weapon_type;
    }

    /// <summary>
    /// 出身卡的**默认名**（设计 21 §七 第 5 条，决策 323）：优先 `origin_def.default_name_cn`
    /// 那一列——因为 `name_cn` 是**卡的标题**（书生那张写的是「家道失落的书生」这种**类名**，
    /// 而真名是**陆文昭**），预填进"名字"那一格会给主角起个类名。
    /// 空值退回 `name_cn`（老数据／没配的卡不会崩）。**"默认名怎么来"只有这一处**——
    /// `MenuController._with_defaults()` 与创建界面的预填都调它。
    /// </summary>
    public static string default_name_of(TableDb db, string origin_id)
    {
        var card = db == null ? null : db.get_row("origin_def", origin_id) as OriginDefRow;
        if (card == null)
        {
            return "";
        }

        string real_name = card.default_name_cn.Trim();
        return real_name.Length > 0 ? real_name : card.name_cn;
    }

    /// <summary>武学的中文名（出身卡的「起始武学」那一项用它）。</summary>
    public static string skill_name_of(TableDb db, string skill_id)
    {
        var row = db == null ? null : db.get_row("skill_base", skill_id) as SkillBaseRow;
        return row != null ? row.name_cn : skill_id;
    }

    /// <summary>自建路线的七维规则（`growth_const` 的三个创建常数）。</summary>
    public static Godot.Collections.Dictionary attr_rules(TableDb db)
    {
        int total = 0;
        int low = 0;
        int high = 0;
        if (db != null)
        {
            foreach (Variant entry in db.rows("growth_const"))
            {
                var row = entry.AsGodotObject() as GrowthConstRow;
                switch (row.const_id)
                {
                    case "create_attr_total":
                        total = (int)row.value;
                        break;
                    case "create_attr_min":
                        low = (int)row.value;
                        break;
                    case "create_attr_max":
                        high = (int)row.value;
                        break;
                }
            }
        }

        return new Godot.Collections.Dictionary { { "total", total }, { "min", low }, { "max", high } };
    }

    /// <summary>武器类型选项（`weapon_type_def` 的全部行）。</summary>
    public static Godot.Collections.Array weapons(TableDb db)
    {
        var out_weapons = new Godot.Collections.Array();
        if (db == null)
        {
            return out_weapons;
        }

        foreach (Variant entry in db.rows("weapon_type_def"))
        {
            var row = entry.AsGodotObject() as WeaponTypeDefRow;
            out_weapons.Add(new Godot.Collections.Dictionary
            {
                { "weapon_type", row.weapon_type },
                { "name_cn", row.name_cn },
                { "desc", row.desc },
            });
        }

        return out_weapons;
    }

    /// <summary>某武器能当起手的那几部武学：**★1 且本系（`any`／留空也算通用）**（设计 13）。</summary>
    public static Godot.Collections.Array start_skill_options(TableDb db, string weapon_type)
    {
        var options = new System.Collections.Generic.List<Godot.Collections.Dictionary>();
        if (db != null)
        {
            foreach (Variant entry in db.rows("skill_base"))
            {
                var row = entry.AsGodotObject() as SkillBaseRow;
                if (row.star != 1 || row.source_type != "start")
                {
                    continue;
                }

                string want = row.weapon_type;
                if (!(want == weapon_type || want == "any" || want.Length == 0))
                {
                    continue;
                }

                options.Add(new Godot.Collections.Dictionary
                {
                    { "skill_id", row.skill_id },
                    { "name_cn", row.name_cn },
                    { "weapon_type", want },
                });
            }
        }

        // GDScript 的 `a["skill_id"] < b["skill_id"]` 是按码点比；C# 默认是文化相关比较。
        options.Sort((left, right) => string.CompareOrdinal(left["skill_id"].AsString(), right["skill_id"].AsString()));
        var out_options = new Godot.Collections.Array();
        foreach (Godot.Collections.Dictionary option in options)
        {
            out_options.Add(option);
        }

        return out_options;
    }

    /// <summary>
    /// 自建角色的起始装备（设计 13：**不由玩家选**，按武器自动给）——
    /// 该武器的凡品一件 ＋ 与模板同款的布衣。找不到对应武器时只给布衣（不编数据）。
    /// </summary>
    public static string[] default_equipment(TableDb db, string weapon_type)
    {
        var out_equipment = new System.Collections.Generic.List<string>();
        if (db != null && weapon_type.Length > 0)
        {
            foreach (Variant entry in db.rows("equip_base"))
            {
                var row = entry.AsGodotObject() as EquipBaseRow;
                if (row.slot == "weapon" && row.weapon_type == weapon_type && row.rarity == "common")
                {
                    out_equipment.Add(row.equip_id);
                    break;
                }
            }
        }

        out_equipment.Add(CUSTOM_ARMOR_ID);
        return out_equipment.ToArray();
    }

    /// <summary>
    /// 校验一份创建方案。返回错误列表（空 = 通过）。
    ///
    /// `spec`：
    ///   `{"route": "origin"|"custom", "origin_id", "char_id", "name_cn",
    ///    "attrs": {七个 attr_id}, "weapon_type", "start_skill_id", "talents": [talent_id],
    ///    "difficulty_id"}`
    /// </summary>
    public static string[] validate(TableDb db, Godot.Collections.Dictionary spec)
    {
        var errors = new System.Collections.Generic.List<string>();
        if (db == null)
        {
            errors.Add("没有配置表");
            return errors.ToArray();
        }

        string route = _text(spec, "route", "origin");
        string char_id = _text(spec, "char_id", "");
        if (route == "origin")
        {
            string origin_id = _text(spec, "origin_id", "");
            var card = db.get_row("origin_def", origin_id) as OriginDefRow;
            if (card == null)
            {
                // 还没选（空 id）是**人话**；选了但查不到才是数据错——只有后者才点名 id。
                // 以前两种情况都走同一句，于是刚进创建界面时状态栏写着「出身不存在：」（空 id）。
                errors.Add(origin_id.Length == 0 ? "还没选出身" : $"出身不存在：{origin_id}");
            }
            else
            {
                char_id = card.char_id;
            }
        }
        else
        {
            char_id = CUSTOM_CHAR_ID;
            Godot.Collections.Dictionary rules = attr_rules(db);
            Godot.Collections.Dictionary attrs = _dictionary(spec, "attrs");
            int total = 0;
            foreach (Variant entry in db.rows("attribute_def"))
            {
                var attr_row = entry.AsGodotObject() as AttributeDefRow;
                string attr_id = attr_row.attr_id;
                if (!attrs.ContainsKey(attr_id))
                {
                    errors.Add($"七维少了 {attr_id}");
                    continue;
                }

                int value = attrs[attr_id].AsInt32();
                total += value;
                int low = rules["min"].AsInt32();
                int high = rules["max"].AsInt32();
                if (value < low || value > high)
                {
                    errors.Add($"{attr_row.name_cn} 必须在 {low}~{high} 之间（现在是 {value}）");
                }
            }

            if (total != rules["total"].AsInt32())
            {
                errors.Add($"七维总和必须正好 {rules["total"].AsInt32()}（现在是 {total}）");
            }

            string weapon = _text(spec, "weapon_type", "");
            if (db.get_row("weapon_type_def", weapon) == null)
            {
                errors.Add($"武器类型不存在：{weapon}");
            }

            string skill_id = _text(spec, "start_skill_id", "");
            bool allowed = false;
            foreach (Variant option_variant in start_skill_options(db, weapon))
            {
                if (option_variant.AsGodotDictionary()["skill_id"].AsString() == skill_id)
                {
                    allowed = true;
                }
            }

            if (!allowed)
            {
                errors.Add($"起始武学 {skill_id} 不是「★1 且本系」的那几部");
            }
        }

        if (char_id.Length == 0)
        {
            errors.Add("没有确定用哪个角色模板");
        }

        if (_text(spec, "name_cn", "").Trim().Length == 0)
        {
            errors.Add("还没起名");
        }

        // 天赋：id 存在 + 总花费不超预算
        var picks = new System.Collections.Generic.List<string>();
        foreach (Variant talent_id in _array(spec, "talents"))
        {
            picks.Add(talent_id.AsString());
        }

        if (picks.Count > 0)
        {
            var running = new System.Collections.Generic.List<string>();
            foreach (string talent_id in picks)
            {
                Godot.Collections.Dictionary check = TalentService.can_pick(db, running.ToArray(), talent_id);
                if (!check["ok"].AsBool())
                {
                    errors.Add(check["error"].AsString());
                }
                else
                {
                    running.Add(talent_id);
                }
            }
        }

        return errors.ToArray();
    }

    /// <summary>按方案造出一局（**不落盘**——落盘由调用方在玩家按「确认」之后做）。返回 `{ok, state, char_id, error}`。</summary>
    public static Godot.Collections.Dictionary build(TableDb db, Godot.Collections.Dictionary spec)
    {
        string[] errors = validate(db, spec);
        if (errors.Length > 0)
        {
            // `Godot.Collections.Dictionary` 不收 C# 的 null（施工单坑⑥）：空对象写 `default(Variant)`，
            // 调用方 `creation["state"].AsGodotObject()` 才是 null。
            return new Godot.Collections.Dictionary
            {
                { "ok", false },
                { "state", default(Variant) },
                { "char_id", "" },
                { "error", string.Join("；", errors) },
            };
        }

        string route = _text(spec, "route", "origin");
        string char_id;
        var custom_spec = new Godot.Collections.Dictionary();
        if (route == "origin")
        {
            var card = db.get_row("origin_def", _text(spec, "origin_id", "")) as OriginDefRow;
            char_id = card.char_id;
            // 「主角可改」＋默认名（设计 21 §七 第 5 条，决策 323）：
            // `GameState.char_name()` 读的是**表里那一行**，所以玩家改过的名字（以及"默认名＝真名"）
            // 必须像自建角色那样**注入一行并存进存档**——否则界面上改的名字谁也不认，
            // 面板会一直显示卡的标题（书生的卡标题是「家道失落的书生」）。
            // 其余字段照模板抄一遍，只有 `name_cn` 换掉；`custom_templates` 是 v14 就有的字段
            // （自建角色那条路在用），所以**不动存档版本**。
            var char_row = db.get_row("character_base", char_id) as CharacterBaseRow;
            string wanted_name = _text(spec, "name_cn", "").Trim();
            if (char_row != null && wanted_name.Length > 0 && wanted_name != char_row.name_cn)
            {
                custom_spec = new Godot.Collections.Dictionary
                {
                    { "name_cn", wanted_name },
                    { "role_tag", char_row.role_tag },
                    { "weapon_type", char_row.weapon_type },
                    { "attrs", char_row.initial_attrs() },
                    { "start_level", char_row.start_level },
                    { "start_skill_ids", char_row.start_skill_ids },
                    { "start_equip_ids", char_row.start_equip_ids },
                    { "desc", char_row.desc },
                };
            }
        }
        else
        {
            char_id = CUSTOM_CHAR_ID;
            string weapon_type = _text(spec, "weapon_type", "");
            custom_spec = new Godot.Collections.Dictionary
            {
                { "name_cn", _text(spec, "name_cn", "无名") },
                { "role_tag", "自建" },
                { "weapon_type", weapon_type },
                { "attrs", _dictionary(spec, "attrs").Duplicate(true) },
                { "start_level", 1 },
                { "start_skill_ids", _text(spec, "start_skill_id", "") },
                { "start_equip_ids", string.Join(";", default_equipment(db, weapon_type)) },
                { "desc", "自建角色（不使用模板）" },
            };
        }

        // 自建角色要**先注入模板**再开新局：`new_game` 是按 char_id 读 character_base 的，
        // 模板没在表里 = 这一行被跳过 = 开局队伍为空。注入是内存行为，CSV 不动。
        if (custom_spec.Count > 0)
        {
            GodotObject probe = new GameState();
            probe.Get("custom_templates").AsGodotDictionary()[char_id] = custom_spec.Duplicate(true);
            probe.Call("apply_custom_templates", db);
        }

        GodotObject state = GameState.new_game(
            db, _text(spec, "difficulty_id", "normal"), new[] { char_id }, 0);
        Godot.Collections.Dictionary talent_picks = state.Get("talent_picks").AsGodotDictionary();
        if (custom_spec.Count == 0)
        {
            talent_picks[char_id] = _array(spec, "talents").Duplicate(true);
        }
        else
        {
            state.Get("custom_templates").AsGodotDictionary()[char_id] = custom_spec.Duplicate(true);
            state.Call("apply_custom_templates", db);
            talent_picks[char_id] = _array(spec, "talents").Duplicate(true);
        }

        // 天赋里「纯增益」的那类：起始铜钱（设计 12：纯增益只加资源与规则）
        var money = (int)TalentService.rule_value(db, state, char_id, "start_money", 0.0);
        if (money != 0)
        {
            GodotObject inventory = state.Get("inventory").AsGodotObject();
            inventory.Set("money", inventory.Get("money").AsInt32() + money);
        }

        return new Godot.Collections.Dictionary
        {
            { "ok", true },
            { "state", state },
            { "char_id", char_id },
            { "error", "" },
        };
    }

    // ---------------------------------------------------------------- 小工具

    /// <summary>对位 GDScript 的 `str(dict.get(key, fallback))`（缺键给兜底）。</summary>
    private static string _text(Godot.Collections.Dictionary dict, string key, string fallback) =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    /// <summary>对位 GDScript 的 `Dictionary(spec.get(key, {}))`：不是字典就当空表。</summary>
    private static Godot.Collections.Dictionary _dictionary(Godot.Collections.Dictionary dict, string key)
    {
        if (!dict.ContainsKey(key) || dict[key].VariantType != Variant.Type.Dictionary)
        {
            return new Godot.Collections.Dictionary();
        }

        return dict[key].AsGodotDictionary();
    }

    /// <summary>
    /// 对位 GDScript 的 `Array(spec.get(key, []))`：`Array` 与 `PackedStringArray` 都收，
    /// 其余（含缺键）当空表——GDScript 那句遇上 `null` 是会报错的，所以这里只放行两种真数组。
    /// </summary>
    private static Godot.Collections.Array _array(Godot.Collections.Dictionary dict, string key)
    {
        var out_items = new Godot.Collections.Array();
        if (!dict.ContainsKey(key))
        {
            return out_items;
        }

        Variant raw = dict[key];
        if (raw.VariantType == Variant.Type.Array)
        {
            return raw.AsGodotArray();
        }

        if (raw.VariantType == Variant.Type.PackedStringArray)
        {
            foreach (string item in raw.AsStringArray())
            {
                out_items.Add(item);
            }
        }

        return out_items;
    }
}
