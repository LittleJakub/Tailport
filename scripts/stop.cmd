@echo off
REM ============================================================
REM  stop.cmd - stop the tailnet forwarder
REM  Reversible: run start.cmd to bring it back.
REM ============================================================

set "PID="
if exist "%~dp0tailnet-forward.pid" set /p PID=<"%~dp0tailnet-forward.pid"
if defined PID taskkill /PID %PID% /F >nul 2>&1
if defined PID del "%~dp0tailnet-forward.pid" >nul 2>&1
if defined PID echo [OK] Forwarder stopped (PID %PID%)
if not defined PID echo [!!] No PID file found - forwarder was not running?

REM Kill the VM keeper so WSL winds down (~1 min)
set "KPID="
if exist "%~dp0keeper.pid" set /p KPID=<"%~dp0keeper.pid"
if defined KPID taskkill /PID %KPID% /F >nul 2>&1
if defined KPID del "%~dp0keeper.pid" >nul 2>&1
if defined KPID echo [OK] Keeper stopped - VM will shut down within ~1 min

netstat -ano | findstr "127.0.0.1:8080" | findstr /I LISTENING >nul 2>&1
if errorlevel 1 echo [OK] Port 8080 is free again.
if not errorlevel 1 echo [!!] Port 8080 still occupied by something.

echo.
pause
