using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// NPC 交往面板（设计 19 §三）：基本信息 ＋ 交互菜单（赠送／切磋／偷窃／兑换／委托）。
/// C# 版，对位原 `src/ui/npc_panel.gd`（760 行）。
///
/// 19 号要求「信息面板与交互菜单分开：按一个键看信息，按另一个键进入交互」——
/// 两个键各是哪个设计没写（`待策划确认.md` Q62），所以这里按「一个面板、信息在最上面」实现：
/// **看信息不需要点任何一次有风险的操作**（打开就能看见），交互项在下面分开列。
/// 等设计点完键位，把信息那一段抽成独立浮层即可（一份数据两处渲染）。
///
/// 界面用代码搭（占位美术阶段的既有约定）。所有规则在 `NpcService` 里，这里只显示与转发。
///
/// **跨语言面**：`NpcService` 2026-10-11 已搬 C#（直接调静态口，不再走 `Call`）；
/// `DialogueService`／`RecruitService`／`RngService`／`TableDb`／`Inventory` 也都是 C#，直调；
/// `GameState` 也已搬 C#（2026-10-11，建局走静态口 `GameState.new_game`，旗标仍走 `Call`）。
/// 两个回调（`return_handler`／`spar_handler`）收 `Callable`（P5 起 GDScript 已清零，不再需要跨语言桥）。
/// 用例直接读 `_actions`／`_favor_label`，所以那两处必须是 `[Export]`。
///
/// 为了守 800 行硬上限，这个类拆成三份 partial：本文件＝骨架／刷新／谈话段／五个动作／助手，
/// `NpcPanelSections.cs`＝其余四个交往段，`NpcPanelSelfTest.cs`＝真实场景自检
/// （棘轮表盯的是**自检那一份**，见 `tests/TestHandshakeFloors.cs`）。
/// </summary>
public partial class NpcPanel : Control
{
    /// <summary>自检与用例的注入点</summary>
    [Export] public Variant state_override = default;

    /// <summary>
    /// 夹具注入用的表（与 `shop_screen.db_override` 同款）：用例要拿**改过的内存副本**开面板
    /// （例：给某个 NPC 临时配一支 `spar_team_id` 验「切磋」按钮的接线），不能只靠 GameData 那份。
    /// </summary>
    [Export] public Variant db_override = default;

    [Export] public string npc_id = "";

    /// <summary>
    /// 三种模式（设计 0.28.0 的 Q62：**E 进交互、Q 看信息**）：
    ///   `info`     = **只读**信息面板：名字／称号／等级／好感／介绍，Esc 返回，**不带任何副作用**；
    ///   `interact` = 交互菜单：赠送／切磋／偷窃／兑换／任务。
    /// </summary>
    public const string MODE_INFO = "info";

    public const string MODE_INTERACT = "interact";

    /// <summary>
    /// 第三种模式（2026-10-04）：「旁白／自白」——**只有台词与选项**，没有「这个人」。
    ///
    /// 为什么需要它：设计 20 号 §3.1 的**序幕·择念**写的是主角自己心里那一句
    /// （原话：「这一句，是主角第一次**替自己**开口，而不是接 NPC 的话」），
    /// 而交往面板的框里全是「这个人是谁、好感多少、能不能偷」——把主角的自白套进去，
    /// 等于给他安了一张好感条与一条偷窃难度。所以这里按数据同一套（`dialogue_node`／
    /// `dialogue_option`）渲染，只把「人」那一段整个收起来。
    /// </summary>
    public const string MODE_STORY = "story";

    [Export] public string mode = MODE_INTERACT;

    /// <summary>返回回调（P5 起 GDScript 已清零，直接用 `Callable`）。</summary>
    [Export] public Callable return_handler;

    /// <summary>切磋要开一场仗——由场景控制器接（它管遭遇与切场景）</summary>
    [Export] public Callable spar_handler;

    [Export] public Variant rng_override = default;

    /// <summary>击杀方式（`npc_quest` 的 `kill_style:` 条件要它；调用方从会话的上一场战斗里取）</summary>
    [Export] public Variant kill_styles = new Godot.Collections.Dictionary();

    /// <summary>当前停在哪个对话节点（空 = 从头开始；面板每次 `refresh()` 都按它取那一句）</summary>
    [Export] public string dialogue_node_id = "";

    public TableDb db;

    private Label _name_label;
    private Label _info_label;

    /// <summary>用例直接读 `panel.Get("_favor_label")`（旁白模式那两条），所以对 GDScript 要可见 → `[Export]`</summary>
    [Export] public Label _favor_label;

    private Label _favor_ticks;

    /// <summary>
    /// 头像：**原生 32×32、显示 2 倍到 64×64**，放左上角，与名字／称号／等级／好感同排
    /// （设计 14 §「NPC 信息面板的结构」——这几项是「这人是谁」的一组信息，不要拆到两处）。
    /// </summary>
    private TextureRect _avatar;

    private Label _status;

    /// <summary>面板上那一排按钮／文案的容器。用例按它找按钮（`_actions(panel).FindChild(…)`） → `[Export]`</summary>
    [Export] public VBoxContainer _actions;

    private GodotObject _rng;

    public override void _Ready()
    {
        setup();
        if (_has_user_arg("--npc-selftest"))
        {
            Callable.From(_run_npc_selftest).CallDeferred();
        }
    }

    /// <summary>幂等：用例可以直接调</summary>
    public void setup()
    {
        if (db != null)
        {
            return;
        }

        db = _resolve_db();
        // 没有会话状态时**自己开一局**（与 `clue_screen`／`character_screen` 同一套）：
        // 面板要读好感、委托、事件判定，全都挂在 `GameState` 上；state 为 null 时
        // 那些调用会报 `Nil` 的运行期错误，而面板看起来还是开着的（自检会红在日志门限上）。
        if (current_state() == null)
        {
            Node session = _session_node();
            if (session != null)
            {
                session.Call("set_state", GameState.new_game(db, "normal", System.Array.Empty<string>(), 0));
            }
            else
            {
                state_override = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
            }
        }

        _build_ui();
        refresh();
    }

    private TableDb _resolve_db()
    {
        if (db_override.VariantType != Variant.Type.Nil)
        {
            return db_override.AsGodotObject() as TableDb;
        }

        GodotObject game_data = _session_node("GameData");
        GodotObject game_db = game_data?.Get("db").AsGodotObject();
        if (game_db != null && game_db.Get("tables").AsGodotDictionary().Count > 0)
        {
            return game_db as TableDb;
        }

        var table_db = new TableDb();
        table_db.load_all();
        return table_db;
    }

    private Node _session_node(string node_name = "GameSession") => _tree()?.Root.GetNodeOrNull(node_name);

    /// <summary>不在场景树里时 `get_tree()` 会打一条 ERROR（`--script` 模式用例直接 new 节点就会踩到）。</summary>
    private SceneTree _tree()
    {
        if (IsInsideTree())
        {
            SceneTree tree = GetTree();
            if (tree != null)
            {
                return tree;
            }
        }

        return Engine.GetMainLoop() as SceneTree;
    }

    public GodotObject current_state()
    {
        if (state_override.VariantType != Variant.Type.Nil)
        {
            return state_override.AsGodotObject();
        }

        return _session_node()?.Get("state").AsGodotObject();
    }

    public GodotObject rng()
    {
        if (rng_override.VariantType != Variant.Type.Nil)
        {
            return rng_override.AsGodotObject();
        }

        _rng ??= RngService.create_random();
        return _rng;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            leave();
        }
    }

    public void leave()
    {
        if (_callable_valid(return_handler))
        {
            _invoke_callback(return_handler);
        }
    }

    // ------------------------------------------------------------------ 界面

    /// <summary>界面配件（卡片、色值）走共享构件</summary>
    private void _build_ui()
    {
        SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var bg = new ColorRect { Name = "Backdrop" };
        // 盖在场景上的面板要够不透明（版式预算会查 0.9）；色值只在主题里一处
        bg.Color = UiKit.color("backdrop");
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
        var margin = new MarginContainer { Name = "Margin" };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 32);
        }

        foreach (string side in new[] { "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        }

        AddChild(margin);
        var column = new VBoxContainer { Name = "Column" };
        margin.AddChild(column);

        var head = new HBoxContainer { Name = "Head" };
        head.AddThemeConstantOverride("separation", 10);
        column.AddChild(head);
        _avatar = new TextureRect
        {
            Name = "Avatar",
            CustomMinimumSize = new Vector2(64, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        head.AddChild(_avatar);
        var who = new VBoxContainer { Name = "Who" };
        who.AddThemeConstantOverride("separation", 2);
        head.AddChild(who);
        _name_label = new Label { Name = "NameLabel" };
        // 名字／称号／等级那一行＝面板的界面标题档（设计 15 §4.4：标题 24／正文 12，16 档已取消）
        _name_label.AddThemeFontSizeOverride("font_size", 24);
        who.AddChild(_name_label);
        _favor_label = new Label { Name = "FavorLabel" };
        who.AddChild(_favor_label);
        // 好感条的**档位刻度**（设计 20 §十 的三个档位：并肩／知心／生死）：只给条不给刻度，
        // 玩家看不出「还差多少能换东西」（设计 19 §三 的原话就是为这个）。
        _favor_ticks = new Label { Name = "FavorTicks" };
        who.AddChild(_favor_ticks);
        _info_label = new Label
        {
            Name = "InfoLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(720, 0),
        };
        column.AddChild(_info_label);

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);
        _actions = new VBoxContainer
        {
            Name = "Actions",
            CustomMinimumSize = new Vector2(720, 0),
        };
        scroll.AddChild(_actions);

        _status = new Label
        {
            Name = "Status",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(720, 0),
        };
        column.AddChild(_status);
        var back = new Button
        {
            Name = "BackButton",
            Text = "离开（Esc）",
            CustomMinimumSize = new Vector2(200, 40),
        };
        back.Pressed += leave;
        column.AddChild(back);
    }

    public void refresh()
    {
        // 旁白／自白模式（序幕择念）：**没有「这个人」**——不查身份、不摆好感条与交往段
        if (mode == MODE_STORY)
        {
            _refresh_story();
            return;
        }

        // 「人」的统一取法：NPC 或**同伴**（同伴没有 npc_def 行，服务层按同一形状合成一行）
        NpcDefRow def = _person_of(npc_id);
        if (def == null)
        {
            _name_label.Text = "（不认识这个人）";
            _info_label.Text = "";
            _favor_label.Text = "";
            _favor_ticks.Text = "";
            return;
        }

        int favor = _favor_of(current_state());
        // 名字/称号/等级/好感是**一组**信息（14 §「NPC 信息面板的结构」）
        _name_label.Text = $"{def.name_cn}　{def.title_cn}　Lv.{def.level}";
        _favor_label.Text = $"好感 {favor}　{_favor_bar(favor)}";
        _favor_ticks.Text = NpcService.favor_tier_text();
        _info_label.Text = def.info_text_cn;
        _refresh_avatar();
        _clear_actions();
        // 只读模式（Q 看信息）：只有上面那一组信息 ＋ 介绍，**不给任何交互项**
        if (mode == MODE_INFO)
        {
            _actions.AddChild(_section("（只读信息面板：按 E 才是与他打交道）"));
            return;
        }

        _add_talk_section();
        _add_gift_section();
        _add_spar_section();
        // **同伴只摆「赠与 ＋ 切磋」两段**（Q88 拍板，2026-10-04）：偷窃对他本来就不适用
        // （`steal_difficulty=0`），兑换／委托本章不给同伴配行——三段**整段不摆**，不摆灰按钮。
        if (!_is_companion(npc_id))
        {
            _add_steal_section();
            _add_offer_section();
            _add_quest_section();
        }
    }

    /// <summary>
    /// 旁白／自白模式（`MODE_STORY`，序幕择念用它）：数据同一套，但把「人」那一段整个收起来。
    /// 台词取 `dialogue_node_id` 指的那一条，选项仍走 `DialogueService.options_for`（条件照判）。
    /// </summary>
    private void _refresh_story()
    {
        GodotObject st = current_state();
        _name_label.Text = "";
        _info_label.Text = "";
        _favor_label.Text = "";
        if (_favor_ticks != null)
        {
            _favor_ticks.Text = "";
        }

        if (_avatar != null)
        {
            _avatar.Visible = false;
        }

        _clear_actions();
        Resource node = DialogueService.node_of(db, dialogue_node_id);
        if (node == null)
        {
            dialogue_node_id = "";
            if (_status != null)
            {
                _status.Text = "这一段现在不说了（条件没到）";
            }

            return;
        }

        var row = node as DialogueNodeRow;
        if (row == null)
        {
            dialogue_node_id = "";
            return;
        }

        _actions.AddChild(_section(row.text_cn));
        Godot.Collections.Array options = DialogueService.options_for(db, st, row.node_id);
        if (options.Count == 0)
        {
            _actions.AddChild(_section("（没有可选的）"));
            return;
        }

        foreach (Variant option_value in options)
        {
            var option = option_value.As<DialogueOptionRow>();
            var button = new Button
            {
                Name = $"TalkOption{option.option_id}",
                Text = option.text_cn,
                CustomMinimumSize = new Vector2(420, 36),
            };
            button.Pressed += () => { choose_dialogue(option.option_id); };
            _actions.AddChild(button);
        }
    }

    /// <summary>
    /// 「聊聊」（设计 20 §十一 的对话容器）：**台词与选项都从表里来**，面板只呈现与转发。
    ///
    /// 形状：先说这个人的那一句（`dialogue_node.text_cn`），下面是他现在能选的选项
    /// （`dialogue_option`，按 `condition` 过滤——设计 20 §四 的「前置」就写在那儿）。
    /// 选完 `dialogue_node_id` 跟着 `next_node_id` 走；空 = 这次谈完了。
    /// </summary>
    private void _add_talk_section()
    {
        GodotObject st = current_state();
        if (!DialogueService.has_dialogue(db, npc_id))
        {
            return;
        }

        Resource node = DialogueService.node_of(db, dialogue_node_id);
        var current_row = node as DialogueNodeRow;
        if (current_row == null || current_row.speaker_id != npc_id)
        {
            node = DialogueService.entry_node(db, st, npc_id);
            current_row = node as DialogueNodeRow;
            dialogue_node_id = current_row != null ? current_row.node_id : "";
        }

        if (current_row == null)
        {
            // 有行、但这句现在不该说（条件是旗标）→ 如实说，不装成"没什么可说"
            _actions.AddChild(_section("他现在不想谈这件事（条件没到）"));
            return;
        }

        var node_row = current_row;
        // 说话人名字也走 `person_of`：同伴没有 npc_def 行，用旧取法会把 `ch_ci` 这种 id 印给玩家
        // （CopyGuard 当场抓到过——文案里出现表内 id 就是 bug）。
        NpcDefRow def = _person_of(npc_id);
        string speaker_name = def != null ? def.name_cn : npc_id;
        _actions.AddChild(_section($"{speaker_name}：{node_row.text_cn}"));
        Godot.Collections.Array options = DialogueService.options_for(db, st, node_row.node_id);
        if (options.Count == 0)
        {
            _actions.AddChild(_section("（他没别的话了）"));
            return;
        }

        foreach (Variant option_value in options)
        {
            var option = option_value.As<DialogueOptionRow>();
            var button = new Button
            {
                Name = $"TalkOption{option.option_id}",
                Text = option.text_cn,
                CustomMinimumSize = new Vector2(420, 36),
            };
            button.Pressed += () => { choose_dialogue(option.option_id); };
            _actions.AddChild(button);
        }
    }

    /// <summary>选一句话：转发给 `DialogueService`（效果全在那边结算），然后刷新到下一句。</summary>
    public Godot.Collections.Dictionary choose_dialogue(string option_id)
    {
        Godot.Collections.Dictionary result = DialogueService.choose(db, current_state(), option_id);
        if (_b(result, "ok"))
        {
            dialogue_node_id = _s(result, "next_node_id");
            _status.Text = $"「{_s(result, "text")}」";
        }
        else
        {
            _status.Text = _s(result, "error", "这句话现在不能说");
        }

        refresh();
        return result;
    }

    /// <summary>好感条：10 格（19 §三要求「必须显示好感度与条形」——玩家要判断还差多少能换东西）</summary>
    private string _favor_bar(int favor)
    {
        NpcFavorRow row = _favor_row_of(npc_id);
        int cap = System.Math.Max(1, row != null ? row.favor_max : 100);
        // GDScript 的 `round()` 是「.5 往远处舍入」；C# 的 `Math.Round` 默认是银行家舍入，别用默认值。
        int filled = (int)System.Math.Round(
            (double)favor / cap * 10.0, System.MidpointRounding.AwayFromZero);
        string out_text = "";
        for (int i = 0; i < 10; i++)
        {
            out_text += i < filled ? "█" : "░";
        }

        return out_text;
    }

    /// <summary>
    /// 头像：按 `npc_id` 取 32×32 的 NPC 头像（美术出图后同名替换）。
    ///
    /// **同伴没有那张图**（美术只出过七个 NPC 的），退到**立绘剪影**——同一张脸，不留空白框；
    /// 两者长宽比不同，所以那种情况按比例居中而不是拉伸。两样都没有时才如实写「（头像待出）」。
    /// </summary>
    private void _refresh_avatar()
    {
        if (_avatar == null)
        {
            return;
        }

        string avatar_file = avatar_path();
        if (ResourceLoader.Exists(avatar_file))
        {
            _avatar.StretchMode = TextureRect.StretchModeEnum.Scale;
            _avatar.Texture = GD.Load<Texture2D>(avatar_file);
            _avatar.TooltipText = "";
            return;
        }

        string portrait_file = $"res://assets/sprites/portraits/portrait_{npc_id}.png";
        if (ResourceLoader.Exists(portrait_file))
        {
            _avatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            _avatar.Texture = GD.Load<Texture2D>(portrait_file);
            _avatar.TooltipText = "";
            return;
        }

        _avatar.StretchMode = TextureRect.StretchModeEnum.Scale;
        _avatar.Texture = null;
        _avatar.TooltipText = "（头像待出）";
    }

    public string avatar_path() => $"res://assets/sprites/avatars/avatar_{npc_id}.png";

    private static Label _section(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    /// <summary>物品名：先看装备表，再看道具表（兑换／偷窃那两段都走它）。</summary>
    private string _item_name(string item_id)
    {
        var equip = db.get_row("equip_base", item_id) as EquipBaseRow;
        if (equip != null)
        {
            return equip.name_cn;
        }

        var item = db.get_row("item_base", item_id) as ItemBaseRow;
        return item != null ? item.name_cn : item_id;
    }

    /// <summary>摘掉旧条目：**先摘再 queue_free**——队列里的旧条目要到帧末才走，名字会撞</summary>
    private void _clear_actions()
    {
        foreach (Node child in _actions.GetChildren())
        {
            _actions.RemoveChild(child);
            child.QueueFree();
        }
    }

    // ------------------------------------------------------------------ 动作（全部转发给 NpcService）

    public Godot.Collections.Dictionary gift(string item_id)
    {
        Godot.Collections.Dictionary result = NpcService.gift(db, current_state(), npc_id, item_id);
        _status.Text = _b(result, "ok") ? _s(result, "text") : _s(result, "error");
        // 「还给谁加了好感」要说出来（设计 20 号 §九 #8：孙掌柜那封信是给燕小七的）——
        // 不然玩家只看见自己这条好感涨了，不知道别人那边也动了。
        Godot.Collections.Array extra = result.ContainsKey("extra_favor")
            ? result["extra_favor"].AsGodotArray()
            : new Godot.Collections.Array();
        if (_b(result, "ok") && extra.Count > 0)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Variant entry in extra)
            {
                names.Add(entry.AsString());
            }

            _status.Text += $"　（{string.Join("、", names)}）";
        }

        refresh();
        return result;
    }

    public Godot.Collections.Dictionary spar()
    {
        // 空队伍 id 对同伴是**有意的**：那表示「打他自己的镜像」，由场景层照人物现造（Q88 拍板 ①）
        Godot.Collections.Dictionary opponent = NpcService.spar_opponent(db, npc_id);
        if (_s(opponent, "kind", "none") == "none")
        {
            _status.Text = "这个人不切磋";
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "no_spar" } };
        }

        if (!_callable_valid(spar_handler))
        {
            _status.Text = "这里不能切磋（没有战斗入口）";
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "no_handler" } };
        }

        string team = _s(opponent, "team_id");
        _invoke_callback(spar_handler, npc_id, team);
        return new Godot.Collections.Dictionary { { "ok", true }, { "team_id", team } };
    }

    public Godot.Collections.Dictionary steal(string offer_id)
    {
        var offer = db.get_row("npc_offer", offer_id) as NpcOfferRow;
        if (offer == null)
        {
            return new Godot.Collections.Dictionary { { "ok", false }, { "error", "没有这个条目" } };
        }

        Godot.Collections.Dictionary result = NpcService.steal(
            db, current_state(), npc_id, offer.item_id, rng().Call("randf").AsDouble());
        _status.Text = _b(result, "ok") ? _s(result, "text") : _s(result, "error");
        refresh();
        return result;
    }

    public Godot.Collections.Dictionary redeem(string offer_id)
    {
        Godot.Collections.Dictionary result = NpcService.redeem(db, current_state(), offer_id);
        _status.Text = _b(result, "ok") ? _s(result, "text") : _s(result, "error");
        refresh();
        return result;
    }

    public Godot.Collections.Dictionary complete_quest(string quest_id)
    {
        Godot.Collections.Dictionary result =
            NpcService.complete_quest(db, current_state(), quest_id, _kill_styles_context());
        _status.Text = _b(result, "ok") ? _s(result, "text") : _s(result, "error");
        refresh();
        return result;
    }

    public string status_text() => _status != null ? _status.Text : "";

    /// <summary>面板自己的自检钩子（真实场景那一条在 `--local-selftest` 的「NPC 面板」一段）</summary>
    public bool backdrop_ok() => LayoutBudget.has_opaque_backdrop(this);

    public string[] copy_hits() => CopyGuard.id_tokens(this);

    // ------------------------------------------------------------------ 服务层的小口

    private NpcDefRow _person_of(string person_id) =>
        NpcService.person_of(db, current_state(), person_id) as NpcDefRow;

    private NpcFavorRow _favor_row_of(string person_id) =>
        db?.get_row("npc_favor", person_id) as NpcFavorRow;

    private int _favor_of(GodotObject st) => NpcService.favor_of(db, st, npc_id);

    private bool _is_companion(string person_id) =>
        NpcService.is_companion(db, person_id);

    /// <summary>`npc_quest.requirement` 里的 `kill_style:` 条件要从会话的上一场战斗里取</summary>
    private Godot.Collections.Dictionary _kill_styles_context() =>
        new() { { "kill_styles", kill_styles } };

    // ------------------------------------------------------------------ 小工具

    private static bool _has_user_arg(string flag) => System.Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    /// <summary>这个回调接上没有（空 Callable 与「根本没接」都算没接）。</summary>
    private static bool _callable_valid(Callable callable) =>
        callable.Delegate != null || (callable.Target != null && !callable.Method.IsEmpty);

    /// <summary>调这个回调（没接就不动）；返回值原样给出来，`sweep_handler` 那类要读它。</summary>
    private static Variant _invoke_callback(Callable callable, params Variant[] args)
    {
        if (!_callable_valid(callable))
        {
            return default;
        }

        return callable.Call(args);
    }

    /// <summary>对位 GDScript 的 `dict.get(key, fallback)`（读不存在的键在 C# 里会抛，坑 88）。</summary>
    private static string _s(Godot.Collections.Dictionary dict, string key, string fallback = "") =>
        dict.ContainsKey(key) ? dict[key].AsString() : fallback;

    private static bool _b(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) && dict[key].AsBool();

    private static int _i(Godot.Collections.Dictionary dict, string key) =>
        dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

    private static string _bool(bool value) => value ? "true" : "false";

    /// <summary>对位 GDScript 的 `"%+d"`：正数带 `+`、零是 `+0`（C# 的 `+0;-0` 格式串写法容易走样）。</summary>
    private static string _plus(int value) => value >= 0 ? $"+{value}" : $"{value}";
}
