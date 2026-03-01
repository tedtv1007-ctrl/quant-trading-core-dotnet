@echo off
chcp 65001 >nul
title QuantTrading - Run Tests
echo ══════════════════════════════════════════════════════════
echo   QuantTrading Core - Run Tests Only
echo ══════════════════════════════════════════════════════════
echo.

cd /d "%~dp0"

echo Building (Debug)...
dotnet build --nologo --verbosity quiet
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)
echo OK
echo.

echo ── Unit Tests (Core + Fugle) ────────────────────────────
dotnet test tests\QuantTrading.Core.Tests --no-build --verbosity normal --nologo
echo.

echo ── E2E Tests ────────────────────────────────────────────
dotnet test tests\QuantTrading.E2E.Tests --no-build --verbosity normal --nologo
echo.

echo ══════════════════════════════════════════════════════════
echo   Done.
echo ══════════════════════════════════════════════════════════
pause
