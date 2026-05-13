@echo off
setlocal EnableDelayedExpansion

set "OUT=./publish"
set "PROJ=XOSC.csproj"

:: Default to Release, capitalize first letter
set "CONFIG=%1"
if "%CONFIG%"=="" set "CONFIG=Release"

:: Capitalize first letter (e.g. debug -> Debug)
set "FIRST=%CONFIG:~0,1%"
set "REST=%CONFIG:~1%"
for %%A in ("a=A" "b=B" "c=C" "d=D" "e=E" "f=F" "g=G" "h=H" "i=I" "j=J" "k=K" "l=L" "m=M" "n=N" "o=O" "p=P" "q=Q" "r=R" "s=S" "t=T" "u=U" "v=V" "w=W" "x=X" "y=Y" "z=Z") do (
    set "FIRST=!FIRST:%%~A!"
)
set "CONFIG=%FIRST%%REST%"

:: Get git short hash
for /f %%i in ('git rev-parse --short HEAD') do set "GIT_HASH=%%i"

set "NEW_VER=%GIT_HASH%"
if /i "%CONFIG%"=="Debug" set "NEW_VER=%GIT_HASH%-debug"

echo === Building %CONFIG% v%NEW_VER% ===

:: Update version in Program.cs
powershell -NoProfile -Command ^
    "(Get-Content Program.cs) -replace 'public const string AppVersion = \".*\";', 'public const string AppVersion = \"%NEW_VER%\";' | Set-Content Program.cs"

:: Clean output directory
if exist "%OUT%" rd /s /q "%OUT%"

:: Build Linux and Windows versions
dotnet publish "%PROJ%" -c %CONFIG% -r linux-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o "%OUT%/linux-x64"

dotnet publish "%PROJ%" -c %CONFIG% -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -o "%OUT%/win-x64"

echo === Creating XOSC.zip ===
if exist "%OUT%\XOSC.zip" del "%OUT%\XOSC.zip"

powershell -NoProfile -Command ^
    "Compress-Archive -Path '%OUT%/linux-x64', '%OUT%/win-x64' -DestinationPath '%OUT%/XOSC.zip' -Force"

if /i "%CONFIG%"=="Release" (
    echo === Publishing to GitHub ===
    git add .
    git commit -m "Release %NEW_VER%"
    git push origin master
    
    where gh >nul 2>&1
    if !errorlevel! equ 0 (
        gh release create "%NEW_VER%" "%OUT%/XOSC.zip" --title "%NEW_VER%" --notes "Automated %CONFIG% build"
    )
)

echo === Done ===
endlocal