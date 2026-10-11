using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// `NpcPanel` 的四个交往段（赠送／切磋／偷窃／兑换／委托）——从 `NpcPanel.cs` 拆出来的 partial
/// （那份要守 800 行硬上限，这边只放"往 `_actions` 里摆控件"的那几段）。
///
/// 「聊聊」那一段留在主文件（它牵着 `dialogue_node_id` 的推进），这里只摆**转发给 NpcService** 的按钮。
/// 文案里的数字全部来自服务层同一处计算（`gift_preview`／`spar_opponent`／`offers_of`／
/// `available_quest`），面板绝不自己算第二份——改规则不会只改一半。
/// </summary>
public partial class NpcPanel
{
    private void _add_gift_section()
    {
        GodotObject st = current_state();
        if (st == null)
        {
            return;
        }

        Inventory inv = st.Get("inventory").As<Inventory>();
        if (inv == null)
        {
            return;
        }

        _actions.AddChild(_section("赠送（送对喜好加得多）"));
        int shown = 0;
        foreach (string item_id in inv.item_ids())
        {
            var row = db.get_row("item_base", item_id) as ItemBaseRow;
            if (row == null || row.item_type == "currency" || row.item_type == "key")
            {
                continue; // 钱不能送；钥匙道具是任务物（19 §2.3 的口径）
            }

            if (shown >= 12)
            {
                break;
            }

            shown += 1;
            // 预期收益写在按钮上（设计示意图「可赠之物」那一栏的信息层级）：玩家要能看出
            // 「送对喜好加得多」，而不是送完才知道。数只有 `gift_preview()` 一处算。
            Godot.Collections.Dictionary preview =
                NpcService.gift_preview(db, npc_id, item_id);
            string tag = _s(preview, "kind", "plain") switch
            {
                "best" => "（最喜）",
                "same" => "（同类）",
                "dislike" => "（不喜）",
                _ => "",
            };
            var button = new Button
            {
                Name = $"Gift_{item_id}",
                Text = $"赠送 {row.name_cn}（持有 {inv.count(item_id)}）"
                    + $"好感 {_plus(_i(preview, "delta"))}{tag}",
                CustomMinimumSize = new Vector2(420, 36),
            };
            button.Pressed += () => { gift(item_id); };
            _actions.AddChild(button);
        }

        if (shown == 0)
        {
            _actions.AddChild(_section("（背包里没有能送的东西）"));
        }
    }

    private void _add_spar_section()
    {
        // 打谁由 `spar_opponent()` 一处判：城镇 NPC 是一支队；**同伴是镜像他自己**（Q88 拍板 ①）
        Godot.Collections.Dictionary opponent =
            NpcService.spar_opponent(db, npc_id);
        if (_s(opponent, "kind", "none") == "none")
        {
            return;
        }

        NpcFavorRow row = _favor_row_of(npc_id);
        int gain = row != null ? row.spar_favor : 0;
        var button = new Button
        {
            Name = "SparButton",
            Text = $"切磋（赢了好感 +{gain}）",
            CustomMinimumSize = new Vector2(420, 36),
        };
        if (_s(opponent, "kind") == "mirror")
        {
            button.Text = $"切磋（与自己的镜像过招；赢了好感 +{gain}）";
        }

        button.Pressed += () => { spar(); };
        _actions.AddChild(button);
    }

    private void _add_steal_section()
    {
        Godot.Collections.Array steals =
            NpcService.offers_of(db, npc_id, "steal");
        if (steals.Count == 0)
        {
            // `steal_difficulty=0` = **不可偷**（设计 19 §2.2 给善意的人留的口子）：摆一行说明、不给按钮
            // ——什么都不写会被当成漏做（UI 清单 §二-7）。同伴整段不摆，那一层在 `refresh()` 就拦掉了。
            NpcFavorRow favor_row = _favor_row_of(npc_id);
            if (favor_row != null && favor_row.steal_difficulty == 0)
            {
                _actions.AddChild(_section("偷窃：此人不可偷"));
            }

            return;
        }

        _actions.AddChild(_section("偷窃（走敏／运判定；失败扣好感）"));
        foreach (Variant offer_value in steals)
        {
            var offer = offer_value.As<NpcOfferRow>();
            var button = new Button
            {
                Name = $"Steal_{offer.offer_id}",
                Text = $"偷 {_item_name(offer.item_id)}",
                CustomMinimumSize = new Vector2(420, 36),
            };
            button.Pressed += () => { steal(offer.offer_id); };
            _actions.AddChild(button);
        }
    }

    private void _add_offer_section()
    {
        Godot.Collections.Array offers =
            NpcService.offers_of(db, npc_id, "offer");
        if (offers.Count == 0)
        {
            return;
        }

        _actions.AddChild(_section("兑换（需要好感，有的还要钱）"));
        foreach (Variant offer_value in offers)
        {
            var offer = offer_value.As<NpcOfferRow>();
            string price = offer.price <= 0 ? "" : $"，{offer.price} 文";
            var button = new Button
            {
                Name = $"Offer_{offer.offer_id}",
                Text = $"兑换 {_item_name(offer.item_id)}（好感 {offer.favor_required}{price}）",
                CustomMinimumSize = new Vector2(420, 36),
            };
            button.Pressed += () => { redeem(offer.offer_id); };
            _actions.AddChild(button);
        }
    }

    private void _add_quest_section()
    {
        var quest = NpcService.available_quest(
            db, current_state(), npc_id, _kill_styles_context()) as NpcQuestRow;
        if (quest == null)
        {
            return;
        }

        _actions.AddChild(_section($"委托：{quest.text_cn}"));
        var button = new Button
        {
            Name = "QuestButton",
            Text = $"交委托（好感 +{quest.reward_favor}）",
            CustomMinimumSize = new Vector2(420, 36),
        };
        button.Pressed += () => { complete_quest(quest.quest_id); };
        _actions.AddChild(button);
    }
}
