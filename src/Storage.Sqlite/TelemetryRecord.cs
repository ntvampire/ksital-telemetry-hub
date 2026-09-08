using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Storage.Sqlite;

public class TelemetryRecord
{
    public long Id { get; set; }
    
    public int MonitoredObjectId { get; set; }
    public MonitoredObject? MonitoredObject { get; set; }

    public DateTime Timestamp { get; set; }
    public PowerState MainPower { get; set; }
    public double? BatteryVoltage { get; set; }
    public decimal? SimBalance { get; set; }
    public string RawSmsText { get; set; } = string.Empty;

    // Связанные температуры датчиков (T1, T2...)
    public List<TemperatureRecord> Temperatures { get; set; } = new();
}