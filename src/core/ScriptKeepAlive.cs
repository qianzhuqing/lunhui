using System.Collections.Generic;
using Godot;

namespace Lunhui.Core;

/// <summary>
/// 把全部 C# 脚本资源钉在内存里——绕开 Godot 上游的一处竞态
/// （`godotengine/godot#109711`，4.4 起就有，4.7.1 仍未修）。
///
/// **它解决的问题**（2026-10-11 实测，别删）：某个 C# 类的**最后一个实例**被 GC 掉时，
/// 引擎会连带析构它对应的 `CSharpScript`（每个 .NET 类型在 Godot 侧的 Resource 表示）并从脚本缓存里摘掉；
/// 如果主线程**正好**在"对象已标记待删、缓存项还没清干净"的那一瞬间去 `new` 同一个类，
/// 引擎会拿到一个已失效的 script 引用，直接
/// `ERROR: FATAL: Condition "script.is_null()" is true.` + `SEHException` 崩进程，**退出码 0xC000001D**。
///
/// 为什么难缠：不是必现，跟"同一批类被反复建／毁"和 GC 时机绑定。现场是平衡观测
/// （`tools/AnalysisBalance.cs` 一轮要 `new` 上万次 `GameState`／`SkillLoadout`／`EnemyFactory`）——
/// 每次跑到中段（约 270 秒、第 8 级那一段）整进程挂掉，两次复现的栈顶分别是
/// `EnemyFactory..ctor` 与 `SkillLoadout..ctor`，共同点都是 `RefCounted..ctor`。
/// 同一份旧 GDScript 链**不崩**：它 `load("res://src/core/Xxx.cs")` 拿到的 Script 被本地变量一直引用着，
/// 恰好就是上游给的这个 workaround——搬成 C# 之后没有本地引用，脚本就被 GC 了。
///
/// **做法**（与上游 issue 里给的 workaround 一致）：启动时把 `res://` 下所有 `.cs` 都加载一遍，
/// 用一个静态列表持住 → 这些 `CSharpScript` 永远不会被回收，也就不存在"摘缓存"的窗口。
/// 放在 `GameData._Ready()`（autoload，所有场景与工具链都先经过它）里调一次。
///
/// **两条已知边界**：① 导出后的发行包里 `.cs` 源文件不进包，这里会扫不到东西——那时要靠引擎自身修复；
/// ② 别在 `res://` 下留"`.cs.uid` 还在、`.cs` 已删"的孤儿 uid，扫目录本身不受影响，
/// 但引擎按 uid 解析脚本时会被它坑（删脚本时 `.uid` 一起删，仓库本来就有这条纪律）。
/// </summary>
public partial class ScriptKeepAlive : RefCounted
{
    /// <summary>钉住的脚本（只增不减，进程级）。</summary>
    private static readonly List<CSharpScript> _held = new();

    private static bool _done;

    /// <summary>加载并持住 `res://` 下的全部 `.cs`（幂等）。</summary>
    public static void hold_all()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _gather("res://");
    }


    private static void _gather(string dir)
    {
        foreach (string entry in ResourceLoader.ListDirectory(dir))
        {
            string full = dir + entry;
            if (entry.EndsWith("/"))
            {
                // 点开头的目录（`.godot` 等）不扫：里面是导入缓存，不是源脚本。
                if (!entry.StartsWith("."))
                {
                    _gather(full);
                }

                continue;
            }

            if (!entry.EndsWith(".cs"))
            {
                continue;
            }

            CSharpScript script = ResourceLoader.Load<CSharpScript>(full);
            if (script != null)
            {
                _held.Add(script);
            }
        }
    }
}