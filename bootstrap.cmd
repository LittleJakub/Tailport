@echo off
REM bootstrap.cmd - one-command WSL2 setup for Tailport (run once on a new machine).
REM Checks WSL2 + distro, then runs bootstrap-wsl.sh inside the distro as root.
REM Optional arg: distro name (default Ubuntu):  bootstrap.cmd MyDistro
setlocal

echo === Tailport WSL2 bootstrap ===
echo.

wsl --status >nul 2>&1
if errorlevel 1 goto NOWSL

set "DISTRO=Ubuntu"
if not "%~1"=="" set "DISTRO=%~1"
wsl -d %DISTRO% -u root -- echo ok >nul 2>&1
if errorlevel 1 goto NODISTRO

REM Convert %~dp0 (C:\dir\) to a WSL path (/mnt/c/dir/) using wslpath -
REM wsl.exe does not translate paths inside quoted arguments, and manual
REM conversion gets drive case wrong. wslpath is authoritative.
REM (The trailing backslash is stripped first: it would escape the quote in bash.)
set "P0=%~dp0"
if defined P0 set "P0=%P0:~0,-1%"
set "P="
for /f "delims=" %%X in ('wsl -d %DISTRO% -u root -- wslpath "%P0%"') do set "P=%%X"
if not defined P goto NOWSL

wsl -d %DISTRO% -u root -- bash "%P%/bootstrap-wsl.sh"
echo.
pause
exit /b 0

:NOWSL
echo [!!] WSL is not installed or not enabled.
echo      Enable it first (admin PowerShell):  wsl --install
echo      then reboot, install a distro (e.g. Ubuntu from the Store), and re-run this.
pause
exit /b 1

:NODISTRO
echo [!!] Distro "%DISTRO%" not found. Install it (wsl --install -d Ubuntu)
echo      or pass the distro name:  bootstrap.cmd MyDistro
pause
exit /b 1
