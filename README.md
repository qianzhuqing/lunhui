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

- 工程结构、数据约定与已知实现决策：[docs/dev/框架说明.md](docs/dev/框架说明.md)
- 策划文档：[docs/design/README.md](docs/design/README.md)
- 自检（生成配置表 → 校验 → 跑用例，退出码 0 表示全绿）：

```bat
tools\run_tests.bat
```

配置表源文件在 `data/tables/`，运行期只读 `data/generated/`（构建生成，不进 Git）。
