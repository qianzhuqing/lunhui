@echo off
rem Build the C# assembly before an engine step runs.
rem usage: call "tools\ensure_csharp_build.bat" || exit /b 1
rem
rem Why every engine step needs this: as soon as ONE game-path script is C#, Godot resolves
rem res://...cs while loading data/generated/*.tres, and it can only do that when
rem .godot\mono\temp\bin\Debug\lunhui.dll exists. Without the build the table load fails and every
rem check goes red for a reason that looks nothing like the real cause.
rem
rem It builds ONCE per cmd session (the LUNHUI_CSHARP_BUILT marker survives across `call`, so
rem run_all_checks.bat does not rebuild 13 times for its 13 scene self-checks).
rem Keep this file ASCII-only + CRLF: cmd.exe parses it in the local codepage.
if defined LUNHUI_CSHARP_BUILT exit /b 0
setlocal
cd /d "%~dp0.."

where dotnet >nul 2>&1
if errorlevel 1 (
	echo [error] the .NET SDK is not on PATH -- the C# half of this project cannot be built
	echo         Install the .NET 8 SDK or add it to PATH.
	exit /b 1
)

echo [build] compiling the C# assembly ...
dotnet build lunhui.csproj --nologo -v quiet
if errorlevel 1 (
	echo [error] dotnet build failed -- fix the C# errors above, then re-run
	exit /b 1
)

endlocal & set "LUNHUI_CSHARP_BUILT=1"
exit /b 0
