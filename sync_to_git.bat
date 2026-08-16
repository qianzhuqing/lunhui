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

rem Use the default remote URL unless another one is provided
set "REMOTE_URL=%~1"
if "%REMOTE_URL%"=="" set "REMOTE_URL=https://github.com/qianzhuqing/lunhui.git"

rem Remove local test artifact if it exists
if exist ".gittest" rmdir /s /q ".gittest"

rem Initialize git repository with the main branch
git init -b main

rem Set a local identity if none is configured
git config user.name  >nul 2>nul
if errorlevel 1 git config user.name "lunhui"
git config user.email >nul 2>nul
if errorlevel 1 git config user.email "lunhui@example.com"

rem Stage files and create the initial commit if there is anything to commit
git add .
git diff --cached --quiet
if errorlevel 1 git commit -m "chore: initial commit for lunhui"

rem Point origin at the remote repository
git remote get-url origin >nul 2>nul
if errorlevel 1 (
    git remote add origin %REMOTE_URL%
) else (
    git remote set-url origin %REMOTE_URL%
)

rem Push the main branch to GitHub
git push -u origin main

echo.
echo Done. lunhui has been pushed to %REMOTE_URL%

endlocal
