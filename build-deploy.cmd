@echo off
REM Tailport build + deploy: repo is the source of truth, this assembles the runtime folder.
REM Usage:  build-deploy.cmd [target-folder]
REM Default target: the local runtime folder the tray app launches from.
setlocal
set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=C:\Users\user\Hermes\work\tailnet-forward"

echo === Building Tailport (Release) ===
dotnet publish "%~dp0src\TailnetForward.csproj" -c Release -o "%TARGET%"
if errorlevel 1 goto :err

echo === Deploying assets + forwarder + launchers ===
xcopy /y /e /i "%~dp0assets" "%TARGET%\assets" >nul
copy /y "%~dp0forwarder\tailnet-forward.py" "%TARGET%" >nul
copy /y "%~dp0scripts\start.cmd" "%TARGET%" >nul
copy /y "%~dp0scripts\stop.cmd" "%TARGET%" >nul
copy /y "%~dp0scripts\check.cmd" "%TARGET%" >nul

echo === Done. Deployed to: %TARGET% ===
exit /b 0

:err
echo BUILD FAILED
exit /b 1
