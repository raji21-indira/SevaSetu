@echo off
title SevaSetu Backend Server
cd /d "%~dp0"
echo =======================================================
echo     Starting SevaSetu Backend & Static Server (Port 5000)
echo =======================================================
echo Web URL: http://localhost:5000/
echo API Base: http://localhost:5000/api/
echo.

if exist "backend\bin\server.exe" (
    backend\bin\server.exe 5000
) else (
    powershell -ExecutionPolicy Bypass -File backend\src\server.ps1
)
pause
