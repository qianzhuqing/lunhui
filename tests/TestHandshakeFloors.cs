using System.Collections.Generic;

namespace Lunhui.Tests;

/// <summary>
/// 36. 场景自检与两个验收脚本的「检查条数棘轮」（对位 `tests/test_handshake.gd` 的
/// `VERIFICATION_CHECK_FLOORS`）。
///
/// 单元自检有 `MIN_ASSERTIONS` 兜底，而**只打印一个 OK 标记**的脚本此前一条下限都没有——
/// 把某行 `ok = ok &amp;&amp; …`（或某个 `_problems.Add(…)`）删掉，`SELF-TEST: OK`／`LOOPCHECK: OK`／
/// `MAPCHECK: OK` 照样打印，验收照样全绿。这张表记的是**当前条数**（棘轮，不是目标）：
/// 删一条就红，加了新检查就往上调（余量上限见 `VerificationFloorSlack`）。
/// 每条都写明"这个 pattern 代表什么"；新加一个带自检的场景必须在表里登记一行（数量对账会拦）。
/// </summary>
public abstract partial class TestHandshake
{
    protected static readonly Dictionary<string, CheckFloor> VerificationCheckFloors = new()
    {
        {
            "src/ui/BattleScreenSelfTest.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 24,
                Why = "战斗自检的聚合断言（2026-10-11 由 .gd 搬 C#，同一批 24 条原样搬过来——含 Q88 的「切磋镜像」一条）",
            }
        },
        {
            "src/ui/ShopScreenSelfTest.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 12,
                Why = "商店场景自检的聚合断言（2026-10-11 由 .gd 搬 C#，12 条原样搬过来）",
            }
        },
        {
            "src/ui/WaypointScreen.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 11, Why = "驿站场景自检的聚合断言（11 条原样搬过来）" }
        },
        {
            "src/ui/DungeonScreen.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 11, Why = "副本场景自检的聚合断言（11 条原样搬过来）" }
        },
        {
            "src/ui/CharacterScreenSelfTest.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 29,
                Why = "角色面板自检的聚合断言（29 条原样搬过来——含 Q74①／Q67④ 的 6 条与 UI 清单 §二-4「角色详情排版」的 8 条）",
            }
        },
        {
            "src/ui/CultivateScreen.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 11, Why = "打坐场景自检的聚合断言（11 条原样搬过来）" }
        },
        {
            "src/ui/ClueScreen.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 16,
                Why = "纪事本自检的聚合断言（16 条原样搬过来——含 UI 清单 §二-10「纪事五类」加的那 8 条）",
            }
        },
        {
            "src/ui/MainMenu.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 7, Why = "启动菜单自检的聚合断言（7 条原样搬过来）" }
        },
        {
            "src/world/OverworldController.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 18,
                Why = "大地图场景自检的聚合断言（2026-10-11 由 .gd 搬 C#，旧 .gd 实际 18 条、棘轮表写 17 留了 1 条余量，这次贴平）",
            }
        },
        {
            "src/world/LocalMapController.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 15,
                Why = "小地图场景自检的聚合断言（2026-10-11 由 .gd 搬 C#，旧 .gd 实际也是 15 条、棘轮表当时写 14，这次贴平）",
            }
        },
        {
            "src/Bootstrap.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 6, Why = "配置表诊断场景自检的聚合断言（6 条原样搬过来）" }
        },
        {
            "src/ui/CreationScreenSelfTest.cs",
            new CheckFloor { Pattern = "ok = ok &&", Floor = 15, Why = "创建角色场景自检的聚合断言（15 条原样搬过来）" }
        },
        {
            "src/ui/NpcPanelSelfTest.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 25,
                Why = "NPC 面板自检的聚合断言（25 条原样搬过来——含 Q88 的「同伴只摆赠与＋切磋」「判成镜像」与 UI 清单 §二-7 的两条）",
            }
        },
        {
            "tools/CheckLoopRunner.cs",
            new CheckFloor
            {
                Pattern = "ok = ok &&",
                Floor = 15,
                Why = "跨场景闭环每段一个聚合断言（③ 现在有胜／败两次；2026-10-11 由 tools/check_loop.gd 搬 C#，15 条原样搬过来）",
            }
        },
        {
            "tools/mapgen/VerifyMapsCs.cs",
            new CheckFloor
            {
                Pattern = "_problems.Add(",
                Floor = 10,
                Why = "地图验收的「入口 ＋ 基础设施」里每个检查点都必须能报错（2026-10-11 拆成三份，三份合计 43 条与旧 .gd 逐条对齐）",
            }
        },
        {
            "tools/mapgen/VerifyMapsCsChecks.cs",
            new CheckFloor
            {
                Pattern = "_problems.Add(",
                Floor = 15,
                Why = "地图验收的「命名与归属／连通性与通路」每个检查点都必须能报错（同一批检查原样搬过来）",
            }
        },
        {
            "tools/mapgen/VerifyMapsCsChecksAssets.cs",
            new CheckFloor
            {
                Pattern = "_problems.Add(",
                Floor = 18,
                Why = "地图验收的「归属与资产」每个检查点都必须能报错（含决策 332 给「按 id 绑的 NPC 位点」加的 2 条与建筑位点的 4 条）",
            }
        },
    };
}
