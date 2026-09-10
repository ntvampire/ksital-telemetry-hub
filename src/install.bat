@echo off
chcp 65001 >nul
echo ========================================================
echo   Установка и настройка КСИТАЛ GSM Telemetry Hub
echo ========================================================
echo.

set "APP_DIR=%~dp0"
set "WORKER_EXE=%APP_DIR%WorkerService\Service.Worker.exe"

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ОШИБКА] Запустите этот файл от имени Администратора!
    echo (Правый клик по install.bat -> Запуск от имени администратора)
    pause
    exit /b 1
)

echo [1/2] Настройка службы фонового сбора данных...
sc stop KsitalTelemetryWorker >nul 2>&1
sc delete KsitalTelemetryWorker >nul 2>&1
sc create KsitalTelemetryWorker binPath= "\"%WORKER_EXE%\"" start= auto DisplayName= "КСИТАЛ GSM - Сервис сбора данных"
sc failure KsitalTelemetryWorker reset= 60 actions= restart/5000/restart/5000/restart/5000
sc start KsitalTelemetryWorker

echo.
echo [2/2] Создание ярлыка на Рабочем столе...
set "SHORTCUT=%USERPROFILE%\Desktop\КСИТАЛ Telemetry Hub.lnk"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut($env:SHORTCUT); $s.TargetPath = '%APP_DIR%UI.Desktop.exe'; $s.WorkingDirectory = '%APP_DIR%'; $s.Save()"

echo.
echo ========================================================
echo   Установка завершена успешно!
echo   - Служба сбора запущена в фоне (24/7)
echo   - Ярлык создан на рабочем столе
echo ========================================================
pause