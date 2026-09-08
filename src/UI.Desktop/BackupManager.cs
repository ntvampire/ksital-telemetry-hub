using System.IO;
using System.Windows.Threading;

namespace KsitalTelemetryHub.UI.Desktop;

public static class BackupManager
{
    private static DispatcherTimer? _timer;
    public static string BackupFolder { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
    private static DateTime _lastBackupDate = DateTime.MinValue;

    public static void InitScheduler()
    {
        Directory.CreateDirectory(BackupFolder);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (s, e) =>
        {
            var now = DateTime.Now;
            // Срабатывание в 04:00 раз в сутки
            if (now.Hour == 4 && now.Minute == 0 && _lastBackupDate.Date != now.Date)
            {
                PerformBackup();
                _lastBackupDate = now.Date;
            }
        };
        _timer.Start();
    }

    public static string PerformBackup(string dbPath = "telemetry.db")
    {
        Directory.CreateDirectory(BackupFolder);
        string actualDbPath = File.Exists(dbPath) ? dbPath : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        if (!File.Exists(actualDbPath)) return "Файл базы данных не найден";

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string destFile = Path.Combine(BackupFolder, $"telemetry_backup_{timestamp}.db");

        File.Copy(actualDbPath, destFile, true);
        RotateBackups();
        return destFile;
    }

    public static void RotateBackups()
    {
        if (!Directory.Exists(BackupFolder)) return;

        var files = new DirectoryInfo(BackupFolder)
            .GetFiles("telemetry_backup_*.db")
            .OrderByDescending(f => f.CreationTime)
            .Skip(10) // оставляем только 10 последних
            .ToList();

        foreach (var file in files)
        {
            try { file.Delete(); } catch { }
        }
    }

    public static void RestoreBackup(string backupFilePath, string targetDbPath = "telemetry.db")
    {
        if (!File.Exists(backupFilePath)) throw new FileNotFoundException("Файл резервной копии не найден");

        string actualDbPath = File.Exists(targetDbPath) ? targetDbPath : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        File.Copy(backupFilePath, actualDbPath, true);
    }
}