# lunhui

一个 Godot 4.7 项目。

## 项目信息

- **项目名称**: lunhui
- **引擎**: Godot 4.7（Mobile 渲染器）
- **物理引擎**: Jolt Physics

## 开始使用

1. 使用 Godot 4.7 或更高版本打开本目录。
2. 等待编辑器导入资源。
3. 按 F5 运行项目，进入启动菜单（新建游戏／读取存档／退出游戏）。

## 开发

- **想先看现状**（能玩到什么、还缺什么）：[docs/dev/当前状态.md](docs/dev/当前状态.md) 这一页看板
- 工程结构、数据约定与已知实现决策：[docs/dev/框架说明.md](docs/dev/框架说明.md)
- 策划文档：[docs/design/README.md](docs/design/README.md)
- **验收（推荐，一条命令）**：生成配置表 → 自检 → 表校验 → 配表审计 → 地图验收 → 跨场景闭环 →
  13 个真实场景自检；末尾看到 `ALL CHECKS: PASSED`、退出码 0 即全绿（步数以输出为准）：

```bat
tools\run_all_checks.bat
```

- 只跑单元自检（改了一小块代码时更快，约 25 秒）：

```bat
tools\run_tests.bat
```

配置表源文件在 `data/tables/`，运行期只读 `data/generated/`（构建生成，不进 Git）。
