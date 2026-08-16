@echo off
setlocal
cd /d "%~dp0"

echo === Syncing lunhui to git ===

rem Make sure git is available
where git >nul 2>nul
if errorlevel 1 (
    if exist "C:\Program Files\Git\cmd\git.exe" (
        set "PATH=C:\Program Files\Git\cmd;%PATH%"
    ) else (
        echo [ERROR] Git was not found. Please install Git for Windows first.
        exit /b 1
    )
)

rem Remove local test artifact if it exists
if exist ".gittest" rmdir /s /q ".gittest"

rem Initialize git repository with the main branch
git init -b main

rem Set a local identity if none is configured
git config user.name  >nul 2>nul
if errorlevel 1 git config user.name "lunhui"
git config user.email >nul 2>nul
if errorlevel 1 git config user.email "lunhui@example.com"

rem Stage and create the initial commit
git add .
git commit -m "chore: initial commit for lunhui"

rem Add remote and push if a URL was provided
if not "%~1"=="" (
    git remote remove origin >nul 2>nul
    git remote add origin %~1
    git push -u origin main
) else (
    echo.
    echo Local commit created on branch "main".
    echo To push to a remote repository, run:
    echo     git remote add origin ^<YOUR_REPOSITORY_URL^>
    echo     git push -u origin main
)

endlocal
