@echo off
rem Volume observation snapshot: prints timings/memory for the heaviest fixtures
rem (4-man full-equipment party + big bag, 40 clues, 30 dungeon floors).
rem Read-only analysis, NOT a pass-fail gate -- durations vary with the machine,
rem so it is deliberately kept out of run_all_checks.bat.
rem usage: tools\run_perf_analysis.bat
rem Keep this file ASCII-only + CRLF (cmd.exe parses it in the local codepage).
setlocal
cd /d "%~dp0.."

if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	echo         Set GODOT_BIN to the Godot console executable.
	exit /b 1
)

if not exist ".logs" mkdir ".logs"
rem Rebuild the generated tables FIRST -- same trap as run_balance_analysis.bat: without it the
rem measurement silently reads the PREVIOUS build's data (see framework notes decision 167).
"%GODOT_BIN%" --headless --path . --log-file ".logs\perf_build.log" --quit-after 2000 --script res://tools/build_tables.gd
if errorlevel 1 (
	echo [error] table build failed, see .logs\perf_build.log
	exit /b 1
)
call "tools\check_log_errors.bat" ".logs\perf_build.log" || exit /b 1
rem --quit-after is a wedge net, same reasoning as check_scene.bat: a --script run whose
rem _initialize() hits a runtime error returns to the main loop and never calls quit(),
rem so the console would sit there forever. With the frame cap it exits on its own.
rem It also means a broken run is NOT distinguishable by exit code, so read the output.
"%GODOT_BIN%" --headless --path . --log-file ".logs\perf.log" --quit-after 900 --script res://tools/analysis_perf.gd
set "RUN_CODE=%ERRORLEVEL%"
rem Marker first: a runtime error aborts the script, and a half table looks exactly like a full one.
findstr /C:"PERF SNAPSHOT: OK" ".logs\perf.log" >nul 2>&1
if errorlevel 1 (
	echo [error] no "PERF SNAPSHOT: OK" in .logs\perf.log -- the analysis stopped early
	echo         the numbers printed above may be from half a run
	set "RUN_CODE=1"
)
call "tools\check_log_errors.bat" ".logs\perf.log" || set "RUN_CODE=1"
exit /b %RUN_CODE%
