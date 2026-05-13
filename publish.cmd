@echo off
setlocal enabledelayedexpansion

set "OUT=./publish"
set "PROJ=XOSC.csproj"

echo === Publishing to GitHub ===

REM Check if there are changes to commit
git status --porcelain >nul 2>&1
if !errorlevel! equ 0 (
    git add .
    git commit -m "Release build" 2>nul
    if !errorlevel! equ 0 (
        git push origin master 2>nul
        echo ✅ Pushed to master
    )
)

REM Create and push a release tag
if exist "%OUT%\XOSC.zip" (
    for /f "tokens=2-4 delims=/ " %%a in ('date /t') do (set mydate=%%c-%%a-%%b)
    for /f "tokens=1-2 delims=/:" %%a in ('time /t') do (set mytime=%%a%%b)
    
    set "TAG=release-!mydate!-!mytime!"
    
    git tag -a "!TAG!" -m "Automated release build - XOSC.zip"
    if !errorlevel! equ 0 (
        git push origin "!TAG!"
        echo ✅ Released with tag: !TAG!
    ) else (
        echo ❌ Tag creation failed
    )
) else (
    echo ❌ Error: %OUT%\XOSC.zip not found. Build the project first.
)

echo.
echo === Done ===
echo Zip location: %CD%\%OUT%\XOSC.zip
pause
endlocal
