using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using KsitalTelemetryHub.Core;

namespace KsitalTelemetryHub.Parser.Owen;

public class OwenMessageParser
{
    private static readonly Regex TempRegex = new(@"\b(T\d+)\s*[:=]\s*([+-]?\d+(?:[\.,]\d+)?)\s*°?C?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BatRegex = new(@"(?:VBAT|BAT|АКБ|Uакб)\s*[:=]\s*(\d+(?:[\.,]\d+)?)\s*V?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PowerRegex = new(@"(?:220V|PWR|СЕТЬ|ПИТАНИЕ)\s*[:=]\s*([A-Za-zА-Яа-я0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
private static readonly Regex ErrorRegex = new(@"\b(?:ERR|АВАРИЯ|ОШИБКА)\s*[:=]\s*(.+?)(?=\s+[A-Za-zА-Яа-я0-9_]+[:=]|$)|(?<!OWEN\s+)\bALARM\s*[:=]\s*(.+?)(?=\s+[A-Za-zА-Яа-я0-9_]+[:=]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        // 1. Извлечение температурных каналов (T1..Tn)
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

        // 2. Напряжение батареи/ИБП
        var batMatch = BatRegex.Match(rawText);
        if (batMatch.Success)
        {
            string batStr = batMatch.Groups[1].Value.Replace(',', '.');
            if (double.TryParse(batStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double batVal))
            {
                report.BatteryVoltage = batVal;
            }
        }

        // 3. Состояние сетевого питания 220V
        var pwrMatch = PowerRegex.Match(rawText);
        if (pwrMatch.Success)
        {
            string pwrStatus = pwrMatch.Groups[1].Value.ToUpperInvariant();
            if (pwrStatus == "1" || pwrStatus.Contains("NORM") || pwrStatus.Contains("OK") || pwrStatus.Contains("ЕСТЬ"))
            {
                report.MainPower = PowerState.Normal;
            }
            else
            {
                report.MainPower = PowerState.Off;
                report.IsAlarm = true;
                report.AlarmDescription = "Авария: Отсутствует основное питание 220V (ОВЕН ПЛК)";
            }
        }
        else
        {
            report.MainPower = PowerState.Normal;
        }

// 4. Анализ флагов и текста аварий ОВЕН
        var errMatch = ErrorRegex.Match(rawText);
        if (errMatch.Success)
        {
            string errText = (errMatch.Groups[1].Success ? errMatch.Groups[1].Value : errMatch.Groups[2].Value).Trim();
            if (!string.IsNullOrEmpty(errText) &&
                !string.Equals(errText, "0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errText, "NORM", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errText, "OK", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(errText, "НЕТ", StringComparison.OrdinalIgnoreCase))
            {
                report.IsAlarm = true;
                report.AlarmDescription = string.IsNullOrWhiteSpace(report.AlarmDescription)
                    ? $"Авария ПЛК: {errText}"
                    : $"{report.AlarmDescription}; Авария ПЛК: {errText}";
            }
        }

        return report;
    }
}