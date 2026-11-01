@echo off
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "[xml]$proj = Get-Content 'src\XNotch\XNotch.csproj'; $proj.Project.PropertyGroup.Version"`) do set "APP_VERSION=%%V"
if not defined APP_VERSION set "APP_VERSION=unknown"

echo ========================================
echo  X-Notch v%APP_VERSION% - Build Release
echo ========================================
echo.

cd /d "%~dp0"

echo Cleaning previous builds...
if exist "publish" rmdir /s /q "publish"

echo.
echo [1/2] Building self-contained single-file executable...
dotnet publish src\XNotch\XNotch.csproj ^
    -c Release ^
    -o publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD FAILED!
    pause
    exit /b 1
)

echo.
echo ========================================
echo  Build complete!
echo  Output: publish\X-Notch.exe
echo ========================================
for %%A in ("publish\X-Notch.exe") do echo  Size: %%~zA bytes
echo.

:: Build installer if Inno Setup is available
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if defined ISCC (
    echo [2/2] Building installer with Inno Setup...
    "%ISCC%" "/DMyAppVersion=%APP_VERSION%" installer\X-Notch.iss
    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo INSTALLER BUILD FAILED!
        pause
        exit /b 1
    )
    echo.
    echo ========================================
    echo  Installer created!
    echo  Output: publish\installer\X-Notch-%APP_VERSION%-Setup.exe
    echo ========================================
    for %%A in ("publish\installer\X-Notch-%APP_VERSION%-Setup.exe") do echo  Size: %%~zA bytes
) else (
    echo [2/2] Inno Setup not found - skipping installer.
    echo  Install from: https://jrsoftware.org/isinfo.php
    echo  Then re-run this script to build the installer.
)

echo.
echo Done! Release artifacts:
echo   - publish\X-Notch.exe          (standalone)
if defined ISCC echo   - publish\installer\X-Notch-%APP_VERSION%-Setup.exe (installer)
echo.
pause
