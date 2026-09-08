using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class ObjectDetailsWindow : Window
{
    private readonly string _dbPath;
    private readonly int _objectId;
    private MonitoredObject? _currentObject;

    public ObjectDetailsWindow(int objectId, string dbPath)
    {
        InitializeComponent();
        _objectId = objectId;
        _dbPath = dbPath;

        CmbDeviceType.ItemsSource = new[]
        {
            new { Type = DeviceType.Ksital, Title = "КСИТАЛ GSM" },
            new { Type = DeviceType.Ccu825, Title = "RADS CCU-825" },
            new { Type = DeviceType.OwenPlc, Title = "ОВЕН ПЛК (ПМ01)" }
        };
        CmbDeviceType.SelectedValuePath = "Type";
        CmbDeviceType.DisplayMemberPath = "Title";

        LoadData();
    }

    private void LoadData()
    {
        using var db = new AppDbContext(_dbPath);

        if (_objectId > 0)
        {
            _currentObject = db.Objects.Find(_objectId);
            if (_currentObject != null)
            {
                TxtHeaderTitle.Text = $"Карточка объекта: {_currentObject.Name}";
                TxtName.Text = _currentObject.Name;
                TxtDistrict.Text = _currentObject.District;
                TxtPhone.Text = _currentObject.PhoneNumber;
                TxtPassword.Text = _currentObject.DevicePassword;
                TxtDescription.Text = _currentObject.Description;
                CmbDeviceType.SelectedValue = _currentObject.DeviceType;
            }
        }
        else
        {
            TxtHeaderTitle.Text = "Добавление нового объекта";
            TxtName.Text = "Новая котельная";
            TxtDistrict.Text = "Основной участок";
            TxtPhone.Text = "+7";
            TxtPassword.Text = "00000";
            CmbDeviceType.SelectedValue = DeviceType.Ksital;
        }

        UpdateTemplates();
        RefreshCommandsHistory();
    }

    private void UpdateTemplates()
    {
        if (CmbDeviceType.SelectedValue is DeviceType devType)
        {
            var templates = DeviceCommandBuilder.GetTemplates(devType);
            CmbTemplates.ItemsSource = templates;
            CmbTemplates.DisplayMemberPath = "Title";
            if (templates.Count > 0) CmbTemplates.SelectedIndex = 0;
        }
    }

    private void RefreshCommandsHistory()
    {
        if (_objectId <= 0) return;

        using var db = new AppDbContext(_dbPath);
        var list = db.OutgoingCommands
            .Where(c => c.MonitoredObjectId == _objectId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new
            {
                c.CreatedAt,
                c.RawPayload,
                c.Description,
                StatusText = c.Status == CommandStatus.Sent ? "Отправлено" :
                             c.Status == CommandStatus.Failed ? "Ошибка" : "В очереди"
            })
            .ToList();

        GridCommands.ItemsSource = list;
    }

    private void CmbDeviceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTemplates();
    }

    private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTemplates.SelectedItem is CommandTemplate tmpl)
        {
            TxtPayload.Text = DeviceCommandBuilder.BuildPayload(tmpl.Pattern, TxtPassword.Text);
        }
    }

    private void TxtPassword_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CmbTemplates.SelectedItem is CommandTemplate tmpl)
        {
            TxtPayload.Text = DeviceCommandBuilder.BuildPayload(tmpl.Pattern, TxtPassword.Text);
        }
    }

    private void BtnSendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPhone.Text) || string.IsNullOrWhiteSpace(TxtPayload.Text))
        {
            MessageBox.Show("Укажите телефон объекта и текст команды!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_objectId <= 0)
        {
            MessageBox.Show("Сначала сохраните объект в базу данных перед отправкой команд!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using var db = new AppDbContext(_dbPath);
        var desc = (CmbTemplates.SelectedItem as CommandTemplate)?.Title ?? "Пользовательская команда";

        var cmd = new OutgoingCommand
        {
            MonitoredObjectId = _objectId,
            PhoneNumber = TxtPhone.Text.Trim(),
            RawPayload = TxtPayload.Text.Trim(),
            Description = desc,
            CreatedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };

        db.OutgoingCommands.Add(cmd);
        db.SaveChanges();

        MessageBox.Show($"Команда поставлена в очередь отправки:\n«{cmd.RawPayload}»", "Команда зарегистрирована", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshCommandsHistory();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text) || string.IsNullOrWhiteSpace(TxtPhone.Text))
        {
            MessageBox.Show("Название и телефон обязательны к заполнению!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var db = new AppDbContext(_dbPath);
        var devType = (DeviceType)(CmbDeviceType.SelectedValue ?? DeviceType.Ksital);

        if (_objectId > 0)
        {
            var obj = db.Objects.Find(_objectId);
            if (obj != null)
            {
                obj.Name = TxtName.Text.Trim();
                obj.District = TxtDistrict.Text.Trim();
                obj.PhoneNumber = TxtPhone.Text.Trim();
                obj.DeviceType = devType;
                obj.DevicePassword = TxtPassword.Text.Trim();
                obj.Description = TxtDescription.Text.Trim();
            }
        }
        else
        {
            var newObj = new MonitoredObject
            {
                Name = TxtName.Text.Trim(),
                District = TxtDistrict.Text.Trim(),
                PhoneNumber = TxtPhone.Text.Trim(),
                DeviceType = devType,
                DevicePassword = TxtPassword.Text.Trim(),
                Description = TxtDescription.Text.Trim()
            };
            db.Objects.Add(newObj);
        }

        try
        {
            db.SaveChanges();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}