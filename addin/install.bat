@echo off
title GearWorks - Register SOLIDWORKS add-in
net session >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
set "REGASM=%windir%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if not exist "%REGASM%" (
    echo [ERROR] RegAsm.exe not found. Install .NET Framework 4.x.
    pause
    exit /b 1
)
echo.
echo Registering: %~dp0GearWorks.dll
echo.
"%REGASM%" /codebase "%~dp0GearWorks.dll"
echo.
if errorlevel 1 (
    echo ====== REGISTRATION FAILED ======
) else (
    echo ====== INSTALLED ======
    echo.
    echo Restart SOLIDWORKS, open a part, and look for the "Gear Tools" tab.
    echo If it is missing, enable it under Tools ^> Add-Ins.
    echo.
    echo NOTE: this folder path is stored in the registry. Do not move it.
)
echo.
pause