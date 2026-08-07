@echo off
chcp 65001 >nul
setlocal
title Обновление GoHome

set "SCRIPT=%~dp0update.ps1"

if not exist "%SCRIPT%" (
    echo Рядом с этим скриптом нет update.ps1.
    echo Распакуйте архив целиком и запустите update.cmd из распакованной папки.
    echo.
    pause
    exit /b 1
)

rem -ExecutionPolicy Bypass только для этого запуска: политику машины не меняем.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "CODE=%ERRORLEVEL%"

echo.
pause
exit /b %CODE%
