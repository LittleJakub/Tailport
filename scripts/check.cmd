@echo off
REM ============================================================
REM  check.cmd - status of the forwarder + full chain to parthenon
REM ============================================================

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

echo === 1) Local listener (127.0.0.1:8080) ===
netstat -ano | findstr "127.0.0.1:8080" | findstr /I LISTENING
if errorlevel 1 echo [!!] Nothing listening on 127.0.0.1:8080 - run start.cmd
echo.

echo === 2) Chain test: forwarder - WSL SOCKS5 - parthenon llama /health ===
curl -s -m 8 http://127.0.0.1:8080/health
if errorlevel 1 echo [!!] No response - see tailnet-forward.log
echo.

echo === 3) Last 10 log lines ===
if exist "%~dp0tailnet-forward.log" powershell -NoProfile -Command "Get-Content '%~dp0tailnet-forward.log' -Tail 10"
if not exist "%~dp0tailnet-forward.log" echo (no log file yet)

echo.
pause
