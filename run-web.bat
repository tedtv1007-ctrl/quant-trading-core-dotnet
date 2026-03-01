@echo off
chcp 65001 >nul
title QuantTrading - Web Dashboard
echo ══════════════════════════════════════════════════════════
echo   QuantTrading Web Dashboard (Development)
echo ══════════════════════════════════════════════════════════
echo.

cd /d "%~dp0"

:: ── Check appsettings.json exists ───────────────────────────
if not exist "src\QuantTrading.Web\appsettings.json" (
    echo [ERROR] appsettings.json not found!
    echo         Please copy appsettings.template.json and fill in your API Token:
    echo.
    echo         copy src\QuantTrading.Web\appsettings.template.json ^
    echo              src\QuantTrading.Web\appsettings.json
    echo.
    pause
    exit /b 1
)

:: ── Build ───────────────────────────────────────────────────
echo [1/2] Building...
dotnet build --configuration Debug --nologo --verbosity quiet
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)
echo       OK
echo.

:: ── Run ─────────────────────────────────────────────────────
echo [2/2] Starting Web Dashboard...
echo.
echo   URL:  http://localhost:5148
echo   URL:  https://localhost:7217
echo.
echo   Press Ctrl+C to stop.
echo ──────────────────────────────────────────────────────────
echo.

dotnet run --project src\QuantTrading.Web --no-build --launch-profile https
