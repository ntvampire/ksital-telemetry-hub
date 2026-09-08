namespace KsitalTelemetryHub.Core;

public class DecodedSms
{
    public string SenderNumber { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
    public string RawPdu { get; set; } = string.Empty;
}