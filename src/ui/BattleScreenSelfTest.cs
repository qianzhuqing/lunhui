using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// `BattleScreen` 的真实场景自检（`-- --battle-selftest`）与掉落文案——从 `BattleScreen.cs`
/// 拆出来的 partial（那份要守 800 行硬上限）；棘轮表盯的是**本文件**，见
/// `tests/TestHandshakeFloors.cs` 的 `VerificationCheckFloors`（2026-10-11 从 `battle_screen.gd`
/// 搬 C#，同一批 24 条原样搬过来）。
/// </summary>
public partial class BattleScreen
{
    public async void _run_battle_selftest()
    {
        // 容器的最小尺寸是延后一帧刷新的：先让界面过两帧，版式才量得准
        SceneTree tree = _tree();
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        ok = ok && allies.Count > 0 && enemies.Count > 0;
        lines.Add($"开战：我方 {allies.Count} 人 vs {Enc.team_name} {enemies.Count} 人");

        // 切磋镜像（Q88 拍板 ①）：遭遇里带 `mirror_char` 时，敌人**不从表里取**，而是照那个人现造
        // （与他上场时同一个 PartyBuilder：等级／加点／装备／招式全同）——这里验「一条命、敌方、
        // 满血、不给经验与钱」。**「只有同伴才走镜像」那条由 `spar_opponent()` 判**，在 NPC 面板自检里钉。
        string mirror_id = state.Get("char_ids").AsStringArray()[0];
        string saved_mirror = Enc.mirror_char;
        Enc.mirror_char = mirror_id;
        Godot.Collections.Array mirrored = _build_enemies(Enc);
        Enc.mirror_char = saved_mirror;
        GodotObject mirror = mirrored.Count == 1 ? mirrored[0].AsGodotObject() : null;
        bool mirror_ok = mirror != null
            && mirror.Get("side").AsInt32() == _side_enemy()
            && mirror.Get("display_name").AsString() == state.Call("char_name", db, mirror_id).AsString()
            && mirror.Get("hp").AsInt32() == mirror.Call("max_hp").AsInt32()
            && mirror.Get("reward_exp").AsInt32() == 0 && mirror.Get("reward_money").AsInt32() == 0
            && mirror.Get("skills").AsStringArray().Length > 0;
        ok = ok && mirror_ok;
        lines.Add($"切磋镜像（照自己现造·敌方·满血·无收益）={_bool(mirror_ok)}");

        // 相性签（22 §1.2.5）写的是**中文系别名**：`_element_name()` 读 `element_def.name_cn`，
        // 取不到就退回 id——而 `element_def` 这张表曾经根本不存在，屏上写的是「克 internal」
        // （撞「界面不许出现表内 id」）。这条一直没人盖，所以在这里钉住：每个单位的相性签里
        // 都不许出现 external／internal／odd，且三个 id 都要查得到中文名。
        string[] element_ids = { "external", "internal", "odd" };
        bool element_ok = true;
        var element_marks = new System.Collections.Generic.List<string>();
        foreach (Godot.Collections.Array group in new[] { allies, enemies })
        {
            foreach (Variant actor_variant in group)
            {
                foreach (string mark in _affinity_marks(actor_variant.AsGodotObject()))
                {
                    element_marks.Add(mark);
                    foreach (string element_id in element_ids)
                    {
                        element_ok = element_ok && !mark.Contains(element_id);
                    }
                }
            }
        }
        foreach (string element_id in element_ids)
        {
            string name = _element_name(element_id);
            element_ok = element_ok && name.Length > 0 && name != element_id;
        }
        ok = ok && element_ok && element_marks.Count > 0;
        lines.Add($"相性签写中文系别名（{string.Join("／", element_marks)}）={_bool(element_ok)}");

        // 相性行节点本身（22 §1.2.5／§八）：**每只存活敌人都画**、**没内容不留空行**、枚数 ≤3、
        // **我方身上不许有**。Boss 那条贴顶长血条右端的签是同一份 `_affinity_marks()` 的另一种挂法
        // （节点名不是 `Affinity_*`），这里量的是「每只敌人身上那一条」。
        bool affinity_node_ok = true;
        int enemy_marks = 0;
        foreach (Variant actor_variant in enemies)
        {
            GodotObject actor = actor_variant.AsGodotObject();
            string actor_id = actor.Get("actor_id").AsString();
            string[] marks = _affinity_marks(actor);
            enemy_marks += marks.Length;
            if (!actor.Call("is_alive").AsBool())
            {
                continue;
            }

            Node row_node = FindChild($"Affinity_{actor_id}", true, false);
            affinity_node_ok = affinity_node_ok && marks.Length <= 3
                && (marks.Length > 0) == (row_node != null);
        }

        foreach (Variant actor_variant in allies)
        {
            string actor_id = actor_variant.AsGodotObject().Get("actor_id").AsString();
            affinity_node_ok = affinity_node_ok
                && FindChild($"Affinity_{actor_id}", true, false) == null;
        }

        ok = ok && affinity_node_ok && enemy_marks > 0;
        lines.Add($"相性行挂在敌人身上（{enemies.Count} 只共 {enemy_marks} 枚·≤3·我方没有·空的不留行）"
            + $"={_bool(affinity_node_ok)}");

        // 版式（22 §八）：整屏·无壳之后不再有「整页最小尺寸」这回事，要量的是
        // **所有可见挂件都留在画框里** ＋ 第五版那条让位（顺序条右缘 ＜ 我方最左单位头顶条左缘）。
        ok = ok && _check_stage_layout(lines);
        // 自动战斗策略按钮（设计 04：保守／均衡／全力）——按钮在、能轮换、写的和实际一致
        bool strategy_ok = _strategy_button != null && strategy_text().Contains("均衡");
        ok = ok && strategy_ok;
        string cycled = press_strategy();
        strategy_ok = strategy_ok && cycled == _strategy_all_out() && strategy_text().Contains("全力");
        press_strategy();
        press_strategy();
        strategy_ok = strategy_ok && strategy_text().Contains("均衡");
        ok = ok && strategy_ok;
        lines.Add($"策略按钮 ok={_bool(strategy_ok)}（当前 {strategy_text()}）");

        // 暂停条（22 §1.6）：**长按战场 0.4s（或 Esc）呼出**，与指令行互斥、用完就收。
        // 工具行整条删掉之后，自动／速度／策略／战报就住在这里——没有入口等于把它们一起删了。
        bool pause_ok = _pause_bar != null && !_pause_bar.Visible && !pause_open();
        lines.Add($"进战斗时暂停条收着（它是呼出式，不常驻）={_bool(pause_ok)}");
        toggle_pause();
        pause_ok = pause_ok && pause_open() && _pause_bar.Visible;
        lines.Add($"长按／Esc 呼得出暂停条={_bool(pause_ok)}");
        toggle_pause();
        pause_ok = pause_ok && !pause_open() && !_pause_bar.Visible;
        lines.Add($"再呼一次收起来={_bool(pause_ok)}");
        ok = ok && pause_ok;

        // 流程自动推进（22 §1.6／§八 第六版）：**没有推进钮节点**、也没有「点战场空白处／Enter 推进」
        // 这条输入路径；**不按任何键** tick 也自己把回合推到我方；**暂停条呼出＝流程停住**（口径注 ③）。
        bool advance_ok = GetNodeOrNull("CommandRow/NextRoundButton") == null
            && GetNodeOrNull("ResultConfirm") != null;
        ok = ok && advance_ok;
        lines.Add($"没有推进钮、结算卡有「确定」={_bool(advance_ok)}");
        if (!_sim_finished())
        {
            // 只让 tick 走：没人下指令，流程也得自己推到「轮到我方」为止（进场那一拍不算卡住）
            int advance_guard = 0;
            while (!_sim_finished() && advance_guard < 12 && !_is_ally_turn())
            {
                _Process(AUTO_INTERVAL);
                advance_guard += 1;
            }

            bool waits = _is_ally_turn() || _sim_finished();
            // 口径注 ③：暂停条开着时 tick 不推进（量「回合数／出手者」这一对，收起再继续）
            toggle_pause();
            string frozen = _turn_mark();
            for (int i = 0; i < 4; i++)
            {
                _Process(AUTO_INTERVAL);
            }

            bool paused_still = _turn_mark() == frozen;
            toggle_pause();
            ok = ok && waits && paused_still;
            lines.Add($"不按任何键自己推到我方={_bool(waits)}（{_turn_mark()}，走了 {advance_guard} 拍）；"
                + $"暂停条开着 tick 不推进={_bool(paused_still)}");
        }

        // 敌方预兆签（22 §1.2.4：横带整条删掉，改成敌人**头顶那一枚签**，**点签＝拆招**）：
        // 预兆是**开回合时**掷的，所以这里先确保有一个进行中的回合，再看每个敌人头上的签。
        if (!sim.Call("in_round").AsBool() && !_sim_finished())
        {
            sim.Call("begin_round");
            _refresh();
        }

        var sign_texts = new System.Collections.Generic.List<string>();
        bool band_ok = enemies.Count > 0;
        foreach (Variant enemy_variant in enemies)
        {
            GodotObject enemy = enemy_variant.AsGodotObject();
            if (!enemy.Call("is_alive").AsBool())
            {
                continue;
            }

            var sign = FindChild($"Intent_{enemy.Get("actor_id").AsString()}", true, false) as Button;
            band_ok = band_ok && sign != null && sign.TooltipText.Contains("将用");
            sign_texts.Add(sign != null ? sign.Text : "-");
        }

        ok = ok && band_ok;
        lines.Add($"敌方预兆签（挂头顶·点签拆招）={_bool(band_ok)}（{string.Join("｜", sign_texts)}）");
        // 我方单位详情那一行（设计 14 §四：左侧 = 气血／内力／架势 ＋ 武器与已装内功）——
        // 前三条在名字与两根条上，这一行补武器与内功。
        var gear = FindChild($"Gear_{_prop_str(allies[0], "actor_id")}", true, false) as Label;
        bool gear_ok = gear != null && gear.Text.Contains("武器：") && gear.Text.Contains("内功：");
        ok = ok && gear_ok;
        lines.Add($"我方详情（武器／已装内功）={_bool(gear_ok)}（{(gear != null ? gear.Text : "-")}）");

        // 战斗里能用哪些招，必须来自「装配」而不是模板（装配改动要真的进战斗）
        string char_id = _prop_str(allies[0], "actor_id");
        string[] equipped = _loadout_active(char_id);
        bool loadout_ok = _same_strings(_prop(allies[0], "skills").AsStringArray(), equipped);
        ok = ok && loadout_ok;
        lines.Add($"战斗招式来自装配：{_bool(loadout_ok)}（{equipped.Length} 招）");
        // 反向再验一次：把装配清空后重造战斗单位，招式必须跟着空。
        // 「模板里有什么就会什么」的实现在这里会变红（模板与装配恰好相同时，上面那条比不出来）
        string[] saved_passive = _loadout_passive(char_id);
        state.Call("set_loadout", char_id, System.Array.Empty<string>(), saved_passive);
        BattleActor stripped = PartyBuilder.build_actor((TableDb)db, state, char_id);
        bool stripped_ok = stripped != null && stripped.skills.Length == 0;
        state.Call("set_loadout", char_id, equipped, saved_passive);
        ok = ok && stripped_ok;
        lines.Add($"卸空装配后战斗单位无招：{_bool(stripped_ok)}");

        // 六指令（设计 08）：普通攻击／内功／道具／主动防御都在这个真实界面上走一遍，
        // 增益减益面板也要**真的有条目**（不是只有一套算好的数据）。
        // 招式与逃跑由别处盖住（本自检后面就是一路用招式打到结束）。
        bool commands_ok = true;
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        Godot.Collections.Array item_options = sim.Call("battle_item_options", inventory).AsGodotArray();
        commands_ok = commands_ok && item_options.Count == 2;
        foreach (Variant option_variant in item_options)
        {
            commands_ok = commands_ok && !_b(option_variant.AsGodotDictionary(), "usable");
        }

        lines.Add($"道具指令：列出 {item_options.Count} 件（效果列未配，全部如实标注）");
        // 内功：自检这一局的装配可能没铺内功，这里给这个单位塞一部（只影响这场自检）
        allies[0].AsGodotObject().Set("passives", new[] { "pf_xuanwei_01" });
        Godot.Collections.Array inner_options =
            sim.Call("passive_cast_options", allies[0]).AsGodotArray();
        commands_ok = commands_ok && inner_options.Count == 1;
        lines.Add($"内功指令：可催动 {inner_options.Count} 部");
        // 普通攻击：真打出伤害，而且不写进招式使用记录
        GodotObject basic_target = enemies[0].AsGodotObject();
        int basic_hp = basic_target.Get("hp").AsInt32();
        press_basic_attack();
        commands_ok = commands_ok
            && basic_target.Get("hp").AsInt32() < basic_hp
            && sim.Call("skill_uses", _prop_str(allies[0], "actor_id")).AsGodotDictionary().Count == 0;
        // 主动防御：真拿到 buff，面板真有条目
        press_defend();
        commands_ok = commands_ok && allies[0].AsGodotObject().Call("has_buff", "buff_guard").AsBool();
        commands_ok = commands_ok
            && FindChild($"Buff_{_prop_str(allies[0], "actor_id")}_buff_guard", true, false) != null;
        // 内功催动：真拿到运功 buff
        Godot.Collections.Dictionary cast = sim.Call(
            "cast_passive", allies[0], _s(inner_options[0].AsGodotDictionary(), "skill_id"))
            .AsGodotDictionary();
        commands_ok = commands_ok && _b(cast, "ok")
            && allies[0].AsGodotObject().Call("has_buff", "buff_yunqi").AsBool();
        lines.Add($"六指令（普攻／内功／道具／主动防御）ok={_bool(commands_ok)}");
        ok = ok && commands_ok;

        int before_exp = state.Get("party_exp").AsInt32();
        press_auto();
        press_stop_auto();
        // 「下一回合」只推进到我方行动为止，所以轮到我方时必须真出招；
        // 只循环 press_next_round() 会在轮到自己的那一刻永远推不动（曾经把自检挂死）。
        int guard = 0;
        int stall = 0;
        string last_signature = "";
        while (!_sim_finished() && guard < 300)
        {
            guard += 1;
            GodotObject actor = sim.Call("in_round").AsBool()
                ? sim.Call("current_actor").AsGodotObject()
                : null;
            if (actor != null && allies.Contains(actor))
            {
                Godot.Collections.Array skills = sim.Call("available_skills", actor).AsGodotArray();
                if (skills.Count == 0)
                {
                    press_next_round();
                }
                else
                {
                    press_skill(_prop_str(skills[0], "skill_id"));
                }
            }
            else
            {
                press_next_round();
            }

            string signature =
                $"{sim.Call("rounds_played").AsInt32()}/{_hp_total(allies)}/{_hp_total(enemies)}";
            stall = signature == last_signature ? stall + 1 : 0;
            last_signature = signature;
            if (stall >= 8)
            {
                lines.Add($"卡死：连续 8 步没有任何变化（第 {guard} 步）");
                ok = false;
                break;
            }
        }

        lines.Add($"推进步数：{guard}");
        lines.Add($"结果：{sim.Call("winner").AsString()}（{sim.Call("rounds_played").AsInt32()} 回合）");
        lines.Add(result_label_text());
        // 口径④：**这一刻**量结算帧屏上有什么最干净（战报还没被下面那一步顺手按出来）
        ok = ok && _check_settle_frame(lines);
        // 结算那一帧战报会自己收起来（第八版 `_apply_settle_hud()`：它从压暗底下面透出来像残留 UI）。
        // 而下面要量的正是「日志自动跟到底」——先把面板露出来再量。**直接给 `Visible`，别走
        // `toggle_log_panel()`**：那条会 `MoveToFront()` 把战报盖到结算面板之上，后面两档包围盒
        // 与截图都会被它遮住。
        if (_log_panel != null && !_log_panel.Visible)
        {
            _log_panel.Visible = true;
        }

        // 日志要自动跟到底：不然打到第 9 回合，日志还停在第一回合那几行
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        VScrollBar log_bar = _log.GetVScrollBar();
        double log_bottom = log_bar != null ? log_bar.MaxValue - log_bar.Page : 0.0;
        bool log_tail = log_bar == null || log_bottom <= log_bar.Value + 1.0;
        ok = ok && log_tail;
        lines.Add($"日志跟随到底：{_bool(log_tail)}（value "
            + $"{(log_bar != null ? log_bar.Value : 0.0):F0}，底 {log_bottom:F0}）");
        // 结算之后挂件还得全在画框里。**结算面板整块在滚动区里**（内容可以比视口高，
        // 包围盒量不出"顶没顶出画框"），所以它走 `_check_settle_panel` 单独量自己的高与宽。
        ok = ok && _check_clip("settled", lines);
        ok = ok && _check_settle_panel(
            "结算", sim.Call("winner").AsString() == _winner_ally(), -1, lines);
        // **最坏一档**（第八版）：掉落按格数上限撑满 ＋ 四件带词条装备 ＋ 两条没捡起，
        // 走真实生成路径（`_drop_cells`）把面板撑起来，等一帧再量。
        var many = new Godot.Collections.Dictionary
        {
            { "items", new Godot.Collections.Array() },
            { "equipment", new Godot.Collections.Array() },
            { "overflow", new Godot.Collections.Array() },
        };
        foreach (string item_id in new[]
                 {
                     "item_herb", "item_iron", "item_pelt", "item_med_01", "item_med_02",
                     "item_potion_small", "item_potion_qi", "item_herb", "item_iron", "item_pelt",
                 })
        {
            many["items"].AsGodotArray().Add(
                new Godot.Collections.Dictionary { { "item_id", item_id }, { "qty", 3 } });
        }

        foreach (string base_id in new[]
                 {
                     "eq_sword_01", "eq_head_01", "eq_armor_01", "eq_ring_01",
                 })
        {
            many["equipment"].AsGodotArray().Add(
                inventory.Call("add_equipment", db, base_id, new Godot.Collections.Array()));
        }

        many["overflow"].AsGodotArray().Add(
            new Godot.Collections.Dictionary { { "item_id", "item_iron" }, { "qty", 2 } });
        many["overflow"].AsGodotArray().Add(
            new Godot.Collections.Dictionary { { "item_id", "item_pelt" }, { "qty", 4 } });
        Godot.Collections.Array worst_cells = _settle_drop_cells(many);
        bool cap_ok = worst_cells.Count == SETTLE_DROP_ITEMS && _drop_cells(many).Count > SETTLE_DROP_ITEMS;
        ok = ok && cap_ok;
        lines.Add($"长文案档的掉落：面板 {worst_cells.Count} 格 / 共 {_drop_cells(many).Count} 件，"
            + $"放不下的进战报（战报那一行 {_describe_drops(many).Length} 字）={_bool(cap_ok)}");
        // 面板整体重建一次（两条升级 → 两行）再量：面板自身高 ≤ 设计高减掉「确定」那一带
        _refresh_settle_panel(new Godot.Collections.Dictionary
        {
            { "title", "胜利" },
            { "harvest", true },
            { "exp", 1280 },
            { "money", 256 },
            {
                "levels", new Godot.Collections.Array
                {
                    _level_change("scholar_fallen", "家道失落的书生", 3, 4, 6),
                    _level_change("ch_ci", "燕小七", 2, 3, 5),
                }
            },
            { "drops", worst_cells },
            // 掉落总件数也要喂：面板只摆 8 格，多出来的那句「还有 N 件放不下（见战报）」跟着出来
            { "drops_total", _drop_cells(many).Count },
            { "martial", new Godot.Collections.Array { "领悟：玄微剑法", "熟练度：玄微剑法 1 → 2" } },
            { "notes", new Godot.Collections.Array { "暂缓规则：本局不可撤退" } },
        });
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        ok = ok && _check_settle_panel("长文案", true, 2, lines);
        // 上面三次量法都可能量在「技能行恰好是空的」那一帧——而真实对局里只要轮到我方，这一行就在。
        // 2026-10-03 正是踩了这个：4 人队时多出来的 31px 全部来自这一行，1 人队却碰巧量在空帧、一路绿
        // （见框架说明决策 162）。现在补一次**最坏情况**：把技能行按上限撑满按钮，两个方向一起量。
        // 上限取自 `growth_const.active_slot_cap`（不写死 9），文案取真实招式名的长度。
        int slot_cap = 9;
        Resource cap_row = Db.get_row("growth_const", "active_slot_cap");
        if (cap_row != null)
        {
            slot_cap = System.Math.Max(1, (int)cap_row.Get("value").AsDouble());
        }

        var temp_buttons = new System.Collections.Generic.List<Button>();
        for (int i = 0; i < slot_cap; i++)
        {
            var temp_button = new Button { Text = $"玄微剑法·起手{i}" };
            _skill_row.AddChild(temp_button);
            temp_buttons.Add(temp_button);
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool full_row_ok = _clip_size().X <= _stage_size().X + 1.0f;
        ok = ok && full_row_ok;
        lines.Add($"{_clip_ascii("满招式")}（展开浮层按上限 {slot_cap} 个按钮撑满）");
        foreach (Button temp_button in temp_buttons)
        {
            temp_button.QueueFree();
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        // 「带状态」那一档（2026-10-04 补）：chip 有图时按**原生 32×32** 摆（不缩放——像素图非整数倍会糊），
        // 所以带状态的那一排比纯文字版高约 12px。而自检此前**从没量到带状态的那一版**：
        // `_check_status_ui` 那些是 headless 用例、不量 LAYOUT（美术交异常图标时指出来的）。
        // 照「满招式」那档的办法：临时塞几枚**带图**的 chip，量一次外壳，再收掉。
        // 图取真实表行（`status_effect` 的 icon 列），所以美术交图/改名的效果当场就能看见。
        var temp_chips = new System.Collections.Generic.List<Button>();
        foreach (string status_id in new[] { "poison", "burn", "bleed", "internal" })
        {
            Resource status_row = Db.get_row("status_effect", status_id);
            if (status_row == null)
            {
                continue;
            }

            temp_chips.Add(_make_effect_chip(new Godot.Collections.Dictionary
            {
                { "kind", "status" },
                { "entry_id", status_id },
                { "buff_id", status_id },
                { "icon", status_row.Get("icon") },
                { "name", status_row.Get("name_cn") },
                { "is_debuff", true },
                { "remaining", 3 },
                { "stacks", 2 },
                { "permanent", false },
                { "side_mark", "我方" },
            }));
        }

        // 状态图标就挂在**我方第一个人的头顶条下方**（22 §1.2.3）——所以直接塞进他那一行，
        // 加满之后量的是「牌子上多了一排图标还塞不塞得下」，不是另一块面板。
        var icon_row = FindChild(
            $"StatusIcons_{_prop_str(allies[0], "actor_id")}", true, false) as HBoxContainer;
        foreach (Button chip in temp_chips)
        {
            icon_row?.AddChild(chip);
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool with_status_ok = _clip_size().X <= _stage_size().X + 1.0f;
        ok = ok && with_status_ok;
        int icon_chips = 0;
        foreach (Button chip in temp_chips)
        {
            if (chip.Icon != null)
            {
                icon_chips += 1;
            }
        }

        lines.Add($"{_clip_ascii("带状态")}（带状态：{temp_chips.Count} 枚 chip，"
            + $"其中 {icon_chips} 枚真的带图）");
        foreach (Button chip in temp_chips)
        {
            chip.QueueFree();
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        // 浮层与全局快捷键（设计 14 §二／§八）：**战斗中 Tab 开角色面板属于「浮层盖场景层」**，
        // 再按一次是「弹回它」而不是叠第二份；Esc 一次弹一层。
        bool overlay_ok = true;
        overlay_ok = overlay_ok && _b(open_overlay(CHARACTER_SCENE, "CharacterPanel", 0), "ok");
        overlay_ok = overlay_ok && open_overlay_count() == 1;
        Node char_panel = FindChild("CharacterPanel", true, false);
        overlay_ok = overlay_ok && char_panel != null
            && char_panel.Call("current_tab").AsInt32() == 0;
        overlay_ok = overlay_ok
            && _b(open_overlay(CHARACTER_SCENE, "CharacterPanel", 2), "reopened");
        overlay_ok = overlay_ok && open_overlay_count() == 1;
        overlay_ok = overlay_ok && char_panel != null
            && char_panel.Call("current_tab").AsInt32() == 2;
        overlay_ok = overlay_ok && close_top_overlay() && open_overlay_count() == 0;
        ok = ok && overlay_ok;
        lines.Add($"浮层（战斗中开角色面板／再按弹回并切行囊页／Esc 弹回）={_bool(overlay_ok)}");

        // 玩家可见文案守卫：整页控件文字里不许出现表内 id 形态（决策 244）
        string[] copy_hits = CopyGuard.id_tokens(this);
        ok = ok && copy_hits.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        if (copy_hits.Length > 0)
        {
            lines.Add($"COPY 命中：{string.Join("；", copy_hits)}");
        }

        if (sim.Call("winner").AsString() == _winner_ally())
        {
            ok = ok && state.Get("party_exp").AsInt32() > before_exp;
            Node session_node = _session_node();
            bool cleared = session_node != null
                && session_node.Get("cleared_spawns").AsGodotDictionary().ContainsKey(Enc.spawn_id);
            ok = ok && cleared;
            lines.Add($"落账：经验 +{state.Get("party_exp").AsInt32() - before_exp}，"
                + $"明雷已清={_bool(cleared)}");
        }

        ok = ok && settled;
        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"BATTLE SELF-TEST: {(ok ? "OK" : "FAILED")}");
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// 出手动作：出手者的卡片闪一下暖光，让「谁在打」一眼看得出来。
    /// 注意**不能动子节点的 position**：卡片挂在 VBoxContainer 里，容器会重排位置把补间顶掉；
    /// 动 modulate　既可靠又不跟容器打架。多段招式只闪一次（按 actor 去重），命中/未命中都闪。
    /// </summary>
    public void _lunge_attackers(Godot.Collections.Array events)
    {
        var seen = new Godot.Collections.Dictionary();
        foreach (Variant event_variant in events)
        {
            Godot.Collections.Dictionary @event = event_variant.AsGodotDictionary();
            string source = _s(@event, "source");
            if (source.Length == 0 || seen.ContainsKey(source))
            {
                continue;
            }

            string kind = _s(@event, "kind");
            if (kind != "hit" && kind != "miss")
            {
                continue;
            }

            seen[source] = true;
            Node row = _find_actor_row(source);
            if (row is not Control item)
            {
                continue;
            }

            item.PivotOffset = _card_pivot(item);
            // 出手分帧：0 蓄势（压一下）→ 1 探身（弹出）→ 2 收势（回位），同时打一层暖光。
            // 卡片在容器里，position 会被下一次排版顶掉，所以整段用 scale（不受容器排版影响）。
            item.Modulate = new Color(1.35f, 1.2f, 0.8f, 1.0f);
            item.Scale = new Vector2(0.97f, 0.97f);
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Quad);
            tween.TweenProperty(item, "scale", new Vector2(1.06f, 1.06f), 0.09);
            tween.TweenProperty(item, "scale", Vector2.One, 0.18);
            tween.Parallel().TweenProperty(item, "modulate", new Color(1, 1, 1, 1), 0.27);
        }
    }

    /// <summary>
    /// 22 §八 的界面验收（**量节点，不量截图**）。整屏·无壳之后没有「整页最小尺寸」这回事了，
    /// 要量的是四件事：① 所有可见挂件都留在画框里；② 顺序条**让开人与条**（贴左缘、宽 ≤40、
    /// 右缘 ＜ 我方最左单位头顶条左缘、整条不与贴顶长血条相交）；③ 每个存活单位（Boss 除外）
    /// 都有头顶条，且整块信息牌落在形象框上方 8px；④ **非 Boss 战里贴顶长血条必须不存在**。
    /// </summary>
    private bool _check_stage_layout(System.Collections.Generic.List<string> lines)
    {
        bool ok = true;
        Vector2 world = _stage_size();
        Vector2 clip = _clip_size();
        bool inside = clip.X <= world.X + 1.0f && clip.Y <= world.Y + 1.0f;
        ok = ok && inside;
        lines.Add($"LAYOUT min_width={clip.X:0} min_height={clip.Y:0}"
            + $" limit={(int)world.X}x{(int)world.Y} ok={_bool(inside)}（所有可见挂件的包围盒）");

        // 顺序条：贴左缘 2px、整条 ≤40px（第五版为什么必须压到 40——旧版 76px 会压进我方第一人头顶条）
        var turn = GetNodeOrNull<VBoxContainer>("TurnBar");
        bool turn_ok = turn != null && turn.Visible;
        double turn_right = 0.0;
        double leftmost_ally_bar = double.MaxValue;
        double boss_overlap = 0.0;
        if (turn != null)
        {
            Rect2 rect = turn.GetGlobalRect();
            turn_right = rect.End.X;
            turn_ok = turn_ok && rect.Position.X <= 4.0 && rect.Size.X <= 40.0;
            if (_boss_bar != null && _boss_bar.Visible)
            {
                boss_overlap = rect.Intersection(_boss_bar.GetGlobalRect()).Size.X;
                turn_ok = turn_ok && boss_overlap <= 0.0;
            }
        }

        // 每个单位：头顶条在、信息牌贴在头顶上方 8px（Boss 不画头顶条——它走贴顶长血条）
        bool bars_ok = true;
        bool gap_ok = true;
        foreach (Godot.Collections.Array group in new[] { allies, enemies })
        {
            foreach (Variant actor_variant in group)
            {
                GodotObject actor = actor_variant.AsGodotObject();
                string actor_id = actor.Get("actor_id").AsString();
                bool is_boss = _boss_actor() == actor;
                var bar = FindChild($"HpBar_{actor_id}", true, false) as ProgressBar;
                var plate = FindChild($"Plate_{actor_id}", true, false) as Control;
                var unit = FindChild($"Actor_{actor_id}", true, false) as Control;
                if (is_boss)
                {
                    bars_ok = bars_ok && bar == null;
                    continue;
                }

                bars_ok = bars_ok && bar != null && plate != null && unit != null;
                if (bar == null || plate == null || unit == null)
                {
                    continue;
                }

                if (actor.Get("side").AsInt32() == _side_ally())
                {
                    leftmost_ally_bar = System.Math.Min(leftmost_ally_bar, bar.GetGlobalRect().Position.X);
                }

                double gap = unit.GetGlobalRect().Position.Y - plate.GetGlobalRect().End.Y;
                gap_ok = gap_ok && gap <= PLATE_GAP + 1.0 && gap >= -1.0;
            }
        }

        turn_ok = turn_ok && leftmost_ally_bar < double.MaxValue && turn_right < leftmost_ally_bar;
        ok = ok && turn_ok && bars_ok && gap_ok;
        lines.Add($"顺序条让位（贴左缘 ≤40、右缘 {turn_right:0} ＜ 我方最左头顶条 {leftmost_ally_bar:0}、"
            + $"与贴顶长血条相交 {boss_overlap:0}）={_bool(turn_ok)}");
        lines.Add($"头顶条（每个存活单位都有·Boss 除外）={_bool(bars_ok)}；"
            + $"信息牌贴头顶 8px={_bool(gap_ok)}");
        bool boss_bar_ok = _boss_actor() != null || (_boss_bar != null && !_boss_bar.Visible);
        ok = ok && boss_bar_ok;
        lines.Add($"非 Boss 战里没有贴顶长血条={_bool(boss_bar_ok)}");
        return ok;
    }

    /// <summary>所有**可见**挂件的包围盒（跳过整屏的底与浮层：它们本来就铺满整个画框）。</summary>
    private Vector2 _clip_size()
    {
        Vector2 world = _stage_size();
        Vector2 max = Vector2.Zero;
        foreach (Node node in FindChildren("*", "Control", true, false))
        {
            if (node is not Control control || !control.IsVisibleInTree() || control == this)
            {
                continue;
            }

            string name = control.Name.ToString();
            if (name == "Backdrop" || name == "FloatLayer" || name == "StageRoot")
            {
                continue;
            }

            // 结算面板整块跳过：它内部是滚动区（内容可以比视口高），包围盒量出来的东西不代表
            // "顶出画框"——面板自己的高与宽由 `_check_settle_panel` 直接量。
            if (_settle_root != null && (_settle_root == control || _settle_root.IsAncestorOf(control)))
            {
                continue;
            }

            Rect2 rect = control.GetGlobalRect();
            if (rect.Size.DistanceTo(world) < 1.0f)
            {
                continue;
            }

            max.X = System.Math.Max(max.X, rect.End.X);
            max.Y = System.Math.Max(max.Y, rect.End.Y);
        }

        return max;
    }

    /// <summary>挂件包围盒的一行（机器可读，`check_scene.bat` 按 `LAYOUT` 抓它）。</summary>
    private string _clip_ascii(string tag)
    {
        Vector2 world = _stage_size();
        Vector2 clip = _clip_size();
        bool ok = clip.X <= world.X + 1.0f && clip.Y <= world.Y + 1.0f;
        return $"LAYOUT[{tag}] min_width={clip.X:0} min_height={clip.Y:0}"
            + $" limit={(int)world.X}x{(int)world.Y} ok={_bool(ok)}";
    }

    private bool _check_clip(string tag, System.Collections.Generic.List<string> lines)
    {
        Vector2 world = _stage_size();
        Vector2 clip = _clip_size();
        bool ok = clip.X <= world.X + 1.0f && clip.Y <= world.Y + 1.0f;
        lines.Add(_clip_ascii(tag));
        return ok;
    }

    /// <summary>「回合数／当前出手者」——比只看回合数稳：同一回合里推进到另一个人也算推进。</summary>
    private string _turn_mark() =>
        $"{sim.Call("rounds_played").AsInt32()}/"
        + (sim.Call("in_round").AsBool() ? _prop_str(sim.Call("current_actor"), "actor_id") : "-");

    /// <summary>现在轮到我方出手吗（推进停止点就是这个状态）。</summary>
    private bool _is_ally_turn() =>
        sim.Call("in_round").AsBool()
        && sim.Call("current_actor").AsGodotObject() is BattleActor actor
        && actor.side == _side_ally();

    /// <summary>
    /// 结算里那条掉落明细（进战报「掉落：…」那一行）：「草药 ×2、铁剑（力+3、外功攻击+7）」。
    /// 以前只写「物品 2 种 装备 1 件」，玩家还得去背包翻。
    /// **全量**（不吃格数上限）：面板那两列读 `_settle_drop_cells()`，两边共用 `_drop_cells()` 这一份。
    /// </summary>
    public string _describe_drops(Godot.Collections.Dictionary applied)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant cell_variant in _drop_cells(applied))
        {
            parts.Add(cell_variant.AsGodotDictionary()["text"].AsString());
        }

        return string.Join("、", parts);
    }

    private string _affix_text(string instance_id)
    {
        GodotObject inventory = state.Get("inventory").AsGodotObject();
        return AffixRoller.summarize(
            Db, inventory.Call("affixes_of", instance_id).AsGodotArray());
    }

    /// <summary>装配里的招式列表（`GameState.loadout_of` 的 `active` 那一项）。</summary>
    private string[] _loadout_active(string char_id) => _loadout_slot(char_id, "active");

    /// <summary>装配里的内功列表（`GameState.loadout_of` 的 `passive` 那一项）。</summary>
    private string[] _loadout_passive(string char_id) => _loadout_slot(char_id, "passive");

    private string[] _loadout_slot(string char_id, string slot)
    {
        Godot.Collections.Dictionary loadout = state.Call("loadout_of", char_id).AsGodotDictionary();
        return loadout.ContainsKey(slot) ? loadout[slot].AsStringArray() : System.Array.Empty<string>();
    }

    /// <summary>逐个比字符串数组（对位 GDScript 的 `PackedStringArray ==`）。</summary>
    private static bool _same_strings(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}
