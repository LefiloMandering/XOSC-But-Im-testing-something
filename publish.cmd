@echo off
setlocal

set "OUT=./publish"
set "PROJ=XOSC.csproj"

echo === Publishing to GitHub Releases ===
git add .
git commit -m "Release build" 2>nul
git push origin master 2>nul

where gh >nul 2>&1
if !errorlevel! equ 0 (
    gh release create "latest" "%OUT%/XOSC.zip" --title "Latest Build" --notes "Automated build" --prerelease
    echo ✅ Released to GitHub!
) else (
    echo gh not found. Install GitHub CLI to auto-upload releases.
)

echo.
echo === Done ===
echo Zip location: %CD%\%OUT%\XOSC.zip
pause
endlocal