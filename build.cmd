@echo off
REM Tailport build: publishes the tray app into the repo root (the runtime folder).
setlocal
echo === Building Tailport (Release) ===
dotnet publish "%~dp0src\Tailport.csproj" -c Release -o "%~dp0"
if errorlevel 1 goto :err
echo === Done. Tailport.exe is ready in the repo root. ===
exit /b 0
:err
echo BUILD FAILED
exit /b 1
