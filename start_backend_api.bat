@echo off
title [MIDDLE TIER] ICTU Assessment Web API & Lecturer Portal (Port 5000)
cd /d "%~dp0backend_api"
echo ========================================================
echo  KHOI DONG MIDDLE TIER & PORTAL GIANG VIEN (PORT 5000)
echo ========================================================

:: Kiem tra co RESET_DEMO=1 hoac tham so --reset de khoi phuc CSDL Vang sach 100%
if "%RESET_DEMO%"=="1" (
    echo [ICTU] Phat hien RESET_DEMO=1. Dang khoi phuc CSDL Vang (Gold DB)...
    call "%~dp0reset_demo_db.bat"
)
if "%1"=="--reset" (
    echo [ICTU] Phat hien tham so --reset. Dang khoi phuc CSDL Vang (Gold DB)...
    call "%~dp0reset_demo_db.bat"
)

:: Tu dong giai phong cong 5000 neu co tien trinh cu chiem dung
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    echo [ICTU] Giai phong cong 5000 - PID %%a
    taskkill /F /PID %%a >nul 2>&1
)

dotnet run
pause
