@echo off
setlocal
echo =========================================================================
echo  ICTU ASSESSMENT SYSTEM - RESTORE GOLD DATABASE (DEMO RESET)
echo =========================================================================

set SCRIPT_DIR=%~dp0
set GOLD_DB=%SCRIPT_DIR%backend_api\ictu_exam_gold.db
set TARGET_BIN_DB=%SCRIPT_DIR%backend_api\bin\Debug\net10.0\ictu_exam.db
set TARGET_ROOT_DB=%SCRIPT_DIR%backend_api\ictu_exam.db

if not exist "%GOLD_DB%" (
    echo [ERROR] Gold database not found at: %GOLD_DB%
    exit /b 1
)

echo [*] Cleaning SQLite WAL journal files...
if exist "%SCRIPT_DIR%backend_api\bin\Debug\net10.0\ictu_exam.db-wal" del /f /q "%SCRIPT_DIR%backend_api\bin\Debug\net10.0\ictu_exam.db-wal" 2>nul
if exist "%SCRIPT_DIR%backend_api\bin\Debug\net10.0\ictu_exam.db-shm" del /f /q "%SCRIPT_DIR%backend_api\bin\Debug\net10.0\ictu_exam.db-shm" 2>nul
if exist "%SCRIPT_DIR%backend_api\ictu_exam.db-wal" del /f /q "%SCRIPT_DIR%backend_api\ictu_exam.db-wal" 2>nul
if exist "%SCRIPT_DIR%backend_api\ictu_exam.db-shm" del /f /q "%SCRIPT_DIR%backend_api\ictu_exam.db-shm" 2>nul

echo [*] Restoring pristine Gold Database for Demo...
if exist "%SCRIPT_DIR%backend_api\bin\Debug\net10.0" (
    copy /y "%GOLD_DB%" "%TARGET_BIN_DB%" >nul
    echo  + Restored: %TARGET_BIN_DB%
)
copy /y "%GOLD_DB%" "%TARGET_ROOT_DB%" >nul
echo  + Restored: %TARGET_ROOT_DB%

echo.
echo [SUCCESS] Database restored to pristine Gold state (CS301, 4 variants, ready for demo)!
echo.
endlocal
