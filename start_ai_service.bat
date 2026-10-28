@echo off
title [MODULE 3] ICTU AI Agent Service (FastAPI)
cd /d "%~dp0ai_service"
echo ========================================================
echo  KHOI DONG MODULE 3: AI AGENT SERVICE (PORT 8000)
echo ========================================================

:: Tu dong giai phong cong 8000 neu co tien trinh cu chiem dung
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":8000" ^| findstr "LISTENING"') do (
    echo [ICTU] Giai phong cong 8000 - PID %%a
    taskkill /F /PID %%a >nul 2>&1
)

set PYTHONPATH=%~dp0ai_service
python -m uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload
pause
