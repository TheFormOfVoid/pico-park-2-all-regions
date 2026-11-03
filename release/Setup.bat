@echo off
rem All Regions for PICO PARK 2: install, turn mods off and on, or uninstall.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0AllRegions-Setup.ps1" %*
echo.
pause
