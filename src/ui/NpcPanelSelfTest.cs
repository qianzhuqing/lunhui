using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `NpcPanel` 的真实场景自检（`-- --npc-selftest`）。从 `NpcPanel.cs` 拆出来的 partial
/// （那边要守 800 行硬上限）；棘轮表盯的是**本文件**，见 `tests/TestHandshakeFloors.cs` 的
/// `VerificationCheckFloors`。
///
/// 开一个可交往的 NPC，走一遍「看信息 → 赠送 → 偷窃 → 兑换 → 委托」，
/// 每一步都要有说法（成功或「条件不够」），不许静默。
/// </summary>
public partial class NpcPanel
{
    private void _run_npc_selftest()
    {
        bool ok = true;
        var lines = new System.Collections.Generic.List<string>();
        GodotObject st = current_state();
        if (st == null)
        {
            Node session = _session_node();
            if (session != null)
            {
                st = GameState.new_game(db, "normal", System.Array.Empty<string>(), 0);
                session.Call("set_state", st);
            }
        }

        ok = ok && st != null;
        // 自检挑一个**能送、能偷、能换、有委托**的人：钱大夫（医馆）
        npc_id = "npc_qian_dafu";
        refresh();
        lines.Add($"信息面板：{_name_label.Text}（好感 {_favor_of(st)}）");
        ok = ok && _info_label.Text.Length > 0;
        // 原 GDScript 这句是 `ok = ok and _favor_label.text.contains("█") or _favor_label.text.contains("░")`，
        // 按优先级实际是 `(ok and 有实心块) or 有空格`——C# 的 `&&` 比 `||` 紧，**照原样搬**，
        // 这里顺手收紧会改变聚合口径（宁可保持一行一行对得上）。
        ok = ok && _favor_label.Text.Contains("█") || _favor_label.Text.Contains("░");
        // 好感条的**档位刻度**（设计 20 §十：并肩 30／知心 60／生死 90；UI 清单 §二-7「必须给阈值刻度」）：
        // 只给一条进度条，玩家看不出「还差多少能换东西」
        bool tier_ok = _favor_ticks != null && _favor_ticks.Text.Contains("并肩")
            && _favor_ticks.Text.Contains("知心") && _favor_ticks.Text.Contains("生死")
            && _favor_ticks.Text.Contains("30") && _favor_ticks.Text.Contains("90");
        ok = ok && tier_ok;
        lines.Add($"好感档位刻度={_bool(tier_ok)}（{(_favor_ticks != null ? _favor_ticks.Text : "缺")}）");
        // 不可偷的人（`steal_difficulty=0`）：**摆一行说明、不给按钮**——静默会被当成漏做
        npc_id = "npc_qiutu";
        refresh();
        string quiet_blob = "";
        bool quiet_has_button = false;
        foreach (Node child_row in _actions.GetChildren())
        {
            string child_text = child_row.Get("text").AsString();
            quiet_blob += $"{child_text}｜";
            if (child_row.Name.ToString().StartsWith("Steal_", System.StringComparison.Ordinal))
            {
                quiet_has_button = true;
            }
        }

        bool quiet_ok = quiet_blob.Contains("不可偷") && !quiet_has_button;
        ok = ok && quiet_ok;
        lines.Add($"不可偷的人只给说明、不给按钮={_bool(quiet_ok)}");
        npc_id = "npc_qian_dafu";
        refresh();
        // 头像（设计 14：原生 32×32、显示 2 倍到 64×64，与名字／称号／等级同排）
        bool avatar_ok = _avatar != null
            && _avatar.CustomMinimumSize == new Vector2(64, 64)
            && _avatar.Texture != null
            && _name_label.Text.Contains("Lv.");
        ok = ok && avatar_ok;
        lines.Add($"头像 64×64 且与名字/称号/等级同排={_bool(avatar_ok)}（{avatar_path()}）");

        Inventory inv = st.Get("inventory").As<Inventory>();
        // 赠送：给一件他喜欢的东西（`like_item_ids` 里有草药）
        inv.add_item(db, "item_herb", 1);
        int before = _favor_of(st);
        Godot.Collections.Dictionary gifted = gift("item_herb");
        ok = ok && _b(gifted, "ok") && _favor_of(st) > before;
        lines.Add($"赠送 ok={_bool(_b(gifted, "ok"))}（好感 {before} → {_favor_of(st)}）：{status_text()}");
        // 送空背包里的东西：要被拒且给说法
        Godot.Collections.Dictionary refused = gift("item_iron");
        ok = ok && !_b(refused, "ok") && status_text().Length > 0;
        lines.Add($"送没有的东西被拒={_bool(!_b(refused, "ok"))}（{status_text()}）");
        // 按钮上要写出**预期收益**（设计示意图「可赠之物」那一栏的信息层级）：种类与数字都来自
        // `gift_preview()`——与真加的那一笔同一个函数，改规则不会只改一半
        inv.add_item(db, "item_herb", 1);
        refresh();
        var gift_btn = _actions.FindChild("Gift_item_herb", true, false) as Button;
        Godot.Collections.Dictionary wish =
            NpcService.gift_preview(db, npc_id, "item_herb");
        bool preview_ok = gift_btn != null && gift_btn.Text.Contains("最喜")
            && gift_btn.Text.Contains(_plus(_i(wish, "delta")));
        ok = ok && preview_ok;
        lines.Add($"赠礼按钮写出预期收益={_bool(preview_ok)}（{(gift_btn != null ? gift_btn.Text : "缺")}）");

        // 偷窃：判定是 `掷点 ≤ 成功率`——掷 0 必成功、掷 1 必失败（固定掷点，用例可复现）
        Godot.Collections.Dictionary steal_win =
            NpcService.steal(db, st, npc_id, "item_potion_small", 0.0);
        ok = ok && _b(steal_win, "ok") && _b(steal_win, "success");
        ok = ok && inv.count("item_potion_small") >= 1;
        lines.Add($"偷窃得手：{_s(steal_win, "text")}");
        Godot.Collections.Dictionary steal_fail =
            NpcService.steal(db, st, npc_id, "item_potion_small", 1.0);
        ok = ok && _b(steal_fail, "ok") && !_b(steal_fail, "success");
        lines.Add($"偷窃失败有说法（扣好感）：{_s(steal_fail, "text")}");

        // 兑换：好感不够时被拒（药王符要 80），够了才换得动
        Godot.Collections.Dictionary poor = redeem("of_qian_acc");
        ok = ok && !_b(poor, "ok") && status_text().Contains("好感");
        lines.Add($"好感不够时兑换被拒={_bool(!_b(poor, "ok"))}（{status_text()}）");

        // 委托：条件没达成时不显示可交的委托（钱大夫那条要 `flag_qiutu_saved`）
        var quest = NpcService.available_quest(db, st, npc_id) as NpcQuestRow;
        ok = ok && quest == null;
        lines.Add($"条件没达成时没有可交的委托={_bool(quest == null)}");
        st.Call("set_flag", "flag_qiutu_saved", true);
        refresh();
        quest = NpcService.available_quest(db, st, npc_id) as NpcQuestRow;
        ok = ok && quest != null;
        if (quest != null)
        {
            Godot.Collections.Dictionary done = complete_quest(quest.quest_id);
            ok = ok && _b(done, "ok")
                && st.Call("has_flag", $"npc_quest_done_{quest.quest_id}").AsBool();
            lines.Add($"交委托 ok={_bool(_b(done, "ok"))}（好感 {_i(done, "favor")}）：{status_text()}");
        }

        // 同伴的交往屏（Q88 拍板 ①，2026-10-04）：**只摆「赠与 ＋ 切磋」两段**——
        // 偷窃／兑换／委托对同伴**整段不摆**（不摆灰按钮）；切磋那一按打的是「他自己的镜像」
        // （同伴没有 enemy_team 行，战斗层按人物现造）。
        npc_id = "ch_ci";
        dialogue_node_id = "";
        Godot.Collections.Dictionary mate_join = RecruitService.join(db, st, npc_id);
        refresh();
        var mate_spar = _actions.FindChild("SparButton", true, false) as Button;
        string mate_blob = "";
        bool mate_gift = false;
        foreach (Node child in _actions.GetChildren())
        {
            string mate_text = child.Get("text").AsString();
            mate_blob += $"{mate_text}｜";
            if (mate_text.StartsWith("赠送 ", System.StringComparison.Ordinal))
            {
                mate_gift = true;
            }
        }

        bool mate_ok = _b(mate_join, "ok") && mate_spar != null
            && mate_spar.Text.Contains("镜像") && mate_gift
            && !mate_blob.Contains("偷窃") && !mate_blob.Contains("兑换")
            && !mate_blob.Contains("委托");
        ok = ok && mate_ok;
        lines.Add($"同伴交往屏只摆赠与＋切磋（镜像过招）={_bool(mate_ok)}（"
            + $"{(mate_spar != null ? mate_spar.Text : "缺")}）");
        bool mate_kind_ok = _s(
            NpcService.spar_opponent(db, npc_id), "kind") == "mirror";
        ok = ok && mate_kind_ok;
        lines.Add($"同伴的切磋判成镜像={_bool(mate_kind_ok)}");

        // 对话容器（设计 20 §十一）：陈氏那条是照 20 号 §四 幕三 落的（她说的话 ＋ 三个选项）
        npc_id = "npc_huangcun";
        dialogue_node_id = "";
        refresh();
        var crouch = _actions.FindChild("TalkOptionopt_chen_crouch", true, false) as Button;
        bool talk_ok = DialogueService.has_dialogue(db, npc_id) && crouch != null;
        ok = ok && talk_ok;
        lines.Add($"对话：台词与选项按表渲染={_bool(talk_ok)}（按钮：{(crouch != null ? crouch.Text : "缺")}）");
        int favor_min = _favor_min();
        int favor_before = _favor_of(st);
        // 陈氏初始好感是 −10（表里就这么写的）：先把好感抬到正数，才验得动「−2」这一步；
        // 顺带钉住**负数不会被吃掉**（以前 `favor_of` 把好感夹在 [0, cap]，−10 一读就是 0）。
        ok = ok && favor_before == favor_min;
        lines.Add($"负好感读得出来（陈氏初始 {favor_before}，地板 {favor_min}）={_bool(favor_before == favor_min)}");
        NpcService.add_favor(db, st, npc_id, 12);
        favor_before = _favor_of(st);
        Godot.Collections.Dictionary chosen = choose_dialogue("opt_chen_press");
        bool effect_ok = _b(chosen, "ok") && st.Call("has_flag", "heart_mou").AsBool()
            && _favor_of(st) == favor_before - 2;
        ok = ok && effect_ok;
        lines.Add($"对话选项效果（置 heart_mou ＋ 好感 {favor_before}→{_favor_of(st)}）={_bool(effect_ok)}");

        // 同伴也是「人」（设计 20 §十）：他们没有 npc_def 行，`person_of()` 按同一形状合成一行；
        // 面板照常显示名字／称号／立绘，并且能谈（幕一 燕小七那四个选项）。
        npc_id = "ch_ci";
        dialogue_node_id = "";
        refresh();
        var char_row = db.get_row("character_base", npc_id) as CharacterBaseRow;
        string companion_name = char_row != null ? char_row.name_cn : npc_id;
        var pay = _actions.FindChild("TalkOptionopt_ci_pay", true, false) as Button;
        bool companion_ok = _name_label.Text.Contains(companion_name)
            && _avatar.Texture != null && pay != null;
        ok = ok && companion_ok;
        lines.Add($"同伴可交往（{npc_id}：名字／立绘／聊聊）={_bool(companion_ok)}（{_name_label.Text}）");
        if (pay != null)
        {
            int favor0 = _favor_of(st);
            Godot.Collections.Dictionary talked = choose_dialogue("opt_ci_pay");
            bool companion_effect = _b(talked, "ok") && _favor_of(st) == favor0 + 5;
            ok = ok && companion_effect;
            lines.Add($"同伴对话效果（好感 {favor0}→{_favor_of(st)}）={_bool(companion_effect)}");
        }

        // 版式与背板（面板自检的既有两条）+ 文案守卫
        ok = ok && backdrop_ok();
        lines.Add(LayoutBudget.ascii_backdrop_line(this));
        string[] copy = copy_hits();
        ok = ok && copy.Length == 0;
        lines.Add(CopyGuard.ascii_line(this));
        if (copy.Length > 0)
        {
            // 命中时把 token 打出来（`OverworldController` 的自检也是这么干的）——
            // 光报一个数字，排查要重跑一遍才知道是哪个控件。
            lines.Add($"COPY 命中：{string.Join("；", copy)}");
        }

        foreach (string line in lines)
        {
            GD.Print("  " + line);
        }

        GD.Print($"NPC SELF-TEST: {(ok ? "OK" : "FAILED")}");
        SceneTree tree = _tree();
        if (tree != null)
        {
            tree.Quit(ok ? 0 : 1);
        }
    }

    /// <summary>`NpcService.FAVOR_MIN`——搬 C# 之后直读常量（2026-10-11）。</summary>
    private static int _favor_min() => NpcService.FAVOR_MIN;
}
