namespace KsitalTelemetryHub.Storage.Sqlite;

public class MonitoredObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty; // Например: +79161234567
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Навигационные свойства
    public List<TelemetryRecord> TelemetryRecords { get; set; } = new();
    public List<AlarmEvent> Alarms { get; set; } = new();
}