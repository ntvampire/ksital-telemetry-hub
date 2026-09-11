using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class SettingsWindow : Window
{
    private const string GitHubRepo = "ntvampire/ksital-telemetry-hub";
    private readonly string _dbPath;
    
    public string SelectedPort { get; private set; }
    public string BackupPath { get; private set; }

    // Добавлен необязательный параметр backupPath, чтобы не сломать текущий вызов из MainWindow
    public SettingsWindow(string currentPort, string dbPath, string backupPath = "")
    {
        InitializeComponent();
        SelectedPort = currentPort;
        _dbPath = dbPath;
        
        BackupPath = string.IsNullOrWhiteSpace(backupPath) 
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups") 
            : backupPath;
            
        TxtBackupPath.Text = BackupPath;

        TxtDbInfo.Text = $"База данных: {Path.GetFullPath(_dbPath)} (Режим: WAL, 24/7)";
        
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        TxtCurrentVersion.Text = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.0";

        RefreshPortList(currentPort);
    }

    private void RefreshPortList(string preferPort)
    {
        CmbPorts.Items.Clear();
        var ports = SerialPort.GetPortNames().Distinct().OrderBy(p => p).ToArray();

        foreach (var port in ports)
        {
            CmbPorts.Items.Add(port);
        }

        if (CmbPorts.Items.Contains(preferPort))
        {
            CmbPorts.SelectedItem = preferPort;
        }
        else if (CmbPorts.Items.Count > 0)
        {
            CmbPorts.SelectedIndex = 0;
        }
        else
        {
            CmbPorts.Items.Add(preferPort);
            CmbPorts.SelectedItem = preferPort;
        }
    }

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        string cur = CmbPorts.SelectedItem?.ToString() ?? SelectedPort;
        RefreshPortList(cur);
    }

    private void BtnBrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для резервных копий",
            InitialDirectory = Directory.Exists(TxtBackupPath.Text) ? TxtBackupPath.Text : AppDomain.CurrentDomain.BaseDirectory
        };
        
        if (dialog.ShowDialog() == true)
        {
            TxtBackupPath.Text = dialog.FolderName;
            BackupPath = dialog.FolderName;
        }
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(BackupPath))
                Directory.CreateDirectory(BackupPath);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFile = Path.Combine(BackupPath, $"telemetry_backup_{timestamp}.db");
            
            // Копируем основной файл и WAL-журналы для целостности
            File.Copy(_dbPath, backupFile, true);
            if (File.Exists(_dbPath + "-wal")) File.Copy(_dbPath + "-wal", backupFile + "-wal", true);
            if (File.Exists(_dbPath + "-shm")) File.Copy(_dbPath + "-shm", backupFile + "-shm", true);

            MessageBox.Show($"Резервная копия успешно создана:\n{backupFile}", "Бэкап", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при создании бэкапа: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл резервной копии",
            Filter = "SQLite База данных (*.db)|*.db|Все файлы (*.*)|*.*",
            InitialDirectory = Directory.Exists(BackupPath) ? BackupPath : AppDomain.CurrentDomain.BaseDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            var res = MessageBox.Show(
                "Внимание! Текущая база данных будет заменена. Программа и системная служба сбора данных будут перезапущены.\n\nПродолжить?", 
                "Восстановление БД", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning);
            
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "KsitalHubRestore_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    
                    string batPath = Path.Combine(tempDir, "apply_restore.bat");
                    string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                    string sourceDb = dialog.FileName;
                    
                    // Скрипт требует остановки системной службы, чтобы снять блокировку с файла
                    string batContent = $@"@echo off
chcp 65001 >nul
echo Остановка интерфейса и службы сбора данных...
taskkill /f /im UI.Desktop.exe >nul 2>&1
net stop KsitalTelemetryWorker >nul 2>&1

echo Восстановление базы данных...
copy /y ""{sourceDb}"" ""{appDir}\telemetry.db"" >nul
if exist ""{sourceDb}-wal"" ( copy /y ""{sourceDb}-wal"" ""{appDir}\telemetry.db-wal"" >nul ) else ( del /f /q ""{appDir}\telemetry.db-wal"" >nul 2>&1 )
if exist ""{sourceDb}-shm"" ( copy /y ""{sourceDb}-shm"" ""{appDir}\telemetry.db-shm"" >nul ) else ( del /f /q ""{appDir}\telemetry.db-shm"" >nul 2>&1 )

echo Запуск службы сбора данных...
net start KsitalTelemetryWorker >nul 2>&1

echo Перезапуск интерфейса...
start """" ""{appDir}\UI.Desktop.exe""
exit
";
                    File.WriteAllText(batPath, batContent);
                    
                    var psi = new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WorkingDirectory = tempDir,
                        Verb = "runas" // Запрос прав администратора для управления службой
                    };
                    
                    Process.Start(psi);
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось запустить процесс восстановления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SelectedPort = CmbPorts.SelectedItem?.ToString() ?? "COM3";
        BackupPath = TxtBackupPath.Text;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.Text = "Связь с GitHub Releases...";
        TxtUpdateStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KsitalTelemetryHub-Updater");

            string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            var response = await client.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                TxtUpdateStatus.Text = "Релизы в репозитории пока не найдены.";
                return;
            }

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            string cleanTag = tagName.TrimStart('v', 'V');

            var currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            if (Version.TryParse(cleanTag, out var latestVer) && latestVer <= currentVer)
            {
                TxtUpdateStatus.Text = $"У вас установлена актуальная версия ({TxtCurrentVersion.Text}).";
                return;
            }

            string? downloadUrl = null;
            string fileName = string.Empty;

            if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        fileName = name;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? string.Empty;
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            fileName = name;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                TxtUpdateStatus.Text = $"Найдена версия {tagName}, но установочный файл в релизе не найден.";
                return;
            }

            TxtUpdateStatus.Text = $"Найдена новая версия {tagName}!";

            var res = MessageBox.Show(
                $"Доступна новая версия: {tagName}\n\nСкачать и установить сейчас?\n(База данных telemetry.db будет сохранена без изменений)",
                "Обновление системы",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                await RunUpdateCycleAsync(client, downloadUrl, fileName);
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = $"Ошибка проверки: {ex.Message}";
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private async Task RunUpdateCycleAsync(HttpClient client, string downloadUrl, string fileName)
    {
        try
        {
            ProgressDownload.Visibility = Visibility.Visible;
            ProgressDownload.IsIndeterminate = true;
            TxtUpdateStatus.Text = "Загрузка обновления...";

            string tempDir = Path.Combine(Path.GetTempPath(), "KsitalHubUpdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string downloadedFilePath = Path.Combine(tempDir, fileName);
            var fileBytes = await client.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(downloadedFilePath, fileBytes);

            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                TxtUpdateStatus.Text = "Запуск установщика...";
                var psi = new ProcessStartInfo { FileName = downloadedFilePath, UseShellExecute = true };
                Process.Start(psi);
                Application.Current.Shutdown();
                return;
            }

            TxtUpdateStatus.Text = "Распаковка пакета...";
            string extractedDir = Path.Combine(tempDir, "extracted");
            ZipFile.ExtractToDirectory(downloadedFilePath, extractedDir);

            string sourcePayloadDir = extractedDir;
            var subDirs = Directory.GetDirectories(extractedDir);
            var filesInRoot = Directory.GetFiles(extractedDir);
            if (filesInRoot.Length == 0 && subDirs.Length == 1)
            {
                sourcePayloadDir = subDirs[0];
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string updaterBat = Path.Combine(tempDir, "apply_update.bat");
            string batContent = $@"@echo off
chcp 65001 >nul
echo Ожидание завершения процессов...
timeout /t 2 /nobreak >nul
taskkill /f /im UI.Desktop.exe >nul 2>&1
net stop KsitalTelemetryWorker >nul 2>&1

echo Создание резервной копии базы данных...
if exist ""{appDir}\telemetry.db"" (
    copy /y ""{appDir}\telemetry.db"" ""{appDir}\telemetry.db.bak"" >nul
)

echo Обновление исполняемых файлов...
xcopy ""{sourcePayloadDir}\*.*"" ""{appDir}\"" /E /Y /H /R /exclude:exclude_db.txt >nul 2>&1

echo Запуск службы и интерфейса...
net start KsitalTelemetryWorker >nul 2>&1
start """" ""{appDir}\UI.Desktop.exe""
exit
";
            File.WriteAllText(Path.Combine(tempDir, "exclude_db.txt"), "telemetry.db\ntelemetry.db-wal\ntelemetry.db-shm\n");
            File.WriteAllText(updaterBat, batContent);

            TxtUpdateStatus.Text = "Применение обновления...";
            var psiZip = new ProcessStartInfo
            {
                FileName = updaterBat,
                UseShellExecute = true,
                CreateNoWindow = true,
                WorkingDirectory = tempDir,
                Verb = "runas"
            };

            Process.Start(psiZip);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            ProgressDownload.Visibility = Visibility.Collapsed;
            TxtUpdateStatus.Text = $"Сбой обновления: {ex.Message}";
            MessageBox.Show($"Не удалось выполнить обновление: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}