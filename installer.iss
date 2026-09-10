#define MyAppName "КСИТАЛ Telemetry Hub"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "NTVampire"
#define MyAppExeName "UI.Desktop.exe"

[Setup]
AppId={{D38F2B7E-7F2A-4B2E-8F12-892D98D1C765}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=ksital-hub-setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "Создать ярлыки в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Копируем все файлы скомпилированного UI и скриптов (исключая рабочие базы)
Source: "dist\ksital-hub\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "telemetry.db*,*.pdb"

[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{autoprograms}\{#MyAppName}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Настройка и запуск системной службы Windows Service после завершения копирования
Filename: "{sys}\sc.exe"; Parameters: "stop KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create KsitalTelemetryWorker binPath= """"{app}\WorkerService\Service.Worker.exe"""" start= auto DisplayName= ""КСИТАЛ GSM - Сервис сбора данных"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "failure KsitalTelemetryWorker reset= 60 actions= restart/5000/restart/5000/restart/5000"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start KsitalTelemetryWorker"; Flags: runhidden

; Предложение запустить интерфейс сразу после установки
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Остановка и удаление службы при удалении программы
Filename: "{sys}\sc.exe"; Parameters: "stop KsitalTelemetryWorker"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete KsitalTelemetryWorker"; Flags: runhidden