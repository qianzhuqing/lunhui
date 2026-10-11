@echo off
rem lunhui self-check entry. Since 2026-10-11 there is a SINGLE pipeline: the GDScript
rem chain (tools\run_tests.gd + the tests\*.gd suites) was retired in the GDScript -> C#
rem migration, so this file now just forwards to the C# self-check. It stays the canonical
rem entry because the docs and the other .bat files call "tools\run_tests.bat".
rem
rem The whole pipeline is C# now: run_tests_cs.bat builds the assembly, rebuilds the tables
rem (tools\build_tables.tscn) and runs the runner scene, all under the .NET build.
rem
rem Keep this file ASCII-only: cmd.exe parses it in the local codepage, so non-ASCII text
rem here turns into unreadable commands on CP936/CP1252 hosts.
setlocal
call "%~dp0run_tests_cs.bat" %*
exit /b %ERRORLEVEL%
