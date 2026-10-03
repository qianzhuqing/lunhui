@echo off
rem Mutation sweep (read-only, MANUAL): break the data on purpose and check that the gates notice.
rem
rem Why: "a rule that fires" and "a rule that silently stopped working" look identical in a green
rem acceptance run. Rounds of this sweep found real holes (see framework notes 152/154/155), so the
rem method is kept as a tool instead of being rewritten each time.
rem
rem Two halves, because acceptance has two nets and they do not cover the same things:
rem   * build-time  -> tools\analysis_mutations.gd  (in-memory copies, never touches CSV/.tres)
rem   * acceptance  -> tools\analysis_mutations.ps1 (temp copies of tables + docs)
rem
rem NOT part of run_all_checks.bat: it copies files into a temp dir and is a diagnostic, not a gate.
rem
rem Usage: run_mutation_sweep.bat [code]
rem   (no arg) runs the two fast halves: build-time + acceptance (about 40 seconds)
rem   code     also runs the slow third half (code constants; each case runs the whole suite,
rem            21 cases = about 4 minutes)
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

rem Same watchdog as the other --script steps: a runtime error inside _initialize would otherwise
rem skip quit() and leave the process spinning forever.
if not defined CHECK_SCRIPT_MAX_FRAMES set "CHECK_SCRIPT_MAX_FRAMES=2000"

set "FAILED_STEPS="
set "PASSED_STEPS=0"

echo === lunhui: mutation sweep ===

echo.
echo --- build-time validator (in-memory mutations) ---
"%GODOT_BIN%" --headless --path . --log-file ".logs\mutation_build.log" --quit-after %CHECK_SCRIPT_MAX_FRAMES% --script res://tools/analysis_mutations.gd > ".logs\mutation_build_stdout.log" 2>&1
set "BUILD_CODE=%ERRORLEVEL%"
rem Marker is the source of truth (a wedged run can still exit 0); the runtime-error gate too.
findstr /C:"MUTATIONS: OK" ".logs\mutation_build_stdout.log" >nul 2>&1
if errorlevel 1 set "BUILD_CODE=1"
call "tools\check_log_errors.bat" ".logs\mutation_build.log" || set "BUILD_CODE=1"
rem ASCII patterns only: non-ASCII findstr patterns get mangled by the console codepage.
findstr /C:"MUTATIONS:" /C:"[OK]" /C:"[INVALID]" ".logs\mutation_build_stdout.log" 2>nul
if "%BUILD_CODE%"=="0" (echo   [OK] build-time mutations& set /a PASSED_STEPS+=1) else (echo   [FAIL] build-time mutations& set "FAILED_STEPS=!FAILED_STEPS! build")

echo.
echo --- acceptance validator (temp copies) ---
set "PS1_CODE=0"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\analysis_mutations.ps1 > ".logs\mutation_ps1_stdout.log" 2>&1 || set "PS1_CODE=1"
findstr /C:"PS1 MUTATIONS: OK" ".logs\mutation_ps1_stdout.log" >nul 2>&1
if errorlevel 1 set "PS1_CODE=1"
findstr /C:"PS1 MUTATIONS" /C:"[OK]" ".logs\mutation_ps1_stdout.log" 2>nul
if "%PS1_CODE%"=="0" (echo   [OK] acceptance mutations& set /a PASSED_STEPS+=1) else (echo   [FAIL] acceptance mutations& set "FAILED_STEPS=!FAILED_STEPS! ps1")

rem Third half: code constants. Slow (one full self-check per case), so it is opt-in.
rem Written as a goto label, not an if/else block: echoed text containing parentheses inside a
rem paren block makes cmd close the block early (that broke this script once).
if /I "%~1"=="code" goto :code_half
echo.
echo   skipped: code constants -- run "tools\run_mutation_sweep.bat code" to include them
goto :summary

:code_half
echo.
echo --- code constants (slow: one self-check per case) ---
set "CODE_CODE=0"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\analysis_mutations_code.ps1 > ".logs\mutation_code_all.log" 2>&1 || set "CODE_CODE=1"
findstr /C:"CODE MUTATIONS: OK" ".logs\mutation_code_all.log" >nul 2>&1
if errorlevel 1 set "CODE_CODE=1"
findstr /C:"CODE MUTATIONS" /C:"[OK]" /C:"[INVALID]" /C:"[FAIL]" ".logs\mutation_code_all.log" 2>nul
rem Two separate "if" lines on purpose: "if cond (a) || (b)" silently did nothing here once.
if "%CODE_CODE%"=="0" (echo   [OK] code constants& set /a PASSED_STEPS+=1)
if not "%CODE_CODE%"=="0" (echo   [FAIL] code constants& set "FAILED_STEPS=!FAILED_STEPS! code")

:summary
echo.
echo === summary ===
echo   passed steps: %PASSED_STEPS%
if defined FAILED_STEPS (
	echo   FAILED steps:%FAILED_STEPS%
	echo MUTATION SWEEP: FAILED
	exit /b 1
)
echo MUTATION SWEEP: PASSED
exit /b 0
