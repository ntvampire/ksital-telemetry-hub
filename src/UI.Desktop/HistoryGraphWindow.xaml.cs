using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using KsitalTelemetryHub.Storage.Sqlite;

namespace KsitalTelemetryHub.UI.Desktop;

public partial class HistoryGraphWindow : Window
{
    private readonly int _objectId;
    private readonly string _objectName;
    private readonly string _dbPath;
    private int _selectedHours = 24;

    private class PointData
    {
        public DateTime Time { get; set; }
        public double? T1 { get; set; }
        public double? T2 { get; set; }
    }

    private List<PointData> _cache = new();

    public HistoryGraphWindow(int objectId, string objectName, string dbPath)
    {
        InitializeComponent();
        _objectId = objectId;
        _objectName = objectName;
        _dbPath = dbPath;

        TxtTitle.Text = $"Динамика температур: {_objectName}";
        Loaded += async (s, e) => await LoadDataAsync();
    }

    private async void BtnPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int hours))
        {
            _selectedHours = hours;
            await LoadDataAsync();
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            using var db = new AppDbContext(_dbPath);
            var dateFrom = DateTime.UtcNow.AddHours(-_selectedHours);

            var rawRecords = await db.Telemetry
                .Where(t => t.MonitoredObjectId == _objectId && t.Timestamp >= dateFrom)
                .OrderBy(t => t.Timestamp)
                .Include(t => t.Temperatures)
                .ToListAsync();

            _cache = rawRecords.Select(r => new PointData
            {
                Time = r.Timestamp,
                T1 = r.Temperatures.FirstOrDefault(t => t.SensorCode == "T1")?.Value,
                T2 = r.Temperatures.FirstOrDefault(t => t.SensorCode == "T2")?.Value
            }).ToList();

            UpdateStats();
            RenderPlot();
        }
        catch (Exception ex)
        {
            TxtPointsCount.Text = $"Ошибка загрузки данных: {ex.Message}";
        }
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_cache.Count == 0)
        {
            MessageBox.Show("Нет данных для экспорта за выбранный период.", "Экспорт CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Формирование безопасного имени файла
        string safeName = string.Join("_", _objectName.Split(System.IO.Path.GetInvalidFileNameChars()));
        string defaultFileName = $"Температура_{safeName}_{_selectedHours}ч_{DateTime.Now:yyyyMMdd_HHmm}.csv";

        var sfd = new SaveFileDialog
        {
            Title = "Сохранение истории температур в CSV",
            Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
            FileName = defaultFileName
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var sb = new StringBuilder();
                // Заголовок таблицы (разделитель точка с запятой для Excel)
                sb.AppendLine("Дата и время;T1 Подача (°C);T2 Обратка (°C)");

                foreach (var item in _cache)
                {
                    string localTime = item.Time.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
                    string t1Str = item.T1.HasValue ? item.T1.Value.ToString("F1", CultureInfo.InvariantCulture) : "";
                    string t2Str = item.T2.HasValue ? item.T2.Value.ToString("F1", CultureInfo.InvariantCulture) : "";

                    sb.AppendLine($"{localTime};{t1Str};{t2Str}");
                }

                // UTF-8 с BOM, чтобы Excel корректно читал русские символы
                File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));

                MessageBox.Show($"Файл успешно сохранен ({_cache.Count} записей):\n{sfd.FileName}", 
                                "Экспорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void UpdateStats()
    {
        TxtPointsCount.Text = $"Точек за период: {_cache.Count}";
        var t1List = _cache.Where(p => p.T1.HasValue).Select(p => p.T1!.Value).ToList();
        var t2List = _cache.Where(p => p.T2.HasValue).Select(p => p.T2!.Value).ToList();

        if (t1List.Count > 0 && t2List.Count > 0)
        {
            TxtStats.Text = $"T1: [{t1List.Min():F1} .. {t1List.Max():F1} °C, ср: {t1List.Average():F1}]  |  T2: [{t2List.Min():F1} .. {t2List.Max():F1} °C, ср: {t2List.Average():F1}]";
        }
        else
        {
            TxtStats.Text = "Нет данных для расчета статистики";
        }
    }

    private void PlotCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderPlot();
    }

    private void RenderPlot()
    {
        PlotCanvas.Children.Clear();
        if (_cache.Count < 2) return;

        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width < 100 || height < 100) return;

        double padLeft = 45;
        double padRight = 20;
        double padTop = 20;
        double padBottom = 30;

        double plotW = width - padLeft - padRight;
        double plotH = height - padTop - padBottom;

        var allTemps = _cache.SelectMany(p => new[] { p.T1, p.T2 }).Where(t => t.HasValue).Select(t => t!.Value).ToList();
        double minT = allTemps.Any() ? Math.Floor(allTemps.Min() - 2.0) : 0;
        double maxT = allTemps.Any() ? Math.Ceiling(allTemps.Max() + 2.0) : 100;
        if (Math.Abs(maxT - minT) < 0.1) maxT += 5;

        DateTime minTime = _cache.First().Time;
        DateTime maxTime = _cache.Last().Time;
        double timeSpan = (maxTime - minTime).TotalSeconds;
        if (timeSpan < 1) timeSpan = 1;

        int yTicks = 5;
        for (int i = 0; i <= yTicks; i++)
        {
            double tVal = minT + (maxT - minT) * i / yTicks;
            double y = padTop + plotH - (i * plotH / yTicks);

            var line = new Line
            {
                X1 = padLeft,
                Y1 = y,
                X2 = padLeft + plotW,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(45, 47, 66)),
                StrokeThickness = 1
            };
            PlotCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = $"{tVal:0}°C",
                Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                FontSize = 10
            };
            Canvas.SetLeft(label, 6);
            Canvas.SetTop(label, y - 7);
            PlotCanvas.Children.Add(label);
        }

        var polyT1 = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(243, 139, 168)),
            StrokeThickness = 2.5
        };

        var polyT2 = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
            StrokeThickness = 2.5
        };

        foreach (var p in _cache)
        {
            double x = padLeft + ((p.Time - minTime).TotalSeconds / timeSpan) * plotW;

            if (p.T1.HasValue)
            {
                double y1 = padTop + plotH - ((p.T1.Value - minT) / (maxT - minT)) * plotH;
                polyT1.Points.Add(new Point(x, y1));
            }

            if (p.T2.HasValue)
            {
                double y2 = padTop + plotH - ((p.T2.Value - minT) / (maxT - minT)) * plotH;
                polyT2.Points.Add(new Point(x, y2));
            }
        }

        PlotCanvas.Children.Add(polyT1);
        PlotCanvas.Children.Add(polyT2);

        var startLabel = new TextBlock
        {
            Text = minTime.ToLocalTime().ToString("HH:mm\ndd.MM", CultureInfo.InvariantCulture),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            FontSize = 10
        };
        Canvas.SetLeft(startLabel, padLeft);
        Canvas.SetTop(startLabel, padTop + plotH + 4);
        PlotCanvas.Children.Add(startLabel);

        var endLabel = new TextBlock
        {
            Text = maxTime.ToLocalTime().ToString("HH:mm\ndd.MM", CultureInfo.InvariantCulture),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            FontSize = 10,
            TextAlignment = TextAlignment.Right
        };
        Canvas.SetLeft(endLabel, padLeft + plotW - 40);
        Canvas.SetTop(endLabel, padTop + plotH + 4);
        PlotCanvas.Children.Add(endLabel);
    }
}