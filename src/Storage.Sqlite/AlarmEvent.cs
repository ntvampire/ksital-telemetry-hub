namespace KsitalTelemetryHub.Storage.Sqlite;

public class AlarmEvent
{
    public long Id { get; set; }

    public int MonitoredObjectId { get; set; }
    public MonitoredObject? MonitoredObject { get; set; }

    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; } // Флаг: квитирована ли тревога оператором
    public DateTime? AcknowledgedAt { get; set; }
}