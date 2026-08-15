@echo off
REM ============================================================
REM  build-installer.cmd - compile the Tailport setup wizard
REM  Needs Inno Setup 6 (free). Default location below; override
REM  with:  set INNO_DIR=C:\path\to\Inno Setup 6
REM  Output: installer\TailportSetup-<version>.exe
REM ============================================================
setlocal

if not defined INNO_DIR set "INNO_DIR=C:\Users\jgrze\Tools\Inno Setup 6"
if not exist "%INNO_DIR%\ISCC.exe" (
  echo [!!] Inno Setup 6 not found at %INNO_DIR%
  echo      Set INNO_DIR to the folder containing ISCC.exe.
  exit /b 1
)

REM read the version from the project (single source of truth)
REM tokens=3 delims=<>: the line is indented ("    <Version>x.y.z</Version>"),
REM so the indent is token 1, the tag name token 2, the value token 3.
REM NOTE: delims== does NOT work in for /f (the '=' delimiter is eaten).
set "TPVER="
for /f "tokens=3 delims=<>" %%V in ('findstr "<Version>" "%~dp0src\Tailport.csproj"') do set "TPVER=%%V"
if not defined TPVER set "TPVER=1.8.1"

echo Building TailportSetup-%TPVER%.exe ...
"%INNO_DIR%\ISCC.exe" /DAppVersion=%TPVER% "%~dp0Tailport.iss"
echo.

if exist "%~dp0installer\TailportSetup-%TPVER%.exe" (
  echo [OK] installer\TailportSetup-%TPVER%.exe
) else (
  echo [!!] Build failed - see ISCC output above.
  exit /b 1
)
