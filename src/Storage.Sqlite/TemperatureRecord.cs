namespace KsitalTelemetryHub.Storage.Sqlite;

public class TemperatureRecord
{
    public long Id { get; set; }

    public long TelemetryRecordId { get; set; }
    public TelemetryRecord? TelemetryRecord { get; set; }

    public string SensorCode { get; set; } = string.Empty; // "T1", "T2" и т.д.
    public double Value { get; set; }                      // Значение в градусах
}