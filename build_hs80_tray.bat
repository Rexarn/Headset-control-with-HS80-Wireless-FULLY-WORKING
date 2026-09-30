@echo off
setlocal
cd /d "%~dp0"

echo Building HS80Tray.exe (system tray, no console)...

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not defined CSC (
  echo csc.exe not found. Install .NET Framework 4.x Developer Pack.
  pause
  exit /b 1
)

"%CSC%" /nologo /optimize+ /target:winexe /platform:anycpu /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:HS80Tray.exe src\HS80Tray.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo SUCCESS: HS80Tray.exe
echo Place headsetcontrol.exe (or HS80Control.exe) next to HS80Tray.exe
echo.
pause
