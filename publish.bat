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

:: === Robust Version Replacement ===
echo Updating AppVersion in Program.cs...

powershell -NoProfile -Command ^
    "$content = Get-Content 'Program.cs' -Raw; " ^
    "$content = $content -replace 'public const string AppVersion\s*=\s*\".*?\";', 'public const string AppVersion = \"%NEW_VER%\";'; " ^
    "Set-Content 'Program.cs' $content" 2>nul || (
        echo Warning: PowerShell replace failed, trying simple method...
        goto :simple_replace
    )
goto :replace_done

:simple_replace
:: Fallback pure batch (less reliable)
(
    for /f "usebackq delims=" %%a in ("Program.cs") do (
        set "line=%%a"
        set "line=!line:public const string AppVersion =.*;=public const string AppVersion = "%NEW_VER%";!"
        echo !line!
    )
) > "Program.cs.tmp" && move /y "Program.cs.tmp" "Program.cs" >nul

:replace_done

:: Clean and Build
if exist "%OUT%" rd /s /q "%OUT%" 2>nul

echo Building Linux target...
dotnet publish "%PROJ%" -c %CONFIG% -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%/linux-x64"

echo Building Windows target...
dotnet publish "%PROJ%" -c %CONFIG% -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%/win-x64"

echo === Creating XOSC.zip ===
if exist "%OUT%\XOSC.zip" del "%OUT%\XOSC.zip"

powershell -NoProfile -Command "Compress-Archive -Path '%OUT%/linux-x64','%OUT%/win-x64' -DestinationPath '%OUT%/XOSC.zip' -Force" 2>nul || (
    tar -a -cf "%OUT%/XOSC.zip" -C "%OUT%" linux-x64 win-x64 2>nul || echo WARNING: Could not create zip
)

:: GitHub Release (Release config only)
if /i "%CONFIG%"=="Release" (
    echo === Publishing to GitHub ===
    git add . 2>nul
    git commit -m "Release %NEW_VER%" 2>nul
    git push origin master 2>nul
    
    where gh >nul 2>&1 && gh release create "%NEW_VER%" "%OUT%/XOSC.zip" --title "%NEW_VER%" --notes "Automated %CONFIG% build" 2>nul
)

echo.
echo === Done ===
echo Version updated to: %NEW_VER%
echo Output: %CD%\%OUT%
endlocal