@echo off
rem Fail if an engine log contains a GDScript runtime error.
rem usage: check_log_errors.bat <logfile>
rem
rem Why this gate exists: a GDScript runtime error (out-of-bounds index, float(null), a call on a
rem null instance) ABORTS the function that hit it -- everything after that line in that function,
rem and the remaining checks of its callers, silently do not run, while the verdict marker still
rem shows up (when the marker is printed by a later function) and the exit code is still 0.
rem "SCRIPT ERROR" in the log is the one place that abort shows itself.
rem Reverse-verified 2026-10-03 (framework notes decision 147): the stale-index case printed two
rem SCRIPT ERROR lines and the self-check still reported OK.
rem NOTE: an abort inside the entry point skips quit() entirely and the process then runs forever.
rem That is why the --script steps also carry --quit-after (decision 148).
setlocal
cd /d "%~dp0.."
if "%~1"=="" (
	echo [error] usage: check_log_errors.bat ^<logfile^>
	exit /b 2
)
if not exist "%~1" (
	echo [error] engine log not found: %~1 -- cannot prove there was no runtime error
	exit /b 1
)
rem 2026-10-03 added two signatures that do NOT carry the "SCRIPT ERROR" prefix. Both were PROBED,
rem not guessed: a scratch script triggered each failure and the engine log was read. They are raised
rem by the C++ binder, so the GDScript statement itself looks like it "ran through" -- exactly the kind
rem of line that gets dismissed as noise:
rem   * "String formatting error"    -- % formatting met an unknown format character. Real case: a test
rem     message wrote "15%" instead of "15%%"; every run logged this ERROR and nobody noticed it.
rem   * "into a TypedArray of type"  -- writing a wrong-typed value into a typed array.
rem The list stays evidence-based: when a new signature shows up, add it here. Do NOT ban all "ERROR:"
rem lines -- push_error is used on purpose in several places (e.g. DamageResolver refusing dmg_reflect).
findstr /C:"SCRIPT ERROR" /C:"Attempt to call function" /C:"Invalid access to property" /C:"String formatting error" /C:"into a TypedArray of type" "%~1" >nul 2>&1
if errorlevel 1 exit /b 0
echo   [FAIL] %~1 contains a GDScript runtime error -- the check that hit it stopped early:
findstr /C:"SCRIPT ERROR" /C:"Attempt to call function" /C:"Invalid access to property" /C:"String formatting error" /C:"into a TypedArray of type" "%~1" 2>nul
exit /b 1
