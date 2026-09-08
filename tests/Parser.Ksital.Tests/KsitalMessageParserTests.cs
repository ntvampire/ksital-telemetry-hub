using Xunit;
using KsitalTelemetryHub.Core;
using KsitalTelemetryHub.Parser.Ksital;

namespace KsitalTelemetryHub.Parser.Tests;

public class KsitalMessageParserTests
{
    [Fact]
    public void Should_Parse_Standard_Boiler_Report()
    {
        string sampleText = 
            "Котельная №1\n" +
            "220V: Есть\n" +
            "Асс: 12.6V\n" +
            "T1=+72.5 T2=+55.0 T3=+22.0\n" +
            "З1:Норма З2:Норма З3:Сработка\n" +
            "Баланс: 250.40р";

        var sms = new DecodedSms
        {
            SenderNumber = "+79160001122",
            Timestamp = DateTime.UtcNow,
            Text = sampleText
        };

        var report = KsitalMessageParser.Parse(sms);

        Assert.Equal("Котельная №1", report.DeviceName);
        Assert.Equal(PowerState.Normal, report.MainPower);
        Assert.Equal(12.6, report.BatteryVoltage);
        Assert.Equal(250.40m, report.SimBalance);

        // Проверка температур (Т1 - подача, Т2 - обратка)
        Assert.Equal(72.5, report.Temperatures["T1"]);
        Assert.Equal(55.0, report.Temperatures["T2"]);
        Assert.Equal(22.0, report.Temperatures["T3"]);

        // Проверка зон (З3 - тревога/загазованность)
        Assert.Equal(ZoneState.Normal, report.Zones[1]);
        Assert.Equal(ZoneState.Normal, report.Zones[2]);
        Assert.Equal(ZoneState.Triggered, report.Zones[3]);
    }

    [Fact]
    public void Should_Identify_Alarm_Message()
    {
        var sms = new DecodedSms
        {
            SenderNumber = "+79160001122",
            Timestamp = DateTime.UtcNow,
            Text = "Тревога! Вскрытие двери котельной З1"
        };

        var report = KsitalMessageParser.Parse(sms);

        Assert.True(report.IsAlarm);
        Assert.Contains("Тревога!", report.AlarmDescription);
    }
}