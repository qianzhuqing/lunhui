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

        // 版式预算：整页要塞得进设计分辨率（曾经最小高度 711 > 648，标题被裁掉半行）
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line(this));
        // 卡片列表在只滚竖向的滚动区里：横滚关着，卡片宽了就被裁掉——而上面那行 LAYOUT 只量到外壳
        ok = ok && LayoutBudget.content_fits(this);
        lines.Add(LayoutBudget.ascii_content_line(this));
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

        // 敌方预兆横带（设计 14 §四要点一：预兆必须显眼、占一整条横带）：
        // 预兆是**开回合时**掷的，所以这里先确保有一个进行中的回合，再看这一行写的是什么
        // （这一行以前显示「最后一条日志」，与下面的日志区重复）。
        if (!sim.Call("in_round").AsBool() && !_sim_finished())
        {
            sim.Call("begin_round");
            _refresh();
        }

        bool band_ok = _action_label != null
            && _action_label.Text.StartsWith("敌方预兆：", System.StringComparison.Ordinal)
            && _action_label.Text.Contains("将用");
        ok = ok && band_ok;
        lines.Add($"敌方预兆横带={_bool(band_ok)}（{(_action_label != null ? _action_label.Text : "-")}）");
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
        // 结算之后版式还得塞得进：结算文案会变长（经验／铜钱／掉落／领悟都在这里）
        ok = ok && LayoutBudget.fits(this);
        lines.Add(LayoutBudget.ascii_line_tagged(this, "settled"));
        // 极端长掉落文案：卡片行数固定三行，但**每一行都会换行**（`Result` 是普通 Label、不在滚动区里），
        // 一长就顶破 648（当年「711 > 648、标题被裁掉半行」就是这么坏的）。
        // 这里**走真实生成路径**（`_describe_drops_card`）造一段「多件掉落 + 带词条装备」的文案，
        // 等一帧再量——直接手写长文字会绕过截断，量到的就不是线上会出现的形态（第一版就这么写错了）。
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
        string card_drops = _describe_drops_card(many);
        ok = ok && card_drops.Contains("明细见战报");
        lines.Add($"长文案下的卡片掉落行（{card_drops.Length} 字）：{card_drops}");
        string saved_result = _result_label.Text;
        _result_label.Text = $"胜利！经验 +1280　铜钱 +256\n掉落：{card_drops}\n升级：书生 3 → 5（+6 点）";
        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool long_text_ok = LayoutBudget.fits(this);
        ok = ok && long_text_ok;
        // 自动换行的 Label 在最小尺寸里不一定算得准换行；**直接量它换了几个视觉行**更实在
        int card_lines = _result_label.GetLineCount();
        ok = ok && card_lines <= SETTLE_CARD_LINES + 1;
        lines.Add($"{LayoutBudget.ascii_line_tagged(this, "长文案")}（卡片视觉行数 {card_lines}）");
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

        bool full_row_ok = LayoutBudget.fits(this);
        ok = ok && full_row_ok;
        lines.Add($"{LayoutBudget.ascii_line_tagged(this, "满招式")}（技能行按上限 {slot_cap} 个按钮撑满）");
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

        foreach (Button chip in temp_chips)
        {
            _buff_panel.AddChild(chip);
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool with_status_ok = LayoutBudget.fits(this);
        ok = ok && with_status_ok;
        int icon_chips = 0;
        foreach (Button chip in temp_chips)
        {
            if (chip.Icon != null)
            {
                icon_chips += 1;
            }
        }

        lines.Add($"{LayoutBudget.ascii_line_tagged(this, "带状态")}（带状态：{temp_chips.Count} 枚 chip，"
            + $"其中 {icon_chips} 枚真的带图）");
        foreach (Button chip in temp_chips)
        {
            chip.QueueFree();
        }

        if (tree != null)
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        _result_label.Text = saved_result;
        ok = ok && LayoutBudget.content_fits(this);
        lines.Add(LayoutBudget.ascii_content_line(this));
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
    /// 结算里的掉落明细：「掉落：草药 ×2、铁剑（力+3、外功攻击+7）」
    /// 以前只写「物品 2 种 装备 1 件」，玩家还得去背包翻。
    /// </summary>
    public string _describe_drops(Godot.Collections.Dictionary applied) =>
        string.Join("、", _drop_parts(applied));

    /// <summary>卡片上的掉落行：最多 SETTLE_DROP_ITEMS 件、最多 SETTLE_DROP_MAX_CHARS 字，其余进战报</summary>
    public string _describe_drops_card(Godot.Collections.Dictionary applied)
    {
        string[] parts = _drop_parts(applied);
        if (parts.Length <= SETTLE_DROP_ITEMS)
        {
            string short_text = string.Join("、", parts);
            return short_text.Length <= SETTLE_DROP_MAX_CHARS
                ? short_text
                : $"{short_text.Substring(0, SETTLE_DROP_MAX_CHARS)}…";
        }

        var shown = new System.Collections.Generic.List<string>();
        for (int index = 0; index < SETTLE_DROP_ITEMS; index++)
        {
            shown.Add(parts[index]);
        }

        string text = string.Join("、", shown);
        if (text.Length > SETTLE_DROP_MAX_CHARS)
        {
            text = text.Substring(0, SETTLE_DROP_MAX_CHARS);
        }

        return $"{text}…（共 {parts.Length} 件，明细见战报）";
    }

    /// <summary>掉落清单的每一项（卡片行与战报明细共用一份，免得两边写法漂）</summary>
    private string[] _drop_parts(Godot.Collections.Dictionary applied)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in _arr_of(applied, "items"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string item_id = _s(entry, "item_id");
            Resource row = Db.get_row("item_base", item_id);
            parts.Add($"{(row != null ? row.Get("name_cn").AsString() : item_id)} ×{_i(entry, "qty", 1)}");
        }

        foreach (Variant instance_variant in _arr_of(applied, "equipment"))
        {
            string instance_id = instance_variant.AsString();
            GodotObject inventory = state.Get("inventory").AsGodotObject();
            string base_id = inventory.Call("base_of", instance_id).AsString();
            Resource base_row = Db.get_row("equip_base", base_id);
            string text = base_row != null ? base_row.Get("name_cn").AsString() : base_id;
            string affix = _affix_text(instance_id);
            parts.Add(affix.Length > 0 ? $"{text}（{affix}）" : text);
        }

        foreach (Variant entry_variant in _arr_of(applied, "overflow"))
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string item_id = _s(entry, "item_id");
            Resource item = Db.get_row("item_base", item_id);
            parts.Add($"{(item != null ? item.Get("name_cn").AsString() : item_id)}"
                + $"×{_i(entry, "qty", 1)} 没捡起（背包满）");
        }

        return parts.ToArray();
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
