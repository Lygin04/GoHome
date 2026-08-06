@echo off
chcp 65001 >nul
setlocal
title Удаление WorkClock

set "TARGET=%LOCALAPPDATA%\Programs\WorkClock"

echo Останавливаю приложение...
taskkill /f /im WorkClock.exe >nul 2>&1

echo Снимаю задачу планировщика...
if exist "%TARGET%\WorkClock.exe" (
    "%TARGET%\WorkClock.exe" --uninstall
) else (
    schtasks /Delete /TN WorkClock /F >nul 2>&1
)

echo Удаляю программу...
rd /s /q "%TARGET%" 2>nul

echo.
echo Готово. Накопленная статистика осталась на месте:
echo   %APPDATA%\WorkClock
echo Если она больше не нужна, удалите эту папку вручную.
echo.
pause
exit /b 0
