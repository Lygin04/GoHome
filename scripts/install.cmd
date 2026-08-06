@echo off
chcp 65001 >nul
setlocal
title Установка WorkClock

set "SOURCE=%~dp0WorkClock.exe"
set "TARGET=%LOCALAPPDATA%\Programs\WorkClock"

if not exist "%SOURCE%" (
    echo Рядом с этим скриптом нет WorkClock.exe.
    echo Распакуйте архив целиком и запустите install.cmd из распакованной папки.
    echo.
    pause
    exit /b 1
)

echo Останавливаю запущенный экземпляр, если он есть...
taskkill /f /im WorkClock.exe >nul 2>&1

echo Копирую в %TARGET% ...
if not exist "%TARGET%" mkdir "%TARGET%" || goto :fail
copy /y "%SOURCE%" "%TARGET%\WorkClock.exe" >nul || goto :fail

echo Регистрирую автозапуск в планировщике задач...
"%TARGET%\WorkClock.exe" --install
if errorlevel 1 goto :fail

echo Запускаю...
start "" "%TARGET%\WorkClock.exe"

echo.
echo Готово. Значок-кольцо появится в трее, время начнёт считаться
echo с ближайшей разблокировки экрана.
echo Журналы дней: %APPDATA%\WorkClock\days
echo.
pause
exit /b 0

:fail
echo.
echo Установка не удалась.
echo Проверьте, что файл не заблокирован системой: свойства файла -^> «Разблокировать».
echo.
pause
exit /b 1
