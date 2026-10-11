using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/buff_grant.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class BuffGrantRow : TableRow
{
    // buff_grant.csv 行：来源 → buff（谁在什么时机发哪个 buff）。
    //
    // source_type 决定 source_id 去哪张表查：skill_passive / skill_active / equip / set。
    [Export]
    public string grant_id { get; set; } = "";

    // skill_passive / skill_active / equip / set
    [Export]
    public string source_type { get; set; } = "";

    [Export]
    public string source_id { get; set; } = "";

    [Export]
    public string buff_id { get; set; } = "";

    // on_cast（催动/施放时）／on_equip（装备即生效）／on_hit（命中时）／on_battle_start（进战斗时）
    [Export]
    public string trigger { get; set; } = "";

    [Export]
    public int stacks { get; set; } = 1;

    [Export]
    public string note { get; set; } = "";
}
