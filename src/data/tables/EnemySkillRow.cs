using Godot;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// P2 迁移生成：字段与 data/tables/enemy_skill.csv 的列一一对应（成员名保持 snake_case，见 src/data/TableRow.cs）。
public partial class EnemySkillRow : TableRow
{
    // enemy_skill.csv 行：一个敌人的一条招式（0.10.0 新增，见 06「敌人招式：谁会哪些招」）。
    //
    // 主键是 `enemy_id + skill_id`；`sort_order` 是出招偏好顺序（战斗里自动选招还会按期望伤害再挑一遍）。
    // 这张表取代了原来写死在 `EnemyFactory` 里的 `AI_SKILL_MAP` / `ENEMY_SKILL_OVERRIDE`
    // （见框架说明决策 246／Q48：设计给了表，就改成配表驱动）。
    [Export]
    public string enemy_id { get; set; } = "";

    [Export]
    public string skill_id { get; set; } = "";

    [Export]
    public int sort_order { get; set; } = 1;

    // 给人看的说明：这招是干什么的（如「被近身时的自卫」）
    [Export]
    public string note { get; set; } = "";
}
