using System.Text.RegularExpressions;
using Godot;
using Lunhui.Core;
using Lunhui.Ui;
using Lunhui.World;

namespace Lunhui.Tests;

/// <summary>
/// 玩家可见文案审计（换对象审计的产物）。对位 `tests/test_copy_audit.gd`
/// （2026-10-11 P4 第四十二批整体搬 C#）。
///
/// 起因：功能一路往前走，文案却留在原地——启动菜单副标题写着「玩法未接入，当前为框架骨架」，
/// 而那时大地图、明雷、战斗、行囊、商店、副本、图鉴全都接进游戏了；玩家看到的第一句话是假话。
/// 这个用例把这类错误变成**绊线**：
///   ① `src/` 下任何源码都不许再出现那几句过时断言（含注释）；
///   ② 真实场景里的关键文案必须真的说到点上（菜单说清是哪一章、能玩什么）；
///   ③ 招式用不了的原因必须是中文名，不能把表里的英文 id 甩到战斗界面上；
///   ④ 玩家可见文案里不许出现「表内 id 形态」与「表名」。
/// </summary>
public partial class TestCopyAudit : TestCase
{
    private const string MenuScene = "res://scenes/main_menu.tscn";
    private const string PlaceholderScene = "res://scenes/placeholder_game.tscn";
    private const string TestDir = "res://.logs/test_copy_audit";
    private const int Seed = 20261003;

    /// <summary>已经查实是谎话的句子。加进来之前先确认代码里真的没有这个功能缺口。</summary>
    private static readonly string[] BANNED_COPY =
    {
        "玩法未接入", // 大地图／明雷／战斗／行囊都已接入
        "玩法还没接",
        "尚未接入", // 游戏内枢纽页曾这么写
        "架势系统未实现", // 架势与拆招在 0.6.x 已经实现
    };

    /// <summary>
    /// 表名静态扫描的**白名单**：这里出现的表名**只进日志或只做内部判断**，玩家看不到。
    /// 每条都要写清「为什么玩家看不到」；**补上了就要删掉那一行**（双向维护）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> TableNameInTextAllowed = new()
    {
        ["PracticeService.cs"] =
            "data_ready() 的 error 串只进 push_error 与返回值；界面上玩家看到的是调用方自己那句"
            + "「木桩还没准备好（缺一条配置，已记进日志）」（local_map_controller.start_practice）",
    };

    /// <summary>「id → 中文名」的唯一入口（`TableDb.display_name`）＋ ① 不许留过时文案。</summary>
    private static readonly string[] IdTokenPattern = { "[a-z][a-z0-9]*_[a-z0-9_]+" };

    public override string SuiteName() => "玩家可见文案审计";

    public override void Run()
    {
        _CheckNoBannedCopyInSource();
        _CheckDisplayNameHelper();
        _CheckHudMentionsEveryKey();
        _CheckMenuCopy();
        _CheckHubCopy();
        _CheckStaleSaveLabels();
        _CheckUnsupportedDamageReason();
        _CheckSurprisePendingRule();
        _CheckNoTableNamesInPlayerText();
    }

    /// <summary>
    /// 1.2) HUD 必须把**可用的按键**说清楚：控制器源码里 `IsActionPressed("xxx")` 用到的动作，
    /// 它的按键标签要出现在同一个文件的 HUD 提示行里（键位来自 `InputSetup.ACTIONS`，
    /// 标签用 `OS.get_keycode_string` 反推；一个动作有多个键时只要广告其中一个）。
    ///
    /// 起因：线索本（K）在**两个** HUD 提示里都没写——玩家只能靠猜，而设计 03 要求
    /// 「线索必须能被找到」。这类「功能做了但没人知道你按哪个键」的缺口，用这条机械钉住。
    /// </summary>
    private void _CheckHudMentionsEveryKey()
    {
        string[] controllers =
        {
            "res://src/world/OverworldController.cs",
            "res://src/world/LocalMapController.cs",
        };
        int checked_actions = 0;
        foreach (string path in controllers)
        {
            string source = FileAccess.GetFileAsString(path);
            string hint = _HudHintText(source);
            CheckFalse(hint.Length == 0, $"{path} 能定位到 HUD 提示行");
            var actions = _HandledActions(source);
            CheckGt(actions.Count, 0.0, $"{path} 能认出它处理了哪些按键动作");
            foreach (string action in actions)
            {
                var labels = _KeyLabels(action);
                if (labels.Count == 0)
                {
                    continue; // ui_cancel 这类不在 InputSetup.ACTIONS 里的动作，不管
                }

                checked_actions += 1;
                bool found = false;
                foreach (string label in labels)
                {
                    if (hint.Contains(label))
                    {
                        found = true;
                    }
                }

                CheckTrue(
                    found,
                    $"{System.IO.Path.GetFileName(path)} 的 HUD 提示里写了「{action}」的按键"
                        + $"（{string.Join("／", labels)}）：{hint.Replace("\n", " ⏎ ")}");
            }
        }

        CheckGt(checked_actions, 6.0, $"覆盖到多个按键动作（{checked_actions} 个）");
    }

    /// <summary>
    /// 源码里 `IsActionPressed("xxx")` / `IsActionJustPressed("xxx")` 用到的动作名。
    /// **GDScript 侧写得不一样**（`is_action_pressed("xxx")`），所以两种写法都认。
    /// </summary>
    private static System.Collections.Generic.List<string> _HandledActions(string source)
    {
        var regex = new Regex("(?:is_action_(?:just_)?pressed|IsAction(?:Just)?Pressed)\\(\"([a-z_]+)\"\\)");
        var out_actions = new System.Collections.Generic.List<string>();
        foreach (Match found in regex.Matches(source))
        {
            string action = found.Groups[1].Value;
            if (!out_actions.Contains(action))
            {
                out_actions.Add(action);
            }
        }

        return out_actions;
    }

    /// <summary>动作对应的按键标签（一个动作可能绑了多个键）。</summary>
    private static System.Collections.Generic.List<string> _KeyLabels(string action)
    {
        var out_labels = new System.Collections.Generic.List<string>();
        Godot.Collections.Dictionary actions = InputSetup.actions();
        if (!actions.ContainsKey(action))
        {
            return out_labels;
        }

        foreach (Variant keycode in actions[action].AsGodotArray())
        {
            out_labels.Add(OS.GetKeycodeString((Key)keycode.AsInt64()));
        }

        return out_labels;
    }

    /// <summary>
    /// HUD 提示那几行（`_status.Text = …` 与 `string hint =` / `hint += …`）。
    /// C# 的多行拼接是 `string hint = $"…"` 后面跟几行 `+ "…"`，按键标签往往只写在续行里——
    /// 不把续行一起收进来就会漏（2026-10-11 小地图搬 C# 时踩到）。
    /// </summary>
    private static string _HudHintText(string source)
    {
        var parts = new System.Collections.Generic.List<string>();
        bool in_hint_decl = false;
        foreach (string line in source.Split('\n'))
        {
            if (line.Contains("_status.text =") || line.Contains("_status.Text =")
                || line.Contains("hint :=") || line.Contains("string hint =") || line.Contains("hint +="))
            {
                parts.Add(line.Trim());
                in_hint_decl = line.Contains("string hint =") || line.Contains("hint :=");
                continue;
            }

            if (in_hint_decl && line.Trim().StartsWith("+"))
            {
                parts.Add(line.Trim());
                continue;
            }

            in_hint_decl = false;
        }

        return string.Join("\n", parts);
    }

    /// <summary>1.5) 「id → 中文名」的唯一入口：界面文案都靠它，所以它自己也要钉住。</summary>
    private void _CheckDisplayNameHelper()
    {
        TableDb db = GetDb();
        CheckEq(db.display_name("item_pickaxe"), "铁镐", "道具说中文名");
        CheckEq(db.display_name("eq_sword_01"), "铁剑", "装备说中文名");
        CheckEq(db.display_name("eq_sword_01#1"), "铁剑", "装备实例 id 会剥掉 #序号");
        CheckEq(
            db.display_name("en_bd_boss"), db.get_row("enemy_base", "en_bd_boss").Get("name_cn").AsString(),
            "敌人说中文名");
        CheckEq(db.display_name("hf3_dungeon"), "后山地牢", "房间取 room_name");
        CheckEq(db.display_name("dmg_normal"), "非暴击直伤", "伤害类型说中文名");
        CheckEq(db.display_name("查无此人"), "查无此人", "查不到就原样返回（不许编假名字）");
        CheckEq(db.display_name(""), "", "空 id 返回空串");
    }

    /// <summary>① 源码里不许再留着那几句过时断言（注释也算，注释是下一个接手的人唯一的地图）。</summary>
    private void _CheckNoBannedCopyInSource()
    {
        var files = _SourceFiles("res://src");
        CheckGt(files.Count, 20.0, $"能递归扫到 src/ 下的 .gd／.cs（扫到 {files.Count} 个）");
        var hits = new System.Collections.Generic.List<string>();
        foreach (string path in files)
        {
            string text = FileAccess.GetFileAsString(path);
            foreach (string phrase in BANNED_COPY)
            {
                if (text.Contains(phrase))
                {
                    hits.Add($"{path} ← 「{phrase}」");
                }
            }
        }

        CheckEq(hits.Count, 0, $"src/ 下没有过时文案：{string.Join(", ", hits)}");
    }

    /// <summary>② 启动菜单：副标题得说清是哪一章、这一版能玩什么。</summary>
    private void _CheckMenuCopy()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，菜单文案检查无法进行");
            return;
        }

        SaveStore store = SaveStore.create(TestDir, 3);
        store.ensure_dir();
        var menu = GD.Load<PackedScene>(MenuScene).Instantiate() as MainMenu;
        menu.store_override = store;
        Tree.Root.AddChild(menu);
        menu.setup();

        var subtitle = menu.FindChild("Subtitle", true, false) as Label;
        CheckNotNull(subtitle, "菜单有副标题标签");
        if (subtitle != null)
        {
            string text = subtitle.Text;
            CheckTrue(text.Contains("黑风寨"), $"副标题说清是第一章哪一段：{text}");
            CheckTrue(text.Contains("大地图"), $"副标题点名大地图这个已接入的玩法：{text}");
            CheckTrue(text.Contains("战斗"), $"副标题点名战斗这个已接入的玩法：{text}");
        }

        // 菜单的三个按钮是本轮的硬功能，文案一起钉住（改名就得有意为之）
        var new_button = menu.FindChild("NewGameButton", true, false) as Button;
        var load_button = menu.FindChild("LoadGameButton", true, false) as Button;
        var quit_button = menu.FindChild("QuitButton", true, false) as Button;
        CheckEq(new_button != null ? new_button.Text : "", "新建游戏", "新建按钮文案");
        CheckEq(load_button != null ? load_button.Text : "", "读取存档", "读取按钮文案");
        CheckEq(quit_button != null ? quit_button.Text : "", "退出游戏", "退出按钮文案");

        _CheckNoIdTokens(menu, "启动菜单");
        Tree.Root.RemoveChild(menu);
        menu.Free();
    }

    /// <summary>③ 游戏内枢纽：提示条要指路，不能再写「尚未接入」。</summary>
    private void _CheckHubCopy()
    {
        if (Tree == null)
        {
            Fail("没有注入场景树，枢纽文案检查无法进行");
            return;
        }

        var hub = GD.Load<PackedScene>(PlaceholderScene).Instantiate() as PlaceholderGame;
        Tree.Root.AddChild(hub);
        hub.setup();
        var hint = hub.FindChild("Hint", true, false) as Label;
        CheckNotNull(hint, "枢纽页有提示标签");
        if (hint != null)
        {
            CheckTrue(hint.Text.Contains("大地图"), $"提示条指向大地图：{hint.Text}");
            CheckTrue(hint.Text.Contains("战斗"), $"提示条提到战斗：{hint.Text}");
        }

        _CheckNoIdTokens(hub, "游戏内枢纽");
        Tree.Root.RemoveChild(hub);
        hub.Free();
    }

    /// <summary>
    /// ⑤ 玩家可见的每一段文案里**不许出现「表内英文 id」那种形态**
    /// （`chapter_01`／`eq_sword_01`／`facility_pawnshop`…）。机械判据：小写字母打头、中间带下划线的 token。
    ///
    /// 这一条是**活的**：2026-10-03 枢纽页那行「章节：第 1 章（chapter_01）」就把章节 id 甩给了玩家——
    /// 当时的审计只钉了「不许说假话」，没钉「不许漏 id」（见框架说明决策 242）。
    /// 中文标点、`Lv1`、`F5`、`Tab`、`Esc` 这些都不带下划线，不会被误伤。
    /// </summary>
    private void _CheckNoIdTokens(Node node, string where)
    {
        var token_re = new Regex(IdTokenPattern[0]);
        var offenders = new System.Collections.Generic.List<string>();
        foreach (string text in _VisibleTexts(node))
        {
            if (token_re.IsMatch(text))
            {
                offenders.Add(text.Trim());
            }
        }

        CheckEq(
            offenders.Count, 0,
            $"{where} 的可见文案里不许出现表内 id 形态（往表里要中文名，id 只留给日志）：{string.Join("；", offenders)}");
    }

    /// <summary>某个界面下所有 Label／Button／RichTextLabel 的文字（递归）。</summary>
    private static System.Collections.Generic.List<string> _VisibleTexts(Node node)
    {
        var out_texts = new System.Collections.Generic.List<string>();
        foreach (Node child in node.GetChildren())
        {
            if (child is Label label)
            {
                out_texts.Add(label.Text);
            }
            else if (child is Button button)
            {
                out_texts.Add(button.Text);
            }
            else if (child is RichTextLabel rich)
            {
                out_texts.Add(rich.Text);
            }

            out_texts.AddRange(_VisibleTexts(child));
        }

        return out_texts;
    }

    /// <summary>
    /// ⑥ 存档里出现**配表里没有的** id 时，标签同样不许把 id 摆给玩家。
    ///
    /// 现实里发生过：设计从 `character_base` 里删过角色（读档要剔掉「已下架角色」）；难度改名同理。
    /// 这类标签出现在**启动菜单的存档列表**与**枢纽页状态行**上，都是玩家可见的。
    /// </summary>
    private void _CheckStaleSaveLabels()
    {
        TableDb db = GetDb();
        var state = (GameState)SoloState(db);
        state.char_ids = new Godot.Collections.Array { "scholar_gone" };
        state.char_levels = new Godot.Collections.Dictionary { { "scholar_gone", 3 } };
        state.difficulty_id = "difficulty_gone";
        string label = state.short_label(db);
        CheckFalse(label.Contains("scholar_gone"), $"下架角色不进存档列表标签：{label}");
        CheckFalse(label.Contains("difficulty_gone"), $"未知难度不进存档列表标签：{label}");
        CheckTrue(label.Contains("已下架"), $"如实说明这个角色已下架：{label}");
        CheckTrue(label.Contains("未知难度"), $"如实说明难度不认识：{label}");
        string joined = string.Join("\n", state.summary_lines(db));
        CheckFalse(joined.Contains("scholar_gone"), $"枢纽页状态行也不漏角色 id：{joined}");
        CheckFalse(joined.Contains("difficulty_gone"), $"枢纽页状态行也不漏难度 id：{joined}");
    }

    /// <summary>
    /// ④ 招式用不了的原因说中文名，别让玩家看表里的 id；**DoT 类招式是能用的**（不是"未接结算"）。
    ///
    /// 由来（2026-10-03）：这里原来断言 `_is_supported_damage("dot_poison") == false`——那在 DoT
    /// 里程碑落地**之前**是对的，之后就成了**替一个过期的过滤条件站台**。现在这条改成：
    /// DoT 可用、只有反伤（`dmg_reflect`）不可用；真正不可用时那条中文原因也照样验。
    /// </summary>
    private void _CheckUnsupportedDamageReason()
    {
        TableDb db = GetDb();
        BattleActor hero = _Actor("hero", BattleActor.side_ally(), null);
        hero.skills = new[] { "sk_wudu_02" }; // dot_poison，中毒
        BattleActor foe = _Actor(
            "foe", BattleActor.side_enemy(),
            new Godot.Collections.Dictionary { { "hp_max", 9999.0 } });
        BattleSimulator sim = BattleSimulator.create(db, RngService.create(Seed));
        sim.setup(
            new Godot.Collections.Array { hero },
            new Godot.Collections.Array { foe },
            new Godot.Collections.Dictionary
            {
                {
                    "modifiers",
                    new Godot.Collections.Dictionary
                    {
                        { "force_hit", true }, { "no_variance", true }, { "no_block", true },
                    }
                },
            });

        Godot.Collections.Array options = sim.skill_options(hero);
        CheckEq(options.Count, 1, "中毒招式列出来了");
        if (options.Count > 0)
        {
            Godot.Collections.Dictionary option = options[0].AsGodotDictionary();
            string reason = option["reason"].AsString();
            CheckTrue(option["ok"].AsBool(), $"DoT 类招式**可用**（五毒掌那一路要能点出来）：{reason}");
            CheckEq(reason, "", "可用时不给原因");
        }

        CheckTrue(sim._is_supported_damage("dmg_normal"), "直伤仍然可用（对照，证明不是把所有招都判死了）");
        CheckTrue(sim._is_supported_damage("dot_poison"), "中毒类可用（结算早就有了）");
        CheckTrue(sim._is_supported_damage("dot_burn"), "灼伤类可用");
        CheckFalse(sim._is_supported_damage("dmg_reflect"), "反伤仍然不可用（只有它真的没接）");

        // 真正不可用时，那句原因必须用中文名、不许漏表内 id
        string reflect_reason = sim._unsupported_damage_reason("dmg_reflect");
        CheckTrue(reflect_reason.Contains("反伤"), $"不可用的原因用中文名：{reflect_reason}");
        CheckFalse(reflect_reason.Contains("dmg_"), $"原因不泄露表里的英文 id：{reflect_reason}");
    }

    /// <summary>⑤ 奇袭的暂缓规则说明：说「等设计定效果」，不甩锅给已经做完的架势系统。</summary>
    private void _CheckSurprisePendingRule()
    {
        TableDb db = GetDb();
        Resource spawn = db.get_row("roaming_spawn", "sp_lp_wolf_01");
        Resource team = db.get_row("enemy_team", "team_wolf_pack");
        Encounter surprise = Encounter.build(db, spawn, team, Encounter.contact_sleep(), "normal");
        CheckEq(surprise.pending_rules.Length, 1, "奇袭只挂一条待定规则（敌方无防备）");
        if (surprise.pending_rules.Length == 0)
        {
            return;
        }

        string rule = surprise.pending_rules[0];
        CheckTrue(rule.Contains("无防备"), $"规则文案点名「无防备」：{rule}");
        CheckTrue(rule.Contains("设计"), $"规则文案说明是设计侧没定效果：{rule}");
        CheckFalse(rule.Contains("架势"), $"不再把责任推给已经实现的架势系统：{rule}");
    }

    /// <summary>
    /// ⑥ **玩家可见文案里不许出现表名**——场景自检量不到的那一半，用静态扫描兜住。
    ///
    /// 由来（2026-10-04，决策 330）：上一轮抓到 NPC 兜底那句把表名 `npc_def` 甩给玩家，靠的是
    /// **逐个交互路径**（五个站位逐个按一遍）。可这类泄漏散在**没人走到的分支**里——数据错路径、
    /// 道具列表的说明、世界事件的提示……场景自检是「跑到哪儿扫哪儿」：跑不到的分支它永远报 0。
    /// 所以这里换成**按对象扫**：把 `data/tables/*.csv` 的表名当词表（唯一真相是数据目录本身，
    /// 不手抄一份），扫 `src/` 里的**字符串字面量**，只要一句**中文**里夹着表名就报出来。
    ///
    /// 三条排除（都写死在判据里，不靠人记）：`TableValidator*`（构建期消息天生给开发看）、
    /// 同一行有日志调用的、以 `"[` 开头的句子（这个工程的日志一律带 `[模块]` 前缀）。
    /// 剩下的若确实是内部用途，写进 `TableNameInTextAllowed` 并写明理由；**补上就要删行**。
    /// </summary>
    private void _CheckNoTableNamesInPlayerText()
    {
        var table_names = _TableNames();
        CheckGt(table_names.Count, 30.0, $"从 data/tables 读到足够多的表名（{table_names.Count} 个）");
        string alt = string.Join("|", table_names);
        var tok_re = new Regex($"\\b({alt})\\b");
        var lit_re = new Regex("\"([^\"]*)\"|'([^']*)'");
        var hits = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
        foreach (string path in _SourceFiles("res://src"))
        {
            // 构建期校验器：`error 列表` 里的文案只进日志与验收输出，玩家一个字都看不到。
            // 原来这里写死跳过 `TableValidator.cs`（单份）；扫描面扩到 `.cs` 之后那一条挡不住
            // 另外几份（会假红），所以改成按前缀挡整族。
            if (System.IO.Path.GetFileName(path).StartsWith("TableValidator"))
            {
                continue;
            }

            string text = FileAccess.GetFileAsString(path);
            if (text.Length == 0)
            {
                continue;
            }

            int line_no = 0;
            foreach (string raw in text.Split('\n'))
            {
                line_no += 1;
                if (raw.Contains("push_error") || raw.Contains("push_warning")
                    || raw.Contains("printerr") || raw.Contains("print(")
                    || raw.Contains("PushError") || raw.Contains("PushWarning")
                    || raw.Contains("Printerr") || raw.Contains("Print("))
                {
                    continue;
                }

                string code = raw;
                int hash = code.IndexOf('#');
                if (hash >= 0)
                {
                    code = code.Substring(0, hash);
                }

                foreach (Match m in lit_re.Matches(code))
                {
                    string lit = m.Value;
                    if (!_HasCjk(lit) || !tok_re.IsMatch(lit))
                    {
                        continue;
                    }

                    if (lit.StartsWith("\"["))
                    {
                        continue; // `"[模块] …"` 是日志句
                    }

                    string key = System.IO.Path.GetFileName(path);
                    if (!hits.ContainsKey(key))
                    {
                        hits[key] = new System.Collections.Generic.List<string>();
                    }

                    hits[key].Add($"{line_no} ← {lit}");
                }
            }
        }

        var bad = new System.Collections.Generic.List<string>();
        foreach (string file_name in hits.Keys)
        {
            if (_AllowedFile(file_name))
            {
                continue;
            }

            bad.Add($"{file_name}:{string.Join(", ", hits[file_name])}");
        }

        CheckEq(
            bad.Count, 0,
            "这些中文文案里夹着表名（玩家看得到吗？真看得到就改人话＋表名进日志；内部用途就写进 TABLE_NAME_IN_TEXT_ALLOWED 并写明理由）："
                + string.Join("；", bad));

        // 白名单**双向**维护：修好了／删了那段代码就要把那行删掉（过期白名单 = 下一个人照着它白找）
        var stale = new System.Collections.Generic.List<string>();
        foreach (string allowed_name in TableNameInTextAllowed.Keys)
        {
            if (!hits.ContainsKey(allowed_name))
            {
                stale.Add(allowed_name);
            }
        }

        CheckEq(stale.Count, 0, $"白名单里这些文件已经没有夹表名的文案了，请删掉对应行：{string.Join("、", stale)}");
    }

    private static bool _HasCjk(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            int code = text[index];
            if (code >= 0x4E00 && code <= 0x9FFF)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>从数据目录本身读表名——**别在用例里手抄一份**（加一张表就漏一个词）。</summary>
    private static System.Collections.Generic.List<string> _TableNames()
    {
        var out_names = new System.Collections.Generic.List<string>();
        DirAccess dir = DirAccess.Open("res://data/tables");
        if (dir == null)
        {
            return out_names;
        }

        dir.ListDirBegin();
        string entry = dir.GetNext();
        while (entry.Length > 0)
        {
            if (!dir.CurrentIsDir() && entry.EndsWith(".csv"))
            {
                out_names.Add(entry.GetBaseName());
            }

            entry = dir.GetNext();
        }

        dir.ListDirEnd();
        return out_names;
    }

    /// <summary>白名单里记的是完整路径；命中表里只留文件名，这里把两边对上。</summary>
    private static bool _AllowedFile(string file_name) => TableNameInTextAllowed.ContainsKey(file_name);

    /// <summary>
    /// 递归收集 `res://` 下的 `.gd` 与 `.cs`（不靠 `.godot` 缓存，纯文件系统遍历）。
    ///
    /// **两种语言都收**：搬成 C# 之后只扫 `.gd` 的话这一族门限会**整个失效**——
    /// 2026-10-10 搬 `practice_service` 时撞上：`PracticeService.cs` 里那句带表名的 error 串
    /// 会直接漏过，而白名单那行会因为「扫不到了」被双向维护检查报成过期。
    /// </summary>
    private static System.Collections.Generic.List<string> _SourceFiles(string root)
    {
        var out_files = new System.Collections.Generic.List<string>();
        var stack = new System.Collections.Generic.Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir_path = stack.Pop();
            DirAccess dir = DirAccess.Open(dir_path);
            if (dir == null)
            {
                continue;
            }

            dir.ListDirBegin();
            string entry = dir.GetNext();
            while (entry.Length > 0)
            {
                string full = dir_path.PathJoin(entry);
                if (dir.CurrentIsDir())
                {
                    stack.Push(full);
                }
                else if (entry.EndsWith(".gd") || entry.EndsWith(".cs"))
                {
                    out_files.Add(full);
                }

                entry = dir.GetNext();
            }

            dir.ListDirEnd();
        }

        return out_files;
    }

    private static BattleActor _Actor(string id, int side, Godot.Collections.Dictionary overrides)
    {
        var actor = new BattleActor
        {
            actor_id = id,
            display_name = id,
            side = side,
            level = 10,
            base_accuracy = 1.0,
            poise_max = 100,
            stats = new Godot.Collections.Dictionary
            {
                { "hp_max", 300.0 }, { "atk_phys", 40.0 }, { "atk_qi", 40.0 },
                { "def_phys", 0.0 }, { "def_qi", 0.0 },
                { "speed", 10.0 }, { "hit_rate", 0.5 }, { "dodge_rate", 0.0 },
                { "crit_rate", 0.0 }, { "crit_dmg", 0.0 },
                { "qi_max", 100.0 }, { "qi_regen", 1.0 }, { "hp_regen", 0.0 },
                { "pen_rate", 0.0 }, { "block_rate", 0.0 },
                { "block_reduction", 0.0 }, { "dmg_reduction", 0.0 },
                { "debuff_chance", 0.0 }, { "debuff_power", 0.0 },
                { "poise_break", 0.0 },
            },
        };
        if (overrides != null)
        {
            foreach (Variant key in overrides.Keys)
            {
                actor.stats[key.AsString()] = overrides[key];
            }
        }

        actor.refill();
        return actor;
    }
}
