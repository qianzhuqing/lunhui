@echo off
rem Run one real-scene self-check and pass its verdict back to the caller.
rem usage: check_scene.bat <name> <scene> <flag> [extra args]
setlocal enabledelayedexpansion
cd /d "%~dp0.."

if not defined GODOT_BIN set "GODOT_BIN=F:\Godot_v4.7.1-stable_win64.exe\Godot_v4.7.1-stable_win64_console.exe"
if not exist "%GODOT_BIN%" (
	echo [error] Godot not found: %GODOT_BIN%
	exit /b 1
)
if not exist ".logs" mkdir ".logs"
rem Safety net against a wedged run: engine-level frame cap. A scene whose selftest flag is
rem misspelled runs normally and would otherwise wait for input forever (hit that while probing);
rem with the cap it quits by itself and the SELF-TEST marker below turns the step into a failure.
if not defined CHECK_MAX_FRAMES set "CHECK_MAX_FRAMES=900"

set "NAME=%~1"
set "SCENE=%~2"
set "FLAG=%~3"
set "EXTRA=%~4"

rem Headless by default. `run_windowed_smoke.bat` sets CHECK_SCENE_WINDOWED=1 to run the same
rem contract in a real window (headless uses the dummy renderer, so it cannot see texture import,
rem shader or font problems -- see framework notes decision 146). Windowed logs get a suffix so
rem the two modes do not overwrite each other's evidence.
set "HEADLESS_FLAG=--headless"
set "LOG_NAME=%NAME%"
if defined CHECK_SCENE_WINDOWED (
	set "HEADLESS_FLAG="
	set "LOG_NAME=%NAME%_windowed"
)

echo.
echo --- scene self-check: %NAME% ---
"%GODOT_BIN%" %HEADLESS_FLAG% --path . --log-file ".logs\%LOG_NAME%_smoke.log" --quit-after %CHECK_MAX_FRAMES% "%SCENE%" -- %FLAG% %EXTRA% > ".logs\%LOG_NAME%_stdout.log" 2>&1
set "CODE=%ERRORLEVEL%"
rem The log marker is the source of truth: the process can die mid-way with a zero code.
findstr /C:"SELF-TEST: OK" ".logs\%LOG_NAME%_stdout.log" >nul 2>&1
if errorlevel 1 set "CODE=1"
rem A GDScript runtime error aborts the function that hit it -- the rest of that function and the
rem remaining checks of its callers do not run, while the SELF-TEST marker can still be printed.
call "tools\check_log_errors.bat" ".logs\%LOG_NAME%_smoke.log" || set "CODE=1"
rem Surface the verdict plus the machine-readable contract lines. All of these are ASCII on
rem purpose: non-ASCII findstr patterns get mangled by the console codepage.
findstr /C:"SELF-TEST" /C:"LAYOUT" /C:"BACKDROP" /C:"CONTENT min_width" ".logs\%LOG_NAME%_stdout.log" 2>nul
exit /b %CODE%
