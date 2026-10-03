# Godot MCP 安装说明

本项目已安装 `godot-mcp` 编辑器插件，插件会随 Godot 编辑器启动一个 MCP HTTP 服务。

## 插件信息

- 插件目录：`res://addons/godot-mcp/`
- MCP 地址：`http://127.0.0.1:3001/mcp`
- 端口可在 `project.godot` 中通过 `godot_mcp/port` 覆盖

## 使用步骤

1. 在 Godot 4.7 中打开本项目。
2. 确认插件已启用：`项目 > 项目设置 > 插件`，勾选 `Godot MCP`。
3. 保持 Godot 编辑器处于打开状态。
4. 在你的 MCP 客户端中接入该服务。

## 测试

保持 Godot 编辑器打开，然后双击 `test_mcp.bat`。

它会依次测试：
1. `initialize` — 初始化 MCP 会话
2. `tools/list` — 列出可用工具
3. `get_project_info` — 获取项目信息

预期会返回对应的 JSON 结果；如果提示连接失败，请确认 Godot 编辑器已打开且插件已启用。

## 已知限制（2026-10-03 实测）

- **探服务必须用 `POST /mcp`**：这个服务**只处理 POST**；用 `GET http://127.0.0.1:3001/`
  去探会 5 秒超时、0 字节——看着像"服务卡死了"，其实只是它不答 GET。
  2026-10-03 就因此误判过一次（当时 `netstat` 里端口明明在听），
  见 `docs/dev/框架说明.md` 决策 183。要确认服务在不在，**用 `test_mcp.bat`**
  （或手写一条 `POST /mcp` 的 `initialize`），别用浏览器/GET 试探。
- **只读接口可用**：`initialize` / `tools/list` / `get_project_info` / `list_scenes` 都正常返回
  （当天复测：`get_project_info` 报 `lunhui` / 4.7.1-stable / mobile / 主场景 `scenes/main_menu.tscn`；
  `list_scenes` 列出 19 个场景，比 `scenes/` 下多一个根目录的 `node_2d.tscn`——
  顺带查出了自检的一个盲区，见 `docs/dev/框架说明.md` 决策 71）。
- **引擎侧视角怎么用**（2026-10-03 复测）：`get_scene_info` 读的是**编辑器当前打开的那个场景**，
  不是任意场景；`get_node_property` 的 `path` 用 `"."` 表示场景根（`""`／`"/root"` 都不行）。
  这两条能验"文件解析看不出来"的部分——例如读根节点的 `script`，实测返回
  `(res://src/ui/main_menu.gd):<GDScript#...>`（**脚本真的被引擎解析成功了**，
  不只是 `.tscn` 里那行路径字符串），`anchors_preset` 返回 `15`、与场景文本一致。
- **`run_project` 会把插件的 HTTP 服务卡住**：实测调用后 20 秒收不到回应（0 字节），
  此后连 `get_project_info` 也不再响应（TCP 端口仍然接受连接）。插件代码按 `AGENTS.md` 不改，
  所以自动化验收不要依赖 `run_project`／`stop_project`；要「真跑一遍」就用命令行的等价做法：

```bat
"%GODOT_BIN%" --headless --path . --log-file ".logs\realrun_menu.log" --quit-after 240 res://scenes/main_menu.tscn
```

  看日志有没有 ERROR、看退出码即可（`.logs\realrun_menu.log`）。
- 编辑器侧若发现 MCP 不再响应，把「项目设置 > 插件」里的 Godot MCP 取消勾选再勾上，
  插件会 `stop()` 后重新 `listen()`，服务即恢复。

## Claude Desktop 配置

将 `mcp_client_config.json` 中的内容合并到：

```text
%APPDATA%\Claude\claude_desktop_config.json
```

或手动添加：

```json
{
  "mcpServers": {
    "godot": {
      "command": "npx",
      "args": [
        "-y",
        "mcp-remote",
        "http://127.0.0.1:3001/mcp"
      ]
    }
  }
}
```

> 需要本机已安装 Node.js（用于 `npx`）。首次运行会自动下载 `mcp-remote`。

## 提供的工具

| 工具 | 说明 |
|---|---|
| `get_project_info` | 获取当前 Godot 项目信息 |
| `run_project` | 在编辑器中运行项目 |
| `stop_project` | 停止当前运行的项目 |
| `get_scene_info` | 获取当前编辑场景的节点树 |
| `list_scenes` | 列出项目中的所有 `.tscn` 场景 |
| `add_node` | 向当前场景添加节点 |
| `set_node_property` | 设置节点属性 |
| `get_node_property` | 读取节点属性 |
| `save_scene` | 保存当前场景 |

## 端口冲突

如果 3001 端口被占用，请在 `project.godot` 中添加：

```ini
[godot_mcp]

port=3002
```

然后同步修改 MCP 客户端配置中的地址。
