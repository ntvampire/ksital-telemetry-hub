namespace KsitalTelemetryHub.Core;

public enum PowerState
{
    Unknown,
    Normal,     // "220V: Есть"
    Off         // "220V: Нет" или "Авария сети 220V"
}

public enum ZoneState
{
    Normal,     // "Норма"
    Triggered,  // "Сработка" / "Тревога"
    OpenCircuit,// "Обрыв"
    ShortCircuit// "Замыкание"
}

public class KsitalReport
{
    public string DeviceName { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }

    // Питание
    public PowerState MainPower { get; set; } = PowerState.Unknown;
    public double? BatteryVoltage { get; set; }

    // Баланс SIM-карты
    public decimal? SimBalance { get; set; }

    // Температурные датчики (T1 -> +65.0, T2 -> +45.5 и т.д.)
    public Dictionary<string, double> Temperatures { get; set; } = new();

    // Зоны / шлейфы (З1 -> Normal, З2 -> Triggered и т.д.)
    public Dictionary<int, ZoneState> Zones { get; set; } = new();

    // Флаг аварийного/тревожного сообщения
    public bool IsAlarm { get; set; }
    public string AlarmDescription { get; set; } = string.Empty;

    // Исходный текст СМС
    public string RawText { get; set; } = string.Empty;
}