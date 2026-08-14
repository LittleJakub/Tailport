@echo off
REM ============================================================
REM  start.cmd - start the tailnet forwarder (hidden, no window)
REM
REM  Chain:  app -> 127.0.0.1:8080 -> forwarder -> SOCKS5 :1055
REM          -> WSL2 tailscaled -> parthenon:8080 (llama.cpp)
REM
REM  Idempotent: safe to run again. WSL boots automatically if
REM  it was stopped.
REM  NOTE: no parenthesized blocks in this file - cmd's parser
REM  chokes on colons/parens inside blocks in interactive mode.
REM ============================================================

REM 1) Make sure WSL2 is up and tailscaled is running inside it
echo Booting WSL2 (first time can take up to 30 s)...
wsl -d Ubuntu -u root -- systemctl start tailscaled
if errorlevel 1 echo [!!] WSL failed to start - see the error above

REM 2) Kick the tailnet session into sync (no-op if already connected)
wsl -d Ubuntu -u root -- timeout 20 tailscale up --accept-dns=true >nul 2>&1

REM 3) Strip any inherited PYTHONPATH so the right Python is used
set "PYTHONPATH="

REM 4) Launch the forwarder hidden (pythonw = no console window)
start "" "C:\Users\user\AppData\Local\Programs\Python\Python311\pythonw.exe" "%~dp0tailnet-forward.py" --local 8080 --host 100.101.102.103 --port 8080

REM 5) Keep the VM alive - attach a hidden keeper session. stop.cmd
REM    kills it so the VM can wind down (~1 min).
set "KPID="
if exist "%~dp0keeper.pid" set /p KPID=<"%~dp0keeper.pid"
if defined KPID taskkill /PID %KPID% /F >nul 2>&1
if defined KPID del "%~dp0keeper.pid" >nul 2>&1
powershell -NoProfile -Command "$p = Start-Process -WindowStyle Hidden wsl -ArgumentList '-d','Ubuntu','-u','root','--','sleep','infinity' -PassThru; Set-Content -Path '%~dp0keeper.pid' -Value $p.Id -Encoding ascii"

REM 6) Wait for the VM to finish booting, then report
ping -n 13 127.0.0.1 >nul
netstat -ano | findstr "127.0.0.1:8080" | findstr /I LISTENING >nul 2>&1
if errorlevel 1 goto NOTLISTENING
echo [OK] Forwarder is listening on 127.0.0.1:8080
echo      Full chain test (allow ~20s for tailnet sync): run check.cmd
goto DONE
:NOTLISTENING
echo [!!] Not listening yet. See tailnet-forward.log for details.
:DONE
echo.
pause
