@echo off
chcp 65001 >nul
setlocal
title Удаление GoHome

set "TARGET=%LOCALAPPDATA%\Programs\GoHome"

echo Останавливаю приложение...
taskkill /f /im GoHome.exe >nul 2>&1

echo Снимаю задачу планировщика...
if exist "%TARGET%\GoHome.exe" (
    "%TARGET%\GoHome.exe" --uninstall
) else (
    schtasks /Delete /TN GoHome /F >nul 2>&1
)

echo Удаляю программу...
rd /s /q "%TARGET%" 2>nul

echo.
echo Готово. Накопленная статистика осталась на месте:
echo   %APPDATA%\GoHome
echo Если она больше не нужна, удалите эту папку вручную.
echo.
pause
exit /b 0
