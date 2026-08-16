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
