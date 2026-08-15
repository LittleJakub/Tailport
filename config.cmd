@echo off
REM config.cmd - load tailport.config values into TP_* env vars.
REM Usage: call config.cmd    (must be CALLED, not started)
REM Keeps every script portable: edit one file, all scripts follow.

set "TP_MAIN_PORT=8080"
for /f "tokens=1,* delims==" %%A in ('findstr /I /B "main_local_port=" "%~dp0tailport.config" 2^>nul') do set "TP_MAIN_PORT=%%B"

set "TP_PYTHONW=pythonw"
for /f "tokens=1,* delims==" %%A in ('findstr /I /B "pythonw=" "%~dp0tailport.config" 2^>nul') do set "TP_PYTHONW=%%B"

set "TP_DISTRO=Ubuntu"
for /f "tokens=1,* delims==" %%A in ('findstr /I /B "wsl_distro=" "%~dp0tailport.config" 2^>nul') do set "TP_DISTRO=%%B"

exit /b 0
