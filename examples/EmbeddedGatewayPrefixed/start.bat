@echo off
:: Windows wrapper — requires PowerShell 7 (pwsh).
:: Download from: https://aka.ms/pscore6
where pwsh >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: pwsh (PowerShell 7^) not found. Install from https://aka.ms/pscore6
    pause
    exit /b 1
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
