using System.IO;
using System.IO.Ports;
using System.Windows;
using Microsoft.Win32;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class SettingsWindow : Window
{
    public string SelectedPort { get; private set; }
    private readonly string _dbPath;

    public SettingsWindow(string currentPort, string dbPath)
    {
        InitializeComponent();
        SelectedPort = currentPort;
        _dbPath = dbPath;

        LoadPorts();
        TxtBackupFolder.Text = BackupManager.BackupFolder;

        // Установка чекбокса темы
        switch (ThemeManager.CurrentMode)
        {
            case AppThemeMode.Dark: RbThemeDark.IsChecked = true; break;
            case AppThemeMode.Light: RbThemeLight.IsChecked = true; break;
            default: RbThemeSystem.IsChecked = true; break;
        }
    }

    private void LoadPorts()
    {
        var ports = SerialPort.GetPortNames();
        CmbPorts.ItemsSource = ports;
        if (ports.Contains(SelectedPort)) CmbPorts.SelectedItem = SelectedPort;
        else if (ports.Length > 0) CmbPorts.SelectedIndex = 0;
    }

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) => LoadPorts();

    private void RbTheme_Checked(object sender, RoutedEventArgs e)
    {
        if (RbThemeDark.IsChecked == true) ThemeManager.ApplyTheme(AppThemeMode.Dark);
        else if (RbThemeLight.IsChecked == true) ThemeManager.ApplyTheme(AppThemeMode.Light);
        else if (RbThemeSystem.IsChecked == true) ThemeManager.ApplyTheme(AppThemeMode.System);
    }

    private void BtnBrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку для резервных копий" };
        if (dialog.ShowDialog() == true)
        {
            TxtBackupFolder.Text = dialog.FolderName;
            BackupManager.BackupFolder = dialog.FolderName;
        }
    }

    private void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
    {
        BackupManager.BackupFolder = TxtBackupFolder.Text;
        string path = BackupManager.PerformBackup(_dbPath);
        MessageBox.Show($"Резервная копия успешно создана:\n{path}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "База SQLite (*.db)|*.db",
            Title = "Выберите файл резервной копии для восстановления"
        };

        if (ofd.ShowDialog() == true)
        {
            if (MessageBox.Show($"Восстановить базу данных из файла?\n{ofd.FileName}\nТекущие данные будут перезаписаны!", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                BackupManager.RestoreBackup(ofd.FileName, _dbPath);
                MessageBox.Show("База данных успешно восстановлена. Приложение обновит данные.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void BtnOpenManageObjects_Click(object sender, RoutedEventArgs e)
    {
        new ManageObjectsWindow(_dbPath) { Owner = this }.ShowDialog();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPorts.SelectedItem != null)
        {
            SelectedPort = CmbPorts.SelectedItem.ToString()!;
        }
        BackupManager.BackupFolder = TxtBackupFolder.Text;
        DialogResult = true;
        Close();
    }
}