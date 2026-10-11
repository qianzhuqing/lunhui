using Godot;

namespace Lunhui.Core;

/// <summary>
/// `GameState` 的「等级、坐标与给玩家看的文案」那一半（对位原 `src/core/game_state.gd` 的
/// `default_party_ids` 到 `touch_saved` 一段）。拆出来的理由：单文件 800 行硬上限。
///
/// **文案口径**：这里只做「表里怎么写的就怎么显示」，不拼游戏机制文案；
/// 队伍/难度/章节三个名字都走表（`character_base.name_cn`／`difficulty_config.name_cn`／
/// `chapter_def.name_cn`），拿不到表时才退回兜底串。
/// </summary>
public partial class GameState
{
    /// <summary>
    /// character_base 里可作为开局队伍的角色（按表的顺序，最多 MAX_PARTY 人）。
    /// 0.10.0 起开局队伍改由 `recruit_def.is_initial` 决定（见 `default_party_ids_from_recruit`），
    /// 这个方法保留给「按表顺序取前 N 行」的老用例与夹具。
    /// </summary>
    public static string[] default_party_ids(TableDb db)
    {
        var out_values = new System.Collections.Generic.List<string>();
        Godot.Collections.Array rows = db.rows("character_base");
        for (int i = 0; i < rows.Count; i++)
        {
            if (out_values.Count >= MAX_PARTY)
            {
                break;
            }

            out_values.Add(rows[i].AsGodotObject().Get("char_id").AsString());
        }

        return out_values.ToArray();
    }

    /// <summary>
    /// 开局队伍：`recruit_def` 里 `is_initial=1` 的角色（设计 09 §3.2——开局只有书生一人，
    /// 同伴按 `join_condition`／`join_scene` 在剧情里陆续加入，不再取 `character_base` 的前 4 行）。
    /// recruit_def 不可用时退回「取前 MAX_PARTY 行」，免得配表出问题就开不了局。
    /// </summary>
    public static string[] default_party_ids_from_recruit(TableDb db)
    {
        var out_values = new System.Collections.Generic.List<string>();
        Godot.Collections.Array rows = db.rows("recruit_def");
        for (int i = 0; i < rows.Count; i++)
        {
            GodotObject row = rows[i].AsGodotObject();
            if (row.Get("is_initial").AsInt32() != 1)
            {
                continue;
            }

            string char_id = row.Get("char_id").AsString();
            if (db.get_row("character_base", char_id) == null)
            {
                GD.PushError($"[GameState] recruit_def 的初始成员不在 character_base 里：{char_id}");
                continue;
            }

            out_values.Add(char_id);
        }

        if (out_values.Count == 0)
        {
            GD.PushWarning("[GameState] recruit_def 里没有 is_initial=1 的成员，退回按 character_base 顺序取人");
            return default_party_ids(db);
        }

        return out_values.ToArray();
    }

    /// <summary>大地图坐标（v13）：会话层写进来的唯一入口。</summary>
    public void set_world_pos(Vector2 pos) => world_pos = new Godot.Collections.Dictionary { { "x", pos.X }, { "y", pos.Y } };

    /// <summary>存档里的大地图坐标；没有记录时返回 `Vector2.ZERO`（大地图把 ZERO 当成「用默认出生点」）。</summary>
    public Vector2 world_position()
        => new((float)_number(world_pos, "x"), (float)_number(world_pos, "y"));

    public int party_size() => char_ids.Count;

    public int level_of(string char_id) => char_levels.ContainsKey(char_id) ? char_levels[char_id].AsInt32() : 1;

    /// <summary>升一级（经验换算在 LevelService 里；这里只管等级本身，脚本与用例也直接调它）。**两个参数都要传。**</summary>
    public int append_level(string char_id, int amount)
    {
        int level = Mathf.Max(1, level_of(char_id) + Mathf.Max(1, amount));
        char_levels[char_id] = level;
        return level;
    }

    public string char_name(TableDb db, string char_id)
    {
        Resource row = db.get_row("character_base", char_id);
        if (row != null)
        {
            return row.Get("name_cn").AsString();
        }

        // 存档里还留着**已经下架的角色**（设计从 character_base 里删过角色，读档时会剔掉）：
        // 界面说人话（别再把这个 char_id 摆给玩家），日志留一条警告便于查。
        GD.PushWarning($"[GameState] 存档里的角色已经不在 character_base 里：{char_id}（读档时会剔掉）");
        return "已下架的角色";
    }

    public string difficulty_name(TableDb db)
    {
        Resource row = db.get_row("difficulty_config", difficulty_id);
        if (row != null)
        {
            return row.Get("name_cn").AsString();
        }

        GD.PushWarning($"[GameState] 存档里的难度不在 difficulty_config 里：{difficulty_id}");
        return "未知难度";
    }

    /// <summary>队伍名，取前两名角色，例如「林铁山、苏九娘」。</summary>
    public string party_label(TableDb db)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (string char_id in _strings(char_ids))
        {
            names.Add($"{char_name(db, char_id)} Lv{level_of(char_id)}");
        }

        return names.Count > 0 ? string.Join("、", names) : "（空队伍）";
    }

    /// <summary>存档列表里显示的一行摘要。</summary>
    public string short_label(TableDb db)
        => $"{party_label(db)}｜{difficulty_name(db)}｜{format_time(saved_unix)}";

    /// <summary>进入游戏后的详细摘要。</summary>
    public string[] summary_lines(TableDb db) => new[]
    {
        $"章节：{chapter_label(db)}",
        $"难度：{difficulty_name(db)}",
        $"队伍：{party_label(db)}",
        $"存档槽：{(slot <= 0 ? "未落盘" : $"第 {slot} 格")}",
        $"开局时间：{format_time(created_unix)}",
        $"最后保存：{format_time(saved_unix)}",
    };

    /// <summary>
    /// 章节显示名：`chapter_01` → 「第 1 章」。
    ///
    /// **不给玩家看 id**：这里原来写的是「第 1 章（chapter_01）」，枢纽页那行直接就把
    /// `chapter_01` 甩给了玩家（2026-10-03 实机截图里能看到）。
    ///
    /// 0.22.0 起**章节有了真正的表**（`chapter_def`）：显示名直接取 `name_cn`
    /// （「第一章·黑风寨」这种），不再从 id 里猜编号。db 没传或表里没有时退回旧口径。
    /// </summary>
    public string chapter_label(TableDb db)
    {
        if (db != null)
        {
            string name_cn = ChapterService.label_of(db, this);
            if (!string.IsNullOrEmpty(name_cn))
            {
                return name_cn;
            }
        }

        string prefix = "chapter_";
        string suffix = chapter_id.StartsWith(prefix) ? chapter_id.Substring(prefix.Length) : chapter_id;
        return int.TryParse(suffix, out int number) ? $"第 {number} 章" : "第 1 章";
    }

    /// <summary>unix 秒 → 本地时间字符串（yyyy-MM-dd HH:mm）。</summary>
    public static string format_time(int unix)
    {
        if (unix <= 0)
        {
            return "—";
        }

        Godot.Collections.Dictionary zone = Time.GetTimeZoneFromSystem();
        int bias = zone.ContainsKey("bias") ? zone["bias"].AsInt32() : 0;
        Godot.Collections.Dictionary parts = Time.GetDatetimeDictFromUnixTime(unix + bias * 60);
        return $"{(int)_number(parts, "year"):D4}-{(int)_number(parts, "month"):D2}-{(int)_number(parts, "day"):D2}"
            + $" {(int)_number(parts, "hour"):D2}:{(int)_number(parts, "minute"):D2}";
    }

    public void touch_saved() => saved_unix = (int)Time.GetUnixTimeFromSystem();

    /// <summary>从字典里取一个数（缺键给 0）——GDScript 的 `float(dict.get(key, 0.0))`。</summary>
    private static double _number(Godot.Collections.Dictionary source, string key)
        => source.ContainsKey(key) ? source[key].AsDouble() : 0.0;
}
