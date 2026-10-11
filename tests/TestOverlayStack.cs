using Lunhui.Ui;

namespace Lunhui.Tests;

/// <summary>
/// 浮层栈（设计 18.1，0.18.1）：纯逻辑——谁被弹、谁被挤，全在返回值里（C# 版，
/// 对位原 `tests/test_overlay_stack.gd`）。
///
/// 界面那一侧的接线（商店 → 角色 → Esc 回商店）在
/// `--local-selftest` 的「浮层栈」一段里验（真实场景、真实面板）。
/// </summary>
public partial class TestOverlayStack : TestCase
{
    public override string SuiteName() => "浮层栈（压栈 / 弹回 / 栈深上限）";

    public override void Run()
    {
        var stack = new OverlayStack();
        CheckEq(OverlayStack.LIMIT, 3, "栈深上限是 3（设计 18.1 定死）");
        CheckTrue(stack.is_empty(), "开局栈是空的");
        CheckEq(stack.pop(), "", "空栈弹一层返回空串（调用方据此做场景层的事）");

        // 压栈：底 → 顶（`_Join` 把栈打成 "底|…|顶" 好读）
        CheckTrue(stack.push("shop").Count == 0, "压第一个不挤掉任何人");
        stack.push("character");
        stack.push("clue");
        CheckEq(stack.depth(), 3, "压了三层");
        CheckEq(stack.top(), "clue", "栈顶是最后压进去的那个");
        CheckEq(_Join(stack), "shop|character|clue", "顺序就是压进来的顺序");

        // 已达上限再压 → **挤掉栈底**
        Godot.Collections.Array squeezed = stack.push("dungeon");
        CheckEq(squeezed.Count, 1, "超上限时挤掉一个（栈底）");
        CheckEq(squeezed[0].AsString(), "shop", "挤掉的正是栈底那个");
        CheckEq(stack.depth(), 3, "挤完仍然只有三层");
        CheckFalse(stack.has("shop"), "被挤掉的已经不在栈里");
        CheckEq(_Join(stack), "character|clue|dungeon", "栈底被挤掉后，原来的第二层成了栈底");

        // 目标已在栈里 → **弹回到它**，不重复压（A→B→A 就是回到 A）
        Godot.Collections.Array closed = stack.push("clue");
        CheckEq(closed.Count, 1, "弹回到 clue：把它上面的 dungeon 弹掉");
        CheckEq(closed[0].AsString(), "dungeon", "被弹掉的是压在它上面的那个");
        CheckEq(stack.top(), "clue", "弹回之后它就是栈顶");
        CheckEq(_Join(stack), "character|clue", "没有把 clue 再压一遍（栈深 2）");

        // 弹一层
        CheckEq(stack.pop(), "clue", "弹一层拿到栈顶那个");
        CheckEq(stack.depth(), 1, "弹掉一层");
        CheckEq(stack.top(), "character", "新的栈顶是原本压在它下面的");

        // 程序化关闭（界面自己的「返回」按钮）：它上面的也要跟着走
        stack.push("shop");
        stack.push("clue");
        CheckEq(_Join(stack), "character|shop|clue", "「角色」在最底下，上面压着商店与线索本");
        Godot.Collections.Array removed = stack.remove("shop");
        CheckEq(removed.Count, 1, "关掉中间那层时，压在它上面的一起走");
        CheckEq(removed[0].AsString(), "clue", "被带走的是压在它上面的线索本");
        CheckFalse(stack.has("shop"), "中间那层自己也没了");
        CheckEq(_Join(stack), "character", "只剩栈底那个");

        // clear（切场景时用）
        stack.clear(); // 先把上一段留下的栈底清掉，免得数错
        stack.push("a");
        stack.push("b");
        CheckEq(stack.clear().Count, 2, "clear 返回原本栈里的全部");
        CheckTrue(stack.is_empty(), "clear 之后栈是空的");
    }

    private static string _Join(OverlayStack stack)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (string id in stack.ids())
        {
            parts.Add(id);
        }

        return string.Join("|", parts);
    }
}
