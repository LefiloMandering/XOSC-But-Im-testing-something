@echo off
setlocal enabledelayedexpansion

set "OUT=./publish"
set "PROJ=XOSC.csproj"

echo === Publishing to GitHub Releases ===

REM Check if there are changes to commit
git status --porcelain >nul 2>&1
if !errorlevel! equ 0 (
    git add .
    git commit -m "Release build" 2>nul
    if !errorlevel! equ 0 (
        git push origin master 2>nul
    )
)

REM Check if gh CLI is installed
where gh >nul 2>&1
if !errorlevel! equ 0 (
    REM Check if zip file exists before uploading
    if exist "%OUT%\XOSC.zip" (
        gh release create "latest" "%OUT%\XOSC.zip" --title "Latest Build" --notes "Automated build" --prerelease
        echo ✅ Released to GitHub!
    ) else (
        echo ❌ Error: %OUT%\XOSC.zip not found. Build the project first.
    )
) else (
    echo ❌ gh not found. Install GitHub CLI to auto-upload releases.
)

echo.
echo === Done ===
echo Zip location: %CD%\%OUT%\XOSC.zip
pause
endlocal
