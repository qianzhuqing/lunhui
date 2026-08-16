@tool
extends EditorPlugin

const McpHttpServer = preload("res://addons/godot-mcp/mcp_http_server.gd")

var server


func _enter_tree() -> void:
	set_process(true)
	var port: int = ProjectSettings.get_setting("godot_mcp/port", 3001)
	server = McpHttpServer.new()
	if server.start(port, self):
		print("[Godot MCP] MCP server started at http://127.0.0.1:%d/mcp" % port)
	else:
		server = null
		push_error("[Godot MCP] Could not start MCP server.")


func _exit_tree() -> void:
	set_process(false)
	if server != null:
		server.stop()
		server = null


func _process(_delta: float) -> void:
	if server != null:
		server.poll()
