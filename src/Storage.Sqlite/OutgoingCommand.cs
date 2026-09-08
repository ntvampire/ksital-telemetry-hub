using System;

namespace KsitalTelemetryHub.Storage.Sqlite;

public enum CommandStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

public class OutgoingCommand
{
    public long Id { get; set; }
    public int MonitoredObjectId { get; set; }
    public MonitoredObject? MonitoredObject { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public CommandStatus Status { get; set; } = CommandStatus.Pending;
    public string? ErrorMessage { get; set; }
}