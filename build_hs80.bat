@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ============================================
echo  HS80Control CLI - build (no Visual Studio)
echo ============================================
echo.

set "OUT=HS80Control.exe"
set "SRC=src\HS80Control.cs"

if not exist "%SRC%" (
  echo ERROR: %SRC% not found.
  pause
  exit /b 1
)

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if defined CSC (
  echo Using: %CSC%
  "%CSC%" /nologo /optimize+ /platform:anycpu /out:"%OUT%" "%SRC%"
  if errorlevel 1 goto :fail
  goto :done
)

echo csc.exe not found. Trying PowerShell...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_hs80.ps1"
if errorlevel 1 goto :fail
goto :done

:fail
echo BUILD FAILED - install .NET Framework 4.8 Developer Pack
pause
exit /b 1

:done
if exist "%OUT%" (
  echo SUCCESS: %OUT%
  echo Rename/copy to headsetcontrol.exe for GUI / tray tools.
) else (
  echo Build reported success but %OUT% missing.
  exit /b 1
)
pause
