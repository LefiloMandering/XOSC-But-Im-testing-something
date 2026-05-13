@echo off
setlocal enabledelayedexpansion

set "OUT=./publish"

echo === Publishing to GitHub ===

REM Commit changes
git add .
git commit -m "Release build" 2>nul

REM Create a version tag (you can customize this)
for /f "tokens=2-4 delims=/ " %%a in ('date /t') do (set mydate=%%c-%%a-%%b)
for /f "tokens=1-2 delims=/:" %%a in ('time /t') do (set mytime=%%a-%%b)

set "TAG=release-!mydate!-!mytime!"

REM Push commits and tag
git push origin master
git tag "!TAG!"
git push origin "!TAG!"

echo ✅ Pushed to GitHub with tag: !TAG!
echo Check GitHub Actions for automated build and release.

pause
endlocal
