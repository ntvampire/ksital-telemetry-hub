using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class ManageObjectsWindow : Window
{
    private readonly string _dbPath;

    public ManageObjectsWindow(string dbPath)
    {
        InitializeComponent();
        _dbPath = dbPath;
        Loaded += async (s, e) => await LoadObjectsAsync();
    }

    private async Task LoadObjectsAsync()
    {
        using var db = new AppDbContext(_dbPath);
        GridObjects.ItemsSource = await db.Objects.OrderBy(o => o.District).ThenBy(o => o.Name).ToListAsync();
    }

    private void GridObjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridObjects.SelectedItem is MonitoredObject selected)
        {
            TxtDistrict.Text = selected.District;
            TxtName.Text = selected.Name;
            TxtPhone.Text = selected.PhoneNumber;
        }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtPhone.Text) || string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show("Заполните название и номер телефона.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        using var db = new AppDbContext(_dbPath);
        var newObj = new MonitoredObject
        {
            Name = TxtName.Text.Trim(),
            PhoneNumber = TxtPhone.Text.Trim(),
            District = string.IsNullOrWhiteSpace(TxtDistrict.Text) ? "Основной участок" : TxtDistrict.Text.Trim()
        };

        db.Objects.Add(newObj);
        await db.SaveChangesAsync();
        await LoadObjectsAsync();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (GridObjects.SelectedItem is MonitoredObject selected)
        {
            using var db = new AppDbContext(_dbPath);
            var entity = await db.Objects.FindAsync(selected.Id);
            if (entity != null)
            {
                entity.Name = TxtName.Text.Trim();
                entity.PhoneNumber = TxtPhone.Text.Trim();
                entity.District = string.IsNullOrWhiteSpace(TxtDistrict.Text) ? "Основной участок" : TxtDistrict.Text.Trim();
                await db.SaveChangesAsync();
                await LoadObjectsAsync();
            }
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GridObjects.SelectedItem is MonitoredObject selected)
        {
            if (MessageBox.Show($"Удалить объект \"{selected.Name}\" и всю его историю?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                using var db = new AppDbContext(_dbPath);
                var entity = await db.Objects.FindAsync(selected.Id);
                if (entity != null)
                {
                    db.Objects.Remove(entity);
                    await db.SaveChangesAsync();
                    await LoadObjectsAsync();
                    TxtName.Clear();
                    TxtPhone.Clear();
                }
            }
        }
    }
}