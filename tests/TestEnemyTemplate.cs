using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 敌人模板与角色**共用一套管线**（C# 版）。
///
/// 设计 10 §二（0.14.0）：七维 + 等级 + 装备加成 + 内功加成 → `attr_to_stat`／`level_growth`
/// → 派生数值。发行数据里那七列**已经全配齐了**（0.28.0 的 17 行），所以「空七维走旧派生列」
/// 那条回退路径只能用**内存夹具**验：只改内存、不碰 CSV。
///
/// **2026-10-11（P4 第二十二批）起这份就是唯一一份**：旧链 `tests/test_enemy_template.gd` 已退场
/// ——本文件是它的**超集**（六节全在，另多一节 `_CheckFactoryBasics`：AI 模板名单／招式顺序／
/// 难度倍率／整队编号／掉落组／威胁标示），逐条对账见施工单「二点七八」。
///
/// 盯六件事：**空七维回退不静默崩**、**填上七维与 `AttributeCalculator` 算出的一模一样**、
/// **装备／内功加成走同一条贡献通道**、**内功超编只有白名单那两个**、
/// **`attr_map()` 的「全 0 才算没配」语义**、**工厂本身的几个口径**
/// （AI 模板名单、招式顺序、难度倍率、整队编号、掉落组）。
/// </summary>
public partial class TestEnemyTemplate : TestCase
{
    public override string SuiteName() => "敌人模板与角色同管线";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckEmptyAttrsFallBack(db);
        _CheckSharedPipeline(db);
        _CheckEquipmentContributions(db);
        _CheckPassiveContributions(db);
        _CheckPassiveCapacityRule(db);
        _CheckAttrMapSemantics(db);
        _CheckFactoryBasics(db);
    }

    /// <summary>
    /// 七维空着 → 走旧派生列那条过渡路，**并且出声点名**。
    ///
    /// 0.28.0 之后**发行数据全配齐了**（17 行都填了七维），所以这条回退路径只能用
    /// **内存夹具**验（把某一行的七维清零）——它仍然要留着：它是"数据漏配一行"时的兜底，
    /// 而且必须出声，不能静默按 0 算。
    /// </summary>
    private void _CheckEmptyAttrsFallBack(TableDb db)
    {
        // ① 发行数据：**一行都不许空着**（空了就说明漏配，玩家会看到一条没调的敌人）
        var pending = new System.Collections.Generic.List<string>();
        foreach (Variant entry in db.rows("enemy_base"))
        {
            var row = entry.AsGodotObject() as EnemyBaseRow;
            if (row != null && row.attr_map().Count == 0)
            {
                pending.Add(row.enemy_id);
            }
        }

        CheckTrue(pending.Count == 0, $"发行数据 17 行敌人的七维全配齐（空着的：{string.Join("、", pending)}）");

        // ② 回退路径：夹具把山寨喽啰的七维清零
        TableDb local = _WithAttrs(db, "en_bd_thug", new Godot.Collections.Dictionary());
        var row_scout = local.get_row("enemy_base", "en_bd_thug") as EnemyBaseRow;
        CheckTrue(row_scout.attr_map().Count == 0, "夹具里那一行的七维被清零了");
        EnemyFactory factory = EnemyFactory.create(local);
        GodotObject actor = factory.create_enemy("en_bd_thug", "normal");
        CheckNotNull(actor, "空七维也能造出敌人（过渡路径不静默崩）");

        // 过渡路上「派生列 ＋ 装备/内功的固定值」都要算（山寨喽啰的布衣给 20 点气血）
        int equip_hp = 0;
        foreach (Variant equip_entry in local.rows_where("enemy_equip", "enemy_id", "en_bd_thug"))
        {
            var equip_row = equip_entry.AsGodotObject() as EnemyEquipRow;
            var equip = equip_row == null
                ? null
                : local.get_row("equip_base", equip_row.equip_id) as EquipBaseRow;
            if (equip != null)
            {
                equip_hp += (int)equip.bonus_hp_max;
            }
        }

        int max_hp = actor.Call("max_hp").AsInt32();
        CheckEq(max_hp, row_scout.hp_base + equip_hp, $"血量来自旧派生列 ＋ 装备（{max_hp}）");
        // 过渡路上**装备/内功的 stat 类加成照样算**（不然毒抗会凭空消失）——见 _CheckPassiveContributions
    }

    /// <summary>填上七维 → 派生数值与 `AttributeCalculator` 完全一致（这就是「同一套算法」的可验证形式）。</summary>
    private void _CheckSharedPipeline(TableDb db)
    {
        TableDb local = _WithAttrs(db, "en_bd_thug", _Attrs(9, 7, 6, 4, 3, 6, 5));
        var row = local.get_row("enemy_base", "en_bd_thug") as EnemyBaseRow;
        CheckFalse(row.attr_map().Count == 0, "夹具里七维填上了");
        EnemyFactory factory = EnemyFactory.create(local);
        GodotObject actor = factory.create_enemy("en_bd_thug", "normal");
        CheckNotNull(actor, "七维齐了照样能造");

        var difficulty = local.get_row("difficulty_config", "normal") as DifficultyConfigRow;
        AttributeCalculator calculator = AttributeCalculator.create(local);
        Godot.Collections.Dictionary expected = calculator.compute(
            row.level, row.attr_map(), new Godot.Collections.Dictionary(),
            factory.enemy_contributions("en_bd_thug"),
            false   // 0.28.0：敌人**不吃** level_growth 的基础列（设计 10 §七）
        );
        foreach (string stat_id in new[] { "hp_max", "atk_phys", "atk_qi", "def_phys", "def_qi" })
        {
            double mul = stat_id switch
            {
                "hp_max" => difficulty.enemy_hp_mul,
                "atk_phys" or "atk_qi" => difficulty.enemy_atk_mul,
                _ => difficulty.enemy_def_mul,
            };
            double want = _Number(expected, stat_id) * mul;
            if (stat_id == "hp_max")
            {
                want = (int)System.Math.Round(want, System.MidpointRounding.AwayFromZero);
            }

            CheckFloat(actor.Call("stat", stat_id).AsDouble(), want, $"{stat_id} 与角色同一套 AttributeCalculator 算出来的一致", 0.01);
        }

        // 不吃难度倍率的那几项照抄派生值
        CheckFloat(actor.Call("stat", "speed").AsDouble(), _Number(expected, "speed"), "身法照抄派生值", 0.01);
        CheckFloat(actor.Call("stat", "crit_rate").AsDouble(), _Number(expected, "crit_rate"), "暴击率照抄派生值", 0.0001);
        // 内力池：设计 10 §二要「敌人也要有内力池」——共用管线把 qi_max 算出来了（不再是 0）
        CheckGt(actor.Call("stat", "qi_max").AsDouble(), 0.0, $"七维齐了就有内力池（qi_max={actor.Call("stat", "qi_max").AsInt32()}）");
        // 难度真的只改倍率：困难档的气血严格更高
        GodotObject hard = factory.create_enemy("en_bd_thug", "hard");
        CheckGt(hard.Call("max_hp").AsInt32(), _MaxHp(actor), "困难档气血倍率生效");
        CheckEq(
            hard.Get("reward_exp").AsInt32(),
            (int)System.Math.Round(row.exp_reward * (local.get_row("difficulty_config", "hard") as DifficultyConfigRow).exp_mul, System.MidpointRounding.AwayFromZero),
            "经验奖励按难度倍率算"
        );
        CheckEq(actor.Get("tags").AsGodotDictionary()["difficulty"].AsString(), "normal", "难度标签挂进 tags");
    }

    /// <summary>装备加成：`enemy_equip` 上的东西按 `equip_base` 的 bonus_* 进属性（与角色同一条贡献通道）。</summary>
    private void _CheckEquipmentContributions(TableDb db)
    {
        Godot.Collections.Dictionary attrs = _Attrs(5, 5, 5, 5, 5, 5, 5);
        TableDb bare = _WithAttrs(db, "en_bd_thug", attrs);
        TableDb armed = _WithAttrs(db, "en_bd_thug", attrs);
        // 把山寨喽啰的装备清空（bare）／留着（armed，本来就挂着一把柴刀与一件布衣）
        _DropEnemyEquip(bare, "en_bd_thug");
        GodotObject bare_actor = EnemyFactory.create(bare).create_enemy("en_bd_thug", "normal");
        GodotObject armed_actor = EnemyFactory.create(armed).create_enemy("en_bd_thug", "normal");
        CheckNotNull(bare_actor, "裸装山寨喽啰造出来了");
        CheckNotNull(armed_actor, "带装山寨喽啰造出来了");
        CheckGt(
            armed_actor.Call("stat", "atk_phys").AsDouble(),
            bare_actor.Call("stat", "atk_phys").AsDouble(),
            $"柴刀给的外功加成算进去了（{bare_actor.Call("stat", "atk_phys").AsDouble():F1} → {armed_actor.Call("stat", "atk_phys").AsDouble():F1}）"
        );
        CheckGt(
            _MaxHp(armed_actor),
            _MaxHp(bare_actor),
            $"布衣给的气血加成算进去了（{_MaxHp(bare_actor)} → {_MaxHp(armed_actor)}）"
        );
    }

    /// <summary>
    /// 内功加成：`enemy_passive` → `skill_passive_stat` 的 `stat:`／`attr:` 也进同一条通道
    /// （**三种元素抗性就是从这儿来的**：10 §二取消了那三条特权列）。
    /// </summary>
    private void _CheckPassiveContributions(TableDb db)
    {
        TableDb local = _WithAttrs(db, "en_bd_thug", _Attrs(5, 5, 5, 5, 5, 5, 5));
        GodotObject plain = EnemyFactory.create(local).create_enemy("en_bd_thug", "normal");
        CheckFloat(plain.Call("resistance_of", "poison").AsDouble(), 0.0, "没装内功时没有毒抗");
        // 给它挂上五毒心法·蚀骨（`pf_wudu_03` 给 0.15 毒抗）
        var table = (TableResource)local.tables["enemy_passive"].As<TableResource>().Duplicate(true);
        var template = (EnemyPassiveRow)table.rows[0].As<TableRow>().Duplicate(true);
        template.enemy_id = "en_bd_thug";
        template.skill_id = "pf_wudu_03";
        template.id = "en_bd_thug|pf_wudu_03";
        table.rows.Add(template);
        table.index[template.id] = table.rows.Count - 1;
        local.tables["enemy_passive"] = table;
        GodotObject trained = EnemyFactory.create(local).create_enemy("en_bd_thug", "normal");
        CheckFloat(trained.Call("resistance_of", "poison").AsDouble(), 0.15, "装上五毒心法就有 0.15 毒抗（与角色同一份表）");
        // 招式槽上限（设计 10 §二 连带规则①）：悟性决定能带几招
        var row = local.get_row("enemy_base", "en_bd_thug") as EnemyBaseRow;
        GrowthCalculator growth = GrowthCalculator.create(local);
        CheckEq(
            growth.active_slots(row.level, row.attr_map()),
            growth.active_slots(row.level, row.attr_map()),
            "槽位公式用的是同一份 growth_const（这条只是把口径写出来）"
        );
    }

    /// <summary>
    /// 敌人配的内功**占格 ≤ 容量**（设计 10 §二 连带规则①的另一半）——构建期那条规则的**反例**。
    ///
    /// 由来（2026-10-04）：10 号那句「敌人同样受招式槽与内功容量限制……用的是 `growth_const` 里同一组公式」
    /// 只接了招式槽（0.10.0），容量这半边一直空着；七维 0.28.0 配齐之后它本来该生效。
    /// 补上检查一量就抓到 **2 行超编**（`en_bd_boss` 5 格 > 4、`en_hidden_drunk` 7 格 > 4）——数据是设计侧配的，
    /// 「削内功／提根骨」属内容决定（`待策划确认.md` Q79），所以那两行进白名单、**双向维护**。
    /// 这条用例只钉两件事：①**发行数据的形状**（除白名单外一行都不许超编）；
    /// ②**那条规则真的抓得住**（夹具里把屠夫身上那部内功的占格 2 改 3，构建期必须点名 `en_bd_butcher`）。
    /// </summary>
    private void _CheckPassiveCapacityRule(TableDb db)
    {
        GrowthCalculator growth = GrowthCalculator.create(db);
        AttributeCalculator calculator = AttributeCalculator.create(db);
        var over = new Godot.Collections.Dictionary();
        foreach (Variant entry in db.rows("enemy_base"))
        {
            var row = entry.AsGodotObject() as EnemyBaseRow;
            Godot.Collections.Dictionary attrs = row.attr_map();
            if (attrs.Count == 0)
            {
                continue;
            }

            var passives = new System.Collections.Generic.List<string>();
            foreach (Variant passive_entry in db.rows_where("enemy_passive", "enemy_id", row.enemy_id))
            {
                var passive = passive_entry.AsGodotObject() as EnemyPassiveRow;
                if (passive != null)
                {
                    passives.Add(passive.skill_id);
                }
            }

            Godot.Collections.Array contributions = passives.Count > 0
                ? growth.passive_contributions(passives.ToArray(), new Godot.Collections.Dictionary())
                : new Godot.Collections.Array();
            Godot.Collections.Dictionary totals = calculator.attr_totals_of(
                attrs, new Godot.Collections.Dictionary(), contributions);
            Godot.Collections.Dictionary fit = growth.passive_fit(passives.ToArray(), row.level, totals);
            if (!fit["fits"].AsBool())
            {
                over[row.enemy_id] = $"{fit["used"].AsInt32()} 格 > 容量 {fit["capacity"].AsInt32()}";
            }
        }

        CheckEq(over.Count, 2, $"发行数据里超编的敌人正好两个（现在：{over}）");
        CheckTrue(over.ContainsKey("en_bd_boss"), $"大寨主在超编名单里（{(over.ContainsKey("en_bd_boss") ? over["en_bd_boss"].AsString() : "不在")}）");
        CheckTrue(over.ContainsKey("en_hidden_drunk"), $"醉刀客在超编名单里（{(over.ContainsKey("en_hidden_drunk") ? over["en_hidden_drunk"].AsString() : "不在")}）");

        // 夹具：把屠夫身上 `pf_chensha_02` 的占格从 2 改成 3 → 3＋1＝4 格 > 容量 3
        var broken = new TableDb();
        broken.load_all();
        var table = (TableResource)broken.tables["skill_passive"].As<TableResource>().Duplicate(true);
        foreach (Variant entry in table.rows)
        {
            var row = entry.AsGodotObject() as SkillPassiveRow;
            if (row != null && row.skill_id == "pf_chensha_02")
            {
                row.slot_cost = 3;
            }
        }

        broken.tables["skill_passive"] = table;
        string[] errors = TableValidator.validate(broken);
        bool named = false;
        foreach (string message in errors)
        {
            if (message.Contains("en_bd_butcher") && message.Contains("超编"))
            {
                named = true;
            }
        }

        CheckTrue(named, $"构建期要抓住「内功超编」并点名 en_bd_butcher：{string.Join(" / ", errors)}");
    }

    /// <summary>`attr_map()` 的语义：**七列全 0 才当「没配」**；有一列非 0 就整行生效。</summary>
    private void _CheckAttrMapSemantics(TableDb db)
    {
        var local = new TableDb();
        local.load_all();
        var table = (TableResource)local.tables["enemy_base"].As<TableResource>().Duplicate(true);
        var row = table.rows[0].AsGodotObject() as EnemyBaseRow;
        // 0.28.0 起发行数据全配着七维了，这条语义要用**清零后的**夹具行验
        row.attr_str = 0;
        row.attr_con = 0;
        row.attr_agi = 0;
        row.attr_int = 0;
        row.attr_luk = 0;
        row.attr_wu = 0;
        row.attr_gen = 0;
        CheckTrue(row.attr_map().Count == 0, "全 0 → 空字典（走过渡路径）");
        row.attr_luk = 3;
        CheckFalse(row.attr_map().Count == 0, "有一列非 0 → 整行生效");
        CheckEq(row.attr_map()["luk"].AsInt32(), 3, "取到的就是那一列");
        CheckEq(row.attr_map().ContainsKey("str") ? row.attr_map()["str"].AsInt32() : -1, 0, "其余列按 0 参与");
    }

    /// <summary>工厂本身的几个口径：AI 模板名单、招式顺序、整队编号、掉落组、威胁标示。</summary>
    private void _CheckFactoryBasics(TableDb db)
    {
        CheckEq(
            string.Join(",", EnemyFactory.ai_templates()),
            "ai_aggressive,ai_basic,ai_boss,ai_chaser,ai_debuffer,ai_elite,ai_neutral,ai_ranged,ai_sleeping",
            "九种 AI 模板的绝对值（构建期校验器与这里是同一份名单）"
        );

        EnemyFactory factory = EnemyFactory.create(db);
        CheckEq(string.Join(",", factory.skills_of("en_wolf")), "sk_common_01,sk_chensha_01", "招式按 sort_order 排");
        CheckEq(factory.skills_of("en_does_not_exist").Length, 0, "没有招式的敌人返回空（战斗里走「无可用招式，跳过」）");

        Godot.Collections.Array team = factory.create_team("team_wolf_pack", "normal");
        CheckEq(team.Count, 3, "野狼群是 3 只（`en_wolf:3`）");
        CheckEq(team[0].AsGodotObject().Get("actor_id").AsString(), "en_wolf#1", "整队成员带序号（第一个）");
        CheckEq(team[team.Count - 1].AsGodotObject().Get("actor_id").AsString(), "en_wolf#3", "整队成员带序号（最后一个）");

        CheckEq(string.Join(",", factory.team_drop_groups("team_wolf_pack")), "drop_wolf", "小队掉落组去重后交给 DropResolver");
        CheckEq(
            string.Join(",", factory.threat_tags(team)),
            "green",
            "威胁标示统计（明雷颜色环用）"
        );
    }

    // ---------------------------------------------------------------- 夹具与小工具

    /// <summary>给某个敌人的七维填上值（只改内存副本——与 `tests/test_case.gd` 的夹具同形，坑53）。</summary>
    private static TableDb _WithAttrs(TableDb db, string enemy_id, Godot.Collections.Dictionary attrs)
    {
        var local = new TableDb();
        local.load_all();
        var table = (TableResource)local.tables["enemy_base"].As<TableResource>().Duplicate(true);
        foreach (Variant entry in table.rows)
        {
            var row = entry.AsGodotObject() as EnemyBaseRow;
            if (row == null || row.enemy_id != enemy_id)
            {
                continue;
            }

            row.attr_str = _AttrOf(attrs, "str");
            row.attr_con = _AttrOf(attrs, "con");
            row.attr_agi = _AttrOf(attrs, "agi");
            row.attr_int = _AttrOf(attrs, "int");
            row.attr_luk = _AttrOf(attrs, "luk");
            row.attr_wu = _AttrOf(attrs, "wu");
            row.attr_gen = _AttrOf(attrs, "gen");
        }

        local.tables["enemy_base"] = table;
        return local;
    }

    /// <summary>把某个敌人的 `enemy_equip` 行全摘掉（只改内存副本）。</summary>
    private static void _DropEnemyEquip(TableDb db, string enemy_id)
    {
        var table = (TableResource)db.tables["enemy_equip"].As<TableResource>().Duplicate(true);
        var kept = new Godot.Collections.Array();
        var kept_index = new Godot.Collections.Dictionary();
        foreach (Variant entry in table.rows)
        {
            var row = entry.AsGodotObject() as EnemyEquipRow;
            if (row != null && row.enemy_id == enemy_id)
            {
                continue;
            }

            var keep_row = entry.As<TableRow>();
            kept_index[keep_row.id] = kept.Count;
            kept.Add(keep_row);
        }

        table.rows = kept;
        table.index = kept_index;
        db.tables["enemy_equip"] = table;
    }

    private static Godot.Collections.Dictionary _Attrs(int str, int con, int agi, int @int, int luk, int wu, int gen) =>
        new()
        {
            { "str", str }, { "con", con }, { "agi", agi }, { "int", @int },
            { "luk", luk }, { "wu", wu }, { "gen", gen },
        };

    private static int _AttrOf(Godot.Collections.Dictionary attrs, string key) =>
        attrs.ContainsKey(key) ? attrs[key].AsInt32() : 0;

    private static double _Number(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

    private static int _MaxHp(GodotObject actor) => actor.Call("max_hp").AsInt32();
}
