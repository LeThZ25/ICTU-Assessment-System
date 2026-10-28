@echo off
title [MIDDLE TIER] ICTU Assessment Web API & Lecturer Portal (Port 5000)
cd /d "%~dp0backend_api"
echo ========================================================
echo  KHOI DONG MIDDLE TIER & PORTAL GIANG VIEN (PORT 5000)
echo ========================================================

:: Tu dong giai phong cong 5000 neu co tien trinh cu chiem dung
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    echo [ICTU] Giai phong cong 5000 - PID %%a
    taskkill /F /PID %%a >nul 2>&1
)

dotnet run
pause
