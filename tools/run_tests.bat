@echo off
rem lunhui self-check pipeline: build tables -> refresh class cache -> run tests.
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage, so
rem non-ASCII text here turns into unreadable commands on CP936/CP1252 hosts.
setlocal
cd /d "%~dp0.."

rem Default to the Godot 4.7.1 install on this machine; override with GODOT_BIN.
if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	echo         Set GODOT_BIN to the Godot console executable.
	exit /b 1
)

if not exist ".logs" mkdir ".logs"

rem Watchdog against a runaway --script run. A GDScript runtime error inside _initialize aborts
rem it before quit() runs, and the SceneTree then iterates forever -- the whole acceptance hangs
rem (probe-verified 2026-10-03, framework notes decision 148). The cap turns that into an exit;
rem the SCRIPT ERROR log gate below turns it into a red step. Raise the cap via the env var if a
rem future suite legitimately needs more iterations.
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"

echo [1/3] building config tables ...
"%GODOT_BIN%" --headless --path . --log-file ".logs\build.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/build_tables.gd
if errorlevel 1 (
	echo [error] table build failed, see .logs\build.log
	exit /b 1
)

echo [2/3] refreshing editor class cache ...
rem Editor mode may exit non-zero when it cannot save editor settings; ignore it.
"%GODOT_BIN%" --headless --editor --quit --path . --log-file ".logs\import.log" >nul 2>&1

echo [3/3] running self-check ...
"%GODOT_BIN%" --headless --path . --log-file ".logs\tests.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/run_tests.gd
set "TEST_CODE=%ERRORLEVEL%"

rem The log marker is the source of truth: if the runner dies mid-way it is absent.
findstr /C:"SELFCHECK: OK" ".logs\tests.log" >nul 2>&1
if errorlevel 1 set "TEST_CODE=1"

rem A runtime error aborts the function that hit it: the rest of that function silently does not
rem run (the marker can still be printed, the exit code can still be 0). The log is the only trace.
call "tools\check_log_errors.bat" ".logs\tests.log" || set "TEST_CODE=1"

if "%TEST_CODE%"=="0" (
	echo [done] self-check passed
) else (
	echo [fail] self-check failed, see .logs\tests.log, exit code %TEST_CODE%
)
exit /b %TEST_CODE%
