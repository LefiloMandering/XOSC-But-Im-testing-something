@echo off
setlocal EnableDelayedExpansion

set "OUT=./publish"
set "PROJ=XOSC.csproj"

:: Config
set "CONFIG=%1"
if "%CONFIG%"=="" set "CONFIG=Release"

:: Capitalize first letter
set "FIRST=%CONFIG:~0,1%"
set "REST=%CONFIG:~1%"
for %%A in (a=A b=B c=C d=D e=E f=F g=G h=H i=I j=J k=K l=L m=M n=N o=O p=P q=Q r=R s=S t=T u=U v=V w=W x=X y=Y z=Z) do (
    set "FIRST=!FIRST:%%A=%%~A!"
)
set "CONFIG=%FIRST%%REST%"

:: Git hash
set "GIT_HASH=unknown"
for /f %%i in ('git rev-parse --short HEAD 2^>nul') do set "GIT_HASH=%%i"

set "NEW_VER=%GIT_HASH%"
if /i "%CONFIG%"=="Debug" set "NEW_VER=%GIT_HASH%-debug"

echo === Building %CONFIG% v%NEW_VER% ===

:: === Fix AppVersion (Pure Batch) ===
echo Updating AppVersion...
if exist "Program.cs" (
    > "Program.cs.tmp" (
        for /f "usebackq delims=" %%a in ("Program.cs") do (
            set "line=%%a"
            set "line=!line:public const string AppVersion =.*;=public const string AppVersion = "%NEW_VER%";!"
            echo !line!
        )
    )
    move /y "Program.cs.tmp" "Program.cs" >nul
) else (
    echo ERROR: Program.cs not found!
    pause
    exit /b 1
)

:: Clean old build
if exist "%OUT%" rd /s /q "%OUT%" 2>nul

:: Build
echo Building Linux target...
dotnet publish "%PROJ%" -c %CONFIG% -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%/linux-x64"

echo Building Windows target...
dotnet publish "%PROJ%" -c %CONFIG% -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%/win-x64"

echo === Creating XOSC.zip ===
if exist "%OUT%\XOSC.zip" del "%OUT%\XOSC.zip"

:: Try to create zip using tar (Windows 10/11 built-in)
tar -a -cf "%OUT%\XOSC.zip" -C "%OUT%" linux-x64 win-x64 2>nul

if not exist "%OUT%\XOSC.zip" (
    echo WARNING: Could not create zip automatically.
    echo You can zip the "%OUT%" folder manually.
)

:: GitHub Release (only on Release)
if /i "%CONFIG%"=="Release" (
    echo === Publishing to GitHub ===
    git add . 2>nul
    git commit -m "Release %NEW_VER%" 2>nul
    git push origin master 2>nul
    
    where gh >nul 2>&1
    if !errorlevel! equ 0 (
        gh release create "%NEW_VER%" "%OUT%/XOSC.zip" --title "%NEW_VER%" --notes "Automated %CONFIG% build" 2>nul || echo GitHub release skipped
    ) else (
        echo gh not found - skipping release
    )
)

echo.
echo === Done ===
echo Version updated to: %NEW_VER%
echo Output: %CD%\%OUT%
endlocal
pause