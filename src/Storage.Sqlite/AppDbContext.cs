using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Storage.Sqlite;

public class AppDbContext : DbContext
{
    public DbSet<MonitoredObject> Objects => Set<MonitoredObject>();
    public DbSet<TelemetryRecord> Telemetry => Set<TelemetryRecord>();
    public DbSet<TemperatureRecord> Temperatures => Set<TemperatureRecord>();
    public DbSet<AlarmEvent> Alarms => Set<AlarmEvent>();

    private readonly string _dbPath;

    public AppDbContext(string dbPath = "telemetry.db")
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Индексы для быстрого поиска по номеру и времени
        modelBuilder.Entity<MonitoredObject>()
            .HasIndex(o => o.PhoneNumber)
            .IsUnique();

        modelBuilder.Entity<TelemetryRecord>()
            .HasIndex(t => t.Timestamp);

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => a.Timestamp);
    }

    /// <summary>
    /// Сохраняет разобранный отчет КСИТАЛ, автоматически регистрируя новый объект, если его еще нет.
    /// </summary>
    public async Task SaveReportAsync(KsitalReport report, CancellationToken cancellationToken = default)
    {
        // 1. Ищем объект по номеру телефона или создаем новый
        var monitoredObj = await Objects
            .FirstOrDefaultAsync(o => o.PhoneNumber == report.SenderPhone, cancellationToken);

        if (monitoredObj == null)
        {
            monitoredObj = new MonitoredObject
            {
                PhoneNumber = report.SenderPhone,
                Name = string.IsNullOrWhiteSpace(report.DeviceName) ? $"Объект {report.SenderPhone}" : report.DeviceName,
                Description = "Автоматически создан при приеме SMS"
            };
            Objects.Add(monitoredObj);
            await SaveChangesAsync(cancellationToken);
        }

        // 2. Создаем срез телеметрии
        var telemetry = new TelemetryRecord
        {
            MonitoredObjectId = monitoredObj.Id,
            Timestamp = report.Timestamp,
            MainPower = report.MainPower,
            BatteryVoltage = report.BatteryVoltage,
            SimBalance = report.SimBalance,
            RawSmsText = report.RawText,
            Temperatures = report.Temperatures.Select(t => new TemperatureRecord
            {
                SensorCode = t.Key,
                Value = t.Value
            }).ToList()
        };

        Telemetry.Add(telemetry);

        // 3. Если это тревога/авария — записываем в журнал тревог
        if (report.IsAlarm)
        {
            var alarm = new AlarmEvent
            {
                MonitoredObjectId = monitoredObj.Id,
                Timestamp = report.Timestamp,
                Description = string.IsNullOrWhiteSpace(report.AlarmDescription) ? report.RawText : report.AlarmDescription,
                IsAcknowledged = false
            };
            Alarms.Add(alarm);
        }

        await SaveChangesAsync(cancellationToken);
    }
}