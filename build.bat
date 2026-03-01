@echo off
chcp 65001 >nul
title QuantTrading - Build & Test
echo ══════════════════════════════════════════════════════════
echo   QuantTrading Core - Build ^& Test
echo ══════════════════════════════════════════════════════════
echo.

cd /d "%~dp0"

echo [1/3] Restoring NuGet packages...
dotnet restore --verbosity quiet
if %ERRORLEVEL% neq 0 (
    echo [ERROR] NuGet restore failed.
    pause
    exit /b 1
)
echo       OK
echo.

echo [2/3] Building solution (Release)...
dotnet build --configuration Release --no-restore --nologo
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)
echo.

echo [3/3] Running all tests...
dotnet test --configuration Release --no-build --verbosity normal --nologo
if %ERRORLEVEL% neq 0 (
    echo [WARNING] Some tests failed.
    pause
    exit /b 1
)

echo.
echo ══════════════════════════════════════════════════════════
echo   All tests passed. Ready to run!
echo ══════════════════════════════════════════════════════════
pause
