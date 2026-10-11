using Godot;
using Lunhui.Data.Tables;

namespace Lunhui.Core;

/// <summary>
/// 经验与升级（C# 版，对位原 `src/core/level_service.gd`；设计 01「等级与经验」）。
///
/// 表里 `exp_to_next` 是「升下一级所需经验」（不是累计值），`upgrade_points` 是这一级给的加点，
/// 满级那一行 `exp_to_next=0`（当前 20 级封顶）。
///
/// 口径：经验进**队伍池**（存档里的 `party_exp`），谁升级就从这个池子里扣一份。
/// 多人时**先补等级最低的那个**（避免一直练同一个人），池子付不起就停。
/// 这条「共享池」是开发侧定的（设计没写分配规则，原来只记账不消费），已记进对接表等设计确认。
///
/// **过渡期的 `state` 收成 `GodotObject`**：`GameState` 还是 GDScript，等它搬过来再收紧类型。
/// 本类只用它读 `char_ids`／`party_exp`，以及调 `level_of`／`append_level`／`char_name`
/// 三个方法。构造走 `create(...)` 静态工厂（GDScript 的 `new(db, state)` 看不见 C# 构造签名）。
/// </summary>
public partial class LevelService : RefCounted
{
    private readonly TableDb _db;
    private readonly GodotObject _state;

    public LevelService(TableDb db, GodotObject state)
    {
        _db = db;
        _state = state;
    }

    /// <summary>GDScript 侧唯一的构造入口（见类注释）。</summary>
    public static LevelService create(TableDb db, GodotObject state) => new(db, state);

    /// <summary>升下一级要多少经验；满级（或表里没这级）返回 0。</summary>
    public int exp_to_next(int level)
    {
        var row = _db.get_row("level_growth", level) as LevelGrowthRow;
        return row != null ? System.Math.Max(0, row.exp_to_next) : 0;
    }

    public int max_level()
    {
        var top = 1;
        foreach (Variant entry in _db.rows("level_growth"))
        {
            var row = entry.AsGodotObject() as LevelGrowthRow;
            if (row != null)
            {
                top = System.Math.Max(top, row.level);
            }
        }

        return top;
    }

    /// <summary>这一级给的加点（用于面板「升级 +N 点」的提示）。</summary>
    public int upgrade_points(int level)
    {
        var row = _db.get_row("level_growth", level) as LevelGrowthRow;
        return row != null ? System.Math.Max(0, row.upgrade_points) : 0;
    }

    /// <summary>
    /// 把队伍经验池换成等级。返回
    /// `{changes: [{char_id, name, from, to, points}], spent, remaining, capped}`。
    /// </summary>
    public Godot.Collections.Dictionary apply_available_levels()
    {
        var changes = new Godot.Collections.Array();
        var spent = 0;
        if (_state == null)
        {
            return new Godot.Collections.Dictionary
            {
                { "changes", changes }, { "spent", spent }, { "remaining", 0 }, { "capped", false },
            };
        }

        string[] char_ids = _state.Get("char_ids").AsStringArray();
        var guard = 0;
        int limit = max_level() * System.Math.Max(1, char_ids.Length) + 1;
        while (guard < limit)
        {
            guard++;
            // 挑「等级最低、且池子付得起」的那个人升一级
            var chosen = "";
            var chosen_level = 0;
            var chosen_cost = 0;
            foreach (string char_id in char_ids)
            {
                int level = _state.Call("level_of", char_id).AsInt32();
                int cost = exp_to_next(level);
                if (cost <= 0 || _state.Get("party_exp").AsInt32() < cost)
                {
                    continue;
                }

                if (chosen.Length == 0 || level < chosen_level)
                {
                    chosen = char_id;
                    chosen_level = level;
                    chosen_cost = cost;
                }
            }

            if (chosen.Length == 0)
            {
                break;
            }

            _state.Set("party_exp", _state.Get("party_exp").AsInt32() - chosen_cost);
            spent += chosen_cost;
            _state.Call("append_level", chosen, 1);
            changes.Add(new Godot.Collections.Dictionary
            {
                { "char_id", chosen },
                { "name", _state.Call("char_name", _db, chosen).AsString() },
                { "from", chosen_level },
                { "to", chosen_level + 1 },
                { "points", upgrade_points(chosen_level) },
            });
        }

        var capped = true;
        foreach (string char_id in char_ids)
        {
            if (exp_to_next(_state.Call("level_of", char_id).AsInt32()) > 0)
            {
                capped = false;
                break;
            }
        }

        return new Godot.Collections.Dictionary
        {
            { "changes", changes },
            { "spent", spent },
            { "remaining", _state.Get("party_exp").AsInt32() },
            { "capped", capped },
        };
    }

    /// <summary>结算面板／面板用的一行文案：「家道失落的书生 Lv1 → Lv2（+3 点）」。</summary>
    public string describe(Godot.Collections.Array changes)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (Variant entry in changes)
        {
            Godot.Collections.Dictionary item = entry.AsGodotDictionary();
            parts.Add(
                $"{item["name"].AsString()} Lv{item["from"].AsInt32()} → Lv{item["to"].AsInt32()}"
                    + $"（+{item["points"].AsInt32()} 点）");
        }

        return string.Join("、", parts);
    }

    /// <summary>面板用：某个角色离下一级还差多少经验。</summary>
    public Godot.Collections.Dictionary exp_progress(string char_id)
    {
        int level = _state != null ? _state.Call("level_of", char_id).AsInt32() : 1;
        int need = exp_to_next(level);
        return new Godot.Collections.Dictionary
        {
            { "level", level },
            { "max_level", max_level() },
            { "need", need },
            { "pool", _state != null ? _state.Get("party_exp").AsInt32() : 0 },
            { "maxed", need <= 0 },
        };
    }
}
