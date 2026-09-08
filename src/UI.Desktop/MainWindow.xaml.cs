using System.ComponentModel;
using System.IO.Ports;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Modem.Engine;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly string _dbPath;
    private string _currentPort = "COM3";
    private long _lastMaxAlarmId = 0;
    private DateTime _lastSoundTime = DateTime.MinValue;

    public MainWindow()
    {
        // Инициализация темы и планировщика бэкапа
        ThemeManager.ApplyTheme(AppThemeMode.System);
        BackupManager.InitScheduler();

        InitializeComponent();

        string candidatePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        _dbPath = System.IO.File.Exists(candidatePath) ? candidatePath : "telemetry.db";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (s, e) => await RefreshDataAsync();
        _timer.Start();

        Loaded += async (s, e) =>
        {
            CheckHardwareStatus();
            await RefreshDataAsync();
        };
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_currentPort, _dbPath) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _currentPort = dlg.SelectedPort;
            CheckHardwareStatus();
        }
    }

	private void BtnManageObjects_Click(object sender, RoutedEventArgs e)
       {
           var win = new ManageObjectsWindow(_dbPath) { Owner = this };
           win.ShowDialog();
           _ = RefreshDataAsync();
       }

    private void CheckHardwareStatus()
    {
        bool comOk = false;
        bool modemOk = false;

        try
        {
            var ports = SerialPort.GetPortNames();
            if (ports.Contains(_currentPort))
            {
                comOk = true;
                using var client = new GsmModemClient(_currentPort, 115200);
                client.Connect();
                if (client.SendCommand("AT").Contains("OK")) modemOk = true;
            }
        }
        catch { }

        LedComPort.Fill = new SolidColorBrush(comOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
        TxtComStatus.Text = comOk ? $"COM: {_currentPort}" : $"COM: {_currentPort} (Нет)";

        LedModem.Fill = new SolidColorBrush(modemOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
        TxtModemStatus.Text = modemOk ? "Модем: Подключен" : "Модем: Нет";
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            if (!await db.Database.CanConnectAsync()) return;

            // 1. Объекты
            var objects = await db.Objects
                .Include(o => o.TelemetryRecords).ThenInclude(t => t.Temperatures)
                .ToListAsync();

            var viewModels = objects.Select(o =>
            {
                var last = o.TelemetryRecords.OrderByDescending(t => t.Timestamp).FirstOrDefault();
                var t1 = last?.Temperatures.FirstOrDefault(t => t.SensorCode == "T1")?.Value;
                var t2 = last?.Temperatures.FirstOrDefault(t => t.SensorCode == "T2")?.Value;
                var t3 = last?.Temperatures.FirstOrDefault(t => t.SensorCode == "T3")?.Value;
                bool isPowerOk = last?.MainPower == PowerState.Normal;

                return new ObjectViewModel
                {
                    Id = o.Id,
                    District = string.IsNullOrWhiteSpace(o.District) ? "Основной участок" : o.District,
                    Name = o.Name,
                    Phone = o.PhoneNumber,
                    TempT1 = t1.HasValue ? $"{t1.Value:F1} °C" : "--",
                    TempT2 = t2.HasValue ? $"{t2.Value:F1} °C" : "--",
                    TempT3 = t3.HasValue ? $"{t3.Value:F1} °C" : "--",
                    PowerStatus = isPowerOk ? "220V: Норма" : "220V: Авария!",
                    PowerColor = isPowerOk ? "#A6E3A1" : "#F38BA8",
                    BatteryStatus = last?.BatteryVoltage != null ? $"АКБ: {last.BatteryVoltage:F1}V" : "АКБ: --",
                    LastUpdate = last != null ? $"Обновлено: {last.Timestamp:HH:mm:ss}" : "Нет данных"
                };
            }).ToList();

            var view = new ListCollectionView(viewModels);
            view.GroupDescriptions.Add(new PropertyGroupDescription("District"));
            ListObjects.ItemsSource = view;

            // 2. Журнал тревог (ограничение ровно 100 записей)
            long? selectedAlarmId = (GridAlarms.SelectedItem as AlarmItemViewModel)?.Id;

            var alarms = await db.Alarms
                .Include(a => a.MonitoredObject)
                .OrderByDescending(a => a.Timestamp)
                .Take(100)
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

            if (selectedAlarmId.HasValue)
            {
                var row = alarms.FirstOrDefault(a => a.Id == selectedAlarmId.Value);
                if (row != null) GridAlarms.SelectedItem = row;
            }

            // 3. Синтез тревожного звука (промышленный зуммер 1200Гц -> 900Гц)
            if (ChkSoundEnabled.IsChecked == true && alarms.Count > 0)
            {
                long currentMaxId = alarms.Max(a => a.Id);
                bool hasUnacknowledged = alarms.Any(a => !a.IsAcknowledged);
                bool isNew = _lastMaxAlarmId > 0 && currentMaxId > _lastMaxAlarmId;
                bool reminder = hasUnacknowledged && (DateTime.UtcNow - _lastSoundTime).TotalSeconds >= 12;

                if (isNew || reminder)
                {
                    _lastSoundTime = DateTime.UtcNow;
                    Task.Run(() =>
                    {
                        try
                        {
                            Console.Beep(1200, 150);
                            Thread.Sleep(50);
                            Console.Beep(900, 220);
                        }
                        catch { }
                    });
                }
                _lastMaxAlarmId = currentMaxId;
            }
        }
        catch { }
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
    }

    private void BtnShowGraph_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is int objId)
        {
            using var db = new AppDbContext(_dbPath);
            var obj = db.Objects.Find(objId);
            new HistoryGraphWindow(objId, obj?.Name ?? "Объект", _dbPath) { Owner = this }.ShowDialog();
        }
    }
}

public class ObjectViewModel
{
    public int Id { get; set; }
    public string District { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TempT1 { get; set; } = string.Empty;
    public string TempT2 { get; set; } = string.Empty;
    public string TempT3 { get; set; } = string.Empty;
    public string PowerStatus { get; set; } = string.Empty;
    public string PowerColor { get; set; } = string.Empty;
    public string BatteryStatus { get; set; } = string.Empty;
    public string LastUpdate { get; set; } = string.Empty;
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