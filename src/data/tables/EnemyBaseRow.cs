using Godot;
using Godot.Collections;
using Lunhui.Data;

namespace Lunhui.Data.Tables;

// enemy_base.csv 行：敌人模板。
//
// 设计 10 §二（0.14.0）：敌人和角色**共用一套模板**——七维 + 等级 → `attr_to_stat`／`level_growth`
// → 派生数值，同一套算法、分开存数据（`character_base` / `enemy_base`）。
// 表里那七列 `attr_*` **填了**就走共用管线（`EnemyFactory._stats_from_attrs`）；**空着**
// （设计还没用平衡工具配数值）就退回下面那些旧的派生列，并出声点名——「先改表结构与工厂
// 跑通空数据，再一起配数值」就是这条（10 §三）。
public partial class EnemyBaseRow : TableRow
{
    [Export]
    public string enemy_id { get; set; } = "";

    [Export]
    public string name_cn { get; set; } = "";

    // beast / neutral / bandit / rogue / hidden
    [Export]
    public string faction { get; set; } = "";

    [Export]
    public int level { get; set; } = 1;

    // 七维（与 `character_base` 同名同义）：填了就走共用管线，空着退回派生列
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
    public int attr_wu { get; set; }

    [Export]
    public int attr_gen { get; set; }

    // 旧派生列（过渡期回退用；七维配齐后由设计侧删除，见 10 §三）
    [Export]
    public int hp_base { get; set; } = 1;

    [Export]
    public double atk_phys { get; set; }

    [Export]
    public double atk_qi { get; set; }

    [Export]
    public double def_phys { get; set; }

    [Export]
    public double def_qi { get; set; }

    [Export]
    public double speed { get; set; }

    [Export]
    public double hit_rate { get; set; }

    [Export]
    public double dodge_rate { get; set; }

    [Export]
    public double crit_rate { get; set; }

    [Export]
    public int poise { get; set; }

    // 内伤抗性：这一列**保留**（设计只取消了毒/火/流血三条特权列——三种元素抗性改为靠
    // `enemy_passive` 装配内功获得，与角色同规则；内伤抗性角色侧本来就有 `stat_def.res_internal`）
    [Export]
    public double res_internal { get; set; }

    // 引用 drop_table.drop_group
    [Export]
    public string drop_group { get; set; } = "";

    // CSV 列名为 exp，与全局函数 exp() 重名，故改名（别名登记在 TableRegistry.cs 的 ALIAS_TRIPLES）
    [Export]
    public int exp_reward { get; set; }

    [Export]
    public int money { get; set; }

    // green / yellow / red / purple
    [Export]
    public string threat_tag { get; set; } = "green";

    [Export]
    public string ai_template { get; set; } = "";

    [Export]
    public string desc { get; set; } = "";

    /// <summary>
    /// 七维（`attr_str`…`attr_gen`）的取值表；**七列全空/全 0 时返回空字典**——
    /// 调用方（`EnemyFactory`）据此判断「这一行走共用管线还是退回派生列」。
    /// </summary>
    public Dictionary attr_map()
    {
        var output = new Dictionary
        {
            { "str", attr_str },
            { "con", attr_con },
            { "agi", attr_agi },
            { "int", attr_int },
            { "luk", attr_luk },
            { "wu", attr_wu },
            { "gen", attr_gen },
        };
        foreach (var key in output.Keys)
        {
            if ((int)output[key] != 0)
            {
                return output;
            }
        }

        return new Dictionary();
    }

    /// <summary>旧派生列那套抗性快照（**只剩内伤**；毒/火/流血改由内功提供，见 `enemy_passive`）。</summary>
    public Dictionary resistances() => new() { { "internal", res_internal } };
}
