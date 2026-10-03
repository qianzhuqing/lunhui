@echo off
setlocal
cd /d "%~dp0"

echo === Godot MCP Test ===
echo.
echo [1/3] initialize:
curl.exe -s -X POST http://127.0.0.1:3001/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary @mcp_test_initialize.json
echo.
echo.
echo [2/3] tools/list:
curl.exe -s -X POST http://127.0.0.1:3001/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary @mcp_test_tools_list.json
echo.
echo.
echo [3/3] get_project_info:
curl.exe -s -X POST http://127.0.0.1:3001/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary @mcp_test_get_project_info.json
echo.
echo.
echo === Test finished ===

endlocal
