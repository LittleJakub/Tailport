@echo off
REM config.cmd - load tailport.config values into TP_* env vars.
REM Usage: call config.cmd    (must be CALLED, not started)
REM Keeps every script portable: edit one file, all scripts follow.

REM TP_ANCHOR_PORT = local port of the FIRST forward.N line - the status
REM anchor the tray probes (the forward with the smallest local port).
set "TP_ANCHOR_PORT="
for /f "tokens=1,* delims==" %%A in ('findstr /I /L /B "forward." "%~dp0tailport.config" 2^>nul') do if not defined TP_ANCHOR_PORT set "TP_ANCHOR_PORT=%%B"
if not defined TP_ANCHOR_PORT set "TP_ANCHOR_PORT=8080"
for /f "tokens=1 delims=:" %%C in ("%TP_ANCHOR_PORT%") do set "TP_ANCHOR_PORT=%%C"

set "TP_PYTHONW=pythonw"
for /f "tokens=1,* delims==" %%A in ('findstr /I /B "pythonw=" "%~dp0tailport.config" 2^>nul') do set "TP_PYTHONW=%%B"

set "TP_DISTRO=Ubuntu"
for /f "tokens=1,* delims==" %%A in ('findstr /I /B "wsl_distro=" "%~dp0tailport.config" 2^>nul') do set "TP_DISTRO=%%B"

exit /b 0
