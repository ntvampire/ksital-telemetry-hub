using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        ThemeManager.ApplyAutoTheme();
        BackupManager.InitScheduler();

        InitializeComponent();

        try
        {
            var iconUri = new Uri("pack://application:,,,/app_logo.png", UriKind.Absolute);
            this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { }

        string candidatePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\telemetry.db"));
        _dbPath = System.IO.File.Exists(candidatePath) ? candidatePath : "telemetry.db";

        AppDbContext.EnsureDatabaseUpdated(_dbPath);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (s, e) =>
        {
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        };
        _timer.Start();

        Loaded += async (s, e) =>
        {
            await CheckHardwareStatusAsync();
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        };
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private async void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_currentPort, _dbPath) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _currentPort = dlg.SelectedPort;
            await CheckHardwareStatusAsync();
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        }
    }

    private async void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is ObjectViewModel vm)
        {
            var dlg = new ObjectDetailsWindow(vm.Id, _dbPath) { Owner = this };
            dlg.ShowDialog();
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        }
    }

    private async void MenuEditObject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is int objId)
        {
            var dlg = new ObjectDetailsWindow(objId, _dbPath) { Owner = this };
            dlg.ShowDialog();
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        }
    }

    private async void MenuAddObject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ObjectDetailsWindow(0, _dbPath) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            await RefreshDataAsync();
            await RefreshCommandsAsync();
        }
    }

    private async void MenuDeleteObject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is int objId)
        {
            using var db = new AppDbContext(_dbPath);
            var obj = db.Objects.Find(objId);
            if (obj != null)
            {
                var res = MessageBox.Show($"Удалить объект «{obj.Name}» ({obj.PhoneNumber}) и всю его телеметрию?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Yes)
                {
                    db.Objects.Remove(obj);
                    await db.SaveChangesAsync();
                    await RefreshDataAsync();
                    await RefreshCommandsAsync();
                }
            }
        }
    }

    private async Task CheckHardwareStatusAsync()
    {
        bool comOk = false;
        bool modemOk = false;
        string port = _currentPort;

        await Task.Run(() =>
        {
            try
            {
                var ports = SerialPort.GetPortNames();
                if (ports.Contains(port))
                {
                    comOk = true;
                    using var client = new GsmModemClient(port, 115200);
                    client.Connect();
                    if (client.SendCommand("AT").Contains("OK")) modemOk = true;
                }
            }
            catch { }
        });

        Dispatcher.Invoke(() =>
        {
            LedComPort.Fill = new SolidColorBrush(comOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
            TxtComStatus.Text = comOk ? $"COM: {port}" : $"COM: {port} (Нет)";

            LedModem.Fill = new SolidColorBrush(modemOk ? Color.FromRgb(166, 227, 161) : Color.FromRgb(243, 139, 168));
            TxtModemStatus.Text = modemOk ? "Модем: ОК" : "Модем: Нет";
        });
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            if (!await db.Database.CanConnectAsync()) return;

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

                string devName = o.DeviceType switch
                {
                    DeviceType.Ccu825 => "[CCU-825]",
                    DeviceType.OwenPlc => "[ОВЕН ПЛК]",
                    _ => "[КСИТАЛ]"
                };

                return new ObjectViewModel
                {
                    Id = o.Id,
                    District = string.IsNullOrWhiteSpace(o.District) ? "Основной участок" : o.District,
                    Name = o.Name,
                    Phone = o.PhoneNumber,
                    DeviceTypeName = devName,
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
                    StatusText = a.IsAcknowledged ? "Подтверждена" : "АКТИВНА ТРЕВОГА"
                })
                .ToListAsync();

            GridAlarms.ItemsSource = alarms;

            if (selectedAlarmId.HasValue)
            {
                var row = alarms.FirstOrDefault(a => a.Id == selectedAlarmId.Value);
                if (row != null) GridAlarms.SelectedItem = row;
            }

            if (ChkSoundEnabled.IsChecked == true && alarms.Count > 0)
            {
                long currentMaxId = alarms.Max(a => a.Id);
                bool hasUnacknowledged = alarms.Any(a => !a.IsAcknowledged);
                bool isNew = _lastMaxAlarmId > 0 && currentMaxId > _lastMaxAlarmId;
                bool reminder = hasUnacknowledged && (DateTime.UtcNow - _lastSoundTime).TotalSeconds >= 12;

                if (isNew || reminder)
                {
                    _lastSoundTime = DateTime.UtcNow;
                    _ = Task.Run(() =>
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

    private async Task RefreshCommandsAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            if (!await db.Database.CanConnectAsync()) return;

            var commands = await db.OutgoingCommands
                .OrderByDescending(c => c.CreatedAt)
                .Take(100)
                .ToListAsync();

            var objNames = await db.Objects.ToDictionaryAsync(o => o.Id, o => o.Name);

            var viewItems = commands.Select(c => new OutgoingCommandViewModel
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                ObjectName = objNames.GetValueOrDefault(c.MonitoredObjectId, "—"),
                PhoneNumber = c.PhoneNumber,
                Description = c.Description,
                RawPayload = c.RawPayload,
                StatusText = c.Status switch
                {
                    CommandStatus.Pending => "⏳ В очереди",
                    CommandStatus.Sent => "✅ Отправлено",
                    CommandStatus.Failed => "❌ Ошибка",
                    _ => c.Status.ToString()
                },
                SentAtText = c.SentAt.HasValue ? $"{c.SentAt.Value:dd.MM.yyyy HH:mm:ss}" : "—",
                ErrorMessage = c.ErrorMessage ?? string.Empty
            }).ToList();

            GridCommands.ItemsSource = viewItems;

            int pendingCount = commands.Count(c => c.Status == CommandStatus.Pending);
            int sentCount = commands.Count(c => c.Status == CommandStatus.Sent);
            int failedCount = commands.Count(c => c.Status == CommandStatus.Failed);

            TxtCommandsSummary.Text = $"Всего: {commands.Count} | Ожидают: {pendingCount} | Отправлено: {sentCount} | Ошибок: {failedCount}";
        }
        catch { }
    }

    private async void BtnRefreshCommands_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCommandsAsync();
    }

    private async void BtnRetryFailedCommands_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            var failedList = await db.OutgoingCommands.Where(c => c.Status == CommandStatus.Failed).ToListAsync();
            if (failedList.Count == 0)
            {
                MessageBox.Show("Нет команд со статусом ошибки.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var cmd in failedList)
            {
                cmd.Status = CommandStatus.Pending;
                cmd.ErrorMessage = null;
            }

            await db.SaveChangesAsync();
            await RefreshCommandsAsync();
            MessageBox.Show($"Повторно поставлено в очередь команд: {failedList.Count}.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Сбой", MessageBoxButton.OK, MessageBoxImage.Error);
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
    public string DeviceTypeName { get; set; } = string.Empty;
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

public class OutgoingCommandViewModel
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string SentAtText { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}