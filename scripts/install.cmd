@echo off
chcp 65001 >nul
setlocal
title Установка GoHome

set "SOURCE=%~dp0GoHome.exe"
set "TARGET=%LOCALAPPDATA%\Programs\GoHome"

if not exist "%SOURCE%" (
    echo Рядом с этим скриптом нет GoHome.exe.
    echo Распакуйте архив целиком и запустите install.cmd из распакованной папки.
    echo.
    pause
    exit /b 1
)

echo Останавливаю запущенный экземпляр, если он есть...
taskkill /f /im GoHome.exe >nul 2>&1

echo Копирую в %TARGET% ...
if not exist "%TARGET%" mkdir "%TARGET%" || goto :fail
copy /y "%SOURCE%" "%TARGET%\GoHome.exe" >nul || goto :fail

echo Регистрирую автозапуск в планировщике задач...
"%TARGET%\GoHome.exe" --install
if errorlevel 1 goto :fail

echo Запускаю...
start "" "%TARGET%\GoHome.exe"

echo.
echo Готово. Значок-кольцо появится в трее, время начнёт считаться
echo с ближайшей разблокировки экрана.
echo Журналы дней: %APPDATA%\GoHome\days
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
