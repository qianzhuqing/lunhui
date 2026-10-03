@echo off
rem Balance observation snapshot: prints win rates / rewards / level gates for the
rem current tables. Read-only analysis, NOT a pass-fail gate -- it is deliberately
rem kept out of run_all_checks.bat because its output is a table and the numbers are
rem expected to move whenever the designers retune data.
rem usage: tools\run_balance_analysis.bat
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
rem Rebuild the generated tables FIRST. Without this the analysis silently reads whatever
rem is in data/generated -- i.e. stale data from the last build. That is not theoretical:
rem on 2026-10-03 a balance probe ran right after editing a CSV, without rebuilding, and the
rem numbers belonged to the OLD table; the wrong conclusion ("4-person parties still lose the
rem elite line") got written into docs because of it (see framework notes decision 167).
"%GODOT_BIN%" --headless --path . --log-file ".logs\balance_build.log" --quit-after 2000 --script res://tools/build_tables.gd
if errorlevel 1 (
	echo [error] table build failed, see .logs\balance_build.log
	exit /b 1
)
call "tools\check_log_errors.bat" ".logs\balance_build.log" || exit /b 1
rem --quit-after is a wedge net, same reasoning as check_scene.bat: a --script run whose
rem _initialize() hits a runtime error returns to the main loop and never calls quit(),
rem so the console would sit there forever. With the frame cap it exits on its own.
rem It also means a broken run is NOT distinguishable by exit code, so read the output.
"%GODOT_BIN%" --headless --path . --log-file ".logs\balance.log" --quit-after 900 --script res://tools/analysis_balance.gd
set "RUN_CODE=%ERRORLEVEL%"
rem Marker first: a runtime error aborts the script, and a half table looks exactly like a full one.
findstr /C:"BALANCE SNAPSHOT: OK" ".logs\balance.log" >nul 2>&1
if errorlevel 1 (
	echo [error] no "BALANCE SNAPSHOT: OK" in .logs\balance.log -- the analysis stopped early
	echo         the table printed above may be half a table
	set "RUN_CODE=1"
)
call "tools\check_log_errors.bat" ".logs\balance.log" || set "RUN_CODE=1"
exit /b %RUN_CODE%
