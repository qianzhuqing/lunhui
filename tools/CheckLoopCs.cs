using Godot;

namespace Lunhui.Tools;

/// <summary>
/// 跨场景闭环自检的**入口外壳**（对位旧链 `tools/check_loop.gd`）。
///
/// **为什么是「外壳 ＋ 跑手」两层**：闭环每一步都要 `ChangeSceneToFile`，而换场景会把
/// **当前场景**释放掉。老链 `extends SceneTree`——脚本就是场景树本身，换场景伤不到它；
/// C# 的 `--script` 只认 GDScript，只能挂场景，所以把跑手（`CheckLoopRunner`）挂到
/// **根下**（不是当前场景的子节点）：换场景只会释放"当前场景"那棵子树，跑手不在里面。
///
/// 用法（`tools/check_loop.bat` 就是这么调的）：
///   godot --headless --path . --log-file .logs/loopcheck_engine.log \
///         --quit-after 2000 res://tools/check_loop_cs.tscn
/// </summary>
public partial class CheckLoopCs : Node
{
    public override void _Ready()
    {
        // 延后一帧再挂：`_Ready` 这一刻根节点正在装配子节点，当场 AddChild 会撞
        // 「Parent node is busy setting up children」（同 `tools/RunTestsCs.cs` 那条注释）。
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new CheckLoopRunner());
    }
}
