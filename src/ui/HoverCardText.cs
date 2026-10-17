using Godot;
using Lunhui.Core;
using Lunhui.Data.Tables;

namespace Lunhui.Ui;

/// <summary>
/// 悬停小卡的**内容**（UI 清单 §二-12 那张表：四类卡的字段与顺序都定死，别每处自己拼）。
///
/// 「行」与「行为」分开：`HoverCard` 管形状（延迟／宽度／摆位／挡不挡点击），
/// 这里管字。这么做不是为了好看——**同一句话要出现在小卡与详情栏两处**时，
/// 两处各拼一遍就是两处定义，改了一处另一处静默不一致（最难发现的那种漂移）。
///
/// 返回的五格固定是「标题 ＋ 行2 ＋ 行3 ＋ 行4 ＋ 脚注」；**空串＝那一行整行不摆**。
/// 目前只落了行囊用得到的两类（物品／装备）——招式卡与内功卡随武学页那一批补进来
/// （`CheckNoNewTestOnlyApi` 会抓「定义了没人调」，所以不留空等位的 builder）。
/// </summary>
public static class HoverCardText
{
    /// <summary>
    /// 物品卡（清单 §二-12）：名称 ／ `类型 · 稀有度 · <尾截>` ／ 效果行 ／ 描述 ／ 脚注。
    ///
    /// `tail` 与 `foot` 由调用方给：**这两样是"这一屏的信息"、不是卡型的一部分**——
    /// 行囊写「剩 3」（你还剩几件），货架写「单价 16 文 · 持有 2」（店里卖多少）——
    /// 同一个「剩 N」放上货架会被读成「货架上还剩 N」（限量），那是另一回事。
    /// 脚注同理（清单原文写「左键使用 · 右键丢弃」，与现状不符：左键＝选中、
    /// 动作在右侧按钮或底部确认，已报策划）。
    /// </summary>
    public static string[] item_card(
        string name, string kind, string rarity, string tail, string effect, string desc, string foot) =>
        new[]
        {
            name,
            join(kind, rarity, tail),
            effect,
            desc,
            foot,
        };

    /// <summary>
    /// 装备卡（清单 §二-12）：名称 ／ `类型 · 部位 · 品阶` ／ 属性行 ／ 词条行（**无词条写「无词条」**）。
    /// 「类型」是武器类型（剑／拳／刀…，非武器为空则不摆那一截）、「部位」是槽位名——两个都照表读。
    /// </summary>
    public static string[] equip_card(
        string name, string kind, string slot, string rarity, string attrs, string affix) =>
        new[]
        {
            name,
            join(kind, slot, rarity),
            attrs,
            string.IsNullOrEmpty(affix) ? "无词条" : affix,
            "",
        };

    /// <summary>
    /// 只拿得到「表里一个 id」的场合（**商店货架**与 **NPC 面板**的赠与／偷窃／兑换）直接出一张卡：
    /// 装备走装备卡、道具走物品卡；`tail`／`foot` 与物品卡同一口径（见上）。
    ///
    /// **这些场合没有实例**，所以装备卡的行4 写「无词条」——店里卖的、人家身上带的都是白板；
    /// 行囊那一侧不同（有实例、读得出真词条），所以它自己那条路留着（那里要 `affix_summary_of`）。
    /// 属性行不在这里重算：走 `CharacterSheet.equipment_summary_of()`，与面板同一处。
    /// </summary>
    public static string[] of_base(TableDb db, string base_id, string tail, string foot)
    {
        if (db == null || base_id.Length == 0)
        {
            return System.Array.Empty<string>();
        }

        if (db.get_row("equip_base", base_id) is EquipBaseRow equip)
        {
            Resource slot_row = db.get_row("equip_slot_def", equip.slot);
            string slot_name = slot_row is EquipSlotDefRow slot ? slot.name_cn : "";
            return equip_card(
                equip.name_cn, _weapon_type_name(db, equip.weapon_type), slot_name,
                _rarity_name(db, equip.rarity),
                CharacterSheet.equipment_summary_of(db, equip), "");
        }

        if (db.get_row("item_base", base_id) is ItemBaseRow item)
        {
            return item_card(
                item.name_cn, ItemText.type_name(item.item_type), _rarity_name(db, item.rarity),
                tail, ItemText.EFFECT_PENDING, item.desc, foot);
        }

        // 查不到就**不浮**（给空数组，`HoverCard.show_now()` 会当场收掉）——比浮一张空卡好
        return System.Array.Empty<string>();
    }

    /// <summary>品阶的中文名（照 `rarity_def` 读；查不到退回 id，与面板那些取值口同一口径）。</summary>
    private static string _rarity_name(TableDb db, string rarity_id)
    {
        Resource row = db.get_row("rarity_def", rarity_id);
        return row is RarityDefRow typed ? typed.name_cn : rarity_id;
    }

    /// <summary>武器类型的中文名（剑／拳／刀…；空串＝这件装备不是武器，那一截不摆）。</summary>
    private static string _weapon_type_name(TableDb db, string weapon_type)
    {
        if (weapon_type.Length == 0)
        {
            return "";
        }

        Resource row = db.get_row("weapon_type_def", weapon_type);
        return row is WeaponTypeDefRow typed ? typed.name_cn : "";
    }

    /// <summary>拼一行次要字：**跳过空的那几截**（不摆「· · 凡品」这种空位）。</summary>
    private static string join(params string[] parts)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (string part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                kept.Add(part);
            }
        }

        return string.Join(" · ", kept);
    }
}
