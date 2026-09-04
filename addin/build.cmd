@echo off
setlocal enabledelayedexpansion
rem ===================================================================
rem  Build GearWorks.dll  鈥? no Visual Studio required
rem  Locates SOLIDWORKS via the registry, copies the interop assemblies
rem  next to the output, and compiles with the .NET Framework csc.exe.
rem ===================================================================

set "CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Install .NET Framework 4.x.
    exit /b 1
)

rem ---- find SOLIDWORKS ----
set "SWDIR="
for %%V in (2026 2025 2024 2023 2022 2021 2020 2019 2018 2017 2016) do (
    if not defined SWDIR (
        for /f "tokens=3,*" %%A in ('reg query "HKLM\SOFTWARE\SolidWorks\SOLIDWORKS %%V\Setup" /v "SolidWorks Folder" 2^>nul ^| findstr /i "SolidWorks Folder"') do (
            set "SWDIR=%%B"
        )
    )
)
if not defined SWDIR (
    echo [ERROR] Could not locate SOLIDWORKS in the registry.
    echo         Set SWDIR manually, e.g.:
    echo             set "SWDIR=D:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\"
    echo         then run this script again.
    exit /b 1
)
set "REDIST=%SWDIR%api\redist"
if not exist "%REDIST%\SolidWorks.Interop.sldworks.dll" (
    echo [ERROR] Interop assemblies not found in:
    echo         %REDIST%
    exit /b 1
)
echo SOLIDWORKS : %SWDIR%

rem ---- stage output ----
set "OUT=%~dp0build"
if not exist "%OUT%" mkdir "%OUT%"
for %%F in (sldworks swconst swpublished) do (
    copy /y "%REDIST%\SolidWorks.Interop.%%F.dll" "%OUT%\" >nul
)

rem ---- compile ----
"%CSC%" /nologo /target:library /platform:x64 /langversion:5 ^
    /out:"%OUT%\GearWorks.dll" ^
    /r:"%REDIST%\SolidWorks.Interop.sldworks.dll" ^
    /r:"%REDIST%\SolidWorks.Interop.swconst.dll" ^
    /r:"%REDIST%\SolidWorks.Interop.swpublished.dll" ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
    "%~dp0src\GearMath.cs" "%~dp0src\GearBuilder.cs" ^
    "%~dp0src\GearPage.cs" "%~dp0src\GearAddin.cs"
if errorlevel 1 (
    echo [ERROR] Compilation failed.
    exit /b 1
)

copy /y "%~dp0install.bat"   "%OUT%\" >nul
copy /y "%~dp0uninstall.bat" "%OUT%\" >nul
echo.
echo Build OK:  %OUT%\GearWorks.dll
echo Next: run "%OUT%\install.bat" as administrator, then restart SOLIDWORKS.
echo.
endlocal