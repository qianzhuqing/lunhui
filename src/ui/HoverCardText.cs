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
    /// 物品卡（清单 §二-12）：名称 ／ `类型 · 稀有度 · 剩 N` ／ 效果行 ／ 描述 ／ 脚注。
    /// `foot` 由调用方给——**那句话是界面用法，不是数据**，而且各屏可能不一样
    /// （清单原文写「左键使用 · 右键丢弃」，与现状不符：左键＝选中，动作在右侧按钮，已报策划）。
    /// </summary>
    public static string[] item_card(
        string name, string kind, string rarity, int count, string effect, string desc, string foot) =>
        new[]
        {
            name,
            join(kind, rarity, $"剩 {count}"),
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
