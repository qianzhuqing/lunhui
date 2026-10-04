@echo off
rem Windowed render smoke: run EVERY scene self-check in a REAL window (D3D12 + Forward Mobile)
rem instead of the headless dummy renderer -- headless cannot see texture import, shader or font
rem problems, so this is the only path that covers the render side (framework notes 146).
rem 2026-10-03: widened from the 3 heaviest scenes to all 11 (same list as run_all_checks.bat).
rem A full sweep costs about 20 seconds, and the other 8 panels had never been rendered at all
rem (framework notes 200).
rem
rem NOT part of run_all_checks.bat on purpose: acceptance must run without a GPU or a desktop
rem session. Run this by hand after changing UI, textures or fonts.
rem Keep this file ASCII-only + CRLF: cmd.exe parses it in the local codepage.
setlocal enabledelayedexpansion
cd /d "%~dp0.."

if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	echo         Set GODOT_BIN to the Godot console executable.
	exit /b 1
)
if not exist ".logs" mkdir ".logs"

rem Hand the windowed switch to check_scene.bat: same verdict marker, same runtime-error log gate,
rem just without --headless and with its own log file names.
rem Rebuild the generated tables first: every scene self-check reads data/generated/*.tres, and
rem only the run_tests chain rebuilt them -- editing a CSV and running this smoke directly would
rem judge the OLD data (a full round was chased on a stale copy, 2026-10-04).
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"
echo [build] rebuilding config tables ...
"%GODOT_BIN%" --headless --path . --log-file ".logs\build.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/build_tables.gd
if errorlevel 1 (
	echo [error] table build failed, see .logs\build.log
	exit /b 1
)
rem Scan the build log too: this file now starts an engine itself, so it owes the same runtime-error
rem gate as the other engine-starting scripts (the repo gate enforces check_log_errors.bat here).
call "tools\check_log_errors.bat" ".logs\build.log"
if errorlevel 1 (
	echo [error] table build left a runtime error in .logs\build.log
	exit /b 1
)

set "CHECK_SCENE_WINDOWED=1"

set "FAILED_STEPS="
set "PASSED_STEPS=0"

echo === lunhui: windowed render smoke ===
call "tools\check_scene.bat" "bootstrap" "res://scenes/bootstrap.tscn" "--bootstrap-selftest" "" && (echo   [OK] windowed:bootstrap& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:bootstrap& set "FAILED_STEPS=!FAILED_STEPS! bootstrap")
call "tools\check_scene.bat" "battle" "res://scenes/battle_screen.tscn" "--battle-selftest" "" && (echo   [OK] windowed:battle& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:battle& set "FAILED_STEPS=!FAILED_STEPS! battle")
call "tools\check_scene.bat" "world" "res://scenes/world_run.tscn" "--world-selftest" "" && (echo   [OK] windowed:world& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:world& set "FAILED_STEPS=!FAILED_STEPS! world")
call "tools\check_scene.bat" "local" "res://scenes/local_run.tscn" "--local-selftest" "" && (echo   [OK] windowed:local& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:local& set "FAILED_STEPS=!FAILED_STEPS! local")
call "tools\check_scene.bat" "character" "res://scenes/character_screen.tscn" "--character-selftest" "" && (echo   [OK] windowed:character& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:character& set "FAILED_STEPS=!FAILED_STEPS! character")
call "tools\check_scene.bat" "shop" "res://scenes/shop_screen.tscn" "--shop-selftest" "" && (echo   [OK] windowed:shop& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:shop& set "FAILED_STEPS=!FAILED_STEPS! shop")
call "tools\check_scene.bat" "cultivate" "res://scenes/cultivate_screen.tscn" "--cultivate-selftest" "" && (echo   [OK] windowed:cultivate& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:cultivate& set "FAILED_STEPS=!FAILED_STEPS! cultivate")
call "tools\check_scene.bat" "waypoint" "res://scenes/waypoint_screen.tscn" "--waypoint-selftest" "" && (echo   [OK] windowed:waypoint& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:waypoint& set "FAILED_STEPS=!FAILED_STEPS! waypoint")
call "tools\check_scene.bat" "clue" "res://scenes/clue_screen.tscn" "--clue-selftest" "" && (echo   [OK] windowed:clue& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:clue& set "FAILED_STEPS=!FAILED_STEPS! clue")
call "tools\check_scene.bat" "dungeon" "res://scenes/dungeon_screen.tscn" "--dungeon-selftest" "" && (echo   [OK] windowed:dungeon& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:dungeon& set "FAILED_STEPS=!FAILED_STEPS! dungeon")
call "tools\check_scene.bat" "creation" "res://scenes/creation_screen.tscn" "--creation-selftest" "" && (echo   [OK] windowed:creation& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:creation& set "FAILED_STEPS=!FAILED_STEPS! creation")
call "tools\check_scene.bat" "npc" "res://scenes/npc_panel.tscn" "--npc-selftest" "" && (echo   [OK] windowed:npc& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:npc& set "FAILED_STEPS=!FAILED_STEPS! npc")
call "tools\check_scene.bat" "menu" "res://scenes/main_menu.tscn" "--menu-selftest" "--save-dir=res://.logs/menu_selftest" && (echo   [OK] windowed:menu& set /a PASSED_STEPS+=1) || (echo   [FAIL] windowed:menu& set "FAILED_STEPS=!FAILED_STEPS! menu")

echo.
echo === summary ===
echo   passed steps: %PASSED_STEPS%
if defined FAILED_STEPS (
	echo   FAILED steps:%FAILED_STEPS%
	echo WINDOWED SMOKE: FAILED
	exit /b 1
)
rem Reminder for the reader: this run cannot prove the absence of render warnings by itself --
rem grep the *_windowed_smoke.log files for ERROR/WARNING if a scene looks off.
echo WINDOWED SMOKE: PASSED
exit /b 0
