using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 悬停小卡（UI 清单 §二-12）——`TestCharacterUi` 的第四份 partial（同日按职责拆的：
/// 前三份是行囊／装备／武学）。钉的是：**卡片的形状**（0.2 秒／236px／不挡点击／贴上边翻下方）、
/// **四类卡的字段顺序**（清单那张表说死了，别每处自己拼）、以及**真格子上那条路**——
/// 移上去、等那一拍、浮出来、移开立刻收。
///
/// **为什么不用真鼠标**：headless 里没有指针，`MouseEntered` 只能靠 `EmitSignal` 打；
/// 而「等 0.2 秒」在同步的 `Run()` 里 `await` 不了，所以**手动把计时器那一拍打出来**——
/// 那正是真实会走的路（`_on_delay_timeout()` → `show_now()`），只是把「等」换成了「叫」。
/// </summary>
public partial class TestCharacterUi
{
    private void _CheckHoverCardUi(TableDb db, CharacterScreen screen, GodotObject state)
    {
        // ---- ① 组件的形状（清单 §二-12 前四行：延迟／宽度／位置／不挡点击）
        var card = screen.FindChild("HoverCard", true, false) as HoverCard;
        CheckNotNull(card, "角色面板有一张悬停小卡（第一次用到时才建）");
        if (card == null)
        {
            return;
        }

        CheckFloat(card.CustomMinimumSize.X, 236.0, "卡片宽 236px");
        CheckTrue(
            card.MouseFilter == Control.MouseFilterEnum.Ignore,
            "卡不吃鼠标——`pointer-events: none` 那条，否则它会挡住自己指着的那一格");
        CheckFalse(card.Visible, "平时藏着");
        CheckGt(card.ZIndex, 0.0, "盖在内容之上（同画布按 z 序画）");
        var delay = card.FindChild("HoverDelay", true, false) as Timer;
        CheckNotNull(delay, "卡里有一个计时器");
        if (delay != null)
        {
            CheckFloat(delay.WaitTime, 0.2, "浮出延迟 0.2 秒");
            CheckTrue(delay.OneShot, "一次性计时器——每一格重新数 0.2 秒，不是累计");
        }

        foreach (string slot_node in new[]
        {
            "HoverCardTitle", "HoverCardRow2", "HoverCardRow3", "HoverCardRow4", "HoverCardFoot",
        })
        {
            CheckTrue(card.FindChild(slot_node, true, false) is Label, $"卡里有 {slot_node} 那一格");
        }

        // ---- ② 摆位是纯函数：默认在元素上方、贴上边翻下方、左右贴边往里夹
        var canvas = new Vector2(1152, 648);
        var card_size = new Vector2(236, 60);
        Vector2 above = HoverCard.place(new Rect2(400, 300, 84, 84), card_size, canvas);
        CheckFloat(above.Y, 234.0, "默认摆在元素上方（300 − 60 − 6）");
        CheckFloat(above.X, 324.0, "横向居中于那一格（400 ＋ (84 − 236) / 2）");
        CheckFloat(
            HoverCard.place(new Rect2(400, 10, 84, 84), card_size, canvas).Y, 100.0,
            "贴上边时翻到下方（94 ＋ 6）——不翻就会被裁掉上半张");
        CheckFloat(
            HoverCard.place(new Rect2(0, 300, 84, 84), card_size, canvas).X, 4.0,
            "贴左缘时往里夹");
        CheckFloat(
            HoverCard.place(new Rect2(1100, 300, 84, 84), card_size, canvas).X, 912.0,
            "贴右缘时往里夹（1152 − 236 − 4）");

        // ---- ③ 四类卡的字段与顺序（清单 §二-12 那张表）
        string[] item = HoverCardText.item_card("草药", "材料", "凡品", "剩 3", "效果待补", "炼药材料", "左键选中");
        CheckEq(item.Length, 5, "物品卡五格（标题 ＋ 三行 ＋ 脚注）");
        CheckEq(item[1], "材料 · 凡品 · 剩 3", "物品卡行2＝`类型 · 稀有度 · 剩 N`");
        CheckEq(item[2], "效果待补", "物品卡行3＝效果行");
        CheckEq(item[3], "炼药材料", "物品卡行4＝描述");
        CheckEq(item[4], "左键选中", "物品卡脚注由界面给（那句话是操作，不是数据）");

        string[] equip = HoverCardText.equip_card("铁剑", "剑", "武器", "凡品", "外功攻击+6", "");
        CheckEq(equip[1], "剑 · 武器 · 凡品", "装备卡行2＝`类型 · 部位 · 品阶`");
        CheckEq(equip[3], "无词条", "装备卡没有词条时写「无词条」");
        CheckEq(
            HoverCardText.equip_card("铁剑", "", "武器", "凡品", "外功攻击+6", "")[1], "武器 · 凡品",
            "非武器没有类型那一截，别留空点");

        // 招式卡／内功卡两个 builder **随"武学页不挂悬停小卡"一起删了**（用户 2026-10-13 口径），
        // 所以这里不再验它们——武学页那一屏的信息收在「点一格 → 右边出详情」一条路上。

        // ---- ④ 行囊格子：真走一遍「移上去 → 那一拍 → 浮 → 移开收」
        screen.select_tab(UiNav.char_tab_of(UiNav.BAG));
        screen.set_filter("all");
        _InventoryOf(state).add_item(db, "item_herb", 2);
        screen.refresh();
        var cell = screen.FindChild("ItemCellitem_herb", true, false) as Button;
        CheckNotNull(cell, "行囊网格里有草药那一格");
        if (cell != null && delay != null)
        {
            CheckEq(cell.TooltipText, "", "格子的原生提示退休了（两套一起摆会叠着弹）");
            cell.EmitSignal(Control.SignalName.MouseEntered);
            CheckFalse(card.Visible, "刚移上去还不浮——要等那 0.2 秒");
            delay.EmitSignal(Timer.SignalName.Timeout);
            CheckTrue(card.Visible, "那一拍到点，卡浮出来");

            var herb = db.get_row("item_base", "item_herb") as ItemBaseRow;
            CheckNotNull(herb, "表里有草药那一行");
            if (herb != null)
            {
                CheckEq(
                    (card.FindChild("HoverCardTitle", true, false) as Label)?.Text, herb.name_cn,
                    "卡头写名字（不写 id）");
                CheckEq(
                    (card.FindChild("HoverCardRow2", true, false) as Label)?.Text,
                    $"材料 · {screen.sheet().rarity_name(herb.rarity)} · 剩 {_InventoryOf(state).count("item_herb")}",
                    "行2 写类型 · 稀有度 · 剩几件");
                CheckEq(
                    (card.FindChild("HoverCardFoot", true, false) as Label)?.Text,
                    "左键选中 · 右侧按钮使用／丢弃", "脚注写这一屏真有的操作");
            }

            cell.EmitSignal(Control.SignalName.MouseExited);
            CheckFalse(card.Visible, "移开立刻收（不等那 0.2 秒）");
        }

        // ---- ⑤ 武学页：**不挂悬停小卡**（用户 2026-10-13 口径）——鼠标停上去不许浮卡
        screen.select_tab(UiNav.char_tab_of(UiNav.SKILL));
        var skill_cell = screen.FindChild("SkillCellsk_xuanwei_01", true, false) as Button;
        CheckNotNull(skill_cell, "武学页有玄微剑法·起手那一格");
        if (skill_cell != null && delay != null)
        {
            card.hide_now();
            skill_cell.EmitSignal(Control.SignalName.MouseEntered);
            delay.EmitSignal(Timer.SignalName.Timeout);
            CheckFalse(card.Visible, "武学页的招式格**不浮小卡**（这一页只用「点一格出详情」）");
            card.hide_now();
        }

        // ---- ⑥ 装备页：穿着东西的槽位（空槽没有装备可摆，仍用原生提示）
        screen.select_tab(UiNav.char_tab_of(UiNav.EQUIP));
        var slot = screen.FindChild("SlotButtonweapon_0", true, false) as Button;
        CheckNotNull(slot, "装备页有武器槽那一行");
        if (slot != null && delay != null)
        {
            card.hide_now();
            slot.EmitSignal(Control.SignalName.MouseEntered);
            delay.EmitSignal(Timer.SignalName.Timeout);
            string row2 = (card.FindChild("HoverCardRow2", true, false) as Label)?.Text ?? "";
            CheckTrue(row2.Contains("武器"), $"装备卡行2 写部位：{row2}");
            var empty_slot = screen.FindChild("SlotButtonlegs_0", true, false) as Button;
            CheckNotNull(empty_slot, "装备页有空着的下装槽");
            if (empty_slot != null)
            {
                CheckTrue(empty_slot.TooltipText.Length > 0, "空槽仍留着原生提示");
            }

            card.hide_now();
        }

        // ---- ⑦ 属性页：战斗属性那一列的来源小卡
        screen.select_tab(UiNav.char_tab_of(UiNav.CHAR));
        var stat_value = screen.FindChild("StatValueatk_phys", true, false) as Label;
        CheckNotNull(stat_value, "属性页有外功攻击那一行");
        if (stat_value != null && delay != null)
        {
            // Label 默认不吃鼠标——不显式 Stop 就永远收不到 MouseEntered（"卡死活不浮"那种错）
            CheckTrue(
                stat_value.MouseFilter == Control.MouseFilterEnum.Stop,
                "数值那一格吃鼠标，悬停才收得到");
            card.hide_now();
            stat_value.EmitSignal(Control.SignalName.MouseEntered);
            delay.EmitSignal(Timer.SignalName.Timeout);
            CheckEq(
                (card.FindChild("HoverCardTitle", true, false) as Label)?.Text, "外功攻击", "来源小卡写属性名");
            string row3 = (card.FindChild("HoverCardRow3", true, false) as Label)?.Text ?? "";
            CheckTrue(row3.StartsWith("基础 "), $"行3 从「基础」起、后面接各项加成：{row3}");
            CheckEq(
                (card.FindChild("HoverCardRow4", true, false) as Label)?.Text, "由力驱动",
                "行4 读 `stat_def.desc`（「由『力』驱动」）");
            card.hide_now();
        }
    }
}
