using System.Globalization;
using System.Text.RegularExpressions;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Parser.Ksital;

public static class KsitalMessageParser
{
    // Регулярные выражения для поиска параметров
    private static readonly Regex TempRegex = new(@"T(?<index>\d+)\s*=\s*(?<val>[+-]?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
    private static readonly Regex ZoneRegex = new(@"З(?<index>\d+)\s*:\s*(?<status>Норма|Сработка|Обрыв|Замыкание)", RegexOptions.IgnoreCase);
    private static readonly Regex Power220Regex = new(@"220V\s*:\s*(?<val>Есть|Нет)", RegexOptions.IgnoreCase);
    private static readonly Regex BatteryRegex = new(@"(?:Асс|Acc|АКБ)\s*:\s*(?<val>\d+(?:[\.,]\d+)?)V?", RegexOptions.IgnoreCase);
    private static readonly Regex BalanceRegex = new(@"Баланс\s*:\s*(?<val>[+-]?\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
    private static readonly Regex AlarmKeywordRegex = new(@"(Тревога!|Авария!)(?<desc>.*)", RegexOptions.IgnoreCase);

    public static KsitalReport Parse(DecodedSms sms)
    {
        var report = new KsitalReport
        {
            SenderPhone = sms.SenderNumber,
            Timestamp = sms.Timestamp,
            RawText = sms.Text
        };

        string text = sms.Text.Replace("\r", " ").Trim();
        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > 0)
        {
            report.DeviceName = lines[0].Trim();
        }

        // 1. Проверка на статус "Авария!" / "Тревога!"
        var alarmMatch = AlarmKeywordRegex.Match(text);
        if (alarmMatch.Success)
        {
            report.IsAlarm = true;
            report.AlarmDescription = alarmMatch.Value.Trim();
        }

        // 2. Парсинг температур T1..Tn
        foreach (Match match in TempRegex.Matches(text))
        {
            string key = $"T{match.Groups["index"].Value}";
            string rawVal = match.Groups["val"].Value.Replace(',', '.');
            if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double tempVal))
            {
                report.Temperatures[key] = tempVal;
            }
        }

        // 3. Парсинг зон контроля З1..Зn
        foreach (Match match in ZoneRegex.Matches(text))
        {
            if (int.TryParse(match.Groups["index"].Value, out int zoneIdx))
            {
                string status = match.Groups["status"].Value.ToLowerInvariant();
                report.Zones[zoneIdx] = status switch
                {
                    "норма" => ZoneState.Normal,
                    "сработка" => ZoneState.Triggered,
                    "обрыв" => ZoneState.OpenCircuit,
                    "замыкание" => ZoneState.ShortCircuit,
                    _ => ZoneState.Normal
                };
            }
        }

        // 4. Парсинг сети 220V
        var pwrMatch = Power220Regex.Match(text);
        if (pwrMatch.Success)
        {
            report.MainPower = pwrMatch.Groups["val"].Value.Equals("Есть", StringComparison.OrdinalIgnoreCase)
                ? PowerState.Normal
                : PowerState.Off;
        }

        // 5. Напряжение резервного аккумулятора
        var batMatch = BatteryRegex.Match(text);
        if (batMatch.Success)
        {
            string rawVal = batMatch.Groups["val"].Value.Replace(',', '.');
            if (double.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out double batVal))
            {
                report.BatteryVoltage = batVal;
            }
        }

        // 6. Баланс
        var balMatch = BalanceRegex.Match(text);
        if (balMatch.Success)
        {
            string rawVal = balMatch.Groups["val"].Value.Replace(',', '.');
            if (decimal.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal balVal))
            {
                report.SimBalance = balVal;
            }
        }

        return report;
    }
}