@tool
extends RefCounted

var tcp_server: TCPServer
var clients: Array = []
var editor_plugin: EditorPlugin


func start(port: int, plugin: EditorPlugin) -> bool:
	editor_plugin = plugin
	tcp_server = TCPServer.new()
	var err := tcp_server.listen(port, "127.0.0.1")
	if err != OK:
		push_error("[Godot MCP] Failed to listen on port %d: %s" % [port, error_string(err)])
		return false
	return true


func stop() -> void:
	for client in clients:
		var conn: StreamPeerTCP = client.get("conn")
		if conn != null:
			conn.disconnect_from_host()
	clients.clear()
	if tcp_server != null:
		tcp_server.stop()
		tcp_server = null


func poll() -> void:
	if tcp_server == null or not tcp_server.is_listening():
		return

	while tcp_server.is_connection_available():
		var conn := tcp_server.take_connection()
		if conn != null:
			clients.append({"conn": conn, "buffer": PackedByteArray()})

	for i in range(clients.size() - 1, -1, -1):
		var client: Dictionary = clients[i]
		var conn: StreamPeerTCP = client.get("conn")
		var status := conn.get_status()
		if status == StreamPeerTCP.STATUS_NONE or status == StreamPeerTCP.STATUS_ERROR:
			clients.remove_at(i)
			continue

		var available: int = conn.get_available_bytes()
		if available <= 0:
			continue

		var result := conn.get_data(available)
		if result[0] != OK:
			clients.remove_at(i)
			continue

		var chunk: PackedByteArray = result[1]
		var buffer: PackedByteArray = client.get("buffer")
		buffer.append_array(chunk)
		client["buffer"] = buffer
		_handle_client(client)
		if client.get("closed", false):
			clients.remove_at(i)


func _handle_client(client: Dictionary) -> void:
	var buffer: PackedByteArray = client.get("buffer")
	var header_end := _find_header_end(buffer)
	if header_end < 0:
		return

	var header_text := buffer.slice(0, header_end).get_string_from_ascii()
	var body_start := header_end + 4
	var content_length := _get_content_length(header_text)

	if content_length < 0:
		# Requests without a body (e.g. GET /mcp) are not handled.
		client["buffer"] = buffer.slice(body_start)
		return

	var total_needed := body_start + content_length
	if buffer.size() < total_needed:
		return

	var body_bytes := buffer.slice(body_start, total_needed)
	client["buffer"] = buffer.slice(total_needed)
	var body_text := body_bytes.get_string_from_utf8()
	_route_request(client, header_text, body_text)


func _find_header_end(data: PackedByteArray) -> int:
	for i in range(0, data.size() - 3):
		if data[i] == 13 and data[i + 1] == 10 and data[i + 2] == 13 and data[i + 3] == 10:
			return i
	return -1


func _get_content_length(header_text: String) -> int:
	for line in header_text.split("\r\n"):
		var lower := line.to_lower()
		if lower.begins_with("content-length:"):
			var value := line.split(":", true, 1)[1].strip_edges()
			return int(value)
	return -1


func _route_request(client: Dictionary, header_text: String, body_text: String) -> void:
	var lines := header_text.split("\r\n")
	var parts := lines[0].split(" ")
	if parts.size() < 2:
		_send_text(client, 400, "Bad Request")
		return

	var method := parts[0]
	if method != "POST":
		_send_text(client, 405, "Method Not Allowed")
		return

	var parsed = JSON.parse_string(body_text)
	if parsed == null:
		_send_json(client, 200, JSON.stringify(_make_error(null, -32700, "Parse error")))
		return

	if parsed is Array:
		var responses: Array = []
		for message in parsed:
			if message is Dictionary and message.has("id"):
				responses.append(_process_message(message))
		if responses.is_empty():
			_send_empty(client, 202)
		else:
			_send_json(client, 200, JSON.stringify(responses))
	elif parsed is Dictionary:
		var response: Dictionary = _process_message(parsed)
		if response.is_empty():
			_send_empty(client, 202)
		else:
			_send_json(client, 200, JSON.stringify(response))
	else:
		_send_json(client, 200, JSON.stringify(_make_error(null, -32700, "Parse error")))


func _process_message(message: Dictionary) -> Dictionary:
	if not message.has("id"):
		return {}

	var id = message.get("id")
	var method: String = message.get("method", "")

	match method:
		"initialize":
			var params: Dictionary = message.get("params", {})
			var protocol_version: String = params.get("protocolVersion", "2024-11-05")
			var result := {
				"protocolVersion": protocol_version,
				"capabilities": {"tools": {}},
				"serverInfo": {"name": "godot-mcp", "version": "1.0.0"},
			}
			return _make_response(id, result)
		"ping":
			return _make_response(id, {})
		"tools/list":
			return _make_response(id, _tools_list())
		"tools/call":
			var params: Dictionary = message.get("params", {})
			return _make_response(id, _tool_call(params))
		_:
			return _make_error(id, -32601, "Method not found: " + method)


func _make_response(id, result) -> Dictionary:
	return {"jsonrpc": "2.0", "id": id, "result": result}


func _make_error(id, code: int, message: String) -> Dictionary:
	return {"jsonrpc": "2.0", "id": id, "error": {"code": code, "message": message}}


func _tools_list() -> Dictionary:
	return {
		"tools": [
			{
				"name": "get_project_info",
				"description": "Get information about the current Godot project.",
				"inputSchema": {"type": "object", "properties": {}},
			},
			{
				"name": "run_project",
				"description": "Run the project in the Godot editor.",
				"inputSchema": {"type": "object", "properties": {}},
			},
			{
				"name": "stop_project",
				"description": "Stop the currently running project in the Godot editor.",
				"inputSchema": {"type": "object", "properties": {}},
			},
			{
				"name": "get_scene_info",
				"description": "Get the currently edited scene tree.",
				"inputSchema": {"type": "object", "properties": {}},
			},
			{
				"name": "list_scenes",
				"description": "List all .tscn scene files in the project.",
				"inputSchema": {"type": "object", "properties": {}},
			},
			{
				"name": "add_node",
				"description": "Add a node to the currently edited scene.",
				"inputSchema": {
					"type": "object",
					"properties": {
						"type": {"type": "string", "description": "The node class, e.g. Node2D, Sprite2D."},
						"name": {"type": "string", "description": "Optional node name."},
						"parent_path": {"type": "string", "description": "Optional node path of the parent. Uses the scene root when omitted."},
					},
					"required": ["type"],
				},
			},
			{
				"name": "set_node_property",
				"description": "Set a property on a node in the currently edited scene.",
				"inputSchema": {
					"type": "object",
					"properties": {
						"path": {"type": "string", "description": "Node path relative to the scene root."},
						"property": {"type": "string"},
						"value": {},
					},
					"required": ["path", "property"],
				},
			},
			{
				"name": "get_node_property",
				"description": "Get a property value from a node in the currently edited scene.",
				"inputSchema": {
					"type": "object",
					"properties": {
						"path": {"type": "string", "description": "Node path relative to the scene root."},
						"property": {"type": "string"},
					},
					"required": ["path", "property"],
				},
			},
			{
				"name": "save_scene",
				"description": "Save the currently edited scene.",
				"inputSchema": {"type": "object", "properties": {}},
			},
		]
	}


func _tool_call(params: Dictionary) -> Dictionary:
	var tool_name: String = params.get("name", "")
	var args: Dictionary = params.get("arguments", {})
	var text := ""

	match tool_name:
		"get_project_info":
			text = _tool_get_project_info()
		"run_project":
			text = _tool_run_project()
		"stop_project":
			text = _tool_stop_project()
		"get_scene_info":
			text = _tool_get_scene_info()
		"list_scenes":
			text = _tool_list_scenes()
		"add_node":
			text = _tool_add_node(args)
		"set_node_property":
			text = _tool_set_node_property(args)
		"get_node_property":
			text = _tool_get_node_property(args)
		"save_scene":
			text = _tool_save_scene()
		_:
			return {"isError": true, "content": [{"type": "text", "text": "Unknown tool: " + tool_name}]}

	return {"content": [{"type": "text", "text": text}]}


func _tool_get_project_info() -> String:
	var info := {
		"name": ProjectSettings.get_setting("application/config/name", ""),
		"path": ProjectSettings.globalize_path("res://"),
		"godot_version": String(Engine.get_version_info().get("string", "")),
		"main_scene": str(ProjectSettings.get_setting("application/run/main_scene", "")),
		"renderer": str(ProjectSettings.get_setting("rendering/renderer/rendering_method", "")),
	}
	return JSON.stringify(info, "  ")


func _tool_run_project() -> String:
	if editor_plugin == null:
		return "Editor interface is not available."
	editor_plugin.get_editor_interface().play_main_scene()
	return "Project started."


func _tool_stop_project() -> String:
	if editor_plugin == null:
		return "Editor interface is not available."
	editor_plugin.get_editor_interface().stop_playing_scene()
	return "Project stopped."


func _tool_get_scene_info() -> String:
	if editor_plugin == null:
		return "Editor interface is not available."
	var root := editor_plugin.get_editor_interface().get_edited_scene_root()
	if root == null:
		return "No scene is currently open in the editor."

	var nodes: Array = []
	var stack: Array = [root]
	while stack.size() > 0:
		var node: Node = stack.pop_back()
		nodes.append({
			"name": node.name,
			"type": node.get_class(),
			"path": str(node.get_path()),
		})
		for child in node.get_children():
			stack.append(child)

	return JSON.stringify({"root": root.name, "nodes": nodes}, "  ")


func _tool_list_scenes() -> String:
	var scenes: Array = []
	var dir := DirAccess.open("res://")
	if dir != null:
		_scan_dir(dir, scenes)
	return JSON.stringify(scenes, "  ")


func _scan_dir(dir: DirAccess, scenes: Array) -> void:
	dir.list_dir_begin()
	var entry := dir.get_next()
	while entry != "":
		if dir.current_is_dir():
			if not entry.begins_with("."):
				var sub := DirAccess.open(dir.get_current_dir().path_join(entry))
				if sub != null:
					_scan_dir(sub, scenes)
		elif entry.ends_with(".tscn"):
			scenes.append(dir.get_current_dir().path_join(entry))
		entry = dir.get_next()
	dir.list_dir_end()


func _tool_add_node(args: Dictionary) -> String:
	var node_type: String = args.get("type", "Node2D")
	var node_name: String = args.get("name", node_type)
	var parent_path: String = args.get("parent_path", "")

	if editor_plugin == null:
		return "Editor interface is not available."
	var root := editor_plugin.get_editor_interface().get_edited_scene_root()
	if root == null:
		return "No scene is currently open in the editor."

	var obj = ClassDB.instantiate(node_type)
	if obj == null:
		return "Unknown node type: " + node_type
	var node := obj as Node
	if node == null:
		return "Type is not a Node: " + node_type

	node.name = node_name

	var parent: Node = root
	if parent_path != "":
		var found := root.get_node_or_null(NodePath(parent_path))
		if found == null:
			return "Parent node not found: " + parent_path
		parent = found

	parent.add_child(node)
	node.set_owner(root)
	return "Added %s at %s" % [node_type, str(node.get_path())]


func _tool_set_node_property(args: Dictionary) -> String:
	var path: String = args.get("path", "")
	var property: String = args.get("property", "")
	var value = args.get("value", null)

	if editor_plugin == null:
		return "Editor interface is not available."
	var root := editor_plugin.get_editor_interface().get_edited_scene_root()
	if root == null:
		return "No scene is currently open in the editor."

	var node := root.get_node_or_null(NodePath(path))
	if node == null:
		return "Node not found: " + path

	node.set(property, value)
	return "Set %s.%s = %s" % [path, property, str(value)]


func _tool_get_node_property(args: Dictionary) -> String:
	var path: String = args.get("path", "")
	var property: String = args.get("property", "")

	if editor_plugin == null:
		return "Editor interface is not available."
	var root := editor_plugin.get_editor_interface().get_edited_scene_root()
	if root == null:
		return "No scene is currently open in the editor."

	var node := root.get_node_or_null(NodePath(path))
	if node == null:
		return "Node not found: " + path

	var value = node.get(property)
	return JSON.stringify({"path": path, "property": property, "value": value}, "  ")


func _tool_save_scene() -> String:
	if editor_plugin == null:
		return "Editor interface is not available."
	var root := editor_plugin.get_editor_interface().get_edited_scene_root()
	if root == null:
		return "No scene is currently open in the editor."

	var packed := PackedScene.new()
	packed.pack(root)

	var scene_path: String = root.scene_file_path
	if scene_path == "":
		scene_path = "res://" + root.name + ".tscn"

	var err := ResourceSaver.save(packed, scene_path)
	if err != OK:
		return "Failed to save scene: " + error_string(err)
	return "Scene saved: " + scene_path


func _send_empty(client: Dictionary, status_code: int) -> void:
	var conn: StreamPeerTCP = client.get("conn")
	var reason := "Accepted"
	if status_code == 200:
		reason = "OK"
	elif status_code == 400:
		reason = "Bad Request"
	elif status_code == 405:
		reason = "Method Not Allowed"
	var header := "HTTP/1.1 %d %s\r\nContent-Length: 0\r\nConnection: close\r\n\r\n" % [status_code, reason]
	conn.put_data(header.to_utf8_buffer())
	_close_client(client)


func _send_json(client: Dictionary, status_code: int, json_text: String) -> void:
	var body := json_text.to_utf8_buffer()
	var conn: StreamPeerTCP = client.get("conn")
	var reason := "OK"
	if status_code == 202:
		reason = "Accepted"
	var header := "HTTP/1.1 %d %s\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n" % [status_code, reason, body.size()]
	conn.put_data(header.to_utf8_buffer())
	conn.put_data(body)
	_close_client(client)


func _send_text(client: Dictionary, status_code: int, text: String) -> void:
	var body := text.to_utf8_buffer()
	var conn: StreamPeerTCP = client.get("conn")
	var reason := "OK"
	if status_code == 400:
		reason = "Bad Request"
	elif status_code == 405:
		reason = "Method Not Allowed"
	var header := "HTTP/1.1 %d %s\r\nContent-Type: text/plain\r\nContent-Length: %d\r\nConnection: close\r\n\r\n" % [status_code, reason, body.size()]
	conn.put_data(header.to_utf8_buffer())
	conn.put_data(body)
	_close_client(client)


# Responses are sent with "Connection: close", so the server must close its own
# end of the socket. Skipping this leaves every request stuck in CLOSE_WAIT and
# leaks one socket per call for the lifetime of the editor session.
func _close_client(client: Dictionary) -> void:
	var conn: StreamPeerTCP = client.get("conn")
	if conn == null:
		return
	# Flush any data still buffered by the engine before closing down.
	conn.poll()
	conn.disconnect_from_host()
	client["closed"] = true
