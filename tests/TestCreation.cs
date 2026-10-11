using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 创建角色（设计 13，0.17.0）＋ 天赋（设计 12，0.16.0）。
///
/// **2026-10-11（P4 第二十六批）起这份就是唯一一份**：旧链 `tests/test_creation.gd` 已并入本文件后退场
/// ——这一批补进的是当初「要真场景」的那一节（实例化 `scenes/creation_screen.tscn`，读出身份卡第二行
/// 与确认段汇总的 `Label` 真文本，验七维条形图／武器名／起始武学名／预填真名／汇总跟着换人）。
///
/// 两条纪律各有一条断言：创建没走完不落盘（由 `test_menu` 的场景流验）、
/// 选了谁当主角谁就不作为同伴出现（本文件验）。
/// </summary>
public partial class TestCreation : TestCase
{
    private const string OriginScholar = "ori_scholar";
    private const string OriginGang = "ori_gang";

    public override string SuiteName() => "创建角色与天赋";

    public override void Run()
    {
        // 这一份用例会**往表里注入行**（自建模板）：必须用私有表库，否则同进程里后跑的用例
        // 会读到注入的行（`TestCase.FreshDb` 的注释里写了 2026-10-11 实测的那次症状）。
        TableDb db = FreshDb();
        _CheckCards(db);
        _CheckValidation(db);
        _CheckOriginRoute(db);
        _CheckCustomRoute(db);
        _CheckTalents(db);
        _CheckScreenCards();
    }

    /// <summary>出身卡与七维规则都从表里来（创建界面直接渲染这两份数据）。</summary>
    private void _CheckCards(TableDb db)
    {
        Godot.Collections.Array cards = CreationService.origins(db);
        CheckEq(cards.Count, 5, "5 张出身卡（origin_def 5 行）");
        var first = cards[0].AsGodotDictionary();
        CheckEq(first["origin_id"].AsString(), OriginScholar, "按 sort_order 排，第一张是书生");
        CheckTrue(first["tagline"].AsString().Length > 0, "出身卡带一句话定位（tagline）");
        CheckTrue(first["playstyle"].AsString().Length > 0, "出身卡带玩法标签（playstyle）");
        // 设计 13 §二：**卡片必须显示七维与起始武学**（"不能只给一句文案——玩家是在做数值选择"）
        var attrs = first["attrs"].AsGodotDictionary();
        CheckEq(attrs.Count, 7, $"出身卡带上七维（{attrs}）");
        int attr_sum = 0;
        foreach (string attr_id in new[] { "str", "con", "agi", "int", "luk", "wu", "gen" })
        {
            attr_sum += attrs.ContainsKey(attr_id) ? attrs[attr_id].AsInt32() : 0;
        }

        CheckEq(attr_sum, 49, "卡片上的七维合计 49（与 character_base 一致）");
        CheckEq(first.ContainsKey("weapon_name") ? first["weapon_name"].AsString() : "", "剑", "卡片带武器中文名（书生的家传剑）");
        string[] skill_names = first.ContainsKey("skill_names") ? first["skill_names"].AsStringArray() : System.Array.Empty<string>();
        CheckEq(skill_names.Length, 1, "卡片带起始武学（书生的玄微剑法·起手）");
        CheckEq(skill_names[0], "玄微剑法·起手", "起始武学写中文名，不写 id");
        // 默认名（设计 21 §七 第 5 条）：卡的标题是类名「家道失落的书生」，真名才是陆文昭
        CheckEq(CreationService.default_name_of(db, OriginScholar), "陆文昭", "书生的默认名是真名（不是卡标题）");
        CheckEq(first.ContainsKey("default_name_cn") ? first["default_name_cn"].AsString() : "", "陆文昭", "出身卡数据里带着默认名（界面直接用它）");
        foreach (Variant card_variant in cards)
        {
            var card = card_variant.AsGodotDictionary();
            CheckTrue(
                card.ContainsKey("default_name_cn") && card["default_name_cn"].AsString().Length > 0,
                $"每张卡都有默认名（{card["origin_id"].AsString()}）"
            );
        }

        Godot.Collections.Dictionary rules = CreationService.attr_rules(db);
        CheckEq(rules["total"].AsInt32(), 49, "七维总和 49（growth_const.create_attr_total）");
        CheckEq(rules["min"].AsInt32(), 3, "单项下限 3");
        CheckEq(rules["max"].AsInt32(), 15, "单项上限 15");
        CheckEq(CreationService.weapons(db).Count, 4, "四种武器可选");
    }

    private void _CheckValidation(TableDb db)
    {
        var ok_spec = _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", OriginScholar }, { "name_cn", "书生" }, { "talents", new Godot.Collections.Array() },
        });
        CheckTrue(CreationService.validate(db, ok_spec).Length == 0, "模板路的最小方案就合法");

        var bad_origin = _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", "ori_missing" }, { "name_cn", "谁" }, { "talents", new Godot.Collections.Array() },
        });
        CheckTrue(CreationService.validate(db, bad_origin).Length > 0, "出身不存在要报错");

        var no_name = _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", OriginScholar }, { "name_cn", "  " }, { "talents", new Godot.Collections.Array() },
        });
        CheckTrue(CreationService.validate(db, no_name).Length > 0, "没起名要报错");
        // 刚进创建界面（什么都没选）时说**人话**，别把「出身不存在：」这种空 id 摆给玩家
        string[] fresh = CreationService.validate(db, _Spec("origin", new Godot.Collections.Dictionary
        {
            { "talents", new Godot.Collections.Array() },
        }));
        CheckTrue(string.Join("；", fresh).Contains("还没选出身"), $"没选出身时提示是人话：{string.Join("；", fresh)}");
        CheckFalse(string.Join("；", fresh).Contains("出身不存在"), "没选出身时不说「出身不存在」（那是数据错才说的话）");

        // 自建路：总和不对 / 单项越界 / 武器不存在 / 起始武学不是本系 ★1
        Godot.Collections.Dictionary custom = _CustomSpec(db);
        CheckTrue(CreationService.validate(db, custom).Length == 0, "自建路的最小合法方案");
        Godot.Collections.Dictionary wrong_sum = custom.Duplicate(true);
        wrong_sum["attrs"] = custom["attrs"].AsGodotDictionary().Duplicate(true);
        wrong_sum["attrs"].AsGodotDictionary()["str"] = wrong_sum["attrs"].AsGodotDictionary()["str"].AsInt32() + 1;
        CheckTrue(CreationService.validate(db, wrong_sum).Length > 0, "七维总和不对要报错");
        Godot.Collections.Dictionary over = custom.Duplicate(true);
        over["attrs"] = custom["attrs"].AsGodotDictionary().Duplicate(true);
        over["attrs"].AsGodotDictionary()["str"] = CreationService.attr_rules(db)["max"].AsInt32() + 1;
        over["attrs"].AsGodotDictionary()["con"] = over["attrs"].AsGodotDictionary()["con"].AsInt32() - 1;
        CheckTrue(CreationService.validate(db, over).Length > 0, "单项超过上限要报错");
        Godot.Collections.Dictionary bad_weapon = custom.Duplicate(true);
        bad_weapon["weapon_type"] = "staff";
        CheckTrue(CreationService.validate(db, bad_weapon).Length > 0, "武器类型不存在要报错");
        Godot.Collections.Dictionary bad_skill = custom.Duplicate(true);
        bad_skill["start_skill_id"] = "sk_xuanwei_08";
        CheckTrue(CreationService.validate(db, bad_skill).Length > 0, "起始武学必须★1本系（★5 不行）");
        Godot.Collections.Dictionary too_many_talents = ok_spec.Duplicate(true);
        too_many_talents["talents"] = new Godot.Collections.Array { "tal_dan_shi", "tal_jiu_ming", "tal_yi_tong" };
        CheckTrue(CreationService.validate(db, too_many_talents).Length > 0, "天赋点超过 5 要报错");
    }

    /// <summary>模板路：主角就是那张卡指向的角色；**他不会再作为同伴出现**（设计 13 的准话）。</summary>
    private void _CheckOriginRoute(TableDb db)
    {
        Godot.Collections.Dictionary built = CreationService.build(db, _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", OriginGang }, { "name_cn", "林铁山" }, { "talents", new Godot.Collections.Array() },
        }));
        CheckTrue(built["ok"].AsBool(), $"模板路能建出存档：{(built.ContainsKey("error") ? built["error"].AsString() : "")}");
        GodotObject state = built["state"].AsGodotObject();
        CheckNotNull(state, "建出来的是一份 GameState");
        CheckEq(built["char_id"].AsString(), "ch_gang", "选「林铁山」这张卡 = 用 ch_gang 的模板");
        CheckEq(state.Call("party_size").AsInt32(), 1, "开局只有主角一人");
        // 主角已经在队里 → 招募链不会把他再收一遍（同一张卡的同伴入口自然跳过）
        Godot.Collections.Array pending = RecruitService.pending_for_scene(db, state, "scene_qingfengyi");
        var pending_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in pending)
        {
            pending_ids.Add((row_variant.AsGodotObject() as RecruitDefRow)?.char_id ?? "");
        }

        CheckFalse(pending_ids.Contains("ch_gang"), "主角不会再作为同伴出现");
        // 换了主角，其它同伴照样能来（燕小七那条链的条件是告示板旗标）
        state.Call("set_flag", "flag_board_read", true);
        Godot.Collections.Array after = RecruitService.pending_for_scene(db, state, "scene_qingfengyi");
        var after_ids = new System.Collections.Generic.List<string>();
        foreach (Variant row_variant in after)
        {
            after_ids.Add((row_variant.AsGodotObject() as RecruitDefRow)?.char_id ?? "");
        }

        CheckTrue(after_ids.Contains("ch_ci"), "主角换成别人之后，燕小七仍然按剧情入队");
        CheckFalse(after_ids.Contains("ch_gang"), "主角（这次是林铁山）仍然不在同伴名单里");

        // 「主角可改」＋默认名（设计 21 §七 第 5 条）：名字要**真的进存档**——
        // `char_name()` 读的是表，所以改过的名字按自建角色那套注入一行（v14 的字段，不动版本）
        Godot.Collections.Dictionary renamed = CreationService.build(db, _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", OriginScholar }, { "name_cn", "王二" }, { "talents", new Godot.Collections.Array() },
        }));
        CheckTrue(renamed["ok"].AsBool(), $"改名的模板角色建得出来：{(renamed.ContainsKey("error") ? renamed["error"].AsString() : "")}");
        GodotObject named_state = renamed["state"].AsGodotObject();
        CheckEq(named_state.Call("char_name", db, "scholar_fallen").AsString(), "王二", "面板读到的是玩家改的名字");
        CheckEq(named_state.Get("char_ids").AsStringArray().Length, 1, "队伍人数不受影响");
        // 存档往返：改名跟着走（不然读档又变回卡标题）
        GodotObject back = _RoundTrip(db, named_state);
        CheckNotNull(back, "改名后的档读得回来");
        CheckEq(back.Call("char_name", db, "scholar_fallen").AsString(), "王二", "读档后名字仍是玩家改的");
    }

    /// <summary>自建路：模板注入 db、起始装备按武器给、面板拿得到这份模板。</summary>
    private void _CheckCustomRoute(TableDb db)
    {
        Godot.Collections.Dictionary spec = _CustomSpec(db);
        spec["name_cn"] = "自建剑客";
        Godot.Collections.Dictionary built = CreationService.build(db, spec);
        CheckTrue(built["ok"].AsBool(), $"自建路能建出存档：{(built.ContainsKey("error") ? built["error"].AsString() : "")}");
        GodotObject state = built["state"].AsGodotObject();
        string char_id = built["char_id"].AsString();
        CheckEq(char_id, CreationService.custom_char_id(), "自建角色用固定 id");
        var row = db.get_row("character_base", char_id) as CharacterBaseRow;
        Godot.Collections.Dictionary want_attrs = spec["attrs"].AsGodotDictionary();
        CheckNotNull(row, "自建模板注入进了 character_base（后面按 char_id 取模板的地方都要它）");
        CheckEq(row.initial_str, want_attrs["str"].AsInt32(), "七维按玩家分的存进模板");
        CheckEq(row.weapon_type, spec["weapon_type"].AsString(), "武器类型进模板");
        CheckTrue(state.Get("custom_templates").AsGodotDictionary().ContainsKey(char_id), "自建模板也进存档（读档要重新注入）");
        // 起始装备：该武器的凡品 ＋ 布衣
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        var equipped = new System.Collections.Generic.List<string>();
        foreach (string instance_id in inventory.Call("equipped_instances", db, char_id).AsStringArray())
        {
            equipped.Add(inventory.Call("base_of", instance_id).AsString());
        }

        CheckTrue(equipped.Count >= 2, $"起始装备发下来了（武器 + 布衣）：{string.Join("、", equipped)}");
        // 面板真的能按这份模板算
        var sheet = new CharacterSheet(db, state, char_id);
        CheckTrue(sheet.valid(), "面板认得这个自建角色");
        CheckEq(
            sheet.total_attrs()["str"].AsInt32(),
            want_attrs["str"].AsInt32(),
            "面板五维 = 自建模板的七维"
        );
    }

    /// <summary>天赋：花费上限、`attr:`／`stat:` 进属性贡献、`rule:` 由各自系统读。</summary>
    private void _CheckTalents(TableDb db)
    {
        CheckEq(TalentService.budget(db), 5, "天赋总点数 5（growth_const.talent_points）");
        CheckEq(TalentService.cards(db).Count, 24, "天赋池 24 条");
        CheckTrue(
            TalentService.remaining_points(db, new[] { "tal_tie_shen" }) == 4,
            "选了 1 点的「铁打身板」还剩 4 点"
        );
        CheckFalse(
            TalentService.can_pick(db, new[] { "tal_dan_shi" }, "tal_jiu_ming")["ok"].AsBool(),
            "3 + 3 超过 5 点：第二个选不上"
        );

        Godot.Collections.Dictionary built = CreationService.build(db, _Spec("origin", new Godot.Collections.Dictionary
        {
            { "origin_id", OriginScholar },
            { "name_cn", "书生" },
            { "talents", new Godot.Collections.Array { "tal_tie_shen", "tal_wu_chi", "tal_jia_di" } },
        }));
        CheckTrue(built["ok"].AsBool(), $"带天赋的创建方案合法：{(built.ContainsKey("error") ? built["error"].AsString() : "")}");
        GodotObject state = built["state"].AsGodotObject();
        string char_id = built["char_id"].AsString();
        // `rule:start_money` = 500：纯增益走资源，不进属性
        CheckEq(state.Get("inventory").AsGodotObject().Get("money").AsInt32(), 500, "「家底殷实」的起始铜钱进钱包（rule 类不进属性）");
        // `stat:res_internal` +0.10 与 `rule:mastery_gain` 0.30 各自归位
        var sheet = new CharacterSheet(db, state, char_id);
        double res_total = 0.0;
        foreach (Variant contribution_variant in sheet.contributions())
        {
            var contribution = contribution_variant.AsGodotDictionary();
            if (contribution.ContainsKey("target") && contribution["target"].AsString() == "res_internal")
            {
                res_total += contribution.ContainsKey("value") ? contribution["value"].AsDouble() : 0.0;
            }
        }

        CheckFloat(res_total, 0.10, "「铁打身板」的内伤抗性走属性贡献通道", 0.0001);
        CheckFloat(
            TalentService.rule_value(db, state, char_id, "mastery_gain", 0.0),
            0.30,
            "「武痴」的熟练度成长是 rule 类，由成长系统读"
        );
        // 存档往返：天赋列表跟着走
        GodotObject back = _RoundTrip(db, state);
        Godot.Collections.Dictionary picks = back.Get("talent_picks").AsGodotDictionary();
        int pick_count = picks.ContainsKey(char_id) ? picks[char_id].AsGodotArray().Count : 0;
        CheckEq(pick_count, 3, "天赋进存档、读回来还是 3 个");
    }

    // ---------------------------------------------------------------- 夹具与小工具

    /// <summary>
    /// 出身卡与确认汇总在**真界面**上长什么样（设计 13 §二／§六）：这两条是「给玩家做数值选择用」的，
    /// 只在纯逻辑层验「数据备齐了」不够——所以这里实例化真场景，读它真建出来的那两个 Label。
    /// </summary>
    private void _CheckScreenCards()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，创建界面冒烟无法进行");
            return;
        }

        TableDb db = GetDb();
        var screen = GD.Load<PackedScene>("res://scenes/creation_screen.tscn").Instantiate() as CreationScreen;
        CheckNotNull(screen, "创建界面场景挂得上 CreationScreen");
        if (screen == null)
        {
            return;
        }

        Tree.Root.AddChild(screen);
        screen.setup();
        var detail = screen.FindChild("OriginDetail_ori_scholar", true, false) as Label;
        CheckNotNull(detail, "出身卡有第二行（七维 ＋ 武器 ＋ 起始武学）");
        if (detail != null)
        {
            CheckTrue(detail.Text.Contains("█"), $"七维用条形图（设计 13 §六）：{detail.Text}");
            CheckTrue(detail.Text.Contains("剑"), "卡片写着武器名");
            CheckTrue(detail.Text.Contains("玄微剑法·起手"), "卡片写着起始武学名");
        }

        // 选书生那张卡 → 「名字」那一格预填的是**真名**，不是卡标题（设计 21 §七 第 5 条）
        screen.pick_origin(OriginScholar);
        var name_edit = screen.FindChild("NameEdit", true, false) as LineEdit;
        CheckNotNull(name_edit, "起名那一格在");
        if (name_edit != null)
        {
            CheckEq(name_edit.Text, "陆文昭", "预填的是真名（不是「家道失落的书生」）");
        }

        // 选一张卡之后，确认段的汇总要跟着变（设计 13 §六「确认页汇总全部选择」）
        screen.pick_origin(OriginGang);
        var summary = screen.FindChild("SummaryLabel", true, false) as Label;
        CheckNotNull(summary, "确认段有一块汇总");
        if (summary != null)
        {
            foreach (string want in new[] { "出身", "七维", "武器", "起始武学", "天赋", "名字" })
            {
                CheckTrue(summary.Text.Contains(want), $"汇总里有「{want}」：{summary.Text}");
            }

            CheckTrue(summary.Text.Contains("林铁山"), "选完出身，汇总里就是那个人");
            // 起始武学名从表里现取（不写死"应该是哪一招"——角色卡换了招式这条不该跟着红）
            var gang = db.get_row("character_base", "ch_gang") as CharacterBaseRow;
            string[] gang_skills = gang != null ? gang.skill_ids() : System.Array.Empty<string>();
            string first_skill = gang_skills.Length > 0 ? gang_skills[0] : "";
            var skill_row = first_skill.Length > 0 ? db.get_row("skill_base", first_skill) as SkillBaseRow : null;
            string expect_name = skill_row != null ? skill_row.name_cn : "";
            CheckTrue(
                expect_name.Length > 0 && summary.Text.Contains(expect_name),
                $"汇总里带着他的起始武学（{expect_name}）：{summary.Text}");
        }

        Tree.Root.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>拼一份创建方案：`route` 之外由 `fields` 补（对位 GDScript 的 `**fields` 用法）。</summary>
    private static Godot.Collections.Dictionary _Spec(string route, Godot.Collections.Dictionary fields)
    {
        var spec = new Godot.Collections.Dictionary { { "route", route } };
        foreach (Variant key in fields.Keys)
        {
            spec[key] = fields[key];
        }

        return spec;
    }

    private static Godot.Collections.Dictionary _CustomSpec(TableDb db)
    {
        Godot.Collections.Dictionary rules = CreationService.attr_rules(db);
        var attrs = new Godot.Collections.Dictionary();
        int left = rules["total"].AsInt32() - rules["min"].AsInt32() * 7;
        foreach (string attr_id in new[] { "str", "con", "agi", "int", "luk", "wu", "gen" })
        {
            int add = System.Math.Min(left, rules["max"].AsInt32() - rules["min"].AsInt32());
            attrs[attr_id] = rules["min"].AsInt32() + add;
            left -= add;
        }

        string weapon = CreationService.weapons(db)[0].AsGodotDictionary()["weapon_type"].AsString();
        Godot.Collections.Array options = CreationService.start_skill_options(db, weapon);
        return _Spec("custom", new Godot.Collections.Dictionary
        {
            { "name_cn", "自建" },
            { "attrs", attrs },
            { "weapon_type", weapon },
            { "start_skill_id", options.Count > 0 ? options[0].AsGodotDictionary()["skill_id"].AsString() : "" },
            { "talents", new Godot.Collections.Array() },
        });
    }

    /// <summary>走一遍真存档往返（与 SaveStore 同一套 `to_dict`／`from_dict`）。</summary>
    private static GodotObject _RoundTrip(TableDb db, GodotObject state) =>
        GameState.from_dict(state.Call("to_dict").AsGodotDictionary(), db);
}
