using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 浮层栈（C# 版，对位原 `src/ui/overlay_stack.gd`；设计 18.1，0.18.1）：浮层改「栈」管理，
/// 不再是「同时只开一个」。
///
/// 规则（14 号文档「已定口径」一节）：
///   · 打开某浮层 → 压栈
///   · 目标**已在栈里** → **弹回到它**，不重复压（A→B→A 就是回到 A，不会越点越深）
///   · `Esc` → 弹一层；栈空则回场景
///   · 栈深上限 3，达上限再压则**挤掉栈底**
///
/// 这里只做**栈本身**（纯逻辑、可单测）：谁被挤掉、谁被弹掉由返回值告诉调用方，
/// 由调用方去关对应的界面。**压栈时重新读状态**那条纪律不在这一层——
/// 它属于各浮层的 `refresh()`，调用方在压栈后必须调一次。
///
/// `LIMIT`／`OVERLAY_LAYER` 走 `limit()`／`overlay_layer()` 取值口（GDScript 读不到 C# 的 `const`，坑 22）。
/// **`mount` 只有一个两参签名**：C# 的默认参数不进 Godot 的方法签名（坑 87），
/// 而**重载也不行**——一个名字出现两次，GDScript 解析静态调用会报
/// `Static function "mount()" not found in base "CSharpScript"`，`Call("mount", …)` 也跟着失灵（坑 92）。
/// 要指定画布层请走 `mount_on_layer()`。
/// </summary>
public partial class OverlayStack : RefCounted
{
    /// <summary>栈深上限（设计 18.1 定死 3）。</summary>
    public const int LIMIT = 3;

    /// <summary>浮层画布层（与 `shop_screen.tscn` 的 `layer = 5` 对齐）：压在世界与 HUD 之上。</summary>
    public const int OVERLAY_LAYER = 5;

    private readonly System.Collections.Generic.List<string> _ids = new();

    public static int limit() => LIMIT;
    public static int overlay_layer() => OVERLAY_LAYER;

    public static Node mount(Node host, Node panel) => mount_on_layer(host, panel, OVERLAY_LAYER);

    /// <summary>
    /// 把一张浮层场景挂到**画布层**上，返回它自己。
    ///
    /// **为什么必须有这一步**（2026-10-04 实测的“面板打不开”）：`npc_panel.tscn` 与
    /// `character_screen.tscn` 的根是 `Control`，直接 `add_child` 到 `Node2D` 场景根下会
    /// **跟着相机平移**——面板明明开了，玩家在屏幕上看不到（旧控制器里 `_hud_layer()` 那句注释
    /// 就是同一个坑的记录：HUD 当年也是这么跑出屏幕的）。
    /// `shop／cultivate／clue／dungeon／waypoint` 几张场景的根本来就是 `CanvasLayer`，
    /// 所以它们一直没露；这里统一收口：是 `CanvasLayer` 就照旧挂，是 `Control`（或别的
    /// CanvasItem）就包一层再挂——**新加浮层不用再记这条**。
    ///
    /// 包出来的那层跟着面板一起回收（面板离开场景树时收掉空画布），
    /// 所以调用方的 `panel.queue_free()` 不用改。
    /// </summary>
    public static Node mount_on_layer(Node host, Node panel, int layer)
    {
        if (panel is CanvasLayer)
        {
            host.AddChild(panel);
            UiKit.paint_backdrop(panel);
            return panel;
        }

        var canvas = new CanvasLayer
        {
            Name = $"{panel.Name}Layer",
            Layer = layer,
        };
        host.AddChild(canvas);
        canvas.AddChild(panel);
        panel.TreeExited += () =>
        {
            if (GodotObject.IsInstanceValid(canvas))
            {
                canvas.QueueFree();
            }
        };
        // 背板统一刷成宣纸（色值在主题里一处，见 `UiKit.paint_backdrop`）。
        // 放在 `add_child` 之后：`_ready` 已经跑完，**代码里建背板的面板**这时也建好了。
        UiKit.paint_backdrop(panel);
        return panel;
    }

    public string[] ids() => _ids.ToArray();

    public int depth() => _ids.Count;

    public bool is_empty() => _ids.Count == 0;

    public bool has(string id) => _ids.Contains(id);

    public string top() => _ids.Count == 0 ? "" : _ids[^1];

    /// <summary>
    /// 压栈。返回**需要被关掉的浮层**（可能不止一个）：
    ///   · 目标已在栈里 → 它上面的全部（「弹回到它」）
    ///   · 栈已满 → 栈底那一个（「挤掉栈底」）
    /// 目标自己不在返回里（它要留着当栈顶）。
    /// </summary>
    public Godot.Collections.Array push(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return new Godot.Collections.Array();
        }

        if (_ids.Contains(id))
        {
            return pop_to(id);
        }

        _ids.Add(id);
        var closed = new Godot.Collections.Array();
        while (_ids.Count > LIMIT)
        {
            closed.Add(_ids[0]);
            _ids.RemoveAt(0);
        }

        return closed;
    }

    /// <summary>弹一层。返回被弹掉的 id（栈空返回空串）。</summary>
    public string pop()
    {
        if (_ids.Count == 0)
        {
            return "";
        }

        string id = _ids[^1];
        _ids.RemoveAt(_ids.Count - 1);
        return id;
    }

    /// <summary>弹到指定浮层：把它**上面**的全部弹掉（自己留在栈顶）。返回被弹掉的。</summary>
    public Godot.Collections.Array pop_to(string id)
    {
        var closed = new Godot.Collections.Array();
        if (!_ids.Contains(id))
        {
            return closed;
        }

        while (_ids.Count > 0 && _ids[^1] != id)
        {
            closed.Add(_ids[^1]);
            _ids.RemoveAt(_ids.Count - 1);
        }

        return closed;
    }

    /// <summary>
    /// 程序化关闭（例：界面自己的「返回」按钮）：从栈里摘掉。
    /// 返回它**上面**被一起关掉的浮层（与 `pop_to` 同一口径：关掉一个浮层时，
    /// 压在它上面的也得跟着走，否则栈里会留下已经不在屏幕上的幽灵）。
    /// </summary>
    public Godot.Collections.Array remove(string id)
    {
        Godot.Collections.Array closed = pop_to(id);
        if (_ids.Count > 0 && _ids[^1] == id)
        {
            _ids.RemoveAt(_ids.Count - 1);
        }

        return closed;
    }

    /// <summary>清空（切场景/回图时用）。返回原本栈里的全部。</summary>
    public Godot.Collections.Array clear()
    {
        var output = new Godot.Collections.Array();
        foreach (string id in _ids)
        {
            output.Add(id);
        }

        _ids.Clear();
        return output;
    }
}
