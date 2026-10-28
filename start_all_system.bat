@echo off
title KHOI DONG TOAN BO HE THONG ICTU ASSESSMENT
echo ========================================================
echo  KHOI DONG TOAN BO HE THONG DANH GIA SINH VIEN & AI AGENT
echo  DE TAI NCKH - TRUONG DAI HOC CNTT & TRUYEN THONG (ICTU)
echo ========================================================
echo.
echo [1/3] Khoi dong AI Service (Port 8000)...
start "ICTU AI Agent" "%~dp0start_ai_service.bat"

timeout /t 3 /nobreak >nul

echo [2/3] Khoi dong Middle Tier Web API & Lecturer Portal (Port 5000)...
start "ICTU Middle Tier API" "%~dp0start_backend_api.bat"

timeout /t 4 /nobreak >nul

echo [3/3] Mo Giao Dien Dashboard Giang Vien tren trinh duyet...
start http://localhost:5000

echo.
echo ========================================================
echo  HE THONG DA SAN SANG!
echo  - Portal Giang Vien: http://localhost:5000
echo  - Cong thi Sinh Vien (Web): http://localhost:5000/student_web.html
echo  - Ung dung Desktop (.EXE): Chay file "start_student_app.bat"
echo ========================================================
pause

