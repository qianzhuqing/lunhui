using System.Text.RegularExpressions;
using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 界面文案守卫（C# 版，对位原 `src/ui/copy_guard.gd`）：扫一个界面下**所有控件文字**里
/// 「表内英文 id 形态」的 token。
///
/// AGENTS 的硬规矩：给玩家看的界面文案不许出现表里的英文 id（`dot_poison`／`eq_sword_01`／
/// `facility_pawnshop`…），要用表里的 `name_cn`。这条规矩原来只被 `tests/TestCopyAudit.cs` 盯着两个界面
/// （启动菜单与枢纽），而**活的那一条**（枢纽页「章节：第 1 章（chapter_01）」）就是那么漏出去的；
/// 2026-10-03 把判据抽到这里，接进**每一个场景自检**（见框架说明决策 244）。
///
/// 判据：小写字母打头、中间带下划线的 token（`[a-z][a-z0-9]*_[a-z0-9_]+`）。
/// 中文标点、`Lv1`、`F5`、`Tab`、`Esc`、`1x/2x/4x` 都不带下划线，不会误伤；
/// **扫描不看可见性**，所以 TabContainer 里没被切到的页也能扫到（容器只是跳过排版，行本身在）。
/// </summary>
public partial class CopyGuard : RefCounted
{
    public const string ID_TOKEN_PATTERN = "[a-z][a-z0-9]*_[a-z0-9_]+";

    private static readonly Regex TokenRegex = new(ID_TOKEN_PATTERN, RegexOptions.Compiled);

    /// <summary>这个界面里所有「像表内 id」的文字，形如 ["节点名：原文", ...]。</summary>
    public static string[] id_tokens(Node root)
    {
        var found = new System.Collections.Generic.List<string>();
        foreach (Variant entry in _texts(root))
        {
            Godot.Collections.Dictionary row = entry.AsGodotDictionary();
            string text = row["text"].AsString();
            if (TokenRegex.IsMatch(text))
            {
                found.Add($"{row["node"].AsString()}：{text.StripEdges()}");
            }
        }

        return found.ToArray();
    }

    /// <summary>机器可读的一行（自检日志里捞它，与 LAYOUT／CONTENT／BACKDROP 同一口径；刻意用 ASCII）。</summary>
    public static string ascii_line(Node root)
    {
        string[] found = id_tokens(root);
        string ok = found.Length == 0 ? "true" : "false";
        return $"COPY id_tokens={found.Length} ok={ok}";
    }

    /// <summary>递归取控件文字：Label／Button／RichTextLabel（其余控件没有玩家可见文字）。</summary>
    private static Godot.Collections.Array _texts(Node node)
    {
        var output = new Godot.Collections.Array();
        foreach (Node child in node.GetChildren())
        {
            string text = child switch
            {
                Label label => label.Text,
                Button button => button.Text,
                RichTextLabel rich => rich.Text,
                _ => "",
            };
            if (!string.IsNullOrEmpty(text.StripEdges()))
            {
                output.Add(new Godot.Collections.Dictionary
                {
                    { "node", child.Name.ToString() },
                    { "text", text },
                });
            }

            output.AddRange(_texts(child));
        }

        return output;
    }
}
