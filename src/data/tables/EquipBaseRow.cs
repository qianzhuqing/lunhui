using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// equip_base.csv 行：装备基础数值。
//
// attr_* 是直接加属性点，会再经过 attr_to_stat 换算成派生数值；
// bonus_* 是直接加派生数值，去掉前缀即为 stat_def.stat_id（见 docs/design/06_配置表说明.md）。
public partial class EquipBaseRow : TableRow
{
    [Export]
    public string equip_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // weapon / armor / head / accessory / hidden
    [Export]
    public string slot { get; set; } = "";

    // 引用 rarity_def.rarity_id
    [Export]
    public string rarity { get; set; } = "";

    [Export]
    public int level_req { get; set; } = 1;

    // 武器系别 external / internal / odd
    [Export]
    public string element { get; set; } = "";

    // 武器类型，引用 weapon_type_def.weapon_type（防具／饰品留空）
    [Export]
    public string weapon_type { get; set; } = "";

    [Export]
    public int attr_str { get; set; }

    [Export]
    public int attr_con { get; set; }

    [Export]
    public int attr_agi { get; set; }

    [Export]
    public int attr_int { get; set; }

    [Export]
    public int attr_luk { get; set; }

    [Export]
    public double bonus_atk_phys { get; set; }

    [Export]
    public double bonus_atk_qi { get; set; }

    [Export]
    public double bonus_def_phys { get; set; }

    [Export]
    public double bonus_hp_max { get; set; }

    [Export]
    public double bonus_speed { get; set; }

    [Export]
    public double bonus_crit_rate { get; set; }

    // 穿透率：抵消目标一部分减伤
    [Export]
    public double bonus_pen_rate { get; set; }

    // 格挡率：与闪避互斥的另一种免伤手段
    [Export]
    public double bonus_block_rate { get; set; }

    [Export]
    public double bonus_block_reduction { get; set; }

    // 减伤率：终局乘区
    [Export]
    public double bonus_dmg_reduction { get; set; }

    [Export]
    public string special_effect { get; set; } = "";

    [Export]
    public bool drop_only { get; set; }

    [Export]
    public string desc { get; set; } = "";

    // 图标 id：**值＝`equip_id`**（设计 15 §六＋A14，0.31.1）。与 `item_base.icon` 同形——
    // 界面按 `assets/icons/equip/<icon>.png` 取图、空则退回 `equip_id`，所以"还没出图"不等于数据错。
    [Export]
    public string icon { get; set; } = "";

    /// <summary>直接加派生数值的部分，键为 stat_def.stat_id。</summary>
    public Dictionary stat_bonuses() => new()
    {
        { "atk_phys", bonus_atk_phys },
        { "atk_qi", bonus_atk_qi },
        { "def_phys", bonus_def_phys },
        { "hp_max", bonus_hp_max },
        { "speed", bonus_speed },
        { "crit_rate", bonus_crit_rate },
        { "pen_rate", bonus_pen_rate },
        { "block_rate", bonus_block_rate },
        { "block_reduction", bonus_block_reduction },
        { "dmg_reduction", bonus_dmg_reduction },
    };

    /// <summary>直接加属性点的部分，键为 attribute_def.attr_id。</summary>
    public Dictionary attr_bonuses() => new()
    {
        { "str", attr_str },
        { "con", attr_con },
        { "agi", attr_agi },
        { "int", attr_int },
        { "luk", attr_luk },
    };
}
