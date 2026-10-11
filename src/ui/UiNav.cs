using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 底栏七入口（设计 14 §六 ＋ `docs/dev/UI示意落地清单.md` §一）——**常驻功能栏**，
/// 等价于快捷键的鼠标路：地图／行囊／武学／角色／纪事／系统／装备。
///
/// 为什么单开一份：这七个入口的名字、顺序、键位提示、以及「点它等于开哪个浮层的哪一页」
/// 是**同一件事的四处说法**——画在底栏上（`UiChrome.bottom_bar`）、
/// 角色面板拿它把四个页签对上（`CharacterScreen`）、场景控制器拿它做跳转（`OverworldController`）、
/// 用例拿它断言「当前项高亮」。抄成四份的下场是加一个入口要改四处、漏一处不报错。
///
/// **入口是「浮层 ＋ 页签」的二元组**（清单 §一那行注）：`行囊`／`武学`／`角色`／`装备`
/// 四个都落在同一个角色面板的四个页签上，所以这里给出 `char_tab_of()`——
/// 谁要跳转谁就得先问它，别在调用处再写一遍 `0/1/2/3` 这种魔数。
/// </summary>
public partial class UiNav : RefCounted
{
    public const string MAP = "map";
    public const string BAG = "bag";
    public const string SKILL = "skill";
    public const string CHAR = "char";
    public const string CLUE = "clue";
    public const string SYSTEM = "system";
    public const string EQUIP = "equip";

    /// <summary>
    /// 七入口：`{id, 显示名, 键位提示}`，**顺序就是底栏从左到右的顺序**（示意图的排法）。
    /// 没有键位的（武学／系统／装备）写空串——它们目前只有鼠标路。
    /// </summary>
    private static readonly string[][] ORDER =
    {
        new[] { MAP, "地图", "Esc" },
        new[] { BAG, "行囊", "I" },
        new[] { SKILL, "武学", "" },
        new[] { CHAR, "角色", "Tab" },
        new[] { CLUE, "纪事", "K" },
        new[] { SYSTEM, "系统", "" },
        new[] { EQUIP, "装备", "" },
    };

    /// <summary>入口表：`[{id, label, key}, …]`（`bottom_bar()` 按它摆按钮）。</summary>
    public static Godot.Collections.Array entries()
    {
        var rows = new Godot.Collections.Array();
        foreach (string[] entry in ORDER)
        {
            rows.Add(new Godot.Collections.Dictionary
            {
                { "id", entry[0] },
                { "label", entry[1] },
                { "key", entry[2] },
            });
        }

        return rows;
    }

    public static string label_of(string id)
    {
        foreach (string[] entry in ORDER)
        {
            if (entry[0] == id)
            {
                return entry[1];
            }
        }

        return "";
    }

    /// <summary>
    /// 入口 → 角色面板页签下标（`CharacterScreen` 的页签顺序：属性 0／装备 1／武学 2／**行囊 3**）。
    /// 这一串下标就是页签顺序的**唯一出处**——世界脚本开关浮层时用它算下标，别再写 `open_character_overlay(2)`
    /// 这种字面量（2026-10-11 行囊从第 3 页挪到第 4 页，四处字面量全要跟着改）。
    /// 不落在角色面板上的入口（地图／纪事／系统）返回 **-1**。
    /// </summary>
    public static int char_tab_of(string id)
    {
        switch (id)
        {
            case CHAR:
                return 0;
            case EQUIP:
                return 1;
            case SKILL:
                return 2;
            case BAG:
                return 3;
            default:
                return -1;
        }
    }

    /// <summary>
    /// 这一档能不能点。**不能点的写清原因**（禁用的按钮不许只灰着不说话，这是本仓的既有口径）——
    /// 「系统」那一屏（存读档＋开关）还没做，见清单 §二-11；做出来就把这里改成空串。
    /// </summary>
    public static string disabled_reason(string id) =>
        id == SYSTEM ? "存档与设置那一屏还没做" : "";

    /// <summary>这个入口是不是「回场景」（地图那一个）。</summary>
    public static bool is_back(string id) => id == MAP;

    /// <summary>
    /// 入口 id → 节点名（`bag` → `NavBag`）：用例要按名字找「当前项高亮的那个按钮」，
    /// 名字由 id 直接算出来，省得每个调用处各写一份。
    /// </summary>
    public static string node_name_of(string id) =>
        string.IsNullOrEmpty(id) ? "NavUnknown" : "Nav" + char.ToUpperInvariant(id[0]) + id.Substring(1);
}
