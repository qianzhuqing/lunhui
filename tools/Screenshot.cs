using Godot;
using Lunhui.Core;

namespace Lunhui.Tools;

/// <summary>
/// 临时截图工具（C# 版，对位 `tools/_shot.gd`）：把某个界面在**真窗口**里渲染成 PNG，好跟示意图逐块对观感。
///
/// 用法（2026-10-11 起 C# 进不了 `--script`，入口是场景；用户参数照样跟在 `--` 后面）：
/// <code>
///   "%GODOT_BIN%" --path . --log-file .logs\ui_shot.log --quit-after 600 \
///       res://tools/screenshot_cs.tscn -- res://scenes/character_screen.tscn res://.logs/ui_shot.png
/// </code>
/// 第 3／4 个参数是可选的面板属性（如线索本的 `scope`／`scene_id`）。同时另存一张缩到 512 宽的
/// 预览（`_small.png`）——大图不塞进上下文，AGENTS 有这条纪律。
/// </summary>
public partial class Screenshot : Node
{
    private string _out = "res://.logs/ui_shot.png";
    private int _frames;
    private bool _done;
    private Node _node;
    /// <summary>第 5 个用户参数：要停在哪一页（空＝不动，面板自己决定）</summary>
    private string _tab = "";

    /// <summary>第 6 个用户参数：`&lt;槽位 id&gt;|&lt;第几格&gt;`（装备页）或 `skill:&lt;武学 id&gt;`（武学页）
    /// ——顺手点一下那里（空＝不点）。</summary>
    private string _slot = "";

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string scenePath = args.Length > 0 ? args[0] : "res://scenes/character_screen.tscn";
        if (args.Length > 1)
        {
            _out = args[1];
        }

        _node = GD.Load<PackedScene>(scenePath).Instantiate();
        if (args.Length > 2 && _has_prop(_node, "scope"))
        {
            _node.Set("scope", args[2]);
        }

        if (args.Length > 3 && _has_prop(_node, "scene_id"))
        {
            _node.Set("scene_id", args[3]);
        }

        if (args.Length > 4)
        {
            // 第 5 个参数：要停在哪一页（角色面板这种多页签的面板，逐页对观感时用）
            _tab = args[4];
        }

        if (args.Length > 5)
        {
            _slot = args[5];
        }

        // `_Ready` 正处在「根节点装配子节点」的窗口里，直接 `AddChild` 会失败——延后一帧。
        Callable.From(() => GetTree().Root.AddChild(_node)).CallDeferred();
    }

    /// <summary>开局那一帧 autoload 还没 `_ready`，所以建局要等到第二帧再做。</summary>
    private void _prepare()
    {
        Node session = GetTree().Root.GetNodeOrNull("GameSession");
        Node gameData = GetTree().Root.GetNodeOrNull("GameData");
        GodotObject db = gameData?.Get("db").AsGodotObject();
        if (session != null && db != null && session.Get("state").AsGodotObject() == null)
        {
            session.Call(
                "set_state", GameState.new_game((TableDb)db, "normal", new[] { "scholar_fallen" }, 0));
        }

        if (_node != null && _node.HasMethod("setup"))
        {
            _node.Call("setup");
        }

        // `setup()` 是幂等的（面板可能已经被 `_ready` 早跑过一遍、那会儿还没有局），
        // 所以这里把「选中的人」补上、再 refresh 一次，让内容真按这一局重画。
        GodotObject state = session?.Get("state").AsGodotObject();
        if (state != null && state.Get("char_ids").AsGodotArray().Count > 0)
        {
            string first = state.Get("char_ids").AsGodotArray()[0].AsString();
            // 面板的 `setup()` 是幂等的：它在 `_ready` 里已经跑过一遍（那会儿还没有局），
            // 这里再调也是空转，`_selected_char` 会留在空串上——「角色模板缺失」那种半截图就是这么来的。
            // 走**公开的** `select_char()` 补选人（私有字段 `_selected_char` 不在 `get_property_list()` 里，
            // 早先那句 `_has_prop(_node, "_selected_char")` 恒为假）。
            if (_node != null && _node.HasMethod("select_char"))
            {
                _node.Call("select_char", first);
            }

            if (_has_prop(_node, "_selected_char") && _node.Get("_selected_char").AsString().Length == 0)
            {
                _node.Set("_selected_char", first);
            }

            if (_node != null && _node.HasMethod("refresh"))
            {
                _node.Call("refresh");
            }

            if (_tab.Length > 0 && _node != null && _node.HasMethod("select_tab")
                && int.TryParse(_tab, out int tab_index))
            {
                _node.Call("select_tab", tab_index);
            }

            // 第 6 个参数：「slot:<槽位 id>|<第几格>」＝顺手选中一个槽位（装备页的详情／可换
            // 只有在选中之后才摆得出来，空态截图看不出效果）。跟 `_tab` 一样是**为了让截图
            // 能停在真正想看的那一屏**，不是产品里的行为。
            // 「skill:<武学 id>」同理：武学页的详情要选中一格才摆得出来。
            if (_slot.StartsWith("skill:", System.StringComparison.Ordinal)
                && _node.HasMethod("select_skill"))
            {
                _node.Call("select_skill", _slot.Substring("skill:".Length));
            }

            if (_slot.Length > 0 && _node != null && _node.HasMethod("press_slot"))
            {
                string[] parts = _slot.Split("|");
                if (parts.Length == 2 && int.TryParse(parts[1], out int slot_index))
                {
                    _node.Call("press_slot", parts[0], slot_index);
                }
            }
        }
    }

    /// <summary>`"x" in node` 对脚本变量不可靠（踩过），改成翻属性表。</summary>
    private static bool _has_prop(GodotObject node, string prop)
    {
        if (node == null)
        {
            return false;
        }

        foreach (Godot.Collections.Dictionary entry in node.GetPropertyList())
        {
            if (entry["name"].AsString() == prop)
            {
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frames += 1;
        if (_frames == 2)
        {
            _prepare();
        }

        if (_frames < 4 || _done)
        {
            return;
        }

        _done = true;
        Image image = GetTree().Root.GetTexture().GetImage();
        Error error = image.SavePng(ProjectSettings.GlobalizePath(_out));
        // 另存一张缩到 512 宽的预览——看观感用（大图不塞进上下文，AGENTS 有这条纪律）
        var small = (Image)image.Duplicate();
        small.Resize(512, 288, Image.Interpolation.Lanczos);
        small.SavePng(ProjectSettings.GlobalizePath(_out.GetBaseName() + "_small.png"));
        GD.Print($"SHOT {_out} ok={error == Error.Ok} {image.GetWidth()}x{image.GetHeight()}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
