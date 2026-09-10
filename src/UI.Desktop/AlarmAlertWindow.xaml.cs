using System;
using System.Windows;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class AlarmAlertWindow : Window
{
    private readonly string _dbPath;
    public long AlarmId { get; }
    public bool IsConfirmed { get; private set; }

    public AlarmAlertWindow(long alarmId, DateTime timestamp, string objectName, string district, 
                            string phone, DeviceType devType, string description, string dbPath)
    {
        InitializeComponent();
        AlarmId = alarmId;
        _dbPath = dbPath;

        TxtTimestamp.Text = timestamp.ToString("dd.MM.yyyy HH:mm:ss");
        TxtObjectName.Text = objectName;
        TxtDistrict.Text = string.IsNullOrWhiteSpace(district) ? "Основной участок" : district;
        TxtPhone.Text = phone;
        TxtDeviceType.Text = devType switch
        {
            DeviceType.Ccu825 => "[CCU-825]",
            DeviceType.OwenPlc => "[ОВЕН ПЛК]",
            _ => "[КСИТАЛ]"
        };
        TxtAlarmDescription.Text = description;
    }

    private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            var alarm = await db.Alarms.FindAsync(AlarmId);
            if (alarm != null)
            {
                alarm.IsAcknowledged = true;
                alarm.AcknowledgedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            IsConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка подтверждения тревоги: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}