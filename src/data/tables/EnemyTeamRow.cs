using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// enemy_team.csv 行：敌人小队配置（明雷队伍）。
public partial class EnemyTeamRow : TableRow
{
    [Export]
    public string team_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // 成员，格式 "enemy_id:count;enemy_id:count"
    [Export]
    public string members { get; set; } = "";

    [Export]
    public string threat_tag { get; set; } = "green";

    [Export]
    public string team_buff { get; set; } = "";

    [Export]
    public string note { get; set; } = "";

    /// <summary>解析 members，返回 [{"enemy_id": String, "count": int}, ...]。</summary>
    public Array parsed_members()
    {
        var output = new Array();
        foreach (var part in members.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // GDScript 侧是 `split(":", false)`（丢空段）＋`strip_edges()`，两边行为要一致。
            var pieces = trimmed.Split(':', System.StringSplitOptions.RemoveEmptyEntries);
            var enemyId = pieces[0].Trim();
            var count = 1;
            if (pieces.Length > 1)
            {
                // GDScript 的 `int(f)` 是**截断**，不是四舍五入。
                count = (int)pieces[1].Trim().ToFloat();
            }

            output.Add(new Dictionary { { "enemy_id", enemyId }, { "count", count } });
        }

        return output;
    }
}
