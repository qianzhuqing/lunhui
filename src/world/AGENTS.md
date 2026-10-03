# 世界层约定（`src/world/`）

适用：大地图／小地图控制器、明雷、输入映射、地图资源。
仓库级约定与命令见根 `AGENTS.md`；核心逻辑看 `src/core/AGENTS.md`；界面看 `src/ui/AGENTS.md`。

- **玩法入口顺序**：启动菜单 →（新建游戏）→ 占位游戏场景 →「进入大地图」→ `world_run` → 撞明雷 → 战斗界面 → 回大地图。
  **大地图明雷 0.8.1 起默认关**（`feature_toggle.overworld_roaming_enemy = 0`，机制没删）：
  开关的读点在 `OverworldController.roaming_enabled()`，会话级覆盖是 `GameSession.roaming_enabled_override`
  （-1 读表／0 关／1 开）；自检与 `tools/check_loop.gd` 都会自己打开它，别把"默认关"当成机制坏了。
- 输入映射在运行期注册（`src/world/input_setup.gd`），改键只改那一处；不要往 project.godot 手写 InputEvent 文本。
- 地编的地图资产（`scenes/maps/`）视为只读：运行期控制层在 `src/world/`，不要直接改图去迁就代码。
- **小地图落点与出口有三条硬约定**（都是踩过坑才定下的，见 `docs/dev/框架说明.md` 决策 42）：
  ① 进图落点用**地编放的 `Characters/player_spawn`**，不要自己按 `dungeon_room` 猜入口；
  ② 房间落点取 **`bounds` 矩形中心**（房间节点位置是矩形左上角，站角上会被墙推出去、
     `current_room_id()` 认不到房间）；③ 出口必须「**先走出去一次**」才武装——
     出生点压在 `Exit_` 位点上是合法摆法（清风驿就是），否则一进城就被弹回大地图。
- **判定位点按「图上真摆了 `Event_<check_id>` ∧ 行允许出现在这张图」收**（`local_map_controller._collect_events`
  ＋ `EventCheckService.check_allowed_in_scene`）：表里 `scene_id` 空、只填 `region_id` 的判定（赌局、碑文）
  也能在图里触发。加新判定位点时，`verify_maps` 会硬校验「位点必须接得上」。
- **改跨场景衔接（大地图↔小地图、战斗↔回程、驿站传送）之后必须跑 `tools\check_loop.bat`**：
  它在真引擎里跑真实场景切换，外加一次**真文件存读档指纹比对**。用例里大多数切场景都被 `handler`
  拦下了，所以那一层只有它能看见（决策 48／49／50 的四个真 bug 全在这一层）。入口同样有
  「先走出去一次才武装」的规矩。这条规矩的完整口径在根 `AGENTS.md` 的「硬性约束」（存读档那一半
  归 `src/core/AGENTS.md`）。
