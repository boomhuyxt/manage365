@echo off
cd /d "%~dp0"
echo Retail365 BLE Scanner - keep this window open while testing.
where dotnet >nul 2>&1
if %errorlevel% equ 0 (
  dotnet "%~dp0BleScannerAgent.dll"
) else (
  "%~dp0BleScannerAgent.exe"
)
echo Scanner stopped. Review the message above.
pause
