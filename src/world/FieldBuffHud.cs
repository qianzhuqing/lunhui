using Godot;
using Lunhui.Core;

namespace Lunhui.World;

/// <summary>
/// 战斗外增益 HUD（C# 版，对位原 `src/world/field_buff_hud.gd`）。
///
/// 08 的 UI 要求：「大地图与城镇的 HUD 上，**战斗外增益**必须可见」——否则玩家不知道自己带着一个
/// 还剩 2 分钟的状态走进战斗。大地图与小地图共用这一份文案与控件。
/// </summary>
public partial class FieldBuffHud : RefCounted
{
    public static readonly Color COLOR = new(0.85f, 0.95f, 1.0f, 0.95f);

    /// <summary>
    /// 一个挂在 HUD 上的小标签（位置由调用方给）。
    ///
    /// **放在第 3 行（y=56）**：大地图第 2 行是揭雾进度、小地图第 2 行是副本完成度——两者都是左上角，
    /// 不放第 3 行就会叠字。两个控制器各有一条断言盯着「这一行不与上面两行同 y」。
    /// </summary>
    public static Label build_label(Vector2 position, int font_size = 12)
    {
        var label = new Label
        {
            Name = "FieldBuffs",
            Position = position,
        };
        label.AddThemeFontSizeOverride("font_size", font_size);
        label.AddThemeColorOverride("font_color", COLOR);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    /// <summary>
    /// `session.active_field_buffs()` 的输出 → 一行文案；没有生效的增益就是空串（标签不占视觉）。
    /// 剩余时间显示到**分钟**（向上取整：还剩 10 秒就别写「0 分钟」），过期由会话层清掉。
    /// </summary>
    public static string text_of(TableDb db, Godot.Collections.Array rows)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry_variant in rows)
        {
            Godot.Collections.Dictionary entry = entry_variant.AsGodotDictionary();
            string buff_id = entry.ContainsKey("buff_id") ? entry["buff_id"].AsString() : "";
            Resource row = db != null ? db.get_row("buff_def", buff_id) : null;
            string name_cn = row != null ? row.Get("name_cn").AsString() : buff_id;
            double remaining = entry.ContainsKey("remaining_sec") ? entry["remaining_sec"].AsDouble() : 0.0;
            int minutes = (int)System.Math.Ceiling(remaining / 60.0);
            int stacks = entry.ContainsKey("stacks") ? entry["stacks"].AsInt32() : 1;
            parts.Add($"{name_cn} 剩 {minutes} 分钟{(stacks <= 1 ? "" : $" ×{stacks}")}");
        }

        if (parts.Count == 0)
        {
            return "";
        }

        return "增益：" + string.Join("　·　", parts);
    }
}
