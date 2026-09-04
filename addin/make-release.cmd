@echo off
setlocal enabledelayedexpansion
rem ===================================================================
rem  Package a ready-to-install release ZIP.
rem  Usage:  make-release.cmd [output_dir] [version]
rem          default: %~dp0release   v1.0.0
rem  The ZIP holds the compiled add-in plus the SOLIDWORKS interop
rem  assemblies, so an end user just unzips and runs install.bat.
rem ===================================================================

if "%~1"=="" (set "RELDIR=%~dp0release") else (set "RELDIR=%~1")
if "%~2"=="" (set "VER=v1.0.0") else (set "VER=%~2")

set "STAGE=%RELDIR%\GearWorks-%VER%"
set "ZIP=%RELDIR%\GearWorks-%VER%-solidworks.zip"

echo Building into staging folder...
call "%~dp0build.cmd" "%STAGE%"
if errorlevel 1 (
    echo [ERROR] Build failed, release aborted.
    exit /b 1
)

> "%STAGE%\READ-ME-FIRST.txt" (
  echo GearWorks - Involute Gear Generator for SOLIDWORKS  %VER%
  echo.
  echo INSTALL
  echo   1. Put this folder somewhere permanent. It must not be moved afterwards.
  echo   2. Right-click install.bat, Run as administrator.
  echo   3. Restart SOLIDWORKS. Open a part. Look for the "Gear Tools" tab.
  echo      If missing: Tools ^> Add-Ins, tick "Gear Tools" in both columns.
  echo.
  echo UNINSTALL
  echo   Run uninstall.bat as administrator, then delete this folder.
  echo.
  echo NOTE
  echo   The install path is written into the registry. Moving or renaming this
  echo   folder breaks the add-in. To relocate: uninstall, move, install again.
  echo   Log file: %%LOCALAPPDATA%%\GearWorks\addin.log
  echo   Interface is currently Chinese only; documentation is bilingual.
  echo.
  echo LICENSE
  echo   MIT for GearWorks.dll and the source.
  echo   SolidWorks.Interop.*.dll are Dassault Systemes redistributables taken
  echo   from the SOLIDWORKS api\redist folder.
)

powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo [ERROR] Packaging failed.
    exit /b 1
)

echo.
echo Release package:  %ZIP%
echo Upload that file as a GitHub Release asset.
echo.
endlocal