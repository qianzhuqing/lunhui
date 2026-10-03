@echo off
rem Run one command step and pass its exit code back to the caller.
rem usage: check_command.bat "<label>" "<command line>"
rem Runs as a child process on purpose: cmd's "call :label" inside a bigger script is
rem unreliable here (the first call could not find the label), child bats are not.
setlocal
cd /d "%~dp0.."
echo.
echo --- %~1 ---
%~2
exit /b %ERRORLEVEL%
