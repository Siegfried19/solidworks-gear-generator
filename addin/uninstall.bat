@echo off
title GearWorks - Unregister
net session >nul 2>&1
if errorlevel 1 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
"%windir%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /unregister "%~dp0GearWorks.dll"
echo.
echo Unregistered. You can now delete this folder.
pause