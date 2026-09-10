using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Storage.Sqlite;

public class AppDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<MonitoredObject> Objects => Set<MonitoredObject>();
    public DbSet<TelemetryRecord> Telemetry => Set<TelemetryRecord>();
    public DbSet<TemperatureRecord> Temperatures => Set<TemperatureRecord>();
    public DbSet<AlarmEvent> Alarms => Set<AlarmEvent>();
    public DbSet<OutgoingCommand> OutgoingCommands => Set<OutgoingCommand>();

    public AppDbContext(string dbPath = "telemetry.db")
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        optionsBuilder.UseSqlite(csb.ToString());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MonitoredObject>()
            .HasIndex(o => o.PhoneNumber)
            .IsUnique();

        modelBuilder.Entity<TelemetryRecord>()
            .HasIndex(t => t.Timestamp);

        modelBuilder.Entity<AlarmEvent>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<OutgoingCommand>()
            .HasIndex(c => c.CreatedAt);
    }

    public async Task SaveReportAsync(string senderPhone, KsitalReport report, CancellationToken cancellationToken = default)
    {
        string phone = !string.IsNullOrWhiteSpace(senderPhone)
            ? senderPhone
            : (!string.IsNullOrWhiteSpace(report.SenderPhone) ? report.SenderPhone : "+79000000000");

        var obj = await Objects.FirstOrDefaultAsync(o => o.PhoneNumber == phone, cancellationToken);
        if (obj == null)
        {
            obj = new MonitoredObject
            {
                PhoneNumber = phone,
                Name = string.IsNullOrWhiteSpace(report.DeviceName) ? $"Объект {phone}" : report.DeviceName,
                District = "Основной участок",
                DeviceType = DeviceType.Ksital,
                DevicePassword = "00000"
            };
            Objects.Add(obj);
            await SaveChangesAsync(cancellationToken);
        }

        var record = new TelemetryRecord
        {
            MonitoredObjectId = obj.Id,
            Timestamp = report.Timestamp != default ? report.Timestamp : DateTime.UtcNow,
            MainPower = report.MainPower,
            BatteryVoltage = report.BatteryVoltage
        };

        if (report.Temperatures != null)
        {
            foreach (var t in report.Temperatures)
            {
                record.Temperatures.Add(new TemperatureRecord
                {
                    SensorCode = t.Key,
                    Value = t.Value
                });
            }
        }

        Telemetry.Add(record);

        // Фиксация аварии основного питания 220V
        if (report.MainPower == PowerState.Off)
        {
            Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = record.Timestamp,
                Description = "Авария: Отсутствует основное питание 220V",
                IsAcknowledged = false
            });
        }

        // Фиксация технологической аварии из отчета (например, шлейф или ошибка ОВЕН)
        if (report.IsAlarm && !string.IsNullOrWhiteSpace(report.AlarmDescription))
        {
            Alarms.Add(new AlarmEvent
            {
                MonitoredObjectId = obj.Id,
                Timestamp = record.Timestamp,
                Description = report.AlarmDescription,
                IsAcknowledged = false
            });
        }

        await SaveChangesAsync(cancellationToken);
    }

    public Task SaveReportAsync(KsitalReport report, CancellationToken cancellationToken = default)
    {
        return SaveReportAsync(report.SenderPhone, report, cancellationToken);
    }

    public static void EnsureDatabaseUpdated(string dbPath)
    {
        using var db = new AppDbContext(dbPath);
        db.Database.EnsureCreated();

        try
        {
            using var conn = db.Database.GetDbConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "ALTER TABLE Objects ADD COLUMN DeviceType INTEGER NOT NULL DEFAULT 0;";
            try { cmd.ExecuteNonQuery(); } catch { }

            cmd.CommandText = "ALTER TABLE Objects ADD COLUMN DevicePassword TEXT NOT NULL DEFAULT '00000';";
            try { cmd.ExecuteNonQuery(); } catch { }

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS OutgoingCommands (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MonitoredObjectId INTEGER NOT NULL,
                    PhoneNumber TEXT NOT NULL,
                    RawPayload TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    SentAt TEXT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    ErrorMessage TEXT NULL,
                    FOREIGN KEY (MonitoredObjectId) REFERENCES Objects(Id) ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}