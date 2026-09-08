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
using System.Media;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class MainWindow : Window
{
    private long _lastMaxAlarmId = 0;
	private DateTime _lastSoundTime = DateTime.MinValue;
	private readonly DispatcherTimer _timer;
    private readonly string _dbPath;
    private string _currentPort = "COM3";

    public MainWindow()
    {
        InitializeComponent();

        string candidatePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        _dbPath = System.IO.File.Exists(candidatePath) ? candidatePath : "telemetry.db";

        RefreshPortList();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (s, e) => await RefreshDataAsync();
        _timer.Start();

        Loaded += async (s, e) =>
        {
            CheckHardwareStatus();
            await RefreshDataAsync();
        };
    }

    private void RefreshPortList()
    {
        var ports = SerialPort.GetPortNames();
        CmbPorts.ItemsSource = ports;
        if (ports.Length > 0 && string.IsNullOrEmpty(CmbPorts.Text))
        {
            CmbPorts.SelectedItem = ports.Contains(_currentPort) ? _currentPort : ports[0];
        }
    }

    private void CmbPorts_DropDownOpened(object sender, EventArgs e) => RefreshPortList();

    private void BtnChangePort_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPorts.SelectedItem != null)
        {
            _currentPort = CmbPorts.SelectedItem.ToString()!;
            CheckHardwareStatus();
            MessageBox.Show($"Выбран порт: {_currentPort}", "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
                // Тестовый опрос модема короткой AT-командой
                using var client = new GsmModemClient(_currentPort, 115200);
                client.Connect();
                string ping = client.SendCommand("AT");
                if (ping.Contains("OK"))
                {
                    modemOk = true;
                }
            }
        }
        catch
        {
            // Ошибки подключения оставляют индикаторы красными
        }

        // Обновление светодиода COM-порта
        LedComPort.Fill = new SolidColorBrush(comOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
        TxtComStatus.Text = comOk ? $"COM-порт: {_currentPort} (Готов)" : $"COM-порт: {_currentPort} (Нет)";

        // Обновление светодиода GSM-модема
        LedModem.Fill = new SolidColorBrush(modemOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
        TxtModemStatus.Text = modemOk ? "Модем: Подключен" : "Модем: Нет ответа";
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            if (!await db.Database.CanConnectAsync()) return;

            // 1. Загрузка объектов и группировка по District
            var objects = await db.Objects
                .Include(o => o.TelemetryRecords)
                    .ThenInclude(t => t.Temperatures)
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
                    District = string.IsNullOrWhiteSpace(o.District) ? "Без участка" : o.District,
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

            // 2. Журнал тревог с СОХРАНЕНИЕМ выделенной строки
            long? selectedAlarmId = (GridAlarms.SelectedItem as AlarmItemViewModel)?.Id;

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

         // Звуковое оповещение диспетчера
         bool soundAllowed = ChkSoundEnabled.IsChecked == true;
         if (soundAllowed && alarms.Count > 0)
         {
             long currentMaxId = alarms.Max(a => a.Id);
             bool hasUnacknowledged = alarms.Any(a => !a.IsAcknowledged);

             // 1. Пришла абсолютно новая тревога (Id больше предыдущего максимального)
             bool isBrandNewAlarm = _lastMaxAlarmId > 0 && currentMaxId > _lastMaxAlarmId;

             // 2. Либо периодическое напоминание раз в 12 секунд о висящих неквитированных тревогах
             bool reminderTick = hasUnacknowledged && (DateTime.UtcNow - _lastSoundTime).TotalSeconds >= 12;

             if (isBrandNewAlarm || reminderTick)
             {
                 SystemSounds.Exclamation.Play();
                 _lastSoundTime = DateTime.UtcNow;
             }

             _lastMaxAlarmId = currentMaxId;
         }
            // Восстановление курсора на той же строке
            if (selectedAlarmId.HasValue)
            {
                var rowToSelect = alarms.FirstOrDefault(a => a.Id == selectedAlarmId.Value);
                if (rowToSelect != null)
                {
                    GridAlarms.SelectedItem = rowToSelect;
                }
            }
        }
        catch
        {
            // Ошибки временного чтения БД игнорируем до следующего тика
        }
    }

    private void BtnManageObjects_Click(object sender, RoutedEventArgs e)
    {
        var win = new ManageObjectsWindow(_dbPath) { Owner = this };
        win.ShowDialog();
        _ = RefreshDataAsync();
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
            MessageBox.Show("Выберите тревогу из списка.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
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