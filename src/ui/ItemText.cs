using Godot;
using Lunhui.Core;

namespace Lunhui.Ui;

/// <summary>
/// 物品的**玩家可见文案与图标路径**（表内值 → 中文名／文件名），只此一处。
///
/// 为什么要独立成文件：`item_base.item_type` 是表内英文枚举，界面不许直接印（决策 244 的文案守卫）；
/// 而行囊页与商店页都要把同一个 `item_type` 翻成中文、把同一个 id 翻成图标路径——各写一份就是
/// 第二处定义，写错**不报错、只是那一处静默少图或少字**（最难发现的那种漂移）。
/// </summary>
public static class ItemText
{
    /// <summary>
    /// 道具类型的玩家可见名。取值与 `TableValidator.ITEM_TYPES` 同一套；
    /// **背包分类标签那六个词不从这里来**——分类是归组，与 `item_type` 不是一对一
    /// （钥匙那一档按 `is_key_item` 判，而铁镐的 `item_type` 是 `tool`）。
    /// </summary>
    public static string type_name(string item_type) => item_type switch
    {
        "material" => "材料",
        "consumable" => "消耗品",
        "skillbook" => "秘籍",
        "tool" => "工具",
        "key" => "钥匙",
        "currency" => "货币",
        _ => "杂项",
    };

    /// <summary>
    /// 装备图标：**`icon` 列优先、空则退回行 id**（15 §六命名表）。**查不到那件装备也照 id 拼**
    /// ——装备那一侧不留空位、更不编一个名字出来；真没出图由 `IconPaths.make_icon()` 返回 null
    /// （「这张图还没出」不是错误）。道具那一侧见 `item_icon_path()`。
    /// </summary>
    public static string equip_icon_path(TableDb db, string base_id) =>
        IconPaths.equip(_icon_id(db != null ? db.get_row("equip_base", base_id) : null, base_id));

    /// <summary>道具图标：与 `equip_icon_path()` 同一条口径，只是走 `icons/item/` 那个目录。</summary>
    public static string item_icon_path(TableDb db, string item_id) =>
        IconPaths.item(_icon_id(db != null ? db.get_row("item_base", item_id) : null, item_id));

    private static string _icon_id(Resource row, string fallback)
    {
        string icon_id = row != null ? row.Get("icon").AsString().StripEdges() : "";
        return icon_id.Length > 0 ? icon_id : fallback;
    }
}
