@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
    echo.
    echo ========================================
    echo BUILD FAILED - terminal will stay open
    echo ========================================
    echo Exit code: %BUILD_EXIT%
    echo.
    pause
    exit /b %BUILD_EXIT%
)

exit /b 0
