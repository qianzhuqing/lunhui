@echo off
rem lunhui C# self-check pipeline: build the C# assembly -> run the runner scene -> gate on the log.
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage, so non-ASCII text here
rem turns into unreadable commands on CP936/CP1252 hosts.
rem
rem This is the C# counterpart of tools\run_tests.bat. While the migration is in flight both
rem pipelines run side by side (see the C# migration plan under docs/dev, phase P1) and either
rem one going red is a regression.
setlocal
cd /d "%~dp0.."

rem C# needs the .NET (mono) build of Godot; the standard build cannot load a CSharpScript.
rem GODOT_CSHARP_BIN wins, then GODOT_BIN (if the host already points it at a .NET build),
rem then the install this machine has.
if not defined GODOT_CSHARP_BIN if defined GODOT_BIN set "GODOT_CSHARP_BIN=%GODOT_BIN%"
if not defined GODOT_CSHARP_BIN set "GODOT_CSHARP_BIN=F:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
rem NOTE: do not put parentheses inside the block below -- an unescaped ")" closes the block
rem early and cmd then tries to run the rest of the echo line as a command.
if not exist "%GODOT_CSHARP_BIN%" (
	echo [error] Godot .NET build not found: %GODOT_CSHARP_BIN%
	echo         Set GODOT_CSHARP_BIN to the Godot .NET console executable.
	exit /b 1
)

if not exist ".logs" mkdir ".logs"
call "tools\ensure_csharp_build.bat" || exit /b 1

rem Watchdog against a runaway run: a crash inside _Ready can leave the main loop turning forever.
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"

rem Rebuild the generated tables first: the C# row classes are round-tripped through
rem data/generated/*.tres, and tests\TestRows.cs reads them. tools\build_tables.tscn is
rem the C# port of the old build_tables.gd (P4, 2026-10-11) -- it needs the .NET build.
echo [1/2] rebuilding config tables ...
"%GODOT_CSHARP_BIN%" --headless --path . --log-file ".logs\build.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% res://tools/build_tables.tscn
if errorlevel 1 (
	echo [error] table build failed, see .logs\build.log
	exit /b 1
)

echo [2/2] running the C# self-check ...
"%GODOT_CSHARP_BIN%" --headless --path . --log-file ".logs\tests_cs.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% res://tools/run_tests_cs.tscn
set "TEST_CODE=%ERRORLEVEL%"

rem The log marker is the source of truth: if the runner dies mid-way it is absent.
findstr /C:"SELFCHECK: OK" ".logs\tests_cs.log" >nul 2>&1
if errorlevel 1 set "TEST_CODE=1"

rem A runtime error aborts the check that hit it: the rest of it silently does not run while the
rem marker can still be printed. The log is the only trace.
call "tools\check_log_errors.bat" ".logs\tests_cs.log" || set "TEST_CODE=1"

if "%TEST_CODE%"=="0" (
	echo [done] C# self-check passed
) else (
	echo [fail] C# self-check failed, see .logs\tests_cs.log, exit code %TEST_CODE%
)
exit /b %TEST_CODE%
