@echo off
rem Map acceptance: run the mapgen verifier (07 section 9) and pass its verdict back to the caller.
rem It checks what the scene self-checks do not: marker/id naming across the six maps, walkable
rem connectivity from dungeon_room.exit_rooms, room side-passages, patrol endpoints, tile budgets.
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage.
setlocal
cd /d "%~dp0.."

if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	exit /b 1
)
if not exist ".logs" mkdir ".logs"

rem Watchdog: same as the other --script steps (framework notes decision 148).
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"

echo.
echo --- map acceptance (07 section 9) ---
"%GODOT_BIN%" --headless --path . --log-file ".logs\mapcheck_engine.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/mapgen/verify_maps.gd > ".logs\mapcheck_stdout.log" 2>&1
set "CODE=%ERRORLEVEL%"
rem The verdict marker is the source of truth: a wedged run can still exit 0.
findstr /C:"MAPCHECK: OK" ".logs\mapcheck_stdout.log" >nul 2>&1
if errorlevel 1 set "CODE=1"
rem A runtime error aborts the function that hit it (the verdict marker can still be printed).
call "tools\check_log_errors.bat" ".logs\mapcheck_engine.log" || set "CODE=1"
rem Surface the verdict and the per-map counts. The design-side notes (the 18 one-way
rem exit_rooms declarations etc.) stay in .logs\mapcheck_stdout.log on purpose: they are
rem informational, and findstr cannot match Chinese patterns anyway (console codepage).
findstr /C:"MAPCHECK" /C:"[check]" ".logs\mapcheck_stdout.log" 2>nul
exit /b %CODE%
