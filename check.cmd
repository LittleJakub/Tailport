@echo off
REM ============================================================
REM  check.cmd - status of the tailnet door + full chain test
REM ============================================================

call "%~dp0config.cmd"

echo === 0) VM keeper (holds the VM open) ===
set "KPID="
if exist "%~dp0keeper.pid" set /p KPID=<"%~dp0keeper.pid"
if not defined KPID goto NOKEEPER
tasklist /FI "PID eq %KPID%" | findstr "%KPID%" >nul 2>&1
if errorlevel 1 goto KEEPERDEAD
echo [OK] Keeper alive (PID %KPID%)
goto KEEPERDONE
:NOKEEPER
echo [!!] No keeper running - VM will die ~1 min after the last wsl command
goto KEEPERDONE
:KEEPERDEAD
echo [!!] Keeper PID %KPID% NOT running
:KEEPERDONE
echo.

echo === 1) Local listener (127.0.0.1:%TP_ANCHOR_PORT%) ===
netstat -ano | findstr "127.0.0.1:%TP_ANCHOR_PORT%" | findstr /I LISTENING
if errorlevel 1 echo [!!] Nothing listening on 127.0.0.1:%TP_ANCHOR_PORT% - run start.cmd
echo.

echo === 2) Chain test: forwarder - WSL SOCKS5 - anchor target (TCP) ===
powershell -NoProfile -Command "Test-NetConnection 127.0.0.1 -Port %TP_ANCHOR_PORT% -InformationLevel Quiet"
if errorlevel 1 echo [!!] No response - see tailport.log
echo.

echo === 3) Last 10 log lines ===
if exist "%~dp0tailport.log" powershell -NoProfile -Command "Get-Content '%~dp0tailport.log' -Tail 10"
if not exist "%~dp0tailport.log" echo (no log file yet)

echo.
pause
