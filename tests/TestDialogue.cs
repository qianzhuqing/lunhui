using Godot;
using Lunhui.Core;
using Lunhui.Data;
using Lunhui.Data.Tables;

namespace Lunhui.Tests;

/// <summary>
/// 对话容器（对位 `tests/test_dialogue.gd`；设计 20 §十一）。
///
/// 2026-10-11（P4 第三十九批）起，旧链十五节里属于**容器这一侧**的十一节整体并了进来：
/// 逐条台词与选项的落表核对（陈氏／序幕／幕二林铁山／幕四寨门／终局账册）、条件语言、
/// 效果三样、`set_flag` 分号多值。**人那一侧**（`person_of`／好感行／委托／地点）在
/// `tests/TestDialogueNpc.cs`，两份各注册一次。
///
/// 容器盯的是这些：
///   ① 入口节点把「被选项指到的接续节点」排掉——否则选完心性会拿心理独白当问候重念；
///   ② 条件语言决定「现在说不说」「这条选项现在能不能选」（`&`／`|`／`!`／`item:`）；
///   ③ `choose` 把三样效果（置旗标／好感／给东西）真的落地，失败时如实报错；
///   ④ 序幕那三条选项在同一处补 `flag_open_*`（心性由序幕一次定死）；
///   ⑤ 好感**可以是负的**（陈氏初始 −10，扣到地板就停，不是夹在 0）。
/// </summary>
public partial class TestDialogue : TestCase
{
    private const string Npc = "npc_huangcun";
    private const string ChenNode = "dl_huangcun_chen";
    /// <summary>终局难题（20 号 §七）：打完大寨主之后沈雁回问你账册怎么办。</summary>
    private const string Shen = "npc_shen_yanhui";
    private const string LedgerNode = "dl_ledger_choice";
    /// <summary>幕四·寨门与地牢（20 号 §四／§五）：铁栏后那句 ＋ 三个选项。</summary>
    private const string CellNode = "dl_shen_cell";

    public override string SuiteName() => "对话容器";

    public override void Run()
    {
        TableDb db = GetDb();
        _CheckOpeningNodeId();
        _CheckEntrySkipsContinuations(db);
        _CheckHasDialogue(db);
        _CheckReleaseRows(db);
        _CheckConditions(db);
        _CheckEffects(db);
        _CheckConditionOps(db);
        _CheckSetFlagMulti(db);
        _CheckMu2LinChain(db);
        _CheckCompanionDialogue(db);
        _CheckOpeningChoice(db);
        _CheckShenCell(db);
        _CheckLedgerChoice(db);
        _CheckFavorEffect(db);
        _CheckUnknownOption(db);
        _CheckParseSetFlags();
    }

    /// <summary>序幕节点 id 的唯一出处，以及给 GDScript 的取值口（P5 再收回成字段）。</summary>
    private void _CheckOpeningNodeId()
    {
        CheckEq(DialogueService.OPENING_NODE, "dl_opening_choice", "序幕择念节点 id 的唯一出处");
        CheckEq(DialogueService.opening_node(), "dl_opening_choice", "过渡期取值口与常量同值");
    }

    /// <summary>
    /// 入口取的是「这个人现在该说的第一句」，**接续节点不算**。
    /// 选过心性之后择念那句的条件不成立；三句心理独白的条件都空着——如果没排掉接续节点，
    /// 这里会取到 `dl_open_yi` 而不是 `null`，正是 2026-10-04 文案批次踩到的那个坑。
    /// </summary>
    private void _CheckEntrySkipsContinuations(TableDb db)
    {
        GodotObject fresh = SoloState(db);
        Resource entry = DialogueService.entry_node(db, fresh, "player");
        CheckNotNull(entry, "主角自己有话说（序幕择念）");
        CheckEq(((DialogueNodeRow)entry).node_id, "dl_opening_choice", "取到的是入口，不是被选项指到的心理独白");

        GodotObject chosen = SoloState(db);
        DialogueService.choose(db, chosen, "opt_open_yi");
        Resource after = DialogueService.entry_node(db, chosen, "player");
        CheckNull(after, "选过心性之后择念不再出现（三句独白被排掉，不会当问候重念）");
    }

    private void _CheckHasDialogue(TableDb db)
    {
        CheckTrue(DialogueService.has_dialogue(db, "player"), "主角记号有对话");
        CheckFalse(DialogueService.has_dialogue(db, "npc_wang_tie"), "没配对话的人如实说没有");
    }

    /// <summary>
    /// 序幕·择念（设计 20 §3.1）：三选一，文案在、旗标映射对、选完置 `heart_*` ＋ `flag_open_*`、
    /// 接上那句心理独白。
    ///
    /// 说话人是记号 `player`（主角是哪一号人物要等玩家选完出身才知道，表里没法写死 `char_id`），
    /// 所以面板按 `MODE_STORY` 渲染（没有好感条与交往段）。条件 `!heart_yi&!heart_mou&!heart_li`
    /// 把「只问一次」钉在表里：设计 §十三 v2 第 2 条说心性由序幕**一次定死**。
    /// </summary>
    private void _CheckOpeningChoice(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", DialogueService.OPENING_NODE);
        CheckEq(node.speaker_id, "player", "说话人是主角自己（记号 player）");
        CheckTrue(node.text_cn.Contains("你为什么要接这桩事"), "那一问在：" + node.text_cn);
        CheckTrue(node.text_cn.Contains("谢银三十两"), "告示原文也在（逐字）");

        GodotObject state = SoloState(db);
        Godot.Collections.Array options = DialogueService.options_for(db, state, DialogueService.OPENING_NODE);
        CheckEq(options.Count, 3, "择念三选一");
        var texts = new System.Collections.Generic.HashSet<string>();
        var flags = new System.Collections.Generic.HashSet<string>();
        var nexts = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in options)
        {
            var option = (DialogueOptionRow)entry.AsGodotObject();
            texts.Add(option.text_cn);
            flags.Add($"{option.option_id}={option.set_flag}");
            nexts.Add(option.next_node_id);
        }
        CheckTrue(texts.Contains("「三十两。」"), "利那条在");
        CheckTrue(texts.Contains("「告示上的字，是账房体。」"), "谋那条在");
        CheckTrue(texts.Contains("「十年前也有一桩事，也在这条河上。」"), "义那条在");
        CheckTrue(flags.Contains("opt_open_li=heart_li"), "「三十两」给利");
        CheckTrue(flags.Contains("opt_open_mou=heart_mou"), "「账房体」给谋");
        CheckTrue(flags.Contains("opt_open_yi=heart_yi"), "义那条置 heart_yi");
        CheckTrue(nexts.Contains("dl_open_li"), $"选完接的是那句心理独白（{string.Join(", ", nexts)}）");

        Godot.Collections.Dictionary chosen = DialogueService.choose(db, state, "opt_open_yi");
        CheckTrue(chosen["ok"].AsBool(), "义那条选得动：" + chosen["error"].AsString());
        CheckTrue(state.Call("has_flag", "heart_yi").AsBool(), "置上心性旗标 heart_yi");
        CheckEq(chosen["next_node_id"].AsString(), "dl_open_yi", "接上那句心理独白");
        // 0.32.0：序幕这一处再置一枚「定死」的 `flag_open_*`，称号与幕结旁白只看它
        CheckTrue(state.Call("has_flag", "flag_open_yi").AsBool(), "同一处顺带置 flag_open_yi");
        CheckFalse(state.Call("has_flag", "flag_open_mou").AsBool(), "其余两枚 flag_open_* 不跟着置");
        CheckFalse(state.Call("has_flag", "flag_open_li").AsBool(), "三条选项一一对应，不串档");
        CheckFalse(state.Call("has_flag", "heart_li").AsBool(), "另外两档不受影响（一次只定一档）");

        var monologue = (DialogueNodeRow)db.get_row("dialogue_node", "dl_open_yi");
        CheckNotNull(monologue, "那句心理独白在表里");
        if (monologue != null)
        {
            CheckEq(monologue.text_cn, "有些账，别人早忘了。你不敢忘。", "独白逐字照 §3.1");
        }

        // 心性定过之后这一问就不再出现（条件在表里，不是选项自带前置）
        CheckFalse(
            DialogueService.condition_ok(state, node.condition),
            "选过之后那条条件不成立（不会反复问）");
        CheckTrue(
            DialogueService.condition_ok(SoloState(db), node.condition),
            "没选过的档里它才出现");
    }

    /// <summary>好感效果：返回里带增量，真的加到 `npc_favor` 上，负好感不被夹在 0。</summary>
    private void _CheckFavorEffect(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckEq(_FavorOf(db, state, Npc), -10, "陈氏初始好感 −10（npc_favor 里就这么写）");
        Godot.Collections.Dictionary press = DialogueService.choose(db, state, "opt_chen_press");
        CheckTrue(press["ok"].AsBool(), "逼问选得动：" + press["error"].AsString());
        CheckTrue(state.Call("has_flag", "heart_mou").AsBool(), "置上心性旗标 heart_mou");
        CheckEq(press["favor"].AsInt32(), -2, "返回里带上好感变化 −2");
        CheckEq(_FavorOf(db, state, Npc), -10, "扣到地板 −10 就停住（负好感允许，不是夹在 0）");

        GodotObject quiet = SoloState(db);
        Godot.Collections.Dictionary crouch = DialogueService.choose(db, quiet, "opt_chen_crouch");
        CheckTrue(crouch["ok"].AsBool(), "对视选得动");
        CheckTrue(quiet.Call("has_flag", "heart_yi").AsBool(), "对视置 heart_yi");
        CheckEq(crouch["favor"].AsInt32(), 0, "它不改好感");
    }

    private void _CheckUnknownOption(TableDb db)
    {
        Godot.Collections.Dictionary missing = DialogueService.choose(db, SoloState(db), "opt_not_exist");
        CheckFalse(missing["ok"].AsBool(), "没有这个选项时选不动");
        CheckTrue(missing["error"].AsString().Length > 0, "并且给原因：" + missing["error"].AsString());
    }

    /// <summary>`set_flag` 一列的拆法只有一处（行类的 `parse_set_flags`），空白段要跳过。</summary>
    private void _CheckParseSetFlags()
    {
        CheckEq(DialogueOptionRow.parse_set_flags(" flag_a ;; flag_b ").Length, 2, "空白段跳过（分号多值）");
        CheckEq(DialogueOptionRow.parse_set_flags("").Length, 0, "空串拆出 0 枚");
    }

    /// <summary>好感的口径只有一处（`NpcService.favor_of`：`npc_favor` ＋上下限），直调。</summary>
    private static int _FavorOf(TableDb db, GodotObject state, string npc_id) =>
        NpcService.favor_of(db, state, npc_id);

    /// <summary>发行数据：陈氏那条是照 20 号 §四 幕三 落的（文案与效果逐字核对）。</summary>
    private void _CheckReleaseRows(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", ChenNode);
        CheckNotNull(node, "陈氏那条对话节点在表里");
        if (node == null)
        {
            return;
        }

        CheckEq(node.speaker_id, Npc, "说话人是陈氏");
        CheckTrue(node.text_cn.Length > 0, $"她有一句话（{node.text_cn}）");
        Godot.Collections.Array options = DialogueService.options_for(db, SoloState(db), ChenNode);
        CheckEq(options.Count, 3, "三个选项（20 号 §四 幕三）");
        var texts = new System.Collections.Generic.HashSet<string>();
        var flags = new System.Collections.Generic.HashSet<string>();
        var favors = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in options)
        {
            var option = (DialogueOptionRow)entry.AsGodotObject();
            texts.Add(option.text_cn);
            flags.Add($"{option.option_id}={option.set_flag}");
            favors.Add($"{option.option_id}={option.favor_delta}");
        }

        CheckTrue(texts.Contains("蹲下来，与她对视"), $"「蹲下来，与她对视」在（{_Text(texts)}）");
        CheckTrue(texts.Contains("「那晚你看见了什么？」（逼问）"), $"逼问那句在（{_Text(texts)}）");
        CheckTrue(texts.Contains("什么都不问"), "「什么都不问」在");
        CheckTrue(flags.Contains("opt_chen_crouch=heart_yi"), $"对视给义（{_Text(flags)}）");
        CheckTrue(flags.Contains("opt_chen_press=heart_mou"), "逼问给谋");
        CheckTrue(flags.Contains("opt_chen_silent="), "「什么都不问」不改心性");
        CheckTrue(favors.Contains("opt_chen_press=-2"), $"逼问扣好感 −2（{_Text(favors)}）");
    }

    /// <summary>
    /// 条件语言：说话人「现在说不说」与选项「现在能不能选」走同一份判定。
    ///
    /// 下半段是夹具（只改**内存副本**）：给陈氏塞一条「看过告示板才出现」的选项，
    /// 验前置没满足时不出现、硬选也选不动且**说明原因**、满足后出现、以及给的东西真的进背包。
    /// </summary>
    private void _CheckConditions(TableDb db)
    {
        GodotObject state = SoloState(db);
        // 说话人的入口节点：条件留空 = 一直可谈
        Resource entry = DialogueService.entry_node(db, state, Npc);
        CheckNotNull(entry, "陈氏现在有话说");
        if (entry != null)
        {
            CheckEq(((DialogueNodeRow)entry).node_id, ChenNode, "取到的就是那条");
        }

        CheckTrue(DialogueService.has_dialogue(db, Npc), "面板问得出「这个人有没有对话」");
        CheckFalse(DialogueService.has_dialogue(db, "npc_wang_tie"), "没配的人如实说没有");

        TableDb local = _WithExtraOption(
            "opt_chen_extra", ChenNode, 9, "想问问告示板上那张纸", "flag_board_read",
            "", 0, "item_herb", "夹具：验前置与给东西");
        Godot.Collections.Array before = DialogueService.options_for(local, state, ChenNode);
        CheckEq(before.Count, 3, $"前置没满足时那条选项**不出现**（{before.Count} 条）");
        Godot.Collections.Dictionary blocked = DialogueService.choose(local, state, "opt_chen_extra");
        CheckFalse(blocked["ok"].AsBool(), "硬选也选不动");
        CheckTrue(
            blocked["error"].AsString().Contains("前置"),
            $"并且说明原因：{blocked["error"].AsString()}");

        state.Call("set_flag", "flag_board_read", true);
        Godot.Collections.Array after = DialogueService.options_for(local, state, ChenNode);
        CheckEq(after.Count, 4, "前置满足后它出现了");

        GodotObject item_state = SoloState(local);
        item_state.Call("set_flag", "flag_board_read", true);
        Inventory inventory = item_state.Get("inventory").As<Inventory>();
        int herb_before = inventory.count("item_herb");
        Godot.Collections.Dictionary chosen = DialogueService.choose(local, item_state, "opt_chen_extra");
        CheckTrue(chosen["ok"].AsBool(), $"选得动：{chosen["error"].AsString()}");
        CheckEq(
            inventory.count("item_herb"), herb_before + 1,
            "选项给的东西真的进背包（走与掉落同一条入账口径）");
    }

    /// <summary>
    /// 「好感可以是负的」这条回归：地板钉**绝对值**（不是符号）——它决定「偷窃失手真的疼」
    /// 能疼到哪一步，放松或收紧都会静默改手感（变异探针 `code` 那一块有对应 case）。
    ///
    /// 效果三样的另外半段（置旗标／给东西／认不出的选项）由下面的 `_CheckFavorEffect` 与
    /// `_CheckUnknownOption` 钉，这里不重复。
    /// </summary>
    private void _CheckEffects(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckEq(NpcService.favor_min(), -10, "好感地板是 −10（与 npc_favor 里最深的初始值一致）");
        CheckEq(NpcService.favor_of(db, state, Npc), -10, "陈氏的初始好感是 −10（表里就这么写）");
        CheckEq(
            NpcService.add_favor(db, state, Npc, -5)["after"].AsInt32(), NpcService.favor_min(),
            $"再扣只会扣到地板（{NpcService.favor_min()}），不会无限往下");
    }

    /// <summary>
    /// 条件语言的两个新连接符（2026-10-04 加）：`|`＝任一满足、`!`＝取反，
    /// 外加 `item:<id>[:数量]`（与 `npc_quest.requirement` 同一种写法）。
    ///
    /// 为什么非加不可：终局难题要表达「三个旗标一个都没点亮」——`&` 只能写「全部满足」、
    /// `|` 只能写「任一满足」，三选一的「还没选过」两种都写不出来，于是玩家能反复进去选、
    /// 三个永久增益会叠在一起（设计 20 §八 第 6 条：三选一、不叠加）。
    /// </summary>
    private void _CheckConditionOps(TableDb db)
    {
        GodotObject state = SoloState(db);
        CheckFalse(GuideService.condition_met(state, "flag_a"), "没点亮的旗标＝不满足");
        CheckTrue(GuideService.condition_met(state, "!flag_a"), "取反：没点亮时 `!flag_a` 成立");
        state.Call("set_flag", "flag_a", true);
        CheckFalse(GuideService.condition_met(state, "!flag_a"), "点亮之后 `!flag_a` 不成立");
        CheckTrue(GuideService.condition_met(state, "flag_a|flag_b"), "任一满足：左边亮就算过");
        CheckFalse(GuideService.condition_met(state, "flag_b|flag_c"), "两个都没亮＝不满足");
        CheckTrue(
            GuideService.condition_met(state, "flag_a&!flag_b"),
            "`&` 先拆、`!` 在后：亮 a 且没亮 b 成立");
        CheckFalse(
            GuideService.condition_met(state, "!flag_a&flag_a"),
            "同一条里自相矛盾＝不成立（写坏了不会静默当成没条件）");
        CheckFalse(GuideService.condition_met(state, "!flag_a|"), "尾部空段＝不成立（与 `&` 同一套纪律）");

        // `item:<id>`：背包里有没有这件东西——终局难题靠它（**没拿到账册就不会问这件事**）
        CheckFalse(
            GuideService.condition_met(state, "item:item_bd_ledger"),
            "背包里没有账册时 `item:item_bd_ledger` 不成立");
        state.Get("inventory").As<Inventory>().add_item(db, "item_bd_ledger", 1);
        CheckTrue(GuideService.condition_met(state, "item:item_bd_ledger"), "拿到账册之后成立");
        CheckTrue(
            GuideService.condition_met(state, "item:item_bd_ledger:1"),
            "带数量的写法（与 npc_quest.requirement 一致）也认");
        CheckFalse(GuideService.condition_met(state, "item:item_bd_ledger:2"), "只有一本时 `:2` 不成立");
    }

    /// <summary>
    /// `set_flag` 一列可以写**多个**旗标（分号隔开，2026-10-04 Q83）：幕二「拔剑」那条选项要一次
    /// 置 `flag_lin_silent` 与 `flag_luoyanpo_met`，而这一列原本只装得下一个。
    ///
    /// 盯四件事：① 多值全部落地；② 单值（旧数据）行为一字不变；③ 多值里带 `heart_*` 时序幕那句
    /// 照旧补 `flag_open_*`；④ 构建期校验点名认不出的那一段。全部在**内存副本**上做。
    /// </summary>
    private void _CheckSetFlagMulti(TableDb db)
    {
        CheckNotNull(db.get_row("dialogue_option", "opt_open_yi"), "发行数据里有序幕那条选项（opt_open_yi）");

        // ② 单值照旧：普通旗标只是置一个，**不**补 `flag_open_*`
        TableDb single_db = _WithPatchedOptionFlag("opt_open_yi", "flag_q83_single");
        GodotObject single_state = SoloState(single_db);
        Godot.Collections.Dictionary single = DialogueService.choose(single_db, single_state, "opt_open_yi");
        CheckTrue(single["ok"].AsBool(), $"单值照样选得动：{single["error"].AsString()}");
        CheckTrue(single_state.Call("has_flag", "flag_q83_single").AsBool(), "单值置上了");
        CheckFalse(
            single_state.Call("has_flag", "flag_open_q83_single").AsBool(),
            "普通旗标不补 flag_open_*");

        // ① / ③ 多值：两枚都落地，且那枚 `heart_*` 照旧补出 `flag_open_yi`
        TableDb multi_db = _WithPatchedOptionFlag("opt_open_yi", "flag_q83_a;heart_yi");
        GodotObject multi_state = SoloState(multi_db);
        Godot.Collections.Dictionary multi = DialogueService.choose(multi_db, multi_state, "opt_open_yi");
        CheckTrue(multi["ok"].AsBool(), $"多值时选得动：{multi["error"].AsString()}");
        CheckTrue(multi_state.Call("has_flag", "flag_q83_a").AsBool(), "多值的第一枚落地");
        CheckTrue(multi_state.Call("has_flag", "heart_yi").AsBool(), "多值的第二枚落地");
        CheckTrue(
            multi_state.Call("has_flag", "flag_open_yi").AsBool(),
            "多值里带 heart_* → 序幕照旧补 flag_open_yi");

        CheckEq(
            DialogueOptionRow.parse_set_flags(" flag_a ;; flag_b ").Length, 2,
            "空白段跳过（分号多值里最常见的打字错）");

        // ④ 校验器认得出多值、并点名**真错的那一段**
        TableDb bogus_db = _WithPatchedOptionFlag("opt_open_yi", "flag_q83_a;bogus_flag");
        string[] errors = TableValidator.validate(bogus_db);
        bool hit = false;
        foreach (string message in errors)
        {
            if (message.Contains("bogus_flag"))
            {
                hit = true;
            }
        }

        CheckTrue(hit, $"构建期校验点名 set_flag 里认不出的那一段（共 {errors.Length} 条错误）");
    }

    /// <summary>
    /// 幕二·落雁坡林铁山那条链（Q83）：四条路都得出得来，而且**交心那句不能被任何选项指到**——
    /// `entry_node` 会把「被选项指到的节点」从入口候选里排掉（幕四踩过同一个坑），一旦有人给
    /// `dl_lin_heart` 挂上 `next_node_id`，威／买／拔剑三条路之后再找他**永远取不到**它。
    ///
    /// 小策划落表时**故意把 `opt_lin_chetai.next_node_id` 留空**（四条路统一「应答 → 再点一次 →
    /// 交心／短句」），这条用例把那个决定钉住。
    /// </summary>
    private void _CheckMu2LinChain(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", "dl_luoyanpo_lin");
        CheckNotNull(node, "幕二相遇那一节点在表里");
        if (node == null)
        {
            return;
        }

        CheckEq(node.speaker_id, "ch_gang", "说话人是林铁山");

        // 按**表**数那四条路（不用 `options_for`：那会带上条件，夹具状态一变条数就飘）
        var options = new System.Collections.Generic.List<DialogueOptionRow>();
        foreach (Variant entry in db.rows("dialogue_option"))
        {
            if (entry.AsGodotObject() is DialogueOptionRow row && row.node_id == "dl_luoyanpo_lin")
            {
                options.Add(row);
            }
        }

        CheckEq(options.Count, 4, "四条路（威／买／拔剑／撤台）");
        int met = 0;
        bool silent = false;
        foreach (DialogueOptionRow option in options)
        {
            string[] flags = DialogueOptionRow.parse_set_flags(option.set_flag);
            if (System.Array.IndexOf(flags, "flag_luoyanpo_met") >= 0)
            {
                met++;
            }

            if (System.Array.IndexOf(flags, "flag_lin_silent") >= 0)
            {
                silent = true;
            }
        }

        CheckEq(met, 4, "四条路**都**点亮 flag_luoyanpo_met（招募链 Q51 等的就是它）");
        CheckTrue(silent, "拔剑那条是分号多值 `flag_lin_silent;flag_luoyanpo_met`（Q83 的新语法）");

        // 交心／短句两句都不许被任何选项指到
        var pointed = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in db.rows("dialogue_option"))
        {
            if (entry.AsGodotObject() is DialogueOptionRow row && row.next_node_id.Length > 0)
            {
                pointed.Add(row.next_node_id);
            }
        }

        CheckFalse(pointed.Contains("dl_lin_heart"), "`dl_lin_heart` 没被任何选项指到（指到就永远取不到了）");
        CheckFalse(pointed.Contains("dl_lin_short"), "`dl_lin_short` 也没被指到（它靠条件自己出现）");
        var short_row = (DialogueNodeRow)db.get_row("dialogue_node", "dl_lin_short");
        CheckNotNull(short_row, "拔剑的收尾短句在表里");
        if (short_row != null)
        {
            CheckTrue(
                short_row.condition.Contains("flag_lin_silent"),
                $"收尾短句的条件认 flag_lin_silent（{short_row.condition}）");
        }
    }

    /// <summary>
    /// 幕一·客栈里的绿林客（20 号 §四）：台词与四个选项逐字一致，效果真的改到同伴身上。
    ///
    /// 「三十两」那条好感 −1 —— **0 −1 = −1**：好感可以是负的（地板 `favor_min()` 是 −10），
    /// 正是这轮把 `[0, cap]` 改成 `[FAVOR_MIN, cap]` 的用意，不然「失手真的疼」根本表达不出来。
    /// </summary>
    private void _CheckCompanionDialogue(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", "dl_qingfengyi_ci");
        CheckNotNull(node, "燕小七那条对话在表里");
        if (node == null)
        {
            return;
        }

        CheckEq(node.speaker_id, "ch_ci", "说话人是同伴（不是 npc_def 行）");
        GodotObject state = SoloState(db);
        Godot.Collections.Array options = DialogueService.options_for(db, state, "dl_qingfengyi_ci");
        CheckEq(options.Count, 4, "四个选项（20 号 §四 幕一）");
        var texts = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in options)
        {
            texts.Add(((DialogueOptionRow)entry.AsGodotObject()).text_cn);
        }

        CheckTrue(texts.Contains("「房钱，我替你付。」"), $"「房钱，我替你付。」在（{_Text(texts)}）");
        CheckTrue(texts.Contains("「你怎么知道，要人不要钱？」"), "反问那句在");
        CheckTrue(texts.Contains("「三十两，够我半年。」"), "「三十两，够我半年。」在");
        CheckTrue(texts.Contains("（不接话，起身就走）"), "「不接话」在");

        int before = NpcService.favor_of(db, state, "ch_ci");
        Godot.Collections.Dictionary chosen = DialogueService.choose(db, state, "opt_ci_pay");
        CheckTrue(chosen["ok"].AsBool(), $"选得动：{chosen["error"].AsString()}");
        CheckTrue(state.Call("has_flag", "heart_yi").AsBool(), "置上心性旗标 heart_yi");
        CheckEq(
            NpcService.favor_of(db, state, "ch_ci"), before + 5,
            "好感 +5（同伴的好感也进了 npc_favor）");

        GodotObject money_state = SoloState(db);
        DialogueService.choose(db, money_state, "opt_ci_money");
        CheckTrue(money_state.Call("has_flag", "heart_li").AsBool(), "「三十两」给利");
        CheckEq(NpcService.favor_of(db, money_state, "ch_ci"), -1, "好感 0 −1 就是 −1（负好感允许）");
    }

    /// <summary>
    /// 幕四·寨门与地牢（20 号 §四 幕四 ＋ §五 的选项表）：她一开口就是「你是来领那三十两的？」，
    /// 三个选项的文案与效果（义／谋／利 ＋ 好感 −5）也是照表落。
    ///
    /// 这一段以前**根本没有容器**（地牢那次判定只置一个旗标，铁栏后的人一句话都没有）；
    /// 落地的入口是 `local_map_controller.EVENT_DIALOGUES`。**没落的那一格**：§五 那行
    /// 「她反问『你为什么帮我』→ 三答」——三句回答设计没写，不自己编（Q73）。
    /// </summary>
    private void _CheckShenCell(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", CellNode);
        CheckNotNull(node, "地牢那一句在表里");
        if (node == null)
        {
            return;
        }

        CheckEq(node.speaker_id, Shen, "说话人是沈雁回");
        CheckEq(node.text_cn, "你是来领那三十两的？", "台词逐字照 20 号 §四 幕四");

        GodotObject state = SoloState(db);
        Godot.Collections.Array options = DialogueService.options_for(db, state, CellNode);
        CheckEq(options.Count, 3, "三个选项（§五 幕四 的选项表里有文案的那三条）");
        var texts = new System.Collections.Generic.HashSet<string>();
        var flags = new System.Collections.Generic.HashSet<string>();
        var favors = new System.Collections.Generic.HashSet<string>();
        foreach (Variant entry in options)
        {
            var option = (DialogueOptionRow)entry.AsGodotObject();
            texts.Add(option.text_cn);
            flags.Add($"{option.option_id}={option.set_flag}");
            favors.Add($"{option.option_id}={option.favor_delta}");
        }

        CheckTrue(texts.Contains("「我带你走。」"), $"「我带你走。」在（{_Text(texts)}）");
        CheckTrue(texts.Contains("「账册在哪？」"), "「账册在哪？」在");
        CheckTrue(texts.Contains("「三十两是我的。」"), "「三十两是我的。」在");
        CheckTrue(flags.Contains("opt_shen_take=heart_yi"), $"带走那条给义（{_Text(flags)}）");
        CheckTrue(flags.Contains("opt_shen_ledger=heart_mou"), "问账册那条给谋");
        CheckTrue(flags.Contains("opt_shen_thirty=heart_li"), "「三十两」那条给利");
        CheckTrue(favors.Contains("opt_shen_thirty=-5"), $"「三十两」那条好感 −5（表里写明，{_Text(favors)}）");

        // 效果真的落地：旗标 ＋ 好感（沈雁回的初始好感是 0）
        int before = NpcService.favor_of(db, state, Shen);
        Godot.Collections.Dictionary chosen = DialogueService.choose(db, state, "opt_shen_thirty");
        CheckTrue(chosen["ok"].AsBool(), $"选得动：{chosen["error"].AsString()}");
        CheckTrue(state.Call("has_flag", "heart_li").AsBool(), "置上心性旗标 heart_li");
        CheckEq(NpcService.favor_of(db, state, Shen), before - 5, "好感 0 −5 就是 −5（负好感允许）");
    }

    /// <summary>
    /// 终局难题（20 号 §七）：一册账，三条路——三条路各置一个旗标，而 `story_node` 那三行
    /// `kind=choice` 的永久增益**照着旗标发**（设计 20 §八）。
    ///
    /// 这条链以前是**断的**：`flag_ledger_public/buried/scattered` 全项目没有来源，三行增益
    /// 谁也拿不到（`test_handshake.KNOWN_FLAG_GAPS` 里挂着它们）。
    /// </summary>
    private void _CheckLedgerChoice(TableDb db)
    {
        var node = (DialogueNodeRow)db.get_row("dialogue_node", LedgerNode);
        CheckNotNull(node, "终局难题那条对话在表里");
        if (node == null)
        {
            return;
        }

        CheckEq(node.speaker_id, Shen, "说话人是沈雁回（§七：她要素真相）");
        var shen_def = (NpcDefRow)db.get_row("npc_def", Shen);
        CheckNotNull(shen_def, "沈雁回有 npc_def 行（美术的头像文件就是照这个 id 出的）");
        if (shen_def != null)
        {
            CheckEq(shen_def.place_id, "scene_heifengzhai", "她挂在地牢那张图上");
        }

        // ① 没打过对质 → 她不问这件事
        GodotObject state = SoloState(db);
        Resource cell_entry = DialogueService.entry_node(db, state, Shen);
        CheckNotNull(cell_entry, "没打赢大寨主时她有话说——是地牢那一句");
        if (cell_entry != null)
        {
            CheckEq(((DialogueNodeRow)cell_entry).node_id, CellNode, "取到的是幕四那一句（不是账册那道题）");
        }

        // ② 打过对质 → 摆出难题，三条路都在
        state.Call("set_flag", "flag_heifeng_confront", true);
        // 账册是条件的一部分（`item:item_bd_ledger`）：先验「对质打过但账册没到手」也不问
        CheckNull(
            DialogueService.entry_node(db, state, Shen),
            "打赢了但账册还没到手 → 她也不问（`item:` 那一段挡着；幕四那句也被 `!flag_heifeng_confront` 挡了）");
        state.Get("inventory").As<Inventory>().add_item(db, "item_bd_ledger", 1);
        Resource entry = DialogueService.entry_node(db, state, Shen);
        CheckNotNull(entry, "打赢大寨主之后她会问");
        if (entry != null)
        {
            CheckEq(((DialogueNodeRow)entry).node_id, LedgerNode, "取到的就是终局难题那一条");
        }

        Godot.Collections.Array options = DialogueService.options_for(db, state, LedgerNode);
        CheckEq(options.Count, 3, "三条路（呈官／沉底／散页）");
        var flags = new System.Collections.Generic.HashSet<string>();
        foreach (Variant item in options)
        {
            flags.Add(((DialogueOptionRow)item.AsGodotObject()).set_flag);
        }

        CheckTrue(flags.Contains("flag_ledger_public"), $"呈官那条置 flag_ledger_public（{_Text(flags)}）");
        CheckTrue(flags.Contains("flag_ledger_buried"), "沉底那条置 flag_ledger_buried");
        CheckTrue(flags.Contains("flag_ledger_scattered"), "散页那条置 flag_ledger_scattered");

        // ③ 选一条 → 旗标落地；再问她就不问了（**三选一不叠加**靠的就是这一步）
        Godot.Collections.Dictionary chosen = DialogueService.choose(db, state, "opt_ledger_public");
        CheckTrue(chosen["ok"].AsBool(), $"选得动：{chosen["error"].AsString()}");
        CheckTrue(state.Call("has_flag", "flag_ledger_public").AsBool(), "选了呈官 → 旗标真的进存档");
        CheckTrue(state.Call("has_flag", "flag_heifeng_confront").AsBool(), "对质那条旗标不受影响");
        CheckNull(
            DialogueService.entry_node(db, state, Shen),
            "选过之后她不再问（三个 `!flag_ledger_*` 一起把它挡住）");

        // 永久增益照旗标发（设计 20 §八 第 3 条：走 AttributeCalculator 的贡献通道）
        string char_id = ((GameState)state).char_ids[0].AsString();
        string[] labels = StoryService.bonus_labels(db, state, char_id);
        string joined = string.Join("、", labels);
        CheckTrue(
            joined.Contains("悟性 +2") && joined.Contains("内力上限 +20"),
            $"选完之后面板写得出「清名」那两条增益（{joined}）");
    }

    /// <summary>夹具：往 `dialogue_option` **追加**一行（新建私有表库，绝不碰共享表与策划的 CSV）。</summary>
    private static TableDb _WithExtraOption(
        string option_id, string node_id, int sort_order, string text_cn, string condition,
        string set_flag, int favor_delta, string grant_item_id, string note)
    {
        var row = new DialogueOptionRow
        {
            id = option_id,
            option_id = option_id,
            node_id = node_id,
            sort_order = sort_order,
            text_cn = text_cn,
            condition = condition,
            next_node_id = "",
            set_flag = set_flag,
            favor_delta = favor_delta,
            grant_item_id = grant_item_id,
            note = note,
        };
        var db = new TableDb();
        db.load_all();
        var table = (TableResource)db.tables["dialogue_option"].As<TableResource>().Duplicate(true);
        var rows = new Godot.Collections.Array();
        foreach (Variant entry in table.rows)
        {
            rows.Add(entry);
        }

        rows.Add(row);
        table.rows = rows;
        // `choose()` 走 `get_row`（吃 index），所以索引也得跟着补——漏了就是"行在表里却选不动"
        table.index[option_id] = rows.Count - 1;
        db.tables["dialogue_option"] = table;
        return db;
    }

    /// <summary>
    /// 夹具：把某一行的 `set_flag` 换成给定值（深拷贝整表，只改副本——直接改共享表会污染别的用例）。
    /// </summary>
    private static TableDb _WithPatchedOptionFlag(string option_id, string set_flag)
    {
        var db = new TableDb();
        db.load_all();
        var table = (TableResource)db.tables["dialogue_option"].As<TableResource>().Duplicate(true);
        foreach (Variant entry in table.rows)
        {
            if (entry.AsGodotObject() is DialogueOptionRow row && row.option_id == option_id)
            {
                row.set_flag = set_flag;
            }
        }

        db.tables["dialogue_option"] = table;
        return db;
    }

    /// <summary>拼一条给断言看的短句（GDScript 那份是 `str(arr)`）。</summary>
    private static string _Text<T>(System.Collections.Generic.IEnumerable<T> items) =>
        string.Join(", ", items);
}
