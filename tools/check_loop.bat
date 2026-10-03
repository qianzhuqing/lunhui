@echo off
rem Cross-scene loop self-check: real scene switches (overworld <-> local map, overworld battle).
rem The unit tests inject handlers instead of switching scenes, so this layer needs its own run.
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage.
setlocal
cd /d "%~dp0.."

if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	exit /b 1
)
if not exist ".logs" mkdir ".logs"

rem Watchdog: a GDScript runtime error inside _initialize aborts it before quit() runs and the
rem SceneTree iterates forever. The log gate below then reports the real reason.
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"

echo.
echo --- cross-scene loop ---
"%GODOT_BIN%" --headless --path . --log-file ".logs\loopcheck_engine.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/check_loop.gd > ".logs\loopcheck_stdout.log" 2>&1
set "CODE=%ERRORLEVEL%"
rem The verdict marker is the source of truth: a wedged run can still exit 0.
findstr /C:"LOOPCHECK: OK" ".logs\loopcheck_stdout.log" >nul 2>&1
if errorlevel 1 set "CODE=1"
rem A runtime error aborts the function that hit it (the verdict marker can still be printed).
call "tools\check_log_errors.bat" ".logs\loopcheck_engine.log" || set "CODE=1"
rem LOOP-prefixed detail lines are ASCII-tagged so findstr can surface them (Chinese patterns
rem do not match: cmd matches in the console codepage).
findstr /C:"LOOP" ".logs\loopcheck_stdout.log" 2>nul
exit /b %CODE%
