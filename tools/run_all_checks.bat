@echo off
rem One-command acceptance run: tables + unit self-check + table validation + table usage audit
rem + the 10 real-scene self-checks. One line per step, summary at the end, exit code 0 = all green.
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage, non-ASCII turns into garbage.
rem Steps run as child .bat files, and the pass/fail marking is inline on the same line --
rem an inline "call :label" in this script could not find its label the first time (cmd quirk),
rem and shelling out is the reliable form.
setlocal enabledelayedexpansion
cd /d "%~dp0.."

rem Default to the Godot 4.7.1 install on this machine; override with GODOT_BIN.
if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	echo         Set GODOT_BIN to the Godot console executable.
	exit /b 1
)
if not exist ".logs" mkdir ".logs"

set "FAILED_STEPS="
set "PASSED_STEPS=0"

echo === lunhui: all checks ===
rem Whole-run stopwatch: one number to compare across runs. Visibility only, no gate --
rem absolute times are machine-dependent, a threshold here would just turn noise into red.
set "RUN_T0="
for /f %%i in ('powershell -NoProfile -Command "[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()"') do set "RUN_T0=%%i"

call "tools\check_command.bat" "unit self-check (tables included)" "tools\run_tests.bat" && (echo   [OK] unit self-check& set /a PASSED_STEPS+=1) || (echo   [FAIL] unit self-check& set "FAILED_STEPS=!FAILED_STEPS! unit")
call "tools\check_command.bat" "table validation" "powershell -NoProfile -ExecutionPolicy Bypass -File tools\validate_tables.ps1" && (echo   [OK] table validation& set /a PASSED_STEPS+=1) || (echo   [FAIL] table validation& set "FAILED_STEPS=!FAILED_STEPS! tables")
call "tools\check_command.bat" "table usage audit" "powershell -NoProfile -ExecutionPolicy Bypass -File tools\audit_table_usage.ps1" && (echo   [OK] table usage audit& set /a PASSED_STEPS+=1) || (echo   [FAIL] table usage audit& set "FAILED_STEPS=!FAILED_STEPS! table-usage")
call "tools\check_maps.bat" && (echo   [OK] map acceptance& set /a PASSED_STEPS+=1) || (echo   [FAIL] map acceptance& set "FAILED_STEPS=!FAILED_STEPS! maps")
call "tools\check_loop.bat" && (echo   [OK] cross-scene loop& set /a PASSED_STEPS+=1) || (echo   [FAIL] cross-scene loop& set "FAILED_STEPS=!FAILED_STEPS! loop")

call "tools\check_scene.bat" "bootstrap" "res://scenes/bootstrap.tscn" "--bootstrap-selftest" "" && (echo   [OK] scene:bootstrap& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:bootstrap& set "FAILED_STEPS=!FAILED_STEPS! bootstrap")
call "tools\check_scene.bat" "battle"    "res://scenes/battle_screen.tscn"    "--battle-selftest"    "" && (echo   [OK] scene:battle& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:battle& set "FAILED_STEPS=!FAILED_STEPS! battle")
call "tools\check_scene.bat" "world"     "res://scenes/world_run.tscn"        "--world-selftest"     "" && (echo   [OK] scene:world& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:world& set "FAILED_STEPS=!FAILED_STEPS! world")
call "tools\check_scene.bat" "local"     "res://scenes/local_run.tscn"        "--local-selftest"     "" && (echo   [OK] scene:local& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:local& set "FAILED_STEPS=!FAILED_STEPS! local")
call "tools\check_scene.bat" "character" "res://scenes/character_screen.tscn" "--character-selftest" "" && (echo   [OK] scene:character& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:character& set "FAILED_STEPS=!FAILED_STEPS! character")
call "tools\check_scene.bat" "shop"      "res://scenes/shop_screen.tscn"      "--shop-selftest"      "" && (echo   [OK] scene:shop& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:shop& set "FAILED_STEPS=!FAILED_STEPS! shop")
call "tools\check_scene.bat" "cultivate" "res://scenes/cultivate_screen.tscn" "--cultivate-selftest" "" && (echo   [OK] scene:cultivate& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:cultivate& set "FAILED_STEPS=!FAILED_STEPS! cultivate")
call "tools\check_scene.bat" "waypoint"  "res://scenes/waypoint_screen.tscn"  "--waypoint-selftest"  "" && (echo   [OK] scene:waypoint& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:waypoint& set "FAILED_STEPS=!FAILED_STEPS! waypoint")
call "tools\check_scene.bat" "clue"      "res://scenes/clue_screen.tscn"      "--clue-selftest"      "" && (echo   [OK] scene:clue& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:clue& set "FAILED_STEPS=!FAILED_STEPS! clue")
call "tools\check_scene.bat" "dungeon"   "res://scenes/dungeon_screen.tscn"   "--dungeon-selftest"   "" && (echo   [OK] scene:dungeon& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:dungeon& set "FAILED_STEPS=!FAILED_STEPS! dungeon")
call "tools\check_scene.bat" "creation"  "res://scenes/creation_screen.tscn"  "--creation-selftest" "" && (echo   [OK] scene:creation& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:creation& set "FAILED_STEPS=!FAILED_STEPS! creation")
call "tools\check_scene.bat" "npc"       "res://scenes/npc_panel.tscn"        "--npc-selftest"       "" && (echo   [OK] scene:npc& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:npc& set "FAILED_STEPS=!FAILED_STEPS! npc")
call "tools\check_scene.bat" "menu"      "res://scenes/main_menu.tscn"        "--menu-selftest"      "--save-dir=res://.logs/menu_selftest" && (echo   [OK] scene:menu& set /a PASSED_STEPS+=1) || (echo   [FAIL] scene:menu& set "FAILED_STEPS=!FAILED_STEPS! menu")

rem Final step: the run must not leave an engine process behind. A selftest that wedges (misspelled
rem flag, missing quit) used to survive the harness -- that happened twice while building this.
rem Queried through PowerShell, not tasklist: tasklist answered "Access denied" in this session, and a
rem check that cannot look must not report OK ("could not query" is a failure here, not a pass).
rem Report only, never kill: a console process could just as well be one the user started by hand.
rem Note: GODOT_BIN points at Godot_..._console.exe, which is a 198 KB LAUNCHER -- the real engine
rem (Godot_..._win64.exe, 179 MB) runs as its child, and killing the launcher takes the child with it.
rem So 'the launcher is still alive' is a valid proxy for 'a run was left behind'. We filter on the
rem launcher name on purpose: the editor process carries the ENGINE name, so matching that would flag
rem the user's own open editor. Do not 'fix' this into an engine-name match.
set "ENGINE_NAME="
for %%f in ("%GODOT_BIN%") do set "ENGINE_NAME=%%~nf"
set "LEFTOVER_RAW="
for /f "delims=" %%p in ('powershell -NoProfile -Command "try { $ids = @(Get-Process -Name %ENGINE_NAME% -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id); if ($ids.Count -eq 0) { Write-Output NONE } else { Write-Output ($ids -join ' ') } } catch { Write-Output QUERY-FAILED }"') do set "LEFTOVER_RAW=%%p"
echo.
echo --- no leaked engine process ---
if /I "%LEFTOVER_RAW%"=="NONE" (
	echo   [OK] no leaked engine process
	set /a PASSED_STEPS+=1
) else if /I "%LEFTOVER_RAW%"=="QUERY-FAILED" (
	echo   [FAIL] could not query the process list -- cannot prove no engine was left behind
	set "FAILED_STEPS=!FAILED_STEPS! leaked-process-unknown"
) else (
	echo   [FAIL] engine process still running ^(PIDs: %LEFTOVER_RAW%^) -- close it if you started it, otherwise investigate
	set "FAILED_STEPS=!FAILED_STEPS! leaked-process"
)

echo.
echo === summary ===
echo   passed steps: %PASSED_STEPS%
set "RUN_ELAPSED_MS=(unknown)"
if defined RUN_T0 for /f %%e in ('powershell -NoProfile -Command "[long][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - [long]%RUN_T0%"') do set "RUN_ELAPSED_MS=%%e"
echo   PERF total_elapsed_ms=%RUN_ELAPSED_MS%
if defined FAILED_STEPS (
	echo   FAILED steps:%FAILED_STEPS%
	echo ALL CHECKS: FAILED
	exit /b 1
)
echo ALL CHECKS: PASSED
exit /b 0
