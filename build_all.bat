@echo off
cd /d "%~dp0"
call build_hs80.bat
if errorlevel 1 exit /b 1
if exist HS80Control.exe copy /Y HS80Control.exe headsetcontrol.exe >nul
call build_hs80_tray.bat
echo.
echo Done. Binaries in this folder:
echo   HS80Control.exe / headsetcontrol.exe
echo   HS80Tray.exe
