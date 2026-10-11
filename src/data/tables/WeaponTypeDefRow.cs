using System;
using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// weapon_type_def.csv 行：武器类型定义。
public partial class WeaponTypeDefRow : TableRow
{
    [Export]
    public string weapon_type { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // external / internal / odd
    [Export]
    public string default_element { get; set; } = "external";

    // 主堆属性，"|" 分隔，引用 attribute_def.attr_id
    [Export]
    public string attr_focus { get; set; } = "";

    // 关联武学流派，对应 skill_base.school_id，留空表示暂无专属流派
    [Export]
    public string skill_school { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>主堆属性列表。C# 的 string[] 会被 Godot 桥成 PackedStringArray，调用点不用改。</summary>
    public string[] focus_attrs()
    {
        var parts = attr_focus.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var output = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            output[index] = parts[index].Trim();
        }

        return output;
    }
}
