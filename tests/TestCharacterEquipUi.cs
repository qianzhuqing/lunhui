using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;
using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 角色面板**装备页**那一半（UI 清单 §二-5）——从 `TestCharacterUi.cs` 拆出来的 partial（2026-10-12）。
///
/// 拆的原因：主文件被顶到 819 行、越过 800 硬上限（`TestHandshakeRepoFiles.CheckFileSize`）。
/// **只挪位置、不改行为**：三栏（七槽 ＋ 换上之后／详情／可换）、空槽写「可装」、装备需求写 `Lv N（剑）`、
/// 词条区与置灰的「洗词条」、点候选只选中、预演值＝真穿上时的外功——都是原来那一簇断言。
/// </summary>
public partial class TestCharacterUi
{
    private void _CheckEquipmentTab(TableDb db, CharacterScreen screen, GodotObject state)
    {
        screen.select_tab(1);
        // 三栏（清单 §二-5）：左＝七槽 ＋ 换上之后｜中＝详情｜右＝可换
        CheckTrue(screen.FindChild("EquipSlotsColumn", true, false) != null, "装备页有七槽栏");
        CheckTrue(screen.FindChild("EquipDetailColumn", true, false) != null, "装备页有详情栏");
        CheckTrue(screen.FindChild("EquipCandidateColumn", true, false) != null, "装备页有可换栏");

        // 七个槽位照 equip_slot_def 摆出来（戒指两格 → 八行）：每个 slot_id 的第一格都在
        Godot.Collections.Array slot_defs = db.rows("equip_slot_def");
        var slot_hits = 0;
        foreach (Variant slot_value in slot_defs)
        {
            string slot_id = slot_value.As<EquipSlotDefRow>().slot_id;
            if (screen.FindChild($"SlotButton{slot_id}_0", true, false) != null)
            {
                slot_hits += 1;
            }
        }

        CheckEq(slot_hits, slot_defs.Count, "七个槽位都摆出来");

        // 空槽也占一行，写「空 · 可装」（清单 §二-5）
        var empty_slot = screen.FindChild("SlotButtonlegs_0", true, false) as Button;
        CheckNotNull(empty_slot, "下装槽空着也占一行");
        if (empty_slot != null)
        {
            CheckTrue(empty_slot.Text.Contains("可装"), $"空槽写「可装」：{empty_slot.Text}");
        }

        var slot_button = screen.FindChild("SlotButtonweapon_0", true, false) as Button;
        CheckNotNull(slot_button, "武器槽有按钮");
        CheckTrue(screen.FindChild("SlotButtonring_1", true, false) != null, "戒指槽有两格");

        // 卸下那一按搬进了详情栏（清单 §二-5：选中槽位后才写出那件装备的详情）——先选槽，再按
        screen.press_slot("weapon", 0);
        var detail_header = screen.FindChild("EquipDetailHeader", true, false) as Label;
        CheckNotNull(detail_header, "选中武器槽后详情栏写出装备名");
        var req_value = screen.FindChild("EquipReqRowValue", true, false) as Label;
        CheckNotNull(req_value, "详情栏写出装备需求");
        if (req_value != null)
        {
            CheckTrue(req_value.Text.StartsWith("Lv "), $"装备需求写 Lv：{req_value.Text}");
        }

        CheckTrue(screen.FindChild("AffixText", true, false) != null, "详情栏有词条区");
        var reroll = screen.FindChild("RerollAffixButton", true, false) as Button;
        CheckNotNull(reroll, "有洗词条按钮");
        if (reroll != null)
        {
            CheckTrue(reroll.Disabled, "洗词条 Q6 待定，先置灰");
        }

        var unequip = screen.FindChild("UnequipButtonweapon_0", true, false) as Button;
        CheckNotNull(unequip, "武器槽有卸下按钮");
        if (unequip == null)
        {
            return;
        }

        Inventory inventory = _InventoryOf(state);
        string instance_id = inventory.equipped_instances(db, CharId)[0];
        double atk_equipped = screen.sheet().stat_value("atk_phys");
        // 2026-10-12 小策划拍板（P19 ②）：不加属性门槛列，但**两条真门槛都要正面写出来**——
        // 武器槽写 `Lv N（剑）`（括注是这件装备要的武器类型），非武器槽只写 `Lv N`
        var weapon_base = db.get_row("equip_base", inventory.base_of(instance_id)) as EquipBaseRow;
        string weapon_cn = weapon_base != null ? screen.sheet().weapon_type_name_of(weapon_base) : "";
        string expect_req = weapon_base != null ? $"Lv {weapon_base.level_req}（{weapon_cn}）" : "";
        CheckTrue(
            expect_req.Length > 0 && req_value != null && req_value.Text == expect_req,
            $"武器槽把武器类型那一关也写出来：{(req_value != null ? req_value.Text : "缺")}");
        var req_note = screen.FindChild("EquipReqRowNote", true, false) as Label;
        CheckTrue(
            req_note != null && req_note.Text == "已满足",
            $"等级够了写「已满足」：{(req_note != null ? req_note.Text : "缺")}");
        unequip.EmitSignal(Button.SignalName.Pressed);
        // 0.10.2 起初始装备是**两件**（武器＋布衣）：卸掉武器后身上还剩那件布衣
        CheckEq(inventory.equipped_instances(db, CharId).Length, 1, "卸下武器后身上还剩上衣");
        CheckFalse(inventory.is_equipped(instance_id), "卸下的那把武器不再算穿着");
        CheckTrue(screen.status_text().Contains("卸下"), "状态栏提示卸下");
        // 状态栏说装备名，不是 eq_sword_01#1 这种实例 id（2026-10-03 文案审计）
        CheckFalse(screen.status_text().Contains("#"), $"卸下提示不漏实例序号：{screen.status_text()}");
        CheckFalse(screen.status_text().Contains("eq_"), $"卸下提示不漏装备 id：{screen.status_text()}");

        // 选槽位后候选里出现刚卸下的武器，点一下就装回去
        screen.press_slot("weapon", 0);
        var candidate_header = screen.FindChild("CandidateHeader", true, false) as Label;
        CheckNotNull(candidate_header, "选槽后出现候选列表标题");
        if (candidate_header != null)
        {
            CheckTrue(
                candidate_header.Text.Contains("武器") && !candidate_header.Text.Contains("weapon"),
                $"候选标题写中文槽位名而不是内部 id：{candidate_header.Text}");
        }

        // 点候选那一行＝**只选中**（清单 §二-5）：详情栏换成它，「换上之后」即时重算
        var candidate_row = screen.FindChild($"CandidateRow{instance_id}", true, false) as Button;
        CheckNotNull(candidate_row, "候选列表里有整行可选");
        if (candidate_row != null)
        {
            candidate_row.EmitSignal(Button.SignalName.Pressed);
            var cand_header = screen.FindChild("EquipDetailHeader", true, false) as Label;
            var base_row = db.get_row("equip_base", inventory.base_of(instance_id)) as EquipBaseRow;
            string weapon_name = base_row != null ? base_row.name_cn : "";
            CheckTrue(
                cand_header != null && weapon_name.Length > 0 && cand_header.Text.Contains(weapon_name),
                $"详情栏换成选中的候选：{(cand_header != null ? cand_header.Text : "缺")}");
            CheckTrue(
                screen.FindChild($"EquipConfirmButton{instance_id}", true, false) != null,
                "选中候选后详情栏出现「换上」");
            CheckNotNull(screen.FindChild("SwapRowatk_phys", true, false), "「换上之后」写出外功那一行");
            Godot.Collections.Dictionary after = screen.sheet().preview_swap_stats("weapon", 0, instance_id);
            double atk_after = after.ContainsKey("atk_phys") ? after["atk_phys"].AsDouble() : -1.0;
            CheckEq(
                (int)System.Math.Round(atk_after), (int)System.Math.Round(atk_equipped),
                "预演算出的外功＝真穿上时的外功");
        }

        var equip_button = screen.FindChild($"EquipButton{instance_id}", true, false) as Button;
        CheckNotNull(equip_button, "候选列表里出现刚卸下的武器");
        if (equip_button != null)
        {
            CheckFalse(equip_button.Disabled, "候选按钮可点");
            equip_button.EmitSignal(Button.SignalName.Pressed);
            CheckEq(inventory.equipped_instances(db, CharId).Length, 2, "装回后身上两件装备");
            CheckTrue(screen.status_text().Contains("装上"), $"状态栏提示装上：{screen.status_text()}");
            CheckFalse(screen.status_text().Contains("eq_"), $"装上提示不漏装备 id：{screen.status_text()}");
        }

        // 武器类型不匹配的装备要被置灰：塞一把拳套进背包
        string fist = inventory.add_equipment(screen._db as TableDb, "eq_fist_01", new Godot.Collections.Array());
        screen.refresh();
        screen.press_slot("weapon", 0);
        var fist_button = screen.FindChild($"EquipButton{fist}", true, false) as Button;
        CheckNotNull(fist_button, "拳套出现在武器的候选里");
        if (fist_button != null)
        {
            CheckTrue(fist_button.Disabled, "武器类型不匹配的候选被置灰");
            CheckTrue(
                fist_button.TooltipText.Contains("只能用"),
                $"置灰的「换上」把原因挂在悬浮里：{fist_button.TooltipText}");
        }

        // 选中那把拳套：需求行写它**要的**武器类型（拳），原因那截写「本角色只能用剑」——
        // 这一条同时挡住「把 `fist` 这种表内 id 甩给玩家」那个坑（`can_equip` 的文案走 name_cn）
        var fist_row = screen.FindChild($"CandidateRow{fist}", true, false) as Button;
        CheckNotNull(fist_row, "拳套那一行整行可选");
        if (fist_row != null)
        {
            fist_row.EmitSignal(Button.SignalName.Pressed);
            var fist_req = screen.FindChild("EquipReqRowValue", true, false) as Label;
            var fist_note = screen.FindChild("EquipReqRowNote", true, false) as Label;
            var fist_base = db.get_row("equip_base", "eq_fist_01") as EquipBaseRow;
            string fist_cn = fist_base != null ? screen.sheet().weapon_type_name_of(fist_base) : "";
            CheckTrue(
                fist_cn.Length > 0 && fist_req != null && fist_req.Text.Contains($"（{fist_cn}）"),
                $"拳套的需求行写出它要的武器类型：{(fist_req != null ? fist_req.Text : "缺")}");
            CheckTrue(
                fist_note != null && fist_note.Text.Contains("只能用")
                    && !fist_note.Text.Contains("fist"),
                $"类型不匹配时需求行写清原因且不漏表内 id：{(fist_note != null ? fist_note.Text : "缺")}");
        }
    }
}

