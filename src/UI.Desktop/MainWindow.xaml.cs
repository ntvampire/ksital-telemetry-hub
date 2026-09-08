using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly string _dbPath;

    public MainWindow()
    {
        InitializeComponent();

        // База данных лежит в корне репозитория (поднимаемся на 4 уровня от bin/Debug/...)
        string candidatePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        _dbPath = System.IO.File.Exists(candidatePath) ? candidatePath : "telemetry.db";

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += async (s, e) => await RefreshDataAsync();
        _timer.Start();

        Loaded += async (s, e) => await RefreshDataAsync();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            if (!await db.Database.CanConnectAsync())
            {
                TxtStatus.Text = "Ожидание базы telemetry.db...";
                return;
            }

            // 1. Загрузка объектов и их последних данных
            var objects = await db.Objects
                .Include(o => o.TelemetryRecords)
                    .ThenInclude(t => t.Temperatures)
                .ToListAsync();

            var viewModels = objects.Select(o =>
            {
                var lastTelemetry = o.TelemetryRecords.OrderByDescending(t => t.Timestamp).FirstOrDefault();
                var t1 = lastTelemetry?.Temperatures.FirstOrDefault(t => t.SensorCode == "T1")?.Value;
                var t2 = lastTelemetry?.Temperatures.FirstOrDefault(t => t.SensorCode == "T2")?.Value;
                var t3 = lastTelemetry?.Temperatures.FirstOrDefault(t => t.SensorCode == "T3")?.Value;

                bool isPowerOk = lastTelemetry?.MainPower == PowerState.Normal;

                return new
                {
                    Name = o.Name,
                    Phone = o.PhoneNumber,
                    TempT1 = t1.HasValue ? $"{t1.Value:F1} °C" : "--",
                    TempT2 = t2.HasValue ? $"{t2.Value:F1} °C" : "--",
                    TempT3 = t3.HasValue ? $"{t3.Value:F1} °C" : "--",
                    PowerStatus = isPowerOk ? "220V: Норма" : "220V: Авария!",
                    PowerColor = isPowerOk ? "#A6E3A1" : "#F38BA8",
                    BatteryStatus = lastTelemetry?.BatteryVoltage != null ? $"АКБ: {lastTelemetry.BatteryVoltage:F1}V" : "АКБ: --",
                    LastUpdate = lastTelemetry != null ? $"Обновлено: {lastTelemetry.Timestamp:HH:mm:ss}" : "Нет данных"
                };
            }).ToList();

            ListObjects.ItemsSource = viewModels;

            // 2. Загрузка журнала последних 30 тревог
            var alarms = await db.Alarms
                .Include(a => a.MonitoredObject)
                .OrderByDescending(a => a.Timestamp)
                .Take(30)
                .Select(a => new AlarmItemViewModel
                {
                    Id = a.Id,
                    Timestamp = a.Timestamp,
                    ObjectName = a.MonitoredObject != null ? a.MonitoredObject.Name : "Неизвестно",
                    Description = a.Description,
                    IsAcknowledged = a.IsAcknowledged,
                    StatusText = a.IsAcknowledged ? "Квитирована" : "АКТИВНА ТРЕВОГА"
                })
                .ToListAsync();

            GridAlarms.ItemsSource = alarms;
            TxtStatus.Text = $"Обновлено: {DateTime.Now:HH:mm:ss} | Объектов: {objects.Count}";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Ошибка чтения БД: {ex.Message}";
        }
    }

    private async void BtnAcknowledge_Click(object sender, RoutedEventArgs e)
    {
        if (GridAlarms.SelectedItem is AlarmItemViewModel selected)
        {
            using var db = new AppDbContext(_dbPath);
            var alarm = await db.Alarms.FindAsync(selected.Id);
            if (alarm != null)
            {
                alarm.IsAcknowledged = true;
                alarm.AcknowledgedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await RefreshDataAsync();
            }
        }
        else
        {
            MessageBox.Show("Выберите тревогу из таблицы для квитирования.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

public class AlarmItemViewModel
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public string StatusText { get; set; } = string.Empty;
}