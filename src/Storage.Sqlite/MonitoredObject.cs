using System.Collections.Generic;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Storage.Sqlite;

public class MonitoredObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string District { get; set; } = "Основной участок";
    public string? Description { get; set; }

    public DeviceType DeviceType { get; set; } = DeviceType.Ksital;
    public string DevicePassword { get; set; } = "00000";

    public ICollection<TelemetryRecord> TelemetryRecords { get; set; } = new List<TelemetryRecord>();
    public ICollection<AlarmEvent> Alarms { get; set; } = new List<AlarmEvent>();
    public ICollection<OutgoingCommand> OutgoingCommands { get; set; } = new List<OutgoingCommand>();
}