using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Parser.Ccu825;

public class Ccu825MessageParser
{
    private static readonly Regex TempRegex = new(@"\b(T\d+)\s*[:=]\s*([+-]?\d+(?:[\.,]\d+)?)\s*°?C?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BatRegex = new(@"(?:Vbat|Bat|АКБ)\s*[:=]\s*(\d+(?:[\.,]\d+)?)\s*V?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerRegex = new(@"(?:220V|Pwr|Питание|Сеть)\s*[:=]\s*([A-Za-zА-Яа-я0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InputRegex = new(@"\b(In\d+|Вход\d+)\s*[:=]\s*([A-Za-zА-Яа-я0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public KsitalReport Parse(string rawText, DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new ArgumentException("Входящее SMS-сообщение не может быть пустым.", nameof(rawText));
        }

        var report = new KsitalReport
        {
            Timestamp = timestamp ?? DateTime.UtcNow,
            RawText = rawText
        };

        // 1. Температурные датчики (T1..T8)
        var tempMatches = TempRegex.Matches(rawText);
        foreach (Match m in tempMatches)
        {
            string code = m.Groups[1].Value.ToUpperInvariant();
            string valStr = m.Groups[2].Value.Replace(',', '.');
            if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                report.Temperatures[code] = val;
            }
        }

        // 2. Напряжение аккумулятора (Vbat)
        var batMatch = BatRegex.Match(rawText);
        if (batMatch.Success)
        {
            string batStr = batMatch.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(batStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double batVal))
            {
                report.BatteryVoltage = batVal;
            }
        }

        // 3. Состояние питания 220V
        var pwrMatch = PowerRegex.Match(rawText);
        if (pwrMatch.Success)
        {
            string pwrStatus = pwrMatch.Groups[1].Value.ToUpperInvariant();
            if (pwrStatus.Contains("NORM") || pwrStatus.Contains("OK") || pwrStatus.Contains("НОРМ") || pwrStatus == "1")
            {
                report.MainPower = PowerState.Normal;
            }
            else
            {
                report.MainPower = PowerState.Off;
                report.IsAlarm = true;
                report.AlarmDescription = "Авария: Отсутствует основное питание 220V (CCU-825)";
            }
        }
        else
        {
            report.MainPower = PowerState.Normal;
        }

        // 4. Проверка входов / шлейфов
        var alarms = new List<string>();
        var inputMatches = InputRegex.Matches(rawText);
        foreach (Match im in inputMatches)
        {
            string inName = im.Groups[1].Value;
            string inState = im.Groups[2].Value.ToUpperInvariant();

            if (inState.Contains("ALARM") || inState.Contains("ТРЕВ") || inState == "1")
            {
                alarms.Add($"Сработка шлейфа {inName}");
            }
        }

        if (alarms.Count > 0)
        {
            report.IsAlarm = true;
            report.AlarmDescription = string.IsNullOrWhiteSpace(report.AlarmDescription)
                ? string.Join(", ", alarms)
                : $"{report.AlarmDescription}; {string.Join(", ", alarms)}";
        }

        return report;
    }
}